# Station & Settlement Editors + Template World-Gen — Analysis & Plan

Status (2026-06-19): **IMPLEMENTED** (the "nothing implemented yet" header is obsolete — P1–P4 and the P5
procedural-enrichment slices in §3 are all shipped; server/shared tests green, client editors need a Unity
build to verify). Requested: build a **space-station editor** and a **town/village editor** (like the ship
editor), then make world generation pick from **lists of hand-designed station/village/town types** *in
addition to* procedural generation.

> **Update (2026-06-27) — large builds.** The structure editor's build volume is now **128×128×128**
> (`StructureEditor.MaxW/MaxH/MaxL`; the ship editor is 48×32×48). To keep large builds editable the editors
> render the placed cells as a chunked combined mesh with face culling (`EditorVoxelChunkView`) instead of one
> GameObject per cell. The settlement **placement** allocator scales with the footprint: the flatness
> tolerance grows with build size, the wet/flatness gates sample a denser grid, large footprints are excluded
> from floating-island seating, and the terrain carve is bounded to the actual relief height (so a 128-tall
> tower no longer carves a 2M-block column). Settlements also get a **stepped support plinth**: the flat floor
> stays level at `gy`, but each column is filled solid down to the natural surface (deep downhill, shallow
> uphill, depth-capped), so a big build on a slope meets the ground all the way round instead of a flat slab
> floating over a dip — a real multi-level foundation. Space stations live in void worlds, so they have no
> placement gate. There is no size cap in the data model or merge tools — the editor bound is the only limit.

What shipped differs from the design below in two notable ways:
- **One editor, two modes — not two editors.** Both the station and the settlement editor are the single
  `client/.../StructureEditor.cs` (`Mode.Station` / `Mode.Settlement`); the menu exposes both entries
  (`AppShell.OpenStationEditor` / `OpenSettlementEditor` → one `StructureEditor`). The `StationEditor` /
  `SettlementEditor` class names and the `StructureEditorCore` extraction in §2.2/§4 were **not** built as
  named here.
- **`StructureTemplate`, not `StationLayout`/`SettlementLayout`.** The shared model that shipped is a single
  `StructureTemplate` (`Key, Name, Tier, Kind, W/H/L, Cells[{X,Y,Z,Kind,Id,Tint,Glow,Shape}]`) with `Pack`
  + `Weight`, loaded by `ContentLoader` from `data/station_templates.json` / `data/settlement_templates.json`
  (both now shipped, non-empty) + a writable `usercontent/` folder; pools are tier-keyed and the placers
  call `GameContent.PickStationTemplate` / `PickSettlementTemplate`. The `LayoutCell`/`LayoutMarker`/
  `StationLayout`/`SettlementLayout` names + `TemplateStamp` helper in §2 are NOT the shipped shape.

For the authoritative, up-to-date account of the shipped pipeline (runtime user-content ingestion, tier-aware
weighted pack-filtered selection, the per-world structure picker, the editor dye/glow/shape/orient palette
upgrade, and dev-editor signposting) see
[EDITORS_WORLDGEN_AND_DEV_LABELLING_ANALYSIS.md](EDITORS_WORLDGEN_AND_DEV_LABELLING_ANALYSIS.md). This
document is retained as the original analysis (including the §1b size/variety assessment and the §3 P5
enrichment record, which IS accurate). Builds directly on the
[ship-type editor](SHIP_TYPE_EDITOR.md) (same editor → export → merge → stamp pattern).

---

## 1. What exists today (analysis)

Both structures are **procedurally generated voxel grids** stamped into the world, with interaction
markers — the same shape the editors must produce.

### Space stations
- [`StationGenerator`](../../src/BlocksBeyondTheStars.WorldGeneration/StationGenerator.cs) →
  `StationStructure` (`Width/Height/Length`, a flat `ushort[]` block grid, `SizeTier`,
  `IReadOnlyList<StationMarker>` and `StationModule`s).
- Built from a **seed + size tier** by laying out **module rooms** (`hub / hangar / market / mission /
  medbay / quarters / corridor`) on a 3D module grid via a seeded random walk; each module is a hollow
  `iron_wall` room; module count/types scale small→huge. Markers = vendor / mission board / heal-tank /
  hangar interaction points.
