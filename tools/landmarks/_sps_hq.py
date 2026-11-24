"""Shared parts of the SPS HEADQUARTERS in the Amazon (#3986): the complex frame its five templates are placed in, the
Service's blue-grey palette, and the pieces several parts use — server racks, consoles, cable trays, overgrowth.

The headquarters of the Scout and Pioneer Service lie ruined deep in the rain forest. Five landmark templates (each at most
256 per axis, all pin-only) stand side by side and join through corridors at their edges; Earth's location list places them
as five given places with the SAME ground row Y and no turn. In the complex frame (x east, z north, Y = 0 the shared ground
row; a template's local y is Y + its `groundRow`) their low corners are:

    key                     low corner (x, z)   size x * z   ground row   joins
    earth_sps_hq_main       (0, 0)              160 * 120    23           hangar (east wall, ground floor), servers (basement tunnel, north)
    earth_sps_hq_hangar     (160, 0)            200 * 120    1            main (west wall), hall (north wall)
    earth_sps_hq_servers    (0, 120)            150 * 150    59 (top)     main (level 1, south), core (level 3, east)
    earth_sps_hq_core       (150, 175)          41 * 37      59 (top)     servers (west)
    earth_sps_hq_hall       (200, 120)          96 * 40      1            hangar (south wall); its great gate opens west

A given seat names the footprint's centre: the low corner + (width // 2, length // 2) (`GivenSeat.OriginFor`).

Not a landmark script itself (the leading underscore keeps tools/gen_landmarks.py from running it).
"""
from __future__ import annotations

import shapecode as sc
import solids
from geom import CAVITY, CLEAR, Design, Mat, Mix

Cell = tuple[int, int, int]

# The complex frame (see the module doc): key -> (low corner x, low corner z, width, length, ground row).
PLACES: dict[str, tuple[int, int, int, int, int]] = {
    "earth_sps_hq_main": (0, 0, 160, 120, 23),
    "earth_sps_hq_hangar": (160, 0, 200, 120, 1),
    "earth_sps_hq_servers": (0, 120, 150, 150, 59),
    "earth_sps_hq_core": (150, 175, 41, 37, 59),
    "earth_sps_hq_hall": (200, 120, 96, 40, 1),
}


def local(key: str, x: int, y: int, z: int) -> Cell:
    """A complex-frame cell (Y = 0 the shared ground row) in the template `key`'s own cells."""
    ox, oz, _, _, g = PLACES[key]
    return (x - ox, y + g, z - oz)


# The joints: where a corridor crosses from one part into the next (complex frame; x0..x1 / z0..z1 inclusive, the walk row Y,
# the clear height). Each part carves its half up to its edge; tests and previews read the same numbers.
JOINT_MAIN_HANGAR = {"x": (159, 160), "z": (56, 63), "y": 1, "h": 5}      # through main's east and the hangar's west wall
JOINT_MAIN_SERVERS = {"x": (66, 70), "z": (119, 120), "y": -20, "h": 4}   # main's basement tunnel into server level 1
JOINT_SERVERS_CORE = {"x": (149, 150), "z": (193, 197), "y": -58, "h": 5}  # server level 3 into the core chamber's tunnel
JOINT_HANGAR_HALL = {"x": (258, 261), "z": (119, 120), "y": 1, "h": 5}     # through the hangar's north and the hall's south wall

# The Service's blue-grey (the guide towers' and the giant ships' palette).
GUNMETAL, PLATE, LIGHT, SPS_BLUE, PALE = 0x3C4652, 0x48515A, 0x5A636D, 0x3E6A8E, 0x7C848D
WALL = Mat("concrete", tint=0x6F7C89)               # blue-grey concrete
WALL_DARK = Mat("concrete", tint=0x56616C)
SLAB = Mat("concrete", tint=0xA9B0B8)               # floor slabs
BAND = Mat("iron_block", tint=SPS_BLUE)             # the Service's blue bands
FRAME = Mat("iron_block", tint=GUNMETAL)
STEEL = Mat("steel_wall", tint=0x4A6A8A)
FLOOR = Mat("steel_floor")
GLASS = Mat("glass", tint=0x41606E)
PANEL = Mat("lab_panel")
DESK = Mat("metal_panel", tint=GUNMETAL)
SCREEN = Mat("iron_block", tint=0x1B2229)           # a dark, dead screen
LAMP_DEAD = Mat("light_blue_off")
LAMP_BLUE = Mat("light_blue")                       # the sky-lights' blue
RACK = Mat("server_rack")
RACK_LIT = Mat("server_rack_lit")
TRAY = Mat("cable_tray")
LEAVES = Mat("tree_leaves")
LOG = Mat("wood_log")
MOSS = Mat("moss_stone")
GRASS = Mat("grass")
STAIR = Mat("concrete", tint=0x8D97A1, keep=True)   # kept stairs: the way through always stays
RAIL = Mat("iron_block", tint=PALE)

# Weathered surfaces: the visible skin of the walls takes moss and green stains near the ground.
WALL_MOSSY = Mix(WALL, (MOSS, 0.10), (Mat("concrete", tint=0x5E7262), 0.08), seed=31)
PAVING = Mix(Mat("concrete", tint=0x8A9096), (MOSS, 0.18), (GRASS, 0.22), (Mat("dirt"), 0.06), seed=7)


def h01(x: int, y: int, z: int, seed: int) -> float:
    """A stable hash in [0, 1) of a cell and a seed."""
    return solids.hash01(x, y, z, seed)


def free(d: Design, p: Cell) -> bool:
    """Whether a cell holds no block: empty (air above the ground row) or a cavity (air below it)."""
    v = d.get(*p)
    return v is None or v[0] == "cavity"


