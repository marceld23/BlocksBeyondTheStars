# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the buried G.D.S. ruin (#3334) into the shipped pools: ten district modules `gds_ruin_*` in
data/settlement_templates.json and the underground city kit `ruin_gds_1` in data/structure_kits.json.

Justus' player report (2026-11-01, "Alte Städte"): old metal cities under the ground, purple and red, flooded with the
glowing white liquid Ressaw. The server composes a ruin from this kit with the ordinary city composer (a 3 x 3 grid of
24 x 24 districts, 88 blocks a side) and then makes it a ruin: it drops every living marker, breaks walls, collapses
spots, sinks every Ressaw cell at floor level into a flooded basin with Ressaw crystal ore under it, and scatters the
remains (see docs/developer/WORLD_GENERATION.md, "Generation 35").

The modules are plain editor templates, so they stay editable in the structure editor (Town editor, "Use as" a city
district, kit `ruin_gds_1`, the "Underground" toggle on). Every module carries `kit: ruin_gds_1` AND `underground: true`:
the surface pools (`SettlementModulesFor`) drop them, so the G.D.S. city on the surface never draws a ruin district.

Conventions the ruin stamper relies on:
- Ressaw (`ressaw`) authored at y = 0 replaces the floor: the stamper sinks that column into a basin three deep.
  Ressaw above the floor stands only inside a closed glass vat (it is still and never flows).
