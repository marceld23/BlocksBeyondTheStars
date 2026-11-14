"""Pure geometry for the GIANT SHIP scripts (tools/giant_ships/<key>.py, #3617): cross-sections, the loft that turns
them into a hull, the `Hull` with its inside / shell / surface queries, and tubes swept along curves. Nothing here
writes a design — `ships.ShipDesign` fills these cells with materials (see tools/landmarks/README.md, "Giant ships").

Conventions (the same as `solids`)
- Template cells: x = across the ship, y = up, z = along it. The BOW points to +z, the stern lies at the low z.
- A cell belongs to an outline / a tube when its CENTRE lies inside or on it. An outline `D` cells wide whose first
  cell is x0 has its centre at x0 + (D - 1) / 2 and a half-width of D / 2 (`solids.span(x0, D)`), so its rim falls
  between cells and nothing hangs on rounding.
- Everything is deterministic: no randomness, no Python `hash`. Ships come out symmetric about their centre line
  (`loft(..., symmetric=True)` rasterises one half and mirrors it).

Internally a hull is kept as SPANS — per slice z, per row y, the sorted runs (x0, x1) of its cells — so the inside of a
2-thick shell around a 256-long hull is a few span intersections, not millions of neighbour lookups.
"""
from __future__ import annotations

import bisect
import itertools
import math
from collections.abc import Callable, Iterable, Sequence
from typing import NamedTuple

Cell = tuple[int, int, int]
Point = tuple[float, float]
Vec = tuple[float, float, float]
Spans = list[tuple[int, int]]            # sorted, disjoint, inclusive (x0, x1) runs of one row
SliceSpans = dict[int, Spans]            # row y -> spans
EPS = 1e-6


# --- spans -----------------------------------------------------------------------------------------------------------
def _merge(runs: Iterable[tuple[int, int]]) -> Spans:
    """Sorted, disjoint runs; touching runs (x1 + 1 == next x0) are joined."""
    out: Spans = []
    for a, b in sorted(runs):
        if b < a:
            continue
        if out and a <= out[-1][1] + 1:
            if b > out[-1][1]:
                out[-1] = (out[-1][0], b)
        else:
            out.append((a, b))
    return out


def _intersect(p: Spans, q: Spans) -> Spans:
    out: Spans = []
    i = j = 0
    while i < len(p) and j < len(q):
        a, b = max(p[i][0], q[j][0]), min(p[i][1], q[j][1])
        if a <= b:
            out.append((a, b))
        if p[i][1] < q[j][1]:
            i += 1
        else:
            j += 1
    return out


def _subtract(p: Spans, q: Spans) -> Spans:
    """The runs of p minus the runs of q."""
    out: Spans = []
    j = 0
    for a, b in p:
        while j < len(q) and q[j][1] < a:
            j += 1
        k, cur = j, a
        while k < len(q) and q[k][0] <= b:
            if q[k][0] > cur:
                out.append((cur, q[k][0] - 1))
            cur = max(cur, q[k][1] + 1)
            k += 1
        if cur <= b:
            out.append((cur, b))
    return out


def _shrink(p: Spans, w: int) -> Spans:
    return [(a + w, b - w) for a, b in p if a + w <= b - w]


def _erode_slice(s: SliceSpans, w: int) -> SliceSpans:
    """A chessboard erosion by `w` (a (2w+1)^2 square) of one slice: shrink every run, then intersect the rows around."""
    xs = {y: _shrink(runs, w) for y, runs in s.items()}
    out: SliceSpans = {}
    for y, runs in xs.items():
        keep = runs
        for k in range(1, w + 1):
            if not keep:
                break
            keep = _intersect(keep, xs.get(y - k, []))
            if keep:
                keep = _intersect(keep, xs.get(y + k, []))
        if keep:
            out[y] = keep
    return out


def _slices_from_cells(cells: Iterable[Cell]) -> dict[int, SliceSpans]:
    rows: dict[int, dict[int, list[int]]] = {}
    for x, y, z in cells:
        rows.setdefault(z, {}).setdefault(y, []).append(x)
    out: dict[int, SliceSpans] = {}
    for z, by_y in rows.items():
        out[z] = {y: _merge((x, x) for x in xs) for y, xs in by_y.items()}
    return out


