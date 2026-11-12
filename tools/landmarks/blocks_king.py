"""Blocks King: a burger restaurant shaped like one giant hamburger, with a sign out front.

From the ground up: the bottom bun (the ground floor, a checkered diner floor, small windows), the patty with a ring of
windows (the gallery floor), a slice of cheese whose four corners stick out and droop and whose edge drips down the
patty, wavy tomato slices, frilly lettuce that sticks out far enough to walk on (the terrace, with a railing and a
stair up from the ground), and the sesame top bun (the dome hall, round windows, four doors onto the terrace). Inside:
the order room behind the front door — a counter with the keeper behind it, who sells burgers to customers standing in
that closed room (`vendor theme=burgers;inside=1`; the closed-room check reaches 24 blocks and 4096 cells, so the order
room is small) — tables with chairs on every floor, two guests, lanterns, and a spiral stair from the ground floor up to
the dome hall. Out front a pylon sign reads BLOCKS KING in glowing letters (the name is the children's own pun; the
colours are the burger's own, no real chain's).

Intact (`ruin: never`), protected, only on inhabited worlds with air.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from geom import CLEAR, Design, Mat, Mix

CX, CZ = 40, 39                     # the burger's centre column
R_BUN, R_PATTY, R_TOP = 29.8, 30.5, 29.0
FLOOR2, FLOOR3 = 9, 22              # the floor rows of the gallery (patty) and the dome hall (lettuce)
DOME_Y, DOME_RY = 23, 20            # the top bun's base row and height
WOOD = "wood_planks_light"          # floors, walls, railings
FURNITURE = "wood_planks"           # tables, chairs, the outdoor stair (darker, so they stand out on the floors)


def r_of(x: float, z: float) -> float:
    return math.hypot(x - CX, z - CZ)


def ang(x: float, z: float) -> float:
    return math.atan2(x - CX, z - CZ)


def ring(y0: int, y1: int, outer, inner: float) -> set[tuple[int, int, int]]:
    """The cells of rows y0..y1 between radius `inner` and `outer` (a number, or a function of (angle, y))."""
    out = set()
    for y in range(y0, y1 + 1):
        for x in range(CX - 40, CX + 41):
            for z in range(CZ - 40, CZ + 41):
                r = r_of(x, z)
                ro = outer(ang(x, z), y) if callable(outer) else outer
                if inner <= r <= ro:
                    out.add((x, y, z))
    return out


def disc(y: int, radius: float) -> set[tuple[int, int, int]]:
    return {(x, y, z) for x, z in solids.disc(CX, CZ, radius)}


# ---------------------------------------------------------------- the burger's layers
def bottom_bun(d: Design) -> None:
    def outer(a: float, y: int) -> float:
        return {0: 27.4, 1: 28.8, 2: 29.4}.get(y, R_BUN if y < 7 else R_BUN - 0.3)

    d.fill(ring(0, 8, outer, 27.6), "burger_bun")
    # The diner floor: a checkerboard of white and dark tiles.
    white, black = Mat("concrete", tint=0xF4F1EA), Mat("concrete", tint=0x2E2A28)
    d.fill(disc(0, 27.6), lambda x, y, z: white if ((x // 2) + (z // 2)) % 2 == 0 else black)


def patty(d: Design) -> None:
    meat = Mix("burger_patty", (Mat("burger_patty", tint=0x4A2E1A), 0.18), seed=21)

    def outer(a: float, y: int) -> float:
        return R_PATTY + 0.45 * solids.value_noise(a * 6, y * 0.5, 0, seed=4)

    d.fill(ring(FLOOR2 + 1, 15, outer, 28.2), meat)
    d.fill(disc(FLOOR2, 29.6), meat)                                  # the gallery floor (the patty's bottom)
    d.fill(disc(FLOOR2, 27.6), Mat(WOOD))                             # ... boarded inside


def cheese(d: Design) -> None:
    """A square slice over a round one: the four corners stick out diagonally and droop, the edge drips."""
    c = Mat("burger_cheese")
    half = 27.5
    cells = set()
    for x in range(CX - 40, CX + 41):
        for z in range(CZ - 40, CZ + 41):
            r = r_of(x, z)
            in_square = abs(x - CX) <= half and abs(z - CZ) <= half
            if r < 28.2 or not (r <= 31.2 or in_square):
                continue
            drop = 0 if r <= 34 else 1 if r <= 36.5 else 2
            for y in (16 - drop, 17 - drop):
                cells.add((x, y, z))
    d.fill(cells, c)
    # The drooping corner tips end in hanging ramps (the deep side toward the burger).
    for x, y, z in cells:
        if r_of(x, z) > 36.5 and (x, y - 1, z) not in cells and y == 14:
            dx = -1 if x > CX else 1
            dz = -1 if z > CZ else 1
            d.put(x, y, z, c, shape=sc.ramp_down(dx, 0) if abs(x - CX) > abs(z - CZ) else sc.ramp_down(0, dz))
    # Drips down the patty: a column of cheese just outside it, ending in a hanging dome (a drop).
    for i in range(14):
        a = 2 * math.pi * (i + 0.37 * solids.hash01(i, 0, 0, 5)) / 14
        length = 2 + int(4 * solids.hash01(i, 1, 0, 5))
        x, z = round(CX + 31.6 * math.sin(a)), round(CZ + 31.6 * math.cos(a))
        for k in range(1, length + 1):
            d.put(x, 16 - k, z, c, shape=sc.hanging("dome") if k == length else 0)


def tomato_and_lettuce(d: Design) -> None:
    d.fill(ring(18, 19, lambda a, y: 30.6 + 1.5 * abs(math.cos(3 * a)), 28.2), "burger_tomato")
    leaf = Mix("burger_lettuce", (Mat("burger_lettuce", tint=0x8BD64E), 0.2), seed=8)
    d.fill(ring(20, 20, lambda a, y: 33.9 + 1.6 * math.sin(9 * a + 1.0), 28.2), leaf)
    d.fill(ring(21, 21, lambda a, y: 33.6 + 1.2 * math.sin(9 * a + 0.5), 28.2), leaf)
    floor3 = ring(FLOOR3, FLOOR3, lambda a, y: 33.8 + 0.8 * math.sin(9 * a), 0)
    d.fill(floor3, leaf)
    d.fill(disc(FLOOR3, 27.4), Mat(WOOD))                             # the dome hall's floor, boarded
    # Leaf tips hanging over the tomato where the lettuce frills out.
    for x, y, z in ring(20, 20, lambda a, y: 33.9 + 1.6 * math.sin(9 * a + 1.0), 32.6):
        if math.sin(9 * ang(x, z) + 1.0) > 0.6:
            d.put(x, 19, z, leaf, shape=sc.hanging("dome"))


def top_bun(d: Design) -> None:
    bun = d.dome(CX, CZ, DOME_Y, R_TOP, "burger_bun_sesame", ry=DOME_RY, wall=2)
    # Sesame seeds you can see from far away: pale dots on the upper outer skin.
    skin = {p for p in bun if p[1] >= DOME_Y + 5 and ((p[0] - CX) ** 2 + (p[2] - CZ) ** 2) / R_TOP ** 2
            + ((p[1] - DOME_Y) / DOME_RY) ** 2 >= 0.86}
    d.scatter(skin, Mat("travertine"), 0.06, seed=17)


# ---------------------------------------------------------------- openings
def windows(d: Design) -> None:
    glass = Mat("glass")
    for i in range(16):                                               # the bun: small windows on the ground floor
        a = 2 * math.pi * i / 16
        if abs(math.sin(a / 2)) > 0.97 or abs(a - math.pi) < 0.5:   # not over the front door / order room
            continue
        _cut_radial(d, a, 3, 5, 28.0, 30.4, 1, glass)
    for i in range(20):                                               # the patty: the window ring
        a = 2 * math.pi * (i + 0.5) / 20
        _cut_radial(d, a, FLOOR2 + 2, FLOOR2 + 5, 28.0, 31.0, 1, glass)
    for i in range(12):                                               # the top bun: round windows
        a = 2 * math.pi * (i + 0.5) / 12
        _cut_radial(d, a, DOME_Y + 4, DOME_Y + 6, 25.0, 30.0, 1, glass)


def _cut_radial(d: Design, a: float, y0: int, y1: int, r0: float, r1: float, half: float, m) -> set:
    """An opening through a round wall at angle `a` (from +z): rows y0..y1, `half` cells to each side of the ray."""
    cells = set()
    for y in range(y0, y1 + 1):
        for x in range(CX - 40, CX + 41):
            for z in range(CZ - 40, CZ + 41):
                r = r_of(x, z)
                if r0 <= r <= r1:
                    # distance of the cell from the ray, along the tangent
                    t = (x - CX) * math.cos(a) - (z - CZ) * math.sin(a)
                    along = (x - CX) * math.sin(a) + (z - CZ) * math.cos(a)
                    if abs(t) <= half + 0.01 and along > 0:
                        cells.add((x, y, z))
    if cells:
        d.fill(cells, m, where="blocks")
    return cells


def front_door(d: Design) -> None:
    """The front door (-z): three wide, four high, through the bun's wall, a sliding door in its middle."""
    for x in range(CX - 1, CX + 2):
        for y in range(1, 5):
            for z in range(CZ - 31, CZ - 26):
                d.clear(x, y, z)
    d.door(CX - 1, 1, CZ - 28, "slide")                              # in the bun's inner course: its jambs exist on every row
    for x in range(CX - 1, CX + 2):                                   # a glass transom: the door closes 3 rows
        d.put(x, 4, CZ - 28, "glass")
    # A doormat path of travertine to the door.
    for x in range(CX - 2, CX + 3):
        for z in range(CZ - 33, CZ - 30):
            if r_of(x, z) > R_BUN - 0.5:
                d.put(x, 0, z, "travertine")


