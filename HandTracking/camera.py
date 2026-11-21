"""Threaded webcam capture with automatic reconnection.

A background thread keeps grabbing frames so the tracker always works on the
newest one (stale frames are dropped instead of queued, which keeps latency
low when detection is slower than the camera).
"""

from __future__ import annotations

import logging
import sys
import threading
import time
from dataclasses import dataclass
from typing import Optional

import cv2
import numpy as np

log = logging.getLogger("filtercmr.camera")


class CameraStatus:
    STARTING = "starting"
    OK = "ok"
    RECONNECTING = "reconnecting"
    ERROR = "error"


@dataclass
class Frame:
    image: np.ndarray  # BGR, as delivered by the camera (NOT mirrored)
    frame_id: int
    timestamp: float  # time.monotonic() when grabbed


def _default_backend() -> int:
    if sys.platform == "darwin":
        return cv2.CAP_AVFOUNDATION
    if sys.platform.startswith("win"):
        return cv2.CAP_DSHOW
    return cv2.CAP_ANY


class CameraStream:
    def __init__(
        self,
        index: int = 0,
        width: int = 1280,
        height: int = 720,
        fps: int = 30,
        timeout_s: float = 2.0,
        backend: Optional[int] = None,
    ) -> None:
        self.index = index
        self.width = width
        self.height = height
        self.fps = fps
        self.timeout_s = timeout_s
        self.backend = _default_backend() if backend is None else backend

        self.status = CameraStatus.STARTING
        self.last_error = ""
        self.actual_size = (0, 0)
        self.reconnects = 0

        self._lock = threading.Lock()
        self._latest: Optional[Frame] = None
        self._last_returned_id = -1
        self._frame_counter = 0
        self._stop = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._cap: Optional[cv2.VideoCapture] = None

    # ------------------------------------------------------------------ API
    def start(self) -> "CameraStream":
        if self._thread is None:
            self._thread = threading.Thread(target=self._run, name="camera", daemon=True)
            self._thread.start()
        return self

    def stop(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)
            self._thread = None
        self._release()

    def read_latest(self) -> Optional[Frame]:
        """Newest frame not yet returned, or None if nothing new arrived."""
        with self._lock:
            f = self._latest
            if f is None or f.frame_id == self._last_returned_id:
                return None
            self._last_returned_id = f.frame_id
            return f

    # ------------------------------------------------------------ internals
    def _open(self) -> bool:
        self._release()
        cap = cv2.VideoCapture(self.index, self.backend)
        if not cap.isOpened():
            cap.release()
            self.last_error = (
                f"cannot open camera {self.index}. On macOS, grant camera access to your "
                "terminal app in System Settings > Privacy & Security > Camera."
            )
            return False
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
        cap.set(cv2.CAP_PROP_FPS, self.fps)
        # Small buffer -> lower latency where the backend supports it.
        cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        self._cap = cap
        return True

    def _release(self) -> None:
        if self._cap is not None:
            try:
                self._cap.release()
            except Exception:  # pragma: no cover - defensive
                pass
            self._cap = None

    def _run(self) -> None:
        backoff = 0.5
        last_ok = time.monotonic()
        while not self._stop.is_set():
            if self._cap is None:
                if self._open():
                    log.info("camera %d opened", self.index)
                    last_ok = time.monotonic()
                    backoff = 0.5
                else:
                    self.status = CameraStatus.ERROR if self.reconnects == 0 else CameraStatus.RECONNECTING
                    log.warning("%s (retrying in %.1fs)", self.last_error, backoff)
                    self._stop.wait(backoff)
                    backoff = min(backoff * 2.0, 5.0)
                    self.reconnects += 1
                    continue

            ok, image = self._cap.read()
            now = time.monotonic()
            if ok and image is not None and image.size > 0:
                last_ok = now
                self.status = CameraStatus.OK
                self.actual_size = (image.shape[1], image.shape[0])
                self._frame_counter += 1
                with self._lock:
                    self._latest = Frame(image, self._frame_counter, now)
                continue

            if now - last_ok > self.timeout_s:
                self.last_error = f"no frames for {self.timeout_s:.1f}s; reopening camera"
                log.warning(self.last_error)
                self.status = CameraStatus.RECONNECTING
                self.reconnects += 1
                self._release()
            else:
                time.sleep(0.005)
        self._release()