def _cells_of(slices: dict[int, SliceSpans]) -> set[Cell]:
    return {(x, y, z) for z, s in slices.items() for y, runs in s.items() for a, b in runs for x in range(a, b + 1)}


# --- cross-sections --------------------------------------------------------------------------------------------------
def _area(pts: Sequence[Point]) -> float:
    return 0.5 * sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, list(pts[1:]) + [pts[0]]))


class Section:
    """A closed cross-section outline in the plane of one slice: x across the ship, y up (template coordinates, so a
    section already sits where it belongs). Build one with `ellipse`, `superellipse`, `rounded_rect`, `ship_section` or
    `polygon`; `moved` / `scaled` give changed copies. The loft interpolates between sections point by point."""

    def __init__(self, points: Sequence[Point]) -> None:
        pts = [(float(x), float(y)) for x, y in points]
        if len(pts) > 1 and pts[0] == pts[-1]:
            pts.pop()
        if len(pts) < 3:
            raise ValueError("a section needs at least three points")
        if _area(pts) < 0:
            pts.reverse()  # counter-clockwise (x right, y up)
        self.points = pts

    @property
    def bounds(self) -> tuple[float, float, float, float]:
        """(xmin, ymin, xmax, ymax)."""
        xs = [p[0] for p in self.points]
        ys = [p[1] for p in self.points]
        return min(xs), min(ys), max(xs), max(ys)

    @property
    def centre_x(self) -> float:
        b = self.bounds
        return (b[0] + b[2]) / 2

    def moved(self, dx: float = 0.0, dy: float = 0.0) -> Section:
        return Section([(x + dx, y + dy) for x, y in self.points])

    def scaled(self, fx: float, fy: float | None = None, about: Point | None = None) -> Section:
        """Scaled by fx across and fy up (default fx) about a point (default the centre of the bounds)."""
        fy = fx if fy is None else fy
        b = self.bounds
        ax, ay = about if about is not None else ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2)
        return Section([(ax + (x - ax) * fx, ay + (y - ay) * fy) for x, y in self.points])

    def resample(self, n: int) -> list[Point]:
        """`n` points evenly spaced along the outline, counter-clockwise, starting at the KEEL — the lowest point where
        the vertical centre line (the middle of the bounds) crosses the outline. Sections that start at the same place
        and run the same way interpolate into a smooth hull."""
        pts = self.points
        cx = self.centre_x
        start, best = None, math.inf
        m = len(pts)
        for i in range(m):
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % m]
            if (x0 - cx) * (x1 - cx) > 0:
                continue  # the edge does not reach the centre line
            if x0 == x1:  # an edge lying on the centre line: its lower end
                y, t = min(y0, y1), (0.0 if y0 <= y1 else 1.0)
            else:
                t = (cx - x0) / (x1 - x0)
                if not -EPS <= t <= 1 + EPS:
                    continue
                y = y0 + (y1 - y0) * t
            if y < best:
                best, start = y, (i, min(1.0, max(0.0, t)))
        if start is None:
            start = (0, 0.0)
        i, t = start
        p0 = (pts[i][0] + (pts[(i + 1) % m][0] - pts[i][0]) * t, pts[i][1] + (pts[(i + 1) % m][1] - pts[i][1]) * t)
        ring = [p0] + [pts[(i + 1 + k) % m] for k in range(m)] + [p0]
        lengths = [math.dist(a, b) for a, b in itertools.pairwise(ring)]
        total = sum(lengths)
        out: list[Point] = []
        seg, acc = 0, 0.0
        for k in range(n):
            target = total * k / n
            while seg < len(lengths) - 1 and acc + lengths[seg] < target:
                acc += lengths[seg]
                seg += 1
            f = 0.0 if lengths[seg] == 0 else (target - acc) / lengths[seg]
            a, b = ring[seg], ring[seg + 1]
            out.append((a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f))
        return out


def ellipse(cx: float, cy: float, rx: float, ry: float, n: int = 192) -> Section:
    """An elliptic section around (cx, cy) with half-widths rx (across) and ry (up)."""
    return Section([(cx + rx * math.cos(2 * math.pi * k / n), cy + ry * math.sin(2 * math.pi * k / n)) for k in range(n)])


