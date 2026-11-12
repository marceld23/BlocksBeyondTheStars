"""The landmark design toolkit: `Design` holds the cells of one landmark template and writes the compact
`data/landmarks/<key>.json` the game loads (and the in-game structure editor opens).

A design is a map of PLAIN cells — a block (with tint, glow, shape and the keep flag) or a cavity — plus a list of
MARKER cells (chests, sounds, dens, vendors, doors ...). Coordinates are template cells: x = width, y = up,
z = length, all >= 0 when written, at most 256 per axis.

Materials: wherever a method takes `m`, it accepts
- a block key (`"travertine"`), a `Mat` (block + tint/glow/shape/keep), a `Mix` (weighted blocks picked per cell by a
  hash), or a callable (x, y, z) -> any of these or None (= leave the cell);
- `CLEAR` (remove whatever the design has there — above the ground row an empty cell becomes air) or `CAVITY` (carve
  to air — needed below the ground row, where an empty cell keeps the planet's rock).
Every shape method returns the set of cells it wrote, so the result can be reused (cut windows into a wall you made,
mark a stair core as keep ...). The pure geometry lives in `solids`; the shape codes in `shapecode`.

The original `Design` API of tools/gen_landmarks.py (block, cavity, clear, box, shell, carve, marker, template, write)
is unchanged — the shipped samples are generated with it byte for byte.
"""
from __future__ import annotations

import itertools
import json
import math
from collections.abc import Callable, Iterable
from pathlib import Path
from typing import NamedTuple

try:  # imported as the package `landmarks.geom`
    from . import blockfont, solids, templatefile
    from . import shapecode as sc
except ImportError:  # imported from a script in tools/landmarks (the usual way: `from geom import Design`)
    import blockfont
    import shapecode as sc
    import solids
    import templatefile

Cell = tuple[int, int, int]
OUT = Path(__file__).resolve().parents[2] / "data" / "landmarks"


class _Op:
    def __init__(self, name: str) -> None:
        self.name = name

    def __repr__(self) -> str:
        return self.name


CLEAR = _Op("CLEAR")    # remove the cell from the design
CAVITY = _Op("CAVITY")  # carve the cell to air (underground rooms, tunnels, caverns)


class Mat(NamedTuple):
    """A block with its per-cell modifiers. `tint` / `glow` are 0xRRGGBB (0 = none), `shape` a packed descriptor
    (`shapecode.pack`), `keep` = the ruin pass leaves the cell standing."""
    block: str
    tint: int = 0
    glow: int = 0
    shape: int = 0
    keep: bool = False

    def with_(self, **kw) -> Mat:
        return self._replace(**kw)

    def kept(self) -> Mat:
        return self._replace(keep=True)

    def shaped(self, shape: int) -> Mat:
        return self._replace(shape=shape)

    def tinted(self, tint: int) -> Mat:
        return self._replace(tint=tint)

    def glowing(self, glow: int) -> Mat:
        return self._replace(glow=glow)


class Mix:
    """A material that varies per cell: `Mix("travertine", ("sandstone", 0.2), seed=3)` — weighted choices (weight 1
    when none is given), picked by a hash of the cell, so the same design always comes out the same."""

    def __init__(self, *choices, seed: int = 0) -> None:
        self.choices: list[tuple[object, float]] = []
        for c in choices:
            if isinstance(c, tuple) and not isinstance(c, Mat) and len(c) == 2 and isinstance(c[1], (int, float)):
                self.choices.append((c[0], float(c[1])))
            else:
                self.choices.append((c, 1.0))
        self.total = sum(w for _, w in self.choices)
        self.seed = seed

    def at(self, x: int, y: int, z: int):
        r = solids.hash01(x, y, z, self.seed) * self.total
        for m, w in self.choices:
            r -= w
            if r < 0:
                return m
        return self.choices[-1][0]


def rgb(r: int, g: int, b: int) -> int:
    """A 0xRRGGBB colour (tint / glow) from 0..255 channels."""
    return (r & 0xFF) << 16 | (g & 0xFF) << 8 | (b & 0xFF)


def hexcolor(text: str) -> int:
    """A 0xRRGGBB colour from '#c0362c' or 'c0362c'."""
    return int(text.lstrip("#"), 16)


class Palette(dict):
    """Named materials of a landmark: `p = Palette(wall=Mat("travertine"), trim="sandstone")`, then `p.wall`."""

    def __getattr__(self, name: str):
        try:
            return self[name]
        except KeyError:
            raise AttributeError(name) from None


# The template fields a landmark may carry (StructureTemplate in C#), and what the validation accepts.
HEADER_FIELDS = {
    "key", "name", "tier", "kind", "pack", "role", "kit", "function", "pinOnly", "underground", "style", "weight",
    "planetTypes", "legacyPool", "landmark", "ruin", "ruinProfile", "ruinStrength", "requires", "groundRow",
    "protected", "nameKey", "source", "sourceParams", "ruinZones", "seat", "protection", "minTerrainGeneration",
}
RUIN_MODES = ("", "never", "always")
PROFILES = ("masonry", "lattice", "underground")
CONDITIONS = ("atmosphere", "inhabited")
PROTECTION = ("auto", "protected", "open")
SEATS = ("", "span")
MARKERS = {
    "vendor", "mission_board", "npc", "door_slide", "door_hinge", "door_energy", "room", "loot", "greenhouse", "chest",
    "data_terminal", "tavern", "workshop", "lounge", "guard_post", "doctor", "grocer", "arms_dealer", "sage", "tamer",
    "blockfarmer", "streamer", "reporter", "sound", "den", "crystal",
}
MAX_SIZE = 256
MAX_RUIN_ZONES = 16   # LandmarkRules.MaxRuinZones
SPAN_MIN_LENGTH = 12  # LandmarkRules.SpanMinLength


def settings(**values) -> str:
    """A marker settings line `key=value;key=value` (None / False left out, True = 1, floats without noise)."""
    parts = []
    for k, v in values.items():
        if v is None or v is False or v == "":
            continue
        if v is True:
            v = 1
        elif isinstance(v, float):
            v = f"{v:g}"
        parts.append(f"{k}={v}")
    return ";".join(parts)


