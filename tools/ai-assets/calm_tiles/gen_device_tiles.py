# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 9 (PAID): material tiles for the device forms. A formed device must not wear a picture of itself, so its
parts get material tiles: dark housing metal, a control deck, a display, embers, tank liquids, a vent grille, a lamp
lens. One generated image per tile (tools/ai-assets/out/calm_tiles/devices/<key>.png, never generated twice), reduced
to 64 px, the repeating ones healed to wrap, then written as raw 64 x 64 RGBA (rows bottom-up) into
client/Assets/Resources/textures. Only embers and the two tank liquids are kept from this step: make_raster_tiles.py
draws the other five by rule afterwards. Image generation costs money and is not reproducible; no test runs this.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/gen_device_tiles.py [--only a,b] [--dry-run] [--no-bundle] [--models]
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
import time

import numpy as np
from PIL import Image, ImageDraw

from make_sheet import crossfade, periodic, seam_ratio
from tilelib import DEVICES, REPO, TEXTURES, TILE, luma

RAW = DEVICES
DEST = TEXTURES
STYLE = ("A flat square game texture seen straight on, in a soft stylised hand-painted look like smooth matte "
         "modelling clay: large simple shapes with soft rounded edges, gentle even shading, very little detail, no "
         "pixel grid, no dithering, no noise, no outlines, no text, no letters, no numbers. The texture fills the "
         "whole image edge to edge with no border, no frame and no background around it. Subject: ")
# key -> (subject, heal: the tile repeats and must wrap, target mean colour or None)
TILES: dict[str, tuple[str, bool, tuple[int, int, int] | None]] = {
    "devform_metal_dark": ("a dark blue-grey gunmetal machine housing plate, almost plain: one large flat metal panel "
                           "with a very soft bevel and four small soft round rivets near the corners.", True,
                           (58, 66, 82)),
    "devform_deck": ("the top of a spaceship control console seen from straight above: a dark gunmetal panel with a "
                     "tidy grid of soft rounded square buttons and two small sliders; most buttons dark grey, a few "
                     "softly lit in cyan and one in amber.", False, None),
    # First try drew a whole monitor with bezel and stand; the form supplies the housing, the tile is the lit surface.
    "devform_screen": ("only the lit surface of a data display, with no monitor, no bezel, no frame and no stand: a "
                       "dark navy blue background that fills the whole image up to every edge, and on it in glowing "
                       "cyan a simple rising line chart in the upper half, three soft horizontal bars of different "
                       "lengths in the lower left and a small grid of soft dots in the lower right, soft glow.",
                       False, None),
    "devform_embers": ("a bed of glowing embers seen from straight above: soft rounded dark coal lumps of different "
                       "sizes with bright orange and yellow glow between them.", True, None),
    "devform_liquid_teal": ("glowing teal liquid behind glass, almost plain: a soft glow that is lighter in the "
                            "middle, a few soft round bubbles of different sizes and one soft vertical highlight "
                            "streak.", True, None),
    "devform_liquid_green": ("bright green algae liquid behind glass, almost plain: a soft glow that is lighter in "
                             "the middle and many soft round bubbles of different sizes.", True, None),
    "devform_vent": ("a dark gunmetal machine ventilation grille: wide horizontal louvre slats with soft rounded "
                     "edges that run across the whole width from the left edge to the right edge, soft dark shadows "
                     "between the slats.", True, (48, 54, 68)),
    "devform_lamp_amber": ("a glowing amber signal lamp lens seen straight on and filling the whole image: warm "
                           "orange-yellow glass with a soft bright centre and soft concentric ridges.", False, None),
}


