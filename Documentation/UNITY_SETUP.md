# Unity Setup (macOS)

**Editor:** Unity **2022.3.62f1** LTS · **Render pipeline:** Built-in (uGUI only in Milestone 2) · **Packages:** uGUI, Test Framework. No third-party packages.

## 1. Install Unity

1. Install **Unity Hub** from unity.com/download.
2. Hub → **Installs → Install Editor → Archive** (or "download archive") → **2022.3.62f1**.
   Add the **Mac Build Support (Mono)** module if you want to build a `.app` later. It isn't needed to run in the Editor.

## 2. Get the project and set up the tracker

```bash
git clone https://github.com/chuubitters/filtercmr.git     # or: git pull, in an existing clone
cd filtercmr/HandTracking
./setup.sh        # Python venv, dependencies, MediaPipe model, tests (see INSTALLATION.md)
```

## 3. Open the Unity project

1. Unity Hub → **Projects → Add → Add project from disk** → select **`filtercmr/UnityProject`**.
2. Hub reads `ProjectSettings/ProjectVersion.txt` and opens it with **2022.3.62f1**.
3. The first import takes a few minutes. Unity creates `Library/` (ignored by git) and fills in the rest of `ProjectSettings/`.
4. When import finishes, a dialog asks: **"MainScene does not exist yet. Create it now?"** → **Create Main Scene**.
   If you dismissed it, use the menu **FILTER CMR → Setup Main Scene** at any time.
5. The Console should show
   `[FILTER CMR] Setup complete (Assets/FILTERCMR/Scenes/MainScene.unity) … Validation problems: 0`.

The setup tool can be run again whenever you like. It never duplicates objects, and it repairs missing components or references. **FILTER CMR → Validate Scene** only checks.

### What the setup tool creates

```
MainScene
├─ Main Camera                     solid black background
├─ FILTER CMR                      FilterCMRManager (shortcuts, references)
│  └─ Systems
│     ├─ UDP Receiver              UDPReceiver           :5065
│     ├─ Video Stream Receiver     VideoStreamReceiver   :5066 (fallback)
│     ├─ Webcam                    WebcamController
│     ├─ Hand Tracking             HandTrackingManager
│     └─ Gestures                  GestureManager
├─ EventSystem
├─ Display Canvas                  (sort order 0)
│  ├─ Background
│  └─ Webcam Area
│     └─ Webcam Display            RawImage + AspectRatioFitter + WebcamDisplay
│        └─ Hand Overlay
│           ├─ Hand Connections    HandConnectionVisualizer
│           └─ Hand Landmarks      HandLandmarkVisualizer
└─ UI Canvas                       (sort order 10, separate from the video)
   ├─ Status UI                    TrackingStatusUI
   └─ Debug Panel                  DebugPanel
```

Plus `Assets/FILTERCMR/Settings/FilterCMRSettings.asset`, which holds all tunables. The tool also adds the scene to Build Settings and sets Player Settings: **Run In Background** on, a macOS **Camera Usage Description**, and Product Name "FILTER CMR".

### Commit the generated files

After the first open, commit what Unity and the setup tool generated so other machines get the same scene:

```bash
cd filtercmr
git add UnityProject/ProjectSettings UnityProject/Packages/packages-lock.json \
        UnityProject/Assets/FILTERCMR/Scenes UnityProject/Assets/FILTERCMR/Settings
git status    # Library/, Temp/, Logs/, UserSettings/ must NOT appear (they are ignored)
git commit -m "chore: add generated Unity project settings and MainScene"
```

Script `.meta` files are already in the repo with fixed GUIDs, so the scene's script references stay valid on every clone.

## 4. Run

1. **Terminal:** `cd filtercmr/HandTracking && ./run.sh`
   The tracker's preview window opens. Leave it running; press Q there to quit.
2. **Unity:** open `Assets/FILTERCMR/Scenes/MainScene.unity` (or **FILTER CMR → Open Main Scene**) → **Play**.
3. The first time, macOS asks whether **Unity** may use the camera → **Allow**.
4. Game view: choose a **16:9** aspect (or Free Aspect). The image is letterboxed, never stretched.

### Keyboard shortcuts (Game view must have focus)

| Key | Action |
|---|---|
| **D** | Toggle the debug panel |
| **R** | Restart the UDP receiver(s) and clear tracking/gesture state |
| **H** | Toggle landmark visibility |
| **C** | Cycle to the next camera (Unity webcam mode) |
| **M** | Flip the video horizontally (alignment aid, see Troubleshooting) |

## 5. Camera source

Select `Assets/FILTERCMR/Settings/FilterCMRSettings.asset` → **Camera → Camera Source**:

| Mode | Use when |
|---|---|
| **Auto** (default) | Normal use. Unity opens the webcam. If it gets no frames, it uses the Python stream when that is running. |
| **Unity Webcam** | You confirmed both apps can share the camera ([camera sharing check](UNITY_TESTING.md#camera-sharing-check)). |
| **Python Stream** | Your Mac does **not** let both apps use the camera. Start the tracker with `./run.sh --stream-video`; Unity then never opens the camera. |

Other useful settings:
* **Preferred Camera Name**: part of a device name, e.g. `FaceTime` or `USB`.
* **Requested Width/Height**: keep these equal to the tracker's capture size (1280×720 by default).
* **Fill Screen**: crop to fill the screen instead of letterboxing.
* **Landmarks**: colours, radii, line width, display smoothing.
* **Gestures**: confirm and release times.
* **Show Debug Panel**: initial visibility.

## 6. Unit tests in Unity

**Window → General → Test Runner → EditMode → Run All.** This runs 31 tests covering the protocol parser, the video chunk assembler and the gesture state machine. The fixtures are packets produced by the real Python encoder.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Hint says **START THE TRACKER** | Python isn't running or is sending elsewhere. Run `./run.sh`; the port must be 5065 in both. |
| Debug panel **UDP ERROR: port 5065 is already in use** | Another listener has the port, usually `tools/udp_monitor.py` or a second Unity instance or build. Close it, then press **R**. |
| **TRACKER DISCONNECTED** | The tracker stopped or crashed. Restart `./run.sh`; Unity reconnects automatically. |
| WEBCAM **PERMISSIONDENIED** | System Settings → Privacy & Security → Camera → enable **Unity**, then restart Unity. |
| WEBCAM **ERROR … sent no frames** | The camera is busy, probably held by the tracker. Use **Python Stream** mode and `./run.sh --stream-video`. |
| Python preview freezes or logs `no frames … reopening camera` after Unity starts | Unity took the camera from Python. Same fix: **Python Stream** mode. |
| Landmarks are mirror images of your hands on the video | The camera driver mirrors already. Press **M** to test; to make it permanent, enable **Flip Display X** in the settings asset. |
| Landmarks are offset or scaled relative to the hands | The aspect ratio differs. The debug panel shows "Aspect mismatch". Set the same resolution in the settings asset and on the tracker (`--capture-width/--capture-height`), or use Python Stream mode, which always aligns. |
| Keyboard shortcuts do nothing | Click the Game view first. If the Console says shortcuts need the legacy Input Manager: Project Settings → Player → **Active Input Handling** = *Input Manager (Old)* or *Both*. |
| Wrong camera | Press **C**, or set **Preferred Camera Name**. For the tracker: `./run.sh --camera-index 1`. |
| Pink or missing text | The built-in font failed to load. Run **FILTER CMR → Validate Scene** and report the Console output. |
| Low FPS | Check `RENDER FPS` in the debug panel. Try a smaller Game view and `./run.sh --tracking-width 480`. Close the tracker preview with `./run.sh --no-preview`. |
