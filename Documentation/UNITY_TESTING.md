# Unity Testing — Milestone 2

Key: ✅ **automated and passing** (run during development) · 🖐 **needs your Mac** (Unity Editor, webcam, hands) · ☐ tick when done.

None of the 🖐 tests have been run yet. The development machine has no Mac, no Unity Editor and no webcam. Treat them as **not yet verified** until you tick them.

## A. Automated

| Suite | How to run | Result during development |
|---|---|---|
| Python tracker (74 tests) | `cd HandTracking && source .venv/bin/activate && python -m pytest -q tests` | ✅ 74 passed |
| Unity EditMode tests (31 tests: parser, video assembler, gestures) | Unity → Window → General → Test Runner → EditMode → **Run All** | ✅ 31 passed under .NET 8 via `Tools/DotnetVerification` (same sources). 🖐 Not yet run inside Unity |
| End-to-end UDP: real Python → C# receivers (4 tests) | `cd Tools/DotnetVerification && FILTERCMR_PYTHON=../../HandTracking/.venv/bin/python dotnet run` | ✅ 35 passed (31 + 4) |
| Compile check of every Unity script | (dev only) compiled against UnityEngine/UnityEditor/uGUI signatures checked one by one in Unity's C# reference source at tag `2022.3.62f1` (103 APIs) and in uGUI 2018.4 + 7.0 | ✅ 0 errors, 0 warnings. 🖐 Real compile happens when you open the project |

The end-to-end tests check:
* ~30 packets/s from `tools/fake_tracker.py` are received and parsed with 0 malformed.
* `point`, `peace` and `open` poses arrive; both hands appear, and the right hand drops out on schedule.
* `bye` arrives on exit.
* Video frames reassemble into valid 640×360 JPEGs.
* The real `main.py` with no camera sends heartbeats reporting the camera error, then `bye` on Ctrl+C.
* The receiver can restart five times in a row. A second listener on the same port is refused with a clear message. The port is free again after `Stop()`.
* Stale and duplicate packets are dropped; tracker restarts are accepted.

## B. Testing Unity without a webcam

`tools/fake_tracker.py` sends realistic packets through the real encoder. Use it to check the whole Unity side before you involve the camera:

```bash
cd HandTracking && source .venv/bin/activate
python tools/fake_tracker.py --stream-video        # both hands, pose cycles point → peace → open every 4 s
# Unity: Camera Source = Python Stream (or Auto), press Play
```

Expected: a navy grid with white rings. The cyan (left) and blue (right) landmark overlays sit **exactly on the rings**. The gesture label shows TWO FINGER POINT, then TWO FINGER FRAME, then none. The cyan "RAW TOP-LEFT" marker appears at the **top-right**, which shows the raw frame is displayed mirrored. Add `--drop-every 4` to make the right hand disappear for 1 s every 4 s.

## Camera sharing check

Do this once; it decides which Camera Source your Mac needs.

1. Terminal: `./run.sh`. The tracker preview shows your camera.
2. Unity: Camera Source **Auto**, press **Play**.
3. Look at both:
   * Debug panel `WEBCAM` = **RUNNING**, `VIDEO SOURCE` = **AUTO → UNITY WEBCAM**, **and** the Python preview still updates → ☐ **sharing works**. Keep Auto, or set Unity Webcam.
   * `WEBCAM` = **ERROR … sent no frames**, **or** the Python preview freezes or logs `no frames … reopening camera` → ☐ **sharing does not work**. Stop both. Set Camera Source = **Python Stream**, run `./run.sh --stream-video`, and press Play again.
4. Try it in the other order too (Unity Play first, then `./run.sh`). Some cameras only refuse the second app.

Please note the result in an issue; it decides the default for later milestones.

## C. Milestone 2 checklist (on your Mac)

