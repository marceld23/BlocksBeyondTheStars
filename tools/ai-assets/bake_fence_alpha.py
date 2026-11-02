# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Bake the see-through gaps into the energy fence's side tile (#3085).

The fence tile is the side of a fence segment (gen_textures.py, key energy_fence): a dark metal rail along the top and
the bottom edge and glowing cyan energy beams running across between them, drawn on a dark blue-black glow. The block
renders as a rigid cutout cube (ChunkMesher: the opaque submesh, BlockAtlas clips below alpha 0.5), so the glow between
the beams must become transparent — that is what makes a row of fence blocks a fence you look through instead of a row
of dark panels. Like bake_prop_alpha.py for the torch and lantern, the mask comes from the tile's own art and RGB stays
untouched (the Sobel normal atlas is built from RGB, and a renderer that does not clip still shows the dark glow):

- The rails are found per ROW: the glow and the beams are pure cyan/blue (red ~0), the gunmetal rails are grey. The
  top rail runs from row 0 down to the last row above the first beam whose mean red is at least --metal-red, the bottom
  rail from the first such row below the last beam to row 63 — the emitter channel inside a rail stays with the rail.
- Between the rails a ROW is cut when its mean Rec.601 luma is below --cutoff (default 40): the dark glow goes, the
  beams and the bright halo rows right around them stay, so each beam keeps a few rows of body for the distant mip
  levels. The beams run straight across, so the mask is decided per row, not per pixel: every row is wholly kept or
  wholly cut, which keeps the gaps lined up with the neighbour block's (no seam, no speckle) and the beams even.

The tile is raw 64x64 RGBA32 bytes (rows bottom-up) at client/Assets/Resources/textures/energy_fence.bytes and is
rewritten in place; the mask depends on RGB only, so a second run changes nothing. Re-generating the tile:

    uv run gen_textures.py --only energy_fence
    uv run python bundle_textures.py --from-out --only energy_fence     # flattens alpha to 255
    uv run python bake_fence_alpha.py                                   # cuts the gaps

Usage:
    uv run --no-project --with "pillow>=13" python tools/ai-assets/bake_fence_alpha.py
    uv run --no-project --with "pillow>=13" python tools/ai-assets/bake_fence_alpha.py --dry-run --preview out/preview
"""

from __future__ import annotations

import argparse
from pathlib import Path

import texture_provenance
from PIL import Image

TILE = 64
KEY = "energy_fence"
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"

BEAM_LUMA = 120.0  # a row this bright on average is a beam (white-hot core or bright cyan)


def luma(p: tuple[int, ...]) -> float:
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def rail_rows(px, metal_red: float) -> tuple[int, int]:
    """(last row of the top rail, first row of the bottom rail), drawn top-down."""
    beams = [y for y in range(TILE) if sum(luma(px[x, y]) for x in range(TILE)) / TILE >= BEAM_LUMA]
    if not beams:
        raise SystemExit(f"{KEY}: no beam row found (no row averages luma >= {BEAM_LUMA}) — is this the fence tile?")

    def metal(y: int) -> bool:
        return sum(px[x, y][0] for x in range(TILE)) / TILE >= metal_red

    top = max((y for y in range(beams[0]) if metal(y)), default=-1)
    bottom = min((y for y in range(beams[-1] + 1, TILE) if metal(y)), default=TILE)
    if top < 0 or bottom >= TILE:
        raise SystemExit(f"{KEY}: no metal rail above the first beam or below the last one (top={top}, bottom={bottom})")
    return top, bottom


def fence_mask(px, cutoff: float, metal_red: float) -> list[list[bool]]:
    """True = transparent: the glow rows between the beams, inside the band between the two rails."""
    top, bottom = rail_rows(px, metal_red)
    cut = [top < y < bottom and sum(luma(px[x, y]) for x in range(TILE)) / TILE < cutoff for y in range(TILE)]
    return [[cut[y]] * TILE for y in range(TILE)]


def bake(args: argparse.Namespace) -> str:
    path = RES / f"{KEY}.bytes"
    data = path.read_bytes()
    if len(data) != TILE * TILE * 4:
        return f"!!! {KEY}: unexpected size {len(data)} (want {TILE * TILE * 4}), skipped"

    # Work top-down (the way the art is drawn) and flip back on write.
    img = Image.frombytes("RGBA", (TILE, TILE), data).transpose(Image.FLIP_TOP_BOTTOM)
    px = img.load()
    mask = fence_mask(px, args.cutoff, args.metal_red)

    transparent = 0
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, _ = px[x, y]
            a = 0 if mask[y][x] else 255  # a hard cutout: the shader clips at 0.5 anyway
            px[x, y] = (r, g, b, a)  # RGB untouched → the normal atlas stays clean
            transparent += a == 0

    if args.preview:
        out = Path(args.preview)
        out.mkdir(parents=True, exist_ok=True)
        img.save(out / f"{KEY}.png")

    if not args.dry_run:
        path.write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())

    verb = "would write" if args.dry_run else "wrote"
    return f"ok  {KEY}: {transparent}/{TILE * TILE} px transparent ({100 * transparent // (TILE * TILE)}%), {verb} {path.name}"


def main() -> None:
    ap = argparse.ArgumentParser(description="Bake the see-through gaps into the energy fence tile (#3085).")
    ap.add_argument("--cutoff", type=float, default=40.0, help="between the rails: a row whose mean luma is below this is cut")
    ap.add_argument("--metal-red", type=float, default=8.0, help="a row whose mean red reaches this is rail metal")
    ap.add_argument("--preview", help="also save the masked tile as PNG into this folder")
    ap.add_argument("--dry-run", action="store_true", help="do not rewrite the .bytes (use with --preview)")
    ap.add_argument("--force", action="store_true", help="also bake a hand-painted tile (texture_provenance.json)")
    args = ap.parse_args()

    if texture_provenance.guard(KEY, args.force, texture_provenance.load()):
        print(bake(args))


if __name__ == "__main__":
    main()
