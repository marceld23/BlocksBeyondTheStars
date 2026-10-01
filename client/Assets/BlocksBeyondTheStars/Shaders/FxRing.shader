// Flat ring / shockwave / scan ring on a quad (VFX overhaul #2152). The quad's UV maps a disc: a thin bright edge
// at the rim, an optional faint inner fill and optional concentric scan lines. Additive (Blend One One). The ring's
// size is the object's scale; _Color.a fades it, both driven per instance (MaterialPropertyBlock) by FxKit.
// Used flat on the ground (gadget pulses, scanner rings, landing dust rings) and camera-facing in space
// (explosion shockwaves, warp arrival rings).
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxRing"
{
    Properties
    {
        _Color ("Colour (a = fade)", Color) = (1, 1, 1, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 2
        _Thickness ("Edge thickness (0..1 of the radius)", Range(0.01, 1)) = 0.12
        _Fill ("Inner fill", Range(0, 1)) = 0.08
        _Lines ("Concentric lines (0 = off)", Range(0, 40)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Thickness;
                float _Fill;
                float _Lines;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                if (r > 1.0)
                {
                    return half4(0, 0, 0, 1);
                }

                float inner = 1.0 - _Thickness;
                float edge = smoothstep(inner, lerp(inner, 1.0, 0.65), r) * (1.0 - smoothstep(0.96, 1.0, r));
                float fill = _Fill * r * r;
                float lines = 0.0;
                if (_Lines > 0.0)
                {
                    lines = pow(abs(sin(r * _Lines * 3.14159)), 18.0) * 0.35 * r;
                }

                float3 col = _Color.rgb * (edge * 1.6 + fill + lines);
                return half4(col * (_Intensity * _Color.a), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}
