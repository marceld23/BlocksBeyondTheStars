"""Pure geometry for the landmark scripts: every function returns a SET OF CELLS (integer (x, y, z) triples) or an
ordered list of them, and writes nothing. `geom.Design` fills such sets with a material, carves them or clears them.

Conventions
- y is up; a building's ground row is template row `groundRow`.
- Circles and spheres take a centre and a radius in cells; a cell belongs to the shape when its CENTRE lies inside
  (distance <= radius). A disc of diameter D whose first cell is x0 has its centre at x0 + (D - 1) / 2 and radius
  D / 2 — `span(x0, D)` returns that pair. A centre may be fractional (.5 for an even width).
- Boxes and ranges are inclusive at both ends, in any order.
- `wall` = 0 means filled; a wall thickness > 0 keeps only the outer `wall` cells. Walls up to 3 thick are cut by
  erosion (at least `wall` cells everywhere, closed — no diagonal pinholes); thicker walls of round solids are cut
  analytically (the same shape `wall` smaller is removed) plus a closed one-cell rim.
Everything is deterministic: noise comes from an integer hash of (cell, seed), never from Python's own `hash`.
"""
from __future__ import annotations

import itertools
import math
from collections.abc import Callable, Iterable

Cell = tuple[int, int, int]
EPS = 1e-9

N26 = [(dx, dy, dz) for dx in (-1, 0, 1) for dy in (-1, 0, 1) for dz in (-1, 0, 1) if (dx, dy, dz) != (0, 0, 0)]
N8 = [(dx, dz) for dx in (-1, 0, 1) for dz in (-1, 0, 1) if (dx, dz) != (0, 0)]
N6 = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
THIN_WALL = 3  # walls up to this thick are cut by erosion, thicker ones analytically (see ring2d)


def span(x0: float, diameter: float) -> tuple[float, float]:
    """(centre, radius) of a disc `diameter` cells wide whose first cell is x0."""
    return x0 + (diameter - 1) / 2, diameter / 2


def _irange(a: float, b: float) -> range:
    lo, hi = (a, b) if a <= b else (b, a)
    return range(math.floor(lo + EPS), math.floor(hi + EPS) + 1)


def _ord(a: int, b: int) -> tuple[int, int]:
    return (a, b) if a <= b else (b, a)


# --- boxes, discs and rings -----------------------------------------------------------------------------------------
def box(x0: int, y0: int, z0: int, x1: int, y1: int, z1: int) -> set[Cell]:
    """A solid box, corners inclusive, any order."""
    (x0, x1), (y0, y1), (z0, z1) = _ord(x0, x1), _ord(y0, y1), _ord(z0, z1)
    return {(x, y, z) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1) for z in range(z0, z1 + 1)}


def box_walls(x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, wall: int = 1) -> set[Cell]:
    """The four side walls of a box (no floor, no roof), `wall` thick, corners inclusive."""
    (x0, x1), (y0, y1), (z0, z1) = _ord(x0, x1), _ord(y0, y1), _ord(z0, z1)
    return {(x, y, z) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1) for z in range(z0, z1 + 1)
            if x < x0 + wall or x > x1 - wall or z < z0 + wall or z > z1 - wall}


def disc(cx: float, cz: float, r: float, rz: float | None = None) -> set[tuple[int, int]]:
    """The (x, z) cells of a disc (an ellipse with `rz`)."""
    rz = r if rz is None else rz
    if r <= 0 or rz <= 0:
        return set()
    out = set()
    for x in _irange(cx - r, cx + r):
        for z in _irange(cz - rz, cz + rz):
            if ((x - cx) / r) ** 2 + ((z - cz) / rz) ** 2 <= 1 + EPS:
                out.add((x, z))
    return out


def erode2d(cells: set[tuple[int, int]], steps: int = 1) -> set[tuple[int, int]]:
    """The (x, z) cells that stay after `steps` 8-neighbour erosions."""
    inner = set(cells)
    for _ in range(steps):
        inner = {(x, z) for (x, z) in inner if all((x + dx, z + dz) in inner for dx, dz in N8)}
    return inner


