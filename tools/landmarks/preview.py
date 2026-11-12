#!/usr/bin/env python3
"""Renders a landmark template to a PNG without Unity: front, side and top orthographic views, an isometric overview and
optional cut-away sections, plus the stats line. Stdlib only (the PNG is written with zlib).

    uv run --no-project python tools/landmarks/preview.py leaning_tower
    uv run --no-project python tools/landmarks/preview.py leaning_tower --cut y=40 --cut z=12
    uv run --no-project python tools/landmarks/preview.py data/landmarks/sample_mine.json --out mine.png
    uv run --no-project python tools/landmarks/preview.py sample_tower --stats-only

A key reads data/landmarks/<key>.json; a path may also be an editor export folder (landmark_exports/<key>). The PNG goes
to artifacts/landmark-previews/<key>.png (git-ignored) unless --out says otherwise.

Views: front looks from -Z (x to the right), side from +X (z to the right), top from above (x right, z up); `iso` from
the (-X, +Y, -Z) corner. `--cut y=40` adds a plan of everything at or below row 40, `--cut z=30` a front view of what lies
at or behind z = 30, `--cut x=50` a side view of what lies at or below x = 50 — the way to look into rooms and stairs.
Colours: data/blocks.json `color`, else the average of the block's texture (client/Assets/Resources/textures), else a
hash; tint and glow as stored. Shaped cells are drawn at 4 x 4 x 4 sub-cells in the orthographic views. Overlays:
ground row (green dashes), ruin zones (coloured boxes), markers (squares: chest orange, sound cyan, den red, vendor yellow,
door brown, other magenta); cavity cells (air carved underground) dim violet where no block hides them.
"""
from __future__ import annotations

import argparse
import itertools
import struct
import sys
import zlib
from pathlib import Path

try:
    from . import blockfont, templatefile
    from . import shapecode as sc
except ImportError:
    import blockfont
    import shapecode as sc
    import templatefile

RGB = tuple[int, int, int]
BG = (24, 27, 34)
PANEL_BG = (36, 41, 52)
SKY = (52, 60, 76)
TEXT = (225, 228, 235)
GROUND = (90, 200, 110)
CAVITY = (88, 64, 128)
OUTLINE = 0.62
MARKER_COLOURS = {"chest": (240, 150, 40), "sound": (60, 220, 240), "den": (235, 60, 60), "vendor": (250, 220, 50),
                  "npc": (250, 180, 160)}
ZONE_COLOURS = [(255, 120, 60), (80, 170, 255), (190, 110, 255), (255, 230, 90), (100, 240, 200), (255, 120, 190)]
TOKENS = {"@wall": (150, 140, 125), "@accent": (110, 160, 200), "@roof": (150, 80, 60), "@floor": (130, 115, 95),
          "@path": (160, 150, 130)}
TEXTURES = templatefile.REPO / "client" / "Assets" / "Resources" / "textures"


