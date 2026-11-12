"""The castle of Soaniri: an old castle of the Sounders' dark carved stone, standing as a ruin in the moss meadows.

An octagonal curtain wall (~156 across, 22 rows to the wall-walk, merlons on top) with a round tower at each of its
eight corners (spiral stairs from the courtyard to the wall-walk and the tower roof), a gatehouse with an arched gate
on the front side, stairs up the inside of the wall, and in the middle the great keep: 40 x 40, eighty rows, eight
floors round a spiral stair (its treads are kept, so the ruin stays climbable), four corner turrets with crystal-tipped
caps and a beacon of resonance crystal on the roof. Glowing crystal sills under the windows, chime moss on the wall
tops and in the courtyard. Soundis live in it (three dens in the courtyard), and a loop of their soft calls echoes
between the walls; chests in the keep, the towers and one hidden in the courtyard.

A ruin (masonry 0.5) and a fixed landmark of Soaniri only (`planetTypes: sounder_world`).
"""
from __future__ import annotations

import math

import solids
from geom import CLEAR, Design, Mat, Mix

R = 76                              # the curtain wall's corner radius (the towers stand on the corners)
WALL = 3                            # its thickness
WALK = 21                           # the wall-walk's row (walk on 22)
TOWER_R, TOWER_TOP = 8, 31          # the corner towers: radius, roof row
C = R + TOWER_R + 1                 # the centre column (x and z)
KEEP = 40                           # the keep's width (x and z)
KEEP_TOP = 80                       # the keep's roof row
FLOORS = list(range(0, KEEP_TOP, 10))  # the keep's floor rows
KEEP_RUIN, WALL_RUIN = 0.65, 0.75    # the ruin strengths of the two zones (the keep, the rest)

STONE = Mix("carved_obsidian", ("obsidian", 0.14), seed=31)
TRIM = Mat("obsidian")
CRYSTAL = Mat("resonance_crystal")
MOSS = Mat("chime_moss")
LANTERN = Mat("paper_lantern")


def corners() -> list[tuple[float, float]]:
    """The octagon's corners, the first at 22.5 degrees from +z (so a straight side faces -z: the gate side)."""
    return [(C + R * math.sin(math.pi / 8 + k * math.pi / 4), C + R * math.cos(math.pi / 8 + k * math.pi / 4))
            for k in range(8)]


def octagon(radius: float) -> set[tuple[int, int]]:
    return solids.polygon([(C + radius * math.sin(math.pi / 8 + k * math.pi / 4), C + radius * math.cos(math.pi / 8 + k * math.pi / 4))
                           for k in range(8)])


def radial_cut(d: Design, cx: float, cz: float, toward: tuple[float, float], cells, y0: int, y1: int, half: float = 1.0,
               m=CLEAR) -> None:
    """An opening through the wall `cells` of a round tower at (cx, cz), facing `toward`: rows y0..y1, `half` cells to
    each side of the ray."""
    ax, az = toward[0] - cx, toward[1] - cz
    n = math.hypot(ax, az)
    ax, az = ax / n, az / n
    hit = [(x, y, z) for x, y, z in cells if y0 <= y <= y1
           and abs((x - cx) * az - (z - cz) * ax) <= half + 0.01 and (x - cx) * ax + (z - cz) * az > 0]
    d.fill(hit, m)


# ---------------------------------------------------------------- the curtain wall, the towers, the gatehouse
def curtain_wall(d: Design) -> set[tuple[int, int]]:
    """The wall band (returned as its (x, z) cells), its merlons and its moss."""
    outer = octagon(R)
    band = outer - octagon(R - WALL / math.cos(math.pi / 8))
    d.prism(band, 0, WALK, STONE)
    d.prism(band, 0, 0, TRIM)                                         # a plinth course
    face = outer - solids.erode2d(outer, 1)
    for x, z in face:                                                 # merlons on the outer edge, two on, two off
        if int((math.atan2(x - C, z - C) + math.pi) * R / 2) % 2 == 0:
            d.put(x, WALK + 1, z, STONE)
            d.put(x, WALK + 2, z, STONE)
    d.scatter({(x, WALK, z) for x, z in band}, MOSS, 0.22, seed=3)
    return band


