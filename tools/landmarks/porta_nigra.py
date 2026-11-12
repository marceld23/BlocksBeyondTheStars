"""The black gate: a Roman city gate of sandstone weathered almost black, after Trier's Porta Nigra at three times its
size (36 x 21.5 m, the west tower 29 m high).

Two towers — rounded toward the field side (-Z), four storeys in the west, three in the east — flank a gate block whose
two big arched passages lead into an open gate court and on through the city-side block. Above the passages two rows of
arched windows light the galleries that join the towers; every face shows the Porta Nigra's grid: arched windows between
engaged half-columns under a projecting entablature on every storey (blind arches on the ground storey). Inside each
tower a switchback stair climbs hall by hall to the roof; doors lead from the passages into the towers, from the towers
into both galleries and from the gate block's roof into the west tower's top hall.
"""
from __future__ import annotations

import itertools
import math

import solids
from _classical import (
    Face,
    band,
    half_column,
    lights,
    ring_band,
    ring_half_columns,
    switchback,
    window,
)
from geom import CLEAR, Design, Mat, Mix

# Rows: the pavement (the ground row), then the floors F1..F4; the ground storey is the tall one.
F = (0, 27, 47, 67, 87)
TOP_WEST = F[4]          # the west tower has four storeys
TOP_EAST = F[3]          # the east tower and the gate block three
# Plan (x across, z from the field side -Z to the city side +Z).
WEST = (0, 31)
EAST = (76, 107)
APSE_Z = 17              # the towers' rounded fronts: half discs on z < APSE_Z
APSE_R = 16.0
FIELD = (15, 20)         # the field-side gate block (its outer face on z = 15)
CITY = (56, 63)          # the city-side gate block (outer face on z = 63)
PASSAGES = ((36, 48), (59, 71))
ARCH_H = 21

WALL = Mix(Mat("blackened_sandstone"), (Mat("blackened_sandstone", tint=0xC8C0B8), 0.10),
           (Mat("sandstone", tint=0x5A524A), 0.03), (Mat("stone"), 0.03), seed=21)
TRIM = Mat("stone", tint=0x9C9080)       # half-columns and entablatures: an even dark warm grey, a shade lighter
FLOOR = Mat("sandstone", tint=0x8C8170)                # the floors inside, a lighter stone to see by
PAVE = Mix(Mat("sandstone", tint=0x6F675C), (Mat("blackened_sandstone"), 0.3), seed=22)
STAIR = Mat("blackened_sandstone")
GLASS = Mat("glass", tint=0x8A9A96)


def apse_centre(tower: tuple[int, int]) -> float:
    return (tower[0] + tower[1]) / 2


def tower_plan(tower: tuple[int, int]) -> set[tuple[int, int]]:
    """The tower's footprint: a rectangle on the city side and a half disc toward the field."""
    x0, x1 = tower
    rect = {(x, z) for x in range(x0, x1 + 1) for z in range(APSE_Z, CITY[1] + 1)}
    cx = apse_centre(tower)
    half = {(x, z) for x, z in solids.disc(cx, APSE_Z, APSE_R) if z < APSE_Z}
    return rect | half


def tower_shell(d: Design, tower: tuple[int, int], top: int) -> None:
    """The tower's walls (two thick), its floors and its roof with a parapet."""
    plan = tower_plan(tower)
    inner = solids.erode2d(plan, 2)
    walls = plan - inner
    d.fill(solids.extrude(walls, 1, top), WALL)
    d.fill(solids.extrude(plan, 0, 0), PAVE)
    for f in F[1:]:
        if f <= top:
            d.fill(solids.extrude(plan, f, f), FLOOR if f < top else WALL)
    d.fill(solids.extrude(plan - solids.erode2d(plan, 1), top + 1, top + 1), WALL)    # the parapet


def tower_faces(tower: tuple[int, int], west: bool) -> dict[str, Face]:
    x0, x1 = tower
    return {
        "side": Face("x", x0 if west else x1, -1 if west else 1, APSE_Z, CITY[1]),
        "city": Face("z", CITY[1], 1, x0, x1),
        "court": Face("x", x1 if west else x0, 1 if west else -1, FIELD[1] + 1, CITY[0] - 1),
    }