# --- an RGB image ---------------------------------------------------------------------------------------------------
class Image:
    def __init__(self, width: int, height: int, colour: RGB = BG) -> None:
        self.w, self.h = max(1, width), max(1, height)
        self.buf = bytearray(bytes(colour) * (self.w * self.h))

    def rect(self, x: int, y: int, w: int, h: int, c: RGB) -> None:
        x0, y0, x1, y1 = max(0, x), max(0, y), min(self.w, x + w), min(self.h, y + h)
        if x1 <= x0 or y1 <= y0:
            return
        row = bytes(c) * (x1 - x0)
        for yy in range(y0, y1):
            o = (yy * self.w + x0) * 3
            self.buf[o:o + len(row)] = row

    def px(self, x: int, y: int, c: RGB) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            o = (y * self.w + x) * 3
            self.buf[o:o + 3] = bytes(c)

    def darken(self, x: int, y: int, f: float) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            o = (y * self.w + x) * 3
            self.buf[o] = int(self.buf[o] * f)
            self.buf[o + 1] = int(self.buf[o + 1] * f)
            self.buf[o + 2] = int(self.buf[o + 2] * f)

    def frame(self, x: int, y: int, w: int, h: int, c: RGB, dashed: bool = False) -> None:
        for i in range(w):
            if not dashed or (i // 4) % 2 == 0:
                self.px(x + i, y, c)
                self.px(x + i, y + h - 1, c)
        for j in range(h):
            if not dashed or (j // 4) % 2 == 0:
                self.px(x, y + j, c)
                self.px(x + w - 1, y + j, c)

    def text(self, x: int, y: int, s: str, c: RGB = TEXT, k: int = 2) -> int:
        for col, row in blockfont.ink(s):
            self.rect(x + col * k, y + row * k, k, k, c)
        return blockfont.line_width(s) * k

    def paste(self, other: Image, x: int, y: int) -> None:
        for yy in range(other.h):
            ty = y + yy
            if not 0 <= ty < self.h:
                continue
            x0, x1 = max(0, x), min(self.w, x + other.w)
            if x1 <= x0:
                continue
            src = (yy * other.w + (x0 - x)) * 3
            dst = (ty * self.w + x0) * 3
            self.buf[dst:dst + (x1 - x0) * 3] = other.buf[src:src + (x1 - x0) * 3]

    def png(self) -> bytes:
        def chunk(tag: bytes, data: bytes) -> bytes:
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        stride = self.w * 3
        raw = b"".join(b"\x00" + bytes(self.buf[y * stride:(y + 1) * stride]) for y in range(self.h))
        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))

    def save(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.png())


# --- colours --------------------------------------------------------------------------------------------------------
_COLOURS: dict[str, RGB] = {}


def _split(v: int) -> RGB:
    return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)


