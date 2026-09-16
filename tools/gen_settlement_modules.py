# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the shipped example building MODULES (#1827) into data/settlement_templates.json.

A module is a settlement template with a `role` (house / market / board / greenhouse / city_*): the
procedural settlement composer stamps it into a plot of that role instead of its own building. These two
keep the pipeline live and testable; the real ones are authored in the Town editor ("Use as" stepper).

  - timber_cottage  village-style house, 6 x 7 x 6: stone walls on log posts under a log gable, a hinged door
  - iron_flat       town-style house, 6 x 9 x 6: two iron/glass storeys, a ladder, deck lights, a slide door

Both carry a `room` marker per storey so the interiors are furnished procedurally (#1828). Deterministic:
running it again rewrites the same two entries (matched by key), everything else in the pool stays.

#1886 (Marcel 2026-09-14): the MODULAR sets that build villages, towns, cities and the G.D.S. city entirely from
modules (tools/settlement_module_shapes.py), the modular kits (two per size, each with one variant set of market,
notice house, greenhouse, tavern and workshop), furnished copies of the four complete templates (#1889 — the originals
become pin-only, so existing worlds replay them unchanged) and the first default kits as pin-only.

Usage:
    python tools/gen_settlement_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _dump, _load  # noqa: E402
import settlement_module_shapes as shapes  # noqa: E402

POOL = DATA / "settlement_templates.json"
SIZE = 6  # the plot building envelope (SettlementGenerator.Building)


def block(cells, x, y, z, block_id):
    cells[(x, y, z)] = {"x": x, "y": y, "z": z, "kind": "block", "id": block_id}


def marker(cells, x, y, z, marker_id):
    cells[(x, y, z)] = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker_id}


def perimeter():
    for x in range(SIZE):
        for z in range(SIZE):
            if x in (0, SIZE - 1) or z in (0, SIZE - 1):
                yield x, z


def is_corner(x, z):
    return x in (0, SIZE - 1) and z in (0, SIZE - 1)


def is_side_centre(x, z):
    """The two middle cells of a wall — where a window goes."""
    return (z in (0, SIZE - 1) and x in (2, 3)) or (x in (0, SIZE - 1) and z in (2, 3))


def door_cell(x, z):
    """The 2-wide door gap on the -Z wall (the same columns the procedural houses use)."""
    return z == 0 and x in (2, 3)


def timber_cottage():
    cells = {}
    for x in range(SIZE):
        for z in range(SIZE):
            block(cells, x, 0, z, "stone")  # floor
            block(cells, x, 4, z, "wood_log")  # ceiling deck
    for y in (1, 2, 3):
        for x, z in perimeter():
            if door_cell(x, z):
                continue  # the doorway
            if is_corner(x, z):
                block(cells, x, y, z, "wood_log")  # posts
            elif y == 2 and is_side_centre(x, z):
                block(cells, x, y, z, "glass")  # windows
            else:
                block(cells, x, y, z, "stone")
    # A stepped log gable: an inset ring, then the ridge.
    for x in range(1, SIZE - 1):
        for z in range(1, SIZE - 1):
            if x in (1, SIZE - 2) or z in (1, SIZE - 2):
                block(cells, x, 5, z, "wood_log")
    for x in (2, 3):
        for z in (2, 3):
            block(cells, x, 6, z, "wood_log")
    marker(cells, 2, 1, 0, "door_hinge")
    marker(cells, 3, 1, 3, "npc")
    marker(cells, 2, 1, 2, "room")
    return entry("timber_cottage", "Timber Cottage", "village", "house", 7, cells)


def iron_flat():
    cells = {}
    for x in range(SIZE):
        for z in range(SIZE):
            block(cells, x, 0, z, "steel_floor")  # floor
            block(cells, x, 4, z, "iron_wall")  # deck between the storeys
            block(cells, x, 8, z, "iron_wall")  # roof
    for storey_base in (0, 4):
        for y in (storey_base + 1, storey_base + 2, storey_base + 3):
            for x, z in perimeter():
                if storey_base == 0 and door_cell(x, z):
                    continue  # the doorway (ground floor only)
                if y == storey_base + 2 and is_side_centre(x, z):
                    block(cells, x, y, z, "glass")
                else:
                    block(cells, x, y, z, "iron_wall")
    # Ladder in the corner through a hole in the deck; a light set INTO each deck (#1808).
    del cells[(1, 4, 1)]
    for y in range(1, 8):
        block(cells, 1, y, 1, "ladder")
    block(cells, 3, 4, 3, "strip_light_warm")
    block(cells, 3, 8, 3, "strip_light_warm")
    marker(cells, 2, 1, 0, "door_slide")
    marker(cells, 3, 1, 3, "npc")
    marker(cells, 2, 1, 2, "room")
    marker(cells, 3, 5, 3, "room")
    return entry("iron_flat", "Iron Flat", "town", "house", 9, cells)


