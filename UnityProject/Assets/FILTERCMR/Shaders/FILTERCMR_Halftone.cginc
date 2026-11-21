// FILTER CMR — halftone dot rendering.
//
// A grid of cells covers the image (rotated by the screen angle). Each cell
// samples the image ONCE at its centre and draws one dot whose AREA is
// proportional to the tone (so tones reproduce like real print), with an
// analytic one-pixel anti-aliased edge (crisp, no pixelation). In quad mode the
// grid can follow the quad's perspective (the C# side supplies a homography).
#ifndef FILTERCMR_HALFTONE_INCLUDED
#define FILTERCMR_HALFTONE_INCLUDED

#include "FILTERCMR_Common.cginc"

float _HTDotSize;    // 1 = dots just merge at full coverage
float _HTShape;      // 0 circle, 1 square, 2 soft circle, 3 irregular

// Dot coverage at `local` (cell space, -0.5..0.5) for a tone coverage 0..1.
// `aa` is the width of one screen pixel in cell units.
float FilterCMR_Dot(float2 local, float coverage, float2 cellId, float aa)
{
    if (coverage <= 0.0) return 0.0;
    aa = max(aa, 1e-3);
    float r = sqrt(coverage) * 0.70710678 * _HTDotSize;   // area-proportional radius
    float shape = _HTShape;

    if (shape < 0.5)                                       // circle
    {
        float d = length(local) - r;
        return 1.0 - smoothstep(-aa, aa, d);
    }
    if (shape < 1.5)                                       // square (same area as the circle)
    {
        float d = max(abs(local.x), abs(local.y)) - r * 0.8862269;
        return 1.0 - smoothstep(-aa, aa, d);
    }
    if (shape < 2.5)                                       // soft circle
    {
        float d = length(local) / max(r * 1.35, 1e-4);
        float m = saturate(1.0 - d);
        return m * m * (3.0 - 2.0 * m) * saturate(coverage * 3.0 + 0.25);
    }
    // irregular: jittered centre, wobbling outline, per-cell size variation
    float2 h = FilterCMR_Hash22(cellId);
    float h3 = FilterCMR_Hash12(cellId + 17.31);
    float2 lp = local - (h - 0.5) * 0.18;
    float ang = atan2(lp.y, lp.x);
    float wob = 1.0 + 0.20 * sin(ang * 3.0 + h.x * 6.2831) + 0.10 * sin(ang * 5.0 - h.y * 6.2831);
    float d2 = length(lp) - r * wob * (0.85 + 0.3 * h3);
    return 1.0 - smoothstep(-aa * 1.5, aa * 1.5, d2);
}

#endif // FILTERCMR_HALFTONE_INCLUDED
