# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the shipped station KIT modules and kits (#1875) into data/station_templates.json and
data/structure_kits.json.

A kit ("Small Station 1" … "Colossal Station 1") composes a station from modules that dock port to port:
iron rooms with a steel deck, a glass band, ceiling lights and 2×3 `door` ports centred on the walls their
function allows (ladder ports on the floor and ceiling of the ladder junction). Functions:

  hub        the arrival hall: spawn, consoles              4 door ports
  corridor   a 5-wide passage / a 4-way junction            2 / 4 ports
  ladder_junction  a junction with floor + ceiling ladder ports (two decks, large and up)
  cabins4 / cabins2  crew quarters off a lane: bed, locker, lamp, a `cabin` marker each, a slide door each
  canteen / bar      tables with chairs / a counter with benches (`room` marker → lounge)
  market     vendor marker, counters, crates
  mission    mission board marker, a table with a terminal
  medbay     heal tank marker, tanks, a bed (1 port)
  hydro      greenhouse marker, tray rows with crops (1 port)
  storage    crates along the walls
  hangar     hangar marker, a force-field mouth on −Z, never rotated (1 port on +Z)

The four original station templates (hub_outpost, pocket_waystation, observation_spire, twin_dock_bazaar)
become `pinOnly` (they stay for the worlds that pinned them, are never rolled again) and return as ROOM
modules (`*_room`) whose door gaps are widened into 2×3 door ports.

Three room sizes: s = 9×6×9 (small + medium kits), l = 11×7×11 (large), xl = 13×8×13 (huge + colossal).
Deterministic: running it again rewrites the same entries (matched by key), everything else stays.

Usage:
    python tools/gen_station_modules.py
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from merge_structure import DATA, _dump, _load  # noqa: E402

STATIONS = DATA / "station_templates.json"
KITS = DATA / "structure_kits.json"

HULL = "iron_wall"
DECK = "steel_floor"
GLASS = "glass"
LIGHT = "light_white"
CONSOLE = "carbon"
SCREEN = "data_cache"
CRATE = "crate"
TANK = "ice"
BED = "bed"
TRAY = "hydro_tray"
CROP = "flora_bush"
FIELD = "force_field"
LADDER = "ladder"
HEAL = "heal_tank"

# Packed shapes (ShapeCode.Pack(shape, yaw) = shape << 2 | yaw): Slab 1, Table 14, Chair 15, BedFoot 61, BedHead 62, Bench 63.
SLAB = 1 << 2
TABLE = 14 << 2
CHAIR = 15 << 2
BED_FOOT = 61 << 2
BED_HEAD = 62 << 2
BENCH = 63 << 2

SIZES = {"s": (9, 6, 9), "l": (11, 7, 11), "xl": (13, 8, 13)}
OLD_TEMPLATES = ("hub_outpost", "pocket_waystation", "observation_spire", "twin_dock_bazaar")


