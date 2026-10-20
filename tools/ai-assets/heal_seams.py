# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Heal the wrap-around seams of tiling textures (#2860) — the creature hides repeat on every cube face, so a tile
whose left edge does not continue its right edge (or top/bottom) shows a line on every repeat.

The seam metric (per tile, on luma 0..1): inner_v = mean |row[y+1] - row[y]|, inner_h = mean |col[x+1] - col[x]|,
seam_v = mean |row[0] - row[last]|, seam_h = mean |col[0] - col[last]|; ratio = max(seam_v / inner_v,
seam_h / inner_h). A seamless tile sits around 1 (its wrap step is an ordinary neighbour step); the creature tests
allow at most 1.3.

The healing is Moisan's periodic + smooth decomposition ("Periodic plus smooth image decomposition", 2011): every
image is a periodic part p plus a smooth part s that carries the jumps across the borders. s is the solution of a
Poisson equation whose source is the border jumps alone, so it is a very smooth ramp near the edges and ~0 in the
middle; the healed tile is p = u - s. No texel is replaced or blended with a shifted copy, so the tile keeps all of
its detail and its character — no ghosting, no blur cross; only the brightness near the edges bends a little so
the edges meet. Each channel (alpha too) is healed on its own; a constant channel is left exactly as it is.

