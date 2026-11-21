// FILTER CMR — webcam composite (Built-in render pipeline, uGUI).
//
// Assigned to the existing "Webcam Display" RawImage by WebcamCompositeController,
// so the effect costs no extra draw call and keeps uGUI clipping/stencil support.
//
// Per pixel:
//   1. Recover the IMAGE coordinate q from the texture UV by inverting the
//      RawImage uvRect (mirroring is therefore never applied twice).
//   2. Evaluate the ribbon and quad signed-distance masks (FILTERCMR_Mask.cginc).
//   3. Outside the masks (and their glow band) return the webcam pixel UNCHANGED.
//   4. Inside: glitch displacement + RGB split sampling -> halftone dots with the
//      FILTER CMR tone ramp -> scanlines, print texture, grain -> blend by mask,
//      add the cyan edge glow and optional outline.
// All effect maths happens in gamma space for perceptual consistency in both
// Gamma and Linear projects; colours are uploaded as gamma values (SetVector).
Shader "FILTERCMR/WebcamComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Webcam Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "FILTERCMR_Composite"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "FILTERCMR_Common.cginc"
            #include "FILTERCMR_Mask.cginc"
            #include "FILTERCMR_Halftone.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            // ---- mapping (WebcamCompositeController) ----
            float4 _FxUVRect;       // RawImage.uvRect (x, y, w, h)
            float  _FxAspect;       // displayed image width / height
            float  _FxPixelIso;     // one screen pixel in ISO units (1 / image height in pixels)
            float  _FxTime;         // unscaled seconds
            float  _FxGrainSeed;    // changes at the grain frame rate (0 = static)

            // ---- quad perspective grid ----
            float  _QuadPersp;      // 1 = halftone grid follows the quad's perspective
            float4 _QuadH0, _QuadH1, _QuadH2;     // square -> quad (rows)
            float4 _QuadHI0, _QuadHI1, _QuadHI2;  // quad -> square (rows)
            float4 _QuadGrid;       // xy = average u/v edge length (ISO)

            // ---- mask edges ----
            float _MaskFeather, _MaskOpacity;
            float _EdgeGlow, _EdgeGlowWidth;
            float _EdgeOutline, _EdgeOutlinePx;
            float _EdgeDistortion, _EdgeNoiseScale;
            float _DebugMask;

            // ---- halftone ----
            float _HTEnabled, _HTIntensity, _HTSpacingPx, _HTAngle;
            float _HTContrast, _HTThreshold, _HTStyle, _HTToneMix;

            // ---- colour (gamma values) ----
            float4 _ColInk, _ColPaper, _ColBlue, _ColCyan, _ColLightCyan, _ColNavy, _ColDarkBlue, _ColWhite;
            float _CyanIntensity, _BlueIntensity, _Saturation, _Brightness;

            // ---- RGB split / glitch / grain ----
            float _RGBEnabled, _RGBDistancePx, _RGBAngle, _RGBAnimate;
            float _GlitchEnabled, _GlitchIntensity, _GlitchFrequency, _GlitchBursts, _ScanlineIntensity;
            float _GrainEnabled, _GrainIntensity, _GrainScalePx, _PrintTexture;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            // ---------------------------------------------------------------- mapping
            float2 ImageFromUV(float2 uv)
            {
                float2 st = (uv - _FxUVRect.xy) / _FxUVRect.zw;
                return float2(st.x, 1.0 - st.y);
            }

            float2 UVFromImage(float2 q)
            {
                return _FxUVRect.xy + _FxUVRect.zw * float2(q.x, 1.0 - q.y);
            }

            float3 ToGamma(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return LinearToGammaSpace(c);
            #endif
            }

            float3 FromGamma(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return GammaToLinearSpace(c);
            #endif
            }

            // Explicit LOD: safe inside non-uniform branches (no derivatives needed).
            float3 SampleGamma(float2 uv)
            {
                float3 c = tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).rgb + _TextureSampleAdd.rgb;
                return ToGamma(saturate(c));
            }

            // ------------------------------------------------------------- colour
            float3 ToneRamp(float t)
            {
                float3 blue = lerp(_ColDarkBlue.rgb, _ColBlue.rgb, saturate(_BlueIntensity));
                float3 cyan = lerp(_ColBlue.rgb, _ColCyan.rgb, saturate(_CyanIntensity));
                return FilterCMR_Gradient5(t, _ColNavy.rgb, blue, cyan, _ColLightCyan.rgb, _ColWhite.rgb, 0.28, 0.58, 0.84);
            }

            float Tone(float3 c)
            {
                return saturate((FilterCMR_Luma(c) - 0.5) * _HTContrast + 0.5 + _Brightness);
            }

            // ------------------------------------------------------- distortion
            // Horizontal displacement (IMAGE units) for glitch bands + scanline wobble.
            float2 GlitchOffset(float2 q, float amount)
            {
                if (_GlitchEnabled < 0.5 || _GlitchIntensity <= 0.0) return float2(0.0, 0.0);
                float freq = saturate(_GlitchFrequency);
                float bands = 14.0 + 34.0 * freq;
                float band = floor(q.y * bands);
                float tick = floor(_FxTime * (1.5 + 10.0 * freq));
                float burst = 1.0;
                if (_GlitchBursts > 0.5)
                    burst = lerp(0.3, 1.6, step(0.86 - 0.3 * freq, FilterCMR_Hash12(float2(tick * 0.37, 7.1))));
                float active = step(1.0 - (0.05 + 0.22 * freq) * burst, FilterCMR_Hash12(float2(band, tick)));
                float off = (FilterCMR_Hash12(float2(band * 1.7, tick + 3.1)) - 0.5) * 0.06 * _GlitchIntensity * active * burst;
                off += sin(q.y * 820.0 + _FxTime * 23.0) * 0.0007 * _ScanlineIntensity * _GlitchIntensity;
                return float2(off * amount, 0.0);
            }

            float3 SampleEffectSource(float2 q, float2 glitch, float2 splitQ)
            {
                float2 q2 = q + glitch;
                if (_RGBEnabled > 0.5)
                {
                    float r = SampleGamma(UVFromImage(q2 + splitQ)).r;
                    float g = SampleGamma(UVFromImage(q2)).g;
                    float b = SampleGamma(UVFromImage(q2 - splitQ)).b;
                    return float3(r, g, b);
                }
                return SampleGamma(UVFromImage(q2));
            }

            // ------------------------------------------------------------ halftone
            float3 HalftoneColor(float tonePix, float toneCell, float2 local, float2 cellId, float aa)
            {
                float3 ramp = ToneRamp(tonePix);
                float th = saturate(_HTThreshold);
                if (_HTStyle < 0.5)
                {
                    // PRINT: ink dots on paper; darker image = bigger dots.
                    float darkness = 1.0 - toneCell;
                    float cov = saturate((darkness - th) / max(1.0 - th, 1e-3));
                    float dotA = FilterCMR_Dot(local, cov, cellId, aa);
                    float3 paper = lerp(_ColPaper.rgb, ramp, _HTToneMix);
                    float3 ink = lerp(_ColInk.rgb, _ColNavy.rgb, smoothstep(0.45, 1.0, darkness) * 0.55);
                    return lerp(paper, ink, dotA);
                }
                // SCREEN: light dots on navy; brighter image = bigger dots.
                float cov2 = saturate((toneCell - th) / max(1.0 - th, 1e-3));
                float dotB = FilterCMR_Dot(local, cov2, cellId, aa);
                float3 ground = lerp(_ColNavy.rgb, ramp * 0.45, _HTToneMix);
                float3 light = lerp(_ColInk.rgb, ToneRamp(0.55 + 0.45 * toneCell), 0.65);
                return lerp(ground, light, dotB);
            }

            float EdgeGlow(float d, float feather, float width)
            {
                float gate = 1.0 - smoothstep(0.0, 0.5 * feather + _FxPixelIso, d); // none outside the feather band
                return exp(-abs(d) / max(width, 1e-5)) * gate;
            }

            float EdgeOutline(float d)
            {
                float halfW = 0.5 * max(_EdgeOutlinePx, 0.5) * _FxPixelIso;
                return 1.0 - smoothstep(halfW, halfW + _FxPixelIso, abs(d));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 orig = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                float clipA = 1.0;
                #ifdef UNITY_UI_CLIP_RECT
                clipA = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                float2 q = ImageFromUV(IN.texcoord);
                float2 P = float2(q.x * _FxAspect, q.y);
                float pix = max(_FxPixelIso, 1e-6);
                float2 screenPx = IN.screenPos.xy / max(IN.screenPos.w, 1e-6) * _ScreenParams.xy;

                // Halftone grid coordinates and their derivatives are computed BEFORE any
                // divergent branch, so fwidth() is well defined on every GPU.
                float spacingPx = max(_HTSpacingPx, 1.0);
                float spacing = spacingPx * pix;
                float2 gS = FilterCMR_Rotate(P / spacing, _HTAngle);
                float aaS = 1.0 / spacingPx;
                float2 gQ = float2(0.0, 0.0);
                float aaQ = aaS;
                if (_QuadPersp > 0.5)
                {
                    float3 hp = float3(dot(_QuadHI0.xyz, float3(P, 1.0)), dot(_QuadHI1.xyz, float3(P, 1.0)), dot(_QuadHI2.xyz, float3(P, 1.0)));
                    float w = abs(hp.z) > 1e-8 ? hp.z : 1e-8;
                    gQ = (hp.xy / w) * _QuadGrid.xy / spacing;
                    float2 fw = fwidth(gQ);
                    aaQ = clamp(0.5 * (fw.x + fw.y), 1e-3, 0.5);
                }

                // ---- masks ----
                float edgeNoise = 0.0;
                if (_EdgeDistortion > 0.0)
                    edgeNoise = (FilterCMR_ValueNoise(P * _EdgeNoiseScale + _FxTime * 0.35) - 0.5) * _EdgeDistortion * max(_MaskFeather, 4.0 * pix);

                float dR = 1e5, dQ = 1e5, mR = 0.0, mQ = 0.0;
                if (_RibbonWeight > 0.0 && FilterCMR_InBounds(P, _RibbonBounds))
                {
                    dR = FilterCMR_RibbonSDF(P) + edgeNoise;
                    mR = FilterCMR_MaskFromSDF(dR, _MaskFeather) * _RibbonWeight;
                }
                if (_QuadWeight > 0.0 && FilterCMR_InBounds(P, _QuadBounds))
                {
                    dQ = FilterCMR_QuadSDF(P) + edgeNoise;
                    mQ = FilterCMR_MaskFromSDF(dQ, _MaskFeather) * _QuadWeight;
                }
                float mRaw = max(mR, mQ);
                float m = mRaw * _MaskOpacity;

                float glow = 0.0, outline = 0.0;
                if (_EdgeGlow > 0.0)
                    glow = max(_RibbonWeight * EdgeGlow(dR, _MaskFeather, _EdgeGlowWidth), _QuadWeight * EdgeGlow(dQ, _MaskFeather, _EdgeGlowWidth)) * _EdgeGlow * _MaskOpacity;
                if (_EdgeOutline > 0.5)
                    outline = max(_RibbonWeight * EdgeOutline(dR), _QuadWeight * EdgeOutline(dQ)) * _MaskOpacity;

                // ---- outside every mask: the webcam pixel, untouched ----
                if (m <= 1e-4 && glow <= 1e-4 && outline <= 1e-4 && _DebugMask < 0.5)
                {
                    orig.a *= clipA;
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(orig.a - 0.001);
                    #endif
                    return orig;
                }

                float3 origG = ToGamma(saturate(orig.rgb));
                float3 eff = origG;

                if (m > 1e-4)
                {
                    // Distortions fade with the mask, so the boundary stays continuous.
                    float2 glitch = GlitchOffset(q, m);
                    float2 splitQ = float2(0.0, 0.0);
                    if (_RGBEnabled > 0.5)
                    {
                        float ang = _RGBAngle + (_RGBAnimate > 0.5 ? 0.6 * sin(_FxTime * 0.9) : 0.0);
                        float dist = _RGBDistancePx * pix * (_RGBAnimate > 0.5 ? 0.8 + 0.2 * sin(_FxTime * 1.7) : 1.0) * m;
                        splitQ = float2(cos(ang) / max(_FxAspect, 1e-4), sin(ang)) * dist;
                    }
                    float3 src = SampleEffectSource(q, glitch, splitQ);
                    float3 col = src;

                    if (_HTEnabled > 0.5 && _HTIntensity > 0.0)
                    {
                        float tonePix = Tone(src);
                        float wQ = _QuadPersp > 0.5 ? mQ : 0.0;
                        float wS = max(mR, _QuadPersp > 0.5 ? 0.0 : mQ);
                        float3 htS = float3(0.0, 0.0, 0.0);
                        float3 htQ = float3(0.0, 0.0, 0.0);
                        if (wS > 0.0)
                        {
                            float2 cell = floor(gS) + 0.5;
                            float2 Pc = FilterCMR_Rotate(cell, -_HTAngle) * spacing;
                            float toneCell = Tone(SampleEffectSource(float2(Pc.x / _FxAspect, Pc.y), glitch, splitQ));
                            htS = HalftoneColor(tonePix, toneCell, gS - cell, cell, aaS);
                        }
                        if (wQ > 0.0)
                        {
                            float2 cellQ = floor(gQ) + 0.5;
                            float2 uvc = cellQ * spacing / max(_QuadGrid.xy, 1e-4);
                            float3 hc = float3(dot(_QuadH0.xyz, float3(uvc, 1.0)), dot(_QuadH1.xyz, float3(uvc, 1.0)), dot(_QuadH2.xyz, float3(uvc, 1.0)));
                            float2 Pq = hc.xy / (abs(hc.z) > 1e-8 ? hc.z : 1e-8);
                            float toneCellQ = Tone(SampleEffectSource(float2(Pq.x / _FxAspect, Pq.y), glitch, splitQ));
                            htQ = HalftoneColor(tonePix, toneCellQ, gQ - cellQ, cellQ, aaQ);
                        }
                        float3 ht = (htS * wS + htQ * wQ) / max(wS + wQ, 1e-5);
                        ht = FilterCMR_Saturate(ht, _Saturation);
                        col = lerp(src, ht, saturate(_HTIntensity));

                        if (_PrintTexture > 0.0)
                        {
                            float n = FilterCMR_ValueNoise(P * 90.0) * 0.6 + FilterCMR_ValueNoise(P * 260.0) * 0.4;
                            col += (n - 0.5) * 0.10 * _PrintTexture * saturate(_HTIntensity);
                        }
                    }

                    if (_GlitchEnabled > 0.5 && _ScanlineIntensity > 0.0)
                    {
                        float sl = 0.5 + 0.5 * sin(screenPx.y * 3.14159265);
                        col *= 1.0 - _ScanlineIntensity * 0.12 * sl;
                    }

                    if (_GrainEnabled > 0.5 && _GrainIntensity > 0.0)
                    {
                        float2 gp = floor(screenPx / max(_GrainScalePx, 1.0));
                        float gn = FilterCMR_Hash12(gp + _FxGrainSeed * 37.17) - 0.5;
                        col += gn * _GrainIntensity * 0.35;
                    }

                    eff = lerp(origG, saturate(col), m);
                }

                eff += _ColCyan.rgb * glow;
                eff = lerp(eff, _ColLightCyan.rgb, saturate(outline));
                if (_DebugMask > 0.5)
                {
                    eff = lerp(eff, float3(1.0, 0.15, 0.75), 0.45 * mRaw);
                    float inB = (FilterCMR_InBounds(P, _RibbonBounds) && _RibbonWeight > 0.0) || (FilterCMR_InBounds(P, _QuadBounds) && _QuadWeight > 0.0) ? 1.0 : 0.0;
                    eff = lerp(eff, float3(0.2, 1.0, 0.3), 0.08 * inB);
                }

                half4 color = half4(FromGamma(saturate(eff)), orig.a * clipA);
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
        ENDCG
        }
    }
    Fallback "UI/Default"
}
