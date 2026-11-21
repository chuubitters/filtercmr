// EditMode tests for the composite shader uniform packing (CompositeUniformWriter) and effect defaults.

using System.Collections.Generic;
using System.Numerics;
using FilterCMR.Mask;
using FilterCMR.Rendering;
using NUnit.Framework;

namespace FilterCMR.Tests
{
    [TestFixture]
    public class RenderingTests
    {
        sealed class Sink : IUniformSink
        {
            public readonly Dictionary<FxUniform, float> F = new Dictionary<FxUniform, float>();
            public readonly Dictionary<FxUniform, Vector4> V = new Dictionary<FxUniform, Vector4>();
            public Vector4[] Points;
            public int PointUploads;
            public void SetFloat(FxUniform u, float v) => F[u] = v;
            public void SetVector(FxUniform u, Vector4 v) => V[u] = v;
            public void SetRibbonPoints(Vector4[] p) { Points = (Vector4[])p.Clone(); PointUploads++; }
        }

        static RibbonShape Ribbon()
        {
            var r = new RibbonShape();
            RibbonMaskGenerator.Generate(new Vector2(0.5f, 0.5f), new Vector2(1.2f, 0.45f), Vector2.Zero, Vector2.Zero, RibbonParams.Default, 0f, r);
            return r;
        }

        static QuadShape Quad()
        {
            var q = new QuadShape();
            QuadrilateralMaskGenerator.Generate(new Vector2(0.5f, 0.3f), new Vector2(0.45f, 0.7f), new Vector2(1.2f, 0.25f), new Vector2(1.25f, 0.72f), q);
            return q;
        }

        [Test]
        public void EveryUniformIsWrittenEachFrame()
        {
            var s = new Sink();
            var w = new CompositeUniformWriter();
            var e = EffectValues.Default;
            w.WriteFrame(s, new FrameMapping { uvRect = new Vector4(1, 0, -1, 1), aspect = 16f / 9f, imageHeightPx = 720, time = 3f });
            w.WriteMasks(s, Ribbon(), 1f, Quad(), 1f, e, 1f / 720f);
            w.WriteEffects(s, e);
            for (int i = 0; i < (int)FxUniform.Count; i++)
            {
                var u = (FxUniform)i;
                Assert.IsTrue(s.F.ContainsKey(u) || s.V.ContainsKey(u), "not written: " + u);
            }
            Assert.AreEqual(RibbonShape.MaxPoints, s.Points.Length);
            Assert.AreEqual(1f / 720f, s.F[FxUniform.FxPixelIso], 1e-9);
            Assert.AreEqual(-1f, s.V[FxUniform.FxUVRect].Z, 0);
        }

        [Test]
        public void HiddenOrInvalidShapesDisableTheirMask()
        {
            var s = new Sink();
            var w = new CompositeUniformWriter();
            var e = EffectValues.Default;
            w.WriteMasks(s, Ribbon(), 0f, Quad(), 0f, e, 1f / 720f);
            Assert.AreEqual(0f, s.F[FxUniform.RibbonWeight]);
            Assert.AreEqual(0f, s.F[FxUniform.QuadWeight]);
            Assert.AreEqual(0f, s.F[FxUniform.RibbonCount]);
            Assert.AreEqual(0f, s.F[FxUniform.QuadPersp]);
            Assert.Greater(s.V[FxUniform.RibbonBounds].X, s.V[FxUniform.RibbonBounds].Z, "empty bounds");

            var invalid = new QuadShape();
            QuadrilateralMaskGenerator.Generate(new Vector2(0.1f, 0.5f), new Vector2(0.2f, 0.5f), new Vector2(0.3f, 0.5f), new Vector2(0.4f, 0.5f), invalid);
            w.WriteMasks(s, null, 1f, invalid, 1f, e, 1f / 720f);
            Assert.AreEqual(0f, s.F[FxUniform.QuadWeight], "collinear tips -> no quad");
            Assert.AreEqual(0f, s.F[FxUniform.RibbonWeight], "null ribbon -> no ribbon");
        }

        [Test]
        public void BoundsContainShapePlusFeatherAndGlow()
        {
            var s = new Sink();
            var w = new CompositeUniformWriter();
            var e = EffectValues.Default;
            RibbonShape r = Ribbon();
            w.WriteMasks(s, r, 1f, null, 0f, e, 1f / 720f);
            Vector4 b = s.V[FxUniform.RibbonBounds];
            float margin = CompositeUniformWriter.BoundsMargin(e, 1f / 720f);
            Assert.Greater(margin, 0.5f * e.edgeFeather + 3f * e.edgeGlowWidth);
            Assert.AreEqual(r.BoundsMin.X - margin, b.X, 1e-6);
            Assert.AreEqual(r.BoundsMax.Y + margin, b.W, 1e-6);
            for (int i = 0; i < r.Count; i++)
            {
                Vector4 p = s.Points[i];
                Assert.AreEqual(r.Centers[i].X, p.X, 0);
                Assert.AreEqual(r.Radii[i], p.Z, 0);
            }
        }

