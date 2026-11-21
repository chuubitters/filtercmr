"""Step-by-step environment check for the FILTER CMR tracker.

    python tools/check_setup.py              # all checks
    python tools/check_setup.py --skip-camera

Checks: Python version, packages, model file (downloads if missing),
MediaPipe initialisation + inference on a blank frame, webcam open + frame
grab, and a UDP loopback round-trip using the real packet encoder.
Exit code is the number of failed checks.
"""

from __future__ import annotations

import argparse
import json
import socket
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RESULTS = []


def check(name):
    def deco(fn):
        def wrapper(*a, **kw):
            t = time.monotonic()
            try:
                detail = fn(*a, **kw) or ""
                RESULTS.append((name, True, detail))
                print(f"[ OK ] {name}  {detail}  ({(time.monotonic()-t)*1000:.0f} ms)")
                return True
            except Exception as exc:
                RESULTS.append((name, False, str(exc)))
                print(f"[FAIL] {name}  {exc}")
                return False
        return wrapper
    return deco


@check("python >= 3.9")
def c_python():
    if sys.version_info < (3, 9):
        raise RuntimeError(sys.version)
    return sys.version.split()[0]


@check("packages")
def c_packages():
    import cv2
    import mediapipe
    import numpy
    return f"mediapipe {mediapipe.__version__}, opencv {cv2.__version__}, numpy {numpy.__version__}"


@check("hand model")
def c_model(path):
    from hand_detector import ensure_model
    p = ensure_model(path)
    return f"{p} ({p.stat().st_size/1e6:.1f} MB)"


@check("mediapipe init + inference")
def c_mediapipe(cfg):
    import numpy as np
    from hand_detector import HandDetector
    det = HandDetector(cfg)
    det.open()
    try:
        blank = np.zeros((720, 1280, 3), dtype=np.uint8)
        hands = det.process(blank, time.monotonic())
        if det.status != "ok":
            raise RuntimeError(det.last_error)
        return f"blank frame -> {len(hands)} hands (expected 0)"
    finally:
        det.close()


@check("webcam")
def c_camera(index):
    from camera import CameraStream
    cam = CameraStream(index).start()
    try:
        deadline = time.monotonic() + 6.0
        while time.monotonic() < deadline:
            f = cam.read_latest()
            if f is not None:
                h, w = f.image.shape[:2]
                return f"camera {index}: {w}x{h}"
            time.sleep(0.05)
        raise RuntimeError(cam.last_error or "no frame within 6 s")
    finally:
        cam.stop()


@check("udp loopback")
def c_udp(port):
    from udp_sender import UdpSender, build_packet
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", port))
    rx.settimeout(2.0)
    tx = UdpSender("127.0.0.1", port)
    try:
        tx.send(build_packet(kind="heartbeat", seq=tx.next_seq(), now=time.monotonic()))
        data, _ = rx.recvfrom(65535)
        msg = json.loads(data)
        if msg.get("type") != "heartbeat":
            raise RuntimeError(f"unexpected packet {msg}")
        return f"port {port}, {len(data)} bytes"
    finally:
        tx.close()
        rx.close()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera-index", type=int, default=0)
    ap.add_argument("--skip-camera", action="store_true")
    ap.add_argument("--udp-port", type=int, default=5099, help="free port for the loopback test")
    args = ap.parse_args(argv)

    if not c_python() or not c_packages():
        return 1
    from config import TrackerConfig
    cfg = TrackerConfig()
    if c_model(cfg.model_path):
        c_mediapipe(cfg)
    if not args.skip_camera:
        c_camera(args.camera_index)
    c_udp(args.udp_port)

    failed = [r for r in RESULTS if not r[1]]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} checks passed")
    return len(failed)


if __name__ == "__main__":
    sys.exit(main())
