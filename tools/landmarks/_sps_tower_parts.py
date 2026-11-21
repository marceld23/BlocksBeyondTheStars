"""The shared parts of the SPS GUIDE TOWER ruins (#3893, docs/developer/SPS_TOWERS.md).

A guide tower is a square lattice mast of blue-grey Service steel, 90 rows to its top deck and 98 to the tip of its antenna:
four corner chords that taper from a 21-wide foot to a 7-wide head, a ring beam every six rows, crossed braces on every
face, six decks, and a kept spine with a ladder up the middle. At its foot stands the relay room (one console, chests, two
emergency lamps); beside the foot, on some towers, the guard post — a wooden hut with the capsule tank.

No tower stands whole anywhere, so the three scripts (`sps_tower_stump.py`, `sps_tower_leaning.py`, `sps_tower_broken.py`)
build the same mast broken in three ways. Everything is drawn through a mapping `T(lx, y, lz) -> cell` from tower
coordinates (lx, lz relative to the centre line, y the row) to design cells, so a piece of the mast can stand, lean, lie on
the ground or hang folded over; members are lines, which stay edge-connected under every mapping (the ruin pass's settle
holds things together through faces and edges).

A `TowerDesign` writes itself to data/sps_towers/<key>.json. The tower's own markers:

- `sps_console` — the relay console (the server places the block after the ruin pass);
- `sps_lamp` — an emergency lamp (`of=tank` = one of the capsule tank's two); it needs a block under it and air above;
- `sps_tank` — the capsule tank, two cells high;
- `sps_guard_post` — the low corner of the guard post's box, `w` / `h` / `l` its size: a tower without a guard post
  loses everything in that box.
"""
from __future__ import annotations

import itertools
import math
from pathlib import Path

import solids
from geom import Design, Mat, settings

OUT = Path(__file__).resolve().parents[2] / "data" / "sps_towers"

# The Service's blue-grey (the giant ships' palette): gunmetal chords, blue ring beams, lighter braces.
GUNMETAL, PLATE, LIGHT, SPS_BLUE, PALE = 0x3C4652, 0x48515A, 0x5A636D, 0x3E6A8E, 0x7C848D
CHORD = Mat("iron_block", tint=GUNMETAL)
RING = Mat("iron_block", tint=SPS_BLUE)
BRACE = Mat("iron_block", tint=LIGHT)
DECK = Mat("steel_floor")
SPINE = Mat("iron_block", tint=PLATE, keep=True)
LADDER = Mat("ladder", keep=True)
WALL = Mat("steel_wall", tint=0x4A6A8A)
SLAB = Mat("concrete", tint=0xB8BEC6)
PANEL = Mat("lab_panel")
BRACKET = Mat("iron_block", tint=PALE, keep=True)
GLASS = Mat("glass", tint=0x41606E)

CX = CZ = 11                 # the mast's centre line
GROUND = 1                   # the ground row: row 0 is the foundation slab, sunk into the ground
TOP = 90                     # the top deck
TIP = 98                     # the antenna's tip
DECKS = (6, 24, 42, 60, 78, 90)
LEVELS = tuple(range(6, TOP + 1, 6))
FOOT, HEAD = 10.0, 3.0       # the half-width at the ground row and at the top deck
THICK_TO = 42                # the chords are angle irons (three cells a row) up to here
POST = (2, 0, 26)            # the guard post's low corner
POST_SIZE = (7, 6, 7)
TOWER_MARKERS = ("sps_console", "sps_lamp", "sps_tank", "sps_guard_post")


def half(y: float) -> float:
    """The mast's half-width on row y."""
    return FOOT - (FOOT - HEAD) * (min(y, TOP) - GROUND) / (TOP - GROUND)


def upright(lean=None):
    """The mapping of a standing mast; `lean(y)` shifts row y along +x."""
    def t(lx: float, y: float, lz: float) -> tuple[int, int, int]:
        return (CX + round(lx) + (lean(round(y)) if lean else 0), round(y), CZ + round(lz))
    return t


