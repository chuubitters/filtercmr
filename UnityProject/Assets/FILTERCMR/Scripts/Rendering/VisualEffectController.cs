// FILTER CMR — central place for every visual parameter (Inspector + runtime).
//
// Edit these in the Inspector (component "Visual Effects" under FILTER CMR/Systems),
// in the in-game settings panel (S), or with the keys T / G / X / [ / ].
// Values are copied into an EffectValues struct each frame (Fill) and uploaded
// to the composite shader by WebcamCompositeController.
//
// Note: like any component, values changed in Play Mode reset when Play Mode
// ends. Use the component menu "Copy Component" → "Paste Component Values".

using FilterCMR.Mask;
using UnityEngine;
using NVector4 = System.Numerics.Vector4;

namespace FilterCMR.Rendering
{
    [DefaultExecutionOrder(-60)]
    [DisallowMultipleComponent]
    public sealed class VisualEffectController : MonoBehaviour
    {
        static readonly EffectValues D = EffectValues.Default;
        static Color C(NVector4 v) => new Color(v.X, v.Y, v.Z, v.W);
        static NVector4 V(Color c) => new NVector4(c.r, c.g, c.b, c.a);

        // ------------------------------------------------------------------ HALFTONE
        [Header("Halftone")]
        [Tooltip("Turn the halftone dot treatment on/off (T).")]
        public bool halftoneEnabled = D.halftoneEnabled;
        [Tooltip("Blend between the webcam image (0) and the full halftone (1) inside the mask ([ / ]).")]
        [Range(0f, 1f)] public float halftoneIntensity = D.halftoneIntensity;
        [Tooltip("Distance between dot centres, in screen pixels.")]
        [Range(3f, 40f)] public float dotSpacing = D.dotSpacingPx;
        [Tooltip("Dot size multiplier. 1 = neighbouring dots just touch at full coverage.")]
        [Range(0.2f, 1.6f)] public float dotSize = D.dotSize;
        [Tooltip("Circle, Square, Soft Circle or Irregular (organic, jittered) dots.")]
        public DotShape dotShape = D.dotShape;
        [Tooltip("Screen angle of the dot grid in degrees (classic print uses 15–45°).")]
        [Range(0f, 90f)] public float gridAngle = D.gridAngleDeg;
        [Tooltip("Tone contrast before dot sizing.")]
        [Range(0.5f, 2.5f)] public float contrast = D.contrast;
        [Tooltip("Tones lighter (Print) / darker (Screen) than this print no dot. 0 = every tone prints.")]
        [Range(0f, 0.6f)] public float threshold = D.threshold;
        [Tooltip("Print: blue ink dots on pale paper, darker image = bigger dots. Screen: light dots on navy, brighter image = bigger dots.")]
        public HalftoneStyle style = D.style;
        [Tooltip("How much of the webcam's tonal image (mapped to the blue ramp) shows behind the dots, for depth.")]
        [Range(0f, 1f)] public float toneMix = D.toneMix;
        [Tooltip("In the frame (quad) mode, bend the dot grid with the quad's perspective.")]
        public bool perspectiveDots = D.perspectiveDots;

        // --------------------------------------------------------------------- COLOR
        [Header("Color")]
        [Tooltip("Preset applied to the colours below when changed (also via the settings panel).")]
        public PalettePreset palette = PalettePreset.FilterCMRBlue;
        [Tooltip("Halftone foreground (ink / light dot) colour.")]
        public Color foreground = C(D.ink);
        [Tooltip("Background tint (paper) behind the dots.")]
        public Color backgroundTint = C(D.paper);
        [Tooltip("Electric blue mid-tone of the tone ramp.")]
        public Color primaryBlue = C(D.primaryBlue);
        [Tooltip("Cyan highlight tone; also the edge-glow colour.")]
        public Color cyan = C(D.cyan);
        [Tooltip("Light cyan highlight; also the outline colour.")]
        public Color lightCyan = C(D.lightCyan);
        [Tooltip("Deep navy shadows.")]
        public Color navy = C(D.navy);
        [Tooltip("Dark blue lower mid-tone.")]
        public Color darkBlue = C(D.darkBlue);
        [Tooltip("Brightest highlight.")]
        public Color white = C(D.white);
        [Tooltip("How strongly highlights shift to cyan (0 = stay electric blue).")]
        [Range(0f, 1f)] public float cyanIntensity = D.cyanIntensity;
        [Tooltip("How strongly mid-tones use electric blue (0 = darker blue).")]
        [Range(0f, 1f)] public float blueIntensity = D.blueIntensity;
        [Tooltip("Saturation of the treated region (0 = monochrome).")]
        [Range(0f, 1.5f)] public float saturation = D.saturation;
        [Tooltip("Brightness offset applied to the tones.")]
        [Range(-0.5f, 0.5f)] public float brightness = D.brightness;

