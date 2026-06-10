// Additive sun-disc glow billboard (M27). Drawn in the sky in the sun direction, tinted by the
// per-system sun colour. Soft radial glow texture, additive blend, depth-tested so terrain/hills
// occlude it (ZTest LEqual) but it never writes depth. Built-in render pipeline; always-included.
// DUAL-PIPELINE: URP HLSL SubShader first, original Built-in CG below.
Shader "Spacecraft/SunGlow"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Background+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 col = _Color.rgb * t.rgb * (t.a * _Color.a);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
    SubShader
    {
        // Drawn before opaque geometry (Background) and never depth-tested, so it always shows on the
        // sky; opaque terrain drawn afterwards naturally occludes it where there are hills. This avoids
        // depth-test edge cases (reversed-Z) that made the disc invisible at the far horizon.
        Tags { "Queue" = "Background+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed3 col = _Color.rgb * t.rgb * (t.a * _Color.a);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
