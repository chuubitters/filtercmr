// FILTER CMR — builds the TWO FINGER POINT ribbon between the two index tips. Pure C#.
//
// Shape
//   * Centre line: cubic Bézier from the left tip A to the right tip B.
//     Its tangents leave each fingertip partly along the chord and partly along
//     the finger's own pointing direction (PIP→TIP), so tilting a finger or
//     moving one hand vertically bends the strip naturally.
//     "Curve Direction" adds a bow to one side; "Curve Strength" scales the tangents.
//   * Width: base width, thinner when the hands are far apart (the strip
//     "stretches"), tapered toward the tips, optionally asymmetric, clamped to
//     [Minimum, Maximum].
//   * Organic motion: a slow travelling wave and width breathing, zero at the
//     fingertips, scaled by "Noise Distortion".
// All values are in ISO units (1 = displayed image height).

using System;
using System.Numerics;

namespace FilterCMR.Mask
{
    [Serializable]
    public struct RibbonParams
    {
        public float width;            // base full width
        public float minWidth;
        public float maxWidth;
        public float curveStrength;    // tangent length as a fraction of the tip distance (0..1)
        public float curveDirection;   // -1..1 sideways bow
        public float fingerInfluence;  // 0..1 how much finger direction bends the tangents
        public float taper;            // end width as a fraction of the middle width (0..1)
        public float asymmetry;        // -1..1 wider at the left (+) or right (−) tip
        public float noiseDistortion;  // 0..1 organic wave / breathing amount
        public float flowSpeed;        // wave speed (cycles per second)
        public float lengthResponse;   // 0..1 how much the strip thins when stretched

        public static RibbonParams Default => new RibbonParams
        {
            width = 0.09f, minWidth = 0.025f, maxWidth = 0.2f,
            curveStrength = 0.35f, curveDirection = 0f, fingerInfluence = 0.55f,
            taper = 0.45f, asymmetry = 0f, noiseDistortion = 0.25f, flowSpeed = 0.35f, lengthResponse = 0.3f,
        };
    }

    public static class RibbonMaskGenerator
    {
        const float ReferenceLength = 0.6f; // tip distance (ISO) at which the width equals the base width

        /// <param name="a">Left index tip (ISO).</param>
        /// <param name="b">Right index tip (ISO).</param>
        /// <param name="dirA">Left index pointing direction (unit, ISO) or zero if unknown.</param>
        /// <param name="dirB">Right index pointing direction (unit, ISO) or zero if unknown.</param>
        /// <param name="time">Seconds, drives the organic motion.</param>
        public static void Generate(Vector2 a, Vector2 b, Vector2 dirA, Vector2 dirB, in RibbonParams p, float time, RibbonShape shape)
        {
            Vector2 chord = b - a;
            float len = chord.Length();
            Vector2 u = len > 1e-5f ? chord / len : new Vector2(1, 0);
            Vector2 n = new Vector2(-u.Y, u.X);

            float strength = MaskMath.Clamp(p.curveStrength, 0f, 1f);
            float influence = MaskMath.Clamp(p.fingerInfluence, 0f, 1f);
            Vector2 tA = MaskMath.SafeNormalize(u + dirA * influence, u);
            Vector2 tB = MaskMath.SafeNormalize(u - dirB * influence, u);
            // Keep tangents pointing "forward" so the curve never loops back over itself.
            if (Vector2.Dot(tA, u) < 0.15f) tA = MaskMath.SafeNormalize(tA + u * (0.15f - Vector2.Dot(tA, u)), u);
            if (Vector2.Dot(tB, u) < 0.15f) tB = MaskMath.SafeNormalize(tB + u * (0.15f - Vector2.Dot(tB, u)), u);
            float handle = len * strength * 0.6f;
            Vector2 bow = n * (MaskMath.Clamp(p.curveDirection, -1f, 1f) * len * 0.3f);

            Vector2 c1 = a + tA * handle + bow;
            Vector2 c2 = b - tB * handle + bow;

            float minW = Math.Max(1e-4f, Math.Min(p.minWidth, p.maxWidth));
            float maxW = Math.Max(minW, p.maxWidth);
            float stretch = (float)Math.Pow(ReferenceLength / Math.Max(len, 0.05f), MaskMath.Clamp(p.lengthResponse, 0f, 1f));
            float baseW = p.width * stretch;
            float taper = MaskMath.Clamp(p.taper, 0.05f, 1f);
            float asym = MaskMath.Clamp(p.asymmetry, -1f, 1f) * 0.6f;
            float noise = MaskMath.Clamp(p.noiseDistortion, 0f, 1f);
            float phase = time * p.flowSpeed * 2f * (float)Math.PI;

            int count = RibbonShape.MaxPoints;
            Vector2 bmin = new Vector2(float.MaxValue), bmax = new Vector2(float.MinValue);
            float arc = 0;
            Vector2 prev = a;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float mt = 1f - t;
                Vector2 c = mt * mt * mt * a + 3f * mt * mt * t * c1 + 3f * mt * t * t * c2 + t * t * t * b;

                float envelope = (float)Math.Sin(Math.PI * t);                // 0 at the tips, 1 in the middle
                float profile = MaskMath.Lerp(taper, 1f, (float)Math.Pow(envelope, 0.6));
                float side = 1f + asym * (1f - 2f * t);
                float breathe = 1f + 0.35f * noise * (float)Math.Sin(phase * 0.7f + t * 5.1f) * envelope;
                float w = MaskMath.Clamp(baseW * profile * side * breathe, minW, maxW);

                // Travelling wave perpendicular to the chord, pinned at both fingertips.
                float wave = noise * 0.5f * w * envelope
                             * (float)(Math.Sin(t * 2.0 * Math.PI * 1.25 - phase) * 0.7 + Math.Sin(t * 2.0 * Math.PI * 2.3 + phase * 1.3) * 0.3);
                c += n * wave;

                shape.Centers[i] = c;
                float r = 0.5f * w;
                shape.Radii[i] = r;
                bmin = Vector2.Min(bmin, c - new Vector2(r));
                bmax = Vector2.Max(bmax, c + new Vector2(r));
                if (i > 0) arc += Vector2.Distance(prev, c);
                prev = c;
            }

            shape.Count = count;
            shape.Start = a;
            shape.End = b;
            shape.Control1 = c1;
            shape.Control2 = c2;
            shape.BoundsMin = bmin;
            shape.BoundsMax = bmax;
            shape.Length = arc;
            shape.Valid = true;
        }

        /// <summary>
        /// Signed distance from <paramref name="p"/> to the ribbon (negative inside). CPU reference of the
        /// shader's FilterCMR_RibbonSDF, used by tests and the debug view.
        /// </summary>
        public static float SignedDistance(RibbonShape s, Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 1 < s.Count; i++)
            {
                Vector2 a = s.Centers[i], ab = s.Centers[i + 1] - a;
                float h = MaskMath.Clamp01(Vector2.Dot(p - a, ab) / Math.Max(Vector2.Dot(ab, ab), 1e-12f));
                float r = MaskMath.Lerp(s.Radii[i], s.Radii[i + 1], h);
                d = Math.Min(d, Vector2.Distance(p, a + ab * h) - r);
            }
            return d;
        }
    }
}
