# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate ONE mural picture and cut it into block tiles (#3644: the Grand Technicians' mural).

A mural is a wall picture several blocks wide and high. It is generated once as a single wide image (one OpenAI call,
1536x1024), the middle band of the mural's aspect is cut out, scaled to cols x rows tiles of 64 px and cut into one tile
per block, row-major from the top left: for the 4 x 2 mural `gt_mural_0..3` is the top row left to right and
`gt_mural_4..7` the bottom row. The tiles land in out/textures/<prefix>_<n>.png, ready for

    uv run bundle_textures.py --from-out --only gt_mural_0 gt_mural_1 ... gt_mural_7

Usage (from tools/ai-assets):
    uv run cut_mural.py --generate gt_mural      # one paid call -> out/murals/gt_mural.png, then cuts it
    uv run cut_mural.py gt_mural                 # (re)cut an image that is already in out/murals/ (free)

Like every generator here the call is not reproducible: the committed tiles are the source of truth (README).
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image

TILE = 64
OUT_MURALS = Path("out/murals")
OUT_TILES = Path("out/textures")

# prefix -> (columns, rows, side margin in source pixels, prompt). The margin trims the picture's own frame lines off its
# left and right edges before the band is cut.
MURALS = {
    "gt_mural": (4, 2, 64, (
        "a very wide horizontal ancient wall mural, a painted relief carved into dark sand-toned sandstone, filling the "
        "whole image edge to edge: three happy friendly beings fly playfully side by side in a cheerful wave, one a little "
        "higher, one a little lower, each with a long curved swoosh trail behind it. Each being is a loose swarm of six to "
        "eight separate small dark brown stone blocks with tiny carved glyph symbols, floating apart with clear gaps "
        "between them (not touching, not a ring, not a gear), around ONE single big round glowing bright red eye in its "
        "middle that looks happy. Little carved stars and spirals between them. Every being is completely inside the "
        "picture with plain sandstone margin to the left and right edges, nothing cut off, all figures inside the middle "
        "band of the image. Warm ochre, umber and dark brown with bright red eyes, cute and kid-friendly, not scary, no "
        "text, no letters, flat even lighting, pixel art"
    )),
}


def generate(prefix: str) -> Path:
    """One paid call: writes the full-size picture to out/murals/<prefix>.png."""
    from dotenv import load_dotenv
    from openai import OpenAI

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set (put it in .env or the environment).")

    print("[cost] ~$0.03 for 1 image (gpt-image-1-mini, medium, 1536x1024)")
    resp = OpenAI(api_key=key).images.generate(
        model="gpt-image-1-mini", prompt=MURALS[prefix][3], size="1536x1024", quality="medium", n=1, background="opaque")
    OUT_MURALS.mkdir(parents=True, exist_ok=True)
    out = OUT_MURALS / f"{prefix}.png"
    Image.open(BytesIO(base64.b64decode(resp.data[0].b64_json))).convert("RGB").save(out)
    print(f"[ok] wrote {out}")
    return out


def cut(prefix: str) -> None:
    """Cuts out/murals/<prefix>.png into out/textures/<prefix>_<n>.png (opaque 64 px tiles, row-major)."""
    cols, rows, margin, _ = MURALS[prefix]
    img = Image.open(OUT_MURALS / f"{prefix}.png").convert("RGB")
    img = img.crop((margin, 0, img.width - margin, img.height))
    w, h = img.size
    band = round(w * rows / cols)  # the middle band of the mural's aspect (the full width)
    if band > h:
        crop_w = round(h * cols / rows)
        img = img.crop(((w - crop_w) // 2, 0, (w - crop_w) // 2 + crop_w, h))
    else:
        img = img.crop((0, (h - band) // 2, w, (h - band) // 2 + band))
    small = img.resize((cols * TILE, rows * TILE), Image.LANCZOS)
    small.save(OUT_MURALS / f"{prefix}_{TILE}px.png")
    OUT_TILES.mkdir(parents=True, exist_ok=True)
    for r in range(rows):
        for c in range(cols):
            n = r * cols + c
            tile = small.crop((c * TILE, r * TILE, (c + 1) * TILE, (r + 1) * TILE)).convert("RGBA")
            tile.save(OUT_TILES / f"{prefix}_{n}.png")
            print(f"{prefix}_{n}: row {r}, column {c}")


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate a mural picture and cut it into block tiles.")
    ap.add_argument("prefix", choices=sorted(MURALS), help="the mural (its tiles are <prefix>_0 .. <prefix>_<n-1>)")
    ap.add_argument("--generate", action="store_true", help="make the picture first (one paid OpenAI call)")
    args = ap.parse_args()
    if args.generate:
        generate(args.prefix)
    cut(args.prefix)


if __name__ == "__main__":
    main()
