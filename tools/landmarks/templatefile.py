"""Reading landmark template files: the compact body (palette + run-length rows), the stats line, and the content lists
the validation checks names against. Shared by `geom` (stats after a write), `preview` and `selftest`.

The compact body (#3485, `TemplateBody` in C#): `palette` is a list of {kind, id, tint, glow, shape, keep}; every
string of `rows` is one (y, z) row, `"y,z:x.count.palette x.count.palette"`; `cells` holds what carries settings
(markers) and, in older files, plain cells one by one.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DATA = REPO / "data"
LANDMARKS = DATA / "landmarks"

Look = tuple  # (kind, id, tint, glow, shape, keep)
MATERIAL_TOKENS = ("@wall", "@accent", "@roof", "@floor", "@path")


def loads_relaxed(text: str):
    """Content JSON the way the game reads it: whole-line // comments and trailing commas allowed."""
    text = re.sub(r"^[ \t]*//.*$", "", text, flags=re.MULTILINE)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return json.loads(text)


def resolve(ref: str | Path) -> Path:
    """A template file from a key (data/landmarks/<key>.json), a .json path or an editor export folder."""
    p = Path(ref)
    if p.suffix.lower() == ".json" and p.exists():
        return p
    if p.is_dir() and (p / "landmark.json").exists():
        return p / "landmark.json"
    q = LANDMARKS / f"{ref}.json"
    if q.exists():
        return q
    raise FileNotFoundError(f"no landmark template '{ref}' (looked for {p} and {q})")


def load(ref: str | Path) -> tuple[dict, Path]:
    path = resolve(ref)
    return json.loads(path.read_text(encoding="utf-8")), path


def decode(t: dict) -> tuple[dict[tuple[int, int, int], Look], list[dict]]:
    """The plain cells (position -> look) and the marker cells of a template, compact or not."""
    cells: dict[tuple[int, int, int], Look] = {}
    looks = [(e.get("kind", "block") or "block", e.get("id", "") or "", int(e.get("tint", 0) or 0), int(e.get("glow", 0) or 0),
              int(e.get("shape", 0) or 0), bool(e.get("keep", False))) for e in t.get("palette", []) or []]
    for row in t.get("rows", []) or []:
        head, _, body = row.partition(":")
        y, z = (int(v) for v in head.split(","))
        for run in body.split():
            x, count, pi = (int(v) for v in run.split("."))
            look = looks[pi]
            for i in range(count):
                cells[(x + i, y, z)] = look
    markers = []
    for c in t.get("cells", []) or []:
        kind = c.get("kind", "block") or "block"
        pos = (int(c.get("x", 0)), int(c.get("y", 0)), int(c.get("z", 0)))
        if kind == "marker":
            markers.append(c)
        else:
            cells[pos] = (kind, c.get("id", "") or "", int(c.get("tint", 0) or 0), int(c.get("glow", 0) or 0),
                          int(c.get("shape", 0) or 0), bool(c.get("keep", False)))
    return cells, markers


def stats(t: dict, nbytes: int | None = None) -> str:
    """One line: dims, cells by sort, markers by id, palette and row count, file size."""
    cells, markers = decode(t)
    blocks = sum(1 for v in cells.values() if v[0] == "block")
    cavity = sum(1 for v in cells.values() if v[0] == "cavity")
    keep = sum(1 for v in cells.values() if v[5])
    shaped = sum(1 for v in cells.values() if v[0] == "block" and v[4])
    tinted = sum(1 for v in cells.values() if v[0] == "block" and v[2])
    glowing = sum(1 for v in cells.values() if v[0] == "block" and v[3])
    by_kind: dict[str, int] = {}
    for m in markers:
        by_kind[m.get("id", "?")] = by_kind.get(m.get("id", "?"), 0) + 1
    marks = ", ".join(f"{k} {n}" for k, n in sorted(by_kind.items())) or "none"
    size = f" | {nbytes:,} bytes" if nbytes is not None else ""
    zones = len(t.get("ruinZones", []) or [])
    zone_txt = f", {zones} ruin zones" if zones else ""
    return (f"{t.get('key', '?')}: {t.get('width', 0)}x{t.get('height', 0)}x{t.get('length', 0)} | {len(cells):,} cells "
            f"({blocks:,} blocks, {cavity:,} cavity, {keep:,} keep, {shaped:,} shaped, {tinted:,} tinted, {glowing:,} glowing) "
            f"| {len(markers)} markers ({marks}) | palette {len(t.get('palette', []) or [])}, rows {len(t.get('rows', []) or [])}"
            f"{zone_txt}{size}")


_CACHE: dict[str, object] = {}


def blocks() -> list[dict]:
    """data/blocks.json (cached)."""
    if "blocks" not in _CACHE:
        _CACHE["blocks"] = loads_relaxed((DATA / "blocks.json").read_text(encoding="utf-8"))
    return _CACHE["blocks"]  # type: ignore[return-value]


def block_keys() -> set[str]:
    return {b["key"] for b in blocks()}


def loot_tables() -> set[str]:
    path = DATA / "loot_tables.json"
    if "loot" not in _CACHE:
        _CACHE["loot"] = {e.get("key", "") for e in loads_relaxed(path.read_text(encoding="utf-8"))} if path.exists() else set()
    return _CACHE["loot"]  # type: ignore[return-value]
