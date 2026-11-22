# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 5: the grass sod. Composes the side tile of the grass kinds from the calm tiles — earth with the grass
hanging over its upper edge — writes it into client/Assets/Resources/textures/face_<key>_side.bytes and a contact
sheet into tools/ai-assets/out/calm_tiles/sheets. Run it after build_tiles.py --bundle (it reads the calm grass,
alien grass, mycelium and dirt tiles).

No picture model is involved: the upper band is the block's own top tile, the rest the earth tile, cut along an
irregular scalloped line that wraps at the tile's sides; the grass's underside and the earth right under it are
shaded so the band reads as hanging over.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_sod.py
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

TILE = 64
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
TILES = REPO / "client" / "Assets" / "Resources" / "textures"
SHEETS = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "sheets"

# block key -> (the earth under it, phases of the scallop's three waves, where the single longer tongues hang)
SODS = {
    "grass": ("dirt", (0.4, 2.1, 4.4), (7, 23, 30, 46, 58)),
    "alien_grass": ("dirt", (1.9, 0.3, 3.0), (3, 15, 37, 41, 53)),
    "mycelium": ("dirt", (3.1, 5.2, 1.2), (11, 19, 34, 49, 60)),
}


def read(key: str) -> np.ndarray:
    """A calm tile (raw 64 x 64 RGBA32, rows bottom-up) as a float array, row 0 = the top row."""
    raw = (TILES / f"{key}.bytes").read_bytes()
    img = Image.frombytes("RGBA", (TILE, TILE), raw).transpose(Image.FLIP_TOP_BOTTOM)
    return np.asarray(img, dtype=np.float64)


def write(key: str, rgba: np.ndarray) -> Image.Image:
    img = Image.fromarray(np.clip(rgba + 0.5, 0, 255).astype(np.uint8), "RGBA")
    (TILES / f"{key}.bytes").write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    return img


def depth(phases: tuple[float, float, float], tongues: tuple[int, ...]) -> np.ndarray:
    """How many rows the grass hangs down at each column: soft scallops (whole waves per tile, so the line wraps)
    plus a few longer tongues two pixels wide."""
    x = np.arange(TILE)
    d = 13.0 + 3.2 * np.sin(2 * math.pi * 2 * x / TILE + phases[0]) \
        + 2.0 * np.sin(2 * math.pi * 5 * x / TILE + phases[1]) \
        + 1.3 * np.sin(2 * math.pi * 9 * x / TILE + phases[2])
    d = np.round(d)
    for i, t in enumerate(tongues):
        extra = 3 if i % 2 == 0 else 2
        d[t % TILE] += extra
        d[(t + 1) % TILE] += extra - 1
    return np.clip(d, 6, 26).astype(int)


def sod(key: str) -> Image.Image:
    earth_key, phases, tongues = SODS[key]
    top = read(key)
    out = read(earth_key).copy()
    d = depth(phases, tongues)
    for x in range(TILE):
        rows = d[x]
        out[:rows, x, :3] = top[:rows, x, :3]
        # the underside of the band turns away from the light …
        for k, shade in enumerate((0.80, 0.90)):
            y = rows - 1 - k
            if y >= 0:
                out[y, x, :3] *= shade
        # … and throws a short shadow onto the earth under it
        for k, shade in enumerate((0.72, 0.84, 0.93)):
            y = rows + k
            if y < TILE:
                out[y, x, :3] *= shade
    out[..., 3] = 255
    return write(f"face_{key}_side", out)


def wall(key: str, side: Image.Image) -> Image.Image:
    """Three blocks wide: a row of the block's top tile (seen from above), under it two rows of blocks whose upper
    one is the grass kind (the new side) and whose lower one is earth."""
    top = Image.fromarray(read(key).astype(np.uint8), "RGBA")
    earth = Image.fromarray(read(SODS[key][0]).astype(np.uint8), "RGBA")
    img = Image.new("RGB", (TILE * 3, TILE * 3))
    for i in range(3):
        img.paste(top.resize((TILE, TILE // 2)), (i * TILE, 0))
        img.paste(side, (i * TILE, TILE // 2))
        img.paste(earth, (i * TILE, TILE // 2 + TILE))
        img.paste(earth, (i * TILE, TILE // 2 + 2 * TILE))
    img = img.crop((0, 0, TILE * 3, TILE * 2 + TILE // 2))
    return img.resize((img.width * 4, img.height * 4), Image.NEAREST)


def old_wall(key: str) -> Image.Image:
    """The same wall as it was before: the grass kind's own tile on its side."""
    top = Image.fromarray(read(key).astype(np.uint8), "RGBA")
    earth = Image.fromarray(read(SODS[key][0]).astype(np.uint8), "RGBA")
    img = Image.new("RGB", (TILE * 3, TILE * 2 + TILE // 2))
    for i in range(3):
        img.paste(top.resize((TILE, TILE // 2)), (i * TILE, 0))
        img.paste(top, (i * TILE, TILE // 2))
        img.paste(earth, (i * TILE, TILE // 2 + TILE))
    return img.resize((img.width * 4, img.height * 4), Image.NEAREST)


def main() -> None:
    cells = []
    for key in SODS:
        side = sod(key)
        cells.append((key, old_wall(key), wall(key, side)))
        print("written", f"face_{key}_side.bytes")
    pad, label = 16, 26
    sheet = Image.new("RGB", (pad * 3 + cells[0][1].width * 2, (cells[0][1].height + label + pad) * len(cells) + pad), (40, 40, 44))
    draw = ImageDraw.Draw(sheet)
    y = pad
    for key, old, new in cells:
        draw.text((pad, y), f"{key}: before (left)  /  with the sod (right)", fill=(235, 235, 235))
        sheet.paste(old, (pad, y + label))
        sheet.paste(new, (pad * 2 + old.width, y + label))
        y += old.height + label + pad
    SHEETS.mkdir(parents=True, exist_ok=True)
    sheet.save(SHEETS / "contact-sod.png")
    print("sheet", sheet.size)


if __name__ == "__main__":
    main()
