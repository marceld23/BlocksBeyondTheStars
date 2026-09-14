# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Builders for the shipped settlement and G.D.S. district MODULES (#1886), used by tools/gen_settlement_modules.py.

Marcel's decisions (2026-09-14): 8 x 8 houses; walls follow the planet (material tokens @wall / @accent / @roof /
@floor / @path, resolved by the composer); every resident has a bed (market, notice house, tavern, workshop and the
gardener's shed each carry a room with a bed); kit settlements are built entirely from modules, with taverns and
workshops; 2-3 variants per function, each as a human and an alien variant; G.D.S. districts 2 each.

Conventions the composers and the server rely on:
- the entrance faces -Z (z = 0); doorways are 2 wide and 3 tall with a door marker on the lower column
  (doorway clearance: the gap and the rows beside it stay free);
- a storey is 4 high: floor y, walls y+1..y+3, deck y+4; a village module is one storey (height <= 7), a town module
  two (height <= 11), a city flat three (height <= 15), a district fits 32 x 32 and height <= 19;
- storeys are joined by a STAIRCASE (Stairs shape, rising toward +Z), never a ladder: NPCs walk steps, not ladders;
  the staircase runs along the west wall (x = 1) beside the doorway lane, so its foot stays free;
- every room carries a `room` marker (furnished by function: a vendor / board / tavern / workshop marker in the same
  room picks the furniture), interior doorways carry a door marker, which keeps two rooms apart for the furnisher.
"""

# Packed shapes: ShapeCode.Pack(shape, yaw) = shape << 2 | yaw; yaw 0 = +Z, 1 = -X, 2 = -Z, 3 = +X.
STAIRS = 6 << 2
TABLE = 14 << 2
CHAIR = 15 << 2
SLAB = 1 << 2

PURPLE = 0x3A1F5C
RED = 0x8A1C24


class Module:
    def __init__(self, w, h, l):
        self.w, self.h, self.l = w, h, l
        self.cells = {}

    def block(self, x, y, z, block_id, shape=0, tint=0):
        if not (0 <= x < self.w and 0 <= y < self.h and 0 <= z < self.l):
            raise ValueError(f"cell ({x},{y},{z}) outside {self.w}x{self.h}x{self.l}")
        c = {"x": x, "y": y, "z": z, "kind": "block", "id": block_id}
        if tint:
            c["tint"] = tint
        if shape:
            c["shape"] = shape
        self.cells[(x, y, z)] = c

    def air(self, x, y, z):
        self.cells.pop((x, y, z), None)

    def marker(self, x, y, z, marker_id):
        self.cells[(x, y, z)] = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker_id}

    def fill(self, x0, y0, z0, x1, y1, z1, block_id, tint=0):
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.block(x, y, z, block_id, tint=tint)

    def clear(self, x0, y0, z0, x1, y1, z1):
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.air(x, y, z)

    def paste(self, other, ox, oy, oz):
        for (x, y, z), c in other.cells.items():
            n = dict(c)
            n["x"], n["y"], n["z"] = x + ox, y + oy, z + oz
            if not (0 <= n["x"] < self.w and 0 <= n["y"] < self.h and 0 <= n["z"] < self.l):
                raise ValueError(f"pasted cell outside the module at ({n['x']},{n['y']},{n['z']})")
            self.cells[(n["x"], n["y"], n["z"])] = n

    def height_used(self):
        return max(y for (_, y, _) in self.cells) + 1

    def ordered(self):
        return [self.cells[k] for k in sorted(self.cells)]


# ------------------------------------------------------------------ shells

def storey_walls(m, base, wall, corner, window, tint=0, door=None, windows=True):
    """Walls y = base+1 .. base+3 around the footprint; windows at the side centres (base+2); a doorway (x0, x1) on -Z."""
    w, l = m.w, m.l
    for y in range(base + 1, base + 4):
        for x in range(w):
            for z in range(l):
                if not (x in (0, w - 1) or z in (0, l - 1)):
                    continue
                if door and z == 0 and door[0] <= x <= door[1]:
                    continue
                is_corner = x in (0, w - 1) and z in (0, l - 1)
                mid_x = x in (w // 2 - 2, w // 2 + 1) if w >= 8 else x == w // 2
                mid_z = z in (l // 2 - 1, l // 2)
                win = windows and y == base + 2 and not is_corner and (
                    (z in (0, l - 1) and mid_x) or (x in (0, w - 1) and mid_z))
                if win and door and z == 0 and door[0] - 1 <= x <= door[1] + 1:
                    win = False
                m.block(x, y, z, window if win else (corner if is_corner else wall), tint=0 if (win or is_corner) else tint)


def deck(m, y, block_id, tint=0):
    m.fill(0, y, 0, m.w - 1, y, m.l - 1, block_id, tint=tint)


def staircase(m, base, wall):
    """A flight from floor `base` up to the next storey along the west wall: steps at x = 1, z = 2..4 rising toward +Z,
    the space under them walled in, holes in the deck above, the landing at (1, base + 5, 5)."""
    for i, z in enumerate((2, 3, 4)):
        m.block(1, base + 1 + i, z, "@wall" if wall is None else wall, shape=STAIRS | 0)
        for y in range(base + 1, base + 1 + i):
            m.block(1, y, z, "@wall" if wall is None else wall)
        m.air(1, base + 4, z)


def interior_wall_z(m, base, z, x0, x1, door_x, marker, wall="@wall"):
    """A partition along X at row z (x0..x1), a 2-wide doorway at door_x, door_x + 1 with a door marker."""
    for x in range(x0, x1 + 1):
        for y in range(base + 1, base + 4):
            if door_x <= x <= door_x + 1:
                m.air(x, y, z)
            else:
                m.block(x, y, z, wall)
    m.marker(door_x, base + 1, z, marker)


def interior_wall_x(m, base, x, z0, z1, door_z, marker, wall="@wall"):
    """A partition along Z at column x (z0..z1), a 2-wide doorway at door_z, door_z + 1 with a door marker."""
    for z in range(z0, z1 + 1):
        for y in range(base + 1, base + 4):
            if door_z <= z <= door_z + 1:
                m.air(x, y, z)
            else:
                m.block(x, y, z, wall)
    m.marker(x, base + 1, door_z, marker)


def village_shell(alien, roof):
    """One storey on an 8 x 8 footprint, entrance x = 2..3 on -Z. Human: log posts and a log roof; alien: crystal
    posts and a crystal roof."""
    m = Module(8, 7, 8)
    deck(m, 0, "@floor")
    corner = "@accent" if alien else "wood_log"
    storey_walls(m, 0, "@wall", corner, "glass", door=(2, 3))
    deck(m, 4, "@wall" if alien else "wood_log")
    roof_on(m, 4, alien, roof)
    m.marker(2, 1, 0, "door_hinge")
    return m


def roof_on(m, top, alien, roof):
    """Roof cells above the deck at `top` (at most two rows)."""
    w, l = m.w, m.l
    mat = "@roof" if alien else "wood_log"
    if roof == 0:  # gable, ridge along X
        m.fill(0, top + 1, 1, w - 1, top + 1, l - 2, mat)
        m.fill(0, top + 2, 3, w - 1, top + 2, l - 4, mat)
    elif roof == 1:  # stepped hip
        for x in range(1, w - 1):
            for z in range(1, l - 1):
                if x in (1, w - 2) or z in (1, l - 2):
                    m.block(x, top + 1, z, mat)
        m.fill(2, top + 2, 2, w - 3, top + 2, l - 3, mat)
    elif roof == 2:  # flat with a parapet (desert-style for humans, a crystal rim for aliens)
        for x in range(w):
            for z in range(l):
                if x in (0, w - 1) or z in (0, l - 1):
                    if not alien or (x + z) % 2 == 0:
                        m.block(x, top + 1, z, "@accent" if alien else "@wall")
        if alien:
            m.fill(3, top + 1, 3, 4, top + 2, 4, "@accent")
    elif roof == 3:  # alien spires on the corners and a crown in the middle
        for x, z in ((0, 0), (0, l - 1), (w - 1, 0), (w - 1, l - 1)):
            m.block(x, top + 1, z, mat)
            m.block(x, top + 2, z, mat)
        m.fill(2, top + 1, 2, w - 3, top + 1, l - 3, mat)
        m.fill(3, top + 2, 3, w - 4, top + 2, l - 4, mat)


def town_shell(alien, storeys, roof):
    """`storeys` storeys of iron on an 8 x 8 footprint, entrance x = 2..3 on -Z, staircases along the west wall
    (a second flight along the east wall), ceiling lights set into every deck."""
    top = storeys * 4
    m = Module(8, top + 2, 8)
    deck(m, 0, "@floor")
    for s in range(storeys):
        base = s * 4
        storey_walls(m, base, "@wall", "@accent" if alien else "@wall", "glass", door=(2, 3) if s == 0 else None)
        deck(m, base + 4, "@floor" if s < storeys - 1 else "@wall")
    # light set INTO each deck (#1808), away from the stairwells
    for s in range(1, storeys + 1):
        m.block(5, s * 4, 2, "strip_light_warm")
    staircase(m, 0, None)
    if storeys >= 3:
        # second flight along the east wall, x = 6, z = 2..4
        for i, z in enumerate((2, 3, 4)):
            m.block(6, 5 + i, z, "@wall", shape=STAIRS | 0)
            for y in range(5, 5 + i):
                m.block(6, y, z, "@wall")
            m.air(6, 8, z)
        m.marker(6, 5, 1, "npc")  # the foot of the second flight stays free (a resident's idle spot)
    # roof trim
    if alien:
        for x, z in ((0, 0), (0, 7), (7, 0), (7, 7)):
            m.block(x, top + 1, z, "@accent")
        if roof == 1:
            m.fill(3, top + 1, 3, 4, top + 1, 4, "@accent")
    else:
        for x in range(8):
            for z in range(8):
                if x in (0, 7) or z in (0, 7):
                    if roof == 0 or (x + z) % 2 == 0:
                        m.block(x, top + 1, z, "@accent" if roof == 0 else "@wall")
    m.marker(2, 1, 0, "door_slide")
    return m


# ------------------------------------------------------------------ village interiors

def rooms_front_back(m, base, door_marker, front_marker, front_at, back_room=True):
    """Front room z = 1..3, a partition at z = 4 with a doorway at x = 5..6, the back room z = 5..6."""
    if back_room:
        interior_wall_z(m, base, 4, 1, 6, 5, door_marker)
        m.marker(2, base + 1, 6, "room")
    m.marker(1, base + 1, 3, "room")
    if front_marker:
        m.marker(front_at[0], base + 1, front_at[1], front_marker)


def rooms_side(m, base, door_marker, main_marker, main_at):
    """Main room x = 1..4, a partition at x = 5 with a doorway at z = 3..4, the side room x = 6."""
    interior_wall_x(m, base, 5, 1, 6, 3, door_marker)
    m.marker(6, base + 1, 1, "room")
    m.marker(3, base + 1, 5, "room")
    if main_marker:
        m.marker(main_at[0], base + 1, main_at[1], main_marker)


def greenhouse_beds(m, crop, xs, zs, tray=None):
    """Planting cells: soil (or a hydro tray) in the floor with the crop above, on every (x, z) of xs x zs."""
    for z in zs:
        for x in xs:
            m.block(x, 0, z, tray or "dirt")
            m.block(x, 1, z, crop)


def village_module(function, variant, alien):
    roof = (variant + (2 if alien else 0)) % 4 if alien else variant % 3
    if alien and roof in (0, 1):
        roof = 2 + roof  # aliens: parapet rim or spires
    m = village_shell(alien, roof)
    if function == "house":
        if variant == 0:
            m.marker(2, 1, 4, "room")
            m.marker(5, 1, 5, "npc")
        elif variant == 1:
            rooms_front_back(m, 0, "door_hinge", "npc", (4, 2))
        else:
            rooms_side(m, 0, "door_hinge", "npc", (2, 3))
    elif function in ("market", "board", "tavern", "workshop"):
        post = {"market": "vendor", "board": "mission_board", "tavern": "tavern", "workshop": "workshop"}[function]
        if variant == 0:
            rooms_front_back(m, 0, "door_hinge", post, (5, 2))
        else:
            rooms_side(m, 0, "door_hinge", post, (2, 5))
    elif function == "greenhouse":
        # glass walls between the posts, the shed with the gardener's bed at the back / side
        for y in (1, 2, 3):
            for x in range(8):
                for z in range(8):
                    c = m.cells.get((x, y, z))
                    if c and c["id"] == "@wall" and z < 4:
                        m.block(x, y, z, "glass")
        if variant == 0:
            for x in range(1, 7):
                m.block(x, 4, 1, "glass")  # a skylight over the beds
                m.block(x, 4, 2, "glass")
            # beds left and right of the doorway lane (x 2..3) in the two front rows; the row before the shed door
            # (z = 3) and the lane stay free
            greenhouse_beds(m, "flora_cropberry", xs=(1, 4, 5, 6), zs=(1, 2))
            interior_wall_z(m, 0, 4, 1, 6, 5, "door_hinge")
            m.marker(2, 1, 6, "room")
            m.marker(1, 1, 3, "greenhouse")
            m.marker(2, 1, 3, "npc")
        else:
            # the shed along the east wall (x = 6); beds in the west half, away from the lane and the shed door
            greenhouse_beds(m, "flora_cropgrain", xs=(1,), zs=(1, 2, 3, 4, 5, 6))
            greenhouse_beds(m, "flora_cropshroom", xs=(4,), zs=(5, 6))
            interior_wall_x(m, 0, 5, 1, 6, 3, "door_hinge")
            m.marker(6, 1, 1, "room")
            m.marker(2, 1, 5, "greenhouse")
            m.marker(3, 1, 4, "npc")
    else:
        raise ValueError(function)
    return m


# ------------------------------------------------------------------ town interiors

def town_module(function, variant, alien, storeys=2):
    roof = variant % 2
    m = town_shell(alien, storeys, roof)
    # ground floor
    if function == "house":
        m.marker(4, 1, 4, "room")
        m.marker(5, 1, 2, "npc")
        if variant == 1:
            interior_wall_x(m, 4, 4, 1, 6, 1, "door_slide")  # two bedrooms upstairs
            m.marker(2, 5, 1, "room")
            m.marker(6, 5, 5, "room")
        else:
            m.marker(4, 5, 5, "room")
        if storeys >= 3:
            m.marker(3, 9, 5, "room")
    elif function in ("market", "board", "tavern", "workshop"):
        post = {"market": "vendor", "board": "mission_board", "tavern": "tavern", "workshop": "workshop"}[function]
        m.marker(4, 1, 5, "room")
        m.marker(5, 1, 3 if variant == 0 else 5, post)
        if variant == 0:
            m.marker(4, 5, 5, "room")  # the keeper's flat upstairs
        else:
            interior_wall_x(m, 4, 4, 1, 6, 1, "door_slide")
            m.marker(2, 5, 1, "room")
            m.marker(6, 5, 5, "room")
    elif function == "greenhouse":
        for y in (1, 2, 3):
            for x in range(8):
                for z in range(8):
                    c = m.cells.get((x, y, z))
                    if c and c["id"] == "@wall":
                        m.block(x, y, z, "glass")
        # hydro trays east of the doorway lane (x 2..3) and clear of the staircase (x = 1)
        crop = ("flora_cropgrain", "flora_cropberry")[variant % 2]
        greenhouse_beds(m, crop, xs=(4, 5, 6), zs=(1, 3, 5) if variant == 0 else (2, 5), tray="hydro_tray")
        for z in (2, 4, 6):
            m.block(6, 4, z, "strip_light_warm")
        m.marker(2, 1, 4, "greenhouse")
        m.marker(3, 1, 4, "npc")
        m.marker(4, 5, 5, "room")  # the gardener's flat upstairs
    else:
        raise ValueError(function)
    return m


# ------------------------------------------------------------------ G.D.S. districts (32 x 32)

SIZE = 32


def lamp_post(m, x, z, height=3):
    for y in range(1, height):
        m.block(x, y, z, "@wall")
    m.block(x, height, z, "strip_light_warm")


def gds_flat(storeys, variant, tint):
    """A G.D.S. town flat: the town builder with every wall cell tinted in the city's colours."""
    f = town_module("house", variant, alien=False, storeys=storeys)
    for c in f.cells.values():
        if c["kind"] == "block" and c["id"] == "@wall" and c.get("shape", 0) == 0 and c["y"] > 0:
            c["tint"] = tint
    return f


def district_base():
    m = Module(SIZE, 19, SIZE)
    for x in range(SIZE):
        for z in range(SIZE):
            m.block(x, 0, z, "@path")
    return m


def gds_housing(variant):
    m = district_base()
    if variant == 0:
        # three by three two-storey flats on a 10-block stride, lamps on the lanes
        for cx in range(3):
            for cz in range(3):
                tint = RED if (cx + cz) % 3 == 0 else PURPLE
                m.paste(gds_flat(2, (cx + cz) % 2, tint), 2 + cx * 10, 0, 2 + cz * 10)
        for i in (1, 11, 21, 31):
            lamp_post(m, i, 1)
            lamp_post(m, 1, i if i < 31 else 30)
    else:
        # two by two three-storey blocks around a green courtyard
        for cx in range(2):
            for cz in range(2):
                tint = RED if (cx + cz) == 1 else PURPLE
                m.paste(gds_flat(3, cx, tint), 4 + cx * 16, 0, 4 + cz * 16)
        for x in range(13, 19):
            for z in range(13, 19):
                m.block(x, 0, z, "grass")
        for x, z in ((14, 14), (17, 17)):
            for y in range(1, 4):
                m.block(x, y, z, "wood_log")
            m.fill(x - 1, 4, z - 1, x + 1, 5, z + 1, "tree_leaves")
        for i in (2, 29):
            lamp_post(m, i, 2)
            lamp_post(m, i, 29)
    return m


def gds_market(variant):
    m = district_base()
    halls = ((2, 2), (22, 2)) if variant == 0 else ((2, 22), (22, 22))
    for n, (x, z) in enumerate(halls):
        hall = town_module("market", n % 2, alien=False)
        for c in hall.cells.values():
            if c["kind"] == "block" and c["id"] == "@wall" and c.get("shape", 0) == 0 and c["y"] > 0:
                c["tint"] = RED if n == 0 else PURPLE
        m.paste(hall, x, 0, z)
    office = town_module("board", variant, alien=False)
    m.paste(office, 12, 0, 22 if variant == 0 else 2)
    # stalls: a glass canopy on four posts, a trader minding each
    row_z = 14
    for i in range(3):
        sx = 6 + i * 8
        for dx, dz in ((0, 0), (3, 0), (0, 3), (3, 3)):
            m.fill(sx + dx, 1, row_z + dz, sx + dx, 2, row_z + dz, "@wall")
        m.fill(sx, 3, row_z, sx + 3, 3, row_z + 3, "glass")
        m.marker(sx + 1, 1, row_z + 1, "npc")
    for x, z in ((1, 1), (30, 1), (1, 30), (30, 30)):
        lamp_post(m, x, z)
    return m


def gds_hall(variant):
    m = district_base()
    fp = 14
    ox = oz = (SIZE - fp) // 2
    hall = Module(fp, 10, fp)
    deck(hall, 0, "@floor")
    storey_walls(hall, 0, "@wall", "@wall", "glass", tint=PURPLE, door=(6, 7))
    deck(hall, 4, "@floor")
    storey_walls(hall, 4, "@wall", "@wall", "glass", tint=PURPLE)
    deck(hall, 8, "@wall", tint=PURPLE)
    for x in range(fp):
        for z in range(fp):
            if x in (0, fp - 1) or z in (0, fp - 1):
                hall.block(x, 9, z, "@wall", tint=RED)  # the red crown row
    # staircase up along the west wall, four rooms upstairs (the residents' bedrooms)
    staircase(hall, 0, None)
    # upstairs: four bedrooms — a partition along x = 7 (doorway z 5..6) and one along z = 7 on each side
    interior_wall_x(hall, 4, 7, 1, 12, 5, "door_slide")
    interior_wall_z(hall, 4, 7, 8, 12, 10, "door_slide")
    interior_wall_z(hall, 4, 7, 1, 6, 3 if variant == 0 else 4, "door_slide")
    hall.marker(4, 5, 2, "room")
    hall.marker(4, 5, 10, "room")
    hall.marker(10, 5, 3, "room")
    hall.marker(10, 5, 10, "room")
    hall.marker(10, 1, 9, "room")
    hall.marker(6, 1, 0, "door_slide")
    hall.marker(10, 1, 4, "mission_board" if variant == 1 else "npc")
    for x in (4, 9):
        hall.block(x, 4, 7, "strip_light_warm")
        hall.block(x, 8, 7, "strip_light_warm")
    m.paste(hall, ox, 0, oz)
    for gx, gz in ((ox - 2, oz - 2), (ox + fp + 1, oz - 2)):
        m.marker(gx, 1, gz, "guard_post")
    for x, z in ((2, 2), (29, 2), (2, 29), (29, 29)):
        lamp_post(m, x, z)
    return m


def gds_garden(variant):
    m = district_base()
    for x in range(1, SIZE - 1):
        for z in range(1, SIZE - 1):
            m.block(x, 0, z, "grass")
    trees = ((6, 6), (6, 25), (25, 25)) if variant == 0 else ((8, 16), (23, 16), (16, 8))
    for x, z in trees:
        for y in range(1, 5):
            m.block(x, y, z, "wood_log")
        m.fill(x - 2, 5, z - 2, x + 2, 6, z + 2, "tree_leaves")
    # a pond or flower beds in the middle
    if variant == 0:
        for x in range(13, 19):
            for z in range(13, 19):
                m.block(x, 0, z, "water" if 14 <= x <= 17 and 14 <= z <= 17 else "@path")
    else:
        for x in range(12, 20):
            for z in range(20, 26):
                if (x + z) % 2 == 0:
                    m.block(x, 1, z, "flora_flower")
    # benches (chairs facing the middle) and lamps
    for x, z, yaw in ((12, 12, 2), (19, 12, 2), (12, 19, 0), (19, 19, 0)):
        m.block(x, 1, z, "@wall", shape=CHAIR | yaw)
    for x, z in ((2, 16), (29, 16), (16, 2), (16, 29)):
        lamp_post(m, x, z)
    # the gardeners' cottage (a two-storey flat) in a corner
    cottage = gds_flat(2, variant, PURPLE)
    m.paste(cottage, 22 if variant == 0 else 2, 0, 2 if variant == 0 else 1)
    m.marker(10 if variant == 0 else 22, 1, 16, "greenhouse")
    return m


def gds_tower(variant):
    m = district_base()
    fp = 8
    ox = 4 if variant == 0 else SIZE - fp - 4
    oz = 4 if variant == 0 else SIZE - fp - 4
    t = Module(fp, 19, fp)
    deck(t, 0, "@floor")
    for s in range(4):
        base = s * 4
        storey_walls(t, base, "@wall", "@wall", "glass", tint=PURPLE if s % 2 == 0 else RED, door=(2, 3) if s == 0 else None)
        deck(t, base + 4, "@floor" if s < 3 else "@wall")
    for s in range(3):
        base = s * 4
        # alternating flights: west wall on even storeys, east wall on odd ones
        x = 1 if s % 2 == 0 else 6
        for i, z in enumerate((2, 3, 4)):
            t.block(x, base + 1 + i, z, "@wall", shape=STAIRS | 0)
            for y in range(base + 1, base + 1 + i):
                t.block(x, y, z, "@wall")
            t.air(x, base + 4, z)
    for x in range(fp):
        for z in range(fp):
            if x in (0, fp - 1) or z in (0, fp - 1):
                t.block(x, 17, z, "@wall", tint=RED)
    t.marker(2, 1, 0, "door_slide")
    m.paste(t, ox, 0, oz)
    m.marker(ox - 2, 1, oz + 3, "guard_post")
    m.marker(ox + fp + 1, 1, oz + 3, "guard_post")
    for x, z in ((1, 30), (30, 1)):
        lamp_post(m, x, z)
    return m
