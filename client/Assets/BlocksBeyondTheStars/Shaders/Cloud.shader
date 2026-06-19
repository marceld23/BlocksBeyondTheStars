// Soft alpha-blended cloud billboard / shell (weather). Used for the surface cloud layer (drifting
// puffs in the sky) and the cloud shell seen over a planet from space. Unlit, tinted by _Color (the
// per-planet cloud colour, darkened in storms), depth-tested so terrain/the planet front occludes it
// but it never writes depth (clouds blend among themselves). Always-included.
//
// Optional directional sun shading (no extra render passes, stays depth-texture-free):
//   _SunShade  0 = flat (legacy: rgb = _Color*tex), 1 = shade by the sun direction.
//   _Bulge     1 = fake a volume normal from the billboard UV (surface puffs); 0 = use the real
//              geometric normal (the orbit cloud shell sphere → a day/night terminator).
//   _CloudSunDir  world-space direction TOWARD the sun (the lit side).
//   _ShadeColor   the colour of the side turned away from the sun (lit side stays _Color).
// Callers that set none of these (e.g. the atmosphere haze) keep the original flat look.
// DUAL-PIPELINE: URP HLSL SubShader first (SRP-batcher friendly), original Built-in CG below.
Shader "BlocksBeyondTheStars/Cloud"
{
    Properties
    {
        _MainTex ("Cloud", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Color", Color) = (0, 0, 0, 1)
        _CloudSunDir ("Sun Dir", Vector) = (0, 1, 0, 0)
        _SunShade ("Sun Shade", Float) = 0
        _Bulge ("UV Bulge Normal", Float) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

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
                half4 _ShadeColor;
                float4 _CloudSunDir;
                float _SunShade;
                float _Bulge;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wN : TEXCOORD1;
                float3 wR : TEXCOORD2;
                float3 wU : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wN = TransformObjectToWorldNormal(v.normalOS);
                o.wR = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
                o.wU = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 rgb = _Color.rgb;
                if (_SunShade > 0.5)
                {
                    float3 nrm;
                    if (_Bulge > 0.5)
                    {
                        // Fake a rounded volume so each puff has a lit and a shadowed side.
                        float2 p = i.uv * 2.0 - 1.0;
                        float bulge = saturate(1.0 - length(p));
                        nrm = normalize(i.wR * p.x + i.wU * p.y + normalize(i.wN) * max(bulge, 0.15));
                    }
                    else
                    {
                        nrm = normalize(i.wN);
                    }

                    half ndl = saturate(dot(nrm, normalize(_CloudSunDir.xyz)) * 0.5 + 0.5);
                    rgb = lerp(_ShadeColor.rgb, _Color.rgb, ndl);
                }

                return half4(rgb * t.rgb, t.a * _Color.a);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged blend; sun shading added) ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _ShadeColor;
            float4 _CloudSunDir;
            float _SunShade;
            float _Bulge;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wN : TEXCOORD1;
                float3 wR : TEXCOORD2;
                float3 wU : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wN = UnityObjectToWorldNormal(v.normal);
                o.wR = normalize(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0)));
                o.wU = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed3 rgb = _Color.rgb;
                if (_SunShade > 0.5)
                {
                    float3 nrm;
                    if (_Bulge > 0.5)
                    {
                        float2 p = i.uv * 2.0 - 1.0;
                        float bulge = saturate(1.0 - length(p));
                        nrm = normalize(i.wR * p.x + i.wU * p.y + normalize(i.wN) * max(bulge, 0.15));
                    }
                    else
                    {
                        nrm = normalize(i.wN);
                    }

                    fixed ndl = saturate(dot(nrm, normalize(_CloudSunDir.xyz)) * 0.5 + 0.5);
                    rgb = lerp(_ShadeColor.rgb, _Color.rgb, ndl);
                }

                return fixed4(rgb * t.rgb, t.a * _Color.a);
            }
            ENDCG
        }
    }
}
