# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=13"]
# ///
"""Contact sheets and publishing for the HUD-free world stills (scripts/capture-stills.ps1, #2765).

  sheets   One contact sheet per shot folder (<stills>/<name>/<name>__<seed>.png) into <stills>/_sheets/<name>.jpg,
           plus _sheets/index.jpg with every shot's best-scoring variant. Each thumbnail is labelled with its seed and
           a rough quality score; a nearly black, washed-out or single-colour frame (a failed pose, a frame inside a
           block) scores low. The score only orders the variants — the pick is made by eye.
  publish  Writes the manifest's picks ("pick": <seed> per entry; 0 = not picked yet) as JPEGs to
           <out>/<name>.jpg — the files the README gallery links.

  uv run tools/screenshots/stills.py sheets  --stills marketing/stills
  uv run tools/screenshots/stills.py publish --manifest scripts/shot-manifests/marketing.json \
      --stills marketing/stills --out docs/screenshots/world
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageStat

THUMB = (640, 360)
LABEL_H = 28


def score(img: Image.Image) -> tuple[float, dict[str, float]]:
    """A rough frame quality: colourfulness + contrast + detail, punished when too dark, too bright or one colour."""
    small = img.convert("RGB").resize((320, 180))
    gray = ImageStat.Stat(small.convert("L"))
    luma = gray.mean[0]
    contrast = gray.stddev[0]
    # Hasler & Suesstrunk colourfulness on the small frame.
    raw = small.tobytes()
    px = [(raw[i], raw[i + 1], raw[i + 2]) for i in range(0, len(raw), 3)]
    rgs = [p[0] - p[1] for p in px]
    ybs = [0.5 * (p[0] + p[1]) - p[2] for p in px]

    def mean_std(values: list[float]) -> tuple[float, float]:
        m = sum(values) / len(values)
        return m, math.sqrt(sum((v - m) ** 2 for v in values) / len(values))

    (mrg, srg), (myb, syb) = mean_std(rgs), mean_std(ybs)
    colour = math.sqrt(srg**2 + syb**2) + 0.3 * math.sqrt(mrg**2 + myb**2)
    detail = ImageStat.Stat(small.convert("L").filter(ImageFilter.FIND_EDGES)).mean[0]
    s = colour * 0.6 + contrast * 0.6 + detail * 1.5
    if luma < 45:
        s -= (45 - luma) * 1.5
    if luma > 215:
        s -= (luma - 215) * 1.5
    if contrast < 18:
        s -= (18 - contrast) * 3
    return s, {"luma": luma, "contrast": contrast, "colour": colour, "detail": detail}


def variants(folder: Path) -> list[tuple[str, Path]]:
    out = []
    for p in sorted(folder.glob("*.png")):
        seed = p.stem.split("__")[-1]
        out.append((seed, p))
    return out


def label(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str) -> None:
    draw.rectangle([xy[0], xy[1], xy[0] + THUMB[0], xy[1] + LABEL_H], fill=(20, 20, 24))
    draw.text((xy[0] + 8, xy[1] + 7), text, fill=(235, 235, 235))


def sheet(cells: list[tuple[str, Image.Image]], cols: int) -> Image.Image:
    rows = max(1, math.ceil(len(cells) / cols))
    w, h = THUMB[0], THUMB[1] + LABEL_H
    out = Image.new("RGB", (cols * w, rows * h), (10, 10, 12))
    draw = ImageDraw.Draw(out)
    for i, (text, img) in enumerate(cells):
        x, y = (i % cols) * w, (i // cols) * h
        out.paste(img.convert("RGB").resize(THUMB), (x, y))
        label(draw, (x, y + THUMB[1]), text)
    return out


def cmd_sheets(args: argparse.Namespace) -> int:
    root = Path(args.stills)
    dest = root / "_sheets"
    dest.mkdir(parents=True, exist_ok=True)
    best_cells: list[tuple[str, Image.Image]] = []
    for folder in sorted(
        p for p in root.iterdir() if p.is_dir() and not p.name.startswith("_")
    ):
        scored = []
        for seed, path in variants(folder):
            img = Image.open(path)
            s, m = score(img)
            scored.append((s, seed, img, m))
        if not scored:
            continue
        scored.sort(key=lambda t: t[0], reverse=True)
        cells = [
            (f"{folder.name}  seed {seed}  score {s:.0f}  luma {m['luma']:.0f}", img)
            for s, seed, img, m in scored
        ]
        sheet(cells, cols=min(3, len(cells))).save(
            dest / f"{folder.name}.jpg", quality=85
        )
        s, seed, img, _ = scored[0]
        best_cells.append((f"{folder.name}  seed {seed}", img))
        print(
            f"{folder.name}: "
            + ", ".join(f"{seed}={s:.0f}" for s, seed, _, _ in scored)
        )
    if best_cells:
        sheet(best_cells, cols=4).save(dest / "index.jpg", quality=85)
    print(f"sheets -> {dest}")
    return 0


def cmd_publish(args: argparse.Namespace) -> int:
    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    root, out = Path(args.stills), Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    missing = 0
    for entry in manifest.get("clips", []):
        name, pick = entry.get("name"), entry.get("pick", 0)
        if not pick:
            print(f"{name}: no pick yet — skipped")
            continue
        src = root / name / f"{name}__{pick}.png"
        if not src.exists():
            print(f"{name}: {src} not found", file=sys.stderr)
            missing += 1
            continue
        dst = out / f"{name}.jpg"
        Image.open(src).convert("RGB").save(
            dst, quality=88, optimize=True, progressive=True
        )
        print(f"{name}: seed {pick} -> {dst} ({dst.stat().st_size // 1024} KB)")
    return 1 if missing else 0


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("sheets", help="contact sheet per shot")
    s.add_argument("--stills", required=True)
    p = sub.add_parser("publish", help="write the manifest's picks as JPEGs")
    p.add_argument("--manifest", required=True)
    p.add_argument("--stills", required=True)
    p.add_argument("--out", required=True)
    args = ap.parse_args()
    return cmd_sheets(args) if args.cmd == "sheets" else cmd_publish(args)


if __name__ == "__main__":
    sys.exit(main())