def storey_grid(d: Design, face: Face, bays: int, storeys: range, blind_ground: bool = True,
                skip: tuple[tuple[int, int], ...] = (), glazed: tuple[int, ...] = ()) -> None:
    """The Porta Nigra grid on a straight face: per storey a row of arched windows (blind arches on the ground storey),
    a half-column on every pier and a projecting entablature on the floor line above. `skip` = (storey, bay) left
    plain; `glazed` = bays whose windows are glass (beside a stair, where an open one would let a player walk out)."""
    centres, piers = face.bays(bays)
    for s in storeys:
        f0, f1 = F[s], F[s + 1]
        for i, c in enumerate(centres):
            if (s, i) in skip:
                continue
            if s == 0:
                if blind_ground:
                    window(d, face, c, f0 + 6, 5, 15, depth=1)
            else:
                window(d, face, c, f0 + 3, 5, 13)
                if i in glazed:
                    window(d, face, c, f0 + 3, 5, 13, m=GLASS, depth=1)
        for u in piers:
            half_column(d, face, u, f0 + 1, f1 - 1, TRIM, capital=TRIM)
        band(d, face, f1, TRIM)


def apse_grid(d: Design, tower: tuple[int, int], top_storey: int) -> None:
    """The rounded front: six arched windows per storey round the half disc, half-columns between them, ring bands."""
    cx = apse_centre(tower)

    def front(x: int, z: int) -> bool:
        return z < APSE_Z

    wall_ring = {(x, z) for x, z in solids.disc(cx, APSE_Z, APSE_R) - solids.disc(cx, APSE_Z, APSE_R - 2) if z < APSE_Z}
    ell = solids.Ellipse(cx, APSE_Z, APSE_R - 1, APSE_R - 1)
    outside = solids.Ellipse(cx, APSE_Z, APSE_R + 0.6, APSE_R + 0.6)
    for s in range(top_storey + 1):
        f0, f1 = F[s], F[s + 1]
        if s == 0:
            skin = {(x, y, z) for x, z in wall_ring for y in range(f0 + 6, f0 + 21)
                    if math.hypot(x - cx, z - APSE_Z) > APSE_R - 1}
            d.fill(solids.ring_openings(skin, ell, f0 + 6, 12, 5, 15), CLEAR)
        else:
            cells = {(x, y, z) for x, z in wall_ring for y in range(f0 + 3, f0 + 16)}
            d.fill(solids.ring_openings(cells, ell, f0 + 3, 12, 5, 13), CLEAR)
        ring_half_columns(d, outside, 12, f0 + 1, f1 - 1, TRIM, capital=TRIM, phase=0.0, keep_if=front)
        ring_band(d, cx, APSE_Z, APSE_R, f1, TRIM, keep_if=front)


def gate_block(d: Design, z0: int, z1: int, corridor: tuple[int, int], outer_out: int) -> None:
    """One of the two blocks between the towers: solid over the passages on the ground storey, a gallery corridor on
    the two storeys above, the roof with its parapet; the two passages cut through it."""
    xa, xb = WEST[1] + 1, EAST[0] - 1
    d.box(xa, 0, z0, xb, 0, z1, PAVE)
    d.fill(solids.box(xa, 1, z0, xb, F[3], z1), WALL)
    for s in (1, 2):
        d.fill(solids.box(xa, F[s], z0, xb, F[s], z1), FLOOR)
        d.fill(solids.box(xa, F[s] + 1, corridor[0], xb, F[s + 1] - 1, corridor[1]), CLEAR)
    d.fill(solids.box(xa, F[3] + 1, z0, xb, F[3] + 1, z0) | solids.box(xa, F[3] + 1, z1, xb, F[3] + 1, z1), WALL)
    for p0, p1 in PASSAGES:
        d.arch("z", p0, 1, z0, z1, p1 - p0 + 1, ARCH_H, "round")
    outer = Face("z", z0 if outer_out < 0 else z1, outer_out, xa, xb, depth=corridor[0] - z0 if outer_out < 0 else z1 - corridor[1])
    inner = Face("z", z1 if outer_out < 0 else z0, -outer_out, xa, xb,
                 depth=z1 - corridor[1] if outer_out < 0 else corridor[0] - z0)
    storey_grid(d, outer, 5, range(1, 3))
    centres, _ = inner.bays(5)
    for s in (1, 2):
        for c in centres:
            window(d, inner, c, F[s] + 3, 5, 11)
    # The ground storey of the outer face: half-columns flanking the passages and the entablature over them.
    for u in (xa + 1, PASSAGES[0][1] + 2, PASSAGES[1][0] - 2, xb - 1):
        half_column(d, outer, u, 1, F[1] - 1, TRIM, capital=TRIM)
    band(d, outer, F[1], TRIM, extend=0)
    band(d, inner, F[1], TRIM, extend=0)


