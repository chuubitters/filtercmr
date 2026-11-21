# Milestones 3 and 4: test plan

Key: ✅ **automated and passing** (run during development) · 🖐 **needs your Mac** (Unity Editor, webcam, hands) · ☐ tick when done.

None of the 🖐 tests have been run: the development machine has no Mac, Unity Editor or webcam. The shader has been **compiled and executed** outside Unity (see A3), but not in Unity's own Metal shader compiler. Treat the 🖐 rows as not yet verified.

## A. Automated

| # | Suite | How to run | Result during development |
|---|---|---|---|
| A1 | Unity EditMode tests (60): parser, gestures, **masks (22)**, **uniform packing (7)** | Unity → Test Runner → EditMode → Run All | ✅ 60/60 under .NET 8 via `Tools/DotnetVerification` (same source files). 🖐 not yet run inside Unity |
| A2 | End-to-end UDP (4): real Python → C# receivers | `cd Tools/DotnetVerification && FILTERCMR_PYTHON=../../HandTracking/.venv/bin/python dotnet run` | ✅ 64/64 (60 + 4) |
| A3 | **Shader check (36):** the real `FILTERCMR_WebcamComposite.shader` compiled (6 keyword variants) and executed on a software GPU with uniforms from the real C# mask code | `python3 Tools/ShaderPreview/shader_check.py --cgincludes <Unity CGIncludes>` (see Tools/ShaderPreview/README.md) | ✅ 36/36 |
| A4 | Unity compile check: every runtime + editor script against API stubs, each stub verified in UnityCsReference **2022.3.62f1** / uGUI | `Tools/UnityCompileCheck` (`python3 verify_api.py` → 136 OK, `dotnet build` → 0 errors, 0 warnings) | ✅ |
| A5 | Python tracker (74) | `cd HandTracking && python -m pytest -q tests` | ✅ 74/74 (tracker unchanged in this milestone) |

What A1–A3 prove:

| Requirement | Automated evidence |
|---|---|
| Ribbon connects both index tips exactly, stays between min and max width, tapers, bends naturally when a hand moves vertically, never loops back (2,000 random cases), no NaN | `MaskTests.Ribbon*`, `MovingOneHandVerticallyBendsSmoothly`, `RibbonNeverLoopsBack…` |
| Quad never self-intersects or inverts, contains all four tips (20,000 random cases), triangle and collinear handling, homography correct | `MaskTests.BowTie…`, `RandomTips…`, `Homography…` |
| Smoothing converges; one-frame jumps are speed-limited | `SmootherSnapsThenConvergesAndLimitsJumps` |
| Point → peace switch never drops below 15% mask visibility (no flash); removing hands fades out completely within 1 s | `PointToPeaceCrossFadesWithoutFlash`, `RemovingHandsFadesOutCompletely` |
| Mirroring/UV mapping correct for normal, mirrored and vertically flipped displays | `UvRoundTripForAllDisplayOrientations`, shader check "mirrored display" |
| **Outside the mask the webcam is pixel-identical** (max difference 0) in Gamma and Linear projects, for ribbon and quad | shader check |
| Inside: 100% of ribbon pixels changed, blue/cyan dominant | shader check |
| Halftone off (with other effects off) → the mask region equals the webcam | shader check |
| RGB split, glitch, grain each confined to the mask | shader check |
| Dot spacing parameter = measured dot period (6, 9, 16 px) | shader check |
| Darker image → larger ink coverage (100% / 82% / 0%) | shader check |
| Soft feathered edge (no hard step) | shader check |
| All 4 dot shapes and 4 palettes render | shader check (preview PNGs) |

## B. Without a webcam (fake tracker)

```bash
cd HandTracking && source .venv/bin/activate
python tools/fake_tracker.py --stream-video        # cycles point → peace → open every 4 s
```

Use Camera Source **Python Stream** or **Auto**. Expected: a ribbon between the two synthetic index tips during "point", a frame during "peace", nothing during "open". Press **V**: the debug markers sit on the white rings drawn into the synthetic video.

## C. Checklist on your Mac

