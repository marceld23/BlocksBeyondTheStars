# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Recolour the rune stone and the ancient brick into the eight biome palettes (#3289).

Screelit asked for his rune stone to look different in every biome: "everything stays as it is, only different notes in
different colours in different biomes — and the same for the old stone". The game's mesher picks the tile by the biome
a block stands in (BiomePalettes in src/BlocksBeyondTheStars.Shared); this script paints those tiles:

  rune_stone_<palette>    — his notes, every one exactly where it is, in the palette's two-colour gradient plus its
                            contrast note; the stone frame takes a subtle tint of the biome.
  ancient_brick_<palette> — the ruin masonry tinted toward the biome's stone, and its moss turned into the biome's
                            growth (frost, ash glow, crystal, acid ...).

Deterministic, no model and no randomness. The notes are not redrawn or traced: the note geometry of
refine_rune_stone.py (which drew them) gives each pixel's note coverage `a` and the colour it was engraved with `N`;
a pixel P = stone·(1−a) + N·a becomes T(stone)·(1−a) + N'·a, i.e. P' = T(P) + a·(N' − T(N)) for the linear stone
tint T — so the anti-aliased edges, the shadows and the stone between the notes stay Screelit's pixels.

    uv run --no-project --with "pillow>=13" python tools/ai-assets/recolour_biome_looks.py [--preview grid.png]

Reads client/Assets/Resources/textures/{rune_stone,ancient_brick}.bytes and writes the 16 tiles next to them (raw 64x64
RGBA32, rows bottom-up, fully opaque). The palette order is BiomePalettes.Names.
"""
import argparse
import colorsys
from pathlib import Path

import refine_rune_stone as rune
from PIL import Image, ImageDraw

SIZE = 64
REPO = Path(__file__).resolve().parents[2]
TEXTURES = REPO / "client" / "Assets" / "Resources" / "textures"
PALETTES = ["verdant", "desert", "frost", "volcanic", "marsh", "rock", "crystal", "toxic"]

# Per palette: the notes' gradient (stops by tile row, like refine_rune_stone.GRADIENT), the contrast note's (like
# CONTRAST), and the stone tint (colour, strength).
RUNE = {
    "verdant": {"notes": [(12, (176, 238, 96)), (30, (118, 220, 84)), (42, (72, 198, 92)), (54, (52, 170, 96))],
        "accent": [(10, (255, 168, 206)), (32, (236, 96, 156))], "stone": ((96, 138, 82), 0.34)},
    "desert": {"notes": [(12, (255, 226, 128)), (30, (250, 196, 84)), (42, (240, 156, 60)), (54, (222, 112, 46))],
        "accent": [(10, (124, 236, 226)), (32, (44, 182, 196))], "stone": ((182, 150, 104), 0.38)},
    "frost": {"notes": [(12, (240, 250, 255)), (30, (190, 230, 255)), (42, (140, 204, 252)), (54, (96, 168, 246))],
        "accent": [(10, (214, 176, 255)), (32, (160, 116, 242))], "stone": ((150, 182, 220), 0.36)},
    "volcanic": {"notes": [(12, (255, 150, 64)), (30, (250, 104, 48)), (42, (232, 70, 40)), (54, (200, 44, 36))],
        "accent": [(10, (255, 240, 140)), (32, (255, 196, 64))], "stone": ((132, 72, 58), 0.40)},
    "marsh": {"notes": [(12, (126, 222, 186)), (30, (98, 198, 158)), (42, (112, 176, 104)), (54, (146, 164, 64))],
        "accent": [(10, (226, 146, 238)), (32, (176, 96, 206))], "stone": ((104, 118, 86), 0.36)},
    "rock": {"notes": [(12, (236, 234, 230)), (30, (208, 206, 204)), (42, (182, 180, 182)), (54, (156, 158, 166))],
        "accent": [(10, (246, 170, 104)), (32, (206, 112, 62))], "stone": ((146, 140, 132), 0.22)},
    "crystal": {"notes": [(12, (250, 150, 255)), (30, (214, 124, 255)), (42, (166, 112, 255)), (54, (126, 104, 250))],
        "accent": [(10, (150, 252, 255)), (32, (64, 206, 246))], "stone": ((142, 118, 178), 0.38)},
    "toxic": {"notes": [(12, (238, 255, 92)), (30, (204, 248, 66)), (42, (160, 236, 54)), (54, (112, 214, 52))],
        "accent": [(10, (226, 120, 255)), (32, (172, 64, 226))], "stone": ((138, 150, 72), 0.40)},
}

# Per palette: the bricks' tint (colour, strength, brightness) and what the moss becomes (colour, strength).
BRICK = {
    "verdant": {"brick": ((150, 162, 116), 0.48, 0.95), "moss": ((70, 182, 50), 1.0), "grow": 0.8},
    "desert": {"brick": ((226, 184, 118), 0.62, 1.04), "moss": ((186, 146, 78), 0.9), "grow": 0.0},
    "frost": {"brick": ((188, 206, 228), 0.72, 1.08), "moss": ((236, 246, 255), 0.95), "grow": 0.15},
    "volcanic": {"brick": ((104, 86, 82), 0.78, 0.62), "moss": ((236, 96, 40), 0.95), "grow": 0.0},
    "marsh": {"brick": ((122, 100, 74), 0.72, 0.76), "moss": ((44, 108, 86), 1.0), "grow": 0.45},
    "rock": {"brick": ((158, 156, 154), 0.80, 0.92), "moss": ((150, 160, 112), 0.7), "grow": 0.0},
    "crystal": {"brick": ((176, 156, 208), 0.66, 0.98), "moss": ((116, 222, 246), 0.95), "grow": 0.1},
    "toxic": {"brick": ((172, 174, 92), 0.62, 0.92), "moss": ((168, 242, 48), 1.0), "grow": 0.25},
}


def luma(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def tinter(colour, strength):
    """The stone tint T(c) = (1−k)·c + k·luma(c)·colour/luma(colour) — linear in c, keeps the luminance."""
    lc = luma(colour)

    def t(c):
        lum = luma(c) / lc
        return tuple((1 - strength) * c[i] + strength * lum * colour[i] for i in range(3))

    return t


def engraved(cover, stops, x, y):
    """The colour refine_rune_stone.paint gave a note pixel: the row's gradient colour, lit on its top edge, darker on
    its lower rim."""
    r, g, b = rune.ramp(stops, y)
    above = cover[x, y - 1] / 255 if y > 0 else 0
    below = cover[x, y + 1] / 255 if y < SIZE - 1 else 0
    if above < 0.5:
        return tuple(min(255, round(c * 1.18 + 18)) for c in (r, g, b))
    if below < 0.5:
        return tuple(round(c * 0.8) for c in (r, g, b))
    return (r, g, b)


def recolour_rune(tile, pal):
    """His notes in the palette's colours, the stone tinted — see the module docstring for the exact rule."""
    his, added = rune.his_notes().load(), rune.added_note().load()
    t = tinter(*pal["stone"])
    out = tile.copy()
    src, dst = tile.load(), out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            p = src[x, y]
            q = list(t(p[:3]))
            a1, a2 = his[x, y] / 255, added[x, y] / 255
            for cover, a, old, new, weight in (
                (his, a1, rune.GRADIENT, pal["notes"], 1 - a2),  # his notes sit under the added one
                (added, a2, rune.CONTRAST, pal["accent"], 1.0),
            ):
                if a <= 0.0:
                    continue
                n_old, n_new = t(engraved(cover, old, x, y)), engraved(cover, new, x, y)
                for i in range(3):
                    q[i] += weight * a * (n_new[i] - n_old[i])
            dst[x, y] = (*(max(0, min(255, round(v))) for v in q), 255)
    return out


def moss_weight(c):
    """How much of a brick pixel is moss: green-dominant and saturated (the bricks are a sandy beige, the mortar grey)."""
    h, s, v = colorsys.rgb_to_hsv(*(ch / 255 for ch in c))
    green = 1.0 - min(1.0, abs(h * 360 - 95) / 45)  # hue 50..140 degrees, peaking at 95
    return max(0.0, min(1.0, green * (s - 0.18) / 0.22)) if v > 0.08 else 0.0


def recolour_brick(tile, pal):
    """The bricks tinted toward the biome's stone, the moss turned into the biome's growth."""
    (bc, bk, value), (mc, mk), grow = pal["brick"], pal["moss"], pal["grow"]
    t = tinter(bc, bk)
    lm = luma(mc)
    out = tile.copy()
    src, dst = tile.load(), out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            p = src[x, y][:3]
            m = moss_weight(p)
            # "grow" lets the growth creep a little onto the mossy brick around it (a lush or icy biome).
            m = min(1.0, m * (1 + grow)) * mk
            b = [c * value for c in t(p)]
            lum = luma(p) / lm
            g = [lum * mc[i] * 1.15 for i in range(3)]
            q = [b[i] * (1 - m) + g[i] * m for i in range(3)]
            dst[x, y] = (*(max(0, min(255, round(v))) for v in q), 255)
    return out


def load(key):
    return rune.load_tile(TEXTURES / f"{key}.bytes").convert("RGBA")


def save(img, key):
    path = TEXTURES / f"{key}.bytes"
    path.write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    return path


def preview(rows, path):
    """A labelled grid: one row per block, the original first, then the eight palettes, each tile at 4x."""
    scale, pad, label = 4, 6, 14
    cols = 1 + len(PALETTES)
    w = cols * (SIZE * scale + pad) + pad
    h = len(rows) * (SIZE * scale + pad + label) + pad
    grid = Image.new("RGB", (w, h), (24, 24, 28))
    draw = ImageDraw.Draw(grid)
    for r, (name, tiles) in enumerate(rows):
        y = pad + r * (SIZE * scale + pad + label)
        for c, (title, img) in enumerate(tiles):
            x = pad + c * (SIZE * scale + pad)
            draw.text((x, y), f"{name} {title}" if c == 0 else title, fill=(220, 220, 220))
            grid.paste(img.convert("RGB").resize((SIZE * scale, SIZE * scale), Image.NEAREST), (x, y + label))
    grid.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--preview", type=Path, help="also write a PNG grid of the originals and all 16 tiles")
    args = parser.parse_args()

    rune_tile, brick_tile = load("rune_stone"), load("ancient_brick")
    rows = [("rune_stone", [("(own)", rune_tile)]), ("ancient_brick", [("(own)", brick_tile)])]
    for name in PALETTES:
        r = recolour_rune(rune_tile, RUNE[name])
        b = recolour_brick(brick_tile, BRICK[name])
        print("wrote", save(r, f"rune_stone_{name}"))
        print("wrote", save(b, f"ancient_brick_{name}"))
        rows[0][1].append((name, r))
        rows[1][1].append((name, b))

    if args.preview:
        preview(rows, args.preview)
        print("wrote", args.preview)


if __name__ == "__main__":
    main()
