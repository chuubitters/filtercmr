# Architecture

FILTER CMR is two cooperating processes on the same Mac. Python tracks hands; Unity (2022.3.62f1 LTS) displays the camera, interprets gestures and draws everything.

## Data flow

```
 ┌──────────────────────────── HandTracking/ (Python 3) ─────────────────────────────┐
 │                                                                                   │
 │  webcam ──► camera.py ──► hand_detector.py ──────────► finger_state.py            │
 │  (AVFoundation)  newest     MediaPipe HandLandmarker     per-hand pose:           │
 │                  frame      mirror x · Left/Right        point/peace/open/fist/   │
 │                  wins       One Euro · hold/drop         other  (+ ext flags)     │
 │                    │                    │                                         │
 │                    │                    ▼                                         │
 │                    │            udp_sender.py ──── JSON ── UDP 127.0.0.1:5065 ─────┼──┐
 │                    │            frame / heartbeat / bye                            │  │
 │                    ▼                                                               │  │
 │            video_stream.py  (only with ./run.sh --stream-video)                    │  │
 │            JPEG → ≤8 kB chunks ─────────────────────── UDP 127.0.0.1:5066 ─────────┼──┼──┐
 └───────────────────────────────────────────────────────────────────────────────────┘  │  │
                                                                                        │  │
 ┌──────────────────────────── UnityProject/ (Unity 2022.3) ─────────────────────────┐  │  │
 │                                                                                   │  │  │
 │  background thread                         main thread                            │  │  │
 │  ┌────────────────────┐  latest-wins      ┌──────────────────────┐                │  │  │
 │  │ UdpReceiverCore    │◄──────────────────┼──────────────────────┼────────────────┼──┘  │
 │  │  Socket.Receive    │  copy under lock  │ UDPReceiver          │                │     │
 │  │  HandDataParser    ├──────────────────►│  (MonoBehaviour)     │                │     │
 │  │  (zero-alloc JSON) │                   └──────────┬───────────┘                │     │
 │  └────────────────────┘                              ▼                            │     │
 │                                           HandTrackingManager                     │     │
 │                                            connection: waiting/connected/lost/    │     │
 │                                            stopped · HandView ×2 (display-smoothed)│    │
 │                                                      │                            │     │
 │                          ┌───────────────────────────┼──────────────────┐         │     │
 │                          ▼                           ▼                  ▼         │     │
 │                  GestureManager            HandConnectionVisualizer   DebugPanel  │     │
 │                  GestureStateMachine       HandLandmarkVisualizer     TrackingStatusUI │
 │                  (combines Python poses;   (procedural UI meshes,                 │     │
 │                   confirm/release timers)   one per overlay)                      │     │
 │                                                      ▲                            │     │
 │  WebcamController ── WebCamTexture ─┐                │ child, stretched           │     │
 │  VideoStreamReceiver ◄──────────────┼── Texture ─► WebcamDisplay (RawImage +      │     │
 │   (VideoReceiverCore thread ◄───────┼──────────────  AspectRatioFitter, uvRect    │◄────┘
 │    + VideoFrameAssembler)           │                mirror) ── Hand Overlay      │
 │                                     └─ chosen by Camera Source: Auto/Unity/Python │
 └───────────────────────────────────────────────────────────────────────────────────┘
```

## Responsibilities

| Concern | Owner | Where |
|---|---|---|
| Webcam capture for tracking, hand landmarks, smoothing, per-hand pose | Python | `HandTracking/` |
| Transport | UDP JSON, port 5065 | `udp_sender.py` → `Networking/UdpReceiverCore.cs` |
| Socket + JSON parsing off the main thread | Unity background thread | `UdpReceiverCore`, `HandDataParser` |
| Connection state, timeouts, hand presence | Unity main thread | `Tracking/HandTrackingManager.cs` |
| Two-hand gesture (point / frame) | Unity, **from Python's per-hand poses** | `Gestures/GestureStateMachine.cs` |
| Camera image on screen | Unity | `Camera/WebcamController.cs`, `Camera/WebcamDisplay.cs` |
| Landmark overlay | Unity | `Tracking/HandLandmarkVisualizer.cs`, `HandConnectionVisualizer.cs` |
| Debug / status UI, shortcuts | Unity | `UI/`, `Core/FilterCMRManager.cs` |
| Scene creation | Unity Editor | `Assets/Editor/FILTERCMRSetup.cs` |

