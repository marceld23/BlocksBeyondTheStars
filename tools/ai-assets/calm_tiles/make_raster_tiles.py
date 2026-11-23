# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 10: device material tiles whose pattern sits on the raster of the micro cubes (1 micro cell = 8 px).

A device form is built of small cubes on the 8-grid, and every part shows the piece of its tile that lies at its
place. A tile drawn as ONE picture for a whole block face (the generated deck, vent, lamp and display) then shows half
a button or a cut circle on a small part. These are therefore drawn here by rule (no model, no cost), soft and matte
like the other calm tiles, with a period of 8 px (keys, slats, lamp facets, display cells) or 32 px (metal plates): any cut on the
8-grid gives whole keys, whole slats, whole plates. Embers and the two tank liquids stay the generated pictures
(no motif, they wrap; gen_device_tiles.py must have run first). devform_metal_light is the plates of
devform_metal_dark on a near-white base, the housing of the ship station decor (StationDecorView.Forms.cs). Drawn at
8x and averaged down; written as raw 64 x 64 RGBA, rows bottom-up, into client/Assets/Resources/textures.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_raster_tiles.py [--no-bundle]
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
DEST = REPO / "client" / "Assets" / "Resources" / "textures"
DEVICES = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "devices"
TILE, SS = 64, 8
N = TILE * SS
YY, XX = np.mgrid[0:N, 0:N]
X = (XX + 0.5) / SS          # tile pixels, x to the right
Y = (YY + 0.5) / SS          # tile pixels, y DOWN from the top row of the picture


def rrect(cx, cy, hw, hh, r):
    """Signed distance (px) to a rounded rectangle; cx/cy may be arrays (periodic cells)."""
    dx = np.abs(X - cx) - (hw - r)
    dy = np.abs(Y - cy) - (hh - r)
    return np.hypot(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0) - r


def cover(d, soft=0.7):
    """1 inside, 0 outside, a soft edge of `soft` px."""
    return np.clip(0.5 - d / soft, 0.0, 1.0)


def mix(a, b, t):
    return a + (b - a) * t[..., None]


def flat(rgb):
    return np.ones((N, N, 3)) * np.asarray(rgb, dtype=np.float64)


def cell_hash(ix, iy, salt):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (salt * 83492791)
    return ((h & 0xFFFF) / 65535.0)


