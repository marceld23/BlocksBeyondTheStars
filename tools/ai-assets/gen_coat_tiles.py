# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the creatures' coat pattern masks (#2863) and horn tiles (#2867) via the OpenAI image API, and turn the
images into seamless 64 x 64 tiles. Mirrors gen_creatures.py (same model, size and quality: ~1 cent per image).

Two sets:

- `patterns` -> out/creatures/pattern_<name>.png, bundled as creature_pattern_<name>: a two-tone image (black shapes
  on white) made into a clean BINARY mask. Bundled convention: white (luma > 127) = MARK (the pattern colour),
  black = body; fully opaque, pure 0/255 values. Image x runs along the body (head -> tail), image y up the flank,
  so a stripe that should ring the body is vertical in the image.
  Post-processing: box-downscale to 320 px, threshold (Otsu), signed distance field, then the tile is cut as the
  first ~256 px (per axis the length whose following band best repeats the first one, so a regular pattern keeps
  its phase) with a 48 px overlap band — the field of the band past the right (bottom) edge is morphed into the
  first columns (rows), so each edge continues into the opposite one (the shapes morph instead of ghosting, and
  nothing of the image is used twice); box-downscale to 64, threshold, drop specks below a few texels
  (wrap-aware), and shift the iso-level of the field until the mark coverage lands in the pattern's target band.
- `horns` -> out/creatures/horn_<name>.png, bundled as creature_horn_<name>: grayscale, light-to-mid tones (they are
  tinted by multiply and normalised in code); image y runs along the horn (growth rings are horizontal lines).
  Post-processing: box-downscale to 64, heal_seams.heal (period alignment + Moisan), levels to light-to-mid grey.

The full-size generations are kept in out/creatures/raw/ (git-ignored) so the post-processing can be re-run for free
(--process-only). --reroll moves a rejected raw aside (raw/<key>.rejected-<n>.png) and generates a new one; a key
is re-rolled at most MAX_REROLLS times.

Bundle the chosen tiles into the client with bundle_textures.py afterwards, e.g.:
    uv run bundle_textures.py --creatures --only pattern_giraffe horn_ridged

Usage (from tools/ai-assets):
    uv run gen_coat_tiles.py --set patterns --dry-run
    uv run gen_coat_tiles.py --set patterns --only giraffe       # generate one (skips an existing raw)
    uv run gen_coat_tiles.py --set horns                         # the whole set
    uv run gen_coat_tiles.py --set patterns --process-only       # re-run the post-processing, no API calls
    uv run gen_coat_tiles.py --set patterns --only tabby --reroll