### No second gesture classifier

Finger geometry is classified once, in Python (`finger_state.classify_pose` → `pose`). Unity's `GestureStateMachine` only combines the two hands' poses:

| Left pose | Right pose | Gesture |
|---|---|---|
| `point` | `point` | **TWO FINGER POINT** |
| `peace` | `peace` | **TWO FINGER FRAME** |
| anything else / a hand missing | | none |

It then debounces the result over time. The gesture activates after both hands hold the poses for `gestureConfirmSeconds` (0.2 s). It survives dropouts shorter than `gestureReleaseSeconds` (0.25 s). Hands in `held` state (briefly lost by MediaPipe) can keep a gesture alive but cannot start one.

## Threading and allocation

* **Receive thread** (one per socket): it blocks on `Socket.Receive` with a 250 ms timeout so `Stop()` is always noticed. It parses into a private, pre-allocated `TrackingMessage`, then copies it into a pending slot under a lock. Older unread messages are overwritten, so the newest always wins.
* **Main thread**: `TryGetLatest` copies the pending slot out under the same lock. Both copies are fixed-size arrays, so nothing is allocated per packet and the main thread never parses JSON or touches a socket.
* **Cleanup**: `OnDisable`, `OnDestroy` and `OnApplicationQuit` all call `Stop()`, which closes the socket, joins the thread (1 s timeout) and frees the port. Leaving Play Mode triggers `OnDisable`, so nothing outlives Play Mode. The integration test verifies the port can be re-bound right after `Stop()`.
* **Sequence numbers**: duplicates and reordered packets are dropped. A jump back of more than 1000, a `bye`, or more than 1 s of silence resets the sequence so a restarted tracker is accepted immediately.
* **Rendering**: each overlay is a single `MaskableGraphic` that rebuilds one mesh per frame while hands are visible. Each hand uses fewer than 1,000 vertices, and no GameObjects are created or destroyed at runtime.
* **UI text**: refreshed at 10 Hz. That is the only per-frame-ish allocation in the steady state.

## Coordinates and alignment

Python sends normalised image coordinates (0–1, origin top-left, y down). When `cam.mirrored` is true they are already mirrored.

1. Both video sources (Unity `WebCamTexture` and the Python stream) deliver the **raw, un-mirrored** camera image.
2. `WebcamDisplay` mirrors the image via `RawImage.uvRect = (1,0,-1,1)` **exactly when the tracker mirrors**, so image and coordinates use the same convention. The **M** key adds an extra flip if a particular camera driver already mirrors.
3. An `AspectRatioFitter` keeps the image at the texture's aspect ratio, letterboxed by default (`fillScreen` crops instead). The image is never stretched.
4. The landmark overlay is a child RectTransform stretched over the image, so `(x, y)` maps to `(xMin + x·w, yMax − y·h)` at any window size.
5. Before any video arrives, the overlay takes the tracker camera's aspect ratio from `cam.w/h`, so landmarks are still placed correctly.
6. If the video and the tracker camera have different aspect ratios (more than 2 %), the debug panel shows a warning, because the two would crop differently.

## Camera access: Unity and Python at the same time

Both processes need the webcam. macOS often lets several apps read one camera, but that couldn't be verified from the development machine, so the code doesn't assume it. There are three modes (`ApplicationSettings.cameraSource`):

