"""Builds the planet maps of the map worlds (#3978): data/maps/<key>.bbmap plus a PNG preview in the cache.

    uv run planet_maps.py earth            # Earth, 2048 x 1024: ETOPO1 elevation + Köppen-Geiger climate, Natural Earth I
    uv run planet_maps.py moon mars        # 1024 x 512: LRO LOLA / MGS MOLA elevation, biome bands from elevation
    uv run planet_maps.py venus mercury    # 1024 x 512: Magellan / MESSENGER elevation (USGS mosaics, rows read remotely)
    uv run planet_maps.py earth --probe    # print the climate, elevation and class at the probe places (always checked)

Downloads go to tools/planet_maps/.cache (git-ignored, never committed); only the derived .bbmap is committed. The
sources, licences and attributions are in README.md and in the repository's NOTICES.md.
"""

from __future__ import annotations

import argparse
import sys
import urllib.request
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.io import netcdf_file

import bbmap
import remote_tiff

HERE = Path(__file__).resolve().parent
CACHE = HERE / ".cache"
REPO = HERE.parent.parent
DEFAULT_OUT = REPO / "data" / "maps"

ETOPO_URL = (
    "https://coastwatch.pfeg.noaa.gov/erddap/griddap/etopo180.nc"
    "?altitude%5B(-90.0):5:(90.0)%5D%5B(-180.0):5:(180.0)%5D"
)
NATURAL_EARTH_URL = "https://naciscdn.org/naturalearth/10m/raster/NE1_LR_LC.zip"
KOPPEN_URL = "https://ndownloader.figshare.com/files/12407516"  # Beck_KG_V1.zip, figshare article 6396959
KOPPEN_MEMBER = "Beck_KG_V1_present_0p083.tif"
LOLA_URL = "https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/ldem_4.img"
MOLA_URL = "https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg004/megt90n000cb.img"
VENUS_URL = "https://planetarymaps.usgs.gov/mosaic/Venus_Magellan_Topography_Global_4641m_v02.tif"
MERCURY_URL = "https://planetarymaps.usgs.gov/mosaic/Mercury_Messenger_USGS_DEM_Global_665m_v2.tif"
# The Magellan topography counts from a 6,051.0 km sphere (its area-weighted mean is about +0.8 km); the map counts from
# the mean planetary radius, 6,051.8 km. The MESSENGER DEM already counts from Mercury's 2,439.4 km reference sphere.
VENUS_DATUM_OFFSET_M = -800.0

# Known features for --probe on the airless and hostile bodies: (name, latitude, east longitude).
VENUS_PLACES = [
    ("Maxwell Montes", 65.2, 3.3),
    ("Lakshmi Planum", 68.6, -20.7),
    ("Ovda Regio", -2.8, 85.8),
    ("Diana Chasma", -14.8, 155.5),
    ("Atalanta Planitia", 45.8, 165.8),
]
MERCURY_PLACES = [
    ("Caloris Planitia", 30.5, -170.2),
    ("Rachmaninoff", 27.6, 57.6),
    ("Rembrandt", -33.2, 87.9),
    ("Borealis Planitia", 73.4, -79.5),
]

# Earth's climate groups: the eight map classes plus two groups classify_earth resolves per cell by the land cover —
# the tropical wet-and-dry climate (Aw): rain forest where Natural Earth I shows forest (South-east Asia's monsoon
# forest), savanna elsewhere; and the temperate climates with a dry season (Cs*, Cw*): savanna in open tropical
# country (the Serengeti, the Kenyan and Ethiopian highlands), temperate everywhere else.
TROPICAL_WET_DRY = 8
DRY_SEASON_TEMPERATE = 9
CLIMATE_GROUP_NAMES = [*bbmap.CLASS_NAMES, "tropical wet-dry", "dry-season temperate"]