        // ------------------------------------------------------------- RGB SPLIT
        [Header("RGB Split")]
        public bool rgbSplitEnabled = D.rgbSplitEnabled;
        [Tooltip("Channel separation in screen pixels.")]
        [Range(0f, 20f)] public float rgbSplit = D.rgbSplitPx;
        [Tooltip("Separation direction in degrees (0 = horizontal).")]
        [Range(0f, 360f)] public float rgbSplitAngle = D.rgbSplitAngleDeg;
        [Tooltip("Slowly animate the separation direction and distance.")]
        public bool rgbSplitAnimated = D.rgbSplitAnimated;

        // ---------------------------------------------------------------- GLITCH
        [Header("Glitch")]
        [Tooltip("Horizontal band displacement + scanlines (X).")]
        public bool glitchEnabled = D.glitchEnabled;
        [Range(0f, 1f)] public float glitchIntensity = D.glitchIntensity;
        [Tooltip("How often and how many bands glitch.")]
        [Range(0f, 1f)] public float glitchFrequency = D.glitchFrequency;
        [Tooltip("Occasional stronger bursts.")]
        public bool glitchBursts = D.glitchBursts;
        [Range(0f, 1f)] public float scanlineIntensity = D.scanlineIntensity;

        // ----------------------------------------------------------------- GRAIN
        [Header("Grain")]
        [Tooltip("Fine digital grain inside the mask (G).")]
        public bool grainEnabled = D.grainEnabled;
        [Range(0f, 1f)] public float grainIntensity = D.grainIntensity;
        [Tooltip("Grain particle size in screen pixels.")]
        [Range(1f, 6f)] public float grainScale = D.grainScalePx;
        [Tooltip("Animated grain changes at Grain Fps; static grain never changes (no flicker).")]
        public bool grainAnimated = D.grainAnimated;
        [Range(1f, 60f)] public float grainFps = D.grainFps;
        [Tooltip("Paper-fibre print texture under the dots.")]
        [Range(0f, 1f)] public float printTexture = D.printTexture;

        // ------------------------------------------------------------------ MASK
        [Header("Mask")]
        [Tooltip("Overall mask opacity.")]
        [Range(0f, 1f)] public float opacity = D.maskOpacity;
        [Tooltip("Soft transition width at the mask edge, as a fraction of the image height.")]
        [Range(0.002f, 0.15f)] public float edgeFeather = D.edgeFeather;
        [Tooltip("Cyan glow along the inside of the mask edge.")]
        [Range(0f, 1.5f)] public float edgeGlow = D.edgeGlow;
        [Range(0.002f, 0.1f)] public float edgeGlowWidth = D.edgeGlowWidth;
        [Tooltip("Thin light-cyan outline on the mask boundary.")]
        public bool edgeOutline = D.edgeOutline;
        [Range(0.5f, 6f)] public float edgeOutlineWidth = D.edgeOutlinePx;
        [Tooltip("Organic wobble of the mask boundary.")]
        [Range(0f, 1f)] public float edgeDistortion = D.edgeDistortion;
        [Range(1f, 40f)] public float edgeNoiseScale = D.edgeNoiseScale;
        [Tooltip("Tint the mask area magenta to check where the mask is (V).")]
        public bool debugMask = D.debugMask;

