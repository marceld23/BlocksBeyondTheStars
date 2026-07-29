# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Regenerate the world-map marker icons (issue #592) as BOLD FILLED WHITE pictograms.

The original `gen_icons.py` map set used the shared "thin glowing cyan lines" style — measured
ink coverage was 1–10 %, which is unreadable at the 24–44 px marker sizes, and the cyan ink
multiplied against `Image.color` tints, destroying the marker colour coding (red pads rendered
grey, amber waypoints rendered green).

This script generates the map set in a bold solid-silhouette style and then **post-processes
every opaque pixel to pure white** (alpha preserved), so a uGUI tint renders exactly the marker
colour no matter what hue the model produced. Resumable like gen_icons.py (existing
out/map-icons/<id>.png are skipped); `--install` copies the results into the client Resources.

Usage (from tools/ai-assets, where .env lives):
    uv run gen_map_icons.py
    uv run gen_map_icons.py --dry-run
    uv run gen_map_icons.py --install [--dest <path-to-client/Assets/Resources/icons>]
"""
from __future__ import annotations

import argparse
import base64
import os
import shutil
import sys
import time
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out/map-icons")
DEFAULT_DEST = Path(__file__).resolve().parents[2] / "client" / "Assets" / "Resources" / "icons"

# Bold-silhouette style: the marker must survive a 24 px draw over noisy terrain, so we ask for
# thick filled shapes, not line art. Colour is irrelevant — post-processing forces white ink.
STYLE = ("bold solid filled flat pictogram, thick heavy silhouette shapes, high ink coverage, "
         "white on a fully transparent background, centered, minimal detail, sci-fi map marker "
         "style, no thin lines, no outline-only shapes, no text, no words, no letters")

ICONS = [
    ("map_player", "an upward pointing navigation arrowhead chevron"),
    ("map_ship", "a small spaceship seen from above"),
    ("map_waypoint", "a map waypoint flag on a pin"),
    ("map_beacon", "a radio beacon tower emitting signal arcs"),
    ("map_pad", "a circular landing pad ring with a center dot"),
    ("map_settlement", "a small village hut with a chimney"),
    ("map_ruin", "a broken cracked stone house ruin"),
    ("map_wreck", "a crashed broken spaceship with a bent wing"),
    ("map_station", "an orbital space station hub with a ring"),
    # New (#592): the player-founded base marker — referenced by WorldMap since the Grundstein
    # feature but the icon never existed, so bases fell back to the ⌂ text glyph.
    ("map_base", "a house on a foundation slab with a flag"),
]


def whiten(img):
    """Force every pixel to pure white ink, preserving alpha — guarantees tintability."""
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            a = px[x, y][3]
            px[x, y] = (255, 255, 255, a)
    return img


def coverage(img) -> float:
    """Fraction of pixels with alpha > 128 — the legibility metric from the #592 analysis."""
    data = img.getdata()
    opaque = sum(1 for p in data if p[3] > 128)
    return opaque / (img.width * img.height)


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the bold white world-map marker icons (#592).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--install", action="store_true", help="copy results into the client Resources")
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    args = ap.parse_args()

    print(f"[map-icons] {len(ICONS)} icons")
    if args.dry_run:
        for i, (sid, desc) in enumerate(ICONS, 1):
            print(f"  {i:2d}. {sid:16s} {desc}")
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

        prompt = f"{desc}, {STYLE}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024",
                    quality="low", n=1, background="transparent")
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((128, 128), Image.LANCZOS)
                img = whiten(img)
                cov = coverage(img)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{total}] {sid}: ok ({out.stat().st_size} bytes, ink {cov:.0%})")
                if cov < 0.10:
                    print(f"[{i}/{total}] {sid}: WARNING ink coverage {cov:.0%} < 10% — "
                          "delete the file and re-run to retry")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {sid}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            fails.append(sid)

    print(f"\n[map-icons] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[map-icons] failed: " + ", ".join(fails))

    if args.install and not fails:
        args.dest.mkdir(parents=True, exist_ok=True)
        for sid, _ in ICONS:
            src = OUT / f"{sid}.png"
            if src.exists():
                shutil.copy2(src, args.dest / f"{sid}.png")
                print(f"[map-icons] installed {sid}.png -> {args.dest}")  # ASCII: cp1252 consoles


if __name__ == "__main__":
    main()