# The Köppen–Geiger codes of the Beck et al. (2018) legend (1..30; 0 = sea, no group) as climate groups.
NO_GROUP = 255
KOPPEN_GROUPS = np.full(256, NO_GROUP, dtype=np.uint8)
for _codes, _group in [
    ((1, 2), bbmap.RAINFOREST),  # Af, Am
    ((3,), TROPICAL_WET_DRY),  # Aw
    ((4, 5), bbmap.DESERT),  # BWh, BWk
    ((6, 7), bbmap.STEPPE),  # BSh, BSk
    (range(8, 14), DRY_SEASON_TEMPERATE),  # Csa, Csb, Csc, Cwa, Cwb, Cwc
    ((14, 15, 16), bbmap.TEMPERATE),  # Cfa, Cfb, Cfc
    ((17, 18, 21, 22, 25, 26), bbmap.TEMPERATE),  # Dsa, Dsb, Dwa, Dwb, Dfa, Dfb: warm-summer continental
    ((19, 20, 23, 24, 27, 28), bbmap.BOREAL),  # Dsc, Dsd, Dwc, Dwd, Dfc, Dfd: the taiga
    ((29,), bbmap.TUNDRA),  # ET
    ((30,), bbmap.ICE),  # EF
]:
    KOPPEN_GROUPS[list(_codes)] = _group

TROPIC_DEGREES = 23.5  # the dry-season temperate group is savanna in open country within the tropics
FOREST_GREENNESS = 0.05  # Natural Earth I forest: green at least this far above both red and blue
GREENNESS_BLUR_CELLS = 2.0  # the land-cover rules read the greenness smoothed over a few cells, so no border speckles
OPEN_GREENNESS = 0.005  # Natural Earth I open land (tundra grey, dry tan): green at most this far above red and blue
TREELINE_DEGREES = 58.0  # beyond it, taiga climate in open grey country is tundra
ALPINE_DEGREES = 50.0  # within it, the polar climates (ET, EF) are mountain tops: high mountains
HIGH_MOUNTAIN_METRES = 3000.0  # a cell whose mean elevation reaches this is high mountains (outside the polar caps)

# Known places Earth's map must get right: (name, latitude, east longitude, the expected class). Every Earth build checks
# them and fails on a wrong one; --probe prints the whole table.
EARTH_PLACES = [
    # Temperate: western and central Europe, the eastern USA, eastern China, Japan, south-east Australia, southern Brazil.
    ("London", 51.5, -0.1, bbmap.TEMPERATE),
    ("Paris", 48.9, 2.4, bbmap.TEMPERATE),
    ("Berlin", 52.5, 13.4, bbmap.TEMPERATE),
    ("Germany", 51.0, 10.0, bbmap.TEMPERATE),
    ("Warsaw", 52.2, 21.0, bbmap.TEMPERATE),
    ("Rome", 41.9, 12.5, bbmap.TEMPERATE),
    ("New York", 40.7, -74.0, bbmap.TEMPERATE),
    ("Atlanta", 33.7, -84.4, bbmap.TEMPERATE),
    ("Shanghai", 31.2, 121.5, bbmap.TEMPERATE),
    ("Wuhan", 30.6, 114.3, bbmap.TEMPERATE),
    ("Tokyo", 35.7, 139.7, bbmap.TEMPERATE),
    ("Sydney", -33.9, 151.2, bbmap.TEMPERATE),
    ("Melbourne", -37.8, 145.2, bbmap.TEMPERATE),
    ("Porto Alegre", -30.0, -51.2, bbmap.TEMPERATE),
    # Desert.
    ("Sahara", 23.0, 12.0, bbmap.DESERT),
    ("Western Desert (Egypt)", 26.0, 28.0, bbmap.DESERT),
    ("Rub' al Khali", 20.0, 50.0, bbmap.DESERT),
    ("Australian outback", -25.0, 133.0, bbmap.DESERT),
    ("Gobi", 43.0, 105.0, bbmap.DESERT),
    ("Atacama", -23.5, -69.8, bbmap.DESERT),
    ("Kalahari", -25.0, 21.0, bbmap.DESERT),
    # Steppe and savanna.
    ("Kazakh steppe", 49.0, 68.0, bbmap.STEPPE),
    ("Great Plains", 41.0, -103.0, bbmap.STEPPE),
    ("Pampas", -37.0, -65.0, bbmap.STEPPE),
    ("Serengeti", -2.5, 35.0, bbmap.STEPPE),
    ("Sahel (Niamey)", 13.5, 2.1, bbmap.STEPPE),
    ("Northern Australia", -17.0, 133.0, bbmap.STEPPE),
    # Rain forest.
    ("Amazon", -4.0, -62.0, bbmap.RAINFOREST),
    ("Congo", 0.5, 22.0, bbmap.RAINFOREST),
    ("Borneo", 1.0, 114.0, bbmap.RAINFOREST),
    ("Sumatra", 0.0, 102.0, bbmap.RAINFOREST),
    ("Malay Peninsula", 4.5, 102.0, bbmap.RAINFOREST),
    ("New Guinea", -5.0, 142.0, bbmap.RAINFOREST),
    # Boreal forest.
    ("Siberian taiga", 61.0, 100.0, bbmap.BOREAL),
    ("Canadian boreal", 55.0, -98.0, bbmap.BOREAL),
    ("Finland", 64.0, 27.0, bbmap.BOREAL),
    ("Northern Sweden", 65.0, 18.0, bbmap.BOREAL),
    ("Alaska interior", 65.0, -150.0, bbmap.BOREAL),
    # Tundra.
    ("Canadian tundra", 66.0, -100.0, bbmap.TUNDRA),
    ("Taymyr", 73.0, 100.0, bbmap.TUNDRA),
    ("Yamal", 70.0, 70.0, bbmap.TUNDRA),
    ("Alaska North Slope", 70.0, -152.0, bbmap.TUNDRA),
    # Ice.
    ("Greenland", 75.0, -40.0, bbmap.ICE),
    ("Southern Greenland", 67.0, -42.0, bbmap.ICE),
    ("Antarctica", -80.0, 30.0, bbmap.ICE),
    # High mountains.
    ("Tibet", 33.0, 88.0, bbmap.HIGH_MOUNTAINS),
    ("Himalaya", 28.0, 86.0, bbmap.HIGH_MOUNTAINS),
    ("Altiplano", -17.0, -68.5, bbmap.HIGH_MOUNTAINS),
    ("Alps", 46.5, 10.0, bbmap.HIGH_MOUNTAINS),
    ("Rockies", 39.5, -106.0, bbmap.HIGH_MOUNTAINS),
]


