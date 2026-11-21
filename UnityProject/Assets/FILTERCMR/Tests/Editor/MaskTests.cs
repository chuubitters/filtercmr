// EditMode tests for the interactive mask core (coordinate mapping, ribbon, quad, smoothing, transitions).

using System;
using System.Numerics;
using FilterCMR.Gestures;
using FilterCMR.Mask;
using FilterCMR.Networking;
using NUnit.Framework;

namespace FilterCMR.Tests
{
    [TestFixture]
    public class MaskTests
    {
        const float Aspect = 16f / 9f;
        static Vector2 V(float x, float y) => new Vector2(x, y);
        static Vector2 Iso(float x, float y) => MaskCoordinateMapper.ImageToIso(V(x, y), Aspect);

        // ------------------------------------------------------------------ mapping
        [Test]
        public void UvRoundTripForAllDisplayOrientations()
        {
            float[][] rects = { new[] { 0f, 0f, 1f, 1f }, new[] { 1f, 0f, -1f, 1f }, new[] { 0f, 1f, 1f, -1f }, new[] { 1f, 1f, -1f, -1f } };
            foreach (var r in rects)
                for (float x = 0; x <= 1; x += 0.25f)
                    for (float y = 0; y <= 1; y += 0.25f)
                    {
                        Vector2 uv = MaskCoordinateMapper.ImageToUV(V(x, y), r[0], r[1], r[2], r[3]);
                        Vector2 back = MaskCoordinateMapper.UVToImage(uv, r[0], r[1], r[2], r[3]);
                        Assert.AreEqual(x, back.X, 1e-6);
                        Assert.AreEqual(y, back.Y, 1e-6);
                    }
        }

        [Test]
        public void MirroredDisplaySamplesMirroredTexel()
        {
            // Python mirrored x: a tip at image x=0.25 (left of screen) is raw camera x=0.75.
            Vector2 uv = MaskCoordinateMapper.ImageToUV(V(0.25f, 0.2f), 1f, 0f, -1f, 1f);
            Assert.AreEqual(0.75f, uv.X, 1e-6);
            Assert.AreEqual(0.8f, uv.Y, 1e-6); // texture v is bottom-up
        }

        [Test]
        public void LocalMappingMatchesLandmarkOverlay()
        {
            // HandGraphicBase.ToLocal: (xMin + x*w, yMax - y*h)
            Vector2 p = MaskCoordinateMapper.ImageToLocal(V(0.3f, 0.7f), -960, -540, 1920, 1080);
            Assert.AreEqual(-960 + 0.3f * 1920, p.X, 1e-3);
            Assert.AreEqual(540 - 0.7f * 1080, p.Y, 1e-3);
            Vector2 iso = MaskCoordinateMapper.ImageToIso(V(0.5f, 0.5f), Aspect);
            Assert.AreEqual(0.5f * Aspect, iso.X, 1e-6);
            Assert.AreEqual(0.5f, MaskCoordinateMapper.IsoToImage(iso, Aspect).X, 1e-6);
            Assert.AreEqual(9f, MaskCoordinateMapper.IsoToPixels(MaskCoordinateMapper.PixelsToIso(9f, 720f), 720f), 1e-4);
        }

        // ------------------------------------------------------------------ ribbon
        static RibbonShape Ribbon(Vector2 a, Vector2 b, Vector2 da, Vector2 db, RibbonParams p, float time = 0f)
        {
            var s = new RibbonShape();
            RibbonMaskGenerator.Generate(a, b, da, db, p, time, s);
            return s;
        }

        static RibbonParams Calm()
        {
            var p = RibbonParams.Default;
            p.noiseDistortion = 0f;
            return p;
        }

        [Test]
        public void RibbonConnectsBothFingertipsExactly()
        {
            Vector2 a = Iso(0.3f, 0.5f), b = Iso(0.7f, 0.45f);
            var s = Ribbon(a, b, V(0, -1), V(0, -1), RibbonParams.Default, 3.7f);
            Assert.IsTrue(s.Valid);
            Assert.AreEqual(RibbonShape.MaxPoints, s.Count);
            Assert.Less(Vector2.Distance(s.Centers[0], a), 1e-5);
            Assert.Less(Vector2.Distance(s.Centers[s.Count - 1], b), 1e-5);
            Assert.Less(RibbonMaskGenerator.SignedDistance(s, a), 0, "fingertip inside the ribbon");
            Assert.Less(RibbonMaskGenerator.SignedDistance(s, b), 0);
        }

