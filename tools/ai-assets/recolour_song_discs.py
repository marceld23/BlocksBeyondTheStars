# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Recolour the one generated Song Disc icon into the story and socket discs of the Sounders pack (#3363).

The pack has eight named discs (SoundersIds.StoryDiscs plus the calm and light socket discs) and the collectible
`song_disc`. Only the collectible's icon is generated (gen_item_icons.py, key song_disc); every named disc is the same
obsidian disc with its glow in a colour of its own, so a child tells them apart in the inventory at a glance.

Deterministic, no model and no randomness. Only the glow changes: a pixel's weight is how saturated it is (the black
obsidian has almost no colour and stays as it is) times how close its hue lies to the base glow's hue; within that
weight the hue turns to the disc's colour and the saturation/value are scaled. Alpha is untouched.

    uv run --no-project --with "pillow>=13" python tools/ai-assets/recolour_song_discs.py [--preview sheet.png]

Reads client/Assets/Resources/icons/item_song_disc.png and writes item_song_disc_<name>.png next to it.
"""
from __future__ import annotations

import argparse
import colorsys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
ICONS = REPO / "client" / "Assets" / "Resources" / "icons"
BASE = ICONS / "item_song_disc.png"

# The generated disc glows cyan-teal around this hue (degrees, measured on the generated icon).
BASE_HUE = 192.0
HUE_REACH = 60.0  # degrees from BASE_HUE over which a pixel still counts as glow (falls off linearly)

# Disc → (hue in degrees, saturation factor, value factor). The order is the pack's: story discs, then the socket discs.
DISCS: dict[str, tuple[float, float, float]] = {
    "beginning": (48.0, 1.0, 1.05),    # gold — the first letters
    "builders": (28.0, 1.0, 1.0),      # orange — the Resonance Blaster's builders
    "hearing": (320.0, 0.95, 1.05),    # pink — the hearing disc (the socket's own)
    "hunger": (2.0, 0.95, 1.0),        # warm red — the humming recorder of the debris field
    "great_song": (278.0, 1.0, 1.05),  # violet — the Great Song
    "voices": (122.0, 0.95, 1.0),      # green — the Sounder instruments
    "calm": (222.0, 0.9, 1.0),         # deep sky blue — Soundraks stay calm
    "light": (50.0, 0.35, 1.2),        # warm white-gold — a soft glow
}


def glow_weight(h: float, s: float) -> float:
    """0 for the colourless obsidian, 1 for the saturated base glow; hue distance fades it out."""
    sat = min(1.0, max(0.0, (s - 0.12) / 0.25))
    dist = abs((h * 360.0 - BASE_HUE + 180.0) % 360.0 - 180.0)
    near = max(0.0, 1.0 - dist / HUE_REACH)
    return sat * near


def recolour(base: Image.Image, hue: float, sat_k: float, val_k: float) -> Image.Image:
    out = base.copy()
    src, dst = base.load(), out.load()
    w, h = base.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            hh, ss, vv = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            k = glow_weight(hh, ss)
            if k <= 0.0:
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(hue / 360.0, min(1.0, ss * sat_k), min(1.0, vv * val_k))
            dst[x, y] = (
                round(r + k * (nr * 255.0 - r)),
                round(g + k * (ng * 255.0 - g)),
                round(b + k * (nb * 255.0 - b)),
                a,
            )
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--preview", help="also write all discs side by side on a grey ground to this PNG")
    args = ap.parse_args()

    base = Image.open(BASE).convert("RGBA")
    made = []
    for name, (hue, sat_k, val_k) in DISCS.items():
        img = recolour(base, hue, sat_k, val_k)
        path = ICONS / f"item_song_disc_{name}.png"
        img.save(path)
        made.append(img)
        print(f"item_song_disc_{name}.png")

    if args.preview:
        size = base.size[0]
        sheet = Image.new("RGBA", ((len(made) + 1) * size, size), (60, 64, 72, 255))
        for i, img in enumerate([base, *made]):
            sheet.alpha_composite(img, (i * size, 0))
        sheet.save(args.preview)
        print(f"preview -> {args.preview}")


if __name__ == "__main__":
    main()
