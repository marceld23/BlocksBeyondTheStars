"""Procedural white line icons for the HUD vitals (health, oxygen, energy, hunger, hull, shield) and a
few HUD glyphs — drawn with Pillow at 4x and downsampled, so no paid asset generation is involved.

Output: client/Assets/Resources/icons/vital_<key>.png (+ a Unity .meta cloned from map_waypoint.png.meta
with a fresh GUID). White ink on transparent, 64x64, single stroke weight — tinted by the UI at runtime.

Run:  uv run --with pillow tools/ai-assets/gen_hud_icons.py
"""
from __future__ import annotations

import math
import re
import uuid
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "client" / "Assets" / "Resources" / "icons"
TEMPLATE_META = OUT / "map_waypoint.png.meta"

SIZE = 64
SS = 4  # supersampling
W = SIZE * SS
STROKE = int(4.5 * SS)  # ~4.5 px at 64 px


def canvas():
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def save(img: Image.Image, name: str) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    small = img.resize((SIZE, SIZE), Image.LANCZOS)
    path = OUT / f"{name}.png"
    small.save(path)
    meta = path.with_suffix(".png.meta")
    if not meta.exists():
        text = TEMPLATE_META.read_text(encoding="utf-8")
        text = re.sub(r"guid: [0-9a-f]{32}", f"guid: {uuid.uuid4().hex}", text, count=1)
        meta.write_text(text, encoding="utf-8", newline="\n")
    print("wrote", path.relative_to(ROOT))


def line(d, pts, width=STROKE):
    d.line(pts, fill=(255, 255, 255, 255), width=width, joint="curve")
    r = width / 2
    for x, y in pts:  # round caps
        d.ellipse((x - r, y - r, x + r, y + r), fill=(255, 255, 255, 255))


def health():
    img, d = canvas()
    c = W / 2
    arm = W * 0.34
    thick = W * 0.105
    # plus sign as two rounded rectangles
    d.rounded_rectangle((c - thick, c - arm, c + thick, c + arm), radius=thick * 0.45, fill=(255, 255, 255, 255))
    d.rounded_rectangle((c - arm, c - thick, c + arm, c + thick), radius=thick * 0.45, fill=(255, 255, 255, 255))
    save(img, "vital_health")


def oxygen():
    img, d = canvas()
    # two bubbles: a big ring and a small one at the upper right
    c = W * 0.44
    r = W * 0.27
    d.ellipse((c - r, c - r, c + r, c + r), outline=(255, 255, 255, 255), width=STROKE)
    c2x, c2y, r2 = W * 0.78, W * 0.26, W * 0.10
    d.ellipse((c2x - r2, c2y - r2, c2x + r2, c2y + r2), outline=(255, 255, 255, 255), width=int(STROKE * 0.8))
    # highlight arc inside the big bubble
    d.arc((c - r * 0.62, c - r * 0.62, c + r * 0.62, c + r * 0.62), start=200, end=250, fill=(255, 255, 255, 255), width=int(STROKE * 0.7))
    save(img, "vital_oxygen")


def energy():
    img, d = canvas()
    # lightning bolt polygon
    pts = [
        (W * 0.58, W * 0.08), (W * 0.30, W * 0.55), (W * 0.48, W * 0.55),
        (W * 0.40, W * 0.92), (W * 0.72, W * 0.42), (W * 0.54, W * 0.42),
    ]
    d.polygon(pts, fill=(255, 255, 255, 255))
    save(img, "vital_energy")


def hunger():
    img, d = canvas()
    # a bowl with steam: half-disc + rim + two steam curls
    cx, cy, r = W * 0.5, W * 0.60, W * 0.30
    d.pieslice((cx - r, cy - r, cx + r, cy + r), start=0, end=180, fill=(255, 255, 255, 255))
    d.rounded_rectangle((cx - r * 1.05, cy - STROKE * 0.6, cx + r * 1.05, cy + STROKE * 0.6), radius=STROKE * 0.6, fill=(255, 255, 255, 255))
    # foot
    d.rounded_rectangle((cx - r * 0.45, cy + r * 0.92, cx + r * 0.45, cy + r * 1.08), radius=STROKE * 0.4, fill=(255, 255, 255, 255))
    for sx in (-0.14, 0.14):
        x = cx + W * sx
        line(d, [(x, cy - r * 0.55), (x + W * 0.04, cy - r * 0.85), (x - W * 0.02, cy - r * 1.15)], width=int(STROKE * 0.7))
    save(img, "vital_hunger")


