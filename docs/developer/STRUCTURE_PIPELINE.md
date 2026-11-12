# The structure pipeline — how built structures get into a world

Epic #3510 (#3511–#3524, terrain generation 38). Every built structure — a village, a station, a monument, a wreck, a
vault, a landmark — goes the same way: a **source** builds it, **post passes** shape it, the **placement** seats, records and
writes it, and on later loads it is **replayed** from its record. The editor runs the same sources, so it can show and start
from every kind. This is the *how it fits together* reference; the kinds themselves are documented where they live
([WORLD_GENERATION.md](WORLD_GENERATION.md), [LANDMARKS.md](LANDMARKS.md),
[STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md), [FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md),
[DERELICT_STATIONS.md](DERELICT_STATIONS.md), [SHIP_TYPE_EDITOR.md](SHIP_TYPE_EDITOR.md)).

```
 source (template | kit | generator entry | generator)      → BuiltStructure / the stamper's structure type
   └─ post passes: material tokens → quarter turn → furnishing → ruin pass (StructureRuiner) → marker filter
 placement: structure_types.json rule → roll (count pinned) → seat (guaranteed search + SeatPolicy)
   → StructureFootprints (keep-clear, register) → StructurePlacementRecord → writer (once, FeatureStamped)
 replay (every load): record → same source + params + seed → markers, loot, residents (blocks are never rewritten)
```

## 1. Sources (`WorldGeneration/Sources`)

`IStructureSource` — `Key`, `Kind` (surface / underground / space / ship), `LabelKey` (`ui.source.<key>`),
`SupportsIntact`, `GetParams(content)` (typed parameters with defaults and locale labels `ui.source.param.<key>`) and
`Build(StructureBuildRequest)` → `BuiltStructure` (dimensions, block ids, tints / glows, shapes, cavity cells, markers,
ground row, `ContentHash`). `StructureSources.All` is the one registration list, in a pinned order; `TryParseParams` /
`FormatParams` read and write a `key=value;…` settings line.

