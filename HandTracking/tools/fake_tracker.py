"""Send synthetic, animated hand-tracking packets to Unity (no camera needed).

Uses the real packet encoder (udp_sender.build_packet), so Unity receives
exactly what the real tracker sends. Useful for testing the Unity side
without a webcam, or before MediaPipe is set up.

    python tools/fake_tracker.py                     # both hands, pose cycles point -> frame -> peace -> open
    python tools/fake_tracker.py --pose point
    python tools/fake_tracker.py --hands left --pose peace
    python tools/fake_tracker.py --drop-every 4      # hide the right hand for 1 s every 4 s
    python tools/fake_tracker.py --stream-video      # also send synthetic video on port 5066

With --stream-video, each frame shows rings drawn where the landmarks are.
In Unity the landmark overlay should sit exactly on these rings, which
checks mirroring and aspect-ratio mapping end to end.
"""

from __future__ import annotations

import argparse
import math
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import cv2  # noqa: E402
import numpy as np  # noqa: E402

from finger_state import FINGERTIPS, classify_pose, finger_extended  # noqa: E402
from hand_detector import HandTrack  # noqa: E402
from synthetic import make_hand  # noqa: E402
from udp_sender import UdpSender, build_packet  # noqa: E402
from video_stream import VideoStreamer  # noqa: E402

POSES = {
    "point": (False, True, False, False, False),
    "peace": (False, True, True, False, False),
    "frame": (True, True, False, False, False),   # L shape: thumb + index
    "open": (True, True, True, True, True),
    "fist": (False, False, False, False, False),
}
CYCLE = ("point", "frame", "peace", "open")


def pose_at(args, t: float) -> str:
    if args.pose != "cycle":
        return args.pose
    return CYCLE[int(t // args.cycle_seconds) % len(CYCLE)]


def build_hands(args, t: float, aspect: float):
    """Return HandTrack list in DISPLAY (mirrored) coordinates."""
    pose = pose_at(args, t)
    hands = []
    for label, cx, phase in (("Left", 0.32, 0.0), ("Right", 0.68, math.pi)):
        if args.hands != "both" and args.hands != label.lower():
            continue
        if label == "Right" and args.drop_every > 0 and (t % args.drop_every) > args.drop_every - 1.0:
            continue  # simulate a lost hand for the last second of each period
        center = (cx + 0.06 * math.sin(t * 0.9 + phase), 0.58 + 0.05 * math.cos(t * 1.3 + phase))
        tilt = 12 * math.sin(t * 0.7 + phase) + (-10 if label == "Left" else 10)
        lm = make_hand(POSES[pose], angle_deg=tilt, center=center, scale=0.13, aspect=aspect)
        ext = finger_extended(lm, aspect)
        hands.append(HandTrack(label, 0.96, lm, lm, "tracked", t, 0.0, ext, classify_pose(ext)))
    return hands


def render_frame(hands, w: int, h: int, t: float) -> np.ndarray:
    """Synthetic RAW camera frame (un-mirrored, as a webcam delivers it)."""
    img = np.full((h, w, 3), (47, 21, 7), np.uint8)  # navy
    for gx in range(0, w, w // 16):
        cv2.line(img, (gx, 0), (gx, h), (90, 50, 20), 1)
    for gy in range(0, h, h // 9):
        cv2.line(img, (0, gy), (w, gy), (90, 50, 20), 1)
    # Raw-frame marker: top-left of the *camera* image. In a mirrored display it appears top-right.
    cv2.rectangle(img, (8, 8), (150, 40), (255, 217, 0), -1)
    cv2.putText(img, "RAW TOP-LEFT", (14, 31), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (13, 7, 5), 1, cv2.LINE_AA)
    cv2.putText(img, f"FAKE CAMERA {t:6.1f}s", (w // 2 - 110, h - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.7,
                (255, 223, 145), 2, cv2.LINE_AA)
    for hand in hands:
        for i, (x, y, _z) in enumerate(hand.landmarks):
            raw_x = 1.0 - x  # display coords are mirrored; the raw frame is not
            r = 14 if i in FINGERTIPS else 7
            cv2.circle(img, (int(raw_x * w), int(y * h)), r, (255, 255, 255), 2, cv2.LINE_AA)
    return img


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5065)
    ap.add_argument("--pose", choices=list(POSES) + ["cycle"], default="cycle")
    ap.add_argument("--cycle-seconds", type=float, default=4.0)
    ap.add_argument("--hands", choices=["both", "left", "right"], default="both")
    ap.add_argument("--drop-every", type=float, default=0.0, help="hide the right hand 1 s every N s (0 = never)")
    ap.add_argument("--fps", type=float, default=30.0)
    ap.add_argument("--duration", type=float, default=0.0, help="seconds to run (0 = until Ctrl+C)")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--stream-video", action="store_true")
    ap.add_argument("--video-port", type=int, default=5066)
    args = ap.parse_args(argv)

    aspect = args.width / args.height
    sender = UdpSender(args.host, args.port)
    streamer = VideoStreamer(args.host, args.video_port, width=640, quality=70, max_fps=args.fps) if args.stream_video else None
    print(f"fake tracker -> udp://{args.host}:{args.port}"
          + (f", video -> :{args.video_port}" if streamer else "") + "  (Ctrl+C to stop)", flush=True)

    t0 = time.monotonic()
    dt = 1.0 / max(args.fps, 1.0)
    next_t = t0
    last_pose = None
    try:
        while True:
            now = time.monotonic()
            t = now - t0
            if args.duration and t >= args.duration:
                break
            hands = build_hands(args, t, aspect)
            sender.send(build_packet(kind="frame", seq=sender.next_seq(), now=now, hands=hands,
                                     frame_size=(args.width, args.height), mirrored=True,
                                     tracking_fps=args.fps, latency_ms=5.0))
            if streamer is not None:
                streamer.send(render_frame(hands, args.width, args.height, t), now)
            pose = pose_at(args, t)
            if pose != last_pose:
                print(f"{t:6.1f}s pose={pose} hands={[h.label for h in hands]}", flush=True)
                last_pose = pose
            next_t += dt
            time.sleep(max(0.0, next_t - time.monotonic()))
    except KeyboardInterrupt:
        pass
    finally:
        sender.send(build_packet(kind="bye", seq=sender.next_seq(), now=time.monotonic(),
                                 camera_status="stopped", tracker_status="stopped"))
        sender.close()
        if streamer is not None:
            streamer.close()
        print(f"sent {sender.sent} packets", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
