"""The CIVILIZATION template toolkit (#3639): `CivDesign` — a `geom.Design` that writes one template of a lost
civilization (`data/civilizations/<civilization>/<key>.json`) — plus the helpers its buildings need: AIR POCKETS built
as diving bells (#3640), `prop` markers (#3641), murals laid tile by tile, and pieces placed in any of the 24 cube
orientations (a glyph cube lying on its side keeps its lid rim and its socket ring where they belong).

A civilization template is found only through the civilization framework (the civilization stage stamps it on the
civilization's home bodies); no settlement, landmark or giant-ship pool ever offers it. Its header carries
`kind: "civilization"`, `civilization` (the civilization's key), `civPool` (`giant`, `city`, `small` or `prop` — a prop
is a small piece a `prop pool=<propPool>` marker places, never a site of its own) and, for a prop, `propPool`.

Water (seat `seabed`, every Grand Technicians template): the stamp fills every EMPTY cell and every CAVITY under the
sea level with water. An `air` cell stays air. The world's water flows down and sideways into air, never up, so an air
pocket must be a DIVING BELL: blocks on every side and on top, its only opening at the bottom (a diver swims up into it
from below), at least 3 x 3 wide and 3 high. `bell_problems` checks exactly that, and that the ruin pass cannot open it
(every cell that closes it is `keep`).

    from civ import CivDesign, Mat, Mix, AIR, CAVITY, CLEAR, orient
    d = CivDesign("gt_example", "Example", civilization="grosstechniker", pool="small", seat="seabed", ruin="always",
                  ruinProfile="masonry", ruinStrength=0.4, groundRow=3)
    ...
    d.write()        # -> data/civilizations/grosstechniker/gt_example.json
"""
from __future__ import annotations

from collections.abc import Iterable
from pathlib import Path

try:  # imported as the package `landmarks.civ`
    from . import shapecode as sc
    from . import solids, templatefile
    from .geom import (
        AIR,
        CAVITY,
        CLEAR,
        MAX_RUIN_ZONES,
        MAX_SIZE,
        PROFILES,
        Design,
        Mat,
        Mix,
        Palette,
        rgb,
    )
except ImportError:  # imported from a script in tools/landmarks
    import shapecode as sc
    import solids
    import templatefile
    from geom import (
        AIR,
        CAVITY,
        CLEAR,
        MAX_RUIN_ZONES,
        MAX_SIZE,
        PROFILES,
        Design,
        Mat,
        Mix,
        Palette,
        rgb,
    )

__all__ = [
    "AIR",
    "CAVITY",
    "CIVILIZATIONS",
    "CLEAR",
    "ORIENTATIONS",
    "CivDesign",
    "Mat",
    "Mix",
    "Palette",
    "orient",
    "rgb",
    "sc",
    "solids",
]

Cell = tuple[int, int, int]
Matrix = tuple[tuple[int, int, int], tuple[int, int, int], tuple[int, int, int]]
CIV_OUT = templatefile.CIVILIZATIONS
CIV_GENERATION = 43  # WorldDescription.CivilizationsGeneration

# Mirrors of CivilizationRules (C#).
POOLS = ("giant", "city", "small", "prop")             # CivilizationRules.TemplatePools
SEATS = ("", "span", "seabed")                          # LandmarkRules.Seats + the civilization stage's sea floor
RUIN_MODES = ("", "never", "always")
PROTECTION = ("", "auto", "protected", "open")
LOST_MARKERS = {                                        # CivilizationRules.LostMarkerAllowed: nobody home
    "chest", "sound", "prop", "loot", "data_terminal", "relic_cache", "crystal", "door_slide", "door_hinge",
    "door_energy",
}
HEADER_FIELDS = (  # written in this order (whatever is set)
    "key", "name", "kind", "civilization", "civPool", "propPool", "nameKey", "weight", "minTerrainGeneration", "seat",
    "ruin", "ruinProfile", "ruinStrength", "protection", "groundRow", "pack", "ruinZones",
)

