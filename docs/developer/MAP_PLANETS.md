# Map planets — terrain from a planet map

> Status: **Implemented** (#3977, #3978, 2026-11). The engine and the map format are in place; `data/maps/earth.bbmap`,
> `moon.bbmap` and `mars.bbmap` are built. Which planet types use a map is data (`terrainMap` in `data/planets.json`).

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

## 5. Building the maps (`tools/planet_maps`)

```bash
cd tools/planet_maps
uv run planet_maps.py earth --probe   # data/maps/earth.bbmap + .cache/earth_preview.png
uv run planet_maps.py moon mars       # data/maps/moon.bbmap, mars.bbmap (1024 × 512)
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
- **Venus, Mercury**: not built yet (#3979).
