// FILTER CMR — the one place that defines how mask coordinates relate to the
// landmarks, the displayed image and the texture. Pure C# (System.Numerics).
//
// Spaces
//   IMAGE  (q):  what Python sends. 0–1 over the displayed image, origin
//               top-left, y down, ALREADY mirrored when the tracker mirrors.
//               Identical to the landmark overlay's space.
//   ISO    (P):  IMAGE with x scaled by the image aspect (w/h):  P = (q.x·aspect, q.y).
//               One unit = the displayed image's height, distances are isotropic.
//               All mask geometry (ribbon, quad) lives here, so circles stay round.
//   QUAD   (s,t):the RawImage's local 0–1 rect, origin bottom-left (uGUI convention).
//               q = (s, 1 − t).
//   UV     (u,v):texture coordinates. RawImage maps QUAD → UV through uvRect:
//               u = uvRect.x + uvRect.w·s,  v = uvRect.y + uvRect.h·t.
//               WebcamDisplay sets uvRect = (1,0,−1,1) when mirroring, (0,1,1,−1) for a
//               vertically mirrored webcam, so the shader inverts uvRect to get q back.
//               That is why mirroring is never applied twice: the mask works in q,
//               which already matches the mirrored image on screen.

using System.Numerics;

namespace FilterCMR.Mask
{
    public static class MaskCoordinateMapper
    {
        public static Vector2 ImageToIso(Vector2 q, float aspect) => new Vector2(q.X * aspect, q.Y);
        public static Vector2 IsoToImage(Vector2 p, float aspect) => new Vector2(aspect > 0 ? p.X / aspect : p.X, p.Y);

        /// <summary>IMAGE → texture UV for a RawImage uvRect (x, y, width, height).</summary>
        public static Vector2 ImageToUV(Vector2 q, float rx, float ry, float rw, float rh)
            => new Vector2(rx + rw * q.X, ry + rh * (1f - q.Y));

        /// <summary>Texture UV → IMAGE (inverse of <see cref="ImageToUV"/>). Same formula the shader uses.</summary>
        public static Vector2 UVToImage(Vector2 uv, float rx, float ry, float rw, float rh)
        {
            float s = rw != 0 ? (uv.X - rx) / rw : 0f;
            float t = rh != 0 ? (uv.Y - ry) / rh : 0f;
            return new Vector2(s, 1f - t);
        }

        /// <summary>IMAGE → local position inside a rect (xMin, yMin, width, height), uGUI y-up.</summary>
        public static Vector2 ImageToLocal(Vector2 q, float xMin, float yMin, float width, float height)
            => new Vector2(xMin + q.X * width, yMin + (1f - q.Y) * height);

        /// <summary>ISO length → screen pixels, given the displayed image height in pixels.</summary>
        public static float IsoToPixels(float isoLength, float imageHeightPixels) => isoLength * imageHeightPixels;
        public static float PixelsToIso(float pixels, float imageHeightPixels) => imageHeightPixels > 0 ? pixels / imageHeightPixels : 0f;
    }
}
