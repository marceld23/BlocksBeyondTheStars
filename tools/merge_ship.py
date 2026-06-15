"""Merge a ship-type export bundle (from the in-game ship editor) into the game data.

The editor writes a bundle to <persistentDataPath>/ship_exports/<key>/{ship.json, layout.json}. This
tool folds that bundle into the repo's data so the designed ship becomes a real, craftable ship that
the server stamps with its true shape:

  - data/ships.json              -> the ShipDefinition entry (with a "layout" reference + derived modules)
  - data/ship_layouts/<key>.json -> the voxel layout
  - data/locales/{en,de}.json    -> placeholder ship.<key>.name / .desc (only if missing)

Plain stdlib JSON (no deps). The dev reviews the resulting diff and commits it.

Usage:
    python tools/merge_ship.py <path-to-export-bundle-dir>
"""
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"

# Station palette ids -> ship module keys.
MODULE_FOR = {
    "cockpit": "cockpit", "reactor": "reactor", "life_support": "life_support",
    "workshop": "workshop", "medbay": "medbay", "quarters": "quarters", "cargo": "cargo_hold_basic",
}


def _load(p):
    return json.loads(Path(p).read_text(encoding="utf-8"))


def _dump(p, obj):
    Path(p).write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def _norm_cell(c):
    """Normalise one layout cell, carrying the optional per-voxel modifiers (dye/glow colour + packed
    shape+orientation) when present so authored tinted/shaped/oriented cells survive the merge."""
    cell = {"x": c["x"], "y": c["y"], "z": c["z"], "kind": c.get("kind", "block"), "id": c.get("id", "")}
    for fld in ("tint", "glow", "shape"):
        if c.get(fld):
            cell[fld] = c[fld]
    return cell


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/merge_ship.py <export-bundle-dir>")

    bundle = Path(sys.argv[1])
    ship = _load(bundle / "ship.json")
    layout = _load(bundle / "layout.json")

    key = (ship.get("key") or "").strip()
    if not key:
        sys.exit("ship.json has no key.")
    if not layout.get("cells"):
        sys.exit("layout.json has no cells.")

    modules = []
    for c in layout["cells"]:
        m = MODULE_FOR.get(c.get("id"))
        if m and m not in modules:
            modules.append(m)

    entry = {
        "key": key,
        "nameKey": f"ship.{key}.name",
        "descriptionKey": f"ship.{key}.desc",
        "baseHull": ship.get("baseHull", 100),
        "baseShield": ship.get("baseShield", 0),
        "flightSpeed": ship.get("flightSpeed", 1.0),
        "handling": ship.get("handling", 1.0),
        "interiorWidth": layout.get("width", 5),
        "interiorLength": layout.get("length", 7),
        "height": layout.get("height", 4),
        "cargoSlots": ship.get("cargoSlots", 48),
        "startModules": modules,
        "layout": key,
    }
    if ship.get("requiredBlueprint"):
        entry["requiredBlueprint"] = ship["requiredBlueprint"]
    if ship.get("craftCost"):
        entry["craftCost"] = [
            {"item": c["item"], "count": c["count"]}
            for c in ship["craftCost"] if c.get("item") and c.get("count")
        ]

    # 1. ships.json — replace any existing entry with this key, then append.
    ships_path = DATA / "ships.json"
    ships = [s for s in _load(ships_path) if s.get("key") != key]
    ships.append(entry)
    _dump(ships_path, ships)

    # 2. layout file (normalised to the ShipLayout schema).
    (DATA / "ship_layouts").mkdir(exist_ok=True)
    norm = {
        "width": layout.get("width", 0),
        "height": layout.get("height", 0),
        "length": layout.get("length", 0),
        "cells": [
            _norm_cell(c)
            for c in layout["cells"]
        ],
    }
    _dump(DATA / "ship_layouts" / f"{key}.json", norm)

    # 3. locale placeholders (only if absent, so existing translations are kept).
    for loc in ("en", "de"):
        lp = DATA / "locales" / f"{loc}.json"
        d = _load(lp)
        d.setdefault(f"ship.{key}.name", ship.get("name") or key)
        d.setdefault(f"ship.{key}.desc", ship.get("description") or "A custom ship.")
        _dump(lp, d)

    print(f"Merged ship '{key}': {len(layout['cells'])} cells, modules={modules}.")
    print("Re-run the server / rebuild the client to pick it up.")


if __name__ == "__main__":
    main()