# The civilizations this toolkit builds for: their blocks (being added to data/blocks.json — known here so the checks and
# the preview know them before they land; the preview colour is used only while blocks.json has none), their chest
# tables and their prop pools.
CIVILIZATIONS: dict[str, dict] = {
    "grosstechniker": {
        "blocks": {
            "glyph_stone": (104, 82, 56), "glyph_stone_band": (176, 140, 82), "dark_sandstone": (142, 114, 78),
            "gt_eye": (238, 238, 232), "gt_fluid": (225, 36, 40),
            **{f"gt_mural_{i}": c for i, c in enumerate([(84, 128, 168), (96, 140, 176), (90, 134, 172), (80, 124, 164),
                                                        (70, 110, 150), (78, 118, 156), (74, 114, 154), (66, 106, 146)])},
        },
        "tables": ("civ_grosstechniker_common", "civ_grosstechniker_rich"),
        "prop_pools": ("remains",),
    },
    "labyrinth_builders": {
        "blocks": {"maze_stone": (62, 62, 70), "maze_stone_carved": (92, 90, 102)},
        "tables": ("civ_labyrinth_builders_common", "civ_labyrinth_builders_rich"),
        "prop_pools": ("statue", "statue_large"),
    },
}

# --- the 24 cube orientations ------------------------------------------------------------------------------------------
_I: Matrix = ((1, 0, 0), (0, 1, 0), (0, 0, 1))


def _mul(a: Matrix, b: Matrix) -> Matrix:
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))  # type: ignore[return-value]


def _apply(m: Matrix, v) -> tuple[int, int, int]:
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2])


# How a piece built upright (lid on local +Y, front on local -Z) lies: world = R * local. A yaw turns it afterwards.
ORIENTATIONS: dict[str, Matrix] = {
    "upright": _I,
    "upside_down": ((1, 0, 0), (0, -1, 0), (0, 0, -1)),   # lid down, front to +Z
    "on_side_px": ((0, 1, 0), (-1, 0, 0), (0, 0, 1)),     # lid toward +X, front stays -Z
    "on_side_nx": ((0, -1, 0), (1, 0, 0), (0, 0, 1)),     # lid toward -X, front stays -Z
    "on_back": ((1, 0, 0), (0, 0, -1), (0, 1, 0)),        # front up, lid toward +Z
    "on_front": ((1, 0, 0), (0, 0, 1), (0, -1, 0)),       # front down, lid toward -Z
}


def turn(orientation: str | Matrix, yaw: int = 0) -> Matrix:
    """An orientation followed by `yaw` quarter turns about +Y (the yaw's sense: one turn takes +Z to -X)."""
    m = ORIENTATIONS[orientation] if isinstance(orientation, str) else orientation
    for _ in range(yaw & 3):
        m = _mul(((0, 0, -1), (0, 1, 0), (1, 0, 0)), m)
    return m


def turn_descriptor(descriptor: int, m: Matrix) -> int:
    """A cell's shape descriptor after the piece it belongs to turned by `m`: a form turns with it; a plain cube keeps a
    stored FRONT (CubeFacing: an up-face 2..5) only while that front still points sideways."""
    if not descriptor:
        return 0
    if sc.shape_of(descriptor) == sc.CUBE:
        up = sc.up_of(descriptor)
        if up not in (2, 3, 4, 5):
            return descriptor
        face = sc.face_from_direction(*_apply(m, sc.face_direction(up)))
        return sc.pack(sc.CUBE, 0, face) if face in (2, 3, 4, 5) else 0
    yaw, up = sc.orientation_of(_mul(m, sc.matrix(sc.yaw_of(descriptor), sc.up_of(descriptor))))
    rest = descriptor & ~0x7FF
    return rest | ((up & 0x7) << 8) | (sc.shape_of(descriptor) << 2) | (yaw & 0x3)


