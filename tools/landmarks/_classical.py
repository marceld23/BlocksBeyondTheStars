"""Shared pieces of the stone landmarks (Porta Nigra, Portasseum, golden bird gate): straight stair flights that clear
their own headroom, switchback stairs between two floors, and the Roman facade grammar — arched windows framed by
engaged half-columns under a projecting entablature, on a straight wall or round a curved one.

Not a landmark itself (the leading underscore keeps tools/gen_landmarks.py from running it).
"""
from __future__ import annotations

import math
from collections.abc import Iterable

import shapecode as sc
import solids
from geom import CLEAR, Design

Cell = tuple[int, int, int]


# --- stairs ---------------------------------------------------------------------------------------------------------
def flight(d: Design, x: int, y: int, z: int, dx: int, dz: int, steps: int, m, width: int = 3, clear: int = 3,
           where: str | None = None) -> set[Cell]:
    """A straight stair: tread k on row y + k at (x, z) + k * (dx, dz), `width` cells to the right-hand side (+x for a
    z-stair, +z for an x-stair), each in the `stairs` form (walkable without jumping). Clears `clear` rows above every
    tread first, so a floor it climbs through gets its stairwell. Returns the treads."""
    px, pz = (0, 1) if dx else (1, 0)
    treads = {(x + k * dx + w * px, y + k, z + k * dz + w * pz) for k in range(steps) for w in range(width)}
    for tx, ty, tz in treads:
        for h in range(1, clear + 1):
            if (tx, ty + h, tz) not in treads:
                d.plain.pop((tx, ty + h, tz), None)
    d.fill(treads, m, where, shape=sc.stairs(dx, dz))
    return treads


def switchback(d: Design, x0: int, z0: int, floor: int, rise: int, m, along: str = "z", sign: int = 1,
               width: int = 3, landing: int = 3, landing_m=None) -> set[Cell]:
    """A stair of two flights from the floor on row `floor` up to the floor on row floor + rise, in two lanes `width`
    wide side by side: the first climbs from (x0, z0) toward +along * sign, a landing `landing` long turns it, the
    second climbs back in the next lane (+x of the first for along 'z', +z for along 'x'). The top tread lies on the
    upper floor's row one cell short of (x0, z0) in the second lane — walk on from there. Returns every tread and
    landing cell."""
    n1 = (rise + 1) // 2
    n2 = rise - n1
    landing_m = m if landing_m is None else landing_m
    if along == "z":
        a = flight(d, x0, floor + 1, z0, 0, sign, n1, m, width)
        zl = z0 + sign * n1
        land = solids.box(x0, floor + n1, zl, x0 + 2 * width - 1, floor + n1, zl + sign * (landing - 1))
        b_start = (x0 + width, floor + n1 + 1, z0 + sign * (n1 - 1))
        dx, dz = 0, -sign
    else:
        a = flight(d, x0, floor + 1, z0, sign, 0, n1, m, width)
        xl = x0 + sign * n1
        land = solids.box(xl, floor + n1, z0, xl + sign * (landing - 1), floor + n1, z0 + 2 * width - 1)
        b_start = (x0 + sign * (n1 - 1), floor + n1 + 1, z0 + width)
        dx, dz = -sign, 0
    for lx, ly, lz in land:
        for h in range(1, 4):
            d.plain.pop((lx, ly + h, lz), None)
    d.fill(land, landing_m)
    b = flight(d, b_start[0], b_start[1], b_start[2], dx, dz, n2, m, width)
    return a | land | b


# --- the facade grammar ---------------------------------------------------------------------------------------------
class Face:
    """A straight wall face for the facade helpers: the wall lies in the plane `axis` = `plane` (its outer face), the
    outside is toward `out` (+1 / -1 along that axis), it runs along the other horizontal axis from u0 to u1 and is
    `depth` cells thick (inward from the plane)."""

    def __init__(self, axis: str, plane: int, out: int, u0: int, u1: int, depth: int = 2) -> None:
        self.axis, self.plane, self.out, self.u0, self.u1, self.depth = axis, plane, out, u0, u1, depth

    def cell(self, u: int, y: int, k: int = 0) -> Cell:
        """The cell at u along the face, row y, k cells outward from the outer face (negative = into the wall)."""
        a = self.plane + self.out * k
        return (a, y, u) if self.axis == "x" else (u, y, a)

    def through(self) -> tuple[int, int]:
        """The wall's extent along its axis (outer face to inner face)."""
        inner = self.plane - self.out * (self.depth - 1)
        return min(self.plane, inner), max(self.plane, inner)

    def bays(self, count: int) -> tuple[list[float], list[int]]:
        """`count` equal bays along the face: their centres, and the count + 1 pier positions between and at the ends."""
        n = self.u1 - self.u0 + 1
        centres = [self.u0 + n * (i + 0.5) / count - 0.5 for i in range(count)]
        piers = [min(self.u1, round(self.u0 + n * i / count - (0.5 if i == count else 0))) for i in range(count + 1)]
        return centres, piers