def towers(d: Design, band: set[tuple[int, int]]) -> None:
    for i, (tx, tz) in enumerate(corners()):
        tx, tz = round(tx), round(tz)
        wall = d.cylinder(tx, tz, 0, TOWER_TOP, TOWER_R, STONE, wall=2)
        inside = solids.cylinder(tx, tz, 1, TOWER_TOP - 1, TOWER_R - 2)
        d.fill(inside, CLEAR)                                         # the wall band runs into it: hollow it
        d.disc(tx, tz, 0, TOWER_R - 1, TRIM)
        d.disc(tx, tz, WALK, TOWER_R - 2, STONE)                      # the floor at the wall-walk
        d.disc(tx, tz, TOWER_TOP, TOWER_R, STONE)                     # the roof
        rim = solids.ring2d(tx, tz, TOWER_R, 1)
        for x, z in rim:                                              # battlements round the roof
            if int((math.atan2(x - tx, z - tz) + math.pi) * TOWER_R / 1.5) % 2 == 0:
                d.put(x, TOWER_TOP + 1, z, STONE)
                d.put(x, TOWER_TOP + 2, z, STONE)
        d.scatter({(x, TOWER_TOP, z) for x, z in solids.disc(tx, tz, TOWER_R)}, MOSS, 0.2, seed=40 + i)
        # Openings: onto the wall-walk on both sides, from the courtyard at the foot, arrow slits.
        d.fill([(x, y, z) for x, y, z in wall if WALK + 1 <= y <= WALK + 3 and (x, z) in band], CLEAR)
        radial_cut(d, tx, tz, (C, C), wall, 1, 3, 1.0)
        for k in range(4):
            a = 2 * math.pi * (k + 0.5) / 4 + i
            radial_cut(d, tx, tz, (tx + math.sin(a), tz + math.cos(a)), wall, 12, 15, 0.5)
        d.spiral_stair(tx, tz, 1, TOWER_TOP, 1.5, TOWER_R - 2.5, STONE, steps_per_turn=16, start=i)
        for x, z in solids.disc(tx, tz, 1.5):
            d.put(x, 1, z, TRIM)                                      # a stub of a newel
        d.put(tx, TOWER_TOP + 1, tz, CRYSTAL)                         # a crystal on the roof
        for y in (WALK - 6, WALK + 4):                                # lanterns set into the wall
            d.put(round(tx + 6.6 * math.sin(i + y)), y, round(tz + 6.6 * math.cos(i + y)), LANTERN)
        d.chest(tx, WALK + 1, tz, table="soaniri_castle_tower", chance=0.7)   # the middle (the stair winds round it)


def gatehouse(d: Design, band: set[tuple[int, int]]) -> None:
    """The gatehouse over the front side's middle: a hollow block with two halls beside the gate passage and an upper
    hall at the wall-walk, the passage a round arch seven wide and ten high."""
    x0, x1 = C - 13, C + 13
    front = C - round(R * math.cos(math.pi / 8))                      # the front side's outer face
    z0, z1 = front - 9, front + 8
    top = 29
    d.box(x0, 0, z0, x1, top, z1, STONE)
    d.box(x0 + 2, 1, z0 + 2, x1 - 2, top - 1, z1 - 2, CLEAR)
    d.box(x0 + 2, WALK, z0 + 2, x1 - 2, WALK, z1 - 2, STONE)          # the upper hall's floor
    for x in range(x0, x1 + 1):                                       # battlements
        for z in range(z0, z1 + 1):
            if (x in (x0, x1) or z in (z0, z1)) and (x + z) % 4 < 2:
                d.put(x, top + 1, z, STONE)
                d.put(x, top + 2, z, STONE)
    # The passage with its own walls and vault, doors into the halls beside it.
    for x in (C - 4, C + 4):
        d.box(x, 1, z0, x, 11, z1, STONE)
    d.box(C - 4, 11, z0, C + 4, 12, z1, STONE)
    d.arch("z", C - 3, 1, z0, z1, 7, 10, kind="round")
    for x in (C - 4, C + 4):
        for y in (1, 2, 3):
            for z in (front + 2, front + 3):
                d.clear(x, y, z)
    d.fill([(x, y, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1) for y in range(WALK + 1, WALK + 4)
            if (x, z) in band and (x in (x0, x0 + 1, x1 - 1, x1))], CLEAR)   # onto the wall-walk
    for x in (C - 5, C + 5):                                          # crystals beside the gate
        for y in (5, 10):
            d.put(x, y, z0, CRYSTAL)
    d.put(C, 11, z0, CRYSTAL)                                         # the keystone
    d.put(C - 9, WALK + 3, C - 70, LANTERN)
    d.chest(x1 - 4, 1, z1 - 4, table="soaniri_castle_tower", chance=0.8)


