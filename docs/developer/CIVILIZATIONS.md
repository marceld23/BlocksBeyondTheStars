# Civilizations — peoples whose ruins exist only where they lived

> Status: **framework implemented** (epic #3649, parts #3638–#3648, terrain generation **43**,
> `WorldDescription.CivilizationsGeneration`, save version 16). Two framework civilizations ship on it: **the Vanished
> People and the Grand Technicians** (one ocean world per galaxy, everything under water) and **the Labyrinth Builders**
> (procedural maze ruins on up to three toxic or airless bodies per galaxy). The G.D.S., the Sounders and the SPS carry
> the same tag as *legacy* civilizations — their structures keep their own stampers.

A **civilization** is a people with its own buildings, defined as data: where it lives (one body, bodies of one kind,
bodies of one system), what its sites are, what its ruins look like, what VEGA, the Codex and the settlers say about it.
Adding one needs no C#. Only **lost** civilizations exist so far — ruins, nobody home; living aliens can follow on the same
framework (a new `state`).

## 1. The civilization file

One file per civilization: `data/civilizations/<key>.json` (`CivilizationDefinition`, checked by
`CivilizationRules.Problems`; a broken file is reported and dropped, the load never fails).

| Field | Meaning |
|---|---|
| `key`, `nameKey`, `beingsKey` | key (default: the file name), locale key of the people's name, optional key of what its beings or machines are called |
| `state` | `lost` (the only state so far): no residents, no posts, no dens |
| `placement` | `framework` = stamped by the civilization stage from its own templates; `legacy` = a tag only |
| `minTerrainGeneration` | never below 43 for a framework civilization (raised at load). **A civilization added later must name a newer generation** — its homes and its guarantee then never touch an existing world |
| `scope` | where it lives: `count` `[min, max]` homes per galaxy with optional `countWeights`, `perSystem` (at most this many homes per system, 0 = no cap), `planetTypes`, `atmospheres` (`breathable`, `toxic`, `none`), `conditions` (`notAsteroid`, `notGas`, `notVoid`, `planet`, `moon`), `startSystem` (`allowed`, `never`, `only`), `underwater` |
| `homeBody.suppress` | structure kinds a home body gives up: `landmark`, `giant_wreck` (everything else — settlements, outposts, bandits, ruins — stays and keeps clear of the sites) |
| `guarantee.planetTypes` | a new galaxy without a planet of these types gets one (§3) |
| `materials` | material token (`@wall`, `@accent`, `@roof`, `@floor`, `@path`) → block, for its templates |
| `ruin` | `rubble` blocks, `overgrowth` block (`none` = no flora; under water only an explicit one, e.g. `flora_seagrass`) |
| `sites` | pools in stamp order: `{ "pool": "giant" \| "city" \| "small", "count": [min, max], "nameKey": map name }` |
| `vega` | VEGA lines, each once per player: `{ "trigger", "value", "key" }` — `first` (first site found), `inside` (inside a site), `block` (near a block; `value` with a trailing `*` = prefix), `prop` (near a prop of pool `value`), `air` (in an air pocket) |
| `codex` | `titleKey` + `textKey` of the Codex entry (only what players can know) |
| `rumours` | locale keys settlers on other worlds tell |

## 2. Tags and templates

Every block, item, structure kit, template and loot table has a `civilization` field. On blocks, items, kits and loot
tables it is a tag: the scanner, VEGA, the Codex and the editor group by it (`GameContent.CivilizationOfBlock`). A block,
item or loot table tag that names no loaded civilization is warned once at load (`LegacyCivilizationTagsTests` checks the
kits and templates too).

A framework civilization's **templates** live in `data/civilizations/<key>/*.json` (the civilization defaults to the
folder). They are landmark-style templates (compact body, ruin mode/profile/strength/zones, ground row, markers) with:

- `civPool`: `giant`, `city`, `small` (the site pools) or `prop` (a small piece placed by a `prop` marker, never a site);
- `propPool`: a prop's pool name (`remains`, `statue`, `statue_large` …);
- `seat`: `seabed` for an underwater site (only a civilization template may name it);
- cell/palette kind `air`: an **air pocket** (§4);
- generator entries (`source` + `sourceParams`, e.g. the `maze_ruin` source) are allowed in any site pool.

No other pool ever offers a civilization template. A lost civilization's template may carry chests, loot (incl. data terminals and relic caches), sounds,
props, doors and circuits — never a resident, a vendor or a den (`CivilizationRules.LostMarkerAllowed`).