Before starting: run **FILTER CMR → Setup Visual Effects**, then **Validate Scene**. In another terminal: `./run.sh`.

| # | Test | Steps | Expected | ☐ |
|---|---|---|---|---|
| 1 | Open MainScene without errors | Open project, run Setup Visual Effects, Validate | No Console errors; *Scene OK*; shader Inspector shows no errors | ☐ |
| 2 | Tracker + Play | `./run.sh`, press Play | Webcam visible, TRK/UDP cyan, debug panel SHADER **OK** | ☐ |
| 3 | Normal webcam without a gesture | Stand in view, hands down or open | No dots, no tint, no distortion anywhere. MASK row: *none: normal webcam* | ☐ |
| 4 | Point → ribbon | Both index fingers up, others folded | Within ~0.2 s a halftone ribbon appears between the index tips; GESTURE = TWO FINGER POINT | ☐ |
| 5 | Ribbon length | Move hands closer and farther apart | Ribbon shortens and lengthens smoothly, thinning slightly when stretched; ends stay on the fingertips | ☐ |
| 6 | Ribbon curve | Move one hand up and down; tilt the fingers | Curve bends naturally, no jumps | ☐ |
| 7 | Peace → quad | Two peace signs | Halftone frame through the 4 fingertips (index + middle of both hands) | ☐ |
| 8 | Quad follows | Move, rotate, cross and spread the fingers | Frame deforms, rotates and skews with the fingers; never a bow-tie or inside-out shape | ☐ |
| 9 | Point → peace | Change gesture without dropping hands | Ribbon fades as the frame fades in; no flash of plain webcam | ☐ |
| 10 | Remove hands | Drop both hands | Mask fades out within ~0.75 s; normal webcam returns; nothing frozen | ☐ |
| 11 | Halftone on | During a gesture (T if off) | Dots only inside the mask | ☐ |
| 12 | Halftone off | **T** | Mask region shows the webcam again (with only RGB split, glitch and grain if those are still on; press **G** and **X**, and set RGB split to 0 for a fully plain region) | ☐ |
| 13 | RGB split | Settings panel: RGB SPLIT on, slider up | Colour fringes only inside the mask | ☐ |
| 14 | Grain + glitch | **G**, **X** on; raise sliders | Grain and band displacement only inside the mask | ☐ |
| 15 | Dot spacing and size | Move the DOT SPACING / DOT SIZE sliders (or Inspector) | Dots change immediately | ☐ |
| 16 | Alignment and mirroring | Press **V**; move your hands | White and coloured debug rings sit on your real fingertips; magenta tint follows the shape; moving your right hand right moves it right on screen | ☐ |
| 17 | 5-minute run | Gesture on and off for 5 min; watch RENDER FPS; optionally Window → Analysis → Profiler | No progressive memory growth (GC Alloc per frame ≈ 0 apart from 10 Hz UI text), no major FPS drop, tracking intact. Record FPS here: ____ | ☐ |
| 18 | Python restart | Quit the tracker (Q), restart `./run.sh` | TRACKER STOPPED → CONNECTED; masks work again without restarting Unity | ☐ |

### Extra checks

| Test | Expected | ☐ |
|---|---|---|
| Keys **1 / 2 / 0** | Status bar MASK shows RIBBON / QUAD / OFF; pressing the same key again → AUTO | ☐ |
| **[ / ]** | Halftone intensity steps by 10% (settings panel slider moves) | ☐ |
| **S**, **P**, corner dot | Settings panel toggles; presentation hides all panels, the status bar and landmarks; the corner dot or **P** brings them back | ☐ |
| Mouse only | SETTINGS / DEBUG / PRESENT buttons in the top bar, plus all settings-panel buttons, work | ☐ |
| Palettes and dot shapes | PALETTE and DOTS buttons cycle visibly | ☐ |
| Game view 4:3 / 21:9 / resized | Mask stays on the fingertips; dots stay round and the same pixel size | ☐ |
| Python Stream mode (`--stream-video`) | Same behaviour as with the Unity webcam | ☐ |
| Setup Visual Effects run twice | Second run: "Scene was already complete" (no duplicates) | ☐ |
