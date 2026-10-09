// The lit atmosphere limb of a planet seen from orbit (#2400): a thin translucent shell whose glow concentrates at the
// rim (Fresnel) and on the sun-facing side, with a warm band along the terminator — the "sci-fi book cover" look. The
// night side stays dark. Replaces the uniform translucent Cloud-shader sphere the flight view used. Additive, so it
// only brightens and reads on LDR presets too; depth-tested, never writes depth. Properties:
//   _Color    the world's sky colour (rgb) and the shell's overall strength (a)
//   _SunDir   world-space direction TO the system's star
//   _Warm     the terminator's warm tint
// DUAL-PIPELINE: URP SubShader first, Built-in fallback below.
Shader "BlocksBeyondTheStars/PlanetLimb"
{
    Properties
    {
        _Color ("Sky colour / strength", Color) = (0.55, 0.75, 0.95, 0.5)
        _SunDir ("Sun direction", Vector) = (0, 0, 1, 0)
        _Warm ("Terminator tint", Color) = (1, 0.6, 0.3, 1)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        Cull Back
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _SunDir;
                half4 _Warm;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normalOS);
                o.wp = wp;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 L = normalize(_SunDir.xyz);
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0);          // the limb: bright at the edge, faint head-on
                float lit = saturate(dot(N, L) * 1.2 + 0.25);             // day side, with a soft terminator
                float terminator = saturate(1.0 - abs(dot(N, L)) * 2.2);  // a warm band where day meets night
                float3 col = _Color.rgb * (rim * 0.9 + 0.08) * lit;
                col += _Warm.rgb * terminator * rim * 0.6 * saturate(lit * 2.0);
                return half4(col * _Color.a, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP ----------------
    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        Cull Back
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _SunDir;
            fixed4 _Warm;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 L = normalize(_SunDir.xyz);
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0);
                float lit = saturate(dot(N, L) * 1.2 + 0.25);
                float terminator = saturate(1.0 - abs(dot(N, L)) * 2.2);
                float3 col = _Color.rgb * (rim * 0.9 + 0.08) * lit;
                col += _Warm.rgb * terminator * rim * 0.6 * saturate(lit * 2.0);
                return fixed4(col * _Color.a, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
