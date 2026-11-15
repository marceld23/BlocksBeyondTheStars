"""The GRAND TECHNICIANS' sunken buildings (#3644): the templates of the civilization `grosstechniker` — the Vanished
People, whose machines science calls the Grand Technicians — written to data/civilizations/grosstechniker/<key>.json.

The Vanished People left buildings and technology of a dark sand-coloured stone with hieroglyph-like script. Their
machines were androids with feelings: a body of floating stone pieces around one eye, red from the red liquid that gave
them their energy. Today the remains lie on the sea floor of one ocean world, eyes white (extinguished), stones scattered.

    gt_giant_dwelling        giant  the dwelling of a giant Grand Technician: the giant white eye in its stone cup on a
                                    tank of red glass (a rest of red liquid, an AIR POCKET above it that a diver reaches
                                    from below through a tube in the pedestal), twelve big glyph cubes around it — several
                                    on their side or upside down, readable by their lid rim and socket ring — and a
                                    half-open hall whose back wall carries the mural three times; a rich chest in a
                                    hidden chamber under the hall
    gt_sunken_city_grid      city   a sunken town on a grid: houses with glyph bands, two avenues, a basin of red liquid on
                                    the central square, a temple with a white eye on its altar
    gt_sunken_city_ring      city   a sunken round town: round houses with domes in two rings, ring streets, a red pool
                                    in the middle with an eye on a pillar, four small basins, an outer wall
    gt_fallen_technician     small  a fallen Grand Technician: its eye half sunk into the sand at the end of a furrow, its
                                    body stones strewn along the way
    gt_red_basin             small  a basin of red liquid with a broken rim; the liquid seeps out through the gap
    gt_glyph_pillar          small  a glyph pillar on a stepped plinth, its broken top lying beside it
    gt_remains_resting ...   prop   four remains (propPool `remains`, about 2 x 2): a white eye with a few glyph pieces

Every template sits on the sea floor (`seat: seabed`, the lower rows sunk below the ground row), stands as a ruin
(`ruin: always`, profile masonry, ruin zones that spare the eye, the tank, the mural wall and the glyph cubes; those
cells are `keep` where they must survive), is open to players and has nobody home. Run:

    uv run --no-project python tools/landmarks/civ_grosstechniker.py [--preview] [--out FOLDER]
    uv run --no-project python tools/gen_landmarks.py civ_grosstechniker      # the collective call does the same
"""
from __future__ import annotations

import math
import sys

import civ
import solids
from civ import CAVITY, CLEAR, CivDesign, Mat, expect, orient, oriented_size, rgb, sc
from geom import Design

CIV = "grosstechniker"
COMMON, RICH = "civ_grosstechniker_common", "civ_grosstechniker_rich"
STONE = Mat("dark_sandstone")
GLYPH = Mat("glyph_stone")
BAND = Mat("glyph_stone_band")
EYE = Mat("gt_eye")
FLUID = Mat("gt_fluid")
RED_GLASS = Mat("glass", tint=rgb(196, 58, 48))
SAND = Mat("sand")

# The display names (en, de); the locale keys are civ.grosstechniker.site.<key> / civ.grosstechniker.prop.<key>.
NAMES: dict[str, tuple[str, str]] = {
    "gt_giant_dwelling": ("Dwelling of a Giant Grand Technician", "Wohnstatt eines Riesen-Großtechnikers"),
    "gt_sunken_city_grid": ("Sunken Glyph City", "Versunkene Schriftstadt"),
    "gt_sunken_city_ring": ("Sunken Ring City", "Versunkene Ringstadt"),
    "gt_fallen_technician": ("Fallen Grand Technician", "Gefallener Großtechniker"),
    "gt_red_basin": ("Broken Red Basin", "Zerbrochenes rotes Becken"),
    "gt_glyph_pillar": ("Broken Glyph Pillar", "Zerbrochene Schriftsäule"),
    "gt_remains_resting": ("Grand Technician Remains", "Überreste eines Großtechnikers"),
    "gt_remains_stacked": ("Grand Technician Remains", "Überreste eines Großtechnikers"),
    "gt_remains_scattered": ("Grand Technician Remains", "Überreste eines Großtechnikers"),
    "gt_remains_upturned": ("Grand Technician Remains", "Überreste eines Großtechnikers"),
}


def site(key: str, pool: str, ground: int, strength: float) -> CivDesign:
    """A ruined site on the sea floor."""
    return CivDesign(key, NAMES[key][0], civilization=CIV, pool=pool, seat="seabed", ruin="always",
                     ruinProfile="masonry", ruinStrength=strength, protection="open", groundRow=ground)


def ring_cells(cx: float, cz: float, y: int, r0: float, r1: float) -> set:
    """The cells of row y whose centre lies r0 < distance <= r1 from (cx, cz)."""
    return {(x, y, z) for x in range(math.floor(cx - r1) - 1, math.ceil(cx + r1) + 2)
            for z in range(math.floor(cz - r1) - 1, math.ceil(cz + r1) + 2)
            if r0 < math.hypot(x - cx, z - cz) <= r1}