        [Test]
        public void PerspectiveGridOnlyWithValidHomographyAndOption()
        {
            var s = new Sink();
            var w = new CompositeUniformWriter();
            var e = EffectValues.Default;
            w.WriteMasks(s, null, 0f, Quad(), 1f, e, 1f / 720f);
            Assert.AreEqual(1f, s.F[FxUniform.QuadPersp]);
            Assert.Greater(s.V[FxUniform.QuadGrid].X, 0f);
            e.perspectiveDots = false;
            w.WriteMasks(s, null, 0f, Quad(), 1f, e, 1f / 720f);
            Assert.AreEqual(0f, s.F[FxUniform.QuadPersp]);
        }

        [Test]
        public void EffectTogglesAndClampsReachTheShader()
        {
            var s = new Sink();
            var w = new CompositeUniformWriter();
            var e = EffectValues.Default;
            e.halftoneEnabled = false; e.grainEnabled = false; e.glitchEnabled = false; e.rgbSplitEnabled = true; e.rgbSplitPx = 0f;
            e.halftoneIntensity = 3f; e.threshold = 2f; e.dotSpacingPx = 0.5f; e.dotShape = DotShape.Irregular;
            w.WriteEffects(s, e);
            Assert.AreEqual(0f, s.F[FxUniform.HTEnabled]);
            Assert.AreEqual(0f, s.F[FxUniform.GrainEnabled]);
            Assert.AreEqual(0f, s.F[FxUniform.GlitchEnabled]);
            Assert.AreEqual(0f, s.F[FxUniform.RGBEnabled], "zero distance disables the split");
            Assert.AreEqual(1f, s.F[FxUniform.HTIntensity]);
            Assert.AreEqual(0.95f, s.F[FxUniform.HTThreshold], 1e-6);
            Assert.AreEqual(2f, s.F[FxUniform.HTSpacingPx]);
            Assert.AreEqual(3f, s.F[FxUniform.HTShape]);
        }

        [Test]
        public void GrainSeedIsStaticOrSteppedAtGrainFps()
        {
            var e = EffectValues.Default;
            e.grainFps = 24f;
            Assert.AreEqual(CompositeUniformWriter.GrainSeed(e, 1.00f), CompositeUniformWriter.GrainSeed(e, 1.01f), "stable within a grain frame");
            Assert.AreNotEqual(CompositeUniformWriter.GrainSeed(e, 1.00f), CompositeUniformWriter.GrainSeed(e, 1.05f));
            e.grainAnimated = false;
            Assert.AreEqual(0f, CompositeUniformWriter.GrainSeed(e, 12.3f));
        }

        [Test]
        public void PalettesStayInTheBlueFamilyAndDefaultsMatchArtDirection()
        {
            var d = EffectValues.Default;
            Assert.AreEqual(EffectValues.Hex(0x155EEF), d.primaryBlue);
            Assert.AreEqual(EffectValues.Hex(0x35DDF2), d.cyan);
            Assert.AreEqual(EffectValues.Hex(0xA5F3FC), d.lightCyan);
            Assert.AreEqual(EffectValues.Hex(0x07152F), d.navy);
            Assert.AreEqual(EffectValues.Hex(0x102A56), d.darkBlue);
            Assert.AreEqual(EffectValues.Hex(0xF7FBFF), d.white);
            for (int p = 0; p < 4; p++)
            {
                var v = d;
                EffectValues.ApplyPalette(ref v, (PalettePreset)p);
                Assert.GreaterOrEqual(v.ink.Z, v.ink.X, "ink is blue-dominant: " + (PalettePreset)p);
                Assert.GreaterOrEqual(v.paper.Z, v.paper.X, "paper is blue-dominant: " + (PalettePreset)p);
            }
            Assert.Less(d.glitchIntensity, 0.5f, "tasteful default glitch");
            Assert.Less(d.rgbSplitPx, 5f, "tasteful default RGB split");
            Assert.Greater(d.fadeOutSeconds, 0.45f, "fade-out longer than release + confirm (flash-free switching)");
        }
    }
}
