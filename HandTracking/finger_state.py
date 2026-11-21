"""Per-finger extension estimate from MediaPipe's 21 landmarks.

This is a *reference / debug* implementation. It is sent to Unity as an
advisory hint and drawn in the Python preview, but the authoritative gesture
logic (with hold-time verification) lives in Unity (Milestone 3+), which uses
the same rules on the raw landmarks.

The test is rotation-invariant: it looks at how straight each finger is and
whether the tip is further from the wrist than the middle joint, so it works
with the hand tilted in any direction in the image plane.
"""

from __future__ import annotations

import numpy as np

# MediaPipe hand landmark indices
WRIST = 0
THUMB_CMC, THUMB_MCP, THUMB_IP, THUMB_TIP = 1, 2, 3, 4
INDEX_MCP, INDEX_PIP, INDEX_DIP, INDEX_TIP = 5, 6, 7, 8
MIDDLE_MCP, MIDDLE_PIP, MIDDLE_DIP, MIDDLE_TIP = 9, 10, 11, 12
RING_MCP, RING_PIP, RING_DIP, RING_TIP = 13, 14, 15, 16
PINKY_MCP, PINKY_PIP, PINKY_DIP, PINKY_TIP = 17, 18, 19, 20

FINGERTIPS = (THUMB_TIP, INDEX_TIP, MIDDLE_TIP, RING_TIP, PINKY_TIP)
FINGER_NAMES = ("thumb", "index", "middle", "ring", "pinky")

# (mcp, pip, dip, tip) for the four long fingers
_LONG_FINGERS = (
    (INDEX_MCP, INDEX_PIP, INDEX_DIP, INDEX_TIP),
    (MIDDLE_MCP, MIDDLE_PIP, MIDDLE_DIP, MIDDLE_TIP),
    (RING_MCP, RING_PIP, RING_DIP, RING_TIP),
    (PINKY_MCP, PINKY_PIP, PINKY_DIP, PINKY_TIP),
)

# Cosine threshold between MCP->PIP and PIP->TIP; 0.5 ~= 60 degrees of bend.
STRAIGHT_COS = 0.5
# Tip must be this much further from the wrist than the PIP joint.
REACH_RATIO = 1.1

# Thumb "out" test (all distances relative to palm size = wrist -> middle MCP).
# Distance based only: real thumbs bend at the IP joint and are often
# foreshortened, so a straightness requirement rejected genuine "L" shapes.
THUMB_OUT_RATIO = 0.50      # thumb tip <-> index MCP: far from the index base (tucked ~0.4)
THUMB_AWAY_RATIO = 0.65     # thumb tip <-> middle MCP: not tucked across the palm
THUMB_LENGTH_RATIO = 0.35   # thumb tip <-> thumb MCP: visibly extended, not folded

# Pinch: thumb tip touching the index tip.
PINCH_RATIO = 0.25          # thumb tip <-> index tip below this = pinch
PINCH_INDEX_REACH = 1.0     # index tip must be this far from the wrist (excludes fists)


def _unit(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def to_planar(landmarks: np.ndarray, aspect: float) -> np.ndarray:
    """(21,3) normalised landmarks -> (21,2) with x scaled by width/height so
    distances are isotropic in image space."""
    pts = np.asarray(landmarks, dtype=np.float64)[:, :2].copy()
    pts[:, 0] *= aspect
    return pts


def palm_size(p: np.ndarray) -> float:
    return float(np.linalg.norm(p[MIDDLE_MCP] - p[WRIST])) + 1e-9


def thumb_extended(p: np.ndarray, out_ratio: float = THUMB_OUT_RATIO) -> bool:
    """Planar landmarks -> is the thumb spread out (as in an "L")?"""
    palm = palm_size(p)
    tip = p[THUMB_TIP]
    return bool(
        np.linalg.norm(tip - p[INDEX_MCP]) > out_ratio * palm
        and np.linalg.norm(tip - p[MIDDLE_MCP]) > THUMB_AWAY_RATIO * palm
        and np.linalg.norm(tip - p[THUMB_MCP]) > THUMB_LENGTH_RATIO * palm
    )


def pinch_ratio(landmarks: np.ndarray, aspect: float = 16 / 9) -> float:
    """Thumb-tip to index-tip distance divided by palm size (small = touching)."""
    p = to_planar(landmarks, aspect)
    return float(np.linalg.norm(p[THUMB_TIP] - p[INDEX_TIP]) / palm_size(p))


def is_pinch(landmarks: np.ndarray, aspect: float = 16 / 9, threshold: float = PINCH_RATIO) -> bool:
    p = to_planar(landmarks, aspect)
    palm = palm_size(p)
    touching = np.linalg.norm(p[THUMB_TIP] - p[INDEX_TIP]) < threshold * palm
    index_up = np.linalg.norm(p[INDEX_TIP] - p[WRIST]) > PINCH_INDEX_REACH * palm
    return bool(touching and index_up)


def finger_extended(landmarks: np.ndarray, aspect: float = 16 / 9, thumb_out: float = THUMB_OUT_RATIO) -> list[bool]:
    """Return [thumb, index, middle, ring, pinky] extension flags."""
    p = to_planar(landmarks, aspect)
    wrist = p[WRIST]
    out: list[bool] = [thumb_extended(p, thumb_out)]

    for mcp, pip, _dip, tip in _LONG_FINGERS:
        straight = float(np.dot(_unit(p[pip] - p[mcp]), _unit(p[tip] - p[pip]))) > STRAIGHT_COS
        reach = np.linalg.norm(p[tip] - wrist) > np.linalg.norm(p[pip] - wrist) * REACH_RATIO
        out.append(bool(straight and reach))
    return out


def classify_hand(landmarks: np.ndarray, aspect: float = 16 / 9, thumb_out: float = THUMB_OUT_RATIO,
                  pinch_threshold: float = PINCH_RATIO) -> tuple[list[bool], str, float]:
    """Full per-hand classification: (extension flags, pose, pinch ratio).
    A thumb-index pinch overrides the finger-shape pose."""
    ext = finger_extended(landmarks, aspect, thumb_out)
    ratio = pinch_ratio(landmarks, aspect)
    pose = "pinch" if is_pinch(landmarks, aspect, pinch_threshold) else classify_pose(ext)
    return ext, pose, ratio


def classify_pose(ext: list[bool]) -> str:
    """Coarse single-hand pose name from extension flags.

    pinch : (see classify_hand) thumb tip touching index tip
    point : index only (thumb folded)
    frame : thumb + index, others folded ("L" shape, a camera-frame corner)
    peace : index + middle (thumb ignored)
    open  : all four long fingers
    fist  : no long finger
    """
    thumb, index, middle, ring, pinky = ext
    if index and not middle and not ring and not pinky:
        return "frame" if thumb else "point"
    if index and middle and not ring and not pinky:
        return "peace"
    if index and middle and ring and pinky:
        return "open"
    if not index and not middle and not ring and not pinky:
        return "fist"
    return "other"