def entry(key, name, tier, role, height, cells):
    ordered = [cells[k] for k in sorted(cells)]
    return {
        "key": key,
        "name": name,
        "tier": tier,
        "kind": "settlement",
        "role": role,
        "pack": "default",
        "weight": 2,
        "width": SIZE,
        "height": height,
        "length": SIZE,
        "cells": ordered,
    }


KITS = DATA / "structure_kits.json"


def default_kits():
    """The default settlement kits (#1876): today's grid per tier with the shipped house modules as optional entries
    (so a kit village looks like a village), plus the G.D.S. city kit reproducing the composer's own 7×7 map.
    #1886: pin-only now — kept for the placements that use them, never drawn for a new settlement."""
    kits = []
    for tier, module in (("hamlet", "timber_cottage"), ("village", "timber_cottage"), ("town", "iron_flat"), ("city", "iron_flat")):
        kits.append({
            "key": f"{tier}_default_1",
            "name": f"{tier.capitalize()} Default 1",
            "kind": "settlement",
            "tier": tier,
            "pack": "default",
            "weight": 2,
            "pinOnly": True,
            "entries": [{"module": module, "min": 0, "max": 2, "required": False, "weight": 1}],
        })
    kits.append({
        "key": "city_gds_default_1",
        "name": "G.D.S. City Default 1",
        "kind": "city",
        "tier": "metropolis",
        "pack": "default",
        "weight": 1,
        "pinOnly": True,
        "planetTypes": ["gds_desert"],
        "entries": [],
    })
    return kits


# ------------------------------------------------------------------ #1886 modular sets

# The profession buildings (2026-09): one variant each, appended so the pool keeps its order.
PROFESSION_FUNCTIONS = (("clinic", 1), ("shop", 1), ("armory", 1), ("library", 1), ("stable", 1), ("quarry", 1),
                        ("studio", 1), ("newsroom", 1))
VILLAGE_FUNCTIONS = (("house", 3), ("market", 2), ("board", 2), ("greenhouse", 2), ("tavern", 2), ("workshop", 2)) \
    + PROFESSION_FUNCTIONS
TOWN_FUNCTIONS = (("house", 2), ("market", 2), ("board", 2), ("greenhouse", 2), ("tavern", 2), ("workshop", 2)) \
    + PROFESSION_FUNCTIONS
DISTRICTS = (("city_housing", shapes.gds_housing), ("city_market", shapes.gds_market), ("city_hall", shapes.gds_hall),
             ("city_garden", shapes.gds_garden), ("city_tower", shapes.gds_tower))
NICE = {"house": "House", "market": "Market", "board": "Notice House", "greenhouse": "Greenhouse", "tavern": "Tavern",
        "workshop": "Workshop", "clinic": "Clinic", "shop": "Shop", "armory": "Armory", "library": "Library",
        "stable": "Stable", "quarry": "Quarry", "studio": "Studio", "newsroom": "Newsroom", "city_housing": "Housing", "city_market": "Market", "city_hall": "Hall",
        "city_garden": "Garden", "city_tower": "Tower"}


def module_entry(key, name, tier, function, family, m, alien=False):
    out = {
        "key": key,
        "name": name,
        "tier": tier,
        "kind": "settlement",
        "role": function,
        "kit": family,
        "function": function,
        "pack": "default",
        "weight": 1,
        "width": m.w,
        "height": m.height_used(),
        "length": m.l,
        "cells": m.ordered(),
    }
    if alien:
        out["style"] = "alien"
    return out


