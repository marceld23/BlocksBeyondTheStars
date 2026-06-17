// Alpha-blended, textured, vertex-coloured particle shader for code-driven ParticleSystems that need opaque-ish
// bits rather than additive glow: mining debris/dust, weather rain/snow/ash, smoke. Sibling of
// BlocksBeyondTheStars/Particle (which is additive). SrcAlpha/OneMinusSrcAlpha, ZWrite Off, Cull Off, Transparent
// queue. Dual-pipeline (URP + Built-in RP); the default ParticleSystem vertex streams feed it directly.
Shader "BlocksBeyondTheStars/ParticleAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(i.color.rgb * t.rgb, i.color.a * t.a);
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                return fixed4(i.color.rgb * t.rgb, i.color.a * t.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
