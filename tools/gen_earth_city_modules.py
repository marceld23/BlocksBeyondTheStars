# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the Earth city kit (#3983): the district modules of the nine metropolises of Earth and the kit `city_earth_1`.

ONE kit for all nine cities. A city differs from the next by its colours — every wall, window, roof, floor and path cell is a
material token (@wall @accent @roof @floor @path) that the city's palette in data/places/sol_earth.json resolves (a block and
a dye per token) — and by its size, its coastline and its landmarks, which the metropolis planner lays on the land
(src/BlocksBeyondTheStars.WorldGeneration/MetropolisPlanner.cs). The space-age look: glass curtain walls between slim
frames, a light set into every ceiling (the windows glow at night), roof gardens with a tree and a mast on the tall ones.

  district (32 x 32)       function       what stands there
  earth_plaza_1            city_plaza     the landing field: the pad ring open, lamps round it, planters, benches
  earth_market_hall_1/2    city_market    a glass-roofed hall with stalls and two shops (vendors, a flat upstairs each)
  earth_notice_house_1     city_hall      the notice house (mission board) on a square, two town houses
  earth_inn_1              city_hall      the inn (innkeeper) with a garden of tables, a town house
  earth_workshop_1         city_hall      the workshop (craftsman) with a yard of crates, a town house
  earth_highrise_small_1   city_housing   two towers of five storeys
  earth_highrise_medium_1  city_housing   a tower of seven storeys on a square with trees
  earth_highrise_large_1/2 city_tower     a tower of ten storeys (the skyline round the centre)
  earth_housing_block_1    city_housing   two four-storey slabs round a green courtyard
  earth_park_1/2           city_garden    lawns, a pond or flower beds, trees, benches, lamps
  earth_quay_1             city_quay      a harbour square: bollards, a harbour office, cargo, a light mast

The rail halls are not here: every city keeps a free square (`S` in its district map) where the rail network will build one.

Conventions (tools/settlement_module_shapes.py): the entrance faces -Z, doorways 2 wide and 3 tall with a door marker,
storeys 4 high, flights of stairs (never ladders — NPCs walk steps), a `room` marker in every room (the furnisher brings the
beds and the rest). Modules carry `pinOnly` and `planetTypes: [sol_earth]`: no pool offers them; only the kit names them.

Deterministic: appends or replaces by key only (data/settlement_templates.json, data/structure_kits.json).

Usage:
    uv run --no-project python tools/gen_earth_city_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _load  # noqa: E402
import settlement_module_shapes as shapes  # noqa: E402
from settlement_module_shapes import Module  # noqa: E402
from gen_lively_settlement_modules import compact_body, dump_lf, upsert  # noqa: E402

POOL = DATA / "settlement_templates.json"
KITS = DATA / "structure_kits.json"
SIZE = 32
HEIGHT = 47  # the metropolis's height (48) minus the floor row
FAMILY = "city_earth"
KIT = "city_earth_1"

STAIRS = 6 << 2
CHAIR = 15 << 2
TABLE = 14 << 2
FENCE = 16 << 2
BENCH = 63 << 2

LIGHT = "strip_light_warm"
BEACON = "strip_light_cyan"


# ------------------------------------------------------------------ pieces

def district():
    """A district: @path paving over the whole 32 x 32."""
    m = Module(SIZE, HEIGHT, SIZE)
    m.fill(0, 0, 0, SIZE - 1, 0, SIZE - 1, "@path")
    return m


def lamp(m, x, z, height=4):
    """A street lamp: a steel post, a warm light on top."""
    for y in range(1, height):
        m.block(x, y, z, "steel_wall")
    m.block(x, height, z, LIGHT)


def tree(m, x, z, trunk=3):
    """A small tree on a grass planter."""
    m.block(x, 0, z, "grass")
    for y in range(1, trunk + 1):
        m.block(x, y, z, "wood_log")
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            m.block(x + dx, trunk + 1, z + dz, "tree_leaves")
    m.block(x, trunk + 2, z, "tree_leaves")


def bench(m, x, z, yaw):
    m.block(x, 1, z, "@roof", shape=BENCH | yaw)


