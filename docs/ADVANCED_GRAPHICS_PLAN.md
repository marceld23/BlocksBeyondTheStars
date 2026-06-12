# Advanced Graphics Plan — Shell Shaders, Parallax & Beyond

Status: **partially shipped.** Phase 0 (URP migration, 2026-06-10), Phase 1 (post stack + emissive)
and Phase 1.5 (normal mapping) are ✅ done — and since 2026-06-12 the project renders in **Linear
color space** (see the professional-look pass in TODO.md and docs/ART_BIBLE.md). Phases 2–4 below
remain the research + roadmap for the next polish milestones. The "Built-in RP" constraints in §0
describe the historical starting point; URP is the shipping pipeline today (the hand-written
shaders are dual-pipeline).

Related: [docs/UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md) (the broader UI + renderer overhaul),
the M27 render pass in [TODO.md](../TODO.md), and the "more professional look with effects" design note.

---

## 0. Constraints (what shapes every decision)

- **Render pipeline:** Built-in RP (Unity 6, 6000.4.9f1). No URP/HDRP today. Everything is built
  **in code** — no scene authoring, no Shader Graph. Shaders are hand-written `.shader` files and must
  be force-added to GraphicsSettings "Always Included" or the player build strips them
  (`BuildScript.EnsureShadersIncluded`).
- **Textures** are bundled as **raw RGBA32 `.bytes`** and loaded with `Texture2D.LoadRawTextureData`
  (the `ImageConversion` module isn't referenced from the client asmdef). Any new map (normal/height/
  roughness) must go through the same `bundle_textures.py` path, or be packed into the existing atlas.
- **Audience:** a father-son game — readable, friendly, "schick" over photoreal. Performance must stay
  fine on a mid-range PC; effects are **preset-gated** (Low/Medium/High in `ClientSettings`).
- **Voxel world:** geometry is blocky and regenerated per chunk. Techniques that fake surface detail
  (normal/parallax) buy a lot because the silhouettes are simple.

### The one strategic question: stay Built-in RP, or move to URP?

Several high-value effects (TAA, SSR, a maintained post-processing stack, Forward+ many-lights,
Shader Graph authoring) are **first-class in URP** and awkward in Built-in RP. Migrating is a real cost
(rewrite the 3 hand shaders, re-verify the always-included list, re-test on all presets) but unlocks the
whole modern toolbox and is where Unity investment goes.

**Recommendation:** treat URP migration as **Phase 0, optional but enabling**. Phases 1–2 below are
written to work in **Built-in RP today** (normal maps, POM, shells, the legacy Post-Processing Stack v2,
planar water). If we later adopt URP, Phases 3+ (SSR, TAA, volumetrics, decals, Forward+ lights) become
much cheaper. Decide before Phase 3. Everything before that is not wasted — the maps and shader math
port directly.

---

## 1. Shell texturing ("shell shaders") — fur, grass, turf

