"""The Portasseum: an amphitheatre the size of Rome's Colosseum (188 x 156 x 48 m, one block a metre) whose arcades are
framed the way the black gate frames its windows — pale travertine arches between dark engaged half-columns under dark
entablatures, three arcaded storeys and a closed attic.

Behind the facade two ring corridors (ambulatories) climb three storeys; stairs in the inner one lead to the top gallery,
from which aisles run down the seating terraces — two tiers of seats with a walkway between — to the podium round the
arena. Four gates at the ends of the axes run straight into the arena; stairs in the arena climb to the podium. Under the
sand lies the hypogeum: a grid of tunnels carved into the rock below the ground row, reached by two stairs and a few
ladder shafts, with a trench in the arena floor that shows it, and chests in its side rooms. On one side the outer ring
has come down, as on the real one — the design carries that loss, and ruin zones make that side decay harder still. The
wind echoes through the arches.

Every ring, the seats and the radial walls are laid out on the same 80 bays, by the offset `d` of a cell from the outer
facade (d = 0 on its face, growing inward; the offset ellipses of the plan).
"""
from __future__ import annotations

import math

import _classical as cl
import shapecode as sc
import solids
from geom import CAVITY, CLEAR, Design, Mat, Mix

W, L = 192, 160              # with the apron round the facade
CX, CZ = (W - 1) / 2, (L - 1) / 2
RX, RZ = 94.0, 78.0          # the facade's outer face
G = 6                        # the ground row; the hypogeum lies in rows 0..5
BAYS = 80


def R(r: int) -> int:
    """A row counted from the ground row."""
    return G + r


# Storeys (rows from the ground row): three arcades with their cornices, then the attic.
STOREYS = ((1, 11), (13, 23), (25, 35))
CORNICES = (12, 24, 36, 47)
ATTIC = (37, 46)
TOP_GALLERY = 36
# The plan, as offsets from the facade.
FACADE = (0.0, 2.0)
AMB1 = (2.0, 6.0)
RING2 = (6.0, 8.0)
AMB2 = (8.0, 12.0)
RING3 = (12.0, 14.0)
SEATS_TOP = 14.0
PODIUM = (49.0, 51.0)
ARENA = 51.0

TRAV = Mat("travertine")
FACE = Mix(Mat("travertine"), (Mat("travertine", tint=0xA8A08C), 0.08), (Mat("travertine", tint=0xB2B0A6), 0.06), seed=31)
DARK = Mat("blackened_sandstone")
TRIM = Mat("blackened_sandstone", tint=0x58524C)
INNER = Mix(Mat("blackened_sandstone"), (Mat("sandstone", tint=0x5A524A), 0.06), seed=32)
SEAT = Mat("travertine", tint=0xC8C2B2)
SAND = Mat("sand")
HYPO = Mat("travertine", tint=0x9A9282)
STAIR = Mat("travertine")


# --- the offset map ----------------------------------------------------------------------------------------------
def _offset(x: int, z: int) -> float:
    """The offset d of a column from the facade: it lies on the ellipse (RX - d, RZ - d)."""
    fx, fz = x - CX, z - CZ

    def f(d: float) -> float:
        return (fx / (RX - d)) ** 2 + (fz / (RZ - d)) ** 2

    lo, hi = -4.0, RZ - 0.5
    if f(lo) > 1:
        return -99.0
    if f(hi) < 1:
        return hi
    for _ in range(32):
        mid = (lo + hi) / 2
        if f(mid) < 1:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


DMAP: dict[tuple[int, int], float] = {}
for _x in range(W):
    for _z in range(L):
        _d = _offset(_x, _z)
        if _d > -3.0:
            DMAP[(_x, _z)] = _d


def cols(d0: float, d1: float) -> list[tuple[int, int]]:
    """The columns whose offset lies in [d0, d1)."""
    return [p for p, d in DMAP.items() if d0 <= d < d1]


def rows(d0: float, d1: float, y0: int, y1: int) -> set:
    return {(x, y, z) for x, z in cols(d0, d1) for y in range(y0, y1 + 1)}


