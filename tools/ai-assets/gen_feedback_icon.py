# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate candidate HUD icons for the new "Spieler Feedback" (player feedback) button.

Same style/pipeline as gen_icons.py (transparent cyan line icons, 128x128). Produces a few
variants into out/icons so we can pick one; the chosen file is later renamed to btn_feedback.png
and moved into client/Assets/Resources/icons.

Usage:
    uv run gen_feedback_icon.py
    uv run gen_feedback_icon.py --dry-run
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

OUT = Path("out/icons")
STYLE = ("thin clean glowing cyan lines on a fully transparent background, centered, simple, "
         "flat, sci-fi HUD style, no text, no words, no letters")

# Player feedback = both bug reports AND feature requests, no distinction. A speech/chat bubble
# is the clearest universal "tell the developers something" symbol; a few variants to choose from.
ICONS = [
    ("btn_feedback_bubble", "a rounded speech bubble with a small exclamation mark inside"),
    ("btn_feedback_chat", "a rounded speech chat bubble with three horizontal dots inside"),
    ("btn_feedback_pen", "a rounded speech bubble with a pencil writing across it"),
    ("btn_feedback_megaphone", "a megaphone announcing, with small signal arcs"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate candidate player-feedback HUD icons.")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    print(f"[feedback-icon] {len(ICONS)} variants")
    if args.dry_run:
        for i, (sid, desc) in enumerate(ICONS, 1):
            print(f"  {i:2d}. {sid:24s} {desc}")
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
    fails: list[str] = []
    total = len(ICONS)

    for i, (sid, desc) in enumerate(ICONS, 1):
        out = OUT / f"{sid}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{total}] {sid}: skip (exists)")
            continue

        prompt = f"minimal flat UI line icon of {desc}, {STYLE}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024",
                    quality="low", n=1, background="transparent")
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((128, 128), Image.LANCZOS)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{total}] {sid}: ok ({out.stat().st_size} bytes)")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {sid}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            fails.append(sid)

    print(f"\n[feedback-icon] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[feedback-icon] failed: " + ", ".join(fails))


if __name__ == "__main__":
    main()