| # | Test | Steps | Expected | ☐ |
|---|---|---|---|---|
| 1 | MainScene loads | Open the project in 2022.3.62f1. Accept "Create Main Scene", or run **FILTER CMR → Setup Main Scene**. Then **FILTER CMR → Validate Scene** | No compile errors in the Console. Validation says *Scene OK*. Hierarchy matches [UNITY_SETUP.md](UNITY_SETUP.md#what-the-setup-tool-creates). Running Setup a second time adds nothing ("Scene was already complete") | ☐ |
| 2 | Python runs | `cd HandTracking && ./run.sh` | Preview window with your camera; FPS shown in its header | ☐ |
| 3 | Unity receives UDP | Press Play in Unity | Status bar `TRK ●` and `UDP ●` cyan. Debug panel: PY TRACKER **CONNECTED**, UDP **LISTENING** ~30 pkt/s, `bad 0`, LAST MESSAGE < 0.1 s ago | ☐ |
| 4 | One hand → 21 landmarks | Raise only your **right** hand | One skeleton (electric blue): 21 dots, 5 larger fingertip rings, connections incl. palm 5-9-13-17-5. RIGHT HAND **DETECTED**, LEFT **LOST**. Hint: RAISE YOUR OTHER HAND | ☐ |
| 5 | Two hands | Raise both hands | Two skeletons: left cyan, right blue. Both DETECTED. Never two of the same side | ☐ |
| 6 | Point gesture | Both hands: index up, others folded; hold ~0.2 s | Both hands `pose point`; GESTURE **TWO FINGER POINT · ACTIVE**; label ◆ TWO FINGER POINT at the top; index tips get white centres; L/R INDEX TIP show coordinates | ☐ |
| 7 | Peace gesture | Both hands: index + middle up | **TWO FINGER FRAME · ACTIVE**; index and middle tips highlighted; L/R MIDDLE TIP show coordinates | ☐ |
| 8 | Landmarks follow | Move both hands around the whole frame, including the corners | Overlay stays on your hands with no visible lag or jitter. Moving your right hand to your right moves the blue skeleton right on screen (mirror view). Fingertip coordinates go from ~0 to ~1 | ☐ |
| 9 | Hand disappears | Drop one hand out of view | Its skeleton fades (held) then vanishes within ~0.2 s. The gesture ends after ≤ 0.25 s. The other hand is unaffected | ☐ |
| 10 | Tracker stopped | Press Q in the tracker preview (or Ctrl+C) | Within ~0.1 s (bye): PY TRACKER **STOPPED (bye)**, hint TRACKER STOPPED, skeletons gone, no errors in the Console. Kill it hard (`kill -9`) to test the timeout: **DISCONNECTED** after ~1 s | ☐ |
| 11 | Tracker restarted | `./run.sh` again | Back to CONNECTED within a second, without touching Unity. **R** in Unity also reconnects cleanly | ☐ |
| 12 | Cleanup on Play Mode exit | Stop Play Mode; then `python tools/udp_monitor.py` | Console: `UDP receiver stopped`. The monitor can bind port 5065, which proves the socket was released. Enter and exit Play Mode 5 times: no "already in use" errors, and the Editor stays responsive | ☐ |

### Extra checks

| Test | Steps | Expected | ☐ |
|---|---|---|---|
| Alignment | Hold a fingertip still over a distinct point on your face or the background | The ring stays on it. If it's mirror-inverted, press **M** and report your camera model | ☐ |
| Aspect | Resize the Game view to 4:3, 21:9 and free | Video letterboxed, never stretched; landmarks stay aligned | ☐ |
| Shortcuts | **D**, **H**, **C** (with 2+ cameras), **R**, **M** | Panel hides/shows; landmarks hide/show; camera switches; receiver restarts; video flips | ☐ |
| Webcam unplug (USB) | Unplug during Play, wait, re-plug | WEBCAM STALLED → ERROR "disconnected" → RUNNING again after re-plug (retry every 4 s) | ☐ |
| Port conflict | Start `udp_monitor.py` **before** Play | Hint **UDP ERROR**, explaining that the port is in use. Close the monitor, press **R** → LISTENING | ☐ |
| FPS | Watch RENDER FPS with both hands tracked | Record it in an issue (no number is promised in advance) | ☐ |
