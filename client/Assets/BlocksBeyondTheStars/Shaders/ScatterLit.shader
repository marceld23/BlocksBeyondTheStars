// GPU-instanced lit shader for the ground-detail scatter layer (T0: grass tufts strewn on natural open ground; the
// pebbles are gone, #3120). Albedo comes from the mesh vertex colour (the blades, darker at the root) re-tinted with the
// world's grass colour. #3120: a tuft is lit like the ground it stands on — the meadow's light (MeadowLight.hlsl, the
// block shader's upward-face light: skylight ambient, the cool night floor, the sun with its real-time shadow and cloud
// shade, placed block light, the headlamp, the shared haze), fed per instance with the skylight and the placed block
// light of its cell (_ScatterLight). It used to take the sun colour only, so at night a tuft went black right beside a
// torch-lit block. #3063: a DepthOnly pass puts the tufts into the depth prepass; they still cast no shadows.
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
        Cull Off // thin two-sided blades read from both sides
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // #3063: receive the sun's real-time shadow map (the block shader's keywords).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): the shared haze (#2393) + cloud shadows (#2394)
            #include "MeadowLight.hlsl"      // #3120: the ground's light (declares the sun + headlamp globals)

            float4 _Sc_GrassTint; // #2835: the world's grass-tuft colour (a > 0.5 = set); GameBootstrap.RebuildFloraTints
            float4 _Sc_Season;    // #3042: x = autumn, y = winter (Sky.cs, per frame; FloraAccent.hlsl turns the plants the same way)

            // #3120: per tuft — rgb = the placed block light of its cell (display-ready, 0 = none), a = its skylight.
            UNITY_INSTANCING_BUFFER_START(BbtsScatterProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ScatterLight)
            UNITY_INSTANCING_BUFFER_END(BbtsScatterProps)

            // #2835: a tuft (vertex alpha 1) takes the world's grass colour like the flora shader re-tints a plant —
            // luminance x tint, 85 % — so the ground tufts match the grass billboards.
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
                // #3042: the season turns the tufts with the plants — straw-coloured in autumn, pale in winter.
                float3 g = _Sc_GrassTint.rgb;
                float lg = dot(g, float3(0.299, 0.587, 0.114));
                g = lerp(g, lg * float3(1.9, 1.35, 0.4), saturate(_Sc_Season.x) * 0.55);
                g = lerp(g, lg * float3(1.05, 1.1, 1.2), saturate(_Sc_Season.y) * 0.5);
                return lerp(c.rgb, lum * g * 1.6, 0.85);
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
                float3 wp : TEXCOORD0;
                float4 light : TEXCOORD1; // rgb block light, a skylight (per tuft)
                float tip : TEXCOORD2;    // 0 at the root .. 1 at a blade's tip (how much sun shines through)
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                // #2397: tufts sway like the plants (rooted at the base, free at the top).
                wp += BbtsWindSway(wp, false, true, saturate(v.positionOS.y / 0.4));
                o.positionCS = TransformWorldToHClip(wp);
                o.color = v.color;
                o.wp = wp;
                o.light = UNITY_ACCESS_INSTANCED_PROP(BbtsScatterProps, _ScatterLight);
                o.tip = saturate(v.positionOS.y / 0.34);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = BbtsMeadowShade(ScatterBase(i.color), i.wp, i.light.a, i.light.rgb, 0.6 * i.tip);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // #3063: the depth prepass. URP draws only shaders that have this pass into a prepass (WebGL with MSAA, depth
        // priming, effects that need depth before the opaques), so without it the tufts were missing from the depth
        // texture. Same instanced transform and wind sway as the forward pass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl" // the tufts' wind sway

            struct DAttr
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DVary
            {
                float4 positionCS : SV_POSITION;
            };

            DVary depthVert(DAttr v)
            {
                DVary o = (DVary)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                wp += BbtsWindSway(wp, false, true, saturate(v.positionOS.y / 0.4));
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }

            half4 depthFrag(DVary i) : SV_Target
            {
                return 0;
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
            float _Sc_DarkFloor;  // #3265: the flat dark floor (0 = unset → 0.045), see AtmosphereCommon's BbtsDarkFloor
            float4 _Sc_GrassTint; // #2835: see the URP pass
            float4 _Sc_Season;    // #3042: see the URP pass

            // #3120: see the URP pass — rgb block light, a skylight.
            UNITY_INSTANCING_BUFFER_START(BbtsScatterProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ScatterLight)
            UNITY_INSTANCING_BUFFER_END(BbtsScatterProps)

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
                // #3042: the season turns the tufts with the plants — straw-coloured in autumn, pale in winter.
                float3 g = _Sc_GrassTint.rgb;
                float lg = dot(g, float3(0.299, 0.587, 0.114));
                g = lerp(g, lg * float3(1.9, 1.35, 0.4), saturate(_Sc_Season.x) * 0.55);
                g = lerp(g, lg * float3(1.05, 1.1, 1.2), saturate(_Sc_Season.y) * 0.5);
                return lerp(c.rgb, lum * g * 1.6, 0.85);
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
                float4 light : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.light = UNITY_ACCESS_INSTANCED_PROP(BbtsScatterProps, _ScatterLight);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // The meadow light without shadows: skylight ambient + the sun on an upward face + placed block light.
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;
                float sky = saturate(i.light.a);
                float ndl = saturate(normalize(_Sc_SunDir.xyz).y);
                float3 albedo = ScatterBase(i.color);
                // #3265: AtmosphereCommon's BbtsSkyAmbient ramp and dark floor, repeated (CG cannot include it).
                float amb = lerp(0.32, 0.78, sky) - 0.30 * saturate(1.0 - sky / 0.55);
                float darkFloor = _Sc_DarkFloor > 0.0 ? _Sc_DarkFloor : 0.045;
                float3 col = albedo * (light * (amb + 0.5 * ndl * saturate(sky * 2.0)) + darkFloor) + albedo * i.light.rgb;
                fixed4 outc = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }
}