def ring2d(cx: float, cz: float, r: float, wall: int, rz: float | None = None) -> set[tuple[int, int]]:
    """The (x, z) cells of a closed ring `wall` thick (an elliptic wall with `rz`). Up to THIN_WALL the disc's outer
    cells by erosion (at least `wall` cells everywhere, no pinholes); thicker, the disc minus the disc `wall` smaller
    (an erosion would bite the ends of an ellipse into points) plus the rim, so it stays closed."""
    full = disc(cx, cz, r, rz)
    if wall <= THIN_WALL:
        return full - erode2d(full, wall)
    rz = r if rz is None else rz
    inner = disc(cx, cz, r - wall, rz - wall) if r - wall > 0 and rz - wall > 0 else set()
    return (full - inner) | (full - erode2d(full, 1))


def extrude(cells2d: Iterable[tuple[int, int]], y0: int, y1: int) -> set[Cell]:
    """(x, z) cells stacked from row y0 to row y1 (inclusive)."""
    y0, y1 = _ord(y0, y1)
    return {(x, y, z) for (x, z) in cells2d for y in range(y0, y1 + 1)}


def cylinder(cx: float, cz: float, y0: int, y1: int, r: float, rz: float | None = None, wall: int = 0) -> set[Cell]:
    """A vertical cylinder (elliptic with `rz`); `wall` > 0 = a tube open at both ends."""
    layer = ring2d(cx, cz, r, wall, rz) if wall > 0 else disc(cx, cz, r, rz)
    return extrude(layer, y0, y1)


def cylinder_along(axis: str, ca: float, cb: float, s0: int, s1: int, r: float, wall: int = 0) -> set[Cell]:
    """A horizontal (or any axis-aligned) cylinder: the circle lies in the two other axes, in x, y, z order — for
    axis 'x' (ca, cb) = (cy, cz), for 'z' (cx, cy), for 'y' (cx, cz) — and runs from s0 to s1 along `axis`."""
    circle = ring2d(ca, cb, r, wall) if wall > 0 else disc(ca, cb, r)
    s0, s1 = _ord(s0, s1)
    out = set()
    for a, b in circle:
        for s in range(s0, s1 + 1):
            out.add({"x": (s, a, b), "y": (a, s, b), "z": (a, b, s)}[axis])
    return out


def shell(cells: Iterable[Cell], wall: int = 1, open_top: bool = False, open_bottom: bool = False) -> set[Cell]:
    """The outer `wall` cells of ANY solid (a 26-neighbour erosion, `wall` times). `open_bottom` / `open_top`: the row
    below the lowest (above the highest) counts as inside, so a dome or a cone keeps no floor (no lid). An erosion
    measures thickness in chessboard steps — fine for walls of 1 to 3; the round solids' own `wall` argument cuts thick
    walls analytically instead."""
    cells = set(cells)
    if not cells or wall <= 0:
        return cells
    ys = [c[1] for c in cells]
    ymin, ymax = min(ys), max(ys)
    inner = cells
    for _ in range(wall):
        keep = set()
        for x, y, z in inner:
            for dx, dy, dz in N26:
                ny = y + dy
                if (open_bottom and ny < ymin) or (open_top and ny > ymax):
                    continue
                if (x + dx, ny, z + dz) not in inner:
                    break
            else:
                keep.add((x, y, z))
        inner = keep
    return cells - inner


# --- round solids ---------------------------------------------------------------------------------------------------
def ellipsoid(cx: float, cy: float, cz: float, rx: float, ry: float | None = None, rz: float | None = None,
              wall: int = 0, ymin: int | None = None, ymax: int | None = None) -> set[Cell]:
    """An ellipsoid (a sphere when ry, rz are left out), optionally cut to rows ymin..ymax; `wall` > 0 = its shell
    (open where it is cut)."""
    ry = rx if ry is None else ry
    rz = rx if rz is None else rz
    out = set()
    for y in _irange(cy - ry, cy + ry):
        if (ymin is not None and y < ymin) or (ymax is not None and y > ymax):
            continue
        fy = ((y - cy) / ry) ** 2
        if fy > 1 + EPS:
            continue
        k = math.sqrt(max(0.0, 1 - fy))
        for x in _irange(cx - rx * k, cx + rx * k):
            fx = fy + ((x - cx) / rx) ** 2
            for z in _irange(cz - rz * k, cz + rz * k):
                if fx + ((z - cz) / rz) ** 2 <= 1 + EPS:
                    out.add((x, y, z))
    if 0 < wall <= THIN_WALL:
        out = shell(out, wall, open_top=ymax is not None, open_bottom=ymin is not None)
    elif wall > THIN_WALL:
        inner = (ellipsoid(cx, cy, cz, rx - wall, ry - wall, rz - wall, 0, ymin, ymax)
                 if min(rx, ry, rz) - wall > 0 else set())
        out = (out - inner) | shell(out, 1, open_top=ymax is not None, open_bottom=ymin is not None)
    return out


