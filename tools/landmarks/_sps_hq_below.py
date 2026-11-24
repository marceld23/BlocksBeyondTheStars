"""The underground parts of the SPS headquarters (#3986): the server halls and the core chamber. See `_sps_hq.py` for
the complex frame and `earth_sps_hq.py` for the script that writes all five templates.

- `earth_sps_hq_servers` (150 x 63 x 150, ground row 59 = the jungle floor): three levels, each one open hall 146 x 146 and
  18 rows high, full of rows of huge ancient server racks (6 x 16 x 6) — dead, a few still blinking, some torn open, some
  toppled — with cable trays under the ceilings, pillars, fallen panels and a ring of consoles in the middle. Level 1 joins
  the main building's basement tunnel in the south wall; a wide kept stair leads down level by level along the west wall;
  level 3 opens in the east wall into the core chamber's tunnel. On the surface: four overgrown vent stacks and a sinkhole
  where the ceiling of level 1 broke in, a mound of rubble under it. A ruin of profile `underground` (a few cave-ins in the
  big halls; the stairs and both joints lie in zones that never cave in).
- `earth_sps_hq_core` (41 x 63 x 37, ground row 59): the round chamber where the first CORE lived, 29 across under a dome,
  its dead core pillar in the middle, consoles round it with dark screens — one faint blue lamp still on. Intact and
  protected: nobody can dig it out or break the pillar.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, fence_rail
from _sps_hq import (
    BAND,
    CAVITY,
    FLOOR,
    FRAME,
    GRASS,
    JOINT_MAIN_SERVERS,
    JOINT_SERVERS_CORE,
    LAMP_BLUE,
    LEAVES,
    MOSS,
    PLACES,
    RACK,
    SCREEN,
    SLAB,
    STAIR,
    STEEL,
    WALL,
    WALL_DARK,
    console,
    fallen_rack,
    h01,
    leaf_patch,
    local,
    put_free,
    rack,
    scatter_rubble,
    tray,
    tree,
)
from geom import Design, Mat, Mix

SERVERS = "earth_sps_hq_servers"
CORE = "earth_sps_hq_core"
G = 59                              # the ground row of both: the jungle floor
WALKS = (39, 20, 1)                 # the walk rows of levels 1, 2, 3 (each level 18 rows high)
LEVEL_H = 18
RACK_ROWS = (10, 30, 50, 70, 90, 110, 130)   # the low z of every rack row (a rack is 6 long)
RACK_GROUPS = tuple(range(12, 123, 22))      # the low x of every group of three racks side by side (18 wide, 4-wide gaps)
PILLARS_X = tuple(range(31, 120, 22))        # the pillars stand in the gaps between the groups …
PILLARS_Z = (22, 62, 102, 142)               # … and in the aisles
PLAZA = (60, 92, 68, 98)            # x0, x1, z0, z1 of the consoles' plaza in the middle of every level (no racks)
SINKHOLE = (115, 122, 5)            # x, z, radius of the hole in level 1's ceiling
STAIR_X = (6, 10)                   # the stairs' cells across
DOWN_1 = 30                         # level 2 -> level 1: the first step (z) on level 2
DOWN_2 = 60                         # level 3 -> level 2: the first step (z) on level 3
DIRT = Mix(Mat("dirt"), (Mat("stone"), 0.3), (MOSS, 0.1), seed=17)


def _box(d: Design, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m, where: str | None = None) -> set:
    return d.fill(solids.box(x0, y0, z0, x1, y1, z1), m, where)


# ------------------------------------------------------------------------------------------------------------ the shell
def _shell(d: Design) -> None:
    """Three halls with their slabs, two-thick outer walls and pillars every 24 cells."""
    for w in WALKS:
        _box(d, 2, w, 2, 147, w + LEVEL_H - 1, 147, CAVITY)
    for y in (0, 19, 38, 57):
        _box(d, 2, y, 2, 147, y, 147, SLAB if y < 57 else WALL_DARK)
    wall = solids.box_walls(0, 0, 0, 149, 57, 149, wall=2)
    d.fill(wall, lambda x, y, z: BAND if y in (17, 36, 55) else WALL_DARK)
    for w in WALKS:
        for i, px in enumerate(PILLARS_X):
            for j, pz in enumerate(PILLARS_Z):
                _box(d, px, w, pz, px + 1, w + LEVEL_H - 1, pz + 1, WALL)
                if (i + j) % 2 == 0:  # every other pillar keeps a faint blue lamp (kept: the ruin leaves it on)
                    d.put(px, w + 9, pz - 1, LAMP_BLUE.with_(keep=True))


def _joints(d: Design) -> None:
    """The way in from the main building's basement (level 1, south wall) and on into the core chamber (level 3, east)."""
    jx0, jx1 = JOINT_MAIN_SERVERS["x"]
    _, y, _ = local(SERVERS, jx0, JOINT_MAIN_SERVERS["y"], JOINT_MAIN_SERVERS["z"][1])
    _box(d, jx0, y, 0, jx1, y + JOINT_MAIN_SERVERS["h"] - 1, 1, CAVITY)
    _, y, z0 = local(SERVERS, JOINT_SERVERS_CORE["x"][0], JOINT_SERVERS_CORE["y"], JOINT_SERVERS_CORE["z"][0])
    z1 = z0 + JOINT_SERVERS_CORE["z"][1] - JOINT_SERVERS_CORE["z"][0]
    _box(d, 148, y, z0, 149, y + JOINT_SERVERS_CORE["h"] - 1, z1, CAVITY)
    for lamp_z in (z0 - 1, z1 + 1):
        d.put(147, y + 3, lamp_z, LAMP_BLUE.with_(keep=True))


