# Planet maps (`tools/planet_maps`)

Builds the planet maps of the map worlds (#3978): `data/maps/<key>.bbmap` — elevation in metres and a biome class per
cell — from public-domain source data. The format and how the game samples a map are in
[docs/developer/MAP_PLANETS.md](../../docs/developer/MAP_PLANETS.md).

```bash
cd tools/planet_maps
uv run planet_maps.py earth --probe    # Earth 2048 x 1024 (+ the class at a dozen known places)
uv run planet_maps.py moon mars venus mercury   # 1024 x 512 each (add --probe for the height at known features)
uv run planet_maps.py earth --out /tmp/maps   # write elsewhere (default: data/maps)
```

Every download goes to `.cache/` (git-ignored) and is reused by later runs; the preview of each map is written there as
`<key>_preview.png` (north up, the class colours) and `<key>_relief.png` (north up, the elevation alone under a hill
shade — the view to check a body's landmarks in). Only the derived `.bbmap` is committed — never the raw data. A
committed map is frozen once a release ships it (every world built from it would change): rebuild only under a new key.

## Sources and licences

| Map | Dataset | Source | Licence | Attribution |
|---|---|---|---|---|
| earth (elevation) | ETOPO1 1 Arc-Minute Global Relief Model, ice surface | NOAA CoastWatch ERDDAP `etopo180`, read at every 5th arc-minute: `https://coastwatch.pfeg.noaa.gov/erddap/griddap/etopo180.nc?altitude[(-90.0):5:(90.0)][(-180.0):5:(180.0)]` | Public domain (U.S. Government work, NOAA) | Amante, C. and B.W. Eakins, 2009. ETOPO1 1 Arc-Minute Global Relief Model: Procedures, Data Sources and Analysis. NOAA Technical Memorandum NESDIS NGDC-24. National Geophysical Data Center, NOAA. doi:10.7289/V5C8276M |
| earth (biome) | Natural Earth I, large size, land cover (`NE1_LR_LC`) | `https://naciscdn.org/naturalearth/10m/raster/NE1_LR_LC.zip` | Public domain | Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com |
| moon | LRO LOLA Gridded Data Record `LDEM_4` (4 px/deg) | `https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/ldem_4.img` | Public domain (NASA PDS) | Smith, D.E. et al., LRO LOLA Gridded Data Records, NASA Planetary Data System, Geosciences Node (LRO-L-LOLA-4-GDR-V1.0) |
| mars | MGS MOLA Mission Experiment Gridded Data Record `MEGT90N000CB` (4 px/deg) | `https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg004/megt90n000cb.img` | Public domain (NASA PDS) | Smith, D.E. et al., MGS MOLA MEGDR, NASA Planetary Data System, Geosciences Node (MGS-M-MOLA-5-MEGDR-L3-V1.0) |
| venus | Magellan Global Topography 4641 m (v2, 1997; from the Global Topographic Data Record) | `https://planetarymaps.usgs.gov/mosaic/Venus_Magellan_Topography_Global_4641m_v02.tif` — only every 4th row is read, over HTTP range requests (about 17 MB of the 67 MB file) | Public domain (NASA / USGS) | Ford, P.G., Pettengill, G.H., Liu, F., Quigley, J. (originators), Venus Magellan Global Topography 4641m, USGS Astrogeology Science Center; Ford, P.G. and G.H. Pettengill, 1992. Venus topography and kilometer-scale slopes. J. Geophys. Res. 97, 13103–13114, doi:10.1029/92JE01085 |
| mercury | MESSENGER Global DEM 665 m (v2, 64 px/deg, `MSGR_DEM_USG_SC_I_V02`) | `https://planetarymaps.usgs.gov/mosaic/Mercury_Messenger_USGS_DEM_Global_665m_v2.tif` — only every 9th row is read, over HTTP range requests (about 59 MB of the 531 MB file) | Public domain (NASA / USGS) | Becker, K.J., Robinson, M.S., Becker, T.L., Weller, L.A., Edmundson, K.L., Neumann, G.A., Perry, M.E., Solomon, S.C., 2016. First Global Digital Elevation Model of Mercury. 47th Lunar and Planetary Science Conference, abstract 2959. USGS Astrogeology Science Center |

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

The Moon, Mars, Venus and Mercury have no climate: class 0 above 80° latitude, else seven equal-area elevation bands
(1 the lowest ground, 7 the highest) — indices into the body's own biome list.

### Venus and Mercury (#3979)

The two USGS mosaics are large uncompressed GeoTIFFs (one row per strip), so `remote_tiff.py` reads the header, checks
the layout and fetches only every n-th row (each standing for the n rows around it); the columns are area-averaged in
full. The rows read are kept in `.cache/<name>/rows_every_<n>.npz`. Venus' Magellan altimetry has gaps (about 8 % of the
samples): a cell with some data averages what it has, a cell with none is filled smoothly from its surroundings
(normalised convolution, `fill_gaps`). Datums: Venus counts from the mean planetary radius, 6,051.8 km (the product counts
from 6,051.0 km, so 800 m are subtracted); Mercury from the DEM's own 2,439.4 km sphere.

| Map | Built range | Known features (`--probe`) |
|---|---|---|
| venus | −2,750 m … 10,243 m | Maxwell Montes 9,735 m, Lakshmi Planum 3,817 m, Ovda Regio 3,762 m, Atalanta Planitia −71 m |
| mercury | −5,338 m … 4,169 m | Rachmaninoff 1,392 m, Caloris Planitia −191 m (a smooth plain ringed by rough rim country), Borealis Planitia −1,922 m |
