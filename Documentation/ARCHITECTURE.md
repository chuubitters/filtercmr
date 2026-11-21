# Architecture

FILTER CMR is two cooperating processes on the same Mac:

```
┌────────────────────── HandTracking/ (Python) ──────────────────────┐
│ camera.py         capture thread, newest-frame-wins, auto-reopen   │
│      │ BGR frame (un-mirrored)                                     │
│ hand_detector.py  downscale → MediaPipe HandLandmarker (VIDEO)     │
│                   → mirror x → Left/Right resolution               │
│                   → One Euro smoothing → hold / drop on loss       │
│ finger_state.py   advisory finger-extension + pose hint            │
│ udp_sender.py     JSON packet → UDP 127.0.0.1:5065                 │
│ debug_view.py     OpenCV preview (verification only)               │
└────────────────────────────────┬───────────────────────────────────┘
                                 │ UDP (see UDP_PROTOCOL.md)
┌────────────────────────────────▼──────── UnityProject/ (Unity 6) ──┐
│ Scripts/HandTracking  UDP receiver thread, tracker/hand timeouts   │  M2
│ Scripts/Camera        WebCamTexture, mirroring, aspect handling    │  M2
│ Scripts/Gesture       index-point / two-finger detection + hold    │  M3–M4
│ Scripts/Interaction   ribbon + convex quad builders                │  M3–M4
│ Scripts/Rendering     mask RT + halftone composite (URP)           │  M3–M5
│ Shaders/              halftone, mask, glitch/RGB split             │  M5
│ Scripts/UI            status, controls, start/reset flow           │  M6
│ Editor/               one-click scene/material setup + validation  │  M2+
└────────────────────────────────────────────────────────────────────┘
```

## Why this split

* **Python + MediaPipe** gives production-quality 21-point hand tracking without native Unity CV plugins.
* **Unity** owns everything visual and every interaction decision, so the tracker stays a dumb, restartable sensor.
* **UDP on localhost** has negligible latency, no connection state, and survives either side restarting.

## Key decisions

**Detect on the raw frame, mirror the output.** The installed MediaPipe Tasks API documents that Left/Right refer to the subject's own hands on an un-mirrored image, and that mirroring the input inverts them. The tracker therefore never flips the image it feeds to MediaPipe. It mirrors only the landmark x-coordinates (`--mirror`, on by default). `--swap-handedness` is a safety valve if a given MediaPipe build behaves differently. TESTING.md has a 10-second check.

**Duplicate handedness.** MediaPipe sometimes labels both hands the same. The tracker then assigns labels by screen position: in a mirrored view the user's right hand is on the screen's right. Each packet carries at most one Left and one Right.

**Newest frame wins.** The capture thread overwrites a single slot, and the tracker always processes the newest frame. When detection is slower than the camera, frames are dropped rather than queued, so latency stays bounded.

**Tracking rate is separate from render rate.** Python runs at camera rate, which is usually 30 FPS on Mac webcams. Unity renders at display rate and reads the latest packet each frame (M2).

**Configurable tracking resolution.** Frames are downscaled to `--tracking-width` (default 640 px) before inference. Landmarks are normalised, so this trades accuracy for speed without changing coordinates.

**Smoothing.** A vectorised One Euro Filter smooths all 63 coordinates per hand: strong smoothing when still, low lag when moving. It is reset whenever a hand is lost, so a re-appearing hand doesn't slide in from its old position.

**Loss and recovery.**
| Failure | Behaviour |
|---|---|
| Hand missing ≤ `--hand-hold-ms` (150 ms) | Sent with `state:"held"` and its last pose |
| Hand missing longer | Dropped; filter reset |
| No camera frames for `--camera-timeout-s` | Camera reopened with exponential backoff (0.5 → 5 s); heartbeats report `cam.status` |
| 5 consecutive MediaPipe errors | Landmarker closed and recreated on the next frame |
| Unity not running | Packets dropped silently; tracker keeps running |
| Tracker quits | Sends `bye`; Unity also times out after 1 s |

## Open question for Milestone 2: camera sharing

Both processes need the webcam: Python for tracking, Unity (`WebCamTexture`) for display. macOS generally lets several apps read the same camera at once, but this has to be verified on the target Mac in M2. If it fails, the fallback is for Python to forward JPEG frames to Unity on a second local port. Both processes should also request the same resolution, because a different aspect ratio crops differently and misaligns the overlays. Packets carry `cam.w`/`cam.h` so Unity can detect that.

## Unity project files

Unity 6 has several concurrent release lines (6.0 LTS, 6.3 LTS, …), and `ProjectSettings/` and `Packages/manifest.json` are tied to the exact editor version. Rather than guess, Milestone 2 generates them with your installed editor (see INSTALLATION.md). The `Assets/` folder structure is already in place.
