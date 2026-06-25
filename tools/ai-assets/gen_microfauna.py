# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate micro-fauna ("Kleinstlebewesen") sprites via the OpenAI image API — tiny pixel-art critters on a
**transparent** background, mostly light grey so they tint per-instance (the client multiplies each sprite by a
species/biome colour, the same trick gen_creatures.py uses for hides). One 64px sprite per archetype.

These are billboard sprites (NOT seamless tiles): butterflies, moths, fireflies, beetles, worms, small fish,
glow-worms, etc. The client assembles them into one atlas at runtime (MicroFaunaAtlas) and falls back to a
procedurally-painted silhouette for any sprite that isn't bundled — so this art is an upgrade, not a hard dep.

Bundle the chosen sprites into the client with `bundle_textures.py --microfauna` afterwards.

Usage:
    uv run gen_microfauna.py --only butterfly   # generate one (the approval test)
    uv run gen_microfauna.py                      # generate the whole set (skips existing)
    uv run gen_microfauna.py --dry-run
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

OUT = Path("out/microfauna")
STYLE = ("single tiny retro 16-bit pixel-art creature sprite, centered, fully transparent background, "
         "mostly light grey / pale so it can be tinted a colour, soft simple shading, no text, no words, "
         "no border, no shadow on the ground")

TEXTURES = [
    ("butterfly", "a butterfly seen from above with two pairs of broad open wings"),
    ("moth", "a fuzzy moth seen from above with rounded wings"),
    ("firefly", "a small glowing firefly with a bright softly glowing abdomen"),
    ("fly", "a tiny housefly with small clear wings"),
    ("bee", "a small fuzzy bumblebee with little wings"),
    ("dragonfly", "a slender dragonfly with four long narrow wings"),
    ("beetle", "a small rounded beetle seen from above with a hard shell"),
    ("ant", "a tiny ant seen from above with three body segments and legs"),
    ("caterpillar", "a small segmented caterpillar seen from the side"),
    ("worm", "a tiny soft earthworm curved on the ground"),
    ("snail", "a tiny snail with a coiled spiral shell seen from the side"),
    ("spider", "a tiny spider seen from above with eight legs"),
    ("fish", "a tiny minnow fish seen from the side with a tail fin"),
    ("tadpole", "a tiny tadpole with a round head and a thin wiggly tail"),
    ("waterbeetle", "a small water beetle seen from above with paddle legs"),
    ("strider", "a tiny water-strider insect with long thin splayed legs"),
    ("glowworm", "a small glowing cave glow-worm larva with a soft luminous body"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate micro-fauna sprites (transparent, tintable).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", help="generate just this one key (the approval test)")
    args = ap.parse_args()

    items = [t for t in TEXTURES if args.only is None or t[0] == args.only]
    if not items:
        sys.exit(f"--only '{args.only}' is not a known micro-fauna sprite: {[t[0] for t in TEXTURES]}")

    print(f"[microfauna] {len(items)} sprite(s): {', '.join(k for k, _ in items)}")
    if args.dry_run:
        for k, d in items:
            print(f"  {k:12s} {d}")
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
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024",
                    quality="low", background="transparent", n=1)
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

    print(f"\n[microfauna] done. generated={done} skipped={skipped} failed={failed}")


if __name__ == "__main__":
    main()
