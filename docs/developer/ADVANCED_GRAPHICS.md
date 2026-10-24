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
  beveled convex edges (chunk mesher + `BlockAtlas`); **GPU-instanced ground-detail scatter** (grass tufts +
  pebbles on open ground, distance-culled, preset-gated); **3D mesh flora** (leafy flora as leaning 3-plane
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
  `_Sc_SeaLevel` (from the far-terrain world info) keeps wet ground and snow caps off the seabed (#2414); the far-terrain
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

- **Shell texturing** (fur / grass / turf): render a surface as N extruded shells, each clipping a
  strand-noise texture, with wind sway. Biggest on-foot win on grass ground; also drives fur on furred
  creatures (we already generate `creature_fur.bytes`) and short turf on mossy rocks / flora. Cap K by
  preset, fade with distance, top faces near the player only.
- **Parallax Occlusion Mapping** (POM): march a height channel (packed in the atlas tile alpha) in
  tangent space for fake depth on rock/ore, metal/hull panels and player-built brick/wall — recessed
  seams, embedded ore nuggets. Builds straight on the existing normal mapping; gate step count by preset
  + distance, keep heightScale small (voxel faces are 1 unit).
- **Sky / atmosphere / volumetrics:** denser per-system nebula skybox, Rayleigh-ish atmospheric
  scattering (horizon glow + sunrise/sunset tints). (Height fog, sun-warmed haze and light-shaft cards shipped
  with the atmosphere package; a true volumetric pass stays out — it needs WebGPU-only compute and the June
  full-screen attempt darkened the frame.)
- **Detail-scatter extensions:** flowers as a third scatter kind, pairing with grass shells (wind sway shipped
  with the atmosphere package).
- **Translucency / fake SSS:** shipped for leaves and plants (atmosphere package); still open for thin fauna
  membranes, ice and crystal blocks.
- **Reflections:** reflection probes per interior/station (extends the current Fresnel sky reflection);
  SSR for wet floors / hull is deferred (risky full-screen pass, low ROI).
- **Decals:** scorch marks, impact craters, scanner pings — extends the existing `WeaponFx` impacts.
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

## Open questions

- **Map authoring:** procedural-derive (Sobel etc.) vs AI-gen vs hand for normal/height/strand maps —
  procedural-derive first.
- **Atlas vs texture arrays:** extend the single atlas with normal+height pages, or move to texture
  arrays (cleaner, needs a mesher change).
- **Perf budget per preset:** define target FPS + which effects each preset enables, on what reference
  hardware.
- **Scope vs the father-son audience:** how far toward photoreal before it stops being friendly/readable?
