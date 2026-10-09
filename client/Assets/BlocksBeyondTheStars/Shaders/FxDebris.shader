// Lit, vertex-coloured opaque shader for MESH particles (VFX overhaul #2152): the block chips and break cubes
// of mining, robot parts and asteroid rubble. The ParticleSystem renders a cube mesh per particle and feeds the
// per-particle start colour as COLOR and the rotated mesh normal as NORMAL, so a chip is shaded like a tiny
// block — the world's sun (_Sc_SunDir / _Sc_Light), an ambient floor, the headlamp-independent FX lights — and
// never reads as a flat sticker. Opaque, so no sorting and no overdraw beyond the chip itself.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to VertexColorOpaque.
Shader "BlocksBeyondTheStars/FxDebris"
{
    Properties
    {
        _Ambient ("Ambient floor", Range(0, 1)) = 0.45
        _Emission ("Self glow", Range(0, 4)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): chips haze like the block they came from (#2393)

            CBUFFER_START(UnityPerMaterial)
                float _Ambient;
                float _Emission;
            CBUFFER_END

            float4 _Sc_Light;  // global day/night × sun colour (a > 0.5 = set)
            float4 _Sc_SunDir; // global world-space direction to the sun

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; float4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float3 L = (dot(_Sc_SunDir.xyz, _Sc_SunDir.xyz) > 0.01) ? normalize(_Sc_SunDir.xyz) : normalize(float3(0.4, 0.8, -0.45));
                float ndl = saturate(dot(N, L));
                float3 albedo = i.color.rgb;
                float3 col = albedo * light * (_Ambient + (1.0 - _Ambient) * ndl * BbtsCloudShade(i.wp));
                col += BbtsFxLights(i.wp, N, albedo);
                col += albedo * _Emission;
                col = BbtsApplyHaze(col, i.wp, 1.0);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/VertexColorOpaque"
}
