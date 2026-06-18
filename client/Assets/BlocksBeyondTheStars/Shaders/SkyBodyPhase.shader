// Sun-lit celestial-body shader: lights a sphere from a per-material sun direction so a terminator / crescent /
// gibbous / full phase emerges naturally (like the Earth's moon) as the body is viewed from an angle. Used by the
// orbital bodies in the surface sky (SkyBodiesView — fed the LOCAL sun's sky direction) and by the planets/moons in
// the orbit/space view (SpaceView — fed each body's TRUE direction to the system star). A soft terminator and a dim
// "earthshine" floor on the night side keep it readable. Distant, unshadowed ambience: no scene Light, no shadows.
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (HLSL), SubShader 2 the Built-in RP pass (CG).
Shader "BlocksBeyondTheStars/SkyBodyPhase"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        // xyz = world-space direction TO the sun (set per-material each frame). w unused.
        _PhaseSunDir ("Sun Direction", Vector) = (0, 0, 1, 0)
        _Earthshine ("Night-side floor", Range(0, 0.4)) = 0.04
        _TermSoft ("Terminator softness", Range(0.001, 0.4)) = 0.07
        _LimbDark ("Limb darkening", Range(0, 1)) = 0.35
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float4 _PhaseSunDir;
            float _Earthshine;
            float _TermSoft;
            float _LimbDark;

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float2 uv : TEXCOORD1; float3 vn : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.vn = TransformWorldToViewDir(o.wn); // view-space normal for limb darkening
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 L = normalize(_PhaseSunDir.xyz);
                float ndl = dot(N, L);
                // Soft day/night boundary → a smooth terminator (the lit fraction of the visible disc IS the phase).
                float lit = smoothstep(-_TermSoft, _TermSoft, ndl);
                float3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 col = _Color.rgb * tex * (_Earthshine + (1.0 - _Earthshine) * lit);
                // Limb darkening: dim the disc edge (normal facing away from the viewer) for a rounder, less flat read.
                float facing = saturate(normalize(i.vn).z * -1.0 + 1.0); // ~1 at centre, ~0 at the rim
                col *= lerp(1.0 - _LimbDark, 1.0, saturate(facing));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _PhaseSunDir;
            float _Earthshine;
            float _TermSoft;
            float _LimbDark;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float2 uv : TEXCOORD1; float3 vn : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.vn = mul((float3x3)UNITY_MATRIX_V, o.wn);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 L = normalize(_PhaseSunDir.xyz);
                float ndl = dot(N, L);
                float lit = smoothstep(-_TermSoft, _TermSoft, ndl);
                fixed3 tex = tex2D(_MainTex, i.uv).rgb;
                fixed3 col = _Color.rgb * tex * (_Earthshine + (1.0 - _Earthshine) * lit);
                float facing = saturate(normalize(i.vn).z * -1.0 + 1.0);
                col *= lerp(1.0 - _LimbDark, 1.0, saturate(facing));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "BlocksBeyondTheStars/LitColor"
}
