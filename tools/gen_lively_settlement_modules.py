# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the building shells, the landmarks and the kits of the lively places (terrain generation 46).

Why: every village and town building but the greenhouse used to share ONE shell (`settlement_module_shapes.village_shell`
/ `town_shell`), so a market, a tavern, a clinic and a library were the same box with a different post inside. Here every
function gets a shell of its own, two variants each, human and alien, village- and town-style:

  village  house (front garden / porch / family house with a child's bed), market (open counter under an awning), notice
           house (a small porch), tavern (hip roof, chimney, a porch with benches), workshop (a lean-to with crates, a
           chimney), clinic (flat roof, a porch with a lantern), shop (display window, awning), armory (battlements, no
           windows), library (a corner turret), stable (a fenced yard), quarry (a walled yard with block piles), studio and
           newsroom (a mast with a light), depot (a lean-to store)
  town     the same functions on the two-storey iron body: a band in the settlement's colour round every deck, and on the
           roof a mast, a roof garden, a corner turret, a vent stack or a tank; shops and inns get an awning strip

The inside is the proven one (`village_module` / `town_module`: rooms, posts, door markers, a bed for every resident) —
only the roof, the front and what stands on and beside the body are new. Roofs, awnings and bands are `@roof` cells, so
the settlement's colour dyes them (`SettlementPalette`). A front with a porch, an awning strip or a yard moves the door
off the module's outer wall; an `entrance` marker on the -Z edge then names the road side (as the pens do).

Landmarks (function `landmark_<kind>`, at most one per settlement, placed at its heart): a mill, a bell tower, a water
tower, a light tower, a market hall, and for alien places a crystal spire; a clock tower and a glass dome for towns.

Kits: `<tier>_grown_2` / `<tier>_grid_2` (minTerrainGeneration 46). Every settlement kit from before gets
`maxTerrainGeneration` 45, so a world of the lively places draws only the new kits and an older world never does.

Appends / replaces by key only. Deterministic: running it again rewrites the same entries.

Usage:
    python tools/gen_lively_settlement_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _dump, _load  # noqa: E402
import settlement_module_shapes as shapes  # noqa: E402
from settlement_module_shapes import Module  # noqa: E402

POOL = DATA / "settlement_templates.json"
KITS = DATA / "structure_kits.json"
GENERATION = 46
FAMILY = "settlement_lively"

# Packed shapes: ShapeCode.Pack(shape, yaw) = shape << 2 | yaw; yaw 0 = +Z, 1 = -X, 2 = -Z, 3 = +X.
SLAB = 1 << 2
CYLINDER = 8 << 2
POST = 10 << 2
FENCE = 16 << 2
POT = 18 << 2
BED_FOOT = 61 << 2
BED_HEAD = 62 << 2
BENCH = 63 << 2

PROFESSIONS = ("clinic", "shop", "armory", "library", "stable", "quarry", "studio", "newsroom", "depot")
NICE = {"house": "House", "market": "Market", "board": "Notice House", "tavern": "Tavern", "workshop": "Workshop",
        "clinic": "Clinic", "shop": "Shop", "armory": "Armory", "library": "Library", "stable": "Stable", "quarry": "Quarry",
        "studio": "Studio", "newsroom": "Newsroom", "depot": "Depot"}
# The depot's keeper (the rail dealer) came after the modular set: its post marker is its job.
POSTS = dict(shapes.POSTS, depot="rail_dealer")


# ------------------------------------------------------------------ the body (the proven inside)

def village_body(function, variant, alien):
    """The classic village module of a function without its roof: floor, walls, deck, rooms, posts, door."""
    if function == "depot":
        m = shapes.village_shell(alien, 0)
        if variant == 0:
            shapes.rooms_front_back(m, 0, "door_hinge", POSTS[function], (5, 2))
        else:
            shapes.rooms_side(m, 0, "door_hinge", POSTS[function], (2, 5))
    else:
        m = shapes.village_module(function, variant, alien)
    for key in [k for k in m.cells if k[1] >= 5]:
        del m.cells[key]
    return m


def town_body(function, variant, alien, storeys=2):
    """The classic town module of a function without its roof trim."""
    if function == "depot":
        m = shapes.town_shell(alien, storeys, variant % 2)
        m.marker(4, 1, 5, "room")
        m.marker(5, 1, 3 if variant == 0 else 5, POSTS[function])
        m.marker(4, 5, 5, "room")
    else:
        m = shapes.town_module(function, variant, alien, storeys)
    top = storeys * 4
    for key in [k for k in m.cells if k[1] > top]:
        del m.cells[key]
    return m


# ------------------------------------------------------------------ roofs (on a body at x0..x0+7, z0..z0+7, deck at `top`)

def roof_gable_x(m, z0, top=4):
    m.fill(0, top + 1, z0 + 1, 7, top + 1, z0 + 6, "@roof")
    m.fill(0, top + 2, z0 + 3, 7, top + 2, z0 + 4, "@roof")


def roof_gable_z(m, z0, top=4):
    m.fill(1, top + 1, z0, 6, top + 1, z0 + 7, "@roof")
    m.fill(3, top + 2, z0, 4, top + 2, z0 + 7, "@roof")


def roof_hip(m, z0, top=4):
    m.fill(1, top + 1, z0 + 1, 6, top + 1, z0 + 6, "@roof")
    m.fill(2, top + 2, z0 + 2, 5, top + 2, z0 + 5, "@roof")


def roof_shed(m, z0, top=4):
    """A mono-pitch: low at the front, a full row at the back."""
    for x in range(8):
        for z in range(z0, z0 + 4):
            m.block(x, top + 1, z, "@roof", shape=SLAB)
    m.fill(0, top + 1, z0 + 4, 7, top + 1, z0 + 7, "@roof")


def roof_parapet(m, z0, top=4, battlements=False):
    for x in range(8):
        for z in range(z0, z0 + 8):
            if x in (0, 7) or z in (z0, z0 + 7):
                if not battlements or (x + z) % 2 == 0:
                    m.block(x, top + 1, z, "@wall")
    if battlements:
        for x, z in ((0, z0), (0, z0 + 7), (7, z0), (7, z0 + 7)):
            m.block(x, top + 1, z, "@wall")
            m.block(x, top + 2, z, "@wall")


def roof_alien(m, z0, top=4, crown=True):
    """An alien roof: spires on the corners and a crown."""
    for x, z in ((0, z0), (0, z0 + 7), (7, z0), (7, z0 + 7)):
        m.block(x, top + 1, z, "@roof")
        m.block(x, top + 2, z, "@roof")
    m.fill(2, top + 1, z0 + 2, 5, top + 1, z0 + 5, "@roof")
    if crown:
        m.fill(3, top + 2, z0 + 3, 4, top + 2, z0 + 4, "@roof")


def roof_alien_rim(m, z0, top=4):
    """An alien flat roof: a toothed rim of crystal and a block of it in the middle."""
    for x in range(8):
        for z in range(z0, z0 + 8):
            if (x in (0, 7) or z in (z0, z0 + 7)) and (x + z) % 2 == 0:
                m.block(x, top + 1, z, "@roof")
    m.fill(3, top + 1, z0 + 3, 4, top + 2, z0 + 4, "@roof")


def chimney(m, x, z, top, high=2, block="stone"):
    """A stack on the roof with a `chimney` marker above it (the client's smoke)."""
    base = max((y for (cx, y, cz) in m.cells if cx == x and cz == z), default=top) + 1
    for y in range(base, base + high):
        m.block(x, y, z, block)
    m.marker(x, base + high, z, "chimney")


def turret(m, x0, z0, top, high=3):
    """A 2 x 2 turret on a corner of the roof, capped in the settlement's colour."""
    for x in (x0, x0 + 1):
        for z in (z0, z0 + 1):
            for y in range(top + 1, top + 1 + high):
                m.block(x, y, z, "@wall")
            m.block(x, top + 1 + high, z, "@roof")


def mast(m, x, z, top, high=3):
    """A slim mast with a lantern on top (a studio's and a newsroom's sign)."""
    base = max((y for (cx, y, cz) in m.cells if cx == x and cz == z), default=top) + 1
    for y in range(base, base + high):
        m.block(x, y, z, "@accent", shape=POST)
    m.block(x, base + high, z, "lantern")


# ------------------------------------------------------------------ fronts (the strip of the lot before the door)

def with_front(body, depth, side=0):
    """A module with the body set `depth` cells back from the road (and `side` free columns on its +X side)."""
    m = Module(body.w + side, body.h + 6, body.l + depth)
    m.paste(body, 0, 0, depth)
    if depth > 0:
        m.marker(2, 1, 0, "entrance")  # the road side: the door stands `depth` cells in
    return m


def awning_strip(m, depth=1):
    """A paved strip before the door under an awning in the settlement's colour (the door is three high)."""
    for z in range(depth):
        for x in range(8):
            m.block(x, 0, z, "@path")
            m.block(x, 4, z, "@roof", shape=SLAB)


def porch(m, alien, benches=False, lantern=False):
    """A porch two deep: a floor, two posts at the road, a roof in the settlement's colour."""
    post = "@accent" if alien else "wood_log"
    for z in (0, 1):
        for x in range(8):
            m.block(x, 0, z, "@floor")
            m.block(x, 4, z, "@roof", shape=SLAB)
    for x in (0, 7):
        for y in (1, 2, 3):
            m.block(x, y, 0, post, shape=POST)
    if benches:
        m.block(6, 1, 1, "wood_planks", shape=BENCH | 0)  # the backrest to the wall; the door's lane (x 1..4) stays free
        m.block(7, 1, 1, "wood_planks", shape=BENCH | 0)
    if lantern:
        m.block(6, 1, 0, "lantern")


def yard(m, alien, depth=3, flowers=True, piles=None, fence=None):
    """A fenced front yard with a gate before the door; flowers, or a quarry's block piles."""
    fence = fence or ("@accent" if alien else "wood_log")
    for x in range(8):
        if x not in (2, 3):
            m.block(x, 1, 0, fence, shape=FENCE | 0)
    for z in range(1, depth):
        m.block(0, 1, z, fence, shape=FENCE | 1)
        m.block(7, 1, z, fence, shape=FENCE | 1)
    if piles:
        for i, (x, z) in enumerate(((5, 1), (6, 1), (6, 2), (5, 2))):
            if z < depth:
                m.block(x, 1, z, piles[i % len(piles)])
        if depth > 1:
            m.block(6, 2, 1, piles[0])
    elif flowers:
        flower = "flora_crystal" if alien else "flora_marigold"
        for x, z in ((5, 1), (6, 1), (6, 2)):  # beside the gate's lane (x 1..4), never in it
            if z < depth:
                m.block(x, 0, z, "dirt")
                m.block(x, 1, z, flower)


def lean_to(m, alien, depth, crates=True):
    """A lean-to on the +X side (3 wide): a paved floor, two posts, a slab roof, crates and a barrel."""
    post = "@accent" if alien else "wood_log"
    z0, z1 = depth + 2, depth + 6
    for x in (8, 9, 10):
        for z in range(z0, z1 + 1):
            m.block(x, 0, z, "@path")
            m.block(x, 4, z, "@roof", shape=SLAB)
    for z in (z0, z1):
        for y in (1, 2, 3):
            m.block(10, y, z, post, shape=POST)
    if crates:
        m.block(8, 1, z1, "wood_crate")
        m.block(9, 1, z1, "wood_crate")
        m.block(8, 2, z1, "wood_crate")
        m.block(8, 1, z0, "wood_planks", shape=CYLINDER)  # a barrel


def open_counter(m, depth):
    """The front wall right of the door opens above a counter (the market's hatch)."""
    for x in (4, 5, 6):
        m.air(x, 2, depth)


def display_window(m, depth):
    """A shop window right of the door."""
    for x in (5, 6):
        for y in (1, 2):
            m.block(x, y, depth, "glass")


def no_windows(m):
    """An armory keeps its windows shut: every pane becomes wall."""
    for key, c in list(m.cells.items()):
        if c.get("kind") == "block" and c["id"] == "glass":
            m.block(key[0], key[1], key[2], "@wall")


def child_bed(m, depth):
    """A child's bed along the back wall of the main room, a `child` marker beside it (its resident is a child)."""
    z = depth + 6
    m.block(3, 1, z, "bed", shape=BED_HEAD | 3)  # head at x = 3, foot toward +X
    m.block(4, 1, z, "bed", shape=BED_FOOT | 3)
    m.marker(4, 1, z - 1, "child")  # (3, 1, z - 1) is the room's own marker


def trim(m):
    """Cuts the module's height to what it uses and returns it."""
    m.h = m.height_used()
    return m


# ------------------------------------------------------------------ the village shells

def village_module(function, variant, alien):
    def roofed(m, z0, kind):
        if alien and kind in ("flat", "battlements"):
            roof_alien_rim(m, z0)
        elif alien:
            roof_alien(m, z0, crown=kind in ("gable_x", "hip"))
        elif kind == "gable_x":
            roof_gable_x(m, z0)
        elif kind == "gable_z":
            roof_gable_z(m, z0)
        elif kind == "hip":
            roof_hip(m, z0)
        elif kind == "shed":
            roof_shed(m, z0)
        elif kind == "battlements":
            roof_parapet(m, z0, battlements=True)
        else:
            roof_parapet(m, z0)

    v = variant
    if function == "house":
        if v == 0:  # a cottage behind a front garden
            m = with_front(village_body("house", 0, alien), 3)
            yard(m, alien)
            roofed(m, 3, "gable_z")
        elif v == 1:  # a house with a porch
            m = with_front(village_body("house", 1, alien), 2)
            porch(m, alien)
            roofed(m, 2, "hip")
        else:  # the family house: a chimney and a child's bed
            m = with_front(village_body("house", 2, alien), 0)
            roofed(m, 0, "gable_x")
            child_bed(m, 0)
            if not alien:
                chimney(m, 6, 5, 4)
    elif function == "market":
        depth = 1 if v == 0 else 2
        m = with_front(village_body("market", v, alien), depth)
        if v == 0:
            awning_strip(m)
        else:
            porch(m, alien)
            m.block(6, 1, 1, "wood_crate")
        open_counter(m, depth)
        roofed(m, depth, "shed" if v == 0 else "gable_x")
    elif function == "board":
        depth = 2 if v == 0 else 1
        m = with_front(village_body("board", v, alien), depth)
        if v == 0:
            porch(m, alien)
        else:
            awning_strip(m)
        roofed(m, depth, "gable_z" if v == 0 else "hip")
    elif function == "tavern":
        depth = 2 if v == 0 else 1
        m = with_front(village_body("tavern", v, alien), depth)
        if v == 0:
            porch(m, alien, benches=True)
        else:
            awning_strip(m)
        roofed(m, depth, "hip" if v == 0 else "gable_x")
        chimney(m, 6, depth + 5, 4, high=3, block="@accent" if alien else "stone")  # an alien hearth vents through crystal
    elif function == "workshop":
        m = with_front(village_body("workshop", v, alien), 0, side=3 if v == 0 else 0)
        if v == 0:
            lean_to(m, alien, 0)
        roofed(m, 0, "gable_x" if v == 0 else "shed")
        chimney(m, 1, 6, 4, high=3, block="@accent" if alien else "stone")
    elif function == "clinic":
        depth = 2 if v == 0 else 1
        m = with_front(village_body("clinic", 0, alien), depth)
        if v == 0:
            porch(m, alien, lantern=True)
        else:
            awning_strip(m)
        roofed(m, depth, "flat")
    elif function == "shop":
        depth = 1 if v == 0 else 2
        m = with_front(village_body("shop", 0, alien), depth)
        if v == 0:
            awning_strip(m)
        else:
            porch(m, alien)
            m.block(6, 1, 1, "wood_planks", shape=CYLINDER)
        display_window(m, depth)
        roofed(m, depth, "gable_x" if v == 0 else "gable_z")
    elif function == "armory":
        m = with_front(village_body("armory", 0, alien), 0)
        no_windows(m)
        roofed(m, 0, "battlements")
        if v == 1:
            turret(m, 6, 6, 4, high=2)
    elif function == "library":
        m = with_front(village_body("library", 0, alien), 0)
        roofed(m, 0, "gable_z" if v == 0 else "hip")
        if v == 0:
            turret(m, 6, 6, 4, high=3)
        else:
            turret(m, 3, 3, 4, high=2)
    elif function == "stable":
        m = with_front(village_body("stable", 0, alien), 3, side=3 if v == 1 else 0)
        yard(m, alien, flowers=False)
        m.block(5, 1, 1, "flora_cropgrain")  # a little fodder
        if v == 1:
            lean_to(m, alien, 3, crates=False)
        roofed(m, 3, "gable_x" if v == 0 else "shed")
    elif function == "quarry":
        m = with_front(village_body("quarry", 0, alien), 3, side=3 if v == 1 else 0)
        yard(m, alien, piles=("stone", "sandstone", "basalt"))
        if v == 1:
            lean_to(m, alien, 3)
        roofed(m, 3, "flat" if v == 0 else "shed")
    elif function in ("studio", "newsroom"):
        depth = 0 if v == 0 else 1
        m = with_front(village_body(function, 0, alien), depth)
        if v == 1:
            awning_strip(m)
        roofed(m, depth, "flat" if function == "studio" else "gable_x")
        mast(m, 5 if function == "studio" else 1, depth + 5, 4, high=3 if v == 0 else 2)
        if function == "studio":
            display_window(m, depth)
    elif function == "depot":
        m = with_front(village_body("depot", v, alien), 1 if v == 1 else 0, side=3)
        lean_to(m, alien, 1 if v == 1 else 0)
        if v == 1:
            awning_strip(m)
        roofed(m, 1 if v == 1 else 0, "shed" if v == 0 else "gable_x")
    else:
        raise ValueError(function)
    return trim(m)


# ------------------------------------------------------------------ the town shells

def colour_bands(m, z0, storeys=2):
    """The outer ring of every deck in the settlement's colour: a band round the building per storey."""
    for s in range(1, storeys + 1):
        y = s * 4
        for x in range(8):
            for z in range(z0, z0 + 8):
                if (x in (0, 7) or z in (z0, z0 + 7)) and (x, y, z) in m.cells and m.cells[(x, y, z)].get("kind") == "block":
                    m.block(x, y, z, "@roof")


def roof_garden(m, z0, top, alien):
    fence = "@accent"
    for x in range(8):
        for z in range(z0, z0 + 8):
            if x in (0, 7) or z in (z0, z0 + 7):
                m.block(x, top + 1, z, fence, shape=FENCE | (0 if z in (z0, z0 + 7) else 1))
    for x, z in ((2, z0 + 2), (5, z0 + 5), (5, z0 + 2), (2, z0 + 5)):
        m.block(x, top + 1, z, "flora_crystal" if alien else "flora_marigold")


def roof_tank(m, x, z, top):
    """A tank on the roof: two barrels of the wall's material."""
    for y in (top + 1, top + 2):
        m.block(x, y, z, "@wall", shape=CYLINDER)
        m.block(x + 1, y, z, "@wall", shape=CYLINDER)


def parapet_trim(m, z0, top, alien):
    for x in range(8):
        for z in range(z0, z0 + 8):
            if (x in (0, 7) or z in (z0, z0 + 7)) and (x + z) % 2 == 0:
                m.block(x, top + 1, z, "@accent" if alien else "@wall")


def town_module(function, variant, alien, storeys=2):
    v = variant
    top = storeys * 4
    front = None
    if v == 0 and function in ("market", "shop", "tavern", "board", "clinic", "depot", "newsroom"):
        front = "strip"
    elif v == 0 and function == "stable":
        front = "yard"
    elif v == 0 and function == "quarry":
        front = "piles"
    depth = {None: 0, "strip": 1, "yard": 3, "piles": 3}[front]
    m = with_front(town_body(function, v if function in ("house", "market", "board", "tavern", "workshop", "depot") else 0, alien, storeys), depth)
    if front == "strip":
        awning_strip(m)
    elif front == "yard":
        yard(m, alien, flowers=False, fence="@accent")
    elif front == "piles":
        yard(m, alien, piles=("stone", "basalt", "sandstone"), fence="@accent")
    colour_bands(m, depth, storeys)
    if function == "house":
        if v == 0:
            roof_garden(m, depth, top, alien)
        else:
            parapet_trim(m, depth, top, alien)
            roof_tank(m, 5, depth + 5, top)
    elif function == "market":
        open_counter(m, depth)  # the market's hatch
        parapet_trim(m, depth, top, alien)
        if v == 1:
            roof_garden(m, depth, top, alien)
    elif function == "shop":
        display_window(m, depth)
        parapet_trim(m, depth, top, alien)
        if v == 1:
            roof_garden(m, depth, top, alien)
    elif function in ("tavern", "workshop"):
        parapet_trim(m, depth, top, alien)
        chimney(m, 6, depth + 5, top, high=2 if function == "tavern" else 3, block="iron_wall")
    elif function == "studio":
        display_window(m, depth)  # the studio's big window
        parapet_trim(m, depth, top, alien)
        mast(m, 5, depth + 5, top, high=3 if v == 0 else 2)
    elif function == "newsroom":
        parapet_trim(m, depth, top, alien)
        mast(m, 1, depth + 1, top, high=4 if v == 0 else 3)
    elif function in ("library", "armory"):
        if function == "armory":
            no_windows(m)
        parapet_trim(m, depth, top, alien)
        turret(m, 6 if v == 0 else 0, depth + 6, top, high=2)
    elif function == "clinic":
        parapet_trim(m, depth, top, alien)
        m.block(3, top + 1, depth + 3, "light_white")  # a beacon: the clinic is found at night
    elif function == "depot":
        parapet_trim(m, depth, top, alien)
        roof_tank(m, 1, depth + 5, top)
        roof_tank(m, 4, depth + 5, top)
    elif function == "stable":
        parapet_trim(m, depth, top, alien)
        if v == 1:
            roof_tank(m, 1, depth + 5, top)
    elif function == "quarry":
        parapet_trim(m, depth, top, alien)
        if v == 1:
            roof_tank(m, 5, depth + 1, top)
            turret(m, 0, depth + 6, top, high=1)
    else:  # board
        parapet_trim(m, depth, top, alien)
    return trim(m)


# ------------------------------------------------------------------ landmarks (one per settlement)

def landmark_mill(alien):
    """A mill: a tapering tower with four sails."""
    m = Module(7, 14, 7)
    wall = "@wall"
    m.fill(1, 0, 1, 5, 0, 5, "@floor")
    for y in range(1, 8):
        inset = 1 if y < 5 else 2
        for x in range(inset, 7 - inset):
            for z in range(inset, 7 - inset):
                if x in (inset, 6 - inset) or z in (inset, 6 - inset):
                    m.block(x, y, z, wall)
    for x in (3,):  # the doorway on -Z, one wide and two high (nobody lives here)
        m.air(x, 1, 1)
        m.air(x, 2, 1)
    m.fill(2, 8, 2, 4, 8, 4, "@roof")
    m.block(3, 9, 3, "@roof")
    # the sails on the -Z face: a hub and four arms of fence rails
    m.block(3, 6, 0, "@accent")
    for d in (1, 2):
        m.block(3 - d, 6, 0, "wood_planks", shape=FENCE | 0)
        m.block(3 + d, 6, 0, "wood_planks", shape=FENCE | 0)
    for dy in (1, 2):
        m.block(3, 6 + dy, 0, "wood_planks", shape=POST)
        m.block(3, 6 - dy, 0, "wood_planks", shape=POST)
    m.marker(3, 1, 0, "entrance")
    return trim(m)


def landmark_bell(alien):
    """A bell tower: four posts, a platform, a bell under a little roof."""
    m = Module(5, 13, 5)
    post = "@accent" if alien else "wood_log"
    m.fill(0, 0, 0, 4, 0, 4, "@path")
    for x in (0, 4):
        for z in (0, 4):
            for y in range(1, 9):
                m.block(x, y, z, post)
    m.fill(0, 6, 0, 4, 6, 4, "@wall")
    m.block(2, 6, 2, "@wall")
    m.block(2, 8, 2, "@accent")  # the bell
    m.fill(0, 9, 0, 4, 9, 4, "@roof")
    m.fill(1, 10, 1, 3, 10, 3, "@roof")
    m.block(2, 11, 2, "@roof")
    m.marker(2, 1, 0, "entrance")
    return trim(m)


def landmark_water_tower(alien):
    """A water tower: a tank on four legs."""
    m = Module(5, 12, 5)
    leg = "@accent" if alien else "wood_log"
    for x in (0, 4):
        for z in (0, 4):
            for y in range(0, 6):
                m.block(x, y, z, leg, shape=POST if y > 0 else 0)
    m.fill(0, 6, 0, 4, 6, 4, "@wall")
    for y in (7, 8):
        for x in range(5):
            for z in range(5):
                if x in (0, 4) or z in (0, 4):
                    m.block(x, y, z, "@roof")
                else:
                    m.block(x, y, z, "water")
    m.fill(0, 9, 0, 4, 9, 4, "@roof")
    m.marker(2, 1, 0, "entrance")
    return trim(m)


def landmark_light(alien):
    """A light tower: a slim tower with a lit lamp room on top, seen from far at night."""
    m = Module(5, 15, 5)
    m.fill(0, 0, 0, 4, 0, 4, "@floor")
    for y in range(1, 10):
        for x in range(1, 4):
            for z in range(1, 4):
                if x in (1, 3) or z in (1, 3):
                    m.block(x, y, z, "@wall")
    m.air(2, 1, 1)
    m.air(2, 2, 1)
    m.fill(0, 10, 0, 4, 10, 4, "@roof")
    for x in range(5):
        for z in range(5):
            if x in (0, 4) or z in (0, 4):
                m.block(x, 11, z, "glass")
    m.block(2, 11, 2, "light_white")
    m.fill(0, 12, 0, 4, 12, 4, "@roof")
    m.block(2, 13, 2, "@roof")
    m.marker(2, 1, 0, "entrance")
    return trim(m)


def landmark_hall(alien):
    """A market hall: an open colonnade under a big roof in the settlement's colour, stalls inside."""
    m = Module(9, 8, 9)
    post = "@accent" if alien else "wood_log"
    m.fill(0, 0, 0, 8, 0, 8, "@path")
    for x in (0, 4, 8):
        for z in (0, 4, 8):
            if (x, z) != (4, 4):
                for y in (1, 2, 3, 4):
                    m.block(x, y, z, post)
    m.fill(0, 5, 0, 8, 5, 8, "@roof")
    m.fill(1, 6, 1, 7, 6, 7, "@roof")
    m.fill(3, 7, 3, 5, 7, 5, "@roof")
    for x, z in ((2, 6), (6, 6)):
        m.block(x, 1, z, "wood_planks", shape=shapes.TABLE)
        m.block(x + 1 if x < 4 else x - 1, 1, z, "wood_crate")
        m.marker(x, 1, z - 1, "stall")
    m.marker(4, 1, 0, "entrance")
    return trim(m)


def landmark_obelisk(alien):
    """A crystal spire on a stepped base (alien places)."""
    m = Module(5, 13, 5)
    m.fill(0, 0, 0, 4, 0, 4, "@path")
    m.fill(1, 1, 1, 3, 1, 3, "@wall")
    for y in range(2, 10):
        m.block(2, y, 2, "crystal")
    m.block(2, 10, 2, "crystal", shape=(2 << 2))  # a pyramid tip
    for x, z in ((1, 1), (3, 3), (1, 3), (3, 1)):
        m.block(x, 2, z, "crystal")
    m.marker(2, 1, 0, "entrance")
    return trim(m)


def landmark_clock(alien):
    """A town's clock tower: an iron tower with a glass face on every side and a coloured cap."""
    m = Module(5, 16, 5)
    m.fill(0, 0, 0, 4, 0, 4, "@floor")
    for y in range(1, 12):
        for x in range(1, 4):
            for z in range(1, 4):
                if x in (1, 3) or z in (1, 3):
                    face = y in (9, 10) and (x == 2 or z == 2)
                    m.block(x, y, z, "glass" if face else "@wall")
    m.air(2, 1, 1)
    m.air(2, 2, 1)
    m.block(2, 9, 2, "light_white")
    m.fill(0, 12, 0, 4, 12, 4, "@roof")
    m.fill(1, 13, 1, 3, 13, 3, "@roof")
    m.block(2, 14, 2, "@accent", shape=POST)
    m.marker(2, 1, 0, "entrance")
    return trim(m)


def landmark_dome(alien):
    """A town's glass dome over a little garden."""
    m = Module(9, 8, 9)
    m.fill(0, 0, 0, 8, 0, 8, "@floor")
    for x in range(9):
        for z in range(9):
            edge = x in (0, 8) or z in (0, 8)
            if edge:
                m.block(x, 1, z, "@roof")
                for y in (2, 3):
                    m.block(x, y, z, "glass")
    for x in (3, 4, 5):
        for y in (1, 2, 3):
            m.air(x, y, 0)  # the entrance, three wide
    for x in range(1, 8):
        for z in range(1, 8):
            m.block(x, 4, z, "glass")
    for x in range(2, 7):
        for z in range(2, 7):
            m.block(x, 5, z, "glass")
            m.air(x, 4, z)
    for x, z in ((2, 2), (6, 6), (2, 6), (6, 2), (4, 4)):
        m.block(x, 1, z, "flora_crystal" if alien else "flora_marigold")
    m.block(4, 1, 6, "@wall", shape=BENCH | 0)
    m.marker(4, 1, 5, "bench")
    m.marker(4, 1, 0, "entrance")
    return trim(m)


LANDMARKS = (
    # kind, builder, name, styles (village / town), who (human / alien / both)
    ("mill", landmark_mill, "Mill", ("village",), "human"),
    ("bell", landmark_bell, "Bell Tower", ("village", "town"), "both"),
    ("water_tower", landmark_water_tower, "Water Tower", ("village", "town"), "human"),
    ("light", landmark_light, "Light Tower", ("village", "town"), "both"),
    ("hall", landmark_hall, "Market Hall", ("village", "town"), "both"),
    ("obelisk", landmark_obelisk, "Crystal Spire", ("village", "town"), "alien"),
    ("clock", landmark_clock, "Clock Tower", ("town",), "human"),
    ("dome", landmark_dome, "Glass Dome", ("town",), "both"),
)


# ------------------------------------------------------------------ entries

def compact_body(m):
    """The module's cells as the compact body (TemplateBody, #3485): a palette of looks and one run-length string per
    (y, z) row — "y,z:x.count.palette x.count.palette", rows by y then z, runs by x. Markers stay listed as cells. About a
    tenth of the size of one JSON object per cell; the content loader expands it into cells again."""
    palette, index, rows, markers = [], {}, [], []
    by_row = {}
    for (x, y, z), c in m.cells.items():
        if c.get("kind") == "marker":
            markers.append(c)
        else:
            by_row.setdefault((y, z), []).append((x, c))
    for (y, z) in sorted(by_row):
        runs = []
        for x, c in sorted(by_row[(y, z)], key=lambda p: p[0]):
            look = (c["id"], c.get("tint", 0), c.get("shape", 0))
            if look not in index:
                index[look] = len(palette)
                entry_ = {"id": c["id"]}
                if look[1]:
                    entry_["tint"] = look[1]
                if look[2]:
                    entry_["shape"] = look[2]
                palette.append(entry_)
            p = index[look]
            if runs and runs[-1][2] == p and runs[-1][0] + runs[-1][1] == x:
                runs[-1][1] += 1
            else:
                runs.append([x, 1, p])
        rows.append(f"{y},{z}:" + " ".join(f"{x}.{n}.{p}" for x, n, p in runs))
    markers.sort(key=lambda c: (c["x"], c["y"], c["z"]))
    return palette, rows, markers


def module_entry(key, name, tier, function, m, alien):
    palette, rows, markers = compact_body(m)
    out = {
        "key": key,
        "name": name,
        "tier": tier,
        "kind": "settlement",
        "role": "",  # never a slot of the classic plot grid: the planned kits name these by key
        "kit": FAMILY,
        "function": function,
        "pack": "default",
        "weight": 1,
        "minTerrainGeneration": GENERATION,
        "width": m.w,
        "height": m.height_used(),
        "length": m.l,
        "palette": palette,
        "rows": rows,
        "cells": markers,
    }
    if alien:
        out["style"] = "alien"
    return out


VILLAGE_VARIANTS = {"house": 3}
FUNCTIONS = ("house", "market", "board", "tavern", "workshop") + PROFESSIONS


def lively_modules():
    out = []
    for suffix, alien in (("", False), ("_alien", True)):
        prefix = "Alien " if alien else ""
        for function in FUNCTIONS:
            for v in range(VILLAGE_VARIANTS.get(function, 2)):
                out.append(module_entry(f"lively_village_{function}_{v + 1}{suffix}", f"{prefix}Village {NICE[function]} {v + 1} (lively)",
                                        "village", function, village_module(function, v, alien), alien))
            for v in range(2):
                out.append(module_entry(f"lively_town_{function}_{v + 1}{suffix}", f"{prefix}Town {NICE[function]} {v + 1} (lively)",
                                        "town", function, town_module(function, v, alien), alien))
        out.append(module_entry(f"lively_city_tall_house_1{suffix}", f"{prefix}City Tall House 1 (lively)", "city", "house",
                                town_module("house", 1, alien, storeys=3), alien))
        for kind, builder, nice, styles, who in LANDMARKS:
            if who == ("alien" if not alien else "human"):
                continue
            for style in styles:
                out.append(module_entry(f"lively_{style}_landmark_{kind}{suffix}", f"{prefix}{nice} ({style})", style,
                                        f"landmark_{kind}", builder(alien), alien))
    return out


def entry(module, weight=1, max_=1, required=False):
    return {"module": module, "min": 1 if required else 0, "max": max_, "required": required, "weight": weight}


def pair(key, **kw):
    return [entry(key, **kw), entry(key + "_alien", **kw)]


def lively_entries(tier, have):
    """The entries of a lively kit: the new shells, the farms and greenhouses of the grown package, the landmarks."""
    style = "village" if tier in ("hamlet", "village") else "town"
    lv = f"lively_{style}"
    e = []
    e += pair(f"{lv}_market_1", required=True)
    e += pair(f"{lv}_market_2")
    e += pair(f"{lv}_board_1", required=True)
    e += pair(f"{lv}_board_2")
    if tier != "hamlet":
        e += pair(f"{lv}_tavern_1", required=True)
        e += pair(f"{lv}_tavern_2")
    e += pair(f"{lv}_workshop_1", required=tier in ("town", "city"))
    e += pair(f"{lv}_workshop_2")
    for n in ((1, 2, 3) if style == "village" else (1, 2)):
        e += pair(f"{lv}_house_{n}", max_=30, weight=3 if n == 1 else 2)
    if tier == "city":
        e += pair("lively_city_tall_house_1", max_=30, weight=3)
    e += pair(f"{style}_greenhouse_1", max_=2)
    e += pair(f"{style}_field_1", max_=2, weight=2, required=tier != "city")
    e += pair(f"{style}_field_2", max_=1)
    e += pair(f"{style}_garden_1", max_=2)
    e += pair(f"{style}_garden_2", max_=1)
    e += pair(f"{style}_pen_{1 if tier != 'town' else 2}", max_=1, weight=2, required=tier in ("village", "town"))
    for function in PROFESSIONS:
        e += pair(f"{lv}_{function}_1", max_=1)
        if tier in ("town", "city"):
            e += pair(f"{lv}_{function}_2", max_=1)
    for kind, _builder, _nice, styles, who in LANDMARKS:
        if style in styles:
            if who != "alien":
                e.append(entry(f"lively_{style}_landmark_{kind}"))
            if who != "human":
                e.append(entry(f"lively_{style}_landmark_{kind}_alien"))
    missing = [x["module"] for x in e if x["module"] not in have]
    if missing:
        raise SystemExit(f"{tier}: kit entries name modules that do not exist: {missing}")
    return e


def lively_kits(have):
    forms = {
        "hamlet": ["street", "cluster", "round", "bank"],
        "village": ["street", "green", "round", "cluster", "cross", "bank"],
        "town": ["street", "green", "cluster", "cross", "bank"],
        "city": ["street", "cluster", "cross", "bank"],
    }
    kits = []
    for tier in ("hamlet", "village", "town", "city"):
        for layout, weight in (("grown", 8), ("grid", 5)):
            kit = {
                "key": f"{tier}_{layout}_2",
                "name": f"{tier.capitalize()} {'Grown' if layout == 'grown' else 'Grown Grid'} 2 (lively)",
                "kind": "settlement",
                "tier": tier,
                "pack": "default",
                "weight": weight,
                "minTerrainGeneration": GENERATION,
                "layout": layout,
                "entries": lively_entries(tier, have),
            }
            if layout == "grown":
                kit["forms"] = forms[tier]
            kits.append(kit)
    return kits


def upsert(pool, new):
    index = next((i for i, t in enumerate(pool) if t.get("key") == new["key"]), None)
    if index is not None:
        pool[index] = new
    else:
        pool.append(new)


def dump_lf(path, obj):
    """merge_structure._dump, with LF line endings on every platform (the repository's convention)."""
    _dump(path, obj)
    path.write_bytes(path.read_bytes().replace(b"\r\n", b"\n"))


def main():
    pool = _load(POOL)
    for m in lively_modules():
        upsert(pool, m)
        print(f"{m['key']}: {m['width']}x{m['height']}x{m['length']}, {len(m['rows'])} rows, {len(m['cells'])} markers")
    dump_lf(POOL, pool)

    have = {t["key"] for t in pool}
    kits = _load(KITS)
    lively = lively_kits(have)
    lively_keys = {k["key"] for k in lively}
    capped = 0
    for kit in kits:
        # Every settlement kit from before the lively places ends at generation 45 (a pin-only kit is never drawn anyway).
        # A civilization's kits are drawn for its own sites, an underground kit by the underground stamper: both stay.
        if kit.get("kind", "station") == "settlement" and kit["key"] not in lively_keys and not kit.get("underground") \
                and not kit.get("pinOnly") and not kit.get("civilization") and kit.get("maxTerrainGeneration") != GENERATION - 1:
            kit["maxTerrainGeneration"] = GENERATION - 1
            capped += 1
    for kit in lively:
        upsert(kits, kit)
        print(f"{kit['key']}: {kit['kind']} {kit['tier']}, {len(kit['entries'])} entries")
    dump_lf(KITS, kits)
    print(f"{capped} older settlement kit(s) now end at generation {GENERATION - 1}.")


if __name__ == "__main__":
    main()