def order_room(d: Design) -> None:
    """The closed order room behind the front door: walls, a ceiling, the counter (with a flap to pass), the keeper behind
    it and a door at the back into the dining hall. Small on purpose: the keeper sells only inside this room."""
    wall = Mat(WOOD)
    z_back, z_counter = CZ - 15, CZ - 20
    def inside(x: int, z: int) -> bool:  # the bun's inner face
        return r_of(x, z) < 28.6

    for z in range(CZ - 30, z_back + 1):
        for y in range(1, 6):
            for x in (CX - 9, CX + 9):
                if inside(x, z):
                    d.put(x, y, z, wall)
    for x in range(CX - 9, CX + 10):
        for y in range(1, 6):
            d.put(x, y, z_back, wall)
        for z in range(CZ - 30, z_back + 1):
            if inside(x, z):
                d.put(x, 5, z, wall)                                  # the ceiling
    for x in (CX - 1, CX):                                            # the door into the hall
        for y in range(1, 4):
            d.clear(x, y, z_back)
    d.door(CX - 1, 1, z_back, "hinge")
    for x in range(CX - 8, CX + 6):                                   # the counter, a flap at the right end
        d.put(x, 1, z_counter, "shop_counter")
    d.vendor(CX, 1, z_counter + 2, "burgers", inside=True)
    d.put(CX - 7, 4, z_counter + 3, "lantern")
    d.put(CX + 7, 4, CZ - 25, "lantern")