def _stairs(d: Design) -> None:
    """Two wide kept stairs along the west wall: level 3 up to level 2, level 2 up to level 1 — each through a hole in the
    slab above with a rail round it."""
    for walk, first in ((WALKS[1], DOWN_1), (WALKS[2], DOWN_2)):
        steps = LEVEL_H + 1
        for k in range(steps):
            for x in range(STAIR_X[0], STAIR_X[1] + 1):
                d.put(x, walk + k, first + k, STAIR, shape=sc.stairs(0, 1))
        slab = walk + steps - 1                      # the slab row the stair climbs through
        _box(d, STAIR_X[0], slab, first + steps - 7, STAIR_X[1], slab, first + steps - 2, CAVITY)
        top = slab + 1
        rail = [(STAIR_X[0] - 1, top, z) for z in range(first + steps - 8, first + steps - 1)]
        rail += [(STAIR_X[1] + 1, top, z) for z in range(first + steps - 8, first + steps - 1)]
        rail += [(x, top, first + steps - 8) for x in range(STAIR_X[0], STAIR_X[1] + 1)]
        fence_rail(d, rail, Mat("iron_block", tint=0x7C848D, keep=True))


# ------------------------------------------------------------------------------------------------------------ the racks
def _rack_slot_free(level: int, x: int, z: int) -> bool:
    px0, px1, pz0, pz1 = PLAZA
    if px0 - 6 < x < px1 and pz0 - 6 < z < pz1:
        return False
    sx, sz, r = SINKHOLE
    return not (level == 0 and math.hypot(x + 3 - sx, z + 3 - sz) < r + 14)


def _racks(d: Design, level: int) -> None:
    """The rows of racks of one level: most stand dead, a few still blink (more of them the deeper and the further east —
    toward the core), some are torn open, some toppled along their row, some slots are empty."""
    walk = WALKS[level]
    for z0 in RACK_ROWS:
        for g0 in RACK_GROUPS:
            if not _rack_slot_free(level, g0, z0) or not _rack_slot_free(level, g0 + 12, z0):
                continue
            for k in range(3):
                x0 = g0 + 6 * k
                seed = level * 1000 + x0 * 7 + z0
                v = h01(x0, level, z0, 401)
                lit = 0.015 + 0.02 * level + (0.05 if level == 2 and x0 > 100 else 0.0)
                if v < 0.07:
                    continue                                               # an empty slot
                if v < 0.12 and z0 + 19 < 147:
                    fallen_rack(d, max(14, x0 - 3), walk, z0 + 6, along="x", length=14, seed=seed)  # toppled into the aisle
                    continue
                broken = 0.35 if v > 0.82 else 0.0
                # only a torn rack shows its hollow inside; a whole one is solid rack through and through (long runs, a small file)
                rack(d, x0, walk, z0, seed=seed, lit=lit, broken=broken, hollow=CAVITY if broken else RACK)


def _trays(d: Design, level: int) -> None:
    """A cable tray along every aisle under the ceiling; here and there a run broke and hangs down to the floor."""
    walk = WALKS[level]
    y = walk + LEVEL_H - 1
    for z0 in RACK_ROWS:
        z = z0 + 9
        if z > 145:
            continue
        x = 12
        while x < 138:
            run = 10 + int(h01(x, level, z, 77) * 14)
            end = min(137, x + run)
            if h01(x, level, z, 78) < 0.15:
                d.fill(solids.line((x, y, z), (end, walk, z)), Mat("cable_tray"))  # a broken run sagging to the floor
            else:
                tray(d, (x, y, z), (end, y, z))
            x = end + 2 + int(h01(x, level, z, 79) * 3)