OUTER = solids.Ellipse(CX, CZ, RX - 1, RZ - 1)          # the bays are measured along the facade's centre line
SPACING = OUTER.perimeter / BAYS


def bay_angle(s: float) -> float:
    """The elliptic angle of the ray at arc length s along the facade (bay i's arch centre: s = i * SPACING)."""
    return OUTER.angle(*OUTER.point(s))


def at(t: float, d: float) -> tuple[float, float]:
    return CX + (RX - d) * math.cos(t), CZ + (RZ - d) * math.sin(t)


def bay_of(x: int, z: int) -> float:
    """The bay coordinate of a column: 0 at bay 0's arch centre, 1 at the next one's."""
    return OUTER.arc(x, z) / SPACING


# --- the seating profile -----------------------------------------------------------------------------------------
def seat_top(d: float, aisle: bool = False) -> int:
    """The row (from the ground row) of the top block of the seating at offset d: the upper tier falls one row a cell
    from the top gallery, a walkway behind a two-row wall (in an aisle a stair instead), the lower tier, the podium
    terrace."""
    if d < 27:
        return TOP_GALLERY - math.floor(d - SEATS_TOP)
    if d < 30:
        return (24, 23, 22)[min(2, math.floor(d - 27))] if aisle else (24 if d < 28 else 22)
    if d < 46:
        return 21 - math.floor(d - 30)
    return 5


AISLES_Z = (CX - 30, CX, CX + 30)       # aisles along z on the long sides (x = const)
AISLES_X = (CZ - 20, CZ, CZ + 20)       # aisles along x at the ends (z = const)


def aisle_dir(x: int, z: int) -> tuple[int, int] | None:
    """The outward direction of the aisle a seat column lies in (None = not in an aisle). Two cells wide."""
    for a in AISLES_Z:
        if a - 1 <= x <= a + 1 and abs(x - CX) * RZ < abs(z - CZ) * RX:
            return (0, -1 if z < CZ else 1)
    for a in AISLES_X:
        if a - 1 <= z <= a + 1 and abs(x - CX) * RZ >= abs(z - CZ) * RX:
            return (-1 if x < CX else 1, 0)
    return None


# --- the building ------------------------------------------------------------------------------------------------
def ground(d: Design) -> None:
    """The ground row: a travertine apron and floor under the whole ring, sand in the arena."""
    d.fill({(x, G, z) for x, z in cols(-2.5, ARENA)}, TRAV)
    d.fill({(x, G, z) for x, z in cols(ARENA, 999)}, SAND)


def facade(d: Design) -> None:
    """The outer wall: three storeys of 80 arches (the upper two behind a one-row parapet) framed by dark half-columns
    and entablatures, the attic with pilasters and small windows, and the top cornice."""
    wall = d.fill(rows(*FACADE, R(1), R(CORNICES[-1])), FACE)
    for k, (y0, y1) in enumerate(STOREYS):
        sill = y0 if k == 0 else y0 + 1
        d.fill(solids.ring_openings(wall, OUTER, R(sill), BAYS, 4.2, 9.2, phase=-SPACING / 2), CLEAR)
    for i in range(BAYS):
        t = bay_angle((i + 0.5) * SPACING)
        x, z = (round(v) for v in at(t, -0.5))
        for k, (y0, y1) in enumerate(STOREYS):
            for y in range(y0, y1 + 1):
                d.put(x, R(y), z, TRIM if y == y1 else DARK, shape=0 if y == y1 else sc.pack(sc.CYLINDER))
        for y in range(ATTIC[0], ATTIC[1] + 1):
            d.put(x, R(y), z, DARK)
        if i % 2 == 0:                                                                     # attic windows
            d.fill(_attic_window(bay_angle(i * SPACING)) & wall, CLEAR)
    proud = cols(-1.0, 0.0)
    for y in CORNICES:
        d.fill({(x, R(y), z) for x, z in proud}, TRIM)
    d.fill({(x, R(CORNICES[-1]), z) for x, z in cols(-1.0, 2.0)}, TRIM)


