"""Builds the planet maps of the map worlds (#3978): data/maps/<key>.bbmap plus a PNG preview in the cache.

    uv run planet_maps.py earth            # Earth, 2048 x 1024: ETOPO1 elevation + Natural Earth I land cover
    uv run planet_maps.py moon mars        # 1024 x 512: LRO LOLA / MGS MOLA elevation, biome bands from elevation
    uv run planet_maps.py earth --probe    # print the source colour, elevation and class at known places

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

HERE = Path(__file__).resolve().parent
CACHE = HERE / ".cache"
REPO = HERE.parent.parent
DEFAULT_OUT = REPO / "data" / "maps"

ETOPO_URL = (
    "https://coastwatch.pfeg.noaa.gov/erddap/griddap/etopo180.nc"
    "?altitude%5B(-90.0):5:(90.0)%5D%5B(-180.0):5:(180.0)%5D"
)
NATURAL_EARTH_URL = "https://naciscdn.org/naturalearth/10m/raster/NE1_LR_LC.zip"
LOLA_URL = "https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/ldem_4.img"
MOLA_URL = "https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg004/megt90n000cb.img"

# Known places for --probe and the self-check: (name, latitude, longitude, the class Earth should show there).
EARTH_PLACES = [
    ("Sahara", 23.0, 12.0, bbmap.DESERT),
    ("Amazon", -4.0, -62.0, bbmap.RAINFOREST),
    ("Congo", 0.5, 22.0, bbmap.RAINFOREST),
    ("Siberian taiga", 61.0, 100.0, bbmap.BOREAL),
    ("Canadian tundra", 66.0, -100.0, bbmap.TUNDRA),
    ("Kazakh steppe", 49.0, 68.0, bbmap.STEPPE),
    ("Germany", 51.0, 10.0, bbmap.TEMPERATE),
    ("Greenland", 75.0, -40.0, bbmap.ICE),
    ("Antarctica", -80.0, 30.0, bbmap.ICE),
    ("Tibet", 33.0, 88.0, bbmap.HIGH_MOUNTAINS),
    ("Australian outback", -25.0, 133.0, bbmap.DESERT),
    ("Serengeti", -2.5, 35.0, bbmap.STEPPE),
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


def classify_earth(elev: np.ndarray, rgb: np.ndarray) -> np.ndarray:
    """The eight classes from the land-cover colour, the latitude and the elevation (documented in README.md)."""
    height, width = elev.shape
    signed = latitudes(height)[:, None].repeat(width, axis=1)
    lat = np.abs(signed) + wobble(height, width, 5.0)  # the latitude thresholds wander, so no band is a straight line
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    v = rgb.max(axis=2)
    land = elev >= 0.0

    # Natural Earth I is a pastel palette: forests a light green, deserts an ivory tan, steppe and savanna a light
    # yellow-tan, tundra a neutral grey, ice a bluish white. Later rules win.
    greenness = g - np.maximum(r, b)  # forest: green above both other channels
    warmth = r - b                    # arid country: red well over blue
    forest = greenness >= 0.05
    classes = np.full(elev.shape, bbmap.TEMPERATE, dtype=np.uint8)
    arid = (warmth >= 0.10) & ~forest
    classes[arid] = bbmap.STEPPE
    classes[arid & (r - g >= 0.02) & (lat <= 45.0)] = bbmap.DESERT  # tan, not yellow; the high-latitude dry land is steppe
    classes[forest & (lat <= 20.0)] = bbmap.RAINFOREST
    classes[forest & (lat > 20.0) & (lat <= 54.0)] = bbmap.TEMPERATE
    classes[forest & (lat > 54.0)] = bbmap.BOREAL
    classes[~forest & (lat > 58.0)] = bbmap.TUNDRA
    classes[((b - r >= 0.03) & (v >= 0.92)) | (lat > 78.0) | (signed < -60.0)] = bbmap.ICE  # all of Antarctica is ice
    classes[(elev >= 3000.0) & (lat < 60.0)] = bbmap.HIGH_MOUNTAINS  # Tibet, the Andes; not the polar ice caps
    return fill_sea_from_land(classes, land)


def wobble(height: int, width: int, amplitude: float, seed: int = 3978) -> np.ndarray:
    """A smooth, fixed (seeded) noise field in [-amplitude, amplitude], wrapping east-west — a few hundred kilometres
    per wave, so a class border drawn along a latitude follows a natural line instead of a ruler."""
    rng = np.random.default_rng(seed)
    field = ndimage.gaussian_filter(rng.standard_normal((height, width)), sigma=width / 96.0, mode="wrap")
    return field / np.abs(field).max() * amplitude


def fill_sea_from_land(classes: np.ndarray, land: np.ndarray) -> np.ndarray:
    """Every sea cell takes the class of the nearest land cell (wrapping east-west), so a coast's biome border is the
    land's own even where the game's border jitter reaches a few cells out to sea."""
    width = classes.shape[1]
    tiled_land = np.concatenate([land, land, land], axis=1)
    tiled = np.concatenate([classes, classes, classes], axis=1)
    _, (iy, ix) = ndimage.distance_transform_edt(~tiled_land, return_indices=True)
    nearest = tiled[iy, ix]
    return np.where(land, classes, nearest[:, width : 2 * width]).astype(np.uint8)


def build_earth(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = earth_elevation(width, height)
    rgb = earth_land_cover(width, height)
    classes = classify_earth(elev, rgb)
    if probe:
        for name, la, lo, want in EARTH_PLACES:
            row, col = cell_of(la, lo, width, height)
            c = rgb[row, col]
            print(f"{name:20s} rgb=({c[0]:.2f},{c[1]:.2f},{c[2]:.2f}) elev={elev[row, col]:7.0f} "
                  f"class={bbmap.CLASS_NAMES[classes[row, col]]:15s} expected={bbmap.CLASS_NAMES[want]}")
    return elev, classes


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


def build_moon(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = pds_grid(LOLA_URL, "lola_ldem4", "<i2", 0.5, width, height)
    return elev, band_classes(elev)


def build_mars(width: int, height: int, probe: bool) -> tuple[np.ndarray, np.ndarray]:
    elev = pds_grid(MOLA_URL, "mola_megdr4", ">i2", 1.0, width, height)
    return elev, band_classes(elev)


BODIES = {
    "earth": (build_earth, 2048, 1024),
    "moon": (build_moon, 1024, 512),
    "mars": (build_mars, 1024, 512),
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("bodies", nargs="+", choices=sorted(BODIES))
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="where the .bbmap files go (default data/maps)")
    parser.add_argument("--probe", action="store_true", help="print the class at known places (Earth)")
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
        counts = np.bincount(classes.ravel(), minlength=8) / classes.size
        shares = ", ".join(f"{bbmap.CLASS_NAMES[i]} {counts[i]:.1%}" for i in range(8))
        print(f"{key}: {width}x{height}, {len(data) / 1e6:.2f} MB, elevation {elev.min():.0f}..{elev.max():.0f} m; {shares}")
        print(f"  preview: {CACHE / f'{key}_preview.png'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
