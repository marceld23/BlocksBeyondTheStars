// Far terrain (#1820): the low-resolution horizon beyond the streamed chunks. Vertex-coloured, lit by the same Sky
// globals as the block atlas (_Sc_Light, _Sc_SunDir, _Sc_Sky) and hazed by the same _Sc_Fog, so the far view blends
// into the real terrain. Two discards keep it out of the way:
//   * the column mask (_Sc_FarMask, one texel per chunk column around the player): a column where a real chunk holding
//     the surface is drawn never shows far terrain on top of it;
//   * the level's inner radius (TEXCOORD0.x, blocks from _Sc_FarCenter): the coarse level stays out of the near disc.
// No Properties block (only globals), so the SRP Batcher needs no UnityPerMaterial cbuffer here.
Shader "BlocksBeyondTheStars/FarTerrain"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Sc_FarMask); SAMPLER(sampler_Sc_FarMask);
        float4 _Sc_FarMaskParams; // xy = mask origin (scene blocks), z = 1 / mask size in blocks, w = on
        float4 _Sc_FarCenter;     // xyz = player, w = far-view range

        // Discards far-terrain fragments that real chunks or the finer level cover.
        void FarClip(float3 wp, float inner)
        {
            if (_Sc_FarMaskParams.w > 0.5)
            {
                float2 uv = (wp.xz - _Sc_FarMaskParams.xy) * _Sc_FarMaskParams.z;
                if (uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0)
                {
                    float covered = SAMPLE_TEXTURE2D_LOD(_Sc_FarMask, sampler_Sc_FarMask, uv, 0).r;
                    clip(0.5 - covered);
                }
            }

            if (inner > 0.0)
            {
                float2 d = wp.xz - _Sc_FarCenter.xz;
                clip(dot(d, d) - inner * inner);
            }
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Fog;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;     // rgb = albedo, a = emissive (lava)
                float2 uv : TEXCOORD0;    // x = inner discard radius, y = level
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float4 color : TEXCOORD2;
                float inner : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.inner = v.uv.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                FarClip(i.wp, i.inner);

                float3 albedo = i.color.rgb;
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float3 N = normalize(i.wn);
                float ndl = saturate(dot(N, normalize(_Sc_SunDir.xyz)));
                // Open sky everywhere out there: the block shader's sky-lit ambient + half-weight direct sun.
                float3 col = albedo * (light * (0.78 + 0.5 * ndl) + 0.05);
                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * nightFloor;
                col += albedo * i.color.a * 2.0; // lava seas glow

                if (_Sc_Fog.w > 0.5)
                {
                    float camDist = distance(i.wp, _WorldSpaceCameraPos);
                    float haze = saturate((camDist - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x)) * _Sc_Fog.z;
                    float3 hazeCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                    col = lerp(col, hazeCol, haze);
                    col += albedo * i.color.a * haze; // a lava glow still reads at the edge of the view
                }

                return half4(col, 1);
            }
            ENDHLSL
        }

        // Depth prepass (URP schedules one on WebGL with MSAA, and for depth priming): the same discards, so the depth
        // texture never holds far terrain where real chunks stand.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag

            struct DAttr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct DVary { float4 positionCS : SV_POSITION; float3 wp : TEXCOORD0; float inner : TEXCOORD1; };

            DVary depthVert(DAttr v)
            {
                DVary o;
                o.wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.inner = v.uv.x;
                return o;
            }

            half4 depthFrag(DVary i) : SV_Target
            {
                FarClip(i.wp, i.inner);
                return 0;
            }
            ENDHLSL
        }
    }
}
