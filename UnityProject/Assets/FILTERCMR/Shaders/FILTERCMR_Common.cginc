// FILTER CMR — shared shader utilities: hashing, noise, colour. Built-in pipeline, HLSL (CGPROGRAM).
#ifndef FILTERCMR_COMMON_INCLUDED
#define FILTERCMR_COMMON_INCLUDED

// --- hashing / noise (no textures needed) ---------------------------------------------
float FilterCMR_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 FilterCMR_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float FilterCMR_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = FilterCMR_Hash12(i);
    float b = FilterCMR_Hash12(i + float2(1, 0));
    float c = FilterCMR_Hash12(i + float2(0, 1));
    float d = FilterCMR_Hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// --- colour ------------------------------------------------------------------------------
float FilterCMR_Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float3 FilterCMR_Saturate(float3 c, float s)
{
    float l = FilterCMR_Luma(c);
    return lerp(float3(l, l, l), c, s);
}

// Piecewise-linear 5-stop gradient map. Stops at 0, s1, s2, s3, 1.
float3 FilterCMR_Gradient5(float t, float3 c0, float3 c1, float3 c2, float3 c3, float3 c4, float s1, float s2, float s3)
{
    t = saturate(t);
    float3 c = lerp(c0, c1, saturate(t / s1));
    c = lerp(c, c2, saturate((t - s1) / (s2 - s1)));
    c = lerp(c, c3, saturate((t - s2) / (s3 - s2)));
    c = lerp(c, c4, saturate((t - s3) / (1.0 - s3)));
    return c;
}

float2 FilterCMR_Rotate(float2 p, float a)
{
    float s = sin(a), c = cos(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

#endif // FILTERCMR_COMMON_INCLUDED
