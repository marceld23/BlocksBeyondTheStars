#!/usr/bin/env python3
"""Generates the GIANT SHIP templates (#3617) from the ship scripts in tools/giant_ships/<key>.py.

A giant ship is a huge authored spaceship (at most 256 blocks per axis) that nobody can build: the game only ever shows
it as a WRECK derived from this intact design with the instance's seed — crashed on a planet (terrain generation 42) or
drifting in space. Each script defines `build()` returning a `ships.ShipDesign` (tools/landmarks/ships.py: lofted hulls,
tubes, decks, windows, domes, greebles, corridors and rooms with sliding doors, entrances, ladder shafts). This script
runs them and writes `<out>/<key>.json`: a compact `StructureTemplate` (palette + run-length rows, markers in `cells`)
with `giantShip: true`, `minTerrainGeneration: 42`, `nameKey: giantship.<key>.name`, `weight`, `groundRow`, `style`.

    uv run --no-project python tools/gen_giant_ships.py                                  # every ship -> data/giant_ships
    uv run --no-project python tools/gen_giant_ships.py --only carrier --out artifacts/gs/ships --preview
    uv run --no-project python tools/gen_giant_ships.py --only _demo_hull --out artifacts/gs/ships --preview
    uv run --no-project python tools/gen_giant_ships.py --check                          # build and check, write nothing

Checks (a failure stops that ship's file and sets exit code 1): size at most 256 per axis, at least one `entrance`
standing on a floor with headroom, every marker inside the ship, `door_slide` markers naming `axis=x|z`, known blocks
only, valid style / ruin zones / ground row. Prints the stats line, the bounding box, the glass share and the compact
JSON size with a deflate estimate (about what the transport sends). Stdlib only; LF line endings.
"""
from __future__ import annotations

import argparse
import importlib
import sys
import time
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
SCRIPTS = TOOLS / "giant_ships"
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / "landmarks"))
ships = importlib.import_module("ships")
preview = importlib.import_module("preview")
REPO = TOOLS.parent


def ship_scripts(include_private: bool = False) -> list[str]:
    """The keys of the ship scripts in tools/giant_ships/, sorted (`_`-scripts only when asked)."""
    return sorted(p.stem for p in SCRIPTS.glob("*.py")
                  if p.stem != "__init__" and (include_private or not p.stem.startswith("_")))


def _finish_header(d, key: str) -> list[str]:
    """Fills the fields every shipped giant ship carries; returns notes about what it changed."""
    notes = []
    h = d.header
    if h.get("key") != key:
        notes.append(f"key '{h.get('key')}' differs from the script name '{key}' (the file is named after the key)")
    h["giantShip"] = True
    h.setdefault("nameKey", f"giantship.{h['key']}.name")
    h.setdefault("minTerrainGeneration", ships.GIANT_GENERATION)
    h.setdefault("weight", 1)
    h.setdefault("groundRow", 0)
    h.setdefault("style", "")
    h.pop("landmark", None)
    return notes


def build_one(key: str, out: Path, write: bool, render: bool) -> bool:
    """Builds, checks and (unless only checking) writes one script's ship(s); True when all went well."""
    t0 = time.time()
    module = importlib.import_module(f"giant_ships.{key}")
    if not hasattr(module, "build"):
        print(f"  skipped tools/giant_ships/{key}.py: no build()")
        return True
    result = module.build()
    designs = result if isinstance(result, (list, tuple)) else [result]
    ok = True
    for d in designs:
        if not isinstance(d, ships.ShipDesign):
            print(f"FAIL {key}: build() must return a ships.ShipDesign, not {type(d).__name__}")
            ok = False
            continue
        for note in _finish_header(d, key):
            print(f"  note: {note}")
        carved = d.finish()
        errors, warnings = d.problems()
        for w in warnings:
            print(f"  warning: {d.header['key']}: {w}")
        if errors:
            for e in errors:
                print(f"  ERROR: {d.header['key']}: {e}")
            print(f"FAIL {d.header['key']}: {len(errors)} error(s), nothing written")
            ok = False
            continue
        t = d.template()
        n = ships.ship_numbers(t)
        if write:
            path = d.write(out, check=False)
        else:
            print(preview.templatefile.stats(t, n["bytes"]))
            path = None
        b = n["bbox"]
        box = f"x {b[0]}..{b[3]}, y {b[1]}..{b[4]}, z {b[2]}..{b[5]}" if b else "empty"
        print(f"  {n['cells']:,} cells ({n['blocks']:,} blocks, {carved:,} buried air -> cavity) | blocks in {box} | "
              f"glass {n['glass']:.1%} | {n['bytes']:,} bytes compact JSON, ~{n['deflated']:,} deflated | "
              f"{time.time() - t0:.1f}s")
        if render and path is not None:
            png = preview.preview(path, out / "preview" / f"{t['key']}.png", cuts=("auto",))
            print(f"  preview -> {png}")
    return ok


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Generate the giant ship templates from tools/giant_ships/<key>.py.")
    ap.add_argument("--only", action="append", default=[], help="ship key(s) to build (repeatable or comma list); "
                    "names starting with _ (the demo) only run this way")
    ap.add_argument("--out", help="output folder (default data/giant_ships; builders use artifacts/gs/ships)")
    ap.add_argument("--check", action="store_true", help="build and check only, write nothing")
    ap.add_argument("--preview", action="store_true", help="also render <out>/preview/<key>.png (views + sections)")
    ap.add_argument("--list", action="store_true", help="list the ship scripts and exit")
    args = ap.parse_args(argv)
    keys = [k for item in args.only for k in item.split(",") if k] or ship_scripts()
    if args.list:
        print("\n".join(ship_scripts(include_private=True)))
        return 0
    known = set(ship_scripts(include_private=True))
    missing = [k for k in keys if k not in known]
    if missing:
        ap.error(f"no script tools/giant_ships/<key>.py for: {', '.join(missing)}")
    out = Path(args.out) if args.out else ships.SHIP_OUT
    if not out.is_absolute():
        out = Path.cwd() / out
    failed = [k for k in keys if not build_one(k, out, not args.check, args.preview and not args.check)]
    if failed:
        print(f"{len(failed)} of {len(keys)} ship script(s) failed: {', '.join(failed)}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
