# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 6: the mushroom cap as a building material.

The cap block of a giant mushroom wore a tile that shows ONE whole little mushroom on brown ground, so a cap built
from many blocks showed a little mushroom on every face. This script paints two tiles in the calm style, by
calculation (no picture model, no cost), and writes them into client/Assets/Resources/textures:

    mushroom_cap                 the cap's skin: red-orange with pale warts of mixed sizes, seamless on all four edges
    face_mushroom_cap_bottom     the underside: pale gills, seamless

Everything is computed on a torus (every distance wraps at the tile's edges), four times oversampled and reduced,
so edges are soft and nothing ends at a tile border.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_mushroom_cap.py [--dry-run]
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

TILE, OVER = 64, 4
N = TILE * OVER
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
TILES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "built"

SKIN = np.array([220.0, 77.0, 26.0])       # the cap's red-orange (the median of the old tile's cap)
SKIN_DEEP = np.array([186.0, 52.0, 18.0])  # in the hollows and round a wart
WART = np.array([240.0, 212.0, 176.0])     # the pale warts (the old tile's spots)
WART_SHADE = np.array([206.0, 168.0, 128.0])
GILL = np.array([228.0, 208.0, 174.0])     # the underside, a little warmer than the stem
GILL_DEEP = np.array([194.0, 168.0, 132.0])


def grid() -> tuple[np.ndarray, np.ndarray]:
    """Pixel centres of the oversampled tile in tile units (0..64)."""
    axis = (np.arange(N) + 0.5) / OVER
    return np.meshgrid(axis, axis)


def wrap(delta: np.ndarray) -> np.ndarray:
    """A distance along one axis on the torus, -32..32."""
    return (delta + TILE / 2) % TILE - TILE / 2


def soft_field(rng: np.random.Generator, waves: int, x: np.ndarray, y: np.ndarray) -> np.ndarray:
    """A smooth seamless field, about -1..1: a few whole-number waves across the tile."""
    out = np.zeros_like(x)
    for _ in range(waves):
        kx, ky = rng.integers(-2, 3), rng.integers(-2, 3)
        if kx == 0 and ky == 0:
            kx = 1
        out += np.cos((2 * np.pi / TILE) * (kx * x + ky * y) + rng.uniform(0, 2 * np.pi)) * rng.uniform(0.5, 1.0)
    return out / waves * 1.6


def smooth(edge0: float, edge1: float, v: np.ndarray) -> np.ndarray:
    t = np.clip((v - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def place_warts(rng: np.random.Generator) -> list[tuple[float, float, float]]:
    """Warts of mixed sizes that keep their distance on the torus: a few large, some medium, some small."""
    warts: list[tuple[float, float, float]] = []
    for radius, count in ((6.4, 3), (4.2, 4), (2.6, 5), (1.5, 6)):
        placed = tries = 0
        while placed < count and tries < 4000:
            tries += 1
            cx, cy = rng.uniform(0, TILE), rng.uniform(0, TILE)
            r = radius * rng.uniform(0.85, 1.15)
            if all(np.hypot(wrap(np.array(cx - ox)), wrap(np.array(cy - oy))) > (r + orr) * 1.25 + 1.5 for ox, oy, orr in warts):
                warts.append((cx, cy, r))
                placed += 1
    return warts


def cap_skin(rng: np.random.Generator) -> np.ndarray:
    x, y = grid()
    relief = soft_field(rng, 5, x, y)
    rgb = SKIN[None, None, :] * (1.0 + 0.055 * relief[..., None])
    rgb = rgb + (SKIN_DEEP - SKIN)[None, None, :] * smooth(0.45, 1.0, -relief)[..., None] * 0.35

    for cx, cy, r in place_warts(rng):
        dx, dy = wrap(x - cx), wrap(y - cy)
        angle = np.arctan2(dy, dx)
        # an irregular outline: the radius swells and shrinks a little round the wart
        lobes = 1.0 + 0.10 * np.cos(2 * angle + rng.uniform(0, 6.3)) + 0.07 * np.cos(3 * angle + rng.uniform(0, 6.3))
        dist = np.hypot(dx, dy) / (r * lobes)
        # the skin sinks a little round the wart (a soft dark rim, stronger below and to the right)
        rim = smooth(1.55, 1.0, dist) * (0.55 + 0.45 * smooth(-1.0, 1.0, (dx + dy) / (r * 1.6)))
        rgb = rgb + (SKIN_DEEP - rgb) * (rim * 0.55)[..., None]
        # the wart itself: pale, lit from the upper left, a touch darker at its lower right edge
        inside = smooth(1.0, 0.86, dist)
        light = smooth(-1.0, 1.0, -(dx + dy) / (r * 1.5))
        wart = WART_SHADE[None, None, :] + (WART - WART_SHADE)[None, None, :] * (0.35 + 0.65 * light)[..., None]
        wart = wart * (1.0 - 0.10 * smooth(0.45, 1.0, dist))[..., None]
        rgb = rgb + (wart - rgb) * inside[..., None]
    return rgb


def gills(rng: np.random.Generator) -> np.ndarray:
    x, y = grid()
    period = 8.0  # eight gills to the block
    # the gills run along y and sway a little; the sway is a whole-number wave, so the tile stays seamless
    sway = 0.9 * np.sin(2 * np.pi * y / TILE + 1.3) + 0.5 * np.sin(4 * np.pi * y / TILE + 4.0)
    phase = (x + sway) / period
    ridge = 0.5 + 0.5 * np.cos(2 * np.pi * phase)       # 1 on a gill's edge, 0 in the gap between two
    depth = smooth(0.0, 0.75, 1.0 - ridge)
    rgb = GILL[None, None, :] + (GILL_DEEP - GILL)[None, None, :] * depth[..., None]
    rgb = rgb * (1.0 + 0.03 * soft_field(rng, 4, x, y))[..., None]
    return rgb


def reduce(rgb: np.ndarray) -> Image.Image:
    small = rgb.reshape(TILE, OVER, TILE, OVER, 3).mean(axis=(1, 3))
    rgba = np.dstack([np.clip(small + 0.5, 0, 255).astype(np.uint8), np.full((TILE, TILE), 255, np.uint8)])
    return Image.fromarray(rgba, "RGBA")


def seam(img: Image.Image) -> float:
    """How visible the tile's own border is: the mean step across the wrap, relative to the mean step inside."""
    a = np.asarray(img.convert("RGB"), dtype=np.float64)
    across = (np.abs(a[:, 0] - a[:, -1]).mean() + np.abs(a[0] - a[-1]).mean()) / 2
    inside = (np.abs(np.diff(a, axis=1)).mean() + np.abs(np.diff(a, axis=0)).mean()) / 2
    return across / max(inside, 1e-6)


def main() -> None:
    dry = "--dry-run" in sys.argv
    made = {
        "mushroom_cap": reduce(cap_skin(np.random.default_rng(1907))),
        "face_mushroom_cap_bottom": reduce(gills(np.random.default_rng(1908))),
    }
    for key, img in made.items():
        OUT.mkdir(parents=True, exist_ok=True)
        img.save(OUT / f"{key}.png")
        print(f"{key}: seam {seam(img):.2f} (1 = no more than inside the tile)")
        if not dry:
            (TILES / f"{key}.bytes").write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
            print("  written to", TILES / f"{key}.bytes")


if __name__ == "__main__":
    main()
