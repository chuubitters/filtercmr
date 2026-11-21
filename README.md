# FILTER CMR

A real-time interactive camera artwork for macOS. Raise your hands in front of the webcam and draw a blue, cyan and navy halftone filter into the live image: a **ribbon** between two index fingers, or a **four-point frame** between two peace signs. Outside the shape, the camera image stays untouched.

The visual language is an original take on Y2K music-editorial print: halftone dots, photocopy grain, RGB split and digital glitch. It uses no third-party logos or artwork.

## Status

| Milestone | Scope | State |
|---|---|---|
| 1 | Repository structure, Python webcam hand tracking | ✅ done |
| **2** | **Unity integration:** UDP receiver, webcam display, live landmarks, gesture recognition, debug UI, scene setup tool | ✅ code complete and verified outside Unity (Python 74 tests, C# 35 tests incl. end-to-end UDP, compile check against Unity 2022.3.62f1 APIs). 🖐 **Needs a first run on your Mac:** [UNITY_TESTING.md](Documentation/UNITY_TESTING.md) |
| 3 | TWO FINGER POINT ribbon mask + halftone | ⏳ next |
| 4 | TWO FINGER FRAME quad mask | ⏳ |
| 5 | Full halftone shader and blue visual effects | ⏳ |
| 6 | UI, calibration, reset, fullscreen, performance | ⏳ |
| 7 | End-to-end testing, final docs | ⏳ |

## Architecture

```
 webcam ─► Python tracker (HandTracking/) ──UDP JSON :5065──► Unity 2022.3 (UnityProject/)
           MediaPipe 21 landmarks × 2 hands                   background thread: socket + parser
           One Euro smoothing, Left/Right,                    main thread: hands, gestures,
           per-hand pose: point / peace / …                   webcam display, landmark overlay, UI
                      └──(optional --stream-video) JPEG :5066 ──► camera fallback
```

* **Python** owns tracking and per-hand classification (`pose`).
* **Unity** owns display, the two-hand gesture interpretation (both `point` → TWO FINGER POINT, both `peace` → TWO FINGER FRAME, with hold and release timers) and all rendering.
* Unity shows the camera with its own `WebCamTexture`. If your Mac won't let both apps use the camera, the tracker can forward its frames instead (`--stream-video`).

Details: [ARCHITECTURE.md](Documentation/ARCHITECTURE.md) · [UDP_PROTOCOL.md](Documentation/UDP_PROTOCOL.md).

## Requirements

| | |
|---|---|
| macOS | 12 or newer, Apple Silicon recommended |
| Unity | **2022.3.62f1 LTS** (Unity Hub). Built-in render pipeline; packages: uGUI, Test Framework |
| Python | 3.9+ (Intel Macs: 3.11 or 3.12), installed by `setup.sh` into `HandTracking/.venv` |
| Webcam | built-in or USB |

## Quick start

```bash
git clone https://github.com/chuubitters/filtercmr.git
cd filtercmr/HandTracking
./setup.sh                  # once: venv, dependencies, MediaPipe model, tests
./run.sh                    # start the tracker (preview window; Q quits)
```

1. **Unity Hub → Add → Add project from disk → `filtercmr/UnityProject`** (opens with 2022.3.62f1).
2. On first open, accept **Create Main Scene**, or use the menu **FILTER CMR → Setup Main Scene**.
3. Open `Assets/FILTERCMR/Scenes/MainScene.unity` → **Play** → allow camera access for Unity.
4. Raise both hands: two skeletons appear. Index fingers up → **TWO FINGER POINT**. Peace signs → **TWO FINGER FRAME**.

Shortcuts in the Game view: **D** debug panel · **R** restart UDP · **H** landmarks · **C** next camera · **M** flip video.

Step by step: [UNITY_SETUP.md](Documentation/UNITY_SETUP.md). Python only: [INSTALLATION.md](Documentation/INSTALLATION.md).

**No camera handy?** `python tools/fake_tracker.py --stream-video` sends animated synthetic hands and video to Unity.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Unity says **START THE TRACKER** | Run `cd HandTracking && ./run.sh`. Both sides use port 5065 by default. |
| **UDP ERROR: port 5065 already in use** | Close `tools/udp_monitor.py` (or another Unity instance), then press **R**. |
| Unity's webcam shows **ERROR … sent no frames**, or the tracker preview freezes | Your Mac won't share the camera. Set **Camera Source = Python Stream** in `Assets/FILTERCMR/Settings/FilterCMRSettings.asset` and run `./run.sh --stream-video`. |
| Camera permission denied | System Settings → Privacy & Security → Camera → enable **Unity** (and your terminal app for the tracker), then restart them. |
| Landmarks are mirror images of your hands | Press **M**. If that fixes it, enable **Flip Display X** in the settings asset. |
| Landmarks are offset | The debug panel shows an aspect mismatch. Use the same resolution in Unity's settings and the tracker, or use Python Stream mode. |
| Shortcuts don't work | Click the Game view. If the Console mentions the Input Manager: Player Settings → Active Input Handling → *Input Manager (Old)* or *Both*. |

More: [UNITY_SETUP.md § Troubleshooting](Documentation/UNITY_SETUP.md#troubleshooting).

## Repository layout

```
filtercmr/
  HandTracking/              Python tracker
    main.py  hand_detector.py  udp_sender.py  video_stream.py  camera.py
    one_euro.py  finger_state.py  debug_view.py  config.py  synthetic.py
    tools/  udp_monitor.py  check_setup.py  fake_tracker.py
    tests/  pytest suite (74)
  UnityProject/              Unity 2022.3.62f1 project
    Assets/FILTERCMR/Scripts/{Core,Networking,Tracking,Camera,Gestures,UI}
    Assets/FILTERCMR/Tests/Editor     EditMode tests (31)
    Assets/Editor/FILTERCMRSetup.cs   FILTER CMR menu
    Packages/  ProjectSettings/
  Tools/
    DotnetVerification/      optional: run the C# core + tests + UDP integration under .NET 8
    generate_meta.py         deterministic .meta GUIDs for new scripts
  Documentation/
    INSTALLATION.md  UNITY_SETUP.md  UNITY_TESTING.md  ARCHITECTURE.md  UDP_PROTOCOL.md  TESTING.md
```

## Palette

| Electric Blue | Cyan | Light Blue | Navy | Black |
|---|---|---|---|---|
| `#164BFF` | `#00D9FF` | `#91DFFF` | `#07152F` | `#05070D` |
