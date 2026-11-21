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

## Interactive masks and the halftone composite (Milestones 3 and 4)

```
 HandTrackingManager ──► GestureManager ──► CurrentGesture (point / peace, debounced)
   HandView.Smoothed[21]          │
        │ IMAGE coords             ▼
        │              MaskTransitions (mode: Auto / Ribbon / Quad / Off)
        ▼                         │ targets
 InteractiveMaskManager ◄─────────┘
   × aspect → ISO space
   PointSmoother ×6 (L/R index tip, L/R middle tip, L/R index direction)
   MaskFader ×2 (ribbon, quad; cross-fade)
   RibbonMaskGenerator ──► RibbonShape  (25 centre samples + half-widths)
   QuadrilateralMaskGenerator ──► QuadShape (convex hull + homography)
        │
        ▼
 WebcamCompositeController (on the Webcam Display RawImage)
   VisualEffectController.Fill → EffectValues
   CompositeUniformWriter → Material.SetFloat / SetVector / SetVectorArray (cached IDs)
        │
        ▼
 FILTERCMR/WebcamComposite shader  (one pass; the same draw call that showed the webcam)
   q = inverse(uvRect)(uv) → mask SDFs → outside: webcam pixel unchanged
                                        inside: glitch + RGB split → halftone → grain → blend + edge glow
```

### Rendering approach

The project uses the **Built-in render pipeline**, and everything on screen is uGUI. The composite is therefore a **uGUI-compatible shader on the existing webcam `RawImage`**. It keeps Unity's UI stencil and clipping support and adds no draw call, render texture or camera. Alternatives were considered and rejected:

* **A mask RenderTexture** drawn from a mesh needs an extra camera or a `CommandBuffer` render, plus a separate blur for the feather, and still gives no edge distance for the glow.
* **URP Renderer Features** would mean switching the whole project to URP for no gain.

### Mask representation: analytic signed-distance fields

The mask is evaluated per pixel from a few uniforms, as a signed distance to the shape (negative inside):