def table(d: Design, x: int, y: int, z: int, chairs: str = "nesw", lamp: bool = False) -> None:
    """A table with chairs round it (the backrests away from the table)."""
    d.put(x, y, z, Mat(FURNITURE, shape=sc.pack("table")))
    for k, (dx, dz) in zip("nesw", ((0, 1), (1, 0), (0, -1), (-1, 0))):
        if k in chairs:
            d.put(x + dx, y, z + dz, Mat(FURNITURE, shape=sc.pack("chair", sc.yaw_toward(dx, dz))))
    if lamp:
        d.put(x, y + 1, z, "lantern")


def furnish(d: Design) -> None:
    """Tables and lanterns on the three floors, two guests on the ground floor."""
    for (x, z) in ((-14, -6), (-6, -4), (6, -4), (14, -6), (-18, 6), (-8, 8), (8, 8), (18, 4), (-4, 18), (6, 20)):
        table(d, CX + x, 1, CZ + z, lamp=(x + z) % 3 == 0)
    d.npc(CX - 13, 1, CZ - 6)
    d.npc(CX + 8, 1, CZ + 9)
    for (x, z) in ((-12, -10), (0, -2), (12, -10), (-16, 12), (0, 14), (6, 12), (-22, 0), (20, -2)):
        table(d, CX + x, FLOOR2 + 1, CZ + z, lamp=True)
    for (x, z) in ((-10, -8), (10, -8), (-12, 8), (0, 0), (12, 8), (-4, -18), (5, 18)):
        table(d, CX + x, FLOOR3 + 1, CZ + z, lamp=x == 0)
    for y in (8, FLOOR3 - 1):                                         # hanging lanterns under the ceilings
        for a in range(0, 360, 45):
            x = round(CX + 20 * math.sin(math.radians(a + 22)))
            z = round(CZ + 20 * math.cos(math.radians(a + 22)))
            if not (abs(x - CX) <= 10 and z < CZ - 14):
                d.put(x, y, z, "lantern")
    for a in range(0, 360, 60):                                       # wall lanterns in the dome hall
        x = round(CX + 25.5 * math.sin(math.radians(a)))
        z = round(CZ + 25.5 * math.cos(math.radians(a)))
        d.put(x, DOME_Y + 2, z, "lantern")


