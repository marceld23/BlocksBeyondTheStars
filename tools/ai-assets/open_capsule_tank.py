# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Make the opened capsule tank tile from the sealed one (#3912).

A guard post's capsule tank is two `capsule_tank` cells; the hacking chip swaps them for `capsule_tank_open`. The two
tiles must show the SAME frame, or the whole tank would change its metal the moment it opens — and two separate
generations never agree on a frame. So only `capsule_tank` is generated (gen_textures.py); this script takes its window
out and switches its warning light off.

Deterministic, no model and no randomness:
- the window is the connected pale region around the tile's middle (the frosted glass). It becomes a dark hollow with an
  inner shadow under the top bar and along the walls;
- the pale rim the glass leaves on the frame (its anti-aliased edge) becomes a dark gasket;
- the red and yellow pixels of the lamp in the top bar become dark glass.
Everything else — every pixel of the frame — stays as it is.

    uv run --no-project --with "pillow>=13" python tools/ai-assets/open_capsule_tank.py [--preview sheet.png]

Reads client/Assets/Resources/textures/capsule_tank.bytes and writes capsule_tank_open.bytes next to it (raw 64x64
RGBA32, rows bottom-up, opaque).
"""
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

SIZE = 64
REPO = Path(__file__).resolve().parents[2]
TEXTURES = REPO / "client" / "Assets" / "Resources" / "textures"
SEALED = TEXTURES / "capsule_tank.bytes"
OPEN = TEXTURES / "capsule_tank_open.bytes"

GLASS_LUMA = 150.0  # brighter than this, and connected to the middle: the frosted glass
RIM_LUMA = 70.0     # brighter than this, and touching the window: the glass's pale edge on the frame
RIM_PASSES = 2
GASKET = (20, 23, 28)


def luma(p: tuple[int, ...]) -> float:
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def window_of(src: Image.Image) -> set[tuple[int, int]]:
    """The glass: a flood fill from the tile's middle over the pale pixels."""
    px = src.load()
    seen: set[tuple[int, int]] = set()
    stack = [(SIZE // 2, SIZE // 2)]
    while stack:
        x, y = stack.pop()
        if (x, y) in seen or not (0 <= x < SIZE and 0 <= y < SIZE) or luma(px[x, y]) < GLASS_LUMA:
            continue
        seen.add((x, y))
        stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    return seen


def open_tank(src: Image.Image) -> Image.Image:
    out = src.copy()
    s, d = src.load(), out.load()
    window = window_of(src)
    if len(window) < SIZE * SIZE // 8:
        raise SystemExit("capsule_tank has no pale window around its middle — was the tile re-rolled into another layout?")
    xs = [x for x, _ in window]
    ys = [y for _, y in window]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)

    for x, y in window:
        depth = (y - y0) / max(1, y1 - y0)                    # 0 under the top bar, 1 at the floor
        side = min(x - x0, x1 - x) / max(1.0, (x1 - x0) / 2)  # 0 at a wall, 1 in the middle
        v = 9.0 + 12.0 * depth * (0.5 + 0.5 * side) + (luma(s[x, y]) - 225.0) * 0.06  # a trace of the glass's grain
        v = max(4.0, min(40.0, v))
        d[x, y] = (round(v * 0.82), round(v * 0.92), round(v * 1.08), 255)

    edge = set(window)
    for _ in range(RIM_PASSES):
        grown: set[tuple[int, int]] = set()
        for x, y in edge:
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    n = (x + dx, y + dy)
                    if n in edge or n in grown or not (0 <= n[0] < SIZE and 0 <= n[1] < SIZE):
                        continue
                    if luma(s[n]) > RIM_LUMA:
                        d[n] = (*GASKET, 255)
                        grown.add(n)
        edge |= grown

    for y in range(0, y0):  # the lamp sits in the top bar
        for x in range(SIZE):
            r, g, b, _ = s[x, y]
            if r - b > 30 and r > 60:
                k = luma((r, g, b)) / 255.0
                d[x, y] = (round(26 + 22 * k), round(28 + 22 * k), round(32 + 24 * k), 255)
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--preview", help="also write the sealed and the opened tile side by side (4x) to this PNG")
    args = ap.parse_args()

    sealed = Image.frombytes("RGBA", (SIZE, SIZE), SEALED.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)
    opened = open_tank(sealed)
    opened.putalpha(255)
    OPEN.write_bytes(opened.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    print(OPEN.name)

    if args.preview:
        sheet = Image.new("RGBA", (SIZE * 8, SIZE * 4), (60, 64, 72, 255))
        for i, img in enumerate((sealed, opened)):
            sheet.paste(img.resize((SIZE * 4, SIZE * 4), Image.NEAREST), (i * SIZE * 4, 0))
        sheet.save(args.preview)
        print(f"preview -> {args.preview}")


if __name__ == "__main__":
    main()
