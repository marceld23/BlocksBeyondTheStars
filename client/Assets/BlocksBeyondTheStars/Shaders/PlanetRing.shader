// Planetary ring disc (#596): alpha-blended, double-sided, tinted via _Color so the sky view can
// re-tint it per frame (daytime sky wash) without touching the mesh. The band pattern lives in
// _MainTex (u = radial across the ring, generated per RingSeed). ZWrite Off + Transparent queue:
// the opaque planet sphere occludes the far half, terrain occludes it near the horizon.
// _SkyDepth (player report 2026-11-27, see SkyDepth.hlsl): in the SURFACE sky the ring discs and the planet's own ring
// band move into a thin depth band just in front of the far plane, so terrain at ANY distance covers them (the ring
// "grew out of the landscape" in front of the far view). 0 = real depth (the space view, the cockpit hologram).
// Dual-pipeline (URP + Built-in RP), like every other shader here.
Shader "BlocksBeyondTheStars/PlanetRing"
{
    Properties
    {
        _MainTex ("Ring Bands", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SkyDepth ("Sky depth (0 off, 1 by distance, 2 nearest sky layer)", Float) = 0
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
            #include "SkyDepth.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // SRP Batcher (#573): per-MATERIAL properties in UnityPerMaterial (texture handles stay outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _SkyDepth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = BbtsSkyDepth(TransformWorldToHClip(wp), wp, _WorldSpaceCameraPos, _SkyDepth);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(_Color.rgb * t.rgb, _Color.a * t.a);
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
            #include "SkyDepth.hlsl"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _SkyDepth;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = BbtsSkyDepth(UnityObjectToClipPos(v.vertex), wp, _WorldSpaceCameraPos, _SkyDepth);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                return fixed4(_Color.rgb * t.rgb, _Color.a * t.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
