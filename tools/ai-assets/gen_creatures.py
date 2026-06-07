"""Generate creature/fauna hide textures via the OpenAI image API — seamless 64px pixel-art tiles that
are mostly **grayscale so they tint** (creatures are procedurally assembled from cubes and coloured per
species, so the texture is multiplied by the species colour). Mirrors gen_avatar.py.

Bundle the chosen tiles into the client with `bundle_textures.py --creatures` afterwards.

Usage:
    uv run gen_creatures.py --only scales   # generate one (the approval test)
    uv run gen_creatures.py                  # generate the whole set (skips existing)
    uv run gen_creatures.py --dry-run
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
import time
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out/creatures")
STYLE = ("seamless tileable texture, mostly grayscale so it can be tinted a colour, retro 16-bit "
         "video-game creature skin, top-down flat, no text, no words")

TEXTURES = [
    ("scales", "reptilian scales, overlapping rounded scales"),
    ("fur", "short animal fur / hide with a soft directional grain"),
    ("chitin", "insectoid chitin shell, hard segmented carapace plates"),
    ("hide", "thick leathery wrinkled animal hide"),
    ("slime", "wet translucent slimy amphibian skin with bumps"),
    ("feathers", "soft layered bird feathers and plumage"),
    ("spots", "animal skin with leopard-like dark spots and rosettes"),
    ("stripes", "animal skin with bold tiger-like stripes"),
    ("warty", "bumpy warty toad amphibian skin with knobbly lumps"),
    ("plated", "armored bony plates and scutes like a crocodile back"),
    ("finned", "sleek wet fish skin with fine shiny scales and a smooth sheen"),
    ("tentacled", "smooth glossy cephalopod octopus skin with soft suckers"),
    # Task 6 — more creature skin variety.
    ("mossy", "shaggy mossy overgrown hide with lichen patches and tufts"),
    ("crystalline", "faceted crystalline mineral skin with angular gem-like plates"),
    ("metallic", "brushed metallic chrome carapace with a hard reflective sheen"),
    ("banded", "skin with bold concentric banded rings like a coral snake"),
    ("shaggy", "long thick shaggy matted fur coat"),
    ("spined", "skin bristling with rows of sharp porcupine quills and spines"),
    ("mottled", "blotchy camouflage mottled skin with irregular dappled patches"),
    ("iridescent", "smooth iridescent beetle shell shimmering with shifting tones"),
    ("barkskin", "rough woody bark-like hide with cracks and ridges"),
    ("veined", "translucent membranous skin with a network of glowing veins"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate creature hide textures (grayscale, tintable).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", help="generate just this one key (the approval test)")
    args = ap.parse_args()

    items = [t for t in TEXTURES if args.only is None or t[0] == args.only]
    if not items:
        sys.exit(f"--only '{args.only}' is not a known creature texture: {[t[0] for t in TEXTURES]}")

    print(f"[creatures] {len(items)} texture(s): {', '.join(k for k, _ in items)}")
    if args.dry_run:
        for k, d in items:
            print(f"  {k:8s} {d}")
        return

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key)
    OUT.mkdir(parents=True, exist_ok=True)

    done = skipped = failed = 0
    for i, (name, desc) in enumerate(items, 1):
        out = OUT / f"{name}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{len(items)}] {name}: skip (exists)")
            continue

        prompt = f"{STYLE}, of {desc}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024", quality="low", n=1)
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((64, 64), Image.NEAREST)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{len(items)}] {name}: ok ({out.stat().st_size} bytes) -> {out}")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{len(items)}] {name}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1

    print(f"\n[creatures] done. generated={done} skipped={skipped} failed={failed}")


if __name__ == "__main__":
    main()
