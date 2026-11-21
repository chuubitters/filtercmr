# FILTER CMR — Hand Tracking Service

Webcam → MediaPipe Hand Landmarker → One Euro smoothing → UDP → Unity.

```bash
./setup.sh                    # once
./run.sh                      # start (debug preview)
python tools/udp_monitor.py   # see what Unity receives (in a second terminal, venv active)
```

| File | Role |
|---|---|
| `main.py` | entry point and main loop (frames, heartbeats, preview keys, clean shutdown) |
| `camera.py` | threaded capture (AVFoundation on macOS), newest-frame-wins, auto-reconnect |
| `hand_detector.py` | MediaPipe wrapper, mirroring, Left/Right resolution, smoothing, hold/drop, auto-restart |
| `one_euro.py` | vectorised One Euro Filter |
| `finger_state.py` | advisory finger-extension and pose hint (point / peace / open / fist) |
| `udp_sender.py` | packet encoding and non-blocking UDP send |
| `debug_view.py` | OpenCV preview overlay |
| `config.py` | every option and its CLI flag |
| `tools/udp_monitor.py` | UDP listener / stand-in for Unity |
| `tools/check_setup.py` | environment self-test |
| `tests/` | pytest suite (`python -m pytest -q tests`) |

## Useful options

```
--camera-index N             which webcam
--capture-width/-height      requested capture size (default 1280x720)
--tracking-width PX          detection resolution (default 640) — lower = faster
--max-tracking-fps F         cap detections per second (default 60)
--min-detection-confidence   0–1 (default 0.6)
--min-presence-confidence    0–1 (default 0.5)
--min-tracking-confidence    0–1 (default 0.5)
--euro-min-cutoff / --euro-beta   smoothing (default 1.2 / 6.0)
--hand-hold-ms MS            keep a lost hand for MS (default 150)
--no-mirror                  send un-mirrored coordinates
--swap-handedness            if Left/Right come out inverted
--udp-host / --udp-port      target (default 127.0.0.1:5065)
--no-preview, --verbose
```

Protocol: [`../Documentation/UDP_PROTOCOL.md`](../Documentation/UDP_PROTOCOL.md).