def _floor_dress(d: Design, level: int) -> None:
    """Fallen ceiling panels on the aisle floors, a few pieces of rubble."""
    walk = WALKS[level]
    cells = [(x, walk, z) for x in range(3, 147) for z in range(3, 147)]
    for x, y, z in cells:
        v = h01(x, level, z, 91)
        if v < 0.006:
            put_free(d, [(x, y, z)], FRAME, shape=sc.pack(sc.SLAB))
    scatter_rubble(d, [c for c in cells if h01(*c, 92) < 0.02], seed=93 + level, density=0.6)


def _plaza(d: Design, level: int) -> None:
    """The consoles' plaza in the middle of a level: a ring of consoles round a dark display column; a chest."""
    walk = WALKS[level]
    px0, px1, pz0, pz1 = PLAZA
    cx, cz = (px0 + px1) / 2, (pz0 + pz1) / 2
    d.fill(solids.cylinder(cx, cz, walk, walk + 10, 2.0), FRAME)
    put_free(d, solids.cylinder(cx, cz, walk + 4, walk + 7, 2.6), SCREEN)
    d.fill(solids.cylinder(cx, cz, walk + 11, walk + LEVEL_H - 1, 1.0), Mat("cable_tray"))
    for k in range(8):
        a = k * math.pi / 4
        x, z = round(cx + 9 * math.cos(a)), round(cz + 9 * math.sin(a))
        facing = ("-x" if math.cos(a) > 0.5 else "+x" if math.cos(a) < -0.5 else "-z" if math.sin(a) > 0 else "+z")
        console(d, x, walk, z, facing)
    d.chest(round(cx) + 5, walk, round(cz) - 12, table="ruin_rich" if level == 2 else "ruin_common")


def _sinkhole(d: Design) -> None:
    """Where level 1's ceiling broke in: a hole in the jungle floor, rubble heaped up under it (no long fall), a young tree
    in the light and a rim of grass."""
    sx, sz, r = SINKHOLE
    walk = WALKS[0]
    for x, z in solids.disc(sx, sz, r):
        for y in range(walk + LEVEL_H - 1, G + 1):
            d.put(x, y, z, CAVITY)
    mound = solids.cone(sx, sz, walk, walk + 14, 15, r1=3.0)
    d.fill(mound, DIRT)
    for x, y, z in solids.highest_cells(mound):
        d.put(x, y, z, GRASS if h01(x, y, z, 5) < 0.7 else MOSS)
    leaf_patch(d, sx + 1, walk + 15, sz, 2.5, seed=6)
    tree(d, sx - 2, walk + 15, sz + 1, height=6, crown=3.5, seed=8)
    for x, z in solids.disc(sx, sz, r + 2) - solids.disc(sx, sz, r):
        d.put(x, G, z, GRASS)


def _vents(d: Design) -> None:
    """Four overgrown vent stacks on the jungle floor, grates on top."""
    for vx, vz in ((20, 20), (128, 30), (40, 128), (96, 136)):
        d.fill(solids.box_walls(vx, G, vz, vx + 2, G + 2, vz + 2), FRAME)
        fence_rail(d, [(vx + i, G + 3, vz + j) for i in range(3) for j in range(3)], FRAME)
        leaf_patch(d, vx + 1, G, vz + 1, 3.0, seed=vx + vz, density=0.5)


def servers() -> Design:
    _, _, w, ln, g = PLACES[SERVERS]
    d = earth_design(SERVERS, "SPS Server Halls", ruin="always", ruinProfile="underground", ruinStrength=0.5, groundRow=g)
    _shell(d)
    _joints(d)
    _stairs(d)
    for level in range(3):
        _racks(d, level)
        _trays(d, level)
        _plaza(d, level)
        _floor_dress(d, level)
    _sinkhole(d)
    _vents(d)
    d.sound(75, WALKS[1] + 2, 60, "amb_station_lab", radius=110, volume=0.35)
    d.sound(120, WALKS[2] + 2, 75, "amb_giant_crystal", radius=60, volume=0.3)
    d.chest(140, WALKS[2], 140, table="ruin_rich", items="data_fragment:1-2")
    d.chest(4, WALKS[0], 140, table="ruin_common")
    # Never a cave-in where the way goes: the stairs, the south joint, the way to the core (masonry decays nothing
    # under the ground row, so these zones only switch lights off).
    d.ruin_zone(2, 0, 24, 14, 57, 84, profile="masonry", strength=0.1)
    d.ruin_zone(58, WALKS[0], 0, 78, WALKS[0] + LEVEL_H - 1, 14, profile="masonry", strength=0.1)
    d.ruin_zone(132, 0, 64, 149, LEVEL_H, 86, profile="masonry", strength=0.1)
    _expect_size(d, w, ln)
    return d


