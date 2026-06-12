// Additive starfield dome drawn behind the world. Each star carries a twinkle phase + speed (TEXCOORD1)
// and a colour (vertex colour); the shader pulses its brightness over time. _Brightness (set per-frame by
// the Starfield component) fades the whole field in at night / in space and out during a bright day.
// Background queue + ZWrite Off: stars draw right after the sky clear, and any opaque geometry (terrain,
// the planet, the ship) drawn afterwards paints over them — so stars only show in open sky.
// DUAL-PIPELINE: URP HLSL SubShader first, original Built-in CG below.
Shader "BlocksBeyondTheStars/Starfield"
{
    Properties
    {
        _MainTex ("Dot", 2D) = "white" {}
        _Brightness ("Brightness", Range(0,2)) = 1
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One     // additive — stars add light onto the dark sky
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 tw : TEXCOORD1; // x = twinkle phase, y = twinkle speed
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float tw : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                float s = sin(_Time.y * v.tw.y + v.tw.x); // per-star pulse
                o.tw = 0.72 + 0.28 * s; // twinkle with a brighter floor so stars never dim to near-black
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a; // soft round dot falloff
                half3 col = i.color.rgb * a * i.tw * _Brightness;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" }
        Blend One One     // additive — stars add light onto the dark sky
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Brightness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 tw : TEXCOORD1; // x = twinkle phase, y = twinkle speed
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float tw : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float s = sin(_Time.y * v.tw.y + v.tw.x); // per-star pulse
                o.tw = 0.72 + 0.28 * s; // twinkle with a brighter floor so stars never dim to near-black
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;        // soft round dot falloff
                fixed3 col = i.color.rgb * a * i.tw * _Brightness;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
