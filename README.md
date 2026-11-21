# FILTER CMR

A real-time interactive camera artwork for macOS. Raise your hands in front of the webcam and draw a blue, cyan and navy halftone filter into the live image: a **ribbon** between two index fingers, or a **four-point frame** between two peace signs. Outside the shape, the camera image stays untouched.

The visual language is an original take on Y2K music-editorial print: halftone dots, photocopy grain, RGB split and digital glitch. It uses no third-party logos or artwork.

**Stack:** Unity 6 (URP, HLSL) for rendering and interaction · Python 3 + MediaPipe Hand Landmarker + OpenCV for tracking · UDP between them.

## Status

| Milestone | Scope | State |
|---|---|---|
| **1** | Repository structure, webcam hand tracking (Python) | ✅ code complete, 67 automated tests pass. Real-camera checks M1-1…M1-16 need a Mac with a webcam |
| 2 | Python → Unity UDP receiver, live landmark display | ⏳ next |
| 3 | TWO FINGER POINT, ribbon mask | ⏳ |
| 4 | TWO FINGER FRAME, convex quad mask | ⏳ |
| 5 | Halftone shader and blue visual effects | ⏳ |
| 6 | UI, calibration, reset, fullscreen, performance | ⏳ |
| 7 | End-to-end testing, final docs | ⏳ |

The Unity side does not exist yet. `UnityProject/Assets/` holds only the planned folder layout.

## Quick start (Milestone 1)

```bash
cd HandTracking
./setup.sh                  # venv, dependencies, model, tests, checks
./run.sh                    # tracker with debug preview
```

In a second terminal, see the UDP stream Unity will consume:

```bash
cd HandTracking && source .venv/bin/activate && python tools/udp_monitor.py
```

Full instructions, including the camera-permission fix, are in [Documentation/INSTALLATION.md](Documentation/INSTALLATION.md).

## Repository layout

```
filtercmr/
  HandTracking/            Python tracking service (Milestone 1)
    main.py  hand_detector.py  udp_sender.py  camera.py  one_euro.py
    finger_state.py  debug_view.py  config.py
    tools/  udp_monitor.py  check_setup.py
    tests/  pytest suite
    setup.sh  run.sh  requirements.txt  requirements-dev.txt
  UnityProject/            Unity 6 project (Milestone 2+)
    Assets/{Scenes,Scripts/{Camera,HandTracking,Gesture,Interaction,Rendering,UI},
            Shaders,Materials,Prefabs,Textures,Editor}
  Documentation/
    INSTALLATION.md  ARCHITECTURE.md  UDP_PROTOCOL.md  TESTING.md
```

## Documentation

* [INSTALLATION.md](Documentation/INSTALLATION.md): setup and running
* [ARCHITECTURE.md](Documentation/ARCHITECTURE.md): components, design decisions, failure handling
* [UDP_PROTOCOL.md](Documentation/UDP_PROTOCOL.md): packet schema and coordinate conventions
* [TESTING.md](Documentation/TESTING.md): automated tests and manual test steps

## Palette

| Electric Blue | Cyan | Light Blue | Navy | Black |
|---|---|---|---|---|
| `#164BFF` | `#00D9FF` | `#91DFFF` | `#07152F` | `#05070D` |
