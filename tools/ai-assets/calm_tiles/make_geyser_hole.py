# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 7: the geyser's hole as a decal.

A geyser vent is a block of its own, and its tile brings its own dark ground: a black square in a meadow or a beach.
The game lets the vent wear the tile of the ground around it (ChunkMesher.Vent.cs) and lays only the hole on top.
This script cuts that hole out of the calm vent tile: the pale sinter ring and everything inside it stay, with one
pixel of the dark ground round it as a contact line; the rest becomes transparent (a cut-out,
TextureTiles.AlphaModeOf). Written to client/Assets/Resources/textures/geyser_hole.bytes. Run it after
build_tiles.py --bundle (it reads the calm geyser_vent tile).

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_geyser_hole.py [--dry-run]
"""
import sys
from pathlib import Path

from PIL import Image

TILE = 64
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
TILES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "built"


def main():
    src = Image.frombytes("RGBA", (TILE, TILE), (TILES / "geyser_vent.bytes").read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)
    px = src.load()

    def luma(x, y):
        r, g, b, _ = px[x, y]
        return (r * 299 + g * 587 + b * 114) // 1000

    def enclosed(threshold):
        """Everything the ground can reach from the tile's border without crossing the ring is outside."""
        outside = [[False] * TILE for _ in range(TILE)]
        stack = [(x, y) for x in range(TILE) for y in (0, TILE - 1)] + [(x, y) for y in range(TILE) for x in (0, TILE - 1)]
        while stack:
            x, y = stack.pop()
            if x < 0 or y < 0 or x >= TILE or y >= TILE or outside[y][x] or luma(x, y) >= threshold:
                continue
            outside[y][x] = True
            stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
        return outside

    # The darkest pixel near the middle is the opening. The ground is dark, the sinter ring clearly lighter: the
    # threshold sits halfway between the ground (the median of the tile's border) and the ring (the light end of the
    # tile), so the whole ring with its lobes is kept and the ground's lighter cracks stay outside.
    cx, cy = min(((x, y) for x in range(24, 40) for y in range(22, 40)), key=lambda p: luma(*p))
    border = sorted(luma(x, y) for x in range(TILE) for y in range(TILE) if min(x, y, TILE - 1 - x, TILE - 1 - y) < 6)
    everything = sorted(luma(x, y) for x in range(TILE) for y in range(TILE))
    ground, ring_light = border[len(border) // 2], everything[int(len(everything) * 0.95)]
    threshold = (ground + ring_light) // 2
    assert not enclosed(threshold)[cy][cx], "the ring does not close round the opening"
    outside = enclosed(threshold)

    def bright(x, y):
        return luma(x, y) >= threshold

    # Only the piece that holds the opening: a bright speck of ground elsewhere is no part of the vent.
    piece = [[False] * TILE for _ in range(TILE)]
    stack = [(cx, cy)]
    while stack:
        x, y = stack.pop()
        if x < 0 or y < 0 or x >= TILE or y >= TILE or piece[y][x] or outside[y][x]:
            continue
        piece[y][x] = True
        stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    outside = [[not piece[y][x] for x in range(TILE)] for y in range(TILE)]
    print("ring threshold", threshold, "opening at", (cx, cy))

    inside = [[not outside[y][x] for x in range(TILE)] for y in range(TILE)]
    # one pixel of ground round the ring: a dark contact line that seats the ring on any ground
    keep = [[inside[y][x] or any(0 <= x + dx < TILE and 0 <= y + dy < TILE and inside[y + dy][x + dx]
                                 for dx in (-1, 0, 1) for dy in (-1, 0, 1)) for x in range(TILE)] for y in range(TILE)]

    ring = [px[x, y][:3] for y in range(TILE) for x in range(TILE) if inside[y][x] and bright(x, y)]
    fill = tuple(sum(c[i] for c in ring) // len(ring) for i in range(3))  # transparent texels carry the ring's colour
    out = Image.new("RGBA", (TILE, TILE))
    op = out.load()
    kept = 0
    xs, ys = [], []
    for y in range(TILE):
        for x in range(TILE):
            if keep[y][x]:
                r, g, b, _ = px[x, y]
                if not inside[y][x]:
                    r, g, b = r * 2 // 3, g * 2 // 3, b * 2 // 3  # the contact line, a little darker than the old ground
                op[x, y] = (r, g, b, 255)
                kept += 1
                xs.append(x)
                ys.append(y)
            else:
                op[x, y] = fill + (0,)

    OUT.mkdir(parents=True, exist_ok=True)
    out.save(OUT / "geyser_hole.png")
    print(f"geyser_hole: {kept} of {TILE * TILE} pixels kept, x {min(xs)}..{max(xs)}, y {min(ys)}..{max(ys)}")
    if "--dry-run" not in sys.argv:
        (TILES / "geyser_hole.bytes").write_bytes(out.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
        print("  written to", TILES / "geyser_hole.bytes")

    # A look at it on three grounds.
    sheet = Image.new("RGB", (3 * 200 + 40, 220), (24, 24, 24))
    for i, ground in enumerate(("grass", "sand", "snow")):
        g = Image.frombytes("RGBA", (TILE, TILE), (TILES / f"{ground}.bytes").read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)
        g.alpha_composite(Image.eval(out, lambda v: v))  # the decal as the shader draws it (alpha test: all or nothing)
        sheet.paste(g.convert("RGB").resize((192, 192), Image.NEAREST), (10 + i * 210, 14))
    sheet.save(OUT / "geyser_hole-on-ground.png")


if __name__ == "__main__":
    main()
