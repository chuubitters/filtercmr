"""MediaPipe Hand Landmarker wrapper for FILTER CMR.

Responsibilities
----------------
* Load the MediaPipe Tasks ``HandLandmarker`` (VIDEO running mode).
* Run it on a downscaled copy of each frame.
* Convert results to normalised (0-1) landmark arrays, optionally mirrored.
* Give each hand a stable Left/Right identity (resolving duplicate labels).
* Smooth each hand with a One Euro Filter.
* Keep briefly-lost hands alive ("held") and drop them after a timeout.
* Recreate the landmarker automatically if it keeps failing.

Handedness note
---------------
The installed MediaPipe Tasks API documents that Left/Right refer to the
*subject's* hands for an un-mirrored image, and that mirroring the input
inverts them. We therefore always detect on the raw camera frame and mirror
only the output coordinates. If your setup still reports inverted labels,
run with ``--swap-handedness``.
"""

from __future__ import annotations

import logging
import shutil
import ssl
import tempfile
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Sequence

import cv2
import numpy as np

from config import MODEL_URL, TrackerConfig
from finger_state import WRIST, classify_hand
from one_euro import OneEuroFilter

log = logging.getLogger("filtercmr.detector")

NUM_LANDMARKS = 21
LEFT, RIGHT = "Left", "Right"


# --------------------------------------------------------------------------- model
def ensure_model(path: Path, url: str = MODEL_URL) -> Path:
    """Download the hand landmarker model to ``path`` if it is missing."""
    path = Path(path)
    if path.is_file() and path.stat().st_size > 0:
        return path
    path.parent.mkdir(parents=True, exist_ok=True)
    log.info("downloading MediaPipe model -> %s", path)
    try:
        import certifi  # installed with mediapipe

        ctx = ssl.create_default_context(cafile=certifi.where())
    except Exception:  # pragma: no cover - fallback to system store
        ctx = ssl.create_default_context()
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False, suffix=".part") as tmp:
        tmp_path = Path(tmp.name)
        try:
            with urllib.request.urlopen(url, context=ctx, timeout=60) as resp:
                shutil.copyfileobj(resp, tmp)
        except Exception as exc:
            tmp.close()
            tmp_path.unlink(missing_ok=True)
            raise RuntimeError(
                f"could not download the hand model from {url}: {exc}\n"
                f"Download it manually and place it at {path}"
            ) from exc
    tmp_path.replace(path)
    return path


# --------------------------------------------------------------------- data types
@dataclass
class Detection:
    """One hand from a single MediaPipe result, before tracking/smoothing."""

    label: str
    score: float
    landmarks: np.ndarray  # (21, 3) normalised; x already mirrored if requested


@dataclass
class HandTrack:
    label: str
    score: float
    landmarks: np.ndarray  # (21, 3) smoothed
    raw: np.ndarray  # (21, 3) unsmoothed
    state: str  # "tracked" | "held"
    last_seen: float  # monotonic seconds
    first_seen: float
    extended: List[bool] = field(default_factory=lambda: [False] * 5)
    pose: str = "other"
    pinch: float = 9.99  # thumb-tip <-> index-tip distance / palm size

    def age_ms(self, now: float) -> float:
        return max(0.0, (now - self.last_seen) * 1000.0)


# ------------------------------------------------------------- pure helpers (testable)
def opposite(label: str) -> str:
    return RIGHT if label == LEFT else LEFT


def normalise_label(name: str, swap: bool) -> str:
    n = (name or "").strip().lower()
    label = RIGHT if n.startswith("r") else LEFT
    return opposite(label) if swap else label


def mirror_x(landmarks: np.ndarray) -> np.ndarray:
    out = np.array(landmarks, dtype=np.float64, copy=True)
    out[:, 0] = 1.0 - out[:, 0]
    return out


