// Lit + reflective opaque shader for the block atlas (M27 renderer). Samples the atlas, shades each
// face by the real sun direction (normal . sun) in the per-system sun colour, and adds per-block
// specular highlights + a cheap grazing-angle environment reflection. Three globals, set by Sky:
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 marks it as set)
//   _Sc_SunDir = world-space direction TO the sun
//   _Sc_Sky    = current sky colour (sampled for environment reflections)
// Per-block material comes from the vertex colour: r=gloss, g=metal, b=per-face AO.
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (HLSL, UniversalForward), SubShader 2 is the
// original Built-in RP pass (CG, unchanged). The active pipeline picks the matching SubShader, so the project
// renders identically in Built-in today and gains URP when the pipeline asset is assigned. The fragment maths
// is identical in both — both bypass the engine's light passes and read the same _Sc_* globals.
Shader "BlocksBeyondTheStars/BlockAtlas"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _HeightTex ("Height", 2D) = "white" {}
        _LeafCutoff ("Leaf alpha cutoff", Range(0,1)) = 0.5
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
            // Alpha-to-coverage smooths the hard foliage cutout edges against MSAA (opaque tiles have a=1 →
            // full coverage, unaffected); kills the shimmering on leaf/grass silhouettes for free.
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // (No multi_compile_fog: the haze is the explicit AtmosphereCommon blend; Unity's fog variants were
            // stripped from every player build anyway and would only double the haze in the Editor.)
            // Receive the sun's real-time shadow map (URP main light).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // Staged crowns: the instanced variant draws the near micro-block clusters (CrownClusters.cs).
            // Everything it adds sits behind the two instancing variants (arrays, or #4046's buffer on the graphics
            // card) — the chunk meshes' variant is unchanged.
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:BbtsCrownInstance
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FxCommon.hlsl" // VFX overhaul (#2152): FX lights + scan wave globals
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): haze + height fog, cloud shadows, torch flicker
            #include "FloraAccent.hlsl" // #2834: the flora re-tint with the accent colours of blossoms and berries
            #include "Headlamp.hlsl"    // #3229: the suit headlamps (own + other players'), with the High occupancy shadows

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            #include "PlantFar.hlsl" // The plants' far pictures baked from their bodies
            // Calm tiles: one texel per atlas slot — 1 = the texture calm applies to that tile, 0 = the tile is
            // calm by design and keeps its pattern (BlockTextureAtlas.Look.cs publishes it; read with the atlas' sampler).
            TEXTURE2D(_Sc_LookTileMask);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_HeightTex); SAMPLER(sampler_HeightTex); // #3055: per-texel height (1 = surface), High only

            // Globals fed by Sky/player via Shader.SetGlobal* — kept outside any per-material cbuffer.
            float  _Sc_Relief;     // #3055: 1 = High-preset parallax on (desktop), else 0
            // #3057: the room the camera stands in (RoomReflections.cs) — its cube, box and blend weight (w = 0: none).
            TEXTURECUBE(_Sc_RoomCube); SAMPLER(sampler_Sc_RoomCube);
            float4 _Sc_RoomProbe;  // xyz = probe position (world), w = weight 0..1
            float4 _Sc_RoomMin;    // xyz = room box min (world)
            float4 _Sc_RoomMax;    // xyz = room box max (world), w = the cube's last mip
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float  _Sc_Indoor;
            float4 _Sc_FloraTint;
            // Cabin light (LookCabinLight.cs): the brightest a white texel of a strong emitter under cover may get
            // (linear, before tone mapping); 0 = no limit.
            float  _Sc_LookCabin;

            // A ceiling of lamps burnt out to one white area — a lamp face reached ~4.5 (emission 3 ×
            // albedo + its own block light + the fill) and the bloom spread the excess over its dark rim and the panels
            // between. Under cover (skylight at or below the shade floor) a strong emitter's face is scaled as a whole by
            // a soft knee on its brightest possible texel (emission + block light + fill on white), so the face keeps its
            // own contrast (lens and rim) and the bloom only gets a glow. Faces under open sky, weak emitters (ores,
            // crystals below 0.45) and every non-emitting face are untouched; a torch keeps its flicker (the knee reads
            // the steady emission).
            float3 BbtsLookCabinLimit(float3 col, float emission, float3 blockLight, float sky)
            {
                float cap = _Sc_LookCabin;
                if (cap <= 0.0 || emission <= 0.45)
                {
                    return col;
                }

                float weight = smoothstep(0.45, 0.7, emission) * saturate((1.0 - sky) / (1.0 - BBTS_SHADE_FLOOR));
                float peak = 3.0 * emission + 2.0 * max(blockLight.r, max(blockLight.g, blockLight.b)) + 0.5;
                float knee = 0.6 * cap;
                if (weight <= 0.0 || peak <= knee)
                {
                    return col;
                }

                float over = peak - knee;
                float limited = knee + over / (1.0 + over / (cap - knee));
                return col * lerp(1.0, limited / peak, weight);
            }

            // SRP Batcher (#573): the per-MATERIAL properties (the Properties block above) live here and
            // nowhere else, with the same layout in every pass — the ShadowCaster below repeats it. The _Sc_*
            // globals above stay outside on purpose: they are per-frame values (Shader.SetGlobal*), and the
            // batcher would otherwise serve each material's own stale copy of them.
            CBUFFER_START(UnityPerMaterial)
                float _LeafCutoff;
            CBUFFER_END

            // Staged crowns: what a chunk-mesh vertex carries per crown CELL comes per instance here.
            #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(BbtsCrownProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _CcTile)     // xy = the leaf tile's centre (atlas UV), z = emission, w = metal
                UNITY_DEFINE_INSTANCED_PROP(float4, _CcTint)     // rgb = species tint or dye (black = the world's flora hue), a = tint mode
                UNITY_DEFINE_INSTANCED_PROP(float4, _CcLight)    // rgb = placed block light, a = skylight
                UNITY_DEFINE_INSTANCED_PROP(float4, _CcLightDir) // xyz = the block light's dominant direction
            UNITY_INSTANCING_BUFFER_END(BbtsCrownProps)
            #endif

            #include "GpuInstances.hlsl" // #4046: on a buffer draw the instance comes from the graphics card
            void BbtsCrownInstance()
            {
                #if defined(BBTS_GPU_INSTANCES)
                    BBTS_GPU_INSTANCE(inst)
                    _CcTile = inst.a;
                    _CcTint = inst.b;
                    _CcLight = inst.c;
                    _CcLightDir = inst.d;
                #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 normal : NORMAL;  // xyz = face normal, w = parallax relief 0..1 (#3055; 0 on meshes without it)
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1;  // x = skylight, y = tint mode (1 flora, 2 hull paint, 3 player dye, 4 bark)
                float4 leaf : TEXCOORD2; // x = cutout code (0 none, 1 crown face, 2 rigid prop, 3 cross plant, 4 aquatic plant — #2519), yzw = flora/hull/dye/bark tint (black = use global flora hue)
                float3 bl : TEXCOORD3;   // propagated coloured block-light (placed lights illuminate)
                float3 blDir : TEXCOORD4; // dominant block-light direction (toward source); 0 = none
                float4 color : COLOR;    // r=gloss, g=metal, b=face AO, a=emission
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                float3 cube : TEXCOORD5; // The middle of the small cube this vertex belongs to (cluster-local)
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD3;
                float3 skyl : TEXCOORD4; // x = skylight, y = tint mode, z = parallax relief (#3055)
                float4 leaf : TEXCOORD5;
                float4 mat : TEXCOORD6;
                float3 bl : TEXCOORD7;
                float3 blDir : TEXCOORD8;
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

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    // Staged crowns: a near cluster. The cell's tile, tint, light and material come from the
                    // instance, the cube's tone and class from the shared mesh; a cube right at the eye shrinks away.
                    float4 ccTile = UNITY_ACCESS_INSTANCED_PROP(BbtsCrownProps, _CcTile);
                    float4 ccTint = UNITY_ACCESS_INSTANCED_PROP(BbtsCrownProps, _CcTint);
                    float4 ccLight = UNITY_ACCESS_INSTANCED_PROP(BbtsCrownProps, _CcLight);
                    float4 ccLightDir = UNITY_ACCESS_INSTANCED_PROP(BbtsCrownProps, _CcLightDir);
                    v.uv = ccTile.xy;
                    // Leaf picture: the leaf tile runs across the cubes of the cell — every face shows the part
                    // of the tile that lies where the face lies in its cell, seen along the face's normal. In world axes,
                    // so the picture does not turn with the cluster and runs on from cell to cell; from the mesh position
                    // before the shrink below, so it does not swim on a cube that shrinks away.
                    if (_Sc_LookLeaf.x > 0.0)
                    {
                        float3 inCell = mul((float3x3)GetObjectToWorldMatrix(), v.positionOS.xyz) + 0.5;
                        float3 faceAxis = abs(TransformObjectToWorldNormal(v.normal.xyz));
                        float2 inTile = faceAxis.y > 0.5 ? inCell.xz : faceAxis.x > 0.5 ? inCell.zy : inCell.xy;
                        v.uv = (floor(ccTile.xy * 32.0) + clamp(inTile, 0.01, 0.99)) / 32.0;
                    }

                    v.sky = float2(ccLight.a, ccTint.a);
                    v.leaf.yzw *= ccTint.rgb;
                    v.bl = ccLight.rgb;
                    v.blDir = ccLightDir.xyz;
                    v.color.g = ccTile.w;
                    v.color.a = ccTile.z;
                    float ccScale = BbtsCrownCubeScale(TransformObjectToWorld(v.cube), _WorldSpaceCameraPos);
                    v.positionOS.xyz = lerp(v.cube, v.positionOS.xyz, ccScale);
                #endif
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float tintMode;
                o.uv = BbtsAnimatedUv(v.uv, v.sky.y, tintMode);
                // #2397: leaves and plants sway in the wind (the tile's v = 0 root … 1 tip for a plant quad).
                // #2519: torches and lanterns stand rigid, sheltered plants stand still (BbtsBlockSway).
                wp += BbtsBlockSway(wp, v.leaf.x, tintMode, v.sky.x, frac(v.uv.y * 32.0));
                // The small cubes of a micro-block crown rock in the wind — not the closed far cubes of staged crowns.
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                #else
                    if (_Sc_Look2.y < 2.5)
                    {
                        wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                    }
                #endif
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal.xyz);
                o.wt = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.wp = wp;
                // The fragment stage sees the plain tint mode. Relief is packed at 114/127 of its value (ChunkMeshData.
                // ReliefScale): a mesh built with 3-component normals reads w = 1 here and must stay flat.
                o.skyl = float3(v.sky.x, tintMode, v.normal.w > 0.95 ? 0.0 : v.normal.w * (127.0 / 114.0));
                o.leaf = v.leaf;
                o.mat = v.color;
                o.bl = v.bl;
                o.blDir = v.blDir;
                return o;
            }

            // #3055: parallax occlusion mapping (High preset, desktop). Marches the height atlas in the face's tangent
            // space — 12 fixed layers plus one secant step — and returns the UV where the view ray meets the relief.
            // One block face is one atlas cell (1/32 of the UV range) and 1 m, so a shift of s metres is s/32 in UV; the
            // result stays inside the cell (no neighbouring tile bleeds in). Explicit-gradient samples only: WebGPU
            // allows no implicit derivatives inside a loop. Fades out between 12 and 24 m, where it would only alias.
            float2 BbtsReliefUv(float2 uv, float2 dUx, float2 dUy, float3 wn, float4 wt, float3 wp, float relief)
            {
                float scale = 0.04 * relief * (1.0 - smoothstep(12.0, 24.0, distance(_WorldSpaceCameraPos, wp)));
                if (scale < 0.0005)
                {
                    return uv;
                }

                float3 gN = normalize(wn);
                float3 T = normalize(wt.xyz);
                float3 B = normalize(cross(gN, T) * wt.w);
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float3 vT = float3(dot(V, T), dot(V, B), dot(V, gN));
                float2 shiftMax = -vT.xy / max(vT.z, 0.2) * (scale / 32.0);
                float2 cell = floor(uv * 32.0);
                float2 lo = (cell + 0.02) / 32.0; // ~1.3 texels inside the cell, so the bilinear height never reads the next tile
                float2 hi = (cell + 0.98) / 32.0;

                float prevLayer = 0.0;
                float prevDepth = 0.0;
                float hitLayer = 1.0;
                bool found = false;
                [unroll]
                for (int s = 0; s <= 12; s++)
                {
                    float layer = s / 12.0;
                    float2 p = clamp(uv + shiftMax * layer, lo, hi);
                    float depth = 1.0 - SAMPLE_TEXTURE2D_GRAD(_HeightTex, sampler_HeightTex, p, dUx, dUy).r;
                    if (!found && layer >= depth)
                    {
                        float a = prevDepth - prevLayer; // the ray was above the relief at the previous layer …
                        float b = layer - depth;         // … and is at or below it now: the crossing lies between
                        hitLayer = s == 0 ? 0.0 : lerp(prevLayer, layer, a / max(a + b, 1e-4));
                        found = true;
                    }

                    prevLayer = layer;
                    prevDepth = depth;
                }

                return clamp(uv + shiftMax * hitLayer, lo, hi);
            }

            // #3057: how much the room cube replaces the sky reflection at wp — the probe's weight inside the room box,
            // fading out within 0.6 m beyond it (walls, floor and ceiling lie on the box faces).
            float BbtsRoomWeight(float3 wp)
            {
                float3 d = max(_Sc_RoomMin.xyz - wp, wp - _Sc_RoomMax.xyz);
                return _Sc_RoomProbe.w * saturate(1.0 - max(max(d.x, d.y), d.z) / 0.6);
            }

            // #3057: the room's reflection along R, box-projected (parallax-corrected) onto the room box and blurred by
            // roughness through the cube's mips.
            float3 BbtsRoomReflection(float3 wp, float3 R, float rough)
            {
                float3 Rs = R + 1e-5 * (step(0.0, R) * 2.0 - 1.0); // never 0: a face on the box would divide 0 by 0
                float3 toMax = (_Sc_RoomMax.xyz - wp) / Rs;
                float3 toMin = (_Sc_RoomMin.xyz - wp) / Rs;
                float3 far3 = max(toMax, toMin);
                float t = min(min(far3.x, far3.y), far3.z);
                float3 dir = wp + R * max(t, 0.0) - _Sc_RoomProbe.xyz;
                return SAMPLE_TEXTURECUBE_LOD(_Sc_RoomCube, sampler_Sc_RoomCube, dir, rough * _Sc_RoomMax.w).rgb;
            }

            // A closed crown face drawn as rows of small round puffs — every puff a shade of its own (a hash of
            // the puff and its block), every other row shifted by half a puff, dark between them, and a dome-like tilt
            // of the normal so the light models each one. A stand-in for real small bodies: it shows how a closed,
            // chunky crown reads next to the airy leaf cutout. Returns x = the shade, yz = the tangent-space tilt.
            float3 BbtsCrownPuffs(float2 uv, float3 wp, float3 wn)
            {
                float3 block = floor(wp - normalize(wn) * 0.5);
                float2 g = frac(uv * 32.0) * 3.0;
                g.x += 0.5 * step(0.5, frac(floor(g.y) * 0.5 + BbtsHash3(block)));
                float2 cell = floor(g);
                float2 f = g - cell - 0.5;
                float h = BbtsHash3(float3(cell, 1.0) + block * 3.7);
                float r2 = dot(f, f) * 4.0; // 0 at the puff's centre, 1 at its cell's edge, 2 in the corner
                float shade = lerp(0.84, 1.12, h) * lerp(1.0, 0.60, smoothstep(0.55, 1.25, r2));
                return float3(shade, f * (1.5 * saturate(1.0 - r2 * 0.35)));
            }

            // Staged crowns: the far picture of a crown cell — the closed cube face painted as 4 × 4 small
            // cubes (25 cm, between the real ones' 12.5 and 37.5 cm) in the micro blocks' range of tones, about one in
            // seven a dark gap. A stand-in for the real cluster beyond its range, where a cube is a few pixels wide.
            // Returns the shade.
            float BbtsCrownFar(float2 uv, float3 wp, float3 wn)
            {
                float3 n = normalize(wn);
                float3 block = floor(wp - n * 0.5);
                float2 cell = floor(frac(uv * 32.0) * 4.0);
                float h = BbtsHash3(float3(cell, dot(abs(n), float3(1.0, 2.0, 3.0))) + block * 3.7);
                return h < 0.14 ? 0.5 : lerp(0.74, 1.1, frac(h * 7.31));
            }

            // Soft blocks — every block face reads as a stone of its own: its border rounds off toward the
            // neighbour (a tilt of the normal within the outer `_Sc_Look3.x` of the tile) and a dark joint runs along
            // it. Each edge has its own strength from a hash of the two cells it lies between — both faces that share
            // the edge get the same value, and about a third of the joints all but vanish, so flat ground reads as
            // slabs of several sizes instead of a grid. Nothing in the mesh or the data changes.
            // Returns xy = the tangent-space tilt, z = the joint's shade (1 = none).
            float3 BbtsSoftBlock(float2 uv, float3 wp, float3 gN, float3 T, float3 B)
            {
                float2 t = frac(uv * 32.0);
                float2 side = step(0.5, t) * 2.0 - 1.0; // the nearer edge on each axis: -1 the low one, +1 the high one
                float2 d = min(t, 1.0 - t);
                float3 cell = floor(wp - gN * 0.5);
                float hu = BbtsHash3(cell * 2.0 + round(T) * side.x);
                float hv = BbtsHash3(cell * 2.0 + round(B) * side.y);
                float2 edge = float2(hu < 0.3 ? 0.08 : lerp(0.55, 1.0, hu), hv < 0.3 ? 0.08 : lerp(0.55, 1.0, hv));
                float2 k = saturate(1.0 - d / max(_Sc_Look3.x, 0.01)) * edge;
                float joint = max(smoothstep(0.62, 1.0, k.x), smoothstep(0.62, 1.0, k.y));
                return float3(side * k * k * 0.9, 1.0 - _Sc_Look3.y * joint);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Explicit gradients from the mesh UV: the parallax offset below must not change the mip selection.
                float2 dUx = ddx(i.uv);
                float2 dUy = ddy(i.uv);
                float2 uv = i.uv;
                // #3461: inside the distance haze (worlds with air only) the texture detail softens — 0 nearby and on
                // airless worlds. The relief and the normal map calm down by the same share.
                float soft = BbtsDistSoftTexture(i.wp);
                float relief = i.skyl.z * (1.0 - soft);
                if (_Sc_Relief > 0.5 && relief > 0.004)
                {
                    uv = BbtsReliefUv(i.uv, dUx, dUy, i.wn, i.wt, i.wp, relief);
                }

                float4 texel = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dUx, dUy);
                // Plant far pictures: a crossed picture whose plant has a body shows the body's baked outline.
                float2 farUv, farCell;
                bool farPicture = BbtsPlantFarUv(uv, i.leaf.x, farUv, farCell);
                if (farPicture)
                {
                    texel = SAMPLE_TEXTURE2D_GRAD(_Sc_LookPlantFar, sampler_MainTex, farUv, BbtsPlantFarGrad(dUx), BbtsPlantFarGrad(dUy));
                }

                // A closed tree crown keeps its faces whole (no leaf holes) and wears small cubes instead.
                bool closedCrown = _Sc_Look2.y > 0.5 && i.leaf.x > 0.5 && i.leaf.x < 1.5;
                if (i.leaf.x > 0.5 && !closedCrown)
                {
                    clip(texel.a - _LeafCutoff);
                }

                // Staged crowns: the screen-door cross-fade at the end of the near range — a pixel shows the
                // near cluster or the far cube, never both (BbtsCrownNearShare / BbtsIgn in AtmosphereCommon).
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    clip(BbtsCrownNearShare(i.wp, _WorldSpaceCameraPos) - BbtsIgn(i.positionCS.xy) - 0.0001);
                #else
                    bool farCrown = closedCrown && _Sc_Look2.y > 2.5;
                    if (farCrown)
                    {
                        clip(BbtsIgn(i.positionCS.xy) - BbtsCrownNearShare(i.wp, _WorldSpaceCameraPos));
                    }

                    // Plant bodies: a plant with a body shows its crossed picture beyond the bodies' range only.
                    if (BbtsStagedPlant(i.leaf.x))
                    {
                        clip(BbtsIgn(i.positionCS.xy) - BbtsPlantNearShare(i.wp, _WorldSpaceCameraPos));
                    }
                #endif

                // Calm surfaces — the #3461 fade toward the tile's average colour, here on every natural face at
                // any distance (BbtsLookCalm). The relief and the normal map keep their detail: the light draws the grain.
                // A calm tile is left alone by the look's calm (its slot's mask texel is 0); the haze's own softening
                // (`soft`) still reaches it.
                float labCalm = BbtsLookCalm(i.skyl.y, i.leaf.x, i.mat);
                if (labCalm > 0.001)
                {
                    labCalm *= SAMPLE_TEXTURE2D_LOD(_Sc_LookTileMask, sampler_MainTex, (floor(uv * 32.0) + 0.5) / 32.0, 0).r;
                }

                float colourSoft = closedCrown ? 1.0 : max(soft, labCalm);
                // Leaf picture: the small cubes of a near cluster show the leaf tile at a share of its strength
                // (_Sc_LookLeaf.x) instead of the tile's mean alone. A cube stays whole: where the tile has a hole — the
                // gap between two leaves — the mean colour stands, a little darker. The picture fades out with distance,
                // where a leaf is smaller than a pixel and its pattern would only shimmer.
                float leafGap = 1.0;
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    if (_Sc_LookLeaf.x > 0.0)
                    {
                        float leafShare = _Sc_LookLeaf.x * (1.0 - smoothstep(_Sc_LookLeaf.y, _Sc_LookLeaf.z, distance(_WorldSpaceCameraPos, i.wp)));
                        float leafSolid = step(_LeafCutoff, texel.a);
                        colourSoft = max(soft, 1.0 - leafShare * leafSolid);
                        leafGap = 1.0 - _Sc_LookLeaf.w * leafShare * (1.0 - leafSolid);
                    }
                #endif
                if (colourSoft > 0.001)
                {
                    // #3461: fade toward the tile's average colour instead of a coarser mip: the point-filtered mips would
                    // only move their hard bands closer, and the cutout leaves would lose coverage. The atlas holds 32 × 32
                    // tiles of 64 px, so one mip (6, or 5 under Potato's halving mip limit — Sky sends it in
                    // _Sc_DistSoft.w) has exactly one texel per tile: its average, nothing of the neighbour bled in. A
                    // cutout tile's holes are black (the alpha bake cuts a dark ground), so its mean is divided by its
                    // alpha to get the leaves' own colour. The alpha stays untouched — distant trees keep their leaves.
                    float2 cellUv = (floor(uv * 32.0) + 0.5) / 32.0;
                    float4 mean = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, cellUv, max(_Sc_DistSoft.w, _Sc_Look2.x));
                    if (farPicture) // The far picture's own mean (its atlas ignores the presets' mip limit)
                    {
                        mean = SAMPLE_TEXTURE2D_LOD(_Sc_LookPlantFar, sampler_MainTex, (farCell + 0.5) / max(_Sc_LookPlantFarInfo.xy, float2(1.0, 1.0)), _Sc_LookPlantFarInfo.w);
                    }

                    float3 meanRgb = i.leaf.x > 0.5 ? saturate(mean.rgb / max(mean.a, 0.25)) : mean.rgb;
                    texel.rgb = lerp(texel.rgb, meanRgb, colourSoft);
                }

                texel.rgb *= leafGap;
                float3 puff = float3(1.0, 0.0, 0.0);
                if (closedCrown && _Sc_Look2.y < 1.5)
                {
                    // Crown mode B: painted puffs. Mode C (micro blocks) has real small cubes — the mesher gave each its
                    // own shade and tint, the face itself stays flat.
                    puff = BbtsCrownPuffs(i.uv, i.wp, i.wn);
                    texel.rgb *= puff.x;
                }

                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    if (farCrown)
                    {
                        texel.rgb *= BbtsCrownFar(i.uv, i.wp, i.wn); // staged crowns: the far picture's painted cubes
                    }
                #endif

                float3 albedo = texel.rgb;
                if (i.skyl.y > 4.5)
                {
                    // Molten lava surface (mode 5): keep the lava tile untinted — the animated crust is applied
                    // at the emissive step below, so it modulates the GLOW rather than the base colour.
                }
                else if (i.skyl.y > 3.5)
                {
                    // Bark (mode 4): the tree trunk's per-world DARK bark hue (TEXCOORD2.yzw). Forced dark at
                    // the source so it always reads clearly darker than the bright leaf tint. A gentler blend
                    // than leaves keeps the bark grain. Black (no resolver / ship mesh) → keep natural bark.
                    if (dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01)
                    {
                        float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                        albedo = lerp(albedo, lum * i.leaf.yzw * 1.4, 0.72);
                    }
                }
                else if (i.skyl.y > 2.5 && BbtsAccentPick(i.leaf.x) > 3.5)
                {
                    albedo = BbtsDyedBlossom(albedo, i.leaf.yzw); // #4012: a dyed flower — the dye on the blossom only
                }
                else if (i.skyl.y > 2.5)
                {
                    // Player dye (mode 3): a luminance-based recolour applied everywhere (independent of the
                    // flora-tint global), so dyed building blocks read vividly on any world and in caves.
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * i.leaf.yzw * 1.6, 0.85);
                }
                else if (i.skyl.y > 1.5)
                {
                    // Hull paint (item 32): multiply the per-ship tint (TEXCOORD2.yzw) into the albedo —
                    // the same look as the old tinted-silhouette hull (_Color * tex), independent of the
                    // flora globals so painted ships read identically on planets and in space.
                    albedo *= i.leaf.yzw;
                }
                else if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    // Per-SPECIES tint from the mesh (TEXCOORD2.yzw) when present; meshes built without a
                    // tint resolver carry black there and keep the planet's uniform global hue.
                    float3 tint = dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01 ? i.leaf.yzw : _Sc_FloraTint.rgb;
                    albedo = BbtsFloraTint(albedo, tint, i.leaf.x); // #2834: blossoms and berries in their own colours
                }

                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float3 gN = normalize(i.wn);
                float3 T = normalize(i.wt.xyz);
                float3 B = normalize(cross(gN, T) * i.wt.w);
                float4 nrm = SAMPLE_TEXTURE2D_GRAD(_NormalTex, sampler_NormalTex, uv, dUx, dUy);
                if (farPicture)
                {
                    nrm = float4(0.5, 0.5, 1.0, 1.0); // The old tile's relief does not belong to the baked outline
                }

                float3 tn = nrm.xyz * 2.0 - 1.0;
                tn.xy *= 1.0 - soft; // #3461: the relief light calms with the texture detail inside the haze
                // A closed crown takes its puffs' tilt instead of the leaf tile's relief; every other opaque
                // face gets the soft block's rounded border and joint (BbtsSoftBlock) on top of its own relief.
                float jointShade = 1.0;
                if (closedCrown)
                {
                    tn.xy = puff.yz;
                }
                else if (_Sc_Look3.x > 0.0 && i.leaf.x < 0.5)
                {
                    float3 softBlock = BbtsSoftBlock(i.uv, i.wp, gN, T, B);
                    tn.xy += softBlock.xy;
                    jointShade = softBlock.z;
                }

                float3 N = normalize(tn.x * T + tn.y * B + tn.z * gN);
                // Fine grain on natural faces (the tile gives the colour, the grain the surface).
                if (_Sc_Look3.z > 0.0)
                {
                    albedo *= BbtsLookGrain(i.wp - gN * 0.01, _Sc_Look3.z * BbtsLookNatural(i.skyl.y, i.mat), distance(_WorldSpaceCameraPos, i.wp));
                }

                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = saturate(dot(N, L));
                float sky = saturate(i.skyl.x);

                // #2398: wet ground after rain, snow caps after snowfall — upward, skylit faces only (not the lava
                // surface, whose glow is the point). Adjusts the albedo and the material gloss in place.
                float glossIn = i.mat.r;
                if (i.skyl.y < 4.5)
                {
                    BbtsWeatherSurface(albedo, glossIn, i.wp, gN, sky);
                }

                // Sun shadow map (URP main light): 1 where lit, →0 in shadow. Only the direct-sun terms are
                // shadowed (ambient/sky fill + emissive stay), so shadowed faces dim but never go black.
                // #1518: the lookup only feeds terms multiplied by ndl*sky, so a face turned away from the sun
                // or without skylight (caves, interiors — about half of all voxel faces) skips the soft-shadow
                // tent filter entirely; the branch is per-face coherent and the result is identical.
                // #1612: blended toward "lit" by the URP shadow-distance fade, so the shadow map's edge dissolves
                // instead of marching across the terrain as a hard line at the preset distance (40/70/110 m).
                float shadow = (ndl * sky > 0.0)
                    ? lerp(MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp)), 1.0, GetMainLightShadowFade(i.wp))
                      * BbtsCloudShade(i.wp) // #2394: drifting cloud shadows ride the same direct-sun term
                    : 0.0;

                // Per-vertex AO (mesher, in .b) widened to a visible contact-shadow range, then multiplied by
                // the texture-scale cavity AO (normal map alpha). Diffuse-only — spec/reflection stay crisp.
                float faceAo = lerp(0.62, 1.0, i.mat.b) * lerp(1.0, closedCrown ? 1.0 : nrm.a, 0.6) * jointShade;
                // #1457 (open sky 0.70 → 0.78), #1611 (sealed end 0.26 → 0.32), #3265: the sky ambient ramp of AtmosphereCommon —
                // unchanged from the shade floor up, almost nothing in a sealed cave or tunnel (0.02), where the flat dark
                // floor below, the lamps and the torches are what is left. Dark, never pitch black.
                float amb = BbtsSkyAmbient(sky);
                // #1608: the shadow map owns "is the sun blocked". The mesher skylight gates the direct sun (and the
                // specular below) at half weight only — saturate(sky*2) — so partially covered ground keeps the sun
                // spots the foliage alpha-clip punches through a canopy (dappled light), instead of the skylight
                // zeroing them before the shadow map ever gets a say.
                float sunOpen = saturate(sky * 2.0);
                // #2814: indoors the fill — and the small flat floor with it — takes the home's colour (warm white by
                // day, amber in the evening; white on a station and wherever nothing set it).
                float3 indoorCol = BbtsIndoorColor();
                float indoorShare = _Sc_Indoor * (1.0 - sky);
                // BbtsWorldLight is the plain light * (amb + 0.5 * ndl * sunOpen * shadow) at an amount of 0.
                float3 col = albedo * (BbtsWorldLight(light, amb, ndl * sunOpen * shadow, gN) + BbtsDarkFloor() * lerp(float3(1, 1, 1), indoorCol, saturate(indoorShare))) * faceAo;
                col += albedo * indoorCol * (indoorShare * 0.5) * faceAo;

                // Night/atmosphere ambient floor: a faint cool fill on open-sky faces, strongest when the sun
                // light is weak (night/storm) and fading out toward day, so worlds read as dim atmospheric blue
                // at night instead of crushing to pure black. Independent of the (near-black) night sky colour
                // and the sun tint; gated by skylight so caves/interiors stay dark and still need a lamp.
                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * (sky * nightFloor) * faceAo;

                float gloss = glossIn;            // perceptual smoothness (0 = matte .. 1 = mirror), wet/snow-adjusted (#2398)
                float metal = i.mat.g;            // metallic (0 = dielectric .. 1 = metal)
                float rough = clamp(1.0 - gloss, 0.045, 1.0);

                // Specular: Unity's stable GGX form (no NaN at low roughness), tinted by the metallic F0
                // (0.04 dielectric → albedo for metals). Tight, bright glints on smooth/metal faces feed the bloom.
                float3 H = normalize(L + V);
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float dterm = nh * nh * (r2 - 1.0) + 1.00001;
                float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                col += light * F0 * (specTerm * ndl * sunOpen * shadow);

                // #2401: caustics on the sea floor while the camera is under water.
                col += albedo * light * (BbtsCaustics(i.wp, gN, sky) * 0.7);

                // #2403: thin, translucent things let the sun through — leaves and plants glow when the sun is behind
                // them (a wrap-lit back-light term, strongest looking into a low sun), so a canopy reads as foliage,
                // not as painted cubes. Skylit faces only; the shadow map keeps a shaded crown dark.
                // #2519: a torch or lantern (cutout code 2) is a cutout, not a leaf — no sun glowing through it.
                bool rigidProp = i.leaf.x > 1.5 && i.leaf.x < 2.5;
                bool translucent = !rigidProp && !closedCrown && (i.leaf.x > 0.5 || (i.skyl.y > 0.5 && i.skyl.y < 1.5));
                if (translucent)
                {
                    float back = pow(saturate(dot(-V, L)), 3.0);
                    float wrap = saturate(dot(N, L) * 0.5 + 0.5);
                    col += albedo * light * (back * 0.45 + wrap * 0.12) * sunOpen * shadow * sky;
                }

                // Rim light — a closed crown's puffs catch a warm edge where they turn away from the eye,
                // strongest looking toward the sun.
                if (closedCrown && _Sc_Look3.w > 0.0)
                {
                    float rim = pow(1.0 - saturate(dot(N, V)), 3.0) * (0.35 + 0.65 * saturate(dot(-V, L) * 0.5 + 0.5));
                    col += albedo * light * (_Sc_Look3.w * rim) * sunOpen * sky;
                }

                // Environment reflection of the sky colour, roughness-aware: metals reflect strongly (tinted by
                // F0) even head-on, dielectrics mostly at grazing angles (Fresnel). Additive, faded by skylight.
                // #3057: inside a ship or base room (High, desktop) the room itself is reflected instead — no skylight
                // gate there, a polished floor shows the lamps and walls around it.
                float3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float envShare = sky;
                if (_Sc_RoomProbe.w > 0.001)
                {
                    float roomW = BbtsRoomWeight(i.wp);
                    if (roomW > 0.0)
                    {
                        envCol = lerp(envCol, BbtsRoomReflection(i.wp, reflect(-V, N), rough), roomW);
                        envShare = lerp(sky, 1.0, roomW);
                    }
                }

                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, 5.0);
                float gr = 1.0 - rough;
                float3 Fr = F0 + (max(float3(gr, gr, gr), F0) - F0) * fres;
                col += envCol * Fr * (saturate(gloss) * envShare * 0.5);

                // Lava surface (mode 5) flows: a slow procedural crust redistributes the glow — dark cooled
                // slabs drift over bright cracks (viscous, ~1/3 water speed; average brightness ≈ unchanged so
                // bloom stays balanced). Procedural on world XZ + time because atlas UVs can't scroll.
                float lavaGlow = 1.0;
                if (i.skyl.y > 6.5)
                {
                    // TORCH FLAME (mode 7): an open flame never burns steady. Two detuned sine bands plus a
                    // sharper occasional gust give an irregular flicker, and the phase is offset by the torch's
                    // own world position so a row of them along a wall flickers out of step instead of pulsing
                    // in unison. Averages slightly above 1.0 so a torch-lit room does not read dimmer than one
                    // lit by a steady lamp.
                    float seed = i.wp.x * 12.9898 + i.wp.z * 78.233 + i.wp.y * 37.719;
                    float ft = _Time.y;
                    float flick = 0.62 * sin(ft * 11.0 + seed)
                                + 0.28 * sin(ft * 19.7 + seed * 1.7)
                                + 0.34 * smoothstep(0.72, 1.0, sin(ft * 5.3 + seed * 0.7)); // the occasional gust
                    lavaGlow = clamp(1.0 + 0.34 * flick, 0.55, 1.7);
                }
                else if (i.skyl.y > 5.5)
                {
                    // Falling-lava flank (mode 6): a hot bright streak racing straight DOWN the face (faster than
                    // the creeping surface crust — molten rock in free fall).
                    float ph = i.wp.y * 2.5 + _Time.y * 5.0;
                    float streak = 0.6 + 0.6 * sin(ph) + 0.5 * smoothstep(0.7, 1.0, sin(ph * 1.7 + (i.wp.x + i.wp.z) * 2.0));
                    lavaGlow = clamp(streak, 0.4, 2.4);
                }
                else if (i.skyl.y > 4.5)
                {
                    float2 lw = i.wp.xz;
                    float lt = _Time.y * 0.25;
                    float slab = sin(lw.x * 0.16 + lt) * sin(lw.y * 0.19 - lt * 0.7);
                    float veins = smoothstep(0.5, 1.0, 0.5 + 0.5 * sin((lw.x + lw.y) * 0.55 + lt * 1.1) + slab * 0.3);
                    lavaGlow = clamp(1.0 + 0.7 * slab + 0.9 * veins, 0.2, 2.2);
                }
                else if (i.mat.a > 0.01 && ((i.skyl.y > 0.5 && i.skyl.y < 1.5) || (i.leaf.x > 2.5 && i.leaf.x < 4.5)))
                {
                    // #2822: a glowing plant or mushroom (flora tint, or a cross plant) breathes slowly. Crystals, ores,
                    // machines and dyed glow blocks are not flora and stay steady.
                    // #2858: and it fades to about a quarter in daylight — full at dusk, at night and out of the sky's
                    // reach (FloraAccent.hlsl). L.y is the sun's height; the haze re-add below reuses lavaGlow, so it fades too.
                    lavaGlow = BbtsGlowBreath(i.wp) * BbtsPlantGlowDaylight(L.y, sky);
                }
                col += albedo * i.mat.a * (3.0 * lavaGlow); // HDR overdrive: push emitters past white so ACES + bloom give a real glow

                // Placed coloured lights (flood-filled per-vertex, TEXCOORD3): illuminate this surface in
                // their colour, regardless of sun/skylight, so lamps light caves + night builds. The baked
                // dominant direction (TEXCOORD4) lets us shade them like the sun — N·L diffuse shaping + a
                // GGX glint + normal-map relief — so lamps sculpt the surface instead of flat-washing
                // it. A fill floor keeps faces the light wrapped around lit; bright enough to feed the bloom.
                // #2820: the cosiness slider lifts the light islands a little at its warm end.
                float3 bl = BbtsBlockLightFlicker(i.bl, i.wp) * BbtsCozyLightGain(); // #2396: warm fixtures breathe, cold ones stay steady
                float blLen = length(i.blDir);
                if (blLen > 0.01)
                {
                    float3 blL = i.blDir / blLen;
                    float blNdl = saturate(dot(N, blL));
                    col += albedo * bl * (0.5 + 0.5 * blNdl) * 2.0;
                    float3 blH = normalize(blL + V);
                    float blNh = saturate(dot(N, blH));
                    float blLh = saturate(dot(blL, blH));
                    float blDterm = blNh * blNh * (r2 - 1.0) + 1.00001;
                    float blSpec = r2 / ((blDterm * blDterm) * max(0.1, blLh * blLh) * (rough * 4.0 + 2.0));
                    col += bl * F0 * (blSpec * blNdl);
                }
                else
                {
                    col += albedo * bl * 2.0; // no direction baked (uniform/none) → flat fallback
                }

                // #3229: the suit headlamps — an LED beam with inverse-square falloff and the surface's own glint.
                col += BbtsHeadlamps(i.wp, N, V, albedo, F0, rough);

                // VFX overhaul (#2152): muzzle flashes, plasma bolts and explosions light the terrain (8 FX lights).
                col += BbtsFxLights(i.wp, N, albedo);

                // Cabin light: a strong emitter's face under cover keeps its tile instead of burning out.
                col = BbtsLookCabinLimit(col, i.mat.a, bl, sky);

                // The shared haze (AtmosphereCommon, #2393/#2406): distance veil + height fog toward the sky colour,
                // warmed toward the sun. Blends in shader space so it only tints distant terrain — it never darkens
                // the frame like a full-screen pass. The mesher skylight keeps caves clear of the height fog.
                if (_Sc_Fog.w > 0.5)
                {
                    float4 hz = BbtsHaze(i.wp, sky, 1.0); // #3271/#3272: amount + colour (mist, cloud band) in one pass
                    float haze = hz.a;
                    col = lerp(col, hz.rgb, haze);

                    // #1748: a beacon must outlast the haze. The emission above went into `col` BEFORE this
                    // lerp, so a warning light on a far tower faded exactly like the rock beside it. Half the
                    // glow comes back in proportion to the haze: rock still dissolves into the sky, a light
                    // still reads as a light at the edge of the view.
                    col += albedo * i.mat.a * (3.0 * lavaGlow) * haze * 0.5;
                }

                // #2153: the scanner's wave rolls over the terrain after the haze, so it reads at any distance.
                col += BbtsScanWave(i.wp);

                col = BbtsAtmoDebug(col, i.wp, gN, sky); // capture diagnostics only (0 in play)
                if (_Sc_AtmoDebug > 4.5 && _Sc_AtmoDebug < 5.5)
                {
                    col = i.bl; // capture diagnostics: the baked block light as the mesher wrote it
                }

                return half4(col, 1);
            }
            ENDHLSL
        }

        // Cast the sun's shadows into the main-light shadow map (URP). Foliage punches holes via alpha-clip.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing // Staged crowns: the near clusters cast the near shadows
            #pragma instancing_options procedural:BbtsCrownInstance
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "AtmosphereCommon.hlsl" // #2397: the shadow follows the swaying leaves

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // Must match the forward pass's UnityPerMaterial layout exactly (SRP Batcher requirement).
            CBUFFER_START(UnityPerMaterial)
                float _LeafCutoff;
            CBUFFER_END

            float3 _LightDirection; // set by URP while rendering the shadow map (a global, not a material property)

            #include "GpuInstances.hlsl" // #4046: on a buffer draw the instance comes from the graphics card
            void BbtsCrownInstance()
            {
                #if defined(BBTS_GPU_INSTANCES)
                    BBTS_GPU_INSTANCE(inst)
                #endif
            }

            struct SAttr { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 sky : TEXCOORD1; float4 leaf : TEXCOORD2; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct SVary { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float cut : TEXCOORD1; float3 wp : TEXCOORD2; }; // cut = the cutout code, wp = the world position

            SVary shadowVert(SAttr v)
            {
                SVary o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float tintMode = fmod(v.sky.y, 16.0);
                wp += BbtsBlockSway(wp, v.leaf.x, tintMode, v.sky.x, frac(v.uv.y * 32.0)); // #2519: same sway as the forward pass
                // … and the same flutter (the near clusters of staged crowns only, as in the forward pass)
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                #else
                    if (_Sc_Look2.y < 2.5)
                    {
                        wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                    }
                #endif
                o.wp = wp;
                float3 wn = TransformObjectToWorldNormal(v.normal);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = v.uv;
                o.cut = v.leaf.x;
                return o;
            }

            #include "PlantFar.hlsl" // A far picture casts the baked outline's shadow

            half4 shadowFrag(SVary i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // Plant far pictures: explicit gradients taken outside the branch (WebGPU allows no implicit
                // derivatives in non-uniform control flow).
                float2 farDx = BbtsPlantFarGrad(ddx(i.uv)), farDy = BbtsPlantFarGrad(ddy(i.uv));
                float2 farUv, farCell;
                if (BbtsPlantFarUv(i.uv, i.cut, farUv, farCell))
                {
                    a = SAMPLE_TEXTURE2D_GRAD(_Sc_LookPlantFar, sampler_MainTex, farUv, farDx, farDy).a;
                }

                if (!(_Sc_Look2.y > 0.5 && i.cut > 0.5 && i.cut < 1.5)) // A closed crown casts a whole shadow
                {
                    clip(a - _LeafCutoff); // opaque tiles (a=1) always pass; foliage holes clip
                }

                // Staged crowns: one of the two pictures casts — the near cluster up to the clusters' shadow
                // range (_Sc_LookEye.w, at most the middle of the cross-fade), the far cube beyond it. No dither
                // here: this pass renders from the sun's view.
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    clip(_Sc_LookEye.w - distance(i.wp, _Sc_LookEye.xyz));
                #else
                    if (_Sc_Look2.y > 2.5 && i.cut > 0.5 && i.cut < 1.5)
                    {
                        clip(distance(i.wp, _Sc_LookEye.xyz) - _Sc_LookEye.w);
                    }

                    // Plant bodies: near the eye the body casts the plant's shadow, beyond it the picture.
                    if (BbtsStagedPlant(i.cut))
                    {
                        clip(distance(i.wp, _Sc_LookEye.xyz) - _Sc_LookPlant.z);
                    }
                #endif

                return 0;
            }
            ENDHLSL
        }

        // #1518: depth prepass. URP renders one whenever it cannot copy the depth buffer — on WebGL (GLES3)
        // with MSAA on, and whenever depth priming is enabled — and it only draws shaders that HAVE this pass.
        // Without it the terrain was missing from _CameraDepthTexture in browsers at Medium/High, so SSAO,
        // the water depth and the heat-haze fade all read the far plane. Same leaf clip as the shadow caster.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing // Staged crowns: the near clusters are in the depth prepass too
            #pragma instancing_options procedural:BbtsCrownInstance
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl" // #2397: the depth prepass follows the swaying leaves too

            #include "GpuInstances.hlsl" // #4046: on a buffer draw the instance comes from the graphics card
            void BbtsCrownInstance()
            {
                #if defined(BBTS_GPU_INSTANCES)
                    BBTS_GPU_INSTANCE(inst)
                #endif
            }

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // Must match the forward pass's UnityPerMaterial layout exactly (SRP Batcher requirement).
            CBUFFER_START(UnityPerMaterial)
                float _LeafCutoff;
            CBUFFER_END

            struct DAttr
            {
                float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 sky : TEXCOORD1; float4 leaf : TEXCOORD2; float4 color : COLOR;
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                float3 cube : TEXCOORD5; // The middle of the small cube this vertex belongs to (cluster-local)
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DVary { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float cut : TEXCOORD1; float3 wp : TEXCOORD2; }; // cut = the cutout code, wp = the world position

            DVary depthVert(DAttr v)
            {
                DVary o;
                UNITY_SETUP_INSTANCE_ID(v);
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    // Staged crowns: the same shrink right at the eye as in the forward pass.
                    float ccScale = BbtsCrownCubeScale(TransformObjectToWorld(v.cube), _WorldSpaceCameraPos);
                    v.positionOS.xyz = lerp(v.cube, v.positionOS.xyz, ccScale);
                #endif
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float tintMode = fmod(v.sky.y, 16.0);
                wp += BbtsBlockSway(wp, v.leaf.x, tintMode, v.sky.x, frac(v.uv.y * 32.0)); // #2519: same sway as the forward pass
                // … and the same flutter (the near clusters of staged crowns only, as in the forward pass)
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                #else
                    if (_Sc_Look2.y < 2.5)
                    {
                        wp += BbtsCrownFlutter(wp, v.leaf.x, v.color.r);
                    }
                #endif
                o.wp = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.cut = v.leaf.x;
                return o;
            }

            #include "PlantFar.hlsl" // The depth prepass must cut the same outline as the forward pass

            half4 depthFrag(DVary i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // Plant far pictures: explicit gradients taken outside the branch (WebGPU allows no implicit
                // derivatives in non-uniform control flow).
                float2 farDx = BbtsPlantFarGrad(ddx(i.uv)), farDy = BbtsPlantFarGrad(ddy(i.uv));
                float2 farUv, farCell;
                if (BbtsPlantFarUv(i.uv, i.cut, farUv, farCell))
                {
                    a = SAMPLE_TEXTURE2D_GRAD(_Sc_LookPlantFar, sampler_MainTex, farUv, farDx, farDy).a;
                }

                if (!(_Sc_Look2.y > 0.5 && i.cut > 0.5 && i.cut < 1.5)) // A closed crown has no holes in the depth either
                {
                    clip(a - _LeafCutoff); // foliage holes stay holes in the depth buffer too
                }

                // Staged crowns: the same screen-door cross-fade as the forward pass, pixel for pixel.
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    clip(BbtsCrownNearShare(i.wp, _WorldSpaceCameraPos) - BbtsIgn(i.positionCS.xy) - 0.0001);
                #else
                    if (_Sc_Look2.y > 2.5 && i.cut > 0.5 && i.cut < 1.5)
                    {
                        clip(BbtsIgn(i.positionCS.xy) - BbtsCrownNearShare(i.wp, _WorldSpaceCameraPos));
                    }

                    // Plant bodies: the same cross-fade as the forward pass, pixel for pixel.
                    if (BbtsStagedPlant(i.cut))
                    {
                        clip(BbtsIgn(i.positionCS.xy) - BbtsPlantNearShare(i.wp, _WorldSpaceCameraPos));
                    }
                #endif

                return 0;
            }
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
            AlphaToMask On // smooth foliage cutout edges against MSAA (opaque a=1 unaffected) — Built-in RP path
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "FloraAccent.hlsl" // #2834: the flora re-tint with the accent colours of blossoms and berries
            #include "Headlamp.hlsl"    // #3229: the suit headlamps (unshadowed here — no SRP texture macros)

            sampler2D _MainTex;
            sampler2D _NormalTex; // tangent-space normal map (Sobel-derived from the atlas)
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun
            fixed4 _Sc_Sky;     // sky colour for environment reflections
            float _Sc_Indoor;     // ship-interior fill (0..1): lights skylight-occluded cabin faces
            fixed4 _Sc_IndoorColor; // #2814: the fill's colour (a > 0.5 = set)
            float _Sc_DarkFloor;  // #3265: the flat dark floor (0 = unset → 0.045), see AtmosphereCommon's BbtsDarkFloor
            fixed4 _Sc_FloraTint; // planet flora base hue (rgb); flora faces are desaturated + re-tinted to it
            float _LeafCutoff;    // alpha-test threshold for cutout foliage (leaf tiles carry a baked alpha mask)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1;  // x = skylight (1 sees sky, 0 underground/indoors); y = tint mode (1 flora, 2 hull paint, 3 player dye)
                float4 leaf : TEXCOORD2; // x = cutout code (1 crown, 2 torch/lantern, 3 cross plant, 4 aquatic plant → alpha-cutout); yzw = flora/hull/dye tint
                float3 bl : TEXCOORD3;   // propagated coloured block-light (placed lights illuminate)
                float3 blDir : TEXCOORD4; // dominant block-light direction (toward source); 0 = none
                fixed4 color : COLOR;   // r=gloss, g=metal, b=face AO
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD4; // world tangent xyz + bitangent handedness w
                float2 skyl : TEXCOORD5; // x = skylight, y = tint mode
                float4 leaf : TEXCOORD6; // x = foliage flag, yzw = per-species/hull/dye tint
                float3 bl : TEXCOORD7; // propagated coloured block-light
                float3 blDir : TEXCOORD8; // dominant block-light direction
                fixed4 mat : COLOR;
                UNITY_FOG_COORDS(3)
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
                o.pos = UnityObjectToClipPos(v.vertex);
                float tintMode;
                o.uv = BbtsAnimatedUv(v.uv, v.sky.y, tintMode);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wt = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.skyl = float2(v.sky.x, tintMode);
                o.leaf = v.leaf;
                o.bl = v.bl;
                o.blDir = v.blDir;
                o.mat = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texel = tex2D(_MainTex, i.uv);
                // Cutout foliage: the leaf tile's baked alpha mask punches the gaps between leaves, so tree
                // crowns + leafy plants are see-through instead of solid cubes. Only leaf-flagged faces clip
                // (every other tile is fully opaque and unaffected).
                if (i.leaf.x > 0.5)
                {
                    clip(texel.a - _LeafCutoff);
                }

                fixed3 albedo = texel.rgb;

                if (i.skyl.y > 4.5)
                {
                    // Molten lava surface (mode 5): untinted; the animated crust modulates the glow below.
                }
                // Bark (mode 4): mirrors the URP pass (#428) — without this branch a wood_log face fell
                // through to the player-dye recolour. Per-world dark bark hue in TEXCOORD2.yzw; black = natural.
                else if (i.skyl.y > 3.5)
                {
                    if (dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01)
                    {
                        float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                        albedo = lerp(albedo, lum * i.leaf.yzw * 1.4, 0.72);
                    }
                }
                // #4012: a dyed flower (pick 4) — the dye on the blossom only, stem and leaves in the tile's colours.
                else if (i.skyl.y > 2.5 && BbtsAccentPick(i.leaf.x) > 3.5)
                {
                    albedo = BbtsDyedBlossom(albedo, i.leaf.yzw);
                }
                // Player dye (mode 3): a luminance-based recolour applied everywhere (independent of the
                // flora-tint global), so dyed building blocks read vividly on any world and in caves.
                else if (i.skyl.y > 2.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * i.leaf.yzw * 1.6, 0.85);
                }
                // Hull paint (item 32): multiply the per-ship tint (TEXCOORD2.yzw) into the albedo — the
                // same look as the old tinted-silhouette hull (_Color * tex), independent of flora globals.
                else if (i.skyl.y > 1.5)
                {
                    albedo *= i.leaf.yzw;
                }
                // Flora re-tint: where the flora flag is set, drop the tile to its luminance and re-colour
                // it. The mesh carries a per-SPECIES x per-world tint (TEXCOORD2.yzw); meshes built without
                // a tint resolver carry black there and fall back to the planet's uniform global hue.
                else if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    float3 tint = dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01 ? i.leaf.yzw : _Sc_FloraTint.rgb;
                    albedo = BbtsFloraTint(albedo, tint, i.leaf.x); // #2834: blossoms and berries in their own colours
                }

                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                // Per-pixel normal from the Sobel-derived map, lifted into world space (tangent basis),
                // so flat faces show micro-relief under the sun + lamp.
                float3 gN = normalize(i.wn);
                float3 T = normalize(i.wt.xyz);
                float3 B = normalize(cross(gN, T) * i.wt.w);
                float4 nrm = tex2D(_NormalTex, i.uv);
                float3 tn = nrm.xyz * 2.0 - 1.0;
                float3 N = normalize(tn.x * T + tn.y * B + tn.z * gN);

                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = saturate(dot(N, L));

                // Skylight: 1 in the open, ~0 underground / indoors. The sun, sky-ambient, specular and
                // environment reflection are all gated by it, so caves + interiors are dark and rely on
                // the headlamp + emissive light blocks (added below, unaffected by skylight).
                float sky = saturate(i.skyl.x);

                // Diffuse: a tiny cave-ambient floor + (sky-ambient + directional) scaled by skylight,
                // coloured by the system sun. A subtle per-face AO keeps cube edges readable.
                // Per-vertex AO (mesher, in .b) widened to a visible contact-shadow range, then multiplied by
                // the texture-scale cavity AO (normal map alpha). Diffuse-only — spec/reflection stay crisp.
                float faceAo = lerp(0.62, 1.0, i.mat.b) * lerp(1.0, nrm.a, 0.6);
                // Ambient fill: a brighter floor so shadowed faces / overcast aren't crushed to black
                // (outdoors ~0.70, a readable cave floor of 0.24). The directional adds the sunny side on top.
                // A small flat term (sun/sky-independent) guarantees a minimum readable level, so blocks in a
                // dark hole or deep cave are dim but never pure black.
                // #1457 (0.70 → 0.78); #1611 (0.26 → 0.32); #3265: BbtsSkyAmbient of AtmosphereCommon, repeated here (CG cannot
                // include it) — unchanged from the shade floor 0.55 up, 0.02 in a sealed cave — plus the flat dark floor global.
                float amb = lerp(0.32, 0.78, sky) - 0.30 * saturate(1.0 - sky / 0.55);
                fixed3 col = albedo * (light * (amb + 0.5 * ndl * sky) + (_Sc_DarkFloor > 0.0 ? _Sc_DarkFloor : 0.045)) * faceAo;

                // Ship interior fill: a neutral, day/night-independent fill on skylight-occluded faces only
                // (so the cabin is lit but the sunlit outdoors seen through windows is untouched).
                col += albedo * (_Sc_IndoorColor.a > 0.5 ? _Sc_IndoorColor.rgb : fixed3(1, 1, 1)) * (_Sc_Indoor * 0.5 * (1.0 - sky)) * faceAo;

                // Night/atmosphere ambient floor (mirrors the URP path): a faint cool fill on open-sky faces,
                // strongest when the sun light is weak (night/storm) and fading out toward day, so worlds read as
                // dim atmospheric blue at night instead of crushing to pure black. Gated by skylight (caves stay dark).
                float nightFloor = saturate(0.6 - dot(light, fixed3(0.299, 0.587, 0.114)));
                col += albedo * fixed3(0.10, 0.13, 0.20) * (sky * nightFloor) * faceAo;

                float gloss = i.mat.r;            // perceptual smoothness
                float metal = i.mat.g;            // metallic
                float rough = clamp(1.0 - gloss, 0.045, 1.0);

                // Specular sun highlight: Unity's stable GGX form, tinted by the metallic F0 (0.04 dielectric →
                // albedo for metals). Tight, bright glints on smooth/metal faces; gated by ndl + skylight.
                float3 H = normalize(L + V);
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float dterm = nh * nh * (r2 - 1.0) + 1.00001;
                float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
                fixed3 F0 = lerp(fixed3(0.04, 0.04, 0.04), albedo, metal);
                col += light * F0 * (specTerm * ndl * sky);

                // Roughness-aware environment reflection of the sky colour: metals reflect strongly (tinted by
                // F0) even head-on, dielectrics mostly at grazing angles (Fresnel). Additive, faded by skylight.
                fixed3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, 5.0);
                float gr = 1.0 - rough;
                fixed3 Fr = F0 + (max(float3(gr, gr, gr), F0) - F0) * fres;
                col += envCol * Fr * (saturate(gloss) * sky * 0.5);

                // Emissive blocks glow independently of sun + fog (lights/lava/crystals/ores), so they
                // shine at night and the bloom pass picks them up. Alpha carries the emission strength.
                // Lava surface (mode 5) flows: a slow procedural crust redistributes the glow — dark cooled
                // slabs drift over bright cracks (viscous, ~1/3 water speed; average brightness ≈ unchanged so
                // bloom stays balanced). Procedural on world XZ + time because atlas UVs can't scroll.
                float lavaGlow = 1.0;
                if (i.skyl.y > 6.5)
                {
                    // TORCH FLAME (mode 7): an open flame never burns steady. Two detuned sine bands plus a
                    // sharper occasional gust give an irregular flicker, and the phase is offset by the torch's
                    // own world position so a row of them along a wall flickers out of step instead of pulsing
                    // in unison. Averages slightly above 1.0 so a torch-lit room does not read dimmer than one
                    // lit by a steady lamp.
                    float seed = i.wp.x * 12.9898 + i.wp.z * 78.233 + i.wp.y * 37.719;
                    float ft = _Time.y;
                    float flick = 0.62 * sin(ft * 11.0 + seed)
                                + 0.28 * sin(ft * 19.7 + seed * 1.7)
                                + 0.34 * smoothstep(0.72, 1.0, sin(ft * 5.3 + seed * 0.7)); // the occasional gust
                    lavaGlow = clamp(1.0 + 0.34 * flick, 0.55, 1.7);
                }
                else if (i.skyl.y > 5.5)
                {
                    // Falling-lava flank (mode 6): a hot bright streak racing straight DOWN the face (faster than
                    // the creeping surface crust — molten rock in free fall).
                    float ph = i.wp.y * 2.5 + _Time.y * 5.0;
                    float streak = 0.6 + 0.6 * sin(ph) + 0.5 * smoothstep(0.7, 1.0, sin(ph * 1.7 + (i.wp.x + i.wp.z) * 2.0));
                    lavaGlow = clamp(streak, 0.4, 2.4);
                }
                else if (i.skyl.y > 4.5)
                {
                    float2 lw = i.wp.xz;
                    float lt = _Time.y * 0.25;
                    float slab = sin(lw.x * 0.16 + lt) * sin(lw.y * 0.19 - lt * 0.7);
                    float veins = smoothstep(0.5, 1.0, 0.5 + 0.5 * sin((lw.x + lw.y) * 0.55 + lt * 1.1) + slab * 0.3);
                    lavaGlow = clamp(1.0 + 0.7 * slab + 0.9 * veins, 0.2, 2.2);
                }
                else if (i.mat.a > 0.01 && ((i.skyl.y > 0.5 && i.skyl.y < 1.5) || (i.leaf.x > 2.5 && i.leaf.x < 4.5)))
                {
                    // #2858 (mirrors the URP pass, without its #2822 breathing): a glowing plant fades to about a quarter
                    // in daylight — full at dusk, at night and out of the sky's reach. Lamps, lava, ores, crystals stay.
                    lavaGlow = BbtsPlantGlowDaylight(L.y, sky);
                }
                col += albedo * i.mat.a * (3.0 * lavaGlow); // HDR overdrive: push emitters past white so ACES + bloom give a real glow

                // Placed coloured lights (flood-filled per-vertex, TEXCOORD3): illuminate this surface in
                // their colour, regardless of sun/skylight, so lamps light caves + night builds. The baked
                // dominant direction (TEXCOORD4) shades them like the sun — N·L diffuse + a GGX glint
                // + normal-map relief — so lamps sculpt the surface; a fill floor keeps wrapped-around faces lit.
                float blLen = length(i.blDir);
                if (blLen > 0.01)
                {
                    float3 blL = i.blDir / blLen;
                    float blNdl = saturate(dot(N, blL));
                    col += albedo * i.bl * (0.5 + 0.5 * blNdl) * 2.0;
                    float3 blH = normalize(blL + V);
                    float blNh = saturate(dot(N, blH));
                    float blLh = saturate(dot(blL, blH));
                    float blDterm = blNh * blNh * (r2 - 1.0) + 1.00001;
                    float blSpec = r2 / ((blDterm * blDterm) * max(0.1, blLh * blLh) * (rough * 4.0 + 2.0));
                    col += i.bl * F0 * (blSpec * blNdl);
                }
                else
                {
                    col += albedo * i.bl * 2.0; // no direction baked (uniform/none) → flat fallback
                }

                // Headlamps — custom spotlights (this shader bypasses Unity's light passes, so the lamps are fed in
                // as globals by the player instead of real Lights; Headlamp.hlsl, #3229).
                col += BbtsHeadlamps(i.wp, N, V, albedo, F0, rough);

                fixed4 outc = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outc); // fade into the sky-coloured distance fog
                return outc;
            }
            ENDCG
        }
    }

    Fallback "BlocksBeyondTheStars/VertexColorOpaque"
}
