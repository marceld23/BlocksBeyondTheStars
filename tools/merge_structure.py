"""Merge a structure export bundle (from the in-game Station / Town editor) into the game data.

The editor writes a bundle to <persistentDataPath>/<kind>_exports/<key>/{structure.json, layout.json}
where <kind> is "station" or "settlement". This tool folds that bundle into a hand-made template pool:

  - data/station_templates.json     -> the pool the station placer can roll from
  - data/settlement_templates.json  -> the pool the settlement placer can roll from

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
    meta = _load(bundle / "structure.json")
    layout = _load(bundle / "layout.json")

    kind = meta.get("kind")
    if kind not in POOL_FOR:
        sys.exit(f"unknown structure kind '{kind}' (expected station or settlement)")

    pool_path = POOL_FOR[kind]
    pool = _load(pool_path) if pool_path.exists() else []

    entry = {
        "key": meta["key"],
        "name": meta.get("name", meta["key"]),
        "tier": meta.get("tier", "medium"),
        "pack": meta.get("pack", "default") or "default",
        "weight": int(meta.get("weight", 1) or 1),
        "width": layout.get("width", 0),
        "height": layout.get("height", 0),
        "length": layout.get("length", 0),
        "cells": layout.get("cells", []),
    }

    # Replace an existing entry with the same key, else append.
    pool = [e for e in pool if e.get("key") != entry["key"]]
    pool.append(entry)

    _dump(pool_path, pool)
    print(f"merged '{entry['key']}' ({len(entry['cells'])} cells) into {pool_path.relative_to(REPO)}")
    print("review the diff and commit. World-gen template-pool selection is the next integration step.")


if __name__ == "__main__":
    main()