def _attic_window(t: float) -> set:
    """A small window two wide and two high through the attic on the ray at elliptic angle t."""
    tx, tz = -(RX - 1) * math.sin(t), (RZ - 1) * math.cos(t)
    n = math.hypot(tx, tz)
    tx, tz = tx / n, tz / n
    out = set()
    for dd in (0.0, 0.5, 1.0, 1.5, 1.9):
        x, z = at(t, dd)
        for o in (-0.5, 0.5):
            out.add((round(x + o * tx), round(z + o * tz)))
    return {(x, R(y), z) for x, z in out for y in (41, 42)}


def rings(d: Design) -> None:
    """The second arcade ring and the ring wall behind the inner ambulatory, the floors of both ambulatories and the
    top gallery. The second ring's arches line up with the facade's (the upper ones behind a one-row parapet); none
    beside the stairs."""
    ring2 = d.fill(rows(*RING2, R(1), R(TOP_GALLERY)), INNER)
    near_stair = {(x, y, z) for x, y, z in ring2 if abs(x - CX) < 26 and z > CZ}
    for k, (y0, _) in enumerate(STOREYS):
        sill = y0 if k == 0 else y0 + 1
        d.fill(solids.ring_openings(ring2 - near_stair, OUTER, R(sill), BAYS, 4.0, 8.6, phase=-SPACING / 2), CLEAR)
    ring3 = d.fill(rows(*RING3, R(1), R(TOP_GALLERY)), INNER)
    # Into the space under the seats: one arch in every fourth bay, between the radial walls.
    d.fill(solids.ring_openings(ring3, OUTER, R(1), BAYS // 4, 3.0, 5.0, phase=-SPACING / 2), CLEAR)
    for y in (12, 24):
        d.fill({(x, R(y), z) for x, z in cols(AMB1[0], AMB2[1])}, TRAV)
    d.fill({(x, R(TOP_GALLERY), z) for x, z in cols(AMB1[0], SEATS_TOP)}, TRAV)


def seating(d: Design) -> None:
    """The seats: a stepped shell two rows thick from the top gallery down to the podium terrace (stairs in the
    aisles), the podium wall with its parapet, the radial walls and a ring wall under the seats."""
    for x, z in cols(SEATS_TOP, PODIUM[0]):
        dd = DMAP[(x, z)]
        aisle = aisle_dir(x, z)
        top = seat_top(dd, aisle is not None)
        d.fill({(x, R(top - 1), z), (x, R(top), z)}, SEAT)
        if 27 <= dd < 28 and aisle is None:                              # the wall behind the walkway
            d.fill({(x, R(y), z) for y in range(21, 24)}, SEAT)
        if aisle is not None:
            ox, oz = x + aisle[0], z + aisle[1]
            if (ox, oz) in DMAP and DMAP[(ox, oz)] >= SEATS_TOP and seat_top(DMAP[(ox, oz)], True) == top + 1:
                d.put(x, R(top), z, SEAT, shape=sc.stairs(*aisle))
    d.fill(rows(PODIUM[0], PODIUM[1], R(1), R(5)), DARK)
    d.fill({(x, R(6), z) for x, z in cols(PODIUM[0] + 1, PODIUM[1])}, TRAV)
    # Radial walls under the seats in every fourth bay (between the arches into the space under the seats), and the
    # ring wall under the walkway with an arch between every two radial walls.
    for i in range(BAYS // 4):
        t = bay_angle((4 * i + 2.5) * SPACING)
        line = set()
        dd = SEATS_TOP
        while dd < PODIUM[0]:
            x, z = at(t, dd)
            line.add((round(x), round(z)))
            dd += 0.3
        for x, z in line:
            if (x, z) in DMAP:
                top = seat_top(DMAP[(x, z)])
                d.fill({(x, R(y), z) for y in range(1, top - 1)}, INNER, where="empty")
    inner = d.fill({(x, y, z) for x, z in cols(29.0, 30.0) for y in range(R(1), R(19))}, INNER, where="empty")
    d.fill(solids.ring_openings(inner, OUTER, R(1), BAYS // 4, 3.0, 5.0, phase=-SPACING / 2), CLEAR)


AXES = (0.0, math.pi / 2, math.pi, 3 * math.pi / 2)


def gates(d: Design) -> None:
    """The four gates at the ends of the axes: straight passages four wide from the facade arch into the arena, as
    high as the seats above them allow (six rows outside, three under the podium)."""
    for t in AXES:
        along_x = abs(math.cos(t)) > 0.5
        for (x, z), dd in DMAP.items():
            if dd < 0 or dd >= PODIUM[1] + 0.5:
                continue
            across = z - CZ if along_x else x - CX
            if not -2.0 <= across <= 1.5 or (math.cos(t) * (x - CX) + math.sin(t) * (z - CZ)) <= 0:
                continue
            top = 6 if dd < SEATS_TOP else min(6, seat_top(dd) - 2) if dd < PODIUM[0] else 3
            d.fill({(x, R(y), z) for y in range(1, top + 1)}, CLEAR)


def _cell(normal: str, u: int, v: int) -> tuple[int, int]:
    return (u, v) if normal == "z" else (v, u)


def _arena_edge(normal: str, sign: int, u: int) -> int:
    """On the line u (x for the long sides, z for the ends), the last arena cell before the podium on side `sign`."""
    v = round(CZ if normal == "z" else CX)
    while DMAP.get(_cell(normal, u, v + sign), -99.0) >= ARENA:
        v += sign
    return v


def podium_stairs(d: Design) -> None:
    """Stairs in the arena up the podium wall beside every gate: five steps along the wall, two wide, and a gap in the
    parapet behind the top steps."""
    for normal, sign in (("z", -1), ("z", 1), ("x", -1), ("x", 1)):
        for k in range(5):
            u = round(CX if normal == "z" else CZ) + 4 + k
            edge = _arena_edge(normal, sign, u)
            for w in range(2):
                x, z = _cell(normal, u, edge - sign * w)
                d.put(x, R(1 + k), z, STAIR, shape=sc.stairs(1, 0) if normal == "z" else sc.stairs(0, 1))
            if k >= 3:
                for j in range(1, 5):
                    x, z = _cell(normal, u, edge + sign * j)
                    if DMAP.get((x, z), -99.0) < PODIUM[1]:
                        d.clear(x, R(6), z)


def ambulatory_stairs(d: Design) -> None:
    """Three flights in the inner ambulatory on the +Z long side (the other side is the fallen one), ground -> storey 2
    -> storey 3 -> the top gallery, two wide down its middle; each clears its headroom (the stairwell in the floor
    above)."""
    for side in (1,):
        for y0, x0, dx in ((0, -20, 1), (12, 20, -1), (24, -20, 1)):
            treads = set()
            for k in range(12):
                x = round(CX) + x0 + dx * k
                lane = [z for z in range(L) if (x, z) in DMAP and 9.0 <= DMAP[(x, z)] < 11.0 and (z - CZ) * side > 0]
                treads.update((x, R(y0 + 1 + k), z) for z in lane)
            for tx, ty, tz in treads:
                for h in range(1, 4):
                    if (tx, ty + h, tz) not in treads:
                        d.clear(tx, ty + h, tz)
            d.fill(treads, STAIR, shape=sc.stairs(dx, 0))


# --- the hypogeum ------------------------------------------------------------------------------------------------
HYPO_D = 53.0
CORRIDOR_D = 55.0
PEN_ANGLES = (math.pi / 4, 3 * math.pi / 4, 5 * math.pi / 4, 7 * math.pi / 4)   # side rooms off the ring corridor


def pen(t: float) -> tuple[set, tuple[int, int]]:
    """A side room (a pen for the animals of old) off the ring corridor, out under the podium at elliptic angle t: a
    three by three room and the short passage to the corridor (rows 1..3). Returns its cells and its middle column."""
    cells = set()
    for dd in (51.0, 51.5, 52.0, 52.5, 53.0, 53.5, 54.0, 54.5, 55.0, 55.5):
        x, z = at(t, dd)
        cells.update((round(x), y, round(z)) for y in range(1, 4))
    mx, mz = (round(v) for v in at(t, 51.5))
    cells.update((x, y, z) for x in range(mx - 1, mx + 2) for z in range(mz - 1, mz + 2) for y in range(1, 4))
    return cells, (mx, mz)


def hypogeum(d: Design) -> None:
    """The tunnels under the arena: a masonry block in rows 0..5 with corridors carved out of it (rows 1..4): one down
    the long axis, two beside it on each side, cross corridors every twelve blocks and a ring corridor; side rooms; a
    trench in the arena floor over the middle of the central corridor, stairs at both ends of it and four ladder shafts."""
    d.fill({(x, y, z) for x, z in cols(HYPO_D, 999) for y in range(G)}, HYPO)
    tunnels = set()
    cx, cz = round(CX), round(CZ)

    def carve(x0: int, z0: int, x1: int, z1: int) -> None:
        for x in range(min(x0, x1), max(x0, x1) + 1):
            for z in range(min(z0, z1), max(z0, z1) + 1):
                if DMAP.get((x, z), -99.0) >= CORRIDOR_D:
                    tunnels.update((x, y, z) for y in range(1, 5))

    carve(0, cz - 2, W - 1, cz + 1)                                     # the central corridor (four wide)
    for off in (9, 17):
        carve(0, cz - off - 1, W - 1, cz - off + 1)
        carve(0, cz + off - 2, W - 1, cz + off)
    for k in range(-3, 4):
        carve(cx + 12 * k - 1, 0, cx + 12 * k + 1, L - 1)
    tunnels.update((x, y, z) for x, z in cols(CORRIDOR_D, CORRIDOR_D + 3) for y in range(1, 5))
    for t in PEN_ANGLES:
        tunnels.update(pen(t)[0])
    d.fill(tunnels, CAVITY)
    # The trench: the arena floor and the vault under it open over the middle of the central corridor.
    d.fill({(x, y, z) for x in range(cx - 18, cx + 18) for z in range(cz - 2, cz + 2) for y in (5, G)}, CAVITY)
    # Stairs down from the arena at both ends of the central corridor: the floor above them carved open.
    for sign in (-1, 1):
        for k in range(5):
            x = cx + sign * (34 - k)
            for z in range(cz - 1, cz + 1):
                d.put(x, 5 - k, z, STAIR, shape=sc.stairs(sign, 0))
                for y in range(5 - k + 1, G + 3):
                    d.put(x, y, z, CAVITY)
    # Ladder shafts: a cavity column from a corridor up through the arena floor, a ladder beside it.
    for x, z in ((cx - 12, cz - 9), (cx + 12, cz + 9), (cx - 24, cz + 17), (cx + 24, cz - 17)):
        for y in range(1, G + 3):
            d.put(x, y, z, CAVITY)
        for y in range(1, G + 1):
            d.put(x + 1, y, z, "ladder")
    d.fill({p for p in tunnels if p[1] == 3 and (p[0] + p[2]) % 11 == 0 and _beside_wall(d, p)}, Mat("torch"))


def _beside_wall(d: Design, p: tuple[int, int, int]) -> bool:
    x, y, z = p
    return any(d.is_block(x + dx, y, z + dz) for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)))


# --- life ----------------------------------------------------------------------------------------------------------
def lights(d: Design) -> None:
    """Torches in both ambulatories, every fourth bay on each storey."""
    spots = []
    for i in range(0, BAYS, 4):
        t = bay_angle((i + 0.5) * SPACING)
        for y in (1, 13, 25):
            for dd in (5.5, 8.5):
                x, z = at(t, dd)
                spots.append((round(x), R(y + 3), round(z)))
    cl.lights(d, spots)


def loot(d: Design) -> None:
    """Chests in the four pens of the hypogeum, on the top gallery and under the seats; the wind in the arena."""
    cx, cz = round(CX), round(CZ)
    for t, kw in zip(PEN_ANGLES, ({"table": "ruin_rich"}, {"table": "ruin_common"},
                                  {"table": "ruin_common", "hidden": True}, {"table": "ruin_common", "chance": 0.6})):
        x, z = pen(t)[1]
        d.chest(x, 1, z, **kw)
    x, z = at(bay_angle(30.5 * SPACING), 4.0)
    d.chest(round(x), R(TOP_GALLERY + 1), round(z), table="ruin_common", chance=0.7)
    x, z = at(bay_angle(52 * SPACING), 20.0)
    d.chest(round(x), R(1), round(z), table="ruin_common")
    d.sound(cx, R(2), cz - 12, "amb_portasseum_wind", radius=120, volume=0.55)


FALLEN = (3 * math.pi / 2 - 0.6, 3 * math.pi / 2 + 0.7)   # the elliptic angles where the outer ring has come down
RUBBLE = Mix(Mat("scree"), (Mat("travertine", tint=0xA8A08C), 1.2), (TRAV, 0.8), (DARK, 0.3), seed=33)


def fallen_side(d: Design) -> None:
    """The stretch of the -Z side where the outer ring has come down, as on the real one: the facade and the outer
    ambulatory's floors are gone down to a ragged line that climbs storey by storey toward both ends, the dark second
    arcade stands bare behind it, and rubble lies heaped at its foot. (The design carries this loss; the ruin pass
    decays the rest — and this side harder, by the ruin zones.)"""
    a0, a1 = FALLEN
    span = a1 - a0
    cuts = (R(1), R(12), R(24), R(36))                      # keep up to here: the stump, one, two, three storeys

    def kept_top(x: int, z: int, t: float) -> int | None:
        f = min(t - a0, a1 - t) / span                      # 0 at the ends of the stretch, 0.5 in its middle
        level = 4 - min(4, int(f / 0.06))
        level += -1 if solids.hash01(int(bay_of(x, z)), 0, 0, 34) < 0.3 and level > 0 else 0
        if level >= 4:
            return None
        return cuts[level] + (int(solids.hash01(x, 0, z, 35) * 3) if level == 0 else 0)

    for (x, z), dd in DMAP.items():
        if not -1.0 <= dd < AMB1[1]:
            continue
        t = OUTER.angle(x, z)
        if not a0 <= t <= a1:
            continue
        top = kept_top(x, z, t)
        if top is None:
            continue
        for y in range(top + 1, R(CORNICES[-1]) + 1):
            d.clear(x, y, z)
    for (x, z), dd in DMAP.items():
        t = OUTER.angle(x, z)
        if not (-2.5 <= dd < 4.0 and a0 + 0.08 <= t <= a1 - 0.08) or d.get(x, R(1), z) is not None:
            continue
        near = 0.5 if 0.0 <= dd < 3.0 else 0.3
        if solids.hash01(x, 1, z, 36) < near:
            d.put(x, R(1), z, RUBBLE)
            if solids.hash01(x, 2, z, 36) < near * 0.4 and d.get(x, R(2), z) is None:
                d.put(x, R(2), z, RUBBLE)


def build() -> Design:
    d = Design("portasseum", "Portasseum", nameKey="landmark.portasseum.name", ruin="always", ruinProfile="masonry",
               ruinStrength=0.9, requires=["atmosphere"], weight=1, groundRow=G, minTerrainGeneration=39)  # ~20-25 % falls
    ground(d)
    facade(d)
    rings(d)
    seating(d)
    gates(d)
    podium_stairs(d)
    ambulatory_stairs(d)
    hypogeum(d)
    lights(d)
    loot(d)
    fallen_side(d)
    # One side has fallen: the facade, the ambulatories and the upper seats on the -Z side decay far more.
    d.ruin_zone(0, R(1), 0, W - 1, R(CORNICES[-1]), 26, "masonry", 1.0)
    d.ruin_zone(0, R(1), 27, W - 1, R(CORNICES[-1]), 40, "masonry", 0.95)
    return d


if __name__ == "__main__":
    build().write()