**What it is:** render a surface as **N stacked, slightly extruded copies** (shells) of itself along the
normal. Each shell samples a noise/strand texture and discards fragments below a per-shell alpha
threshold, so strands appear to thin out toward the tips. Cheap, fully real-time fur/grass/moss. (Popularised
again by Acerola's "fur" breakdown; classic Lengyel "concentric shells".)

**How in our engine (Built-in RP, code):**
- A `BlocksBeyondTheStars/Shell` shader with the geometry drawn **K times** (K≈16–48 for fur, 4–8 for short
  grass). Two options: (a) a multi-pass shader, or (b) draw the mesh K times via
  `Graphics.DrawMeshInstanced` with a per-instance shell index — (b) is more flexible and GPU-instanced.
- Per shell: extrude position `+= normal * shellHeight * (i/K)`; sample a tiling strand-noise (a single
  grayscale `.bytes` map); `clip(noise - threshold(i))`; darken lower shells for self-shadow (ambient
  occlusion toward the roots); tint by the surface colour. Add **wind** as a time-based sway of the tip
  offset (more sway on higher shells) and a gravity droop.

**Where to use it — maximally:**
- **Grassy biome ground:** grass shells on top surfaces of grass/turf blocks → fields that look alive
  instead of flat green cubes. Biggest visual win on foot.
- **Creatures:** we already generate `creature_fur.bytes` — drive a **fur shell** layer on furred
  creatures (the `PickHide`/material path in `CreatureBuilder`). Spiky chitin → short hard shells; slime
  → wet rim only (no shells).
- **Flora:** mossy rocks, fuzzy alien plants, fungal caps — short shell turf on flora meshes.
- **Stations/ships:** none (keep hard-surface clean), except maybe worn carpet in quarters.

**Cost/risk:** overdraw scales with K and screen coverage — cap K by preset (High 32 / Med 16 / Low off),
fade shells out with distance, and only apply to **top faces near the player**. Transparent shells fight
the depth sort; render after opaque with `ZWrite` on the base shell only.

---

## 2. Parallax Occlusion Mapping (POM) — fake depth on flat block faces

**What it is:** in the fragment shader, march the view ray through a **height map** in tangent space and
offset the UV so the surface appears to have real depth (bricks recess, rock cracks deepen, panels have
inset bolts) — with self-occlusion, unlike simple parallax. No extra geometry.

**How in our engine:**
- Extend `BlocksBeyondTheStars/BlockAtlas` (and `LitColor` for hard-surface meshes) with a **height channel**.
  Pack height into the **alpha of the existing atlas tile** (or a parallel height atlas `.bytes`) so we
  don't change the vertex layout much. We need a **tangent** per vertex — voxel faces are axis-aligned,
  so the tangent is trivial to set per face direction in the mesh builder.
- Fragment: a fixed-step linear search (8–24 steps by preset) along the view dir in tangent space, then a
  binary refinement; clamp `heightScale` small (voxel faces are 1 unit) to avoid swim at grazing angles.
  Combine with the normal map (Phase 1.5) for lighting.

**Where to use it — maximally:**
- **Rock/ore/stone blocks:** deep crevices, embedded ore nuggets that catch the sun.
- **Metal/hull/station panels:** recessed panel seams, rivets, vents — instantly "built", not painted.
- **Brick/wall/floor** building blocks: depth in player constructions.
- **Ground detail** under grass shells: pebbly soil.

**Cost/risk:** per-pixel loop is the cost — gate step count by preset and **distance** (drop to plain
normal mapping far away, flat texture at the horizon). Grazing angles + big heightScale = artifacts; keep
scale conservative. Needs correct tangents from the chunk mesher.

**Prerequisite — normal mapping (Phase 1.5, do this first):** POM without normal maps looks half-done.
Add a **normal atlas** parallel to the colour atlas and do tangent-space lighting in `BlockAtlas`/`LitColor`.
Normal maps alone are the **highest bang-for-buck** upgrade for a voxel game (every block face gains
micro-relief) and POM builds straight on top.

---

## 3. The wider toolbox (ordered by impact ÷ effort)

Each entry: what it adds, where, and the engine note.

**A. Post-processing stack (bloom, tonemap, colour grade, AO, vignette, grain).**
The single biggest "professional" lift. In Built-in RP use the **Post-Processing Stack v2** package
(legacy but solid); in URP it's built in.
- **Bloom + HDR tonemapping:** suns, engine flames, weapon beams, glowing ores, holographic UI all
  *glow*. Ties to the existing `SunGlow` and emissive ideas.
- **Ambient Occlusion (SSAO):** contact shadows in every voxel crevice and under objects — enormous for
  the blocky look. (Already flagged in M27 as "needs the post package".)
- **Colour grading / LUT per system + biome:** each star system / biome gets a mood (cold blue ice world,
  amber desert, sickly green swamp) driven by the per-system sun colour we already compute.
- **Vignette + subtle film grain + chromatic aberration:** tasteful, preset-gated, off on Low.

**B. Emissive + glow materials.** Mark ores, lights, engines, reactor cores, UI holograms as emissive so
bloom catches them; pulse via time. Cheap, dramatic. Needs the `LitColor`/`BlockAtlas` emissive channel.

**C. Better sky + atmosphere.**
- **Procedural HDR skybox per system:** denser starfield + a per-system nebula gradient (we have system
  seeds/colours). Replaces flat sky.
- **Atmospheric scattering (Rayleigh-ish gradient):** horizon glow + sky colour driven by sun position +
  per-planet air colour; sunrise/sunset tints (the sun already wanders by time of day).
- **Volumetric light shafts / god rays** from the sun (screen-space radial blur masked by the sun, or true
  volumetric in URP). Strong mood, especially through station windows / forests.

**D. Water & lava.** A proper `BlocksBeyondTheStars/Water` shader: animated normal scroll, Fresnel reflection,
depth-based colour + transparency, shoreline foam (depth difference), and **planar reflections** (a second
camera) for metal/water — cheaper than SSR in Built-in RP. Lava: emissive flow + heat haze (distortion).

**E. Vegetation & detail scatter.** GPU-**instanced** grass tufts, pebbles, rocks, flowers scattered on
surfaces (`DrawMeshInstancedIndirect`), wind-animated in the vertex shader. Pairs with grass shells for
density. Density by preset + distance.

**F. Particles & weather.** GPU particles for ambient dust motes, pollen, embers, engine trails, and
**weather per biome** (rain with ripples, snow, ash, sandstorm) — already flagged under World systems.
Lightning flashes drive a one-frame bloom spike.

**G. Translucency / fake subsurface scattering.** Cheap wrap-lighting + back-light term for leaves, thin
fauna membranes, ice, crystals — light bleeds through edges. Big for flora/crystal biomes.

**H. Triplanar mapping.** For terrain/ore blocks on sloped or carved faces, triplanar avoids UV stretch
and lets one material wrap any face — useful once we carve non-axis geometry; for pure voxels it mainly
helps blended materials.

**I. Decals.** Scorch marks, impact craters, scanner pings, blood/oil — projected decals (deferred decals
in URP, or mesh decals in Built-in). Extends the existing `WeaponFx`/impact effects.

**J. Reflections — probes + SSR.** Reflection **probes** per interior/station give metals real
surroundings to reflect (extends the current Fresnel sky reflection). **SSR** (screen-space reflections)
for wet floors/hull — best in URP/deferred; defer to Phase 0 decision.

**K. Anti-aliasing & camera feel.** SMAA/TAA (TAA needs motion vectors → easiest in URP). Camera polish:
head-bob, FOV kick on boost, impact shake, weapon viewmodel sway — cheap, high "feel" return.

**L. Shadows.** Tighter cascade settings + contact shadows; soft shadows on High. Voxel worlds reveal
shadow acne — tune bias per preset.

---

## 4. Mapping to game elements (so nothing is missed)

| Element | Techniques |
|---|---|
| Terrain blocks | normal maps, POM, SSAO, triplanar, grass shells, detail scatter |
| Ores/crystals | emissive + bloom, POM nuggets, translucency |
| Water / lava | water shader + planar reflections; lava emissive + heat haze |
| Flora | grass/moss shells, translucency, wind, detail scatter |
| Fauna (procedural) | fur shells (we have `creature_fur`), translucency, rim light |
| Player avatar | normal-mapped gear, emissive lamp/visor/jetpack, soft shadow |
| Ships / stations | POM panels, emissive lights/engines, reflection probes, decals |
| Space view | HDR nebula skybox, bloom suns, god rays, planet atmosphere rim |
| Weapons/tools | emissive beams + bloom, impact decals, particle trails (have base) |
| Sky/weather | atmospheric scattering, volumetric shafts, weather particles |
| Whole frame | post stack: bloom, tonemap, AO, colour grade, vignette, AA |

---

## 5. Phasing (proposed milestones)

- **Phase 0 (decision, optional):** evaluate URP migration. Spike: port the 3 shaders, confirm the
  always-included/build flow, measure perf. Decide go/no-go before Phase 3.
- **Phase 1 — Post stack + emissive (highest impact, Built-in RP):** ✅ **DONE** — a code-only `PostFx.cs`
  (no PPv2) with bloom + ACES tonemap + vignette + SSAO (preset-gated), **plus emissive blocks** (ores/
  crystals/lava/lights/glowing flora glow via the atlas vertex-alpha + a BlockAtlas emission term) and a
  **suit headlamp** + **ship lights** (custom shader spotlight + emissive nav/headlights). Remaining:
  per-system/biome **colour grading (LUT)**. Original outline below.
- **Phase 1 (orig) — Post stack + emissive:** Post-Processing Stack v2: bloom,
  ACES tonemap, SSAO, per-system/biome colour grade, vignette; emissive channel for ores/lights/engines/
  beams/UI. Preset-gated. *This alone makes the game look "pro".*
- **Phase 1.5 — Normal mapping — ✅ DONE (blocks).** Normal atlas **derived procedurally** (Sobel on the
  colour atlas luminance, no bundled assets) + per-face tangents in the chunk mesher + tangent-space
  lighting in `BlockAtlas` (sun, specular, Fresnel + headlamp all use the per-pixel normal). Remaining:
  optionally the same for `LitColor` (avatar/creatures/decor), and hand-/AI-authored normal maps for key
  blocks if the Sobel approximation isn't enough.
- **Phase 2 — POM + shells + water:** parallax on rock/metal/panel blocks; grass shells on turf + fur
  shells on furred creatures; the water/lava shader with planar reflections.
- **Phase 3 — Sky/atmosphere/volumetrics:** HDR per-system nebula skybox, atmospheric scattering, god
  rays; weather particles + lightning bloom.
- **Phase 4 — Detail & polish:** GPU detail scatter (grass/pebbles), reflection probes, decals,
  translucency, AA/camera feel, soft shadows; SSR/TAA **if** URP was adopted.

Each phase is **preset-gated** and ships with an updated NOTICES/credits note for any new maps, and a
texture-audit pass to author the new normal/height/strand maps (procedural first, AI-gen gated per the
paid-asset rule).

---

## 6. Open questions

1. **URP or stay Built-in RP?** (Phase 0 — gates SSR/TAA/volumetrics cost.)
2. **Map authoring:** procedural-generate normal/height/strand maps from the existing colour tiles (e.g.
   Sobel height→normal) vs AI-gen vs hand. Probably procedural-derive first.
3. **Atlas vs per-block textures:** extend the single atlas with normal+height pages, or move to texture
   arrays (cleaner, needs a mesher change).
4. **Perf budget per preset:** define target FPS + which effects each preset enables, on what reference
   hardware.
5. **Scope vs the father-son audience:** how far toward photoreal before it stops being friendly/readable?
