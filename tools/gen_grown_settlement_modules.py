# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the farm modules and the planned settlement kits of the grown-settlements package (#3602, generation 41).

Farm modules (#3595), each a village- and a town-style variant, human and alien, function `field` / `garden` / `pen`:

  - field   12 x 8: a fence ring open on -Z, tilled soil in four crop rows with a path between (#3596: their crops regrow
            slower than a greenhouse bed)
  - garden   6 x 6: a low fence with a gate on -Z, beds of berries and flowers
  - pen      9 x 9: a closed fence ring with the product crate set INTO its -Z side (reachable from the road), a trough and
            a lean-to; the `entrance` marker above the crate names the road side (#3585, #3597)

Planned kits (#3598): `<tier>_grown_1` (road first, #3587) and `<tier>_grid_1` (grown grid, #3586) for hamlet, village,
town and city, `minTerrainGeneration` 41, weighted so a fresh settlement is about 25 % the old modular kits (weight 2 + 2),
25 % grown grid (4) and 50 % grown (8). The G.D.S. city that grew (#3599): `city_gds_grown_1` (an irregular district map,
shifted rows, a suburb grown by the pin-only `city_gds_suburb_1`).

Appends / replaces by key only — every existing module and kit stays as it is.

Usage:
    python tools/gen_grown_settlement_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _dump, _load  # noqa: E402
from settlement_module_shapes import Module  # noqa: E402

POOL = DATA / "settlement_templates.json"
KITS = DATA / "structure_kits.json"
GENERATION = 41

FENCE = 16 << 2  # ShapeCode.Pack(BlockShape.Fence, yaw): rails along X at yaw 0, along Z at yaw 1
SLAB = 1 << 2


def fence_ring(m, block, gap=None, y=1):
    """A fence round the module's edge at row y; `gap` = (x0, x1) left open on -Z."""
    for x in range(m.w):
        for z in range(m.l):
            if x not in (0, m.w - 1) and z not in (0, m.l - 1):
                continue
            if gap and z == 0 and gap[0] <= x <= gap[1]:
                continue
            yaw = 0 if z in (0, m.l - 1) else 1
            m.block(x, y, z, block, shape=FENCE | yaw)


def style(alien, town):
    """(fence block, soil, crop rows, flowers) of a style."""
    if alien:
        return "@accent", "dirt", ("flora_cropshroom", "flora_cropshroom"), "flora_crystal"
    if town:
        return "@wall", "dirt", ("flora_cropgrain", "flora_cropberry"), "flora_marigold"
    return "wood_log", "dirt", ("flora_cropgrain", "flora_cropgrain"), "flora_marigold"


def field(alien, town, variant):
    m = Module(12, 3, 8)
    fence, soil, crops, _ = style(alien, town)
    fence_ring(m, fence, gap=(5, 6))
    crop = crops[variant % len(crops)]
    for z in range(1, m.l - 1):
        for x in range(1, m.w - 1):
            if z in (3, 6):
                m.block(x, 0, z, "@path")  # the walk between the rows
            else:
                m.block(x, 0, z, soil)
                m.block(x, 1, z, crop)
    m.marker(5, 1, 0, "entrance")
    return m


def garden(alien, town, variant):
    m = Module(6, 3, 6)
    fence, soil, crops, flower = style(alien, town)
    fence_ring(m, fence, gap=(2, 3))
    for z in range(1, m.l - 1):
        for x in range(1, m.w - 1):
            m.block(x, 0, z, soil)
            m.block(x, 1, z, "flora_cropberry" if (x + z + variant) % 2 == 0 else flower)
    m.marker(2, 1, 0, "entrance")
    return m


def pen(alien, town, variant):
    m = Module(9, 5, 9)
    fence, _, _, _ = style(alien, town)
    fence_ring(m, fence)
    m.block(4, 1, 0, "crate")  # the product crate in the fence line, reachable from the road
    m.marker(4, 2, 0, "entrance")
    # a trough of water by the east fence
    m.block(7, 0, 3, "water")
    m.block(7, 0, 4, "water")
    # a lean-to in the back corner: posts and a slab roof
    post = "@accent" if alien else ("@wall" if town else "wood_log")
    roof = "@roof" if alien else ("@wall" if town else "wood_planks")
    corner_x = 1 if variant == 0 else 5
    for x, z in ((corner_x, 5), (corner_x + 2, 5)):
        m.block(x, 1, z, post)
        m.block(x, 2, z, post)
    for x in range(corner_x, corner_x + 3):
        for z in range(5, 8):
            m.block(x, 3, z, roof, shape=SLAB)
    return m


FARMS = (("field", field, "Field"), ("garden", garden, "Garden"), ("pen", pen, "Pen"))


def module_entry(key, name, tier, function, m, alien):
    out = {
        "key": key,
        "name": name,
        "tier": tier,
        "kind": "settlement",
        "role": function,
        "kit": "settlement_grown",
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


def farm_modules():
    out = []
    for suffix, alien in (("", False), ("_alien", True)):
        prefix = "Alien " if alien else ""
        for tier, town in (("village", False), ("town", True)):
            for function, builder, nice in FARMS:
                for v in range(2):
                    m = builder(alien, town, v)
                    out.append(module_entry(f"{tier}_{function}_{v + 1}{suffix}", f"{prefix}{tier.capitalize()} {nice} {v + 1}",
                                            tier, function, m, alien))
    return out


def entry(module, weight=1, max_=1, required=False):
    return {"module": module, "min": 1 if required else 0, "max": max_, "required": required, "weight": weight}


def pair(key, **kw):
    return [entry(key, **kw), entry(key + "_alien", **kw)]


PROFESSIONS = ("clinic", "shop", "armory", "library", "stable", "quarry", "studio", "newsroom", "depot")


def planned_entries(tier):
    style_ = "village" if tier in ("hamlet", "village") else "town"
    e = []
    e += pair(f"{style_}_market_1", required=True)
    e += pair(f"{style_}_board_1", required=True)
    if tier != "hamlet":
        e += pair(f"{style_}_tavern_1", required=True)
    e += pair(f"{style_}_workshop_1", required=tier in ("town", "city"))
    houses = (1, 2, 3) if style_ == "village" else (1, 2)
    for n in houses:
        e += pair(f"{style_}_house_{n}", max_=30, weight=3 if n == 1 else 2)
    if tier == "city":
        e += pair("city_tall_house_1", max_=30, weight=3)
    e += pair(f"{style_}_greenhouse_1", max_=2)
    e += pair(f"{style_}_field_1", max_=2, weight=2, required=tier != "city")
    e += pair(f"{style_}_field_2", max_=1)
    e += pair(f"{style_}_garden_1", max_=2)
    e += pair(f"{style_}_garden_2", max_=1)
    e += pair(f"{style_}_pen_{1 if tier != 'town' else 2}", max_=1, weight=2, required=tier in ("village", "town"))
    for function in PROFESSIONS:
        e += pair(f"{style_}_{function}_1", max_=1)
    return e


def planned_kits():
    forms = {
        "hamlet": ["street", "cluster", "round", "bank"],
        "village": ["street", "green", "round", "cluster", "cross", "bank"],
        "town": ["street", "green", "cluster", "cross", "bank"],
        "city": ["street", "cluster", "cross", "bank"],
    }
    kits = []
    for tier in ("hamlet", "village", "town", "city"):
        for layout, weight in (("grown", 8), ("grid", 4)):
            kit = {
                "key": f"{tier}_{layout}_1",
                "name": f"{tier.capitalize()} {'Grown' if layout == 'grown' else 'Grown Grid'} 1",
                "kind": "settlement",
                "tier": tier,
                "pack": "default",
                "weight": weight,
                "minTerrainGeneration": GENERATION,
                "layout": layout,
                "entries": planned_entries(tier),
            }
            if layout == "grown":
                kit["forms"] = forms[tier]
            kits.append(kit)
    return kits


def city_kits():
    suburb = {
        "key": "city_gds_suburb_1",
        "name": "G.D.S. Suburb 1",
        "kind": "settlement",
        "tier": "town",
        "pack": "default",
        "weight": 1,
        "pinOnly": True,
        "planetTypes": ["gds_desert"],
        "minTerrainGeneration": GENERATION,
        "layout": "grown",
        "forms": ["street"],
        "flat": True,
        "entries": [entry("town_house_1", max_=12, weight=3), entry("town_house_2", max_=12, weight=2),
                    entry("town_workshop_1", max_=2), entry("town_workshop_2", max_=1), entry("town_depot_1"),
                    entry("town_shop_1"), entry("town_quarry_1")],
    }
    city = {
        "key": "city_gds_grown_1",
        "name": "G.D.S. City Grown 1",
        "kind": "city",
        "tier": "metropolis",
        "pack": "default",
        "weight": 6,
        "planetTypes": ["gds_desert"],
        "minTerrainGeneration": GENERATION,
        "grid": 7,
        "districtSize": 32,
        "street": 4,
        "height": 20,
        "grown": True,
        "suburbKit": "city_gds_suburb_1",
        # '.' = no district: the city's outline is irregular, its corners and two bays left to the desert
        "roleMap": [".RRGR..", "TRRMRRT", "RGHMGRR", "RMMPMMR", "RRGMGR.", "TRRHRRT", "..RGRR."],
        "entries": [
            entry("gds_housing_1", max_=30, weight=2), entry("gds_housing_2", max_=30),
            entry("gds_market_1", max_=4), entry("gds_market_2", max_=4),
            entry("gds_hall_1"), entry("gds_hall_2"),
            entry("gds_garden_1", max_=8), entry("gds_garden_2", max_=8),
            entry("gds_tower_1", max_=4), entry("gds_tower_2", max_=4),
            entry("gds_services_1", weight=3), entry("gds_services_2", weight=3),
        ],
    }
    return [suburb, city]


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
    for m in farm_modules():
        upsert(pool, m)
        print(f"{m['key']}: {m['width']}x{m['height']}x{m['length']}, {len(m['cells'])} cells")
    dump_lf(POOL, pool)
    kits = _load(KITS)
    for kit in planned_kits() + city_kits():
        upsert(kits, kit)
        print(f"{kit['key']}: {kit['kind']} {kit['tier']}, {len(kit['entries'])} entries")
    dump_lf(KITS, kits)


if __name__ == "__main__":
    main()
