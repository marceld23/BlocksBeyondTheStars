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
  surface measures its exposure past the mesher's shade floor (`BBTS_SHADE_FLOOR` = `SkylightRules.ShadeFloor` 0.55), so
  a roofed floor gets no snow cap and no wet gloss (#3164 — it got 55 % of both); the far-terrain
  sea takes the world's water tint and the rainbow bands from the same globals as the near water — vertex alpha 0.5
  marks it (#2415); the IMGUI underwater wash thins to ~15 % while the shader-side underwater haze is active (#2416).
- **Ressaw, the milky liquid** (2026-11, #3335): the still liquid of the buried G.D.S. ruins is a see-through block that
  meshes like water (`TraitWater`: inset surface, per-corner light on top, faces only toward air — the basin walls behind
  it keep their faces), but every face carries shader mode **7** in `TEXCOORD2.x` (`ChunkMesher.Ressaw.cs`; water uses
  1–4, the gas sea 5, the dense gas 6). `BlockAtlasTransparent` draws mode 7 in the tile's own milky white with its glow
  (the vertex emission lifts it past 1 for the bloom; the surface a little brighter than the sides) under a slow shimmer —
  never in the world's water tint, no waves, foam, refraction or reflection — and milky: about half see-through where the
  view crosses a thin layer, nearly opaque where it runs deep through the liquid (one depth sample, Medium+; a fixed
  alpha on Potato/Low and in the Built-in pass). The light it casts is the block data's (`lightColor`, surface cells
  only). With the head in it, `WeatherFx` draws a milky lilac-white veil with one layer of drifting churn noise instead
  of the blue wash, and `Sky.Mist` closes the underwater fog in to 7 m in a milky murk that ignores the world's water
  colour and the night (it glows). Which liquid looks like what is `LiquidLook` (Client.Core, `RessawLookTests`).
- **Mist, wisps and the cloud band** (2026-11, #3270–#3272; the haze code moved to `Sky.Mist.cs`, the rules to
  `MistLook` in Client.Core, tested headless in `MistLookTests`):
  - *Mist amount:* the height fog's strength is the world's `WorldEnvironment.MistAmount` (the server's per-world mist
    profile, #3269 — the air density is already in it) × the time of day × the weather; the client's hardcoded biome
    switch `Sky.MistFor` and its own air-density factor are gone. Floor and falloff are unchanged (`MistLook.FogFloor`,
    `MistLook.Falloff`).
  - *Wisps (#3271):* `BbtsHazeTerms` multiplies the mist density at the Simpson mid and end points by a wisp factor
    (`BbtsWispAt`, 0 in a gap … 2 in a bank, ~1 on average) from the noise texture's green and blue channels — two
    rank-equalised, independent sheets (`CloudNoiseSheet.BakeEven`; red stays the #2394 cloud-shadow sheet), drifting
    downwind (`_Sc_MistWisp.zw`), sheared a little with height and slowly morphing into each other (`_Sc_MistCam.yz`). The
    camera point's factor is computed on the CPU from the same bytes (`_Sc_MistCam.x`), so the wisps cost two LOD-0
    samples per pixel, only where the mist has density. Strength = the world's `MistWispiness` × the "Fog wisps" switch;
    0 is the #2406 blanket exactly. All presets incl. Potato and the browser. Vertex stages (particles, decals hazed per
    vertex) take the smooth averages instead (`BBTS_HAZE_NOISE`, no texture fetch in a vertex shader).
  - *Mist colour:* `_Sc_MistColor` — the world's `MistColor` lit by `MistLook.LitMist` (daylight with a quarter of the
    sky's own light and a touch of the star's hue; a dark blue-grey at night, never black, never glowing). A world without
    its own colour (−1) keeps the haze colour exactly (alpha 0). A festival's fog tint (#3004) now colours the mist.
  - *Cloud band (#3272, shader part):* a third term between `CloudBaseY` and `CloudTopY` — a trapezoid profile (soft
    edges a quarter of the band) integrated analytically along the ray (`BbtsBandIntegral`), holes from the green sheet at
    a 96-block tile drifting with the cloud shadows (about `cover` of the area is cloud), cover =
    `CloudBandRules.Cover(weather, intensity, cloud density)` × the "Cloud fog" switch, eased over ~20 s. The first 6 m in
    front of the camera never count and the extinction is 0.04–0.07/m, so inside a closed cloud ~15 m stay readable and
    ~40 m fade out (`MistLook.BandAmount`). Gated off underground (`AtmosphereProbe.UndergroundFactor`), aboard and under
    water (`_Sc_MistCam.w`). Standing in the band, the camera background turns to the cloud colour and the sun disc and
    rays dim. One more sample, only for rays that cross the band. `Sky.CloudBandCover01`, `MistStrength01` and
    `MistColorLit` publish the state for the mist cards and the cloud deck.
  - *Colour mixing:* `BbtsHaze(wp, skylight, heightScale)` returns amount and colour from one evaluation (veil in the sky
    colour, mist in its colour, band in the lit cloud colour, mixed by their weights); the block, water, far-terrain and
    particle shaders and `BbtsApplyHaze` use it. `BbtsHazeColor` still works for shaders that ask separately (a noise-free,
    ALU-only estimate of the weights).
  - *Weather colour (#3270):* fog (grey), a gale's or sandstorm's grit (the ground's colour), an ion storm (blue) and
    storms (ash, acid, white-out, rain grey) tint the sky — and with it the distance haze, the camera background and the
    legacy fog — and the mist toward their colour by intensity, keeping the sky's brightness (`MistLook.WeatherTint` /
    `ApplyTint`); ground fog tints only the mist. `WeatherFx3D` used to write these colours into `RenderSettings.fog`,
    which no project shader reads; that path is gone. `Sky.MirrorLegacyFog` is now the only writer of Unity's fog: a
    linear copy of the distance haze for the few materials still on Unity's `Unlit/Color` / `Unlit/Transparent` (geyser
    cubes, lava falls, mining sparks, unlit creature parts, ship FX), so they still fade with the landscape.
  - The cloud-shadow sheet now drifts downwind (its offset was added with the wrong sign, so it moved against the wind
    the grit and the leaves follow).
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
    does not sink with the outdoor night (#2814/#2815). Since #3265 the transparent shader reads the skylight too and
    adds the fill only where the sky does not reach, so the lake outside the window stays untouched.
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
    open; the light-shaft cards stand down while it runs. The suit headlamps ride the same march (#3232, see below);
    torches, lanterns and lava are the baked flood-fill light (ADR 0003) and cast no beams.
  - *Headlamp shadows* (#3233): no shadow map — `LampVolume` keeps a 64³ R8 3D texture of the blocks around the camera
    (`LampOccupancy` in Client.Core: wrap-addressed by world cell, the window snaps in 8-cell steps and refills only the
    slab that came in, one 16³ brick re-read per frame; the opacity per block id comes from the mesher's trait table,
    `ChunkMesher.LampOcclusion.cs`). The lit shaders march from the surface toward each lamp (≤ 32 jittered taps,
    ~0.7 m apart) and dim it by what they cross. Own switch; only while a lamp is lit.

### The suit headlamp (#3229–#3234)

One shared include, `Headlamp.hlsl`, lights every lit shader (`BlockAtlas` both pipelines, `BlockAtlasTransparent`,
`LitColor`, `VertexColorOpaque`, the meadow's `MeadowLight`, the `Particle` shader's `_LampLit` motes and the
volumetric march); `Headlamp.cs` sets its globals, `HeadlampRules` (Client.Core) holds the Unity-free rules.

- **Beam:** an LED profile — a hotspot (12° half angle, a 5° core), a faint reflector ring just inside its edge and a
  soft spill out to 34° at 20 % — instead of a linear cosine ramp across the whole screen.
- **Falloff:** windowed inverse square, `saturate(1 − (d/r)⁴)² / (d² + r₀²)` with r = 30 m and a 1.6 m source radius;
  intensity 22 in a calm, slightly cool LED white. The old `(1 − d/26)²` made 10 m only 2.2× darker than 1 m.
- **Shading:** Lambert plus the block shader's stable GGX glint with each face's own gloss; water mirrors the lamps in
  its SSR (`BbtsHeadlampMirror`).
- **Origin:** first person 13 cm right / 8 cm above the eye on the helmet, aimed at the point 9 m ahead of the eye;
  third person the lamp cube on the avatar's helmet. Published in `PlayerController.LateUpdate`, after the camera moved.
- **Air:** the flat cone mesh is gone. The motes' `Beam` mode (`AmbientMotes`) puts near-black dust around the visor
  where no other motes drift; the beam lights it. On High the volumetric march adds the lamps' in-scattering with a
  half-even phase (looking along your own beam is backscatter, so it stays faint in clear air).
- **Eyes:** `HeadlampRules.AdaptedBrightness` lets the eye adaptation count a lit lamp as light — a dark cave earns
  much less of the +EV boost, so the dark around the beam reads darker; the shaders' ambient floor is unchanged.
- **Other players (#3234):** the `GearLook.LampLit` bit of the presence's gear mask (switched on and worn; a bit, not a
  field, so the presence stays under the compression threshold) re-sends the presence on a toggle; the
  client uploads the nearest four lit lamps within 48 m as `_Sc_RLamp*` arrays from each avatar's helmet along its head.

### Caves, tunnels and nights are dark (#3265)

A dug tunnel lit like tree shade along its whole length, and the eye adaptation brightening every dark scene ×1.78, made a
cave about as bright as noon. Caves and moonless nights are now dark — never pitch black (silhouettes stay readable) —
and the suit lamp, torches and glowing cave flora are what lights them. Expected (scene-linear vs. noon on open ground,
top face, sun-like star): a cave without light ≈ 6–7 % by day and ≈ 5 % at night (was ~100 % / 21 %), an open
moonless night ≈ 18 % (was 28 %).

- **Mesher skylight** (`ChunkMesher.Skylight.cs` → `SkylightField` / `SkylightRules` in Client.Core, tested headless in
  `SkylightFieldTests`): open overhead → 1, except at the bottom of a one-block-wide well (≥ 6 of the 8 neighbours are
  walls at the cell's height: a 1×1 shaft, a narrow trench) — `WellLight` by the lowest wall above the cell: 1 block → 1,
  3 → 0.55, ≥ 8 → 0.25 (a one-block hole stays lit). Covered → max(the 5×5 kernel's open share, the shade floor
  0.55 × max(thin cover × depth fade, near opening)). *Thin cover* counts the light-stopping blocks between the cell and
  its column top (the block-light flood's rule — leaves, plants, glass, water, slim props and cutout cubes do not count):
  0–1 → 1, 2 → 0.5, ≥ 3 → 0, times the #1608 depth fade (full to 6 deep, gone at 14), so a crown or a one-block roof
  keeps 0.55 exactly as before. *Near opening* = the nearest column open at the cell's own height — the kernel's rings,
  then the 8 compass directions at 3–7 blocks — ≤ 2 → 1, linear to 0 at 8, times that opening's own skylight (the tunnel
  at the foot of a deep shaft is never brighter than the shaft's floor). The column window grew to 16 + 2 × 10 (the
  probes plus the opening's kernel); per-cell values are cached per build. Every skylight consumer keyed on ≥ 0.55
  (wet surfaces, snow caps, sway shelter, ground scatter, plant glow, height fog) reads exactly as before.
- **Sky ambient** (`BbtsSkyAmbient` in `AtmosphereCommon.hlsl`): `lerp(0.32, 0.78, sky) − 0.30 · saturate(1 − sky/0.55)` —
  unchanged from the shade floor up, 0.02 instead of 0.32 at sky 0, so a cave no longer follows the sun (noon vs. night
  differs only by the small `light` share). Used by `BlockAtlas` (URP; the Built-in pass repeats the formula), the
  meadow (`MeadowLight.hlsl`) and the Built-in `ScatterLit`; `Reports20261103WiringTests` pins all four. The flat
  `+ 0.05` is the global `_Sc_DarkFloor` (`BbtsDarkFloor`, default 0.045; unset reads as the default).
- **Water and glass** (`BlockAtlasTransparent`) get the mesher skylight (`TEXCOORD5`): their light is
  `sun × BbtsSkyLightScale(sky)` (the ramp normalised to the open sky: 1 / 0.73 / 0.03) plus the interior fill on
  sky-occluded panes; the night fill is gated by the skylight, the dark floor added below it, the sky reflection and the
  sun glint darkened by it, and the haze gets the real value. Open water is unchanged.
- **Haze underground:** `BbtsHazeAmountScaled` × `lerp(saturate(sky·2), 1, camera exposure)` (not under water) — a long
  tunnel's far end no longer sinks into a bright sky-coloured veil.
- **Eye adaptation** (`EyeAdaptationRules`, Client.Core): the dark's boost (up to +0.9 EV) is capped at +0.35 EV
  underground — `AtmosphereProbe.UndergroundFactor`: at least three sky-stopping blocks over the head (rock, not leaves,
  glass or water; `ChunkMesher.StopsSky`) — and above ground eases from +0.2 EV at night to the old +0.9 EV by day, so a
  forest floor or a house at noon keeps its look (`ExposedToSky` is false under any tree crown, so it is not the signal;
  the models' light and the cosy damping key on `UndergroundFactor` too); toward the dark at 0.07 EV/s (~5 s to the cap), toward the
  light still 2.5 EV/s. `HeadlampRules.AdaptedBrightness` still counts a lit lamp as light. The `[Atmosphere]` capture
  log line carries `adaptEv` and `modelLight`.
- **Models** (`LitColor`: creatures, avatars, doors, held items): key light, floor and fill × `_Sc_ModelLight`
  (`DarkLightRules.ModelLight`, set per camera in `Sky.OnBeginCamera` — previews and the menu keep 1): the world light's
  luma from 0.12 (moonless night) to 1 (noon), sinking toward 0.15 while the camera is underground, lifted by an interior
  fill. The glow pattern, the headlamps and the FX lights stay on top, so a hostile in the dark stays visible.
- **Cosy grade:** the place factor of `Sky.CozyPlaceTarget` × `lerp(0.3, 1, camera exposure)` unless fixtures light the
  spot (`LampLight > 0.3`) — a dark cave gets no shadow lift, a lit camp in a cave stays cosy. Home is unchanged.
- **Own base:** a room of the player's own base (`AtHome`, not aboard) gets a gentle interior fill of 0.3
  (`DarkLightRules.HomeBaseFill`, `Sky.Dark.cs`), so a dug home without lamps is dim-cosy, not cave-dark. The distance
  haze still fades with the cabin fill only.
- **"Visibility in the dark"** (`ClientSettings.DarkVisibility`, 0–100 % in steps of 25, default 0): the dark floor
  0.045 → 0.15, the underground adaptation cap +0.35 → +0.7 EV and the models' dark value 0.12 → 0.4. The Brightness
  slider and the eye-adaptation switch stay as they were.
- **VEGA** (server): the lamp and torch hints count a player as underground from 3 light-stopping blocks overhead
  (was 6 blocks of anything; leaves, plants, water and glass no longer count — `GameServer.CoversFromSky`).
- Night itself is untouched (`Sky` night floor 0.35): measure the open night with the clip recorder before lowering it.
- **Night sight** (#3414: the bio lab's preparation, a lit Ressaw lamp worn) lifts the dark again for its wearer only:
  `BioSenses.NightSight` (eased 0..1) is a `Sky` input like the lightning flash, and `NightSightRules` (Client.Core,
  `NightSightRulesTests`) give the raised light level (`LightDay` → 0.74 brightness, so the world light, the models'
  light and the ambient follow), the raised day of the sky, camera background, haze, legacy fog, mist, cloud band and
  clouds (`SeenDay` 0.65, applied before the weather tint), a cave fill on sky-less faces (`CaveFill` 0.8) and a star
  boost (×1.35). The time of day and the sun stay, so stars and moons stay; the eye adaptation counts it as light
  (`AdaptedLight`). `NightSightWiringTests` pin the wiring. Details: [BIO_LAB.md](BIO_LAB.md) §16.

### Automatic preset calibration (#1423, #3279–#3283)

A **new install** on every platform starts with `ClientSettings.PresetAuto` on: desktop on Medium, the browser and
tablets on Low. A preset picked by hand in Settings → Graphics clears the flag for good. Existing installs load the flag
as off, so their saved preset never moves. The rules are Unity-free in Client.Core (`QualityCalibration`, tested in
`QualityCalibrationTests`); three places feed them:

- **Session start** (`AutoQualityCalibrator.SessionStart`, in `AppShell.Awake` before the first `Apply`): **Medium →
  High**, and only on a **world measurement** taken at Medium on the same GPU (`ClientSettings.AutoWorld*`). The menu
  backdrop is a space scene without terrain, SSAO on ground, shadow cascades or grass, so it cannot vouch for High.
- **Shell sample** (`AutoQualityCalibrator`, 6 s warm-up + 15 s over studio logo, splash, intro and main menu): one
  notch per session — down when the wall-clock median is over 40 ms, up **at most to Medium** when there is headroom.
  A step down from High remembers the GPU (`AutoHighRejectedGpu`); High is not tried on it again.
- **World** (`WorldPerfWatch`, after `WorldReady` + 12 s, never during a cutscene, the space view, loading or an
  unfocused window): one 20 s sample per session, kept only for the next start. A slow world **never changes a
  setting** by itself. Instead, a 30 s window with a median over 40 ms shows the `ui.perf.slow_hint` toast once per
  session — for hand-picked and auto presets alike, unless Potato and a view distance of 2 leave nothing to lower.

**Headroom.** With GPU time, the GPU p90 × the preset's cost factor has to fit 80 % of the 16.7 ms budget, and the
wall-clock median has to stay at or under 20 ms (a CPU that already misses 50 fps gains nothing). The GPU time comes
from `FrameTimingManager` (`GpuFrameTime`; Player Settings → Frame Timing Stats is on). Without GPU time — the browser,
or an API without timer queries — the VSync-capped wall-clock p90 has to stay under 18 ms. A software renderer
(Microsoft Basic Render Driver, llvmpipe, softpipe, SwiftShader) and a laptop on battery never step up.

**The High cost factor** (`QualityCalibration.HighGpuCostFactor = 2.0`, #3283). PerfProbe on 2026-11-06, reference
laptop (Ryzen 9 PRO 7940HS, RTX 2000 Ada Laptop GPU, Direct3D 12), seed 424242, view distance 8, 30 s idle + 30 s walk,
quiet machine:

| Preset | VSync | idle: fps · GPU p50 / p90 | walk: fps · GPU p50 / p90 |
|---|---|---|---|
| Medium | on | 60 · 8.2 / 8.7 ms | 58 · 10.4 / 14.8 ms |
| Medium | off | 70 · 10.6 / 15.2 ms | 61 · 12.7 / 14.8 ms |
| High | off | 42 · 21.8 / 25.2 ms | 43 · 20.5 / 22.8 ms |
| High | on | 38 · 22.7 / 26.0 ms | 37 · 22.0 / 23.7 ms |

High costs 1.5–3.0 × the GPU p90 of Medium in the same spot (about 1.6 while walking, up to 3.0 in the settled idle
view). The main thread stays at a 6–7 ms median on both, so the step is GPU work. 2.0 makes a step up land at about
60 fps or better. This laptop stays on Medium: its world sample read "median 16.7 ms, p90 18.6 ms, GPU p90 16.1 ms — no
room for High", matching its 37–43 fps on High. A GPU about twice as fast gets High. The menu itself costs 5.2 ms of
GPU p90 at Medium. Measure on a quiet machine: a second game rendering at the same time (a parallel clip capture) doubled
`gpuFrameTime` in a first run.

PerfProbe (`-perfProbe -perfPreset <name>`) writes the GPU p50/p90 next to the wall-clock numbers and switches
`PresetAuto` off for its run, so a re-measurement only needs the same command line on a quiet machine.

### Fog banks in the air (#3273)

The haze is mixed into the surface a pixel hits, so on its own it can never hang a bank of mist in front of the open sky.
`MistCards` adds a few large, soft, camera-facing cards over the valleys — the rule of the package is *a few large cards,
never many small particles* (Valheim's thousands of fog particles are a known frame-rate sink; its mods replace them with
a few ground-mist planes).

- **Placement:** a pool of cards 6–20 m wide (height 32–50 % of the width) in a ring 15–70 m around the camera, at
  `AtmosphereProbe.FogFloor` + 1–4 m. A spot holds mist only where the highest ground under the card's footprint (five
  column scans) lies at most 3 m above the fog floor — never on a hill, a ridge or a tree crown, never inside the ground or
  a parked hull (`LandedShipCovers`). Cards drift with the smoothed wind (0.25 + 2.2 × wind m/s), live 45–100 s, fade in
  and out over 6 s, breathe a little in width, and their two noise offsets evolve; a card that leaves the ring, outlives
  its life or is walked into dissolves and comes back elsewhere.
- **Fades:** per card toward the far edge of the ring (the last 15 m) and while the camera would stand in it; per pixel
  in the shader nothing within 2 m of the camera, fully there from 10 m (`MistCardRules.PixelNearFade`) — never "in your
  face". Soft particles from Medium (`_Sc_ScreenFx` × `_Sc_SoftParticles`, 2 m).
- **Look:** count and opacity from `Sky.MistStrength01` (the world's mist × time of day × weather — dawn, dusk and fog
  weather strongest): nothing below 0.1, the whole budget from 0.55, a card's peak alpha 0.15–0.55. The colour is the
  height fog's own lit mist colour, read in the shader from the shared include (`BbtsHazeMix(wp, (0,1,0))`), so a card is
  exactly the mist the land behind it sinks into; the card itself sits inside the haze (`BbtsHaze`). The shape is not
  baked: a soft ellipse whose rim the tileable noise bites into (`MistNoise` in Client.Core → `MistNoiseTexture`, 128²
  RG, mip-mapped; the fine octave is sampled at twice the tiling, so the wrapped offsets never show a seam).
- **Gate:** only under the open sky — the probe's eased camera exposure × (1 − underground); never aboard, on a station,
  on a spacewalk, above the atmosphere line, in the flight view or under water.
- **Budget:** Potato 0 · Low 4 · Medium 10 · High 20, the browser (and other light devices) none
  (`MistCardRules.Budget`), behind *Settings → Atmosphere effects → Fog banks* (`ClientSettings.FogBanks`). On top a cap
  on the screen area all cards in front of the camera cover (Low 0.6, Medium 1.0, High 1.4 screens): the largest (=
  nearest) card dissolves first. All cards are ONE dynamic mesh written far to near every frame (one draw call, correct
  blending order), `MistCard.shader` (URP only, always-included).

### The sea of clouds and the dome clouds at altitude (#3272)

The haze shader's cloud band is what you stand in between the world's `CloudBaseY` and `CloudTopY`; two client pieces
complete it from the outside.

- **Sea of clouds** (`CloudSea`, Medium+ on a desktop, *Cloud fog* switch): a flat disc (a centre vertex and 40 rings ×
  64 segments, radii growing as (k/40)^1.6, 90 % of the far plane wide, 300–2000 blocks) follows the camera in XZ at
  `CloudTopY`. `CloudSea.shader` cuts the cloud out of world-fixed noise — the coarse octave drifts with the wind, the
  fine one (three times the tiling) evolves on its own, both wrapped at one tile — with the threshold from the weather's
  cover (`Sky.CloudBandCover01`, the same `CloudBandRules.Cover` the band uses): nothing on a clear day, holes with
  clouds, nearly closed in rain (`CloudSeaRules.Threshold/Opacity`). A third sample toward the sun gives the billows a lit
  and a shaded side; the colour is the band's own lit colour (`_Sc_CloudBandColor`), so the cloud you climb out of is the
  cloud below (the C# side's cloud-colour tint stands in while no band colour is set). The rim fades out, the distance
  haze takes it into the horizon, and with the depth texture it melts into the mountain flanks that pierce it (8 m).
  Visible only from above: it fades in from 1 m under the top to fully there 6 m above it
  (`CloudSeaRules.DeckVisibility`); inside the band the band term is the cloud. Never in space, on a station, aboard or
  underground. The class is not called `CloudDeck`: that name is the dome clouds' drift (#3202).
- **Dome clouds** (`Clouds`): the low cumulus and stratus fade out while the camera climbs through the band
  (`CloudSeaRules.DomeLowFade`: all below the base, none above the top), so they no longer hover overhead while you stand
  in a cloud or look down on the sea; the high cirrus stay. Only by height — on a world without a band nothing changes.

### Distance softness on worlds with air (#3461, #3462)

The haze only blends colour; the far field kept its full pixel sharpness, so point-filtered textures flickered and showed
mip bands at distance, and the seam to the coarse far terrain stayed visible. Two parts soften the far part of the haze —
both **barely noticeable** on purpose, both only where the world has air. The gate is the server's
`AtmosphereDensity > 0`, not "the haze is on": the crystal world keeps a haze but has no air and stays as sharp as the
moons, asteroids and the void. The rules are `DistanceSoftLook` (Client.Core, headless-tested). `Sky.Mist.cs`
`ApplyDistanceSoft` turns them into one global, `_Sc_DistSoft` (layout in the `AtmosphereCommon.hlsl` header). The
global is zeroed by `ClearMist`, faded out indoors and under water, and empty for preview cameras (their `_Sc_Fog` is
off). Weather needs no rule of its own: fog, rain and sandstorms pull the haze range in, and both parts follow it.

- **Texture softening (#3461, every preset, the browser too, rides on *Distance haze*):**
  - `BlockAtlas.shader` fades a texel toward its tile's average colour along the distance veil
    (`BbtsDistSoftTexture`: strength × the linear haze-distance term). The strength is 0.25 in thin air and 0.35 in
    thick air, reached at the haze end.
  - The average is one explicit-LOD tap of the atlas's mip with exactly one texel per tile: mip 6 of the 64-px tiles,
    or mip 5 under Potato's global mip limit, which halves runtime textures. Sky sends that mip in `_Sc_DistSoft.w`.
  - A cutout tile's black holes are divided out (mean / alpha).
  - The alpha is never touched, and the normal map and the parallax fade by the same share.
  - It is deliberately **not** a mip bias: with point filtering that only moves the hard mip bands closer, and the
    cutout leaves would lose coverage (distant trees thinning out).
  - Silhouettes stay crisp, so a village or a creature far away is as easy to spot as before.
- **Soft distance (#3462, Medium+ on a desktop client, own switch *Soft distance* — `ClientSettings.DistanceSoftness`):**
  - `DistanceSoftView` enqueues `DistanceSoftPass` on the world cameras at `BeforeRenderingPostProcessing`, after the
    transparents and the heat shimmer and before bloom, while `Sky.DistanceBlur01 > 0`.
  - The passes:
    1. the camera colour to quarter resolution (four depth-aware taps per 4×4 block), with the scene's distance in alpha;
    2. a depth-aware, separable 7-tap gaussian;
    3. a depth-aware upsample blended over the camera colour with
       `blend × smoothstep(band start, haze end, distance)`.
  - The blend is 0.4 (thin air) to 0.5 (thick air). The band starts at 40 % of the haze range and is full at its end.
  - The sky stays sharp:
    - far-plane pixels (open sky, the sun, stars, aurora, rings, clouds against the sky) get weight 0;
    - the moons and planets of the surface sky (`SkyBodyPhase`, opaque with `ZWrite On` at 60–460 m, inside the band)
      write stencil bit 64, and the composite binds the camera depth-stencil read-only and skips them;
    - a pixel without a neighbour at a similar distance (a thin silhouette against the sky) stays sharp;
    - the downsample is depth-aware over its 2×2 pixels (only those at the nearest surface's distance count), so the sky's
      colour never bleeds into a ridgeline texel.
  - Transparents stay sharp: the depth texture is copied after the transparents (`m_CopyDepthMode: 1`), so moving the pass
    before them would force a depth prepass. Instead the particles (`Particle`, `ParticleAlpha`: rain, snow, sparks, dust)
    and the world-space labels (`WorldText`) write the same stencil bit 64 — their pixels read the depth of the hills
    behind them and would otherwise be blurred with those hills. Smoke (`ParticleSmoke`) is soft anyway and does not.
  - It is off with *Reduced effects*, with the haze off, in the flight view, indoors and under water, and it fades out
    underground (`AtmosphereProbe.UndergroundFactor` — not the camera's open-sky exposure, which is 0 under any tree
    crown and would switch it off for a player looking out of a forest).
  - It never runs in the browser: the WebGPU render-texture pitfalls and the frame budget. `DistanceSoft.shader` is
    always-included and in `ShaderPrewarm` (desktop), so it still compiles for WebGL.
  - Cost: measured with PerfProbe (High, 1080p, `-perfFeature softdist=off` for the A/B): at half resolution about 0.9 ms
    GPU, consistently — over the ~0.5 ms budget. Quarter resolution plus a cheap eye-depth early out in the composite (the
    near and middle ground never reach the world-position maths) brought it inside run-to-run noise (on − off −0.35 /
    +0.45 ms over two pairs).
  - Fallback if a graphics API ever makes the stencil route unreliable: move the surface-sky body materials to a queue
    after the opaque depth copy (2501). They then read as far plane, but they would drop out of the water reflections,
    which reflect via the opaque copy.

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
  volumetric sun light shipped with the High details as a fragment raymarch, #3060, the suit lamps' beams with
  #3232 — beams from torches and lava would need the FX lights as point sources in the march.)
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
  `VolumetricLight.shader`, `LampVolume.cs` + `ChunkMesher.LampOcclusion.cs`; Unity-free logic in Client.Core:
  `BlockRelief`, `RoomBounds`, `MeadowField`, `FlowerSprite`, `DecalFaces`, `VoxelRay`, `DecalPatterns`,
  `VolumetricLightPolicy`, `LampOccupancy`.
- Suit headlamp (#3229–#3234): `Shaders/Headlamp.hlsl`, `Headlamp.cs`, `PlayerController.Gear.cs`,
  `PlayerAvatar.Lamp.cs`, `RemotePlayers.Lamps.cs`; Client.Core `HeadlampRules`.
- The dark (#3265): `ChunkMesher.Skylight.cs`, `Sky.Dark.cs`, `AtmosphereProbe.cs`, `AtmosphereCommon.hlsl`
  (`BbtsSkyAmbient`, `BbtsDarkFloor`, `BbtsSkyLightScale`); Client.Core `SkylightRules`, `SkylightField`,
  `EyeAdaptationRules`, `DarkLightRules`.
- Night sight (#3414): `BioSenses.cs` (the eased input), `Sky.cs`, `Sky.Dark.cs`, `Sky.Mist.cs`, `Starfield.cs`,
  `Clouds.cs`, `CloudSea.cs`, `AtmosphereProbe.cs`; Client.Core `NightSightRules`.
- Preset calibration (#3279–#3283): `AutoQualityCalibrator.cs`, `WorldPerfWatch.cs`, `GpuFrameTime.cs`,
  `ClientSettings.AutoQuality.cs`, `PerfProbe.cs`; Client.Core `QualityCalibration`.

- Fog banks and the sea of clouds (#3272/#3273): `MistCards.cs` + `MistCard.shader`, `CloudSea.cs` + `CloudSea.shader`,
  `MistNoiseTexture.cs`, the dome fade in `Clouds.cs`; Client.Core `MistCardRules`, `CloudSeaRules`, `MistNoise`.
- Distance softness (#3461/#3462): `Sky.Mist.cs` (`ApplyDistanceSoft`), `AtmosphereCommon.hlsl` (`_Sc_DistSoft`,
  `BbtsDistSoftTexture`), `BlockAtlas.shader`, `DistanceSoftView.cs` + `DistanceSoftPass.cs` + `DistanceSoft.shader`, the
  stencil bit in `SkyBodyPhase.shader`; Client.Core `DistanceSoftLook`.

## Open questions

- **Map authoring:** procedural-derive (Sobel etc.) vs AI-gen vs hand for normal/height/strand maps —
  procedural-derive first.
- **Atlas vs texture arrays:** extend the single atlas with normal+height pages, or move to texture
  arrays (cleaner, needs a mesher change).
- **Perf budget per preset:** define target FPS + which effects each preset enables, on what reference
  hardware.
- **Scope vs the father-son audience:** how far toward photoreal before it stops being friendly/readable?
