// FILTER CMR — packs mask shapes and effect values into the composite shader's
// uniforms. Pure C#: the Unity side passes a sink that writes to a Material
// (cached property IDs); Tools/ShaderPreview passes a sink that records values
// to drive the real shader outside Unity. One code path, no duplication.

using System;
using System.Numerics;
using FilterCMR.Mask;

namespace FilterCMR.Rendering
{
    /// <summary>Shader uniform names without the leading underscore (e.g. RibbonWeight → _RibbonWeight).</summary>
    public enum FxUniform
    {
        FxUVRect, FxAspect, FxPixelIso, FxTime, FxGrainSeed,
        RibbonCount, RibbonWeight, RibbonBounds,
        QuadV01, QuadV23, QuadWeight, QuadBounds,
        QuadPersp, QuadH0, QuadH1, QuadH2, QuadHI0, QuadHI1, QuadHI2, QuadGrid,
        MaskFeather, MaskOpacity, EdgeGlow, EdgeGlowWidth, EdgeOutline, EdgeOutlinePx, EdgeDistortion, EdgeNoiseScale, DebugMask,
        HTEnabled, HTIntensity, HTSpacingPx, HTDotSize, HTShape, HTAngle, HTContrast, HTThreshold, HTStyle, HTToneMix,
        ColInk, ColPaper, ColBlue, ColCyan, ColLightCyan, ColNavy, ColDarkBlue, ColWhite,
        CyanIntensity, BlueIntensity, Saturation, Brightness,
        RGBEnabled, RGBDistancePx, RGBAngle, RGBAnimate,
        GlitchEnabled, GlitchIntensity, GlitchFrequency, GlitchBursts, ScanlineIntensity,
        GrainEnabled, GrainIntensity, GrainScalePx, PrintTexture,
        Count,
    }

    public interface IUniformSink
    {
        void SetFloat(FxUniform u, float value);
        void SetVector(FxUniform u, Vector4 value);
        /// <summary>Always called with exactly <see cref="RibbonShape.MaxPoints"/> entries (Unity fixes array sizes on first upload).</summary>
        void SetRibbonPoints(Vector4[] points);
    }

    public struct FrameMapping
    {
        /// <summary>RawImage.uvRect as (x, y, width, height).</summary>
        public Vector4 uvRect;
        /// <summary>Displayed image width / height.</summary>
        public float aspect;
        /// <summary>Displayed image height in screen pixels.</summary>
        public float imageHeightPx;
        public float time;
        public float grainSeed;
    }

    public sealed class CompositeUniformWriter
    {
        public const string RibbonPointsName = "_RibbonPts";

        readonly Vector4[] _points = new Vector4[RibbonShape.MaxPoints];

        static float B(bool b) => b ? 1f : 0f;
        const float Deg2Rad = (float)(Math.PI / 180.0);

        public void WriteFrame(IUniformSink s, in FrameMapping f)
        {
            s.SetVector(FxUniform.FxUVRect, f.uvRect);
            s.SetFloat(FxUniform.FxAspect, Math.Max(f.aspect, 1e-4f));
            s.SetFloat(FxUniform.FxPixelIso, f.imageHeightPx > 0 ? 1f / f.imageHeightPx : 1f / 1080f);
            s.SetFloat(FxUniform.FxTime, f.time);
            s.SetFloat(FxUniform.FxGrainSeed, f.grainSeed);
        }

        /// <summary>How far the effect reaches beyond the shape (ISO): feather, glow, edge noise.</summary>
        public static float BoundsMargin(in EffectValues e, float pixelIso)
            => 0.5f * e.edgeFeather + 4f * e.edgeGlowWidth + e.edgeDistortion * Math.Max(e.edgeFeather, 4f * pixelIso)
               + (e.edgeOutlinePx + 2f) * pixelIso;

        public void WriteMasks(IUniformSink s, RibbonShape ribbon, float ribbonWeight, QuadShape quad, float quadWeight,
                               in EffectValues e, float pixelIso)
        {
            float margin = BoundsMargin(e, pixelIso);

            // ---- ribbon ----
            bool ribbonOn = ribbon != null && ribbon.Valid && ribbonWeight > 0f;
            int n = ribbonOn ? ribbon.Count : 0;
            for (int i = 0; i < _points.Length; i++)
                _points[i] = i < n ? new Vector4(ribbon.Centers[i].X, ribbon.Centers[i].Y, ribbon.Radii[i], 0f) : Vector4.Zero;
            s.SetRibbonPoints(_points);
            s.SetFloat(FxUniform.RibbonCount, n);
            s.SetFloat(FxUniform.RibbonWeight, ribbonOn ? MaskMath.Clamp01(ribbonWeight) : 0f);
            s.SetVector(FxUniform.RibbonBounds, ribbonOn
                ? new Vector4(ribbon.BoundsMin.X - margin, ribbon.BoundsMin.Y - margin, ribbon.BoundsMax.X + margin, ribbon.BoundsMax.Y + margin)
                : new Vector4(1, 1, -1, -1));

            // ---- quad ----
            bool quadOn = quad != null && quad.Valid && quadWeight > 0f;
            if (quadOn)
            {
                Vector2[] v = quad.Vertices;
                s.SetVector(FxUniform.QuadV01, new Vector4(v[0].X, v[0].Y, v[1].X, v[1].Y));
                s.SetVector(FxUniform.QuadV23, new Vector4(v[2].X, v[2].Y, v[3].X, v[3].Y));
                s.SetVector(FxUniform.QuadBounds, new Vector4(quad.BoundsMin.X - margin, quad.BoundsMin.Y - margin, quad.BoundsMax.X + margin, quad.BoundsMax.Y + margin));
            }
            else s.SetVector(FxUniform.QuadBounds, new Vector4(1, 1, -1, -1));
            s.SetFloat(FxUniform.QuadWeight, quadOn ? MaskMath.Clamp01(quadWeight) : 0f);

            bool persp = quadOn && e.perspectiveDots && quad.HasHomography;
            s.SetFloat(FxUniform.QuadPersp, B(persp));
            if (persp)
            {
                float[] h = quad.Homography, hi = quad.InverseHomography;
                s.SetVector(FxUniform.QuadH0, new Vector4(h[0], h[1], h[2], 0));
                s.SetVector(FxUniform.QuadH1, new Vector4(h[3], h[4], h[5], 0));
                s.SetVector(FxUniform.QuadH2, new Vector4(h[6], h[7], h[8], 0));
                s.SetVector(FxUniform.QuadHI0, new Vector4(hi[0], hi[1], hi[2], 0));
                s.SetVector(FxUniform.QuadHI1, new Vector4(hi[3], hi[4], hi[5], 0));
                s.SetVector(FxUniform.QuadHI2, new Vector4(hi[6], hi[7], hi[8], 0));
                s.SetVector(FxUniform.QuadGrid, new Vector4(quad.GridU, quad.GridV, 0, 0));
            }
        }

