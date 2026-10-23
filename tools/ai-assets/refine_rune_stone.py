# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Refine Screelit's rune-stone submission into the game's rune_stone tile (#2631).

Screelit painted music notes over our rune stone (texture submission 2026-10-15) and asked for a cleaned-up version,
not a 1:1 copy:

  keep    — the notes he painted (an eighth note top-left, a beamed pair of eighth notes in the middle) and the
            light-blue to light-green colour transition;
  improve — the shape of the notes (heads, stems, flag, beam) and a smoother gradient;
  add     — one more note, in a colour that contrasts with the others.

The note geometry below is his: every head, stem, flag and beam sits where he drew it, only redrawn with round heads
and even strokes. The stone frame and background are his tile's (our old frame), with his note pixels filled from the
surrounding stone. Deterministic: no model, no randomness.

    uv run --no-project --with "pillow>=13" python tools/ai-assets/refine_rune_stone.py <submission>/texture.bytes \
        [--out client/Assets/Resources/textures/rune_stone.bytes] [--preview preview.png]

The output is the game's raw tile format: 64x64 RGBA32, rows bottom-up.
"""
import argparse
import colorsys
import itertools
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 64
SS = 8  # supersampling factor for the anti-aliased shapes
REPO = Path(__file__).resolve().parents[2]
DEFAULT_OUT = REPO / "client" / "Assets" / "Resources" / "textures" / "rune_stone.bytes"

# His gradient, made smoother and a touch lighter ("von Hellblau zu Hellgrün"): stops by tile row.
GRADIENT = [(12, (86, 150, 238)), (30, (64, 196, 232)), (42, (70, 214, 190)), (54, (128, 222, 92))]
# The added note: warm gold — the complement of his blue-green, and the colour the old runes glowed in.
CONTRAST = [(10, (255, 214, 110)), (32, (240, 150, 40))]


def load_tile(path):
    data = Path(path).read_bytes()[: SIZE * SIZE * 4]
    return Image.frombytes("RGBA", (SIZE, SIZE), data).transpose(Image.FLIP_TOP_BOTTOM)


def note_mask(tile):
    """His note pixels: saturated, reasonably bright (the stone is grey)."""
    mask = Image.new("L", (SIZE, SIZE), 0)
    src, dst = tile.load(), mask.load()
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, _ = src[x, y]
            _, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.35 and v > 0.25:
                dst[x, y] = 255
    return mask


def fill_stone(tile, mask):
    """Replace the old note pixels (plus a one-pixel rim) with stone copied from beside them."""
    grown = mask.filter(ImageFilter.MaxFilter(3))
    out = tile.copy()
    src, m, dst = tile.load(), grown.load(), out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if not m[x, y]:
                continue
            for dx, dy in ((14, 0), (-14, 0), (0, 14), (0, -14), (21, 7), (-21, -7), (7, 21), (-7, -21)):
                sx, sy = x + dx, y + dy
                if 5 <= sx < SIZE - 5 and 5 <= sy < SIZE - 5 and not m[sx, sy]:
                    dst[x, y] = src[sx, sy]
                    break
    return out


def head(draw, cx, cy, rx, ry, tilt_deg):
    """A tilted, filled ellipse (a note head), in tile coordinates."""
    t = math.radians(tilt_deg)
    pts = []
    for i in range(48):
        a = 2 * math.pi * i / 48
        x, y = rx * math.cos(a), ry * math.sin(a)
        pts.append(((cx + x * math.cos(t) - y * math.sin(t)) * SS, (cy + x * math.sin(t) + y * math.cos(t)) * SS))
    draw.polygon(pts, fill=255)


def rect(draw, x0, y0, x1, y1):
    draw.rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], fill=255)


def poly(draw, pts):
    draw.polygon([(x * SS, y * SS) for x, y in pts], fill=255)


def eighth_flag(draw, x, top, length, width):
    """A flag sweeping from the stem top down to the right, like the one he drew."""
    outer, inner = [], []
    for i in range(17):
        t = i / 16
        fx = x + width * math.sin(t * math.pi * 0.62)
        fy = top + length * t
        outer.append((fx + 1.9 * (1 - t), fy))
        inner.append((fx - 0.2, fy + 2.6 * (1 - t) + 0.4))
    poly(draw, outer + inner[::-1])


def his_notes():
    """His two notes, redrawn in place (supersampled coverage mask)."""
    m = Image.new("L", (SIZE * SS, SIZE * SS), 0)
    d = ImageDraw.Draw(m)
    # Note 1 — the eighth note top-left: head bottom-left (he drew it at x10-13, y29-32), stem x12-13 up to y13,
    # flag from the stem top out to x≈23.
    head(d, 11.6, 31.0, 3.0, 2.2, -22)
    rect(d, 13.0, 13.0, 14.6, 31.0)
    eighth_flag(d, 14.4, 13.0, 9.5, 8.0)
    # Note 2 — the beamed pair: heads at the bottom (x24-29 / x37-41, y46-52), stems up to a beam that rises to the
    # right (y≈25 on the left, y≈21 on the right), stems on the heads' right side.
    head(d, 26.4, 49.0, 3.5, 2.6, -22)
    head(d, 38.6, 48.0, 3.5, 2.6, -22)
    rect(d, 28.6, 25.5, 30.2, 48.6)
    rect(d, 40.8, 21.5, 42.4, 47.6)
    poly(d, [(28.6, 24.0), (42.4, 20.0), (42.4, 23.6), (28.6, 27.6)])
    return m.resize((SIZE, SIZE), Image.LANCZOS)


def added_note():
    """The added note, top-right, in the contrast colour: a single eighth note like his first."""
    m = Image.new("L", (SIZE * SS, SIZE * SS), 0)
    d = ImageDraw.Draw(m)
    head(d, 46.2, 29.5, 2.9, 2.1, -22)
    rect(d, 47.6, 11.0, 49.1, 29.5)
    eighth_flag(d, 48.9, 11.0, 8.0, 5.6)
    return m.resize((SIZE, SIZE), Image.LANCZOS)


def ramp(stops, y):
    if y <= stops[0][0]:
        return stops[0][1]
    for (y0, c0), (y1, c1) in itertools.pairwise(stops):
        if y <= y1:
            t = (y - y0) / (y1 - y0)
            t = t * t * (3 - 2 * t)  # smoothstep: no visible band edges
            return tuple(round(a + (b - a) * t) for a, b in zip(c0, c1))
    return stops[-1][1]


def paint(base, cover, stops):
    """Engrave a note: a soft shadow down-right, then the gradient fill with a light top edge and a darker rim."""
    out = base.copy()
    px, cv = out.load(), cover.load()
    shadow = cover.filter(ImageFilter.GaussianBlur(0.6))
    sh = shadow.load()
    for y in range(SIZE):
        for x in range(SIZE):
            sx, sy = x - 1, y - 1
            a = sh[sx, sy] / 255 * 0.55 if sx >= 0 and sy >= 0 else 0
            if a:
                r, g, b, al = px[x, y]
                px[x, y] = (round(r * (1 - a)), round(g * (1 - a)), round(b * (1 - a)), al)
    for y in range(SIZE):
        for x in range(SIZE):
            a = cv[x, y] / 255
            if a <= 0.02:
                continue
            r, g, b = ramp(stops, y)
            above = cv[x, y - 1] / 255 if y > 0 else 0
            below = cv[x, y + 1] / 255 if y < SIZE - 1 else 0
            if above < 0.5:  # top edge catches the light
                r, g, b = (min(255, round(c * 1.18 + 18)) for c in (r, g, b))
            elif below < 0.5:  # lower rim sits in the groove
                r, g, b = (round(c * 0.8) for c in (r, g, b))
            br, bg, bb, al = px[x, y]
            px[x, y] = (round(br + (r - br) * a), round(bg + (g - bg) * a), round(bb + (b - bb) * a), al)
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("submission", help="the submission's texture.bytes (64x64 RGBA, rows bottom-up)")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--preview", type=Path, help="also write an 8x PNG preview")
    args = parser.parse_args()

    tile = load_tile(args.submission)
    stone = fill_stone(tile, note_mask(tile))
    result = paint(stone, his_notes(), GRADIENT)
    result = paint(result, added_note(), CONTRAST)

    args.out.write_bytes(result.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    print("wrote", args.out)
    if args.preview:
        result.resize((SIZE * 8, SIZE * 8), Image.NEAREST).save(args.preview)
        print("wrote", args.preview)


if __name__ == "__main__":
    main()
