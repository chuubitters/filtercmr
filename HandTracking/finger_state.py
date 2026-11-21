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


def _unit(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def to_planar(landmarks: np.ndarray, aspect: float) -> np.ndarray:
    """(21,3) normalised landmarks -> (21,2) with x scaled by width/height so
    distances are isotropic in image space."""
    pts = np.asarray(landmarks, dtype=np.float64)[:, :2].copy()
    pts[:, 0] *= aspect
    return pts


def finger_extended(landmarks: np.ndarray, aspect: float = 16 / 9) -> list[bool]:
    """Return [thumb, index, middle, ring, pinky] extension flags."""
    p = to_planar(landmarks, aspect)
    wrist = p[WRIST]
    out: list[bool] = []

    # Thumb: tip far from the palm side (pinky MCP) compared to its IP joint,
    # and reasonably straight.
    palm = np.linalg.norm(p[MIDDLE_MCP] - wrist) + 1e-9
    thumb_reach = np.linalg.norm(p[THUMB_TIP] - p[PINKY_MCP]) > np.linalg.norm(p[THUMB_IP] - p[PINKY_MCP]) * 1.05
    thumb_out = np.linalg.norm(p[THUMB_TIP] - p[INDEX_MCP]) > 0.5 * palm
    thumb_straight = float(np.dot(_unit(p[THUMB_IP] - p[THUMB_MCP]), _unit(p[THUMB_TIP] - p[THUMB_IP]))) > STRAIGHT_COS
    out.append(bool(thumb_reach and thumb_out and thumb_straight))

    for mcp, pip, _dip, tip in _LONG_FINGERS:
        straight = float(np.dot(_unit(p[pip] - p[mcp]), _unit(p[tip] - p[pip]))) > STRAIGHT_COS
        reach = np.linalg.norm(p[tip] - wrist) > np.linalg.norm(p[pip] - wrist) * REACH_RATIO
        out.append(bool(straight and reach))
    return out


def classify_pose(ext: list[bool]) -> str:
    """Coarse single-hand pose name from extension flags (thumb ignored)."""
    _, index, middle, ring, pinky = ext
    if index and not middle and not ring and not pinky:
        return "point"
    if index and middle and not ring and not pinky:
        return "peace"
    if index and middle and ring and pinky:
        return "open"
    if not index and not middle and not ring and not pinky:
        return "fist"
    return "other"