def modular_modules():
    out = []
    for suffix, alien in (("", False), ("_alien", True)):
        prefix = "Alien " if alien else ""
        for function, variants in VILLAGE_FUNCTIONS:
            for v in range(variants):
                m = shapes.village_module(function, v, alien)
                out.append(module_entry(f"village_{function}_{v + 1}{suffix}", f"{prefix}Village {NICE[function]} {v + 1}",
                                        "village", function, "settlement_modular", m, alien))
        for function, variants in TOWN_FUNCTIONS:
            for v in range(variants):
                m = shapes.town_module(function, v, alien)
                out.append(module_entry(f"town_{function}_{v + 1}{suffix}", f"{prefix}Town {NICE[function]} {v + 1}",
                                        "town", function, "settlement_modular", m, alien))
        m = shapes.town_module("house", 1, alien, storeys=3)
        out.append(module_entry(f"city_tall_house_1{suffix}", f"{prefix}City Tall House 1", "city", "house",
                                "settlement_modular", m, alien))
    for function, builder in DISTRICTS:
        for v in range(2):
            m = builder(v)
            out.append(module_entry(f"gds_{function[5:]}_{v + 1}", f"G.D.S. {NICE[function]} {v + 1}", "metropolis",
                                    function, "city_gds_modular", m))
    # The services districts (2026-09, NPC professions): housing-role districts with three profession houses each.
    for v in range(2):
        out.append(module_entry(f"gds_services_{v + 1}", f"G.D.S. Services {v + 1}", "metropolis", "city_housing",
                                "city_gds_modular", shapes.gds_services(v)))
    return out


def kit_entry(module, weight=1, max_=1, required=False):
    return {"module": module, "min": 1 if required else 0, "max": max_, "required": required, "weight": weight}


def style_pair(key, **kw):
    """The human and the alien variant of a module as two entries (the composer keeps the settlement's own)."""
    return [kit_entry(key, **kw), kit_entry(key + "_alien", **kw)]


def modular_kits():
    kits = []
    grids = {
        "hamlet": dict(colsMin=1, colsMax=2, rowsMin=2, rowsMax=3, storeys=1),
        "village": dict(colsMin=2, colsMax=3, rowsMin=2, rowsMax=3, storeys=1),
        "town": dict(colsMin=3, colsMax=4, rowsMin=3, rowsMax=4, storeys=2),
        "city": dict(colsMin=4, colsMax=5, rowsMin=4, rowsMax=5, storeys=3),
    }
    for tier, grid in grids.items():
        style = "village" if tier in ("hamlet", "village") else "town"
        for v in (1, 2):  # the variant set of the services — one market, notice house, tavern, workshop each
            entries = []
            entries += style_pair(f"{style}_market_{v}", required=True)
            entries += style_pair(f"{style}_board_{v}", required=True)
            if tier != "hamlet":
                entries += style_pair(f"{style}_tavern_{v}", required=True)
            entries += style_pair(f"{style}_workshop_{v}", required=tier in ("town", "city"))
            entries += style_pair(f"{style}_greenhouse_{v}", max_=3)
            for n in ((1, 2, 3) if style == "village" else (1, 2)):
                entries += style_pair(f"{style}_house_{n}", max_=30, weight=3 if n == 1 else 2)
            if tier == "city":
                entries += style_pair("city_tall_house_1", max_=30, weight=3)
            # The profession buildings (2026-09): optional, at most one each — appended after the classic entries, so a
            # settlement's pinned composition replays unchanged and only fresh settlements may draw them.
            for function, _ in PROFESSION_FUNCTIONS:
                entries += style_pair(f"{style}_{function}_1", max_=1)
            kits.append({
                "key": f"{tier}_modular_{v}",
                "name": f"{tier.capitalize()} Modular {v}",
                "kind": "settlement",
                "tier": tier,
                "pack": "default",
                "weight": 2,
                "plotStride": 10,
                "building": 8,
                "modulesOnly": True,
                **grid,
                "entries": entries,
            })
    kits.append({
        "key": "city_gds_modular_1",
        "name": "G.D.S. City Modular 1",
        "kind": "city",
        "tier": "metropolis",
        "pack": "default",
        "weight": 2,
        "planetTypes": ["gds_desert"],
        "grid": 7,
        "districtSize": 32,
        "street": 4,
        "height": 20,
        "entries": [
            kit_entry("gds_housing_1", max_=30, weight=2), kit_entry("gds_housing_2", max_=30),
            kit_entry("gds_market_1", max_=4), kit_entry("gds_market_2", max_=4),
            kit_entry("gds_hall_1"), kit_entry("gds_hall_2"),
            kit_entry("gds_garden_1", max_=8), kit_entry("gds_garden_2", max_=8),
            kit_entry("gds_tower_1", max_=4), kit_entry("gds_tower_2", max_=4),
            # 2026-09: the services districts — at most one each, weighted so a fresh city all but always gets both
            kit_entry("gds_services_1", weight=3), kit_entry("gds_services_2", weight=3),
        ],
    })
    return kits


