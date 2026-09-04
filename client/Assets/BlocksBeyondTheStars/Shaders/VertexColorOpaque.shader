// Minimal opaque shader that renders mesh vertex colours (the chunk mesher + creatures bake per-vertex
// colour/shading, so no textures are needed for the blocky look, M21). Tinted by the global day/night sun.
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (UniversalForward — receives the sun shadow with a
// gentle floor — plus a ShadowCaster so vertex-coloured models like creatures CAST shadows); SubShader 2 is the
// original Built-in RP pass (unchanged). The active pipeline picks the matching SubShader.
Shader "BlocksBeyondTheStars/VertexColorOpaque"
{
    Properties
    {
        // Optional atlas for the build editors (#1400). A mesh opts in per vertex through TEXCOORD1.x (1 = sample
        // the atlas at TEXCOORD0, 0 = plain vertex colour), so creatures and every other vertex-colour-only mesh
        // (no TEXCOORD1 → weight 0) render exactly as before.
        _MainTex ("Texture (optional)", 2D) = "white" {}
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _Sc_Light; // global day/night × sun-colour × weather tint (alpha>0.5 = set)
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 tex : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 wp : TEXCOORD0; float3 uvw : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.color = v.color;
                o.wp = wp;
                o.uvw = float3(v.uv, v.tex.x); // atlas uv + sample weight (#1400)
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 l = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));
                float3 tex = lerp(float3(1, 1, 1), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uvw.xy).rgb, saturate(i.uvw.z));
                float3 col = i.color.rgb * tex * l * lerp(0.55, 1.0, shadow); // shadowed models dim, never black
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

        // #1518: depth prepass (see BlockAtlas) — vertex-coloured models (creatures, ships) must land in
        // _CameraDepthTexture where URP draws a prepass instead of copying depth (WebGL + MSAA, depth priming).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DAttr { float4 positionOS : POSITION; };
            struct DVary { float4 positionCS : SV_POSITION; };

            DVary depthVert(DAttr v)
            {
                DVary o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 depthFrag(DVary i) : SV_Target { return 0; }
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
                float2 uv : TEXCOORD0;
                float2 tex : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 uvw : TEXCOORD0; // atlas uv + sample weight (#1400)
            };

            fixed4 _Sc_Light; // global day/night × sun-colour × weather tint (alpha>0.5 = set)
            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uvw = float3(v.uv, v.tex.x);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 l = _Sc_Light;
                if (l.a < 0.5) l = fixed4(1, 1, 1, 1); // default to no tint until set
                fixed3 tex = lerp(fixed3(1, 1, 1), tex2D(_MainTex, i.uvw.xy).rgb, saturate(i.uvw.z));
                return fixed4(i.color.rgb * tex * l.rgb, i.color.a);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