# ------------------------------------------------------------------------------------------------------------- the core
CORE_C = (24.0, 20.0)               # the chamber's centre (x, z) in the core template
CORE_R = 14                          # its inner radius
DOME = 13                            # the row the dome springs from


def core() -> Design:
    """The core chamber: intact, protected (the Earth header's `ruin: never`)."""
    _, _, w, ln, g = PLACES[CORE]
    d = earth_design(CORE, "Core Chamber", groundRow=g)
    cx, cz = CORE_C
    walk = 1
    d.fill(solids.cylinder(cx, cz, walk, DOME - 1, CORE_R + 0.5), CAVITY)
    d.fill(solids.ellipsoid(cx, DOME, cz, CORE_R + 0.5, 9, CORE_R + 0.5) - solids.box(0, 0, 0, 60, DOME - 1, 60), CAVITY)
    d.disc(cx, cz, 0, CORE_R + 2.5, lambda x, y, z: BAND if 5.5 < math.hypot(x - cx, z - cz) < 6.5 else FRAME)
    d.ring(cx, cz, walk, DOME - 1, CORE_R + 2.5, 2, lambda x, y, z: BAND if y in (3, 11) else STEEL)
    d.dome(cx, cz, DOME, CORE_R + 2.5, FRAME, ry=10.5, wall=2)
    # the tunnel from the server halls
    _, y, z0 = local(CORE, JOINT_SERVERS_CORE["x"][1], JOINT_SERVERS_CORE["y"], JOINT_SERVERS_CORE["z"][0])
    z1 = z0 + JOINT_SERVERS_CORE["z"][1] - JOINT_SERVERS_CORE["z"][0]
    _box(d, 0, y - 1, z0 - 1, 10, y + JOINT_SERVERS_CORE["h"], z1 + 1, STEEL)
    _box(d, 0, y - 1, z0, 10, y - 1, z1, FLOOR)
    _box(d, 0, y, z0, 10, y + JOINT_SERVERS_CORE["h"] - 1, z1, CAVITY)
    # the dead core: a pillar from the floor to the top of the dome, collars at its foot and twice above
    d.fill(solids.cylinder(cx, cz, walk, DOME + 9, 2.5), Mat("core_pillar"))
    for y0, y1 in ((walk, walk + 1), (8, 8), (DOME + 2, DOME + 2)):
        d.fill(solids.cylinder(cx, cz, y0, y1, 3.6) - solids.cylinder(cx, cz, y0, y1, 2.5), BAND if y0 > walk else FRAME)
    # consoles round it, every screen dark — one faint lamp still on
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        x, z = round(cx + 8.5 * math.cos(a)), round(cz + 8.5 * math.sin(a))
        facing = "-x" if math.cos(a) > 0.5 else "+x" if math.cos(a) < -0.5 else "-z" if math.sin(a) > 0 else "+z"
        console(d, x, walk, z, facing, lamp=(k == 5))
    # screens on the wall, cable trays from the pillar to the wall under the dome
    for k in range(16):
        a = k * math.pi / 8
        for t in (-1, 0, 1):
            ang = a + t * 0.07
            x, z = round(cx + (CORE_R + 0.6) * math.cos(ang)), round(cz + (CORE_R + 0.6) * math.sin(ang))
            if x <= 10 and abs(z - cz) <= 3:
                continue                                                   # the doorway
            for y in range(4, 9):
                d.put(x, y, z, SCREEN)
    for k in range(6):
        a = k * math.pi / 3 + 0.3
        d.fill(solids.line((cx + 3 * math.cos(a), DOME - 1, cz + 3 * math.sin(a)),
                           (cx + CORE_R * math.cos(a), DOME - 1, cz + CORE_R * math.sin(a))), Mat("cable_tray"))
    d.sound(round(cx), walk + 2, round(cz) + 6, "amb_giant_crystal", radius=36, volume=0.3)
    # on the surface: two small vent grates in the grass
    for vx, vz in ((14, 8), (32, 30)):
        d.fill(solids.box_walls(vx, G, vz, vx + 2, G + 2, vz + 2), FRAME)
        fence_rail(d, [(vx + i, G + 3, vz + j) for i in range(3) for j in range(3)], FRAME)
        leaf_patch(d, vx + 1, G, vz + 1, 2.5, seed=vx, density=0.5)
    d.put(40, G, 36, LEAVES)                                              # the box's far corner (the footprint's size)
    _expect_size(d, w, ln)
    return d


def _expect_size(d: Design, w: int, ln: int) -> None:
    b = d.bounds()
    assert b is not None and b[3] + 1 == w and b[5] + 1 == ln and b[0] >= 0 and b[2] >= 0, \
        f"{d.header['key']}: bounds {b}, expected {w} x {ln} (PLACES in _sps_hq.py)"
