// Plant bodies (PlantBodies.cs / PlantForms.cs): small plants as bodies — a rosette of pointed leaves, a
// flower on round stems with a head of small balls or petals, a bush of small cubes with round berries — drawn near the
// camera as GPU instances in place of the plant's crossed picture, which stays in the chunk mesh as the far picture.
//
// FORM WITHOUT COLOUR: the mesh carries no colour. Every vertex has a brightness and the
// weights of the colour slots; the palette comes from the world, per instance:
//   main   = the species' colour on this world (the same tint the crossed picture wears, turned by the season), at the
//            brightness of the species' own tile (the block atlas at its one-texel-per-tile mip) — so near and far match;
//   second = young leaves and a blossom's middle: the main colour lighter and toward yellow;
//   accent = blossoms, berries, leaf tips: one of the species' three accent colours, by the formula of the crossed
//            picture (FloraAccent.hlsl) — a meadow of one species keeps its mix of colours;
//   dark   = stems and twigs: toward the world's wood colour.
// Each slot is a small ramp, not grey times one colour: the dark end is cooler, the light end warmer.
// Every part moves for itself in the wind: the whole plant bows from its root (by the height of each part), and each
// leaf tip, ball and cube trembles with its own phase. A ball or a cube moves as one piece.
// Lit like a block face with its own normal (the world light of AtmosphereCommon), thin parts let the sun through.
// Casts its own shadow near the eye; beyond, the crossed picture casts (the block shader switches at the same distance).
// URP only.
Shader "BlocksBeyondTheStars/PlantBody"
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
        #include "FloraAccent.hlsl"      // the season's turn of a plant tint and the accent colours' hue maths

        // Per instance (MaterialPropertyBlock arrays):
        //   _PbTile   xy unused, z = skylight of the plant's cell,
        //             w = which of the three accent colours the plant wears (0 = the species has none), or 8 = the plant
        //             keeps its own colours (a crop, an authored plant): _PbTint and _PbAccent ARE its colours
        //   _PbTint   rgb = the species' colour on this world (shader-ready) — or a fruit's, a dyed or a bred plant's own;
        //             a = the plant's own wind phase 0..1, plus 2 for a plant in water (it sways as in a current)
        //   _PbLight  rgb = placed block light at the cell, a = the plant's own glow (0 = none)
        //   _PbAccent rgb = the colour of the leaves in the species' own tile (its brightness and hue feed the tint's
        //             recipe) — or, for a plant that keeps its own colours, its accent colour
        UNITY_INSTANCING_BUFFER_START(BbtsPlantBodyProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _PbTile)
            UNITY_DEFINE_INSTANCED_PROP(float4, _PbTint)
            UNITY_DEFINE_INSTANCED_PROP(float4, _PbLight)
            UNITY_DEFINE_INSTANCED_PROP(float4, _PbAccent)
        UNITY_INSTANCING_BUFFER_END(BbtsPlantBodyProps)

        #include "GpuInstances.hlsl" // #4046: on a buffer draw the instance comes from the graphics card
        void BbtsPlantBodyInstance()
        {
            #if defined(BBTS_GPU_INSTANCES)
                BBTS_GPU_INSTANCE(inst)
                _PbTile = inst.a;
                _PbTint = inst.b;
                _PbLight = inst.c;
                _PbAccent = inst.d;
            #endif
        }

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        float4 _Sc_LookWood; // the world's wood colour (a > 0.5 = set); PlantBodies.UseWorld

        struct BodyAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;     // r = brightness, g = second-colour weight, b = accent weight, a = bend (0 root … top)
            float4 part : TEXCOORD0;  // x = the part's wind phase 0..1, y = dark weight (stem), z = thin (lets light through), w = flutter
            float3 pivot : TEXCOORD1; // the point the wind is read at (object space): a ball or cube moves as one piece
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct BodyVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 wp : TEXCOORD0;
            float3 rest : TEXCOORD1;   // the world position at rest (the sun shadow is looked up there: the wind does not move it through the pattern)
            float3 wn : TEXCOORD2;
            float4 tone : TEXCOORD3;   // the vertex colour as above
            float4 main : TEXCOORD4;   // rgb = the main colour, a = skylight
            float4 accent : TEXCOORD5; // rgb = the accent colour, a = thin
            float4 bl : TEXCOORD6;     // rgb = placed block light, a = dark weight
            float glow : TEXCOORD7;    // the plant's own glow
        };

        // The species' accent colour: the hue of its tint turned by a species-dependent amount, three related picks
        // (the formula of BbtsFloraTint, so a body's blossom has the colour its crossed picture shows).
        float3 BbtsBodyAccent(float3 tint, float pick)
        {
        #ifdef UNITY_COLORSPACE_GAMMA
            float3 tg = tint;
        #else
            float3 tg = sqrt(max(tint, 0.0));
        #endif
            float3 hsv = BbtsRgbToHsv(tg);
            float baseHue = frac(hsv.x + 0.33 + (hsv.y - 0.45) * 0.8);
            float3 acc = pick < 1.5 ? BbtsHsvToRgb(float3(baseHue, 0.80, 1.0))
                : pick < 2.5 ? BbtsHsvToRgb(float3(frac(baseHue + 0.08), 0.70, 1.0))
                : BbtsHsvToRgb(float3(frac(baseHue + 0.94), 0.28, 1.0));
        #ifndef UNITY_COLORSPACE_GAMMA
            acc = acc * acc;
        #endif
            return acc;
        }

        // The swayed world position of a vertex. `eye` is the main camera (the shadow pass renders from the sun's view
        // and passes the published eye), `reach` how far from it the body is drawn at all. Returns false for a plant out
        // of reach: the caller collapses it.
        bool BbtsBodyPosition(BodyAttributes v, float4 tile, float4 tint, float3 eye, float reach, out float3 wp, out float3 rest)
        {
            rest = TransformObjectToWorld(v.positionOS.xyz);
            wp = rest;
            float3 root = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            if (distance(root, eye) > reach)
            {
                return false;
            }

            // The plant bows from its root: the slow wave of the plants, read at the part's pivot, scaled by its height.
            // Where the wind does not reach (a roof, a cave) only the indoor draught is left.
            float3 pivot = TransformObjectToWorld(v.pivot);
            float bend = v.color.a;
            float shelter = tint.a > 1.5 ? 1.0 : BbtsSwayShelter(tile.z); // a plant in water keeps its sway under cover
            wp += lerp(BbtsDraught(pivot, bend), BbtsWindSway(pivot, false, true, bend) * 0.8, shelter);

            // … and every part trembles for itself: a leaf's tip more than its base, a ball or cube as one piece.
            if (_Sc_Wind.w > 0.5 && v.part.w > 0.0)
            {
                float wind = length(_Sc_Wind.xz);
                float2 dir = wind > 0.01 ? _Sc_Wind.xz / wind : float2(0.7071, 0.7071);
                float phase = (v.part.x + tint.a) * 6.2832 + root.x * 1.7 + root.z * 2.3;
                float t = _Sc_Wind.y;
                float wave = sin(t * 1.9 + phase) * 0.6 + sin(t * 3.1 + phase * 1.3) * 0.4;
                wp += float3(dir.x, 0.2, dir.y) * (wave * 0.011 * v.part.w * (0.4 + 0.6 * saturate(wind)) * lerp(0.25, 1.0, shelter));
            }

            return true;
        }

        BodyVaryings BbtsBodyVert(BodyAttributes v)
        {
            BodyVaryings o = (BodyVaryings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            float4 tile = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbTile);
            float4 tint = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbTint);
            float4 light = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbLight);

            float3 wp, rest;
            if (!BbtsBodyPosition(v, tile, tint, _WorldSpaceCameraPos, _Sc_LookPlant.x + 1.0, wp, rest))
            {
                o.positionCS = float4(2.0, 2.0, 2.0, 1.0); // collapsed to one point outside the view: no fragments
                return o;
            }

            o.positionCS = TransformWorldToHClip(wp);
            o.wp = wp;
            o.rest = rest;
            o.wn = TransformObjectToWorldNormal(v.normalOS);
            o.tone = v.color;

            // The main colour: the crossed picture's own recipe (luminance x species tint x 1.6, at 85 %), with the
            // colour of the tile's LEAVES in place of its texels (PlantBodies reads it from the tile: the brighter half
            // of its pixels — the tile's plain average is far darker than the plant the eye sees).
            const float3 lumW = float3(0.299, 0.587, 0.114);
            float3 tileRgb = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbAccent).rgb;
            float3 species = BbtsSeasonTint(tint.rgb);
            float3 accent = tile.w > 0.5 ? BbtsBodyAccent(species, tile.w) : float3(0.0, 0.0, 0.0);
            o.main = float4(lerp(tileRgb, dot(tileRgb, lumW) * species * 1.6, 0.85) * 1.1, tile.z);
            if (tile.w > 7.5)
            {
                // A plant that keeps its own colours: both are given (read from its tile), no tint and no season.
                o.main.rgb = tint.rgb * 1.1;
                accent = tileRgb;
            }

            o.accent = float4(accent, v.part.z);
            o.bl = float4(light.rgb, v.part.y);
            o.glow = light.a;
            return o;
        }

        // The colour of a fragment before the light: the slots mixed by the vertex's weights, times the slot's ramp.
        float3 BbtsBodyAlbedo(BodyVaryings i)
        {
            const float3 lumW = float3(0.299, 0.587, 0.114);
            float3 main = i.main.rgb;
            float lum = dot(main, lumW);
            float3 second = lerp(main, lum * float3(1.30, 1.22, 0.55), 0.45) * 1.25;
            float3 wood = _Sc_LookWood.a > 0.5 ? _Sc_LookWood.rgb : main * 0.5;
            float3 dark = lerp(main * 0.7, wood, 0.5);
            // A species without an accent colour wears the second colour there (lighter leaf tips, no blossoms).
            float3 accent = dot(i.accent.rgb, float3(1.0, 1.0, 1.0)) > 0.001 ? i.accent.rgb : second;

            float3 albedo = lerp(main, second, saturate(i.tone.g));
            albedo = lerp(albedo, accent, saturate(i.tone.b));
            albedo = lerp(albedo, dark, saturate(i.bl.a));
            return albedo * lerp(float3(0.50, 0.57, 0.68), float3(1.12, 1.06, 0.93), saturate(i.tone.r));
        }
        ENDHLSL

        Pass
        {
            Name "PlantBodyForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex BbtsBodyVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsPlantBodyInstance
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "MeadowLight.hlsl" // _Sc_Light, _Sc_SunDir, the headlamp and the faded sun shadow

            half4 frag(BodyVaryings i) : SV_Target
            {
                // The screen-door cross-fade at the end of the range: a pixel shows the body or the crossed picture.
                clip(BbtsPlantNearShare(i.wp, _WorldSpaceCameraPos) - BbtsIgn(i.positionCS.xy) - 0.0001);

                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 N = normalize(i.wn);
                // The "thin" channel also marks a cut stone (2): a crystal's facets (PlantForms.AddGem). Unless the gem look
                // (_Sc_LookGem) is on, it is lit like the prisms before (half thin).
                bool cut = i.accent.a > 1.5;
                bool gem = cut && _Sc_LookGem > 0.5;
                float thin = cut ? (gem ? 0.0 : 0.5) : i.accent.a;
                if (thin > 0.5 && dot(N, V) < 0.0)
                {
                    N = -N; // a leaf or petal seen from below
                }

                float3 albedo = BbtsBodyAlbedo(i);
                float facing = saturate(dot(N, V));
                if (gem)
                {
                    // Deep and saturated where a facet turns away from the eye, lighter where one looks into the stone.
                    albedo = lerp(albedo * albedo * 2.2, albedo * 1.25, facing * facing);
                }

                float sky = saturate(i.main.a);
                float gloss = 0.0;
                BbtsWeatherSurface(albedo, gloss, i.wp, N, sky); // wet darkens, snow settles on what faces up

                float3 light = (_Sc_Light.a < 0.5) ? float3(1.0, 1.0, 1.0) : _Sc_Light.rgb;
                float3 L = normalize(_Sc_SunDir.xyz);
                // A soft terminator: small round things read rounder when the light wraps a little.
                float ndl = saturate((dot(N, L) + 0.3) / 1.3);
                float sunOpen = saturate(sky * 2.0);
                float shadow = (ndl * sky > 0.0) ? BbtsMeadowSunShadow(i.rest) * BbtsCloudShade(i.wp) : 0.0;

                float amb = BbtsSkyAmbient(sky);
                float3 col = albedo * (BbtsWorldLight(light, amb, ndl * sunOpen * shadow, N) + BbtsDarkFloor());

                // The night floor: a faint cool fill on open-sky surfaces while the sun light is weak.
                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * (sky * nightFloor);

                // Thin parts let the sun through when it stands behind them (the plants' #2403 term).
                float back = pow(saturate(dot(-V, L)), 3.0);
                col += albedo * light * (back * 0.45 * thin) * sunOpen * shadow * sky;

                // Under water the sun's caustics dance on what faces up (the block shader's #2401 term; 0 above water).
                col += albedo * light * (BbtsCaustics(i.wp, normalize(N + float3(0.0, 0.6, 0.0)), sky) * 0.7);

                // The rim light: a warm edge where a ball, a cube or a stem turns away from the eye.
                if (_Sc_Look3.w > 0.0)
                {
                    float rim = pow(1.0 - saturate(dot(N, V)), 3.0) * (0.35 + 0.65 * saturate(dot(-V, L) * 0.5 + 0.5));
                    col += albedo * light * (_Sc_Look3.w * rim * 0.6) * sunOpen * sky * lerp(1.0, 0.4, thin);
                }

                if (gem)
                {
                    float3 stone = i.main.rgb;
                    // The stone's hue at full strength, a quarter of the way to white: the colour of its inner light.
                    float3 core = lerp(stone / max(max(stone.r, stone.g), max(stone.b, 0.02)), float3(1.0, 1.0, 1.0), 0.25);
                    // The sun behind the stone shines through it; a facet that mirrors the sun flashes.
                    col += stone * light * (pow(saturate(dot(-V, L)), 2.0) * 0.9) * sunOpen * shadow * sky;
                    col += light * (pow(saturate(dot(reflect(-V, N), L)), 40.0) * 1.4) * sunOpen * shadow * sky;
                    // The sky on a grazing facet (the look's ambient colour).
                    col += _Sc_AmbSky.rgb * (pow(1.0 - facing, 3.0) * 0.15 * sky);
                    // The stone's own light, where one looks straight into it — in the stone's own saturated colour, so
                    // the middle glows and the facets that turn away stay deep: a crystal is never dull, never milky.
                    col += core * ((0.06 + i.glow * 0.5) * facing * facing * facing * BbtsGlowBreath(i.wp));
                }

                // A glowing plant shines itself, like its crossed picture: it breathes slowly and fades to a quarter in
                // daylight (#2822, #2858) — pushed past white, so the bloom catches it.
                if (i.glow > 0.001)
                {
                    col += albedo * (i.glow * 3.0 * BbtsGlowBreath(i.wp) * BbtsPlantGlowDaylight(L.y, sky));
                }

                // Placed lights and the headlamp.
                col += albedo * BbtsBlockLightFlicker(i.bl.rgb, i.wp) * (BbtsCozyLightGain() * 1.7);
                col += BbtsHeadlamps(i.wp, N, V, albedo, float3(0.04, 0.04, 0.04), 0.8);

                return half4(BbtsApplyHaze(col, i.wp, sky), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsPlantBodyInstance
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection; // set by URP while rendering the shadow map

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 wp : TEXCOORD0;
            };

            ShadowVaryings shadowVert(BodyAttributes v)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 tile = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbTile);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(BbtsPlantBodyProps, _PbTint);

                // The body casts up to its shadow range from the eye; beyond it the crossed picture casts.
                float3 wp, rest;
                if (!BbtsBodyPosition(v, tile, tint, _Sc_LookEye.xyz, _Sc_LookPlant.z + 1.0, wp, rest))
                {
                    o.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                    return o;
                }

                float3 wn = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.wp = wp;
                return o;
            }

            half4 shadowFrag(ShadowVaryings i) : SV_Target
            {
                clip(_Sc_LookPlant.z - distance(i.wp, _Sc_LookEye.xyz));
                return 0;
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
            #pragma vertex BbtsBodyVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsPlantBodyInstance

            half4 depthFrag(BodyVaryings i) : SV_Target
            {
                clip(BbtsPlantNearShare(i.wp, _WorldSpaceCameraPos) - BbtsIgn(i.positionCS.xy) - 0.0001);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
