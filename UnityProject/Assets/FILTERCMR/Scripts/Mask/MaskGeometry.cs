// FILTER CMR — mask shape data shared by the generators, the composite shader
// uploader and the debug visualiser. Pure C#; all buffers are pre-allocated.
// Coordinates are ISO space (see MaskCoordinateMapper).

using System;
using System.Numerics;

namespace FilterCMR.Mask
{
    /// <summary>What the user selected (keys 0/1/2). Auto = gestures decide.</summary>
    public enum MaskMode
    {
        /// <summary>Point gesture → ribbon, peace gesture → quad.</summary>
        Auto = 0,
        /// <summary>Ribbon between the index tips whenever both hands are visible (any pose).</summary>
        Ribbon,
        /// <summary>Quad from the four frame tips (thumb + index by default) whenever both hands are visible (any pose).</summary>
        Quad,
        /// <summary>No mask: normal webcam.</summary>
        Off,
    }

    public enum MaskShapeType { None = 0, Ribbon, Quad }

    /// <summary>Variable-width strip sampled along a curve: a chain of round segments.</summary>
    public sealed class RibbonShape
    {
        public const int Segments = 24;
        public const int MaxPoints = Segments + 1;

        /// <summary>Centre line samples (ISO).</summary>
        public readonly Vector2[] Centers = new Vector2[MaxPoints];
        /// <summary>Half-width at each sample (ISO).</summary>
        public readonly float[] Radii = new float[MaxPoints];
        public int Count;

        /// <summary>Fingertip endpoints (ISO) and the cubic Bézier controls used.</summary>
        public Vector2 Start, End, Control1, Control2;
        public Vector2 BoundsMin, BoundsMax;
        /// <summary>Arc length of the centre line (ISO).</summary>
        public float Length;
        public bool Valid;

        public void Clear()
        {
            Count = 0;
            Valid = false;
            Length = 0;
        }
    }

    /// <summary>Convex quadrilateral (or triangle) through the four fingertips.</summary>
    public sealed class QuadShape
    {
        /// <summary>Inputs in identity order: 0 = left index, 1 = left thumb (or middle), 2 = right index, 3 = right thumb (or middle).</summary>
        public readonly Vector2[] Sources = new Vector2[4];
        /// <summary>Polygon vertices in consistent winding (positive signed area in ISO, y down = clockwise on screen).
        /// When the hull is a triangle, vertex 3 repeats vertex 2 so consumers can always read four.</summary>
        public readonly Vector2[] Vertices = new Vector2[4];
        /// <summary>For each vertex, which source fingertip it came from.</summary>
        public readonly int[] Order = new int[4];
        public int VertexCount;
        public float Area;
        public Vector2 BoundsMin, BoundsMax;
        public bool Valid;

        /// <summary>Projective map from the unit square (u,v) to the quad, row-major 3×3. Valid when HasHomography.</summary>
        public readonly float[] Homography = new float[9];
        /// <summary>Inverse map (quad → unit square).</summary>
        public readonly float[] InverseHomography = new float[9];
        public bool HasHomography;
        /// <summary>Average lengths of the u and v edges (ISO), used to size the perspective dot grid.</summary>
        public float GridU, GridV;

        public void Clear()
        {
            VertexCount = 0;
            Valid = false;
            HasHomography = false;
            Area = 0;
        }
    }

    public static class MaskMath
    {
        public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Shoelace signed area; positive = vertex order is counter-clockwise in a y-up frame.</summary>
        public static float SignedArea(Vector2[] v, int n)
        {
            float a = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = v[i], q = v[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return 0.5f * a;
        }

        /// <summary>True if segments ab and cd properly intersect (shared endpoints don't count).</summary>
        public static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>True if the closed polygon has no two non-adjacent edges crossing.</summary>
        public static bool IsSimple(Vector2[] v, int n)
        {
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i || (j + 1) % n == i || (i + 1) % n == j) continue;
                    if (SegmentsIntersect(v[i], v[(i + 1) % n], v[j], v[(j + 1) % n])) return false;
                }
            return true;
        }

        public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback)
        {
            float l = v.Length();
            return l > 1e-6f ? v / l : fallback;
        }

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float SmoothStep01(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Apply a row-major 3×3 homography to a point.</summary>
        public static Vector2 Project(float[] h, Vector2 p)
        {
            float w = h[6] * p.X + h[7] * p.Y + h[8];
            if (Math.Abs(w) < 1e-12f) w = 1e-12f;
            return new Vector2((h[0] * p.X + h[1] * p.Y + h[2]) / w, (h[3] * p.X + h[4] * p.Y + h[5]) / w);
        }
    }
}
