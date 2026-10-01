// Hyperjump tunnel (VFX overhaul #2157): an inside-out cylinder around the camera, its UV running around (x) and
// along (y) the tube. Each of 72 angular columns carries streaks that race toward the viewer at their own speed
// and phase (hashed per column), tinted between _Color and _Color2; both tube ends fade out so it has no visible
// rim. _Color.a fades the whole tunnel in and out with the jump. Additive, no texture.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxTunnel"
{
    Properties
    {
        _Color ("Colour (a = fade)", Color) = (0.55, 0.75, 1, 1)
        _Color2 ("Second colour", Color) = (0.85, 0.6, 1, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 2.2
        _Speed ("Streak speed", Range(0, 20)) = 3
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
            #include "FxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Color2;
                float _Intensity;
                float _Speed;
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
                const float columns = 72.0;
                float col = floor(i.uv.x * columns);
                float h = BbtsHash21(float2(col, 3.7));
                float h2 = BbtsHash21(float2(col, 9.1));
                float across = abs(frac(i.uv.x * columns) - 0.5) * 2.0;
                float stripe = 1.0 - smoothstep(0.1, 0.55, across);
                float along = frac(i.uv.y * (1.5 + h2 * 2.0) + _Time.y * _Speed * (0.6 + h) + h * 7.0);
                float streak = smoothstep(0.0, 0.08, along) * (1.0 - smoothstep(0.08, 0.55, along));
                float ends = sin(saturate(i.uv.y) * 3.14159);
                float3 tint = lerp(_Color.rgb, _Color2.rgb, h2);
                float3 c = tint * stripe * streak * ends * ends;
                return half4(c * (_Intensity * _Color.a), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}