def small_of(raw: np.ndarray, heal: bool, target: tuple[int, int, int] | None, key: str) -> np.ndarray:
    n = raw.shape[0] // TILE
    a = raw[:n * TILE, :n * TILE].astype(np.float64)
    if heal:
        def ratio(img: np.ndarray) -> float:
            return seam_ratio(luma(img).reshape(TILE, n, TILE, n).mean(axis=(1, 3)))

        before = ratio(a)
        healed = np.stack([periodic(a[..., c] / 255.0) for c in range(3)], axis=-1) * 255.0
        if ratio(healed) > 1.3:
            healed = np.stack([periodic(crossfade(a[..., c] / 255.0)) for c in range(3)], axis=-1) * 255.0
        print(f"{key}: seam {before:.2f} -> {ratio(healed):.2f}")
        a = healed
    small = a.reshape(TILE, n, TILE, n, 3).mean(axis=(1, 3))
    if target is not None:
        small = small + (np.asarray(target, dtype=np.float64) - small.reshape(-1, 3).mean(axis=0))
    return np.clip(small + 0.5, 0, 255).astype(np.uint8)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--only", default=None)
    ap.add_argument("--model", default="gpt-image-2.6-sunburst")
    ap.add_argument("--quality", default="low")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-bundle", action="store_true")
    ap.add_argument("--models", action="store_true", help="only list the image models the key can use")
    args = ap.parse_args()
    keys = [k.strip() for k in args.only.split(",")] if args.only else list(TILES)
    RAW.mkdir(parents=True, exist_ok=True)

    if not args.dry_run:
        from dotenv import load_dotenv
        load_dotenv(REPO / "tools" / "ai-assets" / ".env")
        key = os.environ.get("OPENAI_API_KEY")
        if not key:
            sys.exit("OPENAI_API_KEY is not set.")
        from openai import OpenAI
        client = OpenAI(api_key=key)
        if args.models:
            print(sorted(m.id for m in client.models.list() if "image" in m.id))
            return
        for i, k in enumerate(keys, 1):
            out = RAW / f"{k}.png"
            if out.exists() and out.stat().st_size > 0:
                print(f"[{i}/{len(keys)}] {k}: skip (exists)")
                continue
            try:
                started = time.time()
                resp = client.images.generate(model=args.model, prompt=STYLE + TILES[k][0], size="1024x1024",
                                              quality=args.quality, n=1)
                out.write_bytes(base64.b64decode(resp.data[0].b64_json))
                usage = getattr(resp, "usage", None)
                tokens = f", tokens {usage.input_tokens}+{usage.output_tokens}" if usage else ""
                print(f"[{i}/{len(keys)}] {k}: ok ({time.time() - started:.0f} s{tokens})")
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{len(keys)}] {k}: failed: {exc}")

    cell = 64 * 4
    done = [k for k in keys if (RAW / f"{k}.png").exists()]
    sheet = Image.new("RGB", (max(1, len(done)) * (cell + 8), 2 * cell + 60), (30, 30, 34))
    draw = ImageDraw.Draw(sheet)
    for i, k in enumerate(done):
        raw = np.asarray(Image.open(RAW / f"{k}.png").convert("RGB"))
        _, heal, target = TILES[k]
        small = Image.fromarray(small_of(raw, heal, target, k), "RGB")
        small.save(DEVICES / f"{k}_64.png")
        ox = i * (cell + 8)
        draw.text((ox + 2, 2), k, fill=(255, 255, 255))
        sheet.paste(small.resize((cell, cell), Image.NEAREST), (ox, 20))
        twice = Image.new("RGB", (128, 128))
        for dx in (0, 64):
            for dy in (0, 64):
                twice.paste(small, (dx, dy))
        sheet.paste(twice.resize((cell, cell), Image.NEAREST), (ox, 40 + cell))
        if not args.no_bundle and not args.dry_run:
            DEST.mkdir(parents=True, exist_ok=True)
            data = small.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes()
            assert len(data) == 64 * 64 * 4
            (DEST / f"{k}.bytes").write_bytes(data)
            print(f"{k}: bundled -> {DEST / (k + '.bytes')}")
    sheet.save(DEVICES / "device-tiles.png")
    print("sheet", DEVICES / "device-tiles.png", sheet.size)


if __name__ == "__main__":
    main()