def block_colour(key: str) -> RGB:
    """The colour a block is drawn in: its `color`, else its texture's average, else a stable hash colour."""
    if key in _COLOURS:
        return _COLOURS[key]
    c: RGB | None = TOKENS.get(key)
    if c is None:
        try:
            by_key = {b["key"]: b for b in templatefile.blocks()}
        except (OSError, ValueError):
            by_key = {}
        if by_key.get(key, {}).get("color") is not None:
            c = _split(int(by_key[key]["color"]))
    if c is None:
        tex = TEXTURES / f"{key}.bytes"
        if tex.exists():
            data = tex.read_bytes()
            n = r = g = b = 0
            for i in range(0, len(data) - 3, 4 * 3):  # every third pixel is plenty for an average
                if data[i + 3] > 32:
                    r += data[i]
                    g += data[i + 1]
                    b += data[i + 2]
                    n += 1
            if n:
                c = (r // n, g // n, b // n)
    if c is None:
        h = zlib.crc32(key.encode("utf-8"))
        c = (80 + h % 150, 80 + (h >> 8) % 150, 80 + (h >> 16) % 150)
    _COLOURS[key] = c
    return c


def look_colour(look: tuple) -> RGB:
    kind, key, tint, glow, _, _ = look
    if kind == "cavity":
        return CAVITY
    r, g, b = block_colour(key)
    if tint:
        lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255
        f = 0.55 + 0.6 * lum
        tr, tg, tb = _split(tint)
        r, g, b = min(255, int(tr * f)), min(255, int(tg * f)), min(255, int(tb * f))
    if glow:
        gr, gg, gb = _split(glow)
        r, g, b = min(255, (r + gr) // 2 + 40), min(255, (g + gg) // 2 + 40), min(255, (b + gb) // 2 + 40)
    return (r, g, b)


def shade(c: RGB, f: float) -> RGB:
    return (min(255, int(c[0] * f)), min(255, int(c[1] * f)), min(255, int(c[2] * f)))


# --- orthographic views -----------------------------------------------------------------------------------------------
# A view maps a cell to (u, v, depth): u right, v down, depth 0 = nearest; and a sub-cell (i, j, k) the same way.
def _views(w: int, h: int, ln: int) -> dict:
    return {
        "front": (w, h, ln, lambda x, y, z: (x, h - 1 - y, z), lambda i, j, k: (i, sc.N - 1 - j, k)),
        "side": (ln, h, w, lambda x, y, z: (z, h - 1 - y, w - 1 - x), lambda i, j, k: (k, sc.N - 1 - j, sc.N - 1 - i)),
        "top": (w, ln, h, lambda x, y, z: (x, ln - 1 - z, h - 1 - y), lambda i, j, k: (i, sc.N - 1 - k, sc.N - 1 - j)),
    }


_PROJ: dict[tuple[int, str], dict[tuple[int, int], int]] = {}


def _projected(descriptor: int, view: str, sub) -> dict[tuple[int, int], int]:
    key = (descriptor & 0x7FF, view)
    hit = _PROJ.get(key)
    if hit is None:
        hit = {}
        for i, j, k in sc.occupancy(descriptor):
            u, v, d = sub(i, j, k)
            if d < hit.get((u, v), sc.N):
                hit[(u, v)] = d
        _PROJ[key] = hit
    return hit


def ortho(cells: dict, markers: list[dict], t: dict, view: str, s: int, keep=None, show_cavity: bool = True,
          overlay: bool = True) -> Image:
    """One orthographic view at `s` pixels per cell; `keep` filters the cells (a section)."""
    w, h, ln = t["width"], t["height"], t["length"]
    uw, vh, dmax, cell_map, sub = _views(w, h, ln)[view]
    nearest: dict[tuple[int, int], tuple[int, tuple]] = {}
    hollow: dict[tuple[int, int], tuple[int, tuple]] = {}
    shaped: dict[tuple[int, int], list[tuple[int, tuple]]] = {}
    for pos, look in cells.items():
        if keep is not None and not keep(pos):
            continue
        u, v, d = cell_map(*pos)
        if look[0] == "cavity":
            if show_cavity and d < hollow.get((u, v), (dmax + 1,))[0]:
                hollow[(u, v)] = (d, look)
            continue
        if look[4] and sc.shape_of(look[4]) != sc.CUBE and sc.shape_of(look[4]) in sc.SHAPES.values():
            shaped.setdefault((u, v), []).append((d, look))
        elif d < nearest.get((u, v), (dmax + 1,))[0]:
            nearest[(u, v)] = (d, look)
    img = Image(uw * s, vh * s, SKY)
    depth_px: dict[tuple[int, int], float] = {}

    def lit(look: tuple, d: float) -> RGB:
        return shade(look_colour(look), 1.0 - 0.5 * min(1.0, d / max(1, dmax)))
    for (u, v), (d, look) in hollow.items():
        if (u, v) not in nearest:
            img.rect(u * s, v * s, s, s, shade(CAVITY, 1.0 - 0.4 * min(1.0, d / max(1, dmax))))
    for (u, v), (d, look) in nearest.items():
        img.rect(u * s, v * s, s, s, lit(look, d))
        depth_px[(u, v)] = d
    for (u, v), entries in shaped.items():
        front = nearest.get((u, v), (dmax + 1,))[0]
        for d, look in sorted(entries, key=lambda e: -e[0]):
            if d >= front:
                continue
            proj = _projected(look[4], view, sub)
            col = look_colour(look)
            for py in range(s):
                for px_ in range(s):
                    sd = proj.get((px_ * sc.N // s, py * sc.N // s))
                    if sd is not None:
                        img.px(u * s + px_, v * s + py, shade(col, 1.0 - 0.5 * min(1.0, (d + sd / sc.N) / max(1, dmax))))
            depth_px[(u, v)] = min(depth_px.get((u, v), d), d)
    if s >= 3:  # outlines where the depth jumps: the eye reads edges, not flat colour
        for (u, v), d in depth_px.items():
            for du, dv, edge in ((1, 0, "r"), (-1, 0, "l"), (0, 1, "b"), (0, -1, "t")):
                nd = depth_px.get((u + du, v + dv))
                if nd is None or abs(nd - d) > 1.5:
                    for i in range(s):
                        x, y = {"r": (u * s + s - 1, v * s + i), "l": (u * s, v * s + i),
                                "b": (u * s + i, v * s + s - 1), "t": (u * s + i, v * s)}[edge]
                        img.darken(x, y, OUTLINE)
    if overlay:
        _ortho_overlay(img, t, markers, view, s, cell_map, keep)
    return img


def _ortho_overlay(img: Image, t: dict, markers: list[dict], view: str, s: int, cell_map, keep) -> None:
    h = t["height"]
    g = int(t.get("groundRow", 0) or 0)
    if view in ("front", "side") and g > 0:
        y = (h - g) * s
        for x in range(0, img.w, 6):
            img.rect(x, y - 1, 3, 2, GROUND)
    for i, zone in enumerate(t.get("ruinZones", []) or []):
        a = cell_map(zone["x0"], zone["y0"], zone["z0"])
        b = cell_map(zone["x1"], zone["y1"], zone["z1"])
        u0, u1 = sorted((a[0], b[0]))
        v0, v1 = sorted((a[1], b[1]))
        c = ZONE_COLOURS[i % len(ZONE_COLOURS)]
        img.frame(u0 * s, v0 * s, (u1 - u0 + 1) * s, (v1 - v0 + 1) * s, c, dashed=True)
        img.text(u0 * s + 3, v0 * s + 3, f"{i}:{zone.get('profile', '')}", c, 1)
    size = max(3, s)
    for m in markers:
        pos = (m["x"], m["y"], m["z"])
        if keep is not None and not keep(pos):
            continue
        u, v, _ = cell_map(*pos)
        mid = m.get("id", "")
        c = MARKER_COLOURS.get(mid, (150, 100, 60) if mid.startswith("door") else (235, 80, 235))
        x0, y0 = u * s + s // 2 - size // 2, v * s + s // 2 - size // 2
        img.rect(x0 - 1, y0 - 1, size + 2, size + 2, (0, 0, 0))
        img.rect(x0, y0, size, size, c)


# --- the isometric overview -------------------------------------------------------------------------------------------
def _sprite(s: int) -> list[tuple[int, int, int, int]]:
    """The spans (dy, x0, x1, face) of a unit cube at `s` pixels: face 0 top, 1 the -X side, 2 the -Z side, relative to
    the projection of the cube's (x, y, z) corner."""
    hs = s / 2

    def p(x: float, y: float, z: float) -> tuple[float, float]:
        return ((x - z) * s, -(x + z) * hs - y * s)
    faces = [
        [p(0, 1, 0), p(1, 1, 0), p(1, 1, 1), p(0, 1, 1)],
        [p(0, 0, 0), p(0, 1, 0), p(0, 1, 1), p(0, 0, 1)],
        [p(0, 0, 0), p(1, 0, 0), p(1, 1, 0), p(0, 1, 0)],
    ]

    def inside(poly, x: float, y: float) -> bool:
        sign = 0
        for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
            cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax)
            if abs(cross) < 1e-9:
                continue
            if sign == 0:
                sign = 1 if cross > 0 else -1
            elif (cross > 0) != (sign > 0):
                return False
        return True
    xs = [q[0] for f in faces for q in f]
    ys = [q[1] for f in faces for q in f]
    spans = []
    for py in range(int(min(ys)) - 1, int(max(ys)) + 2):
        run = None
        for px_ in range(int(min(xs)) - 1, int(max(xs)) + 2):
            face = next((i for i, f in enumerate(faces) if inside(f, px_ + 0.5, py + 0.5)), -1)
            if run and (face != run[2]):
                if run[2] >= 0:
                    spans.append((py, run[0], px_, run[2]))
                run = None
            if run is None and face >= 0:
                run = [px_, px_, face]
        if run and run[2] >= 0:
            spans.append((py, run[0], int(max(xs)) + 2, run[2]))
    return spans


def _is_full(v: tuple | None) -> bool:
    return v is not None and not (v[4] and sc.shape_of(v[4]) != sc.CUBE and sc.shape_of(v[4]) in sc.SHAPES.values())


def iso(cells: dict, t: dict, s: int, overlay: bool = True, show_cavity: bool = True) -> Image:
    """The isometric overview: blocks as shaded cubes (shaped cells as half-size sub-cubes), drawn far to near; cavity
    cells first, dimmed, as a ghost of the tunnels and rooms behind everything."""
    w, h, ln = t["width"], t["height"], t["length"]
    hs = s / 2
    blocks = {p: v for p, v in cells.items() if v[0] == "block"}
    hollow = {p for p, v in cells.items() if v[0] == "cavity"} if show_cavity else set()

    def exposed(p: tuple, solid) -> bool:
        return not (solid((p[0], p[1] + 1, p[2])) and solid((p[0] - 1, p[1], p[2])) and solid((p[0], p[1], p[2] - 1)))
    def order(p: tuple) -> int:  # far to near along the view direction (1, -1, 1)
        return -(p[0] + p[2] - p[1])
    ghosts = sorted((p for p in hollow if exposed(p, lambda q: q in hollow)), key=order)
    visible = sorted((p for p in blocks if exposed(p, lambda q: _is_full(blocks.get(q)))), key=order)
    ox = ln * s + 4
    oy = int((w + ln) * hs + h * s) + 4
    img = Image(int((w + ln) * s) + 8, int((w + ln) * hs + h * s) + 8, SKY)
    full_sprite = _sprite(s)
    sub_sprite = _sprite(s // 2) if s // 2 >= 1 else None
    light = (1.0, 0.78, 0.6)

    def draw(sprite, ax: float, ay: float, col: RGB) -> None:
        cols = [bytes(shade(col, f)) for f in light]
        bx, by = round(ax), round(ay)
        for dy, x0, x1, face in sprite:
            y = by + dy
            if not 0 <= y < img.h:
                continue
            a, b = max(0, bx + x0), min(img.w, bx + x1)
            if b > a:
                o = (y * img.w + a) * 3
                img.buf[o:o + (b - a) * 3] = cols[face] * (b - a)
    for x, y, z in ghosts:
        draw(full_sprite, ox + (x - z) * s, oy - (x + z) * hs - y * s, shade(CAVITY, 0.7))
    if overlay:
        g = int(t.get("groundRow", 0) or 0)
        corners = [(0, 0), (w, 0), (w, ln), (0, ln), (0, 0)]
        for (ax, az), (bx, bz) in itertools.pairwise(corners):
            steps = int(max(abs(bx - ax), abs(bz - az)) * s) or 1
            for i in range(0, steps, 2):
                fx, fz = ax + (bx - ax) * i / steps, az + (bz - az) * i / steps
                img.px(int(ox + (fx - fz) * s), int(oy - (fx + fz) * hs - g * s), GROUND)
    for x, y, z in visible:
        look = blocks[(x, y, z)]
        col = look_colour(look)
        if _is_full(look) or sub_sprite is None:
            draw(full_sprite, ox + (x - z) * s, oy - (x + z) * hs - y * s, col)
            continue
        half = {(i // 2, j // 2, k // 2) for i, j, k in sc.occupancy(look[4])}
        for i, j, k in sorted(half, key=order):
            fx, fy, fz = x + i / 2, y + j / 2, z + k / 2
            draw(sub_sprite, ox + (fx - fz) * s, oy - (fx + fz) * hs - fy * s, col)
    return img


# --- the sheet ----------------------------------------------------------------------------------------------------------
def _parse_cut(text: str) -> tuple[str, int]:
    axis, _, value = text.partition("=")
    if axis not in ("x", "y", "z") or not value.lstrip("-").isdigit():
        raise ValueError(f"bad cut '{text}' (x=N, y=N or z=N)")
    return axis, int(value)


def render(t: dict, scale: int | None = None, iso_scale: int | None = None, views: tuple[str, ...] = ("front", "side", "top", "iso"),
           cuts: tuple[str, ...] = (), show_cavity: bool = True, overlay: bool = True, nbytes: int | None = None) -> Image:
    """The preview sheet of a template: a title with the stats line and one labelled panel per view and cut."""
    cells, markers = templatefile.decode(t)
    w, h, ln = t["width"], t["height"], t["length"]
    s = scale or max(1, min(10, 900 // max(w, h, ln, 1)))
    si = iso_scale or max(2, min(8, (1300 // max(1, w + ln)) // 2 * 2))
    panels: list[tuple[str, Image]] = []
    names = {"front": "FRONT (from -Z, x right)", "side": "SIDE (from +X, z right)", "top": "TOP (x right, z up)"}
    for v in views:
        if v == "iso":
            panels.append((f"ISO (from -X +Y -Z)  {si}px", iso(cells, t, si, overlay, show_cavity)))
        elif v in names:
            panels.append((f"{names[v]}  {s}px", ortho(cells, markers, t, v, s, None, show_cavity, overlay)))
        else:
            raise ValueError(f"unknown view '{v}' (front | side | top | iso)")
    for c in cuts:
        axis, value = _parse_cut(c)
        view, keep, label = {
            "y": ("top", lambda p, v=value: p[1] <= v, f"PLAN at y <= {value}"),
            "z": ("front", lambda p, v=value: p[2] >= v, f"SECTION z >= {value} (from -Z)"),
            "x": ("side", lambda p, v=value: p[0] <= v, f"SECTION x <= {value} (from +X)"),
        }[axis]
        panels.append((label, ortho(cells, markers, t, view, s, keep, show_cavity, overlay)))
    pad, title_h, label_k = 12, 26, 2
    stats_line = templatefile.stats(t, nbytes)
    slots = [(label, p, max(p.w, blockfont.line_width(label) * label_k)) for label, p in panels]
    max_row = max(1800, max(sw for _, _, sw in slots) + 2 * pad)
    layout, x, y, row_h = [], pad, 70, 0
    for label, p, sw in slots:
        if x > pad and x + sw + pad > max_row:
            x, y, row_h = pad, y + row_h + title_h + pad, 0
        layout.append((label, p, x, y, sw))
        x += sw + pad
        row_h = max(row_h, p.h)
    width = max(max(px_ + sw for _, _, px_, _, sw in layout) + pad, blockfont.line_width(stats_line) + 2 * pad, 600)
    height = y + row_h + title_h + pad
    sheet = Image(width, height, BG)
    sheet.text(pad, 12, f"{t.get('key', '?')}  {t.get('name', '')}", TEXT, 3)
    sheet.text(pad, 44, stats_line, (170, 178, 190), 1)
    for label, p, px_, py, sw in layout:
        sheet.rect(px_ - 2, py - 2, sw + 4, p.h + title_h + 4, PANEL_BG)
        sheet.text(px_, py + 4, label, TEXT, label_k)
        sheet.paste(p, px_, py + title_h)
    return sheet


def preview(ref: str | Path, out: str | Path | None = None, **opts) -> Path:
    """Renders a template file (key or path) and saves the PNG; returns its path."""
    t, path = templatefile.load(ref)
    target = Path(out) if out else templatefile.REPO / "artifacts" / "landmark-previews" / f"{t.get('key', path.stem)}.png"
    if target.suffix.lower() != ".png":
        target = target / f"{t.get('key', path.stem)}.png"
    render(t, nbytes=path.stat().st_size, **opts).save(target)
    return target


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Render a landmark template to a PNG (front/side/top/iso + sections).")
    ap.add_argument("refs", nargs="+", help="template key (data/landmarks/<key>.json), .json path or export folder")
    ap.add_argument("--out", help="PNG file (one template) or folder; default artifacts/landmark-previews/")
    ap.add_argument("--scale", type=int, help="pixels per cell in the orthographic views (default: fit ~900 px)")
    ap.add_argument("--iso-scale", type=int, help="pixels per cell in the isometric view (even; default: fit)")
    ap.add_argument("--views", default="front,side,top,iso", help="comma list of front, side, top, iso")
    ap.add_argument("--cut", action="append", default=[], help="a section: y=N (plan), z=N or x=N; repeatable")
    ap.add_argument("--no-cavity", action="store_true", help="do not draw cavity cells")
    ap.add_argument("--no-overlay", action="store_true", help="no ground row, ruin zones or markers")
    ap.add_argument("--stats-only", action="store_true", help="print the stats line, write no PNG")
    args = ap.parse_args(argv)
    if args.out and len(args.refs) > 1 and args.out.lower().endswith(".png"):
        ap.error("--out must be a folder when more than one template is rendered")
    for ref in args.refs:
        t, path = templatefile.load(ref)
        print(templatefile.stats(t, path.stat().st_size))
        if args.stats_only:
            continue
        target = preview(ref, args.out, scale=args.scale, iso_scale=args.iso_scale,
                         views=tuple(v for v in args.views.split(",") if v), cuts=tuple(args.cut),
                         show_cavity=not args.no_cavity, overlay=not args.no_overlay)
        print(f"  -> {target}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