def superellipse(cx: float, cy: float, rx: float, ry: float, p: float = 4.0, n: int = 192) -> Section:
    """|x/rx|^p + |y/ry|^p = 1: p = 2 an ellipse, 3..6 a rounded box (a liner's or a freighter's hull), large = a box."""
    def f(c: float) -> float:
        return math.copysign(abs(c) ** (2.0 / p), c)
    return Section([(cx + rx * f(math.cos(2 * math.pi * k / n)), cy + ry * f(math.sin(2 * math.pi * k / n))) for k in range(n)])


def rounded_rect(cx: float, cy: float, w: float, h: float, r: float = 0.0, arc: int = 12) -> Section:
    """A box section `w` wide and `h` high centred on (cx, cy) (its rim half a cell outside the outer cell centres when
    cx = x0 + (w - 1) / 2), corners rounded with radius `r`."""
    a, b = w / 2, h / 2
    r = max(0.0, min(r, a, b))
    pts: list[Point] = []
    for qx, qy, start in ((1, 1, 0.0), (-1, 1, 0.5), (-1, -1, 1.0), (1, -1, 1.5)):
        ox, oy = cx + qx * (a - r), cy + qy * (b - r)
        steps = arc if r > 0 else 0
        for k in range(steps + 1):
            ang = math.pi * (start + 0.5 * k / max(1, steps))
            pts.append((ox + r * math.cos(ang), oy + r * math.sin(ang)))
    return Section(pts)


def ship_section(cx: float, keel: float, deck: float, beam: float, bottom: float | None = None, bilge: float = 0.35,
                 flare: float = 0.0, arc: int = 10) -> Section:
    """A ship's hull section: a flat bottom `bottom` wide on the keel row (default half the beam), a round bilge whose
    radius is `bilge` x the height, sides rising to the deck row where the hull is `beam` + `flare` wide. Rows keel and
    deck are the outermost ROWS of cell centres (the rim lies half a cell beyond them)."""
    y0, y1 = keel - 0.5, deck + 0.5
    hb = beam / 2
    hbottom = (beam / 2 if bottom is None else bottom / 2)
    hbottom = min(hbottom, hb)
    htop = hb + flare / 2
    rad = max(0.0, min(bilge * (y1 - y0), hb - hbottom if hb > hbottom else bilge * (y1 - y0)))
    right: list[Point] = [(cx + hbottom, y0)]
    if rad > 0:  # the round bilge: from the bottom edge up to the side
        ox, oy = cx + hb - rad, y0 + rad
        for k in range(arc + 1):
            ang = -math.pi / 2 + (math.pi / 2) * k / arc
            right.append((ox + rad * math.cos(ang), oy + rad * math.sin(ang)))
    else:
        right.append((cx + hb, y0))
    right.append((cx + htop, y1))
    left = [(2 * cx - x, y) for x, y in reversed(right)]
    return Section(right + left)


def polygon(points: Sequence[Point]) -> Section:
    """A section from its corner points (x across, y up), in either order."""
    return Section(points)


# --- rasterising an outline ------------------------------------------------------------------------------------------
def _row_spans(pts: Sequence[Point], y: float) -> list[tuple[float, float]]:
    xs: list[float] = []
    n = len(pts)
    for i in range(n):
        (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
        if (y0 <= y < y1) or (y1 <= y < y0):
            xs.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
    xs.sort()
    return [(xs[k], xs[k + 1]) for k in range(0, len(xs) - 1, 2)]


def raster(pts: Sequence[Point]) -> SliceSpans:
    """The cells (as row spans) whose centre lies inside or on a closed outline (even-odd rule)."""
    ys = [p[1] for p in pts]
    out: SliceSpans = {}
    for y in range(math.ceil(min(ys) - EPS), math.floor(max(ys) + EPS) + 1):
        runs = []
        for yy in (y - 1e-7, y + 1e-7):  # both sides of the row: a rim exactly on a row of centres still counts
            for a, b in _row_spans(pts, yy):
                x0, x1 = math.ceil(a - EPS), math.floor(b + EPS)
                if x0 <= x1:
                    runs.append((x0, x1))
        if runs:
            out[y] = _merge(runs)
    return out


def _mirror_slice(s: SliceSpans, axis: float) -> SliceSpans:
    """The left half (x <= axis) and its mirror image across x = axis (whole or .5)."""
    out: SliceSpans = {}
    for y, runs in s.items():
        left = [(a, min(b, math.floor(axis + EPS))) for a, b in runs if a <= axis + EPS]
        mirrored = [(round(2 * axis - b), round(2 * axis - a)) for a, b in left]
        merged = _merge(left + mirrored)
        if merged:
            out[y] = merged
    return out


# --- the loft --------------------------------------------------------------------------------------------------------
def _tangents(zs: list[float], vals: list[float]) -> list[float]:
    """Monotone cubic tangents (Fritsch-Carlson / Butland): never overshoots, so two equal stations give a straight,
    parallel stretch and a narrowing hull never bulges past its stations."""
    n = len(zs)
    h = [zs[k + 1] - zs[k] for k in range(n - 1)]
    d = [(vals[k + 1] - vals[k]) / h[k] for k in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for k in range(1, n - 1):
        if d[k - 1] * d[k] <= 0:
            continue  # a turning point or a flat stretch: zero slope
        w1, w2 = 2 * h[k] + h[k - 1], h[k] + 2 * h[k - 1]
        m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k])
    return m


