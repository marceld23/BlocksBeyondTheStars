#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate real voxel ship layouts (data/ship_layouts/<key>.json) for each ship type.

Each layout is a complete design: a hollow hull box (floor + walls + roof), a rear airlock door, front
windows, interior station-marker tiles, and a DISTINCT exterior silhouette per type (wings, engines, nose,
nav lights). The server stamps these for the walkable interior (planet) AND meshes them 1:1 in space (item 20).

Coordinate convention (matches StampShipLayout / BuildShipStructure):
  X = 0..W-1 width, Y = 0 floor .. H roof, Z = 0 rear (hatch) .. L-1 front (windows/cockpit). Front = +Z.
Exterior cells may sit outside [0,W)/[0,L) (negative or beyond) — the grid + client mesher handle that.

Cell ids: hull -> "iron_wall"; window -> "glass"; engine -> "engine"; nav lights -> "light_red"/"light_green";
headlight -> "light"; rear opening -> "door_slide" (an airlock); station tiles -> kind "station", id = type.
"""
import json
import os

OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "data", "ship_layouts"))


def build(width, height, length, builder):
    cells = {}

    def put(x, y, z, id_, kind="block"):
        cells[(x, y, z)] = (kind, id_)

    def cut(x, y, z):
        cells.pop((x, y, z), None)

    if builder.__code__.co_argcount >= 5:
        builder(put, width, height, length, cut)
    else:
        builder(put, width, height, length)
    ordered = sorted(cells.items())
    return {
        "width": width, "height": height, "length": length,
        "cells": [{"x": x, "y": y, "z": z, "kind": k, "id": i} for (x, y, z), (k, i) in ordered],
    }


def hull(put, W, H, L, door_x, win_sides=False):
    """A closed hull box: solid floor + roof, perimeter walls, a rear airlock + front window band."""
    # A proper 3-wide rear airlock (the single door marker is sized to the full gap by MakeDoor): clear the
    # door column plus its two neighbours, kept off the rear corners so the hull stays sealed there.
    door_cols = {x for x in (door_x - 1, door_x, door_x + 1) if 0 < x < W - 1}
    for x in range(W):
        for z in range(L):
            put(x, 0, z, "iron_wall")   # floor
            put(x, H, z, "iron_wall")   # roof
    for y in range(1, H):
        for x in range(W):
            for z in range(L):
                if not (x == 0 or x == W - 1 or z == 0 or z == L - 1):
                    continue  # interior stays hollow
                if z == 0 and x in door_cols and y in (1, 2):
                    if y == 1 and x == door_x:
                        put(door_x, 1, 0, "door_slide", "element")  # one airlock door, sized to the 3-wide gap
                    continue  # the whole 3-wide, 2-tall opening stays clear
                if z == L - 1 and y == 2 and 0 < x < W - 1:
                    put(x, y, z, "glass")   # front windscreen band
                    continue
                if win_sides and (x == 0 or x == W - 1) and y == 2 and 0 < z < L - 1:
                    put(x, y, z, "glass")   # side portholes
                    continue
                put(x, y, z, "iron_wall")


def stations(put, items):
    for (x, z, typ) in items:
        put(x, 1, z, typ, "station")


# ---------------- per-type designs ----------------

def starter(put, W, H, L):  # 5 x 4 x 7 — the balanced everyman hull
    cx = W // 2
    hull(put, W, H, L, door_x=cx)
    # cockpit + medbay sit together at the FRONT (medbay = the spawn/heal-tank): away from the rear airlock
    # (so the hatch stays sealed at spawn) and within reach of the cockpit (take the helm without walking).
    stations(put, [
        (cx, L - 2, "cockpit"), (cx, L - 3, "medbay"),
        (1, 2, "workshop"), (W - 2, 2, "cargo"), (1, L - 3, "quarters"), (W - 2, L - 3, "lab"), (cx, L // 2, "console"),
    ])
    wingY = H // 2
    for w in (1, 2):                                   # two-cell wings, port + starboard
        for z in (2, 3, 4):
            put(-w, wingY, z, "iron_wall")
            put(W - 1 + w, wingY, z, "iron_wall")
    put(-2, wingY, 3, "light_red")                     # port nav light
    put(W + 1, wingY, 3, "light_green")                # starboard nav light
    for x in (1, W - 2):
        put(x, 1, -1, "engine")                        # twin rear engines
        put(x, 1, L, "light")                          # front headlights
    put(cx, H + 1, L - 2, "glass"); put(cx, H + 1, L - 3, "glass")  # raised cockpit canopy


def scout(put, W, H, L):    # 5 x 4 x 5 — small, sleek, pointed
    cx = W // 2
    hull(put, W, H, L, door_x=cx)
    stations(put, [(cx, L - 2, "cockpit"), (cx, L - 3, "medbay"), (1, 1, "cargo")])
    # Pointed glass nose poking out the front.
    put(cx, 1, L, "glass"); put(cx, 2, L, "glass"); put(cx, 2, L + 1, "glass")
    # Swept-back wings (angle toward the rear) at mid height.
    wingY = H // 2
    put(-1, wingY, 2, "iron_wall"); put(-2, wingY, 1, "iron_wall")
    put(W, wingY, 2, "iron_wall");  put(W + 1, wingY, 1, "iron_wall")
    put(-2, wingY, 1, "light_red"); put(W + 1, wingY, 1, "light_green")
    # A single strong central engine.
    put(cx, 1, -1, "engine"); put(cx, 2, -1, "engine")


def corvette(put, W, H, L):  # 6 x 4 x 7 — combat-leaning, twin-engine, raised bridge
    cx = W // 2
    hull(put, W, H, L, door_x=cx, win_sides=True)
    stations(put, [
        (cx, L - 2, "cockpit"), (cx - 1, L - 2, "medbay"),
        (1, 2, "workshop"), (W - 2, 2, "cargo"), (cx, L // 2, "quarters"),
    ])
    wingY = H // 2
    for w in (1, 2):
        for z in (2, 3):
            put(-w, wingY, z, "iron_wall")
            put(W - 1 + w, wingY, z, "iron_wall")
    put(-2, wingY, 2, "light_red"); put(W + 1, wingY, 2, "light_green")
    for x in (1, W - 2):                               # twin stacked engines
        put(x, 1, -1, "engine"); put(x, 2, -1, "engine")
    # Forward weapon nubs at the bow corners.
    put(0, 2, L, "engine"); put(W - 1, 2, L, "engine")
    # Raised bridge: a small glass-topped bump over the front.
    for x in (cx - 1, cx):
        put(x, H + 1, L - 2, "iron_wall"); put(x, H + 1, L - 3, "iron_wall")
        put(x, H + 2, L - 2, "glass");     put(x, H + 2, L - 3, "glass")


def hauler(put, W, H, L):   # 7 x 4 x 9 — big boxy freighter with deck cargo + 4 engines
    cx = W // 2
    hull(put, W, H, L, door_x=cx)
    stations(put, [
        (cx, L - 2, "cockpit"), (cx - 1, L - 2, "medbay"),
        (1, 2, "workshop"), (1, L // 2, "cargo"), (W - 2, L // 2, "cargo"), (cx, L // 2, "quarters"), (W - 2, 2, "console"),
    ])
    wingY = H // 2
    for z in (4, 5):                                   # stubby load-bearing wings
        put(-1, wingY, z, "iron_wall"); put(W, wingY, z, "iron_wall")
    put(-1, wingY, 4, "light_red"); put(W, wingY, 4, "light_green")
    for x in (1, W - 2):                               # four rear engines (corners, stacked)
        put(x, 1, -1, "engine"); put(x, 2, -1, "engine")
    # Cargo containers strapped to the roof down the spine.
    for z in range(2, L - 2):
        put(cx, H + 1, z, "carbon")
        if z % 2 == 0:
            put(cx - 1, H + 1, z, "iron_wall"); put(cx + 1, H + 1, z, "iron_wall")


def room_box(put, x0, z0, x1, z1, H):
    """A closed sub-room: floor + roof over the rect, perimeter walls in between (inclusive bounds).
    Abutting rooms share wall cells (the cell dict dedupes overlapping writes)."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            put(x, 0, z, "iron_wall")   # floor
            put(x, H, z, "iron_wall")   # roof
            if x in (x0, x1) or z in (z0, z1):
                for y in range(1, H):
                    put(x, y, z, "iron_wall")


