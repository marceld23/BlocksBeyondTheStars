// The micro-fauna as small bodies (CritterBodies.cs): a butterfly, a beetle or a little fish is a
// handful of boxes and plates in plain colours instead of one flat sprite that always faces the camera. No texture:
// the vertex colour is the part's colour (rgb, linear) and its own glow (a). Lit like the debris chips (FxDebris): the
// world's sun (_Sc_SunDir / _Sc_Light), an ambient floor, the FX lights, the haze — so a critter darkens at night and
// in a storm with the ground it sits on.
//
// The motion is in the vertex stage, from the time and a few values per vertex, so it costs the CPU nothing per frame
// and every critter shares ONE material (no property blocks):
//   TEXCOORD0  x = wing side (-1 the left wing, +1 the right one, 0 = no wing), y = wag weight 0..1 (a fish's tail, a
//              caterpillar's body, a gas bag's threads), zw = the wing's hinge (x, y) in the body's space
//   TEXCOORD1  x = the critter's own phase, y = beats (radians per second), z = a wing's highest angle (radians) or a
//              wag's reach (body units), w = a wing's lowest angle
// A wing turns about the body's long axis (z) through its hinge between its lowest and its highest angle; a wagging
// part sways sideways in a wave that runs down the body. Plates are single quads drawn from both sides (Cull Off), the
// normal turned toward the eye.
Shader "BlocksBeyondTheStars/CritterBody"
{
    Properties
    {
        _Ambient ("Ambient floor", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"
            #include "AtmosphereCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Ambient;
            CBUFFER_END

            float4 _Sc_Light;  // global day/night × sun colour (a > 0.5 = set)
            float4 _Sc_SunDir; // global world-space direction to the sun

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 part : TEXCOORD0;
                float4 beat : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wn : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float3 n = v.normal;
                float t = _Time.y * v.beat.y + v.beat.x;
                if (abs(v.part.x) > 0.5)
                {
                    float angle = lerp(v.beat.w, v.beat.z, sin(t) * 0.5 + 0.5) * v.part.x;
                    float s = sin(angle);
                    float c = cos(angle);
                    float2 rel = p.xy - v.part.zw;
                    p.xy = v.part.zw + float2(rel.x * c - rel.y * s, rel.x * s + rel.y * c);
                    n.xy = float2(n.x * c - n.y * s, n.x * s + n.y * c);
                }
                else if (v.part.y > 0.0)
                {
                    p.x += sin(t - p.z * 4.0) * v.part.y * v.beat.z;
                }

                float3 wp = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(n);
                o.wp = wp;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 N = normalize(i.wn) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float3 L = (dot(_Sc_SunDir.xyz, _Sc_SunDir.xyz) > 0.01) ? normalize(_Sc_SunDir.xyz) : normalize(float3(0.4, 0.8, -0.45));
                float ndl = saturate(dot(N, L));
                float3 albedo = i.color.rgb;
                float3 col = albedo * light * (_Ambient + (1.0 - _Ambient) * ndl * BbtsCloudShade(i.wp));
                col += BbtsFxLights(i.wp, N, albedo);
                col += albedo * (i.color.a * 2.0);
                col = BbtsApplyHaze(col, i.wp, 1.0);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/VertexColorOpaque"
}
