"""FILTER CMR hand-tracking service.

Webcam -> MediaPipe Hand Landmarker -> One Euro smoothing -> UDP (Unity).

Run:  python main.py            (with debug preview window)
      python main.py --no-preview
      python main.py --help
"""

from __future__ import annotations

import logging
import sys
import time
from typing import List, Optional

from camera import CameraStatus, CameraStream
from config import TrackerConfig
from hand_detector import HandDetector, HandTrack
from udp_sender import UdpSender, build_packet
from video_stream import VideoStreamer

log = logging.getLogger("filtercmr")


class RateMeter:
    """Exponential moving average of events per second."""

    def __init__(self, smoothing: float = 0.9) -> None:
        self.k = smoothing
        self.value = 0.0
        self._last: Optional[float] = None

    def tick(self, now: float) -> float:
        if self._last is not None:
            dt = now - self._last
            if dt > 0:
                inst = 1.0 / dt
                self.value = inst if self.value == 0 else self.k * self.value + (1 - self.k) * inst
        self._last = now
        return self.value

    def reset(self) -> None:
        self.value = 0.0
        self._last = None


def run(cfg: TrackerConfig) -> int:
    camera = CameraStream(
        cfg.camera_index, cfg.capture_width, cfg.capture_height, cfg.capture_fps, cfg.camera_timeout_s
    ).start()
    detector = HandDetector(cfg)
    sender = UdpSender(cfg.udp_host, cfg.udp_port)
    fps = RateMeter()
    streamer = None
    if cfg.stream_video:
        streamer = VideoStreamer(cfg.udp_host, cfg.video_port, cfg.video_width, cfg.video_quality,
                                 max_fps=cfg.capture_fps)
        log.info("video forwarding on udp://%s:%d (%dpx, q%d)", cfg.udp_host, cfg.video_port,
                 cfg.video_width, cfg.video_quality)

    preview = cfg.preview
    if preview:
        try:
            import debug_view
        except Exception as exc:  # pragma: no cover - e.g. headless OpenCV build
            log.warning("preview disabled: %s", exc)
            preview = False

    log.info("sending to udp://%s:%d (mirror=%s)", cfg.udp_host, cfg.udp_port, cfg.mirror)

    hands: List[HandTrack] = []
    last_send = 0.0
    last_detect = 0.0
    last_placeholder = 0.0
    latency_ms = 0.0
    last_stats = time.monotonic()
    min_detect_dt = 1.0 / cfg.max_tracking_fps if cfg.max_tracking_fps > 0 else 0.0
    heartbeat_dt = 1.0 / max(cfg.heartbeat_hz, 0.1)
    exit_code = 0

    try:
        while True:
            now = time.monotonic()
            frame = camera.read_latest()

            if frame is None or (min_detect_dt and now - last_detect < min_detect_dt):
                # No new frame (or throttled). Keep Unity informed with heartbeats.
                if camera.status != CameraStatus.OK and hands:
                    detector.reset()
                    hands = []
                    fps.reset()
                if now - last_send >= heartbeat_dt:
                    sender.send(
                        build_packet(
                            kind="heartbeat",
                            seq=sender.next_seq(),
                            now=now,
                            hands=hands if camera.status == CameraStatus.OK else (),
                            camera_status=camera.status,
                            frame_size=camera.actual_size,
                            mirrored=cfg.mirror,
                            tracker_status=detector.status,
                            tracking_fps=fps.value,
                            latency_ms=latency_ms,
                        )
                    )
                    last_send = now
                if preview and camera.status != CameraStatus.OK and now - last_placeholder > 0.1:
                    last_placeholder = now
                    key = debug_view.show(
                        debug_view.placeholder(
                            f"camera: {camera.status}\n{camera.last_error}\n\n[Q] quit"
                        )
                    )
                    if key in (ord("q"), 27):
                        break
                else:
                    time.sleep(0.002)
                continue

            last_detect = now
            hands = detector.process(frame.image, frame.timestamp)
            done = time.monotonic()
            latency_ms = (done - frame.timestamp) * 1000.0
            fps.tick(done)

            sender.send(
                build_packet(
                    kind="frame",
                    seq=sender.next_seq(),
                    now=done,
                    hands=hands,
                    camera_status=camera.status,
                    frame_size=camera.actual_size,
                    mirrored=cfg.mirror,
                    tracker_status=detector.status,
                    tracking_fps=fps.value,
                    latency_ms=latency_ms,
                )
            )
            last_send = done
            if streamer is not None:
                streamer.send(frame.image, done)

            if cfg.verbose and done - last_stats > 5.0:
                last_stats = done
                log.info(
                    "fps=%.1f latency=%.1fms hands=%s sent=%d udp_errors=%d detector=%s",
                    fps.value, latency_ms, [f"{h.label}:{h.pose}" for h in hands],
                    sender.sent, sender.errors, detector.status,
                )

            if preview:
                img = debug_view.draw(
                    frame.image,
                    hands,
                    mirrored=cfg.mirror,
                    camera_status=camera.status,
                    tracker_status=detector.status,
                    tracking_fps=fps.value,
                    latency_ms=latency_ms,
                    udp_target=f"{cfg.udp_host}:{cfg.udp_port}",
                    udp_sent=sender.sent,
                    smoothing=cfg.smoothing,
                )
                key = debug_view.show(img)
                if key in (ord("q"), 27):
                    break
                if key == ord("r"):
                    detector.reset()
                    log.info("tracking state reset")
                elif key == ord("m"):
                    cfg.mirror = not cfg.mirror
                    detector.reset()
                    log.info("mirror=%s", cfg.mirror)
                elif key == ord("s"):
                    cfg.smoothing = not cfg.smoothing
                    detector.reset()
                    log.info("smoothing=%s", cfg.smoothing)
                elif key == ord("h"):
                    cfg.swap_handedness = not cfg.swap_handedness
                    detector.reset()
                    log.info("swap_handedness=%s", cfg.swap_handedness)
    except KeyboardInterrupt:
        pass
    except Exception:
        log.exception("fatal error")
        exit_code = 1
    finally:
        try:
            sender.send(
                build_packet(kind="bye", seq=sender.next_seq(), now=time.monotonic(),
                             camera_status="stopped", mirrored=cfg.mirror, tracker_status="stopped")
            )
        except Exception:
            pass
        sender.close()
        if streamer is not None:
            streamer.close()
        detector.close()
        camera.stop()
        if preview:
            debug_view.close()
        log.info("tracker stopped")
    return exit_code


def main(argv: Optional[List[str]] = None) -> int:
    cfg = TrackerConfig.from_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if cfg.verbose else logging.INFO,
        format="%(asctime)s %(levelname)-7s %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )
    return run(cfg)


if __name__ == "__main__":
    sys.exit(main())
