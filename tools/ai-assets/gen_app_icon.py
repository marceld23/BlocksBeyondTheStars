"""Generate the BlocksBeyondTheStars application / .exe icon — drawn procedurally (no API, free).

Produces a 1024x1024 RGBA PNG: a rounded dark-space tile with a planet arc, a faint orbit ring and a
bold rocket in the game's HUD cyan. Bold shapes only, so it stays readable when Windows scales it down
to 16/32/48 px in the taskbar and Explorer. Unity embeds it into BlocksBeyondTheStars.exe at build time (see
BuildScript.EnsureAppIcon). It must live in a normal (non-Editor) asset folder, otherwise the player
build strips it and falls back to the default Unity icon.

Usage:
    uv run gen_app_icon.py
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
OUT = Path(__file__).resolve().parents[2] / "client" / "Assets" / "BlocksBeyondTheStars" / "Icon" / "app_icon.png"

CYAN = (90, 220, 255)
DEEP_TOP = (12, 20, 52)
DEEP_BOTTOM = (4, 7, 22)
PLANET = (38, 70, 120)


def _rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def _gradient(size: int) -> Image.Image:
    grad = Image.new("RGB", (size, size))
    px = grad.load()
    for y in range(size):
        t = y / (size - 1)
        r = int(DEEP_TOP[0] + (DEEP_BOTTOM[0] - DEEP_TOP[0]) * t)
        g = int(DEEP_TOP[1] + (DEEP_BOTTOM[1] - DEEP_TOP[1]) * t)
        b = int(DEEP_TOP[2] + (DEEP_BOTTOM[2] - DEEP_TOP[2]) * t)
        for x in range(size):
            px[x, y] = (r, g, b)
    return grad


def main() -> None:
    s = SIZE
    base = _gradient(s).convert("RGBA")
    d = ImageDraw.Draw(base, "RGBA")

    # Stars — a deterministic scatter (fixed seed list so the icon is reproducible).
    stars = [(120, 160, 5), (300, 90, 3), (820, 140, 6), (910, 320, 4), (180, 380, 4),
             (700, 80, 3), (560, 200, 5), (90, 560, 3), (940, 600, 4), (260, 640, 3)]
    for x, y, rad in stars:
        a = 200 if rad >= 5 else 130
        d.ellipse((x - rad, y - rad, x + rad, y + rad), fill=(235, 245, 255, a))

    # Planet arc rising from the bottom-right, with a cyan rim light.
    pc, pr = (s + 120, s + 180), 560
    d.ellipse((pc[0] - pr, pc[1] - pr, pc[0] + pr, pc[1] + pr), fill=PLANET + (255,))
    d.arc((pc[0] - pr, pc[1] - pr, pc[0] + pr, pc[1] + pr), 180, 290, fill=CYAN + (180,), width=10)

    # Faint orbit ring behind the rocket (drawn on its own layer + blurred for glow).
    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    cx, cy = s // 2, int(s * 0.46)
    gd.ellipse((cx - 340, cy - 150, cx + 340, cy + 150), outline=CYAN + (160,), width=8)
    glow = glow.filter(ImageFilter.GaussianBlur(6))
    base.alpha_composite(glow)

    # Rocket — pointing up, centred. Bold so it survives downscaling.
    body_w = 150
    nose_y, base_y = cy - 300, cy + 230
    body = [
        (cx - body_w, base_y), (cx - body_w, cy - 120),
        (cx, nose_y),                      # nose tip
        (cx + body_w, cy - 120), (cx + body_w, base_y),
    ]
    d.polygon(body, fill=(238, 244, 252, 255))
    # Nose cap (cyan).
    d.polygon([(cx - body_w, cy - 120), (cx, nose_y), (cx + body_w, cy - 120)], fill=CYAN + (255,))
    # Window.
    d.ellipse((cx - 56, cy - 60, cx + 56, cy + 52), fill=(20, 40, 70, 255))
    d.ellipse((cx - 56, cy - 60, cx + 56, cy + 52), outline=CYAN + (255,), width=12)
    # Fins.
    d.polygon([(cx - body_w, cy + 70), (cx - body_w - 110, base_y + 70), (cx - body_w, base_y)], fill=CYAN + (255,))
    d.polygon([(cx + body_w, cy + 70), (cx + body_w + 110, base_y + 70), (cx + body_w, base_y)], fill=CYAN + (255,))

    # Engine flame.
    flame = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    fd = ImageDraw.Draw(flame)
    fd.polygon([(cx - 70, base_y - 6), (cx + 70, base_y - 6), (cx, base_y + 190)], fill=(255, 170, 60, 255))
    fd.polygon([(cx - 38, base_y - 6), (cx + 38, base_y - 6), (cx, base_y + 120)], fill=(255, 232, 150, 255))
    flame = flame.filter(ImageFilter.GaussianBlur(3))
    base.alpha_composite(flame)

    # Thin cyan border + clip to a rounded tile.
    d.rounded_rectangle((6, 6, s - 7, s - 7), radius=176, outline=CYAN + (90,), width=6)
    mask = _rounded_mask(s, 180)
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(base, (0, 0), mask)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    out.save(OUT)
    print(f"Wrote {OUT} ({s}x{s}).")


if __name__ == "__main__":
    main()