def doorway(put, cut, cells_xz, door_at, H):
    """Punch a full-height (3-tall, y=1..H-1) opening through a wall and hang one slide door in it."""
    for (x, z) in cells_xz:
        for y in range(1, H):
            cut(x, y, z)
    put(door_at[0], 1, door_at[1], "door_slide", "element")


def hammerhead(put, W, H, L, cut):  # 14 x 4 x 15 — the first multi-room hull: a T-shaped heavy gunship
    """Hammerhead floor plan: a wide bridge up front (the "hammer head"), a long central
    corridor running aft, and a workshop (port) + sleeping cabins (starboard) flanking the
    corridor behind their own doors. Three engines aft, stern airlock between them."""
    # -- hull: four abutting closed rooms (shared walls), then door openings punched through --
    room_box(put, 1, 10, 12, L - 1, H)   # bridge/control room, 12 wide x 5 long, full width up front
    room_box(put, 5, 0, 8, 10, H)        # central corridor, 4 wide x 11 long (front wall = bridge rear wall)
    room_box(put, 0, 0, 5, 5, H)         # workshop area 5x5 + shared east wall with the corridor
    room_box(put, 8, 0, 13, 5, H)        # sleeping cabins 5x5 + shared west wall with the corridor
    doorway(put, cut, [(6, 0), (7, 0)], (6, 0), H)      # stern airlock into the corridor
    doorway(put, cut, [(6, 10), (7, 10)], (6, 10), H)   # corridor -> bridge
    doorway(put, cut, [(5, 2), (5, 3)], (5, 2), H)      # corridor -> workshop
    doorway(put, cut, [(8, 2), (8, 3)], (8, 3), H)      # corridor -> sleeping cabins
    # -- windows: full-width bridge windscreen + side portholes on the head --
    for x in range(2, 12):
        put(x, 2, L - 1, "glass")
    for z in (11, 12, 13):
        put(1, 2, z, "glass")
        put(12, 2, z, "glass")
    # -- stations: each of the drawn rooms gets its jobs --
    stations(put, [
        (7, L - 2, "cockpit"), (4, L - 2, "console"),      # bridge: helm + systems console
        (2, 1, "workshop"), (2, 4, "cargo"),               # workshop area
        (11, 1, "quarters"), (11, 4, "medbay"),            # sleeping cabins (medbay = spawn/heal)
    ])
    # -- exterior: main engine block aft (split around the airlock: its exit corridor must stay clear,
    # the #211 rule), plus one side engine behind each room --
    for x in (4, 5, 8, 9):                                 # stacked main nozzles flanking the stern airlock
        put(x, 1, -1, "engine"); put(x, 2, -1, "engine")
    put(2, 1, -1, "engine"); put(11, 1, -1, "engine")      # side engines behind the rooms
    put(0, 2, 12, "light_red"); put(13, 2, 12, "light_green")  # nav lights on the head flanks
    put(4, 2, L, "light"); put(9, 2, L, "light")           # headlights off the windscreen
    for x in (6, 7):                                       # raised glass canopy over the bridge
        put(x, H + 1, 12, "glass"); put(x, H + 1, 13, "glass")


# NOTE: the starter intentionally keeps the parametric box hull (its silhouette is added in the box fallback,
# and the box interior is what the ship-interior tests + energy hatch rely on). Only the unlockable ships get
# bespoke voxel layouts here. (The starter() builder is kept for reference / future use.)
SHIPS = {
    "ship_scout": (5, 4, 5, scout),
    "ship_corvette": (6, 4, 7, corvette),
    "ship_hauler": (7, 4, 9, hauler),
    "ship_hammerhead": (14, 4, 15, hammerhead),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for key, (w, h, l, fn) in SHIPS.items():
        data = build(w, h, l, fn)
        path = os.path.join(OUT, key + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=1)
        print(f"wrote {path}  ({len(data['cells'])} cells, {w}x{h}x{l})")


if __name__ == "__main__":
    main()