| Group | Sources |
|---|---|
| Settlements and ruins | `settlement`, `fallen_ruin`, `city`, `landmark`, `ancient_ruin` |
| Camps and sites | `bandit_camp`, `lodge`, `pioneer_outpost`, `sps_lab`, `factory`, `rail_station`, `rail_ruin`, `monument`, `text_tablet`, `wreck` |
| Space | `station`, `station_kit`, `derelict` |
| Builders (formerly direct writers, #3522) | `vault`, `singing_shrine`, `sealed_observatory`, `mirun`, `ornasa`, `choir_tower`, `guardian_core`, `hunter_camp` |

Every wrapper calls its generator exactly like the server does and changes nothing (`StructureSourceTests.Identity`).
`StructureBuildRequest.Generation` selects the generation-38 behaviour (decay passes, the layout wreck), `AsInWorld` the
generator's own decay where an intact form exists (`wreck`, `landmark`).

The **builders** (`VaultBuilder`, `UniqueSiteBuilder`, `SoaniriBuilder`, `GuardianCoreBuilder`, `HunterCampBuilder`) emit
cells through `IStructureCellSink` in exactly the order the server wrote them before; the server's `WorldCellSink` writes
them into the world, the editor's `FlatGroundSink` collects them on flat ground. Write traces before and after the move are
byte-identical.

## 2. Templates, kits and generator entries

Authored `StructureTemplate`s (whole structures, kit modules, landmarks, underground templates) and kits keep their pools
([STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md) §3b–3d, [LANDMARKS.md](LANDMARKS.md)). New:

- **Generator entries** (#3518) — a template with `source` + `sourceParams` and no cells. It sits in the settlement, city,
  station, landmark and underground pools with pack, weight and planet types, appended **after** the existing entries so
  every existing draw keeps its mapping (pickers clamp weights to ≥ 1, so an entry is filtered, never weighted 0). Records:
  `source:<entry key>` (settlements, cities, stations, underground), `src=` in a landmark's pin. A replay goes through its
  own prefix branch: a missing entry keeps the blocks and re-derives nothing (a station, stamped every session, falls back to
  the procedural interior). Entries never turn. An entry is built **from its seed and parameters alone**
  (`StructureBuildRequest.AsEntry`): a `settlement` or `city` entry is purely procedural (no plot modules, no kit — its
  `kit` parameter must stay empty), and `station_kit`, which composes from the live kit pool, is no entry at all — so a new
  module or kit never changes what an existing world replays. A metropolis-tier entry is a city world's city and stands on
  its landing pads: only the `city` source, which keeps them open, may fill it. `SourceTemplates.Build` never throws — a
  broken entry is logged and treated as unbuildable. Loader: `SourceTemplates.Validate` drops an unknown source, a source
  that may not stand as an entry, bad parameters, a metropolis entry of another source, an entry with cells or a module
  role, and a kit entry that names an entry; no template at all may take a key the records reserve (`procedural`,
  `kit:…`, `source:…`, `StructureCatalog.IsReservedTemplateKey`).
- **Generation 38** (#3519): chest (only with settings — a bare chest marker stays village furniture), `sound` and `den`
  markers work in whole settlement templates, settlement / city modules and whole station templates as in landmarks
  (`GameServerStructureMarkerLife.cs`); keep cells are honoured wherever a ruin pass runs; whole settlement templates take the
  normal ruin roll (no longer dropped on ruined-only worlds) and turn by a hash of the instance seed (`turns=` in the
  record — on every path that stamps one, the legacy re-derive of settlements switched on after a first visit included);
  station templates resolve material tokens (`ModuleMaterials.ForStation`). A station pinned to a complete template replays
  it even when the template coin says no (older worlds keep their old replay exactly).

## 3. Post passes and the ruin pass

`StructureRuiner` is the one ruin pass ([LANDMARKS.md](LANDMARKS.md) §3 for its steps). It runs at a pinned **revision**
(a landmark record's `rev` is read back; missing = 1) with `RuinOptions` (defaults = the landmark behaviour): overgrowth
off / a fixed flora, a weighted rubble palette, `ErodeShaped`, a per-column strength map (`SpareBox`), protected rows, a fixed
switched-off light, substitutions and scorch — plus the profile **`hull`** (no ground: settle anchors on keep cells and the
largest piece; no crumble, rubble or overgrowth) for wrecks. Options are not part of the revision: every caller that sets
them gates on generation 38. `StructureDecay` pins the revision the generation-38 decays use, so a later ruiner revision can
never move a monument's relic cache or a G.D.S. crate that is re-derived from the decayed shape on every load.

| Decay | Generation 38 |
|---|---|
| Fallen settlements, standalone ruins | masonry pass with a spared plot; settle fixes floating storeys; `StampBrokenFeature` stays as shape |
| Monument erosion | masonry, shaped pieces erode, no overgrowth on airless bodies, rubble = surface, `protectY` rows kept; archetype breaks, runes and the cache after the pass |
| G.D.S. buried ruin | masonry, entrance and Ressaw kept, rubble scrap / rusted panel / deepslate, lights off; sinkholes, Ressaw walls, tube, basins, ore, props stay |
| SPS labs | built intact, then a weak pass (scrap, rusted panels); the lab module stands as built (keep cells: roof and walls whole) |
| Box wreck | stays for old worlds; the layout wreck uses `hull` (§6) |
| Rail ruin, derelict stations, vault ring, Ornasa | kept as design — the brokenness is the authored shape or tied to gameplay outputs |
| Rune shrines, stone circle, wall fragment | chunk generation, unchanged |

## 4. Placement

- **Rules as data** (#3517): `data/structure_types.json` (`StructureTypeRules`) — per kind `enabled`, `minGeneration`,
  `chance`, `density`, count `thresholds` / `thresholdCaps` / `minCount` / `cap`, `seatPolicy`, `keepClear`,
  `keepClearFrom`, `conditions` (`atmosphere`, `breathable`, `notGas`, `notVoid`, `notAsteroid`, `notRuinedOnly`,
  `notCityWorld`, `namedByType`) and the structure-whitelist key. The shipped values equal the old constants
  (`StructureTypeRulesTests`); a broken entry is reported and its kind keeps the defaults; `minGeneration` can be raised,
  never lowered below the C# default (it would switch a kind on in older worlds). Not every kind reads `conditions` and the
  whitelist — the class summary of `StructureTypeRules` lists which. `Place*` world switches stay switches;
  `ServerConfig.IntercityRailChance` / `RailRuinChance` / `LandmarkChance` override the file when set — a stored value
  equal to the old code default (0.6 / 0.35 / 0.34, which every older `server.json` holds) reads as unset, and an
  override that differs from the file is logged once.
- **Counts and verdicts are pinned** (#3513): `WorldMetadata.StructureCounts["<location>|<kind>"]`, the record fields
  `Tier`, `Ruined` and `Design` — written the first time a stamper runs (also on the next load of an old save), read back
  from then on, after the original draws were consumed. Changing a number in the file therefore only affects bodies stamped
  afterwards.
- **Footprints** (#3514): `StructureFootprints` (one per loaded world) — pads, the crash site and every stamped structure
  register their box on stamp and on replay; `Overlaps(x, z, halfExtent, margin, exceptKind?)` with the larger of the
  asker's `keepClear`, its `keepClearFrom[kind]` and the entry's own margin. Generation 38 uses it for every keep-clear check;
  older worlds keep their exact old checks (data cubes and chests have no records there, a wider check would move them).
- **Protection** (generation 39): every whole template carries `protection` (`auto` / `protected` / `open`, settable in
  the editor's *Use as* form). A protected box rides on the structure's footprint entry (`GuardBox`) and one query,
  `IsProtectedStructureCell` (`GameServerStructureProtection.cs`), sits beside the station check at every protection
  site; `auto` keeps each kind's old rule. Details in [LANDMARKS.md](LANDMARKS.md) §5.
- **Records**: every stamped kind has a `StructurePlacementRecord` (new kinds `ruin`, `monument`, `text_tablet`, `chest`,
  `datacube`, `guardian_core`; monuments and text tablets also keep their old feature strings). A fresh procedural
  settlement at generation 38 is recorded as `procedural`, never as the ambiguous empty template (only a generation-38
  world reads that marker back). An older save gets its records backfilled on its next load from what stands; the
  Guardian core chamber, which every older load wrote, is marked as stamped without being written again
  (`StructureBookkeepingTests.OldSave`). The planet wreck's record keeps its hull's extent (`Width` / `Length`,
  generation 38): later loads reserve that box as the crash site instead of sizing it by a fresh pick.

## 5. User content

The local server reads `usercontent/` next to `data/`: station and settlement templates, kits, landmark templates, generator
entries (template files without cells) and **ship types** (#3520): `usercontent/ship_types/<key>.json`, one file with the
definition and the embedded layout. User ships live in `GameContent.UserShips`, never in `Ships` (the wreck pools of old
worlds and the creative grant enumerate it); `GetShip` / `GetShipLayout` resolve both. A key the game reserves (`custom`
— the self-built ships' type — `starter`, any shipped ship or layout, case-insensitive) is refused by the loader, and the
editor writes such a ship under `<key>_2`. `uses` (none named = `npc` only): `npc` (raiders, trader flights), `wreck` (new
wrecks; pad-fitting ships only — opt-in, a repaired wreck is claimed), `craft` — crafting additionally needs the server
option `AllowUserShipCrafting`, otherwise any client could craft a free ship; this version's build menu does not list user
ships, so `craft` only answers a `CraftShipIntent`. Every pool takes a user ship only from a pack the world enables. An
owned user ship whose file is gone keeps its type and its stored name (`ShipState.DisplayName`) and flies as its stored
cells or else the starter hull without its edits.

## 6. Wrecks from the ship layout (#3521, generation 38)

`ShipCellBlocks` is the one cell → block mapping for the flight hull and the wreck. `WreckGenerator.GenerateFromLayout`
builds the real hull (doors open, interior cleared, blocks without a placing item → `iron_wall` so the repair can complete,
alien → crystal), decays it with the `hull` profile plus carbon scorch and a gash at the rear door, and puts loot / module /
terminal markers beside the station cells. The planet wreck's record carries the ship key and `src=layout;rev=1;fp=<hash>`;
a changed or missing layout keeps the blocks and refuses repair and claim. Space wrecks pin their design in
`WorldMetadata.SpaceWreckDesigns` and its layout's fingerprint in `WorldMetadata.SpaceWreckLayouts`; a layout changed since
drifts as the box of that design (warned), a design that is gone as the box of the drawn one.

## 7. The editor

The town and station editors generate **any** source (picker, parameters, seed, *intact / as in the world*) and load the
result as editable cells; *Use as → Generator entry* saves an entry (never over a design of the same key, nor a design over
an entry — `UserTemplateFiles`; the fields the form has no row for are written back as loaded; an entry whose source is
unknown or does not fit its pool is flagged and not saved until a source is picked); a panel lists what happens when the
design is stamped; quarter-turn and ruin previews run the server's own code. See
[STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md).

## 8. Adding a kind

1. Write the generator / builder in WorldGeneration (netstandard2.1, no `UnityEngine`), deterministic from seed + params.
2. Register a source in `StructureSources.CreateAll()` (+ `PinnedKeys`, `ui.source.<key>` in en/de).
3. Add its rule to `data/structure_types.json` and the C# defaults.
4. Stamp it in the structures boot stage: rule → pinned count → seat → `StructureFootprints` → record → write once.
5. Replay from the record on every load; add a generator golden and a placement case.

## 9. Key files

| Area | Files |
|---|---|
| Sources | `WorldGeneration/Sources/*`, `StructureCellSink.cs`, `*Builder*.cs` |
| Ruin pass | `StructureRuiner*.cs`, `VoxelGrid.cs`, `RuinMarkers.cs`, `StructureDecay.cs`, `*Generator.Ruin.cs` |
| Templates | `Shared/Definitions/StructureTemplate.cs`, `Shared/Content/StructureCatalog*.cs`, `Sources/SourceTemplates.cs`, `SettlementGenerator.Templates.cs`, `GameServerSourceEntries.cs`, `GameServerStructureMarkerLife.cs` |
| Placement | `data/structure_types.json`, `Shared/Definitions/StructureTypeRules*.cs`, `GameServer/StructureFootprints.cs`, `GameServerStructureFootprints.cs`, `GameServerStructureRecords.cs` |
| Ships and wrecks | `Shared/Definitions/UserShip.cs`, `GameContent.UserShips.cs`, `GameServerUserShips.cs`, `ShipCellBlocks.cs`, `WreckGenerator.Layout*.cs`, `WreckLayoutHull.cs`, `GameServerWrecks.Layout.cs` |
| Tests | `StructureGoldens*`, `StructureSourceTests*`, `StructureBookkeepingTests*` (`.OldSave`: a simulated pre-pipeline save), `StructureRuinerTests*`, `StructureDecayTests*`, `TemplatePipeline*Tests`, `StationTemplatePinTests`, `StructureTypeRulesTests`, `StructureFootprintTests`, `UserShipTests`, `WreckLayout*Tests`, `SpaceWreckLayoutTests`; Client.Tests `UserShipFileRulesTests`, `UserTemplateFilesTests`, `StampStepsDescriberTests` |
