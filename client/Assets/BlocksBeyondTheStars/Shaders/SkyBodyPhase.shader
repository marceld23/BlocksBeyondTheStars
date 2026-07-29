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
        _Earthshine ("Night-side floor", Range(0, 0.4)) = 0.2
        _TermSoft ("Terminator softness", Range(0.001, 0.4)) = 0.07
        _LimbDark ("Limb darkening", Range(0, 1)) = 0.35
        // 0 = pure sun-lit phase (space view + night surface). 1 = fully front-lit visible disc. The surface sky
        // (SkyBodiesView) ramps this up with daylight: by day the sun and every visible body share the upper
        // hemisphere, so the pure phase shows only the unlit far side ("new moon") and reads as a black
        // silhouette against the bright sky. Front-lighting it by day keeps the body a visible feature; the
        // true crescent/phase is preserved at night/twilight where it looks right. Default 0 → space view unchanged.
        _DayLight ("Daytime front-lit blend", Range(0, 1)) = 0
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
            float _DayLight;
            float4 _Sc_Sky; // global: current sky colour (linear, set by Sky.cs) — daytime atmosphere wash

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float2 uv : TEXCOORD1; float3 wp : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp; // world position → world-space view dir for limb darkening
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 L = normalize(_PhaseSunDir.xyz);
                float ndl = dot(N, L);
                // Crisp terminator (the lit fraction of the visible disc IS the phase) over a soft wrap floor so
                // the night side / day-time bodies read as a faint disc instead of a pure-black silhouette.
                float lit = smoothstep(-_TermSoft, _TermSoft, ndl);
                float wrap = saturate(ndl * 0.5 + 0.5);
                float shade = max(lit, wrap * wrap * 0.35);
                // Daytime: lift the sun-lit phase toward a fully front-lit disc so a back-lit body is a visible
                // lit disc (limb darkening still gives it round depth) instead of a black silhouette. At night
                // (_DayLight → 0) the crisp phase is untouched.
                shade = lerp(shade, 1.0, _DayLight);
                float3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 col = _Color.rgb * tex * (_Earthshine + (1.0 - _Earthshine) * shade);
                // Limb darkening via the world-space view direction: bright at the disc centre (normal toward the
                // camera), dimmer at the rim — a rounder read. (The old view-space-z form was inverted.)
                float3 Vw = normalize(_WorldSpaceCameraPos - i.wp);
                float facing = saturate(dot(N, Vw)); // ~1 at centre, ~0 at the rim
                col *= lerp(1.0 - _LimbDark, 1.0, facing);
                // Daytime atmosphere wash (#585): the disc's albedo tops out at a fraction of the daytime sky's
                // brightness, so even front-lit it read as a dark silhouette. The air in front of the body
                // scatters the sky colour over it — wash toward the sky (like the real daytime Moon: pale,
                // sky-tinted, never black). Scaled by the sky's own luminance so airless (black-sky) worlds and
                // the night sky get no wash, and gated by _DayLight so the space view (0) is untouched.
                float skyLum = dot(_Sc_Sky.rgb, float3(0.299, 0.587, 0.114));
                col = lerp(col, _Sc_Sky.rgb * 1.05, _DayLight * 0.55 * saturate(skyLum * 4.0));
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
            float _DayLight;
            float4 _Sc_Sky; // global: current sky colour (linear, set by Sky.cs) — daytime atmosphere wash

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float2 uv : TEXCOORD1; float3 wp : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; // world pos for world-space view dir limb darkening
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 L = normalize(_PhaseSunDir.xyz);
                float ndl = dot(N, L);
                float lit = smoothstep(-_TermSoft, _TermSoft, ndl);
                float wrap = saturate(ndl * 0.5 + 0.5);
                float shade = max(lit, wrap * wrap * 0.35);
                // Daytime front-lit lift (see URP pass) so back-lit bodies read as a lit disc, not a black
                // silhouette; the night phase (_DayLight → 0) is untouched.
                shade = lerp(shade, 1.0, _DayLight);
                fixed3 tex = tex2D(_MainTex, i.uv).rgb;
                fixed3 col = _Color.rgb * tex * (_Earthshine + (1.0 - _Earthshine) * shade);
                float3 Vw = normalize(_WorldSpaceCameraPos - i.wp);
                float facing = saturate(dot(N, Vw)); // ~1 at centre, ~0 at the rim
                col *= lerp(1.0 - _LimbDark, 1.0, facing);
                // Daytime atmosphere wash (#585) — see the URP pass: sky-luminance-scaled blend toward the sky
                // colour so day-sky bodies read pale instead of silhouette-dark; no-op in space (_DayLight = 0).
                float skyLum = dot(_Sc_Sky.rgb, float3(0.299, 0.587, 0.114));
                col = lerp(col, _Sc_Sky.rgb * 1.05, _DayLight * 0.55 * saturate(skyLum * 4.0));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "BlocksBeyondTheStars/LitColor"
}
