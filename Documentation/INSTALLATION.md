# Installation (macOS)

## Requirements

| | Version | Notes |
|---|---|---|
| macOS | 12+ | Apple Silicon recommended |
| Python | 3.9–3.12 (Intel) / 3.9+ (Apple Silicon) | `brew install python@3.12` |
| Unity | any Unity 6 release with URP (6000.0 LTS or newer) | needed from Milestone 2 |
| Webcam | built-in or USB | |
| Git LFS | optional | `brew install git-lfs && git lfs install` (for future binary assets) |

**Intel Macs:** the newest MediaPipe with Intel macOS wheels is 0.10.21, which supports Python 3.9–3.12. `requirements.txt` accepts it, so use Python 3.11 or 3.12 to create the virtual environment.

## 1. Hand-tracking service

```bash
git clone https://github.com/chuubitters/filtercmr.git
cd filtercmr/HandTracking
./setup.sh        # venv + dependencies + model download + tests + checks
```

`setup.sh` creates `HandTracking/.venv` (ignored by git), installs `requirements-dev.txt`, downloads `models/hand_landmarker.task` (~7.5 MB, also ignored by git), runs the unit tests, and runs `tools/check_setup.py --skip-camera`.

Manual equivalent:

```bash
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt
python tools/check_setup.py          # includes the webcam check
```

If the model download fails (proxy or firewall), download it in a browser and save it as `HandTracking/models/hand_landmarker.task`:
<https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task>

### Camera permission

The first time the tracker opens the camera, macOS asks whether your **terminal app** (Terminal, iTerm, VS Code…) may use it. If you denied it, or see `cannot open camera 0`:
System Settings → Privacy & Security → Camera → enable your terminal app → **quit and reopen the terminal**.

## 2. Run

```bash
cd HandTracking
./run.sh                      # debug preview window
./run.sh --no-preview         # headless
./run.sh --camera-index 1     # external webcam
./run.sh --help               # every option
```

Preview keys: **Q** quit · **R** reset tracking · **M** toggle mirror · **S** toggle smoothing · **H** swap Left/Right.

In a second terminal, see exactly what Unity will receive:

```bash
cd HandTracking && source .venv/bin/activate
python tools/udp_monitor.py
```

## 3. Unity project (Milestone 2, not yet available)

Milestone 2 adds the Unity scripts plus an Editor setup menu. The plan is:
1. Open Unity Hub → **Add → Add project from disk** → choose `filtercmr/UnityProject`, and pick your installed Unity 6 editor.
2. Unity generates `ProjectSettings/` and `Packages/` for your exact version; the URP package and scene are configured by the setup menu.

These steps will be finalised and tested in Milestone 2.