def parse_result(result, mirror: bool, swap_handedness: bool) -> List[Detection]:
    """Convert a ``HandLandmarkerResult`` (or a duck-typed equivalent) into
    :class:`Detection` objects in display coordinates."""
    detections: List[Detection] = []
    hands = getattr(result, "hand_landmarks", None) or []
    handedness = getattr(result, "handedness", None) or []
    for i, hand in enumerate(hands):
        if len(hand) != NUM_LANDMARKS:
            continue
        lm = np.array([[p.x, p.y, p.z] for p in hand], dtype=np.float64)
        if not np.all(np.isfinite(lm)):
            continue
        if mirror:
            lm = mirror_x(lm)
        if i < len(handedness) and handedness[i]:
            cat = handedness[i][0]
            label = normalise_label(getattr(cat, "category_name", "") or getattr(cat, "display_name", ""), swap_handedness)
            score = float(getattr(cat, "score", 0.0) or 0.0)
        else:
            label, score = LEFT, 0.0
        detections.append(Detection(label, score, lm))
    return detections


def resolve_labels(detections: Sequence[Detection], mirror: bool) -> List[Detection]:
    """Guarantee at most one Left and one Right.

    MediaPipe occasionally labels both hands the same. In that case we fall
    back to screen position: in a mirrored (selfie) view the user's right
    hand appears on the right of the screen; in an un-mirrored view it
    appears on the left.
    """
    dets = list(detections)
    if len(dets) <= 1:
        return dets
    dets = sorted(dets, key=lambda d: d.score, reverse=True)[:2]
    a, b = dets
    if a.label != b.label:
        return dets
    # Same label: decide by wrist x in display coordinates.
    right_is_larger_x = mirror
    ax, bx = a.landmarks[WRIST, 0], b.landmarks[WRIST, 0]
    a_is_right = (ax > bx) if right_is_larger_x else (ax < bx)
    a.label = RIGHT if a_is_right else LEFT
    b.label = opposite(a.label)
    return dets


def clamp01(landmarks: np.ndarray) -> np.ndarray:
    """Clamp x/y into [0,1] (z left untouched)."""
    out = np.array(landmarks, dtype=np.float64, copy=True)
    out[:, :2] = np.clip(out[:, :2], 0.0, 1.0)
    return out


# ------------------------------------------------------------------- tracker state
class HandTracker:
    """Identity, smoothing and loss handling on top of per-frame detections.

    Independent of MediaPipe so it can be unit tested with synthetic data.
    """

    def __init__(self, cfg: TrackerConfig) -> None:
        self.cfg = cfg
        self.tracks: Dict[str, HandTrack] = {}
        self._filters: Dict[str, OneEuroFilter] = {}

    def _filter(self, label: str) -> OneEuroFilter:
        f = self._filters.get(label)
        if f is None:
            f = OneEuroFilter(self.cfg.euro_min_cutoff, self.cfg.euro_beta, self.cfg.euro_d_cutoff)
            self._filters[label] = f
        return f

    def reset(self) -> None:
        self.tracks.clear()
        for f in self._filters.values():
            f.reset()

    def update(self, detections: Iterable[Detection], now: float, aspect: float) -> List[HandTrack]:
        seen = set()
        for det in detections:
            seen.add(det.label)
            if self.cfg.smoothing:
                smoothed = self._filter(det.label)(det.landmarks, now)
            else:
                smoothed = det.landmarks.copy()
            ext, pose, pinch = classify_hand(det.landmarks, aspect, self.cfg.thumb_out_ratio, self.cfg.pinch_ratio)
            prev = self.tracks.get(det.label)
            self.tracks[det.label] = HandTrack(
                label=det.label,
                score=det.score,
                landmarks=smoothed,
                raw=det.landmarks,
                state="tracked",
                last_seen=now,
                first_seen=prev.first_seen if prev else now,
                extended=ext,
                pose=pose,
                pinch=pinch,
            )

        hold_s = self.cfg.hand_hold_ms / 1000.0
        for label in list(self.tracks):
            if label in seen:
                continue
            tr = self.tracks[label]
            if now - tr.last_seen <= hold_s:
                tr.state = "held"
            else:
                del self.tracks[label]
                self._filter(label).reset()
        # Stable order: Left first, then Right.
        return [self.tracks[k] for k in (LEFT, RIGHT) if k in self.tracks]


