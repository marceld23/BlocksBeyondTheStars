# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Mars's two ground tiles (#4018): red_sand from the calm sand tile, red_rock from the natural stone tile. No model.

Each tile keeps the form of its source pixel by pixel — the same grains, cracks and seams, so it wraps exactly as the source
does and stays on its grid — and takes a new colour: every pixel is the target colour scaled by the pixel's brightness
relative to the source tile's mean brightness (contrast factor k: 1 keeps the source's relative contrast). The target colours
are the blocks' data colours in data/blocks.json (the far terrain and the map read those), so the tile mean and the data
colour agree.

The sources are read from the git revision that holds them (SOURCE_REV), never from the working tree, so the script gives
the same tiles however often it runs:

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_mars_tiles.py [--bundle]

Without --bundle it only writes the approval sheet (out/calm_tiles/sheets/mars_tiles.png: source | new | the new tile laid
3 x 3); with it, also client/Assets/Resources/textures/<key>.bytes (raw 64 x 64 RGBA32, rows bottom-up).
"""
from __future__ import annotations

import argparse
import subprocess

import numpy as np
from PIL import Image

from tilelib import REPO, SHEETS, TEXTURES, TILE, luma, repeat, scale, sheet

SOURCE_REV = "3b8abf26"  # the sources as of #4013: the calm sand tile and the round-6 natural stone tile (#4000)

# key -> (source tile, target colour (the block's data colour), contrast factor)
TILES: dict[str, tuple[str, tuple[int, int, int], float]] = {
    # The regolith: the calm sand's soft grain, a little more contrast so the darker red still shows it.
    "red_sand": ("sand", (0xC8, 0x60, 0x36), 1.6),
    # The red rock: the natural stone's mottled materials and cracks in one rust red.
    "red_rock": ("stone", (0x96, 0x3E, 0x28), 1.0),
}


def source(key: str) -> np.ndarray:
    """The source tile at SOURCE_REV as a top-down RGBA array (0..255)."""
    rel = f"client/Assets/Resources/textures/{key}.bytes"
    raw = subprocess.run(["git", "show", f"{SOURCE_REV}:{rel}"], cwd=REPO, check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.uint8).reshape(TILE, TILE, 4)[::-1].astype(np.float64)


def recolour(src: np.ndarray, target: tuple[int, int, int], k: float) -> np.ndarray:
    """The target colour scaled per pixel by its brightness relative to the tile's mean (contrast k); alpha kept."""
    lum = luma(src[..., :3])
    rel = 1.0 + (k * ((lum / lum.mean()) - 1.0))
    out = np.empty_like(src)
    out[..., :3] = np.clip(np.asarray(target, dtype=np.float64)[None, None, :] * rel[..., None], 0.0, 255.0)
    out[..., 3] = src[..., 3]
    return np.rint(out).astype(np.uint8)


def to_image(arr: np.ndarray) -> Image.Image:
    return Image.fromarray(np.asarray(arr, dtype=np.uint8), "RGBA").convert("RGB")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--bundle", action="store_true", help="write the tiles into client/Assets/Resources/textures")
    args = ap.parse_args()

    rows = []
    for key, (src_key, target, k) in TILES.items():
        src = source(src_key)
        new = recolour(src, target, k)
        mean = new[..., :3].reshape(-1, 3).mean(axis=0)
        rel_std = luma(new[..., :3].astype(np.float64)).std() / luma(new[..., :3].astype(np.float64)).mean()
        print(f"{key}: from {src_key}, mean {np.round(mean, 1)} (target {target}), relative contrast {rel_std:.3f}")
        if args.bundle:
            (TEXTURES / f"{key}.bytes").write_bytes(np.ascontiguousarray(new[::-1]).tobytes())
            print(f"  -> {TEXTURES / (key + '.bytes')}")
        rows.append((key, [scale(to_image(src), 4), scale(to_image(new), 4), repeat(to_image(new), 3)]))

    sheet(rows, ["source", "new", "new, 3 x 3"], SHEETS / "mars_tiles.png", title="Mars ground tiles (#4018)")


if __name__ == "__main__":
    main()