## 3. Homes and the guarantee

`CivilizationHomes.Pick` (WorldGeneration) is a pure function of the seed over the galaxy's **fixed** systems
(`sys0 … sys(N−1)`; frontier growth appends systems and never moves a home): per framework civilization, in file order,
the candidates that pass its scope (never a type with `restrictStructures`, never a body an earlier civilization took),
the count rolled from `count`/`countWeights`, then the candidates in hash order with the per-system cap. The server pins
the answer the first time a generation-43 world starts (`WorldMetadata.CivilizationHomes`, an empty list = no home) and
only reads it afterwards.

`UniverseGenerator.Civilizations.cs`: in a generation-43 galaxy whose fixed systems hold no planet of a civilization's
`guarantee.planetTypes`, the eligible planet with the smallest hash takes the first of them (the rules of the guaranteed
planet types: not in the start system, not the first breathable planet, not a type's first planet, never a landmark type,
another guaranteed type, the gas giant or a world that keeps only its own structures). A type the player switched off is
never forced in. Older galaxies are untouched.

## 4. The stage and the sites

`GameServerCivilizations.cs`, boot stage **"civilizations"** right after the Sounder world and before the settlements:

- replays every placement record of kind `civ:<key>` on the body (index = the site), or — on a home body untouched at
  load — plans and stamps the sites: per pool a count from the seed, per site a weighted pick (up to four tries, each a
  different template when one finds no seat), the instance seed and the turn from their own hash lanes;
- builds the layout with the civilization's materials, ruin options and props (`CivilizationLayouts`), a generator entry
  through its source and the same ruin pass;