def download(url: str, name: str) -> Path:
    """Fetches url into its own folder of the cache once; later runs reuse the file."""
    folder = CACHE / name
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / "download"
    if not target.exists():
        print(f"downloading {url} ...", flush=True)
        request = urllib.request.Request(url, headers={"User-Agent": "planet-maps-tool/1.0"})
        with urllib.request.urlopen(request, timeout=600) as response, open(target.with_suffix(".part"), "wb") as out:
            while chunk := response.read(1 << 20):
                out.write(chunk)
        target.with_suffix(".part").rename(target)
    return target


def overlap_matrix(n_src: int, n_out: int) -> np.ndarray:
    """Area weights (n_out x n_src): how much of each source cell lies in each target cell, rows normalised."""
    m = np.zeros((n_out, n_src), dtype=np.float64)
    scale = n_src / n_out
    for i in range(n_out):
        a, b = i * scale, (i + 1) * scale
        for j in range(int(np.floor(a)), min(int(np.ceil(b)), n_src)):
            m[i, j] = min(b, j + 1) - max(a, j)
        m[i] /= m[i].sum()
    return m


def area_resample(grid: np.ndarray, out_h: int, out_w: int) -> np.ndarray:
    """Box-averages a (rows, cols) grid onto out_h x out_w cells by area (any ratio)."""
    rows = overlap_matrix(grid.shape[0], out_h)
    cols = overlap_matrix(grid.shape[1], out_w)
    return rows @ grid.astype(np.float64) @ cols.T


def latitudes(height: int) -> np.ndarray:
    """The latitude of each row's centre (row 0 = the south edge)."""
    return -90.0 + (np.arange(height) + 0.5) * 180.0 / height


def cell_of(lat: float, lon: float, width: int, height: int) -> tuple[int, int]:
    """(row, col) of a place on a map (column 0 = longitude 0, row 0 = the south edge)."""
    col = int(((lon % 360.0) / 360.0) * width) % width
    row = min(height - 1, int((lat + 90.0) / 180.0 * height))
    return row, col


# --- Earth -------------------------------------------------------------------------------------------------------


