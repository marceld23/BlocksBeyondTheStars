# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The bio lab's symbol set (#3718): what a sample, a deposit or a preparation carries, as tintable icons.

Two kinds of icon, one look (bold filled white ink on transparent, 128 px — the world-map markers' look, see
gen_map_icons.py):

* **Pictograms** come from the OpenAI image API, one per *theme* (speed, heat, grip …). A theme is shared by an
  effect, a material trait and a tool value where they mean the same thing, so the client needs 20 of them, not 40.
  Every pixel is forced to white (alpha kept) so a uGUI tint gives exactly the family / warning colour, and the
  picture is cropped to its ink and centred, so all icons fill their box alike at 16 px.
* **Marks** are drawn here with Pillow: the eight substance-group marks, the five reaction marks and the hexagon
  plate a material trait sits on. Plain geometry is crisper and reproducible when drawn.

Resumable like the other generators (an existing out/bio-icons/<id>.png is kept — delete it to re-roll one icon);
``--install`` copies the set into the client's Resources/icons and writes a ``.png.meta`` for every new file (import
settings cloned from a map marker, fresh GUID). It never touches an icon that does not start with ``bio_``.

Usage (from tools/ai-assets, where .env lives):
    uv run gen_bio_icons.py --dry-run
    uv run gen_bio_icons.py                      # pictograms (paid) + marks (free)
    uv run gen_bio_icons.py --only bio_heat bio_cold
    uv run gen_bio_icons.py --marks-only         # no API call
    uv run gen_bio_icons.py --install [--sheet out/bio-icons/sheet.png]