def sphere(cx: float, cy: float, cz: float, r: float, wall: int = 0) -> set[Cell]:
    return ellipsoid(cx, cy, cz, r, r, r, wall)


def dome(cx: float, cz: float, y0: int, r: float, ry: float | None = None, rz: float | None = None,
         wall: int = 0) -> set[Cell]:
    """The upper half of an ellipsoid standing on row y0 (its widest row), reaching up to row y0 + ry (with a whole
    centre and radius the very top is a single cell; use a .5 centre for a flatter crown). A hollow dome is open at
    the bottom."""
    return ellipsoid(cx, y0, cz, r, r if ry is None else ry, rz, wall, ymin=y0)


def cone(cx: float, cz: float, y0: int, y1: int, r0: float, r1: float = 0.5, wall: int = 0,
         aspect: float = 1.0) -> set[Cell]:
    """A cone / frustum from radius r0 on row y0 to r1 on row y1 (y1 below y0 = an upside-down cone). r1 = 0.5 ends in
    a single cell over an integer centre. `aspect` = rz / rx for an elliptic one. Hollow = open at the wide end."""
    rows = list(_irange(y0, y1))
    if y1 < y0:
        rows.reverse()
    n = len(rows)
    out = set()
    inner = set()
    for i, y in enumerate(rows):
        r = r0 + (r1 - r0) * (i / (n - 1) if n > 1 else 0)
        for x, z in disc(cx, cz, r, r * aspect) if r > 0 else ():
            out.add((x, y, z))
        if wall > THIN_WALL and r - wall > 0:
            inner |= {(x, y, z) for x, z in disc(cx, cz, r - wall, (r - wall) * aspect)}
    if 0 < wall <= THIN_WALL:
        out = shell(out, wall, open_bottom=y1 >= y0, open_top=y1 < y0)
    elif wall > THIN_WALL:
        out = (out - inner) | shell(out, 1, open_bottom=y1 >= y0, open_top=y1 < y0)
    return out


def capsule(p0: tuple[float, float, float], p1: tuple[float, float, float], r0: float,
            r1: float | None = None) -> set[Cell]:
    """Every cell whose centre lies within the radius of the segment p0-p1 (rounded ends; the radius runs from r0 at
    p0 to r1 at p1) — limbs, horns, tree trunks, tapering towers."""
    r1 = r0 if r1 is None else r1
    (ax, ay, az), (bx, by, bz) = p0, p1
    dx, dy, dz = bx - ax, by - ay, bz - az
    ll = dx * dx + dy * dy + dz * dz
    rm = max(r0, r1)
    out = set()
    for x in _irange(min(ax, bx) - rm, max(ax, bx) + rm):
        for y in _irange(min(ay, by) - rm, max(ay, by) + rm):
            for z in _irange(min(az, bz) - rm, max(az, bz) + rm):
                t = 0.0 if ll == 0 else max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy + (z - az) * dz) / ll))
                px, py, pz = ax + t * dx - x, ay + t * dy - y, az + t * dz - z
                r = r0 + (r1 - r0) * t
                if px * px + py * py + pz * pz <= r * r + EPS:
                    out.add((x, y, z))
    return out


# --- lines and curves -----------------------------------------------------------------------------------------------
def _rint(p: tuple[float, float, float]) -> Cell:
    return (round(p[0]), round(p[1]), round(p[2]))


