# FILTER CMR

A real-time interactive camera artwork for macOS. Raise your hands in front of the webcam and draw a blue, cyan and navy halftone filter into the live image: a **ribbon** between two index fingers, or a **four-point frame** between your thumbs and index fingers (an "L" with each hand, like framing a photo). Outside the shape, the camera image stays untouched.

The visual language is an original take on Y2K music-editorial print: halftone dots, photocopy grain, RGB split and digital glitch. It uses no third-party logos or artwork.

## Status

| Milestone | Scope | State |
|---|---|---|
| 1 | Repository structure, Python webcam hand tracking | ✅ done |
| 2 | Unity integration: UDP receiver, webcam display, live landmarks, gesture recognition, debug UI, scene setup tool | ✅ tested by you on a Mac |
| **3 + 4** | **Interactive masks** (point → ribbon, thumb + index "L" → quad) **+ halftone composite shader** (blue/cyan print, RGB split, glitch, grain, edge glow), settings panel, presentation mode | ✅ code complete and verified outside Unity: C# 64 tests, shader compiled and **executed** with 36 numeric checks, compile check against 136 Unity 2022.3.62f1 APIs. 🖐 **Needs a first run on your Mac:** [MILESTONE_3_4_TESTING.md](Documentation/MILESTONE_3_4_TESTING.md) |
| 5 | (merged into 3 + 4) | |
| 6 | UI, calibration, reset, fullscreen, performance | ⏳ |
| 7 | End-to-end testing, final docs | ⏳ |

## What it does

* **No gesture:** the webcam image, untouched.
* **Both index fingers up (point):** an organic ribbon of blue halftone flows between your index fingertips and follows them.
* **Thumb + index "L" on both hands** (like framing a photo): a halftone frame spans the four fingertips (both thumb tips and both index tips). It stretches, rotates and skews with your hands, and its dots follow the frame's perspective.
* Inside the shape the image becomes an editorial blue/cyan print: luminance-driven dots (circle, square, soft or irregular) over a tonal blue ramp, with fine grain, subtle RGB split and controlled glitch bands. The edge is soft, with a faint cyan glow. Outside, every pixel is the original webcam image.
* Switching gestures cross-fades; dropping your hands fades back to the plain webcam.

## Architecture

```
 webcam ─► Python tracker (HandTracking/) ──UDP JSON :5065──► Unity 2022.3 (UnityProject/)
           MediaPipe 21 landmarks × 2 hands                   background thread: socket + parser
           One Euro smoothing, Left/Right,                    main thread: hands, gestures,
           per-hand pose: point / frame / …                   webcam display, landmark overlay, UI
                      └──(optional --stream-video) JPEG :5066 ──► camera fallback
```

* **Python** owns tracking and per-hand classification (`pose`).
* **Unity** owns display, the two-hand gesture interpretation (both `point` → TWO FINGER POINT, both `frame` (thumb + index "L") → TWO FINGER FRAME, with hold and release timers) and all rendering.
* The masks are **signed-distance fields** evaluated in one custom uGUI shader on the existing webcam `RawImage` (Built-in render pipeline), so there are no render textures and no extra draw calls. See [ARCHITECTURE.md](Documentation/ARCHITECTURE.md#interactive-masks-and-the-halftone-composite-milestones-3-and-4).
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
3. Run **FILTER CMR → Setup Visual Effects** (adds the mask and halftone systems to your existing scene).
4. Open `Assets/FILTERCMR/Scenes/MainScene.unity` → **Play** → allow camera access for Unity.
5. Index fingers up on both hands → **ribbon**. Thumb + index "L" on both hands → **frame**. (Prefer peace signs? Set **Frame Fingers = Index And Middle** in `FilterCMRSettings`.)

Shortcuts in the Game view:
* **D** debug panel · **R** restart UDP · **H** landmarks · **C** next camera · **M** flip video
* **1** ribbon · **2** quad · **0** mask off (press again → AUTO) · **T** halftone · **G** grain · **X** glitch · **[ ]** intensity
* **V** mask debug · **S** settings panel · **P** presentation mode

Everything also works with the mouse: the top-bar buttons and the settings panel.

Step by step: [MILESTONE_3_4_SETUP.md](Documentation/MILESTONE_3_4_SETUP.md) · all visual parameters: [SHADER_PARAMETERS.md](Documentation/SHADER_PARAMETERS.md).

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
| Plain webcam only, debug panel SHADER row shows an error | Select `FILTERCMR_WebcamComposite.shader` and check its Inspector for errors, then run **FILTER CMR → Setup Visual Effects** |
| Gesture recognised but no mask | MASK mode may be OFF (press **0**); check *Opacity* / *Halftone Intensity* in the settings panel |
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
    Assets/FILTERCMR/Scripts/{Core,Networking,Tracking,Camera,Gestures,Mask,Rendering,UI}
    Assets/FILTERCMR/Shaders/         FILTERCMR_WebcamComposite.shader + includes
    Assets/FILTERCMR/Tests/Editor     EditMode tests (60)
    Assets/Editor/FILTERCMRSetup.cs   FILTER CMR menu
    Packages/  ProjectSettings/
  Tools/
    DotnetVerification/      optional: run the C# core + tests + UDP integration under .NET 8
    ShaderPreview/           optional: compile + execute the composite shader, numeric checks, previews
    UnityCompileCheck/       optional: compile all Unity scripts against verified 2022.3 API stubs
    generate_meta.py         deterministic .meta GUIDs for new scripts
  Documentation/
    INSTALLATION.md  UNITY_SETUP.md  UNITY_TESTING.md  ARCHITECTURE.md  UDP_PROTOCOL.md  TESTING.md
    MILESTONE_3_4_SETUP.md  MILESTONE_3_4_TESTING.md  SHADER_PARAMETERS.md
```

## Palette

Effect palette (default "FILTER CMR Blue"; also Cyan Print, Midnight, Ice Riso):

| Electric Blue | Bright Cyan | Light Cyan | Deep Navy | Dark Blue | White |
|---|---|---|---|---|---|
| `#155EEF` | `#35DDF2` | `#A5F3FC` | `#07152F` | `#102A56` | `#F7FBFF` |

UI accents: `#164BFF` · `#00D9FF` · `#91DFFF` · `#05070D`.