def earth_elevation(width: int, height: int) -> np.ndarray:
    """ETOPO1 (ice surface) through ERDDAP at every 5th arc-minute, area-averaged to the map, longitude 0 first."""
    path = download(ETOPO_URL, "etopo1_stride5")
    with netcdf_file(path, "r", mmap=False) as nc:
        alt = np.array(nc.variables["altitude"][:], dtype=np.float64)  # rows from 90 S, cols from 180 W
    alt = alt[: alt.shape[0] - 1, : alt.shape[1] - 1]  # drop the duplicated +90 row and +180 column
    alt = np.roll(alt, -alt.shape[1] // 2, axis=1)  # column 0 = longitude 0
    return area_resample(alt, height, width)


def earth_land_cover(width: int, height: int) -> np.ndarray:
    """Natural Earth I (land cover, no shaded relief) as RGB floats, row 0 = the south edge, column 0 = longitude 0."""
    archive = download(NATURAL_EARTH_URL, "natural_earth_lc")
    tif = archive.parent / "NE1_LR_LC.tif"
    if not tif.exists():
        with zipfile.ZipFile(archive) as z:
            member = next(n for n in z.namelist() if n.lower().endswith(".tif"))
            tif.write_bytes(z.read(member))
    Image.MAX_IMAGE_PIXELS = None
    with Image.open(tif) as img:
        small = img.convert("RGB").resize((width, height), Image.Resampling.BOX)
    rgb = np.asarray(small, dtype=np.float64) / 255.0
    rgb = rgb[::-1]  # the image starts at the north edge
    return np.roll(rgb, -width // 2, axis=1)


def earth_climate(width: int, height: int) -> np.ndarray:
    """The Köppen–Geiger map (Beck et al. 2018, present day, 1/12 degree) as climate-group shares: (groups, height,
    width), each cell's area share of every group (all zero where the source has no land). Row 0 = the south edge,
    column 0 = longitude 0."""
    archive = download(KOPPEN_URL, "beck_kg_v1")
    tif = archive.parent / KOPPEN_MEMBER
    if not tif.exists():
        with zipfile.ZipFile(archive) as z:
            tif.write_bytes(z.read(KOPPEN_MEMBER))
    Image.MAX_IMAGE_PIXELS = None
    with Image.open(tif) as img:
        codes = np.asarray(img, dtype=np.uint8)[::-1]  # the image starts at the north edge, column 0 at 180 W
    groups = KOPPEN_GROUPS[np.roll(codes, -codes.shape[1] // 2, axis=1)]  # column 0 = longitude 0
    rows, cols = overlap_matrix(codes.shape[0], height), overlap_matrix(codes.shape[1], width)
    return np.stack([rows @ (groups == g).astype(np.float64) @ cols.T for g in range(len(CLIMATE_GROUP_NAMES))])


def classify_earth(elev: np.ndarray, rgb: np.ndarray, climate: np.ndarray) -> np.ndarray:
    """The eight classes from the Köppen–Geiger climate shares, refined by the Natural Earth I land cover (tundra,
    savanna, glaciers) and the elevation (high mountains) — documented in README.md."""
    height, width = elev.shape
    signed = latitudes(height)[:, None].repeat(width, axis=1)
    lat = np.abs(signed)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    greenness = ndimage.gaussian_filter(g - np.maximum(r, b), GREENNESS_BLUR_CELLS, mode=("nearest", "wrap"))
    forest = greenness >= FOREST_GREENNESS
    land = elev >= 0.0

    shares = climate[: len(bbmap.CLASS_NAMES)].copy()
    resolve_group(shares, climate[TROPICAL_WET_DRY], forest, bbmap.RAINFOREST, bbmap.STEPPE)
    open_tropics = (lat < TROPIC_DEGREES) & ~forest
    resolve_group(shares, climate[DRY_SEASON_TEMPERATE], open_tropics, bbmap.STEPPE, bbmap.TEMPERATE)
    # A coast cell the climate map counts as sea takes its nearest neighbour's climate.
    classes = fill_from_nearest(shares.argmax(axis=0).astype(np.uint8), shares.sum(axis=0) > 1e-6)

    # The Köppen tundra line (a 10 degC warmest month) runs north of the real tree line in Canada and Alaska; Natural
    # Earth I's open grey there is the barren land.
    classes[(classes == bbmap.BOREAL) & (greenness < OPEN_GREENNESS) & (lat > TREELINE_DEGREES)] = bbmap.TUNDRA
    alpine = np.isin(classes, (bbmap.TUNDRA, bbmap.ICE)) & (lat < ALPINE_DEGREES)  # the Alps' and Andes' tops
    # Natural Earth I paints glaciers a bluish white; beyond 78 degrees and on Antarctica it is ice regardless.
    classes[((b - r >= 0.03) & (rgb.max(axis=2) >= 0.92)) | (lat > 78.0) | (signed < -60.0)] = bbmap.ICE
    classes[alpine | ((elev >= HIGH_MOUNTAIN_METRES) & (lat < 60.0))] = bbmap.HIGH_MOUNTAINS  # not the polar caps
    return fill_from_nearest(classes, land)


def resolve_group(shares: np.ndarray, group: np.ndarray, where: np.ndarray, if_true: int, if_false: int) -> None:
    """Adds a climate group's share to class if_true where the mask holds, else to class if_false."""
    shares[if_true] += np.where(where, group, 0.0)
    shares[if_false] += np.where(where, 0.0, group)


def fill_from_nearest(classes: np.ndarray, known: np.ndarray) -> np.ndarray:
    """Every cell outside known takes the class of the nearest known cell (wrapping east-west). For the land mask this
    gives every sea cell its coast's class, so a coast's biome border is the land's own even where the game's border
    jitter reaches a few cells out to sea."""
    width = classes.shape[1]
    tiled_known = np.concatenate([known, known, known], axis=1)
    tiled = np.concatenate([classes, classes, classes], axis=1)
    _, (iy, ix) = ndimage.distance_transform_edt(~tiled_known, return_indices=True)
    nearest = tiled[iy, ix]
    return np.where(known, classes, nearest[:, width : 2 * width]).astype(np.uint8)


def build_earth(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = earth_elevation(width, height)
    climate = earth_climate(width, height)
    classes = classify_earth(elev, earth_land_cover(width, height), climate)
    check_earth_places(elev, climate, classes, probe)
    return elev, classes


def check_earth_places(elev: np.ndarray, climate: np.ndarray, classes: np.ndarray, probe: bool) -> None:
    """Checks the class at every place of EARTH_PLACES; prints the table with --probe (else only the misses) and stops
    the build when a place shows the wrong class."""
    height, width = elev.shape
    wrong = []
    for name, la, lo, want in EARTH_PLACES:
        row, col = cell_of(la, lo, width, height)
        got = classes[row, col]
        cell = climate[:, row, col]
        source = CLIMATE_GROUP_NAMES[cell.argmax()] if cell.sum() > 1e-6 else "sea"
        line = (f"{name:24s} {la:6.1f} {lo:7.1f} elev={elev[row, col]:6.0f} climate={source:20s} "
                f"class={bbmap.CLASS_NAMES[got]:15s} expected={bbmap.CLASS_NAMES[want]}")
        if got != want:
            wrong.append(line)
        if probe or got != want:
            print(("WRONG " if got != want else "ok    ") + line)
    if wrong:
        raise SystemExit(f"earth: {len(wrong)} of {len(EARTH_PLACES)} probe places show the wrong class")


# --- Airless and hostile bodies: elevation only, classes as elevation bands -----------------------------------------


def pds_grid(url: str, name: str, dtype: str, scale: float, width: int, height: int) -> np.ndarray:
    """A PDS simple-cylindrical 1440 x 720 grid (row 0 = 90 N, column 0 = longitude 0) as metres, south row first."""
    raw = np.frombuffer(download(url, name).read_bytes(), dtype=dtype).reshape(720, 1440)
    metres = raw.astype(np.float64)[::-1] * scale
    return area_resample(metres, height, width)


def band_classes(elev: np.ndarray) -> np.ndarray:
    """Classes for a body without Earth's climate: class 0 at the poles (above 80 degrees), else seven elevation bands of
    equal area (class 1 the lowest ground, class 7 the highest) — an index into the body's own biome list."""
    height, _ = elev.shape
    lat = np.abs(latitudes(height))[:, None].repeat(elev.shape[1], axis=1)
    edges = np.quantile(elev, [i / 7.0 for i in range(1, 7)])
    classes = (1 + np.searchsorted(edges, elev, side="right")).astype(np.uint8)
    classes[lat > 80.0] = 0
    return classes


def usgs_mosaic_grid(url: str, name: str, stride: int, offset_m: float, width: int, height: int) -> np.ndarray:
    """A USGS global elevation mosaic (GeoTIFF) as metres on the map, south row first, column 0 = longitude 0.

    Only every stride-th row is read (over HTTP range requests, the rows cached in .cache/<name>), each standing for the
    stride rows around it; the columns are area-averaged in full. Cells without data are averaged around, and a cell
    with none at all takes its nearest neighbour's value. offset_m moves the datum (metres added to every value)."""
    cache = CACHE / name / f"rows_every_{stride}.npz"
    if cache.exists():
        with np.load(cache) as saved:
            raw, scale, nodata, west = saved["raw"], float(saved["scale"]), int(saved["nodata"]), float(saved["west"])
    else:
        layout = remote_tiff.read_layout(url)
        print(f"reading every {stride}th of {layout.height} rows of {url} ...", flush=True)
        raw = remote_tiff.read_rows(layout, list(range(stride // 2, layout.height, stride)))
        scale, west = layout.scale, layout.west_longitude
        nodata = layout.nodata if layout.nodata is not None else -32768
        cache.parent.mkdir(parents=True, exist_ok=True)
        np.savez(cache, raw=raw, scale=scale, nodata=nodata, west=west)
    valid = raw != nodata
    metres = np.where(valid, raw.astype(np.float64) * scale + offset_m, 0.0)[::-1]
    shift = round(-west / 360.0 * raw.shape[1])  # columns from the west edge to longitude 0
    metres, weight = np.roll(metres, -shift, axis=1), np.roll(valid[::-1].astype(np.float64), -shift, axis=1)
    summed, covered = area_resample(metres, height, width), area_resample(weight, height, width)
    grid = np.divide(summed, covered, out=np.full_like(summed, np.nan), where=covered > 1e-6)
    print(f"  {100.0 * (1.0 - valid.mean()):.2f} % of the read samples had no data, "
          f"{100.0 * np.isnan(grid).mean():.2f} % of the cells none at all")
    return fill_gaps(grid)


def fill_gaps(grid: np.ndarray) -> np.ndarray:
    """Fills the NaN cells smoothly from the data around them (normalised convolution with a growing blur, wrapping
    east-west), so a gap in the source becomes a gentle surface instead of a stripe of copied cells."""
    missing = np.isnan(grid)
    if not missing.any():
        return grid
    known = (~missing).astype(np.float64)
    values = np.where(missing, 0.0, grid)
    filled = grid.copy()
    sigma = 1.0
    while np.isnan(filled).any():
        num = ndimage.gaussian_filter(values, sigma, mode=("nearest", "wrap"))
        den = ndimage.gaussian_filter(known, sigma, mode=("nearest", "wrap"))
        reach = np.isnan(filled) & (den > 1e-3)
        filled[reach] = num[reach] / den[reach]
        sigma *= 1.5
    return filled


def build_moon(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = pds_grid(LOLA_URL, "lola_ldem4", "<i2", 0.5, width, height)
    return elev, band_classes(elev)


def build_mars(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = pds_grid(MOLA_URL, "mola_megdr4", ">i2", 1.0, width, height)
    return elev, band_classes(elev)


def build_venus(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = usgs_mosaic_grid(VENUS_URL, "magellan_topo_4641m", 4, VENUS_DATUM_OFFSET_M, width, height)
    probe_places(elev, VENUS_PLACES, probe)
    return elev, band_classes(elev)


def build_mercury(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = usgs_mosaic_grid(MERCURY_URL, "messenger_dem_665m", 9, 0.0, width, height)
    probe_places(elev, MERCURY_PLACES, probe)
    return elev, band_classes(elev)


def probe_places(elev: np.ndarray, places: list[tuple[str, float, float]], probe: bool) -> None:
    """Prints the elevation at known features next to the body's mean, so a flipped or shifted grid shows at once."""
    if not probe:
        return
    height, width = elev.shape
    print(f"  mean elevation {elev.mean():7.0f} m")
    for name, la, lo in places:
        row, col = cell_of(la, lo, width, height)
        print(f"  {name:20s} lat {la:6.1f} lon {lo:6.1f}: {elev[row, col]:7.0f} m")


BODIES = {
    "earth": (build_earth, 2048, 1024),
    "moon": (build_moon, 1024, 512),
    "mars": (build_mars, 1024, 512),
    "venus": (build_venus, 1024, 512),
    "mercury": (build_mercury, 1024, 512),
}

# --- Preview --------------------------------------------------------------------------------------------------------

CLASS_COLOURS = np.array(
    [
        (235, 240, 245),  # ice
        (150, 150, 120),  # tundra
        (40, 90, 55),  # boreal forest
        (85, 150, 70),  # temperate
        (190, 180, 100),  # steppe / savanna
        (225, 200, 140),  # desert
        (20, 110, 40),  # rain forest
        (130, 110, 100),  # high mountains
    ],
    dtype=np.float64,
)


def preview(elev: np.ndarray, classes: np.ndarray, sea: bool) -> Image.Image:
    """North up: land in its class colour shaded by height, the sea blue by depth (when the body has one)."""
    land_shade = 0.75 + 0.25 * np.clip(elev / 6000.0, 0.0, 1.0)
    rgb = CLASS_COLOURS[classes] * land_shade[..., None]
    if sea:
        depth = np.clip(-elev / 6000.0, 0.0, 1.0)[..., None]
        water = np.array([70, 130, 200]) * (1 - depth) + np.array([15, 40, 90]) * depth
        rgb = np.where((elev < 0)[..., None], water, rgb)
    else:
        lo, hi = np.percentile(elev, [1, 99])
        grey = np.clip((elev - lo) / max(hi - lo, 1.0), 0, 1)[..., None]
        rgb = rgb * (0.55 + 0.45 * grey)
    return Image.fromarray(np.clip(rgb[::-1], 0, 255).astype(np.uint8))


def relief(elev: np.ndarray) -> Image.Image:
    """North up: the elevation alone as a colour ramp (dark low ground to white peaks) under a hill shade lit from the
    north-west — the view to check a body's landmarks in (Maxwell Montes, Caloris, Olympus Mons), class bands aside."""
    lo, hi = np.percentile(elev, [0.5, 99.5])
    t = np.clip((elev - lo) / max(hi - lo, 1.0), 0.0, 1.0)[..., None]
    ramp = np.array([[40, 45, 70], [120, 110, 90], [200, 170, 120], [250, 250, 250]], dtype=np.float64)
    pos = t * (len(ramp) - 1)
    i = np.minimum(pos.astype(int), len(ramp) - 2)
    rgb = ramp[i[..., 0]] * (1 - (pos - i)) + ramp[i[..., 0] + 1] * (pos - i)
    dy, dx = np.gradient(elev)
    shade = np.clip(0.75 + (dx - dy) / max(np.percentile(np.abs(dx) + np.abs(dy), 99), 1.0) * 0.25, 0.4, 1.1)
    return Image.fromarray(np.clip(rgb * shade[..., None], 0, 255)[::-1].astype(np.uint8))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("bodies", nargs="+", choices=sorted(BODIES))
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="where the .bbmap files go (default data/maps)")
    parser.add_argument("--probe", action="store_true", help="print the class (Earth) or the elevation (Venus, Mercury) at known places")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    for key in args.bodies:
        build, width, height = BODIES[key]
        elev, classes = build(width, height, args.probe)
        data = bbmap.encode(elev, classes)
        back_elev, back_classes = bbmap.decode(data)
        assert back_elev.shape == (height, width) and np.array_equal(back_classes, classes)
        (args.out / f"{key}.bbmap").write_bytes(data)
        preview(elev, classes, sea=key == "earth").save(CACHE / f"{key}_preview.png")
        relief(elev).save(CACHE / f"{key}_relief.png")
        counts = np.bincount(classes.ravel(), minlength=8) / classes.size
        shares = ", ".join(f"{bbmap.CLASS_NAMES[i]} {counts[i]:.1%}" for i in range(8))
        print(f"{key}: {width}x{height}, {len(data) / 1e6:.2f} MB, elevation {elev.min():.0f}..{elev.max():.0f} m; {shares}")
        print(f"  preview: {CACHE / f'{key}_preview.png'}, relief: {CACHE / f'{key}_relief.png'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