def line(p0: tuple[float, float, float], p1: tuple[float, float, float]) -> list[Cell]:
    """A 3-D Bresenham line from p0 to p1, both ends included, in order; neighbouring cells touch at a face, an edge
    or a corner (26-connected) — the lightest line."""
    x0, y0, z0 = _rint(p0)
    x1, y1, z1 = _rint(p1)
    dx, dy, dz = abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)
    sx, sy, sz = (1 if x1 >= x0 else -1), (1 if y1 >= y0 else -1), (1 if z1 >= z0 else -1)
    out = [(x0, y0, z0)]
    if dx >= dy and dx >= dz:
        e1, e2 = 2 * dy - dx, 2 * dz - dx
        for _ in range(dx):
            x0 += sx
            if e1 > 0:
                y0 += sy
                e1 -= 2 * dx
            if e2 > 0:
                z0 += sz
                e2 -= 2 * dx
            e1 += 2 * dy
            e2 += 2 * dz
            out.append((x0, y0, z0))
    elif dy >= dx and dy >= dz:
        e1, e2 = 2 * dx - dy, 2 * dz - dy
        for _ in range(dy):
            y0 += sy
            if e1 > 0:
                x0 += sx
                e1 -= 2 * dy
            if e2 > 0:
                z0 += sz
                e2 -= 2 * dy
            e1 += 2 * dx
            e2 += 2 * dz
            out.append((x0, y0, z0))
    else:
        e1, e2 = 2 * dy - dz, 2 * dx - dz
        for _ in range(dz):
            z0 += sz
            if e1 > 0:
                y0 += sy
                e1 -= 2 * dz
            if e2 > 0:
                x0 += sx
                e2 -= 2 * dz
            e1 += 2 * dy
            e2 += 2 * dx
            out.append((x0, y0, z0))
    return out


def walk(p0: tuple[float, float, float], p1: tuple[float, float, float]) -> list[Cell]:
    """A face-connected line from p0 to p1 (every step changes one coordinate by one): the cells a straight ray from
    centre to centre passes through. Heavier than `line`, but posts and beams on it join into one member."""
    a, b = _rint(p0), _rint(p1)
    cur = list(a)
    d = [b[i] - a[i] for i in range(3)]
    step = [1 if v > 0 else -1 for v in d]
    # parameter t (0..1) at which the ray crosses the next cell boundary on each axis
    t_max = [(0.5 / abs(v)) if v else math.inf for v in d]
    t_delta = [(1 / abs(v)) if v else math.inf for v in d]
    out = [a]
    for _ in range(sum(abs(v) for v in d)):
        axis = min(range(3), key=lambda i: (t_max[i] - EPS * (2 - i), i))
        cur[axis] += step[axis]
        t_max[axis] += t_delta[axis]
        out.append((cur[0], cur[1], cur[2]))
    return out


def polyline(points: list[tuple[float, float, float]], connect: int = 26) -> list[Cell]:
    """Lines through consecutive points (26 = `line`, 6 = `walk`), joint cells listed once."""
    out: list[Cell] = []
    for p, q in itertools.pairwise(points):
        seg = line(p, q) if connect == 26 else walk(p, q)
        out.extend(seg if not out else seg[1:])
    return out


def sag_curve(p0: tuple[float, float, float], p1: tuple[float, float, float], sag: float,
              kind: str = "parabola") -> Callable[[float], tuple[float, float, float]]:
    """The point at t (0..1) of a cable hung from p0 to p1 whose lowest drop below the straight chord is `sag` at the
    middle. `parabola` is the shape of a suspension bridge's main cable (a uniform deck load), `catenary` a free
    chain. A negative sag bends the curve upward (an arch)."""
    (ax, ay, az), (bx, by, bz) = p0, p1
    span_len = math.hypot(bx - ax, bz - az) or abs(by - ay) or 1.0
    if kind == "parabola" or sag == 0:
        def drop(t: float) -> float:
            return 4 * sag * t * (1 - t)
    elif kind == "catenary":
        f = abs(sag)
        lo, hi = 1e-3, 1e7  # solve a * (cosh(L / 2a) - 1) = f for the catenary parameter a
        for _ in range(200):
            mid = math.sqrt(lo * hi)
            if mid * (math.cosh(min(700.0, span_len / (2 * mid))) - 1) > f:
                lo = mid
            else:
                hi = mid
        a = math.sqrt(lo * hi)
        sign = 1 if sag > 0 else -1

        def drop(t: float) -> float:
            return sign * a * (math.cosh(span_len / (2 * a)) - math.cosh((t - 0.5) * span_len / a))
    else:
        raise ValueError(f"unknown cable kind '{kind}' (parabola | catenary)")

    def at(t: float) -> tuple[float, float, float]:
        return (ax + (bx - ax) * t, ay + (by - ay) * t - drop(t), az + (bz - az) * t)
    return at


