import socket

import cv2
import numpy as np
import pytest

from config import TrackerConfig
from video_stream import CHUNK_PAYLOAD, HEADER_SIZE, VideoStreamer, encode_jpeg, make_chunks, parse_chunk

MACOS_MAX_DGRAM = 9216  # default net.inet.udp.maxdgram


def _test_image(w=1280, h=720):
    img = np.zeros((h, w, 3), np.uint8)
    cv2.rectangle(img, (0, 0), (w // 4, h // 4), (255, 75, 22), -1)  # marker top-left (raw frame)
    noise = np.random.default_rng(0).integers(0, 255, (h, w, 3), dtype=np.uint8)
    return cv2.addWeighted(img, 0.7, noise, 0.3, 0)


def test_chunks_fit_macos_datagram_limit_and_roundtrip():
    jpeg, w, h = encode_jpeg(_test_image(), 640, 70)
    assert (w, h) == (640, 360)
    chunks = make_chunks(jpeg, 42, w, h)
    assert len(chunks) == -(-len(jpeg) // CHUNK_PAYLOAD)
    assert all(len(c) <= HEADER_SIZE + CHUNK_PAYLOAD <= MACOS_MAX_DGRAM for c in chunks)
    parts = {}
    for c in chunks:
        fid, idx, count, total, cw, ch, payload = parse_chunk(c)
        assert (fid, count, total, cw, ch) == (42, len(chunks), len(jpeg), 640, 360)
        parts[idx] = payload
    assert b"".join(parts[i] for i in range(len(chunks))) == jpeg


def test_small_frame_is_single_chunk_and_no_upscale():
    jpeg, w, h = encode_jpeg(np.zeros((90, 160, 3), np.uint8), 640, 50)
    assert (w, h) == (160, 90)
    assert len(make_chunks(jpeg, 1, w, h)) == 1


def test_parse_rejects_garbage():
    with pytest.raises(ValueError):
        parse_chunk(b"short")
    with pytest.raises(ValueError):
        parse_chunk(b"XXXX" + bytes(40))


def test_streamer_loopback_reassembles_and_rate_limits():
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 20)
    rx.bind(("127.0.0.1", 0))
    rx.settimeout(2.0)
    s = VideoStreamer("127.0.0.1", rx.getsockname()[1], width=640, quality=70, max_fps=30)
    try:
        assert s.send(_test_image(), now=1.0)
        assert not s.send(_test_image(), now=1.01)  # throttled (< 1/30 s)
        parts, count = {}, None
        while count is None or len(parts) < count:
            fid, idx, count, total, w, h, payload = parse_chunk(rx.recvfrom(65535)[0])
            parts[idx] = payload
        img = cv2.imdecode(np.frombuffer(b"".join(parts[i] for i in range(count)), np.uint8), cv2.IMREAD_COLOR)
        assert img.shape == (360, 640, 3)
        assert img[10, 10, 0] > 150  # blue marker still top-left: frames are NOT mirrored
    finally:
        s.close()
        rx.close()


def test_config_flags():
    cfg = TrackerConfig.from_args(["--stream-video", "--video-port", "6001", "--video-quality", "55"])
    assert cfg.stream_video and cfg.video_port == 6001 and cfg.video_quality == 55
    assert TrackerConfig.from_args([]).stream_video is False
    with pytest.raises(ValueError):
        TrackerConfig.from_args(["--video-port", "5065"])  # collides with tracking port
