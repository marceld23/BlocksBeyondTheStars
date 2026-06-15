# Editors → World-Gen Integration & Developer-Editor Signposting — Analysis

Status: **analysis only — nothing implemented.** Two requests:

1. Build the **ship editor** and the **station / village / city (settlement) editors** so that
   structures saved in them are picked up by the **random world generator** in new games — and let the
   player choose **at world / server creation which structures get used**.
2. Make it **clear in the game menu** that the **Material editor** and the **Tech (Item & Recipe)
   editor** are **developer editors** — i.e. that those edits do **not** flow straight into a player's
   own game.

This builds on [SHIP_TYPE_EDITOR_PLAN.md](SHIP_TYPE_EDITOR_PLAN.md) and
[STATION_SETTLEMENT_EDITOR_PLAN.md](STATION_SETTLEMENT_EDITOR_PLAN.md), which are now **largely
implemented** (see §1). This doc re-grounds against the shipped code and focuses on the two *new* asks:
**runtime ingestion + per-world structure selection**, and **dev-editor signposting**.

---

## 1. What already exists (ground truth)

The editor → export → merge → stamp pipeline the earlier plans describe is **built and wired**:

### Editors (client, `client/Assets/BlocksBeyondTheStars/Scripts/`)
- **ShipEditor.cs** — free-fly build room, ~20-item palette (hull / glass / lights / doors / 8 station
  markers / engine), metadata + stats + craft-cost panel, exports to
  `persistentDataPath/ship_exports/<key>/{ship.json, layout.json}`.
- **StructureEditor.cs** — one component, two modes (`Mode.Station`, `Mode.Settlement`); palette + size
  tiers switch by mode; markers (vendor / mission_board / hangar / heal_tank / quarters / npc / loot);
  exports to `persistentDataPath/<station|settlement>_exports/<key>/{structure.json, layout.json}`.
- **MaterialEditor.cs** — paint a 64×64 tile + set mining / look / world-spawn; exports to
  `material_exports/<key>/{material.json, texture.bytes}`.
- **ContentEditor.cs** ("Item & Recipe Editor", the *Tech editor*) — define item + recipe + blueprint
  gating; exports to `content_exports/<key>/content.json`.
- All reached from **Main Menu → Editors** ([UiMainMenu.cs](../client/Assets/BlocksBeyondTheStars/Scripts/UiMainMenu.cs),
  [UiEditors.cs](../client/Assets/BlocksBeyondTheStars/Scripts/UiEditors.cs)); phases in
  `AppShell.ShellPhase` (`ShipEditor`, `StructureEditor`, `ContentEditor`, `MaterialEditor`, `Editors`).

### Content models + load
- `ShipLayout` (`Key`, `W/H/L`, `Cells[{X,Y,Z,Kind,Id}]`) and `StructureTemplate`
  (`Key`, `Name`, `Tier`, `Kind`, `W/H/L`, `Cells[{X,Y,Z,Kind,Id}]`) in
  `src/BlocksBeyondTheStars.Shared/Definitions/`.
- [ContentLoader.cs](../src/BlocksBeyondTheStars.Shared/Content/ContentLoader.cs) loads
  `data/ship_layouts/*.json`, and **optionally** `data/station_templates.json` +
  `data/settlement_templates.json` into `GameContent.StationTemplates` / `.SettlementTemplates`.
- `data/ship_layouts/` ships 3 layouts (scout / corvette / hauler). **`station_templates.json` and
  `settlement_templates.json` are not shipped yet** → both pools are empty by default.

### Merge tools (`tools/`, uv)
- `merge_ship.py` → folds an export into `data/ships.json` + `data/ship_layouts/<key>.json`.
- `merge_structure.py` → folds an export into `data/station_templates.json` **or**
  `data/settlement_templates.json`.
- `merge_material.py`, `merge_recipe.py` → fold Material / Content editor exports into
  `data/{blocks,items,recipes,blueprints,planets}.json` + locale placeholders.