        [Test]
        public void RibbonWidthRespectsMinMaxAndTapers()
        {
            var p = Calm();
            var s = Ribbon(Iso(0.2f, 0.5f), Iso(0.8f, 0.5f), Vector2.Zero, Vector2.Zero, p);
            for (int i = 0; i < s.Count; i++)
            {
                Assert.GreaterOrEqual(2 * s.Radii[i], p.minWidth - 1e-6);
                Assert.LessOrEqual(2 * s.Radii[i], p.maxWidth + 1e-6);
            }
            Assert.Less(s.Radii[0], s.Radii[s.Count / 2], "ends narrower than the middle");
            p.minWidth = p.maxWidth = 0.05f;
            s = Ribbon(Iso(0.2f, 0.5f), Iso(0.8f, 0.5f), Vector2.Zero, Vector2.Zero, p);
            for (int i = 0; i < s.Count; i++) Assert.AreEqual(0.025f, s.Radii[i], 1e-6);
        }

        [Test]
        public void StraightFingersAndNoBowGiveStraightStrip()
        {
            var s = Ribbon(Iso(0.2f, 0.5f), Iso(0.8f, 0.5f), Vector2.Zero, Vector2.Zero, Calm());
            for (int i = 0; i < s.Count; i++) Assert.AreEqual(0.5f, s.Centers[i].Y, 1e-5);
        }

        [Test]
        public void RaisedFingersArchTheRibbonAndBowDirectionFlipsSide()
        {
            var p = Calm();
            Vector2 a = Iso(0.3f, 0.6f), b = Iso(0.7f, 0.6f), up = V(0, -1);
            var arch = Ribbon(a, b, up, up, p);
            Assert.Less(arch.Centers[arch.Count / 2].Y, 0.6f - 0.01f, "index fingers pointing up lift the middle");

            p.fingerInfluence = 0f;
            p.curveDirection = 1f;
            float yPlus = Ribbon(a, b, Vector2.Zero, Vector2.Zero, p).Centers[12].Y;
            p.curveDirection = -1f;
            float yMinus = Ribbon(a, b, Vector2.Zero, Vector2.Zero, p).Centers[12].Y;
            Assert.IsTrue((yPlus - 0.6f) * (yMinus - 0.6f) < 0, "opposite curve directions bow to opposite sides");
        }

        [Test]
        public void MovingOneHandVerticallyBendsSmoothly()
        {
            var p = Calm();
            Vector2 up = V(0, -1);
            float prevMid = float.NaN;
            for (float y = 0.3f; y <= 0.7f; y += 0.02f)
            {
                var s = Ribbon(Iso(0.3f, 0.5f), Iso(0.7f, y), up, up, p);
                float mid = s.Centers[12].Y;
                if (!float.IsNaN(prevMid)) Assert.Less(Math.Abs(mid - prevMid), 0.03f, "no sudden jumps");
                prevMid = mid;
            }
        }

        [Test]
        public void RibbonNeverLoopsBackForAnyFingerDirection()
        {
            var rng = new Random(7);
            var p = RibbonParams.Default;
            for (int k = 0; k < 2000; k++)
            {
                Vector2 a = Iso((float)rng.NextDouble(), (float)rng.NextDouble());
                Vector2 b = Iso((float)rng.NextDouble(), (float)rng.NextDouble());
                double t1 = rng.NextDouble() * 6.283, t2 = rng.NextDouble() * 6.283;
                p.curveStrength = (float)rng.NextDouble();
                p.fingerInfluence = (float)rng.NextDouble();
                var s = Ribbon(a, b, V((float)Math.Cos(t1), (float)Math.Sin(t1)), V((float)Math.Cos(t2), (float)Math.Sin(t2)), p, (float)rng.NextDouble() * 10);
                Vector2 u = MaskMath.SafeNormalize(b - a, V(1, 0));
                for (int i = 0; i < s.Count; i++)
                {
                    Assert.IsFalse(float.IsNaN(s.Centers[i].X) || float.IsNaN(s.Radii[i]));
                    Assert.IsTrue(s.Centers[i].X >= s.BoundsMin.X - 1e-5 && s.Centers[i].X <= s.BoundsMax.X + 1e-5);
                }
                if (Vector2.Distance(a, b) < 0.05f) continue;
                for (int i = 1; i < s.Count; i++)
                    Assert.Greater(Vector2.Dot(s.Centers[i] - s.Centers[0], u) - Vector2.Dot(s.Centers[i - 1] - s.Centers[0], u), -0.02f,
                        "centre line must progress from A to B");
            }
        }

