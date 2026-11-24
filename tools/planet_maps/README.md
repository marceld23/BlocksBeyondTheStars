# Planet maps (`tools/planet_maps`)

Builds the planet maps of the map worlds (#3978): `data/maps/<key>.bbmap` — elevation in metres and a biome class per
cell — from public-domain source data. The format and how the game samples a map are in
[docs/developer/MAP_PLANETS.md](../../docs/developer/MAP_PLANETS.md).

```bash
cd tools/planet_maps
uv run planet_maps.py earth --probe    # Earth 2048 x 1024 (+ the class table of the 50 probe places)
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
| earth (biome) | Present-day (1980–2016) Köppen–Geiger climate classification map, V1, 0.0833° (`Beck_KG_V1_present_0p083.tif` from `Beck_KG_V1.zip`) | figshare, doi:10.6084/m9.figshare.6396959: `https://ndownloader.figshare.com/files/12407516` | CC BY 4.0 | Beck, H.E., N.E. Zimmermann, T.R. McVicar, N. Vergopolan, A. Berg, E.F. Wood, 2018. Present and future Köppen-Geiger climate classification maps at 1-km resolution. Scientific Data 5:180214, doi:10.1038/sdata.2018.214. Changes: resampled to 2048 × 1024 by area share and regrouped into the game's eight biome classes |
| earth (biome refinement) | Natural Earth I, large size, land cover (`NE1_LR_LC`) | `https://naciscdn.org/naturalearth/10m/raster/NE1_LR_LC.zip` | Public domain | Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com |
| moon | LRO LOLA Gridded Data Record `LDEM_4` (4 px/deg) | `https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/ldem_4.img` | Public domain (NASA PDS) | Smith, D.E. et al., LRO LOLA Gridded Data Records, NASA Planetary Data System, Geosciences Node (LRO-L-LOLA-4-GDR-V1.0) |
| mars | MGS MOLA Mission Experiment Gridded Data Record `MEGT90N000CB` (4 px/deg) | `https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg004/megt90n000cb.img` | Public domain (NASA PDS) | Smith, D.E. et al., MGS MOLA MEGDR, NASA Planetary Data System, Geosciences Node (MGS-M-MOLA-5-MEGDR-L3-V1.0) |
| venus | Magellan Global Topography 4641 m (v2, 1997; from the Global Topographic Data Record) | `https://planetarymaps.usgs.gov/mosaic/Venus_Magellan_Topography_Global_4641m_v02.tif` — only every 4th row is read, over HTTP range requests (about 17 MB of the 67 MB file) | Public domain (NASA / USGS) | Ford, P.G., Pettengill, G.H., Liu, F., Quigley, J. (originators), Venus Magellan Global Topography 4641m, USGS Astrogeology Science Center; Ford, P.G. and G.H. Pettengill, 1992. Venus topography and kilometer-scale slopes. J. Geophys. Res. 97, 13103–13114, doi:10.1029/92JE01085 |
| mercury | MESSENGER Global DEM 665 m (v2, 64 px/deg, `MSGR_DEM_USG_SC_I_V02`) | `https://planetarymaps.usgs.gov/mosaic/Mercury_Messenger_USGS_DEM_Global_665m_v2.tif` — only every 9th row is read, over HTTP range requests (about 59 MB of the 531 MB file) | Public domain (NASA / USGS) | Becker, K.J., Robinson, M.S., Becker, T.L., Weller, L.A., Edmundson, K.L., Neumann, G.A., Perry, M.E., Solomon, S.C., 2016. First Global Digital Elevation Model of Mercury. 47th Lunar and Planetary Science Conference, abstract 2959. USGS Astrogeology Science Center |

## How Earth's biome classes are chosen

The main source is the Köppen–Geiger climate map (Beck et al. 2018). Each of its codes belongs to one climate group;
per map cell the tool measures the area share of every group (the 1/12° source cells box-averaged onto the map) and
the cell takes the group with the largest share:

| Köppen–Geiger | Group |
|---|---|
| Af, Am | rain forest (6) |
| Aw | tropical wet-dry: rain forest (6) where Natural Earth I shows forest (South-east Asia's monsoon forest, the edges of the Congo), else steppe/savanna (4) |
| BWh, BWk | desert (5) |
| BSh, BSk | steppe/savanna (4) |
| Cs\*, Cw\* | dry-season temperate: steppe/savanna (4) within 23.5° of the equator where Natural Earth I shows no forest (the Serengeti, the Kenyan and Ethiopian highlands), else temperate (3) |
| Cf\*, Dsa, Dsb, Dwa, Dwb, Dfa, Dfb | temperate (3) — western, central and eastern Europe, the eastern USA, eastern China, Japan |
| Dsc, Dsd, Dwc, Dwd, Dfc, Dfd | boreal forest (2) |
| ET | tundra (1) |
| EF | ice (0) |

"Forest" in Natural Earth I is green at least 0.05 above both red and blue, read after a 2-cell Gaussian blur so no border
speckles. A cell the climate map counts as sea but ETOPO1 as land takes its nearest neighbour's climate. Then, in order:

| Rule | Why |
|---|---|
| boreal forest beyond 58° where Natural Earth I shows open grey country (green at most 0.005 over red and blue) → tundra | the Köppen tundra line (a warmest month of 10 °C) runs north of the real tree line in Canada and Alaska |
| tundra or ice within 50° of the equator → high mountains | ET and EF there are mountain tops: the Alps, the Caucasus, the Andes |
| Natural Earth I's bluish white, beyond 78°, or south of 60° S → ice | glaciers, the high Arctic, all of Antarctica |
| a cell mean of 3,000 m or more within 60° → high mountains | Tibet, the Himalaya, the Rockies, the Altiplano |

Sea cells take the class of the nearest land cell, so a coast's biome border is the land's own even where the game's
border jitter reaches a few cells out to sea.

**Probe places.** `EARTH_PLACES` in `planet_maps.py` lists 50 named places with the class Earth must show there — London,
Paris, Berlin, Warsaw, Rome, New York, Atlanta, Shanghai, Wuhan, Tokyo, Sydney, Melbourne and Porto Alegre temperate; the
Sahara, Egypt's Western Desert, the Rub' al Khali, the outback, the Gobi, the Atacama and the Kalahari desert; the
Kazakh steppe, the western Great Plains, the dry Pampas, the Serengeti, the Sahel and northern Australia steppe/savanna;
the Amazon, the Congo, Borneo, Sumatra, the Malay Peninsula and New Guinea rain forest; Siberia, Canada, Finland,
northern Sweden and interior Alaska boreal; the Canadian barren lands, Taymyr, Yamal and Alaska's North Slope tundra;
Greenland and Antarctica ice; Tibet, the Himalaya, the Altiplano, the Alps and the Rockies high mountains. Every Earth
build checks them and stops before writing the map when one is wrong; `--probe` prints the whole table (elevation, the
largest climate group, the class).

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
