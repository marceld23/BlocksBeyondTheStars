# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 8: seam repair of the calm tiles of materials that objects are BUILT from.

A seam check of every opaque calm tile found natural building materials whose calm tile shows its own border on every
block of a stem, a cactus or the school club's giant burger. Repaired here, each by what is wrong with it:

    fifi_stem, saguaro, paul_stem   a vertical-stripe material drifts in brightness down the tile, so stacked blocks
                                    show a line: every row gets the same mean, the rest of the jump goes into a smooth
                                    ramp (Moisan's periodic part)
    burger_bun                      shaded like one cushion per block: the per-tile shading is removed (the seeds stay)
    burger_patty                    the calm grooves do not meet at the border: redrawn by calculation, grooves on
                                    lines that wrap (x + 2y), in the tile's own colours
    paul_petals                     the smooth part of the jump only (the flowers stay as drawn)

The tiles before the repair are kept in tools/ai-assets/out/calm_tiles/before_seam_repair/. Masonry (dark_sandstone, blackened_sandstone) is left as it is:
its lines at the border are joints.

Run it after build_tiles.py --bundle. No model, no cost.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/heal_built_seams.py [--dry-run]
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

TILE = 64
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
TILES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "built"
BEFORE = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "before_seam_repair"


def read(key: str) -> np.ndarray:
    """The tile as it was before the first repair (BEFORE), so a second run never repairs a repaired tile."""
    source = BEFORE / f"{key}.bytes" if (BEFORE / f"{key}.bytes").exists() else TILES / f"{key}.bytes"
    img = Image.frombytes("RGBA", (TILE, TILE), source.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)
    return np.asarray(img, dtype=np.float64)[..., :3]


def periodic(u: np.ndarray) -> np.ndarray:
    """Moisan's periodic component: the picture minus the smooth ramp that carries the jumps across its borders."""
    h, w = u.shape
    v = np.zeros_like(u)
    v[0, :] += u[-1, :] - u[0, :]
    v[-1, :] += u[0, :] - u[-1, :]
    v[:, 0] += u[:, -1] - u[:, 0]
    v[:, -1] += u[:, 0] - u[:, -1]
    q = np.arange(h).reshape(-1, 1)
    r = np.arange(w).reshape(1, -1)
    denom = 2 * np.cos(2 * np.pi * q / h) + 2 * np.cos(2 * np.pi * r / w) - 4
    denom[0, 0] = 1
    s = np.fft.fft2(v) / denom
    s[0, 0] = 0
    return u - np.real(np.fft.ifft2(s))


def lowpass(u: np.ndarray, sigma: float) -> np.ndarray:
    """A Gaussian blur that wraps at the tile's borders (in the frequency domain)."""
    fy = np.fft.fftfreq(u.shape[0]).reshape(-1, 1)
    fx = np.fft.fftfreq(u.shape[1]).reshape(1, -1)
    return np.real(np.fft.ifft2(np.fft.fft2(u) * np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))))


def seam(a: np.ndarray) -> float:
    g = a.mean(axis=2)
    inner_v, inner_h = np.abs(np.diff(g, axis=0)).mean(), np.abs(np.diff(g, axis=1)).mean()
    return max(np.abs(g[0, :] - g[-1, :]).mean() / max(inner_v, 0.5), np.abs(g[:, 0] - g[:, -1]).mean() / max(inner_h, 0.5))


def per_channel(a: np.ndarray, fn) -> np.ndarray:
    return np.stack([fn(a[..., c]) for c in range(3)], axis=-1)


def stem(a: np.ndarray) -> np.ndarray:
    level = a - (a.mean(axis=1, keepdims=True) - a.mean(axis=(0, 1), keepdims=True))  # every row the same mean
    return per_channel(level, periodic)


def cushion(a: np.ndarray) -> np.ndarray:
    flat = per_channel(a, lambda u: u - lowpass(u, 9.0) + u.mean())
    return per_channel(flat, periodic)


def patty(a: np.ndarray) -> np.ndarray:
    luma = a.mean(axis=2)
    base = a[luma >= np.percentile(luma, 55)].mean(axis=0)     # the meat
    groove = a[luma <= np.percentile(luma, 8)].mean(axis=0)    # the dark grooves
    light = a[luma >= np.percentile(luma, 94)].mean(axis=0)    # the lit lip above a groove
    over = 4
    axis = (np.arange(TILE * over) + 0.5) / over
    x, y = np.meshgrid(axis, axis)
    wobble = 0.035 * np.sin(2 * np.pi * (x + y) / TILE * 2 + 0.7)                  # the grooves sway a little, and wrap
    phase = ((2 * x + 4 * y) / TILE + wobble) % 1.0                                # lines x + 2y = const: they wrap
    t = np.clip(phase / 0.30, 0.0, 1.0)
    cut = 1.0 - t * t * (3.0 - 2.0 * t)                                            # 1 in the groove, 0 on the meat
    lip = np.exp(-(((phase - 0.40) / 0.08) ** 2))                                  # the lit edge just beside it
    shade = 0.04 * np.sin(2 * np.pi * (3 * x - 2 * y) / TILE + 1.9) + 0.03 * np.sin(2 * np.pi * (x + 3 * y) / TILE + 4.2)
    rgb = base[None, None, :] * (1.0 + shade[..., None])
    rgb = rgb + (light - base)[None, None, :] * (lip * 0.55)[..., None]
    rgb = rgb + (groove[None, None, :] - rgb) * cut[..., None]
    return rgb.reshape(TILE, over, TILE, over, 3).mean(axis=(1, 3))


JOBS = {
    "fifi_stem": stem, "saguaro": stem, "paul_stem": stem,
    "burger_bun": cushion,
    "burger_patty": patty,
    "paul_petals": lambda a: per_channel(a, periodic),
}


def main() -> None:
    dry = "--dry-run" in sys.argv
    BEFORE.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    for key, job in JOBS.items():
        raw = (TILES / f"{key}.bytes").read_bytes()
        kept = BEFORE / f"{key}.bytes"
        if not kept.exists():
            kept.write_bytes(raw)  # the tile before the first repair
        a = read(key)
        b = np.clip(job(a), 0.0, 255.0)
        b += a.mean(axis=(0, 1)) - b.mean(axis=(0, 1))  # the tile keeps its mean colour
        b = np.clip(b, 0.0, 255.0)
        rgba = np.dstack([(b + 0.5).astype(np.uint8), np.full((TILE, TILE), 255, np.uint8)])
        img = Image.fromarray(rgba, "RGBA")
        img.save(OUT / f"{key}-healed.png")
        print(f"{key}: seam {seam(a):.2f} -> {seam(b):.2f}")
        if not dry:
            (TILES / f"{key}.bytes").write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())


if __name__ == "__main__":
    main()