def orient(d: Design, part: Design, at: Cell, orientation: str | Matrix = "upright", yaw: int = 0,
           where: str | None = None) -> set[Cell]:
    """Writes `part` into `d` turned by `orientation` (a name of ORIENTATIONS or a matrix) and `yaw`, its bounding box's
    low corner on `at`. Shaped cells and markers turn with it. Returns the cells written."""
    m = turn(orientation, yaw)
    plain = {_apply(m, p): v for p, v in part.plain.items()}
    marks = [(_apply(m, (k["x"], k["y"], k["z"])), k) for k in part.markers]
    pts = list(plain) + [p for p, _ in marks]
    if not pts:
        return set()
    lo = [min(p[i] for p in pts) for i in range(3)]
    done = set()
    for p, v in plain.items():
        pos = (p[0] - lo[0] + at[0], p[1] - lo[1] + at[1], p[2] - lo[2] + at[2])
        if not d._allowed(pos, where):
            continue
        if v[0] == "block" and v[4]:
            v = (v[0], v[1], v[2], v[3], turn_descriptor(v[4], m), v[5])
        d.plain[pos] = v
        done.add(pos)
    for p, k in marks:
        c = dict(k)
        c["x"], c["y"], c["z"] = p[0] - lo[0] + at[0], p[1] - lo[1] + at[1], p[2] - lo[2] + at[2]
        d.markers.append(c)
    return done


def oriented_size(part: Design, orientation: str | Matrix = "upright", yaw: int = 0) -> tuple[int, int, int]:
    """The (x, y, z) size of `part` turned like `orient` would."""
    m = turn(orientation, yaw)
    pts = [_apply(m, p) for p in part.plain]
    return tuple(max(p[i] for p in pts) - min(p[i] for p in pts) + 1 for i in range(3))  # type: ignore[return-value]


FRONT_FACE = {(1, 0, 0): sc.UP_PX, (-1, 0, 0): sc.UP_NX, (0, 0, 1): sc.UP_PZ, (0, 0, -1): sc.UP_NZ}


def front(normal: tuple[int, int, int]) -> int:
    """The descriptor of a plain cube whose FRONT (CubeFacing) looks along the horizontal `normal`."""
    return sc.pack(sc.CUBE, 0, FRONT_FACE[normal])


def viewer_right(normal: tuple[int, int, int]) -> tuple[int, int, int]:
    """The direction to the right of a viewer who looks at a face whose outward normal is `normal` (Unity's axes: looking
    along +Z, +X is to the right)."""
    fx, fz = -normal[0], -normal[2]
    return (fz, 0, -fx)


