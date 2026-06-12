# Professional Look — Gap Analysis

Status: **analysis only — nothing in this document is implemented work.**
Date: 2026-06-12

This document compares the external "professional look" plan (*stylized voxel sci-fi with premium
lighting, readable silhouettes, clean UI, atmospheric planets and modular ship detail*) against the
**current state of the codebase**, and lists everything that is **missing or only partially
implemented**. Items the plan asks for that are already done are summarized briefly so they are not
re-planned.

Related docs: [ADVANCED_GRAPHICS_PLAN.md](ADVANCED_GRAPHICS_PLAN.md) (phases 2–4 overlap with many
gaps below), [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md), [SOUND_DESIGN.md](SOUND_DESIGN.md),
and the M-milestones in [TODO.md](../TODO.md).

---

## 1. Verdict in one paragraph

The plan is **largely implemented** in its foundation layer: URP is active with HDR, soft shadows,
SSAO, a code-built post-processing volume (bloom, ACES, vignette, per-biome color grading, menu DoF),
PBR-style block materials (per-block metallic/smoothness, Sobel-derived normal atlas, emission feeding
bloom), milky glass, a fully dynamic data-driven sky/weather/cloud system with per-planet identity,
a cohesive cyan/dark UI kit with a diegetic visor HUD, bilingual localization, and broad SFX coverage.
What is missing is concentrated in four clusters: **(a) one real config gap (Gamma instead of Linear
color space), (b) "juice"/feedback effects (crafting, blueprints, pickup, cinematic camera moments),
(c) ship interior/cockpit presentation (rooms, displays, holograms, damage states, build preview),
and (d) world detail layers (decals, detail props, LOD, reflection probes, heat distortion).**
One plan assumption is obsolete: the client builds as **Windows Standalone x64**, not UWP
([BuildScript.cs:54](../client/Assets/BlocksBeyondTheStars/Editor/BuildScript.cs#L54)), so the plan's
UWP/Xbox-store warnings do not apply.

---

## 2. Already implemented (do not re-plan)

| Plan area | What exists | Evidence |
|---|---|---|
| URP setup (plan §14 Phase 1.1) | URP active, HDR on, 2-cascade soft shadows (2048), preset-gated shadow distance | `client/Assets/Settings/BlocksBeyondTheStarsURP.asset`, `ClientSettings.cs` |
| Post processing (§5) | Bloom, ACES tonemapping, vignette, color adjustments, menu-blur DoF via runtime URP Volume | `Scripts/UrpScenePost.cs` |
| Ambient occlusion (§4.2) | URP SSAO renderer feature (High preset) + per-face vertex AO + baked edge darkening in atlas tiles | `BlocksBeyondTheStarsURP_Renderer.asset`, `ChunkMesher.cs`, `BlockTextureAtlas.cs` |
| Per-planet color grading (§5.2) | Per-biome tint/saturation/contrast grades blended with system sun color; neutral grade in space | `Scripts/Sky.cs` (`GradeFor`, `SetGrade`) |
| PBR-ish block materials (§6.2) | Per-block gloss/metal, tangent-space normal mapping (Sobel atlas), per-block emission → bloom | `Shaders/BlockAtlas.shader`, `BlockTextureAtlas.cs`, `ChunkMesher.cs` |
| Emissive blocks (§12.1/B) | 19+ emissive types: lights, lava veins, glow flora, ore sheen, force fields, data consoles | `ChunkMesher.BlockEmission()`, `data/blocks.json` |
| Milky glass (project rule) | Frosted pale-blue glass, deliberately not clear | `BlockAtlasTransparent.shader`, `BlockTextureAtlas.cs` |
| Surface variation (§7.1, partial) | Per-pixel grain, procedural block decoration (rivets, ore speckles, lava cracks), flora hue per planet | `BlockTextureAtlas.PaintTile()`, `_Sc_FloraTint` path |
| Sky & atmosphere (§8) | Dynamic procedural sky, 1500-star twinkling starfield, sun disc, **other system bodies visible in the sky**, day/night with local-time terminator, distance + storm fog, 18-billboard cloud layer with per-planet color/density | `Sky.cs`, `Starfield.cs`, `SkyBodiesView.cs`, `Clouds.cs` |
| Weather (§14 Phase 2.8) | Snow/hail/ash/sandstorm/rain (3D particles + overlay), lightning, storm fog, per-biome ambient beds | `WeatherFx.cs`, `WeatherFx3D.cs`, `ClientAudio.cs` |
| Space & orbit (§14 Phase 3.3) | Data-driven orbit planets with atmosphere shells, seamless surface↔orbit transitions, voxel ships rendered 1:1 in flight, hyperspace warp overlay | `SpaceView.cs`, `PlanetOrbitLook.cs`, `ShipTransitView.cs`, `HyperspaceWarp.cs` |
| UI design system (§10.1) | Central UiKit (cyan/dark navy palette, rounded holo panels, themed buttons, DPI scaling), code-built, no prefabs | `Scripts/UiKit.cs` |
| HUD (§10.2) | Health/O₂/energy/hunger (+hull/shield aboard), hotbar, compass radar, mission prompt, scan/wreck panels, damage flash with cause label; contextual hiding | `Scripts/HudUi.cs`, `SpaceRadar.cs` |
| Diegetic UI (§10.3, partial) | Visor pipeline: HUD rendered through curved visor shader (fresnel rim, scanlines), VEGA AI companion panel, menus framed as "Ship Interface" | `VisorHud.cs`, `Visor.shader`, `VegaPanel.cs` |
| Localization | Full DE+EN with central Localizer and DE-length-aware text sizing | `src/.../Localization/`, `StreamingAssets/data/locales/` |
| Doors (§12 partial) | Animated hinge/slide doors with trim glow, passable energy-field door, open/close SFX | `DoorView.cs` |
| Mining feedback (§12.2, most) | Wireframe outline, crack-progress tint, debris burst, drill sparks, 4 per-material mining sounds | `MiningFx.cs`, `ClientAudio.cs` |
| Camera feel (§11.1, most) | Head bob, +4° FOV kick while moving, landing shake scaled by impact, zero-g inertia | `PlayerController.cs` |
| Audio (§12) | 142 bundled ElevenLabs SFX + ~30 procedural fallbacks, audio manager with buses, 3D spatialization, underwater low-pass, cave reverb | `ClientAudio.cs`, `Resources/audio/` |

Intentionally **not applicable** (no action needed): baked GI/lightmapping and Light Probes (the
world is procedural voxel geometry with a real-time day/night cycle — flat ambient from the sky is
the by-design replacement), and Shader Graph (the project deliberately uses hand-written
dual-pipeline `.shader` files; see the constraints section of ADVANCED_GRAPHICS_PLAN.md).

---

## 3. Gaps — missing or partial

Ordered by theme; each item names the plan section it comes from.

### 3.1 Rendering / configuration

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G1 | **Linear color space** | §14 Phase 1.2 | **MISSING** | `client/ProjectSettings/ProjectSettings.asset` has `m_ActiveColorSpace: 0` (Gamma). This is the plan's #2 "instant improvement" and the only outright config gap in Phase 1. Caveat: all procedural colors and AI textures were authored/tuned under Gamma — switching requires a visual re-tuning pass (sky, grades, emission strengths), not just the toggle. |
| G2 | **Reflection probes** | §2, §13 | **MISSING** | No probes anywhere; metal/glass reflect only a fresnel sky-color approximation in `BlockAtlas.shader`. Interiors (ship, station) are where probes would pay off. Already listed as ADVANCED_GRAPHICS_PLAN Phase 4/J. |
| G3 | **LOD / distant-terrain strategy** | §2 (LOD) | **MISSING** | No LOD, no greedy meshing (`ChunkMesher.cs:13` notes it as future work). All chunks render full-detail. A perf gap more than a look gap, but it caps draw distance and therefore vista quality. |
| G4 | Film grain / chromatic aberration as *event* effects (damaged visor, EMP, wreck) | §5.5 | **MISSING** (effects exist in the volume at intensity 0) | Plumbing is there in `UrpScenePost.cs`; nothing ever triggers them. Low effort, situational payoff. |
| G5 | DoF beyond menu blur (screenshot mode, dialogs) | §5.4 | **PARTIAL** | DoF only fires on `MenuOpen`. No screenshot/photo mode exists at all. |

#### G1 deep dive: Linear color space vs. the tinting features

Both signature tinting features — the per-planet random flora hue and the sun-color propagation
(red star → red-tinted sky/light/grade) — are **architecturally compatible** with Linear color
space: they are hue multiplications, and hue identity survives the switch. The work splits into:

1. **Script-set colors are not auto-converted.** Unity only converts inspector-serialized colors;
   `Shader.SetGlobalColor` / `VolumeParameter.Override` pass values raw. This project routes its
   *entire* lighting through such globals (`_Sc_Light`, `_Sc_Sky`, `_Sc_FloraTint`, `_Sc_GradeTint`,
   `_Sc_LampColor` — `Sky.cs:163/185/221/263`, `PlayerController.cs:464`, `UrpScenePost.cs:91`).
   All these values are sRGB-authored; after the switch they read as linear → paler, washed-out
   tints (a red sun stays red but visibly weaker). **Fix:** keep all C# color composition in sRGB
   as today and convert once at the shader boundary (`color.linear`, ~12–15 call sites). This
   preserves per-planet hue determinism and the sun-color behavior 1:1.
2. **Multiplies move from gamma to linear space.** `albedo × light × grade` and the flora
   desaturate-and-retint (85% blend) become perceptually weaker at the same constants. Strength
   constants need a visual re-tune: flora blend 0.85, grade strength 0.7, sun-hue folds 0.4/0.5
   (`Sky.cs:215/261`), night floor 0.20, emission ×2, bloom threshold 0.9, vertex AO 0.88, atlas
   edge darkening 0.65.
3. **Normal atlas must be flagged linear.** `BlockTextureAtlas.cs:55` creates `NormalTexture`
   without `linear: true`; under Linear color space the hardware would sRGB-decode normal data →
   broken lighting on all blocks. The color atlas and `.bytes` textures are fine with the sRGB
   default (they are sRGB-authored); vertex-color material data (gloss/metal/AO/emission) is never
   converted and is unaffected.

Net: a small mechanical conversion pass + a contained visual re-tune; no feature redesign. The
custom-globals architecture actually limits the blast radius — there are no hidden inspector colors
behaving differently from script colors.

### 3.2 Blocks & world detail layers

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G6 | **Decal layer** (dirt, scratches, logos, moss, scorch marks, impact craters) | §6, §7.2, §9.2 | **MISSING** | No decal system of any kind (URP Decal Projector unused). Also blocks the "used/worn ship" look (§9.2 Gebrauchsspuren) and weapon scorch feedback. ADVANCED_GRAPHICS_PLAN item I. |
| G7 | **Detail props / scatter** (pebbles, grass tufts, small rocks on surfaces) | §7.2 | **MISSING** | Only cross-quad flora billboards exist. No instanced scatter (`DrawMeshInstanced` etc.). ADVANCED_GRAPHICS_PLAN item E + shells (Phase 2). |
| G8 | **Multiple texture variants per block type + random rotation** | §7.1 | **PARTIAL** | Variation today = per-pixel grain + procedural decoration + flora tint. There is exactly **one tile per block type**, no A/B/C variants ("stone cracked / darker / with veins"), no random face rotation for natural blocks. Atlas has free capacity (16×16 = 256 tiles, 99 used). |
| G9 | Biome blending / transitions (snow caps on top faces, sand dusting at rock edges, side gradients on grass) | §7.2 | **MISSING** | Hard block transitions everywhere; no top-face overlays or edge blending. |
| G10 | Trim-sheet–style ship block faces: light strips, warning stripes, vents as dedicated blocks | §9.2/§6.1 | **PARTIAL** | Panel textures have rivets/seams (AI-generated), but there are no warning-marking blocks, no wall light-strip blocks, no hazard-stripe variants — the "Schiffsmaterialien" palette of the plan is roughly half present (4 hull blocks + lights vs. the listed ~8 categories). |
| G11 | Heat shimmer / screen distortion (lava planets, engines) | §8.1, §12.1 | **MISSING** | Lava worlds get color grade + emissive cracks + audio only; no distortion pass anywhere. ADVANCED_GRAPHICS_PLAN item D (lava heat haze). |
| G12 | Dynamic light emission from glowing flora/crystals (actual light sources, not just emissive texture) | §8.1 | **PARTIAL** | Emissive textures + bloom read as glow, but glow flora doesn't illuminate surroundings at night. (Real point lights per block are costly — a cheap "block light" term in the shader would be the fitting approach.) |

### 3.3 Ship — the weakest area relative to the plan

The plan calls the ship "the center, must look better than normal blocks" (§9). The exterior and
the voxel-design pipeline are in good shape; **interior presentation and feedback are the gap**.

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G13 | **Visually distinct ship rooms** (medbay white/blue, engine room orange/dark, cargo yellow markings…) | §9.1, §9.3 | **PARTIAL** | All 7 station types exist functionally (`GameServerShipStructure.cs:186-192`) but are marked by a single floor block each (ice/stone/carbon/data_cache); walls, lighting and floor are identical in every room. No room color zones, no floor markings, no per-room light color. |
| G14 | **Cockpit experience** (displays, star-map hologram, viewport framing, seat) | §9.3, §10.3 | **MISSING (≈20%)** | Cockpit = an emissive marker block that opens the flight view. No console geometry, no animated displays, no holographic star map, no flight-instrument HUD in space view beyond the radar. This is the plan's flagship diegetic-UI example. |
| G15 | Animated machines/terminals (displays flicker, machinery moves, medbay tank hums visually) | §9.3, §12 | **MISSING** | Doors are the only animated entities. Lab/workshop/medbay/engine room have no visual machinery at all. |
| G16 | Interior light strips / cable runs / floor markings; emergency lighting state | §9.3, §4.1 | **PARTIAL** | One emissive ceiling row + a constant indoor fill term (`_Sc_Indoor`). No wall/floor light strips, no per-room light mood, no red emergency/damage lighting. |
| G17 | **Ship damage visuals** (sparks, smoke, hull cracks, emergency light) | §12.1, §9 | **MISSING (0%)** | `ShipState.Hull` is tracked server-side but never visualized. Spark/debris particle helpers already exist (`WeaponFx.cs`) and could be reused. |
| G18 | **Ship-build placement preview**: green/red validity hologram, materialize animation, completion sound | §12.5 | **PARTIAL (≈30%)** | `ShipEditor.cs` places colored blocks directly; no hologram ghost, no valid/invalid tinting, no block-by-block materialization when a design is stamped into the world. |
| G19 | Thruster particles + landed-ship engine glow + launch dust | §12.1 | **PARTIAL** | Flight view has a stretching glow sprite + engine point light + sound; no exhaust particles, no heat distortion, landed ships are fully dark (nozzle blocks "styled as off"), no dust kicked up on launch/landing (a dust helper exists in `WeaponFx.cs:97-105` but is not wired to transit). |

### 3.4 Feedback & "juice"

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G20 | **Crafting feedback**: progress ring, montage/hologram animation, workbench glow | §12.3 | **PARTIAL** | Today: procedural "ding" + text line in the menu. No progress indication, no visual flourish, no item hologram. |
| G21 | **Blueprint/tech-tree unlock celebration**: node glow, data-fragment flow, new-line reveal | §12.4 | **MISSING (visual)** | Unlock is silent apart from the same "ding". The tech tree UI has no unlock animation states. |
| G22 | **Resource pickup fly-to-inventory** + final-hit flash on block break | §12.2 | **MISSING** | Mining is otherwise complete; these two are the missing "last 10%" of the loop. |
| G23 | Low-oxygen warning (pulsing vignette/tint + audio cue) | §5.3, §10.2 | **MISSING** | O₂ bar exists; no escalation effect. Vignette is static (0.26) and never pulses. Noted as open in SOUND_DESIGN.md too. |
| G24 | **Cinematic camera moments** (first launch, landing, hyperjump, module built, mission complete) | §11.2 | **PARTIAL** | Launch/landing/hyperspace exist as state machines + full-screen overlays, but there is **no scripted camera work at all** (no Cinemachine, no dolly/orbit shots). Mission-complete and module-built have no moment whatsoever. |
| G25 | Head bob not toggleable | §11.1 | **PARTIAL** | Bob/FOV-kick/shake are hardcoded; the plan explicitly wants bob "abschaltbar" (accessibility). `ClientSettings` already has a pattern for toggles. |
| G26 | Item drop/pickup world visuals (no floating item, no magnet pull) | §12.2 | **MISSING** | Loot goes straight to inventory; "Loot" prompt only. |

### 3.5 UI / presentation polish

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G27 | **Futuristic font** | §10.1 | **PARTIAL** | UI uses legacy `LegacyRuntime.ttf`/Arial via dynamic OS fallback (`UiKit.cs:51-54`); no TextMeshPro, no custom font asset. The concept doc itself names Orbitron/Exo-class fonts. Must cover DE umlauts + ß. |
| G28 | UI transition animations (panel fade/slide, hotbar selection animation) | §10, §12 | **PARTIAL** | Buttons have hover/press states + sounds; everything else snaps instantly (menu open/close, tab switch, panel show/hide). |
| G29 | Music contexts (menu / planet / space / combat crossfade) | §12 | **PARTIAL** | One procedural ambient loop (`ClientMusic.cs`); no context switching. ElevenLabs music batch is specced in SOUND_DESIGN.md but not generated. |
| G30 | 3D-rendered item/block preview icons | §13.3 | **PARTIAL** | Icon chain = PNG → atlas tile → procedural glyph; fine, but no rendered 3D previews (Minecraft-style) for held items/blocks. Low priority. |
| G31 | Meteors / distant ships in the sky | §8.2 | **MISSING** | Pure ambience garnish; sky is otherwise complete. Low priority. |

### 3.6 Process / documentation

| # | Gap | Plan ref | Status | Details |
|---|---|---|---|---|
| G32 | **Art Bible** (style rules, named color palette, per-object quality rule) | §13 | **PARTIAL** | The rules exist *de facto* scattered across UI_AND_RENDER_CONCEPT.md, ADVANCED_GRAPHICS_PLAN.md, UiKit constants and memory notes (milky glass, bilingual text). There is no single normative art document with the named palette (Space Black, UI Cyan, Warning Orange, …) and the "every important object needs silhouette + 2 material zones + glow detail + icon" checklist. Cheap to write, prevents drift. |

---

## 4. Suggested priority (if/when implementation is requested)

**Quick wins (small, isolated, high perceived value)**
1. G23 low-O₂ warning (vignette pulse + procedural tone) — closes a gameplay-readability hole.
2. G22 final-hit flash + pickup fly-to-inventory — completes the most-used loop.
3. G25 head-bob/shake toggle in settings.
4. G20/G21 crafting + blueprint celebration (glow, progress ring, popup) — pure UiKit work.
5. G19 (part) wire the existing dust burst + engine glow into launch/landing; idle nozzle glow on landed ships.
6. G32 write the Art Bible from what already exists.

**Medium (one milestone each)**
7. G13+G16 room identity pass: per-room wall/floor accent blocks + per-room light color + light-strip blocks (also shrinks G10).
8. G14 cockpit pass: console blocks with animated emissive displays, simple holographic star map, flight instruments in space HUD.
9. G18 build-preview hologram (green/red ghost) + block-by-block materialize on stamp.
10. G27 TMP migration + bilingual sci-fi font.
11. G28 UI transitions; G29 music contexts (generate the specced ElevenLabs batch).
12. G17 ship damage visuals (reuse WeaponFx sparks + emergency light state).
13. G8 block texture variants + random rotation (atlas has 157 free tiles).

**Large / strategic (already tracked in ADVANCED_GRAPHICS_PLAN phases 2–4)**
14. G1 Linear color space — schedule as its own visual-retune milestone, not a drive-by toggle.
15. G6 decals, G7 detail scatter + shells, G11 heat distortion, G2 reflection probes, G24 cinematic camera system, G3 LOD/greedy meshing, G9 biome blending.

---

## 5. Plan items to drop or amend

- **UWP guidance (plan intro):** obsolete — the client targets Windows Standalone x64; no UWP config
  exists. Keep URP (done) and ignore the UWP/Xbox-store warnings.
- **Light Probes / baked GI (plan §2 table):** not applicable to a procedural voxel world with a
  dynamic day/night cycle; the flat sky ambient + indoor fill term is the correct substitute. Only
  reflection *probes* (G2) remain worth considering, for interiors.
- **Shader Graph (plan §2 table):** the project's deliberate constraint is hand-written dual-pipeline
  shaders built in code; do not introduce Shader Graph.
- **ADVANCED_GRAPHICS_PLAN.md header is stale:** it still says "Built-in RP, no URP today" while
  Phase 0 (URP) has since shipped — worth a small doc update when next touched.
