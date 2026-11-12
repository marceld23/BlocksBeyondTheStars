"""The leaning tower: a round bell tower of pale travertine — a blind-arcaded ground storey, six open galleries of
round columns and little arches, a narrower belfry with its bells on top — leaning a little further with every storey,
eight blocks out at the top (about four degrees, as the real one leans).

Inside, a spiral stair (kept by the ruin pass) climbs from the door to the belfry between a hollow newel — a well you
can look up through from the ground floor — and the core wall; it passes a doorway onto every gallery, so the player
walks out of the stair onto each of them in turn. From the belfry a ladder leads onto the flat roof.

Proportions after the real tower (55.9 m high, 15.5 m across, 3.97 degrees): 29 blocks across the galleries, 113 rows
high, the ground storey twice a gallery's height, the belfry a little narrower than the galleries.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from geom import CLEAR, Design, Mat, Mix

C = 16.0                 # the tower's axis (x and z) before the lean; the stylobate reaches 16 out
STAIR_Y0 = 3             # the first tread (the floor slab lies on row 2)
STEPS_PER_TURN = 12      # one turn of the stair per gallery storey
GROUND_TOP = 26          # the ground storey: rows 3..26
GALLERY = 12             # rows per gallery storey
GALLERIES = 6
BELFRY_FLOOR = GROUND_TOP + 1 + GALLERIES * GALLERY   # 99
ROOF = BELFRY_FLOOR + 12                              # 111
LEAN_TOP = 8             # blocks the top stands out toward +X
FRONT = -math.pi / 2     # the door (and every gallery doorway) faces -Z

R_RIM = 15.5             # the gallery floors, standing out as cornices
R_FACE = 15.0            # the parapet and the arch band
R_COLUMNS = 14.25        # the gallery columns' centre line
R_PARAPET_IN = 13.5      # a one-row parapet between the column bases
R_CORE = 11.0            # the core wall (9..11) around the stair
R_STAIR = 9.0            # the stair's outer edge (= the core wall's inner face)
R_NEWEL = 4.5            # the hollow newel round the well
R_WELL = 3.5

STONE = Mix(Mat("travertine"), (Mat("travertine", tint=0xA49C8A), 0.09), (Mat("travertine", tint=0xB4B2AA), 0.07), seed=11)
PLAIN = Mat("travertine")
MARBLE = Mat("travertine", tint=0xDAD6CC)       # the columns and the arch trim read a little whiter
DARK = Mat("travertine", tint=0x8E8676)         # the grey bands of the ground-storey arcade
STAIR = Mat("travertine", keep=True)
GLASS = Mat("glass")
BRONZE = Mat("bronze_block")


def ring_cells(y0: int, y1: int, r_out: float, r_in: float) -> set:
    """The cells of the annulus r_in < dist <= r_out round the axis, rows y0..y1."""
    return solids.extrude(solids.disc(C, C, r_out) - solids.disc(C, C, r_in), y0, y1)


def at_angle(r: float, a: float) -> tuple[int, int]:
    return round(C + r * math.cos(a)), round(C + r * math.sin(a))


def radial_cut(a: float, y0: int, y1: int, r0: float, r1: float, width: float = 1.0) -> set:
    """The cells along the ray at angle `a` from radius r0 to r1 (a slit window, a doorway), `width` wide across it."""
    out = set()
    ux, uz = math.cos(a), math.sin(a)
    tx, tz = -uz, ux
    r = r0
    while r <= r1 + 1e-9:
        for k in range(int(width * 2) + 1):
            o = -width / 2 + (k + 0.5) * width / (int(width * 2) + 1)
            x, z = round(C + r * ux + o * tx), round(C + r * uz + o * tz)
            for y in range(y0, y1 + 1):
                out.add((x, y, z))
        r += 0.35
    return out


def sector_angle(sector: int) -> float:
    """The middle of one step of the stair (sector 0 is the step at the door)."""
    return FRONT + (sector + 0.5) * 2 * math.pi / STEPS_PER_TURN - math.pi / STEPS_PER_TURN


def base_and_ground_storey(d: Design) -> None:
    """The stylobate, the floor slab and the thick ground storey with its fifteen blind arches and engaged columns."""
    d.disc(C, C, 0, 16.0, PLAIN)
    d.disc(C, C, 1, 15.5, PLAIN)
    d.disc(C, C, 2, 15.0, PLAIN)
    wall = d.fill(ring_cells(3, GROUND_TOP, 14.5, R_STAIR), STONE)
    skin = {p for p in wall if math.hypot(p[0] - C, p[2] - C) > 13.5}
    arcade = solids.Ellipse(C, C, 14.0, 14.0)
    d.fill(solids.ring_openings(skin, arcade, 4, 15, 4.2, 19.0), CLEAR)          # the recesses of the blind arcade
    for x, z, _ in arcade.points(15):
        cx, cz = round(x), round(z)
        for y in range(4, 22):
            if d.is_block(cx, y, cz):
                d.put(cx, y, cz, MARBLE, shape=sc.pack(sc.CYLINDER) if 5 <= y <= 20 else 0)
    d.fill(ring_cells(GROUND_TOP - 1, GROUND_TOP, 15.0, 13.5), PLAIN)           # the cornice
    d.fill(ring_cells(3, 3, 14.5, 13.5), DARK)                                     # the plinth course
    # The door at the front: arched, three wide and five high, through the whole wall onto the first step, and two
    # steps up the stylobate to it.
    d.fill(solids.arch("z", round(C) - 1, 3, 0, round(C), 3, 5, "round") & wall, CLEAR)
    for x in range(round(C) - 1, round(C) + 2):
        d.put(x, 1, 0, PLAIN, shape=sc.stairs(0, 1))
        d.put(x, 2, 1, PLAIN, shape=sc.stairs(0, 1))
    # Slit windows along the stair in three of the blind arches, glazed at the outer face.
    for i, y in ((4, 9), (8, 14), (11, 19)):
        a = FRONT + i * 2 * math.pi / 15 + math.pi / 15
        slit = radial_cut(a, y, y + 2, R_STAIR + 0.3, 13.4)
        d.fill(slit, CLEAR)
        outer = max(slit, key=lambda p: math.hypot(p[0] - C, p[2] - C))
        d.fill({(outer[0], yy, outer[2]) for yy in range(y, y + 3)}, GLASS)


def gallery(d: Design, k: int) -> None:
    """Gallery storey k (1..6): the floor with its cornice, the core wall with a doorway off the stair and three windows,
    a walkway behind a one-row parapet, 24 round columns and a band of 24 little arches carrying the next floor."""
    y = GROUND_TOP + 1 + (k - 1) * GALLERY
    d.fill(ring_cells(y, y, R_RIM, R_STAIR), PLAIN)
    core = d.fill(ring_cells(y + 1, y + GALLERY - 1, R_CORE, R_STAIR), STONE)
    d.fill(ring_cells(y + 1, y + 1, R_FACE, R_PARAPET_IN), PLAIN)
    columns = solids.Ellipse(C, C, R_COLUMNS, R_COLUMNS)
    for x, z, _ in columns.points(24):
        d.column(round(x), round(z), y + 2, y + 8, MARBLE, base=PLAIN, capital=PLAIN)
    band = d.fill(ring_cells(y + 9, y + GALLERY - 1, R_FACE, 12.5), STONE)
    d.fill(solids.ring_openings(band, columns, y + 9, 24, 2.3, 2.6), CLEAR)
    # The doorway off the stair (the stair passes sector 0 on this storey's floor row) and three windows.
    d.fill(solids.arch("z", round(C) - 1, y + 1, 0, round(C), 3, 4, "round") & core, CLEAR)
    for a in (0.0, math.pi / 2, math.pi):
        d.fill(radial_cut(a, y + 5, y + 7, R_STAIR + 0.3, R_CORE, 1.6), GLASS, where="blocks")
    for a in (math.pi / 4, 3 * math.pi / 4):                                      # lanterns on the gallery
        x, z = at_angle(R_CORE - 0.3, a + (k % 2) * math.pi)
        d.put(x, y + 4, z, "lantern")


def belfry(d: Design) -> None:
    """The terrace round the belfry, the belfry with eight bell openings and seven bells, the flat roof with its parapet,
    the ladder up to it and the glass lid over the well."""
    y = BELFRY_FLOOR
    d.disc(C, C, y, R_RIM, PLAIN)
    d.disc(C, C, y, R_WELL, GLASS)
    d.fill(ring_cells(y + 1, y + 1, R_FACE, R_PARAPET_IN), PLAIN)
    wall = d.fill(ring_cells(y + 1, ROOF - 1, 11.5, R_STAIR), STONE)
    openings = solids.Ellipse(C, C, 10.25, 10.25)
    spacing = openings.perimeter / 8
    d.fill(solids.ring_openings(wall, openings, y + 1, 8, 3.2, 7.0, phase=-spacing / 2), CLEAR)
    d.fill(ring_cells(ROOF - 2, ROOF - 1, 12.0, 11.5), PLAIN)                    # the belfry's cornice
    d.disc(C, C, ROOF, 12.0, PLAIN)
    d.fill(ring_cells(ROOF + 1, ROOF + 1, 12.0, 11.0), PLAIN)
    # Seven bells, as in the real belfry: one in every opening but the one at the front, where the stair comes up.
    for i in (0, 1, 2, 3, 4, 5, 7):
        a = i * 2 * math.pi / 8
        x, z = at_angle(10.25, a)
        d.put(x, y + 6, z, BRONZE, shape=sc.post())
        d.put(x, y + 5, z, BRONZE, shape=sc.pack(sc.DOME))
        if i in (0, 4):                                                           # two big bells
            d.put(x, y + 4, z, BRONZE, shape=sc.pack(sc.DOME))
            d.put(x, y + 5, z, BRONZE)
    lx, lz = at_angle(R_STAIR - 0.2, math.pi / 8)          # against a pier, clear of the stairwell (sectors 9..11)
    for yy in range(y + 1, ROOF + 1):
        d.put(lx, yy, lz, "ladder")
    for a in (5 * math.pi / 8, 15 * math.pi / 8):
        x, z = at_angle(R_STAIR - 0.3, a)
        d.put(x, y + 4, z, "lantern")


def stair(d: Design) -> set:
    """The spiral stair from the door to the belfry floor and its hollow newel, both kept; lanterns light it."""
    d.fill(ring_cells(STAIR_Y0, BELFRY_FLOOR - 1, R_NEWEL, R_WELL), STAIR)
    start = FRONT - math.pi / STEPS_PER_TURN
    treads = d.spiral_stair(C, C, STAIR_Y0, BELFRY_FLOOR, R_NEWEL, R_STAIR, STAIR, steps_per_turn=STEPS_PER_TURN,
                            ccw=True, start=start, clear_above=3)
    # A doorway into the well at the foot of the newel, on the far side under the stair (reached under the stair
    # from the door, walking clockwise).
    well_door = radial_cut(-FRONT, STAIR_Y0, STAIR_Y0 + 2, R_WELL - 0.2, R_NEWEL + 0.2, 1.6)
    d.fill(well_door, CLEAR)
    for turn in range((BELFRY_FLOOR - STAIR_Y0) // STEPS_PER_TURN):
        for s in (3, 7, 11):
            yy = STAIR_Y0 + turn * STEPS_PER_TURN + s + 2
            x, z = at_angle(R_STAIR + 0.6, sector_angle(s))
            if yy < BELFRY_FLOOR and d.is_block(x, yy, z):
                d.put(x, yy, z, "lantern")
    return treads


def storey_shift(y: int) -> tuple[int, int]:
    """The lean: each storey shifts as one block — the ground storey stands straight, every gallery one more block
    toward +X, the belfry two more: LEAN_TOP at the top."""
    if y <= GROUND_TOP:
        return (0, 0)
    if y < BELFRY_FLOOR:
        return (1 + (y - GROUND_TOP - 1) // GALLERY, 0)
    return (LEAN_TOP, 0)


def lean(d: Design, treads: set) -> None:
    """Leans the tower, then closes the gap the shift opens in the stair at every storey line: the first step of a
    storey (sector 0, in front of the doorway, where the stair runs toward +X) moved ahead of the last step below it, so
    it gets one (the belfry: two) more step cells behind it."""
    d.shear(storey_shift)
    for x, y, z in treads:
        jump = storey_shift(y)[0] - storey_shift(y - 1)[0]
        for back in range(1, jump + 1):
            cell = (x + storey_shift(y)[0] - back, y, z)
            if cell not in d.plain:
                d.put(*cell, STAIR, shape=sc.stairs(1, 0))


def build() -> Design:
    d = Design("leaning_tower", "Leaning Tower", nameKey="landmark.leaning_tower.name", ruin="always",
               ruinProfile="masonry", ruinStrength=0.8, requires=["atmosphere"], weight=1,  # ~20-30 % falls
               minTerrainGeneration=39)
    base_and_ground_storey(d)
    for k in range(1, GALLERIES + 1):
        gallery(d, k)
    belfry(d)
    treads = stair(d)
    # The ground floor: a lantern by the door and one in the well, a chest in the well.
    d.put(round(C) + 4, 5, round(C - 10.0), "lantern")
    d.put(round(C), STAIR_Y0, round(C) + 2, "lantern")
    d.chest(round(C), STAIR_Y0, round(C), table="ruin_common")
    # A chest on the third gallery behind the tower, one in the belfry.
    g3 = GROUND_TOP + 1 + 2 * GALLERY
    x, z = at_angle(12.2, math.pi / 2)
    d.chest(x, g3 + 1, z, table="ruin_common", chance=0.7)
    x, z = at_angle(6.5, math.pi / 2)
    d.chest(x, BELFRY_FLOOR + 1, z, table="ruin_rich")
    lean(d, treads)
    d.normalize()
    return d


if __name__ == "__main__":
    build().write()