class Design:
    """A landmark under construction: plain cells (a block or a cavity) by position, plus marker cells."""

    def __init__(self, key: str, name: str, **header) -> None:
        self.header = {"key": key, "name": name, "landmark": True, **header}
        self.plain: dict[tuple[int, int, int], tuple] = {}
        self.markers: list[dict] = []

    @classmethod
    def part(cls) -> Design:
        """An empty design used as a reusable piece (`stamp` it into the landmark, turned or mirrored)."""
        return cls("part", "part")

    @classmethod
    def load(cls, ref: str | Path) -> Design:
        """A design from an existing template file (a key under data/landmarks, a path, an editor export folder)."""
        t, _ = templatefile.load(ref)
        body = {"width", "height", "length", "palette", "rows", "cells"}
        d = cls(t["key"], t.get("name", ""), **{k: v for k, v in t.items() if k not in body and k not in ("key", "name", "landmark")})
        cells, markers = templatefile.decode(t)
        d.plain.update(cells)
        d.markers.extend(markers)
        return d

    # --- writing cells (the original API) ------------------------------------------------------------------------
    def block(self, x: int, y: int, z: int, block, tint: int = 0, glow: int = 0, shape: int = 0, keep: bool = False) -> None:
        if isinstance(block, str):
            self.plain[(x, y, z)] = ("block", block, tint, glow, shape, keep)
            return
        kw = {k: v for k, v in (("tint", tint), ("glow", glow), ("shape", shape), ("keep", keep)) if v}
        self.put(x, y, z, block, **kw)

    def cavity(self, x: int, y: int, z: int) -> None:
        self.plain[(x, y, z)] = ("cavity", "", 0, 0, 0, False)

    def clear(self, x: int, y: int, z: int) -> None:
        self.plain.pop((x, y, z), None)

    def box(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, block, **kw) -> None:
        """A solid box, corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.block(x, y, z, block, **kw)

    def shell(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, block, **kw) -> None:
        """The four walls of a box (no floor, no roof), corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    if x in (x0, x1) or z in (z0, z1):
                        self.block(x, y, z, block, **kw)

    def carve(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int) -> None:
        """A cavity box (underground: tunnels and caverns), corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.cavity(x, y, z)

    def marker(self, x: int, y: int, z: int, marker: str, settings: str = "") -> None:
        cell = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker}
        if settings:
            cell["config"] = settings
        self.markers.append(cell)

    # --- materials -------------------------------------------------------------------------------------------------
    @staticmethod
    def _static(m) -> bool:
        return m is CLEAR or m is CAVITY or isinstance(m, (str, Mat))

    @staticmethod
    def _look(m, x: int, y: int, z: int, kw: dict):
        """The stored look for material `m` at a cell, CLEAR / CAVITY, or None (leave the cell)."""
        for _ in range(4):
            if isinstance(m, Mix):
                m = m.at(x, y, z)
            elif callable(m) and not isinstance(m, (_Op, str, Mat)):
                m = m(x, y, z)
            else:
                break
        if m is None or m is CLEAR or m is CAVITY:
            return m
        if isinstance(m, str):
            m = Mat(m)
        if not isinstance(m, Mat):
            raise TypeError(f"not a material: {m!r}")
        if kw:
            m = m._replace(**kw)
        return ("block", m.block, int(m.tint), int(m.glow), int(m.shape), bool(m.keep))

    def put(self, x: int, y: int, z: int, m, **kw) -> None:
        """One cell of material `m` (kw: tint, glow, shape, keep override the material's own)."""
        look = self._look(m, x, y, z, kw)
        if look is None:
            return
        if look is CLEAR:
            self.plain.pop((x, y, z), None)
        elif look is CAVITY:
            self.plain[(x, y, z)] = ("cavity", "", 0, 0, 0, False)
        else:
            self.plain[(x, y, z)] = look

    def _allowed(self, pos: Cell, where: str | None) -> bool:
        if where is None:
            return True
        have = self.plain.get(pos)
        if where == "empty":
            return have is None
        if where == "blocks":
            return have is not None and have[0] == "block"
        if where == "not_cavity":
            return have is None or have[0] != "cavity"
        raise ValueError(f"unknown where '{where}' (empty | blocks | not_cavity)")

    def fill(self, cells: Iterable[Cell], m, where: str | None = None, **kw) -> set[Cell]:
        """Every cell of `cells` with material `m`; `where` limits it to 'empty' cells, existing 'blocks' (swap a wall's
        cells for glass), or 'not_cavity'. Returns the cells written."""
        done = set()
        static = self._look(m, 0, 0, 0, kw) if self._static(m) else None
        for pos in cells:
            if not self._allowed(pos, where):
                continue
            look = static if static is not None else self._look(m, pos[0], pos[1], pos[2], kw)
            if look is None:
                continue
            if look is CLEAR:
                self.plain.pop(pos, None)
            elif look is CAVITY:
                self.plain[pos] = ("cavity", "", 0, 0, 0, False)
            else:
                self.plain[pos] = look
            done.add(pos)
        return done

    # --- reading and restyling -------------------------------------------------------------------------------------
    def get(self, x: int, y: int, z: int) -> tuple | None:
        """The stored look (kind, id, tint, glow, shape, keep) or None."""
        return self.plain.get((x, y, z))

    def is_block(self, x: int, y: int, z: int) -> bool:
        v = self.plain.get((x, y, z))
        return v is not None and v[0] == "block"

    def block_cells(self, block: str | None = None) -> set[Cell]:
        """Every block cell (of one block key when given)."""
        return {p for p, v in self.plain.items() if v[0] == "block" and (block is None or v[1] == block)}

    def cavity_cells(self) -> set[Cell]:
        return {p for p, v in self.plain.items() if v[0] == "cavity"}

    def bounds(self) -> tuple[int, int, int, int, int, int] | None:
        return solids.bounds(list(self.plain) + [(m["x"], m["y"], m["z"]) for m in self.markers])

    def restyle(self, cells: Iterable[Cell], block: str | None = None, tint: int | None = None, glow: int | None = None,
                shape: int | None = None, keep: bool | None = None) -> int:
        """Changes fields of existing BLOCK cells (None = leave); returns how many changed."""
        n = 0
        for pos in cells:
            v = self.plain.get(pos)
            if v is None or v[0] != "block":
                continue
            self.plain[pos] = ("block", v[1] if block is None else block, v[2] if tint is None else tint,
                               v[3] if glow is None else glow, v[4] if shape is None else shape, v[5] if keep is None else keep)
            n += 1
        return n

    def paint(self, cells: Iterable[Cell], tint: int) -> int:
        return self.restyle(cells, tint=tint)

    def keep(self, cells: Iterable[Cell]) -> int:
        """Marks block cells as keep: the ruin pass never touches them (and they hold what hangs on them)."""
        return self.restyle(cells, keep=True)

    def keep_box(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int) -> int:
        return self.keep(solids.box(x0, y0, z0, x1, y1, z1))

    def replace(self, old: str, m, cells: Iterable[Cell] | None = None) -> set[Cell]:
        """Swaps every cell of block `old` (within `cells` when given) for material `m`."""
        pool = self.plain.keys() if cells is None else cells
        return self.fill([p for p in pool if p in self.plain and self.plain[p][0] == "block" and self.plain[p][1] == old], m)

    # --- the header --------------------------------------------------------------------------------------------------
    def configure(self, **fields) -> None:
        """Sets template header fields (ruin, ruinProfile, ruinStrength, seat, protection, requires, groundRow,
        planetTypes, nameKey, weight, pinOnly ...) — they are written as given."""
        self.header.update(fields)

    def ruin_zone(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, profile: str = "",
                  strength: float = 0.0) -> None:
        """Adds a ruin zone (`ruinZones`, at most 16): the ruin pass runs once per zone with its own `profile`
        (masonry | lattice | underground; empty = the template's) and `strength` (0..1, 0 = the profile's default);
        cells in no zone use the template's; where zones overlap the first wins. Corners inclusive, any order."""
        (x0, x1), (y0, y1), (z0, z1) = sorted((x0, x1)), sorted((y0, y1)), sorted((z0, z1))
        self.header.setdefault("ruinZones", []).append(
            {"x0": x0, "y0": y0, "z0": z0, "x1": x1, "y1": y1, "z1": z1, "profile": profile, "strength": float(strength)})

    # --- moving and copying ------------------------------------------------------------------------------------------
    def _selected(self, select) -> tuple[list[Cell], list[int]]:
        if select is None:
            return list(self.plain), list(range(len(self.markers)))
        if isinstance(select, tuple) and len(select) == 6 and all(isinstance(v, int) for v in select):
            x0, y0, z0, x1, y1, z1 = select
            (x0, x1), (y0, y1), (z0, z1) = sorted((x0, x1)), sorted((y0, y1)), sorted((z0, z1))

            def inside(p: Cell) -> bool:
                return x0 <= p[0] <= x1 and y0 <= p[1] <= y1 and z0 <= p[2] <= z1
            cells = [p for p in self.plain if inside(p)]
        else:
            pick = set(select)
            cells = [p for p in self.plain if p in pick]

            def inside(p: Cell) -> bool:
                return p in pick
        marks = [i for i, m in enumerate(self.markers) if inside((m["x"], m["y"], m["z"]))]
        return cells, marks

    def _transform(self, select, fn: Callable[[Cell], Cell], shape_fn: Callable[[int], int] | None, copy: bool) -> set[Cell]:
        cells, marks = self._selected(select)
        moved = {}
        for p in cells:
            v = self.plain[p]
            if shape_fn is not None and v[0] == "block" and v[4]:
                v = (v[0], v[1], v[2], v[3], shape_fn(v[4]), v[5])
            moved[fn(p)] = v
        new_marks = []
        for i in marks:
            m = dict(self.markers[i])
            m["x"], m["y"], m["z"] = fn((m["x"], m["y"], m["z"]))
            new_marks.append(m)
        if not copy:
            for p in cells:
                del self.plain[p]
            for i in sorted(marks, reverse=True):
                del self.markers[i]
        self.plain.update(moved)
        self.markers.extend(new_marks)
        return set(moved)

    def translate(self, dx: int, dy: int = 0, dz: int = 0) -> None:
        """Moves everything — cells, markers, ruin zones and the ground row."""
        self.plain = {(x + dx, y + dy, z + dz): v for (x, y, z), v in self.plain.items()}
        for m in self.markers:
            m["x"] += dx
            m["y"] += dy
            m["z"] += dz
        for zone in self.header.get("ruinZones", []):
            for k, d in (("x0", dx), ("x1", dx), ("y0", dy), ("y1", dy), ("z0", dz), ("z1", dz)):
                zone[k] += d
        if dy and "groundRow" in self.header:
            self.header["groundRow"] += dy

    def normalize(self, y: bool = False) -> tuple[int, int, int]:
        """Shifts the design so its smallest x and z (and y when asked) are 0; returns the shift."""
        b = self.bounds()
        if b is None:
            return (0, 0, 0)
        shift = (-b[0], -b[1] if y else 0, -b[2])
        if shift != (0, 0, 0):
            self.translate(*shift)
        return shift

    def copy(self, select, dx: int, dy: int, dz: int) -> set[Cell]:
        """Copies the selection (a 6-int box, a set of cells, or None = everything) by an offset."""
        return self._transform(select, lambda p: (p[0] + dx, p[1] + dy, p[2] + dz), None, True)

    def move(self, select, dx: int, dy: int, dz: int) -> set[Cell]:
        return self._transform(select, lambda p: (p[0] + dx, p[1] + dy, p[2] + dz), None, False)

    def mirror(self, axis: str, at: float, select=None, copy: bool = True) -> set[Cell]:
        """Mirrors the selection across the plane `axis` = `at` (x' = 2 * at - x; .5 for a plane between two cells);
        shaped cells are mirrored with it. `copy` keeps the original (a symmetric building from one half)."""
        i = "xyz".index(axis)

        def fn(p: Cell) -> Cell:
            q = list(p)
            q[i] = round(2 * at - q[i])
            return (q[0], q[1], q[2])
        return self._transform(select, fn, lambda s: sc.mirror_descriptor(s, axis), copy)

    def rotate(self, turns: int, cx: float, cz: float, select=None, copy: bool = False) -> set[Cell]:
        """Turns the selection `turns` quarter turns about the vertical axis through (cx, cz), in the yaw's sense
        (one turn takes +Z to -X). Use matching fractions (both .0 or both .5) so cells land on cells."""
        if (cx - cz) % 1 or (cx + cz) % 1:
            raise ValueError("rotate: cx and cz must both be whole or both end in .5")

        def fn(p: Cell) -> Cell:
            dx, dz = sc.turn_offset(p[0] - cx, p[2] - cz, turns)
            return (round(cx + dx), p[1], round(cz + dz))
        return self._transform(select, fn, lambda s: sc.rotate_descriptor(s, turns), copy)

    def stamp(self, part: Design, x: int, y: int, z: int, turns: int = 0, mirror: str | None = None,
              where: str | None = None) -> set[Cell]:
        """Writes another design (a `Design.part()`) into this one, mirrored (`'x'` / `'z'`) and turned first, so its
        bounding box's low corner lands on (x, y, z). Its markers come along."""
        def tf(p: Cell) -> Cell:
            q = list(p)
            if mirror:
                q["xyz".index(mirror)] *= -1
            dx, dz = sc.turn_offset(q[0], q[2], turns)
            return (dx, q[1], dz)
        positions = list(part.plain) + [(m["x"], m["y"], m["z"]) for m in part.markers]
        if not positions:
            return set()
        moved = [tf(p) for p in positions]
        ox, oy, oz = (min(p[i] for p in moved) for i in range(3))
        done = set()
        for p, v in part.plain.items():
            q = tf(p)
            pos = (q[0] - ox + x, q[1] - oy + y, q[2] - oz + z)
            if not self._allowed(pos, where):
                continue
            if v[0] == "block" and v[4]:
                s = sc.mirror_descriptor(v[4], mirror) if mirror else v[4]
                v = (v[0], v[1], v[2], v[3], sc.rotate_descriptor(s, turns), v[5])
            self.plain[pos] = v
            done.add(pos)
        for m in part.markers:
            q = tf((m["x"], m["y"], m["z"]))
            c = dict(m)
            c["x"], c["y"], c["z"] = q[0] - ox + x, q[1] - oy + y, q[2] - oz + z
            self.markers.append(c)
        return done

    def shear(self, offset: Callable[[int], tuple[int, int]], y0: int | None = None, y1: int | None = None) -> None:
        """Shifts every row y (within y0..y1) by offset(y) = (dx, dz) — a lean, a twist of storeys. Markers move
        with their row. Rows outside the range stay."""
        def shift(y: int) -> tuple[int, int]:
            if (y0 is not None and y < y0) or (y1 is not None and y > y1):
                return (0, 0)
            return offset(y)
        new = {}
        for (x, y, z), v in self.plain.items():
            dx, dz = shift(y)
            new[(x + dx, y, z + dz)] = v
        self.plain = new
        for m in self.markers:
            dx, dz = shift(m["y"])
            m["x"] += dx
            m["z"] += dz

    def lean(self, dx: float, dz: float, y0: int, y1: int, storey: int | None = None) -> None:
        """Leans everything from row y0 up: row y1 and above are shifted by (dx, dz), the rows between in proportion —
        or, with `storey`, in whole storeys of that many rows, so each storey shifts as one block (a leaning tower)."""
        def frac(y: int) -> float:
            if y <= y0:
                return 0.0
            k = y - y0
            if storey:
                k = (k // storey) * storey
            return min(1.0, k / max(1, y1 - y0))
        self.shear(lambda y: (round(frac(y) * dx), round(frac(y) * dz)), y0=y0)

    # --- solids ------------------------------------------------------------------------------------------------------
    def cylinder(self, cx: float, cz: float, y0: int, y1: int, r: float, m, rz: float | None = None, wall: int = 0,
                 where: str | None = None, **kw) -> set[Cell]:
        """A vertical cylinder (elliptic with `rz`); `wall` > 0 = a tube (a round tower, open at both ends)."""
        return self.fill(solids.cylinder(cx, cz, y0, y1, r, rz, wall), m, where, **kw)

    def ring(self, cx: float, cz: float, y0: int, y1: int, r: float, wall: int, m, rz: float | None = None,
             where: str | None = None, **kw) -> set[Cell]:
        """A ring wall `wall` thick (an elliptic one with `rz`: an amphitheatre's facade)."""
        return self.fill(solids.cylinder(cx, cz, y0, y1, r, rz, wall), m, where, **kw)

    def disc(self, cx: float, cz: float, y: int, r: float, m, rz: float | None = None, where: str | None = None, **kw) -> set[Cell]:
        """One row of a disc (a round floor or roof)."""
        return self.fill(solids.extrude(solids.disc(cx, cz, r, rz), y, y), m, where, **kw)

    def cone(self, cx: float, cz: float, y0: int, y1: int, r0: float, m, r1: float = 0.5, wall: int = 0,
             aspect: float = 1.0, where: str | None = None, **kw) -> set[Cell]:
        """A cone or frustum from radius r0 on row y0 to r1 on row y1 (a spire, a roof, a stalagmite of cells)."""
        return self.fill(solids.cone(cx, cz, y0, y1, r0, r1, wall, aspect), m, where, **kw)

    def sphere(self, cx: float, cy: float, cz: float, r: float, m, wall: int = 0, where: str | None = None, **kw) -> set[Cell]:
        return self.fill(solids.sphere(cx, cy, cz, r, wall), m, where, **kw)

    def ellipsoid(self, cx: float, cy: float, cz: float, rx: float, ry: float, rz: float, m, wall: int = 0,
                  ymin: int | None = None, ymax: int | None = None, where: str | None = None, **kw) -> set[Cell]:
        return self.fill(solids.ellipsoid(cx, cy, cz, rx, ry, rz, wall, ymin, ymax), m, where, **kw)

    def dome(self, cx: float, cz: float, y0: int, r: float, m, ry: float | None = None, rz: float | None = None,
             wall: int = 0, where: str | None = None, **kw) -> set[Cell]:
        """A dome standing on row y0 (a bun, a cupola); hollow ones are open at the bottom."""
        return self.fill(solids.dome(cx, cz, y0, r, ry, rz, wall), m, where, **kw)

    def capsule(self, p0, p1, r0: float, m, r1: float | None = None, where: str | None = None, **kw) -> set[Cell]:
        return self.fill(solids.capsule(p0, p1, r0, r1), m, where, **kw)

    def prism(self, cells2d: Iterable[tuple[int, int]], y0: int, y1: int, m, where: str | None = None, **kw) -> set[Cell]:
        """A 2-D shape (`solids.star`, `solids.polygon`, `solids.disc` ...) extruded from row y0 to y1."""
        return self.fill(solids.extrude(cells2d, y0, y1), m, where, **kw)

    def wall_line(self, x0: float, z0: float, x1: float, z1: float, y0: int, y1: int, thickness: float, m,
                  where: str | None = None, **kw) -> set[Cell]:
        """A straight wall between two (x, z) points at any angle, `thickness` thick, rows y0..y1."""
        return self.fill(solids.extrude(solids.segment2d(x0, z0, x1, z1, thickness), y0, y1), m, where, **kw)

    def polygon_wall(self, points: list[tuple[float, float]], y0: int, y1: int, thickness: float, m,
                     closed: bool = True, where: str | None = None, **kw) -> set[Cell]:
        """A curtain wall along (x, z) corner points (closed: back to the first)."""
        return self.fill(solids.polygon_wall(points, y0, y1, thickness, closed), m, where, **kw)

    def column(self, x: int, z: int, y0: int, y1: int, m, base=None, capital=None, round_shape: bool = True) -> set[Cell]:
        """A one-cell column from row y0 to y1: the `cylinder` form (round_shape), a cube `base` on y0 and a `capital`
        on y1 when given."""
        out = set()
        for y in range(y0, y1 + 1):
            if y == y0 and base is not None:
                self.put(x, y, z, base)
            elif y == y1 and capital is not None:
                self.put(x, y, z, capital)
            else:
                self.put(x, y, z, m, **({"shape": sc.pack(sc.CYLINDER)} if round_shape else {}))
            out.add((x, y, z))
        return out

    # --- openings ------------------------------------------------------------------------------------------------
    def arch(self, axis: str, u0: int, y0: int, a0: int, a1: int, width: int, height: int, kind: str = "round",
             rise: float | None = None, m=CLEAR, where: str | None = None) -> set[Cell]:
        """Cuts (or fills, with another `m`) an arched opening through a wall — see `solids.arch` for the axes."""
        return self.fill(solids.arch(axis, u0, y0, a0, a1, width, height, kind, rise), m, where)

    def arcade(self, axis: str, u0: int, y0: int, a0: int, a1: int, count: int, width: int, height: int, pier: int,
               kind: str = "round", rise: float | None = None, m=CLEAR, where: str | None = None) -> set[Cell]:
        """`count` arches in a row, `pier` cells of wall between them."""
        return self.fill(solids.arcade(axis, u0, y0, a0, a1, count, width, height, pier, kind, rise), m, where)

    def ring_arcade(self, cells: Iterable[Cell], ellipse: solids.Ellipse, y0: int, count: int, width: float,
                    height: float, kind: str = "round", rise: float | None = None, phase: float = 0.0, m=CLEAR,
                    where: str | None = None) -> set[Cell]:
        """Cuts `count` evenly spaced arches into the cells of a ring wall (`ring`), radially, sills on row y0."""
        return self.fill(solids.ring_openings(cells, ellipse, y0, count, width, height, kind, rise, phase), m, where)

    def battlements(self, cells: Iterable[Cell], m, period: int = 2, height: int = 1,
                    ellipse: solids.Ellipse | None = None) -> set[Cell]:
        """Merlons on the top course of a wall: every other `period`-long stretch of its highest cells gets `height`
        blocks on top (along a ring, pass its `ellipse` to space them by arc length)."""
        out = set()
        for x, y, z in solids.highest_cells(cells):
            k = int(ellipse.arc(x, z) // period) if ellipse is not None else (x + z) // period
            if k % 2 == 0:
                out |= self.fill([(x, y + h, z) for h in range(1, height + 1)], m)
        return out

    def doorway(self, x: int, y: int, z: int, width: int = 2, height: int = 3, along: str = "x",
                door: str | None = "hinge", m=CLEAR) -> set[Cell]:
        """Clears a rectangular opening (width along `along`, from (x, y, z) up `height` rows) and puts a door marker
        in its bottom cell (`door_hinge`, `door_slide`, `door_energy`; None = an open doorway)."""
        cells = {(x + (i if along == "x" else 0), y + j, z + (i if along == "z" else 0)) for i in range(width) for j in range(height)}
        self.fill(cells, m)
        if door:
            self.door(x, y, z, door)
        return cells

    # --- lines, members, trusses, cables -------------------------------------------------------------------------
    def _member(self, path: list[Cell], m, style: str, where: str | None) -> set[Cell]:
        if not path:
            return set()
        if style == "solid":
            return self.fill(path, m, where, shape=0)
        span_ = [abs(path[-1][i] - path[0][i]) for i in range(3)]
        dominant = span_.index(max(span_))
        along = {0: sc.beam("x"), 1: sc.post(), 2: sc.beam("z")}
        out = set()
        for i, p in enumerate(path):
            if not self._allowed(p, where):
                continue
            if style == "thin":
                shape = along[dominant]
            else:  # auto: a straight run is a post / beam, a turn is a cube node
                axes = set()
                for q in ((path[i - 1] if i > 0 else None), (path[i + 1] if i + 1 < len(path) else None)):
                    if q is not None:
                        axes |= {k for k in range(3) if q[k] != p[k]}
                shape = along[axes.pop()] if len(axes) == 1 else (along[dominant] if not axes else 0)
            self.put(p[0], p[1], p[2], m, shape=shape)
            out.add(p)
        return out

    def line(self, p0, p1, m, where: str | None = None, **kw) -> set[Cell]:
        """A line of full cells between two points (edge-connected, so the ruin pass keeps it in one piece)."""
        return self.fill(_line18(p0, p1), m, where, **kw)

    def member(self, p0, p1, m, style: str = "auto", where: str | None = None) -> set[Cell]:
        """A structural member between two points made of the slim forms: `auto` = a face-connected line whose straight
        runs are posts (vertical) or beams (along x / z) and whose turns are cube nodes; `thin` = an edge-connected line
        of posts or beams along the member's main axis; `solid` = full cubes."""
        path = solids.walk(p0, p1) if style == "auto" else _line18(p0, p1)
        return self._member(path, m, style, where)

    def polyline(self, points: list, m, style: str = "solid", where: str | None = None) -> set[Cell]:
        out = set()
        for p, q in itertools.pairwise(points):
            out |= self.member(p, q, m, style, where)
        return out

    def truss(self, p0, p1, m, size: int = 3, panel: int = 4, brace: str | None = "zigzag", ties: bool = True,
              style: str = "auto", brace_style: str = "solid", brace_m=None, where: str | None = None) -> set[Cell]:
        """A lattice member from p0 to p1: four chords at the corners of a `size` x `size` section (around the line,
        across the two axes other than its main one), cut into panels about `panel` long; per panel a tie frame of
        beams across the section (`ties`) and a diagonal on each of the four faces (`zigzag` alternates, `x` crosses,
        None = none). Eiffel-style legs, bridge girders, crane jibs."""
        d = [p1[i] - p0[i] for i in range(3)]
        main = max(range(3), key=lambda i: abs(d[i]))
        a, b = [i for i in range(3) if i != main]
        lo = -((size - 1) // 2)
        hi = lo + size - 1
        corners = [(lo, lo), (hi, lo), (hi, hi), (lo, hi)]
        n = max(1, round(abs(d[main]) / max(1, panel)))
        brace_m = m if brace_m is None else brace_m

        def at(t: float, oa: int, ob: int) -> tuple[float, float, float]:
            q = [p0[i] + d[i] * t for i in range(3)]
            q[a] += oa
            q[b] += ob
            return (q[0], q[1], q[2])
        out = set()
        for oa, ob in corners:
            out |= self.member(at(0, oa, ob), at(1, oa, ob), m, style, where)
        for k in range(n + 1):
            t = k / n
            if ties and size > 1:
                for i in range(4):
                    out |= self.member(at(t, *corners[i]), at(t, *corners[(i + 1) % 4]), m, style, where)
            if brace and k < n and size > 1:
                t1 = (k + 1) / n
                for i in range(4):
                    c0, c1 = corners[i], corners[(i + 1) % 4]
                    if brace in ("zigzag", "x"):
                        s, e = (c0, c1) if k % 2 == 0 or brace == "x" else (c1, c0)
                        out |= self.member(at(t, *s), at(t1, *e), brace_m, brace_style, where)
                    if brace == "x":
                        out |= self.member(at(t, *c1), at(t1, *c0), brace_m, brace_style, where)
        return out

    def cable(self, p0, p1, sag: float, m, kind: str = "parabola", style: str = "auto", where: str | None = None) -> list[Cell]:
        """A hanging cable from p0 to p1 sagging `sag` below the chord at its middle (`parabola` = a suspension
        bridge's main cable, `catenary` = a free chain), drawn as beams with cube nodes (`auto`) or cubes (`solid`)."""
        path = solids.cable(p0, p1, sag, kind, connect=6 if style == "auto" else 26)
        if style != "auto":
            path = _densify18(path)
        self._member(path, m, style, where)
        return path

    def hangers(self, p0, p1, sag: float, deck_y: int, m, every: int = 4, kind: str = "parabola", skip: int = 0,
                where: str | None = None) -> set[Cell]:
        """Vertical hangers (posts) from a cable (as `cable`) down to the row above `deck_y`, one every `every` cells
        along the cable's main horizontal axis; `skip` leaves that many out at each end (near the towers)."""
        at = solids.sag_curve(p0, p1, sag, kind)
        main = 0 if abs(p1[0] - p0[0]) >= abs(p1[2] - p0[2]) else 2
        u0, u1 = round(p0[main]), round(p1[main])
        step = 1 if u1 >= u0 else -1
        out = set()
        length = abs(u1 - u0)
        for k, u in enumerate(range(u0, u1 + step, step)):
            if k % every or k < skip or k > length - skip or length == 0:
                continue
            x, y, z = at(k / length)
            top = round(y) - 1
            for yy in range(deck_y + 1, top + 1):
                pos = (round(x), yy, round(z))
                if self._allowed(pos, where):
                    self.put(*pos, m, shape=sc.post())
                    out.add(pos)
        return out

    # --- stairs ----------------------------------------------------------------------------------------------------
    def stairs(self, x: int, y: int, z: int, dx: int, dz: int, steps: int, m, width: int = 1, fill_below: bool = False,
               shaped: bool = True) -> tuple[int, int, int]:
        """A straight staircase climbing one row per cell toward (dx, dz), its first step at (x, y, z), `width` cells
        wide to the right-hand side (+x for a z-stair, +z for an x-stair). With `shaped` every step is the `stairs`
        form (two half steps — walkable without jumping); `fill_below` makes it a solid mass. Returns the cell just
        beyond the top step (where the landing goes)."""
        px, pz = (0, 1) if dx else (1, 0)
        for k in range(steps):
            for w in range(width):
                cx, cz = x + k * dx + w * px, z + k * dz + w * pz
                self.put(cx, y + k, cz, m, shape=sc.stairs(dx, dz) if shaped else 0)
                if fill_below:
                    for yy in range(y, y + k):
                        self.put(cx, yy, cz, m, shape=0)
        return (x + steps * dx, y + steps, z + steps * dz)

    def spiral_stair(self, cx: float, cz: float, y0: int, y1: int, r_in: float, r_out: float, m,
                     steps_per_turn: int = 16, ccw: bool = True, start: float = 0.0, newel=None, clear_above: int = 3,
                     shaped: bool = True) -> set[Cell]:
        """A spiral staircase in the ring r_in..r_out around (cx, cz), one row per step, `steps_per_turn` steps per
        turn, from row y0 up to y1. The cells of the ring are dealt to the steps by angle (from `start`, radians from
        +X), so every turn is a closed helix. `clear_above` rows above each tread are cleared first (floors crossing
        the stairwell get their hole). With `shaped` the first cell of each step (seen in the climbing direction) is a
        `stairs` form, so the stair is walked without jumping. `newel` = a material for the centre post."""
        ring = solids.disc(cx, cz, r_out) - solids.disc(cx, cz, r_in)
        two_pi = 2 * math.pi

        def step_of(x: int, z: int) -> int:
            a = math.atan2(z - cz, x - cx)
            a = (a if ccw else -a) - start
            return int((a % two_pi) / two_pi * steps_per_turn) % steps_per_turn

        treads: dict[Cell, int] = {}
        for x, z in ring:
            k = step_of(x, z)
            y = y0 + k
            while y <= y1:
                treads[(x, y, z)] = k
                y += steps_per_turn
        for (x, y, z) in treads:
            for h in range(1, clear_above + 1):
                if (x, y + h, z) not in treads:
                    self.plain.pop((x, y + h, z), None)
        for (x, y, z), k in treads.items():
            shape = 0
            if shaped:
                tx, tz = -(z - cz), (x - cx)
                if not ccw:
                    tx, tz = -tx, -tz
                dx, dz = ((1 if tx > 0 else -1), 0) if abs(tx) >= abs(tz) else (0, (1 if tz > 0 else -1))
                prev = (x - dx, z - dz)
                if prev not in ring or step_of(*prev) != k:
                    shape = sc.stairs(dx, dz)
            self.put(x, y, z, m, shape=shape)
        if newel is not None and r_in > 0:
            self.fill(solids.cylinder(cx, cz, y0, y1 + clear_above, r_in), newel)
        return set(treads)

    # --- pyramids --------------------------------------------------------------------------------------------------
    def pyramid(self, x0: int, z0: int, y0: int, base_x: int, m, base_z: int | None = None, step: int = 1,
                rise: int = 1, top: int = 1, wall: int = 0, smooth: bool = False, corner: str = "pyramid",
                where: str | None = None) -> set[Cell]:
        """A pyramid on the base rectangle from (x0, z0), base_x by base_z, on row y0: inset `step` cells every `rise`
        rows up to a top `top` cells wide. `smooth` (45 degrees: step = rise = 1) dresses each layer's rim with `ramp`
        forms rising inward and its corners with `corner` ('pyramid', 'ramp' or 'cube'); a wider top stays a flat
        platform, a single apex becomes a pyramid form. `wall` > 0 = a crust that thick (chambers inside)."""
        if smooth and not (step == 1 and rise == 1):
            raise ValueError("pyramid: smooth needs step = rise = 1 (a 45 degree slope)")
        layers = solids.pyramid_layers(x0, z0, y0, base_x, base_z, step, rise, top)
        cells = solids.pyramid(x0, z0, y0, base_x, base_z, step, rise, top, wall)
        out = self.fill(cells, m, where)
        if not smooth:
            return out
        last = len(layers) - 1
        for i, (y, xa, za, xb, zb) in enumerate(layers):
            if i == last:
                if xa == xb and za == zb:
                    self.restyle([(xa, y, za)], shape=sc.pack(sc.PYRAMID))
                continue
            for x in range(xa, xb + 1):
                for z in range(za, zb + 1):
                    sides = [(1, 0)] if x == xa else [(-1, 0)] if x == xb else []
                    sides += [(0, 1)] if z == za else [(0, -1)] if z == zb else []
                    if not sides:
                        continue
                    if len(sides) == 2:
                        shape = {"pyramid": sc.pack(sc.PYRAMID), "ramp": sc.ramp(*sides[0]), "cube": 0}[corner]
                    else:
                        shape = sc.ramp(*sides[0])
                    self.restyle([(x, y, z)], shape=shape)
        return out

    # --- underground -------------------------------------------------------------------------------------------------
    def room(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, floor=None, walls=None, ceiling=None,
             cavity: bool = True) -> set[Cell]:
        """A room: the box is carved to air (`cavity`; False = just cleared, for rooms above the ground row), with an
        optional `floor` row below it, `ceiling` row above it and a one-cell `walls` lining around it."""
        (x0, x1), (y0, y1), (z0, z1) = sorted((x0, x1)), sorted((y0, y1)), sorted((z0, z1))
        inner = self.fill(solids.box(x0, y0, z0, x1, y1, z1), CAVITY if cavity else CLEAR)
        if floor is not None:
            self.fill(solids.box(x0, y0 - 1, z0, x1, y0 - 1, z1), floor, "not_cavity")
        if ceiling is not None:
            self.fill(solids.box(x0, y1 + 1, z0, x1, y1 + 1, z1), ceiling, "not_cavity")
        if walls is not None:
            self.fill(solids.box_walls(x0 - 1, y0, z0 - 1, x1 + 1, y1, z1 + 1) - inner, walls, "not_cavity")
        return inner

    def tunnel(self, points: list[Cell], width: int = 3, height: int = 3, floor=None, frame=None, every: int = 4,
               light=None, light_every: int = 0, track=None) -> set[Cell]:
        """A mine tunnel through (x, y, z) points (consecutive points differ along x or z only; y is the floor row of
        the walkway): `width` x `height` carved to air around the centre line, an optional `floor` row below,
        timber `frame`s (two side posts and a cap) every `every` cells, `light`s set into the side wall every
        `light_every` cells and a `track` along the centre of the walkway."""
        carved: set[Cell] = set()
        lo = -((width - 1) // 2)
        hi = lo + width - 1
        n = 0
        for p, q in itertools.pairwise(points):
            if p[1] != q[1] or (p[0] != q[0] and p[2] != q[2]):
                raise ValueError(f"tunnel: segment {p} -> {q} must run along x or z on one row")
            along_x = p[0] != q[0]
            seg = solids.walk(p, q)
            for i, (x, y, z) in enumerate(seg):
                if i == 0 and n:
                    continue
                cross = [(x, z + o) if along_x else (x + o, z) for o in range(lo, hi + 1)]
                carved |= self.fill([(cx_, y + h, cz_) for cx_, cz_ in cross for h in range(height)], CAVITY)
                if floor is not None:
                    self.fill([(cx_, y - 1, cz_) for cx_, cz_ in cross], floor, "not_cavity")
                if track is not None:
                    self.put(x, y, z, track)
                if frame is not None and every and n % every == 0:
                    for cx_, cz_ in (cross[0], cross[-1]):
                        for h in range(height - 1):
                            self.put(cx_, y + h, cz_, frame)
                    for cx_, cz_ in cross:
                        self.put(cx_, y + height - 1, cz_, frame)
                if light is not None and light_every and n % light_every == light_every // 2:
                    side = (x, y + 1, z + hi + 1) if along_x else (x + hi + 1, y + 1, z)
                    self.put(*side, light)
                n += 1
        return carved

    def shaft(self, x: int, z: int, y_bottom: int, y_top: int, width: int = 1, ladder=None, ladder_side: tuple[int, int] = (0, 1)) -> set[Cell]:
        """A vertical shaft carved to air (`width` square from (x, z)), with a `ladder` up the wall on `ladder_side`."""
        cells = solids.box(x, y_bottom, z, x + width - 1, y_top, z + width - 1)
        out = self.fill(cells, CAVITY)
        if ladder is not None:
            lx = x + (width if ladder_side[0] > 0 else -1 if ladder_side[0] < 0 else 0)
            lz = z + (width if ladder_side[1] > 0 else -1 if ladder_side[1] < 0 else 0)
            for y in range(y_bottom, y_top + 1):
                self.put(lx, y, lz, ladder)
        return out

    def cavern(self, cx: float, cy: float, cz: float, rx: float, ry: float, rz: float, roughness: float = 0.3,
               seed: int = 1, scale: float = 6.0, floor: int | None = None) -> set[Cell]:
        """Carves an irregular cave (`solids.cavern`) to air; returns its cells (for `dripstones`, chests, a den)."""
        return self.fill(solids.cavern(cx, cy, cz, rx, ry, rz, roughness, seed, scale, floor), CAVITY)

    def dripstone(self, x: int, y: int, z: int, length: int, m="dripstone", hanging: bool = False) -> set[Cell]:
        """One stalagmite growing up from row y (or, `hanging`, a stalactite growing down from row y): full cells
        and a cone tip — the hanging cone is turned upside down."""
        dy = -1 if hanging else 1
        out = set()
        for i in range(length):
            tip = i == length - 1
            shape = (sc.stalactite() if hanging else sc.pack(sc.CONE)) if tip else 0
            self.put(x, y + i * dy, z, m, shape=shape)
            out.add((x, y + i * dy, z))
        return out

    def dripstones(self, cave: Iterable[Cell], m="dripstone", density: float = 0.04, max_len: int = 5, seed: int = 1,
                   up: bool = True, down: bool = True, keep_clear: Iterable[Cell] = ()) -> set[Cell]:
        """Stalagmites on the floor and stalactites from the ceiling of a cave's cells, each place by a hash (`density`
        per floor / ceiling cell), 1..max_len long, never longer than the cave is high there; `keep_clear` cells
        (a path through the cave) get none."""
        cave = set(cave)
        avoid = set(keep_clear)
        out: set[Cell] = set()
        for x, y, z in sorted(solids.lowest_cells(cave)) if up else ():
            if (x, y, z) in avoid or solids.hash01(x, y, z, seed) >= density:
                continue
            n = 1 + int(solids.hash01(x, y, z, seed + 1) * max_len)
            room = sum(1 for i in range(n + 2) if (x, y + i, z) in cave)
            out |= self.dripstone(x, y, z, max(1, min(n, room - 2)), m)
        for x, y, z in sorted(solids.highest_cells(cave)) if down else ():
            if (x, y, z) in avoid or solids.hash01(x, y, z, seed + 7) >= density:
                continue
            n = 1 + int(solids.hash01(x, y, z, seed + 8) * max_len)
            room = sum(1 for i in range(n + 2) if (x, y - i, z) in cave)
            out |= self.dripstone(x, y, z, max(1, min(n, room - 2)), m, hanging=True)
        return out

    def scatter(self, cells: Iterable[Cell], m, density: float, seed: int = 1, where: str | None = None, **kw) -> set[Cell]:
        """Material `m` on a hashed share (`density`) of the cells — moss on stone, rubble on a floor, sesame seeds."""
        return self.fill([p for p in cells if solids.hash01(p[0], p[1], p[2], seed) < density], m, where, **kw)

    # --- text --------------------------------------------------------------------------------------------------------
    def text(self, s: str, x: int, y: int, z: int, m, dir: str = "+x", scale: int = 1, depth: int = 1,
             where: str | None = None, **kw) -> set[Cell]:
        """Block letters (the game's 5 x 7 font): the line starts at (x, y, z), its baseline on row y, letters
        `scale` cells per font pixel, `depth` cells thick into the wall. `dir` is the reading direction: '+x' reads
        from -Z (the reader stands on the low-z side), '-x' from +Z, '+z' from +X, '-z' from -X; the depth goes away
        from the reader. Returns the cells (`text_size` gives the width)."""
        adv = {"+x": (1, 0), "-x": (-1, 0), "+z": (0, 1), "-z": (0, -1)}[dir]
        away = {"+x": (0, 1), "-x": (0, -1), "+z": (-1, 0), "-z": (1, 0)}[dir]
        cells = set()
        for col, row in blockfont.ink(s):
            for sx in range(scale):
                for sy in range(scale):
                    u = col * scale + sx
                    v = (blockfont.HEIGHT - 1 - row) * scale + sy
                    for k in range(depth):
                        cells.add((x + adv[0] * u + away[0] * k, y + v, z + adv[1] * u + away[1] * k))
        return self.fill(cells, m, where, **kw)

    @staticmethod
    def text_size(s: str, scale: int = 1) -> tuple[int, int]:
        """(width, height) in cells of a `text` line."""
        return blockfont.line_width(s) * scale, blockfont.HEIGHT * scale

    # --- markers -----------------------------------------------------------------------------------------------------
    def _free(self, x: int, y: int, z: int) -> None:
        v = self.plain.get((x, y, z))
        if v is not None and v[0] == "block":  # a marker's cell is never filled; a cavity stays (air underground)
            del self.plain[(x, y, z)]

    def chest(self, x: int, y: int, z: int, table: str | None = None, items: str | None = None,
              chance: float | None = None, block: str | None = None, hidden: bool = False) -> None:
        """A chest: `items` "iron_plate:2-4,gold_ingot:1", `table` a key of data/loot_tables.json (neither = the table
        ruin_common), `chance` it exists at all, `block` wood_crate / crate, `hidden` a stone on top."""
        self._free(x, y, z)
        self.marker(x, y, z, "chest", settings(items=items, table=table, chance=chance, block=block, hidden=hidden))

    def sound(self, x: int, y: int, z: int, sound: str, radius: int = 64, volume: float = 0.6) -> None:
        """A sound emitter: a bundled loop, silent at `radius` blocks, full near its centre."""
        self._free(x, y, z)
        self.marker(x, y, z, "sound", settings(sound=sound, radius=radius, volume=float(volume)))

    def den(self, x: int, y: int, z: int, species: str = "native", count: int = 3, radius: int = 10) -> None:
        """A creature den: `species` an authored creature key or `native`, kept at `count` within `radius`."""
        self._free(x, y, z)
        self.marker(x, y, z, "den", settings(species=species, count=count, radius=radius))

    def vendor(self, x: int, y: int, z: int, theme: str, inside: bool = False, name: str | None = None) -> None:
        """A vendor post: `theme` the marketTheme of the recipes it sells, `inside` sells only in its own room."""
        self._free(x, y, z)
        self.marker(x, y, z, "vendor", settings(theme=theme, inside=inside, name=name))

    def door(self, x: int, y: int, z: int, kind: str = "hinge") -> None:
        """A door marker (`hinge`, `slide`, `energy`) in the bottom cell of a doorway; the opening is measured from
        the solid cells around it."""
        self._free(x, y, z)
        self.marker(x, y, z, kind if kind.startswith("door_") else f"door_{kind}")

    def npc(self, x: int, y: int, z: int, post: str = "npc", config: str = "") -> None:
        """A resident: `npc`, or a profession post (doctor, grocer, sage, tamer, ...)."""
        self._free(x, y, z)
        self.marker(x, y, z, post, config)

    # --- validation and output ---------------------------------------------------------------------------------------
    def problems(self) -> tuple[list[str], list[str]]:
        """(errors, warnings): errors stop `write`, warnings are printed."""
        errors: list[str] = []
        warnings: list[str] = []
        h = self.header
        b = self.bounds()
        if b is None:
            return ["the design is empty"], warnings
        x0, y0, z0, x1, y1, z1 = b
        if min(x0, y0, z0) < 0:
            errors.append(f"cells below 0 (min {x0},{y0},{z0}); call normalize() or translate()")
        w, ht, ln = x1 + 1, y1 + 1, z1 + 1
        if max(w, ht, ln) > MAX_SIZE:
            errors.append(f"{w}x{ht}x{ln} is larger than {MAX_SIZE} on an axis")
        for k in h:
            if k not in HEADER_FIELDS:
                warnings.append(f"unknown header field '{k}' (passed through)")
        # The same rules LandmarkRules.Problems applies at load (a template it objects to is dropped from the game).
        checks = (("ruin", RUIN_MODES), ("ruinProfile", ("",) + PROFILES), ("protection", ("",) + PROTECTION), ("seat", SEATS))
        for field, allowed in checks:
            if field in h and str(h[field]).lower() not in allowed:
                errors.append(f"{field} '{h[field]}' not one of {' | '.join(a for a in allowed if a)}")
        if str(h.get("seat", "")).lower() == "span" and max(w, ln) < SPAN_MIN_LENGTH:
            errors.append(f"a span needs a long axis of at least {SPAN_MIN_LENGTH} blocks")
        if not 0 <= float(h.get("ruinStrength", 0) or 0) <= 1:
            errors.append("ruinStrength must lie between 0 and 1")
        for r in h.get("requires", []):
            if r not in CONDITIONS:
                errors.append(f"unknown condition '{r}' (atmosphere | inhabited)")
        if not 0 <= int(h.get("groundRow", 0) or 0) < ht:
            errors.append("groundRow lies outside the structure")
        zones = h.get("ruinZones", [])
        if len(zones) > MAX_RUIN_ZONES:
            errors.append(f"more than {MAX_RUIN_ZONES} ruin zones")
        for i, zone in enumerate(zones, 1):
            if str(zone.get("profile", "")).lower() not in ("",) + PROFILES:
                errors.append(f"ruin zone {i}: unknown profile '{zone.get('profile')}'")
            if not 0 <= float(zone.get("strength", 0)) <= 1:
                errors.append(f"ruin zone {i}: strength must lie between 0 and 1")
            lo = [min(zone[a + "0"], zone[a + "1"]) for a in "xyz"]
            hi = [max(zone[a + "0"], zone[a + "1"]) for a in "xyz"]
            if min(lo) < 0 or hi[0] >= w or hi[1] >= ht or hi[2] >= ln:
                errors.append(f"ruin zone {i} reaches outside the structure ({w}x{ht}x{ln})")
        if str(h.get("ruin", "")).lower() == "never" and zones:
            warnings.append("ruin zones on a landmark that never stands as a ruin do nothing")
        try:
            known = templatefile.block_keys()
        except (OSError, ValueError):
            known = set()
        if known:
            used = {v[1] for v in self.plain.values() if v[0] == "block"}
            for blk in sorted(used - known - set(templatefile.MATERIAL_TOKENS)):
                warnings.append(f"unknown block '{blk}'")
        tables = templatefile.loot_tables()
        plain = self.plain
        for m in self.markers:
            pos = (m["x"], m["y"], m["z"])
            if m["id"] not in MARKERS:
                warnings.append(f"marker '{m['id']}' at {pos} is not a known marker kind")
            if pos in plain and plain[pos][0] == "block":
                warnings.append(f"marker '{m['id']}' at {pos} sits in a block cell")
            for part in m.get("config", "").split(";"):
                if part.startswith("table=") and tables and part[6:] not in tables:
                    warnings.append(f"chest at {pos}: unknown loot table '{part[6:]}'")
        return errors, warnings

    def template(self) -> dict:
        xs = [p[0] for p in self.plain] + [m["x"] for m in self.markers]
        ys = [p[1] for p in self.plain] + [m["y"] for m in self.markers]
        zs = [p[2] for p in self.plain] + [m["z"] for m in self.markers]
        if min(xs) < 0 or min(ys) < 0 or min(zs) < 0:
            raise ValueError(f"{self.header['key']}: cells below 0 (min {min(xs)},{min(ys)},{min(zs)}); call normalize()")
        palette: list[dict] = []
        index: dict[tuple, int] = {}
        rows: list[str] = []
        cells = sorted(self.plain.items(), key=lambda kv: (kv[0][1], kv[0][2], kv[0][0]))
        i = 0
        while i < len(cells):
            (_, y, z), _ = cells[i]
            runs = []
            while i < len(cells) and cells[i][0][1] == y and cells[i][0][2] == z:
                (x, _, _), look = cells[i]
                count = 1
                while (i + count < len(cells) and cells[i + count][0] == (x + count, y, z) and cells[i + count][1] == look):
                    count += 1
                if look not in index:
                    index[look] = len(palette)
                    kind, block, tint, glow, shape, keep = look
                    entry = {"kind": kind, "id": block}
                    entry.update({k: v for k, v in (("tint", tint), ("glow", glow), ("shape", shape)) if v})
                    if keep:
                        entry["keep"] = True
                    palette.append(entry)
                runs.append(f"{x}.{count}.{index[look]}")
                i += count
            rows.append(f"{y},{z}:" + " ".join(runs))
        return {
            **self.header,
            "width": max(xs) + 1,
            "height": max(ys) + 1,
            "length": max(zs) + 1,
            "palette": palette,
            "rows": rows,
            "cells": self.markers,
        }

    def stats(self) -> str:
        t = self.template()
        return templatefile.stats(t, len(_dump(t).encode("utf-8")))

    def write(self, out: Path | str | None = None, check: bool = True) -> Path:
        """Writes `<out>/<key>.json` (default data/landmarks) with LF line endings and prints the stats line.
        With `check`, errors from `problems()` stop the write and warnings are printed."""
        if check:
            errors, warnings = self.problems()
            for w in warnings:
                print(f"  warning: {self.header['key']}: {w}")
            if errors:
                raise ValueError(f"{self.header['key']}: " + "; ".join(errors))
        folder = OUT if out is None else Path(out)
        folder.mkdir(parents=True, exist_ok=True)
        t = self.template()
        path = folder / f"{t['key']}.json"
        text = _dump(t)
        with path.open("w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print(templatefile.stats(t, path.stat().st_size))
        return path


def _dump(t: dict) -> str:
    return json.dumps(t, ensure_ascii=False, separators=(",", ":")) + "\n"


def _line18(p0, p1) -> list[Cell]:
    return _densify18(solids.line(p0, p1))


def _densify18(path: list[Cell]) -> list[Cell]:
    """Inserts a cell wherever a 26-connected path steps across a corner, so every step shares at least an edge —
    the ruin pass's settle holds a member together through faces and edges, not corners."""
    out: list[Cell] = []
    for c in path:
        if out:
            p = out[-1]
            if all(p[i] != c[i] for i in range(3)):
                out.append((c[0], c[1], p[2]))
        out.append(c)
    return out


class Sculpture:
    """A statue as a union of simple solids, each with its own material, written as a hollow shell: add the parts
    (later parts paint over earlier ones where they overlap), cut away what should be open, then `write` it into a
    design with a wall thickness. Mirror a half with `mirror` for a symmetric figure."""

    def __init__(self) -> None:
        self.parts: list[tuple[set[Cell], object]] = []
        self.cuts: set[Cell] = set()

    def add(self, cells: Iterable[Cell], m) -> Sculpture:
        self.parts.append((set(cells), m))
        return self

    def ellipsoid(self, c, rx: float, ry: float, rz: float, m) -> Sculpture:
        return self.add(solids.ellipsoid(c[0], c[1], c[2], rx, ry, rz), m)

    def sphere(self, c, r: float, m) -> Sculpture:
        return self.add(solids.sphere(c[0], c[1], c[2], r), m)

    def capsule(self, p0, p1, r0: float, m, r1: float | None = None) -> Sculpture:
        return self.add(solids.capsule(p0, p1, r0, r1), m)

    def box(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m) -> Sculpture:
        return self.add(solids.box(x0, y0, z0, x1, y1, z1), m)

    def cylinder(self, cx: float, cz: float, y0: int, y1: int, r: float, m, rz: float | None = None) -> Sculpture:
        return self.add(solids.cylinder(cx, cz, y0, y1, r, rz), m)

    def cone(self, cx: float, cz: float, y0: int, y1: int, r0: float, m, r1: float = 0.5) -> Sculpture:
        return self.add(solids.cone(cx, cz, y0, y1, r0, r1), m)

    def cut(self, cells: Iterable[Cell]) -> Sculpture:
        self.cuts |= set(cells)
        return self

    def mirror(self, axis: str, at: float) -> Sculpture:
        """Adds a mirrored copy of every part so far (across `axis` = `at`)."""
        i = "xyz".index(axis)

        def flip(p: Cell) -> Cell:
            q = list(p)
            q[i] = round(2 * at - q[i])
            return (q[0], q[1], q[2])
        self.parts += [({flip(p) for p in cells}, m) for cells, m in list(self.parts)]
        self.cuts |= {flip(p) for p in self.cuts}
        return self

    def solid(self) -> dict[Cell, object]:
        out: dict[Cell, object] = {}
        for cells, m in self.parts:
            for p in cells:
                out[p] = m
        for p in self.cuts:
            out.pop(p, None)
        return out

    def write(self, d: Design, wall: int = 2, open_bottom: bool = False, where: str | None = None) -> set[Cell]:
        """Writes the sculpture into `d`: only its outer `wall` cells (0 = solid). Returns the cells written."""
        solid = self.solid()
        cells = solids.shell(solid, wall, open_bottom=open_bottom) if wall > 0 else set(solid)
        done = set()
        by_mat: dict[int, tuple[object, list[Cell]]] = {}
        for p in cells:
            m = solid[p]
            by_mat.setdefault(id(m), (m, []))[1].append(p)
        for m, ps in by_mat.values():
            done |= d.fill(ps, m, where)
        return done