        // ---------------------------------------------------------------- RIBBON
        [Header("Ribbon (TWO FINGER POINT)")]
        [Tooltip("Base ribbon width, as a fraction of the image height.")]
        [Range(0.01f, 0.4f)] public float ribbonWidth = D.ribbon.width;
        [Range(0.005f, 0.2f)] public float ribbonMinWidth = D.ribbon.minWidth;
        [Range(0.02f, 0.5f)] public float ribbonMaxWidth = D.ribbon.maxWidth;
        [Tooltip("How far the curve's tangents reach (0 = straight between the tips).")]
        [Range(0f, 1f)] public float curveStrength = D.ribbon.curveStrength;
        [Tooltip("Sideways bow of the ribbon (−1 … 1).")]
        [Range(-1f, 1f)] public float curveDirection = D.ribbon.curveDirection;
        [Tooltip("How much each index finger's pointing direction bends the ribbon.")]
        [Range(0f, 1f)] public float fingerInfluence = D.ribbon.fingerInfluence;
        [Tooltip("Width at the fingertips relative to the middle.")]
        [Range(0.05f, 1f)] public float ribbonTaper = D.ribbon.taper;
        [Tooltip("Wider at the left (+) or right (−) fingertip.")]
        [Range(-1f, 1f)] public float ribbonAsymmetry = D.ribbon.asymmetry;
        [Tooltip("Organic flowing wave and width breathing.")]
        [Range(0f, 1f)] public float noiseDistortion = D.ribbon.noiseDistortion;
        [Range(0f, 2f)] public float flowSpeed = D.ribbon.flowSpeed;
        [Tooltip("How much the ribbon thins as the hands move apart.")]
        [Range(0f, 1f)] public float stretchResponse = D.ribbon.lengthResponse;

        // ------------------------------------------------------------ TRANSITIONS
        [Header("Tracking & transitions")]
        [Tooltip("Seconds for a mask to fade in.")]
        [Range(0f, 2f)] public float fadeIn = D.fadeInSeconds;
        [Tooltip("Seconds for a mask to fade out. Keep it > 0.45 s for flash-free point ↔ peace switching.")]
        [Range(0f, 3f)] public float fadeOut = D.fadeOutSeconds;
        [Tooltip("Smoothing of the mask control points (1/s). Lower = smoother but laggier. 0 = off.")]
        [Range(0f, 60f)] public float smoothing = D.pointSmoothing;
        [Tooltip("Maximum control point speed (image heights per second); rejects one-frame tracking jumps.")]
        [Range(0f, 20f)] public float maxPointSpeed = D.maxPointSpeed;

        // Remembers which preset the colours came from, so OnValidate only re-applies on a real change.
        [SerializeField, HideInInspector] PalettePreset _appliedPalette = PalettePreset.FilterCMRBlue;

        /// <summary>Copies the current parameters into <paramref name="v"/> (no allocation).</summary>
        public void Fill(ref EffectValues v)
        {
            v.halftoneEnabled = halftoneEnabled; v.halftoneIntensity = halftoneIntensity; v.dotSpacingPx = dotSpacing;
            v.dotSize = dotSize; v.dotShape = dotShape; v.gridAngleDeg = gridAngle; v.contrast = contrast; v.threshold = threshold;
            v.style = style; v.toneMix = toneMix; v.perspectiveDots = perspectiveDots;
            v.ink = V(foreground); v.paper = V(backgroundTint); v.primaryBlue = V(primaryBlue); v.cyan = V(cyan);
            v.lightCyan = V(lightCyan); v.navy = V(navy); v.darkBlue = V(darkBlue); v.white = V(white);
            v.cyanIntensity = cyanIntensity; v.blueIntensity = blueIntensity; v.saturation = saturation; v.brightness = brightness;
            v.rgbSplitEnabled = rgbSplitEnabled; v.rgbSplitPx = rgbSplit; v.rgbSplitAngleDeg = rgbSplitAngle; v.rgbSplitAnimated = rgbSplitAnimated;
            v.glitchEnabled = glitchEnabled; v.glitchIntensity = glitchIntensity; v.glitchFrequency = glitchFrequency;
            v.glitchBursts = glitchBursts; v.scanlineIntensity = scanlineIntensity;
            v.grainEnabled = grainEnabled; v.grainIntensity = grainIntensity; v.grainScalePx = grainScale; v.grainAnimated = grainAnimated;
            v.grainFps = grainFps; v.printTexture = printTexture;
            v.maskOpacity = opacity; v.edgeFeather = edgeFeather; v.edgeGlow = edgeGlow; v.edgeGlowWidth = edgeGlowWidth;
            v.edgeOutline = edgeOutline; v.edgeOutlinePx = edgeOutlineWidth; v.edgeDistortion = edgeDistortion;
            v.edgeNoiseScale = edgeNoiseScale; v.debugMask = debugMask;
            v.ribbon = new RibbonParams
            {
                width = ribbonWidth, minWidth = ribbonMinWidth, maxWidth = ribbonMaxWidth, curveStrength = curveStrength,
                curveDirection = curveDirection, fingerInfluence = fingerInfluence, taper = ribbonTaper, asymmetry = ribbonAsymmetry,
                noiseDistortion = noiseDistortion, flowSpeed = flowSpeed, lengthResponse = stretchResponse,
            };
            v.fadeInSeconds = fadeIn; v.fadeOutSeconds = fadeOut; v.pointSmoothing = smoothing; v.maxPointSpeed = maxPointSpeed;
        }

