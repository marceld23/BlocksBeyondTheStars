"""The giant-ship toolkit (#3617): `ShipDesign` — a `geom.Design` that writes a GIANT SHIP template
(`data/giant_ships/<key>.json`, `giantShip: true`) — with the helpers a huge spaceship is built from: a lofted hull,
tubes along curves, decks, window bands, glass domes, greebles, corridors with rooms behind sliding doors, hull doors
with the `entrance` marker, ladder shafts kept through the ruin pass, turrets and engine nozzles. The pure geometry
(sections, the loft, `Hull`, tubes) lives in `shipgeom`. Ship scripts live in tools/giant_ships/<key>.py and are run by
tools/gen_giant_ships.py; tools/giant_ships/_demo_hull.py shows every helper.

Orientation: x = across the ship, y = up, z = along it; the BOW points to +z. Each helper returns the cells it wrote
(or a small record: `Hull`, `Tube`, `Corridor`, `Room`), so the result can be reused.

What the game reads (spec §3): markers `entrance` (at least one; the first is the boarding arrival in space; it stands on
a floor with two cells of headroom), `door_slide` with `axis=x|z` (the derelict decay reads the rooms from them), `chest`
(`table=…;hidden=1`) and `sound`. `keep` cells never decay — stairs, ladders, the way to every deck.
"""
from __future__ import annotations

import math
import zlib
from collections.abc import Iterable, Sequence
from pathlib import Path
from typing import NamedTuple

try:  # imported as the package `landmarks.ships`
    from . import shapecode as sc
    from . import shipgeom, solids, templatefile
    from .geom import (
        CAVITY,
        CLEAR,
        MAX_RUIN_ZONES,
        MAX_SIZE,
        Design,
        Mat,
        Mix,
        Palette,
        _dump,
        rgb,
    )
    from .shipgeom import (
        Hull,
        Section,
        Tube,
        ellipse,
        polygon,
        rounded_rect,
        ship_section,
        superellipse,
    )
except ImportError:  # imported from a script (the usual way: tools/landmarks on sys.path)
    import shapecode as sc
    import shipgeom
    import solids
    import templatefile
    from geom import (
        CAVITY,
        CLEAR,
        MAX_RUIN_ZONES,
        MAX_SIZE,
        Design,
        Mat,
        Mix,
        Palette,
        _dump,
        rgb,
    )
    from shipgeom import (
        Hull,
        Section,
        Tube,
        ellipse,
        polygon,
        rounded_rect,
        ship_section,
        superellipse,
    )

# A ship script imports everything it needs from here: `from ships import ShipDesign, Mat, Mix, ship_section, ...`.
__all__ = [
    "CAVITY", "CLEAR", "Corridor", "Hull", "Mat", "Mix", "Palette", "Room", "Section", "ShipDesign", "Tube", "ellipse",
    "polygon", "rgb", "rounded_rect", "sc", "ship_numbers", "ship_section", "shipgeom", "solids", "superellipse",
]

Cell = tuple[int, int, int]
SHIP_OUT = Path(__file__).resolve().parents[2] / "data" / "giant_ships"
GIANT_GENERATION = 42      # WorldDescription.GiantWrecksGeneration: shipped ships are rolled on new worlds only
STYLES = ("", "alien")     # GiantShipRules.StyleAlien: a crystal wreck (shards instead of scrap)
# Mirrors of the C# rules (GiantShipRules.Problems drops a template that breaks them at load):
SHIP_PROFILES = ("crash", "hull", "lattice")   # LandmarkRules.GiantShipProfiles: what a giant ship's ruin zone may name
SHIP_MARKERS = ("entrance", "door_slide", "chest", "sound", "loot", "data_terminal", "relic_cache")  # GiantShipRules.Markers
GLASS = ("glass", "glass_clear")
SHIP_HEADER_FIELDS = {
    "key", "name", "giantShip", "nameKey", "weight", "minTerrainGeneration", "groundRow", "style", "pack",
    "planetTypes", "pinOnly", "ruinStrength", "ruinZones", "tier", "kind",
}
DIRS = {"up": (0, 1, 0), "down": (0, -1, 0), "left": (-1, 0, 0), "right": (1, 0, 0), "front": (0, 0, 1),
        "back": (0, 0, -1)}
OPENINGS = {"+x": (1, 0), "-x": (-1, 0), "+z": (0, 1), "-z": (0, -1)}


class Corridor(NamedTuple):
    """A carved corridor: `axis` it runs along ('z' or 'x'), air across `c0..c1` (x for a z-corridor), walk row `y`,
    `height` rows of air, from `a0` to `a1` along the axis."""
    axis: str
    c0: int
    c1: int
    y: int
    height: int
    a0: int
    a1: int


