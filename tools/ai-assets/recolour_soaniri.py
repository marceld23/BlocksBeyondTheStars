# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Recolour Soaniri's ground and singing plants into light blue and light green (#3423).

Justus (F1 report 2026-11-08, "Cool aber..."): everything on the Sounders' planet should be light blue and light green. The
moss and the five singing plants were generated violet, magenta and teal (#3363); this script turns them, pixel by pixel,
into the planet's palette: the moss into light green with aqua clumps, the plants' stalks and leaves into light green and
their bells, tips, strands and throats into light blue. The resonance crystal is light blue already and stays as it is.

Deterministic, no model and no randomness. A pixel keeps its alpha, its shading and its place; only its hue moves along a
per-tile map (piecewise-linear on the colour circle, anchors in degrees), its saturation is scaled and its value lifted
toward white by a share. Near-grey pixels (the black between a billboard's leaves, the white of a glow) keep their hue.

The source tiles are read from the git revision that bundled them (SOURCE_REV), never from the working tree, so the
script gives the same tiles however often it runs:

    uv run --no-project --with "pillow>=11" python tools/ai-assets/recolour_soaniri.py [--preview sheet.png]

Writes client/Assets/Resources/textures/<key>.bytes (raw 64x64 RGBA32, rows bottom-up) for every key in TILES.
"""
from __future__ import annotations

import argparse
import colorsys
import subprocess
from pathlib import Path

from PIL import Image

SIZE = 64
REPO = Path(__file__).resolve().parents[2]
TEXTURES = REPO / "client" / "Assets" / "Resources" / "textures"
SOURCE_REV = "73076176"  # feat(sounders) #3383: the commit that bundled the generated Soaniri tiles

# Ground: teal clumps -> light green, violet/blue clumps -> aqua, the yellow sparkles -> pale yellow-green.
GROUND = [(0, 168), (55, 95), (100, 112), (150, 118), (185, 124), (215, 158), (260, 168), (310, 168)]

# Plants: stalks and leaves (green/teal) -> light green, bells/tips/strands/throats (blue/violet/magenta) -> light blue,
# a warm glow (red/orange) -> pale aqua.
PLANT = [(0, 178), (45, 150), (90, 112), (150, 124), (182, 132), (198, 196), (235, 204), (275, 204), (310, 200), (340, 186)]

# key -> (hue map, saturation factor, value lift toward white)
TILES: dict[str, tuple[list[tuple[float, float]], float, float]] = {
    "chime_moss": (GROUND, 0.80, 0.30),
    "chime_reed": (PLANT, 0.85, 0.22),
    "bell_bloom": (PLANT, 1.25, 0.18),  # its lavender bell is pale: more saturation, or it reads grey
    "bell_bloom_open": (PLANT, 0.80, 0.20),
    "harp_vine": (PLANT, 0.85, 0.22),
    "echo_flower": (PLANT, 0.80, 0.22),
}

GREY_SAT = 0.10  # below this saturation a pixel counts as grey and keeps its hue


def hue_map(anchors: list[tuple[float, float]], h: float) -> float:
    """Maps a hue in degrees along the anchors (sorted by source hue; the circle wraps from the last to the first)."""
    n = len(anchors)
    for i in range(n):
        s0, d0 = anchors[i]
        s1, d1 = anchors[(i + 1) % n]
        span = (s1 - s0) % 360.0 or 360.0
        off = (h - s0) % 360.0
        if off <= span:
            step = (d1 - d0 + 180.0) % 360.0 - 180.0  # the short way round
            return (d0 + step * off / span) % 360.0
    return h


def recolour(img: Image.Image, anchors: list[tuple[float, float]], sat_k: float, lift: float) -> Image.Image:
    out = img.copy()
    src, dst = img.load(), out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if s >= GREY_SAT:
                h = hue_map(anchors, h * 360.0) / 360.0
                s = min(1.0, s * sat_k)
            v = v + (1.0 - v) * lift
            nr, ng, nb = colorsys.hsv_to_rgb(h, s, v)
            dst[x, y] = (round(nr * 255.0), round(ng * 255.0), round(nb * 255.0), a)
    return out


def source(key: str) -> Image.Image:
    rel = f"client/Assets/Resources/textures/{key}.bytes"
    raw = subprocess.run(["git", "show", f"{SOURCE_REV}:{rel}"], cwd=REPO, check=True, capture_output=True).stdout
    return Image.frombytes("RGBA", (SIZE, SIZE), raw).transpose(Image.FLIP_TOP_BOTTOM)


def save(img: Image.Image, key: str) -> None:
    (TEXTURES / f"{key}.bytes").write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--preview", help="also write the tiles before (top) and after (bottom) on a grey ground to this PNG")
    args = ap.parse_args()

    pairs = []
    for key, (anchors, sat_k, lift) in TILES.items():
        before = source(key)
        after = recolour(before, anchors, sat_k, lift)
        save(after, key)
        pairs.append((before, after))
        print(f"{key}.bytes")

    if args.preview:
        scale = 4
        cell = SIZE * scale
        sheet = Image.new("RGBA", (len(pairs) * (cell + 8), 2 * cell + 8), (60, 64, 72, 255))
        for i, (before, after) in enumerate(pairs):
            for row, img in enumerate((before, after)):
                sheet.alpha_composite(img.resize((cell, cell), Image.NEAREST), (i * (cell + 8), row * (cell + 8)))
        sheet.save(args.preview)
        print(f"preview -> {args.preview}")


if __name__ == "__main__":
    main()