def cable(p0: tuple[float, float, float], p1: tuple[float, float, float], sag: float, kind: str = "parabola",
          connect: int = 26) -> list[Cell]:
    """The cells of a hanging cable (`sag_curve`), in order from p0 to p1."""
    at = sag_curve(p0, p1, sag, kind)
    n = max(2, int(max(abs(p1[0] - p0[0]), abs(p1[2] - p0[2]), abs(p1[1] - p0[1]) + abs(sag)) * 2) + 1)
    pts = [at(i / (n - 1)) for i in range(n)]
    out: list[Cell] = []
    for p, q in itertools.pairwise(pts):
        seg = line(p, q) if connect == 26 else walk(p, q)
        for c in seg:
            if not out or out[-1] != c:
                out.append(c)
    return out


# --- walls, polygons and stars (2-D shapes, then extruded) -----------------------------------------------------------
def segment2d(x0: float, z0: float, x1: float, z1: float, thickness: float) -> set[tuple[int, int]]:
    """The (x, z) cells within thickness / 2 of a segment (round ends, so walls meet cleanly at a corner)."""
    h = thickness / 2
    dx, dz = x1 - x0, z1 - z0
    ll = dx * dx + dz * dz
    out = set()
    for x in _irange(min(x0, x1) - h, max(x0, x1) + h):
        for z in _irange(min(z0, z1) - h, max(z0, z1) + h):
            t = 0.0 if ll == 0 else max(0.0, min(1.0, ((x - x0) * dx + (z - z0) * dz) / ll))
            if (x0 + t * dx - x) ** 2 + (z0 + t * dz - z) ** 2 <= h * h + EPS:
                out.add((x, z))
    return out


def polygon_wall(points: list[tuple[float, float]], y0: int, y1: int, thickness: float = 1.0,
                 closed: bool = True) -> set[Cell]:
    """A wall along a polyline of (x, z) points from row y0 to y1 (a curtain wall between towers)."""
    pts = list(points) + ([points[0]] if closed and len(points) > 2 else [])
    flat: set[tuple[int, int]] = set()
    for (ax, az), (bx, bz) in itertools.pairwise(pts):
        flat |= segment2d(ax, az, bx, bz, thickness)
    return extrude(flat, y0, y1)


def polygon(points: list[tuple[float, float]]) -> set[tuple[int, int]]:
    """The (x, z) cells whose centre lies inside a polygon (even-odd rule)."""
    xs = [p[0] for p in points]
    zs = [p[1] for p in points]
    out = set()
    n = len(points)
    for x in _irange(min(xs), max(xs)):
        for z in _irange(min(zs), max(zs)):
            inside = False
            for i in range(n):
                (ax, az), (bx, bz) = points[i], points[(i + 1) % n]
                if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az):
                    inside = not inside
            if inside:
                out.add((x, z))
    return out


def star_points(cx: float, cz: float, r_outer: float, r_inner: float, points: int = 5,
                rotation: float = 0.0) -> list[tuple[float, float]]:
    """The corners of a star polygon (2 * points corners, the first tip at angle `rotation`, radians, from +Z)."""
    out = []
    for i in range(2 * points):
        r = r_outer if i % 2 == 0 else r_inner
        a = rotation + math.pi * i / points
        out.append((cx + r * math.sin(a), cz + r * math.cos(a)))
    return out


