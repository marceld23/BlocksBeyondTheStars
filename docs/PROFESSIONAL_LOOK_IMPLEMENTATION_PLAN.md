# Professional Look — Implementation Plan

Status: **planned — not started.** Source: [PROFESSIONAL_LOOK_GAP_ANALYSIS.md](PROFESSIONAL_LOOK_GAP_ANALYSIS.md)
(gap IDs G1–G32 referenced below). Date: 2026-06-12.

This plan covers the **quick-win and medium** items from the gap analysis (its §4, priorities 1–13),
ordered so the whole plan can be worked through **in one continuous run**. Visual playtests are
consolidated into exactly **two checkpoints** (PT-1, PT-2); everything else is verified by builds,
tests and code review.

**Out of scope for this run** (large/strategic, tracked in
[ADVANCED_GRAPHICS_PLAN.md](ADVANCED_GRAPHICS_PLAN.md) phases 2–4): decal system (G6), detail
scatter/shells (G7), biome blending (G9), heat distortion (G11), flora light emission (G12),
LOD/greedy meshing (G3), reflection probes (G2), cinematic camera system (G24), 3D item icons
(G30), sky meteors (G31), screenshot mode (G5).

---

## Global working rules (apply to every work package)

- **Read before you write.** Every WP lists "Read first" files. Read them fully before changing
  anything — the codebase has established patterns (code-built UI, code-built particles,
  entity-style visuals like `DoorView`, dual-pipeline shaders) and every WP must reuse them, not
  invent parallel ones. If a WP touches an area, also grep for other call sites of the symbols you
  change so nothing is missed.
- **Localization:** every new player-facing string gets a key in **both**
  `client/Assets/StreamingAssets/data/locales/en.json` **and** `de.json`, resolved via the
  Localizer. No hardcoded UI text.
- **Shaders:** any new or renamed `.shader` must be in GraphicsSettings Always-Included
  (`BuildScript.EnsureShadersIncluded`) or it strips from the player build.
- **Networking:** any new message class must be `Register()`'d in `NetCodec`, or sending fails
  silently.
- **Blocks:** new block types go in `data/blocks.json` + atlas tile (capacity 16×16 = 256, ~99
  used). Server `SetBlock` while players are connected must broadcast `BlockChanged`.
- **Build verification:** batch build via `BuildScript`; confirm `BlocksBeyondTheStars.Client.dll`
  timestamp (not the .exe). Run `dotnet test` whenever `src/` (shared/server) is touched.
- **Docs:** update `TODO.md` per commit (single status doc). One conventional commit per WP.
- **Asset generation:** OpenAI textures + ElevenLabs sounds are blanket-approved; pipelines in
  `tools/ai-assets/` (run via `uv`).

---

## Phase A — Rendering foundation

### WP-1 — Linear color space migration (G1)

**Goal:** the project renders in Linear color space with the same perceived look as today: flora
hues per planet, sun-color propagation (red star → red world), biome grades, emission/bloom and UI
all retain their current character.

**Read first (mandatory, this WP has the widest blast radius):**
- `client/Assets/BlocksBeyondTheStars/Scripts/Sky.cs` — every `Shader.SetGlobalColor/SetGlobalVector`
  site (`_Sc_Light`, `_Sc_Sky`, `_Sc_FloraTint`, `_Sc_GradeTint`, lamp-off writes, sun disc tint)
  and all color composition (lerps, hue folds at lines ~207–265).
- `client/Assets/BlocksBeyondTheStars/Scripts/UrpScenePost.cs` — `ApplyGrade`, bloom threshold,
  exposure.
- `client/Assets/BlocksBeyondTheStars/Scripts/PlayerController.cs` (~462–469) — headlamp HDR color.
- `client/Assets/BlocksBeyondTheStars/Scripts/BlockTextureAtlas.cs` — both `Texture2D` constructors
  (color atlas + `NormalTexture`) and every baked constant (edge darkening 0.65, grain range).