def metal_dark(base=(58, 66, 82)):
    """Plates of 32 px (4 micro cells) with a soft joint, a soft bevel lit from the upper left, four small rivets."""
    px, py = X % 32, Y % 32
    plate = (X // 32 + 2 * (Y // 32)).astype(int)
    tone = np.choose(plate % 4, [1.00, 0.955, 0.975, 1.03])
    img = flat(base) * tone[..., None]
    img *= (1.0 + 0.035 * np.cos(2 * np.pi * (Y - 10) / 64))[..., None]          # a broad soft shade, wraps
    edge = np.minimum(np.minimum(px, 32 - px), np.minimum(py, 32 - py))
    lit = np.clip(1 - np.minimum(px, py) / 2.6, 0, 1) * (edge > 0.7)
    shade = np.clip(1 - np.minimum(32 - px, 32 - py) / 2.6, 0, 1) * (edge > 0.7)
    img *= (1 + 0.16 * lit - 0.17 * shade)[..., None]
    img *= (1 - 0.42 * np.clip(1 - edge / 0.9, 0, 1))[..., None]                 # the joint itself
    for rx in (4.0, 28.0):
        for ry in (4.0, 28.0):
            cx, cy = (X // 32) * 32 + rx, (Y // 32) * 32 + ry
            d = np.hypot(X - cx, Y - cy)
            dome = cover(d - 1.35, 0.6)
            light = np.clip(((cx - X) + (cy - Y)) / 2.4, -1, 1)
            img = mix(img, img * (1.10 + 0.22 * light)[..., None], dome)
            img *= (1 - 0.22 * cover(np.hypot(X - cx - 0.5, Y - cy - 0.6) - 1.7, 0.9) * (1 - dome))[..., None]
    return img


def metal_light():
    """The light housing of the ship consoles: the plates of metal_dark on a near-white base."""
    return metal_dark((224, 229, 236))


def deck():
    """One key per micro cell: a soft rounded key on dark metal, a few lit in cyan, two in amber."""
    ix, iy = (X // 8).astype(int), (Y // 8).astype(int)
    cx, cy = ix * 8 + 4.0, iy * 8 + 4.0
    img = flat((38, 43, 54))
    img *= (1 - 0.30 * cover(rrect(cx + 0.45, cy + 0.65, 3.2, 3.2, 1.4), 1.2))[..., None]     # soft drop shadow
    key = cover(rrect(cx, cy, 3.05, 3.05, 1.35), 0.7)
    lit_c = {(1, 0), (5, 1), (2, 3), (6, 4), (0, 6), (4, 6), (3, 7), (7, 2)}
    lit_a = {(6, 1), (1, 5)}
    body = flat((86, 93, 108)) * (0.93 + 0.14 * cell_hash(ix, iy, 3))[..., None]
    cyan = np.zeros((N, N), dtype=bool)
    amber = np.zeros((N, N), dtype=bool)
    for (a, b) in lit_c:
        cyan |= (ix == a) & (iy == b)
    for (a, b) in lit_a:
        amber |= (ix == a) & (iy == b)
    body = np.where(cyan[..., None], np.asarray((72, 224, 238.0)), body)
    body = np.where(amber[..., None], np.asarray((250, 186, 72.0)), body)
    light = np.clip(((cx - X) + (cy - Y)) / 5.0, -1, 1)
    body = body * (1.0 + 0.16 * light)[..., None]
    img = mix(img, body, key)
    glow = np.zeros((N, N, 3))
    glow[cyan] = (72, 224, 238)
    glow[amber] = (250, 186, 72)
    glow *= key[..., None]
    halo = np.asarray(Image.fromarray(np.clip(glow, 0, 255).astype(np.uint8), "RGB").filter(
        ImageFilter.GaussianBlur(1.6 * SS)), dtype=np.float64)
    return img + 0.30 * halo * (1 - key[..., None])


def vent():
    """One louvre slat per micro cell: a rounded slat lit from above, a dark gap below it."""
    v = Y % 8
    slat = np.clip(v / 6.0, 0, 1)
    profile = 0.62 + 0.52 * np.cos((slat - 0.22) * np.pi * 0.95)                  # light near the top, falling off
    img = flat((62, 69, 86)) * profile[..., None]
    gap = np.clip((v - 5.6) / 0.9, 0, 1) * np.clip((8.3 - v) / 0.5, 0, 1)
    img = mix(img, flat((14, 16, 22)), np.clip(gap, 0, 1))
    img *= (1 + 0.03 * np.cos(2 * np.pi * X / 64))[..., None]
    return img


def lamp_amber():
    """An even glowing surface: one soft bright facet per micro cell."""
    u, v = X % 8 - 4.0, Y % 8 - 4.0
    r = np.hypot(u, v) / 4.0
    t = np.clip(r / 1.25, 0, 1)
    t = t * t * (3 - 2 * t)
    img = mix(flat((255, 224, 118)), flat((240, 142, 34)), t)
    edge = np.minimum(np.minimum(X % 8, 8 - X % 8), np.minimum(Y % 8, 8 - Y % 8))
    img *= (1 - 0.10 * np.clip(1 - edge / 0.7, 0, 1))[..., None]
    return img


def screen():
    """A data display in display cells of 8 px: soft bars and small squares in rows on dark navy, with a glow."""
    rows = [
        ([(0, 5)], [6, 7]), ([(0, 3)], [5]), ([(0, 6)], []), ([(4, 8)], [0, 1, 2]),
        ([(0, 2)], [3, 6, 7]), ([(0, 4)], [6]), ([(1, 7)], []), ([(5, 8)], [0, 2, 4]),
    ]
    layer = np.zeros((N, N))
    for r, (bars, squares) in enumerate(rows):
        cy = r * 8 + 4.0
        level = (1.0, 0.62, 0.85, 1.0, 0.7, 1.0, 0.55, 0.9)[r]
        for (a, b) in bars:
            layer = np.maximum(layer, level * cover(rrect((a + b) * 4.0, cy, (b - a) * 4.0 - 1.5, 1.45, 1.2), 0.7))
        for c in squares:
            layer = np.maximum(layer, level * cover(rrect(c * 8 + 4.0, cy, 1.7, 1.7, 0.7), 0.7))
    base = flat((10, 22, 58)) * (1 + 0.10 * np.cos(2 * np.pi * Y / 16))[..., None]
    cyan = np.asarray((84, 236, 248.0))
    lit = (layer[..., None] * cyan)
    halo = np.asarray(Image.fromarray(np.clip(lit, 0, 255).astype(np.uint8), "RGB").filter(
        ImageFilter.GaussianBlur(1.5 * SS)), dtype=np.float64)
    return base * (1 - layer[..., None]) + lit + 0.40 * halo * (1 - layer[..., None])


MAKERS = {"devform_metal_dark": metal_dark, "devform_metal_light": metal_light, "devform_deck": deck, "devform_screen": screen, "devform_vent": vent,
          "devform_lamp_amber": lamp_amber}
KEPT = ["devform_embers", "devform_liquid_teal", "devform_liquid_green"]      # generated: gen_device_tiles.py


def main() -> None:
    bundle = "--no-bundle" not in sys.argv
    tiles: dict[str, Image.Image] = {}
    for key, maker in MAKERS.items():
        big = np.clip(maker(), 0, 255)
        small = big.reshape(TILE, SS, TILE, SS, 3).mean(axis=(1, 3))
        tiles[key] = Image.fromarray((small + 0.5).astype(np.uint8), "RGB")
        DEVICES.mkdir(parents=True, exist_ok=True)
        gen = DEVICES / f"{key}_64.png"
        if gen.exists() and not (DEVICES / f"{key}_generated_64.png").exists():
            gen.rename(DEVICES / f"{key}_generated_64.png")                    # the generated first version, kept
        tiles[key].save(DEVICES / f"{key}_64.png")
    for key in KEPT:
        tiles[key] = Image.open(DEVICES / f"{key}_64.png").convert("RGB")

    cell = 64 * 4
    order = list(MAKERS) + KEPT
    sheet = Image.new("RGB", (len(order) * (cell + 8), 2 * cell + 60), (30, 30, 34))
    draw = ImageDraw.Draw(sheet)
    for i, key in enumerate(order):
        ox = i * (cell + 8)
        draw.text((ox + 2, 2), key + ("" if key in MAKERS else " (generated)"), fill=(255, 255, 255))
        sheet.paste(tiles[key].resize((cell, cell), Image.NEAREST), (ox, 20))
        twice = Image.new("RGB", (128, 128))
        for dx in (0, 64):
            for dy in (0, 64):
                twice.paste(tiles[key], (dx, dy))
        sheet.paste(twice.resize((cell, cell), Image.NEAREST), (ox, 40 + cell))
        if bundle and key in MAKERS:
            data = tiles[key].convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes()
            assert len(data) == 64 * 64 * 4
            (DEST / f"{key}.bytes").write_bytes(data)
            print("bundled", key)
    sheet.save(DEVICES / "device-tiles-raster.png")
    print("sheet", DEVICES / "device-tiles-raster.png", sheet.size)


if __name__ == "__main__":
    main()