def hull():
    img, d = canvas()
    # ship silhouette: a pointed hull with two stubby wings (line art)
    cx = W / 2
    body = [(cx, W * 0.08), (cx + W * 0.16, W * 0.40), (cx + W * 0.16, W * 0.78), (cx - W * 0.16, W * 0.78), (cx - W * 0.16, W * 0.40)]
    d.polygon(body, outline=(255, 255, 255, 255), width=STROKE)
    # wings
    d.polygon([(cx + W * 0.16, W * 0.50), (cx + W * 0.40, W * 0.72), (cx + W * 0.16, W * 0.72)], fill=(255, 255, 255, 255))
    d.polygon([(cx - W * 0.16, W * 0.50), (cx - W * 0.40, W * 0.72), (cx - W * 0.16, W * 0.72)], fill=(255, 255, 255, 255))
    # exhaust
    d.rounded_rectangle((cx - W * 0.08, W * 0.82, cx + W * 0.08, W * 0.92), radius=STROKE * 0.5, fill=(255, 255, 255, 255))
    save(img, "vital_hull")


def shield():
    img, d = canvas()
    cx = W / 2
    top, bottom = W * 0.10, W * 0.92
    pts = [(cx, top), (cx + W * 0.34, W * 0.22), (cx + W * 0.30, W * 0.56), (cx, bottom), (cx - W * 0.30, W * 0.56), (cx - W * 0.34, W * 0.22)]
    d.polygon(pts, outline=(255, 255, 255, 255), width=STROKE)
    # inner tick
    line(d, [(cx - W * 0.12, W * 0.50), (cx - W * 0.02, W * 0.62), (cx + W * 0.15, W * 0.38)], width=int(STROKE * 0.85))
    save(img, "vital_shield")


def exposure():
    img, d = canvas()
    # thermometer (2026-09, the exposure meter on Titas): a tube with a bulb and three scale ticks
    cx = W * 0.44
    tube_w = W * 0.13
    d.rounded_rectangle((cx - tube_w, W * 0.08, cx + tube_w, W * 0.66), radius=tube_w, outline=(255, 255, 255, 255), width=STROKE)
    r = W * 0.19
    by = W * 0.74
    d.ellipse((cx - r, by - r, cx + r, by + r), fill=(255, 255, 255, 255))
    d.rectangle((cx - tube_w * 0.35, W * 0.36, cx + tube_w * 0.35, by), fill=(255, 255, 255, 255))
    for y in (0.20, 0.34, 0.48):
        line(d, [(W * 0.66, W * y), (W * 0.80, W * y)], width=int(STROKE * 0.8))
    save(img, "vital_exposure")


def compass_n():
    img, d = canvas()
    # small diamond needle for the compass N marker backing
    cx, cy = W / 2, W / 2
    d.polygon([(cx, W * 0.10), (cx + W * 0.18, cy), (cx, W * 0.90), (cx - W * 0.18, cy)], fill=(255, 255, 255, 255))
    save(img, "hud_needle")


def crosshair_dot():
    img, d = canvas()
    cx = W / 2
    r = W * 0.10
    d.ellipse((cx - r, cx - r, cx + r, cx + r), fill=(255, 255, 255, 255))
    save(img, "hud_dot")


# ---- #2239: the planet overview card's row icons (air/temperature/water/structures reuse the icons above) ----

def ov_gravity():
    img, d = canvas()
    # a ball resting on the ground with an arrow pulling it down
    cx = W / 2
    r = W * 0.13
    d.ellipse((cx - r, W * 0.52 - r, cx + r, W * 0.52 + r), fill=(255, 255, 255, 255))
    line(d, [(cx, W * 0.08), (cx, W * 0.30)])
    line(d, [(cx - W * 0.10, W * 0.20), (cx, W * 0.30), (cx + W * 0.10, W * 0.20)])
    line(d, [(W * 0.16, W * 0.80), (W * 0.84, W * 0.80)])
    save(img, "ov_gravity")


