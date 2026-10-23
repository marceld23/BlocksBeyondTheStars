# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Bake a hard alpha mask into the torch and lantern billboard tiles (#2520).

Both props render as cutout cross billboards (ChunkMesher's torch path; BlockAtlas clips below alpha 0.5), but their
tiles shipped fully opaque: the torch on a near-black card, the lantern on a beige one — in game a dark (or beige)
rectangle stood on the wall behind every torch, lit grey by the torch's own light. Like bake_leaf_alpha.py for foliage
and bundle_fire.py for the flame, this derives the mask from the tile's own art and leaves RGB untouched (the Sobel
normal atlas is built from RGB):

- torch:   a brightness cut — the card is at most ~18 (Rec.601 luma, 0..255), the stick and flame at least ~44, so
           everything at or below --torch-cutoff (25) becomes transparent.
- lantern: a colour key on the beige ground (220, 173, 117). The ground pixels lie within ~26 of the key (seeds) —
           including the gap inside the handle loop, which no flood fill from the border would reach — and the soft
           edge and a few darker sand specks are grown from those seeds while they stay within 40. Below and beside the
           lantern itself (outside the bounding box of the pixels clearly unlike the ground) the drop shadow is cut up
           to 60, because its colour matches the highlights on the handle and only its place tells them apart.

The tiles are raw 64x64 RGBA32 bytes (rows bottom-up) at client/Assets/Resources/textures/<key>.bytes; they are
rewritten in place, and the mask depends on RGB only, so a second run changes nothing. torch_off / lantern_off have no
tile of their own: the atlas paints them from the lit tile, darkened with the alpha kept (BlockTextureAtlas.DarkenTile).

Usage:
    uv run --no-project --with "pillow>=13" python tools/ai-assets/bake_prop_alpha.py                 # bake both
    uv run --no-project --with "pillow>=13" python tools/ai-assets/bake_prop_alpha.py --dry-run --preview out/preview
"""

from __future__ import annotations

import argparse
import math
from collections import deque
from pathlib import Path

import texture_provenance
from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"

LANTERN_KEY = (220, 173, 117)


def luma(r: int, g: int, b: int) -> float:
    return 0.299 * r + 0.587 * g + 0.114 * b


def key_distance(p: tuple[int, ...], key: tuple[int, int, int]) -> float:
    return math.sqrt(sum((p[i] - key[i]) ** 2 for i in range(3)))


def torch_mask(px, cutoff: float) -> list[list[bool]]:
    """True = transparent: the near-black card around the stick and the flame."""
    return [[luma(*px[x, y][:3]) <= cutoff for x in range(TILE)] for y in range(TILE)]


def lantern_mask(
    px, seed: float, grow: float, shadow: float, solid: float
) -> list[list[bool]]:
    """True = transparent: the beige ground, its soft edge against the lantern and the drop shadow under it."""
    dist = [
        [key_distance(px[x, y], LANTERN_KEY) for x in range(TILE)] for y in range(TILE)
    ]

    # The lantern's own extent: the bounding box of the pixels clearly unlike the ground.
    xs = [x for y in range(TILE) for x in range(TILE) if dist[y][x] > solid]
    ys = [y for y in range(TILE) for x in range(TILE) if dist[y][x] > solid]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)

    clear = [[dist[y][x] <= seed for x in range(TILE)] for y in range(TILE)]
    queue = deque((x, y) for y in range(TILE) for x in range(TILE) if clear[y][x])
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (
                0 <= nx < TILE
                and 0 <= ny < TILE
                and not clear[ny][nx]
                and dist[ny][nx] <= grow
            ):
                clear[ny][nx] = True
                queue.append((nx, ny))

    for y in range(TILE):
        for x in range(TILE):
            outside = x < x0 or x > x1 or y < y0 or y > y1
            if outside and dist[y][x] <= shadow:
                clear[y][x] = True

    # One despeckle pass: a lone edge pixel sticking out of the outline (ground on three sides — an anti-aliasing
    # leftover of the generated art) would stand out as a dot on the billboard's silhouette.
    specks = [
        (x, y)
        for y in range(TILE)
        for x in range(TILE)
        if not clear[y][x]
        and sum(
            1
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))
            if not (0 <= nx < TILE and 0 <= ny < TILE) or clear[ny][nx]
        )
        >= 3
    ]
    for x, y in specks:
        clear[y][x] = True
    return clear


def bake(key: str, args: argparse.Namespace) -> str:
    path = RES / f"{key}.bytes"
    data = path.read_bytes()
    if len(data) != TILE * TILE * 4:
        return (
            f"!!! {key}: unexpected size {len(data)} (want {TILE * TILE * 4}), skipped"
        )

    # Work top-down (the way the art is drawn) and flip back on write.
    img = Image.frombytes("RGBA", (TILE, TILE), data).transpose(Image.FLIP_TOP_BOTTOM)
    px = img.load()
    if key == "torch":
        mask = torch_mask(px, args.torch_cutoff)
    else:
        mask = lantern_mask(
            px,
            args.lantern_seed,
            args.lantern_grow,
            args.lantern_shadow,
            args.lantern_solid,
        )

    transparent = 0
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, _ = px[x, y]
            a = (
                0 if mask[y][x] else 255
            )  # a hard cutout: the shader clips at 0.5 anyway
            px[x, y] = (r, g, b, a)  # RGB untouched → the normal atlas stays clean
            if a == 0:
                transparent += 1

    if args.preview:
        out = Path(args.preview)
        out.mkdir(parents=True, exist_ok=True)
        img.save(out / f"{key}.png")

    if not args.dry_run:
        path.write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())

    verb = "would write" if args.dry_run else "wrote"
    return f"ok  {key}: {transparent}/{TILE * TILE} px transparent ({100 * transparent // (TILE * TILE)}%), {verb} {path.name}"


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Bake the alpha cutout into the torch and lantern tiles (#2520)."
    )
    ap.add_argument(
        "--key",
        action="append",
        choices=["torch", "lantern"],
        help="only this tile (repeatable)",
    )
    ap.add_argument(
        "--torch-cutoff",
        type=float,
        default=25.0,
        help="torch: luma at or below this is the card",
    )
    ap.add_argument(
        "--lantern-seed",
        type=float,
        default=26.0,
        help="lantern: distance to the key colour that is ground",
    )
    ap.add_argument(
        "--lantern-grow",
        type=float,
        default=40.0,
        help="lantern: distance up to which the ground grows",
    )
    ap.add_argument(
        "--lantern-shadow",
        type=float,
        default=60.0,
        help="lantern: distance cut outside the lantern's box",
    )
    ap.add_argument(
        "--lantern-solid",
        type=float,
        default=80.0,
        help="lantern: distance that marks the lantern itself",
    )
    ap.add_argument(
        "--preview", help="also save the masked tiles as PNG into this folder"
    )
    ap.add_argument(
        "--dry-run",
        action="store_true",
        help="do not rewrite the .bytes (use with --preview)",
    )
    ap.add_argument(
        "--force",
        action="store_true",
        help="also bake hand-painted tiles (texture_provenance.json)",
    )
    args = ap.parse_args()

    provenance = texture_provenance.load()
    for key in args.key or ["torch", "lantern"]:
        if texture_provenance.guard(key, args.force, provenance):
            print(bake(key, args))


if __name__ == "__main__":
    main()
