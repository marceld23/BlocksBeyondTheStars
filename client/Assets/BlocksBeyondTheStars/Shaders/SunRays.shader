// God-ray / sun-shaft billboard (Sky.cs): a large additive sprite of radiating light streaks placed at the
// sun. Unlike the SunGlow disc (ZTest Always, always on top), this is depth-tested (ZTest LEqual) and sits at
// the sun's distance, so foreground terrain/trees OCCLUDE it — the rays get clipped around ridges and canopies,
// reading as real light shafts. Purely additive (Blend One One) → it can only brighten, never darken (the safe
// alternative to a full-screen volumetric pass). Tinted by the sun colour + a strength that fades near the
// horizon / in thin air, fed from Sky.cs. Dual-pipeline (URP + Built-in RP).
Shader "BlocksBeyondTheStars/SunRays"
{
    Properties
    {
        _MainTex ("Rays", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual   // foreground geometry occludes the rays → shafts around ridges/canopies
        Blend One One  // additive — only ever brightens

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;

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
                return half4(_Color.rgb * t.rgb * _Color.a, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+20" }
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
                return fixed4(_Color.rgb * t.rgb * _Color.a, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