        [Test]
        public void CoincidentTipsDoNotProduceNaN()
        {
            var s = Ribbon(Iso(0.5f, 0.5f), Iso(0.5f, 0.5f), V(0, -1), V(0, -1), RibbonParams.Default, 1f);
            Assert.IsTrue(s.Valid);
            for (int i = 0; i < s.Count; i++) Assert.IsFalse(float.IsNaN(s.Centers[i].X + s.Centers[i].Y + s.Radii[i]));
        }

        [Test]
        public void StretchingThinsTheRibbon()
        {
            var p = Calm();
            float near = Ribbon(Iso(0.4f, 0.5f), Iso(0.6f, 0.5f), Vector2.Zero, Vector2.Zero, p).Radii[12];
            float far = Ribbon(Iso(0.05f, 0.5f), Iso(0.95f, 0.5f), Vector2.Zero, Vector2.Zero, p).Radii[12];
            Assert.Greater(near, far);
        }

        [Test]
        public void RibbonTranslatesWithTheHands()
        {
            var p = RibbonParams.Default;
            var s1 = Ribbon(Iso(0.3f, 0.5f), Iso(0.6f, 0.4f), V(0, -1), V(0.2f, -1), p, 2f);
            Vector2 off = V(0.11f, -0.07f);
            var s2 = Ribbon(Iso(0.3f, 0.5f) + off, Iso(0.6f, 0.4f) + off, V(0, -1), V(0.2f, -1), p, 2f);
            for (int i = 0; i < s1.Count; i++) Assert.Less(Vector2.Distance(s1.Centers[i] + off, s2.Centers[i]), 1e-5);
        }

        // -------------------------------------------------------------------- quad
        static QuadShape Quad(Vector2 li, Vector2 lm, Vector2 ri, Vector2 rm)
        {
            var q = new QuadShape();
            QuadrilateralMaskGenerator.Generate(li, lm, ri, rm, q);
            return q;
        }

        [Test]
        public void BowTieInputBecomesSimpleConvexQuad()
        {
            // Input order crosses itself: TL, BR, TR, BL.
            var q = Quad(Iso(0.3f, 0.3f), Iso(0.7f, 0.7f), Iso(0.7f, 0.3f), Iso(0.3f, 0.7f));
            Assert.IsTrue(q.Valid);
            Assert.AreEqual(4, q.VertexCount);
            Assert.IsTrue(MaskMath.IsSimple(q.Vertices, 4));
            Assert.Greater(MaskMath.SignedArea(q.Vertices, 4), 0);
            Assert.AreEqual(0, q.Order[0], "starts at the left index tip");
            Assert.Less(QuadrilateralMaskGenerator.SignedDistance(q, Iso(0.5f, 0.5f)), 0);
            Assert.Greater(QuadrilateralMaskGenerator.SignedDistance(q, Iso(0.9f, 0.5f)), 0);
        }

        [Test]
        public void RandomTipsAlwaysGiveSimpleConsistentlyWoundPolygonContainingAllTips()
        {
            var rng = new Random(3);
            int quads = 0, triangles = 0, invalid = 0;
            for (int k = 0; k < 20000; k++)
            {
                var pts = new Vector2[4];
                for (int i = 0; i < 4; i++) pts[i] = Iso((float)rng.NextDouble(), (float)rng.NextDouble());
                var q = Quad(pts[0], pts[1], pts[2], pts[3]);
                if (!q.Valid) { invalid++; continue; }
                if (q.VertexCount == 4) quads++; else triangles++;
                Assert.IsTrue(MaskMath.IsSimple(q.Vertices, q.VertexCount), "self-intersecting polygon");
                Assert.Greater(MaskMath.SignedArea(q.Vertices, q.VertexCount), 0, "inverted winding");
                for (int i = 0; i < 4; i++)
                    Assert.LessOrEqual(QuadrilateralMaskGenerator.SignedDistance(q, pts[i]), 1e-4, "tip outside polygon");
            }
            Assert.Greater(quads, 10000);
            Assert.Greater(triangles, 1000);
            Assert.Less(invalid, 200);
        }