"""
from __future__ import annotations

import argparse
import base64
import math
import os
import re
import shutil
import sys
import time
import uuid
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out/bio-icons")
DEFAULT_DEST = Path(__file__).resolve().parents[2] / "client" / "Assets" / "Resources" / "icons"
TEMPLATE_META = "map_mark_ore.png.meta"

SIZE = 128
INK_BOX = 112  # the ink's longer side after cropping — the same for every icon

STYLE = ("bold solid filled flat pictogram, one single object, thick heavy silhouette shapes, high ink coverage, "
         "white on a fully transparent background, centered, minimal detail, readable at 16 pixels, game HUD "
         "status icon style, no thin lines, no outline-only shapes, no frame, no circle around it, no text, no "
         "words, no letters")

# One pictogram per theme. The client maps effects, traits and tool values onto these (BioSymbols in Client.Core).
ICONS = [
    ("bio_speed", "a running sneaker shoe with three short speed lines behind it"),
    ("bio_jump", "a coiled metal spring with a bold arrow pointing up above it"),
    ("bio_feather", "a single bird feather"),
    ("bio_grip", "an open hand print with five spread fingers"),
    ("bio_strength", "a flexed muscular arm showing the biceps"),
    ("bio_mining", "a handheld power drill tool seen from the side, with a pistol grip and a long spiral drill bit"),
    ("bio_reflex", "a stopwatch with a push button on top and one hand"),
    ("bio_heat", "a single flame"),
    ("bio_cold", "a six armed snowflake"),
    ("bio_toxinward", "a gas mask respirator with two round filters"),
    ("bio_nightsight", "a crescent moon with one small four pointed star beside it"),
    ("bio_perception", "a wide open eye with a round pupil"),
    ("bio_stealth", "a masquerade eye mask with two eye holes"),
    ("bio_gathering", "a woven basket with a curved handle and one leaf sticking out"),
    ("bio_hard", "a faceted cut diamond gemstone"),
    ("bio_heavy", "a kettlebell weight"),
    ("bio_magnetic", "a horseshoe magnet with two short spark lines at each pole"),
    ("bio_unstable", "a jagged explosion burst star"),
    ("bio_range", "a crosshair target reticle, a thick ring with four tick marks and a center dot"),
    ("bio_toxic", "a bubbling puddle of slime with three round bubbles rising from it"),
]

WHITE = (255, 255, 255, 255)
SS = 4  # supersampling of the drawn marks


# ---------------------------------------------------------------- pictogram post-processing


def whiten(img):
    """Force every pixel to pure white ink, preserving alpha — guarantees tintability."""
    from PIL import Image

    # Faint leftovers (a ghost frame around the object, a soft glow) go: only real ink stays.
    alpha = img.getchannel("A").point(lambda a: 0 if a < 48 else a)
    white = Image.new("RGBA", img.size, WHITE)
    white.putalpha(alpha)
    return white


def fit(img):
    """Crop to the ink and centre it at one size, so every icon fills its box alike."""
    from PIL import Image

    box = img.getchannel("A").point(lambda a: 255 if a > 24 else 0).getbbox()
    if box is None:
        return img.resize((SIZE, SIZE), Image.LANCZOS)
    ink = img.crop(box)
    scale = INK_BOX / max(ink.width, ink.height)
    ink = ink.resize((max(1, round(ink.width * scale)), max(1, round(ink.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(ink, ((SIZE - ink.width) // 2, (SIZE - ink.height) // 2))
    return canvas


def coverage(img) -> float:
    """Fraction of pixels with alpha > 128 — the legibility metric of the map markers (#592)."""
    histogram = img.getchannel("A").histogram()
    return sum(histogram[129:]) / (img.width * img.height)


# ---------------------------------------------------------------- drawn marks


def _canvas():
    from PIL import Image, ImageDraw

    img = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def _done(img):
    from PIL import Image

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def _u(v: float) -> float:
    """Icon units (0..128) to supersampled pixels."""
    return v * SS


def _line(d, pts, width: float) -> None:
    pts = [(_u(x), _u(y)) for x, y in pts]
    w = int(_u(width))
    d.line(pts, fill=WHITE, width=w, joint="curve")
    r = w / 2
    for x, y in pts:  # round caps and joints
        d.ellipse((x - r, y - r, x + r, y + r), fill=WHITE)


def _poly(d, pts) -> None:
    d.polygon([(_u(x), _u(y)) for x, y in pts], fill=WHITE)


def _ring(d, cx: float, cy: float, r: float, width: float) -> None:
    d.ellipse((_u(cx - r), _u(cy - r), _u(cx + r), _u(cy + r)), outline=WHITE, width=int(_u(width)))


def _disc(d, cx: float, cy: float, r: float) -> None:
    d.ellipse((_u(cx - r), _u(cy - r), _u(cx + r), _u(cy + r)), fill=WHITE)


def group_ring():
    img, d = _canvas()
    _ring(d, 64, 64, 48, 20)
    return _done(img)


def group_triangle():
    img, d = _canvas()
    _poly(d, [(64, 12), (118, 108), (10, 108)])
    return _done(img)


def group_square():
    img, d = _canvas()
    d.rounded_rectangle((_u(18), _u(18), _u(110), _u(110)), radius=_u(12), fill=WHITE)
    return _done(img)


def group_cross():
    img, d = _canvas()
    _line(d, [(24, 24), (104, 104)], 24)
    _line(d, [(104, 24), (24, 104)], 24)
    return _done(img)


def group_spiral():
    img, d = _canvas()
    pts = []
    turns = 2.25
    for i in range(0, 181):
        t = i / 180
        a = t * turns * 2 * math.pi
        r = 6 + t * 46
        pts.append((64 + r * math.cos(a), 64 + r * math.sin(a)))
    _line(d, pts, 13)
    return _done(img)


def group_wave():
    img, d = _canvas()
    for y0 in (44, 84):
        pts = [(12 + i * 2.6, y0 + 15 * math.sin(i / 40 * 2 * math.pi * 1.5)) for i in range(0, 41)]
        _line(d, pts, 17)
    return _done(img)


def group_dots():
    img, d = _canvas()
    _disc(d, 64, 32, 24)
    _disc(d, 30, 92, 24)
    _disc(d, 98, 92, 24)
    return _done(img)


def group_hourglass():
    img, d = _canvas()
    _poly(d, [(16, 12), (112, 12), (64, 68)])
    _poly(d, [(64, 60), (112, 116), (16, 116)])
    return _done(img)


def _chevron(d, cy: float, up: bool) -> None:
    tip = cy - 22 if up else cy + 22
    base = cy + 10 if up else cy - 10
    _line(d, [(22, base), (64, tip), (106, base)], 20)


def react_amplify():
    img, d = _canvas()
    _chevron(d, 46, True)
    _chevron(d, 94, True)
    return _done(img)


def react_inhibit():
    img, d = _canvas()
    _chevron(d, 34, False)
    _chevron(d, 82, False)
    return _done(img)


def react_couple():
    img, d = _canvas()
    _ring(d, 44, 64, 32, 15)
    _ring(d, 84, 64, 32, 15)
    return _done(img)


def _arrow(d, y: float, right: bool) -> None:
    x0, x1 = (16, 112) if right else (112, 16)
    head = x1 - 30 if right else x1 + 30
    _line(d, [(x0, y), (head, y)], 16)
    _poly(d, [(x1, y), (head - (2 if right else -2), y - 24), (head - (2 if right else -2), y + 24)])


def react_transmute():
    img, d = _canvas()
    _arrow(d, 40, True)
    _arrow(d, 88, False)
    return _done(img)


def react_neutral():
    img, d = _canvas()
    _line(d, [(30, 64), (98, 64)], 20)
    return _done(img)


def hex_plate():
    """The plate a material trait sits on: a pointy-top hexagon that fills the box."""
    img, d = _canvas()
    _poly(d, [(64 + 62 * math.cos(math.radians(a)), 64 + 62 * math.sin(math.radians(a))) for a in range(-90, 270, 60)])
    return _done(img)


MARKS = [
    ("bio_group_0", group_ring),
    ("bio_group_1", group_triangle),
    ("bio_group_2", group_square),
    ("bio_group_3", group_cross),
    ("bio_group_4", group_spiral),
    ("bio_group_5", group_wave),
    ("bio_group_6", group_dots),
    ("bio_group_7", group_hourglass),
    ("bio_react_amplify", react_amplify),
    ("bio_react_inhibit", react_inhibit),
    ("bio_react_couple", react_couple),
    ("bio_react_transmute", react_transmute),
    ("bio_react_neutral", react_neutral),
    ("bio_hex", hex_plate),
]


# ---------------------------------------------------------------- install


def install(ids: list[str], dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    template = (dest / TEMPLATE_META).read_text(encoding="utf-8")
    for sid in ids:
        if not sid.startswith("bio_"):
            sys.exit(f"refusing to install {sid}: this script only writes bio_* icons")
        src = OUT / f"{sid}.png"
        if not src.exists():
            sys.exit(f"{src} is missing - generate it first")
        target = dest / f"{sid}.png"
        shutil.copy2(src, target)
        meta = dest / f"{sid}.png.meta"
        if not meta.exists():
            text = re.sub(r"guid: [0-9a-f]{32}", f"guid: {uuid.uuid4().hex}", template, count=1)
            meta.write_text(text, encoding="utf-8", newline="\n")
        print(f"[bio-icons] installed {sid}.png -> {dest}")  # ASCII: cp1252 consoles


def sheet(ids: list[str], path: Path) -> None:
    """A contact sheet on the HUD's dark blue: every icon at 96 px and at the 16 px the HUD draws."""
    from PIL import Image, ImageDraw

    cols, cell, pad, label = 10, 96, 10, 34
    rows = (len(ids) + cols - 1) // cols
    img = Image.new("RGBA", (cols * (cell + pad) + pad, rows * (cell + pad + label) + pad), (14, 22, 34, 255))
    d = ImageDraw.Draw(img)
    for i, sid in enumerate(ids):
        icon = Image.open(OUT / f"{sid}.png").convert("RGBA")
        x = pad + (i % cols) * (cell + pad)
        y = pad + (i // cols) * (cell + pad + label)
        img.alpha_composite(icon.resize((cell, cell), Image.LANCZOS), (x, y))
        img.alpha_composite(icon.resize((16, 16), Image.LANCZOS), (x, y + cell + 2))
        d.text((x + 22, y + cell + 4), sid[4:], fill=(160, 180, 200, 255))
    path.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(path)
    print(f"[bio-icons] sheet -> {path}")


# ---------------------------------------------------------------- main


def generate_pictograms(icons: list[tuple[str, str]]) -> list[str]:
    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key)
    fails: list[str] = []
    total = len(icons)
    for i, (sid, desc) in enumerate(icons, 1):
        out = OUT / f"{sid}.png"
        if out.exists() and out.stat().st_size > 0:
            print(f"[{i}/{total}] {sid}: skip (exists)")
            continue

        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=f"{desc}, {STYLE}", size="1024x1024",
                    quality="low", n=1, background="transparent")
                raw = base64.b64decode(resp.data[0].b64_json)
                img = fit(whiten(Image.open(BytesIO(raw)).convert("RGBA")))
                cov = coverage(img)
                img.save(out)
                ok = True
                print(f"[{i}/{total}] {sid}: ok ({out.stat().st_size} bytes, ink {cov:.0%})")
                if cov < 0.10:
                    print(f"[{i}/{total}] {sid}: WARNING ink coverage {cov:.0%} < 10% - delete the file and re-run")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {sid}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            fails.append(sid)

    return fails


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the bio lab's symbol set (#3718).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--marks-only", action="store_true", help="draw the marks only - no API call")
    ap.add_argument("--install", action="store_true", help="copy the set into the client Resources (+ .meta)")
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--sheet", type=Path, help="write a contact sheet of the set to this PNG")
    ap.add_argument("--only", nargs="+", metavar="ID", help="only these icon ids")
    args = ap.parse_args()

    icons = ICONS
    marks = MARKS
    if args.only:
        known = {sid for sid, _ in ICONS} | {sid for sid, _ in MARKS}
        unknown = [sid for sid in args.only if sid not in known]
        if unknown:
            sys.exit(f"not in the manifest: {', '.join(unknown)}")
        icons = [(sid, desc) for sid, desc in ICONS if sid in args.only]
        marks = [(sid, fn) for sid, fn in MARKS if sid in args.only]

    print(f"[bio-icons] {len(icons)} pictograms, {len(marks)} drawn marks")
    if args.dry_run:
        for sid, desc in icons:
            print(f"  api   {sid:18s} {desc}")
        for sid, _ in marks:
            print(f"  drawn {sid}")
        return

    OUT.mkdir(parents=True, exist_ok=True)
    for sid, fn in marks:
        fn().save(OUT / f"{sid}.png")
        print(f"[bio-icons] drew {sid}")

    fails = [] if args.marks_only else generate_pictograms(icons)
    if fails:
        print("[bio-icons] failed: " + ", ".join(fails))

    ids = [sid for sid, _ in icons if not args.marks_only] + [sid for sid, _ in marks]
    if args.sheet:
        sheet([sid for sid in ids if (OUT / f"{sid}.png").exists()], args.sheet)
    if args.install and not fails:
        install(ids, args.dest)


if __name__ == "__main__":
    main()
