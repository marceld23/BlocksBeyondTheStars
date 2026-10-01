// Holographic highlight / x-ray ghost on a cube mesh (VFX overhaul #2152): glowing cube edges (from the face UVs),
// a faint fill, world-space scan lines drifting upward and a soft flicker. Two passes, both additive:
//   * "SRPDefaultUnlit" with ZTest Greater — only where the ghost is HIDDEN behind something: a dimmer x-ray glow,
//     so scanned ores read through solid rock (the terrain scanner) and a scan bracket stays visible behind cover;
//   * "UniversalForward" with ZTest LEqual — the bright, sharp-edged hologram where it is in plain view.
// URP renders both light modes of an unlit SubShader. Queue Transparent+50: after the terrain has written its
// depth, which the ZTest Greater pass needs. One cached material per colour (FxKit) keeps them batched.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxHolo"
{
    Properties
    {
        _Color ("Colour (a = fade)", Color) = (0.4, 0.9, 1, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 2
        _EdgeWidth ("Edge width (0..0.5 of a face)", Range(0.01, 0.5)) = 0.08
        _Fill ("Fill", Range(0, 1)) = 0.12
        _LineDensity ("Scan lines per block", Range(0, 16)) = 4
        _Behind ("Through-wall strength", Range(0, 1)) = 0.55
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensity;
            float _EdgeWidth;
            float _Fill;
            float _LineDensity;
            float _Behind;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 wp = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(wp);
            o.uv = v.uv;
            o.wp = wp;
            return o;
        }

        float3 HoloColour(Varyings i, float edgeBoost, float fillBoost)
        {
            float e = min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y));
            float edge = 1.0 - smoothstep(0.0, _EdgeWidth, e);
            float lines = 0.0;
            if (_LineDensity > 0.0)
            {
                lines = pow(abs(sin((i.wp.y * _LineDensity - _Time.y * 1.5) * 3.14159)), 10.0) * 0.4;
            }

            float flicker = 0.92 + 0.08 * sin(_Time.y * 37.0 + i.wp.x * 3.1 + i.wp.z * 1.7);
            return _Color.rgb * (edge * edgeBoost + (_Fill + lines) * fillBoost) * flicker * (_Intensity * _Color.a);
        }
        ENDHLSL

        Pass
        {
            Name "Behind"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBehind

            half4 fragBehind(Varyings i) : SV_Target
            {
                return half4(HoloColour(i, 0.9, 1.6) * _Behind, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Front"
            Tags { "LightMode" = "UniversalForward" }
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFront

            half4 fragFront(Varyings i) : SV_Target
            {
                return half4(HoloColour(i, 1.8, 1.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}
