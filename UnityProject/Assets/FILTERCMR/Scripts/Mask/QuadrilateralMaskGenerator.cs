// FILTER CMR — builds the TWO FINGER FRAME polygon from four fingertips. Pure C#.
//
// Robust ordering
//   The four tips (left index, left middle, right index, right middle) arrive in
//   no particular geometric order and can cross each other as hands rotate.
//   Connecting them in input order can self-intersect ("bow-tie"). Instead we
//   take their CONVEX HULL (Andrew's monotone chain): the result is always a
//   simple, convex polygon with a consistent winding — a quad, or a triangle
//   when one tip is inside the others. It can never self-intersect or invert.
//   The shape changes continuously when a tip crosses a hull edge.
//
// Perspective
//   A homography maps the unit square onto the quad, so the halftone grid can
//   follow the quad's perspective. The hull is rotated so it starts at the vertex
//   from the lowest source index (normally the left index tip), which keeps the
//   grid orientation tied to finger identity and stops it flipping between frames.

using System;
using System.Numerics;

namespace FilterCMR.Mask
{
    public static class QuadrilateralMaskGenerator
    {
        /// <summary>Polygons smaller than this (ISO², ~0.6% of the image height squared) are treated as invalid.</summary>
        public const float MinArea = 4e-4f;

        static readonly int[] s_idx = new int[4];
        static readonly int[] s_hull = new int[8];

        /// <param name="tips">Four tips in identity order (ISO): L index, L middle, R index, R middle.</param>
        public static void Generate(Vector2 leftIndex, Vector2 leftMiddle, Vector2 rightIndex, Vector2 rightMiddle, QuadShape q)
        {
            q.Sources[0] = leftIndex;
            q.Sources[1] = leftMiddle;
            q.Sources[2] = rightIndex;
            q.Sources[3] = rightMiddle;

            int h = ConvexHull(q.Sources, s_hull);
            q.HasHomography = false;
            if (h < 3)
            {
                q.VertexCount = 0;
                q.Valid = false;
                q.Area = 0;
                return;
            }

            // Rotate so the hull starts at the lowest source index (identity-stable start vertex).
            int start = 0;
            for (int i = 1; i < h; i++) if (s_hull[i] < s_hull[start]) start = i;
            for (int i = 0; i < h; i++) q.Order[i] = s_hull[(start + i) % h];
            for (int i = 0; i < h; i++) q.Vertices[i] = q.Sources[q.Order[i]];
            if (h == 3) { q.Vertices[3] = q.Vertices[2]; q.Order[3] = q.Order[2]; }
            q.VertexCount = h;

            q.Area = Math.Abs(MaskMath.SignedArea(q.Vertices, h));
            q.Valid = q.Area >= MinArea;

            Vector2 bmin = q.Vertices[0], bmax = q.Vertices[0];
            for (int i = 1; i < h; i++) { bmin = Vector2.Min(bmin, q.Vertices[i]); bmax = Vector2.Max(bmax, q.Vertices[i]); }
            q.BoundsMin = bmin;
            q.BoundsMax = bmax;

            if (h == 4 && q.Valid)
            {
                q.HasHomography = SquareToQuad(q.Vertices, q.Homography) && Invert3x3(q.Homography, q.InverseHomography);
                q.GridU = 0.5f * (Vector2.Distance(q.Vertices[0], q.Vertices[1]) + Vector2.Distance(q.Vertices[3], q.Vertices[2]));
                q.GridV = 0.5f * (Vector2.Distance(q.Vertices[0], q.Vertices[3]) + Vector2.Distance(q.Vertices[1], q.Vertices[2]));
            }
        }