# --- glyph cubes: big body stones, readable in any orientation --------------------------------------------------------
def glyph_cube(n: int) -> Design:
    """A big glyph-stone cube of side n, built upright: a band round its four sides at half height, a LID on top (a raised
    rim round the top face and a knob in its middle) and a SOCKET on its front (local -Z: a ring standing out of the face
    around a recess). Lying on its side or upside down, the rim, the knob and the socket show how it fell."""
    p = Design.part()
    p.box(0, 0, 0, n - 1, n - 1, n - 1, GLYPH)
    edge = [(x, z) for x in range(n) for z in range(n) if x in (0, n - 1) or z in (0, n - 1)]
    p.fill([(x, n // 2, z) for x, z in edge], BAND)
    p.fill([(x, n, z) for x, z in edge], STONE)                       # the lid rim
    c = (n - 1) / 2
    kr = 0.5 if n % 2 == 0 else (1 if n >= 9 else 0)
    p.fill([(x, n, z) for x in range(n) for z in range(n) if abs(x - c) <= kr and abs(z - c) <= kr], BAND)  # the knob
    r_out = max(2.0, n / 3)
    for x in range(n):
        for y in range(n):
            r = math.hypot(x - c, y - c)
            if r_out - 1.25 < r <= r_out:
                p.put(x, y, -1, STONE)                                 # the socket ring
            elif r <= r_out - 1.25:
                p.clear(x, y, 0)                                       # its recess
    return p


# Twelve cubes round the eye: (low corner x, z, side, orientation, yaw). Several lie on a side, upside down or on their back.
CUBES = [
    (8, 8, 10, "upright", 0),
    (24, 7, 7, "on_side_px", 1),
    (70, 5, 11, "upside_down", 0),
    (80, 25, 8, "on_back", 3),
    (6, 29, 9, "on_side_nx", 0),
    (78, 43, 12, "upright", 3),
    (8, 50, 6, "upside_down", 1),
    (84, 69, 7, "on_side_px", 2),
    (60, 7, 6, "upright", 2),
    (6, 72, 8, "on_back", 1),
    (4, 85, 9, "on_side_nx", 3),
    (80, 84, 10, "on_front", 0),
]


# --- the giant dwelling ------------------------------------------------------------------------------------------------
CX, CZ = 47.5, 41.5   # the eye's axis
G = 10                # the ground row: ten rows sunk


def dwelling_platform(d: CivDesign) -> None:
    """The platform (84 x 84, three rows, its top on the ground row) with a band round its edge, and the front avenue."""
    d.box(6, G - 2, 6, 89, G, 89, STONE)
    d.fill([(x, G, z) for x in range(6, 90) for z in range(6, 90) if x in (6, 89) or z in (6, 89)], BAND)
    rings = ring_cells(CX, CZ, G, 26.4, 27.6) | ring_cells(CX, CZ, G, 32.4, 33.6)
    d.fill({c for c in rings if d.get(*c) == ("block", "dark_sandstone", 0, 0, 0, False)}, GLYPH)  # script rings
    d.fill(solids.box(44, G, 6, 51, G, 20), GLYPH)                    # the avenue to the pit, paved with script
    d.fill(solids.box(43, G, 6, 43, G, 20) | solids.box(52, G, 6, 52, G, 20), BAND)
    for x, z, h in ((40, 9, 9), (54, 9, 7), (40, 15, 6)):             # avenue pillars, two standing, one shorter
        glyph_pillar(d, x, G + 1, z, h)
    glyph_pillar(d, 54, G + 1, 15, 3, cap=False)                      # broken: its top lies beside it
    fallen = Design.part()
    glyph_pillar(fallen, 0, 0, 0, 6)
    orient(d, fallen, (57, G, 15), "on_side_px", 0)                  # fallen away from its stump, capital last


def glyph_pillar(d: Design, x: int, y: int, z: int, h: int, cap: bool = True) -> None:
    """A 2 x 2 glyph pillar h rows high from row y, a band every third row, a 4 x 4 cap on top."""
    for k in range(h):
        d.fill(solids.box(x, y + k, z, x + 1, y + k, z + 1), BAND if k % 3 == 2 else GLYPH)
    if cap:
        d.fill(solids.box(x - 1, y + h, z - 1, x + 2, y + h, z + 2), STONE)


def dwelling_pit(d: CivDesign) -> None:
    """The round pit the eye stands in: five rings of steps down to its floor six rows below the ground, a band on every
    step's edge, a low band parapet round it with four openings."""
    for x in range(int(CX - 23), int(CX + 24)):
        for z in range(int(CZ - 23), int(CZ + 24)):
            r = math.hypot(x - CX, z - CZ)
            if r > 21:
                continue
            depth = min(6, 1 + int((21 - r) // 2))
            top = G - depth
            outer = 23 - 2 * depth if depth < 6 else 11
            d.put(x, top, z, BAND if r > outer - 1 else STONE)
            d.fill(solids.box(x, top + 1, z, x, G, z), CAVITY)
    d.fill({c for c in ring_cells(CX, CZ, G + 1, 21.3, 23) if abs(c[0] - CX) > 3 and abs(c[2] - CZ) > 3}, BAND)


def dwelling_tank(d: CivDesign) -> None:
    """The pedestal, the tank and the eye. The pedestal (a drum on footings down to row 0) is crossed by two tunnels on
    the pit floor; from their crossing a tube of red glass rises through the pedestal and the red liquid into the tank, so
    a diver swims up into the AIR POCKET above the liquid — a diving bell: glass round it, the lid on top, its only
    opening the tube below. On the lid the giant white eye rests in a stone cup whose rim frames it like an eyelid."""
    drum = {(x, z) for x, z in solids.disc(CX, CZ, 9)}
    for x, z in drum:
        r = math.hypot(x - CX, z - CZ)
        for y in range(G + 4):
            shell = r > 7.9 and y >= G - 6
            d.put(x, y, z, (BAND if y in (G - 2, G + 3) else GLYPH) if shell else STONE)
    d.fill(solids.box(46, G - 5, 32, 49, G - 3, 51) & {(x, y, z) for x, z in drum for y in range(G - 5, G - 2)}, CAVITY)
    d.fill(solids.box(38, G - 5, 40, 57, G - 3, 43) & {(x, y, z) for x, z in drum for y in range(G - 5, G - 2)}, CAVITY)
    tube = solids.box(46, G - 2, 40, 49, G + 6, 43) - solids.box(47, G - 2, 41, 48, G + 6, 42)
    bore = solids.box(47, G - 2, 41, 48, G + 6, 42)
    d.fill(tube, RED_GLASS, keep=True)
    d.fill({c for c in bore if c[1] <= G}, CAVITY)
    d.fill({c for c in bore if c[1] > G}, CLEAR)
    disc8 = solids.disc(CX, CZ, 8)
    d.keep({(x, G + 3, z) for x, z in disc8})                          # the tank's floor
    wall = solids.cylinder(CX, CZ, G + 4, G + 14, 8, wall=1)
    d.fill(wall, RED_GLASS, keep=True)
    inside = {(x, z) for x, z in disc8} - {(x, z) for x, _, z in wall}
    for x, z in inside:
        for y in range(G + 4, G + 7):
            if (x, y, z) not in tube and (x, y, z) not in bore:
                d.put(x, y, z, FLUID, keep=True)                       # the rest of the red liquid
        d.air((x, y, z) for y in range(G + 7, G + 15))                 # the air pocket
    d.fill({(x, G + 15, z) for x, z in disc8}, STONE, keep=True)       # the lid
    d.fill(ring_cells(CX, CZ, G + 15, 7.6, 9.2), BAND, keep=True)
    ey = G + 24.5
    eye = solids.sphere(CX, ey, CZ, 9)
    d.fill(eye, EYE, keep=True)
    cup = solids.sphere(CX, ey, CZ, 11) - solids.sphere(CX, ey, CZ, 9.2)
    back = {c for c in cup if c[2] >= CZ - 2 and c[1] >= G + 15}
    cradle = {c for c in cup if c[2] < CZ - 2 and G + 15 <= c[1] <= ey - 5}
    written = d.fill(back | cradle, GLYPH, keep=True, where="empty")
    d.restyle({c for c in written if (c in back and c[2] <= CZ - 1) or (c in cradle and c[1] >= ey - 6)},
              block="glyph_stone_band")                               # the rim that frames the eye


def dwelling_hall(d: CivDesign) -> None:
    """The half-open mural hall at the back: a dais, a back wall with the mural three times (kept), side walls, a
    colonnade toward the eye (its right end fallen), a roof over the back half only; under it a hidden chamber."""
    d.box(20, G + 1, 64, 75, G + 1, 88, STONE)
    d.fill(solids.box(20, G + 1, 64, 75, G + 1, 64), STONE, shape=sc.stairs(0, 1))
    back = solids.box(20, G + 2, 86, 75, G + 17, 88)
    d.fill(back, GLYPH, keep=True)
    d.restyle({c for c in back if c[1] in (G + 2, G + 9, G + 17)}, block="glyph_stone_band")
    for x0 in (20, 73):
        d.fill(solids.box(x0, G + 2, 76, x0 + 2, G + 17, 85), GLYPH)
        d.fill(solids.box(x0, G + 2, 72, x0 + 2, G + 12, 75), GLYPH)
        d.fill(solids.box(x0, G + 2, 72, x0 + 2, G + 2, 85) | solids.box(x0, G + 9, 72, x0 + 2, G + 9, 85), BAND)
    for x in (24, 31, 38, 56, 63):
        glyph_pillar(d, x, G + 2, 66, 13)
    glyph_pillar(d, 70, G + 2, 66, 5, cap=False)                      # broken; its top lies in front of the hall
    top = Design.part()
    glyph_pillar(top, 0, 0, 0, 7)
    orient(d, top, (69, G, 56), "on_front", 0)                        # fallen toward the eye, capital last
    d.fill(solids.box(22, G + 16, 65, 55, G + 17, 68), STONE)          # the architrave, its right end fallen
    d.fill(solids.box(22, G + 17, 65, 55, G + 17, 65), BAND)
    d.fill(solids.box(20, G + 18, 74, 75, G + 18, 88), STONE)          # the roof over the back half
    d.fill(solids.box(20, G + 18, 74, 75, G + 18, 74), BAND)
    for x0 in (30, 46, 62):                                            # the mural, three times, in framed niches
        d.mural(x0, G + 5, 86, (0, 0, -1))
        frame = solids.box(x0 - 1, G + 4, 85, x0 + 4, G + 7, 85) - solids.box(x0, G + 5, 85, x0 + 3, G + 6, 85)
        d.fill(frame, BAND, keep=True)
    d.fill(solids.box(44, G + 8, 85, 51, G + 8, 85), STONE, keep=True)  # a cornice over the middle one
    d.room(26, G - 5, 74, 33, G - 2, 83, floor=STONE, walls=STONE, ceiling=STONE)   # the hidden chamber
    d.fill(solids.box(26, G - 1, 82, 27, G, 83), CAVITY)               # its shaft from the hall's back corner
    d.fill(solids.box(26, G + 1, 82, 27, G + 1, 83), CLEAR)
    d.fill(solids.box(29, G - 4, 84, 30, G - 3, 84), BAND)             # a glyph tablet in its back wall


def dwelling_cubes(d: CivDesign) -> list[tuple[int, int, int, int, int, int]]:
    """The twelve glyph cubes, each on the platform (or beside it on the sea floor); returns their boxes."""
    boxes = []
    for x, z, n, orientation, yaw in CUBES:
        part = glyph_cube(n)
        sx, sy, sz = oriented_size(part, orientation, yaw)
        clash = [c for c in solids.box(x, G + 1, z, x + sx - 1, G + sy, z + sz - 1) if c in d.plain]
        assert not clash, f"cube at {x},{z} runs into {clash[:3]}"
        orient(d, part, (x, G + 1, z), orientation, yaw)
        boxes.append((x, G + 1, z, x + sx - 1, G + sy, z + sz - 1))
    return boxes


def sand_drifts(d: CivDesign, boxes, y: int, density: float, seed: int) -> None:
    """Sand slabs heaped against the foot of each box on row y."""
    foot = set()
    for x0, _, z0, x1, _, z1 in boxes:
        foot |= solids.box(x0 - 1, y, z0 - 1, x1 + 1, y, z1 + 1) - solids.box(x0, y, z0, x1, y, z1)
    d.scatter(foot, SAND.shaped(sc.pack(sc.SLAB)), density, seed=seed, where="empty")


def gt_giant_dwelling() -> CivDesign:
    d = site("gt_giant_dwelling", "giant", G, 0.5)
    dwelling_platform(d)
    dwelling_pit(d)
    dwelling_tank(d)
    dwelling_hall(d)
    boxes = dwelling_cubes(d)
    sand_drifts(d, boxes, G + 1, 0.45, seed=31)
    for x0, y0, z0, x1, y1, z1 in boxes:                               # the cubes keep their shape
        d.ruin_zone(x0, y0, z0, x1, y1, z1, "masonry", 0.12)
    d.ruin_zone(24, 0, 18, 71, G + 35, 65, "masonry", 0.2)             # the pit, the pedestal, the tank, the eye
    d.ruin_zone(20, G - 6, 59, 75, G + 18, 88, "masonry", 0.4)          # the hall
    d.chest(32, G - 5, 75, table=RICH)                                 # in the hidden chamber
    d.chest(40, G - 5, 42, table=COMMON)                               # in the tunnel under the tank
    d.chest(71, G + 2, 84, table=COMMON)                               # in the hall's corner
    d.chest(37, G + 1, 12, table=COMMON, chance=0.8)                   # by the avenue
    for x, y, z in ((24, G + 1, 27), (71, G + 1, 30), (17, G + 1, 62), (78, G + 1, 62), (36, G + 2, 78)):
        d.prop(x, y, z, pool="remains")
    return d


# --- the cities ----------------------------------------------------------------------------------------------------------
def house(d: CivDesign, x0: int, z0: int, w: int, ln: int, h: int, door: str, g: int, storey: bool = False) -> set:
    """A flat-roofed house of dark sandstone on the floor row g: walls from g + 1 to g + h, a glyph band as the top course
    and over the door, a 2 x 3 doorway on side `door` (-x, +x, -z, +z), small windows; `storey` adds a smaller upper
    floor. Returns the cells inside (the floor row above g) for chests."""
    x1, z1 = x0 + w - 1, z0 + ln - 1
    d.fill(solids.box(x0, g, z0, x1, g, z1), STONE)
    d.fill(solids.box_walls(x0, g + 1, z0, x1, g + h, z1), STONE)
    d.fill(solids.box_walls(x0, g + h, z0, x1, g + h, z1), BAND)
    d.fill(solids.box(x0, g + h + 1, z0, x1, g + h + 1, z1), STONE)
    mx, mz = (x0 + x1) // 2, (z0 + z1) // 2
    doors = {"-x": [(x0, mz), (x0, mz + 1)], "+x": [(x1, mz), (x1, mz + 1)],
             "-z": [(mx, z0), (mx + 1, z0)], "+z": [(mx, z1), (mx + 1, z1)]}
    for x, z in doors[door]:
        d.fill(solids.box(x, g + 1, z, x, g + 3, z), CLEAR)
        d.put(x, g + 4, z, BAND)
    for side, cells in doors.items():
        if side == door:
            continue
        for x, z in cells[:1]:
            d.fill(solids.box(x, g + 2, z, x, g + 3, z), CLEAR)        # a window
    if storey and w >= 7 and ln >= 7:
        d.fill(solids.box_walls(x0 + 1, g + h + 2, z0 + 1, x1 - 1, g + h + 4, z1 - 1), STONE)
        d.fill(solids.box_walls(x0 + 1, g + h + 4, z0 + 1, x1 - 1, g + h + 4, z1 - 1), BAND)
        d.fill(solids.box(x0 + 1, g + h + 5, z0 + 1, x1 - 1, g + h + 5, z1 - 1), STONE)
    return solids.box(x0 + 1, g + 1, z0 + 1, x1 - 1, g + 1, z1 - 1)


def basin(d: CivDesign, x0: int, z0: int, w: int, g: int, posts: bool = False) -> None:
    """A square basin w x w on the ground row g: a band rim one row high round red liquid two rows deep."""
    x1, z1 = x0 + w - 1, z0 + w - 1
    d.fill(solids.box(x0 + 1, g - 2, z0 + 1, x1 - 1, g - 2, z1 - 1), STONE)
    d.fill(solids.box(x0 + 1, g - 1, z0 + 1, x1 - 1, g, z1 - 1), FLUID)
    d.fill(solids.box_walls(x0, g - 1, z0, x1, g + 1, z1), BAND)
    if posts:
        for x, z in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
            d.fill(solids.box(x, g + 2, z, x, g + 3, z), GLYPH)


def gt_sunken_city_grid() -> CivDesign:
    """84 x 84: four by four blocks between two paved avenues and two lanes each way, the square with its basin where the
    avenues cross, a temple on the square's north-east corner, houses facing the streets, two more basins."""
    g = 3
    d = site("gt_sunken_city_grid", "city", g, 0.5)
    spans = [(2, 17), (21, 36), (47, 62), (66, 81)]                    # block spans along x and z
    for a in (18, 63):                                                  # the lanes
        d.fill(solids.box(a, g, 0, a + 2, g, 83) | solids.box(0, g, a, 83, g, a + 2), STONE)
    d.fill(solids.box(37, g, 0, 46, g, 83) | solids.box(0, g, 37, 83, g, 46), GLYPH)   # the avenues
    d.fill(solids.box(37, g, 0, 37, g, 83) | solids.box(46, g, 0, 46, g, 83)
           | solids.box(0, g, 37, 83, g, 37) | solids.box(0, g, 46, 83, g, 46), BAND)
    d.fill(solids.box(31, g, 31, 52, g, 52), STONE)                    # the square
    d.fill(solids.box_walls(31, g, 31, 52, g, 52), BAND)
    basin(d, 38, 38, 8, g, posts=True)
    temple(d, 47, 47, g)
    insides = []
    for i, (xa, _) in enumerate(spans):
        for j, (za, _) in enumerate(spans):
            if (i, j) == (2, 2):
                continue                                               # the temple's block
            plots = [(xa, za), (xa + 8, za), (xa, za + 8), (xa + 8, za + 8)]
            for k, (px, pz) in enumerate(plots):
                if px + 6 >= 31 and px <= 52 and pz + 6 >= 31 and pz <= 52:
                    continue                                           # the square
                if (i, j, k) in BASIN_PLOTS:
                    basin(d, px, pz, 7, g)                             # a courtyard with a basin
                    continue
                if solids.hash01(i, j, k, 3) < 0.14:
                    continue                                           # an empty plot
                h = 5 + int(solids.hash01(i, j, k, 7) * 4)
                insides.append(house(d, px, pz, 7, 7, h, street_side(px + 3, pz + 3), g,
                                     storey=solids.hash01(i, j, k, 5) < 0.3))
    for x, z in ((41, 8), (42, 28), (41, 60), (40, 76), (8, 42), (26, 41), (60, 42), (76, 41)):
        d.prop(x, g + 1, z, pool="remains")
    for n, cells in enumerate(insides[2::7][:3]):
        x, y, z = sorted(cells)[len(cells) // 2]
        d.chest(x, y, z, table=COMMON)
        assert n < 3
    d.ruin_zone(47, 0, 47, 63, g + 15, 63, "masonry", 0.25)
    return d


BASIN_PLOTS = {(1, 3, 1), (3, 1, 2), (0, 1, 3)}
STREETS = (19, 41.5, 64)  # the middle lines of the lanes and avenues of the grid city (along x and along z)


def street_side(cx: int, cz: int) -> str:
    """The side of a house centred on (cx, cz) that faces the nearest street."""
    dx = min(STREETS, key=lambda s: abs(s - cx))
    dz = min(STREETS, key=lambda s: abs(s - cz))
    if abs(dx - cx) <= abs(dz - cz):
        return "+x" if dx > cx else "-x"
    return "+z" if dz > cz else "-z"


def temple(d: CivDesign, x0: int, z0: int, g: int) -> None:
    """A stepped temple 16 x 16 on the square's corner: a two-step base, walls with glyph bands, a stepped roof; inside an
    altar with a white eye and a basin of red liquid in front of it; a rich chest behind the altar."""
    d.box(x0, g + 1, z0, x0 + 15, g + 1, z0 + 15, STONE)
    d.box(x0 + 1, g + 2, z0 + 1, x0 + 14, g + 2, z0 + 14, BAND)
    d.fill(solids.box_walls(x0 + 1, g + 3, z0 + 1, x0 + 14, g + 9, z0 + 14), GLYPH)
    d.fill(solids.box_walls(x0 + 1, g + 6, z0 + 1, x0 + 14, g + 6, z0 + 14), BAND)
    d.pyramid(x0 + 1, z0 + 1, g + 10, 14, STONE, step=1, rise=1, top=4, wall=1)
    d.fill(solids.box(x0 + 6, g + 3, z0 + 1, x0 + 9, g + 6, z0 + 1), CLEAR)        # the doorway, toward the square
    d.fill(solids.box(x0 + 1, g + 3, z0 + 6, x0 + 1, g + 6, z0 + 9), CLEAR)
    d.fill(solids.box(x0 + 6, g + 3, z0 + 11, x0 + 9, g + 3, z0 + 12), GLYPH)      # the altar
    d.fill(solids.box(x0 + 7, g + 4, z0 + 11, x0 + 8, g + 4, z0 + 11), EYE, keep=True)
    d.fill(solids.box(x0 + 6, g + 2, z0 + 6, x0 + 9, g + 2, z0 + 7), FLUID)        # a small basin before the altar
    d.chest(x0 + 7, g + 3, z0 + 13, table=RICH)


def round_house(d: CivDesign, cx: float, cz: float, r: float, h: int, g: int, toward: tuple[float, float]) -> set:
    """A round house: a wall ring from g + 1 to g + h with a band, a dome roof, a doorway toward `toward`, two windows.
    Returns the floor cells inside."""
    d.fill({(x, g, z) for x, z in solids.disc(cx, cz, r)}, STONE)
    wall = solids.cylinder(cx, cz, g + 1, g + h, r, wall=1)
    d.fill(wall, STONE)
    d.fill({c for c in wall if c[1] == g + h - 1}, BAND)
    d.dome(cx, cz, g + h + 1, r, STONE, ry=r * 0.8, wall=1)
    ang = math.atan2(toward[1] - cz, toward[0] - cx)
    for c in wall:
        a = math.atan2(c[2] - cz, c[0] - cx)
        off = abs((a - ang + math.pi) % (2 * math.pi) - math.pi)
        if off < 0.42 and c[1] <= g + 3:
            d.clear(*c)                                                # the doorway
        elif abs(off - math.pi / 2) < 0.2 and c[1] in (g + 3, g + 4):
            d.clear(*c)                                                # windows
    return {(x, g + 1, z) for x, z in solids.disc(cx, cz, r - 1.2)}


def gt_sunken_city_ring() -> CivDesign:
    """About 78 across: a red pool in the middle with a white eye on a pillar, a ring street, ten round houses, a second
    ring street, sixteen more houses, four radial streets with small basins, an outer wall with four gates."""
    g = 3
    d = site("gt_sunken_city_ring", "city", g, 0.5)
    c = 38.5
    for x, z in solids.disc(c, c, 28):
        r = math.hypot(x - c, z - c)
        if r <= 10 or 23.5 < r <= 28:
            d.put(x, g, z, STONE if r <= 10 else GLYPH)                 # the square and the outer ring street
        elif r <= 14.5:
            d.put(x, g, z, GLYPH)                                      # the inner ring street
    d.fill(ring_cells(c, c, g, 9, 10) | ring_cells(c, c, g, 13.5, 14.5) | ring_cells(c, c, g, 27, 28), BAND)
    for x in range(78):
        for z in range(78):
            r = math.hypot(x - c, z - c)
            if 10 < r <= 37 and (abs(x - c) <= 2.5 or abs(z - c) <= 2.5):
                d.put(x, g, z, GLYPH)                                  # the radial streets
    pool = solids.disc(c, c, 6)
    d.fill({(x, g - 2, z) for x, z in pool}, STONE)
    d.fill({(x, y, z) for x, z in pool for y in (g - 1, g)}, FLUID)
    d.fill(ring_cells(c, c, g + 1, 5.2, 6.6) | ring_cells(c, c, g, 5.2, 6.6), BAND)
    d.fill(solids.box(38, g - 1, 38, 39, g + 5, 39), GLYPH)            # the pillar in the pool ...
    d.fill(solids.box(38, g + 6, 38, 39, g + 7, 39), EYE, keep=True)    # ... with a white eye on top
    insides = []
    for k in range(8):                                                 # the inner ring of houses, off the radial streets
        a = math.radians(22.5 + 45 * k)
        hx, hz = c + 19 * math.cos(a), c + 19 * math.sin(a)
        insides.append(round_house(d, round(hx) + 0.5, round(hz) + 0.5, 3.6 + (k % 3) * 0.5, 5 + k % 3, g, (c, c)))
    for k in range(12):                                                # the outer ring
        a = math.radians(15 + 30 * k)
        hx, hz = c + 32.5 * math.cos(a), c + 32.5 * math.sin(a)
        r = 5.2 if k == 4 else 3.6 + (k % 2) * 0.8
        insides.append(round_house(d, round(hx) + 0.5, round(hz) + 0.5, r, 6 + (k % 4 == 1) * 2 + (k == 4), g, (c, c)))
    wall = ring_cells(c, c, g + 1, 36.5, 38)
    gate = {q for q in wall if abs(q[0] - c) <= 3 or abs(q[2] - c) <= 3}
    for y in (g + 1, g + 2, g + 3):
        d.fill({(x, y, z) for x, _, z in wall - gate}, BAND if y == g + 3 else STONE)
    d.fill({(x, g, z) for x, _, z in wall}, STONE)
    for x, z in ((36, 61), (36, 12), (61, 36), (12, 36)):               # small basins on the radial streets
        basin(d, x, z, 5, g)
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        d.prop(round(c + 26 * math.cos(a)), g + 1, round(c + 26 * math.sin(a)), pool="remains")
    for cells, table in ((insides[1], COMMON), (insides[6], COMMON), (insides[10], COMMON), (insides[12], RICH)):
        x, y, z = sorted(cells)[len(cells) // 2]
        d.chest(x, y, z, table=table)                                  # the rich one in the big house of the outer ring
    return d


# --- the small sites -----------------------------------------------------------------------------------------------------
def gt_fallen_technician() -> CivDesign:
    """30 x 17: a fallen Grand Technician, a big one. Its eye (five across) lies half sunk at the end of the furrow it
    ploughed through the sea floor, sand heaped up in front of it; the stones of its body, which once floated round the
    eye, lie strewn along both sides of the furrow — cubes, slabs, stair-cut pieces — and one was flung ahead."""
    g = 2
    d = site("gt_fallen_technician", "small", g, 0.25)
    for x in range(2, 20):                                             # the furrow, deeper and wider toward the eye
        w = 1 + (x > 8) + (x > 14)
        for z in range(9 - w, 10 + w):
            d.put(x, g, z, CAVITY)
            if x > 11 and abs(z - 9) <= w - 1:
                d.put(x, g - 1, z, CAVITY)
    for x in range(23, 28):                                            # sand heaped up in front of the eye
        for z in range(4, 15):
            k = abs(z - 9) + (x - 23)
            if k <= 3:
                d.put(x, g + 1, z, SAND)
            elif k <= 5:
                d.put(x, g + 1, z, SAND.shaped(sc.pack(sc.SLAB)))
    d.fill(solids.sphere(21, g + 1, 9, 2.5), EYE, keep=True)           # the eye, its lower rows in the ground
    pieces = [  # (x, y, z, size x, y, z): the body stones
        (3, g + 1, 5, 2, 1, 2), (5, g + 1, 13, 2, 2, 2), (9, g + 1, 3, 3, 2, 2), (13, g + 1, 4, 2, 3, 2),
        (15, g + 1, 13, 3, 2, 2), (19, g + 1, 14, 2, 1, 1), (26, g + 1, 2, 2, 2, 2),
    ]
    for x, y, z, sx, sy, sz in pieces:
        d.fill(solids.box(x, y, z, x + sx - 1, y + sy - 1, z + sz - 1), GLYPH)
        d.fill(solids.box(x, y + sy - 1, z, x + sx - 1, y + sy - 1, z), BAND)
    for x, y, z, m in ((10, g + 3, 3, GLYPH.shaped(sc.pack(sc.SLAB))), (8, g + 1, 14, BAND.shaped(sc.stairs(1, 0))),
                       (12, g + 1, 15, GLYPH.shaped(sc.pack(sc.QUARTER_CUBE))), (18, g + 1, 4, GLYPH.shaped(sc.pack(sc.SLAB))),
                       (14, g + 4, 4, BAND.shaped(sc.pack(sc.QUARTER_CUBE, 1))), (1, g + 1, 10, GLYPH.shaped(sc.stairs(0, 1))),
                       (24, g + 1, 15, GLYPH), (24, g + 2, 15, GLYPH.shaped(sc.pack(sc.SLAB)))):
        d.put(x, y, z, m)
    d.chest(4, g + 1, 4, table=COMMON, hidden=True)                    # under a stone, beside the first piece
    d.prop(10, g + 1, 16, pool="remains")                              # a small one that fell with it
    return d


def gt_red_basin() -> CivDesign:
    """22 x 22: a square basin of red liquid, its rim broken on one side: rim stones lie scattered beside the gap and a
    tongue of red liquid seeps out over the sea floor. A short glyph post stands at one corner."""
    g = 3
    d = site("gt_red_basin", "small", g, 0.3)
    x0, z0, w = 4, 4, 13
    x1, z1 = x0 + w - 1, z0 + w - 1
    d.fill(solids.box(x0 + 2, g - 3, z0 + 2, x1 - 2, g - 3, z1 - 2), STONE)
    d.fill(solids.box(x0 + 2, g - 2, z0 + 2, x1 - 2, g, z1 - 2), FLUID)
    rim = solids.box_walls(x0, g - 2, z0, x1, g + 2, z1, wall=2)
    d.fill(rim, GLYPH)
    d.fill({c for c in rim if c[1] == g + 2}, BAND)
    gap = solids.box(x1 - 1, g - 1, z0 + 5, x1, g + 2, z0 + 8)          # the broken side
    d.fill(gap, CLEAR)
    d.fill({c for c in gap if c[1] <= g}, CAVITY)
    d.fill(solids.box(x1 - 1, g - 1, z0 + 5, x1, g, z0 + 8), FLUID)      # the liquid runs out through the gap ...
    for x in range(x1 + 1, x1 + 6):                                    # ... and over the sea floor
        for z in range(z0 + 5 - (x - x1) // 2, z0 + 9 + (x - x1) // 3):
            if solids.hash01(x, 0, z, 11) < 0.85 - (x - x1) * 0.1:
                d.put(x, g, z, FLUID)
    for x, y, z, m in ((x1 + 2, g + 1, z0 + 3, GLYPH), (x1 + 4, g + 1, z0 + 11, BAND),
                       (x1 + 1, g + 1, z0 + 10, GLYPH.shaped(sc.pack(sc.SLAB))),
                       (x1 + 3, g + 1, z0 + 2, GLYPH.shaped(sc.stairs(0, -1))),
                       (x1 + 2, g + 2, z0 + 3, BAND.shaped(sc.pack(sc.QUARTER_CUBE, 2)))):
        d.put(x, y, z, m)
    d.fill(solids.box(x1 - 1, g + 2, z0 + 4, x1, g + 2, z0 + 4), BAND.shaped(sc.stairs(0, -1)))   # cracked edges
    d.fill(solids.box(x1 - 1, g + 2, z0 + 9, x1, g + 2, z0 + 9), BAND.shaped(sc.stairs(0, 1)))
    for k in range(4):                                                  # the corner post
        d.fill(solids.box(x0, g + 3 + k, z0, x0 + 1, g + 3 + k, z0 + 1), BAND if k == 3 else GLYPH)
    d.chest(x0 + 3, g + 1, z1 + 2, table=COMMON)
    d.prop(x0 + 4, g + 1, z1 + 4, pool="remains")
    return d


def gt_glyph_pillar() -> CivDesign:
    """20 x 18: a glyph pillar (3 x 3) on a stepped plinth, broken eleven rows up; its upper part lies beside the plinth
    with its bands standing upright now, a few chips around."""
    g = 2
    d = site("gt_glyph_pillar", "small", g, 0.2)
    for k, s in enumerate((0, 1, 2)):
        d.fill(solids.box(1 + s, g + k, 1 + s, 11 - s, g + k, 11 - s), STONE if k < 2 else BAND)
    d.fill(solids.box(1, g - 2, 1, 11, g - 1, 11), STONE)               # the plinth's sunk footing
    for k in range(11):
        d.fill(solids.box(5, g + 3 + k, 5, 7, g + 3 + k, 7), BAND if k % 3 == 1 else GLYPH)
    for x, y, z, m in ((5, g + 14, 5, GLYPH.shaped(sc.stairs(1, 0))), (6, g + 14, 5, GLYPH.shaped(sc.pack(sc.SLAB))),
                       (7, g + 14, 7, GLYPH.shaped(sc.pack(sc.QUARTER_CUBE))), (5, g + 14, 7, GLYPH)):
        d.put(x, y, z, m)                                              # the broken top
    top = Design.part()
    for k in range(7):
        top.fill(solids.box(0, k, 0, 2, k, 2), BAND if k % 3 == 1 else GLYPH)
    top.fill(solids.box(-1, 7, -1, 3, 7, 3), STONE)                    # its capital
    orient(d, top, (12, g, 4), "on_side_px", 0)                       # the capital digs into the ground
    for x, z in ((14, 13), (17, 2), (10, 15)):
        d.put(x, g + 1, z, GLYPH.shaped(sc.pack(sc.SLAB)))
    d.chest(2, g + 1, 14, table=COMMON)
    d.prop(16, g + 1, 15, pool="remains")
    return d


# --- the remains (props) -------------------------------------------------------------------------------------------------
def remains(key: str) -> CivDesign:
    """A prop of the pool `remains`: one Grand Technician's remains (it was about 2 x 2 blocks): a white eye and a few
    glyph pieces. Placed by `prop pool=remains` markers after the ruin pass, so it is never ruined itself."""
    return CivDesign(key, NAMES[key][0], civilization=CIV, pool="prop", propPool="remains", seat="seabed",
                     ruin="never", protection="open", groundRow=0)


def gt_remains() -> list[CivDesign]:
    slab, quarter = sc.pack(sc.SLAB), sc.pack(sc.QUARTER_CUBE)
    eye_front = civ.front((0, 0, -1))
    a = remains("gt_remains_resting")                                  # the eye resting among three pieces
    a.put(0, 0, 0, EYE.shaped(eye_front))
    a.put(1, 0, 0, GLYPH.shaped(slab))
    a.put(1, 0, 1, GLYPH)
    a.put(1, 1, 1, BAND.shaped(slab))
    a.put(0, 0, 1, GLYPH.shaped(quarter))
    b = remains("gt_remains_stacked")                                  # the eye still on its last stone
    b.put(0, 0, 0, GLYPH)
    b.put(0, 1, 0, EYE.shaped(civ.front((1, 0, 0))))
    b.put(1, 0, 0, BAND.shaped(sc.stairs(0, 1)))
    b.put(1, 0, 1, GLYPH.shaped(quarter))
    b.put(0, 0, 1, GLYPH.shaped(slab))
    b.put(0, 2, 0, GLYPH.shaped(sc.pack(sc.QUARTER_CUBE, 2)))
    c = remains("gt_remains_scattered")                                # pieces rolled apart, the eye between them
    c.put(1, 0, 1, EYE.shaped(civ.front((0, 0, 1))))
    c.put(0, 0, 0, GLYPH)
    c.put(0, 1, 0, GLYPH.shaped(slab))
    c.put(1, 0, 0, BAND.shaped(sc.ramp(-1, 0)))
    c.put(0, 0, 1, GLYPH.shaped(sc.pack(sc.STAIRS, 0, sc.UP_PX)))       # a stair-cut piece lying on its side
    c.put(2, 0, 2, GLYPH.shaped(quarter))
    d = remains("gt_remains_upturned")                                 # an upturned piece, the eye looking up beside it
    d.put(0, 0, 0, GLYPH.shaped(sc.pack(sc.STAIRS, 1, sc.UP_NY)))       # upside down: the steps hang underneath
    d.put(0, 1, 0, BAND.shaped(slab))
    d.put(1, 0, 0, EYE.shaped(civ.front((-1, 0, 0))))
    d.put(1, 0, 1, GLYPH.shaped(slab))
    d.put(0, 0, 1, GLYPH)
    d.put(0, 1, 1, GLYPH)
    d.put(0, 2, 1, BAND.shaped(quarter))
    return [a, b, c, d]


# --- the collective call -------------------------------------------------------------------------------------------------
CIV_BLOCKS = ("glyph_stone", "glyph_stone_band", "dark_sandstone", "gt_eye", "gt_fluid")


def build() -> list[CivDesign]:
    """Every Grand Technicians template, each checked against its size and content."""
    giant = gt_giant_dwelling()
    print("  " + expect(giant, (88, 96), (30, 40), (8, 12), CIV_BLOCKS + tuple(f"gt_mural_{i}" for i in range(8)),
                        {"chest": (3, 6), "prop": (3, 8)}))
    assert giant.air_cells(), "the giant dwelling needs its air pocket"
    out = [giant]
    for city in (gt_sunken_city_grid(), gt_sunken_city_ring()):
        print("  " + expect(city, (64, 96), (8, 17), (2, 4), CIV_BLOCKS, {"chest": (2, 5), "prop": (4, 10)}))
        out.append(city)
    for small in (gt_fallen_technician(), gt_red_basin(), gt_glyph_pillar()):
        print("  " + expect(small, (16, 32), (2, 16), (2, 4), ("glyph_stone", "gt_eye") if "fallen" in small.header["key"]
                            else ("glyph_stone",), {"chest": (1, 2), "prop": (0, 2)}))
        out.append(small)
    for prop in gt_remains():
        print("  " + expect(prop, (2, 3), (1, 2), (0, 0), ("gt_eye", "glyph_stone"), {}))
        b = prop.bounds()
        assert b is not None and b[3] <= 2 and b[5] <= 2 and b[4] <= 2, f"{prop.header['key']}: larger than 3 x 3 x 3"
        out.append(prop)
    keys = [d.header["key"] for d in out]
    assert len(set(keys)) == len(keys) and all(k.startswith("gt_") for k in keys)
    return out


def main(argv: list[str]) -> int:
    import argparse

    import preview
    ap = argparse.ArgumentParser(description="Build the Grand Technicians' templates.")
    ap.add_argument("--out", help="folder (default data/civilizations/grosstechniker)")
    ap.add_argument("--preview", action="store_true", help="also render artifacts/civ-previews/<key>.png")
    args = ap.parse_args(argv)
    civ.seed_preview_colours(preview)
    for d in build():
        path = d.write(args.out)
        if args.preview:
            cuts = PREVIEW_CUTS.get(d.header["key"], ())
            print(f"  preview -> {preview.preview(path, None, cuts=cuts)}")
    return 0


PREVIEW_CUTS = {
    "gt_giant_dwelling": ("y=17", "z=42", "x=47", "z=80"),
    "gt_sunken_city_grid": ("y=6",),
    "gt_sunken_city_ring": ("y=6",),
}


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