# ------------------------------------------------------------------ #1889 furnished copies of the complete templates

BED_HEAD = 62 << 2
BED_FOOT = 61 << 2
HEAD_TO_FOOT_YAW = {(0, 1): 0, (-1, 0): 1, (0, -1): 2, (1, 0): 3}  # ShapeCode yaw of the head-to-foot step

# key -> (room markers, beds as (head, foot)); read off the template layers: floor cells of a room, clear of its
# markers and doorways.
COPIES = {
    "river_hamlet": ([(6, 1, 6)], [((9, 1, 7), (9, 1, 6)), ((1, 1, 7), (1, 1, 6))]),
    "stone_roundhouse": ([(5, 1, 3)], [((3, 1, 6), (3, 1, 5))]),
    "stilt_hamlet": ([(4, 3, 4), (8, 3, 8)], [((1, 3, 1), (2, 3, 1)), ((11, 3, 9), (11, 3, 8))]),
    "walled_market": ([(4, 1, 4), (10, 1, 8)], [((2, 1, 2), (3, 1, 2)), ((12, 1, 10), (12, 1, 9))]),
}


def furnished_copies(pool):
    """The four complete templates as furnished copies (`<key>_home`); the originals become pin-only."""
    out = []
    for key, (rooms, beds) in COPIES.items():
        original = next(t for t in pool if t.get("key") == key)
        original["pinOnly"] = True
        occupied = {(c["x"], c["y"], c["z"]) for c in original["cells"]}
        cells = [dict(c) for c in original["cells"]]
        for x, y, z in rooms:
            assert (x, y, z) not in occupied, f"{key}: room marker on an occupied cell {(x, y, z)}"
            cells.append({"x": x, "y": y, "z": z, "kind": "marker", "id": "room"})
        for head, foot in beds:
            assert head not in occupied and foot not in occupied, f"{key}: bed on an occupied cell {head}/{foot}"
            yaw = HEAD_TO_FOOT_YAW[(foot[0] - head[0], foot[2] - head[2])]
            cells.append({"x": head[0], "y": head[1], "z": head[2], "kind": "block", "id": "bed", "shape": BED_HEAD | yaw})
            cells.append({"x": foot[0], "y": foot[1], "z": foot[2], "kind": "block", "id": "bed", "shape": BED_FOOT | yaw})
        copy = {k: v for k, v in original.items() if k not in ("cells", "legacyPool", "pinOnly")}
        copy["key"] = key + "_home"
        copy["cells"] = sorted(cells, key=lambda c: (c["x"], c["y"], c["z"]))
        out.append(copy)
    return out


def upsert(pool, new):
    index = next((i for i, t in enumerate(pool) if t.get("key") == new["key"]), None)
    if index is not None:
        pool[index] = new
    else:
        pool.append(new)


def main():
    pool = _load(POOL) if POOL.exists() else []
    for new in furnished_copies(pool) + modular_modules():
        upsert(pool, new)
    for new in (timber_cottage(), iron_flat()):
        existing = next((e for e in pool if e.get("key") == new["key"]), None)
        if existing is not None:
            pool[pool.index(existing)] = new
        else:
            pool.append(new)
        print(f"{new['key']}: {len(new['cells'])} cells, {new['width']}x{new['height']}x{new['length']}, role {new['role']}")
    _dump(POOL, pool)
    print(f"settlement pool: {len(pool)} templates")

    kit_pool = _load(KITS) if KITS.exists() else []
    for kit in default_kits() + modular_kits():
        existing = next((e for e in kit_pool if e.get("key") == kit["key"]), None)
        if existing is not None:
            kit_pool[kit_pool.index(existing)] = kit
        else:
            kit_pool.append(kit)
        print(f"{kit['key']}: {kit['kind']} {kit['tier']}, {len(kit['entries'])} entries")
    _dump(KITS, kit_pool)


if __name__ == "__main__":
    main()
