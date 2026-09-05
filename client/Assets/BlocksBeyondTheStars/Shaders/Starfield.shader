// Additive starfield dome drawn behind the world. Each star carries a twinkle phase + speed (TEXCOORD1)
// and a colour (vertex colour); the shader pulses its brightness over time. _Brightness (set per-frame by
// the Starfield component) fades the whole field in at night / in space and out during a bright day.
// End of the opaque queue (Geometry+499, #1513) + ZWrite Off: stars draw AFTER the terrain/planet/ship, so the
// depth test rejects every covered pixel instead of shading the whole sky and overpainting it — stars only show
// in open sky, and additive-over-opaque is order-independent, so the picture is unchanged. The vertex shader
// pushes the dome to the far plane (#1582), so the test rejects covered pixels at ANY distance — an opaque body
// beyond the dome's 0.45 × far radius used to pass it and get stars painted across it.
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
        Tags { "Queue" = "Geometry+499" "RenderType" = "Background" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
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
                // #1582: the dome draws at the END of the opaque queue with ZWrite Off (#1513), so its depth test must
                // reject exactly the pixels an opaque draw already covers — at ANY distance, not only nearer than the
                // dome's 0.45 × far radius (a moon beyond it got stars painted across it). Push the vertex to the far
                // plane (a hair inside it, so no driver clips it): the dome is at infinity, its radius stops mattering.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1.0e-6;         // reversed-Z: far plane = 0
                #else
                    o.positionCS.z = o.positionCS.w * (1.0 - 1.0e-6); // far plane = w
                #endif
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
        Tags { "Queue" = "Geometry+499" "RenderType" = "Background" "IgnoreProjector" = "True" }
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
                #if UNITY_REVERSED_Z
                    o.pos.z = o.pos.w * 1.0e-6;         // #1582: dome at the far plane (reversed-Z: far = 0)
                #else
                    o.pos.z = o.pos.w * (1.0 - 1.0e-6); // #1582: dome at the far plane (far = w)
                #endif
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
