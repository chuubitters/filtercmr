import json
import socket

import numpy as np
import pytest

from config import TrackerConfig
from conftest import make_hand
from hand_detector import HandTrack
from udp_sender import UdpSender, build_packet


def _track(label, lm, state="tracked"):
    return HandTrack(label, 0.93, lm, lm, state, last_seen=1.0, first_seen=0.5,
                     extended=[False, True, False, False, False], pose="point")


def _free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def test_packet_schema_and_size():
    lm_l = make_hand(center=(0.3, 0.5))
    lm_r = make_hand(center=(0.7, 0.5))
    lm_r[0] = (1.4, -0.2, 0.1)  # out of frame -> clamped
    data = build_packet(kind="frame", seq=7, now=1.05, hands=[_track("Left", lm_l), _track("Right", lm_r, "held")],
                        frame_size=(1280, 720), mirrored=True, tracking_fps=29.94, latency_ms=12.34)
    assert len(data) < 1500  # fits in one Ethernet MTU even off-loopback
    msg = json.loads(data)
    assert msg["type"] == "frame" and msg["v"] == 1 and msg["seq"] == 7
    assert msg["cam"] == {"status": "ok", "w": 1280, "h": 720, "mirrored": True}
    assert msg["trk"]["fps"] == 29.9
    assert [h["id"] for h in msg["hands"]] == ["Left", "Right"]
    for h in msg["hands"]:
        lm = np.array(h["lm"]).reshape(21, 3)
        assert lm.shape == (21, 3)
        assert lm[:, :2].min() >= 0.0 and lm[:, :2].max() <= 1.0
        assert h["ext"] == [0, 1, 0, 0, 0] and h["pose"] == "point"
    assert msg["hands"][1]["state"] == "held"
    assert msg["hands"][1]["age"] == pytest.approx(50.0, abs=0.2)
    assert msg["hands"][1]["lm"][0] == 1.0 and msg["hands"][1]["lm"][1] == 0.0


def test_heartbeat_has_no_hands():
    msg = json.loads(build_packet(kind="heartbeat", seq=1, now=0.0, camera_status="reconnecting"))
    assert msg["hands"] == [] and msg["cam"]["status"] == "reconnecting"


def test_nan_is_rejected():
    lm = make_hand()
    lm[2, 2] = np.nan
    with pytest.raises(ValueError):
        build_packet(kind="frame", seq=1, now=0.0, hands=[_track("Left", lm)])


def test_loopback_roundtrip_and_sequence():
    port = _free_port()
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", port))
    rx.settimeout(2.0)
    tx = UdpSender("127.0.0.1", port)
    try:
        for _ in range(3):
            assert tx.send(build_packet(kind="frame", seq=tx.next_seq(), now=0.0, hands=[_track("Left", make_hand())]))
        seqs = [json.loads(rx.recvfrom(65535)[0])["seq"] for _ in range(3)]
        assert seqs == [1, 2, 3]
    finally:
        tx.close()
        rx.close()


def test_send_without_listener_does_not_raise():
    tx = UdpSender("127.0.0.1", _free_port())
    for _ in range(20):
        tx.send(b"{}")
    tx.close()


def test_config_cli_flags():
    cfg = TrackerConfig.from_args(["--no-mirror", "--udp-port", "6000", "--min-detection-confidence", "0.8",
                                   "--max-hands", "1", "--no-preview", "--swap-handedness"])
    assert cfg.mirror is False and cfg.udp_port == 6000 and cfg.min_detection_confidence == 0.8
    assert cfg.max_hands == 1 and cfg.preview is False and cfg.swap_handedness is True
    d = TrackerConfig.from_args([])
    assert d.mirror is True and d.smoothing is True and d.udp_port == 5065


@pytest.mark.parametrize("args", [["--min-tracking-confidence", "1.5"], ["--max-hands", "3"], ["--udp-port", "0"]])
def test_config_validation(args):
    with pytest.raises(ValueError):
        TrackerConfig.from_args(args)
