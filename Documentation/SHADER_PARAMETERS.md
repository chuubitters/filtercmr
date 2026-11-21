# Shader and visual parameters

All parameters live on **VisualEffectController** (Hierarchy: *FILTER CMR / Systems / Visual Effects*). `WebcamCompositeController` uploads them every frame to the `FILTERCMR/WebcamComposite` shader through `CompositeUniformWriter`. Sizes marked *ISO* are fractions of the displayed image height; *px* values are screen pixels.

Changing them: Inspector (anytime) · settings panel **S** (marked ◆) · keys (marked with the key).

## Halftone

| Parameter | Default | Range | Effect | Uniform |
|---|---|---|---|---|
| Halftone Enabled **T** | on | | Dots + colour treatment inside the mask. Off: the mask region shows the webcam (plus any RGB split, glitch or grain still on) | `_HTEnabled` |
| Halftone Intensity ◆ **[ ]** | 1 | 0–1 | Blend between webcam and full halftone | `_HTIntensity` |
| Dot Spacing ◆ | 9 px | 3–40 | Distance between dot centres | `_HTSpacingPx` |
| Dot Size ◆ | 1 | 0.2–1.6 | Dot radius multiplier; 1 = dots touch at 100% coverage | `_HTDotSize` |
| Dot Shape ◆ | Circle | Circle, Square, Soft Circle, Irregular | Irregular = jittered, wobbling, per-cell size variation | `_HTShape` |
| Grid Angle | 30° | 0–90 | Rotation of the dot grid | `_HTAngle` |
| Contrast | 1.25 | 0.5–2.5 | Tone contrast before dot sizing | `_HTContrast` |
| Threshold | 0.06 | 0–0.6 | Tones beyond this print no dot (clean highlights) | `_HTThreshold` |
| Style | Print | Print, Screen | Print: blue ink on pale paper, dark = bigger dots. Screen: light dots on navy, bright = bigger dots | `_HTStyle` |
| Tone Mix | 0.35 | 0–1 | Webcam tones (through the blue ramp) shown behind the dots, for depth | `_HTToneMix` |
| Perspective Dots | on | | Frame mode: the dot grid follows the quad's perspective | `_QuadPersp` |

Dot area is proportional to tone (radius ∝ √coverage), the way print reproduces tones. Each dot samples the image once, at its cell centre, and edges are anti-aliased over one screen pixel.

## Colour

| Parameter | Default | Role | Uniform |
|---|---|---|---|
| Palette ◆ | FILTER CMR Blue | Preset that sets all colours below: FILTER CMR Blue, Cyan Print, Midnight, Ice Riso | — |
| Foreground | `#155EEF` | Dot ink (Print) or light-dot tint (Screen) | `_ColInk` |
| Background Tint | `#DDF8FD` | Paper behind the dots | `_ColPaper` |
| Primary Blue | `#155EEF` | Tone ramp: mid-tones | `_ColBlue` |
| Cyan | `#35DDF2` | Tone ramp: highlights; edge-glow colour | `_ColCyan` |
| Light Cyan | `#A5F3FC` | Tone ramp: bright; outline colour | `_ColLightCyan` |
| Navy | `#07152F` | Tone ramp: shadows; Screen background | `_ColNavy` |
| Dark Blue | `#102A56` | Tone ramp: dark mid-tones | `_ColDarkBlue` |
| White | `#F7FBFF` | Tone ramp: top | `_ColWhite` |
| Cyan Intensity | 1 | How far highlights move to cyan | `_CyanIntensity` |
| Blue Intensity | 1 | How far mid-tones use electric blue rather than dark blue | `_BlueIntensity` |
| Saturation | 1 | Saturation of the halftone result | `_Saturation` |
| Brightness | 0.02 | Tone offset | `_Brightness` |

Tone ramp: navy (0) → dark/electric blue (0.28) → electric blue/cyan (0.58) → light cyan (0.84) → white (1).

**Changing colours:** pick a Palette, then fine-tune the colour fields. Colours are entered and used in gamma (sRGB) space, so they look the same in Gamma and Linear projects.

## RGB split

| Parameter | Default | Range | Uniform |
|---|---|---|---|
| RGB Split Enabled ◆ | on | | `_RGBEnabled` |
| RGB Split ◆ | 2.5 px | 0–20 | `_RGBDistancePx` |
| RGB Split Angle | 0° | 0–360 | `_RGBAngle` |
| RGB Split Animated | on | slow drift of angle and distance | `_RGBAnimate` |

