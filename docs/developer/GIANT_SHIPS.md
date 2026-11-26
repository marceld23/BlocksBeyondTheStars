# Giant ships — huge authored spaceships that exist only as wrecks

> Status: **implemented** (epic #3625, parts #3605–#3624, 2026-11-14). On planets from terrain generation **42**
> (`WorldDescription.GiantWrecksGeneration`, a server-side stamp — no chunk of any generation changes); in space as a
> galaxy pass of its own that reaches older saves too, like the derelict stations. Five ships ship (§8) plus the pin-only
> `sample_hulk`. Their scripts: `tools/giant_ships/<key>.py`, run by `tools/gen_giant_ships.py`;
> `tools/landmarks/preview.py <file>` renders a PNG; `BlocksBeyondTheStars.Tools giantship <key> --check` checks one.

A **giant ship** is a huge authored spaceship — up to 256 blocks on every axis — that nobody can build. The game knows
it only as a **wreck**, derived from the intact design with the instance's seed: crashed long ago on a planet (the
landmark machinery, ruin profile `crash`) or drifting in space (the derelict-station technique, ruin profile `hull` plus
the derelict decay). Every wreck is abandoned and everyone got away. This is the *how it fits together* reference; the
pieces it reuses are documented where they live: [LANDMARKS.md](LANDMARKS.md) (template body, ruin pass, overlay storage,
far silhouette), [DERELICT_STATIONS.md](DERELICT_STATIONS.md) (boarding, no air, spacewalk, guardian bay),
[STRUCTURE_PIPELINE.md](STRUCTURE_PIPELINE.md) (placement rules, records), [STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md)
§3f (the editor).

## 1. Why never a ship type

A giant ship is a `StructureTemplate` with `giantShip: true`, never a `ShipDefinition`: it has no blueprint, no cost and
no craft path, and it never enters `GameContent.Ships`, the user ships or any blueprint pool
(`GiantShipTemplateTests.NoShipTypeList_AndNoOtherPool_EverSeesAGiantShip`). A new entry in `data/ships.json` would show up
everywhere a ship type is enumerated — the raiders' design pool (`RaiderDesignPool`), the traders' pool (`TraderShipPool`,
which lands on 17 × 17 pads), the creative "every ship" grant, `CraftShipIntent` (a shipped ship without a blueprint —
the starter — could be crafted for free until #3606, §10), the old worlds' wreck picks (`GameServerWrecks.cs`,
`GameServerSpaceWrecks.cs`) — and a ship layout stores one JSON object per cell in a 48 × 32 × 48 editor room. A giant ship
instead uses the landmarks' compact body (palette + run-length rows) and its own pool, so the question "how do I build
one" has no answer by construction. An intact, flying giant ship (an NPC capital ship) would be a later step of its own.

The lore: the human ships belonged to the old SPS survey fleet *Meridian* ([LORE_STRUCTURE.md](LORE_STRUCTURE.md)); their
Codex texts (`giantship.<key>.desc`) say the crews left long ago and got away safe and sound. The crystal squid's builders
are unknown.

## 2. The template

One file per ship under `data/giant_ships/<key>.json` (the key defaults to the file name), plus the editor's own under
`usercontent/giant_ship_templates/` (`GiantShipRules.DataFolder` / `UserFolder`). `ContentLoader` reads both after the
loot tables (`LoadGiantShips`); the folder decides the kind (`kind: giant_ship`, `GiantShip = true`). A file that says it
is a landmark stays one and is refused by the check below.

| field | meaning |
|---|---|
| `key`, `name`, `nameKey` | `nameKey` = `giantship.<key>.name` (set by the pool when empty); the Codex text is `giantship.<key>.desc` by convention (`GiantShipRules.NameKey` / `DescriptionKey`) |
| `width` / `height` / `length` | 1..256 each (`GiantShipRules.MaxSize`) |
| `palette` + `rows`, `cells` | the landmarks' compact body ([LANDMARKS.md](LANDMARKS.md) §2): plain cells (block or cavity, tint, glow, shape, keep) run-length coded, markers in `cells`. **Concrete block keys only** — no material tokens, no new blocks |
| `groundRow` | on a planet the template row that lies on the ground — the rows below it are the keel, sunk into the rock (cavity cells there stay hollow) |
| `ruinStrength` | 0..1, 0 = the profile's own (0.5); used for both settings |
| `ruinZones` | optional, at most 16 boxes like a landmark's, profiles `crash`, `hull` or `lattice` (`GiantShipRules.ZoneProfiles` = `LandmarkRules.GiantShipProfiles`); in space every zone decays as `hull` with its own strength (`LandmarkRules.GiantProfileOf`) |
| `style` | empty = metal; `alien` = a crystal ship: scorch and rubble become crystal, no soot, no scrap |
| `weight`, `pack`, `planetTypes`, `pinOnly`, `minTerrainGeneration` | as for landmarks. The shipped ships set `minTerrainGeneration: 42`; a `pinOnly` ship is never rolled (admin stamp and tests only) |
| cell `keep` | never decays and holds what hangs on it: stairs, ladder shafts, the way to every deck |

**Markers** (`GiantShipRules.Markers`; a giant ship is abandoned, so no person, vendor or den is ever part of one):

| marker | settings | meaning |
|---|---|---|
| `entrance` | — | the way in. At least one; it stands on a solid floor with two free cells above it. The **first** one is the boarding arrival in space; on a planet every one is an open way inside |
| `door_slide` | `axis=x` \| `axis=z` | a 2 × 3 doorway between two rooms, the marker in its bottom cell at the low end. `axis=x`: the wall is a plane of constant x, the opening runs along z (z .. z + 1); `axis=z`: a plane of constant z, the opening runs along x. The derelict decay reads the rooms of a space wreck from these |
| `chest` | `table`, `items`, `chance`, `block` (`crate` / `wood_crate`), `hidden=1` | the landmark chest ([LANDMARKS.md](LANDMARKS.md) §2) |
| `sound` | `sound`, `radius`, `volume` | a bundled loop (§9) |
| `loot`, `data_terminal`, `relic_cache` | — | the classic loot markers a ruin keeps (planet wrecks spawn them; the shipped ships use chests only) |
| `sps_console`, `sps_lamp` | `face=±x\|±z` (console) | #3902, `GiantShipRules.BridgeMarkers`: a warship's bridge console and its lamps (§14). Valid, but not in `GiantShipRules.Markers` — the editor's palette does not offer them, and only a wreck in space keeps them |

**What makes one unusable.** `GiantShipRules.Problems(t, blockExists, isSolid)` — one walk over the body: a size of 1 to
256 per axis, at least one block, every block known and inside the box (a material token is an unknown block), the ground
row inside, the strength 0..1, the style, the protection word, the zones (`ZoneProblems`), every marker known and inside,
every `door_slide` with `axis=x|z`, at least one `entrance` on a solid floor with two cells of headroom, and never also a
module, an underground structure, a generator entry or a landmark. `GameContent.GiantShipProblems(t)` adds the content's
checks: a chest's `table` names a loot table that exists, and the **device rule** — no block of the categories `machine`,
`light` or `door` whose drop is an item every recipe of which needs a blueprint (a heal tank, a fabricator, a signal
display): a wreck is open to mining and would hand such a machine to anyone with a drill (`GiantShipDeviceTests`).
`StructureCatalog.SetGiantShips` drops a template with any problem with a warning — one broken file never breaks the load.

**The pool.** `GameContent.GiantShips` (pool order = ordinal file name), `GetGiantShip(key)` (pin-only ships too) and
`GiantShipsFor(packs, planetType, namedTypeOnly, generation)` — never pin-only, in the enabled packs, no younger than the
world's generation, on the planet type (empty `planetTypes` = any; with `namedTypeOnly` only ships that name the type).
Every other pool — settlements, stations, modules, landmarks, underground — leaves a `giantShip` template out with a
warning (`StructureCatalog.GiantShips.cs`).

## 3. Building one — the toolkit

