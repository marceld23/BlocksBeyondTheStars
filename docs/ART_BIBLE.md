# Art Bible — Blocks Beyond the Stars

Status: **normative.** The single style reference for any visual work (blocks, ships, planets, UI,
effects). Written from code truth — constants are quoted with their source so this document stays
verifiable. Related: [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md),
[ADVANCED_GRAPHICS_PLAN.md](ADVANCED_GRAPHICS_PLAN.md), [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md).

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
   (`BlockTextureAtlas.BaseColor`, project rule).
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

## 5. Ship rooms (interior palette)

From the room-identity pass (`GameServerShipStructure.PaintStationAccents` + decor in
`StationDecorView`):

| Room | Floor accent | Light/decor |
|---|---|---|
| Medbay | `medbay_panel` (white/blue, medical cross) | pulsing translucent heal-tank |
| Cockpit | `lab_panel` (cool tech blue) | console + animated screen + holo system map |
| Lab | `lab_panel` | green flickering terminal |
| Ship console | `lab_panel` | cyan flickering terminal |
| Cargo | `cargo_floor` (amber/black hazard stripes) | — |
| Workshop | `engine_panel` (dark + orange corners) | periodic spark bursts |
| Quarters | `metal_panel` (dark, calm) | — |
| Corridor/walls | `iron_wall` + `strip_light_cyan` rows | emissive cyan strips above the windows |
| Engines | `engine_nozzle` (glowing throat ring) | idle glow even when landed |

Ship/tech blocks keep **aligned seams** — they are excluded from the natural-block variant/rotation
system (`BlockTextureAtlas.VariantKeys` whitelist is natural blocks only).

## 6. Light & post

- ACES tonemapping, bloom threshold 0.9 / intensity 0.5, vignette 0.26 base (`UrpScenePost`).
- Emission is the sci-fi signature: lights, strips, lava veins, glow flora, ores, force fields,
  console screens — bloom catches them; **every important tech object carries one glowing detail.**
- Dynamic vignette is reserved for meaning: blue pulse = low oxygen, red kick = damage; chroma/
  grain bursts only for events (damaged visor, EMP) via `UrpScenePost.Burst`.
- Night is never pitch black (0.20 brightness floor); interiors get the `_Sc_Indoor` fill.

## 7. Feedback ("juice") rules

Every player action answers with **sound + motion + light** within ~0.1 s:
mining = crack tint → debris → final-hit flash → tile flies to the hotbar;
crafting/unlock = card pulse + floating label + fanfare; menus fade/rise in 0.14 s;
hotbar selection ticks; hits spark; low resources pulse. New features must wire into these
channels (`MiningFx`, `UrpScenePost`, `UiKit.TransitionIn`, `ClientAudio`/`ProceduralAudio`)
instead of inventing parallel ones.

## 8. Quality checklist (every important object)

- [ ] clear silhouette (readable at a glance, also in the hotbar icon)
- [ ] at least two material zones (e.g. dark housing + bright band)
- [ ] one small glowing detail (emission feeds bloom)
- [ ] a function the design communicates (warning stripes mean hazard, cyan means interface)
- [ ] localized name (DE+EN), icon resolvable via `IconResolver`
- [ ] effects preset-gated / comfort-toggle aware