- Placed by [`GameServerSpaceStations.cs`](../../src/BlocksBeyondTheStars.GameServer/GameServerSpaceStations.cs):
  **`StationGenerator.Generate(station.SizeTier, sSeed, _content)`** → stamped into a space instance;
  markers drive vendors/boards; NPCs spawned separately (`SpawnStationNpcs`).

### Settlements (villages / towns)
- [`SettlementGenerator`](../../src/BlocksBeyondTheStars.WorldGeneration/SettlementGenerator.cs) →
  `SettlementStructure` (`Width/Height/Length`, `ushort[]`, `Tier` = `village|town`, `Ruined`,
  `Inhabitant` = `human|alien`, `SettlementMarker`s, `BuildingCount`).
- Built from a **seed** by laying **buildings on plots** (8-stride grid, 6×6 footprint): villages are
  single-storey in the **biome's surface material**, towns multi-storey iron/glass; one building hosts a
  vendor + mission board. Markers = vendor / mission_board / npc / loot.
- Placed by [`GameServerSettlements.cs`](../../src/BlocksBeyondTheStars.GameServer/GameServerSettlements.cs):
  **`SettlementGenerator.Generate(tier, ruined, sSeed, surface, _content)`** → stamped onto terrain.

### The ship-editor pattern we reuse
- `ShipLayout` (voxel cells: `Kind` block/station/element + `Id`) in Shared; loaded from
  `data/ship_layouts/*.json` by `ContentLoader`; `GameContent.GetShipLayout`.
- `ShipEditor.cs`: free-fly build room, palette, metadata panel, **Export** bundle to
  `persistentDataPath/ship_exports/<key>/{ship.json, layout.json}`.
- `tools/merge_ship.py` folds a bundle into `data/`; `StampShipLayout` stamps the real shape.

**Key insight:** stations and settlements already *are* voxel grids + markers. A hand-designed
station/settlement is just an authored grid + markers — exactly what an editor produces, and trivially
stampable next to the procedural path.

---

## 1b. Current procedural output — size & variety assessment

Measured from the generators (block units).

### Settlement size
`Plot = 8`, `Building = 6×6`, `FloorH = 4`. `Layout(tier)`:
| Tier | Plots | Floors | Footprint (W×L) | Height | Buildings |
|---|---|---|---|---|---|
| village | 2×2 | 1 | **17×17** | 5 | ≤4 (each plot ~15% skipped → usually 3–4) |
| town | 3×3 | 2 | **25×25** | 9 | ≤9 |

So a village is a tiny 17×17 hamlet of 3–4 huts; a town a 25×25 block of up to nine 2-storey boxes.
**Only two sizes exist**, and every instance of a tier has the **same footprint** (no size variation
between worlds).

### Settlement variety — **LOW**
- **All buildings are identical**: one `StampBuilding` makes a hollow 6×6 box, window band at `y%4==2`,
  one door in the −Z wall, a corner ladder if multi-storey. No variation in footprint, height, **roof**
  (always flat/open-topped), shape, or per-building material.
- Material is uniform per tier: village = the **biome surface block** (one material), town = iron/glass.
- The only randomness: a village's 15% plot-skip (an open square), inhabitant human/alien (affects NPCs
  only, **not looks**), and the ruined decay pass (35% block removal + a little flora).
- No streets/paths, plaza, well, walls/palisade, fences, lamps, market stalls, signage, gardens,
  chimneys, or landmark building. Alien vs human settlements look **identical**.

### Station size
Room `7×6×7`, shared walls. `Layout(sizeTier)`:
| Tier | Modules | Floors |
|---|---|---|
| small | 3 | 1 |
| medium | 5 | 1 |
| large | 9 | 2 |
| huge | 14 | 3 |
Footprint depends on the random walk (e.g. small ≈ 13–19 wide; huge sprawls over 3 floors).

### Station variety — **MEDIUM (silhouette only)**
- The random-walk module layout gives **genuinely different exterior shapes** per seed — the strongest
  variety in the game's structures today.
- But **every module is the same hollow 7×6×7 shell** with one glass viewport band; the module *type*
  (hub/hangar/market/medbay/quarters/corridor) only changes the **marker**, never the interior or shape.
  Interiors are **empty** (no counters, tanks, bunks, consoles, props). The exterior has no antennae,
  solar panels, docking arms, or tubes — just stacked boxes.