## Glitch

| Parameter | Default | Range | Uniform |
|---|---|---|---|
| Glitch Enabled **X** ◆ | on | | `_GlitchEnabled` |
| Glitch Intensity ◆ | 0.25 | 0–1 (max displacement ≈ 3% of the width) | `_GlitchIntensity` |
| Glitch Frequency | 0.35 | 0–1 (more bands, faster changes) | `_GlitchFrequency` |
| Glitch Bursts | on | occasional stronger moments | `_GlitchBursts` |
| Scanline Intensity | 0.3 | 0–1 (darkened scanlines + wobble) | `_ScanlineIntensity` |

## Grain and print texture

| Parameter | Default | Range | Uniform |
|---|---|---|---|
| Grain Enabled **G** ◆ | on | | `_GrainEnabled` |
| Grain Intensity ◆ | 0.25 | 0–1 | `_GrainIntensity` |
| Grain Scale | 1.5 px | 1–6 | `_GrainScalePx` |
| Grain Animated / Grain Fps | on / 24 | static grain never changes (no flicker) | `_FxGrainSeed` |
| Print Texture | 0.35 | 0–1, paper-fibre texture under the dots | `_PrintTexture` |

## Mask and edge

| Parameter | Default | Range | Uniform |
|---|---|---|---|
| Opacity | 1 | 0–1 | `_MaskOpacity` |
| Edge Feather ◆ | 0.035 ISO (~25 px at 720p) | 0.002–0.15 | `_MaskFeather` |
| Edge Glow ◆ | 0.35 | 0–1.5 (cyan, inside the edge only) | `_EdgeGlow` |
| Edge Glow Width | 0.02 ISO | | `_EdgeGlowWidth` |
| Edge Outline / Width | off / 1.5 px | thin light-cyan line on the boundary | `_EdgeOutline`, `_EdgeOutlinePx` |
| Edge Distortion / Noise Scale | 0.3 / 9 | organic wobble of the boundary | `_EdgeDistortion`, `_EdgeNoiseScale` |
| Debug Mask **V** | off | magenta mask tint + green bounding boxes | `_DebugMask` |

## Ribbon (TWO FINGER POINT)

| Parameter | Default | Effect |
|---|---|---|
| Ribbon Width ◆ | 0.09 ISO | Base width |
| Ribbon Min / Max Width | 0.025 / 0.2 ISO | Width clamp |
| Curve Strength | 0.35 | Tangent length; 0 = straight |
| Curve Direction | 0 | Sideways bow (−1 … 1) |
| Finger Influence | 0.55 | How much each index finger's direction bends the strip |
| Ribbon Taper | 0.45 | Width at the fingertips relative to the middle |
| Ribbon Asymmetry | 0 | Wider at the left (+) or right (−) tip |
| Noise Distortion | 0.25 | Flowing wave + width breathing; zero at the fingertips |
| Flow Speed | 0.35 Hz | Wave speed |
| Stretch Response | 0.3 | Thinner when the hands move apart |

## Tracking and transitions

| Parameter | Default | Effect |
|---|---|---|
| Fade In | 0.25 s | Mask appearance |
| Fade Out | 0.5 s | Mask disappearance. Keep it > 0.45 s (gesture release + confirm) for flash-free switching |
| Smoothing | 18 /s | Control-point smoothing (0 = off) |
| Max Point Speed | 6 ISO/s | Rejects one-frame tracking jumps |

Gesture timing (hold to confirm 0.2 s, release grace 0.25 s) is in the **FilterCMRSettings** asset (Milestone 2).

## Mapping uniforms (set automatically)

| Uniform | Meaning |
|---|---|
| `_FxUVRect` | RawImage uvRect; inverted per pixel to get landmark (IMAGE) coordinates |
| `_FxAspect` | Displayed image width/height (IMAGE → ISO) |
| `_FxPixelIso` | One screen pixel in ISO units (pixel-sized parameters, anti-aliasing) |
| `_FxTime` | Unscaled time |
| `_RibbonPts[25]`, `_RibbonCount`, `_RibbonWeight`, `_RibbonBounds` | Ribbon samples (x, y, half-width), fade, bounding box |
| `_QuadV01`, `_QuadV23`, `_QuadWeight`, `_QuadBounds` | Quad vertices, fade, bounding box |
| `_QuadH0..2`, `_QuadHI0..2`, `_QuadGrid` | Homography, its inverse, and grid size for perspective dots |