### Stamping (server already consults the pools)
- `GameServerSpaceStations.StampStation` ([GameServerSpaceStations.cs:416](../src/BlocksBeyondTheStars.GameServer/GameServerSpaceStations.cs#L416)):
  if `StationTemplates` is non-empty, a seeded roll `< StructureTemplateChance` (a constant ≈ 0.35)
  stamps `StationGenerator.FromTemplate(pool[random])`; otherwise `StationGenerator.Generate(tier,…)`.
- `GameServerSettlements.StampSettlement` — same shape for `SettlementTemplates`.
- Deterministic: same world seed → same choice. Empty pool → always procedural (zero regression).

### World creation already has structure knobs
- [WorldCreationOptions.cs](../client/Assets/BlocksBeyondTheStars/Scripts/WorldCreationOptions.cs)
  exposes frequency sliders incl. **Settlements** and **Stations** (+ Wrecks, Vaults, Flora, Ore,
  Exotic, universe size, survival rules, story). `ToArgs()` emits only non-defaults as `--…` overrides.
- Flows: UI → CLI args → [ServerConfig.ApplyCommandLine](../src/BlocksBeyondTheStars.Shared/Configuration/ServerConfig.cs)
  → `WorldDescription` (frequencies) + `GameRules` → baked into `WorldMetadata` (`RulesOverride`,
  `Description`) and persisted; on reload the world keeps its own rules.

**So requests #1's plumbing mostly exists.** What's missing is exactly the *two new things the user
asked for*, plus three latent gaps. See §2.

---

## 2. Gap analysis for request #1

### Gap A — Editor output does not reach a running game without a dev rebuild
The running (local/embedded) server reads content from **`StreamingAssets/data`**
([LocalServerLauncher.cs:119](../client/Assets/BlocksBeyondTheStars/Scripts/LocalServerLauncher.cs#L119)
passes `--data <streamingAssets>/data`). Editors write to **`persistentDataPath/*_exports`**. The only
bridge is the **Python `merge_*.py` tools + a source-tree rebuild / asset re-sync**. Therefore today an
in-game saved ship/station/village is **not** usable in a new game until a developer runs a merge and
rebuilds — for *all* editors, including ship/structure. This is the core of why request #2 (signposting
dev-only editors) is even necessary, and the core obstacle to request #1 ("directly used in new games").

### Gap B — Template selection is a flat, fixed-chance random pick
`StampStation` / `StampSettlement` pick **uniformly at random from the whole pool** at a hard-coded
`StructureTemplateChance`. There is:
- **no tier matching** — a `small`-tier station slot can stamp a `colossal` template (size mismatch);
- **no per-world weighting** — every template is equally likely;
- **no way to choose *which* templates** a given world may use (all-or-nothing pool).

### Gap C — No per-world / per-server "which structures" selector
World creation can scale **how many** settlements/stations appear (frequency), but not **which authored
templates** are eligible, nor the **template-vs-procedural ratio**, nor enable/disable specific packs.

---

## 3. Design options for request #1

### 3.1 Runtime ingestion of editor structures (closing Gap A)
Three tiers, pick per how "player-facing" this should be:

- **Option A1 — Dev-only (status quo, just documented).** Keep merge + rebuild. Editors stay developer
  tooling; their output enters the game only through a content release. Cheapest; matches today.
- **Option A2 — Local user-content folder, auto-loaded (recommended for ships/structures).** Have the
  server *also* scan a writable content dir (e.g. `persistentDataPath/usercontent/{ship_layouts,
  station_templates,settlement_templates}/`) in addition to `StreamingAssets/data`, merging found
  entries into the pools at load. The editors' **Save** would (also) write there in the final
  data-shaped JSON. Then a structure built in-game appears in the *next* new world with **no Python
  step and no rebuild** — exactly "directly used by the random generator." Determinism and tests are
  unaffected (pools just have more entries). This is the smallest change that satisfies the literal ask.
- **Option A3 — Bake selected exports into the save at creation.** When creating a world, copy the
  chosen templates into the save folder so the world is self-contained / shareable. More work; only
  needed if worlds must be portable between machines.

`merge_*.py` stays the path for **shipping** content into the official `data/` (curated, reviewed,
localized). A2 is the *player/test* path; the merge tools are the *release* path. They coexist.

### 3.2 Tier-aware, weighted selection (closing Gap B)
- Give `StructureTemplate` an optional **`Weight`** and treat **`Tier`** as a filter: build
  `StationTemplatesByTier[tier]` / `SettlementTemplatesByTier[tier]` pools in `GameContent`, and at the
  stamp site pick from the **tier-matching** sub-pool (weighted), falling back to procedural when that
  sub-pool is empty. Removes the size-mismatch bug and makes authoring per-tier landmarks meaningful.
- Make `StructureTemplateChance` a **tunable** (constant today) — see §3.3.

### 3.3 Per-world / per-server "which structures" selection (closing Gap C, the core ask)
Mirror the existing frequency-slider plumbing (`WorldCreationOptions → ToArgs → ServerConfig →
WorldDescription → WorldMetadata`). Add, at increasing fidelity:

- **Level 1 — a template ratio slider** per structure kind: "Authored structures" = Off / Rare / Mix /
  Often (drives `templateChance`). Off = pure procedural (today's default); higher = more authored
  landmarks. One float per kind in `WorldDescription`, one `--station-templates` / `--settlement-
  templates` CLI arg, one slider row in the creation panel. Smallest UX that satisfies "choose how much
  the authored ones are used."
- **Level 2 — a structure pack picker.** Group templates into named **packs** (a `pack`/`tags` field on
  `StructureTemplate`, e.g. "Vanilla", "My builds", "Alien ruins"). The creation panel lists packs with
  checkboxes; the world stores the enabled pack set in `WorldDescription`; the stamp site filters the
  pool to enabled packs. This is the natural "choose which structures" UI and composes with A2 (the
  user's own builds form a "My builds" pack).
- **Level 3 — per-template allow-list** (advanced page only): toggle individual templates. Probably
  overkill; Level 2 covers the intent.

**Server side:** the same fields live in `ServerConfig`/`server.json` so a dedicated server host sets
the structure policy once; `ApplyCommandLine` already accepts world overrides, so adding
`--station-templates`, `--settlement-templates`, `--structure-packs "vanilla,mybuilds"` is consistent.

### 3.4 Suggested phasing for request #1
1. **P1 — tier-aware weighted pools** (§3.2) + ship one sample `station_templates.json` /
   `settlement_templates.json` so the pools are non-empty and testable. Pure server, low risk.
2. **P2 — `templateChance` as a world option** (§3.3 Level 1): slider + CLI + `WorldDescription` field +
   persistence. Reuses the exact frequency-slider pattern.
3. **P3 — runtime user-content folder** (§3.2 Option A2): server scans `persistentDataPath/usercontent`;
   editors save data-shaped JSON there. Now in-game builds appear in new worlds without a rebuild.
4. **P4 — structure packs** (§3.3 Level 2): pack tagging + creation-panel pack picker + server config.
5. **P5 — (optional) bake-into-save** (A3) for portable/shareable worlds.

---

## 4. Request #2 — signposting Material & Tech editors as developer tools

### 4.1 Why these two differ from ship/structure editors
- **Ship / Station / Settlement** editors produce **per-world content** (structures dropped into
  generation). With §3.1-A2 they can become genuinely player-usable ("build a base type, see it appear
  in your next world").
- **Material editor** and **Tech (Item & Recipe) editor** change the game's **fundamental content +
  balance** — new blocks/materials, items, recipes, blueprint costs, texture atlas. These are **global,
  cross-cutting, and require a merge + rebuild** (atlas regeneration, id assignment, locale keys). They
  cannot meaningfully be hot-loaded into one save and are inherently a **developer / modding** activity.
  The user's instinct to fence them off is correct.

### 4.2 Current signposting (what's already there)
- They live under a separate **Editors** submenu (not the main play flow).
- `ui.editors.info_body` mentions running `tools/merge_*.py`.
- App splash reads `PROTOTYPE / DEV BUILD`; version `0.20.0-dev`.

This is weak: a non-developer reading "Material Editor" reasonably expects in-game effect, and the merge
hint is buried in one info paragraph.

### 4.3 Options to make the developer-only nature explicit
Bilingual (DE + EN) for any visible string (per project rule). In rough order of effort:

- **S1 — Group + label as a "Developer / Creator Tools" section.** Within the Editors submenu, split
  into two groups: **"In-game creator"** (Avatar, and — once §3.1-A2 lands — Ship/Station/Town that feed
  your worlds) and **"Developer content tools (rebuild required)"** (Material, Item & Recipe). Add a
  short subtitle to the dev group: *"For mod/game developers — changes need a merge + rebuild and do not
  appear in your current game."* Locale keys e.g. `ui.editors.devtools.title` / `.note`.
- **S2 — Per-editor banner.** When entering MaterialEditor / ContentEditor, show a persistent header
  badge: **"DEVELOPER TOOL — exports to disk; not applied to your game until merged + rebuilt."** One
  label in each editor's IMGUI, reusing the existing status-line styling.
- **S3 — A confirm-on-first-open dialog** explaining the export-then-merge workflow (with a "don't show
  again" flag in `ClientSettings`). Strongest clarity; mildly intrusive.
- **S4 — Gate behind a "Developer mode" toggle** in Settings (off by default). The two dev editors are
  hidden unless enabled; enabling shows the explanation once. This most strongly communicates "not for
  normal play," at the cost of a settings flag + conditional menu build.
- **S5 — Visual de-emphasis + icon.** A wrench/`</>` "dev" icon + muted styling on the two entries vs
  the creator editors, so the distinction is legible at a glance.

**Recommended combination:** **S1 + S2** (clear grouping with an explanatory note, plus an in-editor
banner) as the baseline; add **S4** if these should be hidden from ordinary players entirely. S1+S2 are
low-risk, purely additive UI + locale work.

### 4.4 Note on consistency
If §3.1-A2 is **not** adopted, then ship/structure editors *also* require a merge + rebuild, and the
honest signpost is "all editors are developer tooling." The clean story the user is reaching for —
"material/tech are dev-only, the others feed your game" — only becomes *true* once runtime ingestion
(§3.1-A2) exists for ships/structures. So **request #2's framing depends on a request #1 decision**:
either adopt A2 (and the two-group split in §4.3-S1 is accurate), or keep everything dev-only (and label
all editors uniformly as developer tools).

---

## 5. Decisions — LOCKED (2026-06-15)

1. **Runtime ingestion:** **A2** — auto-load a writable user-content folder (in-game builds appear in
   new worlds without a Python merge or rebuild). `merge_*.py` stays the *release* path only.
2. **Selection granularity:** **Level 2** — pack picker (templates grouped into named packs, enabled per
   world).
3. **Tier handling:** **add `Weight` + tier-matched sub-pools now** (fixes the size-mismatch bug, makes
   per-tier landmarks meaningful).
4. **Dev-editor signposting:** **S1 + S2** — split the Editors menu into "creator" vs "developer
   content tools (rebuild required)" + a persistent in-editor banner on Material / Item&Recipe. No
   Developer-mode gate (S4) for now.
5. **Scope of "city":** **no new tier** — "city" = authoring large templates at the **existing `city`
   tier** (hamlet/village/town/city already exist).

---

## 6. Implementation plan (locked) — file-touch list

Conventions: docs/comments **English**; any player-visible string **DE + EN**
([spacecraft-game-bilingual]). New net messages — none expected (all server-load / creation-time). Add
tests per phase.

### P1 — Tier-aware, weighted, pack-filtered template selection (server-only, no UI)
- **`src/.../Shared/Definitions/StructureTemplate.cs`** — add `string Pack = "default"` and `int Weight =
  1`. (`Tier`, `Kind`, `Cells` stay.)
- **`src/.../Shared/Content/GameContent.cs`** — in `SetStructureTemplates`, also build
  `StationTemplatesByTier` / `SettlementTemplatesByTier` (`Dictionary<string,List<StructureTemplate>>`)
  and expose `StructurePacks` (distinct pack names). Add weighted selectors
  `PickStationTemplate(string tier, IReadOnlySet<string>? packs, Random rng)` and the settlement twin —
  filter by tier + enabled packs, weight by `Weight`, return `null` when the sub-pool is empty.
- **`src/.../Shared/World/WorldDescription.cs`** — add `Frequency StationTemplateUse = Frequency.Rare`,
  `Frequency SettlementTemplateUse = Frequency.Rare`, `List<string> EnabledStructurePacks = new()`
  (empty = all). Reuse `Frequency.Probability()` for the template-vs-procedural chance.
- **`src/.../GameServer/GameServerSpaceStations.cs`** (`StampStation`, ~L416–437) — replace the flat
  `pool[roll.Next]` + constant `StructureTemplateChance` with `PickStationTemplate(station.SizeTier,
  meta.Description.EnabledStructurePacks, rng)` gated by `meta.Description.StationTemplateUse
  .Probability()`; procedural fallback unchanged. (Drop/retire the `StructureTemplateChance` constant.)
- **`src/.../GameServer/GameServerSettlements.cs`** (`StampSettlement`) — same change with
  `SettlementTemplateUse` + settlement tier.
- **`tools/merge_structure.py`** — add `pack` + `weight` to each pool entry.
- **`data/station_templates.json` + `data/settlement_templates.json`** — ship one hand-authored sample
  each (per tier) so pools are non-empty + tests have fixtures.
- **Tests** (`tests/.../`): weighted tier+pack selection picks correctly + falls back on empty sub-pool;
  deterministic pick for a fixed seed; `Off` use → always procedural.

### P2 — Runtime user-content ingestion (closes Gap A)
- **`src/.../Shared/Content/ContentLoader.cs`** — add optional `string? userContentDir` to
  `LoadFromDirectory`; when present, scan `userContentDir/station_templates/*.json` +
  `settlement_templates/*.json` (one data-shaped `StructureTemplate` per file, key = filename, like
  `ship_layouts`) and merge into the pools before `SetStructureTemplates`. Same pattern can later cover
  `ship_layouts`.
- **`src/.../GameServer/Program.cs`** — accept `--usercontent <dir>` and pass it through.
- **`client/.../LocalServerLauncher.cs`** (~L118–151) — pass
  `--usercontent "<persistentDataPath>/usercontent"`.
- **`client/.../StructureEditor.cs`** (`Export`, L255–294; `MetaJson`) — add `pack` + `weight` to the
  metadata UI + `MetaJson`; on Save, **also** write a data-shaped `StructureTemplate` JSON straight to
  `persistentDataPath/usercontent/{station|settlement}_templates/<key>.json` (in addition to the export
  bundle). Update the status/hint: "Appears in your new worlds now — run merge_structure.py only to ship
  it into the game."
- **Tests:** ContentLoader merges a usercontent template into the right tier/pack pool.

### P3 — Per-world "which structures" creation UI (closes Gap C)
- **`client/.../WorldCreationOptions.cs`** — add `int StationTemplates`, `int SettlementTemplates`
  (Freq indices, default = Rare) + `HashSet<string> DisabledPacks` (or enabled set); `ToArgs()` emits
  `--station-templates`, `--settlement-templates`, `--structure-packs "a,b"` when non-default.
- **`src/.../Shared/Configuration/ServerConfig.cs`** (`ApplyCommandLine`) — parse the three args into
  `World.StationTemplateUse` / `.SettlementTemplateUse` / `.EnabledStructurePacks`.
- **`client/.../UiWorldOptions.cs`** — add two slider rows (Structures column) + a **pack picker**
  (checkbox list) on the Advanced page. Pack names: client gathers distinct packs from
  `StreamingAssets/data/{station,settlement}_templates.json` + the usercontent folder.
- **Tests:** `ApplyCommandLine` parses the new args; `WorldDescription` round-trips the new fields
  through the snapshot/persistence layer.

### P4 — Dev-editor signposting (S1 + S2)
- **`client/.../UiEditors.cs`** — split buttons into two labelled groups: **"In-game creator"** (Ship,
  Station, Town, Avatar) and **"Developer content tools (rebuild required)"** (Item & Recipe, Material),
  with a short note under the dev group. Update the right-hand info panel accordingly.
- **`client/.../MaterialEditor.cs` + `ContentEditor.cs`** — persistent top banner:
  *"DEVELOPER TOOL — exports to disk; not applied to your game until merged + rebuilt."*
- **`data/locales/{en,de}.json`** — new keys: `ui.editors.group.creator`, `ui.editors.group.dev`,
  `ui.editors.dev.note`, `ui.editors.devbanner`, struct-editor `pack`/`weight` labels, world-option
  `station_templates`/`settlement_templates` slider labels + pack-picker title.

### Build order & risk
P1 (pure server, lowest risk, immediately testable) → P2 (runtime ingestion, the headline feature) →
P3 (creation UI) → P4 (signposting, additive UI). Each phase ships independently; client phases (P2
editor, P3, P4) need a Unity client build to verify. No new net messages; no save-format break (new
`WorldDescription` fields default to today's behaviour).

**P1–P4 status: IMPLEMENTED 2026-06-15 (server/shared tests green, 620/620). Needs a Unity client
build to verify the P2/P3/P4 client UI.**

---

## 7. Editor palette + dye + shape + orientation + glow upgrade (NEW ASK — plan)

### 7.1 What the editors expose today (gap)
- **StructureEditor** (station + settlement modes): a **hard-coded ~6-block palette**
  ([StructureEditor.cs:75-102](../client/Assets/BlocksBeyondTheStars/Scripts/StructureEditor.cs#L75-L102))
  + a few markers. **BUG:** the `light`/`Lamp` entries use block id `"light"`, which **does not exist**
  (real light blocks are `light_white/red/green`, `strip_light_cyan/warm`) → those cells stamp as **air**.
- **ShipEditor**: a richer **~20-entry** hard-coded palette (hull, glass, light *elements*, engine,
  hatch, doors, stations, weapons) — but also uses the non-existent `light` (as a ship "element").
- **Neither editor** offers: the **full block catalogue** (119 blocks), **dye/tint**, **glow colour**,
  **shape** (slab/ramp/stairs/pyramid/dome/sphere/cone/cylinder), or **shape orientation** — even though
  in-game **every block is tintable + shapeable and shaped blocks are oriented at placement**.
- **Format gap:** `TemplateCell` and `ShipLayoutCell` carry only `X,Y,Z,Kind,Id`. The stamp paths
  (`GameServerSpaceStructure.BuildShipStructureFrom`, `StationGenerator.FromTemplate`,
  `SettlementGenerator.FromTemplate`) set only a block id — no `SetModifier`/`SetShape`.

### 7.2 Target (per the user)
In **both** the ship editor and the station/settlement editor:
1. The **full material catalogue** (every placeable block), browsable + searchable, not a fixed handful.
2. **Dye any block** (per-cell tint) and set **glow colour** (the glowing blocks), like in-game.
3. **Shape any block** (the 9 `BlockShape`s) and **orient** the shape (the 4 cardinal facings), like the
   in-game place flow.
4. Keep the **special elements** stations/ships already have — exterior lamps, engines/thrusters,
   hatch/airlock, doors, station markers, ship weapons.

### 7.3 The enabling insight (it's already a solved problem in-game)
The runtime already stores per-voxel **tint+glow** (`ChunkData` modifier dict, `0xRRGGBB`) and
**shape+orientation** (`ChunkData` shape dict, packed `ShapeCode.Pack(shape, facing)`), persists them
(`BlockEdit.Tint/Glow/Shape`), streams them (`ChunkDataMessage`, `BlockChanged`), and meshes them
(`ChunkMesher` + `BlockShapeGeometry`). The item key already encodes `#t<rgb>g<rgb>s<xx>`
([ItemKey](../src/BlocksBeyondTheStars.Shared/Definitions/ItemKey.cs)). The **only** missing links are
(a) the **template/layout cell** can't carry tint/glow/shape, (b) the **stamp paths** don't apply them,
and (c) the **editor UI + editor preview** don't let you pick or show them.

### 7.4 Plan

**A. Shared format (the spine).** Add to **`ShipLayoutCell`** and **`TemplateCell`**:
`int Tint`, `int Glow`, `int Shape` (all default 0 = none/cube). Backward-compatible — old files omit
them and read as 0. Mirror the fields into the editors' `CellJson`/`TemplateJson`/`LayoutJson` and into
`tools/merge_structure.py` + `tools/merge_ship.py` pool entries.

**B. Stamp paths apply them.** Where each stamp loop sets a block id, also call
`SetModifier(p, cell.Tint, cell.Glow)` when non-zero and `SetShape(p, cell.Shape)` when non-zero — in
`GameServerSpaceStructure.BuildShipStructureFrom`, `StationGenerator.FromTemplate`,
`SettlementGenerator.FromTemplate`. (`StationStructure`/`SettlementStructure`/`SpaceStructure` need a
per-cell tint/glow/shape store + getters so the *placement* code that copies them into the world carries
them — mirrors how block ids already flow.) Then `BlockChanged`/chunk streaming already do the rest.
Also **fix the `light` bug**: drop the non-existent `light` palette id (use `light_white` etc.).

**C. Editor UI — full palette.** Replace the hard-coded `Pal[]` with a palette **built from
`GameContent.Blocks`** (all placeable blocks), grouped by a block category/tag with a **search box** and
scrollable grid (icons reuse the block atlas). Keep the **special elements + markers** as their own
groups (engine, exterior lamp, hatch, doors, station/ship-station markers, weapons). One shared palette
core for both editors (the ship adds element/station/weapon groups; structures add their markers).

**D. Editor UI — dye / glow / shape / orient (a per-cell "brush" state).** Add an inspector strip:
- **Tint** + **Glow** colour pickers (reuse the in-game dye colour UI if extractable; else a compact
  HSV/preset swatch). 0 = none.
- **Shape** picker (the 9 `BlockShape`s as icons) + an **orientation** control (rotate key, e.g. `R`,
  cycling the 4 facings) exactly like the in-game place flow (`ShapeCode.Pack(shape, facing)`).
- The brush writes `{Id, Tint, Glow, Shape}` into the placed cell; place/remove unchanged.

**E. Editor preview renders them.** Today the editor previews cells as plain Unity primitive cubes. To
*show* dye/glow/shape/orientation it must either (i) reuse `BlockShapeGeometry.Build(shape, facing)` +
tint material for the preview mesh, or (ii) drive the real `ChunkMesher` on the editor grid. (i) is the
smaller lift and matches the in-game look closely enough for authoring.

**F. Export/round-trip.** Save writes the new fields (bundle + usercontent template + ship export);
Load restores them; `merge_*.py` preserves them. With §3.1-A2 already shipped, an authored tinted/shaped
station appears in your next world with no rebuild.

### 7.5 Phasing
- **U1 — format + stamp + bugfix (server/shared, testable now):** add `Tint/Glow/Shape` to both cell
  types + the structure stores + stamp calls; fix the `light` id; tests that a tinted/shaped/oriented
  template stamps the right modifiers. No UI yet — authored-by-hand JSON already benefits.
- **U2 — full block palette** in both editors (search + groups, from `GameContent`), keeping elements +
  markers. Drop the hard-coded lists.
- **U3 — dye + glow + shape + orient brush** + editor preview rendering (the visible payload).
- **U4 — export/load/merge round-trip** for the new fields + polish (copy brush from an existing cell,
  recently-used swatches/shapes).

U1 is pure server/shared (unit-testable); U2–U4 are client + need a Unity build. U1 unblocks everything
and is independently shippable.

**U1–U4 status: IMPLEMENTED 2026-06-15.** Server/shared (U1) tested green. Both editors now build their
palette from `GameContent.Blocks` (all placeable blocks) + keep the markers/elements groups, with a
search box; a per-cell brush sets dye + glow colour + shape + orientation (key `R`), and the preview
renders the real shape (via `BlockShapeGeometry`) + tint. The `light` palette bug is gone (real light
blocks come from the catalogue). `ShipLayoutCell`/`TemplateCell` carry `Tint/Glow/Shape`; station +
settlement stamps apply them through `_world.SetBlock(...,tint,glow,shape)`; the ship `SpaceStructure`
+ `SpaceShipDesign`/`LandedShipState`/`StructureBlockChanged` carry per-cell arrays and the client ship
mesh paths (`SpaceView`, `LandedShipView`, `ShipMeshBuilder`) feed them into the shared mesher.
`merge_ship.py`/`merge_structure.py` preserve the fields. **Client (U2–U4) needs a Unity build to
verify/compile.**
