# Map planets — terrain from a planet map

> Status: **Implemented** (#3977, #3978, #3979, 2026-11). The engine and the map format are in place;
> `data/maps/earth.bbmap`, `moon.bbmap`, `mars.bbmap`, `venus.bbmap` and `mercury.bbmap` are built, and the browser
> fetches them on demand (§6). Which planet types use a map is data (`terrainMap` in `data/planets.json`): Sol's Earth,
> Moon, Mars, Venus and Mercury (§4). A map world's fixed places and landing pads come from its place list (#3981, §7).

A procedural world is rolled from its seed. A **map world** takes its ground and its biomes from a **planet map**: a grid
of real elevation and biome classes, sampled onto the torus. Everything else — rivers, caves, ores, small landforms,
flora, fauna, structures — still comes from the seed and runs over the map's ground exactly as over rolled ground.

See [WORLD_GENERATION.md](WORLD_GENERATION.md) §4–§5 for the procedural terrain and biomes and
[WORLD_WRAP.md](WORLD_WRAP.md) for the torus.

---

## 1. The terrain base form (#3977)

The **base form** is the shape a world's land has before any overlay. It gives three answers, and the generator asks
only the form:

| Answer | Asked by | Rolled | Cratered | Map |
|---|---|---|---|---|
| raw height of a column | `RawSurfaceHeight` | archetypes / styles × drama on the baseline (continents, escarpments, …), the sand sea | flat regolith + craters + crater chains | the map's elevation through the height curve + detail |
| sea level and fluid | the calibration (`BuildCalibration` step 2) | a percentile of the sampled ground: gas, water or lava (`PercentileSea`) | as rolled | the fixed `mapSeaLevelY`, water (dry without water) |
| biome index of a column | `BiomeIndex` | sand sea and hot zones first, then the region-and-altitude mix (`ProceduralBiomeIndex`) | as rolled | the map's class at a jittered point |

The form is chosen once per world (`BaseFormFor`, cached in the world's `WonderProfile` and copied into its
`WorldCalibration`): **map** when the type names a `terrainMap`, else **cratered** when the type or the body is cratered,
else **rolled**. A form may also switch off feature gates it does not carry (`RestrictFeatures`), before the world's
landmark rows are chosen. The landmark overlays, the sea-relative rows, the river valleys, the site grades and every
cache stay outside the form.

Code: `WorldGenerator.BaseForm.cs` (the abstraction, rolled, cratered), `WorldGenerator.MapBaseForm.cs` (the map form),
`MapTerrain.cs` (the map's integer sampler). The split from the former branches was behaviour-identical: the terrain
goldens and the wrap, column-cache and calibration tests did not change.

## 2. The planet map file (`.bbmap`)

`data/maps/<key>.bbmap`, read by `PlanetMapCodec` (Shared), written by `tools/planet_maps/bbmap.py`. Little-endian:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | magic `BBMP` |
| 4 | 2 | format version (1) |
| 6 | 2 | layers: bit 0 elevation, bit 1 biome (both required in version 1) |
| 8 | 4 | width (cells per row — the whole circumference) |
| 12 | 4 | height (rows — the whole latitude period) |
| 16 | 8 | content hash: FNV-1a 64 over the uncompressed payload |
| 24 | … | raw deflate (RFC 1951): `width·height` int16 elevations in metres, then `width·height` biome bytes |

**Projection.** Equirectangular. Column 0 is longitude 0 = world X 0, the columns run east over the circumference.
Row 0 is the **south** edge = world Z −P/2 (P = `WorldConstants.LatitudePeriodFor(C)`), the rows run north. A cell is
C/width blocks wide and P/height blocks tall — Earth at 16,000 blocks: 2048 × 1024 cells of about 7.8 blocks.

**Biome classes** (`PlanetMapBiome`): 0 ice, 1 tundra, 2 boreal forest, 3 temperate, 4 steppe/savanna, 5 desert,
6 rain forest, 7 high mountains. A map world's planet type lists its biomes **in this order**; its world keeps the whole
list in order (no per-world shuffle), so class *i* is biome *i* (a shorter list clamps to its last entry). A body without
Earth's climate (the Moon, Mars) uses the classes as plain indices — see §5.

**Loading.** The content load never reads a map. `GameContent.GetPlanetMap(key)` reads and decodes it on first use and
keeps it; `RegisterPlanetMap(key, bytes)` hands the content a map directly and `SetPlanetMapSource` replaces where maps
come from (a client that fetches maps on demand). A planet type naming a missing map is reported by the loader, and
generating such a world throws — a world built without its map would be a different world. A damaged file (wrong magic,
version, size, hash or class) throws `InvalidDataException`.

**Frozen.** A map is frozen once a release ships it: every world made from it would change. A changed map is a new key.

## 3. Sampling the map (integer only)

Server and client must build the same ground, so the map path uses no floating point and no trigonometry:

- **Elevation** (`MapTerrain.ElevationFixed`): the column's place in cell units in fixed point (×256), the four cell
  centres around it, bilinear in `long` arithmetic → metres × 256. Both axes wrap (column W−1 meets column 0, row H−1 meets
  row 0).
- **Height curve** (`MapTerrain.HeightBlocks`), floored, from `mapSeaLevelY`:

  | Elevation | Blocks per metre | Defaults |
  |---|---|---|
  | 0 … `mapKneeMetres` | 1 / `mapLowlandMetresPerBlock` | 20 m per block up to 1,500 m (75 blocks) |
  | above the knee | 1 / `mapHighlandMetresPerBlock` | 57 m per block: 8,848 m → Y 283, under the 288 cap |
  | below 0 m | 1 / `mapOceanMetresPerBlock`, at most `mapOceanMaxDepth` deep | 120 m per block, 48 blocks deep from about 5,700 m |

  Floored means 0 m and up is never below the sea and anything below 0 m always is: the coastline is the map's.
- **Detail**: the world's torus FBM (wavelength 24 blocks, ±`mapDetailAmplitude`, default 3) so a slope between two cells is
  no flat ramp. It is scaled down within that many blocks of 0 m and can never move a column across the waterline. It is
  the existing seeded noise every procedural world uses (deterministic on every runtime) and rounds to an integer before it
  meets the map's integers.
- **Biome** (`MapTerrain.BiomeClassAt`): the class of the cell under a point jittered by two noise fields up to three cells
  (`MapBiomeJitterCells`), so a border is no pixel stair. Never blended — a class is a category.

**The torus seam.** A map's north and south rows are different places on the real body (Earth: the Arctic Ocean and the
Antarctic plateau). When a map is prepared (once per map, `MapTerrain.For`), the outermost 4 % of rows on each edge
(`SeamBandPercent`) blend toward one **seam height** — the mean of the two edge rows — with a smoothstep in fixed point,
and the outer half of that band becomes the ice class. The Z seam then joins without a cliff; the X seam joins because the
columns wrap.

## 4. What a map world carries

- **Sea**: water at `mapSeaLevelY` when the type has water (`waterAbundance`, by default any world with air); a type with
  `waterAbundance: 0` is dry. No lava sea, no gas sea, no percentile.
- **Off** (`MapBaseForm.RestrictFeatures`): the procedural giants — volcano cones, calderas, massifs, table mountains, rifts
  and the mega-rift, escarpments and the other baseline regimes (stepped, tilted, equatorial ridge, salt polygons, basalt
  fields), continents, shield volcanoes, impact basins, glacial troughs, the sand sea, the gaming landmarks and the
  spectacle families (generation 21). The map has its own mountains and coasts.
- **On**: rivers (routed over the map's ground), river valleys and site grades (generation 40), caves, ores, the small
  landforms the type's tags select, flora, fauna, structures.
- **Craters** (#3979): a cratered map world — the type's `cratered` (Mercury) or an airless moon (the Moon) — lays the
  cratered form's impact bowls and crater chains (`CraterCarve`, `CraterChainCarve`, the body's own crater profile) on top of
  the map's ground (`MapCraters`). Such a body is dry, so no crater crosses a waterline.
- **One generation rule** (#3981, `MapWorldRules.PipelineGeneration`): a map world joins saves of every terrain generation —
  Sol appends itself to old galaxies too — and its fixed places need the newer structure pipeline. So on a map world the
  geography stages (river valleys, site grades, the river distance layers — `WorldGenerator.GeographyWorldFor`) and the
  server's structure pipeline (the footprint registry of generation 38, the landmark overlays, rules and ruin revision of 39,
  the reserved and graded ground and the site-biome foundations of 40 — `GameServer.PipelineGeneration`) run at least at
  `GeographyGeneration` (40), whatever generation the save was created with. Server and client read the same rule (the far
  view grades the same sites). Only map worlds read it, so no world that existed before changes; *which* kinds roll, and
  from which generation, still follows the save's generation.
- **The globe** (client): the polar-cap blend of the orbit globe (`PlanetGlobeCaps`, see
  [PLANET_VIEW_AND_WEATHER.md](PLANET_VIEW_AND_WEATHER.md) §1a) is off — the map's poles are real
  (`PlanetMapData.RealPoles`, `PlanetGlobeCaps.AppliesTo`, `WorldMinimap.RequestGlobe`).

### The `PlanetType` fields

| JSON | Default | Meaning |
|---|---|---|
| `terrainMap` | `""` | the map key (`data/maps/<key>.bbmap`); empty = procedural terrain |
| `mapSeaLevelY` | 80 | world Y of 0 m (the sea level) |
| `mapLowlandMetresPerBlock` | 20 | the lowland stretch |
| `mapKneeMetres` | 1500 | where the lowlands end |
| `mapHighlandMetresPerBlock` | 57 | the high-mountain compression |
| `mapOceanMetresPerBlock` | 120 | the sea-floor scale |
| `mapOceanMaxDepth` | 48 | deepest ground below the sea (blocks) |
| `mapDetailAmplitude` | 3 | the rolled detail (blocks, 0–16) |

### Sol's map types (`data/planets.json`)

| Type | Map | Curve (sea Y · lowland m/block · knee · highland m/block · ocean m/block, depth) | Highest cell → Y | Water |
|---|---|---|---|---|
| `sol_earth` | `earth` | 80 · 20 · 1,500 · **37** · 120, 48 | 6,309 m → 284 | yes (0.6) |
| `sol_moon` | `moon` | 100 · 60 · 3,000 · 75 · 170, 50 | 9,725 m → 239 | dry |
| `sol_mars` | `mars` | 80 · 40 · 3,000 · 150 · 160, 50 | 21,091 m (Olympus Mons) → 275 | dry |
| `sol_venus` | `venus` | 80 · 20 · 1,500 · 70 · 120, 48 | 10,243 m → 279 | dry |
| `sol_mercury` | `mercury` | 80 · 30 · 1,500 · 40 · 120, 48 | 4,169 m → 196 | dry |

Each `atmosphereHeight` stands above its highest ground (Earth 320, Mars and Venus 310, the Moon 260, Mercury 220).
**Earth's biomes**, in class order: ice (snow over ice, −25 °C), tundra (grass over peat, `tundra`, −6 °C), boreal forest
(`tundra` flora, trees ×1.8, 2 °C), temperate (`temperate`, 12 °C), steppe/savanna (`savanna`, trees ×0.25, 22 °C), desert
(sand over sandstone, almost bare, no trees, 33 °C), rain forest (grass over mud, `tropical`, flora ×1.6, trees ×2.4,
27 °C), high mountains (stone, `alpine`, −8 °C). Its plants wear a fixed palette of natural greens (`floraPalette`, the
seasons still turn them), `colourMood: balanced`; `creatureAbundance: authored` — only the Earth animals (`homes`) live
there, no rolled aliens. The other four list class 0 (the poles: ice under the Moon's and Mercury's, Mars' polar caps of
ice) and classes 1–7 from the lowest ground to the highest (the Moon's maria basalt, its highlands stone; Mars sand,
sandstone and the basalt of the Tharsis heights; Venus basalt, ash and sulfur stone).

## 5. Building the maps (`tools/planet_maps`)

```bash
cd tools/planet_maps
uv run planet_maps.py earth --probe   # data/maps/earth.bbmap + .cache/earth_preview.png
uv run planet_maps.py moon mars       # data/maps/moon.bbmap, mars.bbmap (1024 × 512)
uv run planet_maps.py venus mercury   # data/maps/venus.bbmap, mercury.bbmap (1024 × 512)
```

Downloads land in the git-ignored `tools/planet_maps/.cache/`; only the derived `.bbmap` files are committed. Sources,
licences and attributions: [tools/planet_maps/README.md](../../tools/planet_maps/README.md) and `NOTICES.md`.

- **Earth** (2048 × 1024): elevation from NOAA ETOPO1 (ice surface: Antarctica and Greenland at their ice height), read at
  every 5th arc-minute and area-averaged to the cells — the cell mean, so a cell lies below the summit it contains: the
  built map spans −10,022 m to 6,309 m (the Everest cell; Y ≈ 239 with the default curve — a type that wants its highest
  cell just under the cap lowers `mapHighlandMetresPerBlock`, e.g. 37 puts 6,309 m at Y 284). Biome classes from the Natural Earth I land-cover colours, the latitude and the elevation (forest
  green by latitude band → rain forest, temperate, boreal; arid tan → desert; other dry colours → steppe; above 58°
  outside forest → tundra; from 3,000 m → high mountains; white or beyond 78° → ice). Sea cells take the class of the
  nearest land cell, so a coast's biome border is the land's even where the jitter reaches out to sea.
- **Moon, Mars** (1024 × 512): elevation from LRO LOLA (LDEM_4) and MGS MOLA (MEGDR, 4 pixels per degree). No climate:
  class 0 above 80° latitude, else seven equal-area elevation bands (class 1 the lowest ground … 7 the highest) — indices
  into the body's own biome list.
- **Venus, Mercury** (1024 × 512, #3979): elevation from the USGS Astrogeology global mosaics — Magellan Global
  Topography 4641 m and the MESSENGER Global DEM 665 m. Both are large uncompressed GeoTIFFs, so the tool reads only every
  4th (Venus) / 9th (Mercury) row over HTTP range requests (`remote_tiff.py`) and area-averages the columns in full.
  Magellan's altimetry gaps (about 8 % of the samples) are filled smoothly from the cells around them. Built ranges:
  **Venus −2,750 m … 10,243 m** (from the 6,051.8 km mean radius; Maxwell Montes' cell 9,735 m, Ishtar Terra and
  Aphrodite Terra plain to see) and **Mercury −5,338 m … 4,169 m** (from the DEM's 2,439.4 km sphere; the Caloris basin a
  smooth plain inside rough rim country). Classes as for the Moon and Mars. The tool also writes `<key>_relief.png`, the
  elevation alone under a hill shade, to check such landmarks.

## 6. Maps in the browser (#3979)

The desktop reads a map from `data/maps` the first time a generator asks (§2, *Loading*). The browser must not: everything
in `StreamingAssets/data` that the manifest lists is downloaded before the first menu frame, and the maps are megabytes
nobody needs before the Sol system appears. So:

- **Out of the eager download.** `data/maps/` is still synced into `StreamingAssets/data/maps/` (the sync scripts copy all
  of `data/`), but `BuildScript.EnsureStreamingAssetsManifest` leaves every file for which
  `PlanetMapDemand.IsOnDemandDataFile` holds (everything under `maps/`) out of `manifest.json`, the generated fallback
  list and the fallback-staleness warning; the built-in `StreamingAssetsCache.FallbackManifest` never lists them either
  (`StoryUiFixesClientTests` checks it against `data/` minus those files).
- **The decision** (`PlanetMapDemand`, Client.Core, headless-tested in `PlanetMapDemandTests`): which map a planet type
  needs (`MapKeyOf`), whether it is here (registered, or readable from the content's source — a copy kept earlier), and
  starting **one** fetch per missing map (`IsReady`, `Need`). A damaged or failed fetch is tried again on the next ask, at
  most `MaxAttempts` (3) times per session. Built without a fetch (desktop, the Editor), every map counts as ready and
  nothing changes.
- **The fetch** (`PlanetMapFetcher` + `StreamingAssetsCache.FetchOnDemand`, client asmdef): `UnityWebRequest` of
  `StreamingAssets/data/maps/<key>.bbmap` — only where StreamingAssets is remote (HTTP), so a desktop build keeps the file
  path. The bytes are kept in the browser's data cache (a new build clears it with the rest) and handed to
  `GameContent.RegisterPlanetMap`.
- **Who waits.** Every place a map planet type reaches a `WorldGenerator` on the client asks first:
  - the **in-process browser server** (`BrowserLocalServer`) — it builds worlds inside its tick (a join, a landing, a warp),
    so it does not tick while any map of a body in its galaxy is still on its way (`PlanetMapTickGate`): after its start,
    and again when the galaxy grows the Sol system. A map that failed every attempt no longer holds it — a world that needs
    it then fails loudly instead of freezing the game;
  - the **far terrain** (`FarTerrainView`, `FarWorldGenerator` via `FarTerrainSource`) and the **biome looks**
    (`GameBootstrap.BiomeLooks`, `BiomeLookResolver`) — they build their generator once the map arrived
    (`PlanetMapWait`);
  - the **planet bakes** (`WorldMinimap`, `PlanetMapBakeJob`: the orbit globe, the bodies in the sky, the landing map) — a
    map body's bake stays parked and its caller keeps the flat colour until the map is here.
- **Asking early.** When the star map arrives (`GameBootstrap.Knowledge`), the maps of every map body on it start loading,
  so they are usually here before the player lands. Hosted multiplayer in the browser fetches the same way (the world itself
  is the remote server's; the client needs the map for the far terrain, the biome looks and the bakes).
- **Not covered:** a browser save whose *start* world (`WorldMetadata.DefaultPlanetType`, generated inside
  `GameServer.Start`) is a map planet — no start body is one today; such a start would need the map before
  `BrowserLocalServer.StartServer`.

## 7. Fixed places and landing pads (#3981)

A map world's type may carry a **place list**: `data/places/<planetType>.json` (`PlaceListDefinition`, loaded by
`ContentLoader.LoadPlaceLists` into `GameContent.PlaceLists`, `PlacesFor(type)`). Every body of that type reads it.

```json
{ "places": [
  { "key": "pad_london", "kind": "pad",      "latitude": 51.51,  "longitude": -0.13 },
  { "key": "berlin",     "kind": "city",     "latitude": 52.52,  "longitude": 13.40,
    "landmarks": ["earth_brandenburg_gate", "earth_berlin_tv_tower"] },
  { "key": "giza_great", "kind": "landmark", "latitude": 25.821, "longitude": 25.328, "template": "earth_pyramid_great", "turn": 0 }
] }
```

| Field | Kinds | Meaning |
|---|---|---|
| `key` | all | unique in the list, lower-case letters, digits, underscores |
| `kind` | all | `landmark`, `city` or `pad` (`PlaceListKinds`; a later kind, e.g. a rail stop, is one more constant and one more check) |
| `longitude`, `latitude` | all | degrees, east and north positive: −180 ≤ longitude < 360, −90 < latitude < 90 |
| `template` | landmark | a landmark template that is `pinOnly` (in no pool) |
| `turn` | landmark (city later) | the quarter turn, 0..3 |
| `theme`, `grid`, `kit`, `nameKey`, `landmarks` | city | stored for the metropolis package (theme = the key when empty, grid 0 = the composer's default, the landmark keys `pinOnly`); not stamped yet |

**The projection** (`PlaceRules.BlockOf`, `PlaceDefinition.BlockOn(C)`): X = longitude / 360 · C wrapped to [0, C), Z =
latitude / 180 · P with P = `WorldConstants.LatitudePeriodFor(C)`, rounded to the nearest block — the map's own projection
(§2: column 0 = longitude 0, row 0 = the south edge = Z −P/2, so north is +Z). On Earth (16,000 blocks) that is 44.4 blocks
per degree both ways.

**Load checks** (`GameContent.ValidatePlaces`, `PlaceRules.Problems`, a content validation error): the list's type exists and
has a terrain map; keys unique; kinds known; coordinates in range; turns 0..3; every named template exists and is `pinOnly`.
**The place-only guard** (`PlaceListTests`, #3982): every template a list names is `pinOnly`, and every `earth_*` landmark is
named by a place (a landmark entry or a city's `landmarks`).

**The order is pinned.** A landmark place's record index and a pad's index follow the list, so a place is only ever
appended.

What the server does with it (`GameServerPlaceLists.cs`):

- **Landmarks** — the default `GivenLandmarkSource` (`PlaceListLandmarks`): each `landmark` place, in order, is a given
  landmark at its block column and turn, pinned under record 1000 + its position among the landmark places, stamped first
  in the structures stage, its ground graded ([LANDMARKS.md](LANDMARKS.md) §2, [STRUCTURE_PIPELINE.md](STRUCTURE_PIPELINE.md)
  §4, *the given seat*).
- **Landing pads** — a type with `pad` places has exactly that many pads (`PadCountFor`), at those columns, in list order:
  pad 0, the home touchdown, is the first (`ListedPads`, from `ComputeLandingPadsUncached`). A listed pad is never nudged;
  `DecidePad` decides it on its own ground (dry, wet, an islet) and the pad is pinned in `WorldMetadata.BodyLandingPads` like
  every pad. So a pad must be listed on dry ground (below).
- **Old saves** — both work on a save of any generation by the one rule of §4.

**Earth** (`data/places/sol_earth.json`): ten pads — one at each metropolis centre (London first, the home pad; Paris,
Berlin, Cairo, Tokyo, Sydney, Toronto, New York, Rio de Janeiro) and one in the rain forest about 250 blocks east of the SPS
headquarters site (5.0 S 65.0 W); the nine cities on the same centres with their landmark keys; and the three pyramids of
Giza as landmark places west of Cairo in their real arrangement (great north-east, second south-west of it, third further
south-west). The pyramids are built 1:1 (the great one 231 × 238 blocks), so at this scale they stand hundreds of blocks
from where they really are: each footprint 16 blocks clear of the next and outside a 120-block circle round Cairo's centre
(a city is up to 216 across), on dry desert. **Coastal centres** are listed moved inland, each until a pad's footprint
(radius 16) lies at least 20 m above the sea on the built map; the real coordinates are in the file's comments.
`PlaceListTests.EveryPlaceAndPad_StandsOnLand_OnTheRealMap` holds every pad square and landmark footprint above the sea, and
`EarthPlacesTests` stamps them on a real Earth (and on a generation-30 save) and replays them after a restart.

**Earth's structure list.** `sol_earth` restricts its structures (`restrictStructures`, on every save — a hand-made system's
type, `StructuresRestricted`) to settlements, lodges, SPS labs and guide towers, fallen guardian giants, giant ship wrecks and
net fragments (`allowedStructures`; the whitelist keys `settlements` and `lodges` are new). So no G.D.S. ruin, rune monument,
lost civilization, rail ruin, bandit camp, factory, rolled landmark or city world stands there; the rune shrines (chunk
generation) need `monuments`, and the lone rune stones the new chunk-level key `rune_stones`, which every whitelisted type
before Earth names and Earth does not.