def put_free(d: Design, cells, m, **kw) -> set[Cell]:
    """Material `m` into those of `cells` that hold no block (`free`)."""
    return d.fill([p for p in cells if free(d, p)], m, **kw)


# ---------------------------------------------------------------------------------------------------------------- racks
def rack(d: Design, x0: int, y0: int, z0: int, w: int = 6, h: int = 16, ln: int = 6, seed: int = 0,
         hollow=CAVITY, lit: float = 0.035, broken: float = 0.0) -> set[Cell]:
    """One huge ancient server rack standing on row y0 with its low corner at (x0, z0): a shell of rack faces, a plain
    gunmetal cap, its inside `hollow` (CAVITY underground, CLEAR above). A share `lit` of its face cells still blinks;
    `broken` > 0 tears that share of its upper face away (the inside shows) and lays a fallen panel at its foot."""
    cells = solids.box(x0, y0, z0, x0 + w - 1, y0 + h - 1, z0 + ln - 1)
    inner = solids.box(x0 + 1, y0 + 1, z0 + 1, x0 + w - 2, y0 + h - 2, z0 + ln - 2)
    shell = cells - inner
    out: set[Cell] = set()
    for p in shell:
        x, y, z = p
        if y == y0 + h - 1:
            d.put(x, y, z, FRAME)
        elif broken and y > y0 + h // 2 and h01(x, y, z, seed + 5) < broken:
            d.put(x, y, z, hollow)
            continue
        else:
            d.put(x, y, z, RACK_LIT if h01(x, y, z, seed) < lit else RACK)
        out.add(p)
    d.fill(inner, hollow)
    if broken:
        fx = x0 + w // 2
        for k in range(3):
            d.put(fx - 1 + k, y0, z0 - 1, FRAME, shape=sc.pack(sc.SLAB))
    return out


def fallen_rack(d: Design, x0: int, y: int, z0: int, along: str = "x", length: int = 16, seed: int = 0) -> set[Cell]:
    """A rack that toppled long ago: lying on row y along x (or z), 6 wide and 6 high, its faces dark."""
    if along == "x":
        box = solids.box(x0, y, z0, x0 + length - 1, y + 5, z0 + 5)
    else:
        box = solids.box(x0, y, z0, x0 + 5, y + 5, z0 + length - 1)
    shell = solids.shell(box)
    for x, yy, z in shell:
        d.put(x, yy, z, RACK_LIT if h01(x, yy, z, seed) < 0.01 else RACK)
    return shell


def console(d: Design, x: int, y: int, z: int, facing: str = "-z", lamp: bool = False) -> None:
    """An SPS console standing on row y: a desk (a gunmetal ramp toward the operator) and a dark screen behind it;
    `lamp` = one faint blue lamp still glowing on the screen's top."""
    fx, fz = {"-z": (0, -1), "+z": (0, 1), "-x": (-1, 0), "+x": (1, 0)}[facing]
    d.put(x, y, z, DESK, shape=sc.ramp(-fx, -fz, low=True))
    d.put(x - fx, y, z - fz, DESK)
    d.put(x - fx, y + 1, z - fz, SCREEN)
    d.put(x - fx, y + 2, z - fz, LAMP_BLUE if lamp else LAMP_DEAD, shape=sc.pack(sc.SLAB))


def tray(d: Design, p0: Cell, p1: Cell) -> set[Cell]:
    """A straight run of cable tray (consecutive cells along one axis)."""
    return d.fill(solids.box(*p0, *p1), TRAY)


# ------------------------------------------------------------------------------------------------------------ overgrowth
def leaf_patch(d: Design, cx: float, y: int, cz: float, r: float, seed: int, density: float = 0.7, rows: int = 1) -> set[Cell]:
    """A ragged patch of leaves on row y and up to `rows` above it (only into cells that hold no block): moss and creepers
    on a roof or a floor."""
    out: set[Cell] = set()
    for x, z in solids.disc(cx, cz, r):
        for k in range(rows):
            if h01(x, y + k, z, seed) < density * (1 - k / max(1, rows)):
                out |= put_free(d, [(x, y + k, z)], LEAVES)
    return out


def tree(d: Design, x: int, y0: int, z: int, height: int, crown: float, seed: int, keep: bool = True) -> None:
    """A rain-forest tree on row y0: a straight trunk (kept: the ruin pass never fells it) and a broad leafy crown."""
    d.fill(solids.box(x, y0, z, x, y0 + height - 1, z), LOG.with_(keep=keep))
    top = y0 + height
    for x2, y2, z2 in solids.ellipsoid(x + 0.5, top, z + 0.5, crown, crown * 0.45, crown):
        if h01(x2, y2, z2, seed) < 0.82:
            d.put(x2, y2, z2, LEAVES)


def scatter_rubble(d: Design, cells, seed: int, density: float = 0.08) -> set[Cell]:
    """Broken concrete and moss on floor cells (only into cells that hold no block): a slab or a quarter block here and
    there."""
    out: set[Cell] = set()
    for x, y, z in cells:
        v = h01(x, y, z, seed)
        if v < density:
            shape = sc.pack(sc.SLAB) if v < density * 0.6 else sc.pack(sc.QUARTER_CUBE)
            out |= put_free(d, [(x, y, z)], WALL_DARK if v < density * 0.8 else MOSS, shape=shape)
    return out


def carve_or_clear(d: Design, cells, ground_row: int) -> None:
    """Empties cells: below the ground row they are carved (CAVITY — else the planet's rock stays), above it cleared."""
    for x, y, z in cells:
        d.put(x, y, z, CAVITY if y < ground_row else CLEAR)