- **`tools/landmarks/ships.py`** — `ShipDesign`, a `Design` (the landmark toolkit) with every ship helper: `loft` (a hull
  from cross-sections with a monotone cubic between stations and tapered bow and stern), `add_hull`, `tube` (barrels,
  tentacles, walkable tunnels), `deck`, `window_band`, `glass_dome`, `greebles`, `corridor`, `rooms` (each with a
  `door_slide` doorway), `slide_door`, `entrance`, `hull_door`, `lift_shaft` / `ladder` (always `keep`), `turret`,
  `nozzle`, the inherited `chest` and `sound`; `finish()` turns the empty cells inside a hull below the ground row into
  cavity; `problems()` mirrors `GiantShipRules.Problems`. Orientation: x across, y up, z along, **the bow points to +z**.
  The reference is [tools/landmarks/README.md § Giant ships](../../tools/landmarks/README.md#giant-ships).
- **`tools/landmarks/shipgeom.py`** — the pure geometry: sections (`ship_section`, `ellipse`, `superellipse`,
  `rounded_rect`, `polygon`), the loft, `Hull` (stored as row spans, inside / shell / surface queries), tubes.
- **`tools/giant_ships/<key>.py`** — one script per ship with a `build()` returning its `ShipDesign`
  ([README](../../tools/giant_ships/README.md)); `_demo_hull.py` shows every helper in build order (scripts starting with
  `_` run only with `--only`).
- **`tools/gen_giant_ships.py`** — runs the scripts and writes `data/giant_ships/<key>.json` (`--out` another folder,
  `--only`, `--check` = build and check only, `--list`, `--preview`). It refuses a ship (no file, exit code 1) that is
  over 256, has no standable entrance, a marker outside, a door without an axis or an unknown block or material token,
  and prints the stats, the bounding box, the glass share and the compact size with a deflate estimate.
- **`tools/landmarks/preview.py`** — renders a template (also a dumped wreck) to a PNG without Unity; a giant ship gets
  `--cut auto` (a plan above each walk row that carries doors, entrances or chests, a cross-section, a long section) and
  writes to `artifacts/giant-ship-previews/`; `--split` writes every panel as a file of its own.
- **The Tools command** — `BlocksBeyondTheStars.Tools giantship <key|file.json> [--data dir] [--check] [--setting
  intact|planet|space] [--seed N] [--airless] [--turns N] [--out file.json]` (`GiantShipTool.cs`,
  `GiantShipWreckDump.cs`): `--check` prints the stats and every problem `GameContent.GiantShipProblems` finds (exit code
  2 when there is one); `--out` writes the ship intact or as the wreck the server derives with that seed, as a compact
  template `preview.py` renders.
- **Lights.** `Design.write()` hangs every unshaped torch and lantern on what holds it (the light-mount pass,
  `tools/landmarks/lightmount.py`, [LANDMARKS.md](LANDMARKS.md) §7) — for a giant ship too, where nothing outside the hull
  counts as rock. The shipped ships carry no torch or lantern (their lamps are light blocks), so the pass changes nothing
  for them; `GiantShipGenerator` does not run the ruin pass's `LandmarkLights.DropUnheld`.
- **Props carry their form** (#4026). The stamp writes a cell's shape exactly as the template gives it — it never applies
  `PropShapes.DefaultPlaceShape` the way a player's placement does — so a script writes a prop with its place form:
  `Mat("rug", shape=sc.pack(sc.SHEET))`, `Mat("flower_pot", shape=sc.pack(sc.POT))`, `Mat("stretcher",
  shape=sc.pack(sc.TABLE))`. A plain `Mat("rug")` is a carpet cube (the battleship's and the ark's cabins until #4026).
  Exempt: `bed` (the abandoned-bed pass, §4, lays it out), `ladder` (drawn by its key on the nearest wall) and the
  carrier's rec-room rug, which lies *in* the floor row as an inlaid carpet. `TemplatePropShapeTests` guards every
  shipped giant ship and landmark. Changing a shipped ship's cells changes its fingerprint (§4): a space wreck with
  stored player changes stays away until its base comes out as stored again (§6), and a planet wreck already stamped
  keeps the blocks it was written with — accepted for this fix.

## 4. Ruin revision 3 — `crash` and `hull` (#3609)

`GiantShipGenerator` (WorldGeneration, pure and deterministic) reads the body with the landmark reader, runs the shared
`StructureRuiner` at **revision 3** (`GiantShipGenerator.RuinRevision` = `StructureRuiner.GiantRevision`) and filters the
markers. Revision 3 changes only the profiles `hull` and `crash` — every landmark profile builds exactly what revision 2
builds, and `StructureRuiner.Revision` (what a fresh landmark takes) stays 2. The ruin seed mixes the instance seed with the
setting and the ship's key, so a planet wreck and a space wreck of one seed never share a pattern. Revision 1 and 2's steps
were tuned on buildings a few dozen blocks across; on a 256-block hull of thin plating their cell-by-cell erosion read as
moth-eaten, not as a wreck. Revision 3 (`StructureRuiner.Giant.cs`, `StructureRuiner.GiantDecay.cs`):

- **No cell-by-cell erosion.** Every hole is a coherent shape: **collapse balls** whose radius grows with the integer
  cube root of the box (3–14), centred on a block the decay may take and placed until they took 0.04 + 0.2 × strength of
  the hull's own blocks (a share of the blocks, not the box, so a deck-filled carrier and a hollow shell lose alike), their
  faces shaped by value noise; and **bites** — small noise-shaped balls (radius 2–3), 0.08 × strength of the blocks, 85 %
  of them on the edge of a breach.
- **Thin parts are spared.** A ball or a bite lands in a slice across the long axis with the share of material that slice
  holds, so a tapered bow is hit far less and stays recognisable.
- **Hull breaks.** A hull whose long axis is at least 96 and three times each other side breaks across it into two or
  three sections: planes 2–4 cells thick, gently leaning, their faces jagged in noise (up to three more slices), never
  through a slice that holds a kept cell (so the kept shafts decide where a hull can break) and at least a fifth of the
  length apart.
- **Glass** is smashed in noise patches — a third to a half of the panes, more next to a breach — never at or below the
  ground row; **every light** becomes its switched-off twin.
- **Scorch in patches** round the breaches and break faces (within three cells, thinning out) plus a few large burn zones.
  A human hull keeps its plating and gets a **soot tint** (`GiantShipGenerator.SootTint` = `0x3A3734`), so the stampers
  copy every cell's tint; an alien hull gets crystal in the same pattern. Water and plants are never scorched.
- **`crash`** (a wreck on a planet): the rows up to the ground row (the buried keel) and the kept cells anchor; what no
  longer hangs on them falls and lies as **rubble** within the ground row and three rows above it — scrap metal, scrap
  piles, rusted panels, carbon, iron (`HumanRubble`), crystal for an alien ship; **overgrowth** only when the body has air.
  Cavity cells below the ground row stay carved air.
- **`hull`** (a wreck in space): no ground — every piece of at least `MinPieceCells` (400) touching blocks stays where it
  is, the largest always, the crumbs drift off.
- **Frozen water** (`RuinOptions.FreezeWater`, an option of every revision, after the scorch): every `water` cell becomes
  `ice` with its tint and glow, kept cells included — always in space, on a planet without air.
- **Markers.** A planet wreck keeps chests, the loot markers, sounds and entrances (`PlanetMarkers`); a space wreck also
  the doorways (`SpaceMarkers`). A doorway and a standing entrance stay where the author put them; on a planet the others
  rest on what is left (the landmark rule), in space nothing falls. A marker that needs a floor (a chest, a loot cache, an
  entrance) and has none any more moves to the nearest free cell with a floor and a roof above it, never onto another
  marker's cell. In space `GiantSpaceLayout.Entrance` — the boarding arrival — is the first entrance that survived, else
  the standable cell nearest the middle of the top.
- **Abandoned beds** (a world of terrain generation 45 — the `abandonBeds` flag of both builds, set by the server from the
  save's generation, the space build cache keys it): after the ruin pass and before the markers settle, every `bed` becomes
  `bed_abandoned` — the bed's look, no bed to any rule (no home spawn, rest heal or settler). The templates place one-cell
  `bed` cubes; two in a row become one two-cell bed, a lone one gets its foot on a free floor cell beside it (+Z first;
  never a kept cell, never within one column of a marker, below the ground row only into a cavity) or stays a head alone.
  The markers come out exactly as without it, and an older world's wrecks keep their beds — so no pin or base hash moves.
  Write `bed` in a ship script as before. See [WORLD_GENERATION.md](WORLD_GENERATION.md) §59.

Calibration at the default strength: about 20–30 % of a hull's cells go (the shipped ships lose 15–21 %), lone one-cell
holes are a fraction of a percent of the outer hull, about a tenth of it is sooted; a 256 × 80 × 100 hull builds in a few
hundred milliseconds.

`BuildPlanet(t, seed, turns, content, surfaceBlock, atmosphere, abandonBeds)` returns the `LandmarkLayout` the landmark stamp
writes (turned by quarter turns, tier `giant_wreck`); `BuildSpace(t, seed, content, abandonBeds)` returns a `GiantSpaceLayout` — never turned,
in template coordinates, with blocks, modifiers, shapes, the surviving markers, the arrival and the intact block count.
`Fingerprint(t)` is a 16-hex-digit hash of everything that shapes the wreck (size, ground row, strength, style, zones, the
body, every listed cell) — not the name, weight, pack or planet types — so a record can pin it and notice a changed design.

## 5. On planets (terrain generation 42, #3608, #3610, #3611)

One line of the structures boot stage, right after the landmarks (`BootDetail("giant wrecks", …)` →
`StampGiantWrecks`, GameServerGiantWrecks.cs), before the buried ruins. It reuses the landmark machinery: plan, cell
writer, overlay storage, chests, emitters, streaming lift and far silhouette. A wreck lives in its own list
(`LoadedWorld.GiantWrecks`, `LandmarkInstance.GiantWreck`).

- **The gate.** The `giant_wreck` kind of `data/structure_types.json` (`StructureTypeRules.GiantWreck`): enabled, the
  world's terrain generation at least its `minGeneration` (42), its conditions `notVoid` and `notGas` — an airless moon
  or an asteroid may carry one —, a type with a structure whitelist only when it names `giant_wrecks` (and then only ships
  whose `planetTypes` name the type). Plus the host's `ServerConfig.PlaceGiantWrecks` and the world option
  `WorldDescription.GiantWrecks` not *Off*; never the finale body; only a body untouched at load (a visited body records a
  skip). From terrain generation 43 never on a **civilization's home body** either: a civilization whose `homeBody.suppress`
  names `giant_wreck` (both shipped ones do) keeps the planet wreck off its homes — the body records a skip
  (`CivilizationSuppresses`, [CIVILIZATIONS.md](CIVILIZATIONS.md) §1, §4). The space wrecks are not affected.
- **The roll.** `LaneRoll(seed, "giantwreck:roll:<body>")` below the kind's chance (`chance` 0.05 in the file;
  `ServerConfig.GiantWreckChance` overrides it when set) × the structures frequency (`StructureFactor`) × the world option
  (`GiantWrecks.Probability() / Normal.Probability()`: very rare 0.125, rare 0.375, normal 1, frequent 1.875) — about
  every 20th eligible planet at the defaults.
- **Pick, turn, seed.** Among `GiantShipsFor(…, the world's generation)` that the body is at least four footprints around
  (`LandmarkFitsBody`, so a small moon carries only a ship a fraction of its size), by weight (`giantwreck:pick:<body>`),
  a quarter turn and the instance seed — each a hash of its own lane. A pick without a seat gives way to another (up to
  four tries).
- **The seat.** The guaranteed search (#586) with the footprint registry (`KeepClearFor("giant_wreck")`: 6 everywhere,
  24 from the pads, 10 from the crash site, 16 from a landmark) and the site rule of generation 40 (high prominence, flat
  relief; a fresh wreck reserves its ground with radius 16, cut and fill up to 24 — [WORLD_GENERATION.md](WORLD_GENERATION.md)
  §55). Seats `flat`, `slope` or `shelf`, or `stilts` only in shallow water (every sampled column's ground at most
  `GiantWreckShallowWater` = 4 below the hull's ground row); never lava.
- **Pinned.** One placement record (`giant_wreck`, index 0; an admin's hand stamp takes the next index) with the ship's
  key, the seat and, in its layout field, `turns`, `rev` (3), `seed`, `air` (0/1 — whether the body had air), `fp` (the
  fingerprint) and `storage=overlay`.
- **Written once** through the landmark cell writer (monument rule above the ground row, blocks and sealed cavities below
  it, a crate in every existing chest's cell) into **one structure overlay**, key `giant_wreck:<index>`
  (`GiantShipRules.FeatureKey`) — never a block edit per cell, on any generation. Registered in the footprint registry as
  `giant_wreck` and **unprotected**: what players mine stays mined. `OverlapsLandmark` counts a wreck's footprint, so the
  vaults, cubes, chests and unique sites stamped later keep clear of it.
- **Every later load** replays every record of the body — whatever the switches say now: a wreck a world has stays — and
  re-derives the layout for its markers, the silhouette and the lift. A record whose ship left the content, or changed
  since (another fingerprint or ruin revision), keeps its blocks (the overlay still loads with its chunks) and re-derives
  nothing — no chests, emitters, silhouette or map entry (warned in the log).
- **Chests** are the landmark's (a one-time `salvage` container, never refilled) on a lane of their own (`giantwreck`)
  with the container prefix `loot_giantwreck_chest_`; `block` defaults to the wooden crate and `hidden=1` puts a stone on
  top. The classic loot markers spawn as structure loot of kind `giant_wreck`.
- **Sound emitters** ride in the client's `LandmarkList` (tag 335) after the landmarks' (`NetLandmark.Kind` =
  `giant_wreck`; an older client draws a wreck like a landmark).
- **Discovery** (GameServerGiantWrecksWorld.cs): on everyone's map (POI type `giant_wreck`, the ship's name) once a
  player on foot comes within **100** blocks of its footprint; VEGA's line `vega.giantwreck.first` once per player within
  **64** blocks (an advisor hint: muted hints and a training that holds hints keep it for the next wreck; milestone
  `vega:giantwreck:first`); a scan of any of its blocks reads as the wreck (kind `giant_wreck`, info
  `ui.scan.giant_wreck`, threat *inert*) and is worth 8 knowledge once per body and ship (ledger
  `giantwreck:<body>:<key>`, the Codex group `giantwreck`, heading `ui.wiki.discoveries.giantwreck`, text
  `giantship.<key>.desc`).
- **Seeing it.** The streaming lift lifts a wreck's columns like a landmark's, the far silhouette comes from its overlay's
  cells, and the far tiles leave its cells out (FarTerrainSystem) — [LANDMARKS.md](LANDMARKS.md) §6.
- **Admin** (behind the world's cheat rule; GameServerGiantWrecksAdmin.cs): `/giantwreck list` names every giant ship of
  the content (pin-only ones too, with their size); `/giantwreck <key>` lays that ship's wreck six blocks in front of the
  admin, whatever the roll said, with a fresh seed each time, under the next record index, stored as an overlay on any
  world (refused on a void or gas world and in space; no seat search, ground at the footprint's centre); `/tp giantwreck
  [n]` goes to the ground in front of one (`giantwrecks`, `giant_wreck`, `giantship`, `riesenschiff`, `riesenwrack`,
  `riesenwracks` also work). #3655 (GameServerAdminTeleportSites.cs): the list names each wreck after its ship and goes on
  with the giant wrecks **in space** of the player's current system (body name · ship, "in space" instead of a distance;
  a retired one left out, listing builds none); such a target carries its station id and the teleport boards it through
  `TravelToStation(quickTravel: false)` — the travel menu's path, from a planet and from flight alike. In flight `/tp`
  offers only those (every other kind is refused with `srv.tp.no_surface_targets`).

Known limit (as for landmarks): on an ungraded or stilts seat a water cell on the ground row is carried down by the plinth.

## 6. In space (#3612–#3614)

**Placement** (`GiantWreckPlacer`, WorldGeneration — pure, seed-stable, its own salt `giantwreck:`), run right after the
derelict stations at the very end of the galaxy build (`PlaceDerelicts` → `PlaceGiantDerelicts`), so no body the generator
made moves and an older save gains its wrecks at its next start:

- only the fixed procedural systems (`sys0 … sys(fixedCount-1)`) — never the finale system, never a grown one;
- the **start system always holds one** (`derelict:giant-<sys>-0`) unless the world option is *Off*; every other system rolls
  `Frequency.Probability() × 0.15` (`GiantShipRules.SpaceOddsScale`; normal 0.06 — about one system in seventeen) for one
  (`MaxPerSystem` 1); *Off* places none;
- each is a `SpaceStation` body whose id carries the derelict prefix (`GiantShipRules.IdPrefix` = `derelict:giant-`), so
  every derelict rule holds for it by its id; a coined, language-neutral ship name (`NameGenerator.Ship`); a star-map spot
  700 units beside a seeded planet, moon or asteroid field (widening twice, 300 clear of every other body) — cosmetic;
- the server adds a wreck only where a ship can be built for it (the pool is not empty) or one is pinned already;
- the host's switch `ServerConfig.PlaceGiantWrecks` off keeps every space wreck out of a world — of an existing one too:
  no body, no contact; its pin, flight spot, delta, interior overlay, record and chest keys stay stored, and the wreck comes
  back as it was when the switch is on again.

**Which ship, pinned** (GameServerGiantDerelictPins.cs). `GiantWreckPlacer.PickDesign` — a weighted pick by a hash of the
world seed and the id over the pool in key order (never a pin-only ship). **The wrecks of one world are different ships
while a design is left** (#3764): the registration walks the wrecks in the systems' order with the set of designs already
taken — every pinned ship first — and a fresh pick only chooses among `GiantWreckPlacer.Untaken` (all of them again once
none is free). Each wreck used to pick on its own, so two wrecks of a world were the same ship about one time in four.
A wreck stores only the players' changes over its
base build, and the base is rebuilt every server run, so the first build pins everything it depends on in
`WorldMetadata.GiantWreckDesigns`: `key|fingerprint|r<ruin revision>|d<ship-decay revision>|b<base hash>`
(`GiantShipGenerator.RuinRevision`, `DerelictGenerator.ShipDecayRevision`, `GiantDerelictLayout.BaseHash` — the decayed
wreck by block keys, dyes, shapes, doors, arrival and guardian bay), and marks the id with the `GeneratedLoot` key
`derelict-shape:giant:<id>` (never rebuilt as a legacy station). On a later start: anything differs (the ship, a revision —
checked at registration — or the base hash, checked at the build) and the save holds something of the wreck (its delta, its
interior overlay, door records): it **stays away** for that run, off the star map, with a warning — nothing stored is
touched, and it comes back once the base comes out as stored again. Anything differs and nothing is stored (it was only
seen): it is built anew (the ship's new version, else a fresh pick) and pinned and placed again.

**The build** (`BuildGiantDerelict`, from `EnsureDerelict` on first need — the flight contacts, a menu travel and a rejoin
all build it on the way): `GiantShipGenerator.BuildSpace` (revision 3, `hull`) with the seed `GiantWreckPlacer.BuildSeed`
→ the derelict decay in **ship mode** → the base build (cells with dye, glow and shape, the intact doors) and its visible
shell → a **derived** station structure (`RegisterDerivedStation(id, build, arrival, "colossal")`) at its flight spot. The
pure part takes about a second for a big ship on a desktop (several on a Raspberry Pi), so it runs ahead
(GameServerGiantDerelictBuilds.cs). **With background threads** (`ChunkGenWorkers` above 0): the start system's wrecks
right at the boot, then once a second those of the systems where a player sits aboard their ship (about to launch) or flies
— queued, ONE builder thread at a time; `BuildGiantDerelict` takes the finished result (and waits only if it is still
running) and keeps only the cheap registration on the tick; a finished build nobody took within 600 s
(`GiantPrebuildKeepSeconds`) is dropped and built again when needed. A process-wide cache (`GiantWreckBuildCache`, the last
four decayed wrecks, keyed by fingerprint, build seed, ruin and decay revision and the content's block signature) serves a
restart in the same process. **Without threads** (0 workers — the in-browser singleplayer — or a platform that cannot start
one): the start system's wreck is built during the boot, under the loading screen, any other at its first need, and the
process cache is not used. The worker also builds the hull's visible shell; it is always adopted, and the players' stored
changes are replayed over it (`AdoptGiantShell` → `HullShell.Changed`), so the first design send builds no shell on the
tick. Measured on a desktop (the battleship, 160 000 blocks): the first flight into the start system took 0.9–1.1 s before
(0.7–0.9 s of it the build on the tick) and takes about 0.18 s with the prebuild (50 ms with the giant wrecks off); inline
with a warm cache about 0.3 s. One build allocates 43–59 MB in all; about 7–15 MB of it stays. Not covered: a hyperjump
straight into a system whose giant nobody has prebuilt waits for (or does) the build.

**Ship mode** (`DerelictGenerator.GenerateShip`, DerelictGenerator.Ship*.cs; the station paths never run it): the force
fields are dead; the **rooms** are the inside air the outside cannot reach, split at the template's doorways; each walkable
room but the arrival's is **torn open** with 50 % (at least one of two or more), never through a wall a closed room, a
doorway, a chest, a sound or the arrival relies on; a doorway that still parts two places with a closed room on a side gets
an intact **sliding door**; a dead light beside a closed room burns again as an **emergency light** (`light_red`,
`strip_light_warm`); soot round the new holes and a little scrap on the decks (a human hull only); the arrival standable;
**one guardian bay** in the largest closed walkable room but the arrival's, facing the way in. No beta-station damage, no
trading posts, no old supply crates or logs — the template's chests are the chests. Layout `Source` = `giant:<key>`, tier
`colossal`.

**Derived storage** (GameServerSpaceStructureDerived.cs, #3613). A giant wreck is far too big for a cell row, and its cells
are a pure function of template + seed, so the save keeps only the players' changes: **one delta blob** (a
`StructureOverlay` in the `structure_overlay` table, location `space`, key `delta:<id>` — every cell that differs from the
base: block, dye, glow, shape, air where a block was mined); never a `StoredSpaceStructure` row. Its writes are
**coalesced** (GameServerSpaceStructureDerivedStore.cs): an edit folds its cell into the in-memory delta; only the very
first change of a station nothing was stored of is written at once, after that the blob is re-encoded at most once a
second (the tick step `DerivedDeltas`) and on every save (`SaveAll`: the autosave, a checkpoint, the clean stop) — a hundred
blocks mined in a second cost one write. Door records stay structure edits and ride along with the same flush when the
doors changed. A small **record** beside it (the named blob `derived-station:<id>`, a few bytes of JSON: the stamp origin,
the anchor = the base build's minimum, and the base hash the changes were made on), written with the first stored change or
the first boarding, keeps the interior mapping fixed: once stored, the anchor is never recomputed from a rebuilt base. The
**interior world** (`station:<id>`) gets the build as it stands at the first boarding as **one structure overlay** (key
`derived:<id>`) instead of a block edit per cell; later boardings stamp nothing (every change since reached the interior as
an ordinary edit). Restart = base (rebuilt from template + seed) + delta, mapped through the stored anchor; the interior =
its overlay + its block edits. The arrival is searched ±8, then ±24 around the entrance, only then the whole volume. A
block-id palette migration remaps the blobs like every overlay.

**Over the wire** (GameServerSpaceStructureDesign.cs, #3612) — for every station structure, not only giants. This is
**protocol version 12** (`Networking/Protocol.Version`): a version-11 client would take every part for the whole design and
mesh a single changed cell plain, so it is refused at the join (`srv.join.protocol_mismatch`, "server 12, client 11")
instead of drawing a broken hull.

- **In parts.** A `SpaceShipDesign` whose cell arrays would pass 600 000 bytes as JSON (`DesignPartJsonBudget`, well
  under the 1 MB packet cap) goes as parts of one `Serial` (`PartIndex` of `PartCount`; part 0 carries the doors and the
  hatch, every part the header). A design that fits is one message with `PartCount` 1, as before.
- **The shell only.** A `ShellOnly` structure (a giant wreck) sends the cells a pilot can see — every block next to outside
  space plus what shows through windows and shaped cells (`HullShell`, the rule of `StationHull.VisibleCells`), kept up to
  date per edit: a mined hull block lays open the walls of the hall behind it.
- **One cell at a time.** A block mined or placed on a station structure (a player station, a derelict, a giant wreck) —
  from inside or on a spacewalk — reaches the pilots of every orbit holding it as `StructureBlockChanged` with its dye,
  glow and shape, plus the cells it laid open. The whole design goes again only when the change moved the cell box, when
  nothing was noted, when more than max(64, min(4096, cells / 4)) cells changed, or when the laid-open set overflowed.
  A door built or taken out on a spacewalk still re-sends the design. A full design is built and encoded once per publish
  and that one message goes to every pilot (`SendShipDesignTo`, `SendToSessions`).
- **Ranges from the hull.** The spacewalk edit range is measured to the edited cell where the flight view draws it
  (`InStructureEditRange`): a giant hull is worked along its whole length, and from its middle its far end is out of
  reach. A structure whose half box diagonal is within the 40-block range (an asteroid, a debris piece, a small player station)
  keeps the centre measure it always had. `StationFlightDistance` and every box question use the cached
  `SpaceStructure.CellBox()` (O(1) after the first call).

**In flight** a giant wreck is a contact (`SpaceStation`, size tier `colossal`) of every orbit instance of its system, its
hull added with the derelicts' (`AddGiantDerelictHulls`). `GiantWreckFlightPosition`: x = 0 behind the launch point, its
near face at most −110 and 40 past the far face of every obstacle — the system's derelicts (their ORIGINAL layouts), its
player stations (their hull boxes as they stand) and the earlier giants — its lowest block at 50 over the flight plane,
clear of every planet sphere. The spot is chosen the first time the wreck is built and pinned in
`WorldMetadata.GiantWreckSpots` (`x;y;z`), so a station built later never moves it; it is placed anew only when the wreck
itself is (its base changed with nothing stored on it).

**Aboard** every derelict rule holds by the id: boardable by anyone, **no air** (dead life support — `@srv.giantwreck.no_air`
once per boarding names the suit tank), nobody home, no trade, never a home spawn (`@srv.giantwreck.no_home`), falling out
is a **spacewalk** and a breach lets the suit back in (`@srv.giantwreck.drifted_back` where no flight can open). At the first
boarding (anyone's) every surviving `chest` marker becomes a one-time salvage container (`giant-chest:<id>:<n>` in
`GeneratedLoot`, container `loot_giantchest_<id>_<n>`, lane `giantwreck`): its crate (setting `block`, default the metal
`crate`; `hidden=1` puts a `scrap_pile` on top) is set in the interior AND written into the build like a player edit, so
the hull from space and the delta keep it. The `sound` markers play aboard (the station marker life). The guardian bay
follows the derelict machinery (survival only, [DERELICT_STATIONS.md](DERELICT_STATIONS.md) § Guardians).

**VEGA and the scanner.** `vega.giantwreck.approach` once per player and wreck within 150 of the **hull box** (milestone
`giantwreck:seen:<id>`); `vega.giantwreck.boarded` at the first boarding of each giant wreck (the very first derelict
boarding of all still gets the no-air hint). The ship scanner measures its range to the hull box; the readout is kind
`station`, subject `giant_wreck`, info `ui.scan.giant_wreck_space`, traits *breached*, the ship's name and its size
(`ui.scan.trait.giant_size_large|huge|colossal`: under 120, from 120, from 200 blocks long), worth 5 knowledge once per
wreck (ledger `space:giantwreck:<id>#<ship>` — the ship class after the `#`, so the Codex names it in every session).

## 7. The client (#3615, #3616)

- **One design feed.** `Client.Core/SpaceDesignFeed` puts every received `SpaceShipDesign` through one
  `SpaceDesignAssembler` (parts of one serial per structure id, in any order; a newer serial replaces an unfinished older
  one, an older part is dropped, at most 1024 parts) and raises `DesignReady` only for whole designs — a single-part design
  is the very same object. It resets on a join, a disconnect, a closed flight and a world reset, and forgets a structure
  that leaves. The flight view's stations, the own ship and the remote ships all subscribe to it (`Game.SpaceDesigns`).
- **Chunk meshes** (`SpaceView.StructureMesh.cs`, `Client.Core/StructureChunkMath.cs`). A structure is a set of 16³ chunk
  meshes with their colliders; a `StructureBlockChanged` (dye, glow and shape applied) re-meshes the cell's chunk and those
  of the 26 around it that the cell touches (a face across a border, the corner shading of an edge or a corner), the
  chunks in the reach of the lamps near it (and its own reach when it gained, lost or changed a light), and — when it
  turned between light-stopping and open — the chunks whose skylight reads its column; an air change of a cell the client
  never held does nothing. Meshing runs from a queue, nearest first, 3 ms a frame (at least one chunk); an edited chunk
  jumps the queue; a new design keeps the old chunks on screen until their replacements are built. Doors and a derelict's
  broken-look extras are built once per design.
- **The far skin** (`Client.Core/HullSilhouette`, `SpaceView.HullLod.cs`). A giant wreck (or any station 96 blocks
  across) is drawn from afar as a coarse model — one cell per 4³ blocks, coloured by its most common block, closed halls
  filled so only the outside skin is meshed (`LandmarkSilhouetteMesh`) — built on a worker thread as the design arrives; in
  the browser (no threads) inline and only the first time the hull is seen from afar, so arriving next to one never stalls a
  frame. Beyond 200 blocks from the hull box the skin stands in and the chunk meshes are freed; under 160 the chunks build,
  and the skin covers the hull only while its chunks are built from nothing (`VoxStruct.Building`) — never over chunks
  already on screen (a mined block, a re-sent design) and never closer than 32. It is rebuilt on a new design only.
- **On planets** a wreck is a `NetLandmark` of kind `giant_wreck`: its silhouette and sound emitters are drawn and played
  like a landmark's. The planet map draws the `giant_wreck` POI with the wreck icon and the legend entry
  `ui.map.kind_giant_wreck`.
- **The world option** *Giant ship wrecks* (`WorldCreationOptions.GiantWrecks`, default *Normal*, `--giant-wrecks`): a
  slider right below *Derelict stations*.
- **Chat:** `/giantwreck` is passed to the server; `/help admin` lists `ui.admin.help_giantwreck`.
- **The browser** fetches every file under `data/` at start (`StreamingAssetsCache`), so the ship files (about 2 MB
  together) are part of its first load — keep them compact.

- **In space** (`Client.Core/GiantWreckText`, `SpaceView.GiantWrecks.cs`): the flight chart (`SpaceMap`) draws a giant
  wreck as the derelicts' amber disc, a size larger, labelled `{name} · Giant wreck` (`ui.map.kind_giant_wreck`), a
  waypoint target; the travel screen uses the same kind word and `ui.map.giantwreck_detail`; the radar's nearest-wreck
  readout, the three client scan-range gates and the target brackets measure to the hull BOX (`Client.Core/HullApproach`),
  like the server (the brackets are capped at `SpaceTargeting.LockFrameMaxHalf`, and a derelict locks as a **neutral**
  "Wreck", never a friendly ring — #3659); the autopilot and a chart waypoint stop 24 blocks in front of the hull box instead of flying for its
  centre. The scan card reads "Giant ship wreck · {name}" in space and "Giant wreck · {class}" on a planet
  (`ScanReadoutText`).
- **The Codex** (`WikiUI.GiantWrecks.cs`): one group *Discoveries → Giant ship wrecks* (`ui.wiki.discoveries.giantwreck`)
  for both ledgers — a planet wreck (`giantwreck:<body>:<ship>`) is named after its ship class with the class's Codex text;
  a space wreck (`space:giantwreck:<id>#<ship>` — the server records the class after the `#`) is listed under its coined
  name with "class — class text".
- **The derelict decor** on a giant hull (`DerelictDecorPlan`): an occupancy bit set over the hull box and a sort of the
  ragged edge cells only — the battleship's 118 816-cell shell plans in about 11 ms (was 200 ms), at most 8 sparks and 6
  pieces, once per design.

**The editor** (village editor, *Use as → Giant ship*; [STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md) §3f): the
style, strength, ground row and name key; the palette shows only the giant ship's markers (its entrance labelled just
*Entrance*, `StructureStampRules.MarkerLabelKey`) plus keep, cavity and settings;
*Show as a wreck: On a planet / In space* runs `BuildPlanet` / `BuildSpace` with the preview seed; save derives a missing
door axis from the walls (`GiantShipEditorRules`), runs `GiantShipProblems` and writes
`usercontent/giant_ship_templates/<key>.json` plus `giant_ship_exports/<key>/giant_ship.json`;
`python tools/merge_structure.py giant_ship_exports/<key> [--data <copy>]` merges it into `data/giant_ships/<key>.json`
(keeping `pinOnly`, and the zones and minimum generation when the bundle has none).

## 8. The five ships

| key | name | size (W × H × L) | weight | style | ground row | inside | chests (loot table) | sounds |
|---|---|---|---|---|---|---|---|---|
| `carrier` | Carrier | 57 × 72 × 236 | 3 | metal | 5 | crew deck with quarters, mess, galley, medbay; the engine room; two hangar decks with parked small ships; the workshop; a launch tunnel out through the bow; the flight deck; a five-storey island with the bridge | 14 `giant_carrier`, 3 hidden `giant_cache` | 2 × `amb_giant_hull` |
| `liner` | Cruise Liner | 61 × 68 × 208 | 3 | metal | 4 | cabin decks, promenades with empty lifeboat bays, a two-deck theatre, lounge, dining hall; the lido deck under three glass domes with two pools (ice where there is no air), a garden and a water slide | 15 `giant_liner`, 3 hidden `giant_cache` | `amb_giant_hull`, `amb_giant_greenhouse` |
| `battleship` | Battleship | 63 × 71 × 250 | 3 | metal | 6 | a retired escort ship built round one spine gun: the barrel is a walkable tunnel 9 × 7 from the breech to the muzzle; reactor hall, side hangar, cabins, the empty armoury, a compact command block with an antenna array; eight triple turrets and radiator fin banks on the shoulders, in the SPS blue-grey (an original look — no franchise traits) | 14 `giant_battleship`, 4 hidden `giant_cache` | 2 × `amb_giant_hull` |
| `ark` | Terraforming Ark | 92 × 62 × 197 | 1 | metal | 5 | three greenhouse halls (crop dome, orchard vault, blossom dome with a pond), the tank hall with tall decorative growth tanks, the seed vault, a lab, animal pens, quarters, the bridge | 14 `giant_ark`, 3 hidden `giant_cache` | `amb_giant_greenhouse`, `amb_giant_hull` |
| `squid` | Crystal Squid | 103 × 46 × 246 | 1 | alien | 4 | a squid of dyed crystal and glass: eight arms, big glass eyes, round tunnels on two levels, bubble rooms, a heart hall with a glowing core and a spiral ramp | 16 `giant_squid`, 3 hidden `giant_cache` | 3 × `amb_giant_crystal` |
| `sample_hulk` | Sample Hulk | 40 × 14 × 16 | 1 | metal | 1 | pin-only test hull: two rooms joined by a doorway, a small pool and a tree in a planter | 2 `ruin_common` (one hidden) | — |
| `escort_cruiser` | Escort Cruiser | 31 × 25 × 88 | 1 | metal | 3 | pin-only — the hull a beaten escort cruiser leaves (§14), never a rolled wreck: one long barrel along the spine over a bridge with red window lamps, two decks with a corridor each, cabins, holds, the engine room, a big robot bay, the bridge with its console markers | 4 `giant_battleship`, 1 hidden `giant_cache` | `amb_giant_hull` |

Every shipped ship has three entrances (the sample one), and kept cells join its decks (ladder shafts; the squid's spiral
ramp round the heart). Measured on the battleship, the ark and the squid, a wreck loses 15–19 % of its blocks on a planet
and about 20 % in space. The loot tables live in `data/loot_tables.json`
(`GiantShipLootTests`: every item exists, no tools, vehicles, reactor fuel or blueprint-locked machines — the sensor parts
`quantum_sensor` and `sensor_lens` excepted —, 2–8 stacks a chest, rare caps). The crystal squid's interior is minable
crystal — a big crystal source for whoever finds one. Weights: carrier, battleship and liner 3 each, ark and squid 1 each.

## 9. Sounds

Three new `bed` loops (14 s, mono, `tools/ai-assets/gen_sound.py`, levelled by `tools/audio/level_clips.py`) that the ships'
`sound` markers name: `amb_giant_hull` (soft metal creaks and distant groans in a big empty hull), `amb_giant_crystal` (a
soft resonant crystal hum) and `amb_giant_greenhouse` (water trickling between plant beds over a quiet air hum). The
editor's marker panel offers them (`EditorMarkerPanel.KnownSounds`). Prompts: [SOUND_DESIGN.md](SOUND_DESIGN.md) §38.

## 10. The two fixes in the same package

- **The roofless planned villages (#3605).** The planned stamp of generation 41 cleared the vegetation after writing the
  buildings, and the vegetation carve removes every `wood_log` — the village modules' roofs, ridges, posts and fences.
  It now lays the ground of every claimed column first, carves the vegetation, then writes the cells
  ([WORLD_GENERATION.md](WORLD_GENERATION.md) §56; `GrownSettlementServerTests.PlannedVillage_KeepsEveryBuiltBlock_LogRoofsIncluded`).
- **The free starter ship (#3606).** `CraftShip` checked the blueprint only when a ship named one, and the starter names
  none and costs nothing; a modified client could craft starters. A shipped ship without a blueprint is now refused (a
  user ship keeps its own rule); the build menu never listed it (`ShipFleetTests.CraftShip_RefusesTheStarter_ItHasNoBlueprintAndIsNeverForSale`).

## 11. Content rules

The game is for children: a wreck is abandoned long ago and everyone got away (the texts, VEGA's lines and the Codex say
so); nothing gory or frightening; no names, symbols or recognisable designs of any real franchise; existing blocks only (a
new block shifts every block id); names and texts are locale keys (`giantship.<key>.name` / `.desc`), never literals.

## 12. Key files

| Area | Files |
|---|---|
| Rules, template, pool | `Shared/Definitions/GiantShipRules.cs`, `StructureTemplate.cs` (`GiantShip`), `LandmarkRules.cs` (`ProfileCrash`, `GiantShipProfiles`); `Shared/Content/GameContent.GiantShips.cs`, `StructureCatalog.GiantShips.cs`, `ContentLoader.cs` |
| Placement rule, option | `data/structure_types.json` (`giant_wreck`), `StructureTypeRules.cs`, `WorldDescription.cs` (`GiantWrecksGeneration`, `GiantWrecks`), `ServerConfig.cs` (`PlaceGiantWrecks`, `GiantWreckChance`), `ServerConfigOptions.cs` (`giant-wrecks`) |
| Wreck builds | `WorldGeneration/GiantShipGenerator*.cs`, `StructureRuiner.Giant.cs`, `StructureRuiner.GiantDecay.cs`, `RuinOptions.cs` (`FreezeWater`), `GiantWreckPlacer.cs`, `DerelictGenerator.Ship*.cs`, `HullShell.cs` |
| Server, planets | `GameServer/GameServerGiantWrecks.cs` (gate, roll, seat, record, stamp), `…GiantWrecksWorld.cs` (map, VEGA, scan), `…GiantWrecksAdmin.cs`, the landmark files it shares (`…LandmarkLife.cs`, `…LandmarkStamp.cs`, `…LandmarkStream.cs`, `…LandmarkWorld.cs`, `FarTerrainSystem.cs`) |
| Server, space | `GameServerGiantDerelicts.cs` (placement, build, flight spot), `…GiantDerelictPins.cs` (the pin, the spot pin), `…GiantDerelictBuilds.cs` (prebuild, cache), `…GiantDerelictLife.cs` (chests, sounds, VEGA, scanner), `…SpaceStructureBox.cs`, `…SpaceStructureDesign.cs`, `…SpaceStructureDerived.cs`, `…SpaceStructureDerivedStore.cs` (delta, record, coalesced writes), `GameServerDerelicts.cs` (routing), `Shared/State/WorldMetadata.cs` (`GiantWreckDesigns`, `GiantWreckSpots`), `Networking/Protocol.cs` (version 12) |
| Wire | `Networking/Messages/StructureMessages.cs` (`SpaceShipDesign.Serial/PartIndex/PartCount`, `StructureBlockChanged`), `LandmarkMessages.cs` (`NetLandmark.Kind`) |
| Client | `Client.Core/SpaceDesignAssembler.cs`, `SpaceDesignFeed.cs`, `StructureChunkMath.cs`, `HullSilhouette.cs`, `GiantShipEditorRules.cs`; `SpaceView.StructureMesh.cs`, `SpaceView.HullLod.cs`, `StructureEditor.GiantShip.cs`, `UiWorldOptions.cs`, `WorldMap.cs`, `ChatUi.cs` |
| Data and tools | `data/giant_ships/*.json`, `data/loot_tables.json` (`giant_*`), `tools/gen_giant_ships.py`, `tools/giant_ships/*.py`, `tools/landmarks/ships.py`, `shipgeom.py`, `preview.py`, `tools/merge_structure.py`, `Tools/GiantShipTool.cs` |

## 13. Tests

`GiantShipRulesTests`, `GiantShipTemplateTests` (the pool, the folders, no ship-type list ever sees one),
`GiantShipDeviceTests`, `GiantShipLootTests`, `StructureRuinerCrashTests` (revision 3, no speckle, coherent holes and
scorch; revisions 1 and 2 unchanged), `GiantShipGeneratorTests` (planet and space wrecks, markers, fingerprint pins),
`GiantWreckPlanetTests` (a generation-42 body carries its wreck as one overlay, airless = ice, gas giant and generation 41
none, keep-clear, a changed ship, the one-in-twenty roll), `GiantWreckWorldTests` (list, map, VEGA, scan, the admin stamp),
`GiantWreckPlacerTests`, `GiantDerelictTests` (start system, boarding, no air, spacewalk, chests, a mined hull cell as one
cell and a delta, a changed ship, the guardian bay, VEGA, the flight spot), `HullShellTests`, `DerivedStationTests`,
`AbandonedBedTests` (generation 45: only abandoned two-cell beds aboard, the markers unmoved), `CruiserWreckTests` (§14),
`TemplatePropShapeTests` (no shipped ship or landmark places a rug, pot or stretcher as a cube, §3);
Client.Tests `SpaceDesignAssemblerTests`, `FlightStructureMeshTests`, `GiantShipEditorTests`, `WorldOptionsLayoutTests`.

## 14. The escort cruiser's wreck (#3902)

A beaten escort cruiser (the space boss, [SPACE_COMBAT_CONCEPT.md](SPACE_COMBAT_CONCEPT.md) § "The escort cruiser") does not
vanish: it goes dark and stays where it fell as a wreck the players can board (canon C22). It is the first wreck that is
**registered at runtime** instead of by the galaxy pass, and the first that is **fresh** instead of centuries old.

**The template.** `data/giant_ships/escort_cruiser.json` from `tools/giant_ships/escort_cruiser.py` — a mid-sized warship
(31 × 25 × 88; the length stays under 96, where the ruin pass starts breaking a hull in two) and `pinOnly`, so no planet and
no galaxy pass ever rolls it (`GiantShipsFor` leaves a pin-only ship out; `CruiserWreckTests` asserts it for every planet
type and for `GiantWreckPlacer.PickDesign`). Its bridge carries two marker types that only a warship has —
`GiantShipRules.BridgeMarkers`: one `sps_console` (setting `face=±x|±z`: where the console's front looks) and `sps_lamp`
(each on a one-block post). They are accepted by `GiantShipRules.Problems` (`IsShipMarker`) but are deliberately **not** in
`GiantShipRules.Markers`, the structure editor's palette of a giant ship; only a wreck in space keeps them
(`GiantShipGenerator.SpaceMarkers`). The bridge deck, the posts and the wall behind them are `keep` cells, and so is the
whole shell of the robot bay (every block touching the hall's air, its three red ceiling lamps included — a kept lamp is
never switched off): the fight never tears the hall open, so the decay finds its room for the guardian bay.

**The fresh option.** `GiantShipGenerator.BuildSpace(…, fresh: true)` runs the same ruin pass (revision 3, `hull`: collapse
balls, bites, smashed glass, every light switched off) with **no scorch** — no soot tint. `DerelictGenerator.GenerateShip(…,
fresh: true)` then skips the soot round its own breaches and the scrap on the decks, and the lights did not all die: a dead
lamp (never a strip) within `CruiserWreckRules.BridgeLampReach` of the console always burns red again, any other with
`DerelictGenerator.FreshLampShare` (35 %, a hash of seed and cell — never the pass's random stream). A fresh wreck always
has its guardian bay: should the fight have torn every room with a doorway open, the bay stands below deck — on the lowest
deck with an open roofed spot, there as far from the way in as it goes (`PlaceFreshBay`; the cruiser's kept bay hall makes
this a safety net). In both modes a ship with a console marker gets its bridge after everything else
(`DerelictGenerator.ShipBridge.cs`): the deck under the console and the lamps is never torn, nothing else is put there, and
the block `sps_console` and a `light_red` per lamp marker are set (`GiantDerelictLayout.Console` / `Lamps`). **Nothing of
this touches another wreck**: `fresh` defaults to false, a ship without the markers takes none of the branches, and
`DerelictGenerator.ShipDecayRevision` stays 1 — measured against the build before the change (all six shipped ships, three
seeds each: the same `BaseHash`), five of those hashes are pinned in `CruiserWreckTests`.

**The battle scars** (#3915, `DerelictGenerator.ShipBattle.cs`). Instead of the centuries' soot and scrap, a fresh wreck
carries the fight's own marks — the very last step of the pass (after the bridge), drawn from a random stream of their own
(`derelict-giant-battle:<ship>` mixed with the seed), so the main stream is untouched and the chests, the doors, the
arrival, the guardian bay, the console and its lamps come out exactly where they come out without them (`CruiserWreckTests`
builds the wreck both ways through the internal `GenerateShip(…, battleScars)` seam and compares). A human hull only:

- **Scorch round the breaches**: within 3 blocks of each breach's first cell (`ScorchReach`, a sphere), 55 % of the plating
  (`ScorchShare`) takes `DerelictGenerator.ScorchTint` (`0x2A2420`, darker than the soot's `0x3A3734`; its glow kept) —
  tighter and darker than the soot ring. Never glass, a lamp (on or off), a machine (the bridge console, the guardian bay), a
  door, a ladder, a plant or a reserved cell.
- **Dead screens**: 75 % of the `lab_panel` cells (`DeadScreenShare`) take `DerelictGenerator.DeadScreenTint` (`0x1C2024`,
  no glow). A tint, not a block: there is no "off" screen block, and a new block would shift every block id. `engine_panel`
  is only scorched where a breach is near; `sps_console` is never touched (ECHO needs it).
- **Loose plates**: in each walkable room one or two (torn room) or none or one (closed room) hull plates lie on the deck —
  `iron_wall` in the `Panel` form (the square quarter-height plate, so no yaw), scorched — on a free deck cell at least 3
  from the arrival, never on a reserved cell (a doorway or right in front of one, a chest, a sound, the bridge), never on top
  of one, never within 2 of the guardian bay (its release ring stays free); each plate is reserved.

The wreck's `BaseHash` covers the scars like every other cell, so a wreck stored before #3915 rebuilds as any wreck whose
base changed (adopted when nothing is stored, kept away while changes are); the giant wrecks never take this step.

**The scan card** (#3915). The ship scanner reads a cruiser's wreck with its own readout (`ScanCruiserWreck`, before the
derelict station's): kind `station`, subject `cruiser_wreck` — the readout's display is that key too, so the client titles
it "Escort cruiser wreck" in its own language (`ui.scan.subject.cruiser_wreck`; the contact's name on the server is the
English one) — info `ui.scan.cruiser_wreck`, traits *breached*, the ship's name (`giantship.escort_cruiser.name`) and its size
(`ui.scan.trait.giant_size_*`), worth a giant wreck's 5 knowledge once per wreck (ledger `space:cruiserwreck:<id>`; a new wreck
never reuses an old one's id). Like a giant wreck's, the server measures its range to the hull box (`ScanDistanceSq`); the
client's range gate (`SpaceView.GiantWrecks.cs` `HullDistance`) still measures a cruiser's wreck to its centre, so in play
the scan reaches as far as from the centre until that gate learns the cruiser's wreck too.

**Registered at runtime** (`GameServerEscortCruiserWreck.cs`). The boss fight calls `OnEscortCruiserDefeated(instance, boss,
fighterIds, carriesEcho)` inside the kill. The hook writes a `CruiserWreckRecord`, builds the hull inline (about 20 000
blocks — a few dozen milliseconds, so no background build), registers it as a **derived station** (§6: the save keeps only
the players' changes) and puts its contact and hull into every open flight of the system; the kill's own `SpaceState`
carries it to the flight it fell in, the star map is re-sent.

- **The id** is `derelict:cruiser-<system>-<n>` (`CruiserWreckRules.IdPrefix`, `n` = how many wrecks that system has held).
  The `derelict:` prefix makes every derelict rule hold by the id: boardable by anyone, no air, no shield
  (`SpaceCombatRules.StationHasShield`), nobody home, no home spawn, falling out is a spacewalk. A wreck that replaces
  another never reuses its id, so nothing stored under the old one (the interior world, a chest mark, a player's
  milestone) can reach the new one.
- **Where the id alone is not enough** the cruiser's registry (`_cruiserWrecks`) is asked beside the derelicts' and the
  giants': `EnsureDerelict` (routes to `EnsureCruiserWreck`), `RegisterDerelicts` (never a beta station; calls
  `RegisterCruiserWrecks`), `AddDerelictHulls` (`AddCruiserWreckHulls`), `DerelictSpawnerSpotWorld` (the guardian bay),
  `OnDerelictBoarded` (`OnCruiserWreckBoarded`, VEGA's own line), `CheckDerelictApproach` (no "an old station" remark),
  `GiantWreckMissing` (a wreck that has to stay away is no contact, no travel target and no rejoin), and the "a ship, not a
  station" texts (`IsShipWreckId`: no air, no home, drifted back).
- **Where.** `CruiserWreckFlightPosition`: where the boss fell, raised until the hull's lowest block is 44 over the flight
  plane (the station hulls' height — clear of every planet sphere), then pushed sideways until its box keeps 20 from the
  launch column and 30 from every station hull the server knows there: the flight's own (NPC stations, derelicts, giant
  wrecks, player stations) plus the system's player stations and the NPC hulls already seen this run. An NPC station of
  another orbit that nobody has seen yet is not known — the one case in which two hulls can come close. The hull is never
  turned (no station structure is); the record keeps the yaw for a later use.
- **One per system.** A new wreck retires the system's old one (`RetireCruiserWrecksIn`): off the star map and out of the
  open flights at once, and removed for good — the record, the delta blob, the derived record, the door records, the
  interior overlay, block edits and containers, the chest marks — as soon as nobody is aboard: now, at the next defeat or
  at the next start (`CruiserWreckRecord.Replaced`).
- **The record** (`WorldMetadata.CruiserWrecks`, additive): id, system, serial, position, yaw, seed, template, base hash,
  `CarriesEcho`, `ConsoleOpened`, `EchoFreed`, `Replaced`, the fighters, who got a crate, who got a core. Every start
  re-registers the wrecks from it (`RegisterCruiserWrecks`); the hull is rebuilt from the seed at its first need and the
  stored changes are laid over it. Like a giant wreck's pin, a base that no longer comes out as recorded keeps the wreck
  away while the save holds changes of it (nothing is touched), and is simply adopted when nothing is stored.
- **The name** is the locale key `station.cruiser_wreck.name`, sent in the receiver's language on the star map
  (`ToNetBody`) and in the flight's contacts (`WithCruiserWreckNames`); logs and the boarding messages use the English one.

**Aboard** (`GameServerEscortCruiserWreckLife.cs`). Only robots: the guardian bay releases as on every derelict
([DERELICT_STATIONS.md](DERELICT_STATIONS.md) § Guardians). The chest markers fill once at the first boarding through the
giant wrecks' path (`SpawnGiantWreckChest`: `giant-chest:<id>:<n>`, `loot_giantchest_<id>_<n>`). Every pilot who hurt the
boss finds an **owner-locked crate** near the way in the first time they come aboard — 6 to 11 guardian plates and, with
35 %, a hacking chip (`CruiserWreckCrateFor`; the backpack when no deck cell is free, the deck when the backpack is full).
Every boarding raises `cruiser:boarded` with the argument `echo` (the story's cruiser) or `plain`.

**The bridge console** (`GameServerEscortCruiserBridge.cs`). The block `sps_console` has one use handler,
`UseSpsConsole`: it asks the cruiser's bridge first (`TryUseBridgeConsole` — the player is aboard a cruiser's wreck) and
otherwise calls the guide towers' hook `partial void UseTowerConsole(PlayerSession s, Vector3i cell, ref bool handled)`
([SPS_TOWERS.md](SPS_TOWERS.md)). Without a `hacking_chip`: `srv.cruiser.console_locked`. With one: the chip is used up,
every `light_red` within the bridge reach becomes `light_blue` — block changes of the interior, mirrored into the hull and
so into the wreck's stored changes —, the cue `lights_to_blue` plays, `srv.cruiser.console_blue` goes to everyone aboard
and the record is marked (a second chip is never taken: `srv.cruiser.console_done`).

- **ECHO's cruiser** (`CarriesEcho`): VEGA's sister core, held silent since the red night, speaks once and says goodbye —
  the VEGA pack's cutscene `echo.speaks` for everyone aboard (#3899, [CUTSCENES.md](CUTSCENES.md#the-sky-lights-scenes-3899)),
  **and** her five lines as panel lines under her own name (`ShipAiLine.SpeakerName`, `chr.echo.name`) with VEGA's two
  answers (`story.vega.echo.*`): a client that plays the scene keeps them out of VEGA's panel (they are the scene's
  `replaceLineKeys`) and gets them back when the scene is cut short; a client without cutscenes, or a player who was shown
  the scene before, reads them there. Everyone aboard gets `echo:freed` once (milestone `story:echo_freed`) and ONE
  `echo_memory_core`, once per player and wreck; a fighter who comes aboard later gets theirs at the boarding. Carried,
  VEGA reads the core's record into the Journal (`ReadEchoMemoryCore`, the 1 Hz story pass — the lore text
  `cruiser_echo_core` of site `cruiser_bridge`). ECHO never becomes a companion.
- **A later cruiser**: the lamps turn blue and the console shows one of the ship's logs (the other texts of site
  `cruiser_bridge`; `TryRevealLoreText` with a filter never draws ECHO's record there), no core.

**Tests** (`CruiserWreckTests`): the template loads, validates and is never rolled; the bridge markers stay out of the
editor's palette; the pinned hashes of the wrecks without the option; a fresh wreck (holes, no soot, no scrap, red lamps,
console, lamps, bay); its battle scars (scorch in at most one patch per torn room, dead screens only on `lab_panel`, plates
only on free deck cells — and every other cell and spot as without them); the scan card (subject, info, traits, the hull-box
range, knowledge once); one boardable wreck where the boss fell, clear of every hull; the flight spot's rules; a second wreck
replaces the first and its data is gone; a restart puts the wreck back with the players' changes; chests, the bay's robots
and the fighters' crates; the console without and with a chip, once; ECHO's cores, event, lines and Journal text; every
text in English and German.
