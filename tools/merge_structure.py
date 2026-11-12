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

A LANDMARK (#3492, town editor, "Use as: Landmark") is one file: landmark_exports/<key>/landmark.json, the compact
template (a palette plus run-length rows). It is written to data/landmarks/<key>.json — one file per landmark, the
editor's defaults stripped; `pinOnly` of a file that already exists is kept (the editor does not know the field).

A GENERATOR ENTRY (#3518, #3523, either editor, "Use as: Generator entry") is a template without cells — a structure
source, its parameters, weight, pack and planet types: <kind>_exports/<key>/entry.json, or
landmark_exports/<key>/entry.json for a landmark entry. It goes to the pool the editor saved it for —
data/station_templates.json, data/settlement_templates.json (an underground entry with `underground: true`) or
data/landmarks/<key>.json — replaced in place by key, else appended (a pool draws its entries after everything else
anyway), the editor's defaults stripped.

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
    if (bundle / "landmark.json").exists():
        merge_landmark(_load(bundle / "landmark.json"))
        merged = True

    if (bundle / "kit.json").exists():
        merge_kit(_load(bundle / "kit.json"))
        merged = True

    if (bundle / "entry.json").exists():
        merge_generator_entry(_load(bundle / "entry.json"))
        merged = True

    if not (bundle / "structure.json").exists():
        if not merged:
            sys.exit(f"no structure.json, kit.json, landmark.json or entry.json in {bundle}")
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
    # #3333: `underground` (a whole settlement only the underground stamper seats) is kept when true, stripped when false;
    # a bundle from an editor that predates the flag leaves the pool's value alone.
    for field in ("kit", "function", "style", "underground"):
        if field in meta:
            if meta[field]:
                entry[field] = meta[field]
            else:
                entry.pop(field, None)
    # The school club's landmarks (terrain generation 39): `protection` (auto | protected | open) of a whole template — `auto`
    # (the default) is stripped; a bundle from an editor that predates the field leaves the pool's value alone.
    if "protection" in meta:
        _set_protection(entry, meta["protection"])
    return entry


def _set_protection(entry, value):
    """Writes a template's `protection` word into a data entry: `auto` / empty (the default, the rule of its kind) removes
    the field, `protected` / `open` set it."""
    if value and value != "auto":
        entry["protection"] = value
    else:
        entry.pop("protection", None)


def _clean_zone(zone):
    """A ruin zone as the data file keeps it: its corners always, the profile and strength only when set."""
    z = {k: int(zone.get(k, 0) or 0) for k in ("x0", "y0", "z0", "x1", "y1", "z1")}
    if zone.get("profile"):
        z["profile"] = zone["profile"]
    if zone.get("strength"):
        z["strength"] = zone["strength"]
    return z


def _clean_cell(cell):
    """A cell as the pool stores it: the editor writes every field, the pool keeps an empty port out (#1877)."""
    c = dict(cell)
    if not c.get("port"):
        c.pop("port", None)
    return c


LANDMARK_DIR = DATA / "landmarks"
LANDMARK_KEEP = ("key", "name", "landmark", "width", "height", "length", "palette", "rows", "cells")


def landmark_entry(landmark, existing):
    """The data-file shape of a landmark the editor saved: every field the editor left at its default is dropped (an
    empty ruin profile, weight 1, pack `default`, no conditions …), the cells and palette entries likewise; `pinOnly`
    — a field the editor does not write — survives from the file that is already there."""
    entry = {}
    for field, value in landmark.items():
        if field in LANDMARK_KEEP or value not in (None, "", 0, 0.0, False, []):
            entry[field] = value
    if entry.get("pack") == "default":
        entry.pop("pack")
    if entry.get("weight") == 1:
        entry.pop("weight")
    entry.pop("kind", None)  # the folder says it is a landmark
    entry["landmark"] = True
    entry["palette"] = [{k: v for k, v in p.items() if k in ("kind", "id") or v not in (None, "", 0, False)}
                        for p in landmark.get("palette", [])]
    entry["cells"] = [{k: v for k, v in c.items() if k in ("x", "y", "z", "kind", "id") or v not in (None, "", 0, False)}
                      for c in landmark.get("cells", [])]
    # Generation 39: `ruinZones` (boxes with their own profile and strength), `seat` (`span`) and `protection` — the
    # defaults (no zones, the guaranteed search, `auto`) are stripped by the loop above or here.
    if entry.get("ruinZones"):
        entry["ruinZones"] = [_clean_zone(z) for z in entry["ruinZones"]]
    _set_protection(entry, entry.get("protection"))
    if existing and existing.get("pinOnly"):
        entry["pinOnly"] = True
    # `minTerrainGeneration` (39 on the school club's landmarks: older worlds never roll them) is not an editor field either.
    if existing and existing.get("minTerrainGeneration") and "minTerrainGeneration" not in landmark:
        entry["minTerrainGeneration"] = existing["minTerrainGeneration"]
    return entry


def merge_landmark(landmark):
    if not landmark.get("key"):
        sys.exit("landmark.json has no key")
    LANDMARK_DIR.mkdir(parents=True, exist_ok=True)
    path = LANDMARK_DIR / f"{landmark['key']}.json"
    entry = landmark_entry(landmark, _load(path) if path.exists() else None)
    _dump_landmark(path, entry)
    print(f"merged landmark '{entry['key']}' ({len(entry['rows'])} rows, {len(entry['cells'])} cells with settings) "
          f"into {path.relative_to(REPO)}")


def _dump_landmark(path, entry):
    with path.open("w", encoding="utf-8", newline="\n") as f:  # one line per file, LF: the rows are the bulk
        json.dump(entry, f, ensure_ascii=False, separators=(",", ":"))
        f.write("\n")


# What a generator entry never carries: its source builds it (no cells, rows or palette) and it is a whole structure (no
# module role, kit or function). A re-merge over an authored template of the same key drops them (#3518).
ENTRY_SHAPE_FIELDS = ("cells", "rows", "palette", "width", "height", "length", "groundRow", "role", "kit", "function",
                      "style")


def entry_pool(entry):
    """The pool a generator entry belongs to: `landmark` (a landmark entry), `station` or `settlement` (which also holds
    the underground entries, flagged)."""
    if entry.get("landmark") or entry.get("kind") == "landmark":
        return "landmark"
    return "station" if entry.get("kind") == "station" else "settlement"


def generator_entry(entry, existing):
    """The data-file shape of a generator entry the editor saved (#3518, #3523; EntryJson in StructureEditor.Entry.cs).

    The editor writes every field; the file keeps the ones set — no `pack: default`, no `weight: 1`, no empty planet
    types or parameters, `underground` only on an underground entry of the settlement pool. A landmark entry carries no
    tier and no kind (its folder says what it is) and `landmark: true`, like a landmark file. A field of the existing
    entry the editor does not write at all (`pinOnly`, a landmark's `requires` …) survives a re-merge — except the cells
    and module fields an entry must not have (the loader would drop the entry).
    """
    pool = entry_pool(entry)
    data = {"key": entry["key"], "name": entry.get("name") or entry["key"]}
    if pool != "landmark":
        data["tier"] = entry.get("tier") or "medium"
        data["kind"] = pool
    pack = entry.get("pack") or "default"
    if pack != "default":
        data["pack"] = pack
    weight = max(1, int(entry.get("weight", 1) or 1))
    if weight != 1:
        data["weight"] = weight
    if entry.get("planetTypes"):
        data["planetTypes"] = list(entry["planetTypes"])
    if pool == "settlement" and entry.get("underground"):
        data["underground"] = True
    if pool == "landmark":
        data["landmark"] = True
    data["source"] = entry["source"].strip()
    if entry.get("sourceParams"):
        data["sourceParams"] = entry["sourceParams"]
    # Generation 39: an entry's `protection` (any pool), a landmark entry's `seat` and ruin zones — written when set.
    _set_protection(data, entry.get("protection"))
    if pool == "landmark" and entry.get("seat"):
        data["seat"] = entry["seat"]
    if pool == "landmark" and entry.get("ruinZones"):
        data["ruinZones"] = [_clean_zone(z) for z in entry["ruinZones"]]
    for field, value in (existing or {}).items():
        if field not in entry and field not in data and field not in ENTRY_SHAPE_FIELDS:
            data[field] = value
    return data


def merge_generator_entry(entry):
    if not entry.get("key"):
        sys.exit("entry.json has no key")
    if not (entry.get("source") or "").strip():
        sys.exit(f"entry.json '{entry['key']}' names no source")

    pool = entry_pool(entry)
    if pool == "landmark":
        LANDMARK_DIR.mkdir(parents=True, exist_ok=True)
        path = LANDMARK_DIR / f"{entry['key']}.json"
        data = generator_entry(entry, _load(path) if path.exists() else None)
        _dump_landmark(path, data)
    else:
        path = POOL_FOR[pool]
        templates = _load(path) if path.exists() else []
        index = next((i for i, t in enumerate(templates) if t.get("key") == entry["key"]), None)
        data = generator_entry(entry, templates[index] if index is not None else None)
        if index is None:
            templates.append(data)
        else:
            templates[index] = data
        _dump(path, templates)
    where = "underground " if data.get("underground") else ""
    print(f"merged {where}generator entry '{data['key']}' (source '{data['source']}') into {path.relative_to(REPO)}")


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


def merged_kit_entry(kit, existing):
    """The kit entry for a re-merge: the bundle's fields (defaults stripped, so `underground: false` removes the flag,
    #3333), plus every field of the existing entry the bundle does not carry at all — a field the editor does not write
    (`pinOnly`, or `underground` from an editor that predates it) survives, like `merged_entry` does for templates."""
    entry = kit_entry(kit)
    for field, value in (existing or {}).items():
        if field not in kit and field not in entry:
            entry[field] = value
    return entry


def merge_kit(kit):
    if not kit.get("key"):
        sys.exit("kit.json has no key")
    kits = _load(KIT_PATH) if KIT_PATH.exists() else []
    index = next((i for i, k in enumerate(kits) if k.get("key") == kit["key"]), None)
    entry = merged_kit_entry(kit, kits[index] if index is not None else None)
    if index is None:
        kits.append(entry)
    else:
        kits[index] = entry
    _dump(KIT_PATH, kits)
    print(f"merged kit '{entry['key']}' ({len(entry['entries'])} entries) into {KIT_PATH.relative_to(REPO)}")


if __name__ == "__main__":
    main()