def planter(m, x0, z0, x1, z1, flora="flora_bush"):
    """A raised bed of grass with plants on it, framed in @wall."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            if x in (x0, x1) or z in (z0, z1):
                m.block(x, 1, z, "@wall", shape=shapes.SLAB)
            else:
                m.block(x, 0, z, "grass")
                if (x + z) % 2 == 0:
                    m.block(x, 1, z, flora)


def tower(w, l, storeys, mast=True, garden=True):
    """A space-age tower: glass curtain walls between @wall frames, a band at every deck, a light set into every ceiling, a
    flight of stairs per storey (west and east by turns), a room per storey, a roof garden and a mast."""
    top = storeys * 4
    m = Module(w, min(HEIGHT, top + 7), l)
    shapes.deck(m, 0, "@floor")
    mid = w // 2
    for s in range(storeys):
        base = s * 4
        for y in range(base + 1, base + 4):
            for x in range(w):
                for z in range(l):
                    if not (x in (0, w - 1) or z in (0, l - 1)):
                        continue
                    if s == 0 and z == 0 and mid - 1 <= x <= mid:
                        continue  # the doorway
                    corner = x in (0, w - 1) and z in (0, l - 1)
                    along = x if z in (0, l - 1) else z
                    m.block(x, y, z, "@wall" if corner or along % 3 == 0 else "@accent")
        deck_y = base + 4
        shapes.deck(m, deck_y, "@floor" if s < storeys - 1 else "@roof")
        for x in range(w):
            for z in range(l):
                if x in (0, w - 1) or z in (0, l - 1):
                    m.block(x, deck_y, z, "@wall")  # the band round every deck
        for lx, lz in ((mid + 1, l // 2 - 2), (mid + 1, l // 2 + 2), (w // 2 - 2, l - 3)):
            m.block(lx, deck_y, lz, LIGHT)  # a light set INTO the deck: the window glows at night
        m.marker(mid, base + 1, l // 2 + 1, "room")
    for s in range(storeys - 1):
        base = s * 4
        x = 1 if s % 2 == 0 else w - 2
        for i, z in enumerate((2, 3, 4)):
            m.block(x, base + 1 + i, z, "@wall", shape=STAIRS | 0)
            for y in range(base + 1, base + 1 + i):
                m.block(x, y, z, "@wall")
            m.air(x, base + 4, z)
    m.marker(mid - 1, 1, 0, "door_slide")
    m.marker(mid + 1, 1, 2, "npc")
    if garden:
        roof_garden(m, top, mast)
    return m


def roof_garden(m, top, mast):
    """A parapet, a lawn with bushes and a small tree on the roof, a mast with a beacon."""
    w, l = m.w, m.l
    for x in range(w):
        for z in range(l):
            if x in (0, w - 1) or z in (0, l - 1):
                m.block(x, top + 1, z, "@wall", shape=shapes.SLAB)
            elif 2 <= x <= w - 3 and 2 <= z <= l - 3:
                m.block(x, top, z, "grass")
                if (x * 7 + z * 3) % 5 == 0:
                    m.block(x, top + 1, z, "flora_bush")
    cx, cz = w // 2, l // 2
    m.air(cx, top + 1, cz)
    for y in range(top + 1, top + 3):
        m.block(cx, y, cz, "wood_log")
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            m.block(cx + dx, top + 3, cz + dz, "tree_leaves")
    if mast:
        mx, mz = w - 2, l - 2
        for y in range(top + 1, min(m.h - 1, top + 5)):
            m.block(mx, y, mz, "steel_wall")
        m.block(mx, min(m.h - 1, top + 5), mz, BEACON)


def town_house(function, variant, storeys=2):
    """A service building of the town set (two storeys of 8 x 8: the post, the rooms, the keeper's bed upstairs)."""
    return shapes.town_module(function, variant, alien=False, storeys=storeys)


# ------------------------------------------------------------------ the districts

def plaza():
    """The landing field round the pad (the pad ring — 23 x 23 in the middle — stays open ground: the composer keeps it)."""
    m = district()
    c = SIZE // 2
    for x in range(SIZE):
        for z in range(SIZE):
            d2 = (x - c) ** 2 + (z - c) ** 2
            if d2 <= 13 * 13:
                m.block(x, 0, z, "@floor")
            if 12 * 12 <= d2 <= 13 * 13 and (x + z) % 3 == 0:
                m.block(x, 0, z, "light_white")  # the landing ring, lit in the floor
    for x, z in ((2, 2), (29, 2), (2, 29), (29, 29)):
        planter(m, x - 1, z - 1, x + 1, z + 1)
        m.air(x, 1, z)
        tree(m, x, z)
    for x, z in ((16, 1), (16, 30), (1, 16), (30, 16), (4, 9), (27, 9), (4, 22), (27, 22)):  # outside the pad ring (5..27)
        lamp(m, x, z, 5)
    for x, z, yaw in ((9, 2, 0), (22, 2, 0), (9, 29, 2), (22, 29, 2), (2, 9, 3), (2, 22, 3), (29, 9, 1), (29, 22, 1)):
        bench(m, x, z, yaw)
    for x, z in ((12, 3), (19, 28)):
        m.marker(x, 1, z, "npc")
    return m


def market_hall(variant):
    """A glass-roofed hall with three stalls and two shops of the town set at its ends."""
    m = district()
    x0, x1, z0, z1 = 3, 28, 9, 22
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            m.block(x, 0, z, "@floor")
            if (x in (x0, x1) or z in (z0, z1)) and (x - x0) % 5 == 0 or (x in (x0, x1) and (z - z0) % 4 == 0):
                for y in range(1, 6):
                    m.block(x, y, z, "@wall")
            m.block(x, 6, z, "@accent")  # the glass roof
    for x in range(x0, x1 + 1, 4):
        m.block(x, 6, (z0 + z1) // 2, LIGHT)
    for i in range(3):
        sx = 7 + i * 7
        for dx, dz in ((0, 0), (3, 0), (0, 3), (3, 3)):
            m.block(sx + dx, 1, 14 + dz, "@roof", shape=shapes.SLAB)
        m.block(sx + 1, 1, 15, "crate")
        m.marker(sx + 2, 1, 16, "npc")
    shops = ((2, 0), (22, 24)) if variant == 0 else ((22, 0), (2, 24))
    for n, (x, z) in enumerate(shops):
        m.paste(town_house("market", n % 2), x, 0, z)
    for x, z in ((1, 1), (30, 1), (1, 30), (30, 30)):
        lamp(m, x, z)
    return m


def service(function, variant=0):
    """A service district: the town set's building of the function on a square, its extras, one or two houses beside it."""
    m = district()
    m.paste(town_house(function, variant), 12, 0, 4)
    if function == "board":
        # the square in front: a notice pillar with a light, benches round it
        for y in range(1, 4):
            m.block(16, y, 18, "@wall")
        m.block(16, 4, 18, BEACON)
        for x, z, yaw in ((13, 18, 3), (19, 18, 1), (16, 15, 0), (16, 21, 2)):
            bench(m, x, z, yaw)
        houses = ((2, 22), (22, 22))
    elif function == "tavern":
        # the garden: tables and chairs under the lamps
        for tx, tz in ((4, 16), (10, 16), (20, 16), (26, 16)):
            m.block(tx, 1, tz, "@roof", shape=TABLE)
            m.block(tx - 1, 1, tz, "@roof", shape=CHAIR | 3)
            m.block(tx + 1, 1, tz, "@roof", shape=CHAIR | 1)
        houses = ((22, 22),)
    else:
        # the yard: crates and a fenced store
        for x, z in ((3, 15), (4, 15), (3, 16), (26, 15), (27, 15), (27, 16)):
            m.block(x, 1, z, "crate")
        for x in range(2, 9):
            m.block(x, 1, 20, "@wall", shape=FENCE)
        houses = ((22, 22),)
    for n, (x, z) in enumerate(houses):
        m.paste(town_house("house", n % 2, storeys=3), x, 0, z)
    for x, z in ((1, 1), (30, 1), (1, 30), (30, 30)):
        lamp(m, x, z)
    tree(m, 5, 5)
    tree(m, 26, 5)
    return m


def highrise_small():
    m = district()
    m.paste(tower(10, 10, 5), 3, 0, 4)
    m.paste(tower(10, 10, 5), 19, 0, 18)
    planter(m, 18, 3, 28, 9)
    planter(m, 3, 19, 13, 25)
    for x, z in ((1, 1), (30, 30), (16, 16)):
        lamp(m, x, z)
    return m


def highrise_medium():
    m = district()
    m.paste(tower(14, 14, 7), 9, 0, 9)
    for x, z in ((4, 4), (27, 4), (4, 27), (27, 27)):
        tree(m, x, z)
    for x, z in ((16, 3), (16, 28), (3, 16), (28, 16)):
        lamp(m, x, z)
    for x, z, yaw in ((12, 5, 0), (20, 5, 0), (12, 26, 2), (20, 26, 2)):
        bench(m, x, z, yaw)
    return m


def highrise_large(variant):
    m = district()
    w = 16 if variant == 0 else 14
    off = (SIZE - w) // 2
    m.paste(tower(w, w, 10), off, 0, off)
    for x, z in ((3, 3), (28, 3), (3, 28), (28, 28)):
        planter(m, x - 1, z - 1, x + 1, z + 1, "flora_fern")
    for x, z in ((8, 2), (23, 2), (8, 29), (23, 29), (2, 16), (29, 16)):
        lamp(m, x, z, 5)
    m.marker(16, 1, 4, "npc")
    return m


def housing_block():
    """Two four-storey slabs (one long room per storey each) round a courtyard with a lawn, trees and benches."""
    m = district()
    for z in (2, 21):
        m.paste(tower(26, 9, 4, mast=False, garden=False), 3, 0, z)
    for x in range(4, 28):
        for z in range(12, 20):
            m.block(x, 0, z, "grass")
    for x, z in ((8, 16), (16, 15), (24, 16)):
        tree(m, x, z)
    for x, z, yaw in ((12, 13, 0), (20, 13, 0), (12, 18, 2), (20, 18, 2)):
        bench(m, x, z, yaw)
    for x, z in ((1, 16), (30, 16)):
        lamp(m, x, z)
    return m


def park(variant):
    m = district()
    for x in range(1, SIZE - 1):
        for z in range(1, SIZE - 1):
            m.block(x, 0, z, "grass")
    for i in range(1, SIZE - 1):  # the cross of paths
        m.block(i, 0, 15, "@path")
        m.block(i, 0, 16, "@path")
        m.block(15, 0, i, "@path")
        m.block(16, 0, i, "@path")
    if variant == 0:
        for x in range(20, 28):
            for z in range(20, 28):
                edge = x in (20, 27) or z in (20, 27)
                m.block(x, 0, z, "@wall" if edge else "water")
        trees = ((5, 5), (10, 8), (6, 24), (24, 6), (27, 11), (9, 27))
    else:
        for x in range(4, 12):
            for z in range(19, 27):
                if (x + z) % 2 == 0:
                    m.block(x, 1, z, "flora_flower")
        trees = ((5, 6), (24, 5), (22, 24), (27, 27), (10, 10), (26, 18))
    for x, z in trees:
        tree(m, x, z, 3 + (x + z) % 2)
    for x, z, yaw in ((13, 13, 0), (18, 13, 0), (13, 18, 2), (18, 18, 2)):
        bench(m, x, z, yaw)
    for x, z in ((2, 2), (29, 2), (2, 29), (29, 29), (14, 14), (17, 17)):
        lamp(m, x, z)
    for x, z in ((8, 15), (23, 16)):
        m.marker(x, 1, z, "npc")
    return m


def quay():
    """A harbour square: bollards along the edges, a harbour office, cargo and a light mast."""
    m = district()
    for i in range(1, SIZE - 1, 4):
        for x, z in ((i, 0), (i, SIZE - 1), (0, i), (SIZE - 1, i)):
            m.block(x, 1, z, "steel_wall", shape=shapes.SLAB)
    m.paste(town_house("house", 0), 4, 0, 4)
    for x, z in ((20, 6), (21, 6), (22, 6), (20, 7), (21, 7), (20, 8), (24, 20), (25, 20), (24, 21)):
        m.block(x, 1, z, "transport_crate")
    for y in range(1, 9):
        m.block(26, y, 26, "steel_wall")
    m.block(26, 9, 26, BEACON)
    for x, z, yaw in ((10, 20, 0), (14, 20, 0), (18, 20, 0)):
        bench(m, x, z, yaw)
    for x, z in ((3, 16), (16, 3), (28, 16), (16, 28)):
        lamp(m, x, z)
    m.marker(12, 1, 24, "npc")
    return m


MODULES = (
    ("earth_plaza_1", "Earth Plaza 1", "city_plaza", plaza),
    ("earth_market_hall_1", "Earth Market Hall 1", "city_market", lambda: market_hall(0)),
    ("earth_market_hall_2", "Earth Market Hall 2", "city_market", lambda: market_hall(1)),
    ("earth_notice_house_1", "Earth Notice House 1", "city_hall", lambda: service("board")),
    ("earth_inn_1", "Earth Inn 1", "city_hall", lambda: service("tavern")),
    ("earth_workshop_1", "Earth Workshop 1", "city_hall", lambda: service("workshop")),
    ("earth_highrise_small_1", "Earth High-rise Small 1", "city_housing", highrise_small),
    ("earth_highrise_medium_1", "Earth High-rise Medium 1", "city_housing", highrise_medium),
    ("earth_highrise_large_1", "Earth High-rise Large 1", "city_tower", lambda: highrise_large(0)),
    ("earth_highrise_large_2", "Earth High-rise Large 2", "city_tower", lambda: highrise_large(1)),
    ("earth_housing_block_1", "Earth Housing Block 1", "city_housing", housing_block),
    ("earth_park_1", "Earth Park 1", "city_garden", lambda: park(0)),
    ("earth_park_2", "Earth Park 2", "city_garden", lambda: park(1)),
    ("earth_quay_1", "Earth Quay 1", "city_quay", quay),
)


def module_entry(key, name, function, m):
    palette, rows, markers = compact_body(m)
    return {
        "key": key,
        "name": name,
        "tier": "metropolis",
        "kind": "settlement",
        "role": function,
        "kit": FAMILY,
        "function": function,
        "pack": "default",
        "weight": 1,
        "pinOnly": True,
        "planetTypes": ["sol_earth"],
        "width": m.w,
        "height": m.height_used(),
        "length": m.l,
        "palette": palette,
        "rows": rows,
        "cells": markers,
    }


def entry(module, max_=1, weight=1, required=False):
    return {"module": module, "min": 1 if required else 0, "max": max_, "required": required, "weight": weight}


def earth_kit():
    return {
        "key": KIT,
        "name": "Earth City 1",
        "kind": "city",
        "tier": "metropolis",
        "pack": "default",
        "weight": 1,
        "pinOnly": True,  # never drawn: the city places name it
        "planetTypes": ["sol_earth"],
        "grid": 5,
        "districtSize": SIZE,
        "street": 4,
        "height": 40,  # the kit format's cap — a metropolis takes its grid and height (48) from its place's plan
        "entries": [
            entry("earth_plaza_1", required=True),
            entry("earth_notice_house_1", required=True),
            entry("earth_inn_1", required=True),
            entry("earth_workshop_1", required=True),
            entry("earth_market_hall_1", required=True),
            entry("earth_market_hall_2"),
            entry("earth_highrise_large_1", max_=12, weight=2),
            entry("earth_highrise_large_2", max_=12, weight=2),
            entry("earth_highrise_small_1", max_=60, weight=3),
            entry("earth_highrise_medium_1", max_=60, weight=3),
            entry("earth_housing_block_1", max_=60, weight=2),
            entry("earth_park_1", max_=20),
            entry("earth_park_2", max_=20),
            entry("earth_quay_1", max_=60),
        ],
    }


def main():
    pool = _load(POOL)
    for key, name, function, build in MODULES:
        new = module_entry(key, name, function, build())
        upsert(pool, new)
        print(f"{key}: {new['width']}x{new['height']}x{new['length']}, {function}, {len(new['rows'])} rows, {len(new['cells'])} markers")
    dump_lf(POOL, pool)

    kits = _load(KITS)
    upsert(kits, earth_kit())
    dump_lf(KITS, kits)
    print(f"{KIT}: {len(earth_kit()['entries'])} entries")


if __name__ == "__main__":
    main()