### Can it be improved? — **yes, substantially.** Two complementary levers:
1. **Hand-designed templates (this plan's editors)** — the biggest, most direct win: drop authored,
   characterful stations/villages/towns into the world via the pools (§2.3). Memorable landmarks among
   the procedural filler, at zero generator risk.
2. **Procedural enrichment** (a separate pass; tracked here so it isn't forgotten):
   - **Settlements:** vary building footprint (4×4..8×8), height (1–3 storeys, even some village
     two-storeys), **roofs** (flat/pitched/domed), and per-building material; add a **central feature**
     (well / campfire / plaza / statue), **paths** between plots, a **perimeter wall/palisade** on some,
     **lamps**, **market stalls**, fences, gardens/flora; **theme by inhabitant** (alien = organic,
     non-grid, alien blocks; human = rectilinear) and by biome; add more size tiers
     (**hamlet → village → town → city**) with per-instance size variation.
   - **Stations:** **type-specific interiors** (market counters, medbay heal tanks, quarters bunks, hub
     control consoles, a docked shuttle in the hangar); **module shape variety** (long corridors, round
     observation domes); **exterior detail** (solar panels, antennae, docking arms, connecting tubes);
     more/with-purpose module types; bigger huge-tier sprawl.

This assessment feeds the phasing: **P1–P3 add the template lever** (highest ratio of variety to risk);
a later **P5 — procedural enrichment** pass tackles lever 2.

---

## 2. Design

### 2.1 Shared layout models (mirror `ShipLayout`)
Add to `BlocksBeyondTheStars.Shared.Definitions`:

```csharp
StationLayout    { Key, Width, Height, Length, SizeTier, List<LayoutCell> Cells, List<LayoutMarker> Markers }
SettlementLayout { Key, Width, Height, Length, Tier ("village"|"town"), string Inhabitant,
                   List<LayoutCell> Cells, List<LayoutMarker> Markers }
LayoutCell   { int X,Y,Z; string Id; }      // Id = block key (iron_wall, glass, light, …, or biome-relative "surface"/"wall")
LayoutMarker { int X,Y,Z; string Type; }    // vendor / mission_board / medbay / hangar / npc / loot …
```

- A `LayoutCell.Id` is a **block key**; settlements may use the sentinel `"surface"`/`"wall"` so a village
  re-tints to the host biome's material at stamp time (keeps the "village in the biome's material" look).
- Loaded by `ContentLoader` from `data/station_layouts/*.json` + `data/settlement_layouts/*.json`;
  exposed via `GameContent.GetStationLayout(key)` / `GetSettlementLayout(key)` (+ pools by tier, §2.3).

### 2.2 The editors (one reusable core)
The ship editor is ~one self-contained `MonoBehaviour`. Extract its reusable bones into a shared
**`StructureEditorCore`** (free-fly camera, place/remove raycast, `Dictionary<Vector3i,Id>` grid, IMGUI
palette + metadata via `UiScale`, export via `JsonUtility`), then three thin editors configure it with a
palette + metadata fields + export schema:
- **ShipEditor** (exists) — refactor onto the core (optional, low risk to defer).
- **StationEditor** — palette: `iron_wall / glass / light / door / floor` + **markers** vendor / mission
  board / medbay / hangar / quarters; metadata: key, name, **sizeTier**, blueprint/are-spawn weighting.
- **SettlementEditor** — palette: `wall / surface (biome) / glass / roof / door / light` + markers vendor /
  mission_board / npc / loot; metadata: key, name, **tier** (village/town), **inhabitant** (human/alien),
  ruined-allowed flag, spawn weight.

Each **Export** writes `persistentDataPath/<kind>_exports/<key>/{meta.json, layout.json}`.
Movement = free-fly (same as ship editor). The built voxel layout **is** the real structure.

### 2.3 World-gen template integration (the core ask)
Add **template pools** the placers consult *before* falling back to procedural — minimal, surgical:

- `GameContent` builds pools at load: `StationLayoutsByTier[sizeTier]`, `SettlementLayoutsByTier[tier]`
  (each layout carries a spawn `Weight`).
- A small `TemplateStamp` helper turns a `StationLayout`/`SettlementLayout` into the **same**
  `StationStructure`/`SettlementStructure` the generators return (so the placers/markers/NPC code are
  unchanged downstream).
- At the two call sites, gate by a **seed roll** vs a configurable `templateChance` (e.g. 0.5):
  ```csharp
  // GameServerSpaceStations
  var tpl = _content.PickStationLayout(station.SizeTier, sSeed);   // weighted, null if pool empty / roll says procedural
  var structure = tpl != null ? TemplateStamp.Station(tpl, _content)
                              : StationGenerator.Generate(station.SizeTier, sSeed, _content);
  // GameServerSettlements — same shape, keyed by tier + honouring `ruined`
  ```
- Deterministic: the **same world seed → same choice**, so persistence/tests stay stable. Empty pools →
  always procedural (today's behaviour, zero regression).

### 2.4 Merge tooling
`tools/merge_structure.py <kind> <bundle>` (or two thin wrappers) folds a bundle into
`data/{station,settlement}_layouts/<key>.json` + bilingual name placeholders, mirroring `merge_ship.py`.

---

## 3. Phasing

- **P1 — models + load + template stamp + integration (highest value, no editor yet):**
  `StationLayout`/`SettlementLayout` + `ContentLoader` + `GameContent` pools + `TemplateStamp` +
  the two call-site gates + `merge_structure.py`. Ship a **sample hand-authored JSON** for each so the
  pools are non-empty and tests cover "a template world-gen picks the template, markers intact."
- **P2 — StationEditor** (palette + markers + metadata + export), main-menu entry + locale.
- **P3 — SettlementEditor** (village/town, inhabitant, biome-relative cells), main-menu entry + locale.
- **P4 — polish:** extract `StructureEditorCore` (refactor ShipEditor onto it), undo, mirror, a 3D
  preview, per-pool spawn-weight tuning, `templateChance` as a server rule.
- **P5 — procedural enrichment (separate from the editors; lever 2 from §1b):** lift the *procedural*
  generators. Independent of P1–P4; run in slices, each updating the stamp tests.
  - **DONE (slice 1):** settlements — per-instance size jitter (towns 3–4 plots, 2–3 storeys), per-building
    footprint/height/**roof** (flat parapet / pitched)/door-side/accent-band variety, a **central feature**
    (well / monument / garden plaza), **street paths**, lamp posts + gardens by doors, an optional
    **perimeter fence**, and **alien theming** (alien materials + denser growth). Stations — **type-specific
    interiors** (hub consoles, market counter, medbay heal tank, quarters bunks, hangar crates, corridor
    guide lights) + corner ceiling lights in every room.
  - **DONE (slice 2):** station **exterior detail** — a reserved hull margin + solar-panel wings on
    exposed faces, roof antennae with beacon tips, and a stepped **command dome** on the hub. Settlements
    gained **four size tiers** (hamlet → village → town → city) chosen weighted at the placement site,
    with tier-fitting name suffixes.
  - **DONE (slice 3):** station **module shapes** — round command hub, ~35% octagonal "round" modules
    (chamfered corners), glass **observation domes** on round tops, **connector conduits** along the
    roofs between adjacent top modules. Settlements got **biome theming** — biome-specific garden flora
    (cactus/frostflower/mushroom/fern/emberbloom; alien = crystal), biome-ground paths (sand/ice), and
    flat adobe roofs on desert worlds.
  - **Dropped:** genuinely different module *footprints* (long corridors) would need the shared-wall grid
    to flex — decided against (higher risk, low marginal payoff; the shape/greeble work above covers the
    visible variety). **P5 is complete.**

P1 delivers the requested **"world-gen picks from a list of hand-designed types in addition to
procedural"** even before the editors exist (author a couple of templates by hand / via the ship-editor-
style export). The editors (P2/P3) then make authoring them in-game easy.

---

## 4. Open questions
1. **Marker parity:** confirm the full marker vocabulary each editor must expose (vendor, mission board,
   medbay/heal-tank, hangar, quarters, npc spawn, loot) so authored structures are fully functional.
2. **Biome-relative cells:** keep the `"surface"`/`"wall"` sentinels (village re-tints to biome) or bake a
   fixed material per template? (Sentinels keep one village usable on any planet.)
3. **`templateChance`:** fixed constant, per-pool weight, or a server rule (admin-tunable)?
4. **Size/tier coverage:** do we require a template per size tier / per tier before enabling pools, or
   weight-mix templates with procedural per roll? (Plan assumes weighted mix, procedural fallback.)
5. **Validation:** how strict at merge time (must contain ≥1 vendor? a door? fit a max footprint)?
6. **Editor core refactor:** do P1–P3 first and refactor in P4, or extract `StructureEditorCore` up front?
   (Plan defers the refactor to reduce risk.)