def tower_doors(d: Design, tower: tuple[int, int], west: bool, top: int) -> None:
    """Doorways three wide and four high: from the passage into the tower, from the city side, from the tower into
    both galleries on storeys 1 and 2, and (west) from the gate block's roof into the top hall."""
    x0, x1 = tower
    inner_x = (x1 - 1, x1) if west else (x0, x0 + 1)
    pier = (WEST[1] + 1, PASSAGES[0][0] - 1) if west else (PASSAGES[1][1] + 1, EAST[0] - 1)
    xa, xb = min(inner_x[0], pier[0]), max(inner_x[1], pier[1])
    d.fill(solids.box(xa, 1, 17, xb, 4, 19), CLEAR)                                  # passage -> tower
    mid = (x0 + x1) // 2
    d.fill(solids.box(mid - 1, 1, CITY[1] - 1, mid + 1, 4, CITY[1]), CLEAR)        # city side -> tower
    for s in (1, 2):
        d.fill(solids.box(inner_x[0], F[s] + 1, 17, inner_x[1], F[s] + 4, 19), CLEAR)
        d.fill(solids.box(inner_x[0], F[s] + 1, 58, inner_x[1], F[s] + 4, 60), CLEAR)
    if top > F[3]:
        d.fill(solids.box(inner_x[0], F[3] + 1, 17, inner_x[1], F[3] + 4, 19), CLEAR)
        d.fill(solids.box(inner_x[0], F[3] + 1, 58, inner_x[1], F[3] + 4, 60), CLEAR)


def tower_stairs(d: Design, tower: tuple[int, int], west: bool, top: int) -> None:
    """A switchback per storey along the tower's outer side wall, the same place on every storey, up to the roof."""
    x0, x1 = tower
    lane = x0 + 2 if west else x1 - 7
    z0 = 34
    floors = [f for f in F if f <= top]
    for f, g in itertools.pairwise(floors):
        switchback(d, lane, z0, f, g - f, STAIR, along="z", sign=1, width=3)


def tower_lights_and_loot(d: Design, tower: tuple[int, int], west: bool, top: int) -> None:
    x0, x1 = tower
    cx = (x0 + x1) // 2
    for f in [f for f in F if f < top]:
        lights(d, [(cx - 6, f + 5, CITY[1] - 2), (cx + 6, f + 5, CITY[1] - 2), (cx, f + 5, 4),
                   ((x1 - 2) if west else (x0 + 2), f + 5, 30)])


def court(d: Design) -> None:
    """The gate court between the two blocks: paved, with a few steps of rubble-free pavement and lights."""
    xa, xb = WEST[1] + 1, EAST[0] - 1
    d.box(xa, 0, FIELD[1] + 1, xb, 0, CITY[0] - 1, PAVE)


def build() -> Design:
    d = Design("porta_nigra", "Porta Nigra", nameKey="landmark.porta_nigra.name", ruin="always", ruinProfile="masonry",
               ruinStrength=0.75, requires=["atmosphere"], weight=1,  # ~20-30 % falls
               minTerrainGeneration=39)
    gate_block(d, FIELD[0], FIELD[1], (17, 19), -1)
    gate_block(d, CITY[0], CITY[1], (58, 61), 1)
    court(d)
    for tower, west, top in ((WEST, True, TOP_WEST), (EAST, False, TOP_EAST)):
        tower_shell(d, tower, top)
        faces = tower_faces(tower, west)
        storeys = range(len([f for f in F if f < top]))
        storey_grid(d, faces["side"], 5, storeys, glazed=(2, 3))
        storey_grid(d, faces["city"], 3, storeys, skip=((0, 1),))
        storey_grid(d, faces["court"], 3, storeys)
        apse_grid(d, tower, len(storeys) - 1)
        tower_doors(d, tower, west, top)
    for tower, west, top in ((WEST, True, TOP_WEST), (EAST, False, TOP_EAST)):
        tower_stairs(d, tower, west, top)
        tower_lights_and_loot(d, tower, west, top)
    # Lights in the galleries and the passages.
    for s in (1, 2):
        lights(d, [(x, F[s] + 5, 17) for x in range(36, 75, 9)] + [(x, F[s] + 5, 61) for x in range(36, 75, 9)])
    for p0, p1 in PASSAGES:
        lights(d, [(p0, 5, FIELD[0] + 2), (p1, 5, FIELD[0] + 2), (p0, 5, CITY[1] - 2), (p1, 5, CITY[1] - 2)])
    # Chests: the west tower's top hall, the east tower's second storey, the field gallery over the passages, the
    # court, the west tower's ground floor.
    d.chest(10, F[3] + 1, 50, table="ruin_rich")
    d.chest(96, F[2] + 1, 52, table="ruin_common")
    d.chest(54, F[2] + 1, 18, table="ruin_common", hidden=True)
    d.chest(WEST[1] + 3, 1, CITY[0] - 3, table="ruin_common", chance=0.5)
    d.chest(20, 1, 24, table="ruin_common", chance=0.6)
    d.normalize()
    return d


if __name__ == "__main__":
    build().write()