Works on the bundled raw tiles (client/Assets/Resources/textures/*.bytes: 64 x 64 RGBA32, rows bottom-up; the byte
size is kept) and on PNGs. Plain Python + Pillow (a small radix-2 FFT, no numpy), so it runs in the tools' uv env.

Usage (from tools/ai-assets):
    uv run heal_seams.py --report                      # seam ratio of every bundled creature_* tile
    uv run heal_seams.py --hides --dry-run             # which hide tiles are above --max-ratio (default 1.3)
    uv run heal_seams.py --hides                       # heal them: .bytes in place + out/creatures/<key>.png
    uv run heal_seams.py path/to/tile.bytes other.png  # heal named files in place (bytes or PNG)

--hides covers creature_<hide>.bytes only (not the creature_pattern_* masks or creature_horn_* tiles, which
gen_coat_tiles.py makes seamless itself). Before a .bytes is overwritten the original is copied to
out/creatures/pre_heal/ (git-ignored), and every run prints the before/after ratios. Hand-painted tiles
(texture_provenance.json) are skipped unless --force.
"""
from __future__ import annotations

import argparse
import array
import cmath
import math
import shutil
import sys
from pathlib import Path

from PIL import Image

import texture_provenance

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = Path(__file__).resolve().parent / "out" / "creatures"
NOT_HIDES = ("creature_pattern_", "creature_horn_")
# HEAL_ABOVE is the target: --hides heals every tile above it. Period alignment (see align_periods) runs when a seam
# is above ALIGN_ABOVE (a structural break, not just a brightness step); the crop keeps at least MIN_PERIOD of the 64
# lines (a stretch of at most 64/MIN_PERIOD), matches MATCH_LINES lines, may slide up to MAX_SHEAR texels, pays
# STRETCH_PENALTY per cropped line and SHEAR_PENALTY per texel of slide (so a near-full, unslid crop wins a tie),
# and is only used when its match score is at most ALIGN_MAX_SCORE.
HEAL_ABOVE = 1.3
ALIGN_ABOVE = 1.5
MIN_PERIOD = 52
MATCH_LINES = 2
MAX_SHEAR = 12
STRETCH_PENALTY = 0.01
SHEAR_PENALTY = 0.01
ALIGN_MAX_SCORE = 1.2

# An image is a list of channel planes, each a list of rows (top row first) of floats in 0..255.
Planes = list[list[list[float]]]


# ---------------------------------------------------------------------------------------------- I/O

def load(path: Path) -> Planes:
    """RGBA planes (top row first) of a raw 64 x 64 RGBA32 .bytes tile (stored bottom-up) or of any image file."""
    if path.suffix == ".bytes":
        data = path.read_bytes()
        if len(data) != TILE * TILE * 4:
            raise ValueError(f"{path}: {len(data)} bytes, expected a raw {TILE}x{TILE} RGBA32 tile")
        img = Image.frombytes("RGBA", (TILE, TILE), data).transpose(Image.FLIP_TOP_BOTTOM)
    else:
        img = Image.open(path).convert("RGBA")
    return from_image(img)


def from_image(img: Image.Image) -> Planes:
    w, h = img.size
    return [[[float(v) for v in row] for row in _rows(band.tobytes(), w, h)] for band in img.convert("RGBA").split()]


def _rows(data: bytes, w: int, h: int) -> list[bytes]:
    return [data[y * w:(y + 1) * w] for y in range(h)]


def to_image(planes: Planes) -> Image.Image:
    h, w = len(planes[0]), len(planes[0][0])
    bands = []
    for plane in planes:
        flat = bytes(max(0, min(255, round(v))) for row in plane for v in row)
        bands.append(Image.frombytes("L", (w, h), flat))
    return Image.merge("RGBA", bands)


def save(path: Path, planes: Planes) -> None:
    """Writes a .bytes tile (raw RGBA32, bottom-up, same size as before) or an image file."""
    img = to_image(planes)
    if path.suffix == ".bytes":
        path.write_bytes(img.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    else:
        img.save(path)


# ---------------------------------------------------------------------------------------------- metric

def luma(planes: Planes) -> list[list[float]]:
    r, g, b = planes[0], planes[1], planes[2]
    return [[(0.299 * r[y][x] + 0.587 * g[y][x] + 0.114 * b[y][x]) / 255.0 for x in range(len(r[0]))]
            for y in range(len(r))]


def seam_ratio_plane(plane: list[list[float]]) -> tuple[float, float, float]:
    """(ratio, vertical-wrap ratio, horizontal-wrap ratio) of one plane, see the module doc. A plane without any
    interior step in a direction counts 0 for it (a seam there is then the only step: ratio inf)."""
    h, w = len(plane), len(plane[0])
    inner_v = sum(abs(plane[y + 1][x] - plane[y][x]) for y in range(h - 1) for x in range(w)) / ((h - 1) * w)
    inner_h = sum(abs(plane[y][x + 1] - plane[y][x]) for y in range(h) for x in range(w - 1)) / (h * (w - 1))
    seam_v = sum(abs(plane[0][x] - plane[h - 1][x]) for x in range(w)) / w
    seam_h = sum(abs(plane[y][0] - plane[y][w - 1]) for y in range(h)) / h

    def r(seam: float, inner: float) -> float:
        if inner > 0:
            return seam / inner
        return 0.0 if seam == 0 else math.inf

    rv, rh = r(seam_v, inner_v), r(seam_h, inner_h)
    return max(rv, rh), rv, rh


def seam_ratio(planes: Planes) -> tuple[float, float, float]:
    """The seam metric on the tile's luma."""
    return seam_ratio_plane(luma(planes))


# ---------------------------------------------------------------------------------------------- FFT

def _fft(a: list[complex], inverse: bool = False) -> list[complex]:
    """Iterative radix-2 FFT (len(a) a power of two); the inverse is unnormalised."""
    n = len(a)
    if n & (n - 1):
        raise ValueError("FFT size must be a power of two")
    out = list(a)
    j = 0
    for i in range(1, n):
        bit = n >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j |= bit
        if i < j:
            out[i], out[j] = out[j], out[i]
    size = 2
    sign = 1 if inverse else -1
    while size <= n:
        step = cmath.exp(sign * 2j * math.pi / size)
        half = size // 2
        for start in range(0, n, size):
            wk = 1 + 0j
            for k in range(start, start + half):
                t = wk * out[k + half]
                out[k + half] = out[k] - t
                out[k] = out[k] + t
                wk *= step
        size *= 2
    return out


def _fft2(grid: list[list[complex]], inverse: bool = False) -> list[list[complex]]:
    rows = [_fft(row, inverse) for row in grid]
    cols = [_fft([rows[y][x] for y in range(len(rows))], inverse) for x in range(len(rows[0]))]
    return [[cols[x][y] for x in range(len(cols))] for y in range(len(rows))]


def smooth_component(plane: list[list[float]]) -> list[list[float]]:
    """Moisan's smooth part s of a plane (power-of-two sides): the Poisson solution driven by the border jumps."""
    h, w = len(plane), len(plane[0])
    v = [[0.0] * w for _ in range(h)]
    for y in range(h):
        jump = plane[y][w - 1] - plane[y][0]
        v[y][0] += jump
        v[y][w - 1] -= jump
    for x in range(w):
        jump = plane[h - 1][x] - plane[0][x]
        v[0][x] += jump
        v[h - 1][x] -= jump
    spec = _fft2([[complex(c) for c in row] for row in v])
    cy = [2.0 * math.cos(2.0 * math.pi * q / h) for q in range(h)]
    cx = [2.0 * math.cos(2.0 * math.pi * r / w) for r in range(w)]
    for q in range(h):
        for r in range(w):
            spec[q][r] = 0j if q == 0 and r == 0 else spec[q][r] / (cy[q] + cx[r] - 4.0)
    s = _fft2(spec, inverse=True)
    n = h * w
    return [[s[y][x].real / n for x in range(w)] for y in range(h)]


def periodic_component(plane: list[list[float]]) -> list[list[float]]:
    """The tileable part p = u - s of a plane (same mean as u)."""
    s = smooth_component(plane)
    return [[plane[y][x] - s[y][x] for x in range(len(plane[0]))] for y in range(len(plane))]


def _transpose(plane: list[list[float]]) -> list[list[float]]:
    return [list(col) for col in zip(*plane)]


def best_period(lum: list[list[float]], axis: str, max_shear: int) -> tuple[int, int, int, float]:
    """(offset, length, shear, score) of the crop along `axis` ("x" = columns, "y" = rows) whose first lines match
    the lines just past its end best — cropping there makes the content after the last line continue as the first
    line, so a lattice (scales, cells, ridges) whose period does not divide the tile keeps its phase across the
    wrap. `shear` lets the match slide sideways (cyclically, up to max_shear texels) for staggered lattices such as
    rows of feathers that sit half a feather apart. The score is the mean step between the matched lines in units of
    the ordinary neighbour step (0 = the content repeats exactly), plus small penalties for stretch and shear."""
    lines = lum if axis == "y" else _transpose(lum)
    n, m = len(lines), len(lines[0])
    inner = sum(abs(lines[i + 1][j] - lines[i][j]) for i in range(n - 1) for j in range(m)) / ((n - 1) * m) or 1.0
    best = (0, n, 0, math.inf)
    for length in range(MIN_PERIOD, n):
        for offset in range(n - length):
            pairs = [(offset + k, offset + length + k) for k in range(MATCH_LINES) if offset + length + k < n]
            for shear in range(-max_shear, max_shear + 1):
                diff = sum(abs(lines[b][j] - lines[a][(j + shear) % m]) for a, b in pairs for j in range(m))
                score = (diff / (len(pairs) * m) / inner + STRETCH_PENALTY * (n - length)
                         + SHEAR_PENALTY * abs(shear))
                if score < best[3]:
                    best = (offset, length, shear, score)
    return best


def apply_period(planes: Planes, axis: str, offset: int, length: int, shear: int) -> Planes:
    """Crops every plane to `length` lines from `offset` along `axis`, slides line r of the crop sideways by
    -shear * r / length texels (cyclically, so nothing is lost and the other axis stays whole), and resamples the
    crop back to the full size along `axis`. The resample wraps around (the crop is padded with its own other end),
    so it adds no edge of its own."""
    pad = 4
    out = []
    for plane in planes:
        lines = plane if axis == "y" else _transpose(plane)
        n, m = len(lines), len(lines[0])
        crop = []
        for r in range(length):
            slide = round(-shear * r / length)
            line = lines[offset + r]
            crop.append([line[(j + slide) % m] for j in range(m)])
        padded = [[crop[(r - pad) % length][(j - pad) % m] for j in range(m + 2 * pad)] for r in range(length + 2 * pad)]
        img = Image.new("F", (m + 2 * pad, length + 2 * pad))
        img.putdata([v for row in padded for v in row])
        img = img.resize((m, n), Image.BICUBIC, box=(pad, pad, pad + m, pad + length))
        data = array.array("f", img.tobytes())
        result = [[max(0.0, min(255.0, data[r * m + j])) for j in range(m)] for r in range(n)]
        out.append(result if axis == "y" else _transpose(result))
    return out


def align_periods(planes: Planes) -> tuple[Planes, list[str]]:
    """The structural half of the heal. Moisan's decomposition only evens out brightness; when a lattice arrives
    out of phase at the wrap (half a scale, a cut cell, a ridge that ends), that break stays visible. So when a tile
    has a seam above ALIGN_ABOVE, every axis above HEAL_ABOVE is cropped to its best period (x first, then y) — if
    that period really matches (score <= ALIGN_MAX_SCORE); otherwise the axis is left to Moisan alone. A shear
    is only allowed on the last axis cropped, and only when the other axis needs nothing (a slide moves that axis's
    seam into the tile, where no later step reaches it). Returns the planes and a note per aligned axis."""
    _, rv, rh = seam_ratio(planes)
    if max(rv, rh) <= ALIGN_ABOVE:
        return planes, []
    axes = [a for a, r in (("x", rh), ("y", rv)) if r > HEAL_ABOVE]
    notes = []
    for i, axis in enumerate(axes):
        _, rv, rh = seam_ratio(planes)
        other = rv if axis == "x" else rh
        may_shear = i == len(axes) - 1 and (other <= HEAL_ABOVE or len(axes) == 2)
        offset, length, shear, score = best_period(luma(planes), axis, MAX_SHEAR if may_shear else 0)
        if score > ALIGN_MAX_SCORE:
            notes.append(f"{axis}: no matching period (best {score:.2f}), Moisan only")
            continue
        planes = apply_period(planes, axis, offset, length, shear)
        notes.append(f"{axis}: period {length} from {offset}, shear {shear} (match {score:.2f})")
    return planes, notes


def heal(planes: Planes, align: bool = True) -> Planes:
    """The healed tile: the lattice phase aligned where a seam is structural (align_periods; align=False skips
    that), then every channel replaced by its periodic part (clamped to 0..255); constant channels stay untouched."""
    if align:
        planes, _ = align_periods(planes)
    out = []
    for plane in planes:
        flat = [v for row in plane for v in row]
        if max(flat) == min(flat):
            out.append([list(row) for row in plane])
            continue
        p = periodic_component(plane)
        out.append([[max(0.0, min(255.0, v)) for v in row] for row in p])
    return out


# ---------------------------------------------------------------------------------------------- CLI

def creature_tiles() -> list[Path]:
    return sorted(RES.glob("creature_*.bytes"))


def is_hide(path: Path) -> bool:
    return not path.stem.startswith(NOT_HIDES)


def heal_file(path: Path, max_ratio: float | None, dry_run: bool, force: bool, provenance: dict) -> None:
    planes = load(path)
    before = seam_ratio(planes)[0]
    if max_ratio is not None and before <= max_ratio:
        print(f"{path.stem:28s} {before:5.2f}  ok, untouched")
        return
    if not texture_provenance.guard(path.stem, force, provenance):
        return
    aligned, notes = align_periods(planes)
    healed = heal(aligned, align=False)
    after = seam_ratio(to_planes_rounded(healed))[0]
    how = "; ".join(notes) if notes else "Moisan only"
    if dry_run:
        print(f"{path.stem:28s} {before:5.2f} -> {after:5.2f}  [{how}] (dry run, nothing written)")
        return
    if path.suffix == ".bytes":
        OUT.joinpath("pre_heal").mkdir(parents=True, exist_ok=True)
        backup = OUT / "pre_heal" / path.name
        if not backup.exists():
            shutil.copy2(path, backup)
    save(path, healed)
    note = ""
    if path.suffix == ".bytes" and path.stem.startswith("creature_"):
        png = OUT / f"{path.stem[len('creature_'):]}.png"
        if png.exists():
            save(png, healed)
            note = f" + {png.relative_to(OUT.parent.parent)}"
    print(f"{path.stem:28s} {before:5.2f} -> {after:5.2f}  [{how}] healed{note}")


def to_planes_rounded(planes: Planes) -> Planes:
    """The planes as they will be stored (rounded bytes), so a reported ratio is the stored tile's."""
    return [[[float(max(0, min(255, round(v)))) for v in row] for row in plane] for plane in planes]


def main() -> None:
    ap = argparse.ArgumentParser(description="Heal the wrap-around seams of tiling textures (#2860).")
    ap.add_argument("files", nargs="*", type=Path, help=".bytes tiles or images to heal in place")
    ap.add_argument("--report", action="store_true", help="print the seam ratio of every bundled creature_* tile")
    ap.add_argument("--hides", action="store_true", help="heal every bundled creature hide tile above --max-ratio")
    ap.add_argument("--max-ratio", type=float, default=HEAL_ABOVE,
                    help=f"--hides leaves tiles at or below this ratio alone (default {HEAL_ABOVE})")
    ap.add_argument("--dry-run", action="store_true", help="measure and report, write nothing")
    ap.add_argument("--force", action="store_true", help="also heal hand-painted tiles (texture_provenance.json)")
    args = ap.parse_args()

    if not (args.files or args.report or args.hides):
        ap.error("name files to heal, or pass --report or --hides")

    if args.report:
        rows = []
        for path in creature_tiles():
            ratio, rv, rh = seam_ratio(load(path))
            rows.append((ratio, path.stem, rv, rh))
        for ratio, name, rv, rh in sorted(rows, reverse=True):
            print(f"{name:28s} ratio {ratio:5.2f}  (v {rv:4.2f}, h {rh:4.2f})")
        return

    provenance = texture_provenance.load()
    if args.hides:
        print(f"[heal] creature hide tiles, healing those above ratio {args.max_ratio}")
        for path in creature_tiles():
            if is_hide(path):
                heal_file(path, args.max_ratio, args.dry_run, args.force, provenance)
    for path in args.files:
        if not path.exists():
            sys.exit(f"{path}: no such file")
        heal_file(path, None, args.dry_run, args.force, provenance)


if __name__ == "__main__":
    main()
