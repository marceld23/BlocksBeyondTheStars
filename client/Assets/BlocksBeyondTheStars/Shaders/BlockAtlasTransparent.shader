// Alpha-blended sibling of BlocksBeyondTheStars/BlockAtlas for see-through blocks (glass viewports + station
// force-field/energy barriers + water + the gas giant's gas sea, #2128). Same atlas + sun globals, but drawn in the
// Transparent queue so the world behind shows through. Vertex colour: r=gloss, g=metal, b=face shade, a=emission (glow).
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 = set)
//   _Sc_SunDir = world-space direction TO the sun
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (HLSL, UniversalForward — also receives the sun's
// shadow on the directional term so water/glass dims under shadow); SubShader 2 is the original Built-in RP
// pass (CG, unchanged). Transparent surfaces cast no shadows (no ShadowCaster) by design.
Shader "BlocksBeyondTheStars/BlockAtlasTransparent"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.4
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off          // a single-layer pane/field reads from both sides
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
            // Scene depth (enabled by the URP asset's depth texture, Phase 0) — lets water read its own column
            // depth against the bed/objects behind it for a shallow→deep colour gradient + intersection foam.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // Opaque scene colour (Phase 0 opaque texture) — the bed behind the water, sampled wave-distorted for
            // refraction (the surface bends what's beneath it; plain alpha-blending can't do that).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): water and glass haze like the terrain (#2393)

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;   // sky colour (set by Sky.cs) — water SSR sky fallback
            float _Sc_ScreenFx; // 1 when the depth+opaque textures exist (Medium+); 0 on Low → water uses the simple look
            float4 _Sc_WaterTint; // #1758: per-world water colour (Sky.cs); read in mode 1
            float _Sc_WaterMode;  // #1758: 0 = the classic blue, 1 = tint, 2 = static rainbow bands by position
            // #2128: the gas tile's darkest / average / brightest tone (BlockTextureAtlas.PublishGasPalette; a>0.5 = set).
            float4 _Sc_GasLo;
            float4 _Sc_GasMid;
            float4 _Sc_GasHi;

            // SRP Batcher (#573): per-MATERIAL properties only. The _Sc_* globals above stay outside — they are
            // set once per frame via Shader.SetGlobal*, not per material.
            CBUFFER_START(UnityPerMaterial)
                float _BaseAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1; // y carries the animation code of the tile (#1957); the rest is the opaque shader's
                // Water top faces: x=mode (1 lake, 2 open, 3 river), y=foam (corner-smoothed),
                // z=wave amplitude factor (corner-smoothed), w=flow axis (0=X, 1=Z).
                float4 water : TEXCOORD2;
                float4 color : COLOR; // r=gloss, g=metal, b=face shade, a=emission
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 mat : TEXCOORD3;
                float4 water : TEXCOORD4;
            };

            // #1957 animated tiles. TEXCOORD1.y = tint mode (low 4 bits) + 16*frames + 256*speedIndex + 1024*stripStart
            // (BlockTextureAtlas.AnimationCode) — every term is exact in a float. The mesh UV stays on the block's
            // OWN atlas cell; an animated face is moved onto the strip cell of the current frame here, in the vertex
            // stage, so it costs the fragment stage nothing. 32 = atlas cells per side (AtlasBands.Cols/Rows).
            float2 BbtsAnimatedUv(float2 uv, float code, out float mode)
            {
                mode = fmod(code, 16.0);
                float frames = fmod(floor(code / 16.0), 16.0);
                if (frames < 1.5)
                {
                    return uv;
                }

                float speedIndex = fmod(floor(code / 256.0), 4.0);
                float fps = speedIndex < 0.5 ? 2.0 : speedIndex < 1.5 ? 4.0 : speedIndex < 2.5 ? 8.0 : 12.0;
                float slot = floor(code / 1024.0) + fmod(floor(_Time.y * fps), frames);
                float2 cell = floor(uv * 32.0);
                return (float2(fmod(slot, 32.0), floor(slot / 32.0)) + (uv * 32.0 - cell)) / 32.0;
            }

            // #2128: value noise for the gas haze. The hash is sin-free (stable at large world coordinates); three
            // octaves on a rotated lattice so no axis — and never the block grid — shows through.
            float BbtsGasHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float BbtsGasNoise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(BbtsGasHash(c), BbtsGasHash(c + float2(1.0, 0.0)), u.x),
                            lerp(BbtsGasHash(c + float2(0.0, 1.0)), BbtsGasHash(c + float2(1.0, 1.0)), u.x), u.y);
            }

            float BbtsGasFbm(float2 p)
            {
                float s = 0.5 * BbtsGasNoise(p);
                p = float2(1.6 * p.x + 1.2 * p.y, -1.2 * p.x + 1.6 * p.y);
                s += 0.25 * BbtsGasNoise(p);
                p = float2(1.6 * p.x + 1.2 * p.y, -1.2 * p.x + 1.6 * p.y);
                s += 0.125 * BbtsGasNoise(p);
                return s / 0.875;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);

                // Water bobs on three crossed sine waves — displaced DOWN only (the rest surface is the
                // block top). The amplitude factor is corner-smoothed by the mesher (open water 1, lake
                // 0.25, river/bank 0) and further flattened into the foam band, so shared block corners
                // displace identically — no cracks — and the shoreline stays flush with the terrain.
                // Only REAL water waves (#1374): TEXCOORD2.x carries the water-body mode (1..4) on water
                // faces, but on every other block those same channels carry the flora/hull/dye tint — so a
                // dyed pane's blue channel used to be read as a wave amplitude and the glass physically bobbed.
                // Water always writes a positive mode, so this gate can never exclude water.
                // #1749: the amplitude comes from the corner-smoothed weights — open water bobs fully, a calm
                // basin a quarter, a brook not at all — and never a falling flank (mode 4).
                float openW = saturate(v.water.x - 1.0);
                float brookW = saturate(v.water.z + v.water.w);
                float ampFactor = openW + (1.0 - openW) * (1.0 - brookW) * 0.25;
                float amp = (v.water.x > 0.5 && v.water.x < 3.5) ? 0.12 * ampFactor * (1.0 - saturate(v.water.y)) : 0.0;
                if (amp > 0.0005)
                {
                    float t = _Time.y;
                    float w = sin(wp.x * 0.50 + t * 1.10) + sin(wp.z * 0.41 + t * 1.43)
                            + sin((wp.x + wp.z) * 0.27 + t * 0.70);
                    wp.y -= amp * (0.5 + w / 6.0);
                }

                o.positionCS = TransformWorldToHClip(wp);
                float unusedMode;
                o.uv = BbtsAnimatedUv(v.uv, v.sky.y, unusedMode);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.mat = v.color;
                o.water = v.water;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 albedo = tex.rgb;
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float3 N = normalize(i.wn);
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float shade = lerp(0.7, 1.0, i.mat.b); // per-face shading baked by the mesher
                float emission = i.mat.a;              // glow (energy fields shine; plain glass = 0)

                // Sun shadow on the directional half only — shadowed water/glass dims, never blacks out.
                // #1518: only multiplied by ndl, so faces turned away from the sun skip the lookup (identical result).
                float shadow = (ndl > 0.0) ? MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp)) : 0.0;

                float3 col = albedo * light * (0.55 + 0.45 * ndl * shadow) * shade;
                col += albedo * emission * 2.0;        // emissive energy-field glow (bloom catches it)

                // Match the opaque night ambient floor so water/glass don't read darker than the terrain at
                // night: a faint cool fill, strongest when the sun light is weak (night/storm), fading out by day.
                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * nightFloor * shade;

                // What makes a face WATER is a see-through tile — but that alone is not enough, and trusting
                // it alone was the bug behind #1372/#1373: clear glass shipped with a fully transparent tile
                // (the image model read "perfectly clear glass" as an alpha channel) and fire carries a real
                // flame cutout, so both fell into the water branch and were rendered as a pond — animated
                // refraction, SSR and a forced opaque composite. Pick them out explicitly, before the branch.
                float isClear = saturate(-i.water.x);     // 1 for glass_clear (the mesher writes -1, #1274)
                float isField = saturate(emission * 4.0); // ~1 for fire + energy fields, 0 for water and glass

                float alpha;
                if (i.water.x > 5.5)
                {
                    // #2134: the dense gas under the gas sea — the floor you see from the islands and sink into, where the bare
                    // heightfield rock used to show. The gas tile's own tones, darkened and pulled towards violet; the warp
                    // itself moves, so the body wells up and folds in place instead of sliding past like the gas above it. Nearly
                    // opaque: nothing under it is meant to be seen. Three noise fields — cheaper than the gas sea's haze.
                    float t = _Time.y;
                    bool palette = _Sc_GasMid.a > 0.5;
                    float3 dLo = (palette ? _Sc_GasLo.rgb : albedo * 0.72) * float3(0.16, 0.12, 0.19);
                    float3 dMid = (palette ? _Sc_GasMid.rgb : albedo) * float3(0.30, 0.23, 0.31);
                    float3 dHi = (palette ? _Sc_GasHi.rgb : albedo * 1.15) * float3(0.52, 0.41, 0.50);

                    float2 layerBase = i.wp.xz + float2(0.7, -0.4) * i.wp.y;
                    float2 p = layerBase / 20.0;
                    float2 warp = float2(BbtsGasFbm(p * 0.7 + float2(t * 0.050, 3.1)), BbtsGasFbm(p * 0.7 + float2(-5.7, t * 0.041)));
                    float field = BbtsGasFbm(p + (warp - 0.5) * 3.0 + float2(t * 0.011, -t * 0.008));

                    float3 dense = lerp(dLo, dMid, smoothstep(0.26, 0.55, field));
                    dense = lerp(dense, dHi, smoothstep(0.62, 0.86, field) * 0.75); // slow pale billows welling up
                    dense *= 0.88 + 0.12 * sin(t * 0.6 + field * 6.2832);             // a slow breathing of the whole body

                    col = dense * light * (0.60 + 0.40 * ndl * lerp(0.7, 1.0, shadow)) * shade;
                    col += dense * float3(0.10, 0.13, 0.20) * nightFloor * shade;

                    alpha = saturate(0.90 + 0.08 * (field - 0.5));
                    if (_Sc_ScreenFx > 0.5)
                    {
                        // A sinking body breaks the surface softly, like the gas above.
                        float2 denseUV = GetNormalizedScreenSpaceUV(i.positionCS);
                        float denseScene = LinearEyeDepth(SampleSceneDepth(denseUV), _ZBufferParams);
                        float denseFrag = -TransformWorldToView(i.wp).z;
                        alpha *= lerp(0.45, 1.0, saturate(max(0.0, denseScene - denseFrag) / 1.5));
                    }
                }
                else if (i.water.x > 4.5)
                {
                    // #2128: the gas sea is a drifting haze, not a sheet of tiles. Its tile repeated once per block drew
                    // a grid of stripes; here the colour is a world-space, domain-warped noise field in the tile's own
                    // tones (lo = the troughs, mid = the body, hi = the lit tops), dragged along by the storm, with
                    // wisps sampled UNDER the surface along the view ray so the haze has depth. No waves, foam, glint,
                    // refraction or reflection — it is gas, not water.
                    float t = _Time.y;
                    float3 Vw = normalize(i.wp - _WorldSpaceCameraPos);
                    bool palette = _Sc_GasMid.a > 0.5;
                    float3 gLo = palette ? _Sc_GasLo.rgb : albedo * 0.72;
                    float3 gMid = palette ? _Sc_GasMid.rgb : albedo;
                    float3 gHi = palette ? _Sc_GasHi.rgb : albedo * 1.15;

                    float2 drift = float2(0.55, 0.18) * t; // blocks per second — the gale drags the haze along
                    // The body: a large, slow colour field tens of blocks across. Height enters as an offset, so a side
                    // face does not smear one column of the pattern down its whole height.
                    float2 layerBase = i.wp.xz + float2(0.7, -0.4) * i.wp.y;
                    float2 p = (layerBase + drift) / 34.0;
                    float2 warp = float2(BbtsGasFbm(p * 0.6 + 7.3), BbtsGasFbm(p * 0.6 - 3.9));
                    float field = BbtsGasFbm(p + (warp - 0.5) * 2.2 + float2(0.0, t * 0.006));

                    // Wisps: thin streaks stretched along the wind, sampled 1.5 / 4 / 7 blocks under the surface along the
                    // view ray (1 / |V.y| lengthens the offset at a grazing view). Each layer drifts at its own pace, so
                    // looking across the sea they slide over each other like haze at different depths. The Low and
                    // Potato presets (no screen effects) keep the top layer only.
                    float slant = 1.0 / max(abs(Vw.y), 0.2);
                    float2 q = (layerBase + Vw.xz * (1.5 * slant) + drift * 1.4) * float2(1.0 / 26.0, 1.0 / 9.0);
                    float wisp = smoothstep(0.48, 0.78, BbtsGasFbm(q + warp * 0.8)) * 0.75;
                    if (_Sc_ScreenFx > 0.5)
                    {
                        q = (layerBase + Vw.xz * (4.0 * slant) + drift * 1.1) * float2(1.0 / 30.0, 1.0 / 11.0);
                        wisp += smoothstep(0.48, 0.78, BbtsGasFbm(q + 17.0)) * 0.50;
                        q = (layerBase + Vw.xz * (7.0 * slant) + drift * 0.8) * float2(1.0 / 36.0, 1.0 / 13.0);
                        wisp += smoothstep(0.48, 0.78, BbtsGasFbm(q - 29.0)) * 0.35;
                    }

                    wisp = saturate(wisp);
                    float graze = 1.0 - abs(Vw.y);

                    float3 gas = lerp(gLo, gMid, smoothstep(0.30, 0.52, field));
                    gas = lerp(gas, gHi, smoothstep(0.55, 0.78, field) * 0.85);
                    gas = lerp(gas, gHi * 1.12, wisp * 0.80);             // the wisps are the lightest, thickest haze
                    gas = lerp(gas, gHi, graze * graze * graze * 0.45); // and it pales towards the horizon, like any haze

                    // A haze scatters light through its body, so an island's shadow only dims it a little — a crisp,
                    // block-stepped shadow on a fog bank reads as a solid floor.
                    float fogShadow = lerp(0.6, 1.0, shadow);
                    col = gas * light * (0.62 + 0.38 * ndl * fogShadow) * shade;
                    col += gas * float3(0.10, 0.13, 0.20) * nightFloor * shade;
                    col += gas * light * pow(saturate(dot(Vw, L)), 6.0) * 0.30; // forward scatter: towards the sun the haze glows

                    // Opacity: a thick haze you see only a little way into — thinner in the troughs, thicker in the
                    // wisps, closing to a solid wall towards the horizon.
                    alpha = 0.72 + 0.14 * (field - 0.5) + 0.18 * wisp;
                    alpha = lerp(alpha, 1.0, graze * graze * 0.85);

                    if (_Sc_ScreenFx > 0.5)
                    {
                        // Where something breaks the surface (a pylon, a deck leg, a sinking player) the haze thins out
                        // softly instead of drawing water's white foam line.
                        float2 gasUV = GetNormalizedScreenSpaceUV(i.positionCS);
                        float gasScene = LinearEyeDepth(SampleSceneDepth(gasUV), _ZBufferParams);
                        float gasFrag = -TransformWorldToView(i.wp).z;
                        alpha *= lerp(0.35, 1.0, saturate(max(0.0, gasScene - gasFrag) / 1.5));
                    }

                    alpha = saturate(alpha);
                }
                else if (tex.a < 0.95 && isClear < 0.5 && isField < 0.5)
                {
                    // Water: a clear blue body (no milky frost), alpha straight from the tile, so you see into
                    // and through it while swimming.
                    alpha = tex.a;

                    // #1758 (school club wave 3): the world's water colour. A luminance recolour keeps the wave
                    // shading; mode 2 lays static rainbow bands across the world (by position, never animated).
                    // `wtint` stays in scope: the screen-space block below composites the BED through the water
                    // and tints the depths, and both must follow the world's colour or a sandy shallow sea reads
                    // as sand with a faint hue and a deep one as the classic blue (Marcel's playtest 2026-09-11).
                    float3 wtint = float3(1.0, 1.0, 1.0);
                    if (_Sc_WaterMode > 0.5)
                    {
                        float wlum = dot(col, float3(0.299, 0.587, 0.114));
                        wtint = _Sc_WaterTint.rgb;
                        if (_Sc_WaterMode > 1.5)
                        {
                            float hue = frac((i.wp.x + i.wp.z) / 96.0);
                            wtint = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                            wtint = lerp(float3(0.5, 0.5, 0.5), wtint, 0.85);
                        }
                        col = lerp(col, wlum * wtint * 2.2, 0.85);
                    }

                    float mode = i.water.x;
                    float t = _Time.y;
                    if (mode > 3.5)
                    {
                        // Waterfall flank: a bright sheet of streaks racing straight DOWN the face. Procedural on
                        // world height (atlas UVs can't scroll); sin(k*y + w*t) translates the pattern downward.
                        // `across` only ever enters as a NESTED perturbation (a per-column phase offset): added
                        // linearly to the phase it tilted the streaks ~41 degrees into a diagonal glare (#1853).
                        float across = i.wp.x + i.wp.z;
                        float ph = i.wp.y * 3.0 + t * 6.5;
                        float rip = 0.5 + 0.5 * sin(ph + sin(across * 2.3) * 1.5);
                        col += light * 0.12 * rip;
                        float streak = smoothstep(0.84, 1.0, sin(ph * 1.27 + sin(across * 2.9) * 1.2));
                        col = lerp(col, light * float3(0.95, 0.98, 1.0), streak * 0.45);
                        alpha = saturate(alpha + streak * 0.30 + rip * 0.06);
                    }
                    else
                    {
                        // #1749: below the waterfall there is no branch. Open water, brook (along X and/or Z)
                        // and calm basin are WEIGHTS the mesher averages over block corners, so a body of
                        // varying width — or one full of reeds — blends from one look to the next instead of
                        // switching per cell and drawing a mosaic of ripple directions and brightness tiles.
                        float open = saturate(mode - 1.0);
                        float wX = saturate(i.water.z);
                        float wZ = saturate(i.water.w);
                        float calm = saturate(1.0 - open - wX - wZ);

                        // Brook: bright ripple bands + thin white streaks racing along the flow axis, one set
                        // per axis, each weighted. Procedural on world position — atlas UVs cannot scroll.
                        float ripSum = 0.0, streakSum = 0.0;
                        if (wX > 0.001)
                        {
                            float ph = i.wp.x * 1.9 - t * 5.5;
                            float rip = 0.5 + 0.5 * sin(ph + sin(i.wp.z * 2.7) * 1.2);
                            float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 + i.wp.z * 3.1));
                            ripSum += wX * rip;
                            streakSum += wX * streak;
                        }
                        if (wZ > 0.001)
                        {
                            float ph = i.wp.z * 1.9 - t * 5.5;
                            float rip = 0.5 + 0.5 * sin(ph + sin(-i.wp.x * 2.7) * 1.2);
                            float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 - i.wp.x * 3.1));
                            ripSum += wZ * rip;
                            streakSum += wZ * streak;
                        }
                        col += light * 0.10 * ripSum;
                        col = lerp(col, light * float3(0.95, 0.97, 1.0), streakSum * 0.35);
                        alpha = saturate(alpha + streakSum * 0.20 + ripSum * 0.04);

                        // Open water: a soft moving sun glint, plus an animated rippled foam band where the
                        // surface meets the shore (i.water.y fades over the last three blocks).
                        float glint = pow(0.5 + 0.5 * sin(i.wp.x * 1.7 + i.wp.z * 1.3 + t * 1.9), 6.0);
                        col += light * 0.06 * ndl * glint * open;
                        float foam = i.water.y * open;
                        if (foam > 0.01)
                        {
                            float cell = frac(sin(dot(floor(i.wp.xz * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                            float surge = 0.55 + 0.45 * sin(t * 1.6 + (i.wp.x + i.wp.z) * 0.9 + cell * 6.2832);
                            float f = saturate(foam * surge * (0.6 + 0.6 * cell));
                            col = lerp(col, light * float3(0.97, 0.99, 1.0), f * 0.85);
                            alpha = saturate(alpha + f * 0.45);
                        }

                        // Calm basin: a barely-there slow shimmer for whatever weight is left.
                        col += light * 0.03 * calm * (0.5 + 0.5 * sin(t * 0.6 + i.wp.x * 0.8 + i.wp.z * 1.1));
                    }

                    // Screen-space water (depth colour, refraction, SSR) needs the depth + opaque textures, which
                    // Potato/Low switch off — gate the whole block so on those presets the water keeps the simple
                    // alpha look above (no sampling of unbound textures → never black/garbage). Falling water
                    // (mode 4) is a VERTICAL sheet: the depth/refraction/SSR assume a flat surface and would
                    // blue it out opaque + reflect wrong, so skip it and keep the simple streaky cascade look.
                    if (_Sc_ScreenFx > 0.5 && mode < 3.5)
                    {
                    // Depth-based water body: read how much water sits between the surface and the bed/object
                    // behind this pixel, then darken+blue with depth (you can't see the bottom of a deep sea)
                    // and froth a bright foam line where geometry breaks the surface (shores, rocks, swimmers).
                    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float fragEye = -TransformWorldToView(i.wp).z;
                    float column = max(0.0, sceneEye - fragEye); // metres of water column ALONG THE VIEW RAY
                    // Depth tint keys on the VERTICAL water depth, not the ray length: at a shallow viewing
                    // angle the ray travels far through even knee-deep water (1/cos blow-up), which used to
                    // tint the whole lake "deep sea" from the shore. Scaling by |V.y| recovers ~true depth.
                    float3 Vw = normalize(i.wp - _WorldSpaceCameraPos);
                    float vertical = column * max(abs(Vw.y), 0.08); // floor keeps near-horizontal rays finite
                    float depth01 = saturate(vertical / 16.0);
                    // The deep tint follows the world's water colour (#1758); classic worlds keep the blue depths.
                    float3 deepTint = _Sc_WaterMode > 0.5 ? wtint * 0.18 : float3(0.03, 0.10, 0.16);
                    col = lerp(col, col * 0.6 + light * deepTint, depth01 * 0.45); // gentler, lighter deep tint
                    alpha = lerp(alpha, saturate(alpha + 0.08), depth01); // depth reads as colour, barely as opacity
                    float edge = 1.0 - saturate(column / 0.55);          // ~1 right at the waterline / around objects
                    if (edge > 0.01)
                    {
                        float ef = edge * (0.6 + 0.4 * sin(t * 3.1 + (i.wp.x + i.wp.z) * 3.7));
                        col = lerp(col, light * float3(0.96, 0.99, 1.0), saturate(ef) * 0.7);
                        alpha = saturate(alpha + saturate(ef) * 0.5);
                    }

                    // Refraction: composite the bed ourselves from the opaque texture, sampled at a wave-distorted
                    // screen position, so the surface BENDS what's beneath it (plain alpha-blending can't). We then
                    // output opaque and reuse `alpha` (depth-driven) as how much the water hides the bed — shallow
                    // water shows the wobbling bed, deep water its own blue. Degrades gracefully if no opaque texture.
                    float2 wob = float2(sin(i.wp.x * 1.3 + t * 1.5) + sin(i.wp.z * 0.9 - t * 1.1),
                                        cos(i.wp.z * 1.2 + t * 1.3) + sin(i.wp.x * 0.7 - t * 0.9));
                    float refr = 0.018 * (1.0 - depth01); // shallow distorts the visible bed; deep hides it anyway
                    float3 bed = SampleSceneColor(screenUV + wob * refr);
                    col = lerp(bed, col, saturate(alpha));
                    // #1758: the bed is seen THROUGH coloured water — recolour the composite as well, else the
                    // refracted sand wins over the tint and rainbow water looks like a sandy sea with a hue.
                    if (_Sc_WaterMode > 0.5)
                    {
                        float clum = dot(col, float3(0.299, 0.587, 0.114));
                        col = lerp(col, clum * wtint * 1.7, 0.55);
                    }

                    // Screen-space reflection on the surface: mirror the view ray about the (wave-rippled) normal
                    // and march it through the depth buffer; on a hit reflect the opaque colour there, else fall
                    // back to the sky. Blended by Fresnel (grazing angles reflect most) + a tight sun glint. This
                    // is SSR localised to WATER — no full-screen pass, so no darkening risk, and it only reflects
                    // the surface that should (the sea), never matte walls.
                    // Stronger wave perturbation on the reflection ray breaks the mirror into a soft, watery
                    // reflection instead of a hard pixel-perfect one. (Vw declared above for the depth measure.)
                    float3 rn = normalize(N + float3(wob.x, 0.0, wob.y) * 0.12);
                    float3 Rw = reflect(Vw, rn);
                    // Keep the reflection a sheen, not a mirror, so you can see INTO the water: low base sheen and
                    // a capped grazing maximum (0.45, not full mirror) let the depth/bed colour read through.
                    float fres = lerp(0.08, 0.45, pow(1.0 - saturate(dot(-Vw, rn)), 4.0));
                    float3 reflCol = _Sc_Sky.rgb; // most of a water reflection is the sky
                    float3 sp = i.wp;
                    float stepLen = 0.5;
                    // Texel LOADS, not samples, inside the march: a sample needs screen derivatives, which a loop
                    // with a data-dependent exit cannot provide — GLES3 only warned ("gradient instruction in a
                    // loop"), WebGPU's WGSL rejects it (#2390). The depth and opaque copies are full-size here
                    // (opaque downsample off), so UV × size is the texel.
                    int2 depthSize = (int2)_CameraDepthTexture_TexelSize.zw;
                    int2 colorSize = (int2)_CameraOpaqueTexture_TexelSize.zw;
                    [loop] for (int k = 0; k < 14; k++)
                    {
                        sp += Rw * stepLen;
                        stepLen *= 1.35;
                        float4 cp = TransformWorldToHClip(sp);
                        if (cp.w <= 0.0) { break; }
                        float2 ruv = cp.xy / cp.w * 0.5 + 0.5;
                        if (_ProjectionParams.x < 0.0) { ruv.y = 1.0 - ruv.y; }
                        if (ruv.x < 0.0 || ruv.x > 1.0 || ruv.y < 0.0 || ruv.y > 1.0) { break; }
                        int2 dp = clamp((int2)(ruv * depthSize), int2(0, 0), depthSize - 1);
                        float hitEye = LinearEyeDepth(LoadSceneDepth((uint2)dp), _ZBufferParams);
                        float rayEye = -TransformWorldToView(sp).z;
                        if (rayEye > hitEye + 0.1 && rayEye < hitEye + 4.0)
                        {
                            // Soft 5-tap blur of the reflected scene colour so the mirror is gently diffused
                            // (water is never a perfect mirror) — kills the "too hard" sharp reflection.
                            int2 rp = (int2)(ruv * colorSize);
                            int2 br = max(int2(1, 1), (int2)(colorSize * 0.0035)); // ≈ 0.0035 of the frame
                            int2 lo = int2(0, 0), hi = colorSize - 1;
                            reflCol = (LoadSceneColor((uint2)clamp(rp, lo, hi))
                                     + LoadSceneColor((uint2)clamp(rp + int2(br.x, 0), lo, hi))
                                     + LoadSceneColor((uint2)clamp(rp - int2(br.x, 0), lo, hi))
                                     + LoadSceneColor((uint2)clamp(rp + int2(0, br.y), lo, hi))
                                     + LoadSceneColor((uint2)clamp(rp - int2(0, br.y), lo, hi))) * 0.2;
                            break;
                        }
                    }
                    // Glint capped well below the old x4 blow-out: it should sparkle, not white out the bed below.
                    float sunSpec = min(pow(saturate(dot(Rw, normalize(_Sc_SunDir.xyz))), 220.0) * 1.5, 1.5);
                    reflCol += light * sunSpec;
                    col = lerp(col, reflCol, saturate(fres) * 0.35);

                    alpha = 1.0; // bed + reflection composited here → opaque output, no hardware double-blend
                    } // _Sc_ScreenFx (depth/opaque available)
                }
                else
                {
                    // Plain glass (no emission) reads as a frosted, milky pane — clearly glass, not an open hole
                    // — while emissive energy fields stay an airy, see-through curtain. Clear glass (#1274,
                    // the canopy/dome exception) arrives with TEXCOORD2.x = -1 from the mesher — the only
                    // negative value on that channel — and skips the frost for a faint, look-through pane.
                    col = lerp(col + light * 0.16 * (1.0 - isClear), col, isField); // the white frost on frosted glass only
                    alpha = lerp(lerp(0.72, 0.22, isClear), _BaseAlpha, isField);   // milky / clear pane vs. see-through field
                    alpha = saturate(alpha + emission * 0.15);
                    // A field whose tile carries a CUTOUT keeps its own silhouette (#1373): fire is a flame
                    // shape, not a square curtain. The energy fields' tiles are fully opaque, so this is a
                    // no-op for them, and glass ignores tile alpha entirely — a bad glass tile can no longer
                    // change how the pane reads, it only ever routes through here.
                    alpha = lerp(alpha, min(alpha, tex.a), isField);

                    // Dyed glass (#1126): only non-water faces reach this branch, and for those the mesher
                    // writes the cell's dye into TEXCOORD2.yzw (zero when undyed — fields/water never dye).
                    // The opaque shader's luminance recolour (mode 3), slightly gentler so the frost survives.
                    float3 dyeT = i.water.yzw;
                    if (dot(dyeT, float3(1.0, 1.0, 1.0)) > 0.01)
                    {
                        float glum = dot(col, float3(0.299, 0.587, 0.114));
                        col = lerp(col, glum * dyeT * 1.6, 0.75);
                    }
                }

                half4 outc = half4(col, alpha);
                outc.rgb = BbtsApplyHaze(outc.rgb, i.wp, 1.0); // the shared haze (#2393); cave water is kept clear by the camera's exposure
                return outc;
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off          // a single-layer pane/field reads from both sides
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun
            float _BaseAlpha;
            float4 _Sc_WaterTint; // #1758: per-world water colour (Sky.cs); read in mode 1
            float _Sc_WaterMode;  // #1758: 0 = the classic blue, 1 = tint, 2 = static rainbow bands by position

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1; // y carries the animation code of the tile (#1957); the rest is the opaque shader's
                // Water top faces: x=mode (1 lake, 2 open, 3 river), y=foam (corner-smoothed),
                // z=wave amplitude factor (corner-smoothed), w=flow axis (0=X, 1=Z).
                float4 water : TEXCOORD2;
                fixed4 color : COLOR; // r=gloss, g=metal, b=face shade, a=emission
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD3;
                float4 water : TEXCOORD4;
                fixed4 mat : COLOR;
                UNITY_FOG_COORDS(2)
            };

            // #1957 animated tiles. TEXCOORD1.y = tint mode (low 4 bits) + 16*frames + 256*speedIndex + 1024*stripStart
            // (BlockTextureAtlas.AnimationCode) — every term is exact in a float. The mesh UV stays on the block's
            // OWN atlas cell; an animated face is moved onto the strip cell of the current frame here, in the vertex
            // stage, so it costs the fragment stage nothing. 32 = atlas cells per side (AtlasBands.Cols/Rows).
            float2 BbtsAnimatedUv(float2 uv, float code, out float mode)
            {
                mode = fmod(code, 16.0);
                float frames = fmod(floor(code / 16.0), 16.0);
                if (frames < 1.5)
                {
                    return uv;
                }

                float speedIndex = fmod(floor(code / 256.0), 4.0);
                float fps = speedIndex < 0.5 ? 2.0 : speedIndex < 1.5 ? 4.0 : speedIndex < 2.5 ? 8.0 : 12.0;
                float slot = floor(code / 1024.0) + fmod(floor(_Time.y * fps), frames);
                float2 cell = floor(uv * 32.0);
                return (float2(fmod(slot, 32.0), floor(slot / 32.0)) + (uv * 32.0 - cell)) / 32.0;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Same water motion as the URP pass: down-only crossed sines scaled by the corner-
                // smoothed amplitude factor (open 1, lake 0.25, river/bank 0) and flattened into the
                // foam band — shared corners displace identically, the shoreline stays flush.
                // Gated on the water mode like the URP pass (#1374) — on a dyed pane those channels carry the
                // dye, not a wave, and the glass used to bob. Water always writes a positive mode.
                // #1749: the amplitude comes from the corner-smoothed weights — open water bobs fully, a calm
                // basin a quarter, a brook not at all — and never a falling flank (mode 4).
                float openW = saturate(v.water.x - 1.0);
                float brookW = saturate(v.water.z + v.water.w);
                float ampFactor = openW + (1.0 - openW) * (1.0 - brookW) * 0.25;
                float amp = (v.water.x > 0.5 && v.water.x < 3.5) ? 0.12 * ampFactor * (1.0 - saturate(v.water.y)) : 0.0;
                if (amp > 0.0005)
                {
                    float t = _Time.y;
                    float w = sin(wp.x * 0.50 + t * 1.10) + sin(wp.z * 0.41 + t * 1.43)
                            + sin((wp.x + wp.z) * 0.27 + t * 0.70);
                    wp.y -= amp * (0.5 + w / 6.0);
                }

                o.pos = UnityWorldToClipPos(wp);
                float unusedMode;
                o.uv = BbtsAnimatedUv(v.uv, v.sky.y, unusedMode);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.water = v.water;
                o.mat = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed3 albedo = tex.rgb;
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                float3 N = normalize(i.wn);
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float shade = lerp(0.7, 1.0, i.mat.b); // per-face shading baked by the mesher
                float emission = i.mat.a;              // glow (energy fields shine; plain glass = 0)

                fixed3 col = albedo * light * (0.55 + 0.45 * ndl) * shade;
                col += albedo * emission * 2.0;        // emissive energy-field glow (bloom catches it)

                // Match the opaque night ambient floor so water/glass don't read darker than the terrain at night.
                float nightFloor = saturate(0.6 - dot(light, fixed3(0.299, 0.587, 0.114)));
                col += albedo * fixed3(0.10, 0.13, 0.20) * nightFloor * shade;

                // Same water test as the URP pass (#1372/#1373): a see-through tile alone does not make a face
                // water — clear glass and the fire cutout have one too and must not take this branch.
                float isClear = saturate(-i.water.x);     // 1 for glass_clear (mesher writes -1, #1274)
                float isField = saturate(emission * 4.0); // ~1 for fire + energy fields, 0 for water and glass

                float alpha;
                if (i.water.x > 5.5)
                {
                    // #2134: the dense gas under the gas sea — its darkened, near-opaque tile, calm in this fallback pass
                    // (none of the open-water glint and foam below).
                    alpha = tex.a;
                }
                else if (tex.a < 0.95 && isClear < 0.5 && isField < 0.5)
                {
                    // Water: a clear blue body (no milky frost), alpha straight from the tile, so you see into
                    // and through it while swimming.
                    alpha = tex.a;

                    // #1758: the world's water colour — mirrors the URP pass (luminance recolour; mode 2 = static rainbow).
                    if (_Sc_WaterMode > 0.5)
                    {
                        float wlum = dot(col, fixed3(0.299, 0.587, 0.114));
                        float3 wtint = _Sc_WaterTint.rgb;
                        if (_Sc_WaterMode > 1.5)
                        {
                            float hue = frac((i.wp.x + i.wp.z) / 96.0);
                            wtint = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                            wtint = lerp(float3(0.5, 0.5, 0.5), wtint, 0.85);
                        }
                        col = lerp(col, wlum * wtint * 2.2, 0.85);
                    }

                    float mode = i.water.x;
                    float t = _Time.y;
                    if (mode > 3.5 && mode < 4.5) // #2128: mode 5 (the gas sea) is no waterfall — this fallback pass keeps it calm
                    {
                        // Waterfall flank: bright streaks racing straight DOWN the face (procedural on world
                        // height — atlas UVs cannot scroll; sin(k*y + w*t) moves the pattern downward).
                        // `across` only ever enters as a NESTED perturbation (a per-column phase offset): added
                        // linearly to the phase it tilted the streaks ~41 degrees into a diagonal glare (#1853).
                        float across = i.wp.x + i.wp.z;
                        float ph = i.wp.y * 3.0 + t * 6.5;
                        float rip = 0.5 + 0.5 * sin(ph + sin(across * 2.3) * 1.5);
                        col += light * 0.12 * rip;
                        float streak = smoothstep(0.84, 1.0, sin(ph * 1.27 + sin(across * 2.9) * 1.2));
                        col = lerp(col, light * fixed3(0.95, 0.98, 1.0), streak * 0.45);
                        alpha = saturate(alpha + streak * 0.30 + rip * 0.06);
                    }
                    else
                    {
                        // #1749: below the waterfall there is no branch. Open water, brook (along X and/or Z)
                        // and calm basin are WEIGHTS the mesher averages over block corners, so a body of
                        // varying width — or one full of reeds — blends from one look to the next instead of
                        // switching per cell and drawing a mosaic of ripple directions and brightness tiles.
                        float open = saturate(mode - 1.0);
                        float wX = saturate(i.water.z);
                        float wZ = saturate(i.water.w);
                        float calm = saturate(1.0 - open - wX - wZ);

                        // Brook: bright ripple bands + thin white streaks racing along the flow axis, one set
                        // per axis, each weighted. Procedural on world position — atlas UVs cannot scroll.
                        float ripSum = 0.0, streakSum = 0.0;
                        if (wX > 0.001)
                        {
                            float ph = i.wp.x * 1.9 - t * 5.5;
                            float rip = 0.5 + 0.5 * sin(ph + sin(i.wp.z * 2.7) * 1.2);
                            float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 + i.wp.z * 3.1));
                            ripSum += wX * rip;
                            streakSum += wX * streak;
                        }
                        if (wZ > 0.001)
                        {
                            float ph = i.wp.z * 1.9 - t * 5.5;
                            float rip = 0.5 + 0.5 * sin(ph + sin(-i.wp.x * 2.7) * 1.2);
                            float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 - i.wp.x * 3.1));
                            ripSum += wZ * rip;
                            streakSum += wZ * streak;
                        }
                        col += light * 0.10 * ripSum;
                        col = lerp(col, light * fixed3(0.95, 0.97, 1.0), streakSum * 0.35);
                        alpha = saturate(alpha + streakSum * 0.20 + ripSum * 0.04);

                        // Open water: a soft moving sun glint, plus an animated rippled foam band where the
                        // surface meets the shore (i.water.y fades over the last three blocks).
                        float glint = pow(0.5 + 0.5 * sin(i.wp.x * 1.7 + i.wp.z * 1.3 + t * 1.9), 6.0);
                        col += light * 0.06 * ndl * glint * open;
                        float foam = i.water.y * open;
                        if (foam > 0.01)
                        {
                            float cell = frac(sin(dot(floor(i.wp.xz * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                            float surge = 0.55 + 0.45 * sin(t * 1.6 + (i.wp.x + i.wp.z) * 0.9 + cell * 6.2832);
                            float f = saturate(foam * surge * (0.6 + 0.6 * cell));
                            col = lerp(col, light * fixed3(0.97, 0.99, 1.0), f * 0.85);
                            alpha = saturate(alpha + f * 0.45);
                        }

                        // Calm basin: a barely-there slow shimmer for whatever weight is left.
                        col += light * 0.03 * calm * (0.5 + 0.5 * sin(t * 0.6 + i.wp.x * 0.8 + i.wp.z * 1.1));
                    }
                }
                else
                {
                    // Plain glass (no emission) reads as a frosted, milky pane — clearly glass, not an open hole
                    // — while emissive energy fields stay an airy, see-through curtain. Clear glass (#1274,
                    // the canopy/dome exception) arrives with TEXCOORD2.x = -1 from the mesher — the only
                    // negative value on that channel — and skips the frost for a faint, look-through pane.
                    col = lerp(col + light * 0.16 * (1.0 - isClear), col, isField); // the white frost on frosted glass only
                    alpha = lerp(lerp(0.72, 0.22, isClear), _BaseAlpha, isField);   // milky / clear pane vs. see-through field
                    alpha = saturate(alpha + emission * 0.15);
                    // A field with a CUTOUT tile keeps its silhouette (#1373 — fire is flame-shaped); the energy
                    // fields' tiles are opaque, so this is a no-op for them.
                    alpha = lerp(alpha, min(alpha, tex.a), isField);

                    // Dyed glass (#1126): mirrors the URP pass — dye from TEXCOORD2.yzw, gentle luminance
                    // recolour so the frosted look survives (zero for undyed glass, fields and water).
                    fixed3 dyeT = i.water.yzw;
                    if (dot(dyeT, fixed3(1.0, 1.0, 1.0)) > 0.01)
                    {
                        float glum = dot(col, fixed3(0.299, 0.587, 0.114));
                        col = lerp(col, glum * dyeT * 1.6, 0.75);
                    }
                }

                fixed4 outc = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }

    Fallback Off
}
