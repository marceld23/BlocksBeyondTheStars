# Landmarks — giant authored buildings, derived into ruins

> Status: **framework implemented** (epic #3483, parts #3484–#3500, terrain generation 37). The buildings
> themselves — the school club's list — are a content package that follows; today the game ships three small
> **samples** that are never rolled and only stamped by an admin (`/landmark <key>`).

A **landmark** is one giant building that stands on a planet on its own: a leaning tower, a city gate, a mine, a
statue. It is authored (and stays editable) in the structure editor, and most of them stand only as **ruins** that the
server derives from the intact design with the instance's seed — so the same tower never falls apart the same way
twice. This is the *how it fits together* reference; for the stamp chain around it see
[WORLD_GENERATION.md](WORLD_GENERATION.md), for the editor [STATION_SETTLEMENT_EDITOR.md](STATION_SETTLEMENT_EDITOR.md),
for the older ruins [FACTORIES_RUINS_AND_CLAIMING.md](FACTORIES_RUINS_AND_CLAIMING.md).

## 1. Why a kind of its own

Before the landmarks an authored template was a whole settlement (always intact and inhabited — a template without a
`vendor` marker even gets a fallback vendor), a plot or district module of a kit, a station module or an underground
structure. A single building on the surface had no stamper. And the game had five decay passes — the fallen
settlements, the monuments, the buried G.D.S. ruin, the abandoned rail station, the rune shrines — each wired into
its own generator; none could be called for an arbitrary structure.

## 2. The template

A landmark is a `StructureTemplate` with `landmark: true`, one file per landmark under `data/landmarks/<key>.json`
(the editor's own under `usercontent/landmark_templates/`). It never enters a settlement, module or underground pool,
and nothing else enters the landmark pool (`GameContent.Landmarks`, `LandmarksFor`).

| field | meaning |
|---|---|
| `ruin` | `always` = run through the ruin pass; `never` / empty = as built |
| `ruinProfile` | `masonry` (walls and towers, the default), `lattice` (light erosion, whole spans fall), `underground` (cave-ins) |
| `ruinStrength` | 0..1, 0 = the profile's own (0.5; lattice 0.4) |
| `requires` | `atmosphere`, `inhabited` (the body carries at least one inhabited settlement) |
| `groundRow` | the template row that lies on the ground; the rows below are carved into the rock |
| `protected` | an inhabited landmark: read-only like a lodge, its posts staffed (ignored on a ruin) |
| `nameKey` | the locale key of its display name (map, VEGA); empty = `name` |
| `planetTypes`, `pack`, `weight`, `pinOnly` | as for every template |

A landmark that is broken (an unknown word, a size above 256, an unknown block, a ground row outside the structure) is
dropped at load with a warning — it never breaks the load (`LandmarkRules.Problems`).

### The compact body (#3485)

One JSON object per cell costs about 120 bytes and an object in memory; a giant has 50 000 to 600 000 cells. A
template may therefore carry its plain cells — a block or a cavity with tint, glow, shape and the *keep* flag — as a
`palette` plus run-length `rows`, one string per (y, z) row: `"y,z:x.count.palette x.count.palette"`. Cells with
settings (markers, ports, Crystal Net devices) stay in `cells`. `TemplateBody` compacts, expands and — what the
readers use — walks the plain cells of either form without ever building cell objects. A hollow 256 × 128 × 256 hall
(600 000 cells) is under 1 MB.

The files are small, so they are loaded with the rest of the content and stay in memory in compact form; the dense
voxel grid exists only while a landmark is built.

### Marker settings (#3488)

A marker cell's `config` is its settings line, `key=value;key=value` (`MarkerSettings`). It travels as
`SettlementMarker.Data` through every offset and quarter turn.

| marker | settings |
|---|---|
| `chest` | `items=iron_plate:2-4,gold_ingot:1` (fixed; a range draws), `table=<key>` (a roll of `data/loot_tables.json`), both, or neither (the table `ruin_common`); `chance` (the chest exists at all); `block` (`wood_crate` / `crate`); `hidden=1` (a stone on top) |
| `vendor` | `theme` (the `marketTheme` of the recipes it sells), `inside=1` (sells only to a customer in its own closed room), `name` (a locale key for the nameplate) |
| `sound` | `sound` (a bundled loop), `radius` (blocks), `volume` (0..1) |
| `den` | `species` (an authored creature key, or `native` = the planet's first peaceful species), `count`, `radius` |

`data/loot_tables.json` holds named tables: rolls of weighted entries with count ranges and a chance. Every draw is a
hash of the chest's own cell, never the stamp's random stream — a chest's content does not depend on what was rolled
before it. The older marker kinds (`loot`, `chest` in a settlement, `relic_cache` …) keep their hard-coded rolls: their
streams are in saved worlds.

## 3. The ruin pass (#3487)

`LandmarkGenerator.Build(template, seed, turns, content, surface)` reads the body into a voxel grid, turns it by
quarter turns, resolves the material tokens (`@wall` … follow the planet) and — for a ruin — runs `StructureRuiner`.
Every choice is a stateless function of seed, lane and cell (hashes and value noise), so the same seed gives the same
ruin and no step moves another:

1. **Crumbled patches** — in coherent patches of columns (2-D value noise) everything above a cut height falls: a
   tower loses one flank of its top.
2. **Erosion** — bites out of the walls (3-D value noise, so a hole is a hole) plus a light speckle, both rising with
   height. The first course above the ground stays.
3. **Collapse spots** — ragged shafts through roofs and floors (masonry) or a ball cut out of the frame (lattice).
4. **Openings** — glass is smashed; a light becomes its switched-off twin (`<key>_off`) or is gone.
5. **Cave-ins** (profile `underground`) — stretches of a tunnel or cavern fill with rock, never within five rows of
   the ground row: the way in stays open.
6. **Settle** — whatever no longer hangs together with the ground comes down (a flood fill through faces and edges,
   so a stepped arch and a diagonal lattice member hold).
7. **Rubble and overgrowth** on the ground where something fell.

What stays: cells the author marked **keep** are never touched and hold what hangs on them; ladders and shaped pieces
are never eroded but fall with the wall that carried them; rows at and below the ground row are anchored. A marker's
cell is never filled, and a marker whose floor fell comes down with it and rests on what is left. A ruin keeps only
the markers a visitor finds — chests, the classic loot markers, sounds and dens; residents, vendors, doors, rooms and
pre-built circuits are gone.

`StructureRuiner.Revision` is pinned with every placement: a stamped ruin is re-derived on later loads for its
markers, so a change of the pass needs a new revision that old records do not take.

## 4. The stamp (#3486, #3490)

`StampLandmarks` is one line of the structures boot stage, after everything that stands on the ground and before the
buried ruins — so the vaults, cubes, chests and unique sites stamped later keep clear (`OverlapsAnySettlement`).

- **Gate.** `WorldDescription.LandmarkGeneration` = 37 = `CurrentTerrainGeneration`. A server-side stamp: no chunk of
  any generation changes.
- **Where never.** An asteroid, an airless, gas or void world, the finale body; with `PlaceLandmarks` off; on a type
  with a structure whitelist unless it names `landmarks` — and then only landmarks whose `planetTypes` name that type
  (Soaniri, #3500).
- **Roll.** `LandmarkRolls`: a hash of seed and body below `LandmarkChance` (0.34) × the structures frequency — about
  every third planet.
- **Pick, turn, seed.** One of the planet's candidates by weight (only those the body is four footprints around), a
  quarter turn and the instance seed, each a hash of its own.
- **Seat.** The guaranteed placement search (#586) with every pad and structure reserved. A landmark that only stands
  on the ground may stand in water on its piers; one with an underground part needs a flat or sloped dry seat.
- **Pinned.** One placement record (`landmark`, index 0): the template key, the seat, and `turns`, `rev`, `seed` in
  its layout field. A body that was visited before, a roll that said no and a failed seat all record a skip.
- **Written once** (feature `landmark:<index>`), in one transaction:
  - *above the ground row* the monument rule — no foundation plate; a column that carries something is cleared from
    the ground row to its top and seated on a plinth (a pile every fourth column in water);
  - *below it* only the template's blocks and its **cavity** cells carved to air; an empty cell keeps the planet's
    rock. Beside every cavity that touches air, a liquid or loose ground a stone seals it (the buried ruin's rule);
  - a crate block in every chest marker's cell that exists by its chance.

Ruins are not protected: what players mine stays mined.

## 5. In the living world

- **Chests** — a one-time `salvage` container in the crate's cell (`GeneratedLoot`), content as its settings say.
- **Sound emitters** (#3494) — the client is sent the world's emitters on entry (`LandmarkList`, tag 335) and plays
  the three loudest, one per sound, on pooled voices: silent at the radius, a smooth swell, full within 12 % of it
  (`SoundEmitterMix`).
- **Dens** (#3495) — while a player is within the den's radius + 48 it brings its pack up to the count, one animal a
  second, keeps them within the radius and out of the far-creature prune; an animal that was killed or tamed is
  replaced after five minutes. A den only houses a species the world's roster already has — it never adds one.
- **Discovery** (#3496) — on everyone's map (type `landmark`) when a player on foot comes within 80 blocks; VEGA
  points the first one out once per player; a scan of any of its blocks reads as the landmark and is worth a relic's
  knowledge once per body and landmark (ledger `landmark:<body>:<key>`, its own group in the Codex).
- **Inhabited landmarks** (#3499) — `protected` and not a ruin: the box, down through the plinth, is read-only through
  `IsSettlementBlock`; profession posts and `vendor` / `npc` markers are staffed on every load and keep their post.
- **Admin** — `/landmark list`, `/landmark <key>` (cheats on: stamps one six blocks in front of the admin under the
  next record index, with a fresh seed each time), `/tp landmark [n]`.

## 6. Seeing it: streaming and the far silhouette

- **Streaming (#3489).** A near chunk column streams three chunks above the player, a far one its surface chunk plus
  one. A column that carries a landmark streams up to the landmark's top *in that column*, capped by the player's
  chunk sphere (the sent-set prune radius minus one): a chunk beyond it would be dropped and sent again every pass.
  The stone text tablets keep their own uncapped lift.
- **Far silhouette (#3497).** Beyond the streamed chunks a build is a height blob per 8- or 32-block cell. The server
  sends a coarse model of each landmark with the list (`LandmarkSilhouette`: one cell per 4³ blocks from the ground
  row up, filled from three solid blocks, a palette of block ids and dyes). The client meshes its skin
  (`LandmarkSilhouetteMesh`), colours it from the block atlas and draws it with the far terrain's shader; vertices
  marked `TEXCOORD0.x < 0` are cut by a sphere around the player (`_Sc_LandmarkClip`) instead of the column mask, so
  the top of a tower above the streamed sphere stays drawn. The far tiles leave a landmark's cells out.

Known limits: the model comes from the pinned layout, so a landmark players dismantled still shows whole from afar;
with the far view off it is drawn up to 512 blocks and only as far as the camera's far plane reaches.

## 7. Authoring

- **In the editor** (town editor, *Use as → Landmark*): the room is 256 blocks a side; the form sets ruin, profile,
  strength, conditions, the ground row and "inhabited"; **Show as a ruin** runs the server's own pass with a seed and
  shows the result (nothing can be edited or saved while it shows); the palette has the **keep** brush, the **cavity**
  brush, the **sound** and **den** markers and the **settings** tool. Save writes the compact file to the user-content
  folder — the local server offers it to the next new world — and `landmark_exports/<key>/landmark.json`.
- **Into the game:** `python tools/merge_structure.py <landmark_exports/<key>>` writes `data/landmarks/<key>.json`
  (defaults stripped, `pinOnly` of an existing file kept).
- **By script:** `tools/gen_landmarks.py` holds a small `Design` class (boxes, shells, cavities, markers) and writes
  the same compact format; the three shipped samples come from it. A generated landmark opens in the editor like any
  other.
- **New blocks** for the content: `mine_track`, `mine_cart`, `dripstone`, `burger_bun`, `burger_patty`,
  `burger_cheese` (#3498, save version 14). A stalagmite is a `dripstone` cone; hanging, the same cone with its up
  face turned down.

## 8. Key files

| Area | Files |
|---|---|
| Template, rules, settings, loot | `Shared/Definitions/StructureTemplate.cs`, `LandmarkRules.cs`, `MarkerSettings.cs`, `LootTable.cs`; `Shared/Content/TemplateBody.cs`, `StructureCatalog.cs`, `GameContent.Landmarks.cs`, `ContentLoader.cs` |
| Build and ruin | `WorldGeneration/LandmarkGenerator.cs`, `StructureRuiner*.cs`, `VoxelGrid.cs`, `LandmarkSilhouette.cs` |
| Server | `GameServer/GameServerLandmarks.cs` (roll, pick, seat, record), `…LandmarkStamp.cs`, `…LandmarkLife.cs` (markers, chests), `…LandmarkWorld.cs` (emitters, dens, discovery, the client's list), `…LandmarkResidents.cs`, `…LandmarkStream.cs`, `…LandmarkAdmin.cs` |
| Wire | `Networking/Messages/LandmarkMessages.cs` (tag 335) |
| Client | `Client.Core/Audio/SoundEmitterMix.cs`, `Client.Core/LandmarkSilhouetteMesh.cs`; `SoundEmitterView.cs`, `LandmarkSilhouetteView.cs`, `Shaders/FarTerrain.shader`, `WorldMap.cs`, `WikiUI.cs` |
| Editor | `StructureEditor.Landmark.cs`, `EditorMarkerPanel.cs` |
| Data and tools | `data/landmarks/*.json`, `data/loot_tables.json`, `tools/gen_landmarks.py`, `tools/merge_structure.py` |
| Tests | `LandmarkTemplateTests`, `StructureRuinerTests`, `LandmarkStampTests`, `LandmarkWorldTests`, `LandmarkClientTests` |
