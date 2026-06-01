"""Generate the M27 UI icon set (approved batch) via OpenAI images — transparent cyan line icons.

One API call per icon; resumable (existing out/icons/<id>.png are skipped) and tolerant of single
failures. Chosen icons are moved into client/Assets/Resources/icons and logged in NOTICES.md.

Usage:
    uv run gen_icons.py
    uv run gen_icons.py --dry-run
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

ICONS = [
    ("sys_engines", "a rocket engine thruster nozzle with exhaust"),
    ("sys_shields", "a protective shield"),
    ("sys_life", "a heart symbol"),
    ("sys_comms", "an antenna broadcasting radio signal waves"),
    ("sys_nav", "a navigation compass"),
    ("btn_singleplayer", "a planet globe with latitude lines"),
    ("btn_join", "two connected person figures forming a network"),
    ("btn_host", "a server rack with stacked units"),
    ("btn_settings", "a settings gear cog"),
    ("btn_credits", "a five pointed star"),
    ("btn_exit", "a power on off button symbol"),
    ("info_mode", "a 3d cube box"),
    ("info_multiplayer", "a hexagon with three connected dot nodes"),
    ("info_procedural", "a planet with an orbit ring"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the M27 UI icon set (approved batch).")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    print(f"[icons] {len(ICONS)} icons")
    if args.dry_run:
        for i, (sid, desc) in enumerate(ICONS, 1):
            print(f"  {i:2d}. {sid:20s} {desc}")
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

    print(f"\n[icons] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[icons] failed: " + ", ".join(fails))


if __name__ == "__main__":
    main()
