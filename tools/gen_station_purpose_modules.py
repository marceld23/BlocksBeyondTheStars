# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the station rooms and kits made for a PURPOSE (the lively places, docs/developer/LIVELY_PLACES.md §8) into
data/station_templates.json and data/structure_kits.json.

A kit station that is composed for the first time in a world of terrain generation 46 or later gets a purpose — trade,
mining or research. A kit may name the purposes it is made for (`purposes`); such a kit is drawn for that purpose only.
This script writes one kit per tier and purpose (15) and the rooms only they use (8 per tier):

  trade     bazaar    a market hall with three stalls (three `vendor` posts)          function market
            customs   a scanner arch across the passage, a desk, sealed freight       function room
  mining    ore_bay   four heaps of ore, freight crates                               function storage
            smelter   two lit forges on the far wall (they work), ore and crates      function room
            assay     a counter, test panels, a row of sample ores                    function room
  research  lab       benches with instruments, tanks, a table                        function room
            observatory  glass walls and a glass roof, a telescope, consoles          function room
            archive   aisles of data shelves, a reading desk                          function room

Every room is a passage — at least two door ports — so a kit that requires it never runs out of ports to dock on (a
dead end takes a port and gives none back; three required dead ends on top of the medbay and the hydroponics did not
fit). Every room keeps the cross of walkways behind its doors free (two cells in from every port, #1901). The rooms are
written in the compact body (`palette` + `rows`, #3485); cells with settings — ports and markers — stay in `cells`.

The purpose kits take the entries of the classic kit of their tier (read from data/structure_kits.json, so the
profession rooms and the rooms made from the old templates stay in) and add their own rooms. Each purpose has its own
floor plan (#3871, `LAYOUT`): a trade station a concourse of corridors around a crossroads, a mining station a long
spine of corridors without a junction, a research station decks (one ladder junction more from large on). The classic kits and their
modules are not touched: they have no purpose and stay what every older world and every station without one draws.

Deterministic: running it again rewrites the same entries (matched by key), everything else stays.

Usage:
    python tools/gen_station_purpose_modules.py
"""
import copy
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gen_station_modules import (  # noqa: E402
    BENCH, CHAIR, CONSOLE, CRATE, DECK, GLASS, HULL, KIT_KEYS, KITS, LIGHT, SCREEN, SIZE_OF_TIER, SIZES, SLAB, STATIONS,
    TABLE, TANK, Module, entry, prefix, upsert,
)
from merge_structure import _load  # noqa: E402

LIVELY_GENERATION = 46  # WorldDescription.LivelyPlacesGeneration
PURPOSES = ("trade", "mining", "research")
PURPOSE_NAMES = {"trade": "Trade", "mining": "Mining", "research": "Research"}

# One room works, by the maintainer's decision (2026-11-20): the smelter's furnaces are real, lit `forge` blocks — a
# refinery for whoever stands beside them (GameServer.StationAvailable), the service a mining station offers. Every
# other room stays decoration: no block that WORKS where it stands (an `algae_tank` is an algae station, so the
# laboratory shows the ice tanks the medbay has), and nothing of the Crystal Net anywhere — a forge no conduit touches
# is on no circuit.
FURNACE = "forge"
PANEL = "metal_panel"
ENGINE = "engine_panel"
LAB = "lab_panel"
FREIGHT = "transport_crate"
WARM = "strip_light_warm"
CYAN = "strip_light_cyan"

# A cube's front rides in the descriptor's up-face field (CubeFacing.Pack(face) = face << 8): 3 = −X.
FRONT_MINUS_X = 3 << 8

LAB_SOUND = "sound=amb_station_lab;radius=12;volume=0.5"

ORES = ("iron_ore", "copper_ore", "titanium_ore", "lead_ore")
SAMPLES = ("gold_ore", "silver_ore", "diamond_ore", "platinum_ore", "cobalt_ore", "uranium_ore")


class PurposeModule(Module):
    """A station module that may carry a marker with settings, and that knows its walkways."""

    def marker(self, x, y, z, marker_id, config=""):
        c = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker_id}
        if config:
            c["config"] = config
        assert (x, y, z) not in self.cells, f"{self.key}: marker {marker_id} on an occupied cell {(x, y, z)}"
        self.cells[(x, y, z)] = c

    @property
    def cx(self):
        return self.w // 2

    @property
    def cz(self):
        return self.l // 2

    def lane(self, x, z):
        """Whether a column lies on the cross of walkways between the door ports (two wide each way)."""
        return x in (self.cx - 1, self.cx) or z in (self.cz - 1, self.cz)

    def put(self, x, y, z, block_id, shape=0):
        """A furnishing block — never on a walkway, never outside the room's interior."""
        assert 1 <= x <= self.w - 2 and 1 <= z <= self.l - 2 and 1 <= y <= self.h - 2, f"{self.key}: {(x, y, z)} is not inside"
        assert not self.lane(x, z), f"{self.key}: {block_id} at {(x, y, z)} blocks a walkway"
        self.block(x, y, z, block_id, shape)

    def ceiling_strip(self, block_id):
        """A light strip along the ceiling's middle (it replaces hull cells, so the room stays sealed)."""
        for z in range(2, self.l - 2):
            self.block(self.cx, self.h - 1, z, block_id)


def room(tier, function_key, name, function, ports, glass_band=True):
    w, h, l = SIZES[SIZE_OF_TIER[tier]]
    m = PurposeModule(f"{prefix(tier)}_{function_key}", name, function, tier, w, h, l)
    m.shell(glass_band=glass_band)
    m.lights()
    for face in ports:
        m.door_port(face)
    return m


# ------------------------------------------------------------------ trade

def bazaar(tier):
    """Three stalls — a counter toward the walkway, the trader behind it, goods against the wall — and a corner to sit."""
    m = room(tier, "bazaar", "Bazaar Hall", "market", ("x+", "x-", "z-"))
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    # The two stalls on the far side (+Z): counters along the row beside the cross walkway.
    for x in range(1, cx - 1):
        m.put(x, 1, cz + 1, DECK, SLAB)
    for x in range(cx + 1, w - 1):
        m.put(x, 1, cz + 1, DECK, SLAB)
    m.put(1, 1, l - 2, CRATE)
    m.put(1, 2, l - 2, CRATE)
    m.put(w - 2, 1, l - 2, FREIGHT)
    m.put(w - 2, 2, l - 2, CRATE)
    m.marker(cx - 2, 1, cz + 2, "vendor")
    m.marker(cx + 1, 1, cz + 2, "vendor")
    # The third stall by the −Z door (+X side): its counter along the walkway to the door.
    for z in range(1, cz - 1):
        m.put(cx + 1, 1, z, DECK, SLAB)
    m.put(w - 2, 1, 1, CRATE)
    m.marker(cx + 2, 1, cz - 2, "vendor")
    # The corner left of the door: a bench under a warm light, a crate to put things on.
    for z in range(1, cz - 1):
        m.put(1, 1, z, DECK, BENCH | 1)
    m.put(cx - 2, 1, 1, CRATE)
    m.block(2, m.h - 1, 2, WARM)
    m.block(w - 3, m.h - 1, l - 3, WARM)
    m.marker(cx - 2, 1, cz - 2, "room")
    return m


def customs(tier):
    """A scanner arch across the passage, the officer's desk and sealed freight waiting to be cleared."""
    m = room(tier, "customs", "Customs Office", "room", ("x+", "x-"))
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    # The arch stands in the middle of the passage: two posts beside the walkway, a lit lintel over it.
    for y in (1, 2, 3):
        m.block(cx, y, cz - 2, PANEL)
        m.block(cx, y, cz + 1, PANEL)
    m.block(cx, 3, cz - 1, CYAN)
    m.block(cx, 3, cz, CYAN)
    # The desk in the far corner.
    m.put(w - 3, 1, l - 2, DECK, TABLE)
    m.put(w - 2, 1, l - 2, CONSOLE)
    m.put(w - 2, 2, l - 2, SCREEN)
    m.put(w - 3, 1, l - 3, DECK, CHAIR)
    # Freight: sealed crates on both sides of the way in.
    for z in range(1, cz - 1):
        m.put(1, 1, z, FREIGHT)
        if z % 2:
            m.put(1, 2, z, FREIGHT)
    m.put(2, 1, 1, FREIGHT)
    for z in range(cz + 1, l - 1):
        m.put(1, 1, z, CRATE)
    m.put(w - 2, 1, 1, FREIGHT)
    m.put(w - 2, 2, 1, FREIGHT)
    m.put(w - 3, 1, 1, CRATE)
    m.marker(cx + 1, 1, cz + 1, "room")
    return m


# ------------------------------------------------------------------ mining

def ore_bay(tier):
    """Four heaps of ore — one kind each, high against the walls — and freight crates by the passage."""
    m = room(tier, "ore_bay", "Ore Bay", "storage", ("x+", "x-"), glass_band=False)
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    quadrants = (
        (range(1, cx - 1), range(1, cz - 1), 1, 1),
        (range(cx + 1, w - 1), range(1, cz - 1), w - 2, 1),
        (range(1, cx - 1), range(cz + 1, l - 1), 1, l - 2),
        (range(cx + 1, w - 1), range(cz + 1, l - 1), w - 2, l - 2),
    )
    for ore, (xs, zs, corner_x, corner_z) in zip(ORES, quadrants):
        for x in xs:
            for z in zs:
                # A heap falls off from its corner: two high in the corner, one high beside it, nothing by the walkway.
                far = abs(x - corner_x) + abs(z - corner_z)
                if far == 0:
                    m.put(x, 1, z, ore)
                    m.put(x, 2, z, ore)
                elif far == 1 or (far == 2 and x != corner_x and z != corner_z and len(xs) > 2):
                    m.put(x, 1, z, ore)
    m.put(cx - 2, 1, cz + 1, FREIGHT)  # by the passage, off the heap
    m.marker(cx + 1, 1, cz + 1, "room")
    return m


def smelter(tier):
    """Two furnaces on the far wall (lit forges a visitor can smelt at, their flues above them), one each side of the far
    door; ore to feed them and crates to carry the plates off."""
    m = room(tier, "smelter", "Smelter", "room", ("x-", "x+"), glass_band=False)
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    for z in (cz - 2, cz + 1):
        m.put(w - 2, 1, z, FURNACE, FRONT_MINUS_X)
        m.put(w - 2, 2, z, ENGINE)
        m.put(w - 2, 3, z, ENGINE)
    m.block(w - 3, m.h - 1, cz - 2, WARM)
    m.block(w - 3, m.h - 1, cz + 1, WARM)
    # Ore to feed them, in the corners beside the furnaces.
    m.put(w - 2, 1, 1, "iron_ore")
    m.put(w - 2, 2, 1, "iron_ore")
    m.put(w - 3, 1, 1, "iron_ore")
    m.put(w - 2, 1, l - 2, "copper_ore")
    m.put(w - 3, 1, l - 2, "copper_ore")
    # The finished plates leave in crates by the door.
    m.put(1, 1, 1, CRATE)
    m.put(1, 2, 1, CRATE)
    m.put(2, 1, 1, FREIGHT)
    m.put(1, 1, l - 2, FREIGHT)
    m.marker(cx - 2, 1, cz + 2, "room")
    return m


def assay(tier):
    """A counter across the far side, test panels behind it and a row of sample ores to compare against."""
    m = room(tier, "assay", "Assay Office", "room", ("x+", "x-"))
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    for x in range(2, w - 2):
        if x in (cx - 1, cx):
            continue
        m.put(x, 1, cz + 1, DECK, SLAB)  # the counter, with a way through in the middle
    row = [x for x in range(1, w - 1) if x not in (cx - 1, cx)]
    for i, x in enumerate(row):
        if i % 2 == 0:
            m.put(x, 1, l - 2, SAMPLES[(i // 2) % len(SAMPLES)])
        else:
            m.put(x, 1, l - 2, LAB)
            m.put(x, 2, l - 2, SCREEN)
    # A desk on the near side.
    m.put(1, 1, 1, DECK, TABLE)
    m.put(2, 1, 1, DECK, CHAIR | 3)
    m.put(w - 2, 1, 1, FREIGHT)
    m.put(w - 2, 2, 1, CRATE)
    m.marker(cx + 2, 1, cz - 2, "room")
    return m


# ------------------------------------------------------------------ research

def lab(tier):
    """Instrument benches along both side walls, tanks in the far corners, a table to work at."""
    m = room(tier, "lab", "Laboratory", "room", ("x-", "x+"))
    w, l, cx, cz = m.w, m.l, m.cx, m.cz
    for x in range(2, w - 2):
        if x in (cx - 1, cx):
            continue
        m.put(x, 1, 1, CONSOLE)
        m.put(x, 2, 1, SCREEN if x % 2 else LAB)
        m.put(x, 1, l - 2, LAB)
        if x % 2 == 0:
            m.put(x, 2, l - 2, SCREEN)
    for z in (1, l - 2):
        m.put(w - 2, 1, z, TANK)
        m.put(w - 2, 2, z, TANK)
    m.put(w - 2, 1, cz - 2, DECK, TABLE)
    m.put(w - 2, 1, cz + 1, DECK, TABLE)
    m.put(w - 3, 1, cz + 1, DECK, CHAIR | 1)
    m.ceiling_strip(CYAN)
    m.marker(cx + 1, 1, cz - 2, "room")
    m.marker(cx + 1, 1, cz + 2, "sound", LAB_SOUND)
    return m


def observatory(tier):
    """Glass on three sides and overhead, a telescope aimed out through the roof, consoles to read it from."""
    m = room(tier, "observatory", "Observatory", "room", ("x-", "x+"))
    w, h, l, cx, cz = m.w, m.h, m.l, m.cx, m.cz
    # More glass: the band reaches up to the ceiling on the three walls without a door.
    for y in range(4, h - 1):
        for x in range(1, w - 1):
            m.block(x, y, 0, GLASS)
            m.block(x, y, l - 1, GLASS)
        for z in range(1, l - 1):
            m.block(w - 1, y, z, GLASS)
    # … and a window in the roof over the telescope.
    for x in range(cx + 1, w - 2):
        for z in range(cz - 2, cz + 2):
            if m.get(x, h - 1, z) != LIGHT:
                m.block(x, h - 1, z, GLASS)
    # The telescope: a foot beside the walkway, the tube stepping up and out, a lens at its end.
    m.put(cx + 1, 1, cz + 1, PANEL)
    m.put(cx + 1, 2, cz + 1, ENGINE)
    m.put(cx + 2, 3, cz + 1, PANEL)
    m.put(cx + 3, 4, cz + 1, GLASS)
    # Consoles along the near wall, a chair in front of each second one.
    for x in range(2, w - 2):
        if x in (cx - 1, cx):
            continue
        m.put(x, 1, 1, CONSOLE)
        m.put(x, 2, 1, SCREEN)
        if x % 2 == 0:
            m.put(x, 1, 2, DECK, CHAIR)
    m.marker(cx - 2, 1, cz + 2, "room")
    m.marker(cx + 2, 1, cz + 2, "sound", LAB_SOUND)
    return m


def archive(tier):
    """Aisles of data shelves on both sides of the walkway and a reading desk by the far door."""
    m = room(tier, "archive", "Archive", "room", ("x-", "x+"))
    w, h, l, cx, cz = m.w, m.h, m.l, m.cx, m.cz
    high = 3 if h >= 7 else 2
    for x in range(2, w - 2, 2):
        if x in (cx - 1, cx):
            continue
        for z in list(range(1, cz - 1)) + list(range(cz + 1, l - 1)):
            m.put(x, 1, z, CONSOLE)
            for y in range(2, high + 1):
                m.put(x, y, z, SCREEN)
    m.put(w - 2, 1, cz - 2, DECK, TABLE)
    m.put(w - 2, 1, cz + 1, DECK, CHAIR | 2)
    m.ceiling_strip(CYAN)
    m.marker(1, 1, 1, "room")
    return m


ROOMS = {
    "trade": (bazaar, customs),
    "mining": (ore_bay, smelter, assay),
    "research": (lab, observatory, archive),
}


# ------------------------------------------------------------------ entries

def compact(m):
    """The module as a template entry with the compact body (TemplateBody, #3485): a palette of looks and one run-length
    string per (y, z) row — "y,z:x.count.palette …". Markers and port cells keep their settings and stay in `cells`."""
    palette, index, rows, listed = [], {}, [], []
    by_row = {}
    for (x, y, z), c in m.cells.items():
        if c["kind"] != "block" or c.get("port") or c.get("mode"):
            listed.append(c)
        else:
            by_row.setdefault((y, z), []).append((x, c))
    for (y, z) in sorted(by_row):
        runs = []
        for x, c in sorted(by_row[(y, z)], key=lambda p: p[0]):
            look = (c["id"], c.get("shape", 0))
            if look not in index:
                index[look] = len(palette)
                p = {"id": c["id"]}
                if look[1]:
                    p["shape"] = look[1]
                palette.append(p)
            i = index[look]
            if runs and runs[-1][2] == i and runs[-1][0] + runs[-1][1] == x:
                runs[-1][1] += 1
            else:
                runs.append([x, 1, i])
        rows.append(f"{y},{z}:" + " ".join(f"{x}.{n}.{p}" for x, n, p in runs))
    listed.sort(key=lambda c: (c["x"], c["y"], c["z"]))
    return palette, rows, listed


def module_entry(m, purpose):
    palette, rows, listed = compact(m)
    return {
        "key": m.key,
        "name": m.name,
        "tier": m.tier,
        "kind": "station",
        "pack": "default",
        "weight": 1,
        "kit": purpose_kit_key(m.tier, purpose),
        "function": m.function,
        "width": m.w,
        "height": m.h,
        "length": m.l,
        "palette": palette,
        "rows": rows,
        "cells": listed,
    }


def purpose_kit_key(tier, purpose):
    return f"station_{tier}_{purpose}_1"


# Per purpose: the own rooms as (room, required from this tier on — "" = on every tier; optional below it), and what
# the purpose changes on the classic entries: (module suffix, minimum, maximum).
TIER_ORDER = ("small", "medium", "large", "huge", "colossal")
OWN_ROOMS = {
    "trade": (("bazaar", ""), ("customs", "medium")),
    "mining": (("ore_bay", ""), ("smelter", "medium"), ("assay", "large")),
    "research": (("lab", ""), ("observatory", "medium"), ("archive", "large")),
}
# A trade station staffs four stalls (the market's and the bazaar's three), so it needs the cabins for them, and it
# keeps two store rooms; the others keep the classic numbers.
CLASSIC_CHANGES = {
    "trade": {"small": (("cabins2", 1, 1),), "medium": (("storage", 2, 2),), "large": (("storage", 2, 2),),
              "huge": (("storage", 2, 2),), "colossal": (("storage", 2, 3),)},
    "mining": {},
    "research": {},
}
# The floor plan of a purpose (#3871): which passages its kit requires or leaves out, per tier, as
# (module suffix, minimum, maximum) — a minimum of 0 with a maximum of 0 leaves the module out. Only modules the tier's
# classic kit has are named (a small station has corridors only, ladder junctions start at large; huge and colossal
# stations need their ladder junctions to fit, so trade and mining keep them).
#   trade     a concourse: two corridors and a crossroads junction
#   mining    a spine: a long line of corridors, no junction to branch off
#   research  decks: a junction on a medium station, two ladder junctions or more from large on — they stack, and the
#             own rooms go upstairs first (StationPurposes.Climbs)
LAYOUT = {
    "trade": {"small": (("corridor", 2, 2),), "medium": (("corridor", 2, 2), ("junction", 1, 1)),
              "large": (("corridor", 2, 3), ("junction", 1, 1)), "huge": (("corridor", 2, 4), ("junction", 1, 2)),
              "colossal": (("corridor", 2, 5), ("junction", 1, 3))},
    "mining": {"small": (("corridor", 2, 2),), "medium": (("corridor", 2, 3), ("junction", 0, 0)),
               "large": (("corridor", 3, 4), ("junction", 0, 0)), "huge": (("corridor", 3, 5), ("junction", 0, 0)),
               "colossal": (("corridor", 4, 6), ("junction", 0, 0))},
    "research": {"medium": (("junction", 1, 1),), "large": (("ladder_junction", 2, 2),),
                 "huge": (("ladder_junction", 2, 3),), "colossal": (("ladder_junction", 3, 4),)},
}
# What hangs on the hull (the composer's exterior counts), on top of the classic kit's.
EXTERIOR = {
    "trade": {"antennas": 2},
    "mining": {"solarWings": 2},
    "research": {"domes": 2, "antennas": 1},
}


def purpose_kit(classic, tier, purpose):
    """The classic kit of a tier, made for a purpose: its entries, the purpose's own rooms and its changes."""
    k = copy.deepcopy(classic)
    k["key"] = purpose_kit_key(tier, purpose)
    k["name"] = f"{PURPOSE_NAMES[purpose]} Station ({tier}) 1"
    k["purposes"] = [purpose]
    # A purpose exists from terrain generation 46 on, so its kit is offered from then on — to the server's kit pool, and
    # to nothing that reads "the kits every world has" (the pinned station goldens walk exactly those).
    k["minTerrainGeneration"] = LIVELY_GENERATION
    for field, more in EXTERIOR[purpose].items():
        k[field] = k.get(field, 0) + more
    by_module = {e["module"]: e for e in k["entries"]}
    required_more, optional_more = 0, 0
    for suffix, minimum, maximum in CLASSIC_CHANGES[purpose].get(tier, ()):
        key = f"{prefix(tier)}_{suffix}"
        e = by_module.get(key)
        if e is None:
            e = entry(key)
            k["entries"].append(e)
            by_module[key] = e
        before = e["min"] if e.get("required") else 0
        e["required"], e["min"], e["max"] = True, minimum, max(maximum, e.get("max", 1))
        required_more += max(0, minimum - before)
    for suffix, minimum, maximum in LAYOUT[purpose].get(tier, ()):
        e = by_module.get(f"{prefix(tier)}_{suffix}")
        if e is None:
            raise SystemExit(f"{tier}: the classic kit has no {suffix} for the {purpose} floor plan")
        before = e["min"] if e.get("required") else 0
        e["required"], e["min"], e["max"] = minimum > 0, minimum, maximum
        # Required passages more (or fewer) raise (or lower) the station's minimum; the maximum grows with them, so
        # the optional rooms keep the room they had.
        required_more += minimum - before
    for name, from_tier in OWN_ROOMS[purpose]:
        required = from_tier == "" or TIER_ORDER.index(tier) >= TIER_ORDER.index(from_tier)
        k["entries"].append(entry(f"{prefix(tier)}_{name}", required=required, maximum=1, weight=3))
        if required:
            required_more += 1
        else:
            optional_more += 1
    k["modulesMin"] += required_more
    k["modulesMax"] += required_more + optional_more
    return k


def dump_lf(path, obj):
    with Path(path).open("w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(obj, indent=2, ensure_ascii=False) + "\n")


def main():
    pool = _load(STATIONS)
    for tier in KIT_KEYS:
        for purpose in PURPOSES:
            for build in ROOMS[purpose]:
                e = module_entry(build(tier), purpose)
                upsert(pool, e)
                print(f"{e['key']}: {e['width']}x{e['height']}x{e['length']}, {len(e['rows'])} rows, {len(e['cells'])} listed cells")
    dump_lf(STATIONS, pool)

    kit_pool = _load(KITS)
    for tier, classic_key in KIT_KEYS.items():
        classic = next(k for k in kit_pool if k.get("key") == classic_key)
        for purpose in PURPOSES:
            k = purpose_kit(classic, tier, purpose)
            upsert(kit_pool, k)
            print(f"{k['key']}: {len(k['entries'])} entries, {k['modulesMin']}..{k['modulesMax']} modules")
    dump_lf(KITS, kit_pool)


if __name__ == "__main__":
    main()