def wall_stairs(d: Design) -> None:
    """Two stairs up the inside of the wall onto the wall-walk, on the left and the right side."""
    inner = round(R * math.cos(math.pi / 8)) - WALL                   # the inner face's distance from the centre
    for x in (C - inner, C + inner - 2):
        d.stairs(x, 0, C - 11, 0, 1, WALK + 1, STONE, width=3, fill_below=True)


# ---------------------------------------------------------------- the keep
def keep(d: Design) -> None:
    h = KEEP // 2
    x0, x1, z0, z1 = C - h, C + h - 1, C - h, C + h - 1
    for x, z, w, l in ((x0, z0, KEEP, 3), (x0, z1 - 2, KEEP, 3), (x0, z0, 3, KEEP), (x1 - 2, z0, 3, KEEP)):
        d.box(x, 0, z, x + w - 1, KEEP_TOP, z + l - 1, STONE)
    for f in FLOORS + [KEEP_TOP]:
        d.box(x0 + 3, f, z0 + 3, x1 - 3, f, z1 - 3, TRIM if f == 0 else STONE)
    for y in (20, 40, 60):                                            # string courses
        d.fill([(x, y, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1) if x in (x0, x1) or z in (z0, z1)], TRIM)
    for x in range(x0, x1 + 1):                                       # battlements round the roof
        for z in range(z0, z1 + 1):
            if (x in (x0, x1) or z in (z0, z1)) and (x + z) % 4 < 2:
                d.put(x, KEEP_TOP + 1, z, STONE)
                d.put(x, KEEP_TOP + 2, z, STONE)
    # Corner turrets with crystal-tipped caps.
    for tx, tz in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        d.cylinder(tx, tz, 0, KEEP_TOP + 8, 4.5, STONE, wall=1)
        d.disc(tx, tz, KEEP_TOP + 8, 4.5, STONE)
        d.cone(tx, tz, KEEP_TOP + 9, KEEP_TOP + 16, 4.5, "carved_obsidian")
        d.put(tx, KEEP_TOP + 17, tz, CRYSTAL)
        d.put(tx, KEEP_TOP + 18, tz, CRYSTAL)
    # The door (front, -z) and the windows: pointed, two wide, crystal sills.
    d.arch("z", C - 2, 1, z0, z0 + 2, 4, 7, kind="pointed")
    for f in FLOORS:
        for u in (-13, -1, 11):
            if f == 0 and u == -1:
                continue
            for axis, a0 in (("z", z0), ("z", z1 - 2), ("x", x0), ("x", x1 - 2)):
                d.arch(axis, C + u, f + 3, a0, a0 + 2, 2, 5, kind="pointed")
                sill = (C + u, f + 2, a0) if axis == "z" else (a0, f + 2, C + u)
                if a0 in (z1 - 2, x1 - 2):
                    sill = (sill[0] + 2, sill[1], sill[2]) if axis == "x" else (sill[0], sill[1], sill[2] + 2)
                d.put(*sill, CRYSTAL)
                d.put(sill[0] + (1 if axis == "z" else 0), sill[1], sill[2] + (1 if axis == "x" else 0), CRYSTAL)
    # The spiral stair in the back-right corner, kept; lanterns; the beacon on the roof.
    treads = d.spiral_stair(x1 - 8, z1 - 8, 1, KEEP_TOP, 1.5, 5.4, STONE, steps_per_turn=16)
    d.keep(treads)
    d.fill(solids.cylinder(x1 - 8, z1 - 8, 1, KEEP_TOP, 1.5), Mat("carved_obsidian", keep=True))
    for f in FLOORS:
        d.put(x0 + 3, f + 5, C, LANTERN)
        d.put(x1 - 3, f + 5, C - 6, LANTERN)
    d.cylinder(C, C, KEEP_TOP + 1, KEEP_TOP + 3, 2.5, "carved_obsidian")
    d.cone(C, C, KEEP_TOP + 4, KEEP_TOP + 12, 2.5, CRYSTAL)
    # Chests: the hall, two upper floors, the roof.
    d.chest(x0 + 5, 1, z1 - 5, table="soaniri_castle")
    d.chest(x0 + 5, 31, z0 + 5, table="soaniri_castle_tower", chance=0.8)
    d.chest(x1 - 5, 61, z0 + 5, table="soaniri_castle", chance=0.8)
    d.chest(x0 + 6, KEEP_TOP + 1, z0 + 6, table="soaniri_castle", chance=0.6)


