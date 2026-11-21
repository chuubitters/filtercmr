// FILTER CMR — every visual parameter as plain data (pure C#), plus the defaults.
//
// VisualEffectController owns the Inspector-friendly fields (Unity Colors,
// tooltips, ranges) and copies them into an EffectValues each frame.
// CompositeUniformWriter turns EffectValues + mask shapes into shader uniforms.
// Keeping this pure lets the same packing code drive the real shader outside
// Unity (Tools/ShaderPreview) and in unit tests.
//
// Units: *Px values are screen pixels; mask sizes are ISO (1 = image height).
// Colours are gamma-space RGBA in 0..1.

using System;
using System.Numerics;

namespace FilterCMR.Rendering
{
    public enum DotShape { Circle = 0, Square = 1, SoftCircle = 2, Irregular = 3 }

    /// <summary>Print = blue ink dots on pale paper (dark areas → bigger dots). Screen = light dots on navy (bright areas → bigger dots).</summary>
    public enum HalftoneStyle { Print = 0, Screen = 1 }

    public enum PalettePreset { FilterCMRBlue = 0, CyanPrint, Midnight, IceRiso }

    public struct EffectValues
    {
        // HALFTONE
        public bool halftoneEnabled;
        public float halftoneIntensity;   // 0..1
        public float dotSpacingPx;        // cell size in screen pixels
        public float dotSize;             // 1 = dots touch at full coverage
        public DotShape dotShape;
        public float gridAngleDeg;
        public float contrast;
        public float threshold;           // 0..1, tones below it print no dot
        public HalftoneStyle style;
        public float toneMix;             // 0..1, tonal underlay behind the dots
        public bool perspectiveDots;      // quad mode: grid follows the quad

        // COLOUR (gamma RGBA)
        public Vector4 ink, paper, primaryBlue, cyan, lightCyan, navy, darkBlue, white;
        public float cyanIntensity, blueIntensity, saturation, brightness;

        // RGB SPLIT
        public bool rgbSplitEnabled;
        public float rgbSplitPx;
        public float rgbSplitAngleDeg;
        public bool rgbSplitAnimated;

        // GLITCH
        public bool glitchEnabled;
        public float glitchIntensity;     // 0..1
        public float glitchFrequency;     // 0..1
        public bool glitchBursts;
        public float scanlineIntensity;   // 0..1

        // GRAIN
        public bool grainEnabled;
        public float grainIntensity;      // 0..1
        public float grainScalePx;
        public bool grainAnimated;
        public float grainFps;
        public float printTexture;        // 0..1

        // MASK EDGE
        public float maskOpacity;         // 0..1
        public float edgeFeather;         // ISO
        public float edgeGlow;            // 0..1
        public float edgeGlowWidth;       // ISO
        public bool edgeOutline;
        public float edgeOutlinePx;
        public float edgeDistortion;      // 0..1
        public float edgeNoiseScale;
        public bool debugMask;

        // MASK SHAPE / MOTION (consumed by InteractiveMaskManager)
        public Mask.RibbonParams ribbon;
        public float fadeInSeconds, fadeOutSeconds;
        public float pointSmoothing;      // 1/s
        public float maxPointSpeed;       // ISO/s

        public static Vector4 Hex(uint rgb) =>
            new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>Tasteful defaults: recognisable faces and hands, subtle distortion.</summary>
        public static EffectValues Default
        {
            get
            {
                var v = new EffectValues
                {
                    halftoneEnabled = true, halftoneIntensity = 1f, dotSpacingPx = 9f, dotSize = 1f,
                    dotShape = DotShape.Circle, gridAngleDeg = 30f, contrast = 1.25f, threshold = 0.06f,
                    style = HalftoneStyle.Print, toneMix = 0.35f, perspectiveDots = true,
                    cyanIntensity = 1f, blueIntensity = 1f, saturation = 1f, brightness = 0.02f,
                    rgbSplitEnabled = true, rgbSplitPx = 2.5f, rgbSplitAngleDeg = 0f, rgbSplitAnimated = true,
                    glitchEnabled = true, glitchIntensity = 0.25f, glitchFrequency = 0.35f, glitchBursts = true, scanlineIntensity = 0.3f,
                    grainEnabled = true, grainIntensity = 0.25f, grainScalePx = 1.5f, grainAnimated = true, grainFps = 24f, printTexture = 0.35f,
                    maskOpacity = 1f, edgeFeather = 0.035f, edgeGlow = 0.35f, edgeGlowWidth = 0.02f, edgeOutline = false, edgeOutlinePx = 1.5f,
                    edgeDistortion = 0.3f, edgeNoiseScale = 9f, debugMask = false,
                    ribbon = Mask.RibbonParams.Default,
                    fadeInSeconds = 0.25f, fadeOutSeconds = 0.5f, pointSmoothing = 18f, maxPointSpeed = 6f,
                };
                ApplyPalette(ref v, PalettePreset.FilterCMRBlue);
                return v;
            }
        }

        /// <summary>Palette presets — all within the FILTER CMR blue family.</summary>
        public static void ApplyPalette(ref EffectValues v, PalettePreset p)
        {
            // Shared tone ramp (navy → dark blue → electric blue → cyan → light cyan → white)
            v.navy = Hex(0x07152F);
            v.darkBlue = Hex(0x102A56);
            v.primaryBlue = Hex(0x155EEF);
            v.cyan = Hex(0x35DDF2);
            v.lightCyan = Hex(0xA5F3FC);
            v.white = Hex(0xF7FBFF);
            switch (p)
            {
                case PalettePreset.CyanPrint:     // cyan ink on white paper
                    v.ink = Hex(0x0FA9D6); v.paper = Hex(0xF7FBFF); v.style = HalftoneStyle.Print; break;
                case PalettePreset.Midnight:      // light dots on deep navy
                    v.ink = Hex(0x35DDF2); v.paper = Hex(0x07152F); v.style = HalftoneStyle.Screen; break;
                case PalettePreset.IceRiso:       // dark-blue ink on icy cyan
                    v.ink = Hex(0x102A56); v.paper = Hex(0xA5F3FC); v.style = HalftoneStyle.Print; break;
                default:                          // FILTER CMR Blue: electric-blue ink on pale cyan
                    v.ink = Hex(0x155EEF); v.paper = Hex(0xDDF8FD); v.style = HalftoneStyle.Print; break;
            }
        }

        public static string Label(PalettePreset p)
        {
            switch (p)
            {
                case PalettePreset.CyanPrint: return "CYAN PRINT";
                case PalettePreset.Midnight: return "MIDNIGHT";
                case PalettePreset.IceRiso: return "ICE RISO";
                default: return "FILTER CMR BLUE";
            }
        }
    }
}
