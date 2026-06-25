# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate avatar / gear textures via the OpenAI image API — seamless 64px pixel-art tiles that are
mostly **grayscale so they tint** (the avatar multiplies them by each part's colour, so the player's
chosen / NPC colours still come through). Mirrors gen_textures.py: one API call per texture, resumable
(existing out/avatar/<key>.png skipped), tolerant of single failures.

Bundle the chosen tiles into the client with `bundle_textures.py` (raw RGBA .bytes) afterwards.

Usage:
    uv run gen_avatar.py --only suit     # generate one (the approval test)
    uv run gen_avatar.py                  # generate the whole set (skips existing)
    uv run gen_avatar.py --dry-run
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

OUT = Path("out/avatar")
STYLE = ("seamless tileable texture, mostly grayscale so it can be tinted a colour, retro 16-bit "
         "sci-fi video-game material, top-down flat, no text, no words, no logo")

TEXTURES = [
    ("suit", "woven spacesuit fabric with subtle padded panel seams and stitching"),
    ("armor", "brushed metal sci-fi armour plate with rivets, bevelled edges and panel lines"),
    ("visor", "glossy dark helmet visor glass with a faint pale-cyan reflection sheen"),
    ("skin", "smooth even matte skin surface, very subtle pores"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate avatar/gear textures (grayscale, tintable).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", help="generate just this one key (the approval test)")
    args = ap.parse_args()

    items = [t for t in TEXTURES if args.only is None or t[0] == args.only]
    if not items:
        sys.exit(f"--only '{args.only}' is not a known avatar texture: {[t[0] for t in TEXTURES]}")

    print(f"[avatar] {len(items)} texture(s): {', '.join(k for k, _ in items)}")
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

    print(f"\n[avatar] done. generated={done} skipped={skipped} failed={failed}")


if __name__ == "__main__":
    main()
