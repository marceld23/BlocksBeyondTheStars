// Minimal opaque shader that renders mesh vertex colours (the chunk mesher + creatures bake per-vertex
// colour/shading, so no textures are needed for the blocky look, M21). Tinted by the global day/night sun.
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (UniversalForward — receives the sun shadow with a
// gentle floor — plus a ShadowCaster so vertex-coloured models like creatures CAST shadows); SubShader 2 is the
// original Built-in RP pass (unchanged). The active pipeline picks the matching SubShader.
Shader "BlocksBeyondTheStars/VertexColorOpaque"
{
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _Sc_Light; // global day/night × sun-colour × weather tint (alpha>0.5 = set)

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 wp : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.color = v.color;
                o.wp = wp;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 l = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));
                float3 col = i.color.rgb * l * lerp(0.55, 1.0, shadow); // shadowed models dim, never black
                return half4(col, i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttr { float4 positionOS : POSITION; float3 normal : NORMAL; };
            struct SVary { float4 positionCS : SV_POSITION; };

            SVary shadowVert(SAttr v)
            {
                SVary o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(v.normal);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 shadowFrag(SVary i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
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

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Sc_Light; // global day/night × sun-colour × weather tint (alpha>0.5 = set)

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 l = _Sc_Light;
                if (l.a < 0.5) l = fixed4(1, 1, 1, 1); // default to no tint until set
                return fixed4(i.color.rgb * l.rgb, i.color.a);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
