"""Bake an alpha-cutout mask into the bundled foliage block tiles (B6 — transparent leaves).

The block atlas is already RGBA32 and preserves a tile's alpha, but the foliage tiles ship fully
opaque, so tree crowns + leafy plants render as solid cubes. This punches real, leaf-shaped holes by
deriving an alpha mask from each tile's OWN art: the darkest ~third of pixels (the shadowed gaps
between leaves/petals) become transparent, the lit leaf/flower body stays opaque. RGB is left
untouched so the Sobel normal atlas (derived from RGB luminance) stays clean — only alpha changes.

The tiles live as raw 64x64 RGBA32 bytes (bottom-up flipped) at
client/Assets/Resources/textures/<key>.bytes; we read, set alpha, write back in place (surgical — no
other tile is touched, no re-bundle of the whole set). The shader clips on this alpha for leaf-flagged
faces (see BlockAtlas.shader / ChunkMesher.IsFoliageBlock — keep the foliage set in sync with this).

Usage:
    uv run python bake_leaf_alpha.py            # bake all foliage tiles
    uv run python bake_leaf_alpha.py --hole 0.30 --key tree_leaves
"""
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = Path("out/textures")

# Foliage = tree crowns + leafy plants. MUST match ChunkMesher.IsFoliageBlock: tree_leaves + every
# flora_* EXCEPT the structural/solid + glowing-cap ones (those read better as solid cubes).
FOLIAGE = [
    "tree_leaves",
    "flora_ashweed", "flora_bellflower", "flora_bush", "flora_coral", "flora_dryshrub",
    "flora_fern", "flora_flower", "flora_frostflower", "flora_kelp", "flora_lichen", "flora_lily",
    "flora_moss", "flora_orchid", "flora_palm", "flora_plant", "flora_reed", "flora_seagrass",
    "flora_thornbush", "flora_vine",
]


def leafiness(r: int, g: int, b: int) -> float:
    """How 'leaf/flower body' a pixel is (high) vs a shadowed gap (low): bright + colourful = body."""
    lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    mx, mn = max(r, g, b) / 255.0, min(r, g, b) / 255.0
    sat = 0.0 if mx <= 1e-4 else (mx - mn) / mx
    return lum * 0.75 + sat * 0.25


def bake(key: str, hole: float, band: float) -> str:
    path = RES / f"{key}.bytes"
    if not path.exists():
        return f"--- {key}: no bundled tile, skipped"

    data = path.read_bytes()
    if len(data) != TILE * TILE * 4:
        return f"!!! {key}: unexpected size {len(data)} (want {TILE*TILE*4}), skipped"

    img = Image.frombytes("RGBA", (TILE, TILE), data)  # raw layout preserved (we don't re-flip)
    px = list(img.getdata())

    scores = sorted(leafiness(r, g, b) for r, g, b, _ in px)
    cutoff = scores[min(len(scores) - 1, int(len(scores) * hole))]  # darkest `hole` fraction → holes

    out = []
    transparent = 0
    for r, g, b, _ in px:
        s = leafiness(r, g, b)
        # Soft step around the cutoff for a slightly anti-aliased edge; near-binary so the cutout stays crisp.
        a = 0.0 if s <= cutoff - band else (1.0 if s >= cutoff + band else (s - (cutoff - band)) / (2 * band))
        ai = int(round(a * 255))
        if ai < 128:
            transparent += 1
        out.append((r, g, b, ai))  # RGB untouched → normal atlas stays clean

    img.putdata(out)
    path.write_bytes(img.tobytes())

    OUT.mkdir(parents=True, exist_ok=True)  # also drop a viewable PNG for the record (un-flip for normal viewing)
    img.transpose(Image.FLIP_TOP_BOTTOM).save(OUT / f"{key}.png")

    return f"ok  {key}: {transparent}/{len(px)} px transparent ({100*transparent/len(px):.0f}% holes)"


def main() -> None:
    ap = argparse.ArgumentParser(description="Bake alpha-cutout holes into foliage block tiles.")
    ap.add_argument("--hole", type=float, default=0.34, help="target transparent fraction per tile (0..1)")
    ap.add_argument("--band", type=float, default=0.05, help="soft-edge half-width around the cutoff")
    ap.add_argument("--key", action="append", help="only bake this key (repeatable); default = all foliage")
    args = ap.parse_args()

    keys = args.key if args.key else FOLIAGE
    for k in keys:
        print(bake(k, args.hole, args.band))


if __name__ == "__main__":
    main()
