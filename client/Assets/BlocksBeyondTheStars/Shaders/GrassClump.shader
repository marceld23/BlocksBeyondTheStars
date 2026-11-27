// Grass clumps (GrassClumps.cs): grass as dense clumps of slim, pointed blades on every open grass top near
// the camera — real small bodies instead of the sparse tufts, GPU-instanced per chunk.
//
// FORM WITHOUT COLOUR: the clump mesh carries no colour at all. Every vertex has a brightness
// (grey value) and the weights of two colour slots; the colours come from a palette the world gives:
//   main   = the ground under the clump — the average colour of the block's own top tile (the shared block atlas at its
//            one-texel-per-tile mip), so a clump matches its block on every world and with every texture pack;
//   second = the world's grass-tuft colour (_Sc_GrassTint, turned by the season like the plants), at the ground's
//            brightness — the dry tips and the odd blade of another shade;
//   accent = one of the world's flower colours (per instance) — the seed heads of a clump in bloom.
// Each slot is a small ramp, not grey times one colour: the dark end is cooler, the light end warmer.
// A clump grows out of the ground at the end of its range and under snow (nothing pops), its blades get wider with
// distance so one never falls far below a pixel, and they bend from the root in the wind. Lit like the meadow
// (MeadowLight.hlsl: the block shader's light for an upward face). Never casts shadows. URP only.
Shader "BlocksBeyondTheStars/GrassClump"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "AtmosphereCommon.hlsl" // wind, weather traces, haze and the look's globals

        // Per instance (MaterialPropertyBlock arrays):
        //   _GcTile   xy = the centre of the ground block's top tile (atlas UV), z = skylight of the air above,
        //             w = 1 when the clump is in bloom (its seed heads wear the accent colour)
        //   _GcLight  rgb = placed block light at the cell
        //   _GcAccent rgb = the accent colour (one of the world's flower colours, linear)
        UNITY_INSTANCING_BUFFER_START(BbtsGrassClumpProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _GcTile)
            UNITY_DEFINE_INSTANCED_PROP(float4, _GcLight)
            UNITY_DEFINE_INSTANCED_PROP(float4, _GcAccent)
        UNITY_INSTANCING_BUFFER_END(BbtsGrassClumpProps)

        #include "GpuInstances.hlsl" // #4046: on a buffer draw the instance comes from the graphics card
        void BbtsGrassClumpInstance()
        {
            #if defined(BBTS_GPU_INSTANCES)
                BBTS_GPU_INSTANCE(inst)
                _GcTile = inst.a;
                _GcLight = inst.b;
                _GcAccent = inst.c;
            #endif
        }

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        struct ClumpAttributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;     // r = brightness, g = second-colour weight, b = accent weight, a = bend (0 root … tip)
            float2 uv : TEXCOORD0;    // x = the blade's own wind phase 0..1
            float2 axis : TEXCOORD1;  // the blade's middle line at this vertex (object xz): the blade widens about it
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct ClumpVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 wp : TEXCOORD0;
            float4 tone : TEXCOORD1;   // the vertex colour as above
            float4 main : TEXCOORD2;   // rgb = the ground's colour, a = skylight
            float4 accent : TEXCOORD3; // rgb = the accent colour, a = in bloom
            float3 bl : TEXCOORD4;     // placed block light
            float3 ground : TEXCOORD5; // the point on the ground under this vertex at rest (the shadow is looked up there)
        };

        ClumpVaryings BbtsClumpVert(ClumpAttributes v)
        {
            ClumpVaryings o = (ClumpVaryings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            float4 tile = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassClumpProps, _GcTile);
            float4 light = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassClumpProps, _GcLight);
            float4 accent = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassClumpProps, _GcAccent);

            // The whole clump shares its growth (per instance): it rises out of the ground as one piece.
            float3 root = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float dist = distance(root, _WorldSpaceCameraPos);
            float snow = _Sc_Surface.z > 0.5 ? saturate(_Sc_Surface.y) : 0.0;
            float grow = saturate((_Sc_LookGrass.x - dist) / max(_Sc_LookGrass.y, 0.01)) * (1.0 - 0.8 * snow);
            if (grow < 0.03)
            {
                o.positionCS = float4(2.0, 2.0, 2.0, 1.0); // collapsed to one point outside the view: no fragments
                return o;
            }

            // Calm at a distance (against flicker): `far` runs 0 near … 1 at the far end. Blades get wider with distance so
            // one never falls far below a pixel; the wind's wave is halved and the small flutter stops where it would
            // only be a pixel twitching; the tones draw together and the seed heads blend into the grass, so a far
            // clump is one calm shade instead of sparkling pixels.
            float far = smoothstep(6.0, 18.0, dist);
            float3 p = v.positionOS.xyz;
            p.xz = v.axis + (p.xz - v.axis) * (1.0 + dist * 0.16);
            p.y *= grow * _Sc_LookGrass.z;
            float3 wp = TransformObjectToWorld(p);

            // The wind: the plants' slow wave bends every blade from its root, plus a small flutter of its own.
            float bend = v.color.a * grow;
            wp += BbtsWindSway(wp, false, true, bend) * (0.55 * BbtsSwayShelter(tile.z) * lerp(1.0, 0.5, far));
            float flutter = 1.0 - smoothstep(3.0, 9.0, dist);
            if (_Sc_Wind.w > 0.5 && flutter > 0.0)
            {
                float strength = length(_Sc_Wind.xz);
                float2 dir = strength > 1e-4 ? _Sc_Wind.xz / strength : float2(0.7, 0.7);
                wp.xz += dir * (sin(_Sc_Wind.y * 3.3 + v.uv.x * 6.2832 + root.x * 1.7 + root.z * 2.3)
                    * 0.012 * bend * flutter * BbtsSwayShelter(tile.z));
            }

            o.positionCS = TransformWorldToHClip(wp);
            o.wp = wp;
            float3 rest = TransformObjectToWorld(float3(v.positionOS.x, 0.0, v.positionOS.z));
            o.ground = float3(rest.x, root.y + 0.03, rest.z);
            o.tone = float4(lerp(v.color.r, 0.74, far * 0.7), v.color.g * (1.0 - 0.6 * far), v.color.b * (1.0 - far), v.color.a);
            // The ground's colour: the tile's average — the atlas mip with one texel per tile (_Sc_Look2.x).
            o.main = float4(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, tile.xy, _Sc_Look2.x).rgb, tile.z);
            o.accent = float4(accent.rgb, tile.w);
            o.bl = light.rgb;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "GrassClumpForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex BbtsClumpVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsGrassClumpInstance
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FloraAccent.hlsl" // BbtsSeasonTint: the second colour turns with the plants
            #include "MeadowLight.hlsl"

            float4 _Sc_GrassTint; // the world's grass-tuft colour (a > 0.5 = set); GameBootstrap.RebuildFloraTints

            half4 frag(ClumpVaryings i) : SV_Target
            {
                const float3 lumW = float3(0.299, 0.587, 0.114);
                float3 main = i.main.rgb;

                // The second colour: the world's tuft hue at the ground's brightness (a little lighter).
                float3 tuft = _Sc_GrassTint.a > 0.5 ? BbtsSeasonTint(_Sc_GrassTint.rgb) : main;
                float3 second = tuft * (dot(main, lumW) * 1.2 / max(dot(tuft, lumW), 1e-3));

                // The palette: main → second → accent by the vertex's slot weights (a clump not in bloom keeps its
                // seed heads in the second colour).
                float3 albedo = lerp(main, second, saturate(i.tone.g + i.tone.b * (1.0 - i.accent.a)));
                albedo = lerp(albedo, i.accent.rgb * 0.8, i.tone.b * i.accent.a);

                // The slot's ramp: the dark end cooler, the light end warmer — not grey times one colour.
                albedo *= lerp(float3(0.50, 0.57, 0.68), float3(1.12, 1.06, 0.93), saturate(i.tone.r));

                float gloss = 0.0;
                BbtsWeatherSurface(albedo, gloss, i.wp, float3(0.0, 1.0, 0.0), i.main.a); // wet darkens, snow whitens
                // The sun shadow of the ground beneath, softened (MeadowLight.hlsl): a blade does not flip between sun
                // and shade when the edge of a swaying crown's shadow passes over it.
                return half4(BbtsMeadowShadeAt(albedo, i.wp, i.ground, _Sc_LookGrassShadow, i.main.a, i.bl, saturate(i.tone.a)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex BbtsClumpVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsGrassClumpInstance

            half4 depthFrag(ClumpVaryings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