# ---------------------------------------------------------------- the courtyard
def courtyard(d: Design, band: set[tuple[int, int]]) -> None:
    """Chime moss paving inside the wall, an obsidian path from the gate to the keep, crystal clusters, the dens, the
    sound and a hidden chest."""
    yard = octagon(R - WALL / math.cos(math.pi / 8) + 0.5) - band
    d.prism(yard, 0, 0, MOSS, where="empty")
    inner = round(R * math.cos(math.pi / 8)) - WALL
    for x in range(C - 3, C + 4):
        for z in range(C - inner, C - KEEP // 2):
            d.put(x, 0, z, "carved_obsidian")
    for i, (a, rr) in enumerate(((0.6, 45), (1.9, 52), (2.7, 38), (3.9, 50), (4.6, 44), (5.6, 55))):
        x, z = round(C + rr * math.sin(a)), round(C + rr * math.cos(a))
        for (dx, dz), hgt in (((0, 0), 5), ((1, 0), 3), ((0, 1), 4), ((-1, 0), 2), ((0, -1), 3), ((1, 1), 1)):
            for y in range(1, 1 + hgt - (i % 2)):
                d.put(x + dx, y, z + dz, CRYSTAL)
    for x, z in ((C - 42, C + 22), (C + 40, C - 26), (C + 6, C + 46)):
        d.den(x, 1, z, species="soundi", count=4, radius=30)
    d.sound(C - 4, 1, C - 34, "amb_soaniri_castle", radius=100, volume=0.6)
    d.chest(C + 9, 1, C + 31, table="soaniri_castle", hidden=True)


def build() -> Design:
    d = Design("soaniri_castle", "Castle of Soaniri", nameKey="landmark.soaniri_castle.name", minTerrainGeneration=39,
               ruin="always", ruinProfile="masonry", ruinStrength=0.5, requires=["atmosphere"], planetTypes=["sounder_world"],
               weight=1)
    band = curtain_wall(d)
    towers(d, band)
    gatehouse(d, band)
    wall_stairs(d)
    keep(d)
    courtyard(d, band)
    # Two ruin zones, so each part decays against its own height (one pass over the whole would grade the low walls by
    # the keep's 98 rows and barely touch them): the keep first, then the walls, towers and gatehouse.
    x0, _, z0, x1, _, z1 = d.bounds()
    h = KEEP // 2 + 5
    d.ruin_zone(C - h, 0, C - h, C + h - 1, KEEP_TOP + 18, C + h - 1, "masonry", KEEP_RUIN)
    d.ruin_zone(x0, 0, z0, x1, TOWER_TOP + 3, z1, "masonry", WALL_RUIN)
    d.normalize()
    return d


if __name__ == "__main__":
    build().write()