def _interp(zs: list[float], vals: list[float], slopes: list[float] | None, z: float) -> float:
    """The value at z between the stations: straight (`slopes` None) or a monotone cubic Hermite curve."""
    i = max(0, min(len(zs) - 2, bisect.bisect_right(zs, z) - 1))
    za, zb = zs[i], zs[i + 1]
    dz = zb - za
    t = 0.0 if dz == 0 else (z - za) / dz
    a, b = vals[i], vals[i + 1]
    if slopes is None:
        return a + (b - a) * t
    t2, t3 = t * t, t * t * t
    return ((2 * t3 - 3 * t2 + 1) * a + (t3 - 2 * t2 + t) * dz * slopes[i] + (-2 * t3 + 3 * t2) * b
            + (t3 - t2) * dz * slopes[i + 1])


def taper_factor(d: float, tip: float, curve: str) -> float:
    """The scale at relative distance d (0 = the tip, 1 = where the taper starts) of a bow / stern taper."""
    d = max(0.0, min(1.0, d))
    if curve == "linear":
        g = d
    elif curve == "ogive":  # a circular arc: full almost to the end, then round to the tip
        g = math.sqrt(max(0.0, 1 - (1 - d) ** 2))
    elif curve == "blunt":
        g = 1 - (1 - d) ** 4
    else:
        raise ValueError(f"unknown taper curve '{curve}' (ogive | linear | blunt)")
    return tip + (1 - tip) * g


def loft(stations: Sequence[tuple[float, Section]], n: int = 128, smooth: bool = True, bow: int = 0, stern: int = 0,
         tip: float = 0.2, curve: str = "ogive", anchor: str = "centre", taper_axes: str = "xy",
         symmetric: bool = True, axis_x: float | None = None) -> Hull:
    """A hull from cross-sections along z: `stations` = (z, Section) pairs (at least two); every whole z between the
    first and the last station gets a slice, its outline interpolated point by point (`smooth` = a monotone cubic
    curve through the stations that never overshoots them, else straight). `bow` / `stern` taper the last / first that many slices toward `tip` x the
    size along `curve` (`ogive` round, `linear` pointed, `blunt` full), scaled about `anchor` — 'centre', 'keel' (the
    bottom stays, the deck drops) or 'deck' (the deck stays level, the keel rises) — in `taper_axes` ('xy', 'x' = only
    narrower, 'y' = only lower). `symmetric` mirrors the left half across `axis_x` (default the first station's centre,
    rounded to .5)."""
    if len(stations) < 2:
        raise ValueError("loft needs at least two stations")
    st = sorted(stations, key=lambda s: s[0])
    zs = [float(z) for z, _ in st]
    if len(set(zs)) != len(zs):
        raise ValueError("loft: two stations at the same z")
    rings = [sec.resample(n) for _, sec in st]
    if axis_x is None:
        axis_x = round(st[0][1].centre_x * 2) / 2
    z_lo, z_hi = math.ceil(zs[0] - EPS), math.floor(zs[-1] + EPS)
    xs = [[r[k][0] for r in rings] for k in range(n)]   # per outline point: its x (and y) at every station
    ys_ = [[r[k][1] for r in rings] for k in range(n)]
    curved = smooth and len(zs) > 2
    mx = [_tangents(zs, v) if curved else None for v in xs]
    my = [_tangents(zs, v) if curved else None for v in ys_]
    slices: dict[int, SliceSpans] = {}
    for z in range(z_lo, z_hi + 1):
        pts = [(_interp(zs, xs[k], mx[k], z), _interp(zs, ys_[k], my[k], z)) for k in range(n)]
        f = 1.0
        if bow > 0 and z > z_hi - bow:
            f = taper_factor((z_hi - z + 0.5) / bow, tip, curve)
        if stern > 0 and z < z_lo + stern:
            f = min(f, taper_factor((z - z_lo + 0.5) / stern, tip, curve))
        if f < 1.0:
            ys = [p[1] for p in pts]
            ay = {"centre": (min(ys) + max(ys)) / 2, "keel": min(ys), "deck": max(ys)}[anchor]
            fx = f if "x" in taper_axes else 1.0
            fy = f if "y" in taper_axes else 1.0
            pts = [(axis_x + (x - axis_x) * fx, ay + (y - ay) * fy) for x, y in pts]
        s = raster(pts)
        if symmetric:
            s = _mirror_slice(s, axis_x)
        if s:
            slices[z] = s
    return Hull(slices, axis_x)


