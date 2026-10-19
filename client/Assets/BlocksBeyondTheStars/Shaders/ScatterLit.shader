// GPU-instanced lit shader for the ground-detail scatter layer (T0: grass tufts + pebbles strewn on open
// ground). Albedo comes from the mesh vertex colour (tufts baked green, pebbles grey); lighting reuses the
// same sun globals as the block atlas (_Sc_Light colour, _Sc_SunDir), so scatter reads under the same sun as
// the terrain. Cheap N·L diffuse + an ambient floor — no shadows/normal maps (it's tiny throwaway detail).
//
// DUAL-PIPELINE like BlockAtlas: SubShader 1 = URP (HLSL), SubShader 2 = Built-in (CG). Both declare GPU
// instancing so Graphics.DrawMeshInstanced replicates the mesh across per-chunk instance matrices in one call.
Shader "BlocksBeyondTheStars/ScatterLit"
{
    Properties { }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off // tiny two-sided quads (grass) read from both sides
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): the shared haze (#2393) + cloud shadows (#2394)

            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_GrassTint; // #2835: the world's grass-tuft colour (a > 0.5 = set); GameBootstrap.RebuildFloraTints

            // #2835: a tuft (vertex alpha 1) takes the world's grass colour like the flora shader re-tints a plant —
            // luminance x tint, 85 % — so the ground tufts match the grass billboards; pebbles (alpha 0) keep theirs.
            float3 ScatterBase(float4 c)
            {
                if (c.a < 0.5 || _Sc_GrassTint.a < 0.5)
                {
                    return c.rgb;
                }

                // Vertex colours are not linearised by Unity; the flora shader takes the luminance of the LINEAR
                // texel, so square the colour first in a linear project, or the tufts glow 2.5x brighter than the grass.
#ifdef UNITY_COLORSPACE_GAMMA
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
#else
                float lum = dot(c.rgb * c.rgb, float3(0.299, 0.587, 0.114));
#endif
                return lerp(c.rgb, lum * _Sc_GrassTint.rgb * 1.6, 0.85);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float3 wn : TEXCOORD0;
                float3 wp : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                // #2397: tufts sway like the plants (rooted at the base, free at the top).
                wp += BbtsWindSway(wp, false, true, saturate(v.positionOS.y / 0.4));
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.wp = wp;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float3 N = normalize(i.wn);
                float ndl = saturate(dot(N, normalize(_Sc_SunDir.xyz))) * BbtsCloudShade(i.wp);
                float3 col = ScatterBase(i.color) * light * (0.45 + 0.55 * ndl);
                col = BbtsApplyHaze(col, i.wp, 1.0); // scatter sits on open ground
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_GrassTint; // #2835: see the URP pass

            float3 ScatterBase(float4 c)
            {
                if (c.a < 0.5 || _Sc_GrassTint.a < 0.5)
                {
                    return c.rgb;
                }

                // Vertex colours are not linearised by Unity; the flora shader takes the luminance of the LINEAR
                // texel, so square the colour first in a linear project, or the tufts glow 2.5x brighter than the grass.
#ifdef UNITY_COLORSPACE_GAMMA
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
#else
                float lum = dot(c.rgb * c.rgb, float3(0.299, 0.587, 0.114));
#endif
                return lerp(c.rgb, lum * _Sc_GrassTint.rgb * 1.6, 0.85);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 wn : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;
                float3 N = normalize(i.wn);
                float ndl = saturate(dot(N, normalize(_Sc_SunDir.xyz)));
                fixed4 outc = fixed4(ScatterBase(i.color) * light * (0.45 + 0.55 * ndl), 1);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }
}
