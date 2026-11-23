"""Shared pieces of the Earth landmarks (#3982): the header every one of them carries, a ladder shaft with a doorway onto
each floor, a cylindrical core with a spiral stair, rails round a deck and a ring of lights.

The Earth landmarks stand only where Earth's location list places them: every one is `pinOnly` (no world rolls it), needs
an atmosphere and is named `landmark.<key>.name` / described `landmark.<key>.desc`. Earth's cities are inhabited, so a city
landmark is intact, lit and protected; only the pyramids are ancient ruins. Not a landmark script itself (the leading
underscore keeps tools/gen_landmarks.py from running it).
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from geom import CLEAR, Design, Mat

Cell = tuple[int, int, int]

OPENINGS = {"+x": (1, 0), "-x": (-1, 0), "+z": (0, 1), "-z": (0, -1)}


def earth_design(key: str, name: str, ruin: str = "never", **header) -> Design:
    """A design with the Earth landmark header: pin-only, needs an atmosphere, its name key; an intact one (the default)
    is `protection: protected`, a ruin (`ruin='always'`) leaves protection to the kind's rule."""
    fields = {"nameKey": f"landmark.{key}.name", "pinOnly": True, "ruin": ruin}
    if ruin == "never":
        fields["protection"] = "protected"
    fields.update(header)
    fields.setdefault("requires", ["atmosphere"])
    return Design(key, name, **fields)


def ladder_shaft(d: Design, x: int, z: int, floors: list[int], opening: str = "-z", width: int = 2, walls="steel_wall",
                 ladder="ladder", landing=None, keep: bool = False) -> set[Cell]:
    """A ladder shaft joining floors (their walk rows): `width` ladder cells side by side at (x, z) (along x for an opening
    to +-z, along z for +-x) from the lowest walk row to one above the highest, walled in on three sides; on the `opening`
    side a 3-high doorway onto every floor with a two-cell landing in front of it. The same shaft as `ShipDesign.lift_shaft`
    (ships.py), for a landmark."""
    ox, oz = OPENINGS[opening]
    ux, uz = (1, 0) if ox == 0 else (0, 1)
    floors = sorted(floors)
    ytop = floors[-1] + 1
    lad = [(x + ux * i, z + uz * i) for i in range(width)]
    out: set[Cell] = set()
    wall_m = Mat(walls) if isinstance(walls, str) else walls
    land_m = wall_m if landing is None else (Mat(landing) if isinstance(landing, str) else landing)
    lad_m = Mat(ladder) if isinstance(ladder, str) else ladder
    kw = {"keep": True} if keep else {}
    back = [(lx - ox, lz - oz) for lx, lz in lad]
    ends = [(x - ux - ox * k, z - uz - oz * k) for k in (0, 1)] + \
           [(x + ux * width - ox * k, z + uz * width - oz * k) for k in (0, 1)]
    front = [(lx + ox, lz + oz) for lx, lz in lad]
    for y in range(floors[0] - 1, ytop + 3):
        out |= d.fill([(cx, y, cz) for cx, cz in back + ends], wall_m, **kw)
        for cx, cz in lad:
            out |= d.fill([(cx, y, cz)], wall_m if (y == floors[0] - 1 or y > ytop) else lad_m, **kw)
        for cx, cz in front:
            if any(f <= y <= f + 2 for f in floors):
                d.put(cx, y, cz, CLEAR)
            elif y > floors[0] - 1:
                out |= d.fill([(cx, y, cz)], wall_m, **kw)
    for f in floors:
        out |= d.fill([(lx + ox * k, f - 1, lz + oz * k) for lx, lz in lad for k in (1, 2)], land_m, **kw)
    return out


def fence_rail(d: Design, cells, m) -> None:
    """A railing in the `fence` form along a set of cells on one row: each cell's fence runs along its longer neighbour
    line (along x where its x neighbours are railing too, else along z)."""
    cells = set(cells)
    along_x = Mat(m) if isinstance(m, str) else m
    for x, y, z in cells:
        run_x = (x - 1, y, z) in cells or (x + 1, y, z) in cells
        d.put(x, y, z, along_x, shape=sc.pack(sc.FENCE) if run_x else sc.pack(sc.FENCE, 1))


def square_ring(cx: float, cz: float, y: int, r_out: float, r_in: float) -> set[Cell]:
    """The cells of a square ring on row y: within r_out of the centre in both axes but not within r_in in both."""
    lo, hi = math.floor(cx - r_out), math.ceil(cx + r_out)
    return {(x, y, z) for x in range(lo, hi + 1) for z in range(math.floor(cz - r_out), math.ceil(cz + r_out) + 1)
            if max(abs(x - cx), abs(z - cz)) <= r_out and max(abs(x - cx), abs(z - cz)) > r_in}


def square_edge(cx: float, cz: float, y: int, r: float) -> set[Cell]:
    """The outermost cells of the square of half size r on row y."""
    return square_ring(cx, cz, y, r, r - 1)


def ring_lights(d: Design, cx: float, cz: float, y: int, r: float, count: int, block: str = "lantern",
                phase: float = 0.0) -> int:
    """`count` lights evenly round a circle of radius r on row y, each only into an empty cell (the light-mount pass
    hangs it on what holds it)."""
    n = 0
    for x, z, _ in solids.Ellipse(cx, cz, r, r).points(count, phase):
        p = (round(x), y, round(z))
        if p not in d.plain:
            d.put(*p, block)
            n += 1
    return n


def glow(block: str, colour: int, **kw) -> Mat:
    """A block that glows in `colour` (a light band, a lit window, a lamp)."""
    return Mat(block, glow=colour, **kw)
