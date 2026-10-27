# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Bake an alpha-cutout mask into the bundled foliage block tiles (B6 — transparent leaves).

The block atlas is already RGBA32 and preserves a tile's alpha, but the foliage tiles ship fully
opaque, so tree crowns + leafy plants render as solid cubes. This punches real, leaf-shaped holes by
deriving an alpha mask from each tile's OWN art. Three kinds of tile, three cuts:

- dense foliage (leaves edge to edge): the darkest ~third of coarse cells (the shadowed gaps between
  leaves/petals) become transparent, the lit leaf/flower body stays opaque;
- one object on a pure black ground (DARK_GROUND — fruit, berries and the side-view flora billboards):
  every (nearly) black pixel is cut;
- one object on a flat, lighter ground (GROUND_KEY): every pixel close to that ground colour is cut.
  The darkness cut is exactly wrong here — the plant is the darkest thing in the tile, so it cut the
  plant and kept the ground card (#2772).

RGB is left untouched so the Sobel normal atlas (derived from RGB luminance) stays clean — only alpha
changes. The one exception is ROOTED: a billboard that stands on the ground is moved down onto the
tile's bottom edge, because the billboard roots at the tile's bottom row (#2773).

The tiles live as raw 64x64 RGBA32 bytes (bottom-up flipped) at
client/Assets/Resources/textures/<key>.bytes; we read, set alpha, write back in place (surgical — no
other tile is touched, no re-bundle of the whole set). The shader clips on this alpha for leaf-flagged
faces (see BlockAtlas.shader / ChunkMesher.IsFoliageBlock — keep the foliage set in sync with this).

Usage:
    uv run python bake_leaf_alpha.py            # bake all foliage tiles
    uv run python bake_leaf_alpha.py --hole 0.30 --key tree_leaves
"""
from __future__ import annotations

import argparse
import json
import math
from collections import deque
from pathlib import Path

import texture_provenance
from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
# The viewable PNG of each baked tile. NOT out/textures: that is where gen_textures.py leaves the generated images that
# bundle_textures.py --from-out reads, and a bake run used to overwrite them with previews of the OLD bundled tiles.
OUT = Path("out/baked")

# Foliage = tree crowns + leafy plants. MUST match ChunkMesher.IsFoliageBlock: tree_leaves + every
# flora_* EXCEPT the structural/solid + glowing-cap ones (those read better as solid cubes).
FOLIAGE = [
    "tree_leaves", "pine_needles", "palm_frond", "giant_leaves",
    "tree_leaves_b", "tree_leaves_c", "blossom_leaves",  # the tree variety (#2851, #2853): two more leaf slots + the blossom crown
    "flora_ashweed", "flora_bellflower", "flora_bush", "flora_cinderbush", "flora_coral", "flora_cropberry",
    "flora_cropgrain", "flora_dryshrub",
    "flora_fern", "flora_flower", "flora_frostflower", "flora_grasstuft", "flora_icereed", "flora_kelp",
    "flora_lichen", "flora_lily", "flora_moss", "flora_orchid", "flora_palm", "flora_plant", "flora_reed",
    "flora_rockflower", "flora_saltgrass", "flora_seagrass", "flora_snowbush", "flora_thornbush", "flora_vine",
    "flora_sunblossom", "flora_tulip", "flora_hangkelp",
    "flora_glowmoss", "flora_glowthread",  # generation 11 cave flora (code-painted cutouts until their tiles exist)
    "flora_fruit_round", "flora_fruit_long", "flora_fruit_grape", "flora_fruit_banana",  # fruit trees (#2038): a pale fruit on black
    "flora_lantern_fruit",  # the lantern tree's glowing fruit (#2853), a fruit on black like the others
    "fifi_leaf", "flora_fifi_berries",  # the Fifi plant (#2085): its yellow crown + its berries (a berry cluster on black)
    # the mixed meadows (#2833, generation 28): twelve side-view plants on black, rooted on the bottom edge
    "flora_berrybush", "flora_blossombush", "flora_featherbush", "flora_droopbush", "flora_needlebush", "flora_frostberry",
    "flora_clover", "flora_daisy", "flora_thistle", "flora_herb", "flora_foxtail", "flora_lupine",
    # the sapling kinds (#2977–#2980): a young tree on black, rooted on the bottom edge
    "flora_sapling_conifer", "flora_sapling_fruit_round", "flora_sapling_fruit_long", "flora_sapling_fruit_grape", "flora_sapling_fruit_banana", "flora_sapling_fifi", "flora_sapling_mahogany",
    # flora_sapling (#1774) is NOT here: its tile ships with a chroma-keyed alpha (the plant on a plain tan ground),
    # so the darkness cut would remove the stem, not the background.
]


# Tiles painted as ONE object on a pure black ground (the fruit of #2038): a fixed fraction would either leave
# black around the object or eat its edge, so these cut every pixel that is (nearly) black instead — per pixel, not per
# coarse cell, because a fruit needs a clean outline, not chunky leaf gaps. #2773: the flora billboards that used to be
# top-down "fields of plants on a ground" are one side-view plant on black now (gen_textures.py ON_BLACK).
DARK_GROUND = {
    "flora_fruit_round", "flora_fruit_long", "flora_fruit_grape", "flora_fruit_banana", "flora_fifi_berries",
    "flora_lantern_fruit",
    "flora_coral", "flora_lily", "flora_orchid", "flora_palm", "flora_rockflower", "flora_saltgrass", "flora_seagrass",
    "flora_snowbush",
    # the mixed meadows (#2833)
    "flora_berrybush", "flora_blossombush", "flora_featherbush", "flora_droopbush", "flora_needlebush", "flora_frostberry",
    "flora_clover", "flora_daisy", "flora_thistle", "flora_herb", "flora_foxtail", "flora_lupine",
    # the sapling kinds (#2977–#2980): a young tree on black, rooted on the bottom edge
    "flora_sapling_conifer", "flora_sapling_fruit_round", "flora_sapling_fruit_long", "flora_sapling_fruit_grape", "flora_sapling_fruit_banana", "flora_sapling_fifi", "flora_sapling_mahogany",
}
DARK_GROUND_CUTOFF = 0.12  # brightness at or below this = the black ground (a dark stem sits well above it)

# #2772: tiles painted as ONE object on a flat, LIGHTER ground. The darkness cut kept the ground and cut the plant (the
# plant is the darkest thing in the tile — the dry shrub stood in the world as a sand card with its own silhouette
# punched out). These cut every pixel within the tolerance of the ground colour instead, per pixel, like the lantern's
# key in bake_prop_alpha.py. The colours are fixed (sampled from the tiles' borders) so a second run keys the same.
GROUND_KEY = {
    "flora_dryshrub": ((226, 179, 104), 45.0),  # a brown shrub on tan sand
    "flora_bush": ((152, 124, 85), 45.0),  # a round berry bush on brown soil
}

# Tiles baked with their own --hole/--grid (#2013: the cave flora on black — moss in 32x32 cells, threads per pixel, each
# at its black background's share). A run with the defaults re-punched them with the dense-leaf cut; these are the
# values that reproduce the bundled masks exactly.
BAKE_ARGS = {
    "flora_glowmoss": (0.78, 32),
    "flora_glowthread": (0.9, 64),
}

# An opaque island smaller than this (8-connected pixels) is a fleck of ground texture the key or the black cut left
# standing beside the plant, not part of it: it would float in the air next to the billboard. Rooted tiles only.
SPECK_PIXELS = 4

# #2773: billboards that stand on the ground. A cross billboard roots at its tile's bottom row (ChunkMesher.AddCrossPlant),
# and the generated art leaves a margin under the plant — the plant floated above the ground by that margin. These are
# moved down until the plant touches the bottom edge (the freed rows on top take the ground colour, so they stay cut).
# Hanging tiles (the fruit, the berries) root at the TOP edge and are not listed.
ROOTED = {
    "flora_bush", "flora_coral", "flora_dryshrub", "flora_lily", "flora_orchid", "flora_palm", "flora_rockflower",
    "flora_saltgrass", "flora_seagrass", "flora_snowbush",
    # the mixed meadows (#2833)
    "flora_berrybush", "flora_blossombush", "flora_featherbush", "flora_droopbush", "flora_needlebush", "flora_frostberry",
    "flora_clover", "flora_daisy", "flora_thistle", "flora_herb", "flora_foxtail", "flora_lupine",
    # the sapling kinds (#2977–#2980): a young tree on black, rooted on the bottom edge
    "flora_sapling_conifer", "flora_sapling_fruit_round", "flora_sapling_fruit_long", "flora_sapling_fruit_grape", "flora_sapling_fruit_banana", "flora_sapling_fifi", "flora_sapling_mahogany",
}


def _flora_looks() -> tuple[list[str], list[str]]:
    """#2832: the look tiles named in data/blocks.json ("looks"), split into crown looks (dense canopies, the darkness
    cut like their block) and billboard looks (one side-view plant on black: the dark-ground cut, rooted)."""

    # The content loader reads blocks.json with // comment lines allowed; drop them for the plain JSON parser.
    text = (REPO / "data" / "blocks.json").read_text(encoding="utf-8")
    plain = "\n".join(line for line in text.split("\n") if not line.lstrip().startswith("//"))
    crowns, billboards = [], []
    for block in json.loads(plain):
        if block["key"] in OPAQUE_LOOK_KEYS:
            continue  # bark looks (#2855) are opaque block tiles — never cut
        for look in block.get("looks") or []:
            target = crowns if block["key"] in CROWN_KEYS else billboards
            if look not in target:  # the leaf slots share one pool (#2851): each tile is baked once
                target.append(look)
    return crowns, billboards


CROWN_KEYS = {"tree_leaves", "pine_needles", "palm_frond", "giant_leaves", "tree_leaves_b", "tree_leaves_c", "blossom_leaves"}
# Blocks whose looks are opaque tiles (the bark of #2855): their looks are not foliage at all.
OPAQUE_LOOK_KEYS = {"wood_log", "wood_log_b"}
CROWN_LOOKS, BILLBOARD_LOOKS = _flora_looks()
FOLIAGE += CROWN_LOOKS + BILLBOARD_LOOKS
DARK_GROUND |= set(BILLBOARD_LOOKS)
ROOTED |= set(BILLBOARD_LOOKS)


def leafiness(r: int, g: int, b: int) -> float:
    """How 'leaf/flower body' a pixel is (high) vs a shadowed gap (low): bright + colourful = body."""
    lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    mx, mn = max(r, g, b) / 255.0, min(r, g, b) / 255.0
    sat = 0.0 if mx <= 1e-4 else (mx - mn) / mx
    return lum * 0.75 + sat * 0.25


def bake(key: str, hole: float, grid: int) -> str:
    path = RES / f"{key}.bytes"
    if not path.exists():
        return f"--- {key}: no bundled tile, skipped"

    data = path.read_bytes()
    if len(data) != TILE * TILE * 4:
        return f"!!! {key}: unexpected size {len(data)} (want {TILE*TILE*4}), skipped"

    img = Image.frombytes("RGBA", (TILE, TILE), data)  # raw layout preserved (we don't re-flip): row 0 is the BOTTOM row
    px = img.load()

    if key in GROUND_KEY:
        ground, tolerance = GROUND_KEY[key]
        clear = [[key_distance(px[x, y], ground) < tolerance for x in range(TILE)] for y in range(TILE)]
        mode = f"ground key {ground}"
    else:
        clear = darkness_mask(px, key in DARK_GROUND, hole, grid)
        ground = (0, 0, 0)
        mode = "black ground" if key in DARK_GROUND else f"{grid}x{grid} cells"

    lowered = 0
    if key in ROOTED:
        drop_specks(clear)
        lowered = root_on_bottom(px, clear, ground)

    transparent = 0
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, _ = px[x, y]
            a = 0 if clear[y][x] else 255  # binary → crisp cutout edges
            px[x, y] = (r, g, b, a)  # RGB untouched → normal atlas stays clean
            if a == 0:
                transparent += 1

    path.write_bytes(img.tobytes())

    OUT.mkdir(parents=True, exist_ok=True)  # also drop a viewable PNG for the record (un-flip for normal viewing)
    img.transpose(Image.FLIP_TOP_BOTTOM).save(OUT / f"{key}.png")

    moved = f", lowered {lowered} rows onto the bottom edge" if lowered else ""
    return f"ok  {key}: {transparent}/{TILE*TILE} px transparent ({100*transparent//(TILE*TILE)}% holes, {mode}{moved})"


def darkness_mask(px, dark_ground: bool, hole: float, grid: int) -> list[list[bool]]:
    """True = transparent. Dense foliage: the darkest `hole` fraction of coarse cells. A black ground: every pixel at or
    below DARK_GROUND_CUTOFF brightness."""
    # Compute the mask on a COARSE grid so holes are chunky, connected gaps (visible at a distance + after
    # mip-mapping) instead of scattered single pixels that just average back to opaque. Each coarse cell's
    # leafiness is the mean over its block; the darkest `hole` fraction of cells become fully transparent.
    if dark_ground:
        grid = TILE  # per pixel

    cell = max(1, TILE // grid)
    cells = []
    for cy in range(0, TILE, cell):
        for cx in range(0, TILE, cell):
            tot, cnt = 0.0, 0
            for y in range(cy, min(cy + cell, TILE)):
                for x in range(cx, min(cx + cell, TILE)):
                    r, g, b, _ = px[x, y]
                    # the black ground is cut by brightness alone: near-black pixels carry noisy saturation
                    tot += (0.299 * r + 0.587 * g + 0.114 * b) / 255.0 if dark_ground else leafiness(r, g, b)
                    cnt += 1
            cells.append((cx, cy, tot / cnt))

    cutoff = DARK_GROUND_CUTOFF if dark_ground else sorted(s for _, _, s in cells)[min(len(cells) - 1, int(len(cells) * hole))]

    clear = [[False] * TILE for _ in range(TILE)]
    for cx, cy, s in cells:
        if s <= cutoff:  # binary per coarse cell → crisp chunky leaf gaps
            for y in range(cy, min(cy + cell, TILE)):
                for x in range(cx, min(cx + cell, TILE)):
                    clear[y][x] = True
    return clear


def key_distance(p: tuple[int, ...], key: tuple[int, int, int]) -> float:
    return math.sqrt(sum((p[i] - key[i]) ** 2 for i in range(3)))


def drop_specks(clear: list[list[bool]]) -> None:
    """Cuts every opaque island smaller than SPECK_PIXELS (8-connected) — flecks of ground beside the plant."""
    seen = [[False] * TILE for _ in range(TILE)]
    for sy in range(TILE):
        for sx in range(TILE):
            if clear[sy][sx] or seen[sy][sx]:
                continue
            island, todo = [], deque([(sx, sy)])
            seen[sy][sx] = True
            while todo:
                x, y = todo.popleft()
                island.append((x, y))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < TILE and 0 <= ny < TILE and not clear[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            todo.append((nx, ny))
            if len(island) < SPECK_PIXELS:
                for x, y in island:
                    clear[y][x] = True


def root_on_bottom(px, clear: list[list[bool]], ground: tuple[int, int, int]) -> int:
    """Moves the art down until its lowest opaque row is the tile's bottom row (raw row 0) and returns the rows moved.
    The freed rows at the top are filled with the ground colour and cut, so a second run finds nothing to move."""
    lowest = next((y for y in range(TILE) if not all(clear[y])), 0)
    if lowest == 0:
        return 0
    for y in range(TILE):
        for x in range(TILE):
            src = y + lowest
            if src < TILE:
                px[x, y] = px[x, src]
                clear[y][x] = clear[src][x]
            else:
                px[x, y] = (*ground, 0)
                clear[y][x] = True
    return lowest


def main() -> None:
    ap = argparse.ArgumentParser(description="Bake alpha-cutout holes into foliage block tiles.")
    ap.add_argument("--hole", type=float, default=0.34, help="target transparent fraction per tile (0..1)")
    ap.add_argument("--grid", type=int, default=16, help="coarse mask resolution (NxN); lower = chunkier holes")
    ap.add_argument("--key", action="append", help="only bake this key (repeatable); default = all foliage")
    ap.add_argument("--force", action="store_true", help="also re-punch hand-painted tiles (texture_provenance.json)")
    args = ap.parse_args()

    # A tile somebody painted by hand carries its OWN alpha — punching the darkness mask over it would eat the
    # painting (#1953). Those are skipped unless --force.
    provenance = texture_provenance.load()
    keys = args.key if args.key else FOLIAGE
    for k in keys:
        if texture_provenance.guard(k, args.force, provenance):
            hole, grid = BAKE_ARGS.get(k, (args.hole, args.grid))
            print(bake(k, hole, grid))


if __name__ == "__main__":
    main()