        [Test]
        public void InteriorTipGivesTriangleAndCollinearIsInvalid()
        {
            var t = Quad(Iso(0.2f, 0.2f), Iso(0.5f, 0.45f), Iso(0.8f, 0.2f), Iso(0.5f, 0.8f));
            Assert.IsTrue(t.Valid);
            Assert.AreEqual(3, t.VertexCount);
            Assert.AreEqual(t.Vertices[2], t.Vertices[3]);
            Assert.IsFalse(t.HasHomography);

            var line = Quad(Iso(0.1f, 0.5f), Iso(0.3f, 0.5f), Iso(0.6f, 0.5f), Iso(0.9f, 0.5f));
            Assert.IsFalse(line.Valid);
            var tiny = Quad(Iso(0.5f, 0.5f), Iso(0.501f, 0.5f), Iso(0.5f, 0.501f), Iso(0.501f, 0.501f));
            Assert.IsFalse(tiny.Valid);
        }

        [Test]
        public void HomographyMapsUnitSquareOntoPerspectiveQuad()
        {
            var q = Quad(Iso(0.25f, 0.2f), Iso(0.3f, 0.85f), Iso(0.8f, 0.35f), Iso(0.7f, 0.7f));
            Assert.IsTrue(q.HasHomography);
            Vector2[] corners = { V(0, 0), V(1, 0), V(1, 1), V(0, 1) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = MaskMath.Project(q.Homography, corners[i]);
                Assert.Less(Vector2.Distance(p, q.Vertices[i]), 1e-4, "corner " + i);
                Vector2 back = MaskMath.Project(q.InverseHomography, q.Vertices[i]);
                Assert.Less(Vector2.Distance(back, corners[i]), 1e-4);
            }
            Assert.Less(QuadrilateralMaskGenerator.SignedDistance(q, MaskMath.Project(q.Homography, V(0.5f, 0.5f))), 0);
            Assert.Greater(q.GridU, 0.1f);
            Assert.Greater(q.GridV, 0.1f);
        }

        [Test]
        public void QuadSdfIsExactForAxisAlignedSquare()
        {
            var q = Quad(V(0, 0), V(1, 0), V(1, 1), V(0, 1));
            Assert.AreEqual(-0.5f, QuadrilateralMaskGenerator.SignedDistance(q, V(0.5f, 0.5f)), 1e-5);
            Assert.AreEqual(0.25f, QuadrilateralMaskGenerator.SignedDistance(q, V(1.25f, 0.5f)), 1e-5);
            Assert.AreEqual(-0.1f, QuadrilateralMaskGenerator.SignedDistance(q, V(0.1f, 0.5f)), 1e-5);
        }

        // --------------------------------------------------------------- smoothing
        [Test]
        public void SmootherSnapsThenConvergesAndLimitsJumps()
        {
            var s = new PointSmoother();
            s.Update(V(0.2f, 0.2f), 1 / 60f, 18f, 6f);
            Assert.AreEqual(0.2f, s.Value.X, 1e-6);
            for (int i = 0; i < 60; i++) s.Update(V(0.4f, 0.2f), 1 / 60f, 18f, 6f);
            Assert.AreEqual(0.4f, s.Value.X, 1e-3);
            // a one-frame tracking glitch across the screen moves at most maxSpeed*dt
            Vector2 before = s.Value;
            s.Update(V(1.7f, 0.9f), 1 / 60f, 1000f, 6f);
            Assert.LessOrEqual(Vector2.Distance(before, s.Value), 6f / 60f + 1e-5);
        }

        [Test]
        public void FaderTimingAndEasing()
        {
            var f = new MaskFader();
            int frames = 0;
            while (f.Value < 1f && frames < 1000) { f.Update(1f, 1 / 60f, 0.25f, 0.5f); frames++; }
            Assert.AreEqual(15, frames, 1);
            frames = 0;
            while (f.Value > 0f && frames < 1000) { f.Update(0f, 1 / 60f, 0.25f, 0.5f); frames++; }
            Assert.AreEqual(30, frames, 1);
            f.Value = 0.5f;
            Assert.AreEqual(0.5f, f.Eased, 1e-6);
            f.Value = 0.1f;
            Assert.Less(f.Eased, 0.1f);
        }

