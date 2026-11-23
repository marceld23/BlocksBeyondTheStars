# Planet maps (`tools/planet_maps`)

Builds the planet maps of the map worlds (#3978): `data/maps/<key>.bbmap` — elevation in metres and a biome class per
cell — from public-domain source data. The format and how the game samples a map are in
[docs/developer/MAP_PLANETS.md](../../docs/developer/MAP_PLANETS.md).

```bash
cd tools/planet_maps
uv run planet_maps.py earth --probe    # Earth 2048 x 1024 (+ the class at a dozen known places)
uv run planet_maps.py moon mars        # 1024 x 512 each
uv run planet_maps.py earth --out /tmp/maps   # write elsewhere (default: data/maps)
```

Every download goes to `.cache/` (git-ignored) and is reused by later runs; the preview of each map is written there as
`<key>_preview.png` (north up). Only the derived `.bbmap` is committed — never the raw data. A committed map is frozen
once a release ships it (every world built from it would change): rebuild only under a new key.

## Sources and licences

| Map | Dataset | Source | Licence | Attribution |
|---|---|---|---|---|
| earth (elevation) | ETOPO1 1 Arc-Minute Global Relief Model, ice surface | NOAA CoastWatch ERDDAP `etopo180`, read at every 5th arc-minute: `https://coastwatch.pfeg.noaa.gov/erddap/griddap/etopo180.nc?altitude[(-90.0):5:(90.0)][(-180.0):5:(180.0)]` | Public domain (U.S. Government work, NOAA) | Amante, C. and B.W. Eakins, 2009. ETOPO1 1 Arc-Minute Global Relief Model: Procedures, Data Sources and Analysis. NOAA Technical Memorandum NESDIS NGDC-24. National Geophysical Data Center, NOAA. doi:10.7289/V5C8276M |
| earth (biome) | Natural Earth I, large size, land cover (`NE1_LR_LC`) | `https://naciscdn.org/naturalearth/10m/raster/NE1_LR_LC.zip` | Public domain | Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com |
| moon | LRO LOLA Gridded Data Record `LDEM_4` (4 px/deg) | `https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/ldem_4.img` | Public domain (NASA PDS) | Smith, D.E. et al., LRO LOLA Gridded Data Records, NASA Planetary Data System, Geosciences Node (LRO-L-LOLA-4-GDR-V1.0) |
| mars | MGS MOLA Mission Experiment Gridded Data Record `MEGT90N000CB` (4 px/deg) | `https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg004/megt90n000cb.img` | Public domain (NASA PDS) | Smith, D.E. et al., MGS MOLA MEGDR, NASA Planetary Data System, Geosciences Node (MGS-M-MOLA-5-MEGDR-L3-V1.0) |

Venus and Mercury are not built yet (#3979).

## How Earth's biome classes are chosen

Natural Earth I colours the land by its natural environment (humid greens, arid browns, polar white). Per cell, after
the image is box-averaged to the map:

| Class | Rule (first match from the bottom wins) |
|---|---|
| ice (0) | very bright and grey (white), or beyond 78° latitude |
| high mountains (7) | the cell's mean elevation is 3,000 m or more |
| tundra (1) | beyond 58° latitude and not forest |
| boreal forest (2) | dark green beyond 50° |
| temperate (3) | dark green between 20° and 50°, or a lighter green anywhere |
| rain forest (6) | dark green within 20° of the equator |
| desert (5) | bright arid tan (red over green, red well over blue) |
| steppe / savanna (4) | every other dry colour (not green) |

Sea cells take the class of the nearest land cell, so a coast's biome border is the land's own even where the game's
border jitter reaches a few cells out to sea. `--probe` prints the colour, elevation and class at a dozen known places
(the Sahara, the Amazon, the taiga, the tundra, the steppe, Germany, Greenland, Antarctica, Tibet, the outback, the
Serengeti) next to the class Earth should show there.

The Moon and Mars have no climate: class 0 above 80° latitude, else seven equal-area elevation bands (1 the lowest ground,
7 the highest) — indices into the body's own biome list.
