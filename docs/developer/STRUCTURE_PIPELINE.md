# The structure pipeline — how built structures get into a world

Epic #3510 (#3511–#3524, terrain generation 38). Every built structure — a village, a station, a monument, a wreck, a
vault, a landmark, a giant ship wreck, a lost civilization's site — goes the same way: a **source** builds it, **post passes** shape it, the **placement** seats, records and
writes it, and on later loads it is **replayed** from its record. The editor runs the same sources, so it can show and start
from every kind. This is the *how it fits together* reference; the kinds themselves are documented where they live
([WORLD_GENERATION.md](WORLD_GENERATION.md), [LANDMARKS.md](LANDMARKS.md), [GIANT_SHIPS.md](GIANT_SHIPS.md),
[CIVILIZATIONS.md](CIVILIZATIONS.md),
[STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md), [FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md),
[DERELICT_STATIONS.md](DERELICT_STATIONS.md), [SHIP_TYPE_EDITOR.md](SHIP_TYPE_EDITOR.md)).

```
 source (template | kit | generator entry | generator)      → BuiltStructure / the stamper's structure type
   └─ post passes: material tokens → quarter turn → furnishing → ruin pass (StructureRuiner) → marker filter
                   → dress (a civilization's props, #3641)
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
| Lost civilizations (#3645) | `maze_ruin` — the Labyrinth Builders' maze (`MazeRuinPlan` + `MazeRuinGenerator`): `size` S / M / L (≈ 80 / 120 / 180 blocks across), `height` (walls above the floor, default 20), `braid` (% of dead ends opened into loops), `halls`, `statues` (% of roomy dead ends with a statue), the blocks `wall` / `accent` / `floor` by key (a source builds block ids, so the civilization's materials travel as parameters). Built intact; it falls through the ruin pass as a landmark-style entry with `ruin: always` ([CIVILIZATIONS.md](CIVILIZATIONS.md) §7) |

Every wrapper calls its generator exactly like the server does and changes nothing (`StructureSourceTests.Identity`).
`StructureBuildRequest.Generation` selects the generation-38 behaviour (decay passes, the layout wreck), `AsInWorld` the
generator's own decay where an intact form exists (`wreck`, `landmark`).

**Kept cells from a source** (#3645). A source may name cells the shared ruin pass must leave standing —
`BuiltStructure.Keep`, the keep brush of an authored template (the maze ruin keeps its lowest three wall rows, so its plan
stays readable whatever falls). Both converters that feed a ruin pass, `BuiltConverters.ToSettlement` and
`ToLandmarkLayout`, copy them into the working grid's keep bits. `ContentHash` mixes them in only when there are any, so every
source that keeps nothing hashes exactly as before. The editor's bake of a source does not carry them yet (it loads blocks,
cavities and markers only).

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
- **Civilization templates** (#3639, generation 43) — landmark-style templates in `data/civilizations/<key>/`, offered by
  no pool but the civilization stage (`civilization`, `civPool` giant / city / small / prop, `propPool`; a generator entry
  is allowed in a site pool, never as a prop). Their material tokens resolve to the civilization's `materials`
  (`CivilizationLayouts.MaterialsOf`: a village's set with each named token replaced). Two additions any landmark-style
  reader now knows:
  - the cell / palette kind **`air`** (#3640) — an **air pocket**: a cavity that stays air even under water. The compact
    body keeps it apart from `cavity` (`TemplateBody.Plain.Air`, always a cavity too, so every reader that knows cavities
    treats it as one); `LandmarkLayout.IsAirPocket` carries it to the writer;
  - the marker **`prop`** (#3641, `pool=<propPool>` or `template=<key>`) — kept by the ruin marker filter
    (`RuinMarkerRules.Kept`) and dressed after the pass (§3).

  Only a civilization template may name the seat `seabed`, and a lost civilization's template carries no resident, vendor
  or den marker. Details: [CIVILIZATIONS.md](CIVILIZATIONS.md) §2.

## 3. Post passes and the ruin pass

`StructureRuiner` is the one ruin pass ([LANDMARKS.md](LANDMARKS.md) §3 for its steps). It runs at a pinned **revision**
(a landmark record's `rev` is read back; missing = 1) with `RuinOptions` (defaults = the landmark behaviour): overgrowth
off / a fixed flora, a weighted rubble palette, `ErodeShaped`, a per-column strength map (`SpareBox`), protected rows, a fixed
switched-off light, substitutions and scorch — plus the profile **`hull`** (no ground: settle anchors on keep cells and the
largest piece; no crumble, rubble or overgrowth) for wrecks. Options are not part of the revision: every caller that sets
them gates on generation 38. **Revision 3** (#3609) is the giant ship wrecks' ([GIANT_SHIPS.md](GIANT_SHIPS.md) §4): it
changes only `hull` and adds the profile **`crash`** (the hull decay anchored on a buried keel, with rubble and — with
air — overgrowth); every landmark profile builds what revision 2 builds, and revisions 1 and 2 stay byte-identical. The
option `FreezeWater` turns water into ice (a wreck in space or on an airless body). `StructureDecay` pins the revision the generation-38 decays use, so a later ruiner revision can
never move a monument's relic cache or a G.D.S. crate that is re-derived from the decayed shape on every load.

A civilization site (#3639) runs the landmark pass at the revision a fresh landmark takes, with its civilization's
`RuinOptions` (`CivilizationLayouts.RuinOptionsOf`: its rubble palette, its overgrowth block — none on an airless body,
and under water only an explicit one such as `flora_seagrass`). Two steps are new and leave every older caller
byte-identical: an **air pocket** is a cavity and a keep cell, so no cave-in fills it and no rubble lands in it, the
overgrowth never grows into it (`StructureRuiner.Settle.cs`) and a ruin zone's window keeps it one
(`StructureRuiner.Zones.cs`); and an optional
**`LayoutDress`** step runs on the grid after the pass and the marker filter (`LandmarkGenerator.Finish`) — the
civilizations' `CivilizationPropDress` stands a prop of the marker's pool or template on every `prop` marker, picked and
turned by a hash of the seed and the cell, written only into empty cells (never into a wall, a cavity or an air pocket).
The prop markers stay in the layout.

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
  `chance`, `chanceFrom` (#3631: generation → chance — a world reads the entry of the highest generation at or below its
  own, `chance` below every entry, `StructureTypeDefinition.ChanceFor`; a key before the kind's own C# generation, a key
  that is no whole number or a chance outside 0..1 drops the entry; the intercity line has `{ "42": 0.8 }`), `density`,
  count `thresholds` / `thresholdCaps` / `minCount` / `cap`, `seatPolicy`, `keepClear`,
  `keepClearFrom`, `conditions` (`atmosphere`, `breathable`, `notGas`, `notVoid`, `notAsteroid`, `notRuinedOnly`,
  `notCityWorld`, `namedByType`) and the structure-whitelist key. The shipped values equal the old constants
  (`StructureTypeRulesTests`); a broken entry is reported and its kind keeps the defaults; `minGeneration` can be raised,
  never lowered below the C# default (it would switch a kind on in older worlds). Not every kind reads `conditions` and the
  whitelist — the class summary of `StructureTypeRules` lists which. `Place*` world switches stay switches;
  `ServerConfig.IntercityRailChance` / `RailRuinChance` / `LandmarkChance` override the file when set — a stored value
  equal to the old code default (0.6 / 0.35 / 0.34, which every older `server.json` holds) reads as unset, and an
  override that differs from the file is logged once. `GiantWreckChance` (#3608) overrides the `giant_wreck` chance
  (0.05) the same way but never held a written-out default, so every stored value counts.
- **The giant wreck rule** (`giant_wreck`, generation 42, #3608): chance 0.05 × the structures frequency × the world
  option `GiantWrecks`, conditions `notVoid` and `notGas` (airless bodies and asteroids allowed; the body must be four
  footprints around), whitelist key `giant_wrecks`, keep-clear 6 (24 from pads, 10 from the crash site, 16 from a
  landmark), the landmark's site (high prominence, flat relief) and grade (radius 16, cut and fill 24). Switched by
  `ServerConfig.PlaceGiantWrecks` and the world option; stamped by `StampGiantWrecks` right after the landmarks.
- **The civilization kind** (`civilization`, generation 43, #3639): no roll — `chance` and `density` are 0, because where and
  how many sites stand come from the civilization files (homes pinned in `WorldMetadata.CivilizationHomes`, a count per site
  pool; [CIVILIZATIONS.md](CIVILIZATIONS.md) §3–§4). The rule supplies the footprint registry kind, keep-clear 6 (24 from the
  pads, 10 from the crash site) and the landmark's site and grade (a land site reserves its ground; a sunken one, seated by
  the `seabed` search, does not). The stage reads neither `enabled` nor `minGeneration` of the entry: it is gated by the
  world's terrain generation and the pinned homes. Its place in the structures boot stage: text tablets → Sounder world →
  **civilizations** → settlements (+ intercity line) → ruins → … → landmarks → giant wrecks → buried ruins → …, so every
  later stamper keeps clear of the sites (`OverlapsLandmark` includes them). On a home body the kinds its
  `homeBody.suppress` names (`landmark`, `giant_wreck`) record a skip instead of rolling.
- **Counts and verdicts are pinned** (#3513): `WorldMetadata.StructureCounts["<location>|<kind>"]`, the record fields
  `Tier`, `Ruined` and `Design` — written the first time a stamper runs (also on the next load of an old save), read back
  from then on, after the original draws were consumed. Changing a number in the file therefore only affects bodies stamped
  afterwards.
- **Footprints** (#3514): `StructureFootprints` (one per loaded world) — pads, the crash site and every stamped structure
  register their box on stamp and on replay; `Overlaps(x, z, halfExtent, margin, exceptKind?)` with the larger of the
  asker's `keepClear`, its `keepClearFrom[kind]` and the entry's own margin. Generation 38 uses it for every keep-clear check;
  older worlds keep their exact old checks (data cubes and chests have no records there, a wider check would move them).
- **Site rules and reserved ground** (generation 40, #3563–#3571): each kind's `site` object says where it likes to stand
  (fresh-water and coast distance bands, `prominence` high/low, `relief` flat/rugged, an `altitude` band, `ground`
  blocks, `weights`) — the guaranteed search tries its first ring best-first by it, every hard check unchanged — and its
  `grade` object (`radius`, `maxCut`, `maxFill`) how a fresh stamp seated `flat` / `slope` / `shelf` reserves its
  ground: the footprint joins the world's site flats before the stamp writes, the generator levels and grades it, the list
  is pinned in `WorldMetadata.BodySites` and read back in the landing-pad stage of every later load. Only during the boot's
  structures stage of a body without edits (never an admin placement). The foundation, skirt and shelf fill come from the
  biome at the site. Details in [WORLD_GENERATION.md](WORLD_GENERATION.md) §55.
- **Planned settlements** (generation 41): a planned kit (grid or grown) seats first and is planned into the site's ground
  (`SettlementPlanner`); the plan is pinned in `StructurePlacementRecord.Plan` and baked on every replay
  (`SettlementPlanBaker`). The structure carries a column map (`SettlementStructure.ColumnFloors`): the stamper, the
  reserved ground (one site per lot and road stretch), the protection and the creature town zone follow the claimed
  columns, not the box. Details in [WORLD_GENERATION.md](WORLD_GENERATION.md) §56.
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
  generation 38): later loads reserve that box as the crash site instead of sizing it by a fresh pick. A giant wreck
  (kind `giant_wreck`) pins `turns`, `rev=3`, `seed`, `air`, `fp` (the template fingerprint) and `storage=overlay`; it is
  replayed whatever the switches say, and a changed or missing ship keeps its blocks and re-derives nothing. A
  civilization site (kind `civ:<key>`, index = the site on its body) pins `turns`, `rev`, `seed`, `src` (a generator
  entry's source), `wt` (the water top of a sunken site), `name` (the pool's map name) and `storage=overlay`; its cells are
  one structure overlay `civ:<key>:<index>`. It is replayed whatever the content says now; a template that left the content
  (or changed between authored and entry) keeps its blocks and re-derives nothing.

## 5. User content

The local server reads `usercontent/` next to `data/`: station and settlement templates, kits, landmark templates, giant
ships (`usercontent/giant_ship_templates/`, #3607 — checked like the shipped ones, a broken one dropped with a warning),
generator entries (template files without cells) and **ship types** (#3520): `usercontent/ship_types/<key>.json`, one file with the
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

The **giant ship wrecks** (#3625) are no ship layouts and never ship types: authored templates (`data/giant_ships/`) built
with ruin revision 3 — on a planet stamped like a landmark, in space a derived station structure that stores only the
players' changes ([GIANT_SHIPS.md](GIANT_SHIPS.md)).

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
| Giant ship wrecks | `Shared/Definitions/GiantShipRules.cs`, `GameContent.GiantShips.cs`, `WorldGeneration/GiantShipGenerator*.cs`, `StructureRuiner.Giant*.cs`, `GiantWreckPlacer.cs`, `GameServerGiantWrecks*.cs`, `GameServerGiantDerelict*.cs` — see [GIANT_SHIPS.md](GIANT_SHIPS.md) §12 |
| Lost civilizations | `Shared/Definitions/CivilizationDefinition.cs`, `CivilizationRules.cs`, `GameContent.Civilizations.cs`, `WorldGeneration/CivilizationHomes.cs`, `CivilizationLayouts.cs` (materials, ruin options, `CivilizationPropDress`), `UniverseGenerator.Civilizations.cs`, `MazeRuinPlan.cs`, `MazeRuinGenerator.cs`, `Sources/MazeRuinSource.cs`, `GameServerCivilizations.cs` (stage, record), `GameServerCivilizationStamp.cs` (seats, water write mode), `GameServerCivilizationsWorld.cs` — see [CIVILIZATIONS.md](CIVILIZATIONS.md) |
| Tests | `StructureGoldens*`, `StructureSourceTests*`, `StructureBookkeepingTests*` (`.OldSave`: a simulated pre-pipeline save), `StructureRuinerTests*`, `StructureDecayTests*`, `TemplatePipeline*Tests`, `StationTemplatePinTests`, `StructureTypeRulesTests`, `StructureFootprintTests`, `UserShipTests`, `WreckLayout*Tests`, `SpaceWreckLayoutTests`, `CivilizationFrameworkTests`, `CivilizationStampTests`, `MazeRuinSourceTests*`; Client.Tests `UserShipFileRulesTests`, `UserTemplateFilesTests`, `StampStepsDescriberTests` |