| Mode | Behaviour |
|---|---|
| **Auto** (default) | Uses Unity `WebCamTexture`. If it delivers no frames (start timeout, error, stall) and Python frames are arriving on 5066, it shows those instead, and stops retrying the webcam so it doesn't compete with the tracker for the camera. |
| **UnityWebcam** | Only `WebCamTexture`. The video receiver is never started. |
| **PythonStream** | Unity never opens the camera. It only shows frames from `./run.sh --stream-video`. This alignment is exact by construction, because the frames are the ones that were tracked. |

**Frame forwarding protocol (port 5066)** is additive and opt-in. The tracking messages on 5065 are unchanged. Each JPEG frame (640 px wide, quality 70 by default) is split into datagrams of a 20-byte header plus ≤ 8000 bytes of payload. That stays under macOS's default 9216-byte UDP datagram limit. A frame with any missing chunk is discarded. Details are in [UDP_PROTOCOL.md](UDP_PROTOCOL.md#video-forwarding-port-5066).

How to find out which mode your Mac needs: [UNITY_TESTING.md](UNITY_TESTING.md#camera-sharing-check).

## Failure handling

| Failure | Behaviour |
|---|---|
| Tracker not started | Status "START THE TRACKER", connection `Waiting` |
| Tracker quits cleanly | `bye` → `Stopped` immediately; hands cleared |
| Tracker crashes / packets stop | After `trackerTimeoutSeconds` (1 s) → `Lost`; hands cleared |
| Tracker restarts | First packet reconnects (sequence reset rules above) |
| Malformed / truncated / unknown-version JSON | Dropped and counted ("bad" in the debug panel); the reason is kept |
| Port 5065 already in use (e.g. `udp_monitor.py`) | Receiver state `Error` with an explanation; **R** retries |
| A hand missing | That hand's overlay disappears; a gesture survives ≤ 0.25 s gaps |
| Webcam permission denied / none / busy / stalled / unplugged | Status message, automatic retry every `cameraRetrySeconds`; restart when stalled |
| Window loses focus | `Application.runInBackground = true`, so it keeps running |

## Rendering pipeline

Milestone 2 renders with uGUI on the Built-in Render Pipeline and doesn't need URP. The halftone shader and masks arrive in Milestone 3. The render pipeline (URP or Built-in) will be decided there, and nothing in M2 depends on it.

## Project layout

```
UnityProject/
  Assets/
    FILTERCMR/
      Scripts/                       FilterCMR.Runtime.asmdef
        Core/        FilterCMRManager, ApplicationSettings, Palette
        Networking/  UDPReceiver, UdpReceiverCore, HandDataParser, HandTrackingData,
                     VideoStreamReceiver, VideoStreamCore
        Tracking/    HandTrackingManager, HandView, HandSkeleton, HandGraphicBase,
                     HandLandmarkVisualizer, HandConnectionVisualizer
        Camera/      WebcamController, WebcamDisplay
        Gestures/    GestureManager, GestureStateMachine, GestureType
        UI/          DebugPanel, TrackingStatusUI, UIFactory
      Tests/Editor/  NetworkingTests, GestureTests          FilterCMR.Tests.asmdef
      Scenes/        MainScene.unity            (created by the setup tool)
      Settings/      FilterCMRSettings.asset    (created by the setup tool)
      Materials/ Prefabs/ Textures/             (Milestone 3+)
    Editor/          FILTERCMRSetup.cs                       FilterCMR.Editor.asmdef
  Packages/manifest.json             uGUI + Test Framework + built-in modules
  ProjectSettings/ProjectVersion.txt 2022.3.62f1 (the rest is generated by Unity on first open)
```

C# namespaces: `FilterCMR` (manager), `FilterCMR.Core`, `.Networking`, `.Tracking`, `.CameraInput`, `.Gestures`, `.UI`, `.EditorTools`. The camera namespace is `CameraInput`, not `Camera`, so that it doesn't hide `UnityEngine.Camera`.
