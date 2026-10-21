# Art Bible — Blocks Beyond the Stars

Status: **normative.** The single style reference for any visual work (blocks, ships, planets, UI,
effects). Written from code truth — constants are quoted with their source so this document stays
verifiable. Related: [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md),
[ADVANCED_GRAPHICS.md](ADVANCED_GRAPHICS.md), [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md).

## 1. Art direction in one sentence

> **A colorful block-based space exploration game with modular starships, readable voxel worlds,
> cinematic sci-fi lighting, glowing technology, atmospheric planets and clean holographic UI.**

Stylized voxel sci-fi: **blocky stays, cheap doesn't.** Readable and friendly over photoreal
(father-son audience); "schick" through light, materials, atmosphere and feedback — never through
geometric detail.

## 2. Hard style rules

1. **Block-based forms.** Large shapes are voxels; detail comes from texture (seams, rivets,
   grain), per-block PBR params (gloss/metal/emission) and the Sobel-derived normal atlas — not
   from extra geometry.
2. **No photorealism, no cheap pixel look.** 64×64 tiles, point-filtered, with per-pixel grain and
   darkened tile edges (`BlockTextureAtlas.PaintTile`).
3. **Glass is milky/frosted, never clear** — base `(0.82, 0.91, 0.95)`, you can tell it's glass
   (`BlockTextureAtlas.BaseColor`, project rule). The one deliberate exception is the rarer, blueprint-gated
   **`glass_clear`** (#1274) for cockpit canopies and observation domes: a faint look-through pane the
   transparent shader picks out by a `-1` in `TEXCOORD2.x`. Plain `glass` stays frosted.
4. **Everything is code-built.** No scene authoring, no prefabs, no Shader Graph; hand-written
   dual-pipeline shaders, force-included in GraphicsSettings.
5. **Linear color space** with sRGB-authored constants converted at the script→shader boundary via
   `ShaderColor.Srgb()` — never upload an authored colour raw, never wrap engine-managed colour
   properties (Light, RenderSettings, camera background, uGUI, URP colorFilter).
6. **Bilingual** (DE+EN) for every player-facing string; avionics abbreviations (SPD/THR/HDG) are
   the one sanctioned exception.
7. **Preset-gated effects.** Anything expensive scales over Potato/Low/Medium/High; comfort
   effects honour `ReducedEffects` (and `UiKit.ReducedMotion`), camera motion honours
   `CameraMotion`.
8. **Bodies are jointed cubes.** Anything alive — the player avatar, remote players, NPCs, creatures
   — is built from cuboids on pivots and animated procedurally: limbs bend at a knee/elbow, feet are
   flat soles that stay level, soft parts (tails, tentacles, necks, trunks) are chains that lag the
   body. No skinned meshes, no imported clips, no Mecanim. Joints are cubes too — a sphere joint
   would read as a different material language. See [CREATURE_RIG.md](CREATURE_RIG.md).

## 3. Color palette

UI system (`UiKit.cs`):

| Name | Value | Use |
|---|---|---|
| UI Cyan | `(0.40, 0.82, 1.00)` | primary accent, borders, highlights |
| Deep Navy panel | `(0.05, 0.12, 0.24, 0.80)` | translucent panel fill |
| Soft White text | `(0.86, 0.93, 1.00)` | body text |
| Success green | `UiKit.Ok` | craft ready, confirmations |
| Health red / Oxygen cyan / Energy amber / Hunger orange | vitals bars | semantic, never reused decoratively |
| Warning Orange | `(1, ~0.6, 0.2)` family | sparks, hazards, engine glow |
| Hazard Red | `(1, 0.12–0.2, 0.08–0.2)` | damage flash, emergency light |

World/tech accents: Engine Blue `(0.5–0.65, 0.85, 1)` (exhaust, energy doors, hatch beacon),
Alien Green (lab screens `(0.35, 1, 0.6)`), Crystal Purple `(0.8, 0.7, 1)` (crystal worlds),
Space Black `(0.01, 0.01, 0.03)` (airless sky).

## 4. Planet identity (data-driven)

Every planet type owns a mood — sky + fog follow the system **sun colour** (a red star tints its
worlds), the grade comes from `Sky.GradeFor` (tint × saturation × contrast), clouds from
`planets.json` (`cloudColor`, `cloudDensity`), flora from the per-world random hue
(`FloraTints`, deterministic from seed + location + species):

| Biome | Grade tint | Sat | Contrast | Mood |
|---|---|---|---|---|
| jungle/forest | (0.98, 1.05, 0.96) | 1.12 | 1.05 | lush |
| desert | (1.07, 1.00, 0.90) | 0.95 | 1.12 | warm, dusty |
| ice/frozen | (0.94, 1.00, 1.09) | 0.90 | 1.06 | cold blue |
| lava/volcanic | (1.10, 0.95, 0.86) | 1.05 | 1.14 | hot orange |
| swamp | (0.97, 1.03, 0.95) | 0.85 | 1.03 | muted sickly |
| crystal | (1.04, 0.97, 1.09) | 1.10 | 1.05 | cool sparkle |

New planet types: add data (`planets.json`) + a `GradeFor` entry; never hardcode looks client-side.

**Flora looks and accents (#2832, #2834, 2026-10-19).** A planet's plants differ in shape too, not only in hue:

- **Looks.** An archetype may list extra tiles of the same plant in `data/blocks.json` (`"looks"`: `<block>_look<n>`).
  Every planet wears one of them per archetype (`FloraLooks`, from seed + location + block, like the colour). The
  client repaints the block's own atlas slot on a world change (`BlockTextureAtlas.SetLooks`), so no id, save or mesh
  changes; a texture pack or a world texture of the block itself wins over the look.
- **Accents.** The flora shader keeps a tile's luminance and swaps its hue for the species colour, which made every plant
  one colour. A block with `"accent": true` gives its blossoms, berries and caps a colour of their own:
  `FloraAccent.hlsl` finds the accent texels in the tile (bright, saturated, not green) and colours them from the
  species tint turned by a species-dependent hue. Each plant wears one of three related accents
  (`ChunkMesher.AccentCodeAt`, carried as the fraction of the cutout code in TEXCOORD2.x), so a meadow of one species
  is mixed.
- **Art rules for new flora tiles.** Billboards are one side-view plant on pure black (`ON_BLACK` in
  `gen_textures.py`), cut by the dark-ground bake and rooted on the bottom edge. Crowns are dense canopies edge to edge.
  Leaves are drawn green (the planet recolours them). Blossoms and berries are bright and saturated, so the accent
  finds them.
- **Tree slots and the leaf pool (#2851, #2855, 2026-10-20).** From generation 29 a planet's tree kinds are built from
  three leaf slots (`tree_leaves`, `tree_leaves_b`, `tree_leaves_c`) that share ONE pool of crown tiles (24 leaf shapes +
  the two slot tiles); `FloraLooks.TextureKeysForGroup` gives the three slots three different tiles on every world, and
  `FloraTints` gives each its own colour. The blossom crown (`blossom_leaves`) keeps its painted colours (the accent
  shader turned blossoms green on some worlds). Trunks have looks too (`wood_log` five barks, the pale `wood_log_b`
  three): bark tiles are opaque, drawn in natural browns / whites (the bark tint keeps their luminance), and the bake
  leaves them alone. The client varies leaf and bark tints gently per grove (a low-frequency field), so one wood is not
  a set of copies.

## 5. Ship rooms (interior palette)

From the room-identity pass (`GameServerSpaceStructure.PaintStationAccents` + decor in
`StationDecorView`):

| Room | Floor accent | Light/decor |
|---|---|---|
| Medbay | `medbay_panel` (white/blue, medical cross) | pulsing translucent heal-tank |
| Cockpit | `lab_panel` (cool tech blue) | console + animated screen + holo system map |
| Lab | `lab_panel` | green flickering terminal |
| Ship console | `lab_panel` | cyan flickering terminal |
| Cargo | `cargo_floor` (amber/black hazard stripes) | — |
| Workshop | `engine_panel` (dark + orange corners) | periodic spark bursts |
| Quarters | `metal_panel` (dark, calm) | warm-white ceiling lamp |
| Corridor/walls | `iron_wall` + `strip_light_cyan` rows | emissive cyan strips above the windows |
| Engines | `engine_nozzle` (glowing throat ring) | idle glow even when landed |

Ceiling lamps (`PlaceInteriorLights`, #776/#2816): the rooms a crew **lives** in — quarters, medbay, workshop,
cargo — get a `light_white` dyed warm white (`FFD8A8`), the rooms it **works** in — cockpit, console — keep the plain
cool `light_white`. Warm light belongs to home and rest, cyan to interface and flight. Electric light glows steadily;
only flame-coloured light (torch, lantern, campfire) breathes.

Ship/tech blocks keep **aligned seams** — they are excluded from the natural-block variant/rotation
system (`BlockTextureAtlas.VariantKeys` whitelist is natural blocks only).

## 6. Light & post

- ACES tonemapping (Neutral on an LDR buffer — WebGL Low and Potato), bloom threshold 0.9 / intensity 0.5,
  vignette 0.18 base (`UrpScenePost`; was 0.26 until #1457). Contrast: the neutral world sits at +3 on top of the
  biome contrast (#2803 — until then a hidden +6 kept the world at 9–20).
- Emission is the sci-fi signature: lights, strips, lava veins, glow flora, ores, force fields,
  console screens — bloom catches them; **every important tech object carries one glowing detail.** Glowing
  plants and mushrooms breathe slowly (one breath every ~6 s, #2822); crystals, ores and machines stay steady.
- Authored light colours are sRGB like every other colour: the block light is converted to linear with its
  brightness kept (`ChunkMesher.DisplayLight`, #2804), so a torch lights amber and the cyan strip cyan.
- Dynamic vignette is reserved for meaning: blue pulse = low oxygen, red kick = damage, a soft close-in = resting
  in bed; chroma/grain bursts only for events (damaged visor, EMP) via `UrpScenePost.Burst`.
- Night is never pitch black (0.35 brightness floor since PR #65); interiors get the `_Sc_Indoor` fill.
- Effects never spawn Unity `Light`s (URP additional lights are off): a flash that should light the room goes through
  the shader-global **FX lights** (`FxLights`, max 8, preset-scaled), an existing light through `FxLightBridge`.
- Effect glow is HDR colour + bloom, but every effect must still read on Low/Potato, where the buffer is LDR: bloom
  runs on every preset, but on an LDR buffer only near-white pixels cross its threshold — so a bright core plus a soft
  additive halo, never bloom alone. Large full-screen flashes honour **Reduce flashes**; camera motion honours
  **Screen shake** + **Camera motion** ([VFX.md](VFX.md)).

## 6a. Cosiness and home (#2802)

Cosy comes from safety and softness, not from painting the world orange:

- **The home stands out.** Aboard your ship — or under a roof inside your own base's air — the grade blends over
  ~1.5 s into a warm **home grade** (`UrpScenePost`, `Sky` → `SetCozyContext`): the planet's tint, saturation and
  contrast fade out, the white balance warms, shadows lift a little and lose their cool lean, the biome mood LUT
  drops to a quarter. The cabin fill (`_Sc_IndoorColor`) is warm white by day and amber in the evening (the
  planet's local time when landed, the player's real clock in space). The ice world's chill and the lava world's
  orange come back as the hatch opens.
- **One slider, scaled by place.** *Cosiness* (0–100 %, default 50 %) softens contrast and lifts shadows first, then
  warms, then adds a little glow — multiplied by how cosy the place may get: home 1, a lit camp or village 0.8,
  gentle worlds 0.6, harsh worlds 0.3–0.4, storms half, and nothing while a hostile is close or Valuma watches.
  Biomes keep their identity because the outdoor share stays small.
- **Never darker.** The warmer white balance is exposure-compensated; the one deliberate dimming is resting in bed.
- **Not on the slider:** vignette (reserved for meaning), film grain, chromatic aberration, lens distortion, depth of
  field while exploring.

## 7. Feedback ("juice") rules

Every player action answers with **sound + motion + light** within ~0.1 s:
mining = crack tint → debris → final-hit flash → tile flies to the hotbar;
crafting/unlock = card pulse + floating label + fanfare; menus fade/rise in 0.14 s;
hotbar selection ticks; hits spark; low resources pulse. New features must wire into these
channels (`MiningFx`, `UrpScenePost`, `UiKit.TransitionIn`, `ClientAudio`/`ProceduralAudio`)
instead of inventing parallel ones. Effects are built from **`FxKit`** (shared emitters, cached materials, pooled
beams/rings/shells/holograms, density scaling) — never a per-spawn GameObject/Material — and a device's look comes
from its data-driven `fx` object (`data/items.json`, `data/ship_modules.json`); see [VFX.md](VFX.md). Defeat stays
kid-friendly: creatures break apart into sparkles, robots into parts, bandits beam away.

## 8. Quality checklist (every important object)

- [ ] clear silhouette (readable at a glance, also in the hotbar icon)
- [ ] at least two material zones (e.g. dark housing + bright band)
- [ ] one small glowing detail (emission feeds bloom)
- [ ] a function the design communicates (warning stripes mean hazard, cyan means interface)
- [ ] localized name (DE+EN), icon resolvable via `IconResolver`
- [ ] effects preset-gated / comfort-toggle aware (`FxKit.Scaled`, `FlashScale`, `FxCamera`)
- [ ] a tool/weapon/module carries an `fx` look with a known style