# --- the hull --------------------------------------------------------------------------------------------------------
class Hull:
    """A solid volume kept as slices of row spans, with the queries the ship helpers need. `cells` is the solid; the
    INSIDE for a wall thickness w (`interior(w)`) is every cell whose (2w+1)^3 cube lies in the solid, so the SHELL
    (`shell(w)`, the solid minus that inside) is closed — no corner pinholes — and at least w thick everywhere,
    the bow and stern caps included. `wall` is the thickness the hull was written with (`ShipDesign.loft` sets it);
    the helpers that need "the inside" use it unless told otherwise."""

    def __init__(self, slices: dict[int, SliceSpans], axis_x: float | None = None, wall: int = 0) -> None:
        self.slices = {z: s for z, s in slices.items() if s}
        self.axis_x = axis_x
        self.wall = wall
        self._cells: set[Cell] | None = None
        self._inside: dict[int, dict[int, SliceSpans]] = {}
        self._inside_cells: dict[int, set[Cell]] = {}

    @classmethod
    def from_cells(cls, cells: Iterable[Cell], axis_x: float | None = None, wall: int = 0) -> Hull:
        """A hull from any solid (a capsule, a union of boxes, a sculpture) — so decks, windows and greebles work on
        it too."""
        return cls(_slices_from_cells(cells), axis_x, wall)

    def __or__(self, other: Hull) -> Hull:
        """The union of two hulls (a hull and its superstructure): the inside of the union is computed afresh, so
        walls between the parts disappear."""
        out: dict[int, SliceSpans] = {}
        for z in set(self.slices) | set(other.slices):
            a, b = self.slices.get(z, {}), other.slices.get(z, {})
            out[z] = {y: _merge(a.get(y, []) + b.get(y, [])) for y in set(a) | set(b)}
        return Hull(out, self.axis_x if self.axis_x is not None else other.axis_x, max(self.wall, other.wall))

    def moved(self, dx: int, dy: int = 0, dz: int = 0) -> Hull:
        s = {z + dz: {y + dy: [(a + dx, b + dx) for a, b in runs] for y, runs in sl.items()} for z, sl in self.slices.items()}
        return Hull(s, None if self.axis_x is None else self.axis_x + dx, self.wall)

    # solid and inside
    @property
    def cells(self) -> set[Cell]:
        if self._cells is None:
            self._cells = _cells_of(self.slices)
        return self._cells

    def __contains__(self, p: object) -> bool:
        return isinstance(p, tuple) and len(p) == 3 and self.contains(p)  # type: ignore[arg-type]

    def contains(self, p: Cell, wall: int | None = 0) -> bool:
        """Whether cell p lies in the solid (wall 0) or in the inside for that wall thickness (None = `self.wall`) —
        a span lookup, no cell set is built."""
        runs = self._inside_slices(self.wall if wall is None else wall).get(p[2], {}).get(p[1])
        if not runs:
            return False
        i = bisect.bisect_right(runs, (p[0], math.inf)) - 1
        return i >= 0 and runs[i][0] <= p[0] <= runs[i][1]

    def _inside_slices(self, wall: int) -> dict[int, SliceSpans]:
        if wall <= 0:
            return self.slices
        if wall not in self._inside:
            eroded = {z: _erode_slice(s, wall) for z, s in self.slices.items()}
            out: dict[int, SliceSpans] = {}
            for z, s in eroded.items():
                keep: SliceSpans = {}
                for y, runs in s.items():
                    r = runs
                    for k in range(1, wall + 1):
                        if not r:
                            break
                        r = _intersect(r, eroded.get(z - k, {}).get(y, []))
                        if r:
                            r = _intersect(r, eroded.get(z + k, {}).get(y, []))
                    if r:
                        keep[y] = r
                if keep:
                    out[z] = keep
            self._inside[wall] = out
        return self._inside[wall]

    def interior(self, wall: int | None = None) -> set[Cell]:
        """Every cell at least `wall` (default `self.wall`) cells from the outside — the hollow of a shell that thick."""
        w = self.wall if wall is None else wall
        if w not in self._inside_cells:
            self._inside_cells[w] = _cells_of(self._inside_slices(w))
        return self._inside_cells[w]

    def shell(self, wall: int | None = None) -> set[Cell]:
        """The solid minus its interior: a closed shell `wall` thick (cached; the same set on every call)."""
        w = self.wall if wall is None else wall
        if w <= 0:
            return self.cells
        key = -w  # cached beside the inside sets under a negative key
        if key not in self._inside_cells:
            inner = self._inside_slices(w)
            out: set[Cell] = set()
            for z, sl in self.slices.items():
                isl = inner.get(z, {})
                for y, runs in sl.items():
                    for a, b in _subtract(runs, isl.get(y, [])):
                        out.update((x, y, z) for x in range(a, b + 1))
            self._inside_cells[key] = out
        return self._inside_cells[key]

    def row_cells(self, y: int, wall: int | None = None) -> list[Cell]:
        """Every cell of row y in the inside for `wall` (None = `self.wall`; 0 = the solid), slice by slice."""
        out: list[Cell] = []
        for z, sl in sorted(self._inside_slices(self.wall if wall is None else wall).items()):
            for a, b in sl.get(y, []):
                out.extend((x, y, z) for x in range(a, b + 1))
        return out

    def below(self, y: int) -> list[Cell]:
        """Every cell of the solid on the rows below y."""
        return [(x, yy, z) for z, sl in self.slices.items() for yy, runs in sl.items() if yy < y
                for a, b in runs for x in range(a, b + 1)]

    def z_extent(self, x: int, y: int, wall: int | None = 0) -> tuple[int, int] | None:
        """(zmin, zmax) of the cells at (x, y) — of the solid or of the inside — or None."""
        zs = [z for z in self.slices if self.contains((x, y, z), wall)]
        return (min(zs), max(zs)) if zs else None

    def outer(self) -> set[Cell]:
        """The outermost layer of cells (what you see from outside)."""
        return self.shell(1)

    # queries
    def bounds(self) -> tuple[int, int, int, int, int, int] | None:
        if not self.slices:
            return None
        xs = [v for s in self.slices.values() for runs in s.values() for r in runs for v in r]
        ys = [y for s in self.slices.values() for y in s]
        return min(xs), min(ys), min(self.slices), max(xs), max(ys), max(self.slices)

    def z_range(self) -> tuple[int, int]:
        return min(self.slices), max(self.slices)

    def section(self, z: int, wall: int | None = 0) -> set[tuple[int, int]]:
        """The (x, y) cells of slice z — of the solid (wall 0) or of the inside for that wall thickness."""
        s = self._inside_slices(self.wall if wall is None else wall).get(z, {})
        return {(x, y) for y, runs in s.items() for a, b in runs for x in range(a, b + 1)}

    def row(self, y: int, z: int, wall: int | None = 0) -> tuple[int, int] | None:
        """(xmin, xmax) of row y in slice z — of the solid (wall 0) or of the inside — or None."""
        runs = self._inside_slices(self.wall if wall is None else wall).get(z, {}).get(y)
        return (runs[0][0], runs[-1][1]) if runs else None

    def column(self, x: int, z: int, wall: int | None = 0) -> tuple[int, int] | None:
        """(ymin, ymax) of the column at (x, z) — of the solid (wall 0) or of the inside — or None."""
        s = self._inside_slices(self.wall if wall is None else wall).get(z, {})
        ys = [y for y, runs in s.items() if any(a <= x <= b for a, b in runs)]
        return (min(ys), max(ys)) if ys else None

    def rows(self, z: int, wall: int | None = 0) -> tuple[int, int] | None:
        """(ymin, ymax) of slice z — of the solid or of the inside — or None."""
        s = self._inside_slices(self.wall if wall is None else wall).get(z, {})
        return (min(s), max(s)) if s else None