- `ressaw_block` cells in the walls glow (and crack when a player lingers, #3336).
- `loot` markers become the ruin's crates (`gds_cache:<n>`); put them on dry floor, a few on high galleries.
- No `npc`, vendor, board or guard markers: the ruin is abandoned. Doorways are plain gaps (door markers only where a
  copied G.D.S. flat brings its own — the stamper ignores them).

Deterministic: running it again rewrites the same entries (matched by key); everything else in both pools stays.

Usage:
    python tools/gen_ancient_ruin_modules.py
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import settlement_module_shapes as shapes
from merge_structure import DATA, _load
from settlement_module_shapes import PURPLE, RED, STAIRS, TABLE, Module

POOL = DATA / "settlement_templates.json"
KITS = DATA / "structure_kits.json"

KIT = "ruin_gds_1"
SIZE = 24  # the district side of the kit
HEIGHT = 11  # the module envelope: the kit's structure height (12) minus one row
WALL = "iron_wall"
FLOOR = "steel_floor"
RESSAW = "ressaw"
GLOW = "ressaw_block"
LIGHT = "strip_light_warm"
PIPE = "factory_pipe"
RUBBLE = ("scrap_metal", "rusted_panel")
LIVING_MARKERS = {"npc", "vendor", "mission_board", "guard_post", "tavern", "workshop", "greenhouse"}


# ------------------------------------------------------------------ helpers

def district(floor="@path"):
    m = Module(SIZE, HEIGHT, SIZE)
    m.fill(0, 0, 0, SIZE - 1, 0, SIZE - 1, floor)
    return m


def walls(m, x0, z0, w, l, top, tint, bands=(3,), doors=(), windows=False):
    """Hollow walls y = 1 .. top around the box (x0, z0, w, l), red at the `bands` rows. A door is (side, offset): a gap
    two wide and three tall on side n (-Z), s (+Z), w (-X) or e (+X), `offset` cells along that wall."""
    gaps = set()
    for side, off in doors:
        for d in (0, 1):
            for y in (1, 2, 3):
                gaps.add({"n": (x0 + off + d, y, z0), "s": (x0 + off + d, y, z0 + l - 1),
                          "w": (x0, y, z0 + off + d), "e": (x0 + w - 1, y, z0 + off + d)}[side])
    for y in range(1, top + 1):
        for x in range(x0, x0 + w):
            for z in range(z0, z0 + l):
                if not (x in (x0, x0 + w - 1) or z in (z0, z0 + l - 1)) or (x, y, z) in gaps:
                    continue
                corner = x in (x0, x0 + w - 1) and z in (z0, z0 + l - 1)
                if windows and y == 2 and not corner and (x + z) % 4 == 0:
                    m.block(x, y, z, "glass")
                else:
                    m.block(x, y, z, WALL, tint=RED if y in bands else tint)


def roof(m, x0, z0, w, l, y, tint, hole=None):
    """A flat roof at row y; `hole` (x0, z0, x1, z1) is where it fell in."""
    for x in range(x0, x0 + w):
        for z in range(z0, z0 + l):
            if hole and hole[0] <= x <= hole[2] and hole[1] <= z <= hole[3]:
                continue
            m.block(x, y, z, WALL, tint=tint)


def pool(m, x0, z0, x1, z1):
    """Ressaw at floor level — the stamper sinks every such column into a flooded basin."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            m.block(x, 0, z, RESSAW)


def vat(m, x0, z0):
    """A G.D.S. vat: a 4 x 4 glass tank three high, Ressaw standing two deep in its 2 x 2 middle."""
    for y in (1, 2, 3):
        for x in range(x0, x0 + 4):
            for z in range(z0, z0 + 4):
                if x0 < x < x0 + 3 and z0 < z < z0 + 3:
                    if y <= 2:
                        m.block(x, y, z, RESSAW)
                else:
                    m.block(x, y, z, "glass")


def place(m, block_id, cells):
    for x, y, z in cells:
        m.block(x, y, z, block_id)


def glow_in_walls(m, cells):
    """Ressaw blocks set into existing walls (asserted, so a moved wall never leaves a floating block)."""
    for x, y, z in cells:
        c = m.cells.get((x, y, z))
        assert c is not None and c["kind"] == "block" and c["id"] in (WALL, "@wall"), f"no wall at {(x, y, z)}"
        m.block(x, y, z, GLOW)


def crates(m, cells):
    for x, y, z in cells:
        below = m.cells.get((x, y - 1, z))
        assert below is not None and below["kind"] == "block" and below["id"] != RESSAW, f"crate floats at {(x, y, z)}"
        assert (x, y, z) not in m.cells, f"crate inside a block at {(x, y, z)}"
        m.marker(x, y, z, "loot")


def rubble(m, cells):
    for i, (x, z) in enumerate(cells):
        m.block(x, 1, z, RUBBLE[i % 2])


def flat(storeys, variant, tint):
    """A G.D.S. town flat in the city's colours, its residents gone (no npc marker). A one-storey flat has no upstairs,
    so it is always the plain variant and keeps no room marker on its roof."""
    f = shapes.gds_flat(storeys, variant if storeys > 1 else 0, tint)
    for key in [k for k, c in f.cells.items()
                if c["kind"] == "marker" and (c["id"] in LIVING_MARKERS or c["y"] >= storeys * 4)]:
        del f.cells[key]
    return f


def tunnel(m, x0, z0, x1, z1, along_z, tint, roof_hole=None):
    """A corridor box from (x0, z0) to (x1, z1): walls y 1..4 along its two long sides, open ends, a roof at y 5."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            side = x in (x0, x1) if along_z else z in (z0, z1)
            if side:
                for y in range(1, 5):
                    m.block(x, y, z, WALL, tint=RED if y == 3 else tint)
            if not (roof_hole and roof_hole[0] <= x <= roof_hole[2] and roof_hole[1] <= z <= roof_hole[3]):
                m.block(x, 5, z, WALL, tint=tint)


def opening(m, cells_xz):
    """Cuts a doorway (y 1..3) through whatever wall stands on these columns."""
    for x, z in cells_xz:
        for y in (1, 2, 3):
            m.air(x, y, z)


# ------------------------------------------------------------------ the districts

def flooded(variant):
    """city_garden: the flooded hall — the heart of the ruin, a hall whose floor is one glowing pool."""
    m = district(FLOOR)
    if variant == 0:
        walls(m, 1, 1, 22, 22, 7, PURPLE, bands=(3, 7), doors=(("n", 10), ("s", 10), ("w", 10), ("e", 10)))
        for x in range(3, 21):
            for z in range(3, 21):
                if x not in (11, 12) and z not in (11, 12):
                    m.block(x, 0, z, RESSAW)  # the pool; a rim walk and a cross of bridges stay floor
        for px, pz in ((6, 6), (6, 17), (17, 6), (17, 17)):
            m.block(px, 0, pz, FLOOR)  # a pillar stands on floor, not in the liquid
            m.fill(px, 1, pz, px, 7, pz, WALL, tint=RED)
        for x in range(1, 23):
            for z in range(1, 23):
                if x in (1, 2, 3, 20, 21, 22) or z in (1, 2, 3, 20, 21, 22):
                    m.block(x, 8, z, WALL, tint=PURPLE)  # the roof ring; the middle fell in long ago
        place(m, LIGHT, ((3, 8, 7), (3, 8, 16), (20, 8, 7), (20, 8, 16)))
        glow_in_walls(m, ((1, 2, 5), (1, 2, 17), (22, 2, 6), (22, 2, 18), (6, 2, 1), (17, 2, 22)))
        crates(m, ((2, 1, 2), (21, 1, 21), (21, 1, 2)))
    else:
        # an open square: a 16 x 16 pool behind a red curb, one bridge across, a broken statue, four kiosks
        for x in range(4, 20):
            for z in range(4, 20):
                if x in (4, 19) or z in (4, 19):
                    m.block(x, 1, z, WALL, tint=RED)
                elif z not in (11, 12):
                    m.block(x, 0, z, RESSAW)
        opening(m, ((4, 11), (4, 12), (19, 11), (19, 12)))
        for x in (11, 12):
            for z in (7, 8):
                m.block(x, 0, z, FLOOR)
                m.fill(x, 1, z, x, 4, z, WALL, tint=PURPLE)
        m.block(11, 5, 7, WALL, tint=RED)  # what is left of the statue's head
        for kx, kz, door in ((0, 0, "s"), (20, 0, "s"), (0, 20, "n"), (20, 20, "n")):
            walls(m, kx, kz, 4, 4, 3, RED if kx == 0 else PURPLE, bands=(), doors=((door, 1),))
            roof(m, kx, kz, 4, 4, 4, PURPLE)
        glow_in_walls(m, ((0, 2, 2), (23, 2, 1), (0, 2, 22), (23, 2, 21)))
        crates(m, ((1, 1, 1), (22, 1, 1), (2, 1, 22), (21, 1, 22)))
        place(m, LIGHT, ((2, 4, 2), (21, 4, 21)))
    return m


def pump_hall(variant):
    """city_hall: the pump hall — the pools the G.D.S. pumped their Ressaw from, galleries above them."""
    m = district(FLOOR)
    if variant == 0:
        walls(m, 2, 2, 20, 20, 9, PURPLE, bands=(3, 7), doors=(("n", 9),))
        roof(m, 2, 2, 20, 20, 10, PURPLE)
        for x in range(3, 21):
            for z in range(3, 21):
                if x in (3, 4, 19, 20) or z in (3, 4, 19, 20):
                    m.block(x, 4, z, FLOOR)  # the gallery two wide inside the walls
        for i, z in enumerate((6, 7, 8)):  # the flight up to it along the west wall, rising toward +Z
            for x in (3, 4):
                m.block(x, 1 + i, z, "@wall", shape=STAIRS | 0)
                for y in range(1, 1 + i):
                    m.block(x, y, z, "@wall")
                m.air(x, 4, z)
        pool(m, 8, 8, 15, 15)
        for px, pz in ((6, 6), (16, 6), (6, 16), (16, 16)):
            m.fill(px, 1, pz, px + 1, 7, pz + 1, WALL, tint=RED)  # the four pumps
        for i in range(7, 17):
            for x, z in ((i, 7), (i, 16), (7, i), (16, i)):
                m.block(x, 7, z, PIPE)  # the ring main over the pool
        vat(m, 16, 10)
        place(m, LIGHT, ((7, 10, 7), (16, 10, 7), (7, 10, 16), (16, 10, 16)))
        glow_in_walls(m, ((2, 2, 12), (21, 2, 7), (21, 5, 15), (8, 2, 21), (15, 6, 2)))
        crates(m, ((20, 5, 20), (3, 5, 20), (5, 1, 18)))
    else:
        walls(m, 1, 2, 10, 20, 7, RED, doors=(("n", 4), ("e", 9)))
        walls(m, 13, 2, 10, 20, 7, PURPLE, doors=(("n", 4), ("w", 9)))
        roof(m, 1, 2, 10, 20, 8, PURPLE)
        roof(m, 13, 2, 10, 20, 8, RED, hole=(16, 8, 19, 13))
        pool(m, 4, 7, 7, 17)
        pool(m, 16, 7, 19, 17)
        for px, line in ((5, 5), (17, 17)):
            m.fill(px, 1, 19, px + 1, 6, 20, WALL, tint=RED)  # a pump at the south end of each pool
            for z in range(7, 19):
                m.block(line, 6, z, PIPE)
        place(m, LIGHT, ((5, 8, 5), (5, 8, 15), (17, 8, 5), (21, 8, 15)))
        glow_in_walls(m, ((1, 2, 8), (10, 2, 16), (22, 2, 6), (13, 2, 17)))
        crates(m, ((2, 1, 20), (21, 1, 3), (11, 1, 21)))
    return m


def lab(variant):
    """city_market: the labs — glass vats of Ressaw, benches, store sheds."""
    m = district("@path")
    if variant == 0:
        walls(m, 3, 2, 18, 12, 5, PURPLE, doors=(("n", 8),), windows=True)
        roof(m, 3, 2, 18, 12, 6, PURPLE)
        for vx in (5, 10, 15):
            vat(m, vx, 8)
        for x in (5, 6, 7, 8, 15, 16, 17, 18):
            m.block(x, 1, 5, "@wall", shape=TABLE)
        place(m, LIGHT, ((7, 6, 6), (16, 6, 6), (11, 6, 10)))
        glow_in_walls(m, ((3, 1, 6), (20, 1, 9), (8, 1, 13)))
        walls(m, 3, 17, 6, 6, 3, RED, bands=(), doors=(("n", 2),))
        roof(m, 3, 17, 6, 6, 4, RED)
        pool(m, 13, 17, 16, 20)  # a vat burst here once
        place(m, "glass", ((12, 1, 18), (17, 1, 19), (14, 1, 21)))
        crates(m, ((4, 1, 3), (19, 1, 3), (5, 1, 20)))
    else:
        walls(m, 1, 2, 10, 10, 5, RED, doors=(("s", 4),), windows=True)
        walls(m, 13, 2, 10, 10, 5, PURPLE, doors=(("s", 4),), windows=True)
        roof(m, 1, 2, 10, 10, 6, PURPLE)
        roof(m, 13, 2, 10, 10, 6, RED, hole=(15, 3, 18, 6))
        vat(m, 3, 4)
        vat(m, 17, 4)
        for x in (14, 15):
            m.block(x, 1, 9, "@wall", shape=TABLE)
        pool(m, 8, 16, 15, 20)  # the big vat in the yard burst
        place(m, "glass", ((7, 1, 17), (16, 1, 18), (11, 1, 21), (12, 1, 15)))
        place(m, LIGHT, ((5, 6, 9), (19, 6, 9)))
        glow_in_walls(m, ((1, 1, 6), (10, 1, 4), (13, 1, 8), (22, 1, 5)))
        crates(m, ((2, 1, 3), (21, 1, 3), (2, 1, 20)))
    return m


def housing(variant):
    """city_housing: the flats of the G.D.S., empty, one of them fallen in."""
    m = district("@path")
    if variant == 0:
        for n, (x, z) in enumerate(((2, 2), (14, 2), (2, 14), (14, 14))):
            m.paste(flat(2, n % 2, RED if n in (1, 2) else PURPLE), x, 0, z)
        pool(m, 10, 10, 13, 13)  # the courtyard well, flooded
        glow_in_walls(m, ((2, 1, 7), (21, 1, 4), (9, 1, 19), (14, 1, 20)))
        crates(m, ((11, 1, 1), (12, 1, 22), (1, 1, 12)))
    else:
        m.paste(flat(2, 0, PURPLE), 2, 0, 2)
        m.paste(flat(2, 1, RED), 14, 0, 2)
        m.paste(flat(1, 0, PURPLE), 2, 0, 14)
        ruin = flat(1, 0, RED)
        for key in [k for k, c in ruin.cells.items() if c["kind"] == "marker" or k[1] >= 3 or (k[1] == 2 and (k[0] + k[2]) % 2 == 0)]:
            del ruin.cells[key]  # the fallen flat: its roof and half its walls are gone
        m.paste(ruin, 14, 0, 14)
        rubble(m, ((18, 18), (19, 17), (17, 19), (12, 16)))
        glow_in_walls(m, ((2, 1, 7), (21, 1, 4), (9, 1, 18)))
        crates(m, ((11, 1, 1), (12, 1, 22), (22, 1, 12)))
    return m


def corridor(variant):
    """city_tower: the collapsed corridors — covered passages with store rooms off them."""
    m = district("@path")
    if variant == 0:
        tunnel(m, 10, 0, 13, 23, True, PURPLE)
        tunnel(m, 0, 10, 23, 13, False, PURPLE, roof_hole=(17, 10, 21, 13))
        opening(m, ((10, 11), (10, 12), (13, 11), (13, 12), (11, 10), (12, 10), (11, 13), (12, 13)))
        for rx, rz, gap in ((2, 2, ((7, 4), (7, 5), (10, 4), (10, 5))), (16, 16, ((16, 18), (16, 19), (13, 18), (13, 19)))):
            walls(m, rx, rz, 6, 6, 3, RED, bands=())
            roof(m, rx, rz, 6, 6, 4, RED)
            opening(m, gap)
        rubble(m, ((18, 11), (19, 12), (20, 11)))
        place(m, LIGHT, ((11, 5, 5), (12, 5, 18), (5, 5, 11)))
        glow_in_walls(m, ((10, 2, 3), (13, 2, 20), (3, 2, 10), (15, 2, 13)))
        crates(m, ((3, 1, 3), (20, 1, 20), (11, 1, 22)))
    else:
        tunnel(m, 3, 0, 6, 23, True, RED)
        tunnel(m, 17, 0, 20, 23, True, RED, roof_hole=(17, 3, 20, 7))
        tunnel(m, 6, 10, 17, 13, False, PURPLE)
        opening(m, ((6, 11), (6, 12), (17, 11), (17, 12)))
        for rz, gap in ((2, ((11, 7), (12, 7), (11, 10), (12, 10))), (16, ((11, 16), (12, 16), (11, 13), (12, 13)))):
            walls(m, 8, rz, 8, 6, 3, PURPLE, bands=())
            roof(m, 8, rz, 8, 6, 4, PURPLE)
            opening(m, gap)
        rubble(m, ((18, 4), (19, 5), (18, 6)))
        place(m, LIGHT, ((4, 5, 5), (4, 5, 18), (11, 5, 11)))
        glow_in_walls(m, ((3, 2, 8), (20, 2, 15), (6, 2, 4), (17, 2, 19)))
        crates(m, ((9, 1, 3), (14, 1, 20), (4, 1, 1)))
    return m


DISTRICTS = (
    ("flooded", "city_garden", "Flooded Hall", flooded),
    ("pumphall", "city_hall", "Pump Hall", pump_hall),
    ("lab", "city_market", "Lab", lab),
    ("housing", "city_housing", "Housing", housing),
    ("corridor", "city_tower", "Collapsed Corridors", corridor),
)


def module_entry(key, name, function, m):
    height = m.height_used()
    assert height <= HEIGHT, f"{key}: {height} rows, the envelope is {HEIGHT}"
    assert any(c["kind"] == "marker" and c["id"] == "loot" for c in m.cells.values()), f"{key}: no crate"
    return {
        "key": key,
        "name": name,
        "tier": "metropolis",
        "kind": "settlement",
        "role": function,
        "kit": KIT,
        "function": function,
        "pack": "default",
        "weight": 1,
        "underground": True,
        "width": SIZE,
        "height": height,
        "length": SIZE,
        "cells": m.ordered(),
    }


def ruin_modules():
    out = []
    for slug, function, nice, builder in DISTRICTS:
        for v in range(2):
            out.append(module_entry(f"gds_ruin_{slug}_{v + 1}", f"G.D.S. Ruin {nice} {v + 1}", function, builder(v)))
    return out


def kit_entry(module, max_, weight=1):
    return {"module": module, "min": 0, "max": max_, "required": False, "weight": weight}


def ruin_kit():
    """The underground city kit: 3 x 3 districts of 24 with 4-wide streets (88 a side), 12 rows; the district map puts the
    flooded hall in the middle, the pump hall west of it, labs north and south, corridors in two corners."""
    return {
        "key": KIT,
        "name": "G.D.S. Buried Ruin 1",
        "kind": "city",
        "tier": "metropolis",
        "pack": "default",
        "weight": 2,
        "underground": True,
        "planetTypes": [],
        "grid": 3,
        "districtSize": SIZE,
        "street": 4,
        "height": HEIGHT + 1,
        "roleMap": ["RMT", "HGR", "TMR"],
        "entries": [
            kit_entry("gds_ruin_housing_1", 3, 2), kit_entry("gds_ruin_housing_2", 3, 2),
            kit_entry("gds_ruin_lab_1", 2), kit_entry("gds_ruin_lab_2", 2),
            kit_entry("gds_ruin_corridor_1", 2), kit_entry("gds_ruin_corridor_2", 2),
            kit_entry("gds_ruin_pumphall_1", 1), kit_entry("gds_ruin_pumphall_2", 1),
            kit_entry("gds_ruin_flooded_1", 1), kit_entry("gds_ruin_flooded_2", 1),
        ],
    }


def dump_lf(path, obj):
    """The pools are LF files on every OS (.gitattributes); Path.write_text would write CRLF on Windows."""
    path.write_bytes((json.dumps(obj, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))


def upsert(pool, new):
    index = next((i for i, t in enumerate(pool) if t.get("key") == new["key"]), None)
    if index is not None:
        pool[index] = new
    else:
        pool.append(new)


def main():
    pool = _load(POOL)
    for new in ruin_modules():
        upsert(pool, new)
        print(f"{new['key']}: {len(new['cells'])} cells, {new['width']}x{new['height']}x{new['length']}, {new['function']}")
    dump_lf(POOL, pool)

    kits = _load(KITS)
    kit = ruin_kit()
    upsert(kits, kit)
    dump_lf(KITS, kits)
    print(f"{kit['key']}: {kit['kind']}, {len(kit['entries'])} entries, underground")


if __name__ == "__main__":
    main()
