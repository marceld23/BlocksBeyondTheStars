"""The golden bird gate: a classical city gate after Berlin's Brandenburg Gate at three times its size (65.5 x 11 m,
columns 13.5 m high), with a stylised golden bird spreading its wings on top where the original has its four-horse
chariot.

Twelve fluted columns — six at the front, six at the back — joined by walls into five passages (the middle one wider)
carry an entablature with a triglyph frieze and a cornice; on it stands the attic with a recessed panel, and on the
attic's raised middle the bird, about 30 blocks high, all gold. Two lower pavilions with four-column porticos and
pediments flank the gate. A stair inside the west end pier climbs to the roof; from the roof terrace steps lead onto
the attic and up to the bird. The ruin pass keeps the bird and everything that carries it (the middle passage's columns,
walls and entablature, the attic's middle): the rest of the gate crumbles around it.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _classical import flight, lights, switchback
from geom import CLEAR, Design, Mat, Mix, Sculpture

# Plan: x across (the west pavilion, the gate, the east pavilion), z from the front (-Z) to the back.
GATE = (37, 157)                # the gate body
PIERS = ((37, 52), (142, 157))  # the solid end piers (the outer columns stand at their inner corners)
COLUMNS = (51, 68, 85, 109, 126, 143)   # the column axes; the passages lie between them
FRONT_Z, BACK_Z = 2, 30         # the axes of the front and back column rows
DEPTH = (0, 32)
MID = 97                        # the gate's axis
WINGS = ((0, 36), (158, 194))
# Rows.
FLOOR = 2                       # the stylobate's top; the passages are walked on row 3
SHAFT = (4, 40)
CAPITAL = (41, 42)
ARCHITRAVE = (43, 47)
FRIEZE = (48, 53)
CORNICE = (54, 56)
ROOF = 56
ATTIC = (57, 66)
PODIUM = (67, 72)
BIRD_BASE = 73
ATTIC_INSET = 10                # the roof terrace at the ends of the attic (the pier stair comes up there)

STONE = Mix(Mat("sandstone", tint=0xB4A68C), (Mat("sandstone", tint=0xA49882), 0.10), (Mat("travertine"), 0.06), seed=41)
DRESSED = Mat("travertine")                       # the columns, capitals, cornices: the light dressed stone
FLUTE = Mat("travertine", tint=0xB8B2A2)         # every other cell of a column's skin: the fluting
TRIGLYPH = Mat("sandstone", tint=0x8C8270)
PANEL = Mat("sandstone", tint=0x9A8E78)
GOLD = Mat("gold_block", keep=True)
GOLD_DEEP = Mat("gold_block", tint=0xB08428, keep=True)
EYE = Mat("obsidian", keep=True)
STAIR = Mat("sandstone", tint=0xB4A68C)


# --- the gate body ---------------------------------------------------------------------------------------------------
def stylobate(d: Design) -> None:
    """Three steps of dressed stone under the whole gate and the pavilions."""
    for y, inset in ((0, 0), (1, 1), (2, 2)):
        d.box(WINGS[0][0] + inset, y, DEPTH[0] - 3 + inset, WINGS[1][1] - inset, y, DEPTH[1] + 3 - inset, DRESSED)
    for x in range(MID - 9, MID + 10):                 # steps up to the middle passage, front and back
        for y, z in ((1, DEPTH[0] - 3), (2, DEPTH[0] - 2)):
            d.put(x, y, z, DRESSED, shape=sc.stairs(0, 1))
        for y, z in ((1, DEPTH[1] + 3), (2, DEPTH[1] + 2)):
            d.put(x, y, z, DRESSED, shape=sc.stairs(0, -1))


def column(d: Design, cx: int, cz: int, keep: bool) -> None:
    """A Doric column five across: a round base, a fluted shaft, the cushion and the square slab of the capital."""
    k = {"keep": True} if keep else {}
    d.fill(solids.extrude(solids.disc(cx, cz, 2.9), FLOOR + 1, FLOOR + 1), DRESSED, **k)
    shaft = solids.extrude(solids.disc(cx, cz, 2.5), SHAFT[0], SHAFT[1])
    skin = {p for p in shaft if math.hypot(p[0] - cx, p[2] - cz) > 1.5}
    d.fill(shaft, DRESSED, **k)
    d.fill({p for p in skin if (round(math.degrees(math.atan2(p[2] - cz, p[0] - cx))) // 30) % 2 == 0}, FLUTE, **k)
    d.fill(solids.extrude(solids.disc(cx, cz, 3.2), CAPITAL[0], CAPITAL[0]), DRESSED, **k)
    d.box(cx - 3, CAPITAL[1], cz - 3, cx + 3, CAPITAL[1], cz + 3, DRESSED, **k)


def body(d: Design) -> None:
    """The end piers (the west one hollow for the stair, the east one with a little room), the walls between the
    passages, twelve columns, the entablature over everything (hollow, its underside the passages' ceilings)."""
    for x0, x1 in PIERS:
        d.fill(solids.box(x0, FLOOR + 1, DEPTH[0] + 3, x1, ARCHITRAVE[0] - 1, DEPTH[1] - 3), STONE)
        d.fill(solids.box(x0 + 2, FLOOR + 1, DEPTH[0] + 5, x1 - 2, ARCHITRAVE[0] - 1, DEPTH[1] - 5), CLEAR)
        d.fill(solids.box(x0 + 2, 29, DEPTH[0] + 5, x1 - 2, 29, DEPTH[1] - 5), STONE)    # the landing floor
    for cx in COLUMNS:
        keep = cx in (85, 109)
        if cx not in (COLUMNS[0], COLUMNS[-1]):
            walls = solids.box(cx - 1, FLOOR + 1, FRONT_Z + 3, cx + 1, ARCHITRAVE[0] - 1, BACK_Z - 3)
            d.fill(walls, STONE, keep=keep)
        for cz in (FRONT_Z, BACK_Z):
            column(d, cx, cz, keep)
    x0, x1 = GATE
    ent = solids.box(x0, ARCHITRAVE[0], DEPTH[0], x1, ROOF, DEPTH[1])
    inner = solids.box(x0 + 2, ARCHITRAVE[0] + 1, DEPTH[0] + 2, x1 - 2, ROOF - 1, DEPTH[1] - 2)
    d.fill(ent - inner, STONE)
    kept = {p for p in ent - inner if 83 <= p[0] <= 111}
    d.keep(kept)
    # The frieze: triglyphs standing proud over every column and between, the cornice above.
    for x in range(x0, x1 + 1):
        if (x - x0) % 8 in (0, 1, 2):
            for y in range(FRIEZE[0], FRIEZE[1] + 1):
                d.put(x, y, DEPTH[0] - 1, TRIGLYPH, shape=sc.pack(sc.POST) if (x - x0) % 8 == 1 else 0)
                d.put(x, y, DEPTH[1] + 1, TRIGLYPH, shape=sc.pack(sc.POST) if (x - x0) % 8 == 1 else 0)
    d.fill(solids.box(x0 - 1, CORNICE[0], DEPTH[0] - 1, x1 + 1, CORNICE[1], DEPTH[1] + 1)
           - solids.box(x0, CORNICE[0], DEPTH[0], x1, CORNICE[1], DEPTH[1]), DRESSED)
    d.fill(solids.box(x0 - 1, ROOF, DEPTH[0] - 1, x1 + 1, ROOF, DEPTH[1] + 1), DRESSED)
    d.fill(solids.box(x0 - 1, ROOF + 1, DEPTH[0] - 1, x1 + 1, ROOF + 1, DEPTH[1] + 1)
           - solids.box(x0, ROOF + 1, DEPTH[0], x1, ROOF + 1, DEPTH[1]), DRESSED)          # the roof's parapet


def attic(d: Design) -> None:
    """The attic, set back from the cornice so a terrace runs round it, with a recessed panel on both faces; the
    stepped podium for the bird on its middle. The middle of both is kept."""
    x0, x1 = GATE[0] + ATTIC_INSET, GATE[1] - ATTIC_INSET
    z0, z1 = DEPTH[0] + 4, DEPTH[1] - 4
    box = solids.box(x0, ATTIC[0], z0, x1, ATTIC[1], z1)
    inner = solids.box(x0 + 2, ATTIC[0], z0 + 2, x1 - 2, ATTIC[1] - 1, z1 - 2)
    d.fill(box - inner, STONE)
    d.fill(solids.box(x0 - 1, ATTIC[1], z0 - 1, x1 + 1, ATTIC[1], z1 + 1), DRESSED)       # its cornice
    for z in (z0, z1):
        d.fill(solids.box(x0 + 14, ATTIC[0] + 2, z, x1 - 14, ATTIC[1] - 2, z), PANEL)
    for x in range(x0 + 14, x1 - 13, 10):                                                  # pilasters in the panel
        for z, k in ((z0 - 1, -1), (z1 + 1, 1)):
            d.fill(solids.box(x, ATTIC[0] + 1, z, x + 1, ATTIC[1] - 1, z), DRESSED)
    # The podium: two steps, then the block the bird stands on.
    for step, (ya, yb, inset) in enumerate(((PODIUM[0], PODIUM[0] + 1, 0), (PODIUM[0] + 2, PODIUM[0] + 3, 2),
                                             (PODIUM[0] + 4, PODIUM[1], 4))):
        cells = solids.box(76 + inset, ya, 8 + inset, 118 - inset, yb, 24 - inset)
        d.fill(cells, STONE if step < 2 else DRESSED)
    d.keep(solids.box(74, ATTIC[0], z0, 120, PODIUM[1], z1))


def stair_way(d: Design) -> None:
    """The way up: a switchback inside the west pier to its landing, a second one to the roof terrace, a flight on the
    west terrace onto the attic and one more up the podium's steps to the bird."""
    x0, x1 = PIERS[0]
    switchback(d, x0 + 3, DEPTH[0] + 7, FLOOR, 29 - FLOOR, STAIR, along="z", sign=1, width=3)
    switchback(d, x0 + 3, DEPTH[0] + 7, 29, ROOF - 29, STAIR, along="z", sign=1, width=3)
    # The door from the first passage into the pier.
    d.fill(solids.box(x1 - 1, FLOOR + 1, 14, x1 + 1, FLOOR + 4, 16), CLEAR)
    # The roof terrace at the west end: a flight along +z up to the attic top, then along +x up the podium.
    flight(d, GATE[0] + 1, ROOF + 1, DEPTH[0] + 6, 0, 1, ATTIC[1] - ROOF, STAIR, width=3)
    d.fill(solids.box(GATE[0] + 1, ATTIC[1], DEPTH[0] + 16, GATE[0] + ATTIC_INSET - 1, ATTIC[1], DEPTH[0] + 18), STAIR)
    flight(d, 74, ATTIC[1] + 1, 14, 1, 0, PODIUM[1] - ATTIC[1], STAIR.kept(), width=3)     # kept, like the podium


def pavilion(d: Design, x0: int, x1: int) -> None:
    """A lower pavilion: a hall with a door and windows behind a portico of four columns under a pediment."""
    zf, zb = DEPTH[0] + 6, DEPTH[1] - 4
    top = 30
    hall = solids.box(x0 + 2, FLOOR + 1, zf, x1 - 2, top, zb)
    d.fill(hall - solids.box(x0 + 4, FLOOR + 1, zf + 2, x1 - 4, top - 1, zb - 2), STONE)
    mid = (x0 + x1) // 2
    d.fill(solids.box(mid - 1, FLOOR + 1, zf, mid + 1, FLOOR + 5, zf + 1), CLEAR)            # the door
    for wx in (x0 + 8, x1 - 8):
        d.fill(solids.arch("z", wx - 1, FLOOR + 8, zf, zf + 1, 3, 8, "round"), CLEAR)
    for wz in range(zf + 6, zb - 3, 8):
        d.fill(solids.arch("x", wz - 1, FLOOR + 8, x0 + 2, x0 + 3, 3, 8, "round")
               | solids.arch("x", wz - 1, FLOOR + 8, x1 - 3, x1 - 2, 3, 8, "round"), CLEAR)
    for cx in (x0 + 6, x0 + 14, x1 - 14, x1 - 6):
        cells = solids.extrude(solids.disc(cx, DEPTH[0] + 2, 1.2), FLOOR + 1, top - 2)
        d.fill(cells, DRESSED, shape=sc.pack(sc.CYLINDER))
        d.box(cx - 1, top - 1, DEPTH[0] + 1, cx + 1, top - 1, DEPTH[0] + 3, DRESSED)
    d.fill(solids.box(x0 + 1, top, DEPTH[0], x1 - 1, top + 2, zb + 1), STONE)               # entablature and roof
    d.fill(solids.box(x0 + 1, top + 1, DEPTH[0] - 1, x1 - 1, top + 1, DEPTH[0] - 1), DRESSED)
    # The pediment over the portico: a low gable, its slopes in half-height ramps.
    half = (x1 - x0 - 2) // 2
    for j in range(half // 2 + 1):
        y = top + 3 + j
        a, b = x0 + 1 + 2 * j, x1 - 1 - 2 * j
        if a > b:
            break
        d.fill(solids.box(a, y, DEPTH[0], b, y, DEPTH[0] + 1), STONE)
        d.put(a, y, DEPTH[0] - 1, DRESSED, shape=sc.ramp(1, 0, low=True))
        d.put(b, y, DEPTH[0] - 1, DRESSED, shape=sc.ramp(-1, 0, low=True))
        for x in range(a + 1, b):
            d.put(x, y, DEPTH[0] - 1, DRESSED if j == 0 else PANEL)


# --- the bird ------------------------------------------------------------------------------------------------------
def wing_outline(side: int) -> list[tuple[float, float]]:
    """One wing seen from the front, as (x, y) points: from the shoulder up and out to five feather fingers."""
    pts = [(4, 13), (8, 19), (13, 25), (18, 29), (21, 30), (22, 27), (24, 27), (24, 24), (26, 23), (25, 20),
           (27, 18), (25, 16), (26, 13), (22, 13), (21, 10), (17, 10), (14, 8), (9, 8), (4, 9)]
    return [(MID + side * x, BIRD_BASE + y) for x, y in pts]


def bird(d: Design) -> None:
    """A stylised bird facing the front: body, head with a hooked beak and a crest, wings raised in a wide V with
    feather fingers, a tail fan, legs gripping a gold perch. Everything gold and kept."""
    cz = 16
    s = Sculpture()
    s.box(MID - 4, BIRD_BASE, cz - 3, MID + 4, BIRD_BASE + 1, cz + 3, GOLD)                 # the perch
    s.capsule((MID - 2, BIRD_BASE + 2, cz - 1), (MID - 2, BIRD_BASE + 6, cz), 1.2, GOLD)   # the legs
    s.capsule((MID + 2, BIRD_BASE + 2, cz - 1), (MID + 2, BIRD_BASE + 6, cz), 1.2, GOLD)
    s.ellipsoid((MID, BIRD_BASE + 13, cz), 5.5, 8.5, 5.0, GOLD)                              # the body
    s.ellipsoid((MID, BIRD_BASE + 12, cz - 3), 4.0, 5.5, 2.5, GOLD_DEEP)                     # the breast
    s.sphere((MID, BIRD_BASE + 23, cz - 1), 3.6, GOLD)                                        # the head
    s.capsule((MID, BIRD_BASE + 23, cz - 4), (MID, BIRD_BASE + 22, cz - 7), 1.4, GOLD_DEEP, 0.8)   # the beak
    s.capsule((MID, BIRD_BASE + 22, cz - 7), (MID, BIRD_BASE + 20, cz - 7), 0.8, GOLD_DEEP)        # its hook
    for dx, h in ((-2, 4), (0, 5), (2, 4)):                                                    # the crest
        s.capsule((MID + dx, BIRD_BASE + 25, cz), (MID + dx * 1.5, BIRD_BASE + 25 + h, cz + 2), 0.9, GOLD)
    for dx in (-5, -2.5, 0, 2.5, 5):                                                          # the tail fan
        s.capsule((MID, BIRD_BASE + 7, cz + 3), (MID + dx, BIRD_BASE + 2, cz + 6), 1.1, GOLD)
    s.write(d, wall=2)
    for side in (-1, 1):
        flat = solids.polygon(wing_outline(side))
        wing = {(x, y, z) for x, y in flat for z in (cz, cz + 1)}
        d.fill(wing, GOLD)
        # Feather lines: every third row of the wing in the deeper gold, the fingers' edges too.
        d.fill({p for p in wing if (p[1] - BIRD_BASE) % 3 == 0 and abs(p[0] - MID) > 8}, GOLD_DEEP)
    for side in (-1, 1):                                                                      # the eyes
        d.put(MID + side * 3, BIRD_BASE + 24, cz - 2, EYE)


def build() -> Design:
    d = Design("golden_bird_gate", "Golden Bird Gate", nameKey="landmark.golden_bird_gate.name", ruin="always",
               ruinProfile="masonry", ruinStrength=0.5, requires=["atmosphere"], weight=1, minTerrainGeneration=39)
    stylobate(d)
    body(d)
    attic(d)
    for x0, x1 in WINGS:
        pavilion(d, x0, x1)
    stair_way(d)
    bird(d)
    # A door from the last passage into the east pier's little room.
    d.fill(solids.box(PIERS[1][0] - 1, FLOOR + 1, 14, PIERS[1][0] + 1, FLOOR + 4, 16), CLEAR)
    lights(d, [(x, 8, z) for x in (53, 66, 70, 83, 87, 107, 111, 124, 128, 141) for z in (8, 24)]
           + [(PIERS[0][0] + 2, 6, 20), (PIERS[0][0] + 2, 33, 20), (PIERS[1][0] + 2, 6, 20)]
           + [(WINGS[0][0] + 4, 8, 20), (WINGS[1][1] - 4, 8, 20)])
    d.chest(MID - 8, PODIUM[1] + 1, 12, table="ruin_rich")
    d.chest(WINGS[0][0] + 10, FLOOR + 1, 20, table="ruin_common")
    d.chest(PIERS[1][0] + 4, FLOOR + 1, 20, table="ruin_common", hidden=True)
    d.chest(PIERS[0][0] + 9, 30, DEPTH[0] + 6, table="ruin_common", chance=0.5)
    d.normalize()  # z moves by 3 (the stylobate reaches 3 in front of the gate): the zones below are in final coordinates
    # The ruin zones: the bird stands 104 rows high, so a pass over the whole gate grades its crumbling and erosion by THAT
    # height and barely touches the 57-row gate body and the 40-row pavilions. Each part decays as if it stood alone; the
    # attic, the podium and the bird above them keep the template's strength (and their kept chain).
    for x0, x1 in WINGS:
        d.ruin_zone(x0, 0, 0, x1, 45, DEPTH[1] + 6, "masonry", 1.0)
    d.ruin_zone(GATE[0], 0, 0, GATE[1], ROOF + 1, DEPTH[1] + 6, "masonry", 0.9)  # all in all ~20-30 % falls
    return d


if __name__ == "__main__":
    build().write()
