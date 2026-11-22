// A creature's fur and feathers as geometry (CoatForms / CreatureBuilder.Coat3d). A coat mesh hangs under a
// body part; this shader gives every blade the colour of the animal's own coat under its root (the part's texture and
// colour, so patterns, the mood tint and the glow reach the fur), lights it like the body (LitColor's light, copied),
// lets the blades trail the movement and flutter in the wind, shrinks the short felt away at the end of its range and
// folds a wing's flight feathers along the arm.
//
// Drawn right after the opaques (queue 2501) with no depth-only pass: the coat stays out of the depth the ambient
// occlusion reads (fine blades flicker there, as the grass carpet did) and takes the occlusion of the body beneath.
// It casts no shadow — the body does.
Shader "BlocksBeyondTheStars/CreatureCoat"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _Floor ("Ambient floor", Range(0, 1)) = 0.62
        _Fill ("Fill light", Range(0, 1)) = 0.3
        _Glow ("Glow (alpha 0 texels)", Range(0, 4)) = 0
        _CoatAccent ("Accent colour (feather tips)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest+51" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FxCommon.hlsl"
            #include "AtmosphereCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // Per renderer (MaterialPropertyBlock, CoatMotion): outside UnityPerMaterial like LitColor's _HitFlash.
            float _HitFlash;
            float4 _CoatUv;     // xy = the animal's coat placement shift, z = 1 mirrors u
            float4 _CoatMotion; // xyz = how far a blade's tip leans (in blade lengths, world space), w = the wing's fold 0..1

            float _Sc_ModelLight;
            float4 _Sc_LookCoat; // x = the felt's range in metres, y = the band at its end over which it shrinks away

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Floor;
                float _Fill;
                float _Glow;
                float4 _CoatAccent;
            CBUFFER_END

            #include "Headlamp.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;     // r = brightness / 1.25, g = accent weight, b = dark weight, a = bend (0 root … tip)
                float4 coat : TEXCOORD0;  // xy = the coat's texture coordinate under the root, z = phase, w = 1: felt (fades with range)
                float4 root : TEXCOORD1;  // xyz = the blade's root (object space), w = a feather's fold angle
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wn : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 tone : TEXCOORD3;
                float3 shadowAt : TEXCOORD4; // the root, a little off the skin: the whole blade takes one shadow value
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 rel = v.positionOS.xyz - v.root.xyz;
                float3 n = v.normal;
                float fold = v.root.w * _CoatMotion.w;
                if (abs(fold) > 1e-4)
                {
                    // Unity's turn about Y: a flight feather lays itself along the arm as the wing folds.
                    float s, c;
                    sincos(fold, s, c);
                    rel = float3(rel.x * c + rel.z * s, rel.y, -rel.x * s + rel.z * c);
                    n = float3(n.x * c + n.z * s, n.y, -n.x * s + n.z * c);
                }

                float3 rootW = TransformObjectToWorld(v.root.xyz);
                float3 relW = mul((float3x3)UNITY_MATRIX_M, rel);
                if (v.coat.w > 0.5)
                {
                    float d = distance(rootW, _WorldSpaceCameraPos);
                    relW *= saturate((_Sc_LookCoat.x - d) / max(_Sc_LookCoat.y, 0.1));
                }

                // The tip trails the part's movement and trembles in the wind; the root stays on the skin.
                float3 lean = _CoatMotion.xyz;
                if (_Sc_Wind.w > 0.5)
                {
                    float wind = length(_Sc_Wind.xz);
                    float2 dir = wind > 0.01 ? _Sc_Wind.xz / wind : float2(0.7071, 0.7071);
                    float phase = v.coat.z * 6.2832 + rootW.x * 1.7 + rootW.z * 2.3;
                    float t = _Sc_Wind.y;
                    float wave = sin(t * 2.3 + phase) * 0.6 + sin(t * 3.7 + phase * 1.3) * 0.4;
                    lean += float3(dir.x, 0.15, dir.y) * (wave * 0.14 * (0.35 + 0.65 * saturate(wind)));
                }

                float3 wp = rootW + relW + lean * (v.color.a * length(relW));
                float3 wn = TransformObjectToWorldNormal(n);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = wn;
                float2 uv = v.coat.xy;
                uv.x = lerp(uv.x, -uv.x, saturate(_CoatUv.z));
                o.uv = TRANSFORM_TEX(uv + _CoatUv.xy, _MainTex);
                o.wp = wp;
                o.tone = v.color;
                o.shadowAt = rootW + wn * 0.03;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toEye = normalize(_WorldSpaceCameraPos - i.wp);
                float3 N = normalize(i.wn);
                if (dot(N, toEye) < 0.0)
                {
                    N = -N; // a feather seen from below, a blade peeking over the body's edge
                }

                // LitColor's light, term by term, so the coat is lit like the part it grows on.
                float3 L = normalize(float3(0.4, 0.7, -0.55));
                float ndl = saturate(dot(N, L));
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.shadowAt)) * BbtsCloudShade(i.wp);
                float fill = saturate(dot(N, normalize(float3(-0.55, 0.25, 0.5)))) * _Fill;
                // The coat three mip levels down: a blade takes the coat's colour and pattern, not the hide tile's fine grain
                // (drawn strands on a tile turn into a salt-and-pepper noise when every blade picks one texel of them).
                float4 texel = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 3.0);
                float modelLight = _Sc_ModelLight > 0.0 ? _Sc_ModelLight : 1.0;
                float3 lit = float3(1.0, 1.0, 1.0) * (_Floor + (1.1 - _Floor) * ndl * shadow + fill);
                if (_Sc_LookModel > 0.0)
                {
                    float3 sunDir = normalize(_Sc_LookSunDir.xyz);
                    float sunNdl = saturate(dot(N, sunDir));
                    float3 ambHue = lerp(_Sc_AmbGround.rgb, _Sc_AmbSky.rgb, saturate(N.y * 0.5 + 0.5)) / max(_Sc_LookSunHue.a, 1e-3);
                    float3 labLit = ambHue * (_Floor + 0.5 * _Fill) + _Sc_LookSunHue.rgb * ((1.1 - _Floor) * sunNdl * shadow);
                    float towardSun = saturate(dot(-toEye, sunDir));
                    float rim = pow(1.0 - saturate(dot(N, toEye)), 3.0) * (0.35 + 0.65 * (towardSun * 0.5 + 0.5));
                    labLit += _Sc_LookSunHue.rgb * (_Sc_Look3.w * rim * (0.4 + 0.6 * shadow));
                    // Thin tips let the low sun through: the bright fringe of a coat seen against the light.
                    labLit += _Sc_LookSunHue.rgb * (pow(towardSun, 3.0) * 0.5 * i.tone.a * shadow);
                    lit = lerp(lit, labLit, saturate(_Sc_LookModel));
                }

                // The slots: the coat under the root, the accent (a feather's tip or band), the dark keel.
                float3 albedo = _Color.rgb * texel.rgb;
                float grain = dot(texel.rgb, float3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, _CoatAccent.rgb * (0.55 + 0.45 * grain), saturate(i.tone.g));
                albedo *= (1.0 - 0.45 * saturate(i.tone.b)) * (i.tone.r * 1.25);

                float3 col = albedo * (lit * modelLight);
                col += BbtsHeadlamps(i.wp, N, toEye, albedo, float3(0.04, 0.04, 0.04), 0.7);
                col += BbtsFxLights(i.wp, N, albedo);
                col += albedo * (_Glow * (1.0 - texel.a));
                col = BbtsApplyHaze(col, i.wp, 1.0);
                col += BbtsScanWave(i.wp);
                col = lerp(col, float3(1.6, 1.6, 1.7), saturate(_HitFlash));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
