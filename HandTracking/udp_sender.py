"""UDP packet encoding and sending (Python -> Unity).

Packets are compact UTF-8 JSON, one datagram per message. The schema is
documented in ``Documentation/UDP_PROTOCOL.md``. UDP is fire-and-forget:
if Unity is not running, packets are simply dropped and the tracker keeps
going.
"""

from __future__ import annotations

import json
import logging
import socket
from typing import Dict, Iterable, Optional

import numpy as np

from config import PROTOCOL_VERSION
from hand_detector import HandTrack, clamp01

log = logging.getLogger("filtercmr.udp")

# Stay well under the 65 507-byte UDP payload limit; a 2-hand frame is ~1-1.5 kB.
MAX_PACKET_BYTES = 8192
COORD_DECIMALS = 4


def _hand_dict(h: HandTrack, now: float) -> Dict:
    lm = clamp01(h.landmarks)
    flat = np.round(lm.reshape(-1), COORD_DECIMALS)
    return {
        "id": h.label,
        "score": round(float(h.score), 3),
        "state": h.state,
        "age": round(h.age_ms(now), 1),
        "ext": [1 if e else 0 for e in h.extended],
        "pose": h.pose,
        "lm": [float(v) for v in flat],
    }


def build_packet(
    *,
    kind: str,
    seq: int,
    now: float,
    hands: Iterable[HandTrack] = (),
    camera_status: str = "ok",
    frame_size: tuple = (0, 0),
    mirrored: bool = True,
    tracker_status: str = "ok",
    tracking_fps: float = 0.0,
    latency_ms: float = 0.0,
) -> bytes:
    """Encode one message. ``kind`` is "frame", "heartbeat" or "bye"."""
    msg = {
        "type": kind,
        "v": PROTOCOL_VERSION,
        "seq": int(seq),
        "t": round(now * 1000.0, 1),
        "cam": {
            "status": camera_status,
            "w": int(frame_size[0]),
            "h": int(frame_size[1]),
            "mirrored": bool(mirrored),
        },
        "trk": {
            "status": tracker_status,
            "fps": round(float(tracking_fps), 1),
            "latency": round(float(latency_ms), 1),
        },
        "hands": [_hand_dict(h, now) for h in hands],
    }
    data = json.dumps(msg, separators=(",", ":"), allow_nan=False).encode("utf-8")
    if len(data) > MAX_PACKET_BYTES:
        raise ValueError(f"packet too large: {len(data)} bytes")
    return data


class UdpSender:
    def __init__(self, host: str = "127.0.0.1", port: int = 5065) -> None:
        self.address = (host, port)
        self.seq = 0
        self.sent = 0
        self.errors = 0
        self._sock: Optional[socket.socket] = None
        self._open()

    def _open(self) -> None:
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.setblocking(False)

    def next_seq(self) -> int:
        self.seq = (self.seq + 1) % (2**31)
        return self.seq

    def send(self, data: bytes) -> bool:
        if self._sock is None:
            self._open()
        try:
            self._sock.sendto(data, self.address)
            self.sent += 1
            return True
        except (BlockingIOError, InterruptedError):
            self.errors += 1  # socket buffer full: drop this packet
        except OSError as exc:
            # e.g. ECONNREFUSED surfaced from a previous datagram, or network down.
            self.errors += 1
            if self.errors % 100 == 1:
                log.debug("udp send failed: %s", exc)
        return False

    def close(self) -> None:
        if self._sock is not None:
            self._sock.close()
            self._sock = None
