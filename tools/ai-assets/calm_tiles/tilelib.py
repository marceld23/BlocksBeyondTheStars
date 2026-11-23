# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Shared paths and helpers of the calm tile scripts: read a tile, tile and scale pictures, label contact sheets.

Paths (see README.md):
    TEXTURES  client/Assets/Resources/textures — where the finished tiles go (and where the later steps read the calm
              tiles the earlier steps wrote)
    PREVIOUS  tools/ai-assets/out/calm_tiles/previous — the tile set BEFORE the calm set, the reference every redraw and
              every colour is taken from (export it from git, README.md)
    OUT       tools/ai-assets/out/calm_tiles — raw generated images, intermediate tiles and sheets (git-ignored)

Run with the tools' uv environment plus numpy:
    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/<script>.py
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TILE = 64
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
TEXTURES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = REPO / "tools" / "ai-assets" / "out" / "calm_tiles"
PREVIOUS = OUT / "previous"
FORMS = OUT / "forms"          # generated greyscale forms and masks (gen_calm_tiles.py)
REDRAWS = OUT / "redraws"      # 1024 px redraws of the previous tiles (restyle_tiles.py)
DEVICES = OUT / "devices"      # generated device material pictures and their 64 px versions (gen_device_tiles.py)
BUILT = OUT / "built"          # every finished tile as a PNG (build_tiles.py)
SHEETS = OUT / "sheets"        # contact sheets for the approval


def read_previous(key: str) -> Image.Image:
    """The previous tile of a key (before the calm set, raw 64 x 64 RGBA32, rows bottom-up) as an RGB picture."""
    raw = (PREVIOUS / f"{key}.bytes").read_bytes()
    return Image.frombytes("RGBA", (TILE, TILE), raw).transpose(Image.FLIP_TOP_BOTTOM).convert("RGB")


def luma(rgb: np.ndarray) -> np.ndarray:
    """Luminance 0..1 of an RGB array (0..255)."""
    return (rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114) / 255.0


def repeat(img: Image.Image, n: int) -> Image.Image:
    """The tile laid n x n times, as a wall of blocks shows it."""
    out = Image.new("RGB", (img.width * n, img.height * n))
    for y in range(n):
        for x in range(n):
            out.paste(img, (x * img.width, y * img.height))
    return out


def scale(img: Image.Image, factor: int) -> Image.Image:
    """Hard-edged enlargement: the game filters its atlas by point, so this is what a close block shows."""
    return img.resize((img.width * factor, img.height * factor), Image.NEAREST)


def font(size: int) -> ImageFont.ImageFont:
    for name in ("segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def sheet(rows: list[tuple[str, list[Image.Image]]], heads: list[str], path: Path, title: str = "",
          pad: int = 14, label_w: int = 170) -> None:
    """A labelled grid: one row per entry, one column per head. Cells may differ in size per column."""
    cols = len(heads)
    col_w = [max((r[1][c].width for r in rows if c < len(r[1])), default=0) for c in range(cols)]
    row_h = [max(i.height for i in r[1]) for r in rows]
    head_h = 34
    title_h = 46 if title else 0
    w = label_w + sum(col_w) + pad * (cols + 1)
    h = title_h + head_h + sum(row_h) + pad * (len(rows) + 1)
    out = Image.new("RGB", (w, h), (28, 30, 36))
    d = ImageDraw.Draw(out)
    if title:
        d.text((pad, 10), title, fill=(240, 240, 240), font=font(24))
    x = label_w + pad
    for c in range(cols):
        d.text((x, title_h + 6), heads[c], fill=(200, 205, 215), font=font(17))
        x += col_w[c] + pad
    y = title_h + head_h + pad
    for r, (label, cells) in enumerate(rows):
        d.text((pad, y + 4), label, fill=(240, 240, 240), font=font(18))
        x = label_w + pad
        for c, cell in enumerate(cells):
            out.paste(cell, (x, y))
            x += col_w[c] + pad
        y += row_h[r] + pad
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save(path)
    print("wrote", path, out.size)