- **Grep the whole client for** `new Texture2D(` and `SetGlobalColor|SetGlobalVector|material.color|
  material.SetColor` — classify each hit: *color data* (needs `.linear` or sRGB texture flag) vs
  *non-color data* (normals, masks, radar/minimap → needs `linear: true`). The grep list includes
  Clouds, Starfield, MenuBackground, SpaceView, StationBackdrop, CreatureBuilder, PlayerAvatar,
  WeaponFx, MiningFx, DoorView, UiKit, IconFactory, WorldMap/WorldMinimap, MaterialEditor.
- All `client/Assets/BlocksBeyondTheStars/Shaders/*.shader` — find math that assumes gamma-space
  inputs (emission ×2, faceAo lerp 0.88, fresnel, fog) so the retune list is complete.
- `client/Assets/BlocksBeyondTheStars/Scripts/PostFx.cs` — Built-in-RP fallback path: decide and
  document whether it stays gamma-only (URP is the shipping path) or is converted too.

**Do:**
1. Add a single conversion helper (e.g. `ColorSpaceUtil.ToShader(Color)` returning `c.linear`) and
   apply it at **every script→shader color boundary** found above. Keep all C# color *composition*
   in sRGB exactly as today — convert once at the boundary, so per-planet hues and sun-color
   behavior stay 1:1.
2. Create `NormalTexture` (and every other non-color data texture found in the grep) with
   `linear: true`.
3. Flip `m_ActiveColorSpace` to 1 in `client/ProjectSettings/ProjectSettings.asset`.
4. Retune in-shader/strength constants against the pre-change look: flora retint blend (0.85),
   grade strength (0.7) and sun-hue folds (0.4/0.5), night brightness floor (0.20), emission
   multiplier (×2), bloom threshold/intensity (0.9/0.5), vertex AO (0.88), atlas edge darkening
   (0.65), UI panel alphas in `UiKit` if translucency visibly shifts.
5. Capture before/after screenshots of: day desert, night with glow flora, lava biome, ice biome,
   red-sun system, space view, ship interior, main menu.

**Done when:** batch build green; **PT-1 playtest checkpoint** (the one unavoidable visual pass of
Phase A): the eight screenshot scenarios above look equivalent-or-better than before; flora hue
differs between two worlds as before; a red-sun system visibly tints sky/light/grade.

### WP-2 — Sci-fi font (G27)

**Goal:** all UI text renders in a bundled futuristic-but-readable font (OFL-licensed, e.g.
Orbitron/Exo class) with full DE glyph coverage (äöüÄÖÜß); OS-font fallback kept.

**Read first:** `UiKit.cs` (~51–54, the single `Font` resolution point + every
`resizeTextForBestFit` usage), `NOTICES.md` (attribution format), one consumer each of
`UiKit.Font` in HudUi/UiMainMenu to confirm nothing caches the font elsewhere (grep `UiKit.Font`
and `.font =`).

**Do:** bundle the TTF under `client/Assets` (or Resources), load it in `UiKit.Font` with the
existing fallback chain, verify umlauts/ß render, adjust min/max best-fit sizes if metrics differ,
add the license to `NOTICES.md`. **No TMP migration** in this run (UiKit centralizes the font; TMP
stays a future option).

**Done when:** build green; a German and an English settings screen render without missing glyphs
or clipped labels (verified at PT-2).

### WP-3 — Camera-feel settings toggles (G25)

**Goal:** head bob, FOV kick and camera shake are individually controlled by a single "camera
motion" settings toggle (accessibility), default on.

**Read first:** `PlayerController.cs` (~1030–1060 bob/FOV/shake), `ClientSettings.cs` (existing
toggle pattern + persistence), `UiSettings.cs` (how toggles are built + localized), locale files.

**Do:** add setting + UI row + DE/EN keys; gate bob amplitude, FOV offset and shake amplitude on it
(zero when off; landing-impact *sound* unaffected).

**Done when:** build green; setting persists across restart (verify by reading the settings file);
code-review confirms all three motion sources honor the flag.

### WP-4 — Event post-effects: low-O₂ warning + damage pulse + event hooks (G23, G4)

**Goal:** (a) low oxygen (<25%, escalating <10%) produces a pulsing blue-tinted vignette plus a
periodic warning tone; (b) taking damage briefly pulses vignette intensity; (c) a small generic API
exposes timed chromatic-aberration/film-grain bursts for future events (wired to nothing yet except
an EMP-style test path if trivially available).