        // ------------------------------------------------------------- transitions
        [Test]
        public void TargetsFollowModeAndGesture()
        {
            MaskTransitions.Targets(MaskMode.Auto, GestureType.TwoFingerPoint, true, out float r, out float q);
            Assert.AreEqual(1f, r); Assert.AreEqual(0f, q);
            MaskTransitions.Targets(MaskMode.Auto, GestureType.TwoFingerFrame, true, out r, out q);
            Assert.AreEqual(0f, r); Assert.AreEqual(1f, q);
            MaskTransitions.Targets(MaskMode.Auto, GestureType.None, true, out r, out q);
            Assert.AreEqual(0f, r + q);
            MaskTransitions.Targets(MaskMode.Ribbon, GestureType.None, true, out r, out q);
            Assert.AreEqual(1f, r);
            MaskTransitions.Targets(MaskMode.Ribbon, GestureType.TwoFingerFrame, true, out r, out q);
            Assert.AreEqual(1f, r); Assert.AreEqual(0f, q);
            MaskTransitions.Targets(MaskMode.Quad, GestureType.None, false, out r, out q);
            Assert.AreEqual(0f, q, "forced modes still need both hands");
            MaskTransitions.Targets(MaskMode.Off, GestureType.TwoFingerPoint, true, out r, out q);
            Assert.AreEqual(0f, r + q);
        }

        [Test]
        public void PointToPeaceCrossFadesWithoutFlash()
        {
            // Full pipeline: Python poses -> gesture state machine -> mask targets -> faders (default timings).
            var g = new GestureStateMachine { ConfirmSeconds = 0.2f, ReleaseSeconds = 0.25f };
            var ribbon = new MaskFader();
            var quad = new MaskFader();
            const float dt = 1f / 60f;
            float minCombinedAfterShown = 1f;
            bool shown = false;
            for (int frame = 0; frame < 180; frame++) // 3 s: 1 s point, then peace
            {
                HandPose pose = frame < 60 ? HandPose.Point : HandPose.Peace;
                g.Update(HandStatus.Detected, pose, HandStatus.Detected, pose, dt);
                MaskTransitions.Targets(MaskMode.Auto, g.Current, true, out float rt, out float qt);
                ribbon.Update(rt, dt, 0.25f, 0.5f);
                quad.Update(qt, dt, 0.25f, 0.5f);
                float combined = Math.Max(ribbon.Eased, quad.Eased);
                if (ribbon.Value >= 1f) shown = true;
                if (shown) minCombinedAfterShown = Math.Min(minCombinedAfterShown, combined);
            }
            Assert.IsTrue(shown);
            Assert.AreEqual(1f, quad.Value, 1e-6, "quad fully visible at the end");
            Assert.AreEqual(0f, ribbon.Value, 1e-6, "ribbon gone at the end");
            Assert.Greater(minCombinedAfterShown, 0.15f, "mask must not vanish during the switch (no flash)");
        }

        [Test]
        public void RemovingHandsFadesOutCompletely()
        {
            var g = new GestureStateMachine { ConfirmSeconds = 0.2f, ReleaseSeconds = 0.25f };
            var ribbon = new MaskFader();
            const float dt = 1f / 60f;
            for (int i = 0; i < 60; i++)
            {
                g.Update(HandStatus.Detected, HandPose.Point, HandStatus.Detected, HandPose.Point, dt);
                MaskTransitions.Targets(MaskMode.Auto, g.Current, true, out float rt, out _);
                ribbon.Update(rt, dt, 0.25f, 0.5f);
            }
            Assert.AreEqual(1f, ribbon.Value, 1e-6);
            int frames = 0;
            while (ribbon.Value > 0 && frames < 600)
            {
                g.Update(HandStatus.Lost, HandPose.Unknown, HandStatus.Lost, HandPose.Unknown, dt);
                MaskTransitions.Targets(MaskMode.Auto, g.Current, false, out float rt, out _);
                ribbon.Update(rt, dt, 0.25f, 0.5f);
                frames++;
            }
            Assert.AreEqual(0f, ribbon.Value);
            Assert.Less(frames, 60, "gone within 1 s (0.25 s grace + 0.5 s fade)");
        }
    }
}
