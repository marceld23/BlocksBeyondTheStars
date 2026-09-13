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

Usage:
    python tools/gen_settlement_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _dump, _load  # noqa: E402

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


def main():
    pool = _load(POOL) if POOL.exists() else []
    for new in (timber_cottage(), iron_flat()):
        existing = next((e for e in pool if e.get("key") == new["key"]), None)
        if existing is not None:
            pool[pool.index(existing)] = new
        else:
            pool.append(new)
        print(f"{new['key']}: {len(new['cells'])} cells, {new['width']}x{new['height']}x{new['length']}, role {new['role']}")
    _dump(POOL, pool)


if __name__ == "__main__":
    main()
