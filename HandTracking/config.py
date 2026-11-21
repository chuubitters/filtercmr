"""Runtime configuration for the FILTER CMR hand-tracking service.

Every value can be overridden from the command line (see ``python main.py --help``).
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, fields
from pathlib import Path
from typing import List, Optional

HERE = Path(__file__).resolve().parent

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
    "hand_landmarker/float16/latest/hand_landmarker.task"
)
DEFAULT_MODEL_PATH = HERE / "models" / "hand_landmarker.task"

PROTOCOL_VERSION = 1


@dataclass
class TrackerConfig:
    # --- camera ---------------------------------------------------------
    camera_index: int = 0
    capture_width: int = 1280
    capture_height: int = 720
    capture_fps: int = 30
    # Width (px) the frame is downscaled to before MediaPipe runs.
    # Landmarks are normalised, so this only trades accuracy for speed.
    tracking_width: int = 640
    # Upper bound on detections per second (0 = as fast as frames arrive).
    max_tracking_fps: float = 60.0
    # Seconds without a frame before the camera is considered lost and reopened.
    camera_timeout_s: float = 2.0

    # --- orientation ----------------------------------------------------
    # Mirror landmark x-coordinates (selfie view). Unity must mirror its
    # webcam texture the same way — this flag is sent in every packet.
    mirror: bool = True
    # Swap Left/Right labels if MediaPipe reports them inverted on your setup.
    swap_handedness: bool = False

    # --- MediaPipe ------------------------------------------------------
    model_path: Path = DEFAULT_MODEL_PATH
    max_hands: int = 2
    min_detection_confidence: float = 0.6
    min_presence_confidence: float = 0.5
    min_tracking_confidence: float = 0.5

    # --- smoothing (One Euro Filter, coordinates in normalised units) ----
    smoothing: bool = True
    euro_min_cutoff: float = 1.2
    euro_beta: float = 6.0
    euro_d_cutoff: float = 1.0

    # --- pose thresholds (relative to palm size) -------------------------
    # Thumb counts as "out" (frame "L") when its tip is this far from the index base.
    thumb_out_ratio: float = 0.50
    # Thumb tip closer than this to the index tip = "pinch" (toggles halftone in Unity).
    pinch_ratio: float = 0.25

    # --- loss handling ----------------------------------------------------
    # Keep a vanished hand alive ("held" state) for this long before dropping it.
    hand_hold_ms: float = 150.0

    # --- networking -----------------------------------------------------
    udp_host: str = "127.0.0.1"
    udp_port: int = 5065
    # Heartbeat rate (Hz) used when no camera frames are available.
    heartbeat_hz: float = 5.0

    # --- optional video forwarding (fallback when Unity cannot open the camera)
    stream_video: bool = False
    video_port: int = 5066
    video_width: int = 640
    video_quality: int = 70

    # --- debug ----------------------------------------------------------
    preview: bool = True
    verbose: bool = False

    @classmethod
    def from_args(cls, argv: Optional[List[str]] = None) -> "TrackerConfig":
        parser = build_arg_parser()
        ns = parser.parse_args(argv)
        cfg = cls()
        for f in fields(cls):
            if hasattr(ns, f.name):
                value = getattr(ns, f.name)
                if value is not None:
                    setattr(cfg, f.name, value)
        cfg.validate()
        return cfg

    def validate(self) -> None:
        for name in ("min_detection_confidence", "min_presence_confidence", "min_tracking_confidence"):
            v = getattr(self, name)
            if not 0.0 <= v <= 1.0:
                raise ValueError(f"{name} must be in [0, 1], got {v}")
        if not 1 <= self.max_hands <= 2:
            raise ValueError("max_hands must be 1 or 2")
        if not 1 <= self.udp_port <= 65535:
            raise ValueError("udp_port must be 1-65535")
        if self.tracking_width < 64:
            raise ValueError("tracking_width must be >= 64")
        if not 1 <= self.video_port <= 65535 or self.video_port == self.udp_port:
            raise ValueError("video_port must be 1-65535 and differ from udp_port")
        if not 10 <= self.video_quality <= 100:
            raise ValueError("video_quality must be 10-100")
        if self.video_width < 64:
            raise ValueError("video_width must be >= 64")
        if not 0.1 <= self.thumb_out_ratio <= 2.0:
            raise ValueError("thumb_out_ratio must be 0.1-2.0")
        if not 0.05 <= self.pinch_ratio <= 1.0:
            raise ValueError("pinch_ratio must be 0.05-1.0")
        if self.hand_hold_ms < 0:
            raise ValueError("hand_hold_ms must be >= 0")
        self.model_path = Path(self.model_path)


def _bool_flag(parser: argparse.ArgumentParser, name: str, help_text: str) -> None:
    dest = name.replace("-", "_")
    group = parser.add_mutually_exclusive_group()
    group.add_argument(f"--{name}", dest=dest, action="store_true", default=None, help=help_text)
    group.add_argument(f"--no-{name}", dest=dest, action="store_false", default=None)


def build_arg_parser() -> argparse.ArgumentParser:
    d = TrackerConfig()
    p = argparse.ArgumentParser(
        prog="filtercmr-tracker",
        description="FILTER CMR hand-tracking service: webcam -> MediaPipe -> UDP (Unity).",
    )
    cam = p.add_argument_group("camera")
    cam.add_argument("--camera-index", type=int, help=f"webcam index (default {d.camera_index})")
    cam.add_argument("--capture-width", type=int, help=f"requested capture width (default {d.capture_width})")
    cam.add_argument("--capture-height", type=int, help=f"requested capture height (default {d.capture_height})")
    cam.add_argument("--capture-fps", type=int, help=f"requested capture FPS (default {d.capture_fps})")
    cam.add_argument("--tracking-width", type=int, help=f"downscale width for detection (default {d.tracking_width})")
    cam.add_argument("--max-tracking-fps", type=float, help=f"cap on detections/s, 0 = uncapped (default {d.max_tracking_fps})")
    cam.add_argument("--camera-timeout-s", type=float, help=f"seconds without frames before reopening (default {d.camera_timeout_s})")

    ori = p.add_argument_group("orientation")
    _bool_flag(ori, "mirror", f"mirror x like a selfie view (default {d.mirror})")
    _bool_flag(ori, "swap-handedness", f"swap Left/Right labels (default {d.swap_handedness})")

    mp_ = p.add_argument_group("mediapipe")
    mp_.add_argument("--model-path", type=Path, help=f"hand_landmarker.task path (default {d.model_path})")
    mp_.add_argument("--max-hands", type=int, help=f"1 or 2 (default {d.max_hands})")
    mp_.add_argument("--min-detection-confidence", type=float, help=f"default {d.min_detection_confidence}")
    mp_.add_argument("--min-presence-confidence", type=float, help=f"default {d.min_presence_confidence}")
    mp_.add_argument("--min-tracking-confidence", type=float, help=f"default {d.min_tracking_confidence}")

    sm = p.add_argument_group("smoothing")
    _bool_flag(sm, "smoothing", f"One Euro smoothing (default {d.smoothing})")
    sm.add_argument("--euro-min-cutoff", type=float, help=f"lower = smoother when still (default {d.euro_min_cutoff})")
    sm.add_argument("--euro-beta", type=float, help=f"higher = less lag when moving (default {d.euro_beta})")
    sm.add_argument("--euro-d-cutoff", type=float, help=f"derivative cutoff (default {d.euro_d_cutoff})")
    sm.add_argument("--hand-hold-ms", type=float, help=f"keep a lost hand for N ms (default {d.hand_hold_ms})")

    po = p.add_argument_group("pose thresholds (relative to palm size)")
    po.add_argument("--thumb-out-ratio", type=float, help=f"thumb counts as out for the L frame above this (default {d.thumb_out_ratio}; lower = easier)")
    po.add_argument("--pinch-ratio", type=float, help=f"thumb-index tip distance below this = pinch (default {d.pinch_ratio}; higher = easier)")

    net = p.add_argument_group("network")
    net.add_argument("--udp-host", type=str, help=f"Unity host (default {d.udp_host})")
    net.add_argument("--udp-port", type=int, help=f"Unity UDP port (default {d.udp_port})")
    net.add_argument("--heartbeat-hz", type=float, help=f"heartbeat rate without camera (default {d.heartbeat_hz})")

    vid = p.add_argument_group("video forwarding (Unity camera fallback)")
    _bool_flag(vid, "stream-video", f"send JPEG camera frames to Unity (default {d.stream_video})")
    vid.add_argument("--video-port", type=int, help=f"UDP port for frames (default {d.video_port})")
    vid.add_argument("--video-width", type=int, help=f"frame width sent (default {d.video_width})")
    vid.add_argument("--video-quality", type=int, help=f"JPEG quality 10-100 (default {d.video_quality})")

    dbg = p.add_argument_group("debug")
    _bool_flag(dbg, "preview", f"show OpenCV debug window (default {d.preview})")
    _bool_flag(dbg, "verbose", f"verbose logging (default {d.verbose})")
    return p