"""
from __future__ import annotations

import argparse
import array
import base64
import math
import os
import sys
import time
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv
from PIL import Image

import heal_seams

OUT = Path("out/creatures")
RAW = OUT / "raw"
TILE = 64
MAX_REROLLS = 3

PATTERN_STYLE = ("seamless tileable two-tone pattern tile, pure black shapes on a pure white background, flat, "
                 "no shading, no gradients, no texture, no text, no border, no frame, top-down, fills the whole "
                 "square edge to edge")
HORN_STYLE = ("seamless tileable texture, grayscale, light and mid grey tones, retro 16-bit video-game creature "
              "horn surface, top-down flat, fills the whole square edge to edge, no text, no words")


@dataclass(frozen=True)
class Pattern:
    name: str
    desc: str
    lo: float  # target mark coverage band
    hi: float
    rotate: bool = False  # generated pointing up / stacked vertically; turned 90 degrees clockwise (up -> +x)


PATTERNS = [
    Pattern("giraffe", "a reticulated giraffe coat: large irregular angular polygon patches packed closely "
            "together and separated only by thin white lines, about five patches across", 0.60, 0.70),
    Pattern("dalmatian", "a dalmatian coat: about fifteen round black spots of mixed sizes, mostly large, scattered "
            "irregularly with plenty of white between them, about four spots across", 0.20, 0.30),
    Pattern("pinto", "a pinto horse coat: three or four big irregular rounded black patches with wavy edges",
            0.35, 0.45),
    Pattern("cheetah", "a cheetah coat seen close up: about thirty solid round black spots of similar size, "
            "slightly irregular, scattered at random positions with no grid and no rows, about five spots across",
            0.25, 0.35),
    Pattern("jaguar", "a jaguar coat seen close up: only two or three huge rosettes across, each rosette a ring of "
            "four or five fat black blotches around a lighter centre with one black dot inside", 0.25, 0.35),
    Pattern("tabby", "a tabby cat coat: bold wavy vertical black stripes and swirls running from the top edge to "
            "the bottom edge, about six stripes across", 0.35, 0.45),
    Pattern("brindle", "a brindle dog coat: many thin irregular broken vertical black streaks running from the top "
            "edge to the bottom edge", 0.30, 0.40),
    Pattern("okapi", "bold wavy vertical black bars like okapi leg stripes standing upright, running from the top "
            "edge to the bottom edge, about five bars across", 0.38, 0.45),
    Pattern("ladybird", "ladybird beetle dots: a few large round solid black dots far apart from each other, "
            "about three dots across", 0.15, 0.25),
    Pattern("poisonfrog", "a poison dart frog skin: irregular rounded black blotches and short worm-like shapes",
            0.35, 0.45),
    Pattern("salamander", "a fire salamander skin: big irregular black blotches and thick dashes", 0.30, 0.40),
    Pattern("eyespots", "round target-like eye spots and nothing else, no animals, no insects: each spot a thick "
            "black ring around a solid black centre dot with a white gap between ring and dot, the spots scattered "
            "at irregular positions well apart, about three spots across", 0.20, 0.30),
    Pattern("tortoise", "a tortoise shell: irregular hexagonal white plates outlined by thick black lines, about "
            "four plates across", 0.30, 0.40),
    Pattern("diamondback", "a rattlesnake back: horizontal chains of large solid black diamonds touching tip to "
            "tip, each chain running from the left edge to the right edge, two chains with white between them",
            0.30, 0.40),
    Pattern("chevrons", "bold black chevron stripes: thick V shapes all pointing up, nested and stacked one above "
            "the other from the bottom edge to the top edge, about five chevrons", 0.35, 0.45, rotate=True),
    Pattern("lengthwise", "three straight horizontal black bands of different widths, each band running across the "
            "entire width of the image from the left border to the right border without any margin, like the "
            "stripes of a flag", 0.30, 0.40),
]

HORNS = [
    ("ridged", ("animal horn surface with evenly spaced horizontal growth rings and ridges running straight across "
                "from left to right")),
    ("grain", "smooth polished horn or ivory with fine vertical lengthwise grain lines running from top to bottom"),
    ("spiral", ("a twisted spiral horn surface with diagonal ridges running at a slant across it, like a markhor "
                "horn")),
    ("crystal", "a faceted translucent crystal horn surface with angular flat planes and sharp facet edges"),
    ("bark", "a rough bark-like horn surface with coarse cracked furrows running from top to bottom"),
]

# Pattern post-processing (see the module doc): the raw image is box-downscaled to WORK; the tile is the first
# length_x x length_y of it (each in MIN_LEN..WORK-BAND, chosen by best_length, preferring TILE_WORK at
# LENGTH_PENALTY per texel) with a BAND-wide overlap morphed in at each wrap.
WORK = 320
TILE_WORK = 256
BAND = 48
MIN_LEN = 208
LENGTH_PENALTY = 0.0005  # per texel off TILE_WORK, against a mismatch measured as a share of texels (masks)
LENGTH_PENALTY_VALUES = 0.005  # the same against a mismatch in neighbour steps (horns)
FULL_WRAP = 1.5  # an axis along which the raw image already wraps this well is used whole (see best_length)
ROLL_ABOVE = 1.1  # a tile whose seam metric is above this is shifted cyclically (see best_roll)
MIN_MARK = 6  # mark components smaller than this (texels at 64 px) are dropped ...
MIN_BODY = 4  # ... and body holes smaller than this are filled
MAX_LEVEL = 10.0  # the iso-level search moves the shapes' outline by at most this many work texels


# ---------------------------------------------------------------------------------------------- generation

def generate(client, key: str, prompt: str) -> bool:
    RAW.mkdir(parents=True, exist_ok=True)
    out = RAW / f"{key}.png"
    for attempt in (1, 2):
        try:
            resp = client.images.generate(
                model="gpt-image-1-mini", prompt=prompt, size="1024x1024", quality="low", n=1)
            raw = base64.b64decode(resp.data[0].b64_json)
            Image.open(BytesIO(raw)).convert("RGBA").save(out)
            print(f"  {key}: generated -> {out}")
            return True
        except Exception as exc:  # noqa: BLE001
            print(f"  {key}: attempt {attempt} failed: {exc}")
            time.sleep(2)
    return False


def reject(key: str) -> bool:
    """Moves raw/<key>.png aside for a re-roll; False when the key already used up its re-rolls."""
    used = len(list(RAW.glob(f"{key}.rejected-*.png")))
    if used >= MAX_REROLLS:
        print(f"  {key}: already re-rolled {used} times (max {MAX_REROLLS}) — not re-rolling again")
        return False
    src = RAW / f"{key}.png"
    if src.exists():
        src.rename(RAW / f"{key}.rejected-{used + 1}.png")
    return True


# ---------------------------------------------------------------------------------------------- distance field

_INF = 1e20


def _edt_1d(f: list[float]) -> list[float]:
    """Squared Euclidean distance transform of a sampled function (Felzenszwalb & Huttenlocher)."""
    n = len(f)
    v = [0] * n
    z = [0.0] * (n + 1)
    k = 0
    z[0], z[1] = -_INF, _INF
    for q in range(1, n):
        while True:
            s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k])
            if s > z[k]:
                break
            k -= 1
        k += 1
        v[k] = q
        z[k] = s
        z[k + 1] = _INF
    d = [0.0] * n
    k = 0
    for q in range(n):
        while z[k + 1] < q:
            k += 1
        d[q] = (q - v[k]) ** 2 + f[v[k]]
    return d


def _distance(inside: list[list[bool]]) -> list[list[float]]:
    """Distance of every texel to the nearest `inside` texel (0 on them)."""
    rows = [_edt_1d([0.0 if c else _INF for c in row]) for row in inside]
    h, w = len(rows), len(rows[0])
    cols = [_edt_1d([rows[y][x] for y in range(h)]) for x in range(w)]
    return [[math.sqrt(cols[x][y]) for x in range(w)] for y in range(h)]


def signed_field(mark: list[list[bool]]) -> list[list[float]]:
    """Positive inside the mark (distance to the body), negative outside (minus the distance to the mark)."""
    to_mark = _distance(mark)
    to_body = _distance([[not c for c in row] for row in mark])
    return [[to_body[y][x] - to_mark[y][x] for x in range(len(mark[0]))] for y in range(len(mark))]


def _smoothstep(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def _sides_differ(a: list[float], b: list[float]) -> float:
    """Distance-field lines: how many texels lie on different sides of the outline."""
    return float(sum(1 for u, v in zip(a, b) if (u > 0) != (v > 0)))


def _values_differ(a: list[float], b: list[float]) -> float:
    """Grey lines: the summed absolute difference."""
    return sum(abs(u - v) for u, v in zip(a, b))


def best_length(lines: list[list[float]], values: bool = False) -> tuple[int, float]:
    """(length, mismatch): the tile length along the lines' axis whose overlap band (lines length..length+BAND)
    agrees best with the first BAND lines — for a distance field (values=False) on which side of the outline every
    texel lies, for grey values (values=True) by their difference in units of an ordinary neighbour step — so a
    regular pattern blends two near-identical bands instead of two out-of-phase ones. Lightly prefers TILE_WORK.
    When the image already wraps by itself along this axis (the model drew a true tile: its last line steps into
    its first no harder than FULL_WRAP times an ordinary step), the whole length is used, without a band."""
    differ = _values_differ if values else _sides_differ
    n = len(lines)
    inner = sum(differ(lines[i], lines[i + 1]) for i in range(n - 1)) / (n - 1)
    if inner > 0 and differ(lines[n - 1], lines[0]) <= FULL_WRAP * inner:
        return n, 0.0
    unit = BAND * (inner if values and inner > 0 else len(lines[0]))
    penalty = LENGTH_PENALTY_VALUES if values else LENGTH_PENALTY
    best = (TILE_WORK, math.inf)
    for length in range(MIN_LEN, n - BAND + 1):
        diff = sum(differ(lines[k], lines[length + k]) for k in range(BAND))
        score = diff / unit + penalty * abs(length - TILE_WORK)
        if score < best[1]:
            best = (length, score)
    return best


def _wrap_lines(lines: list[list[float]], length: int, mean: float | None) -> list[list[float]]:
    """Lines 0..length-1, the first BAND blended from the band past `length` (so line length-1 is followed by the
    image's own continuation) into the lines themselves. A distance field (mean None) blends linearly — the
    outline morphs; grey values blend variance-preserving around `mean`, so the band keeps the tile's contrast
    instead of fading to a washed-out average. The whole length is returned as it is (it wraps already)."""
    out = [list(line) for line in lines[:length]]
    if length == len(lines):
        return out
    for k in range(BAND):
        t = _smoothstep((k + 0.5) / BAND)
        if mean is None:
            out[k] = [(1.0 - t) * u + t * v for u, v in zip(lines[length + k], lines[k])]
        else:
            norm = math.sqrt((1.0 - t) ** 2 + t * t)
            out[k] = [mean + ((1.0 - t) * (u - mean) + t * (v - mean)) / norm
                      for u, v in zip(lines[length + k], lines[k])]
    return out


def wrap_field(field: list[list[float]], values: bool = False) -> tuple[list[list[float]], tuple[int, int]]:
    """The periodic image (rows x columns = length_y x length_x) and its two lengths: columns first (on every row
    of the work image), then rows. values=False: a signed distance field; True: grey values."""
    mean = sum(v for row in field for v in row) / (len(field) * len(field[0])) if values else None
    cols = [list(c) for c in zip(*field)]
    lx, _ = best_length(cols, values)
    g = [list(c) for c in zip(*_wrap_lines(cols, lx, mean))]  # back to rows, lx wide, all WORK rows
    ly, _ = best_length(g, values)
    return _wrap_lines(g, ly, mean), (lx, ly)


# ---------------------------------------------------------------------------------------------- mask

def _components(mask: list[list[bool]], value: bool, eight: bool) -> list[list[tuple[int, int]]]:
    """Wrap-around connected components of the texels equal to `value`."""
    n = len(mask)
    seen = [[False] * n for _ in range(n)]
    steps = [(1, 0), (-1, 0), (0, 1), (0, -1)]
    if eight:
        steps += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
    comps = []
    for y0 in range(n):
        for x0 in range(n):
            if seen[y0][x0] or mask[y0][x0] != value:
                continue
            stack, comp = [(x0, y0)], []
            seen[y0][x0] = True
            while stack:
                x, y = stack.pop()
                comp.append((x, y))
                for dx, dy in steps:
                    nx, ny = (x + dx) % n, (y + dy) % n
                    if not seen[ny][nx] and mask[ny][nx] == value:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            comps.append(comp)
    return comps


def clean(mask: list[list[bool]]) -> list[list[bool]]:
    """Drops mark specks below MIN_MARK texels and fills body holes below MIN_BODY (wrap-aware)."""
    mask = [list(r) for r in mask]
    for comp in _components(mask, True, eight=True):
        if len(comp) < MIN_MARK:
            for x, y in comp:
                mask[y][x] = False
    for comp in _components(mask, False, eight=False):
        if len(comp) < MIN_BODY:
            for x, y in comp:
                mask[y][x] = True
    return mask


def mask_at(field: list[list[float]], level: float) -> list[list[bool]]:
    """The 64 px mask of the periodic work field cut at `level`: box-downscaled (each tile texel averages its own
    footprint, so the result stays periodic), majority, cleaned."""
    h, w = len(field), len(field[0])
    img = Image.frombytes("L", (w, h), bytes(255 if v > level else 0 for row in field for v in row))
    small = img.resize((TILE, TILE), Image.BOX).tobytes()
    mask = [[small[y * TILE + x] > 127 for x in range(TILE)] for y in range(TILE)]
    return clean(mask)


def coverage(mask: list[list[bool]]) -> float:
    return sum(sum(r) for r in mask) / (TILE * TILE)


def best_roll(mask: list[list[bool]]) -> tuple[list[list[bool]], tuple[int, int], float]:
    """A seamless tile can start anywhere; the seam metric only samples the wrap lines, so a straight edge lying
    exactly on them (a horizontal stripe's border on the top row) would read as a seam. Keeps the tile as it is
    when its ratio is fine, else picks the cyclic shift with the lowest ratio."""
    def ratio(m):
        return heal_seams.seam_ratio_plane([[1.0 if c else 0.0 for c in r] for r in m])[0]

    r0 = ratio(mask)
    if r0 <= ROLL_ABOVE:
        return mask, (0, 0), r0
    # the smallest shift that brings the ratio under ROLL_ABOVE, else the lowest ratio
    shifts = sorted(((dx, dy) for dy in range(TILE) for dx in range(TILE)),
                    key=lambda s: min(s[0], TILE - s[0]) + min(s[1], TILE - s[1]))
    best = (r0, 0, 0)
    for dx, dy in shifts:
        m = [[mask[(y + dy) % TILE][(x + dx) % TILE] for x in range(TILE)] for y in range(TILE)]
        r = ratio(m)
        if r <= ROLL_ABOVE:
            best = (r, dx, dy)
            break
        if r < best[0] - 1e-9:
            best = (r, dx, dy)
    r, dx, dy = best
    return [[mask[(y + dy) % TILE][(x + dx) % TILE] for x in range(TILE)] for y in range(TILE)], (dx, dy), r


def _otsu(data: bytes) -> int:
    hist = [0] * 256
    for v in data:
        hist[v] += 1
    total = len(data)
    sum_all = sum(i * h for i, h in enumerate(hist))
    best_t, best_var, w0, sum0 = 128, -1.0, 0, 0.0
    for t in range(256):
        w0 += hist[t]
        if w0 == 0 or w0 == total:
            continue
        sum0 += t * hist[t]
        m0 = sum0 / w0
        m1 = (sum_all - sum0) / (total - w0)
        var = w0 * (total - w0) * (m0 - m1) ** 2
        if var > best_var:
            best_var, best_t = var, t
    return max(64, min(192, best_t))


def process_pattern(p: Pattern) -> dict | None:
    src = RAW / f"pattern_{p.name}.png"
    if not src.exists():
        print(f"  {p.name}: no raw image (generate it first)")
        return None
    img = Image.open(src).convert("L")
    if p.rotate:
        img = img.transpose(Image.ROTATE_270)  # 90 degrees clockwise: "pointing up" -> pointing along +x
    work = img.resize((WORK, WORK), Image.BOX)
    data = work.tobytes()
    t = _otsu(data)
    # black shapes = mark
    mark = [[data[y * WORK + x] <= t for x in range(WORK)] for y in range(WORK)]
    field, lengths = wrap_field(signed_field(mark))

    target = (p.lo + p.hi) / 2
    level = 0.0
    mask = mask_at(field, level)
    cov = coverage(mask)
    if not p.lo <= cov <= p.hi:
        # Higher level = thinner shapes = less coverage; bisect for the band's middle within +-MAX_LEVEL.
        lo_l, hi_l = -MAX_LEVEL, MAX_LEVEL
        for _ in range(14):
            mid = (lo_l + hi_l) / 2
            if coverage(mask_at(field, mid)) > target:
                lo_l = mid
            else:
                hi_l = mid
        level = (lo_l + hi_l) / 2
        mask = mask_at(field, level)
        cov = coverage(mask)
    mask, roll, ratio = best_roll(mask)

    out = OUT / f"pattern_{p.name}.png"
    flat = bytes(255 if c else 0 for row in mask for c in row)
    lum = Image.frombytes("L", (TILE, TILE), flat)
    Image.merge("RGBA", (lum, lum, lum, Image.new("L", (TILE, TILE), 255))).save(out)

    comps = _components(mask, True, eight=True)
    sizes = sorted(len(c) for c in comps)
    diam = [2 * math.sqrt(s / math.pi) for s in sizes]
    ok = p.lo <= cov <= p.hi
    print(f"  pattern_{p.name:12s} coverage {cov:.3f} (target {p.lo:.2f}-{p.hi:.2f}{'' if ok else ' MISSED'}) "
          f"otsu {t} crop {lengths} level {level:+.1f} roll {roll} seam {ratio:.2f} | {len(comps)} shapes, "
          f"smallest {diam[0] if diam else 0:.1f} px, median {diam[len(diam) // 2] if diam else 0:.1f} px "
          f"-> {out}")
    return {"key": f"pattern_{p.name}", "coverage": cov, "ok": ok, "seam": ratio, "shapes": len(comps)}


# ---------------------------------------------------------------------------------------------- horns

def process_horn(name: str) -> dict | None:
    src = RAW / f"horn_{name}.png"
    if not src.exists():
        print(f"  horn_{name}: no raw image (generate it first)")
        return None
    work = Image.open(src).convert("L").resize((WORK, WORK), Image.BOX).tobytes()
    grid = [[float(work[y * WORK + x]) for x in range(WORK)] for y in range(WORK)]
    periodic, lengths = wrap_field(grid, values=True)
    h, w = len(periodic), len(periodic[0])
    big = Image.new("F", (w, h))
    big.putdata([v for row in periodic for v in row])
    small = array.array("f", big.resize((TILE, TILE), Image.BOX).tobytes())
    g0 = [[max(0.0, min(255.0, small[y * TILE + x])) for x in range(TILE)] for y in range(TILE)]
    opaque = [[255.0] * TILE for _ in range(TILE)]
    planes = heal_seams.heal([g0, [list(r) for r in g0], [list(r) for r in g0], opaque])
    lum = sorted(v for row in planes[0] for v in row)
    lo, hi = lum[int(0.02 * len(lum))], lum[int(0.98 * len(lum)) - 1]
    span = max(1.0, hi - lo)
    # light-to-mid grey: the 2nd..98th percentile onto 0.40..0.92
    a, b = 0.40 * 255, 0.92 * 255

    def level(v: float) -> float:
        return max(0.0, min(255.0, a + (v - lo) / span * (b - a)))

    g = [[level(v) for v in row] for row in planes[0]]
    out_planes = [g, [list(r) for r in g], [list(r) for r in g], [[255.0] * TILE for _ in range(TILE)]]
    out = OUT / f"horn_{name}.png"
    heal_seams.to_image(out_planes).save(out)
    ratio = heal_seams.seam_ratio(heal_seams.to_planes_rounded(out_planes))[0]
    mean = sum(v for row in g for v in row) / (TILE * TILE) / 255
    print(f"  horn_{name:9s} crop {lengths} mean {mean:.2f} seam {ratio:.2f} -> {out}")
    return {"key": f"horn_{name}", "seam": ratio, "mean": mean}


# ---------------------------------------------------------------------------------------------- CLI

def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the creature coat pattern masks and horn tiles.")
    ap.add_argument("--set", choices=("patterns", "horns"), required=True)
    ap.add_argument("--only", help="just these comma-separated names (e.g. giraffe,tabby)")
    ap.add_argument("--dry-run", action="store_true", help="list the prompts, call nothing")
    ap.add_argument("--process-only", action="store_true", help="only re-run the post-processing of out/creatures/raw/")
    ap.add_argument("--reroll", action="store_true", help="move the existing raw images of --only aside and generate "
                    f"new ones (at most {MAX_REROLLS} times per key)")
    args = ap.parse_args()

    if args.set == "patterns":
        items = [(f"{p.name}", f"{PATTERN_STYLE}, of {p.desc}", p) for p in PATTERNS]
        raw_key = lambda name: f"pattern_{name}"
    else:
        items = [(name, f"{HORN_STYLE}, of {desc}", name) for name, desc in HORNS]
        raw_key = lambda name: f"horn_{name}"
    if args.only is not None:
        wanted = {k.strip() for k in args.only.split(",")}
        unknown = wanted - {name for name, _, _ in items}
        if unknown:
            sys.exit(f"--only: unknown {args.set} {sorted(unknown)}; known: {[n for n, _, _ in items]}")
        items = [i for i in items if i[0] in wanted]
    if args.reroll and args.only is None:
        sys.exit("--reroll needs --only: re-roll the rejected tiles by name, never the whole set")

    print(f"[coat-tiles] {args.set}: {len(items)} tile(s)")
    if args.dry_run:
        for name, prompt, _ in items:
            print(f"  {name}: {prompt}")
        return

    if not args.process_only:
        RAW.mkdir(parents=True, exist_ok=True)
        todo = []
        for name, prompt, _ in items:
            key = raw_key(name)
            if args.reroll and not reject(key):
                continue
            if (RAW / f"{key}.png").exists():
                print(f"  {key}: raw exists, skip generation")
                continue
            todo.append((key, prompt))
        if todo:
            load_dotenv()
            api_key = os.environ.get("OPENAI_API_KEY")
            if not api_key:
                sys.exit("OPENAI_API_KEY is not set.")
            from openai import OpenAI

            client = OpenAI(api_key=api_key)
            for key, prompt in todo:
                generate(client, key, prompt)

    OUT.mkdir(parents=True, exist_ok=True)
    for _, _, spec in items:
        if args.set == "patterns":
            process_pattern(spec)
        else:
            process_horn(spec)


if __name__ == "__main__":
    main()
