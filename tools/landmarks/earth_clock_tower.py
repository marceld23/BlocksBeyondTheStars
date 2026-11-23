"""Earth's clock tower of London (#3982): `earth_london_clock_tower`, about 97 high on a 19 square — a gothic stone tower
at about full scale.

A stepped plinth, the tall shaft of honey-coloured stone with corner buttresses and tall panels of narrow pointed
windows, the wider clock stage with four big lit clock faces in gilded frames (hour marks and hands), little gilded
pinnacles at its corners, the open belfry with the great bell and four smaller bells, the glazed lantern with its light
and the steep slate spire with gilded ridges up to a golden finial. Inside a spiral stair climbs from the door through
three floors to the clock room and the belfry; a ladder goes on up to the lantern. Intact, lit and protected (an
inhabited city's landmark); pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, glow
from geom import CLEAR, Design, Mat

KEYS = ("earth_london_clock_tower",)
C = 9                               # the centre column (x and z)
SHAFT = 7                           # the shaft's half size (15 wide)
STAGE = 8                           # the clock stage's half size (17 wide)
FLOORS = (2, 20, 37, 55)            # floor rows (walk one above); 55 = the clock room
CLOCK = (55, 68)                    # the clock stage's rows
DIAL_Y = 61                         # the dials' centre row
BELFRY = (69, 79)
LANTERN = (80, 84)                  # its floor slab and glazed rows
SPIRE = 85                          # the spire's first row
TOP = 97

STONE = Mat("sandstone", tint=0xD6BE8C)
STONE_DARK = Mat("sandstone", tint=0xBFA572)
TRIM = Mat("travertine", tint=0xE8DAB4)
GOLD = Mat("gold_block")
SLATE = Mat("metal_panel", tint=0x3C4654)
DIAL = Mat("travertine", tint=0xF7EFD8, glow=0xFFE8A8)
HAND = Mat("carved_obsidian")
BRONZE = Mat("bronze_block")


def faces(cx: int, cz: int, half: int):
    """The four faces: (outward x, outward z, the plane's coordinate) — front (-z) first."""
    return ((0, -1, cz - half), (1, 0, cx + half), (0, 1, cz + half), (-1, 0, cx - half))


def on_face(ox: int, oz: int, plane: int, u: int, y: int, out: int = 0) -> tuple[int, int, int]:
    """The cell u along a face (u = 0 at the centre, to the viewer's right) on row y, `out` cells proud of the plane."""
    if ox == 0:
        return (C + u * (-oz), y, plane + oz * out)
    return (plane + ox * out, y, C + u * ox)


def plinth(d: Design) -> None:
    """Three stepped courses; the front step is a stair to the door."""
    for y, half in ((0, 9), (1, 8.5), (2, 8)):
        h = math.floor(half)
        d.box(C - h, y, C - h, C + h, y, C + h, TRIM if y < 2 else STONE)
    for x in range(C - 1, C + 2):
        d.put(x, 1, C - 9, TRIM, shape=sc.stairs(0, 1))
        d.put(x, 2, C - 9, TRIM)
        d.put(x, 2, C - 8, TRIM, shape=sc.stairs(0, 1))


def shaft(d: Design) -> None:
    """The hollow shaft (walls two thick), corner buttresses, the panels of lancet windows, the floors."""
    lo, hi = C - SHAFT, C + SHAFT
    for y in range(3, CLOCK[0]):
        d.fill(solids.box(lo, y, lo, hi, y, hi) - solids.box(lo + 2, y, lo + 2, hi - 2, y, hi - 2),
               STONE_DARK if y % 9 == 0 else STONE)
        for x, z in ((lo, lo), (hi, lo), (lo, hi), (hi, hi)):            # the buttresses, one proud each way
            d.fill({(x + sx, y, z + sz) for sx in (0, -1 if x == lo else 1) for sz in (0, -1 if z == lo else 1)}, TRIM)
    for ox, oz, plane in faces(C, C, SHAFT):
        for u in (-4, -2, 0, 2, 4):                                       # the panels: pilasters, windows between
            for y in range(8, CLOCK[0]):
                d.put(*on_face(ox, oz, plane, u, y, 1), TRIM)
        for u in (-3, -1, 1, 3):
            for base in (8, 25, 42):
                for y in range(base, base + 6):
                    d.put(*on_face(ox, oz, plane, u, y), Mat("glass", tint=0x8C9AA8))
                d.put(*on_face(ox, oz, plane, u, base + 6), TRIM, shape=sc.pack(sc.PYRAMID))
    for fy in FLOORS[1:]:
        d.box(lo + 2, fy, lo + 2, hi - 2, fy, hi - 2, Mat("wood_planks"))
    d.box(lo + 2, FLOORS[0], lo + 2, hi - 2, FLOORS[0], hi - 2, Mat("stone"))
    for y in range(3, 7):                                                 # the door (-z)
        for x in range(C - 1, C + 2):
            d.fill([(x, y, lo), (x, y, lo + 1)], CLEAR)
    d.door(C - 1, 3, lo)
    for x in range(C - 2, C + 3):
        d.put(x, 7, lo - 1, TRIM, shape=sc.ramp_down(0, 1))


def clock_stage(d: Design) -> None:
    """The wider clock stage with a dial on every face, a gabled hood over each and gilded corner pinnacles."""
    lo, hi = C - STAGE, C + STAGE
    for y in range(CLOCK[0], CLOCK[1] + 1):
        d.fill(solids.box(lo, y, lo, hi, y, hi) - solids.box(lo + 2, y, lo + 2, hi - 2, y, hi - 2),
               TRIM if y in CLOCK else STONE)
    for ox, oz, plane in faces(C, C, STAGE):
        dial(d, ox, oz, plane)
        for k in range(4):                                                # the hood: a little gable over the dial
            for u in range(-4 + k, 5 - k):
                d.put(*on_face(ox, oz, plane, u, CLOCK[1] + 1 + k), TRIM)
        d.put(*on_face(ox, oz, plane, 0, CLOCK[1] + 5), GOLD, shape=sc.pack(sc.PYRAMID))
    for x, z in ((lo, lo), (hi, lo), (lo, hi), (hi, hi)):
        d.fill([(x, y, z) for y in range(CLOCK[1] + 1, CLOCK[1] + 6)], TRIM)
        d.put(x, CLOCK[1] + 6, z, GOLD, shape=sc.pack(sc.PYRAMID))


def dial(d: Design, ox: int, oz: int, plane: int) -> None:
    """A clock face: a lit disc 11 across in a gilded ring, twelve dark hour marks, the hands at ten to two... almost."""
    r_face, r_ring = 5.5, 6.5
    for u in range(-7, 8):
        for v in range(-7, 8):
            dist = math.hypot(u, v)
            if dist <= r_face:
                m = DIAL
            elif dist <= r_ring:
                m = GOLD
            else:
                continue
            d.put(*on_face(ox, oz, plane, u, DIAL_Y + v), m)
    for k in range(12):
        a = k * math.pi / 6
        d.put(*on_face(ox, oz, plane, round(4.6 * math.sin(a)), DIAL_Y + round(4.6 * math.cos(a))), HAND)
    for t in range(0, 5):                                                 # the minute hand, straight up
        d.put(*on_face(ox, oz, plane, 0, DIAL_Y + t), HAND)
    for t in range(1, 4):                                                 # the hour hand, toward two
        d.put(*on_face(ox, oz, plane, t, DIAL_Y + round(t * 0.55)), HAND)


def belfry(d: Design) -> None:
    """The open belfry: three pointed openings a face, the great bell in the middle and four quarter bells."""
    lo, hi = C - SHAFT, C + SHAFT
    for y in range(BELFRY[0], BELFRY[1] + 1):
        d.fill(solids.box(lo, y, lo, hi, y, hi) - solids.box(lo + 1, y, lo + 1, hi - 1, y, hi - 1), STONE)
    for u0 in (C - 6, C - 1, C + 4):
        for axis, a0, a1 in (("z", lo, lo), ("z", hi, hi), ("x", lo, lo), ("x", hi, hi)):
            d.arch(axis, u0, BELFRY[0] + 2, a0, a1, 3, 7, kind="pointed")
    d.box(lo, LANTERN[0], lo, hi, LANTERN[0], hi, TRIM)
    d.box(lo - 1, LANTERN[0] - 1, lo - 1, hi + 1, LANTERN[0] - 1, hi + 1, TRIM)       # the cornice
    bell(d, C, C, LANTERN[0] - 1, 3)
    for x, z in ((C - 4, C - 4), (C + 4, C - 4), (C - 4, C + 4), (C + 4, C + 4)):
        bell(d, x, z, LANTERN[0] - 1, 1)


def bell(d: Design, x: int, z: int, hang: int, size: int) -> None:
    """A bronze bell hanging from row `hang`: a post, then a widening body `size` rows below it."""
    d.put(x, hang, z, BRONZE, shape=sc.post())
    if size <= 1:
        d.put(x, hang - 1, z, BRONZE, shape=sc.pack(sc.DOME))
        return
    for k in range(size):
        y = hang - 1 - k
        d.fill({(xx, y, zz) for xx, zz in solids.disc(x, z, 0.6 + k * 0.8)}, BRONZE)
    d.put(x, hang - size - 1, z, BRONZE)


def lantern_and_spire(d: Design) -> None:
    """The glazed lantern (lit inside), then the steep slate spire with gilded ridges and the finial."""
    lo, hi = C - 5, C + 5
    for y in range(LANTERN[0] + 1, LANTERN[1] + 1):
        ring = solids.box(lo, y, lo, hi, y, hi) - solids.box(lo + 1, y, lo + 1, hi - 1, y, hi - 1)
        d.fill(ring, lambda x, yy, z: GOLD if (x - C) % 5 == 0 and (z - C) % 5 == 0 else Mat("glass", tint=0xFFF0C0))
    d.put(C, LANTERN[0] + 1, C, glow("light_white", 0xFFF2C0))
    d.put(C, LANTERN[0] + 2, C, glow("light_white", 0xFFF2C0))
    spire = d.pyramid(lo, lo, SPIRE, 11, SLATE, step=1, rise=2, top=1, wall=1)
    d.restyle({p for p in spire if abs(p[0] - C) == abs(p[2] - C)}, block="gold_block", tint=0)   # the ridges
    d.box(lo - 1, SPIRE - 1, lo - 1, hi + 1, SPIRE - 1, hi + 1, TRIM)
    top = max(y for _, y, _ in spire)
    d.fill([(C, y, C) for y in range(top + 1, TOP)], GOLD, shape=sc.post())
    d.put(C, TOP, C, GOLD, shape=sc.pack(sc.SPHERE))


def inside(d: Design) -> None:
    """The spiral stair from the door to the belfry, a ladder on to the lantern, lanterns on every floor, the clock
    room's works, chests."""
    d.spiral_stair(C, C, 3, BELFRY[0], 1.5, 3.5, Mat("stone"), steps_per_turn=14, newel=STONE_DARK)
    for y in range(BELFRY[0], LANTERN[0] + 1):
        d.put(C - 5, y, C - 5, "ladder")
    for fy in FLOORS:
        for ox, oz in ((-1, 0), (1, 0), (0, 1)):                          # on the wall: the last free cell outward
            k = 4
            while k < 8 and (C + ox * (k + 1), fy + 3, C + oz * (k + 1)) not in d.plain:
                k += 1
            if (C + ox * k, fy + 3, C + oz * k) not in d.plain:
                d.put(C + ox * k, fy + 3, C + oz * k, "lantern")
    works = CLOCK[0] + 1
    for x, z in ((C + 4, C - 4), (C - 4, C + 4)):
        d.put(x, works, z, Mat("machine_block"))
        d.put(x, works + 1, z, Mat("iron_block", tint=0x8A7A5A), shape=sc.pack(sc.CYLINDER))
    d.chest(C + 4, works, C + 4, items="iron_plate:2-3,copper_wire:2-4,crystal:1")
    d.chest(C + 4, FLOORS[1] + 1, C - 4, table="ruin_common")


def build() -> Design:
    d = earth_design("earth_london_clock_tower", "Clock Tower")
    plinth(d)
    shaft(d)
    clock_stage(d)
    belfry(d)
    lantern_and_spire(d)
    inside(d)
    return d


if __name__ == "__main__":
    build().write()