        // ------------------------------------------------------------ runtime API
        public void ToggleHalftone() => halftoneEnabled = !halftoneEnabled;
        public void ToggleGrain() => grainEnabled = !grainEnabled;
        public void ToggleGlitch() => glitchEnabled = !glitchEnabled;
        public void ToggleRgbSplit() => rgbSplitEnabled = !rgbSplitEnabled;
        public void ToggleDebugMask() => debugMask = !debugMask;
        public void AdjustHalftoneIntensity(float delta) => halftoneIntensity = Mathf.Clamp01(halftoneIntensity + delta);

        public void SetPalette(PalettePreset p)
        {
            EffectValues tmp = default;
            EffectValues.ApplyPalette(ref tmp, p);
            foreground = C(tmp.ink); backgroundTint = C(tmp.paper); primaryBlue = C(tmp.primaryBlue); cyan = C(tmp.cyan);
            lightCyan = C(tmp.lightCyan); navy = C(tmp.navy); darkBlue = C(tmp.darkBlue); white = C(tmp.white);
            style = tmp.style;
            palette = _appliedPalette = p;
        }

        public void CyclePalette() => SetPalette((PalettePreset)(((int)palette + 1) % 4));
        public void CycleDotShape() => dotShape = (DotShape)(((int)dotShape + 1) % 4);

        /// <summary>Restore every parameter to the FILTER CMR defaults.</summary>
        public void ResetToDefaults()
        {
            var d = EffectValues.Default;
            halftoneEnabled = d.halftoneEnabled; halftoneIntensity = d.halftoneIntensity; dotSpacing = d.dotSpacingPx; dotSize = d.dotSize;
            dotShape = d.dotShape; gridAngle = d.gridAngleDeg; contrast = d.contrast; threshold = d.threshold; toneMix = d.toneMix;
            perspectiveDots = d.perspectiveDots;
            cyanIntensity = d.cyanIntensity; blueIntensity = d.blueIntensity; saturation = d.saturation; brightness = d.brightness;
            rgbSplitEnabled = d.rgbSplitEnabled; rgbSplit = d.rgbSplitPx; rgbSplitAngle = d.rgbSplitAngleDeg; rgbSplitAnimated = d.rgbSplitAnimated;
            glitchEnabled = d.glitchEnabled; glitchIntensity = d.glitchIntensity; glitchFrequency = d.glitchFrequency;
            glitchBursts = d.glitchBursts; scanlineIntensity = d.scanlineIntensity;
            grainEnabled = d.grainEnabled; grainIntensity = d.grainIntensity; grainScale = d.grainScalePx; grainAnimated = d.grainAnimated;
            grainFps = d.grainFps; printTexture = d.printTexture;
            opacity = d.maskOpacity; edgeFeather = d.edgeFeather; edgeGlow = d.edgeGlow; edgeGlowWidth = d.edgeGlowWidth;
            edgeOutline = d.edgeOutline; edgeOutlineWidth = d.edgeOutlinePx; edgeDistortion = d.edgeDistortion; edgeNoiseScale = d.edgeNoiseScale;
            debugMask = false;
            ribbonWidth = d.ribbon.width; ribbonMinWidth = d.ribbon.minWidth; ribbonMaxWidth = d.ribbon.maxWidth;
            curveStrength = d.ribbon.curveStrength; curveDirection = d.ribbon.curveDirection; fingerInfluence = d.ribbon.fingerInfluence;
            ribbonTaper = d.ribbon.taper; ribbonAsymmetry = d.ribbon.asymmetry; noiseDistortion = d.ribbon.noiseDistortion;
            flowSpeed = d.ribbon.flowSpeed; stretchResponse = d.ribbon.lengthResponse;
            fadeIn = d.fadeInSeconds; fadeOut = d.fadeOutSeconds; smoothing = d.pointSmoothing; maxPointSpeed = d.maxPointSpeed;
            SetPalette(PalettePreset.FilterCMRBlue);
        }

        void Reset() => ResetToDefaults();      // Inspector "Reset"

        void OnValidate()
        {
            // Changing the palette field in the Inspector applies its colours.
            if (palette != _appliedPalette) SetPalette(palette);
            if (ribbonMaxWidth < ribbonMinWidth) ribbonMaxWidth = ribbonMinWidth;
        }
    }
}
