// Night-aurora curtain (AuroraView): a soft, additive, vertically-hanging light curtain with waving vertical
// ray-streaks and a top/bottom fade, instead of the old hard-edged flat cloud quads. Additive (Blend One One)
// so it only glows; ZTest LEqual so terrain occludes it; ZWrite Off. Colour + overall brightness come from
// _Color (AuroraView shifts the hue green→teal→violet and fades it with night). Dual-pipeline (URP + Built-in).
Shader "BlocksBeyondTheStars/Aurora"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 1, 0.55, 1)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One One // additive glow

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;

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
                float t = _Time.y;
                float2 uv = i.uv;

                // Gentle horizontal undulation of the whole curtain.
                float yy = uv.y + sin(uv.x * 6.0 + t * 0.7) * 0.04 + sin(uv.x * 13.0 - t * 0.4) * 0.02;

                // Hangs from the top: bright through the upper-middle, soft-faded at the very top + trailing off
                // toward the bottom — no hard edges.
                float vGrad = smoothstep(0.0, 0.35, yy) * (1.0 - smoothstep(0.55, 1.0, yy));

                // Vertical ray streaks that wave sideways over time (the classic curtain "folds").
                float rayPhase = uv.x * 42.0 + sin(uv.x * 8.0 + t * 0.5) * 2.2 + t * 0.6;
                float rays = pow(saturate(0.55 + 0.45 * sin(rayPhase)), 1.6);

                // Soft side fade so the curtain ends don't cut off hard.
                float sideFade = smoothstep(0.0, 0.12, uv.x) * (1.0 - smoothstep(0.88, 1.0, uv.x));

                float a = saturate(vGrad) * rays * sideFade * _Color.a;
                return half4(_Color.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                float yy = uv.y + sin(uv.x * 6.0 + t * 0.7) * 0.04 + sin(uv.x * 13.0 - t * 0.4) * 0.02;
                float vGrad = smoothstep(0.0, 0.35, yy) * (1.0 - smoothstep(0.55, 1.0, yy));
                float rayPhase = uv.x * 42.0 + sin(uv.x * 8.0 + t * 0.5) * 2.2 + t * 0.6;
                float rays = pow(saturate(0.55 + 0.45 * sin(rayPhase)), 1.6);
                float sideFade = smoothstep(0.0, 0.12, uv.x) * (1.0 - smoothstep(0.88, 1.0, uv.x));
                float a = saturate(vGrad) * rays * sideFade * _Color.a;
                return fixed4(_Color.rgb * a, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