def star(cx: float, cz: float, r_outer: float, r_inner: float, points: int = 5, rotation: float = 0.0) -> set[tuple[int, int]]:
    """A star's (x, z) cells — Liberty's star-shaped fort under the statue has eleven points."""
    return polygon(star_points(cx, cz, r_outer, r_inner, points, rotation))


# --- arches ---------------------------------------------------------------------------------------------------------
def arch_inside(u: float, v: float, width: float, height: float, kind: str = "round", rise: float | None = None) -> bool:
    """Whether the point (u across the opening from its left jamb, v up from its sill) lies in an arched opening.
    `round` = semicircle on top of straight jambs; `segmental` = a flatter arc of the given rise (default width / 4);
    `pointed` = two arcs meeting in a point (default rise: equilateral, width * 0.87); `flat` = a plain rectangle."""
    if u < 0 or u > width or v < 0 or v > height:
        return False
    if kind == "flat":
        return True
    if kind == "round":
        r = width / 2
        spring = height - r
        return v <= spring or (u - r) ** 2 + (v - spring) ** 2 <= r * r + EPS
    if kind == "segmental":
        f = width / 4 if rise is None else rise
        f = max(0.5, min(f, width / 2))
        rho = (width * width / 4 + f * f) / (2 * f)
        spring = height - f
        return v <= spring or (u - width / 2) ** 2 + (v - (height - rho)) ** 2 <= rho * rho + EPS
    if kind == "pointed":
        f = width * math.sqrt(3) / 2 if rise is None else rise
        f = max(0.5, f)
        rho = (width * width / 4 + f * f) / width
        spring = height - f
        if v <= spring:
            return True
        return (u - rho) ** 2 + (v - spring) ** 2 <= rho * rho + EPS and (u - (width - rho)) ** 2 + (v - spring) ** 2 <= rho * rho + EPS
    raise ValueError(f"unknown arch kind '{kind}' (round | segmental | pointed | flat)")


def arch_profile(width: int, height: int, kind: str = "round", rise: float | None = None) -> set[tuple[int, int]]:
    """The (u, v) cells of an arched opening `width` wide and `height` high (u 0..width-1, v 0..height-1)."""
    return {(u, v) for u in range(width) for v in range(height) if arch_inside(u + 0.5, v + 0.5, width, height, kind, rise)}


def arch(axis: str, u0: int, y0: int, a0: int, a1: int, width: int, height: int, kind: str = "round",
         rise: float | None = None) -> set[Cell]:
    """An arched opening you walk through along `axis` ('z': the wall lies in x-y and the opening spans x from u0;
    'x': the wall lies in z-y and the opening spans z from u0), its sill on row y0, through a0..a1 along the axis."""
    a0, a1 = _ord(a0, a1)
    out = set()
    for u, v in arch_profile(width, height, kind, rise):
        for a in range(a0, a1 + 1):
            out.add((u0 + u, y0 + v, a) if axis == "z" else (a, y0 + v, u0 + u))
    return out


def arcade(axis: str, u0: int, y0: int, a0: int, a1: int, count: int, width: int, height: int, pier: int,
           kind: str = "round", rise: float | None = None) -> set[Cell]:
    """`count` arches side by side, each `width` wide, `pier` cells of wall between them."""
    out: set[Cell] = set()
    for i in range(count):
        out |= arch(axis, u0 + i * (width + pier), y0, a0, a1, width, height, kind, rise)
    return out