# ------------------------------------------------------------------- detector
class HandDetector:
    MAX_CONSECUTIVE_ERRORS = 5

    def __init__(self, cfg: TrackerConfig) -> None:
        self.cfg = cfg
        self.tracker = HandTracker(cfg)
        self._landmarker = None
        self._last_ts_ms = -1
        self._errors = 0
        self.status = "starting"
        self.last_error = ""
        self.restarts = 0

    # ----------------------------------------------------------- lifecycle
    def open(self) -> None:
        # Imported lazily so unit tests that only exercise pure helpers
        # do not need the native MediaPipe runtime.
        from mediapipe.tasks.python import BaseOptions, vision

        model_path = ensure_model(self.cfg.model_path)
        options = vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(model_path)),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=self.cfg.max_hands,
            min_hand_detection_confidence=self.cfg.min_detection_confidence,
            min_hand_presence_confidence=self.cfg.min_presence_confidence,
            min_tracking_confidence=self.cfg.min_tracking_confidence,
        )
        self._landmarker = vision.HandLandmarker.create_from_options(options)
        self._last_ts_ms = -1
        self._errors = 0
        self.status = "ok"
        log.info("MediaPipe HandLandmarker ready (model %s)", model_path.name)

    def close(self) -> None:
        if self._landmarker is not None:
            try:
                self._landmarker.close()
            except Exception:  # pragma: no cover - defensive
                pass
            self._landmarker = None

    def reset(self) -> None:
        self.tracker.reset()

    # ------------------------------------------------------------ per frame
    def _prepare(self, frame_bgr: np.ndarray) -> np.ndarray:
        h, w = frame_bgr.shape[:2]
        tw = self.cfg.tracking_width
        if w > tw:
            th = max(1, int(round(h * tw / w)))
            frame_bgr = cv2.resize(frame_bgr, (tw, th), interpolation=cv2.INTER_AREA)
        return cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

    def process(self, frame_bgr: np.ndarray, timestamp_s: float) -> List[HandTrack]:
        """Detect hands in an un-mirrored BGR frame. Never raises for
        per-frame failures; returns the (possibly held) last known tracks."""
        h, w = frame_bgr.shape[:2]
        aspect = w / float(h) if h else 16 / 9
        detections: List[Detection] = []

        if self._landmarker is None:
            try:
                self.open()
            except Exception as exc:
                self.status = "error"
                self.last_error = str(exc)
                log.error("cannot start MediaPipe: %s", exc)
                return self.tracker.update([], timestamp_s, aspect)

        try:
            import mediapipe as mp

            rgb = np.ascontiguousarray(self._prepare(frame_bgr))
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            ts_ms = int(timestamp_s * 1000.0)
            if ts_ms <= self._last_ts_ms:  # VIDEO mode needs strictly increasing timestamps
                ts_ms = self._last_ts_ms + 1
            self._last_ts_ms = ts_ms
            result = self._landmarker.detect_for_video(image, ts_ms)
            detections = resolve_labels(
                parse_result(result, self.cfg.mirror, self.cfg.swap_handedness), self.cfg.mirror
            )
            self._errors = 0
            self.status = "ok"
        except Exception as exc:
            self._errors += 1
            self.last_error = str(exc)
            log.warning("detection failed (%d in a row): %s", self._errors, exc)
            if self._errors >= self.MAX_CONSECUTIVE_ERRORS:
                log.error("restarting MediaPipe after %d consecutive errors", self._errors)
                self.status = "restarting"
                self.restarts += 1
                self.close()  # reopened lazily on the next frame
                self.tracker.reset()

        return self.tracker.update(detections, timestamp_s, aspect)