# --- the design ----------------------------------------------------------------------------------------------------------
class CivDesign(Design):
    """A civilization template under construction. Header: `key, name, kind: civilization, civilization, civPool,
    propPool (a prop only), nameKey, weight, minTerrainGeneration` plus what is passed (seat, ruin, ruinProfile,
    ruinStrength, protection, groundRow, ruinZones ...). `nameKey` defaults to `civ.<civilization>.site.<key>` for a site
    and `civ.<civilization>.prop.<key>` for a prop."""

    def __init__(self, key: str, name: str, civilization: str, pool: str, propPool: str = "", weight: int = 1,
                 **header) -> None:
        super().__init__(key, name)
        kind = "prop" if pool == "prop" else "site"
        self.header = {"key": key, "name": name, "kind": "civilization", "civilization": civilization, "civPool": pool}
        if propPool:
            self.header["propPool"] = propPool
        self.header.update({"nameKey": f"civ.{civilization}.{kind}.{key}", "weight": weight,
                            "minTerrainGeneration": CIV_GENERATION})
        self.header.update(header)

    # --- cells -------------------------------------------------------------------------------------------------------
    def air(self, cells: Iterable[Cell]) -> set[Cell]:
        """Air-pocket cells (#3640): they stay air under water. Build them as a diving bell (see `bell`)."""
        return self.fill(cells, AIR)

    def bell(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, wall, roof=None, keep: bool = True,
             openings: Iterable[tuple[int, int]] = ()) -> set[Cell]:
        """A DIVING BELL: the air box x0..x1, y0..y1, z0..z1 (corners inclusive, at least 3 x 3 wide and 3 high), closed
        by `wall` on its four sides and `roof` (default: `wall`) on top, all of it `keep` so the ruin pass never opens
        it; its floor row (y0 - 1) is left alone — give it `openings` (x, z) the diver swims up through, and fill the
        rest of the floor yourself (a tank's liquid, a stone floor). Returns the air cells."""
        (x0, x1), (y0, y1), (z0, z1) = sorted((x0, x1)), sorted((y0, y1)), sorted((z0, z1))
        if x1 - x0 < 2 or z1 - z0 < 2 or y1 - y0 < 2:
            raise ValueError("a diving bell needs at least 3 x 3 x 3 cells of air")
        roof = wall if roof is None else roof
        kw = {"keep": True} if keep else {}
        self.fill(solids.box_walls(x0 - 1, y0, z0 - 1, x1 + 1, y1, z1 + 1), wall, **kw)
        self.fill(solids.box(x0 - 1, y1 + 1, z0 - 1, x1 + 1, y1 + 1, z1 + 1), roof, **kw)
        cells = self.air(solids.box(x0, y0, z0, x1, y1, z1))
        for x, z in openings:
            self.clear(x, y0 - 1, z)
        return cells

    # --- markers -----------------------------------------------------------------------------------------------------
    def prop(self, x: int, y: int, z: int, pool: str | None = None, template: str | None = None) -> None:
        """A `prop` marker (#3641): after the ruin pass the server places a random prop of the civilization's
        `pool` (or the template `template`) standing on the ground here, turned by a hash of the cell."""
        if (pool is None) == (template is None):
            raise ValueError("a prop marker names a pool or a template")
        self._free(x, y, z)
        self.marker(x, y, z, "prop", f"pool={pool}" if pool else f"template={template}")

    def mural(self, x: int, y: int, z: int, normal: tuple[int, int, int], prefix: str = "gt_mural_", cols: int = 4,
              rows: int = 2, keep: bool = True) -> set[Cell]:
        """A mural of cols x rows tiles (`<prefix>0` .. : the top row left to right, then the next row) in the wall
        cells whose visible face looks along the horizontal `normal` (into the room). (x, y, z) is the BOTTOM-LEFT tile
        as the viewer sees it. Every tile stores that face as its front (CubeFacing), so a block with a front shows its
        picture there. Returns the cells."""
        r = viewer_right(normal)
        out = set()
        for row in range(rows):
            for col in range(cols):
                pos = (x + r[0] * col, y + (rows - 1 - row), z + r[2] * col)
                self.put(*pos, Mat(f"{prefix}{row * cols + col}", shape=front(normal), keep=keep))
                out.add(pos)
        return out

    # --- checks ------------------------------------------------------------------------------------------------------
    def known_blocks(self) -> set[str]:
        try:
            known = set(templatefile.block_keys())
        except (OSError, ValueError):
            known = set()
        for civ in CIVILIZATIONS.values():
            known |= set(civ["blocks"])
        return known

    def _non_solid(self) -> set[str]:
        try:
            return {b["key"] for b in templatefile.blocks() if b.get("solid") is False}
        except (OSError, ValueError):
            return set()

    def bell_problems(self) -> list[str]:
        """The diving-bell rule for every connected group of air cells: closed by solid blocks on all four sides and on
        top (no empty cell, no cavity, nothing non-solid), every closing cell `keep`, at least one opening at the bottom
        (an empty cell or a cavity right below an air cell), and room for a player (a 3 x 3 x 3 box of air)."""
        air = self.air_cells()
        if not air:
            return []
        soft = self._non_solid()
        problems = []
        seen: set[Cell] = set()
        for start in sorted(air):
            if start in seen:
                continue
            group, todo = set(), [start]
            while todo:
                p = todo.pop()
                if p in group:
                    continue
                group.add(p)
                for dx, dy, dz in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
                    q = (p[0] + dx, p[1] + dy, p[2] + dz)
                    if q in air and q not in group:
                        todo.append(q)
            seen |= group
            name = f"air pocket at {min(group)}"
            leaks, loose, openings = [], [], []
            for x, y, z in sorted(group):
                for q in ((x + 1, y, z), (x - 1, y, z), (x, y, z + 1), (x, y, z - 1), (x, y + 1, z)):
                    if q in group:
                        continue
                    v = self.plain.get(q)
                    if v is None or v[0] != "block" or v[1] in soft:
                        leaks.append(q)
                    elif not v[5]:
                        loose.append(q)
                below = self.plain.get((x, y - 1, z))
                if (x, y - 1, z) not in group and (below is None or below[0] == "cavity"):
                    openings.append((x, y - 1, z))
            if leaks:
                problems.append(f"{name}: water gets in from the side or the top at {leaks[:4]} "
                                f"(a diving bell is closed everywhere but at the bottom)")
            if loose:
                problems.append(f"{name}: {len(loose)} closing cells are not keep (the ruin pass could open the bell), "
                                f"e.g. {loose[:3]}")
            if not openings:
                problems.append(f"{name}: no opening at the bottom — nobody can reach it")
            if not _has_box(group, 3, 3, 3):
                problems.append(f"{name}: no 3 x 3 x 3 box of air — a player does not fit")
        return problems

    def problems(self) -> tuple[list[str], list[str]]:
        """(errors, warnings) of a civilization template: the landmark rules, the civilization rules (pool, prop pool,
        nobody home), known blocks, markers on the ground, the diving bells."""
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
            if k not in HEADER_FIELDS:
                warnings.append(f"unknown header field '{k}' (passed through)")
        civ = CIVILIZATIONS.get(h.get("civilization", ""))
        if civ is None:
            errors.append(f"unknown civilization '{h.get('civilization')}' ({' | '.join(CIVILIZATIONS)})")
            civ = {"blocks": {}, "tables": (), "prop_pools": ()}
        pool = h.get("civPool", "")
        if pool not in POOLS:
            errors.append(f"civPool '{pool}' is not one of {' | '.join(POOLS)}")
        if pool == "prop" and h.get("propPool") not in civ["prop_pools"]:
            errors.append(f"a prop needs a propPool of its civilization ({' | '.join(civ['prop_pools'])})")
        if pool != "prop" and h.get("propPool"):
            errors.append("only a prop has a propPool")
        for field, allowed in (("ruin", RUIN_MODES), ("ruinProfile", ("",) + PROFILES), ("protection", PROTECTION),
                               ("seat", SEATS)):
            if str(h.get(field, "")).lower() not in allowed:
                errors.append(f"{field} '{h[field]}' not one of {' | '.join(a for a in allowed if a)}")
        if not 0 <= float(h.get("ruinStrength", 0) or 0) <= 1:
            errors.append("ruinStrength must lie between 0 and 1")
        if not 0 <= int(h.get("groundRow", 0) or 0) < ht:
            errors.append("groundRow lies outside the structure")
        if int(h.get("weight", 1) or 0) < 1:
            errors.append("weight must be at least 1")
        errors += self._zone_problems(w, ht, ln)
        used = {v[1] for v in self.plain.values() if v[0] == "block"}
        for blk in sorted(used - self.known_blocks()):
            errors.append(f"unknown block '{blk}'")
        e2, w2 = self._marker_problems(civ)
        errors += e2 + self.bell_problems()
        warnings += w2
        return errors, warnings

    def _zone_problems(self, w: int, ht: int, ln: int) -> list[str]:
        errors = []
        zones = self.header.get("ruinZones", []) or []
        if len(zones) > MAX_RUIN_ZONES:
            errors.append(f"more than {MAX_RUIN_ZONES} ruin zones")
        for i, zone in enumerate(zones, 1):
            if str(zone.get("profile", "")).lower() not in ("",) + PROFILES:
                errors.append(f"ruin zone {i}: unknown profile '{zone.get('profile')}'")
            if not 0 <= float(zone.get("strength", 0)) <= 1:
                errors.append(f"ruin zone {i}: strength must lie between 0 and 1")
            if min(zone["x0"], zone["y0"], zone["z0"]) < 0 or zone["x1"] >= w or zone["y1"] >= ht or zone["z1"] >= ln:
                errors.append(f"ruin zone {i} reaches outside the structure ({w}x{ht}x{ln})")
        if str(self.header.get("ruin", "")).lower() != "always" and zones:
            errors.append("ruin zones on a template that never stands as a ruin")
        return errors

    def _marker_problems(self, civ: dict) -> tuple[list[str], list[str]]:
        errors: list[str] = []
        warnings: list[str] = []
        try:
            tables = set(templatefile.loot_tables()) | set(civ["tables"])
        except (OSError, ValueError):
            tables = set(civ["tables"])
        g = int(self.header.get("groundRow", 0) or 0)
        for m in self.markers:
            pos = (m["x"], m["y"], m["z"])
            cfg = dict(part.split("=", 1) for part in m.get("config", "").split(";") if "=" in part)
            if m["id"] not in LOST_MARKERS:
                errors.append(f"marker '{m['id']}' at {pos}: a lost civilization has nobody home")
            v = self.plain.get(pos)
            if v is not None and v[0] == "block":
                errors.append(f"marker '{m['id']}' at {pos} sits in a block cell")
            if m["id"] == "chest" and cfg.get("table") and cfg["table"] not in tables:
                warnings.append(f"chest at {pos}: unknown loot table '{cfg['table']}'")
            if m["id"] == "prop":
                if cfg.get("pool") and cfg["pool"] not in civ["prop_pools"]:
                    errors.append(f"prop at {pos}: unknown prop pool '{cfg['pool']}'")
                under = self.plain.get((m["x"], m["y"] - 1, m["z"]))
                if not (m["y"] - 1 <= g or (under is not None and under[0] == "block")):
                    warnings.append(f"prop at {pos} does not stand on the ground or a floor")
                crowd = [q for q in solids.box(m["x"] - 1, m["y"], m["z"] - 1, m["x"] + 1, m["y"] + 1, m["z"] + 1)
                         if q in self.plain and self.plain[q][0] == "block"]
                if crowd:
                    warnings.append(f"prop at {pos}: blocks next to it leave no room ({crowd[:3]})")
        return errors, warnings

    def write(self, out: Path | str | None = None, check: bool = True) -> Path:
        """Writes `<out>/<key>.json` (default data/civilizations/<civilization>/)."""
        return super().write(CIV_OUT / self.header["civilization"] if out is None else out, check)