class Ellipse:
    """An ellipse in the x-z plane with arc-length bookkeeping: equal spacing of arches, columns or seats around an
    amphitheatre. `arc(x, z)` is the arc length (0..perimeter) of the ray from the centre through a cell — every cell
    on that ray, inner or outer wall face, gets the same value, so openings are cut radially."""

    SAMPLES = 4096

    def __init__(self, cx: float, cz: float, rx: float, rz: float) -> None:
        self.cx, self.cz, self.rx, self.rz = cx, cz, rx, rz
        acc = [0.0]
        prev = (rx, 0.0)
        for i in range(1, self.SAMPLES + 1):
            a = 2 * math.pi * i / self.SAMPLES
            p = (rx * math.cos(a), rz * math.sin(a))
            acc.append(acc[-1] + math.hypot(p[0] - prev[0], p[1] - prev[1]))
            prev = p
        self._acc = acc
        self.perimeter = acc[-1]

    def angle(self, x: float, z: float) -> float:
        """The elliptic angle (0..2 pi, from +X toward +Z) of the ray through (x, z)."""
        return math.atan2((z - self.cz) / self.rz, (x - self.cx) / self.rx) % (2 * math.pi)

    def arc(self, x: float, z: float) -> float:
        f = self.angle(x, z) / (2 * math.pi) * self.SAMPLES
        i = min(int(f), self.SAMPLES - 1)
        return self._acc[i] + (self._acc[i + 1] - self._acc[i]) * (f - i)

    def at_angle(self, a: float, scale: float = 1.0) -> tuple[float, float]:
        return (self.cx + scale * self.rx * math.cos(a), self.cz + scale * self.rz * math.sin(a))

    def point(self, s: float, scale: float = 1.0) -> tuple[float, float]:
        """The point at arc length s (scale > 1 = on a larger concentric ellipse, same ray)."""
        s %= self.perimeter
        lo, hi = 0, self.SAMPLES
        while lo < hi:
            mid = (lo + hi) // 2
            if self._acc[mid + 1] < s:
                lo = mid + 1
            else:
                hi = mid
        seg = self._acc[lo + 1] - self._acc[lo]
        f = lo + ((s - self._acc[lo]) / seg if seg else 0)
        return self.at_angle(2 * math.pi * f / self.SAMPLES, scale)

    def points(self, count: int, phase: float = 0.0, scale: float = 1.0) -> list[tuple[float, float, float]]:
        """`count` points at equal arc spacing: (x, z, elliptic angle)."""
        out = []
        for i in range(count):
            s = phase + self.perimeter * i / count
            x, z = self.point(s, scale)
            out.append((x, z, self.angle(x, z)))
        return out

    def inside(self, x: float, z: float, scale: float = 1.0) -> bool:
        return ((x - self.cx) / (self.rx * scale)) ** 2 + ((z - self.cz) / (self.rz * scale)) ** 2 <= 1 + EPS


def ring_openings(cells: Iterable[Cell], ellipse: Ellipse, y0: int, count: int, width: float, height: float,
                  kind: str = "round", rise: float | None = None, phase: float = 0.0) -> set[Cell]:
    """The cells of a ring wall that lie in one of `count` arched openings spaced evenly around it (measured along
    `ellipse`, normally the wall's centre line), sills on row y0 — an amphitheatre's arcade. Pass the result to
    `Design.fill(..., CLEAR)`."""
    spacing = ellipse.perimeter / count
    left = (spacing - width) / 2
    out = set()
    for x, y, z in cells:
        v = y - y0 + 0.5
        if v < 0 or v > height:
            continue
        u = ((ellipse.arc(x, z) - phase) % spacing) - left
        if arch_inside(u, v, width, height, kind, rise):
            out.add((x, y, z))
    return out


# --- pyramids -------------------------------------------------------------------------------------------------------
def pyramid_layers(x0: int, z0: int, y0: int, base_x: int, base_z: int | None = None, step: int = 1, rise: int = 1,
                   top: int = 1) -> list[tuple[int, int, int, int, int]]:
    """The layers of a square (rectangular) pyramid whose base's first corner is (x0, z0) on row y0: a list of
    (y, xa, za, xb, zb) rectangles, inset by `step` cells every `rise` rows, until a side would be narrower than `top`."""
    base_z = base_x if base_z is None else base_z
    out = []
    y = y0
    k = 0
    while True:
        inset = k * step
        xa, xb, za, zb = x0 + inset, x0 + base_x - 1 - inset, z0 + inset, z0 + base_z - 1 - inset
        if xb - xa + 1 < top or zb - za + 1 < top:
            break
        for _ in range(rise):
            out.append((y, xa, za, xb, zb))
            y += 1
        k += 1
    return out


