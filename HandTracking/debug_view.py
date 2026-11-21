"""OpenCV debug preview for the tracking service.

Shows the (mirrored) camera image with landmarks, fingertip markers, hand
labels, finger-extension flags, detected pose, FPS and connection info.
This window is for verifying tracking only; the real visuals are rendered
by Unity.
"""

from __future__ import annotations

from typing import Sequence

import cv2
import numpy as np

from finger_state import FINGER_NAMES, FINGERTIPS
from hand_detector import HandTrack

WINDOW = "FILTER CMR // tracker debug"

# FILTER CMR palette (BGR)
ELECTRIC_BLUE = (255, 75, 22)  # #164BFF
CYAN = (255, 217, 0)  # #00D9FF
LIGHT_BLUE = (255, 223, 145)  # #91DFFF
NAVY = (47, 21, 7)  # #07152F
BLACK = (13, 7, 5)  # #05070D
WHITE = (240, 240, 240)
AMBER = (0, 190, 255)

HAND_CONNECTIONS = (
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (17, 18), (18, 19), (19, 20),
    (0, 17),
)

FONT = cv2.FONT_HERSHEY_SIMPLEX


def _text(img, s, org, scale=0.45, color=LIGHT_BLUE, thick=1):
    cv2.putText(img, s, org, FONT, scale, BLACK, thick + 2, cv2.LINE_AA)
    cv2.putText(img, s, org, FONT, scale, color, thick, cv2.LINE_AA)


def draw(
    frame_bgr: np.ndarray,
    hands: Sequence[HandTrack],
    *,
    mirrored: bool,
    camera_status: str,
    tracker_status: str,
    tracking_fps: float,
    latency_ms: float,
    udp_target: str,
    udp_sent: int,
    smoothing: bool,
) -> np.ndarray:
    img = cv2.flip(frame_bgr, 1) if mirrored else frame_bgr.copy()
    h, w = img.shape[:2]

    for hand in hands:
        pts = np.column_stack((hand.landmarks[:, 0] * w, hand.landmarks[:, 1] * h)).astype(np.int32)
        line_color = ELECTRIC_BLUE if hand.state == "tracked" else (120, 90, 60)
        for a, b in HAND_CONNECTIONS:
            cv2.line(img, tuple(pts[a]), tuple(pts[b]), line_color, 2, cv2.LINE_AA)
        for i, p in enumerate(pts):
            r = 7 if i in FINGERTIPS else 3
            cv2.circle(img, tuple(p), r, CYAN if i in FINGERTIPS else LIGHT_BLUE, -1 if i not in FINGERTIPS else 2, cv2.LINE_AA)
        # index fingertip coordinates
        ix, iy = hand.landmarks[8, 0], hand.landmarks[8, 1]
        _text(img, f"{ix:.3f},{iy:.3f}", (pts[8][0] + 10, pts[8][1] - 8), 0.4, CYAN)
        wx, wy = pts[0]
        flags = "".join(n[0].upper() if e else "." for n, e in zip(FINGER_NAMES, hand.extended))
        _text(img, f"{hand.label.upper()} {hand.score:.2f} [{flags}] {hand.pose}  pinch {hand.pinch:.2f}", (wx - 60, wy + 28), 0.5,
              CYAN if hand.pose in ("frame", "pinch") else WHITE)
        if hand.state != "tracked":
            _text(img, "HELD", (wx - 60, wy + 48), 0.45, AMBER)

    # header bar
    cv2.rectangle(img, (0, 0), (w, 30), NAVY, -1)
    cv2.line(img, (0, 30), (w, 30), ELECTRIC_BLUE, 1)
    _text(img, "FILTER CMR // TRACKER", (10, 20), 0.5, CYAN)
    status = (
        f"CAM {camera_status.upper()}  TRK {tracker_status.upper()}  "
        f"{tracking_fps:5.1f} FPS  {latency_ms:4.1f} MS  HANDS {len(hands)}  "
        f"UDP {udp_target} #{udp_sent}"
    )
    _text(img, status, (220, 20), 0.42, LIGHT_BLUE)

    # footer
    cv2.rectangle(img, (0, h - 24), (w, h), NAVY, -1)
    _text(
        img,
        f"[Q] quit  [R] reset  [M] mirror:{'on' if mirrored else 'off'}  "
        f"[S] smoothing:{'on' if smoothing else 'off'}  [H] swap L/R",
        (10, h - 8),
        0.42,
        LIGHT_BLUE,
    )
    if not hands:
        msg = "RAISE BOTH HANDS"
        (tw, _), _ = cv2.getTextSize(msg, FONT, 0.8, 2)
        _text(img, msg, ((w - tw) // 2, h // 2), 0.8, CYAN, 2)
    return img


def placeholder(message: str, size=(1280, 720)) -> np.ndarray:
    img = np.full((size[1], size[0], 3), BLACK, dtype=np.uint8)
    _text(img, "FILTER CMR // TRACKER", (20, 40), 0.7, CYAN, 2)
    y = 90
    for line in message.split("\n"):
        _text(img, line, (20, y), 0.55, LIGHT_BLUE)
        y += 28
    return img


def show(img: np.ndarray) -> int:
    cv2.imshow(WINDOW, img)
    return cv2.waitKey(1) & 0xFF


def close() -> None:
    try:
        cv2.destroyWindow(WINDOW)
    except cv2.error:
        pass