# --- curves and tubes ------------------------------------------------------------------------------------------------
def _sub(a: Vec, b: Vec) -> Vec:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _norm(a: Vec) -> Vec:
    n = math.sqrt(a[0] ** 2 + a[1] ** 2 + a[2] ** 2)
    return (0.0, 0.0, 1.0) if n == 0 else (a[0] / n, a[1] / n, a[2] / n)


def _dot(a: Vec, b: Vec) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def curve(points: Sequence[Vec], kind: str = "catmull", step: float = 0.25) -> list[Vec]:
    """Dense points (about `step` apart) along a path through `points`: `poly` = straight segments, `catmull` = a
    centripetal Catmull-Rom spline through every point (no loops or cusps on uneven spacing) — a tentacle, a pipe run,
    a curved gun mount."""
    pts = [(float(p[0]), float(p[1]), float(p[2])) for p in points]
    if len(pts) < 2:
        return pts
    out: list[Vec] = [pts[0]]
    if kind == "poly" or len(pts) == 2:
        for a, b in itertools.pairwise(pts):
            k = max(1, math.ceil(math.dist(a, b) / step))
            out += [(a[0] + (b[0] - a[0]) * i / k, a[1] + (b[1] - a[1]) * i / k, a[2] + (b[2] - a[2]) * i / k)
                    for i in range(1, k + 1)]
        return out
    if kind != "catmull":
        raise ValueError(f"unknown curve '{kind}' (catmull | poly)")
    ext = [tuple(2 * pts[0][i] - pts[1][i] for i in range(3))] + pts + [tuple(2 * pts[-1][i] - pts[-2][i] for i in range(3))]
    for j in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[j - 1], ext[j], ext[j + 1], ext[j + 2]
        t0 = 0.0
        t1 = t0 + max(EPS, math.dist(p0, p1) ** 0.5)
        t2 = t1 + max(EPS, math.dist(p1, p2) ** 0.5)
        t3 = t2 + max(EPS, math.dist(p2, p3) ** 0.5)
        k = max(2, math.ceil(math.dist(p1, p2) / step))
        for i in range(1, k + 1):
            t = t1 + (t2 - t1) * i / k

            def lerp(a, b, ta, tb, tt=t):
                f = 0.0 if tb == ta else (tt - ta) / (tb - ta)
                return tuple(a[c] + (b[c] - a[c]) * f for c in range(3))
            a1, a2, a3 = lerp(p0, p1, t0, t1), lerp(p1, p2, t1, t2), lerp(p2, p3, t2, t3)
            b1, b2 = lerp(a1, a2, t0, t2), lerp(a2, a3, t1, t3)
            out.append(lerp(b1, b2, t1, t2))
    return out