- seats it: the guaranteed search with the footprint registry (kind `civilization`, site rules like a landmark) on land —
  only a graded seat (flat, slope, shelf; never stilts over water, where nothing would clear a maze's passages) — and the
  **sea floor** under water (§5);
- records every site of the body first and saves the records together, then stamps them: a crash mid-stamp replays all of
  them (each overlay is written once, `FeatureStamped`);
- pins one record (`turns`, `rev`, `seed`, the source, `wt` = the water top of a sunken site, `rim` = its crater rim,
  `name`, `storage=overlay`), writes the cells once into one structure overlay (`civ:<key>:<index>`), registers the
  footprint (and a sunken site's rim reach around it), materializes the
  markers (chests on their own lane `civ`, container prefix `loot_civ_chest_`, sounds);
- the home body skips the `homeBody.suppress` kinds (`StampLandmarks`, `StampGiantWrecks` record a skip there).

A civilization site is never staffed (`LandmarkInstance.Staffed`). Every later load re-derives it from its record; a
template that left the content keeps its blocks and re-derives nothing.

**Props** (`CivilizationPropDress`): after the ruin pass every `prop` marker (`pool=<propPool>` or `template=<key>`) places a
prop of the same civilization standing on its cell — picked and turned by a hash of the seed and the cell, written only
into empty cells of the marker's own room (the columns its floor reaches from the marker, never through a wall) and never
into another marker's cell or the cell above it (a chest and its cover stone). A cavity cell is taken only from a marker that
stands in a cavity itself (#3701: the flooded bowl around the Grand Technicians' eye), an air pocket never; a filled cavity
cell stops being one. The marker stays in the layout so VEGA knows where the props stand.

## 5. Under water

An `underwater` civilization's sites take the **seabed seat** (`GameServerCivilizationStamp.cs`): of 480 draws, the
footprint fully under liquid water with the best score (`SeabedScore`): the shallowest **cut** — how far the site's ground
lies below the highest natural floor of the footprint, i.e. the wall the dig leaves (#3700: a footprint cutting at most 10
blocks always beats a deeper one; a sandbank, and equally a single trench under a shelf, cut deep) — then the least water
to spare above it (a site in a deep trench is never found); the ground row rests on the footprint's deepest floor, sunk further when the
sea is too shallow for the site plus 3 blocks of water over its top (at most 48 blocks) — the site lies in a basin.

**The crater rim** (#3700, `GameServerCivilizationRim.cs`): a basin dug into a shallow sea used to be a shaft with sheer
walls — the Grand Technicians' giant ruin on a 1–3 m sandbank stood 35–37 m deep, its cubes invisible at the bottom. Now
the natural seabed (`TryGetWaterSurface`, not `SurfaceHeight`, which over the sea is the water's surface) around the
footprint is lowered to a slope, one block down per block toward the footprint from the site's ground up (0–1 block of
unevenness), out to the rim pinned in the record (`SunkenRimFor`: the highest sampled floor of the footprint above the
ground, +4, at most 48). Only columns under liquid water are lowered — an island or a shore never — and never another
footprint or a pad; lowered cells become water, the new top keeps the floor's surface block (no cave below opens into the
sea) and the flora on the old floor goes. The rim's reach is registered with the footprint, so what is placed later keeps
clear of the slope (one owner for both boxes, the site's overlay key). The seat search prefers a footprint whose rim box
keeps clear of every registered box, and its latitude band leaves room for the widest rim, so a rim never reaches across
the seam. A site stamped before the rim has none (`rim` absent).

The writer then fills every column from the ground row up to the higher of its
top and the natural floor: the site's blocks as they are, every other cell **water** at and below the water top — never
air — except the template's **air pockets**, which stay air. Air pockets are built as **diving bells**: closed by blocks on
every side and on top, open only from below (water flows down and sideways, never up). A player whose head is in one
breathes (ocean worlds are breathable). Sunken sites cast no far silhouette.

## 6. The world

`GameServerCivilizationsWorld.cs`: a site goes on the map (POI type `civilization`, its name) when a player comes within 80
blocks; discovery, the Codex kind `civilization`, the scanner readout, VEGA's lines and the rumours on other worlds — see
#3642.

Admin (#3654, GameServerAdminTeleportSites.cs): `/tp civ [n]` lists and reaches every site of the current body in stamp
order, named like the map entry (`civilization`, `zivilisation`, `volk`, `grosstechniker`, `labyrinth`, `maze` and their
plurals also work). On land the spot is three blocks in front of the footprint like a landmark's, found by `StandSpot`:
from the generated ground up to the first two cells a body fits in, a liquid counting as passable. #3702: a sunken site's
natural surroundings can be a sandbank at the surface, so its spot lies **in the ruin on its floor** (`SunkenStandSpot`):
from the footprint's centre outward along −Z, the first column with a solid floor under two cells of water (never an air
pocket, never the red fluid) between 8 blocks under the site's ground and 3 over it, open to the sea above (not the
channel under the giant's eye) — for the giant dwelling the bowl
beside the eye; the spot in front of the footprint only when no such floor exists.

## 7. The shipped civilizations

| Civilization | Where | Sites |
|---|---|---|
| `grosstechniker` — the Vanished People; their machines are the Grand Technicians | one `ocean`/`rainbow_sea` body per galaxy (an ocean guaranteed), all under water (the sea worlds see 30 m under water, #3699); no landmark, no giant wreck there | the giant dwelling (eye, tank with red fluid and an air pocket, tipped glyph cubes, the mural, #3701: eight remains in the bowl around the eye 4–10 m from the tank), 2–3 sunken cities, 4–6 small sites, remains as props |
| `labyrinth_builders` | 1–3 toxic or airless bodies per galaxy (weights 20/30/50 %), each in another system; no landmark, no giant wreck there | one `maze_ruin` (S/M/L ≈ 80/120/180 blocks, walls ~20 high, passages 2–3 to 8–10 wide, lowest wall rows kept), square-headed statues as props |
| `gds`, `sounders`, `sps` | legacy tags — placement unchanged | — |

Scripts: `tools/landmarks/civ_grosstechniker.py`, `tools/landmarks/civ_labyrinth_statues.py` (helpers in
`tools/landmarks/civ.py`; previews with `tools/landmarks/preview.py`). Lore: [LORE_STRUCTURE.md](LORE_STRUCTURE.md).

## 8. Adding a civilization

A standing site is re-derived on every load from its record AND the civilization file (materials, ruin look, props): change
those only with a new `minTerrainGeneration`, or the re-derived chests and VEGA's prop positions of existing sites may move
(the stamped blocks stay). The galaxy guarantee is pinned per world (`WorldMetadata.CivilizationGuarantees`), so a
changed or broken file never retypes a guaranteed body back.


1. Write `data/civilizations/<key>.json` (§1) with a new `minTerrainGeneration` if any world already exists at the current
   one; raise `WorldDescription.CurrentTerrainGeneration` with it.
2. Build its templates into `data/civilizations/<key>/` (hand-built with the tools, or a generator entry).
3. New blocks shift the block ids: raise `WorldMetadata.CurrentSaveVersion` (see DEVELOPER.md).
4. Texts in en + de (names, Codex, VEGA, rumours, site names), then the community languages.
