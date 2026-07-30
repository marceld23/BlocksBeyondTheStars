// Thermal contact blob (ThermalVision): a camera-facing additive quad with a soft radial falloff — one heat
// signature. Drawn in the Overlay queue with ZTest Always so a contact stays visible THROUGH terrain, which is
// the whole point of an infrared scope; the ore-scan glow (SunGlow) sits in the Background queue and would be
// painted over by the opaque terrain that draws after it. No texture: the falloff is computed from the quad's
// own UVs, so a marker costs one tiny material and nothing else. DUAL-PIPELINE: URP HLSL first, Built-in CG below.
Shader "BlocksBeyondTheStars/ThermalMarker"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Core ("Core Size", Range(0.02, 0.5)) = 0.16
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Core;
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
                float d = length(i.uv - 0.5) * 2.0;              // 0 at the centre, 1 at the quad edge
                float glow = saturate(1.0 - d);
                glow = glow * glow;                               // soft halo
                float core = saturate(1.0 - d / max(_Core, 0.02)); // hot centre
                half a = saturate(glow * 0.75 + core * 0.9);
                return half4(_Color.rgb * a * _Color.a, a);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP ----------------
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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

            fixed4 _Color;
            float _Core;

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
                float d = length(i.uv - 0.5) * 2.0;
                float glow = saturate(1.0 - d);
                glow = glow * glow;
                float core = saturate(1.0 - d / max(_Core, 0.02));
                fixed a = saturate(glow * 0.75 + core * 0.9);
                return fixed4(_Color.rgb * a * _Color.a, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
