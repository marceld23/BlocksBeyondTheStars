# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Bake an alpha-cutout mask into the bundled foliage block tiles (B6 — transparent leaves).

The block atlas is already RGBA32 and preserves a tile's alpha, but the foliage tiles ship fully
opaque, so tree crowns + leafy plants render as solid cubes. This punches real, leaf-shaped holes by
deriving an alpha mask from each tile's OWN art: the darkest ~third of pixels (the shadowed gaps
between leaves/petals) become transparent, the lit leaf/flower body stays opaque. RGB is left
untouched so the Sobel normal atlas (derived from RGB luminance) stays clean — only alpha changes.

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
from pathlib import Path

from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = Path("out/textures")

# Foliage = tree crowns + leafy plants. MUST match ChunkMesher.IsFoliageBlock: tree_leaves + every
# flora_* EXCEPT the structural/solid + glowing-cap ones (those read better as solid cubes).
FOLIAGE = [
    "tree_leaves", "pine_needles", "palm_frond",
    "flora_ashweed", "flora_bellflower", "flora_bush", "flora_cinderbush", "flora_coral", "flora_dryshrub",
    "flora_fern", "flora_flower", "flora_frostflower", "flora_grasstuft", "flora_icereed", "flora_kelp",
    "flora_lichen", "flora_lily", "flora_moss", "flora_orchid", "flora_palm", "flora_plant", "flora_reed",
    "flora_rockflower", "flora_saltgrass", "flora_seagrass", "flora_snowbush", "flora_thornbush", "flora_vine",
]


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

    img = Image.frombytes("RGBA", (TILE, TILE), data)  # raw layout preserved (we don't re-flip)
    px = img.load()

    # Compute the mask on a COARSE grid so holes are chunky, connected gaps (visible at a distance + after
    # mip-mapping) instead of scattered single pixels that just average back to opaque. Each coarse cell's
    # leafiness is the mean over its block; the darkest `hole` fraction of cells become fully transparent.
    cell = max(1, TILE // grid)
    cells = []
    for cy in range(0, TILE, cell):
        for cx in range(0, TILE, cell):
            tot, cnt = 0.0, 0
            for y in range(cy, min(cy + cell, TILE)):
                for x in range(cx, min(cx + cell, TILE)):
                    r, g, b, _ = px[x, y]
                    tot += leafiness(r, g, b)
                    cnt += 1
            cells.append((cx, cy, tot / cnt))

    cutoff = sorted(s for _, _, s in cells)[min(len(cells) - 1, int(len(cells) * hole))]

    transparent = 0
    for cx, cy, s in cells:
        a = 0 if s <= cutoff else 255  # binary per coarse cell → crisp chunky leaf gaps
        for y in range(cy, min(cy + cell, TILE)):
            for x in range(cx, min(cx + cell, TILE)):
                r, g, b, _ = px[x, y]
                px[x, y] = (r, g, b, a)  # RGB untouched → normal atlas stays clean
                if a == 0:
                    transparent += 1

    path.write_bytes(img.tobytes())

    OUT.mkdir(parents=True, exist_ok=True)  # also drop a viewable PNG for the record (un-flip for normal viewing)
    img.transpose(Image.FLIP_TOP_BOTTOM).save(OUT / f"{key}.png")

    return f"ok  {key}: {transparent}/{TILE*TILE} px transparent ({100*transparent//(TILE*TILE)}% holes, {grid}x{grid} cells)"


def main() -> None:
    ap = argparse.ArgumentParser(description="Bake alpha-cutout holes into foliage block tiles.")
    ap.add_argument("--hole", type=float, default=0.34, help="target transparent fraction per tile (0..1)")
    ap.add_argument("--grid", type=int, default=16, help="coarse mask resolution (NxN); lower = chunkier holes")
    ap.add_argument("--key", action="append", help="only bake this key (repeatable); default = all foliage")
    args = ap.parse_args()

    keys = args.key if args.key else FOLIAGE
    for k in keys:
        print(bake(k, args.hole, args.grid))


if __name__ == "__main__":
    main()
