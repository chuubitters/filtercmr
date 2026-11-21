"""Optional camera-frame forwarding (Python -> Unity), port 5066 by default.

Fallback for setups where Unity cannot open the webcam while Python is
using it. Off unless the tracker is started with ``--stream-video``. It is
independent of the tracking packets on port 5065, which are unchanged.

Each camera frame is JPEG-encoded and split into UDP chunks small enough
for macOS's default 9216-byte datagram limit:

    header (20 bytes, big-endian)                     payload
    ┌──────┬──────────┬───────┬───────┬───────────┬──────┬──────┐┌──────────┐
    │"FCV1"│ frame_id │ index │ count │ total_len │ w    │ h    ││ ≤ 8000 B │
    │ 4s   │ uint32   │ uint16│ uint16│ uint32    │uint16│uint16││ JPEG     │
    └──────┴──────────┴───────┴───────┴───────────┴──────┴──────┘└──────────┘

Frames are the raw (un-mirrored) camera image. The receiver applies the same
mirroring it uses for the webcam (``cam.mirrored`` in tracking packets). A
frame with any missing chunk is discarded by the receiver.
"""

from __future__ import annotations

import logging
import socket
import struct
from typing import List, Optional

import cv2
import numpy as np

log = logging.getLogger("filtercmr.video")

MAGIC = b"FCV1"
HEADER = struct.Struct(">4sIHHIHH")
HEADER_SIZE = HEADER.size  # 20
CHUNK_PAYLOAD = 8000
MAX_CHUNKS = 0xFFFF


def encode_jpeg(frame_bgr: np.ndarray, width: int, quality: int) -> tuple[bytes, int, int]:
    h, w = frame_bgr.shape[:2]
    if width and w > width:
        nh = max(1, int(round(h * width / w)))
        frame_bgr = cv2.resize(frame_bgr, (width, nh), interpolation=cv2.INTER_AREA)
        h, w = nh, width
    ok, buf = cv2.imencode(".jpg", frame_bgr, [int(cv2.IMWRITE_JPEG_QUALITY), int(quality)])
    if not ok:
        raise RuntimeError("JPEG encoding failed")
    return buf.tobytes(), w, h


def make_chunks(jpeg: bytes, frame_id: int, width: int, height: int, payload: int = CHUNK_PAYLOAD) -> List[bytes]:
    count = max(1, (len(jpeg) + payload - 1) // payload)
    if count > MAX_CHUNKS:
        raise ValueError("frame too large")
    fid = frame_id & 0xFFFFFFFF
    return [
        HEADER.pack(MAGIC, fid, i, count, len(jpeg), width, height) + jpeg[i * payload:(i + 1) * payload]
        for i in range(count)
    ]


def parse_chunk(datagram: bytes):
    """Inverse of one ``make_chunks`` element (used by tests and tools)."""
    if len(datagram) < HEADER_SIZE:
        raise ValueError("short datagram")
    magic, fid, idx, count, total, w, h = HEADER.unpack_from(datagram)
    if magic != MAGIC:
        raise ValueError("bad magic")
    return fid, idx, count, total, w, h, datagram[HEADER_SIZE:]


class VideoStreamer:
    def __init__(self, host: str = "127.0.0.1", port: int = 5066, width: int = 640,
                 quality: int = 70, max_fps: float = 30.0) -> None:
        self.address = (host, port)
        self.width = width
        self.quality = quality
        self.min_dt = 1.0 / max_fps if max_fps > 0 else 0.0
        self.frame_id = 0
        self.frames_sent = 0
        self.errors = 0
        self.last_size = 0
        self._last_t: Optional[float] = None
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 1 << 20)
        except OSError:  # pragma: no cover - platform dependent
            pass
        self._sock.setblocking(False)

    def send(self, frame_bgr: np.ndarray, now: float) -> bool:
        if self._last_t is not None and self.min_dt and now - self._last_t < self.min_dt:
            return False
        self._last_t = now
        try:
            jpeg, w, h = encode_jpeg(frame_bgr, self.width, self.quality)
        except Exception as exc:
            self.errors += 1
            log.debug("encode failed: %s", exc)
            return False
        self.frame_id = (self.frame_id + 1) & 0xFFFFFFFF
        self.last_size = len(jpeg)
        try:
            for chunk in make_chunks(jpeg, self.frame_id, w, h):
                self._sock.sendto(chunk, self.address)
        except OSError:
            # Buffer full / nobody listening: the receiver drops incomplete frames.
            self.errors += 1
            return False
        self.frames_sent += 1
        return True

    def close(self) -> None:
        self._sock.close()
