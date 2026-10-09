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
  coloured block light stays a baked voxel flood-fill (TEXCOORD3) with directional shading on top.

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
- `client/Assets/BlocksBeyondTheStars/Scripts/BlockTextureAtlas.cs` — procedural colour + normal atlas.
- `client/Assets/BlocksBeyondTheStars/Scripts/ClientSettings.cs` — preset gating of every effect.

## Open questions

- **Map authoring:** procedural-derive (Sobel etc.) vs AI-gen vs hand for normal/height/strand maps —
  procedural-derive first.
- **Atlas vs texture arrays:** extend the single atlas with normal+height pages, or move to texture
  arrays (cleaner, needs a mesher change).
- **Perf budget per preset:** define target FPS + which effects each preset enables, on what reference
  hardware.
- **Scope vs the father-son audience:** how far toward photoreal before it stops being friendly/readable?