def pyramid(x0: int, z0: int, y0: int, base_x: int, base_z: int | None = None, step: int = 1, rise: int = 1,
            top: int = 1, wall: int = 0) -> set[Cell]:
    """A stepped pyramid (`pyramid_layers`); `wall` > 0 keeps only a slanted crust `wall` cells thick (rooms inside)
    under a solid top `wall` rows thick."""
    out = set()
    layers = pyramid_layers(x0, z0, y0, base_x, base_z, step, rise, top)
    for i, (y, xa, za, xb, zb) in enumerate(layers):
        roof = i >= len(layers) - wall
        for x in range(xa, xb + 1):
            for z in range(za, zb + 1):
                if wall <= 0 or roof or x < xa + wall or x > xb - wall or z < za + wall or z > zb - wall:
                    out.add((x, y, z))
    return out


# --- caves ----------------------------------------------------------------------------------------------------------
def hash01(x: int, y: int, z: int, seed: int = 0) -> float:
    """A deterministic value in [0, 1) for a cell and a seed."""
    h = (x * 0x27D4EB2D) ^ (y * 0x165667B1) ^ (z * 0x1B873593) ^ (seed * 0x68E31DA4)
    h &= 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 0x2C1B3C6D) & 0xFFFFFFFF
    h ^= h >> 12
    h = (h * 0x297A2D39) & 0xFFFFFFFF
    h ^= h >> 15
    return h / 4294967296.0


def value_noise(x: float, y: float, z: float, seed: int = 0) -> float:
    """Smooth 3-D value noise in [0, 1] (trilinear over the hash lattice, smoothstep-eased)."""
    ix, iy, iz = math.floor(x), math.floor(y), math.floor(z)
    fx, fy, fz = x - ix, y - iy, z - iz
    fx, fy, fz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)

    def h(a: int, b: int, c: int) -> float:
        return hash01(ix + a, iy + b, iz + c, seed)

    def lerp(a: float, b: float, t: float) -> float:
        return a + (b - a) * t

    x00 = lerp(h(0, 0, 0), h(1, 0, 0), fx)
    x10 = lerp(h(0, 1, 0), h(1, 1, 0), fx)
    x01 = lerp(h(0, 0, 1), h(1, 0, 1), fx)
    x11 = lerp(h(0, 1, 1), h(1, 1, 1), fx)
    return lerp(lerp(x00, x10, fy), lerp(x01, x11, fy), fz)


def cavern(cx: float, cy: float, cz: float, rx: float, ry: float, rz: float, roughness: float = 0.3, seed: int = 1,
           scale: float = 6.0, floor: int | None = None) -> set[Cell]:
    """An irregular cave: an ellipsoid whose surface is pushed in and out by smooth noise (`roughness` 0 = a clean
    ellipsoid, 0.3 = lumpy, `scale` = the size of a lump in cells). `floor` cuts it flat at that row (walkable)."""
    out = set()
    grow = 1 + roughness
    for x in _irange(cx - rx * grow, cx + rx * grow):
        for y in _irange(cy - ry * grow, cy + ry * grow):
            if floor is not None and y < floor:
                continue
            for z in _irange(cz - rz * grow, cz + rz * grow):
                d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 + ((z - cz) / rz) ** 2
                if d <= (1 + roughness * (2 * value_noise(x / scale, y / scale, z / scale, seed) - 1)) ** 2:
                    out.add((x, y, z))
    return out


def lowest_cells(cells: Iterable[Cell]) -> set[Cell]:
    """The cells of a set with nothing of the set directly below them (a cave's floor cells, a roof's underside)."""
    s = set(cells)
    return {(x, y, z) for (x, y, z) in s if (x, y - 1, z) not in s}


def highest_cells(cells: Iterable[Cell]) -> set[Cell]:
    """The cells of a set with nothing of the set directly above them (a cave's ceiling, a wall's top course)."""
    s = set(cells)
    return {(x, y, z) for (x, y, z) in s if (x, y + 1, z) not in s}


def bounds(cells: Iterable[Cell]) -> tuple[int, int, int, int, int, int] | None:
    """(x0, y0, z0, x1, y1, z1) of a set, None when it is empty."""
    cells = list(cells)
    if not cells:
        return None
    xs, ys, zs = zip(*cells)
    return min(xs), min(ys), min(zs), max(xs), max(ys), max(zs)