class Room(NamedTuple):
    """A room off a corridor: its air box (x0..x1, y0..y1, z0..z1; y0 = the walk row, y0 - 1 the floor), the cells it
    really carved (clipped to the hull), the `door` marker cell, the wall `axis` of that door and its `side`."""
    x0: int
    y0: int
    z0: int
    x1: int
    y1: int
    z1: int
    cells: frozenset
    door: Cell | None
    axis: str
    side: str

    @property
    def floor(self) -> list[Cell]:
        """The walk-row cells of the room (sorted), where chests, sounds and furniture stand."""
        return sorted(c for c in self.cells if c[1] == self.y0)

    @property
    def centre(self) -> Cell:
        """The walk-row cell nearest the middle of the room."""
        mx, mz = (self.x0 + self.x1) / 2, (self.z0 + self.z1) / 2
        return min(self.floor, key=lambda c: (abs(c[0] - mx) + abs(c[2] - mz), c))

    def corner(self, k: int = 0) -> Cell:
        """A walk-row cell at one of the four corners: 0 = low x / low z, 1 = high x / low z, 2 = high x / high z,
        3 = low x / high z (the nearest cell the room really has there)."""
        corners = [(self.x0, self.z0), (self.x1, self.z0), (self.x1, self.z1), (self.x0, self.z1)]
        tx, tz = corners[k % 4]
        return min(self.floor, key=lambda c: (abs(c[0] - tx) + abs(c[2] - tz), c))


def _axis_shape(axis: str) -> int:
    return sc.post() if axis == "y" else sc.beam(axis)


def _shaped(m, shape: int):
    """A block key or Mat in a slim form; any other material (a Mix, a callable) is left as it is."""
    if isinstance(m, str):
        return Mat(m, shape=shape)
    return m.shaped(shape) if isinstance(m, Mat) else m