def radius_profile(radius) -> Callable[[float], float]:
    """A radius along a tube as a function of t (0 = the first point, 1 = the last, by length): a number, a pair
    (r0, r1) = linear, a list of (t, r) points = piecewise linear, or a callable."""
    if callable(radius):
        return radius
    if isinstance(radius, (int, float)):
        return lambda t, r=float(radius): r
    pts = list(radius)
    if len(pts) == 2 and all(isinstance(v, (int, float)) for v in pts):
        r0, r1 = float(pts[0]), float(pts[1])
        return lambda t: r0 + (r1 - r0) * t
    pts = sorted((float(t), float(r)) for t, r in pts)

    def at(t: float) -> float:
        if t <= pts[0][0]:
            return pts[0][1]
        for (ta, ra), (tb, rb) in itertools.pairwise(pts):
            if t <= tb:
                return ra + (rb - ra) * (0.0 if tb == ta else (t - ta) / (tb - ta))
        return pts[-1][1]
    return at


class Tube(NamedTuple):
    """What `tube` returns: `wall` = the tube's cells (the whole tube when solid), `bore` = the hollow inside it (empty
    for a solid tube; with open ends it runs through the end caps), `path` = the dense centre-line points."""
    wall: set[Cell]
    bore: set[Cell]
    path: list[Vec]


