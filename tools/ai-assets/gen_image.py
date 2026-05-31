"""Generate ONE image/texture from a text prompt via OpenAI's image API.

Design: **one file per run**. This matches the project's cost discipline — assets are
generated individually so each request's cost is visible and approved beforehand, never in
a silent batch. Reads ``OPENAI_API_KEY`` from the environment / a local ``.env``; prints an
estimated cost before calling.

Cheapest small textures: use ``--model gpt-image-1-mini --quality low`` (the API's smallest
output is 1024x1024) and ``--downscale 32`` (or 64/256) with ``--nearest`` to shrink the
result to a crisp pixel-art texture locally.

Usage:
    uv run gen_image.py --prompt "seamless 32px stone block texture, top-down, grey, pixel art" \
        --out out/stone.png --downscale 32 --nearest
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

# Approximate USD per generated 1024x1024 image (verify at https://openai.com/api/pricing).
# Non-square sizes cost roughly 1.5x. gpt-image-1-mini/low is the cheap floor (~$0.005).
COST_USD = {
    ("gpt-image-1-mini", "low"): 0.005,
    ("gpt-image-1-mini", "medium"): 0.02,
    ("gpt-image-1-mini", "high"): 0.07,
    ("gpt-image-2", "low"): 0.006,
    ("gpt-image-2", "medium"): 0.053,
    ("gpt-image-2", "high"): 0.211,
}


def estimate(model: str, quality: str, size: str) -> float | None:
    base = COST_USD.get((model, quality))
    if base is None:
        return None
    return base * (1.0 if size == "1024x1024" else 1.5)


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate one image/texture via OpenAI (one file per run).")
    ap.add_argument("--prompt", required=True, help="text description of the image/texture")
    ap.add_argument("--out", required=True, help="output PNG path")
    ap.add_argument("--model", default="gpt-image-1-mini", help="image model (gpt-image-1-mini is cheapest)")
    ap.add_argument("--quality", default="low", choices=["low", "medium", "high"])
    ap.add_argument("--size", default="1024x1024", choices=["1024x1024", "1024x1536", "1536x1024"])
    ap.add_argument("--downscale", type=int, default=0,
                    help="resize the result to NxN px (e.g. 32, 64, 256); 0 = keep full size")
    ap.add_argument("--nearest", action="store_true",
                    help="nearest-neighbour downscale (crisp pixel-art textures); default is smooth")
    args = ap.parse_args()

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set (put it in .env or the environment).")

    est = estimate(args.model, args.quality, args.size)
    if est is not None:
        print(f"[cost] ~${est:.4f} for 1 image ({args.model}, {args.quality}, {args.size})")
    else:
        print(f"[cost] unknown for {args.model}/{args.quality} — check https://openai.com/api/pricing")

    from openai import OpenAI

    client = OpenAI(api_key=key)
    resp = client.images.generate(
        model=args.model,
        prompt=args.prompt,
        size=args.size,
        quality=args.quality,
        n=1,
    )
    raw = base64.b64decode(resp.data[0].b64_json)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    if args.downscale > 0:
        from io import BytesIO

        from PIL import Image

        img = Image.open(BytesIO(raw)).convert("RGBA")
        resample = Image.NEAREST if args.nearest else Image.LANCZOS
        img = img.resize((args.downscale, args.downscale), resample)
        img.save(out)
    else:
        out.write_bytes(raw)

    print(f"[ok] wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
