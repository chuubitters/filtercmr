// FILTER CMR — analytic interactive masks (signed distance fields).
//
// The mask is not a texture: the C# side uploads the ribbon samples and the quad
// vertices (ISO space, see MaskCoordinateMapper.cs) and every pixel evaluates
// its exact distance to the shape. That gives a resolution-independent 0..1
// mask with real feathering, an edge-distance for glow/outline, and no render
// targets to allocate. CPU reference implementations with identical maths:
// RibbonMaskGenerator.SignedDistance / QuadrilateralMaskGenerator.SignedDistance.
#ifndef FILTERCMR_MASK_INCLUDED
#define FILTERCMR_MASK_INCLUDED

#define FILTERCMR_RIBBON_POINTS 25

// Ribbon: centre samples (xy) and half-widths (z), ISO units.
float4 _RibbonPts[FILTERCMR_RIBBON_POINTS];
float  _RibbonCount;
float  _RibbonWeight;   // 0..1 fade (already eased) — 0 disables the ribbon
float4 _RibbonBounds;   // xy = min, zw = max (ISO), expanded by feather/glow/noise

// Quad: four vertices (ISO), consistent winding. Triangles repeat vertex 2.
float4 _QuadV01;        // v0.xy, v1.xy
float4 _QuadV23;        // v2.xy, v3.xy
float  _QuadWeight;
float4 _QuadBounds;

float FilterCMR_RibbonSDF(float2 p)
{
    float d = 1e5;
    int n = (int)_RibbonCount;
    [loop]
    for (int i = 0; i < FILTERCMR_RIBBON_POINTS - 1; i++)
    {
        if (i + 1 >= n) break;
        float4 a = _RibbonPts[i];
        float4 b = _RibbonPts[i + 1];
        float2 ab = b.xy - a.xy;
        float h = saturate(dot(p - a.xy, ab) / max(dot(ab, ab), 1e-10));
        float r = lerp(a.z, b.z, h);
        d = min(d, length(p - a.xy - ab * h) - r);
    }
    return d;
}

// Exact signed distance to a simple polygon (Inigo Quilez), negative inside.
float FilterCMR_QuadSDF(float2 p)
{
    float2 v[4];
    v[0] = _QuadV01.xy; v[1] = _QuadV01.zw; v[2] = _QuadV23.xy; v[3] = _QuadV23.zw;
    float d = dot(p - v[0], p - v[0]);
    float s = 1.0;
    int j = 3;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 e = v[j] - v[i];
        float2 w = p - v[i];
        float ee = dot(e, e);
        float2 b = w - e * (ee > 1e-12 ? saturate(dot(w, e) / ee) : 0.0);
        d = min(d, dot(b, b));
        bool c1 = p.y >= v[i].y;
        bool c2 = p.y < v[j].y;
        bool c3 = e.x * w.y > e.y * w.x;
        if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        j = i;
    }
    return s * sqrt(d);
}

bool FilterCMR_InBounds(float2 p, float4 b)
{
    return p.x >= b.x && p.y >= b.y && p.x <= b.z && p.y <= b.w;
}

// Feather centred on the boundary: 1 inside, 0 outside, smooth over `feather`.
float FilterCMR_MaskFromSDF(float d, float feather)
{
    float f = max(feather, 1e-5);
    return 1.0 - smoothstep(-0.5 * f, 0.5 * f, d);
}

#endif // FILTERCMR_MASK_INCLUDED