def _has_box(cells: set[Cell], sx: int, sy: int, sz: int) -> bool:
    for x, y, z in cells:
        if all((x + i, y + j, z + k) in cells for i in range(sx) for j in range(sy) for k in range(sz)):
            return True
    return False


# --- size checks the generator scripts assert ---------------------------------------------------------------------------
def expect(d: CivDesign, footprint: tuple[int, int], height_above: tuple[int, int], sunk: tuple[int, int] = (0, 0),
           blocks: Iterable[str] = (), markers: dict[str, tuple[int, int]] | None = None) -> str:
    """Asserts the size of a finished design: its larger footprint side within `footprint`, the rows above the ground
    row within `height_above`, the rows below it within `sunk`; every block of `blocks` used; each marker kind's count
    within its range. Returns a one-line summary."""
    b = d.bounds()
    assert b is not None, f"{d.header['key']}: empty"
    w, ht, ln = b[3] + 1, b[4] + 1, b[5] + 1
    g = int(d.header.get("groundRow", 0) or 0)
    above = ht - g - 1
    key = d.header["key"]
    assert footprint[0] <= max(w, ln) <= footprint[1], f"{key}: footprint {w}x{ln} outside {footprint}"
    assert height_above[0] <= above <= height_above[1], f"{key}: {above} rows above the ground row, not {height_above}"
    assert sunk[0] <= g <= sunk[1], f"{key}: {g} rows sunk, not {sunk}"
    used = {v[1] for v in d.plain.values() if v[0] == "block"}
    missing = set(blocks) - used
    assert not missing, f"{key}: does not use {sorted(missing)}"
    counts: dict[str, int] = {}
    for m in d.markers:
        counts[m["id"]] = counts.get(m["id"], 0) + 1
    for kind, (lo, hi) in (markers or {}).items():
        assert lo <= counts.get(kind, 0) <= hi, f"{key}: {counts.get(kind, 0)} {kind} markers, not {lo}..{hi}"
    return f"{key}: {w}x{ht}x{ln} (ground row {g}, {above} above) markers {counts}"


def seed_preview_colours(preview_module) -> None:
    """Gives the preview the colours of the civilization blocks that data/blocks.json does not know yet."""
    try:
        known = {b["key"] for b in templatefile.blocks() if b.get("color") is not None}
    except (OSError, ValueError):
        known = set()
    for civ in CIVILIZATIONS.values():
        for key, colour in civ["blocks"].items():
            if key not in known:
                preview_module._COLOURS.setdefault(key, colour)