def window(d: Design, face: Face, centre: float, sill: int, width: int, height: int, m=CLEAR, kind: str = "round",
           depth: int | None = None) -> set[Cell]:
    """An arched opening through the face's wall (or `depth` cells into it: a blind arch), centred on u = centre."""
    u0 = round(centre - (width - 1) / 2)
    a0, a1 = face.through()
    if depth is not None:
        a0, a1 = (face.plane - depth + 1, face.plane) if face.out > 0 else (face.plane, face.plane + depth - 1)
    walk = "x" if face.axis == "x" else "z"
    return d.fill(solids.arch(walk, u0, sill, a0, a1, width, height, kind), m)


def half_column(d: Design, face: Face, u: int, y0: int, y1: int, m, capital=None, base=None) -> set[Cell]:
    """An engaged half-column standing one cell proud of the face: the `cylinder` form from y0 to y1, a cube `base`
    and `capital` when given."""
    out = set()
    for y in range(y0, y1 + 1):
        x, yy, z = face.cell(u, y, 1)
        if y == y1 and capital is not None:
            d.put(x, yy, z, capital)
        elif y == y0 and base is not None:
            d.put(x, yy, z, base)
        else:
            d.put(x, yy, z, m, shape=sc.pack(sc.CYLINDER))
        out.add((x, yy, z))
    return out


def band(d: Design, face: Face, y: int, m, proud: int = 1, extend: int = 1) -> set[Cell]:
    """A horizontal band (an entablature, a cornice) `proud` cells out from the face on row y, `extend` cells past both
    ends so two faces' bands meet at the corner."""
    cells = {face.cell(u, y, k) for u in range(face.u0 - extend, face.u1 + extend + 1) for k in range(1, proud + 1)}
    return d.fill(cells, m)


# --- the same on a round wall ---------------------------------------------------------------------------------------
def ring_band(d: Design, cx: float, cz: float, r_face: float, y: int, m, proud: float = 1.0, rz: float | None = None,
              keep_if: callable | None = None) -> set[Cell]:
    """A band proud of a round (elliptic) wall whose outer face lies at r_face: the cells between r_face and
    r_face + proud on row y (only those `keep_if(x, z)` accepts — the half of an apse)."""
    rz = r_face if rz is None else rz
    ring = solids.disc(cx, cz, r_face + proud, rz + proud) - solids.disc(cx, cz, r_face, rz)
    cells = {(x, y, z) for x, z in ring if keep_if is None or keep_if(x, z)}
    return d.fill(cells, m)


def ring_half_columns(d: Design, ellipse: solids.Ellipse, count: int, y0: int, y1: int, m, capital=None,
                      phase: float = 0.0, keep_if: callable | None = None) -> set[Cell]:
    """`count` half-columns evenly round a round wall (`ellipse` = a line just outside its face), each the `cylinder`
    form from y0 to y1 with a cube capital."""
    out = set()
    for x, z, _ in ellipse.points(count, phase):
        cx, cz = round(x), round(z)
        if keep_if is not None and not keep_if(cx, cz):
            continue
        for y in range(y0, y1 + 1):
            if y == y1 and capital is not None:
                d.put(cx, y, cz, capital)
            else:
                d.put(cx, y, cz, m, shape=sc.pack(sc.CYLINDER))
            out.add((cx, y, cz))
    return out


def radial(cx: float, cz: float, a: float, r0: float, r1: float, y0: int, y1: int, width: float = 1.0) -> set[Cell]:
    """The cells along the ray from (cx, cz) at angle `a` (radians, from +X toward +Z), from radius r0 to r1, `width`
    wide across it, rows y0..y1 — a doorway or a slit through a round wall."""
    out = set()
    ux, uz = math.cos(a), math.sin(a)
    tx, tz = -uz, ux
    lanes = max(1, int(width * 2) + 1)
    r = r0
    while r <= r1 + 1e-9:
        for k in range(lanes):
            o = -width / 2 + (k + 0.5) * width / lanes
            x, z = round(cx + r * ux + o * tx), round(cz + r * uz + o * tz)
            for y in range(y0, y1 + 1):
                out.add((x, y, z))
        r += 0.35
    return out


def lights(d: Design, cells: Iterable[Cell], block: str = "torch") -> int:
    """Puts a light in every given cell that is still empty (torches stand in the air beside a wall)."""
    n = 0
    for p in cells:
        if p not in d.plain:
            d.put(*p, block)
            n += 1
    return n
