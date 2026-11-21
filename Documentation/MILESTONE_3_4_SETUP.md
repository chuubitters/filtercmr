# Milestones 3 and 4: interactive masks and halftone effects (setup)

What you get: the live webcam stays normal until you gesture. Then:
* **Both index fingers (point)** → a flowing **ribbon** between the fingertips.
* **Two peace signs** → a **frame** spanning the four fingertips.

Inside the shape the webcam turns into a blue/cyan halftone print with grain, a subtle glitch and RGB split. Everything outside stays untouched.

## 1. Update and open

```bash
cd filtercmr && git pull
```

Open `UnityProject` in **Unity 2022.3.62f1** as before and wait for the import. The Console must show no compile errors.

## 2. Upgrade your existing MainScene

Menu **FILTER CMR → Setup Visual Effects**. It:

* opens your existing `Assets/FILTERCMR/Scenes/MainScene.unity` (it creates it if missing);
* keeps every existing object, component and setting, and only adds what's missing:

| Added | Where |
|---|---|
| `InteractiveMaskManager` | FILTER CMR / Systems / **Interactive Mask** |
| `VisualEffectController` (every visual parameter) | FILTER CMR / Systems / **Visual Effects** |
| `WebcamCompositeController` + composite material | on **Webcam Display** (the existing RawImage) |
| `MaskDebugVisualizer` | Webcam Display / Hand Overlay / **Mask Debug** |
| `VisualSettingsPanel` | UI Canvas / **Settings Panel** (bottom-right) |
| Presentation exit button | UI Canvas / **Presentation Exit** (top-right, only in presentation mode) |
| `Assets/FILTERCMR/Materials/FILTERCMR_WebcamComposite.mat` | material using `FILTERCMR/WebcamComposite` |

It then wires every reference and saves the scene. You can run it as often as you like. **FILTER CMR → Validate Scene** should then report *Scene OK*.

**Shader check:** select `Assets/FILTERCMR/Shaders/FILTERCMR_WebcamComposite.shader` and look at the Inspector. It must not list errors. If the shader can't be used, the app logs an error and shows the plain webcam instead of failing.

Commit what Unity and the tool generated, so other clones get the same scene:

```bash
git add UnityProject/Assets/FILTERCMR/Scenes UnityProject/Assets/FILTERCMR/Materials UnityProject/Assets/FILTERCMR/Settings UnityProject/ProjectSettings UnityProject/Packages/packages-lock.json
git commit -m "chore: add MainScene with visual effects"
```

## 3. Run

```bash
cd HandTracking && ./run.sh          # or ./run.sh --stream-video if you use Python Stream mode
```

Press **Play** in Unity, then raise both hands:

| Do | See |
|---|---|
| Both hands, index fingers up, other fingers folded, hold ~0.2 s | **TWO FINGER POINT**: the ribbon appears between the index tips and follows them |
| Two peace signs | **TWO FINGER FRAME**: the halftone frame through the four fingertips |
| Switch between the two | Smooth cross-fade, no flash |
| Drop your hands | The mask fades out (≤ 0.25 s grace + 0.5 s fade) and the normal webcam returns |

## 4. Controls

All the Milestone 2 keys still work: **D** debug panel, **R** restart UDP, **H** landmarks, **C** camera, **M** flip video.

| Key | Action |
|---|---|
| **1** | Ribbon mode: the ribbon whenever both hands are visible, any pose. Press again for AUTO |
| **2** | Quad mode: the frame whenever both hands are visible, any pose. Press again for AUTO |
| **0** | Mask off (plain webcam). Press again for AUTO |
| *(default)* | **AUTO**: gestures decide (point → ribbon, peace → quad) |
| **T** | Halftone on/off |
| **G** | Grain on/off |
| **X** | Glitch on/off |
| **[** / **]** | Halftone intensity −/+ 10% |
| **V** | Mask debug: magenta mask tint, ribbon endpoints and control points, quad hull and fingertips |
| **S** | Visual settings panel |
| **P** | Presentation mode: hide every panel, the status bar and the landmarks. **P** or the faint dot in the top-right corner exits |

**No keyboard?** Gestures do everything. The top bar has **SETTINGS / DEBUG / PRESENT** buttons, and the settings panel has buttons for every mode and toggle.

## 5. Adjusting the look

* **Quick, while running:** the settings panel (**S**) has ribbon width, dot spacing, dot size, halftone intensity, grain, RGB split, glitch, edge feather and edge glow. The **PALETTE** button cycles FILTER CMR Blue, Cyan Print, Midnight and Ice Riso. **DOTS** cycles circle, square, soft circle and irregular. **RESET LOOK** restores the defaults.
* **Everything:** select **FILTER CMR / Systems / Visual Effects** in the Hierarchy. Every parameter has a tooltip; see [SHADER_PARAMETERS.md](SHADER_PARAMETERS.md).
* **Colours:** set **Palette** for a preset, or edit *Foreground* (dot ink), *Background Tint* (paper) and the six tone-ramp colours.
* **Keeping changes:** values changed in Play Mode revert when you stop. To keep them, right-click the component → **Copy Component**, stop Play, then **Paste Component Values**. Or edit outside Play Mode.

## 6. Checking alignment

Press **V**:
* White rings must sit on the two index fingertips (ribbon).
* Coloured rings must sit on the four fingertips (quad).
* The magenta tint shows exactly where the effect applies.

If everything is mirrored relative to your hands, press **M** (see the Milestone 2 troubleshooting).

## Troubleshooting

| Symptom | Fix |
|---|---|
| Console: `FilterCMRManager.masks is not assigned` | Run **FILTER CMR → Setup Visual Effects** |
| Plain webcam only; the debug panel's SHADER row shows an error | Select the shader asset and fix the errors shown, or report them. Then run Setup Visual Effects again |
| Gesture shows in the debug panel but nothing appears | Check that MASK MODE isn't OFF (press **0** to toggle) and that `Opacity` and `Halftone Intensity` aren't 0 |
| The mask appears offset from your fingers | Press **V** and compare the markers with your fingertips. A constant offset means an aspect mismatch (the debug panel warns about it; see Milestone 2) |
| Ribbon flickers in and out | Hold the pose steadier, or raise **Fade Out**. Forced mode **1** ignores the pose |
| Faces too hard to recognise inside the mask | Lower **Dot Spacing** (finer dots), lower **Contrast**, raise **Tone Mix**, or lower **Halftone Intensity** (**[**) |
| Too much distortion | **X** (glitch off), or lower *Glitch Intensity* / *RGB Split* in the settings panel |
| Low FPS | Lower the Game view size. RGB split and perspective dots cost the most; turn them off first |