def ov_weather():
    img, d = canvas()
    # a cloud with three rain streaks
    d.ellipse((W * 0.14, W * 0.30, W * 0.46, W * 0.58), fill=(255, 255, 255, 255))
    d.ellipse((W * 0.32, W * 0.18, W * 0.70, W * 0.56), fill=(255, 255, 255, 255))
    d.ellipse((W * 0.56, W * 0.32, W * 0.86, W * 0.58), fill=(255, 255, 255, 255))
    d.rounded_rectangle((W * 0.22, W * 0.44, W * 0.80, W * 0.58), radius=W * 0.06, fill=(255, 255, 255, 255))
    for x in (0.32, 0.50, 0.68):
        line(d, [(W * x, W * 0.68), (W * (x - 0.06), W * 0.88)], width=int(STROKE * 0.8))
    save(img, "ov_weather")


def ov_lava():
    img, d = canvas()
    # a drop of molten rock with a bubble
    cx = W / 2
    d.polygon([(cx, W * 0.08), (cx + W * 0.26, W * 0.52), (cx - W * 0.26, W * 0.52)], fill=(255, 255, 255, 255))
    r = W * 0.27
    d.ellipse((cx - r, W * 0.64 - r, cx + r, W * 0.64 + r), fill=(255, 255, 255, 255))
    rb = W * 0.07
    d.ellipse((cx + W * 0.04 - rb, W * 0.62 - rb, cx + W * 0.04 + rb, W * 0.62 + rb), fill=(0, 0, 0, 0))
    save(img, "ov_lava")


def ov_plants():
    img, d = canvas()
    # a sprout: stem and two leaves
    cx = W / 2
    line(d, [(cx, W * 0.90), (cx, W * 0.40)])
    d.ellipse((cx - W * 0.36, W * 0.30, cx - W * 0.02, W * 0.52), outline=(255, 255, 255, 255), width=STROKE)
    d.ellipse((cx + W * 0.02, W * 0.14, cx + W * 0.36, W * 0.38), outline=(255, 255, 255, 255), width=STROKE)
    save(img, "ov_plants")


def ov_animals():
    img, d = canvas()
    # a paw print: one pad and four toes
    d.ellipse((W * 0.28, W * 0.48, W * 0.72, W * 0.86), fill=(255, 255, 255, 255))
    for x, y in ((0.20, 0.36), (0.38, 0.20), (0.62, 0.20), (0.80, 0.36)):
        r = W * 0.09
        d.ellipse((W * x - r, W * y - r, W * x + r, W * y + r), fill=(255, 255, 255, 255))
    save(img, "ov_animals")


def ov_machines():
    img, d = canvas()
    # a cog wheel
    cx = cy = W / 2
    for i in range(8):
        a = i * math.pi / 4
        x = cx + math.cos(a) * W * 0.34
        y = cy + math.sin(a) * W * 0.34
        d.rounded_rectangle((x - W * 0.07, y - W * 0.07, x + W * 0.07, y + W * 0.07), radius=W * 0.02, fill=(255, 255, 255, 255))
    r = W * 0.28
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=(255, 255, 255, 255), width=int(STROKE * 1.4))
    ri = W * 0.08
    d.ellipse((cx - ri, cy - ri, cx + ri, cy + ri), fill=(255, 255, 255, 255))
    save(img, "ov_machines")


def ov_terrain():
    img, d = canvas()
    # two mountain peaks
    line(d, [(W * 0.08, W * 0.82), (W * 0.36, W * 0.30), (W * 0.54, W * 0.60), (W * 0.68, W * 0.40), (W * 0.92, W * 0.82)])
    line(d, [(W * 0.08, W * 0.82), (W * 0.92, W * 0.82)])
    save(img, "ov_terrain")


if __name__ == "__main__":
    import sys as _sys

    only = set(_sys.argv[1:])  # e.g. `exposure` — regenerate just the named icons
    for fn in (health, oxygen, energy, hunger, hull, shield, exposure, compass_n, crosshair_dot,
               ov_gravity, ov_weather, ov_lava, ov_plants, ov_animals, ov_machines, ov_terrain):
        if not only or fn.__name__ in only:
            fn()