class Module:
    def __init__(self, key, name, function, tier, w, h, l, rotate=True):
        self.key, self.name, self.function, self.tier = key, name, function, tier
        self.w, self.h, self.l, self.rotate = w, h, l, rotate
        self.cells = {}

    def block(self, x, y, z, block_id, shape=0, port=""):
        c = {"x": x, "y": y, "z": z, "kind": "block", "id": block_id}
        if shape:
            c["shape"] = shape
        if port:
            c["port"] = port
        self.cells[(x, y, z)] = c

    def marker(self, x, y, z, marker_id):
        self.cells[(x, y, z)] = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker_id}

    def clear(self, x, y, z):
        self.cells.pop((x, y, z), None)

    def get(self, x, y, z):
        c = self.cells.get((x, y, z))
        return c["id"] if c and c["kind"] == "block" else None

    def shell(self, glass_band=True):
        """A hollow box: steel deck, iron walls and ceiling, a glass band at y 2..3 on the walls (not the corners)."""
        for x in range(self.w):
            for z in range(self.l):
                self.block(x, 0, z, DECK)
                self.block(x, self.h - 1, z, HULL)
                edge = x in (0, self.w - 1) or z in (0, self.l - 1)
                corner = x in (0, self.w - 1) and z in (0, self.l - 1)
                for y in range(1, self.h - 1):
                    if edge:
                        band = glass_band and not corner and 2 <= y <= 3
                        self.block(x, y, z, GLASS if band else HULL)

    def lights(self):
        for x, z in ((1, 1), (self.w - 2, 1), (1, self.l - 2), (self.w - 2, self.l - 2)):
            self.block(x, self.h - 1, z, LIGHT)
        if self.w >= 11:
            self.block(self.w // 2, self.h - 1, self.l // 2, LIGHT)

    def door_port(self, face, tag="door"):
        """A 2 wide × 3 high port centred on a wall (y 1..3)."""
        cx, cz = self.w // 2, self.l // 2
        for y in (1, 2, 3):
            if face == "x+":
                cells = ((self.w - 1, y, cz - 1), (self.w - 1, y, cz))
            elif face == "x-":
                cells = ((0, y, cz - 1), (0, y, cz))
            elif face == "z+":
                cells = ((cx - 1, y, self.l - 1), (cx, y, self.l - 1))
            else:
                cells = ((cx - 1, y, 0), (cx, y, 0))
            for x, yy, z in cells:
                self.block(x, yy, z, HULL, port=tag)

    def ladder_port(self, face):
        """A 2 × 2 ladder port in the floor ("y-") or ceiling ("y+") centre."""
        cx, cz = self.w // 2, self.l // 2
        y = 0 if face == "y-" else self.h - 1
        for x in (cx - 1, cx):
            for z in (cz - 1, cz):
                self.block(x, y, z, DECK if y == 0 else HULL, port="ladder")

    def entry(self):
        ordered = [self.cells[k] for k in sorted(self.cells)]
        return {
            "key": self.key,
            "name": self.name,
            "tier": self.tier,
            "kind": "station",
            "pack": "default",
            "weight": 1,
            "kit": KIT_KEYS[self.tier],
            "function": self.function,
            "width": self.w,
            "height": self.h,
            "length": self.l,
            "cells": ordered,
        }


KIT_KEYS = {
    "small": "station_small_1",
    "medium": "station_medium_1",
    "large": "station_large_1",
    "huge": "station_huge_1",
    "colossal": "station_colossal_1",
}
SIZE_OF_TIER = {"small": "s", "medium": "s", "large": "l", "huge": "xl", "colossal": "xl"}


def prefix(tier):
    return f"st_{tier}"


def hub(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_hub", "Arrival Hall", "hub", tier, w, h, l)
    m.shell()
    m.lights()
    for face in ("x+", "x-", "z+", "z-"):
        m.door_port(face)
    # A console bank along the −X wall (keeping the doorway columns clear), screens above.
    cz = l // 2
    for z in range(2, l - 2):
        if z in (cz - 1, cz):
            continue
        m.block(1, 1, z, CONSOLE)
        m.block(1, 2, z, SCREEN)
    m.marker(w // 2, 1, cz, "spawn")
    m.marker(w // 2 + 1, 1, 2, "room")
    return m


def corridor(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_corridor", "Corridor", "corridor", tier, 5, h, l)
    m.shell(glass_band=True)
    m.lights()
    m.door_port("z+")
    m.door_port("z-")
    return m


def junction(tier, ladder=False):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    key = f"{prefix(tier)}_ladder_junction" if ladder else f"{prefix(tier)}_junction"
    m = Module(key, "Ladder Junction" if ladder else "Junction", "corridor", tier, w, h, l)
    m.shell()
    m.lights()
    for face in ("x+", "x-", "z+", "z-"):
        m.door_port(face)
    if ladder:
        m.ladder_port("y-")
        m.ladder_port("y+")
    m.marker(1, 1, 1, "room")
    return m


def cabins(tier, rows):
    """Crew quarters: a 2-wide lane down the middle (x 5..6) with door ports on ±Z, cabins 3 wide on both sides,
    `rows` cabins per side along z (4 deep each, walls between). Every cabin: a two-cell bed, a crate, a lamp in the
    ceiling, a slide door from the lane, a `cabin` marker on its floor."""
    _, h, _ = SIZES[SIZE_OF_TIER[tier]]
    w = 12
    l = 1 + rows * 5
    m = Module(f"{prefix(tier)}_cabins{rows * 2}", f"Crew Quarters ({rows * 2})", "cabins", tier, w, h, l)
    m.shell()
    m.door_port("z+")
    m.door_port("z-")
    for x in (5, 6):
        m.block(x, h - 1, l // 2, LIGHT)
    for r in range(rows):
        z0 = 1 + r * 5  # cabin z0..z0+3, wall at z0+4 (the outer wall for the last row)
        for z in range(z0, z0 + 5):
            for y in range(1, h - 1):
                m.block(4, y, z, HULL)  # lane walls
                m.block(7, y, z, HULL)
        if r < rows - 1:
            for x in range(1, w - 1):
                for y in range(1, h - 1):
                    if x in (5, 6):
                        continue
                    m.block(x, y, z0 + 4, HULL)  # wall between the cabin rows
        for side, xs in (("left", (1, 2, 3)), ("right", (8, 9, 10))):
            door_x = 4 if side == "left" else 7
            m.clear(door_x, 1, z0 + 1)
            m.clear(door_x, 2, z0 + 1)
            m.marker(door_x, 1, z0 + 1, "door_slide")
            bed_x = 1 if side == "left" else 10
            m.block(bed_x, 1, z0 + 2, BED, shape=BED_HEAD)  # head at the far end, foot toward +Z … the pair lies along z
            m.block(bed_x, 1, z0 + 3, BED, shape=BED_FOOT)
            m.block(bed_x, 1, z0, CRATE)
            m.block(xs[1], h - 1, z0 + 1, LIGHT)
            m.marker(xs[1], 1, z0 + 2, "cabin")
    return m


def canteen(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_canteen", "Canteen", "canteen", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x+")
    m.door_port("x-")
    # A serving counter along the +Z wall, tables with chairs in two rows.
    for x in range(2, w - 2):
        m.block(x, 1, l - 2, DECK, shape=SLAB)
    cz = l // 2
    for z in (2, cz + 1) if l >= 11 else (2,):
        for x in range(2, w - 2, 3):
            if x in (w // 2 - 1, w // 2) and z == cz + 1:
                continue
            m.block(x, 1, z, DECK, shape=TABLE)
            m.block(x + 1, 1, z, DECK, shape=CHAIR | 3)  # backrest away from the table (+X)
            if x - 1 >= 1:
                m.block(x - 1, 1, z, DECK, shape=CHAIR | 1)  # … and −X
    m.marker(w // 2, 1, cz, "room")
    return m


def bar(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_bar", "Bar", "bar", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x+")
    m.door_port("x-")
    for x in range(2, w - 2):
        m.block(x, 1, l - 2, DECK, shape=SLAB)  # the counter
        m.block(x, 1, l - 3, DECK, shape=BENCH)  # benches facing it (backrest toward +Z … the counter side)
    m.block(w // 2, 2, l - 2, SCREEN)
    m.marker(w // 2, 1, 2, "room")
    return m


def market(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_market", "Market Hall", "market", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x+")
    m.door_port("x-")
    m.door_port("z-")
    for x in range(2, w - 2):
        m.block(x, 1, l - 2, DECK, shape=SLAB)
    m.block(1, 1, l - 2, CRATE)
    m.block(w - 2, 1, l - 2, CRATE)
    m.block(w - 2, 2, l - 2, CRATE)
    m.marker(w // 2, 1, l - 3, "vendor")
    m.marker(2, 1, 2, "room")
    return m


def mission(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_mission", "Mission Office", "mission", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x+")
    m.door_port("x-")
    m.block(w // 2, 1, l - 2, DECK, shape=TABLE)
    m.block(w // 2, 2, l - 2, SCREEN)
    m.marker(w // 2, 1, l - 3, "mission_board")
    m.marker(2, 1, 2, "room")
    return m


def medbay(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_medbay", "Medbay", "medbay", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x-")
    for z in (1, l - 2):
        m.block(w - 2, 1, z, TANK)
        m.block(w - 2, 2, z, TANK)
    m.block(w - 2, 1, l // 2, HEAL)
    m.block(2, 1, 1, BED, shape=BED_HEAD | 3)
    m.block(3, 1, 1, BED, shape=BED_FOOT | 3)
    m.marker(w - 3, 1, l // 2, "heal_tank")
    m.marker(2, 1, l - 2, "room")
    return m


def hydro(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_hydro", "Hydroponics", "hydro", tier, w, h, l)
    m.shell()
    m.lights()
    m.door_port("x-")
    cx, cz = w // 2, l // 2
    for z in range(2, l - 2):
        for x in (2, w - 3):
            if x == 2 and z in (cz - 1, cz):
                continue  # the lane in from the -X door stays a deck walkway (#1901: two rows free behind every door)
            m.block(x, 0, z, TRAY)  # the deck plate becomes the growing bed (two cells in from the hull)
            if (x + z) % 5:
                m.block(x, 1, z, CROP)
    m.marker(cx, 1, l // 2, "greenhouse")
    return m


def storage(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_storage", "Store Room", "storage", tier, w, h, l)
    m.shell(glass_band=False)
    m.lights()
    m.door_port("x+")
    m.door_port("x-")
    cz = l // 2
    for z in range(1, l - 1, 2):
        if z in (cz - 1, cz):
            continue  # the way in from the ±X ports stays clear
        m.block(1, 1, z, CRATE)
        m.block(w - 2, 1, z, CRATE)
        if z % 4 == 1:
            m.block(1, 2, z, CRATE)
    m.marker(w // 2, 1, l // 2, "room")
    return m


def hangar(tier):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = Module(f"{prefix(tier)}_hangar", "Hangar", "hangar", tier, w, h, l, rotate=False)
    m.shell(glass_band=False)
    m.lights()
    m.door_port("z+")
    top = min(h - 2, 5 if h >= 8 else 3)
    for x in range(1, w - 1):
        for y in range(1, top + 1):
            m.block(x, y, 0, FIELD)  # the docking mouth: you see space, nobody falls out
    m.block(1, 1, l - 2, CRATE)
    m.block(1, 2, l - 2, CRATE)
    m.block(w - 2, 1, l - 2, CRATE)
    m.marker(w // 2, 1, l // 2, "hangar")
    return m


def modules_for(tier):
    mods = [hub(tier), corridor(tier), junction(tier), cabins(tier, 2), cabins(tier, 1), canteen(tier), bar(tier),
            market(tier), mission(tier), medbay(tier), hydro(tier), storage(tier), hangar(tier)]
    if tier in ("large", "huge", "colossal"):
        mods.append(junction(tier, ladder=True))
    return mods


def entry(module, required=False, minimum=0, maximum=1, weight=1, rotate=None):
    e = {"module": module, "min": max(minimum, 1 if required else 0), "max": maximum, "required": required, "weight": weight}
    if rotate is False:
        e["rotate"] = False
    return e


def kit(tier, name, entries, modules_min, modules_max):
    return {
        "key": KIT_KEYS[tier],
        "name": name,
        "kind": "station",
        "tier": tier,
        "pack": "default",
        "weight": 2,
        "modulesMin": modules_min,
        "modulesMax": modules_max,
        "start": f"{prefix(tier)}_hub",
        "entries": entries,
    }


def kits():
    p = prefix
    return [
        kit("small", "Small Station 1", [
            entry(f"{p('small')}_hub", required=True),
            entry(f"{p('small')}_hangar", required=True, rotate=False),
            entry(f"{p('small')}_cabins4", required=True),
            entry(f"{p('small')}_market", required=True),
            entry(f"{p('small')}_canteen", maximum=1, weight=3),
            entry(f"{p('small')}_storage", maximum=1),
            entry(f"{p('small')}_corridor", maximum=2, weight=2),
        ], 5, 8),
        kit("medium", "Medium Station 1", [
            entry(f"{p('medium')}_hub", required=True),
            entry(f"{p('medium')}_hangar", required=True, rotate=False),
            entry(f"{p('medium')}_cabins4", required=True),
            entry(f"{p('medium')}_cabins2", required=True),
            entry(f"{p('medium')}_market", required=True),
            entry(f"{p('medium')}_mission", required=True),
            entry(f"{p('medium')}_canteen", required=True),
            entry(f"{p('medium')}_medbay", maximum=1, weight=2),
            entry(f"{p('medium')}_hydro", maximum=1, weight=2),
            entry(f"{p('medium')}_bar", maximum=1),
            entry(f"{p('medium')}_storage", maximum=1),
            entry(f"{p('medium')}_corridor", maximum=2, weight=2),
            entry(f"{p('medium')}_junction", maximum=1),
        ], 8, 12),
        kit("large", "Large Station 1", [
            entry(f"{p('large')}_hub", required=True),
            entry(f"{p('large')}_hangar", required=True, rotate=False),
            entry(f"{p('large')}_cabins4", required=True, minimum=2, maximum=2),
            entry(f"{p('large')}_cabins2", required=True),
            entry(f"{p('large')}_market", required=True),
            entry(f"{p('large')}_mission", required=True),
            entry(f"{p('large')}_canteen", required=True),
            entry(f"{p('large')}_medbay", required=True),
            entry(f"{p('large')}_hydro", required=True),
            entry(f"{p('large')}_bar", maximum=1, weight=2),
            entry(f"{p('large')}_storage", maximum=1),
            entry(f"{p('large')}_corridor", maximum=3, weight=2),
            entry(f"{p('large')}_junction", maximum=1),
            entry(f"{p('large')}_ladder_junction", maximum=2, weight=2),
        ], 11, 16),
        kit("huge", "Huge Station 1", [
            entry(f"{p('huge')}_hub", required=True),
            entry(f"{p('huge')}_hangar", required=True, rotate=False),
            entry(f"{p('huge')}_cabins4", required=True, minimum=3, maximum=3),
            entry(f"{p('huge')}_cabins2", required=True),
            entry(f"{p('huge')}_market", required=True, maximum=2),
            entry(f"{p('huge')}_mission", required=True),
            entry(f"{p('huge')}_canteen", required=True),
            entry(f"{p('huge')}_bar", required=True),
            entry(f"{p('huge')}_medbay", required=True),
            entry(f"{p('huge')}_hydro", required=True),
            entry(f"{p('huge')}_storage", required=True),
            entry(f"{p('huge')}_corridor", maximum=4, weight=2),
            entry(f"{p('huge')}_junction", maximum=2),
            entry(f"{p('huge')}_ladder_junction", required=True, maximum=2, weight=2),
        ], 15, 22),
        kit("colossal", "Colossal Station 1", [
            entry(f"{p('colossal')}_hub", required=True),
            entry(f"{p('colossal')}_hangar", required=True, rotate=False),
            entry(f"{p('colossal')}_cabins4", required=True, minimum=5, maximum=5),
            entry(f"{p('colossal')}_market", required=True, minimum=2, maximum=2),
            entry(f"{p('colossal')}_mission", required=True),
            entry(f"{p('colossal')}_canteen", required=True, maximum=2),
            entry(f"{p('colossal')}_bar", required=True),
            entry(f"{p('colossal')}_medbay", required=True),
            entry(f"{p('colossal')}_hydro", required=True, maximum=2),
            entry(f"{p('colossal')}_storage", required=True, maximum=2),
            entry(f"{p('colossal')}_corridor", maximum=5, weight=2),
            entry(f"{p('colossal')}_junction", maximum=3),
            entry(f"{p('colossal')}_ladder_junction", required=True, minimum=2, maximum=3, weight=2),
        ], 20, 30),
    ]


def room_from_template(t):
    """A copy of one of the original station templates as a ROOM module: its door gaps (door_slide blocks in the
    outer wall, one cell wide) become 2×3 door ports, the interior gets a `room` marker."""
    w, h, l = t["width"], t["height"], t["length"]
    cells = {(c["x"], c["y"], c["z"]): dict(c) for c in t["cells"]}
    blocks = {k: v for k, v in cells.items() if v["kind"] == "block"}
    doors = sorted(k for k, v in blocks.items() if v["id"].startswith("door"))

    def air(x, y, z):
        c = cells.get((x, y, z))
        return c is None or c["kind"] != "block"

    for (x, y, z) in doors:
        if z in (0, l - 1):
            second = x + 1 if x + 1 < w - 1 and air(x + 1, 1, 1 if z == 0 else l - 2) else x - 1
            columns = [(x, z), (second, z)]
        else:
            second = z + 1 if z + 1 < l - 1 and air(1 if x == 0 else w - 2, 1, z + 1) else z - 1
            columns = [(x, z), (x, second)]
        for (px, pz) in columns:
            for py in (1, 2, 3):
                if py >= h - 1:
                    continue
                cells[(px, py, pz)] = {"x": px, "y": py, "z": pz, "kind": "block", "id": HULL, "port": "door"}

    # A room marker on the first free interior floor cell.
    for x in range(1, w - 1):
        for z in range(1, l - 1):
            if (x, 1, z) not in cells and (x, 0, z) in blocks:
                cells[(x, 1, z)] = {"x": x, "y": 1, "z": z, "kind": "marker", "id": "room"}
                break
        else:
            continue
        break

    ordered = [cells[k] for k in sorted(cells)]
    function = "market" if t["key"] == "twin_dock_bazaar" else "room"
    return {
        "key": t["key"].replace("_bazaar", "").replace("hub_outpost", "outpost") + ("_hall" if function == "market" else "_room"),
        "name": t["name"] + " (room)",
        "tier": t["tier"],
        "kind": "station",
        "pack": "default",
        "weight": 1,
        "kit": KIT_KEYS[t["tier"]] if t["tier"] in KIT_KEYS else KIT_KEYS["medium"],
        "function": function,
        "width": w,
        "height": h,
        "length": l,
        "cells": ordered,
    }


def upsert(pool, new):
    existing = next((e for e in pool if e.get("key") == new["key"]), None)
    if existing is not None:
        pool[pool.index(existing)] = new
    else:
        pool.append(new)


def main():
    pool = _load(STATIONS) if STATIONS.exists() else []
    rooms = []
    for t in pool:
        if t.get("key") in OLD_TEMPLATES:
            t["pinOnly"] = True
            rooms.append(room_from_template(t))

    for tier in KIT_KEYS:
        for m in modules_for(tier):
            e = m.entry()
            upsert(pool, e)
            print(f"{e['key']}: {len(e['cells'])} cells, {e['width']}x{e['height']}x{e['length']}, {e['function']}")

    for r in rooms:
        upsert(pool, r)
        print(f"{r['key']}: {len(r['cells'])} cells (from a pinned template), {r['function']}")

    _dump(STATIONS, pool)

    kit_pool = _load(KITS) if KITS.exists() else []
    for k in kits():
        # The rooms derived from the old templates join the medium and large kits as optional single rooms.
        for r in rooms:
            if r["kit"] == k["key"]:
                k["entries"].append(entry(r["key"], maximum=1))
        upsert(kit_pool, k)
        print(f"{k['key']}: {len(k['entries'])} entries, {k['modulesMin']}..{k['modulesMax']} modules")
    _dump(KITS, kit_pool)


if __name__ == "__main__":
    main()