        public void WriteEffects(IUniformSink s, in EffectValues e)
        {
            s.SetFloat(FxUniform.MaskFeather, Math.Max(e.edgeFeather, 1e-4f));
            s.SetFloat(FxUniform.MaskOpacity, MaskMath.Clamp01(e.maskOpacity));
            s.SetFloat(FxUniform.EdgeGlow, Math.Max(0f, e.edgeGlow));
            s.SetFloat(FxUniform.EdgeGlowWidth, Math.Max(e.edgeGlowWidth, 1e-4f));
            s.SetFloat(FxUniform.EdgeOutline, B(e.edgeOutline));
            s.SetFloat(FxUniform.EdgeOutlinePx, e.edgeOutlinePx);
            s.SetFloat(FxUniform.EdgeDistortion, Math.Max(0f, e.edgeDistortion));
            s.SetFloat(FxUniform.EdgeNoiseScale, e.edgeNoiseScale);
            s.SetFloat(FxUniform.DebugMask, B(e.debugMask));

            s.SetFloat(FxUniform.HTEnabled, B(e.halftoneEnabled));
            s.SetFloat(FxUniform.HTIntensity, MaskMath.Clamp01(e.halftoneIntensity));
            s.SetFloat(FxUniform.HTSpacingPx, Math.Max(2f, e.dotSpacingPx));
            s.SetFloat(FxUniform.HTDotSize, Math.Max(0f, e.dotSize));
            s.SetFloat(FxUniform.HTShape, (float)e.dotShape);
            s.SetFloat(FxUniform.HTAngle, e.gridAngleDeg * Deg2Rad);
            s.SetFloat(FxUniform.HTContrast, e.contrast);
            s.SetFloat(FxUniform.HTThreshold, MaskMath.Clamp(e.threshold, 0f, 0.95f));
            s.SetFloat(FxUniform.HTStyle, (float)e.style);
            s.SetFloat(FxUniform.HTToneMix, MaskMath.Clamp01(e.toneMix));

            s.SetVector(FxUniform.ColInk, e.ink);
            s.SetVector(FxUniform.ColPaper, e.paper);
            s.SetVector(FxUniform.ColBlue, e.primaryBlue);
            s.SetVector(FxUniform.ColCyan, e.cyan);
            s.SetVector(FxUniform.ColLightCyan, e.lightCyan);
            s.SetVector(FxUniform.ColNavy, e.navy);
            s.SetVector(FxUniform.ColDarkBlue, e.darkBlue);
            s.SetVector(FxUniform.ColWhite, e.white);
            s.SetFloat(FxUniform.CyanIntensity, e.cyanIntensity);
            s.SetFloat(FxUniform.BlueIntensity, e.blueIntensity);
            s.SetFloat(FxUniform.Saturation, e.saturation);
            s.SetFloat(FxUniform.Brightness, e.brightness);

            s.SetFloat(FxUniform.RGBEnabled, B(e.rgbSplitEnabled && e.rgbSplitPx > 0f));
            s.SetFloat(FxUniform.RGBDistancePx, e.rgbSplitPx);
            s.SetFloat(FxUniform.RGBAngle, e.rgbSplitAngleDeg * Deg2Rad);
            s.SetFloat(FxUniform.RGBAnimate, B(e.rgbSplitAnimated));

            s.SetFloat(FxUniform.GlitchEnabled, B(e.glitchEnabled));
            s.SetFloat(FxUniform.GlitchIntensity, MaskMath.Clamp01(e.glitchIntensity));
            s.SetFloat(FxUniform.GlitchFrequency, MaskMath.Clamp01(e.glitchFrequency));
            s.SetFloat(FxUniform.GlitchBursts, B(e.glitchBursts));
            s.SetFloat(FxUniform.ScanlineIntensity, MaskMath.Clamp01(e.scanlineIntensity));

            s.SetFloat(FxUniform.GrainEnabled, B(e.grainEnabled));
            s.SetFloat(FxUniform.GrainIntensity, MaskMath.Clamp01(e.grainIntensity));
            s.SetFloat(FxUniform.GrainScalePx, Math.Max(1f, e.grainScalePx));
            s.SetFloat(FxUniform.PrintTexture, MaskMath.Clamp01(e.printTexture));
        }

        /// <summary>Grain seed for time <paramref name="t"/>: changes grainFps times per second, or 0 when static.</summary>
        public static float GrainSeed(in EffectValues e, float t)
            => e.grainAnimated && e.grainFps > 0 ? (float)Math.Floor(t * e.grainFps) % 997f : 0f;
    }
}
