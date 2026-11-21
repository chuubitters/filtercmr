// Prepended to the composite shader's CGPROGRAM block by shader_check.py.
// Mimics the subset of what Unity's shader compiler defines on desktop.
#define SHADER_API_D3D11 1
#define SHADER_TARGET 30
#define UNITY_VERSION 202236

// Harness-only: glslang's DX9 compatibility mode mis-lowers `sampler2D` / `tex2D` /
// `tex2Dlod` (Unity's own compiler handles them natively at target 3.0). Map the DX9
// syntax onto the equivalent DX10 objects so the SAME shader source can be compiled
// and executed here. Semantics are identical: bilinear sampling, single mip level.
#define sampler2D Texture2D
SamplerState fcmr_harness_sampler;
#define tex2D(s, uv) (s).Sample(fcmr_harness_sampler, (uv))
#define tex2Dlod(s, c) (s).SampleLevel(fcmr_harness_sampler, (c).xy, (c).w)
