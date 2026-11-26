# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""#4031 (PAID, one call): the school club's `bricks` tile — a red brick wall in the calm style, on the micro-cube raster.

A shaped block is built of micro cubes (8 per block edge = 8 px of the 64 px tile), and every part shows the piece of the
tile at its place, so the bricks must sit on that raster (whole bricks on a slab, a stair step, a quarter cube): courses
16 px high, bricks 32 px long, every second course shifted by 16 px (a running bond), the mortar joints centred on the
brick edges (1 px on each side, so two neighbouring blocks or parts close a 2 px joint).

1. A flat layout guide of exactly that bond (red bricks, grey joints) is the image-edit reference; the model
   (gpt-image-2.6-sunburst, like the calm set) paints it as soft fired bricks and mortar. The picture lands in
   out/calm_tiles/redraws/bricks.png and is not generated again while it exists.
2. Reduced to 64 px, then SNAPPED to the raster (no model): every pixel is split into the structure — the median colour
   of its own brick, or of the mortar — and the painted detail around it; the joints take the mortar's colour wherever
   the guide has them and nowhere else, the detail is made to wrap (Moisan's periodic part, make_sheet.periodic) and is
   added back at a calm strength. So the joints lie exactly on the 8 px raster, each brick keeps the colour the model gave
   it, and the tile wraps.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_bricks_tile.py [--bundle]

Without --bundle it prints the numbers (mean colour = the block's data colour in data/blocks.json, contrast, seam ratio,
how far the painted joints strayed from the raster) and writes the sheet out/calm_tiles/sheets/bricks.png; with it, also
client/Assets/Resources/textures/bricks.bytes (raw 64 x 64 RGBA32, rows bottom-up).
"""
from __future__ import annotations

import argparse
import base64
import io
import os
import sys

import numpy as np
from make_sheet import periodic, seam_ratio
from PIL import Image
from tilelib import REDRAWS, REPO, SHEETS, TEXTURES, TILE, luma, repeat, scale, sheet

KEY = "bricks"
COURSE = 16            # px per course (2 micro cells)
BRICK = 32             # px per brick (4 micro cells)
BOND = 16              # every second course shifted by half a brick (2 micro cells)
DETAIL = 0.8           # how much of the painted detail inside a brick or joint is kept (calm, like the other tiles)
MODEL = "gpt-image-2.6-sunburst"
PROMPT = ("Paint this flat brick wall layout as a calm game block texture of a red brick wall, front view, filling the "
          "whole image edge to edge. Keep every brick and every mortar joint exactly where it is in the image: the same "
          "rows, the same brick lengths, the same offsets, straight horizontal and vertical joints. Fired clay bricks in "
          "soft warm reds with a little orange and brown, each brick a slightly different shade, a faint soft surface "
          "texture; pale warm grey mortar, slightly recessed. Soft stylised hand-painted look like smooth matte "
          "modelling clay, gentle even lighting, very little detail, no pixel grid, no outlines, no shadows from outside, "
          "no text.")


def region_of(x: int, y: int) -> int:
    """-1 for a mortar pixel, else the brick's id (course * 2 + index within the course; the id wraps with the tile)."""
    course = y // COURSE
    shift = BOND if course % 2 else 0
    lx = (x - shift) % BRICK
    if y % COURSE in (0, COURSE - 1) or lx in (0, BRICK - 1):
        return -1
    return course * 2 + ((x - shift) % TILE) // BRICK


REGIONS = np.array([[region_of(x, y) for x in range(TILE)] for y in range(TILE)])


def guide() -> Image.Image:
    """The layout guide at 1024 px: flat red bricks, flat grey joints — the bond the model is told to keep."""
    rgb = np.where((REGIONS < 0)[..., None], np.array([178, 172, 160]), np.array([164, 76, 56])).astype(np.uint8)
    return Image.fromarray(rgb, "RGB").resize((1024, 1024), Image.NEAREST)


def generate(path) -> None:
    from dotenv import load_dotenv
    load_dotenv(REPO / "tools" / "ai-assets" / ".env")
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")
    from openai import OpenAI
    buf = io.BytesIO()
    guide().save(buf, format="PNG")
    buf.seek(0)
    buf.name = "bricks_guide.png"
    resp = OpenAI(api_key=key).images.edit(model=MODEL, image=buf, prompt=PROMPT, size="1024x1024", quality="low", n=1)
    path.write_bytes(base64.b64decode(resp.data[0].b64_json))
    print(f"{KEY}: generated with {MODEL}")


def reduce(path) -> np.ndarray:
    """The generated picture as a 64 px top-down RGB array (each tile pixel the mean of its block of picture pixels)."""
    a = np.asarray(Image.open(path).convert("RGB").resize((1024, 1024), Image.LANCZOS), dtype=np.float64)
    n = a.shape[0] // TILE
    return a.reshape(TILE, n, TILE, n, 3).mean(axis=(1, 3))


def snap(small: np.ndarray) -> tuple[np.ndarray, float]:
    """The structure (each region's median colour) plus the wrapped, calmed detail. Also returns how far the painted
    joints strayed: the share of guide-brick pixels the model painted closer to the mortar than to their brick."""
    structure = np.zeros_like(small)
    for r in np.unique(REGIONS):
        m = REGIONS == r
        structure[m] = np.median(small[m], axis=0)
    mortar = np.median(small[REGIONS < 0], axis=0)
    bricks = REGIONS >= 0
    d_own = np.linalg.norm(small - structure, axis=-1)
    d_mortar = np.linalg.norm(small - mortar, axis=-1)
    strayed = float((bricks & (d_mortar < d_own)).sum() / bricks.sum())
    detail = small - structure
    # A joint pixel the model painted as brick (or the reverse) keeps no detail of the wrong material.
    detail[d_own > 40.0] = 0.0
    detail = np.stack([periodic(detail[..., c]) for c in range(3)], axis=-1)
    return np.clip(structure + DETAIL * detail, 0.0, 255.0), strayed


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--bundle", action="store_true", help="write the tile into client/Assets/Resources/textures")
    args = ap.parse_args()

    REDRAWS.mkdir(parents=True, exist_ok=True)
    picture = REDRAWS / f"{KEY}.png"
    if not picture.exists():
        generate(picture)
    small = reduce(picture)
    tile, strayed = snap(small)
    lum = luma(tile)
    mean = tile.reshape(-1, 3).mean(axis=0)
    colour = (int(round(mean[0])) << 16) | (int(round(mean[1])) << 8) | int(round(mean[2]))
    print(f"{KEY}: mean {np.round(mean, 1)} = data colour {colour} (0x{colour:06X}), relative contrast "
          f"{lum.std() / lum.mean():.3f}, seam ratio {seam_ratio(lum):.2f} (unsnapped {seam_ratio(luma(small)):.2f}), "
          f"painted joints off the raster {strayed:.1%} of the brick pixels")
    rgba = np.concatenate([np.rint(tile), np.full((TILE, TILE, 1), 255.0)], axis=-1).astype(np.uint8)
    if args.bundle:
        (TEXTURES / f"{KEY}.bytes").write_bytes(np.ascontiguousarray(rgba[::-1]).tobytes())
        print(f"  -> {TEXTURES / (KEY + '.bytes')}")
    raw = Image.fromarray(np.clip(np.rint(small), 0, 255).astype(np.uint8), "RGB")
    img = Image.fromarray(rgba, "RGBA").convert("RGB")
    sheet([(KEY, [scale(raw, 4), scale(img, 4), repeat(img, 3)])], ["generated, 64 px", "snapped", "snapped, 3 x 3"],
          SHEETS / "bricks.png", title="Bricks (#4031)")


if __name__ == "__main__":
    main()