class ShipDesign(Design):
    """A giant ship under construction. The header is written as `key, name, giantShip, nameKey, weight,
    minTerrainGeneration, groundRow, style` plus anything passed in `header` (pack, planetTypes, pinOnly, ruinStrength,
    ruinZones ...). `groundRow` is the row that lies on the ground when the wreck crashes on a planet: the rows below it
    are buried (the keel sunk into the rock). `style='alien'` = a crystal wreck."""

    def __init__(self, key: str, name: str, weight: int = 1, groundRow: int = 0, style: str = "", **header) -> None:
        super().__init__(key, name)
        self.header = {"key": key, "name": name, "giantShip": True, "nameKey": f"giantship.{key}.name",
                       "weight": weight, "minTerrainGeneration": GIANT_GENERATION, "groundRow": groundRow,
                       "style": style}
        self.header.update(header)
        self.header.pop("landmark", None)  # a giant ship is never a landmark
        self.hulls: list[Hull] = []

    # --- volumes ----------------------------------------------------------------------------------------------------
    def add_hull(self, hull: Hull, m, wall: int = 2, skin=None) -> Hull:
        """Writes a hull: its shell `wall` thick in `m` (0 = solid), the outermost layer in `skin` when given (mix only
        the skin — a `Mix` breaks the file's runs). Registers it, so `finish` keeps the air inside it below the ground
        row, and records `hull.wall` for the helpers that need its inside."""
        hull.wall = wall
        self.fill(hull.shell(wall) if wall > 0 else hull.cells, m)
        if skin is not None:
            self.fill(hull.outer(), skin)
        self.hulls.append(hull)
        return hull

    def loft(self, stations: Sequence[tuple[float, Section]], m, wall: int = 2, skin=None, **kw) -> Hull:
        """A lofted hull (`shipgeom.loft`: stations, bow, stern, tip, curve, anchor, taper_axes, smooth, symmetric)
        written as a shell (`add_hull`). Returns the `Hull`."""
        return self.add_hull(shipgeom.loft(stations, **kw), m, wall, skin)

    def tube(self, points: Sequence, m, radius, hollow: int = 0, kind: str = "catmull", ends: str = "flat",
             open_ends: bool = True, carve: bool = True, where: str | None = None) -> Tube:
        """A tube along a curve (`shipgeom.tube`): gun barrels, tentacles, pipes, a walkable tunnel. Writes the wall in
        `m`; with `hollow` and `carve` the bore is cleared to air, also where it runs through other cells (a tunnel
        cuts its way into the hull). Returns the `Tube` (wall, bore, path)."""
        t = shipgeom.tube(points, radius, hollow, kind, ends, open_ends)
        self.fill(t.wall, m, where)
        if hollow and carve:
            self.fill(t.bore, CLEAR)
        return t

    # --- decks and windows ------------------------------------------------------------------------------------------
    def deck(self, hull: Hull, y: int, m, hatches: Iterable[tuple[int, int, int, int]] = (), x0: int | None = None,
             x1: int | None = None, z0: int | None = None, z1: int | None = None, wall: int | None = None) -> set[Cell]:
        """A floor plate on row y filling the hull's inside (for its wall, or `wall`), limited to x0..x1 / z0..z1 when
        given, with `hatches` (x0, z0, x1, z1) left open (stairwells, ladder holes). People walk on row y + 1."""
        holes = [(min(a, c), min(b, d), max(a, c), max(b, d)) for a, b, c, d in hatches]
        cells = [p for p in hull.row_cells(y, wall)
                 if (x0 is None or p[0] >= x0) and (x1 is None or p[0] <= x1)
                 and (z0 is None or p[2] >= z0) and (z1 is None or p[2] <= z1)
                 and not any(h[0] <= p[0] <= h[2] and h[1] <= p[2] <= h[3] for h in holes)]
        return self.fill(cells, m)

    def window_band(self, hull: Hull, y0: int, y1: int, m="glass", every: int = 4, width: int = 2, side: str = "both",
                    start: int | None = None, end: int | None = None, depth: int | None = None,
                    wall: int | None = None) -> set[Cell]:
        """Windows through the hull wall on rows y0..y1: along the hull, `width` cells of glass every `every` cells
        (width >= every = one continuous band), from `start` to `end` (z for the sides, x for the ends). `side`:
        'left' (-x), 'right' (+x), 'both', 'front' (the bow face, +z) or 'back' (-z). Each window pierces the wall
        horizontally where it is at most `depth` cells (default wall + 2) thick, so a sloping part gets none; only
        existing hull blocks are swapped."""
        w = hull.wall if wall is None else wall
        depth = depth if depth is not None else w + 2
        sides = {"both": ("left", "right")}.get(side, (side,))
        out: set[Cell] = set()
        ends: dict[tuple[int, int], list[int]] = {}
        if any(s in ("front", "back") for s in sides):
            for y in range(y0, y1 + 1):
                for x, _, z in hull.row_cells(y, 0):
                    e = ends.setdefault((x, y), [z, z])
                    e[0], e[1] = min(e[0], z), max(e[1], z)
        b = hull.bounds()
        for sd in sides:
            if sd not in ("left", "right", "front", "back"):
                raise ValueError(f"window_band: unknown side '{sd}' (left | right | both | front | back)")
            lo, hi = (b[2], b[5]) if sd in ("left", "right") else (b[0], b[3])
            a0 = lo if start is None else start
            a1 = hi if end is None else end
            for a in range(a0, a1 + 1):
                if (a - a0) % every >= width:
                    continue
                for y in range(y0, y1 + 1):
                    if sd in ("left", "right"):
                        r = hull.row(y, a)
                        if r is None:
                            continue
                        step = 1 if sd == "left" else -1
                        p = (r[0] if sd == "left" else r[1], y, a)
                    else:
                        e = ends.get((a, y))
                        if e is None:
                            continue
                        step = -1 if sd == "front" else 1
                        p = (a, y, e[1] if sd == "front" else e[0])
                    run = []
                    while hull.contains(p) and not hull.contains(p, w) and len(run) <= depth:
                        run.append(p)
                        p = (p[0] + step, y, p[2]) if sd in ("left", "right") else (p[0], y, p[2] + step)
                    if run and len(run) <= depth and hull.contains(p, w):
                        out |= self.fill(run, m, "blocks")
        return out

    def glass_dome(self, cx: float, cz: float, y0: int, r: float, m="glass", frame="iron_block", ribs: int = 8,
                   rings: int = 2, ry: float | None = None, rz: float | None = None, wall: int = 1,
                   clear_inside: bool = True) -> set[Cell]:
        """A glass dome standing on row y0 (its widest row): a shell `wall` thick, its base row, `ribs` meridian ribs
        and `rings` latitude rings in `frame`, the rest `m`. `clear_inside` clears everything inside it from row y0 up
        — the hull's roof under it too, so the dome opens onto the deck below."""
        ry = r if ry is None else ry
        rz = r if rz is None else rz
        shell = solids.dome(cx, cz, y0, r, ry, rz, wall)
        if clear_inside:
            self.fill(solids.dome(cx, cz, y0, r, ry, rz) - shell, CLEAR)
        ring_rows = {y0 + round(k * ry / (rings + 1)) for k in range(1, rings + 1)}
        frame_cells = set()
        for x, y, z in shell:
            if y == y0 or y in ring_rows:
                frame_cells.add((x, y, z))
                continue
            rho = math.hypot(x - cx, (z - cz) * r / rz if rz else 0)
            ang = math.atan2(z - cz, x - cx) % (2 * math.pi)
            step = 2 * math.pi / max(1, ribs)
            off = abs((ang + step / 2) % step - step / 2)
            if ribs and off * rho <= 0.5:
                frame_cells.add((x, y, z))
        out = self.fill(shell - frame_cells, m)
        return out | self.fill(frame_cells, frame)

    # --- greebles ---------------------------------------------------------------------------------------------------
    def greebles(self, hull: Hull, seed: int, m="iron_block", density: float = 0.02,
                 faces: Sequence[str] = ("up", "left", "right"), kinds: Sequence[str] = ("box", "antenna", "pipe"),
                 region: tuple[int, int, int, int, int, int] | None = None, avoid: Iterable[Cell] = (),
                 light="light_red") -> set[Cell]:
        """Small details on the hull's outer surface, placed by a hash of `seed` (the same seed = the same greebles):
        `box` (1..3-cell boxes, vents, hatches), `antenna` (on up faces: a mast of posts with a `light` tip; None =
        no tip) and
        `pipe` (a run of beams along the hull). On the `faces` given ('up', 'down', 'left', 'right', 'front', 'back'),
        on about `density` of their surface cells, inside `region` (x0, y0, z0, x1, y1, z1) when given. A greeble stands
        only on hull blocks of the design that are not glass (never on a window, a dome or in a door you cut); one that
        would reach into the hull, another cell of the design or an `avoid` cell (a flight deck) is skipped."""
        avoid = set(avoid)
        support = {c for c in hull.outer() if (v := self.plain.get(c)) is not None and v[0] == "block"
                   and v[1] not in GLASS}
        out: set[Cell] = set()
        for p in sorted(support):
            if region and not (region[0] <= p[0] <= region[3] and region[1] <= p[1] <= region[4]
                               and region[2] <= p[2] <= region[5]):
                continue
            for fi, face in enumerate(faces):
                d = DIRS[face]
                q = (p[0] + d[0], p[1] + d[1], p[2] + d[2])
                if hull.contains(q) or solids.hash01(p[0], p[1], p[2], seed * 7 + fi) >= density:
                    continue
                kind = kinds[int(solids.hash01(p[0], p[1], p[2], seed * 7 + 31) * len(kinds)) % len(kinds)]
                if kind == "antenna" and face != "up":
                    kind = "box"  # masts stand up; sideways they would only bristle
                parts = self._greeble(kind, q, d, support, seed, p, m, light)
                if not parts or any(hull.contains(c) or c in avoid or c in self.plain or c in out for c, _ in parts):
                    continue
                for c, mat in parts:
                    self.put(c[0], c[1], c[2], mat)
                    out.add(c)
        return out

    @staticmethod
    def _greeble(kind: str, q: Cell, d: Cell, support: set, seed: int, p: Cell, m, light) -> list[tuple[Cell, object]]:
        """The cells of one greeble rooted on surface cell p, growing from q = p + d outward; [] when it would not rest
        on `support` everywhere."""
        def h(k: int) -> float:
            return solids.hash01(p[0], p[1], p[2], seed * 13 + k)
        along = (0, 0, 1) if d[2] == 0 else (1, 0, 0)
        across = tuple(1 - abs(d[i]) - abs(along[i]) for i in range(3))

        def at(i: int, j: int, k: int) -> Cell:
            return tuple(q[c] + i * across[c] + j * d[c] + k * along[c] for c in range(3))  # type: ignore[return-value]
        if kind == "box":
            a, b, c = 1 + int(h(1) * 3), 1 + int(h(2) * 2), 1 + int(h(3) * 4)
            base = [at(i, 0, k) for i in range(a) for k in range(c)]
            if any(tuple(x - d[n] for n, x in enumerate(cell)) not in support for cell in base):
                return []
            return [(at(i, j, k), m) for i in range(a) for j in range(b) for k in range(c)]
        if kind == "antenna":
            n = 3 + int(h(4) * 6)
            axis = "y" if d[1] else ("x" if d[0] else "z")
            cells = [(at(0, j, 0), _shaped(m, _axis_shape(axis))) for j in range(n - 1)]
            tip = light if light is not None else m
            return cells + [(at(0, n - 1, 0), tip)]
        if kind == "pipe":
            n = 4 + int(h(5) * 9)
            axis = "z" if along[2] else "x"
            cells = []
            for k in range(n):
                c = at(0, 0, k)
                if tuple(x - d[i] for i, x in enumerate(c)) not in support:
                    break
                cells.append(c)
            if len(cells) < 3:
                return []
            shaped = _shaped(m, _axis_shape(axis))
            return [(c, m if i in (0, len(cells) - 1) else shaped) for i, c in enumerate(cells)]
        raise ValueError(f"unknown greeble '{kind}' (box | antenna | pipe)")

    # --- corridors, rooms, doors ------------------------------------------------------------------------------------
    @staticmethod
    def _pt(axis: str, across: int, y: int, along: int) -> Cell:
        return (across, y, along) if axis == "z" else (along, y, across)

    def corridor(self, hull: Hull, y: int, a0: int, a1: int, axis: str = "z", centre: float | None = None,
                 width: int = 3, height: int = 3, floor=None, walls=None, ceiling=None, light=None,
                 light_every: int = 0, wall: int | None = None) -> Corridor:
        """Carves a corridor inside the hull along `axis` ('z' = fore and aft, 'x' = athwart) from a0 to a1, `width`
        wide around `centre` (default the hull's centre line), `height` rows of air from the walk row y. Optional
        `floor` (row y - 1), side `walls` and `ceiling` (row y + height), all clipped to the hull's inside so the hull
        itself is never cut; a `light` in the ceiling every `light_every` cells. The ends stay open."""
        if axis not in ("x", "z"):
            raise ValueError("corridor: axis is 'x' or 'z'")
        if centre is None:
            b = hull.bounds()
            if axis == "z":
                centre = hull.axis_x if hull.axis_x is not None else (b[0] + b[3]) / 2
            else:
                centre = (b[2] + b[5]) / 2
        c0 = math.floor(centre - (width - 1) / 2 + 0.5)
        c1 = c0 + width - 1
        a0, a1 = min(a0, a1), max(a0, a1)

        def box(u0: int, u1: int, v0: int, v1: int) -> list[Cell]:
            return [p for u in range(u0, u1 + 1) for yy in range(v0, v1 + 1) for a in range(a0, a1 + 1)
                    if hull.contains(p := self._pt(axis, u, yy, a), wall)]
        if walls is not None:
            self.fill(box(c0 - 1, c0 - 1, y, y + height - 1) + box(c1 + 1, c1 + 1, y, y + height - 1), walls)
        if floor is not None:
            self.fill(box(c0 - 1 if walls is not None else c0, c1 + 1 if walls is not None else c1, y - 1, y - 1), floor)
        if ceiling is not None:
            self.fill(box(c0 - 1 if walls is not None else c0, c1 + 1 if walls is not None else c1,
                          y + height, y + height), ceiling)
        self.fill(box(c0, c1, y, y + height - 1), CLEAR)
        if light is not None and light_every:
            mid = (c0 + c1) // 2
            for a in range(a0 + light_every // 2, a1 + 1, light_every):
                p = self._pt(axis, mid, y + height, a)
                if self.is_block(*p):
                    self.put(p[0], p[1], p[2], light)
        return Corridor(axis, c0, c1, y, height, a0, a1)

    def slide_door(self, x: int, y: int, z: int, axis: str, width: int = 2, height: int = 3) -> set[Cell]:
        """A doorway with a `door_slide` marker in its bottom cell. `axis` is the WALL's axis as the derelict decay
        reads it: 'x' = the wall is a plane of constant x (you walk through it along x) and the opening runs from z to
        z + width - 1; 'z' = a plane of constant z, the opening from x to x + width - 1. The game's doorway is 2 wide
        and 3 high (the marker cell and its +z / +x neighbour), so keep the defaults for a door the decay knows."""
        if axis not in ("x", "z"):
            raise ValueError("slide_door: axis is 'x' or 'z' (the wall's axis)")
        cells = {(x, y + j, z + i) if axis == "x" else (x + i, y + j, z) for i in range(width) for j in range(height)}
        self.fill(cells, CLEAR)
        self._free(x, y, z)
        self.marker(x, y, z, "door_slide", f"axis={axis}")
        return cells

    def rooms(self, hull: Hull, cor: Corridor, per_side: int = 3, depth: int = 7, sides: str = "both", walls="steel_wall",
              floor=None, ceiling=None, a0: int | None = None, a1: int | None = None, height: int | None = None,
              doors: bool = True, wall: int | None = None) -> list[Room]:
        """A row of rooms off a corridor: `per_side` rooms on each of `sides` ('left' = low across, 'right' = high
        across, 'both'), `depth` cells deep, sharing one-cell `walls`, between a0 and a1 along the corridor (default
        its whole length), clipped to the hull's inside. Each room gets a 2 x 3 doorway in the corridor wall with a
        `door_slide` marker (axis = the corridor wall's axis). A room the hull leaves too small (under 4 floor cells)
        is skipped. Returns the `Room`s (place chests with `room.corner(2)`, sounds at `room.centre`)."""
        a0 = cor.a0 if a0 is None else a0
        a1 = cor.a1 if a1 is None else a1
        h = cor.height if height is None else height
        length = (a1 - a0 + 1 - (per_side + 1)) // per_side
        if length < 3:
            raise ValueError(f"rooms: {per_side} rooms do not fit between {a0} and {a1}")
        door_axis = "x" if cor.axis == "z" else "z"
        out: list[Room] = []
        for side in (("left", "right") if sides == "both" else (sides,)):
            sgn = 1 if side == "right" else -1
            cw = cor.c1 + 1 if side == "right" else cor.c0 - 1        # the corridor wall's across coordinate
            u_near, u_far = cw + sgn, cw + sgn * depth                # the room's air, across
            u0, u1 = min(u_near, u_far), max(u_near, u_far)
            for k in range(per_side):
                s0 = a0 + 1 + k * (length + 1)
                s1 = s0 + length - 1
                air = {p for u in range(u0, u1 + 1) for yy in range(cor.y, cor.y + h) for a in range(s0, s1 + 1)
                       if hull.contains(p := self._pt(cor.axis, u, yy, a), wall)}
                if sum(1 for p in air if p[1] == cor.y) < 4:
                    continue
                # The ring of walls around the room: the corridor wall (cw), the walls to the neighbours, the far wall.
                shell = {p for u in range(u0 - 1, u1 + 2) for yy in range(cor.y, cor.y + h)
                         for a in range(s0 - 1, s1 + 2) if hull.contains(p := self._pt(cor.axis, u, yy, a), wall)} - air
                self.fill(shell, walls)
                if floor is not None:
                    self.fill({(p[0], p[1] - 1, p[2]) for p in air if p[1] == cor.y}, floor)
                if ceiling is not None:
                    self.fill({(p[0], p[1] + 1, p[2]) for p in air if p[1] == cor.y + h - 1}, ceiling)
                self.fill(air, CLEAR)
                door = None
                if doors:
                    d0 = s0 + (length - 2) // 2
                    door = self._pt(cor.axis, cw, cor.y, d0)
                    self.slide_door(door[0], door[1], door[2], door_axis)
                xs, zs = [p[0] for p in air], [p[2] for p in air]
                out.append(Room(min(xs), cor.y, min(zs), max(xs), cor.y + h - 1, max(zs), frozenset(air), door,
                                door_axis, side))
        return out

    # --- the way in and the way up ----------------------------------------------------------------------------------
    def entrance(self, x: int, y: int, z: int, clear: bool = True) -> Cell:
        """The `entrance` marker: in space the boarding arrival (the first one written), on a planet an open way in. It
        must stand on a floor (row y - 1 a block) with two cells of headroom; `clear` empties the cell and the one
        above it."""
        if clear:
            self.fill([(x, y, z), (x, y + 1, z)], CLEAR)
        self._free(x, y, z)
        self.marker(x, y, z, "entrance")
        return (x, y, z)

    def hull_door(self, hull: Hull, side: str, y: int, at: int, width: int = 2, height: int = 3, entrance: bool = True,
                  frame=None, wall: int | None = None, depth: int | None = None) -> Cell:
        """Cuts a doorway `width` x `height` through the hull wall on `side` ('left', 'right', 'front', 'back'), its sill
        on row y (the deck's walk row), starting at `at` along the hull (z for the sides, x for the ends), and puts the
        `entrance` marker on the first inside cell behind it. `frame` = a material for a one-cell frame around the
        opening's outer face. Returns the marker cell (also when `entrance` is False)."""
        depth = depth if depth is not None else (hull.wall if wall is None else wall) + 6
        sx, sz = {"left": (1, 0), "right": (-1, 0), "front": (0, -1), "back": (0, 1)}[side]
        mark = None
        for i in range(width):
            for j in range(height):
                yy = y + j
                if side in ("left", "right"):
                    r = hull.row(yy, at + i)
                    if r is None:
                        raise ValueError(f"hull_door: no hull at row {yy}, z {at + i}")
                    p = (r[0] if side == "left" else r[1], yy, at + i)
                else:
                    e = hull.z_extent(at + i, yy)
                    if e is None:
                        raise ValueError(f"hull_door: no hull at x {at + i}, row {yy}")
                    p = (at + i, yy, e[1] if side == "front" else e[0])
                run = []
                while hull.contains(p) and not hull.contains(p, wall) and len(run) <= depth:
                    run.append(p)
                    p = (p[0] + sx, yy, p[2] + sz)
                if not hull.contains(p, wall):
                    raise ValueError(f"hull_door: the wall at {run[0] if run else p} is thicker than {depth} cells")
                self.fill(run, CLEAR)
                if j == 0 and i == 0:
                    mark = p
        if frame is not None:
            self._door_frame(hull, side, y, at, width, height, frame)
        assert mark is not None
        if entrance:
            self.entrance(*mark, clear=False)
        return mark

    def _door_frame(self, hull: Hull, side: str, y: int, at: int, width: int, height: int, frame) -> None:
        """A one-cell frame on the outer face around a hull door (the hull blocks there are restyled)."""
        ring = [(i, j) for i in range(-1, width + 1) for j in range(height + 1) if i in (-1, width) or j == height]
        for i, j in ring:
            if side in ("left", "right"):
                r = hull.row(y + j, at + i)
                p = None if r is None else (r[0] if side == "left" else r[1], y + j, at + i)
            else:
                e = hull.z_extent(at + i, y + j)
                p = None if e is None else (at + i, y + j, e[1] if side == "front" else e[0])
            if p is not None and self.is_block(*p):
                self.put(p[0], p[1], p[2], frame)

    def ladder(self, x: int, z: int, y0: int, y1: int, m="ladder", keep: bool = True) -> set[Cell]:
        """A ladder column from row y0 to y1 (kept by default). A ladder hangs on a wall beside it — put one there."""
        return self.fill([(x, y, z) for y in range(y0, y1 + 1)], Mat(m) if isinstance(m, str) else m, keep=keep)

    def lift_shaft(self, x: int, z: int, decks: Sequence[int], opening: str = "-z", width: int = 2,
                   walls="steel_wall", ladder="ladder", landing=None, keep: bool = True) -> set[Cell]:
        """A ladder shaft joining decks: `width` ladder cells side by side at (x, z) (along x for an opening to ±z, along
        z for ±x), from the lowest deck's walk row to one above the highest, walled in on three sides and closed below
        and above; on the `opening` side ('+x', '-x', '+z', '-z') a 3-high doorway onto every deck in `decks` (walk
        rows) with a two-cell landing in front of it. Every cell of it — walls, ladder, the landings' floor — is `keep`,
        so the ruin pass never cuts a deck off. Deck plates across the shaft become ladder."""
        ox, oz = OPENINGS[opening]
        ux, uz = (1, 0) if ox == 0 else (0, 1)          # along the row of ladder cells
        decks = sorted(decks)
        ytop = decks[-1] + 1
        lad = [(x + ux * i, z + uz * i) for i in range(width)]
        out = set()
        wall_m = Mat(walls) if isinstance(walls, str) else walls
        land_m = wall_m if landing is None else (Mat(landing) if isinstance(landing, str) else landing)
        back = [(lx - ox, lz - oz) for lx, lz in lad]
        ends = [(x - ux - ox * k, z - uz - oz * k) for k in (0, 1)] + \
               [(x + ux * width - ox * k, z + uz * width - oz * k) for k in (0, 1)]
        front = [(lx + ox, lz + oz) for lx, lz in lad]
        for y in range(decks[0] - 1, ytop + 3):
            for cx, cz in back + ends:
                out |= self.fill([(cx, y, cz)], wall_m, keep=keep)
            for cx, cz in lad:
                if y == decks[0] - 1 or y > ytop:
                    out |= self.fill([(cx, y, cz)], wall_m, keep=keep)       # the floor and the cap
                else:
                    out |= self.fill([(cx, y, cz)], Mat(ladder) if isinstance(ladder, str) else ladder, keep=keep)
            for cx, cz in front:
                at_deck = any(d <= y <= d + 2 for d in decks)
                if at_deck:
                    self.put(cx, y, cz, CLEAR)
                elif y > decks[0] - 1:
                    out |= self.fill([(cx, y, cz)], wall_m, keep=keep)
        for d in decks:  # the landings: the floor in front of every doorway, two cells out
            for k in (1, 2):
                cells = [(lx + ox * k, d - 1, lz + oz * k) for lx, lz in lad]
                out |= self.fill(cells, land_m, keep=keep)
        return out

    # --- armament and engines ---------------------------------------------------------------------------------------
    def turret(self, cx: int, y: int, cz: int, r: float = 4, barrels: int = 2, length: int = 16, barrel_r: float = 1.0,
               facing: str = "+z", m="iron_block", barrel_m=None, spacing: int = 3, rise: int = 1) -> set[Cell]:
        """A gun turret standing on row y: a two-row ring base of radius r, a flattened dome on it and `barrels` tubes
        `length` long pointing `facing` ('+z', '-z', '+x', '-x'), `spacing` apart, `rise` rows above the dome's base.
        Barrels of radius >= 1.5 are hollow (a muzzle you can look into)."""
        out = self.cylinder(cx, cz, y, y + 1, r, m)
        out |= self.dome(cx, cz, y + 2, r, m, ry=max(2.0, r * 0.6))
        fx, fz = OPENINGS[facing]
        px, pz = (1, 0) if fx == 0 else (0, 1)
        by = y + 2 + rise
        for k in range(barrels):
            off = (k - (barrels - 1) / 2) * spacing
            p0 = (cx + px * off + fx * (r - 1), by, cz + pz * off + fz * (r - 1))
            p1 = (p0[0] + fx * length, by, p0[2] + fz * length)
            t = self.tube([p0, p1], barrel_m or m, barrel_r, hollow=1 if barrel_r >= 1.5 else 0, kind="poly")
            out |= t.wall
        return out

    def nozzle(self, cx: float, cy: float, z: int, r: float, length: int, m="engine_nozzle", glow=None,
               facing: str = "-z") -> set[Cell]:
        """An engine bell opening backwards (`facing` '-z', the stern) from z: a hollow cone, radius r * 0.6 at z to r at
        the mouth, one cell thick, with a disc of `glow` (a glowing Mat, e.g. Mat('light_white', glow=0x66ccff)) at its
        throat."""
        dz = -1 if facing == "-z" else 1
        # carve=False: the bell may start in the hull's stern cap, which must stay closed.
        t = self.tube([(cx, cy, z), (cx, cy, z + dz * length)], m, (r * 0.6, r), hollow=1, kind="poly", carve=False)
        out = set(t.wall)
        if glow is not None:
            out |= self.fill({(x, y, zz) for x, y, zz in t.bore if zz == z + dz}, glow)
        return out

    # --- finishing, checks, output ----------------------------------------------------------------------------------
    def translate(self, dx: int, dy: int = 0, dz: int = 0) -> None:
        """Moves everything — cells, markers, ruin zones, the ground row and the registered hulls."""
        super().translate(dx, dy, dz)
        self.hulls = [h.moved(dx, dy, dz) for h in self.hulls]

    def finish(self) -> int:
        """Before writing (gen_giant_ships.py calls it): every EMPTY cell inside a registered hull below the ground row
        becomes a cavity — below the ground row an empty cell keeps the planet's rock, and the buried decks must stay
        hollow. Returns how many cells it carved."""
        g = int(self.header.get("groundRow", 0) or 0)
        n = 0
        for hull in self.hulls:
            for p in hull.below(g):
                if p not in self.plain:
                    self.plain[p] = ("cavity", "", 0, 0, 0, False)
                    n += 1
        return n

    def problems(self) -> tuple[list[str], list[str]]:
        """(errors, warnings) of a giant ship (spec §3): size, header, ruin zones, blocks, markers, the entrance."""
        errors: list[str] = []
        warnings: list[str] = []
        h = self.header
        b = self.bounds()
        if b is None:
            return ["the design is empty"], warnings
        if min(b[0], b[1], b[2]) < 0:
            errors.append(f"cells below 0 (min {b[0]},{b[1]},{b[2]}); call normalize()")
        w, ht, ln = b[3] + 1, b[4] + 1, b[5] + 1
        if max(w, ht, ln) > MAX_SIZE:
            errors.append(f"{w}x{ht}x{ln} is larger than {MAX_SIZE} on an axis")
        for k in h:
            if k not in SHIP_HEADER_FIELDS:
                warnings.append(f"unknown header field '{k}' (passed through)")
        if h.get("giantShip") is not True:
            errors.append("giantShip must be true")
        if str(h.get("style", "")).lower() not in STYLES:
            errors.append(f"style '{h.get('style')}' is not empty or 'alien'")
        if not 0 <= float(h.get("ruinStrength", 0) or 0) <= 1:
            errors.append("ruinStrength must lie between 0 and 1")
        if not 0 <= int(h.get("groundRow", 0) or 0) < ht:
            errors.append("groundRow lies outside the structure")
        if int(h.get("weight", 1) or 0) < 1:
            errors.append("weight must be at least 1")
        errors += self._zone_problems(w, ht, ln)
        errors += self._block_problems()
        e2, w2 = self._marker_problems()
        return errors + e2, warnings + w2

    def _zone_problems(self, w: int, ht: int, ln: int) -> list[str]:
        errors = []
        zones = self.header.get("ruinZones", []) or []
        if len(zones) > MAX_RUIN_ZONES:
            errors.append(f"more than {MAX_RUIN_ZONES} ruin zones")
        for i, zone in enumerate(zones, 1):
            if str(zone.get("profile", "")).lower() not in ("",) + SHIP_PROFILES:
                errors.append(f"ruin zone {i}: unknown profile '{zone.get('profile')}' ({' | '.join(SHIP_PROFILES)})")
            if not 0 <= float(zone.get("strength", 0)) <= 1:
                errors.append(f"ruin zone {i}: strength must lie between 0 and 1")
            lo = [min(zone[a + "0"], zone[a + "1"]) for a in "xyz"]
            hi = [max(zone[a + "0"], zone[a + "1"]) for a in "xyz"]
            if min(lo) < 0 or hi[0] >= w or hi[1] >= ht or hi[2] >= ln:
                errors.append(f"ruin zone {i} reaches outside the structure ({w}x{ht}x{ln})")
        return errors

    def _block_problems(self) -> list[str]:
        errors = []
        used = {v[1] for v in self.plain.values() if v[0] == "block"}
        for tok in sorted(used & set(templatefile.MATERIAL_TOKENS)):
            errors.append(f"material token '{tok}' (a giant ship names concrete blocks)")
        try:
            known = templatefile.block_keys()
        except (OSError, ValueError):
            known = set()
        if known:
            for blk in sorted(used - known - set(templatefile.MATERIAL_TOKENS)):
                errors.append(f"unknown block '{blk}' (no new blocks: use an existing one)")
        return errors

    def _marker_problems(self) -> tuple[list[str], list[str]]:
        errors: list[str] = []
        warnings: list[str] = []
        cells = [p for p, v in self.plain.items() if v[0] == "block"] or list(self.plain)
        lo = [min(p[i] for p in cells) for i in range(3)]
        hi = [max(p[i] for p in cells) for i in range(3)]
        tables = templatefile.loot_tables()
        seen: dict[Cell, str] = {}
        entrances = 0
        for m in self.markers:
            pos = (m["x"], m["y"], m["z"])
            mid = m["id"]
            if any(not lo[i] <= pos[i] <= hi[i] for i in range(3)):
                errors.append(f"marker '{mid}' at {pos} lies outside the ship ({tuple(lo)}..{tuple(hi)})")
            if pos in seen:
                warnings.append(f"markers '{seen[pos]}' and '{mid}' share the cell {pos}")
            seen[pos] = mid
            if mid not in SHIP_MARKERS:
                errors.append(f"marker '{mid}' at {pos} is not a giant ship marker ({' | '.join(SHIP_MARKERS)})")
            if self.is_block(*pos):
                warnings.append(f"marker '{mid}' at {pos} sits in a block cell")
            cfg = dict(part.split("=", 1) for part in m.get("config", "").split(";") if "=" in part)
            if cfg.get("table") and tables and cfg["table"] not in tables:
                warnings.append(f"chest at {pos}: unknown loot table '{cfg['table']}'")
            if mid == "entrance":
                entrances += 1
                x, y, z = pos
                if not _solid(self.get(x, y - 1, z)) or _solid(self.get(x, y, z)) or _solid(self.get(x, y + 1, z)):
                    errors.append(f"entrance at {pos} needs a solid floor below and two cells of headroom")
            if mid == "door_slide":
                errors += self._door_problems(pos, cfg, warnings)
        if entrances == 0:
            errors.append("no 'entrance' marker (at least one: the way in)")
        return errors, warnings

    def _door_problems(self, pos: Cell, cfg: dict, warnings: list[str]) -> list[str]:
        axis = cfg.get("axis")
        if axis not in ("x", "z"):
            return [f"door_slide at {pos} needs axis=x or axis=z (the wall's axis)"]
        x, y, z = pos
        other = (x, y, z + 1) if axis == "x" else (x + 1, y, z)
        opening = [(c[0], c[1] + j, c[2]) for c in (pos, other) for j in range(3)]
        if any(self.is_block(*c) for c in opening):
            warnings.append(f"door_slide at {pos}: the 2 x 3 doorway is not clear")
        sides = [(x - 1, y, z), (x + 1, y, z)] if axis == "x" else [(x, y, z - 1), (x, y, z + 1)]
        if any(self.is_block(*c) for c in sides):
            warnings.append(f"door_slide at {pos}: a block stands right in front of or behind the doorway")
        return []

    def write(self, out: Path | str | None = None, check: bool = True) -> Path:
        """Writes `<out>/<key>.json` (default data/giant_ships — builders pass their artifacts folder)."""
        return super().write(SHIP_OUT if out is None else out, check)


def _solid(look: tuple | None) -> bool:
    """Whether a stored cell is a solid block (a floor, a wall) — a ladder or a light (`solid: false`) is not."""
    if look is None or look[0] != "block":
        return False
    try:
        flags = {b["key"]: b.get("solid", True) for b in templatefile.blocks()}
    except (OSError, ValueError):
        return True
    return bool(flags.get(look[1], True))


# --- numbers for the generator ---------------------------------------------------------------------------------------
def ship_numbers(t: dict) -> dict:
    """Cell count, bounding box of the blocks, glass share, the compact JSON size and a deflate estimate (the size on
    the wire is about that)."""
    cells, markers = templatefile.decode(t)
    blocks = [p for p, v in cells.items() if v[0] == "block"]
    glass = sum(1 for v in cells.values() if v[0] == "block" and v[1] in GLASS)
    text = _dump(t).encode("utf-8")
    box = solids.bounds(blocks) if blocks else None
    return {"cells": len(cells), "blocks": len(blocks), "markers": len(markers), "bbox": box,
            "glass": glass / len(blocks) if blocks else 0.0, "bytes": len(text), "deflated": len(zlib.compress(text, 6))}