def _sweep(path: list[Vec], radii: list[float], lo: float = 0.0) -> set[Cell]:
    out: set[Cell] = set()
    for (px, py, pz), r in zip(path, radii):
        if r <= lo:
            continue
        rr = r * r + EPS
        for x in range(math.ceil(px - r - EPS), math.floor(px + r + EPS) + 1):
            dx = (x - px) ** 2
            for y in range(math.ceil(py - r - EPS), math.floor(py + r + EPS) + 1):
                dxy = dx + (y - py) ** 2
                if dxy > rr:
                    continue
                for z in range(math.ceil(pz - r - EPS), math.floor(pz + r + EPS) + 1):
                    if dxy + (z - pz) ** 2 <= rr:
                        out.add((x, y, z))
    return out


def tube(points: Sequence[Vec], radius, hollow: int = 0, kind: str = "catmull", ends: str = "flat",
         open_ends: bool = True, step: float = 0.5) -> Tube:
    """A tube along a path through `points` (`kind` 'catmull' or 'poly'): a ball of `radius` (see `radius_profile`)
    swept along it. `ends` 'flat' cuts the ends square to the path, 'round' leaves the ball's half. `hollow` > 0 keeps a
    wall that thick and returns the inside as the bore; `open_ends` lets the bore run out through both ends (a gun
    barrel, a walkable tunnel: a bore radius of 1.5 or more gives 3 x 3 cells across)."""
    path = curve(points, kind, min(step, 0.5))
    lengths = [0.0]
    for a, b in itertools.pairwise(path):
        lengths.append(lengths[-1] + math.dist(a, b))
    total = lengths[-1] or 1.0
    rf = radius_profile(radius)
    radii = [rf(s / total) for s in lengths]
    outer = _sweep(path, radii)
    t0 = _norm(_sub(path[min(1, len(path) - 1)], path[0]))
    t1 = _norm(_sub(path[-1], path[max(0, len(path) - 2)]))
    if ends == "flat" and len(path) > 1:
        reach = max(radii) + 1
        near = {c for c in outer if math.dist(c, path[0]) <= reach or math.dist(c, path[-1]) <= reach}
        for c in near:
            if _dot(_sub(c, path[0]), t0) < -0.5 + EPS or _dot(_sub(c, path[-1]), t1) > 0.5 - EPS:
                outer.discard(c)
    elif ends != "round":
        raise ValueError(f"unknown ends '{ends}' (flat | round)")
    bore: set[Cell] = set()
    if hollow > 0:
        inner_r = [r - hollow for r in radii]
        bpath, bradii = list(path), list(inner_r)
        if open_ends and ends == "round":  # run the bore on past both ends so it pierces the round caps
            ext = max(radii) + 1
            k = max(1, math.ceil(ext / 0.5))
            bpath = ([tuple(path[0][c] - t0[c] * ext * i / k for c in range(3)) for i in range(k, 0, -1)] + bpath
                     + [tuple(path[-1][c] + t1[c] * ext * i / k for c in range(3)) for i in range(1, k + 1)])
            bradii = [inner_r[0]] * k + bradii + [inner_r[-1]] * k
        # A flat cap's middle lies inside the last inner ball, so with flat ends the bore opens them by itself.
        bore = _sweep(bpath, bradii) & outer
        if not open_ends and ends == "flat":  # keep a cap `hollow` thick at both ends
            bore = {c for c in bore if _dot(_sub(c, path[0]), t0) >= hollow - 0.5 - EPS
                    and _dot(_sub(c, path[-1]), t1) <= -(hollow - 0.5) + EPS}
    wall = outer - bore
    return Tube(wall, bore, path)
