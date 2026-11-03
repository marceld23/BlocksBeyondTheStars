# Advanced graphics — how it works + roadmap

Status: implemented (foundation + several phases) — see TODO.md for live Done/Open status. Date: 2026-06-19, updated 2026-07-04 (organic look pass, PR #192).

This doc covers the renderer's advanced-look features: what has shipped, how it works, and the research
roadmap for the polish milestones still ahead. The historical "Built-in RP, no URP" framing of the
original plan is obsolete — **URP is the shipping pipeline** (see `URP_MIGRATION.md`), the project
renders in **linear colour space**, and the hand-written shaders are all dual-pipeline.

## What has shipped

- **URP migration** (2026-06-10): real soft sun shadows (terrain + models + creatures + enemies, cast +
  receive), a URP Volume post stack (ACES / bloom / vignette / grade — built in `UrpScenePost`, rendering
  on the gameplay camera since PR #50 enabled `renderPostProcessing` there), URP SSAO, the diegetic visor
  as a render-graph blit pass, per-system sun tint, and per-preset shadow cost. Details in
  `URP_MIGRATION.md`.
- **Post stack + emissive** (Phase 1): bloom + ACES tonemap + vignette + SSAO (preset-gated) plus
  emissive blocks (ores / crystals / lava / lights / glowing flora glow via the atlas vertex-alpha + a
  `BlockAtlas` emission term), a suit headlamp and ship lights. Per-system / biome colour grading is
  driven from `Sky.SetGrade` → `UrpScenePost.ApplyGrade`.
- **Normal mapping** (Phase 1.5, blocks): a normal atlas derived **procedurally** (Sobel on the colour
  atlas luminance, no bundled assets) + per-face tangents in the chunk mesher + tangent-space lighting in
  `BlockAtlas` (sun, specular, Fresnel and headlamp all use the per-pixel normal).
- **Depth + opaque textures** on (the gate for fog / water-depth / SSR-class effects), per-preset.
- **Cinematic post polish:** screen-space lens flare (Medium+), a teal-orange `ShadowsMidtonesHighlights`
  baseline grade, flight-only motion blur (High+), and AlphaToMask MSAA edge AA on grass/leaves.
- **Depth-aware water + refraction** (Phase 3, URP path): `BlockAtlasTransparent.shader` reads scene
  depth for a shallow→deep colour gradient + shoreline/object foam, and composites the bed from the
  opaque texture at a wave-distorted screen UV so the surface refracts what's beneath.
- **Linear colour space** (2026-06-12, see `PROFESSIONAL_LOOK_IMPLEMENTATION.md`).
- **Organic look pass** (2026-07-01, PR #192): per-vertex ambient occlusion + texture-scale cavity AO +
  beveled convex edges (chunk mesher + `BlockAtlas`); **GPU-instanced ground-detail scatter** (grass tufts on
  open ground, distance-culled, preset-gated; the pebbles were dropped in #3120 — see the ground-detail rules below);
  **3D mesh flora** (leafy flora as leaning 3-plane
  rosettes, solid flora like cactus/crystal/mushroom as real shapes); sparse **ship-hull greeble** plating on
  exposed hull faces. All client presentation only — server data unchanged.
- **Atmosphere package** (2026-10, #2408, Unity 6.6 + WebGPU first — ADR 0015): one shared haze for every
  world-space shader (`AtmosphereCommon.hlsl`: distance veil + **height fog** that lies in the low ground, warmed
  toward the sun; creatures, ships, particles and clouds no longer cut out of the fog); **cloud shadows** on the
  direct-sun term (wind-scrolled baked noise); **eye adaptation** driving the post exposure from a chunk-data light
  probe (`AtmosphereProbe`); **breathing torch light**; **wind sway** of leaves, grass and flora (no mesher change —
  the foliage flag and the tint mode decide, the shadow and depth passes follow); **wet ground and snow caps** from
  the weather (`_Sc_Surface`); weather drops as one **particle system** driven by `SetParticles` with ground
  splashes; **shooting stars** and the **planet limb** (`PlanetLimb.shader`) from orbit; **underwater** haze,
  caustics and bubbles; **light-shaft cards** at cave mouths and canopy gaps; **lava, crystals and glowing flora
  light their surroundings** quietly (ADR 0016); fake **SSS** for foliage and **soft particles**. Everything is
  preset-gated and has its own switch under *Atmosphere effects* (#2404); nothing flashes faster than 3/s.
- **Water after the atmosphere package** (2026-10-09, #2410): the screen-space water hazes its *own* colour before it
  composites the refracted bed — the bed and every SSR hit arrive hazed already, so the old final pass veiled the
  shallows twice (#2411); the height fog counts half on a water surface, because the probe's mist floor *is* the water
  (`BbtsHazeAmountScaled`, #2412); the sky sheen caps at 30 % of the sky at a grazing angle instead of 16 % (#2413);
  `_Sc_SeaLevel` (from the far-terrain world info) keeps wet ground and snow caps off the seabed (#2414); the weather
  surface measures its exposure past the mesher's shade floor (`BBTS_SHADE_FLOOR` = `ChunkMesher.ShadeFloor` 0.55), so
  a roofed floor gets no snow cap and no wet gloss (#3164 — it got 55 % of both); the far-terrain
  sea takes the world's water tint and the rainbow bands from the same globals as the near water — vertex alpha 0.5
  marks it (#2415); the IMGUI underwater wash thins to ~15 % while the shader-side underwater haze is active (#2416).
- **Wind sway by cutout code** (2026-10-13, #2519/#2520): `TEXCOORD2.x` of the opaque chunk mesh is a *cutout code*,
  not a flag — every code above 0.5 is alpha-clipped, and `BbtsBlockSway` (`AtmosphereCommon.hlsl`, called by the
  forward, shadow and depth passes alike) reads how the vertex moves: **1** a tree-crown shell face sways as one rigid
  piece (the offset depends on the world position only, so a crown's leaf cubes never tear apart — they used to bend
  per cube from the tile's `v`, which opened slits between stacked crown cubes); **2** a rigid cutout prop — the torch
  and lantern (lit or the Crystal Net's unlit twins; since #2683 also their wall and ceiling forms, drawn from
  `LightMountGeometry` cards with brackets and chains) — never sways and skips the leaf back-light; **3** a cross-billboard
  plant bends from its root; **4** an aquatic plant — a cross plant the water surrounds (the mesher's `WetProp` rule,
  the one that draws its cell's water) — bends like 3. Plants and solid flora are scaled by
  `BbtsSwayShelter(skylight)` = `smoothstep(0.55, 0.9)`: the mesher's shade floor gives every cell up to six blocks
  under cover 0.55, so plants in caves, indoors and under a thick canopy stand still while open ground keeps the
  calm-day breathing. Each plant carries ONE skylight value on all its vertices — solid flora too, because it is always
  meshed as one shaped form (`AddShapedBlock`, one skylight per block), never through the cube-face path; keep it that
  way, or its faces would drift apart (an EditMode test pins it; leaving solid flora unscaled instead would set cave
  caps and spore pods swaying underground). Crown faces carry a skylight per face (the air each face looks into), so
  crowns are deliberately not scaled — scaling them would move a leaf cube's top but not its underside. Aquatic plants are not scaled either: the skylight counts the water above as cover, and under
  water the sway reads as current (Marcel: caves and indoors stand still, kelp and seagrass keep swaying — a plant in a
  flooded cave sways too). The ChunkMesher constants `LeafCodeCrown/Rigid/Plant/Aquatic` are the source of the codes
  (pinned by `ChunkMesherCutoutCodeEditModeTests`). The torch
  and lantern tiles got their alpha mask the same day (`tools/ai-assets/bake_prop_alpha.py`; `BlockTileAlphaTests`
  holds every billboard tile to a real share of transparent pixels).
- **Cutout cubes** (2026-11-01, #3085): the energy fence is a SOLID cube whose tile is the side of a fence segment —
  rails along the top and bottom edge, cyan beams across, see-through gaps between them (cut per row by
  `tools/ai-assets/bake_fence_alpha.py`, so the gaps line up with the neighbour's). It left the alpha-blended submesh
  (it was drawn like a force field, the whole block a 48 % curtain) for the opaque one with cutout code **2** on all six
  faces: the gaps are clipped, so there is no sorting, the rails stay solid metal and the shadow has the gaps too. The
  mesher trait `TraitCutoutCube` (`ChunkMesher.IsCutoutCubeKey`) makes it behave like a crown shell everywhere else: it
  never seals a neighbour's face (the ground under it stays drawn), lets placed light through, casts no AO, gets no bevel
  or parallax, and culls only against its own kind, so a row of fences is one plane of rails and beams per side. It keeps
  its collider. Top and bottom wear `face_energy_fence_top` (the emitter rail); a drop packet clips the gaps too
  (`ChunkMesher.CubeCutoutCode`). The old pylon picture lives on as the block's own inventory icon
  (`Resources/icons/block_<key>.png`, `BlockCubeIcon.OwnIcon`), which wins over the tile and the cube icon everywhere.
- **Cozy package** (2026-10, epic #2802; rules in [ART_BIBLE.md](ART_BIBLE.md) §6a):
  - *Fixes first:* the world's ColorAdjustments contrast sits at +3 over the biome contrast (#2803; a leftover +6 had
    kept it at 9–20, so #1457's softening only reached the menu). Placed-light colours go sRGB → linear at the
    per-vertex bake: `ChunkMesher.DisplayLight` splits a lit cell into hue and level, linearises the hue through a
    256-entry table and scales it back to the luminance the shader got before (≤ 1.5×), so the flood's per-channel
    reach — what the lamps were tuned with — is untouched and a torch lights amber instead of pale yellow (#2804).
  - *Home grade:* `GameBootstrap.AtHome` (aboard and not on a spacewalk, or under a roof inside your own base's air)
    drives `Sky._homeBlend` (~1.5 s), which `UrpScenePost.SetCozyContext` folds into the one global volume: the
    biome tint/saturation/contrast fade out, `WhiteBalance` warms, `LiftGammaGain` lifts the shadows, the
    `ShadowsMidtonesHighlights` cool lean turns warm, the mood LUT drops to a quarter (#2813). The cabin fill gets a
    colour, `_Sc_IndoorColor` — warm white by day, amber in the evening (`Sky.CabinEvening`: the planet's sun when
    landed, the real clock in the space ship-interior world), and the evening fill is a fifth brighter so the cabin
    does not sink with the outdoor night (#2814/#2815). The transparent shader keeps its neutral light: it has no
    skylight channel, so a cabin fill there would also warm the lake outside the window.
  - *Cosiness slider* (`ClientSettings.Cosiness`, default 0.5, #2820): multiplied by `Sky.CozyPlaceTarget` (home 1,
    lit camp 0.8, gentle worlds 0.6, harsh 0.3–0.4, storms half, a hostile within 24 m or Valuma's unease 0); softens
    contrast and lifts shadows from 0 %, warms from 15 %, and from 50 % lowers the bloom threshold a little and lifts the
    block light (`_Sc_CozyLight`, ≤ 1.3). Exposure-compensated — never darker. All of it is grading baked into URP's
    LUT, so it costs nothing on WebGL/Potato.
  - *Motion:* glowing flora breathes (`BbtsGlowBreath`, ~6 s, a smooth spatial phase so a forest breathes as a slow
    wave; off with Reduce flashes, #2822); sheltered plants get a ~1 cm indoor draught instead of standing frozen
    (`BbtsDraught`, #2819).
  - *Rest in bed* (#2823): `UrpScenePost.SetRest` warms, dims (−0.6 EV) and closes the vignette in a little.
  - *Menu blur* fades over ~0.25 s by walking the gaussian start/end in from far away (the radius cannot reach 0, #2810).
- **High details** (2026-11, epic #3066) — the High preset is **desktop-class**: these extras run only on High on a
  desktop client (`HighDetails.Active` = High and not WebGL/mobile, `ClientSettings.FarViewLightDevice`), each behind its
  own switch in *Settings → Graphics → Details (High)* (`ClientSettings.HighDetails.cs`, `UiSettings.HighDetails.cs`,
  #3061). The browser keeps the plain High look. Every technique is still one WebGL 2 allows (fragment shaders,
  instancing, camera renders, meshes — ADR 0015 §3 unchanged); High simply may cost more than a browser tab can afford.
  No new shader keywords: each branch is a uniform branch on a global (`_Sc_Relief`, `_Sc_RoomProbe.w`, …). The capture
  tool's `-highDetails Off|All` (with `-preset High`) gives the before/after on one build;
  `scripts/clip-manifests/high-details-check.json` holds the scenes.
  - *Shadows* (#3054, no switch): 4 cascades (the asset's 4-split ≈ 10/30/70/150 m) out to 150 m, and the sun's
    soft-shadow quality High (`Sky.HighDetails.cs`; the shaders' plain `_SHADOWS_SOFT` branches on it at runtime).
    Every other preset keeps 2 cascades and its distance (`ClientSettings.ApplyShadowTier`).
  - *Parallax occlusion mapping* (#3055): `BlockTextureAtlas.Height.cs` builds an R8 height atlas next to the normal
    atlas (desktop only; per-tile luminance, 3×3 blur, normalised so the brightest texel is the face), bound as
    `_HeightTex` through `BindNormals`. The per-face relief — `BlockDefinition.Relief` or `BlockRelief.Default`
    (Client.Core: brick 1.0, rock/ore 0.8, metal 0.5, everything else flat) — rides in the packed `Normal.w` at
    114/127 (`ChunkMeshData.ReliefScale`; w > 0.95 is what a mesh with 3-component normals reads, kept flat).
    `BbtsReliefUv` marches 12 fixed layers + one secant step in tangent space (≈ 4 cm × relief deep), clamps inside the
    atlas cell and fades out from 12 to 24 m; explicit-gradient samples only (WebGPU). Cube faces only — shaped
    blocks, painted, cutout, flora and see-through faces stay flat. The mesher's tangent now follows the 90° UV
    rotation of natural top faces (the parallax shift and the normal-map light point the way the texture does), and
    the normal atlas wraps its neighbour lookups inside each tile (#3065 — it used to take every tile's border relief
    from the neighbouring texture).
  - *Grass carpet and flowers* (#3056): the mesher records every open-topped plain-cube `grass`/`alien_grass`/`mycelium`
    top (`ChunkMeshData.GrassTops` + `GrassLight`, on every preset, so switching needs no remesh); `GrassShells`
    draws an instanced stack of 10 shells (0–0.22 m) per top within 16 m — strands cut from a hash (14 per block edge),
    colour from the very atlas texel the strand grows out of (variant tile and turn mirrored by `MeadowField`, so texture
    packs work), the block shader's lighting for an upward face (sun + shadow map, skylight, placed block light,
    headlamp, haze, wind, wet/snow — snow presses the carpet down), flattening and thinning over the last 8 m.
    *Against the crawl* (#3078 — the first playtest called the carpet "wuselig"): no strand gets thinner than about a
    pixel (the minimum radius comes from `fwidth` of the cell UV — the cut itself jumps at each strand cell's border);
    with MSAA (High: 4×) the forward pass turns a one-pixel ramp across the strand edge into alpha-to-coverage, and
    without MSAA it keeps the hard cut (URP raises `_AlphaToMaskAvailable` only for MSAA targets; alpha-to-coverage on
    a single-sample target differs between GPUs); the depth pass always cuts at the coverage's 50 % line; and the shells
    sway only with the plants' slow wave at 0.15 of their reach (`BbtsShellSway` — the 2.9 rad/s flutter term made a
    22 cm carpet jitter). Flowers:
    one plain-grass top in nine (a hash of the canonical cell), out to 28 m, in the world's floral palette
    (`FloraTints.For` of four floral species), denser in spring, few in autumn, none in winter, growing in and sinking
    out instead of popping; the sprite is painted at runtime (`FlowerSprite`). Never shadow casters. #3063 alongside:
    the T0 tufts (`ScatterLit`, Medium+) now receive the sun shadow and have a DepthOnly pass. **Instancing variants:**
    every instanced shader here (`ScatterLit`, `GrassShell`, `MeadowFlower`) is drawn with a material made at runtime,
    so with *Instancing Variants: Strip Unused* the build dropped their `INSTANCING_ON` variants and
    `Graphics.DrawMeshInstanced` drew nothing — the T0 tufts and pebbles had been invisible in player builds, and the
    first carpet capture showed 1661 instances and no blade. `GraphicsSettings.asset` keeps them now
    (`m_InstancingStripping: 2`, pinned by `RuntimeShaderInclusionTests`).
  - *T0 ground-detail rules* (#3118–#3121, after a tester's six reports on v2026.11.2, the first build that drew the
    layer): `GroundScatterRules` (Client.Core) — tufts only on `grass`/`dirt`/`mud` (the pebbles on sand, snow, stone
    and basalt read as untextured boxes and are gone), only where the cell above sees the sky straight up (mesher
    skylight exactly 1; the 0.55 shade floor reached six blocks under any roof, so shallow tunnels grew them), and never
    on a **built cell**. Built = a non-air cell with a stored delta (player build, settlement stamp, weather snow):
    `ChunkData` keeps a sparse edited set, `ServerWorld` marks it from `LoadChunkEdits` and on every persisted
    `SetBlock`, the chunk stream carries it as `ChunkDataMessage.EditRuns` ((start, length) runs over the local indices,
    additive), `ClientWorld` restores it and marks every live `BlockChanged`. Each tuft carries its cell's skylight and
    placed block light (`ChunkMeshData.ScatterLight` → per-instance `_ScatterLight`) and `ScatterLit` shades it with the
    meadow's `BbtsMeadowShade` (night floor, torches, headlamp), so it no longer goes black beside a lit block at night.
    The tuft mesh is five tapered blades. *Settings → Graphics → Ground details* (`ClientSettings.GroundDetails`) drives
    `GroundScatter.Enabled`.
  - *Room reflections* (#3057): `RoomBounds` (Client.Core) flood-fills the open cells from the camera (≤ 4096 cells,
    ≤ 40 per axis, a parked ship's hull counts as wall) — a closed cabin or base room yields its box, an open door
    yields nothing. `RoomReflections` renders the room with a realtime `ReflectionProbe` (128² HDR, a face per frame,
    into the back of two cubes, so a face never samples the texture it is drawn into) on entering, after block edits
    and every 4 s, and publishes `_Sc_RoomCube/_Sc_RoomProbe/_Sc_RoomMin/_Sc_RoomMax`. `BlockAtlas` box-projects it
    (roughness → mip) and blends it in as the environment colour inside the room without the skylight gate. #3064:
    `steel_wall`, `steel_floor`, `metal_panel`, `iron_block` and the module panels got gloss/metal in `blocks.json`.
  - *Decals* (#3058): voxel-face scorch marks and craters — see [VFX.md](VFX.md) § Decals.
  - *Far view "Maximum"* (#3059): a 2048-block step (`FarViewRange.Effective` caps it at 1024 below desktop High, the
    stored choice stays); the server serves far build tiles out to 2368 m and tells the client its range in
    `FarTerrainWorldInfo.MaxTileRangeBlocks` (an older server reads 0 = the legacy 1344).
  - *Volumetric sun light* (#3060): a fragment raymarch, no compute — `VolumetricSunLight` enqueues
    `VolumetricLightPass` (Render Graph, after transparents, so water and glass do not wipe a beam) on the world
    cameras. At half resolution 24 fixed steps march to min(scene depth, shadow distance), sampling the raw sun shadow
    map × cloud shade; the density is the haze's own (`_Sc_Fog` base + `BbtsMistDensity` height fog) plus a little dust
    aboard, at home and under a roof or canopy (`VolumetricLightPolicy`); Henyey-Greenstein phase toward the sun; a
    depth-aware blur and an **additive** upsample (`Blend One One`) — it can only add light, never darken (the June
    attempt's colour round-trip darkened the frame). Off at night, in space or on airless worlds, under water, with a menu
    open; the light-shaft cards stand down while it runs. Only the sun casts beams — torches, lanterns and lava are the
    baked flood-fill light (ADR 0003).

## Design rationale worth keeping

- **Why URP.** TAA, SSR, a maintained post stack, Forward+ many-lights and Shader Graph are first-class
  in URP and awkward in Built-in RP; URP is where Unity's investment goes. The cost (porting the hand
  shaders, re-verifying the always-included list, re-testing presets) was paid; the maps and shader math
  port directly so nothing was wasted.
- **Stay stylized, not photoreal.** Audience is a father-son game — readable and "schick" over photoreal.
  Every effect is **preset-gated** (Potato/Low/Medium/High via `ClientSettings`) and must stay fine on a
  mid-range PC.
- **Procedural-first asset rule.** Derive normal/height/variant maps from the existing colour tiles
  (e.g. Sobel height→normal) before reaching for AI-gen or hand-authored maps. The voxel world's simple
  silhouettes mean surface-detail fakes (normal/parallax/shells) buy a lot of look for little cost.
- **No real URP lights for placed lights.** Real lights would leak through walls and cap the count;
  coloured block light stays a baked voxel flood-fill (TEXCOORD3) with directional shading on top. It is baked
  **per corner**, not per face: each vertex averages the open cells meeting at that corner (fluid tops since #1701,
  opaque faces since #2881), so neighbouring faces agree along their shared edge and a lit floor or wall reads as one
  surface instead of a grid of block-sized tiles.
  Because a lamp lights up to eight cells into the neighbouring chunks, a block edit re-meshes every chunk its light can
  reach (#2951): a lamp placed, mined or recoloured, or a cell turned from light-stopping to open (or back) inside a
  lamp's reach, dirties the chunks around it by `ClientWorld.LightReachThrough` (the steps the light can still travel
  from that cell) via `LightReach.ChunksTouched`; a stored chunk with lamps also refreshes its edge and corner
  neighbours. Before, only the edited chunk rebuilt and a pool of light stopped dead at the next chunk border.

## Roadmap (research, not yet implemented)

Ordered by impact ÷ effort. These are the next polish milestones; each is preset-gated.

- **Shell texturing beyond grass:** the grass carpet shipped with the High details (#3056); fur on furred creatures
  (we already generate `creature_fur.bytes`) and short turf on mossy rocks / flora are still open.
- **Parallax on shaped blocks:** POM shipped for cube faces (#3055); ramps, domes and cones need UVs that follow
  their tangents first.
- **Sky / atmosphere:** denser per-system nebula skybox, Rayleigh-ish atmospheric scattering (horizon glow +
  sunrise/sunset tints). (Height fog, sun-warmed haze and light-shaft cards shipped with the atmosphere package; the
  volumetric sun light shipped with the High details as a fragment raymarch, #3060 — beams from torches and lava
  would need the FX lights as point sources in the march.)
- **Translucency / fake SSS:** shipped for leaves and plants (atmosphere package); still open for thin fauna
  membranes, ice and crystal blocks.
- **Reflections:** room probes shipped (#3057); SSR for wet floors / hull is deferred (risky full-screen pass, low ROI).
- **Decals:** scorch marks and craters shipped (#3058); scanner pings are still open.
- **Triplanar mapping** for sloped/carved faces — unblocked now that non-axis geometry exists (shaped
  blocks incl. ramps/domes/cones, mesh flora).

## Key files

- `client/Assets/BlocksBeyondTheStars/Shaders/BlockAtlas.shader` — lit block shader (normals, sun, lamps,
  emission, foliage AlphaToMask).
- `client/Assets/BlocksBeyondTheStars/Shaders/BlockAtlasTransparent.shader` — depth-aware water +
  refraction + glass + falling-water streaks.
- `client/Assets/BlocksBeyondTheStars/Scripts/UrpScenePost.cs` — bloom / tonemap / vignette / grade /
  lens-flare / SMH / motion blur.
- `client/Assets/BlocksBeyondTheStars/Scripts/Sky.cs` — sun, sky colour, per-system grade.
- `client/Assets/BlocksBeyondTheStars/Scripts/BlockTextureAtlas.cs` — procedural colour + normal atlas (the
  code-painted tiles' base colours and per-block looks in `BlockTextureAtlas.CodeTiles.cs`).
- `client/Assets/BlocksBeyondTheStars/Scripts/ClientSettings.cs` — preset gating of every effect.
- High details (#3066): `HighDetails.cs` (the gate), `ClientSettings.HighDetails.cs`, `BlockTextureAtlas.Height.cs`,
  `ChunkMesher.HighDetails.cs`, `GrassShells*.cs` + `MeadowAssets.cs` + `GrassShell`/`MeadowFlower` shaders,
  `RoomReflections.cs`, `FxDecals*.cs` + `Decal.shader`, `VolumetricSunLight.cs` + `VolumetricLightPass.cs` +
  `VolumetricLight.shader`; Unity-free logic in Client.Core: `BlockRelief`, `RoomBounds`, `MeadowField`,
  `FlowerSprite`, `DecalFaces`, `VoxelRay`, `DecalPatterns`, `VolumetricLightPolicy`.

## Open questions

- **Map authoring:** procedural-derive (Sobel etc.) vs AI-gen vs hand for normal/height/strand maps —
  procedural-derive first.
- **Atlas vs texture arrays:** extend the single atlas with normal+height pages, or move to texture
  arrays (cleaner, needs a mesher change).
- **Perf budget per preset:** define target FPS + which effects each preset enables, on what reference
  hardware.
- **Scope vs the father-son audience:** how far toward photoreal before it stops being friendly/readable?