def stair_and_terrace(d: Design) -> None:
    """The spiral stair inside (ground floor -> gallery -> dome hall), the terrace's doors and railing, the outdoor stair."""
    d.spiral_stair(CX + 17, CZ + 15, 1, FLOOR3, 1.5, 4.6, Mat(WOOD), steps_per_turn=16, newel=Mat("wood_log"))
    for x, z in solids.disc(CX + 17, CZ + 15, 1.5):                   # the newel stops at the dome hall's floor
        for y in range(FLOOR3 + 1, FLOOR3 + 4):
            d.clear(x, y, z)
    # Four doorways from the dome hall onto the lettuce terrace (2 wide, 3 high).
    for a in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2):
        _cut_radial(d, a + 0.21, DOME_Y, DOME_Y + 2, 26.0, 30.0, 1, CLEAR)
    # The railing: a fence round the terrace's rim (open where the outdoor stair arrives).
    rail = set()
    for x in range(CX - 40, CX + 41):
        for z in range(CZ - 40, CZ + 41):
            if 32.4 <= r_of(x, z) <= 33.4:
                rail.add((x, FLOOR3 + 1, z))
    land_z = CZ - 14
    for x, y, z in rail:
        if x < CX - 25 and abs(z - land_z) <= 2:
            continue
        tx, tz = (z - CZ), -(x - CX)                                  # the tangent
        yaw = 0 if abs(tx) >= abs(tz) else 1
        d.put(x, y, z, Mat(WOOD, shape=sc.pack("fence", yaw)))
    # The outdoor stair up the left side, three wide, and a landing onto the terrace.
    d.stairs(CX - 40, 0, CZ + 10, 0, -1, FLOOR3 + 1, Mat(FURNITURE), width=3, fill_below=True)
    for x in range(CX - 40, CX - 30):
        for z in range(land_z - 1, land_z + 2):
            if r_of(x, z) > 32.0:
                d.put(x, FLOOR3, z, Mat(FURNITURE))
    for x, z in ((CX - 37, land_z - 1), (CX - 37, land_z + 1), (CX - 34, land_z - 1), (CX - 34, land_z + 1)):
        for y in range(FLOOR3):
            d.put(x, y, z, Mat("wood_log", shape=sc.post()))


def sign(d: Design) -> None:
    """The pylon sign out front: two log posts, a board, BLOCKS / KING in glowing cheese-yellow letters (facing -z)."""
    zb = CZ - 38
    x0, x1 = CX + 25, CX + 65
    for px in (CX + 31, CX + 59):
        d.box(px, 0, zb, px + 1, 23, zb + 1, "wood_log")
    d.box(x0, 24, zb + 1, x1, 43, zb + 1, "wood_planks")
    for x in range(x0, x1 + 1):                                       # the frame round its rim
        for y in range(24, 44):
            if x in (x0, x1) or y in (24, 43):
                d.put(x, y, zb + 1, "wood_log")
    letters = Mat("burger_cheese", glow=0xFFD24A)
    for word, base in (("BLOCKS", 35), ("KING", 26)):
        w, _ = Design.text_size(word)
        d.text(word, (x0 + x1 + 1 - w) // 2, base, zb, letters, dir="+x")


def build() -> Design:
    d = Design("blocks_king", "Blocks King", nameKey="landmark.blocks_king.name", minTerrainGeneration=39, ruin="never",
               protected=True, protection="protected", requires=["atmosphere", "inhabited"], weight=1)
    bottom_bun(d)
    patty(d)
    cheese(d)
    tomato_and_lettuce(d)
    top_bun(d)
    windows(d)
    front_door(d)
    order_room(d)
    furnish(d)
    stair_and_terrace(d)
    sign(d)
    d.normalize()
    return d


if __name__ == "__main__":
    build().write()
