// Fresnel energy shell on a sphere (VFX overhaul #2152) — one shader for the ship's shield bubble, scanner shells
// and the re-entry plasma sheath. Additive (Blend One One), both faces drawn so it also reads from inside (a scan
// shell expanding around the camera):
//   * rim     — pow(1 - |N·V|, _RimPower): the bright silhouette edge of every energy bubble;
//   * hex     — a procedural hexagon grid on the sphere (_Hex) that lights its cell borders (the shield look);
//   * hits    — up to 8 impact ripples (_FxHits, object-space direction + start time, set per renderer through a
//               MaterialPropertyBlock): a bright spot at the impact and a ring racing away from it over the sphere;
//   * plasma  — flowing noise streaks along the object's -Z (_Plasma): the heat sheath of atmosphere entry, hottest
//               on the leading +Z face.
// _FxHits / _FxHitCount / _FxHitParams are loose uniforms (arrays cannot be material properties), so only the
// shield renderer that carries a property block pays for them.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxShell"
{
    Properties
    {
        _Color ("Colour (a = fade)", Color) = (0.35, 0.8, 1, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 1.6
        _RimPower ("Rim power", Range(0.5, 8)) = 3
        _Fill ("Flat fill", Range(0, 1)) = 0.02
        _Hex ("Hex grid", Range(0, 1)) = 0
        _HexScale ("Hex cells around", Range(4, 64)) = 22
        _Plasma ("Plasma sheath", Range(0, 1)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _RimPower;
                float _Fill;
                float _Hex;
                float _HexScale;
                float _Plasma;
            CBUFFER_END

            float4 _FxHits[8];   // xyz object-space unit direction of the impact, w = _Time.y when it hit
            float _FxHitCount;   // live entries in _FxHits
            float4 _FxHitParams; // x ring speed (radians/s), y ring width (radians), z life (s)

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; float3 op : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.op = v.positionOS.xyz;
                return o;
            }

            // Distance to the nearest hexagon border, 0 at the border → ~0.5 in the cell centre.
            float HexBorder(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float2 a = p - s * floor(p / s) - s * 0.5;
                float2 b = (p - s * 0.5) - s * floor((p - s * 0.5) / s) - s * 0.5;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                float2 q = abs(g);
                return 0.5 - max(dot(q, s * 0.5), q.x);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float nv = abs(dot(N, V));
                float rim = pow(saturate(1.0 - nv), _RimPower);
                float3 p = normalize(i.op);

                float hexLine = 0.0;
                if (_Hex > 0.0)
                {
                    float2 sph = float2(atan2(p.z, p.x) / 6.28318 + 0.5, acos(clamp(p.y, -1.0, 1.0)) / 3.14159);
                    float border = HexBorder(sph * float2(_HexScale, _HexScale * 0.5));
                    hexLine = (1.0 - smoothstep(0.02, 0.09, border)) * _Hex;
                }

                float hit = 0.0;
                int count = (int)_FxHitCount;
                float life = max(0.05, _FxHitParams.z);
                [loop]
                for (int k = 0; k < 8; k++)
                {
                    if (k >= count)
                    {
                        break;
                    }

                    float age = _Time.y - _FxHits[k].w;
                    if (age < 0.0 || age > life)
                    {
                        continue;
                    }

                    float fade = 1.0 - age / life;
                    float ang = acos(clamp(dot(p, normalize(_FxHits[k].xyz)), -1.0, 1.0));
                    float front = age * _FxHitParams.x;
                    float ringD = (ang - front) / max(0.02, _FxHitParams.y);
                    hit += exp(-ringD * ringD) * fade;              // the ripple racing over the shell
                    hit += exp(-ang * ang * 18.0) * fade * fade * 2.0; // the bright impact spot
                }

                float plasma = 0.0;
                if (_Plasma > 0.0)
                {
                    float lead = saturate(p.z * 0.5 + 0.5);
                    float ang = atan2(p.y, p.x);
                    float streak = BbtsNoise(float2(ang * 4.0, p.z * 3.0 + _Time.y * 6.0));
                    streak *= BbtsNoise(float2(ang * 9.0 + 3.0, p.z * 6.0 + _Time.y * 11.0));
                    plasma = (lead * lead * 1.4 + streak * 1.6 * (1.0 - lead * 0.5)) * _Plasma;
                }

                float glow = rim * (0.55 + hexLine * 1.4) + _Fill + hit * (0.8 + hexLine * 2.5) + plasma;
                float3 col = _Color.rgb * glow;
                return half4(col * (_Intensity * _Color.a), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}