        /// <summary>
        /// Andrew's monotone chain for up to 4 points. Writes hull indices with positive signed area
        /// (counter-clockwise in y-up math, clockwise on screen in y-down ISO). Collinear points are dropped.
        /// Returns the hull size (0–4).
        /// </summary>
        public static int ConvexHull(Vector2[] pts, int[] hull)
        {
            int n = pts.Length;
            for (int i = 0; i < n; i++) s_idx[i] = i;
            // insertion sort by x, then y (n = 4)
            for (int i = 1; i < n; i++)
            {
                int k = s_idx[i], j = i - 1;
                while (j >= 0 && Less(pts[k], pts[s_idx[j]])) { s_idx[j + 1] = s_idx[j]; j--; }
                s_idx[j + 1] = k;
            }
            int m = 0;
            for (int i = 0; i < n; i++)                       // lower hull
            {
                while (m >= 2 && Turn(pts[hull[m - 2]], pts[hull[m - 1]], pts[s_idx[i]]) <= 1e-9f) m--;
                hull[m++] = s_idx[i];
            }
            for (int i = n - 2, lo = m + 1; i >= 0; i--)    // upper hull
            {
                while (m >= lo && Turn(pts[hull[m - 2]], pts[hull[m - 1]], pts[s_idx[i]]) <= 1e-9f) m--;
                hull[m++] = s_idx[i];
            }
            m--; // last point equals the first
            if (m < 3) return m < 0 ? 0 : m;
            return m;
        }

        static bool Less(Vector2 a, Vector2 b) => a.X < b.X || (a.X == b.X && a.Y < b.Y);
        static float Turn(Vector2 o, Vector2 a, Vector2 b) => MaskMath.Cross(a - o, b - o);

        /// <summary>
        /// Heckbert's square→quad projective map: (0,0)→v0, (1,0)→v1, (1,1)→v2, (0,1)→v3.
        /// Row-major [a b c; d e f; g h 1].
        /// </summary>
        public static bool SquareToQuad(Vector2[] v, float[] m)
        {
            float sx = v[0].X - v[1].X + v[2].X - v[3].X;
            float sy = v[0].Y - v[1].Y + v[2].Y - v[3].Y;
            float g, h;
            if (Math.Abs(sx) < 1e-9f && Math.Abs(sy) < 1e-9f)
            {
                g = 0; h = 0; // affine (parallelogram)
            }
            else
            {
                float dx1 = v[1].X - v[2].X, dx2 = v[3].X - v[2].X;
                float dy1 = v[1].Y - v[2].Y, dy2 = v[3].Y - v[2].Y;
                float den = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(den) < 1e-12f) return false;
                g = (sx * dy2 - dx2 * sy) / den;
                h = (dx1 * sy - sx * dy1) / den;
            }
            m[0] = v[1].X - v[0].X + g * v[1].X;
            m[1] = v[3].X - v[0].X + h * v[3].X;
            m[2] = v[0].X;
            m[3] = v[1].Y - v[0].Y + g * v[1].Y;
            m[4] = v[3].Y - v[0].Y + h * v[3].Y;
            m[5] = v[0].Y;
            m[6] = g;
            m[7] = h;
            m[8] = 1f;
            return true;
        }

        public static bool Invert3x3(float[] m, float[] r)
        {
            float a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            float A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            float det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            r[0] = A * inv; r[1] = -(b * i - c * h) * inv; r[2] = (b * f - c * e) * inv;
            r[3] = B * inv; r[4] = (a * i - c * g) * inv; r[5] = -(a * f - c * d) * inv;
            r[6] = C * inv; r[7] = -(a * h - b * g) * inv; r[8] = (a * e - b * d) * inv;
            return true;
        }

        /// <summary>Signed distance to the polygon (negative inside). CPU reference of the shader's FilterCMR_QuadSDF.</summary>
        public static float SignedDistance(QuadShape q, Vector2 p)
        {
            Vector2[] v = q.Vertices;
            const int n = 4;
            float d = Vector2.DistanceSquared(p, v[0]);
            float s = 1f;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                float ee = Vector2.Dot(e, e);
                Vector2 b = ee > 1e-12f ? w - e * MaskMath.Clamp01(Vector2.Dot(w, e) / ee) : w;
                d = Math.Min(d, Vector2.Dot(b, b));
                bool c1 = p.Y >= v[i].Y, c2 = p.Y < v[j].Y, c3 = e.X * w.Y > e.Y * w.X;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * (float)Math.Sqrt(d);
        }
    }
}
