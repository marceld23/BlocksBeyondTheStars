#!/usr/bin/env python3
"""Turn door BLOCK cells of the shipped SETTLEMENT templates into door MARKERS with a walkable doorway (#1982).

A door in a settlement template is a marker cell (kind "marker", id door_slide / door_hinge / door_energy) on the
doorway's floor: the server hangs a real door there when the building is stamped. A cell of kind "block" with a
door id is stamped as that block — a solid, door-textured wall. Eight shipped settlement templates carried such
cells (the editors' block palette used to list the door blocks next to the markers), each with the wall
continuing right above it: converting the cell alone would hang a 2.8-tall door in a one-cell hole nobody can
walk through, so the doorway is opened as well.

For every template: each vertical run of door block cells becomes ONE marker at its lowest cell; the other cells
of the run are dropped (a 2-tall column must not become two stacked doors); the wall cell straight above the
marker is removed (a body needs two cells), and the one above that too when it is wall with more wall over it
(a lintel under a roof stays). The report printed first says, per marker, whether the shared door rule finds a
jamb beside it — read it before applying.

STATION templates are left alone on purpose: a door block in a station's outer hull is the airtight airlock
block the station air model counts as sealed; converting it would open the hull to the void.

Usage (from the repo root, stdlib only):
    python tools/fix_template_door_blocks.py            # report only
    python tools/fix_template_door_blocks.py --apply    # rewrite the file (round-trip is byte-identical apart from the cells)
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILE = ROOT / "data" / "settlement_templates.json"
DOOR_IDS = {"door_slide", "door_hinge", "door_energy", "door_wood"}


def fix_template(t: dict) -> list[str]:
    """Rewrites the template in place; returns report lines (empty when nothing changed)."""
    cells: list[dict] = t.get("cells", [])
    door_blocks = [c for c in cells if c.get("id") in DOOR_IDS and c.get("kind", "block") != "marker"]
    if not door_blocks:
        return []

    height = int(t.get("height", 0))
    solid = {(c["x"], c["y"], c["z"]): c for c in cells if c.get("kind", "block") != "marker" and c not in door_blocks}
    by_column: dict[tuple[int, int], list[dict]] = {}
    for c in door_blocks:
        by_column.setdefault((c["x"], c["z"]), []).append(c)

    lines = []
    for (x, z), column in sorted(by_column.items()):
        column.sort(key=lambda c: c["y"])
        runs: list[list[dict]] = []
        for c in column:
            if runs and c["y"] == runs[-1][-1]["y"] + 1:
                runs[-1].append(c)
            else:
                runs.append([c])
        for run in runs:
            foot = run[0]
            y = foot["y"]
            x_jamb = (x - 1, y, z) in solid or (x + 1, y, z) in solid
            z_jamb = (x, y, z - 1) in solid or (x, y, z + 1) in solid
            foot["kind"] = "marker"
            for k in ("tint", "glow", "shape", "port"):
                foot.pop(k, None)
            for extra in run[1:]:
                cells.remove(extra)

            # Open the doorway: the cell over the marker always (a body is two tall); the one above that when it is
            # wall with more wall over it — a lintel that is the roof (nothing above) stays.
            opened = []
            top = y + len(run) - 1
            for k in (1, 2):
                yy = y + k
                if yy <= top or yy >= height:
                    continue
                above = solid.get((x, yy, z))
                if above is None:
                    continue
                if k == 2 and (x, yy + 1, z) not in solid:
                    continue
                cells.remove(above)
                del solid[(x, yy, z)]
                opened.append(yy)

            lines.append(
                f"    {t.get('key')}: ({x},{y},{z}) {foot['id']} -> marker"
                f"{'' if len(run) == 1 else f' (dropped {len(run) - 1} above)'}"
                f" | jamb {'X' if x_jamb else ''}{'Z' if z_jamb else ''}{'NONE' if not (x_jamb or z_jamb) else ''}"
                f" | opened y={opened if opened else 'nothing'}"
            )
    return lines


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apply", action="store_true", help="rewrite the template file")
    args = ap.parse_args()

    raw = FILE.read_text(encoding="utf-8")
    data = json.loads(raw)
    templates = data if isinstance(data, list) else data.get("templates", [])
    report = []
    for t in templates:
        report.extend(fix_template(t))
    if not report:
        print(f"{FILE.name}: no door block cells")
        return 0

    print(f"{FILE.name}: {len(report)} door marker(s) from block cells")
    print("\n".join(report))
    if args.apply:
        out = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
        FILE.write_text(out, encoding="utf-8", newline="\n")
        print(f"  written {FILE}")
    else:
        print("dry run - pass --apply to write")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
