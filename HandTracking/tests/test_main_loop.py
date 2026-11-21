"""Integration test of main.run(): fake camera + mocked MediaPipe -> real UDP."""

import json
import socket
import time

import numpy as np
import pytest

import main as tracker_main
from camera import CameraStatus, Frame
from conftest import fake_result, make_hand


class FakeCamera:
    frames_to_send = 8

    def __init__(self, *a, **kw):
        self.status = CameraStatus.OK
        self.actual_size = (1280, 720)
        self.last_error = ""
        self.n = 0

    def start(self):
        return self

    def stop(self):
        pass

    def read_latest(self):
        self.n += 1
        if self.n > self.frames_to_send:
            raise KeyboardInterrupt  # ends the loop the same way Ctrl+C does
        return Frame(np.zeros((720, 1280, 3), np.uint8), self.n, time.monotonic())


class FakeLandmarker:
    def detect_for_video(self, image, ts):
        two_index = (False, True, False, False, False)
        return fake_result([("Left", 0.9, make_hand(two_index, center=(0.7, 0.5))),
                            ("Right", 0.9, make_hand(two_index, center=(0.3, 0.5)))])

    def close(self):
        pass


def test_run_sends_frames_and_bye(cfg, monkeypatch):
    pytest.importorskip("mediapipe")
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", 0))
    rx.settimeout(2.0)
    cfg.udp_port = rx.getsockname()[1]
    cfg.max_tracking_fps = 0

    def fake_open(self):
        self._landmarker = FakeLandmarker()
        self.status = "ok"

    monkeypatch.setattr(tracker_main, "CameraStream", FakeCamera)
    monkeypatch.setattr(tracker_main.HandDetector, "open", fake_open)
    assert tracker_main.run(cfg) == 0

    msgs = []
    try:
        while True:
            msgs.append(json.loads(rx.recvfrom(65535)[0]))
            if msgs[-1]["type"] == "bye":
                break
    finally:
        rx.close()

    frames = [m for m in msgs if m["type"] == "frame"]
    assert len(frames) == FakeCamera.frames_to_send
    assert msgs[-1]["type"] == "bye"
    last = frames[-1]
    assert last["cam"]["w"] == 1280 and last["cam"]["mirrored"] is True
    assert [h["id"] for h in last["hands"]] == ["Left", "Right"]
    assert all(h["pose"] == "point" for h in last["hands"])
    # mirrored output: raw x=0.7 for Left becomes ~0.3 on screen
    left_wrist_x = last["hands"][0]["lm"][0]
    assert left_wrist_x == pytest.approx(0.3, abs=0.05)
    seqs = [m["seq"] for m in msgs]
    assert seqs == sorted(seqs) and len(set(seqs)) == len(seqs)


def test_run_with_stream_video_sends_chunks_without_changing_tracking(cfg, monkeypatch):
    pytest.importorskip("mediapipe")
    from video_stream import parse_chunk

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", 0))
    rx.settimeout(2.0)
    vrx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    vrx.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 20)
    vrx.bind(("127.0.0.1", 0))
    vrx.settimeout(2.0)
    cfg.udp_port = rx.getsockname()[1]
    cfg.video_port = vrx.getsockname()[1]
    cfg.stream_video = True
    cfg.max_tracking_fps = 0

    def fake_open(self):
        self._landmarker = FakeLandmarker()
        self.status = "ok"

    monkeypatch.setattr(tracker_main, "CameraStream", FakeCamera)
    monkeypatch.setattr(tracker_main.HandDetector, "open", fake_open)
    assert tracker_main.run(cfg) == 0
    try:
        first = json.loads(rx.recvfrom(65535)[0])
        assert first["type"] == "frame" and set(first) == {"type", "v", "seq", "t", "cam", "trk", "hands"}
        fid, idx, count, total, w, h, _ = parse_chunk(vrx.recvfrom(65535)[0])
        assert (w, h) == (640, 360) and count >= 1 and total > 0
    finally:
        rx.close()
        vrx.close()


def test_fake_tracker_tool_sends_valid_packets():
    import subprocess
    import sys
    from pathlib import Path

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", 0))
    rx.settimeout(3.0)
    port = rx.getsockname()[1]
    tool = Path(__file__).resolve().parents[1] / "tools" / "fake_tracker.py"
    proc = subprocess.run([sys.executable, str(tool), "--port", str(port), "--duration", "0.5", "--pose", "peace"],
                          capture_output=True, text=True, timeout=20)
    assert proc.returncode == 0, proc.stderr
    msgs = []
    try:
        while True:
            msgs.append(json.loads(rx.recvfrom(65535)[0]))
            if msgs[-1]["type"] == "bye":
                break
    finally:
        rx.close()
    frames = [m for m in msgs if m["type"] == "frame"]
    assert len(frames) >= 10
    assert all(h["pose"] == "peace" for m in frames for h in m["hands"])
    assert [h["id"] for h in frames[0]["hands"]] == ["Left", "Right"]