**Read first:** `UrpScenePost.cs` (volume ownership — add the runtime-driven parameters here),
`HudUi.cs` (~77–119 damage feedback, where vitals state lives), `GameBootstrap`/player-state source
for the oxygen value, `ClientAudio.cs` + `ProceduralAudio.cs` (procedural cue pattern, SOUND_DESIGN
naming), `PostFx.cs` (decide: Built-in fallback gets the same pulses or is skipped — document).

**Do:** add `UrpScenePost` methods (`PulseVignette(strength)`, `SetOxygenAlarm(level01)`,
`Burst(effect, duration)`); drive from HudUi/vitals update; add `o2_warning` procedural tone with
escalation interval; respect the "reduced effects" setting.

**Done when:** build green; unit-testable pieces (alarm threshold/interval logic) covered if a
client test target exists, otherwise code-review; visual confirmation at PT-2.

---

## Phase B — Gameplay juice (client-only)

### WP-5 — Mining loop completion: final-hit flash + pickup fly-to-inventory (G22, G26)

**Goal:** breaking a block produces a brief bright flash at the block plus a small item icon that
flies from the break position to the hotbar; the loop feels finished.

**Read first:** `MiningFx.cs` (outline/crack/debris + the `FxParticle` pattern and how
`MiningProgressReceived`/block-break events arrive), `HudUi.cs` (hotbar rect position for the
fly-to target, canvas setup), `IconResolver.cs` (icon source for the mined block), `ClientAudio.cs`
(existing mine-sound hook, to place the flash on the same event).

**Do:** flash = one frame-scaled emissive quad or light at break position (reuse FxParticle);
fly-to = screen-space RawImage spawned at the world→screen position of the block, eased to the
hotbar slot over ~0.4 s, then a subtle slot pulse. Gate behind "reduced effects" off.

**Done when:** build green; logic reviewed (event source = authoritative block-change, not local
prediction); visual confirmation at PT-2.

### WP-6 — Crafting + blueprint celebration (G20, G21)

**Goal:** (a) crafting shows a short progress/assembly animation on the craft button/recipe row and
a glow + popup on completion; (b) unlocking a blueprint plays a node-glow animation in the tech tab
and a distinct unlock sound.

**Read first:** `CraftingTechShipUI.cs` (3-pane menu structure, `_feedback` path, how the tech tree
tab renders nodes, how `CraftResult` arrives), `UiKit.cs` (sprite/panel helpers to reuse for glow),
`ClientAudio.cs` (~134, `CraftCompleted` hook), `ProceduralAudio.cs` (add an unlock fanfare cue
distinct from `_ok`), locale files for popup text keys.

**Do:** completion glow = animated outline color on the crafted row + floating "+1 <item>" label;
blueprint unlock = node ring pulse (2–3 cycles) + brighter persistent state + fanfare; all
code-built (no prefabs), all strings localized DE/EN.

**Done when:** build green; tech-tab rebuild still performs (no per-frame allocations added —
check the existing Rebuild pattern); visuals at PT-2.

### WP-7 — UI transitions (G28)

**Goal:** menu/tab/panel changes animate (120–180 ms fade+slide), hotbar selection animates a
scale/glow tick; instant-snap UI is gone. A "reduced effects" setting disables all of it.

