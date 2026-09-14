# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Merge a structure export bundle (from the in-game Station / Town editor) into the game data.

The editor writes a bundle to <persistentDataPath>/<kind>_exports/<key>/{structure.json, layout.json}
where <kind> is "station" or "settlement". This tool folds that bundle into a hand-made template pool:

  - data/station_templates.json     -> the pool the station placer can roll from
  - data/settlement_templates.json  -> the pool the settlement placer can roll from

The kit panel (#1877) writes <kind>_exports/<kit key>/kit.json; a bundle holding one is folded into
data/structure_kits.json (replaced in place by key, else appended). A directory may hold both.

Each pool entry is { "key", "name", "tier", "cells": [ { x, y, z, kind, id }, ... ],
"width", "height", "length" }. World-gen reads the matching pool and, when non-empty, may pick a
hand-made template instead of the procedural generator (integration tracked in the plan).

Plain stdlib JSON (no deps). The dev reviews the resulting diff and commits it.

Usage:
    python tools/merge_structure.py <path-to-export-bundle-dir>
"""
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"

POOL_FOR = {
    "station": DATA / "station_templates.json",
    "settlement": DATA / "settlement_templates.json",
}


def _load(p):
    return json.loads(Path(p).read_text(encoding="utf-8"))


def _dump(p, obj):
    Path(p).write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/merge_structure.py <export-bundle-dir>")

    bundle = Path(sys.argv[1])
    merged = False
    if (bundle / "kit.json").exists():
        merge_kit(_load(bundle / "kit.json"))
        merged = True

    if not (bundle / "structure.json").exists():
        if not merged:
            sys.exit(f"no structure.json or kit.json in {bundle}")
        return

    meta = _load(bundle / "structure.json")
    layout = _load(bundle / "layout.json")

    kind = meta.get("kind")
    if kind not in POOL_FOR:
        sys.exit(f"unknown structure kind '{kind}' (expected station or settlement)")

    pool_path = POOL_FOR[kind]
    pool = _load(pool_path) if pool_path.exists() else []

    existing = next((e for e in pool if e.get("key") == meta["key"]), None)
    entry = merged_entry(meta, layout, existing)

    # Replace the existing entry in place (keeps the pool order), else append.
    if existing is not None:
        pool[pool.index(existing)] = entry
    else:
        pool.append(entry)

    _dump(pool_path, pool)
    print(f"merged '{entry['key']}' ({len(entry['cells'])} cells) into {pool_path.relative_to(REPO)}")
    print("review the diff and commit. World-gen template-pool selection is the next integration step.")


def merged_entry(meta, layout, existing):
    """The pool entry for a bundle. Starts from the existing entry (if any) so `legacyPool` (pinned replays,
    #1115 — never drop it) and any field the editor does not know about survive a re-merge; `planetTypes`
    comes from the bundle when the editor wrote it, else stays as it was (#1399)."""
    entry = dict(existing or {})
    entry.update({
        "key": meta["key"],
        "name": meta.get("name", meta["key"]),
        "tier": meta.get("tier", "medium"),
        "pack": meta.get("pack", "default") or "default",
        "weight": int(meta.get("weight", 1) or 1),
        "width": layout.get("width", 0),
        "height": layout.get("height", 0),
        "length": layout.get("length", 0),
        "cells": [_clean_cell(c) for c in layout.get("cells", [])],
    })
    if "planetTypes" in meta:
        if meta["planetTypes"]:
            entry["planetTypes"] = list(meta["planetTypes"])
        else:
            entry.pop("planetTypes", None)  # the editor cleared the restriction on purpose
    # #1826: whole structure ("" / absent) or a building module with a role (house, market, city_housing …).
    if "role" in meta:
        if meta["role"]:
            entry["role"] = meta["role"]
        else:
            entry.pop("role", None)  # saved as a whole structure again
    # #1877: a kit module names its kit and its function; a whole structure carries neither.
    for field in ("kit", "function", "style"):
        if field in meta:
            if meta[field]:
                entry[field] = meta[field]
            else:
                entry.pop(field, None)
    return entry


def _clean_cell(cell):
    """A cell as the pool stores it: the editor writes every field, the pool keeps an empty port out (#1877)."""
    c = dict(cell)
    if not c.get("port"):
        c.pop("port", None)
    return c


KIT_PATH = DATA / "structure_kits.json"
KIT_ENTRY_DEFAULTS = {"min": 0, "max": 0, "required": False, "weight": 1, "rotate": True}


def kit_entry(kit):
    """The data-file shape of a kit the editor saved: the editor writes every field, the file keeps the ones set."""
    entry = {
        "key": kit["key"],
        "name": kit.get("name") or kit["key"],
        "kind": kit.get("kind") or "station",
        "tier": kit.get("tier") or "medium",
        "pack": kit.get("pack") or "default",
        "weight": int(kit.get("weight", 1) or 1),
    }
    for field, value in kit.items():
        if field in entry or field == "entries":
            continue
        if value in (None, "", 0, False, []):
            continue
        entry[field] = value
    entries = []
    for e in kit.get("entries", []):
        if not e.get("module"):
            continue
        row = {"module": e["module"]}
        for field, default in KIT_ENTRY_DEFAULTS.items():
            value = e.get(field, default)
            if field in ("min", "max", "weight") or value != default:
                row[field] = value
        entries.append(row)
    entry["entries"] = entries
    return entry


def merge_kit(kit):
    if not kit.get("key"):
        sys.exit("kit.json has no key")
    kits = _load(KIT_PATH) if KIT_PATH.exists() else []
    entry = kit_entry(kit)
    index = next((i for i, k in enumerate(kits) if k.get("key") == entry["key"]), None)
    if index is None:
        kits.append(entry)
    else:
        kits[index] = entry
    _dump(KIT_PATH, kits)
    print(f"merged kit '{entry['key']}' ({len(entry['entries'])} entries) into {KIT_PATH.relative_to(REPO)}")


if __name__ == "__main__":
    main()
