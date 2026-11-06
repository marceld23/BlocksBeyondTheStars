// The sea of clouds (#3272): a large horizontal sheet at the top of the world's cloud band, seen from above — summits and
// towers rise out of it. CloudSea.cs keeps the disc (dense rings near the middle) under the camera at CloudTopY and drives
// the look. The cloud is a threshold on two octaves of the tileable mist noise in world XZ (MistNoiseTexture: R coarse,
// scrolled with the wind; G fine at three times the tiling, evolving on its own), so it stays put in the world while the
// disc follows the camera, and the weather's cover (CloudSeaRules.Threshold) opens holes or closes it. Lit from the sun:
// a third sample a little toward the sun turns the noise into billows with a bright and a shadowed side. The colour is the
// band's own lit cloud colour (_Sc_CloudBandColor from the shared include — the cloud you climb out of is the cloud below);
// the C# side's colours (the planet's cloud colour, night and storms darkened) stand in while no band colour is set. The
// far rim fades out and the distance haze
// (AtmosphereCommon.hlsl) takes it into the horizon; with the depth texture (Medium+, which the deck needs anyway) it
// melts into the mountain flanks that pierce it. Unlit, alpha-blended, no depth write. URP only; always-included.
Shader "BlocksBeyondTheStars/CloudSea"
{
    Properties
    {
        _MainTex ("Noise (R coarse, G fine; tileable)", 2D) = "gray" {}
        _LitColor ("Sunlit side (linear)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shaded side (linear)", Color) = (0.62, 0.64, 0.7, 1)
        _Params ("x = cover threshold, y = edge softness, z = opacity, w = noise tiles per block", Vector) = (0.5, 0.18, 1, 0.0041667)
        _Scroll ("xy = wind offset (blocks), zw = fine-octave offset (uv)", Vector) = (0, 0, 0, 0)
        _Shape ("x = radius (blocks), y = rim fade start 0..1, z = terrain fade (m), w unused", Vector) = (1200, 0.55, 8, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // the terrain fade
            #include "AtmosphereCommon.hlsl" // the shared haze (#2408): the deck dissolves into the horizon

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Sc_ScreenFx; // 1 when the depth texture exists (Medium+)

            // SRP Batcher (#573): per-material properties in UnityPerMaterial (the texture handle stays outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _LitColor;
                half4 _ShadeColor;
                float4 _Params;
                float4 _Scroll;
                float4 _Shape;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wp : TEXCOORD0;
                float4 haze : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wp = wp;
                o.haze = BbtsHaze(wp, 1.0, 1.0); // per vertex (the rings are dense where it matters), one evaluation
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // World-fixed noise: the coarse octave rides the wind, the fine one evolves on its own. Both repeat every
                // tile, so the C# side wraps the offsets without a seam. A third sample toward the sun gives the billows a lit side.
                float2 uv = (i.wp.xz - _Scroll.xy) * _Params.w;
                float2 toSun = _Sc_FogSunDir.xz;
                float n1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                float n2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * 3.0 + _Scroll.zw).g;
                float nSun = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + toSun * 0.02).r;
                float n = n1 * 0.68 + n2 * 0.32;
                float dens = smoothstep(_Params.x, _Params.x + _Params.y, n);

                // The deck is the band seen from above: it takes the band's own lit colour (#3272, _Sc_CloudBandColor), so the
                // cloud you climb out of is the cloud below you; the C# colours stand in while no band colour is set.
                bool band = _Sc_CloudBand.z > 0.001;
                float3 litCol = band ? _Sc_CloudBandColor.rgb * 1.08 : _LitColor.rgb;
                float3 shadeCol = band ? _Sc_CloudBandColor.rgb * 0.66 : _ShadeColor.rgb;

                // A slope rising toward the sun faces it; the sun's height lights the whole top (none at night).
                float relief = saturate(0.55 + (nSun - n1) * 5.0);
                float sunUp = saturate(_Sc_FogSunDir.y * 2.0 + 0.2);
                float lit = lerp(0.3, 1.0, relief * sunUp);
                float3 col = lerp(shadeCol, litCol, lit) * lerp(0.86, 1.0, dens); // thin edges a little greyer
                col = lerp(col, i.haze.rgb, i.haze.a);

                // The far rim fades out (the haze has mostly taken it by then), so the disc never ends in a line.
                float r = length(i.wp.xz - _WorldSpaceCameraPos.xz) / max(_Shape.x, 1.0);
                float alpha = dens * _Params.z * (1.0 - smoothstep(_Shape.y, 0.98, r));

                // Melt into the mountain flanks that pierce the deck instead of cutting a hard line along them.
                if (_Sc_ScreenFx > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    alpha *= saturate((sceneEye - i.screenPos.w) / max(_Shape.z, 0.01));
                }

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