* **Ribbon:** a chain of 24 round segments whose radius interpolates along each segment (`FilterCMR_RibbonSDF`). Pixels outside the shape's bounding box, expanded by feather, glow and edge noise, skip the loop entirely.
* **Quad:** the exact signed distance to a 4-vertex polygon (`FilterCMR_QuadSDF`, Inigo Quilez's formulation).
* **Mask value:** `1 − smoothstep(−feather/2, +feather/2, d)`, so the edge is soft, resolution-independent and anti-aliased.
* **Edge effects:** the distance also drives the inner cyan glow, the optional outline, and a noise displacement for an organic boundary.

Both masks are evaluated, weighted by their fade value, and combined with `max`. That is how a point→peace switch becomes a cross-fade.

The CPU has equivalents of both distance functions, using the same formulas (`RibbonMaskGenerator.SignedDistance`, `QuadrilateralMaskGenerator.SignedDistance`), used by the tests and the shader preview harness.

### Coordinate mapping, with no double mirroring

| Space | Definition | Used by |
|---|---|---|
| IMAGE `q` | 0–1 over the displayed image, top-left origin, y down, already mirrored by Python | landmarks, overlays |
| ISO `P` | `(q.x · aspect, q.y)`; 1 unit = image height; isotropic | all mask geometry, dot grid |
| QUAD `(s,t)` | RawImage-local 0–1, bottom-left origin | uGUI |
| UV `(u,v)` | `uvRect.xy + uvRect.wh · (s,t)` | texture sampling |

The shader inverts the RawImage `uvRect` to get `q = (s, 1 − t)` from the texture UV. That is the same rectangle-relative position the landmark overlay uses, whatever `uvRect` mirroring (tracker mirroring, the **M** flip, or a vertically flipped camera) is active. Mirroring therefore happens exactly once, in `uvRect`. The mask follows the landmarks by construction, and the texture is sampled back through `uvRect` (`UVFromImage`). This is covered by `MaskTests.UvRoundTripForAllDisplayOrientations` and by the shader harness's mirrored-display test.

### Ribbon (TWO FINGER POINT)

1. Inputs: both index tips (landmark 8) and each index finger's direction (landmark 6 → 8), in ISO space, each smoothed: exponential smoothing at 18/s plus a 6 image-heights/s speed limit that absorbs one-frame tracking jumps.
2. The centre line is a **cubic Bézier** A → B. Each tangent is a blend of the chord and the finger direction, set by *Finger Influence*, so raised index fingers arch the strip and tilting a hand bends it. *Curve Strength* scales the tangents and *Curve Direction* adds a sideways bow. Tangents are kept pointing forward, so the curve can't loop back; 2,000 random cases are fuzz-tested.
3. Width: *Ribbon Width* × a stretch factor (thinner when the hands are far apart) × a taper toward the tips × optional asymmetry × a slow "breathing" term, clamped to [*Minimum*, *Maximum*].
4. *Noise Distortion* adds a travelling wave perpendicular to the chord. It is pinned to zero at both fingertips, so the ribbon always ends exactly on the fingers.
5. The curve is sampled at 25 points, and each point (x, y, half-width) is uploaded as a `float4`.

### Quad (TWO FINGER FRAME)

1. Inputs: left index (8), left middle (12), right index (8), right middle (12), smoothed **by identity** before any ordering. Smoothing after ordering would blend unrelated corners whenever the order changes.
2. **Ordering:** the convex hull of the four points, using Andrew's monotone chain. It always produces a simple, consistently wound polygon that cannot self-intersect or invert, whatever order the tips arrive in (a quad, or a triangle when one tip lies inside the others). This is fuzz-tested on 20,000 random configurations.
3. The hull is rotated to start at the lowest source index, normally the left index tip. That ties the perspective grid's orientation to finger identity, so it doesn't spin from frame to frame.
4. A **homography** (Heckbert's square→quad mapping) and its inverse let the halftone grid follow the quad's perspective (*Perspective Dots*). Polygons under 4·10⁻⁴ image-heights² count as invalid and are hidden.

### Halftone and effects (in shader order)

1. **Glitch:** horizontal displacement of random bands. *Frequency* sets how many bands and how often; *bursts* occasionally make them stronger. Scanline wobble is added, and everything is scaled by the mask so the edge stays continuous.
2. **RGB split:** R, G and B are sampled at `+d`, `0` and `−d` along a direction. The offset slowly animates and is scaled by the mask.
3. **Halftone:**
   * A rotated grid of cells, in screen pixels, so dots stay round and keep the same size on any display. In quad mode with *Perspective Dots* on, the grid follows the quad's perspective instead.
   * Each cell samples the image once, at its centre. A dot's area is proportional to the tone (radius ∝ √coverage), like real print. The edge is anti-aliased analytically over one pixel, so dots stay crisp without pixelation.
   * Two styles: *Print* (blue ink on pale paper, dark = bigger dots) and *Screen* (light dots on navy, bright = bigger dots).
   * Colour comes from a 5-stop tone ramp: navy, dark blue, electric blue, cyan, light cyan, white. The paper shows the webcam's tones through that ramp (*Tone Mix*), so the result has depth rather than a flat overlay.
4. **Print texture and grain:** two-octave value noise for a paper-fibre texture, plus per-pixel hashed grain. Grain is re-seeded only at *Grain Fps* (24), or never (static), so it doesn't flicker.
5. **Blend:** `lerp(webcam, effect, mask × opacity)`, plus the cyan glow and the optional outline.

All effect maths runs in **gamma space**, converting in and out when the project uses Linear colour, so the look matches in both colour spaces. Colours are uploaded with `SetVector` as gamma values. Outside the mask the shader returns the sampled webcam pixel directly, with no conversion. The harness verifies this as pixel-identical (max difference 0) in Gamma and Linear.

### Performance

* **CPU, per frame:** about 150 multiply-adds for the ribbon, a 4-point hull and a 3×3 inverse, plus about 70 cached-ID material property sets. No allocations, no GameObjects created, no texture readbacks.
* **GPU, outside the masks' bounding boxes:** two texture samples, the mapping maths, and an early return.
* **GPU, inside:** up to 24 segment distances, and 2–6 texture samples (pixel and cell centre, ×3 with RGB split). When the two masks overlap during a cross-fade, the halftone is computed for both grids.
* **Derivatives:** grid coordinates and their derivatives are computed before any divergent branch, so `fwidth` is well defined. Samples inside branches use `tex2Dlod`.
* **UI:** the debug and settings panels update text at 10 Hz. The landmark and mask-debug meshes rebuild only while something is visible.

### Known limitations

* The composite has been compiled (glslang, all keyword variants) and executed on a software GPU, but **not yet in the Unity Editor**. Unity's own compiler (Metal on macOS) may still report something the harness can't see. The first open on your Mac is the real check: select the shader and look at its Inspector.
* When a tip moves inside the other three, the frame becomes a **triangle**. That's correct and continuous, but the perspective dot grid then switches to the flat screen grid. The grid also changes orientation if the left index tip leaves the hull.
* Forced **Ribbon/Quad** modes (keys 1 and 2) need both hands visible; they ignore the pose.
* Inspector changes made in Play Mode revert when Play Mode ends, as with any Unity component.
* A webcam that reports a non-zero `videoRotationAngle` (not the case on Macs) rotates the image but not the mask space.

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
        UI/          DebugPanel, TrackingStatusUI, VisualSettingsPanel, UIFactory
        Mask/        InteractiveMaskManager, RibbonMaskGenerator, QuadrilateralMaskGenerator,
                     MaskGeometry, MaskCoordinateMapper, MaskSmoothing, MaskTransitions, MaskDebugVisualizer
        Rendering/   VisualEffectController, WebcamCompositeController, CompositeUniformWriter, EffectValues
      Shaders/       FILTERCMR_WebcamComposite.shader + FILTERCMR_Mask / _Halftone / _Common .cginc
      Materials/     FILTERCMR_WebcamComposite.mat      (created by Setup Visual Effects)
      Tests/Editor/  NetworkingTests, GestureTests, MaskTests, RenderingTests    FilterCMR.Tests.asmdef
      Scenes/        MainScene.unity            (created by the setup tool)
      Settings/      FilterCMRSettings.asset    (created by the setup tool)
      Materials/ Prefabs/ Textures/             (Milestone 3+)
    Editor/          FILTERCMRSetup.cs                       FilterCMR.Editor.asmdef
  Packages/manifest.json             uGUI + Test Framework + built-in modules
  ProjectSettings/ProjectVersion.txt 2022.3.62f1 (the rest is generated by Unity on first open)
```

C# namespaces: `FilterCMR` (manager), `FilterCMR.Core`, `.Networking`, `.Tracking`, `.CameraInput`, `.Gestures`, `.Mask`, `.Rendering`, `.UI`, `.EditorTools`. The camera namespace is `CameraInput`, not `Camera`, so that it doesn't hide `UnityEngine.Camera`.