**Read first:** `UiKit.cs` (where canvases/panels are created — the tween helper belongs here),
`GameMenu.cs` + `CraftingTechShipUI.cs` (open/close + tab switching flow), `HudUi.cs` (hotbar
selection), `VisorMenuGlass.cs` (ensure transitions don't fight the visor overlay).

**Do:** add a minimal code-built tween utility (CanvasGroup alpha + anchored-position offset,
unscaled time); apply to menu open/close, tab switch, panel show/hide (scan panel, wreck panel,
VEGA panel entry); hotbar tick on selection change.

**Done when:** build green; transitions respect `MenuOpen` pause logic and unscaled time; PT-2.

### WP-8 — Music contexts (G29)

**Goal:** music switches with crossfade between four contexts: menu, planet surface, space,
combat — using generated ElevenLabs tracks with the procedural loop as fallback.

**Read first:** `ClientMusic.cs` (current single-loop design), `ClientAudio.cs` (bus routing,
volume settings), `docs/SOUND_DESIGN.md` (the specced music batch — follow its naming), `tools/
ai-assets/gen_sound.py` (generation flags), `GameBootstrap` (signals for context: MenuOpen,
SpaceViewActive, ship-combat state).

**Do:** generate the music batch (approved, run via `uv`); add context detection + 2–3 s crossfade
between two AudioSources; keep procedural fallback when a clip is missing; bundle clips under
`Resources/audio/` and update SOUND_DESIGN.md status table.

**Done when:** build green; context switching logic reviewed against all four states incl.
edge cases (EVA, station interior — pick nearest context and document the mapping); PT-2.

---

## Phase C — World & block surface

### WP-9 — Block texture variants + random rotation (G8)

**Goal:** natural blocks (stone, dirt, sand, snow, ice, basalt, deepslate, granite) get 2–3 tile
variants chosen deterministically per world position, and natural top/bottom faces get deterministic
90° UV rotation — breaking the visible tiling without any data or network change.

**Read first:** `BlockTextureAtlas.cs` (`PaintTile` per-block decoration, tile addressing, free
capacity), `ChunkMesher.cs` (`AddFace` UV emission — where a position hash can pick variant +
rotation; confirm remesh determinism: hash must use world coords, not chunk-local), `tools/
ai-assets/gen_textures.py` + `bundle_textures.py` (only if AI variants are wanted — prefer
procedural derivation of variants from the base tile first, per ADVANCED_GRAPHICS_PLAN's
"procedural first" rule).

**Do:** procedural variants (crack overlay / darker / vein-speckled derivations of the base tile)
written into spare atlas slots; mesher: `hash(worldX,Y,Z, face)` → variant index + rotation for a
whitelisted natural-block set; ship/tech blocks excluded (panels must stay aligned).

**Done when:** build green; determinism shown by a unit-style check of the hash (same input → same
variant) if test target exists, else code review; remesh of the same chunk yields identical UVs;
visual check at PT-2.

---

## Phase D — Ship presentation

> Server-touching WPs in this phase: re-read the **Blocks** and **Networking** global rules first.
> `dotnet test` after every server/shared change.

### WP-10 — Room identity pass (G13, G16, part of G10)

**Goal:** each ship room type is recognizable at a glance: per-room accent wall/floor blocks and a
per-room light color (medbay white/blue, engine room warm orange, cockpit cyan, cargo yellow/orange
markings, quarters warm dim, lab cool white, workshop neutral + orange), implemented as new blocks
stamped by the server layout.

**Read first:** `src/BlocksBeyondTheStars.GameServer/GameServerShipStructure.cs` (StampShip,
station placement ~186–192, ceiling-light row ~123–130 — understand exactly how rooms map to floor
areas before painting them), `data/ship_layouts/*.json` (layout cell format — decide layout-driven
vs. procedural painting), `data/blocks.json` (block def format incl. emission override),
`BlockTextureAtlas.cs` (tile painting for new blocks), `ChunkMesher.cs` (`BlockMaterial`/
`BlockEmission` switches must learn the new ids), Block-broadcast rule (stamping into a live world).

**Do:** add ~8–10 new blocks (light-strip white/cyan/orange/red, hazard-stripe panel, medbay panel,
cargo-marking floor, warm quarters light); extend stamping to lay accent floor border + wall strip +
room-colored light blocks per station area; update the three ship layouts; names localized DE/EN in
locale files (`block.<key>.name`).

**Done when:** `dotnet test` green; client + server build green; a freshly stamped ship in a test
save shows distinct rooms (verified at PT-2); no ghost blocks in multiplayer (broadcast path
re-checked in code).

### WP-11 — Cockpit experience (G14)

**Goal:** the cockpit station gets (a) a console visual entity with an animated emissive display,
(b) a simple holographic system map floating above the console when the player is near, and (c)
flight instruments in the space-view HUD (speed, throttle, hull/shield, heading) styled like the
existing radar.

**Read first:** `DoorView.cs` (the established pattern for server-entity-driven visuals — console +
hologram should follow it), `GameServerShipStructure.cs` (cockpit station placement + interaction
~571–576), `SpaceView.cs` (existing space HUD, radar wiring, where speed/throttle live),
`SpaceRadar.cs` + `UiKit.cs` (diegetic canvas pattern for instruments), `SkyBodiesView.cs`/
`PlanetOrbitLook.cs` (data source for the holographic system map bodies), `Shaders/
BlockAtlasTransparent.shader` (hologram look: additive/translucent cyan, reuse force-field
treatment if possible — avoid a new shader if an existing one serves).

**Do:** client-side `CockpitView` (mesh console + scrolling emissive display texture, animated in
code like the atlas force-field bands); hologram = billboarded points/spheres of the current
system's bodies, slow rotation, visible within ~4 blocks; instruments panel on the diegetic canvas
in space view; all labels localized.

**Done when:** build green; hologram body data matches `SkyBodiesView` source (same provider, no
duplicated tables — verify in review); PT-2.

### WP-12 — Animated machines/terminals (G15)

**Goal:** medbay, lab and workshop stations each get one small idle animation: medbay tank glow
pulse, lab terminal flicker/scroll, workshop occasional spark — using the same entity-view pattern
as WP-11.

**Read first:** `DoorView.cs` + the WP-11 `CockpitView` result (generalize into one
`StationDecorView` if the third copy starts repeating code), `GameServerShipStructure.cs` (station
positions reach the client how? — confirm the data path before building), `WeaponFx.cs` (~71–79
spark helper for the workshop), `ClientAudio.cs` (tie existing hum/loop cues to proximity if cheap).

**Do:** one decor visual per station type, animation in `Update` (emission pulse / UV scroll /
timed spark burst), distance-gated (only animate within ~20 m).

**Done when:** build green; no per-frame allocations in the animation paths (review); PT-2.

### WP-13 — Thruster & transit FX (G19)

**Goal:** (a) flight view: exhaust particles behind the glow sprite, scaled by throttle; (b)
launch/landing: dust burst at the pad + engine glow already present; (c) landed ships: faint idle
nozzle glow so ships never look dead.

**Read first:** `SpaceView.cs` (~2015–2060 exhaust rig, throttle source), `ShipTransitView.cs`
(launch/landing curves ~56–69, engine light ~123–134 — wire dust into the same curve),
`WeaponFx.cs` (~97–105 dust helper, ~71–79 sparks; reuse, don't duplicate), `MiningFx.cs`
(FxParticle lifecycle pattern), `GameServerShipStructure.cs` (engine-nozzle block positions for the
landed glow — how does the client know where nozzles are? verify via the stamped layout or ship
design cache before choosing the approach).

**Do:** code-built particle stream (cyan-white, additive, short-lived) at exhaust anchors; dust
burst at transit start/end ground position; landed idle glow = small emissive quad or point light at
nozzle blocks of the player's own stamped ship.

**Done when:** build green; particle counts preset-gated (off on Potato/Low); PT-2.

### WP-14 — Ship damage visuals (G17)

**Goal:** while aboard, hull below 50% shows occasional interior spark bursts; below 25% adds a red
pulsing emergency light + a low alarm cue; the space view shows brief spark/smoke puffs at hull-hit
moments. No new network messages if hull state already reaches the client.

**Read first:** `src/.../ShipState` (where Hull lives) and the message that carries it to the
client (grep `Hull` in client Scripts — confirm availability; if it does **not** reach the client,
that's a new message → NetCodec rule), `SpaceView.cs` (~1552–1660 combat status, existing
`_hitFlash`/`_shake`), `WeaponFx.cs` (sparks), `DoorView.cs`/WP-12 pattern (emergency light as an
interior entity), `ClientAudio.cs` (alarm loop pattern).

**Do:** client-side damage director keyed off hull fraction: spark spawner at random interior
positions of the stamped ship volume, red rotating/pulsing light entity near the engine room, alarm
loop with hysteresis (no flicker at the threshold); hit-moment puffs in space view.

**Done when:** `dotnet test` green if shared/server touched; build green; thresholds + hysteresis
reviewed; PT-2.

### WP-15 — Ship-build placement preview + materialize (G18)

**Goal:** (a) the ship editor shows a translucent ghost of the block to place — cyan/green when
valid, red when invalid — before placement; (b) when a ship design is stamped into the world, the
client plays a block-by-block materialize reveal (bottom-up, ~1.5 s) instead of the hull popping in.

**Read first:** `ShipEditor.cs` (~191–214 placement raycast + palette; find the actual validity
rules — what *is* invalid? bounds, overlap, support? Read the server-side validation in
`GameServerShipStructure.cs`/ship design handling so the ghost matches real rules), `ShipMeshBuilder.cs`
(voxel mesh building for the ghost), `BlockAtlasTransparent.shader` (ghost material — translucent
tint path), `GameBootstrap.cs` (~800, chunk rebuild on `BlockChanged` — the materialize effect must
be a **client-side overlay**, not staged server stamping, to avoid broadcast spam; confirm where a
stamp event is observable on the client).

**Do:** editor ghost (mesh of the selected block at the target cell, validity-tinted, soft pulse);
stamp materialize = transient client effect: hide region via a rising clip plane or per-block
reveal quads synced to a single stamp notification, plus the existing build sound at completion.

**Done when:** build green; ghost validity matches server rules (cross-checked against the server
validation code in review); PT-2.

---

## Phase E — Documentation closure

### WP-16 — Art Bible (G32) + doc hygiene

**Goal:** a single normative `docs/ART_BIBLE.md` capturing the **final** state after WP-1…15: style
rules (stylized voxel sci-fi, readable over photoreal, milky glass rule), the named color palette
(Space Black, Deep Navy, UI Cyan, Engine Blue, Warning Orange, Soft White, Metal Gray, Hazard Red,
Alien Green, Crystal Purple — mapped to the actual `UiKit`/grade constants), per-room ship palette
from WP-10, planet-type mood table from `Sky.GradeFor` + `planets.json`, and the quality checklist
("every important object: clear silhouette, ≥2 material zones, one glow detail, readable icon").

**Read first:** `UiKit.cs` color constants, `Sky.cs` `GradeFor`, `data/planets.json`,
`UI_AND_RENDER_CONCEPT.md` (fold its still-valid style rules in, mark it superseded where it
overlaps), the WP-10 room palette as implemented.

**Do:** write the Art Bible from code truth (constants quoted with file references); fix the stale
"Built-in RP, no URP today" header in `ADVANCED_GRAPHICS_PLAN.md`; final `TODO.md` sweep marking
all WPs; update `PROFESSIONAL_LOOK_GAP_ANALYSIS.md` statuses (G-items closed by this run).

**Done when:** docs consistent with code; no doc claims an unimplemented feature.

---

## Playtest checkpoints (the only two)

- **PT-1 — after WP-1 only.** Linear color space cannot be signed off without eyes: run the client
  and verify the eight screenshot scenarios listed in WP-1. Fix-forward within WP-1 until parity.
- **PT-2 — once, after WP-15.** One structured pass covering everything since: fonts/DE text
  (WP-2), camera toggle (WP-3), low-O₂ + damage pulse (WP-4), mining flash + fly-to (WP-5),
  craft/blueprint celebration (WP-6), UI transitions (WP-7), music contexts (WP-8), block variants
  (WP-9), ship rooms/cockpit/machines/thrusters/damage/build-preview (WP-10…15). Log findings as a
  fix list, fix, re-verify only the failed items.

Everything between the checkpoints is gated by: batch build green + fresh
`BlocksBeyondTheStars.Client.dll` timestamp, `dotnet test` green when `src/` changed, locale files
complete (en+de key parity for new keys), and the per-WP "Done when" review points.

## Suggested commit sequence

One commit per WP, conventional commits, e.g.:
`feat(render): linear color space migration with boundary conversions (WP-1)` …
`docs(art): art bible + plan closure (WP-16)`. Update `TODO.md` in each commit.