def lying(y0: int, x0: int, sunk: int = 2):
    """The mapping of a mast piece lying on the ground along +x: row y0 of the tower at x0, its width upright, `sunk` rows
    of it in the ground."""
    h0 = half(y0)

    def t(lx: float, y: float, lz: float) -> tuple[int, int, int]:
        return (x0 + round(y - y0), GROUND - sunk + round(lx + h0), CZ + round(lz))
    return t


def folded(y0: int, length: int):
    """The mapping of the mast above row y0 folded over its +x edge: it hangs down toward +x and its tip, `length` rows
    along, rests on the ground row."""
    h0 = half(y0)
    a = math.asin(min(1.0, (y0 - GROUND) / length))
    ux, uy = math.cos(a), -math.sin(a)       # the mast's axis
    wx, wy = -math.sin(a), -math.cos(a)      # its +x side

    def t(lx: float, y: float, lz: float) -> tuple[int, int, int]:
        u, w = y - y0, lx - h0
        return (round(CX + h0 + u * ux + w * wx), round(y0 + u * uy + w * wy), CZ + round(lz))
    return t


class TowerDesign(Design):
    """A guide tower ruin: a `Design` with the tower header, the tower's markers and its own checks."""

    def __init__(self, key: str, name: str, **header) -> None:
        super().__init__(key, name)
        self.header = {"key": key, "name": name, "spsTower": True, "nameKey": f"landmark.{key}.name", "weight": 1,
                       "minTerrainGeneration": 47, "ruin": "always", "ruinProfile": "lattice", "ruinStrength": 0.3,
                       "protection": "protected", "requires": ["atmosphere", "inhabited"], "groundRow": GROUND, **header}

    # --- the markers ---------------------------------------------------------------------------------------------
    def console(self, x: int, y: int, z: int) -> None:
        self._free(x, y, z)
        self.marker(x, y, z, "sps_console")

    def lamp(self, x: int, y: int, z: int, tank: bool = False) -> None:
        """An emergency lamp. The cell below must hold a block and the cell above must be free (the ruin pass seats a
        marker on what is left and clears the cell above it)."""
        self._free(x, y, z)
        self._free(x, y + 1, z)
        self.marker(x, y, z, "sps_lamp", settings(of="tank" if tank else None))

    def lamp_on_top(self, x: int, z: int, tank: bool = False) -> bool:
        """A lamp on the highest block of column (x, z); False when the column is empty."""
        ys = [y for (cx, y, cz), v in self.plain.items() if cx == x and cz == z and v[0] == "block"]
        if not ys:
            return False
        self.keep([(x, max(ys), z)])
        self.lamp(x, max(ys) + 1, z, tank)
        return True

    def tank(self, x: int, y: int, z: int) -> None:
        self._free(x, y, z)
        self._free(x, y + 1, z)
        self.marker(x, y, z, "sps_tank")

    # --- the mast --------------------------------------------------------------------------------------------------
    def mast(self, t, y0: int, y1: int, plates: bool = True, spine_to: int | None = None, keep_chords: bool = False,
             faces=range(4)) -> None:
        """The lattice between rows y0 and y1 through the mapping `t`: chords, ring beams, crossed braces, the decks
        (`plates`), and the kept spine with its ladder up to row `spine_to` (None = no spine: a fallen piece)."""
        def corners(y: int) -> list[tuple[float, float]]:
            h = half(y)
            return [(-h, -h), (h, -h), (h, h), (-h, h)]

        chord = CHORD.kept() if keep_chords else CHORD
        rows = sorted({y0, y1, *(y for y in LEVELS if y0 < y < y1)})
        for a, b in itertools.pairwise(rows):
            ca, cb = corners(a), corners(b)
            for i in range(4):
                (ax, az), (bx, bz) = ca[i], cb[i]
                self.line(t(ax, a, az), t(bx, b, bz), chord)
                if a < THICK_TO:  # an angle iron: one more cell toward each neighbouring corner
                    sx, sz = (1 if ax < 0 else -1), (1 if az < 0 else -1)
                    self.line(t(ax + sx, a, az), t(bx + sx, b, bz), chord)
                    self.line(t(ax, a, az + sz), t(bx, b, bz + sz), chord)
                if i in faces:
                    j = (i + 1) % 4
                    self.line(t(*_at(ca[i], a)), t(*_at(cb[j], b)), BRACE, where="empty")
                    self.line(t(*_at(ca[j], a)), t(*_at(cb[i], b)), BRACE, where="empty")
        for y in rows:
            if y in LEVELS:
                c = corners(y)
                for i in range(4):
                    self.line(t(*_at(c[i], y)), t(*_at(c[(i + 1) % 4], y)), RING, where="empty")
        if plates:
            for y in DECKS:
                if y0 <= y <= y1:
                    n = math.floor(half(y))
                    self.fill([t(lx, y, lz) for lx in range(-n, n + 1) for lz in range(-n, n + 1)], DECK, where="empty")
        if spine_to is not None:
            for y in range(max(y0, GROUND), spine_to + 1):
                self.put(*t(0, y, 0), SPINE)
                self.put(*t(0, y, -1), LADDER)
            for y in DECKS:  # the deck round the spine stays, so every platform can be stepped onto
                if y0 <= y < spine_to:
                    self.keep([t(lx, y, lz) for lx in range(-2, 3) for lz in range(-2, 3)])

    def beacon(self, t, whole: bool = True) -> None:
        """The head above the top deck: four posts, the lantern's frame and the antenna."""
        for sx, sz in ((-2, -2), (2, -2), (2, 2), (-2, 2)):
            self.line(t(sx, TOP + 1, sz), t(sx, TOP + 4, sz), CHORD, where="empty")
        ring = [(-2, -2), (2, -2), (2, 2), (-2, 2)]
        for i in range(4 if whole else 2):
            (ax, az), (bx, bz) = ring[i], ring[(i + 1) % 4]
            self.line(t(ax, TOP + 4, az), t(bx, TOP + 4, bz), RING, where="empty")
        self.line(t(0, TOP + 1, 0), t(0, TIP if whole else TIP - 3, 0), SPINE, where="empty")

    def deck_lamps(self, t, y: int) -> None:
        """Two emergency lamps on deck y, in opposite corners; the deck cells under them stay."""
        n = math.floor(half(y)) - 1
        for sx, sz in ((1, -1), (-1, 1)):
            x, yy, z = t(sx * n, y, sz * n)
            self.put(x, yy, z, DECK, keep=True)
            self.lamp(x, yy + 1, z)

    def head(self, t) -> None:
        """The top deck as a platform that stays: three lamps round the spine and a chest."""
        self.keep([t(lx, TOP, lz) for lx in range(-3, 4) for lz in range(-3, 4)])
        for lx, lz in ((-2, 0), (2, 0), (0, 2)):
            x, y, z = t(lx, TOP, lz)
            self.put(x, y, z, DECK, keep=True)
            self.lamp(x, y + 1, z)
        x, y, z = t(-1, TOP + 1, 1)
        self.chest(x, y, z, table="ruin_rich", block="crate")

    def tear(self, y_cut: int, seed: int, spare=lambda lx, lz: False) -> None:
        """Tears the standing mast off above row y_cut: each quarter of it ends at a height of its own, ragged by a few
        rows; `spare(lx, lz)` keeps a column whole (the side a folded piece hangs on)."""
        extra = {(False, False): 0, (True, False): 6, (True, True): 3, (False, True): 9}
        for (x, y, z) in list(self.plain):
            lx, lz = x - CX, z - CZ
            if abs(lx) > FOOT + 1 or abs(lz) > FOOT + 1 or spare(lx, lz):
                continue
            top = y_cut + extra[(lx >= 0, lz >= 0)] + int(solids.hash01(x // 2, 0, z // 2, seed) * 4)
            if y > top and not self.plain[(x, y, z)][5]:
                self.clear(x, y, z)

    # --- the relay room ----------------------------------------------------------------------------------------------
    def relay_room(self) -> None:
        """The foundation slab and the relay room at the foot: steel walls round the spine, a doorway, the console in
        front of the instrument wall, two chests, two emergency lamps on brackets. Its roof is the first deck."""
        self.fill(solids.box(0, 0, 0, 2 * CX, 0, 2 * CZ), SLAB)
        x0, x1, z0, z1 = CX - 5, CX + 5, CZ - 5, CZ + 5
        self.keep(solids.box(x0, 0, z0, x1, 0, z1))
        walls = {(x, y, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1) for y in range(GROUND, 6)
                 if x in (x0, x1) or z in (z0, z1)}
        self.fill(walls, WALL)
        for (x, y, z) in solids.box(x0 + 1, GROUND, z0 + 1, x1 - 1, 5, z1 - 1):
            if (x, z) not in ((CX, CZ), (CX, CZ - 1)):
                self.clear(x, y, z)                                   # the room is free but for the spine and its ladder
        for (x, y, z) in solids.box(CX + 2, GROUND, z0, CX + 3, GROUND + 2, z0):
            self.clear(x, y, z)                                       # the doorway, two wide and three high
        self.fill([(x, 3, z) for x in (x0, x1) for z in range(CZ - 2, CZ + 3)], GLASS)        # a window strip each side
        self.fill([(x, y, z1) for x in range(CX - 3, CX + 4) for y in (2, 3)], PANEL)          # the instrument wall
        self.fill(solids.box(x0, 6, z0, x1, 6, z1), DECK, where="empty")
        self.keep(solids.box(x0, 6, z0, x1, 6, z1))                    # the roof holds: the console never stands in the rain
        self.keep([(x, y, z1) for x in range(CX - 1, CX + 2) for y in range(GROUND, 6)])
        for x in (x0 + 1, x1 - 1):                                     # two racks beside the instrument wall
            self.fill([(x, y, z1 - 1) for y in (GROUND, GROUND + 1)], Mat("machine_block"))
        self.put(CX - 3, 5, CZ + 2, Mat("strip_light_warm_off"))       # the room's own lights: dead since the towers went dark
        self.put(CX + 3, 5, CZ - 2, Mat("strip_light_warm_off"))
        for x in (CX - 3, CX + 3):                                     # the emergency lamps, on brackets on the back wall
            self.put(x, 2, z1 - 1, BRACKET)
            self.lamp(x, 3, z1 - 1)
        self.console(CX, GROUND, z1 - 1)
        self.chest(x0 + 1, GROUND, z0 + 1, table="ruin_common", block="crate")
        self.chest(x1 - 1, GROUND, CZ + 1, table="giant_battleship", block="crate")

    # --- the guard post ----------------------------------------------------------------------------------------------
    def guard_post(self) -> None:
        """The guard post beside the foot: a small wooden hut, half its roof gone, with the capsule tank between its two
        lamps and a chest. The `sps_guard_post` marker names its box; a tower without a post loses all of it."""
        hx, hy, hz = POST
        w, h, ln = POST_SIZE
        x1, z1 = hx + w - 1, hz + ln - 1
        planks, light, roof, log = Mat("wood_planks"), Mat("wood_planks_light"), Mat("wood_planks_mahogany"), Mat("wood_log")
        self.fill([(x, 0, z) for x in range(hx, x1 + 1) for z in range(hz, z1 + 1) if (x, z) != (hx, hz)], planks)
        for x in range(hx, x1 + 1):
            for z in range(hz, z1 + 1):
                edge = x in (hx, x1) or z in (hz, z1)
                corner = x in (hx, x1) and z in (hz, z1)
                if corner:
                    self.fill([(x, y, z) for y in range(GROUND, 5)], log)
                elif edge:
                    top = 3 if solids.hash01(x, 7, z, 31) > 0.25 else 1          # a wall with a few planks missing
                    self.fill([(x, y, z) for y in range(GROUND, top + 1)], light)
                if (x - hx) + (z - hz) <= 7:                                     # the roof: the far half fell in
                    self.put(x, 4, z, roof)
        for y in (GROUND, GROUND + 1):
            self.clear(hx + 3, y, hz)                                            # the door gap, toward the tower
        for z in (hz + 2, hz + 4):
            self.clear(hx, 2, z)                                                 # two window gaps
            self.clear(x1, 2, z)
        tx, tz = hx + 3, hz + 4
        self.keep([(x, 0, z) for x in range(tx - 1, tx + 2) for z in range(tz - 1, tz + 2)])
        for x in (tx - 1, tx + 1):                                               # the tank's two lamps on steel stubs
            self.put(x, GROUND, tz, BRACKET)
            self.lamp(x, GROUND + 1, tz, tank=True)
        self.clear(tx, 4, tz)                                                    # the tank stands two cells high
        self.tank(tx, GROUND, tz)
        self.chest(hx + 1, GROUND, hz + 5, table="ruin_common", block="wood_crate")
        self.marker(hx, hy, hz, "sps_guard_post", settings(w=w, h=h, l=ln))
        self.ruin_zone(hx, hy, hz, x1, hy + h - 1, z1, profile="masonry", strength=0.35)

    # --- checks and output ---------------------------------------------------------------------------------------
    def trim(self) -> None:
        """Drops what a mapping pushed below the foundation row."""
        for cell in [c for c in self.plain if c[1] < 0]:
            self.clear(*cell)
        self.markers = [m for m in self.markers if m["y"] >= 0]

    def problems(self) -> tuple[list[str], list[str]]:
        errors, warnings = super().problems()
        warnings = [w for w in warnings if "'spsTower'" not in w
                    and not any(f"marker '{m}'" in w and "not a known marker kind" in w for m in TOWER_MARKERS)]
        count = {m: sum(1 for c in self.markers if c["id"] == m) for m in TOWER_MARKERS}
        if count["sps_console"] != 1:
            errors.append(f"a tower needs exactly one sps_console marker, this one has {count['sps_console']}")
        if count["sps_guard_post"] > 1 or count["sps_tank"] != count["sps_guard_post"]:
            errors.append("a tower holds at most one guard post, with exactly one tank")
        blocks = {c for c, v in self.plain.items() if v[0] == "block"}
        for c in self.markers:
            pos = (c["x"], c["y"], c["z"])
            if c["id"] in ("sps_lamp", "sps_console", "sps_tank") and (pos[0], pos[1] - 1, pos[2]) not in blocks:
                errors.append(f"{c['id']} at {pos} has no block under it")
            if c["id"] in ("sps_lamp", "sps_tank") and (pos[0], pos[1] + 1, pos[2]) in blocks:
                errors.append(f"{c['id']} at {pos} has a block above it (the ruin pass would clear it)")
        if not any(c["id"] == "sps_lamp" and "of=tank" not in c.get("config", "") for c in self.markers):
            errors.append("a tower needs at least one lamp of its own")
        if any(v[0] == "block" and v[1] in ("sps_console", "capsule_tank") for v in self.plain.values()):
            errors.append("the console and the tank are placed by their markers, never as blocks")
        return errors, warnings

    def write(self, out: Path | str | None = None, check: bool = True) -> Path:
        return super().write(OUT if out is None else out, check)


def _at(corner: tuple[float, float], y: int) -> tuple[float, int, float]:
    return (corner[0], y, corner[1])
