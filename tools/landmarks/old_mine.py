"""The old mine: a timber headframe with its sheave wheels over a shaft house, an engine house with a winding drum and a
chimney, a yard with a track to the spoil heap — and below, 120 rows deep, a ladder shaft to three levels of timbered
tunnels with mine tracks, carts and lamps, three big caverns full of stalagmites and stalactites (one with an underground
pool, one where wild animals have their den), ore stopes with chests, and a long decline between the two lower levels.

The ground row (111 rows above the flooded sump) lies on the ground: the surface part stands on it, everything below is
carved into the planet's own rock (an empty cell keeps the rock, the tunnels and caves are cavity cells). Profile `underground`: a ruin's tunnels cave in here
and there; the ladder is kept, so the way down stays, though a cave-in may have to be dug through.
"""
import itertools

import shapecode as sc
import solids
from geom import CAVITY, CLEAR, Design, Mat, Mix

G = 120                            # the ground row
SX, SZ = 78, 78                    # the shaft's low corner (4 x 4: x 78..81, z 78..81)
LEVELS = (G - 30, G - 64, G - 108)  # the walk rows of the three levels (90, 56, 12) before normalize()
LADDER = (79, 81)                  # the ladder column (x, z) inside the shaft, against its +z wall

LOG = Mat("wood_log")
PLANK = Mat("wood_planks")
TRACK = Mat("mine_track")
CART = Mat("mine_cart")
STONE = Mat("stone")
ROOF = Mat("wood_planks_mahogany")


def box(d: Design, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m) -> set:
    """A box with its corners in any order."""
    return d.fill(solids.box(x0, y0, z0, x1, y1, z1), m)


# --- underground -------------------------------------------------------------------------------------------------------
def drift(d: Design, points: list[tuple[int, int]], y: int, lamps: bool = True, track: bool = True) -> set:
    """A timbered tunnel through (x, z) points on walk row y (consecutive points share x or z): carved five wide and four
    high, a frame every four blocks (two log posts at the sides, a log cap across the ceiling — three blocks stay free
    between the posts), a track set into the floor down the middle, a lantern under every third cap and a torch in a
    wall niche between the frames."""
    carved: set = set()
    n = 0
    for (ax, az), (bx, bz) in itertools.pairwise(points):
        along_x = az == bz
        step = 1 if (bx - ax if along_x else bz - az) > 0 else -1
        length = abs(bx - ax if along_x else bz - az)
        for i in range(0 if n == 0 else 1, length + 1):
            x, z = (ax + i * step, az) if along_x else (ax, az + i * step)
            cross = [(x, z + o) if along_x else (x + o, z) for o in range(-2, 3)]
            carved |= d.fill([(cx, y + h, cz) for cx, cz in cross for h in range(4)], CAVITY)
            if track:
                d.put(x, y - 1, z, TRACK)
            if n % 4 == 0:
                for cx, cz in (cross[0], cross[-1]):
                    d.fill([(cx, y + h, cz) for h in range(3)], LOG)
                d.fill([(cx, y + 3, cz) for cx, cz in cross], LOG.shaped(sc.beam("z" if along_x else "x")))
                if lamps and n % 12 == 0:
                    d.put(x, y + 2, z, "lantern")
            elif lamps and n % 8 == 2:
                wx, wz = (x, z + 3) if along_x else (x + 3, z)
                d.put(wx, y + 1, wz, "torch")
            n += 1
    return carved


def station(d: Design, y: int) -> None:
    """The room round the shaft at a level: 16 x 16, five high, posts at the corners, a railing round the shaft's hole
    (open at the ladder), lanterns, a cart."""
    box(d, SX - 6, y, SZ - 6, SX + 9, y + 4, SZ + 9, CAVITY)
    for x, z in ((SX - 6, SZ - 6), (SX + 9, SZ - 6), (SX - 6, SZ + 9), (SX + 9, SZ + 9), (SX - 1, SZ - 6), (SX + 4, SZ - 6),
                 (SX - 1, SZ + 9), (SX + 4, SZ + 9)):
        d.fill([(x, yy, z) for yy in range(y, y + 4)], LOG)
    for x in range(SX - 6, SX + 10):
        d.put(x, y + 4, SZ - 6, LOG.shaped(sc.beam("x")))
        d.put(x, y + 4, SZ + 9, LOG.shaped(sc.beam("x")))
    rail = [(x, y, z) for x in range(SX - 1, SX + 5) for z in range(SZ - 1, SZ + 5)
            if (x in (SX - 1, SX + 4) or z in (SZ - 1, SZ + 4)) and not (z == SZ + 4 and LADDER[0] - 1 <= x <= LADDER[0] + 1)]
    d.fill(rail, PLANK.shaped(sc.pack(sc.FENCE)))
    for x, z in ((SX - 5, SZ - 5), (SX + 8, SZ + 8), (SX - 5, SZ + 8), (SX + 8, SZ - 5)):
        d.put(x, y + 3, z, "lantern")
    d.put(SX + 7, y, SZ - 4, CART)


def shaft(d: Design) -> None:
    """The shaft from the flooded sump under the deepest level up to the shaft house, a timber ring set into the rock
    every four rows."""
    bottom = LEVELS[-1] - 3
    box(d, SX, bottom, SZ, SX + 3, G, SZ + 3, CAVITY)
    for y in range(bottom + 2, G - 1, 4):
        ring = [(x, y, z) for x in range(SX - 1, SX + 5) for z in range(SZ - 1, SZ + 5)
                if x in (SX - 1, SX + 4) or z in (SZ - 1, SZ + 4)]
        d.fill(ring, PLANK, where="empty")
    box(d, SX, bottom, SZ, SX + 3, bottom, SZ + 3, Mat("water"))


def ladder(d: Design) -> None:
    """The ladder up the shaft's +z wall from the sump to the shaft house — kept, so a ruin keeps its way down (drawn
    last: the stations' rooms would carve it away)."""
    for y in range(LEVELS[-1] - 2, G + 2):
        d.put(LADDER[0], y, LADDER[1], Mat("ladder", keep=True))


def dripstone_cave(d: Design, cx: int, cy: int, cz: int, r: tuple[int, int, int], floor: int, seed: int,
                   path: list[tuple[int, int]], wants: list[tuple[int, int]]) -> tuple[set, list]:
    """A big cavern with a flat floor on walk row `floor` and a walkway carved along `path` (the cave's floor is narrower
    than the cave, so the tunnel's end would otherwise meet a rock step); the floor cells nearest the wanted (x, z) spots are picked
    (for chests, a den, a lantern), then stalagmites and stalactites everywhere but over the path and those spots, and a
    few tall dripstone columns. Returns the cave's cells and the spots."""
    cave = d.cavern(cx, cy, cz, *r, roughness=0.3, seed=seed, scale=7.0, floor=floor)
    cave |= d.fill([(x, y, z) for (x, z) in path for y in range(floor, floor + 4)], CAVITY)  # the walk in, on the floor
    ground = sorted((x, y, z) for (x, y, z) in cave if y == floor and (x, z) not in set(path))
    spots = [min(ground, key=lambda p, w=w: (p[0] - w[0]) ** 2 + (p[2] - w[1]) ** 2) for w in wants]
    free = set(path) | {(x + dx, z + dz) for (x, _, z) in spots for dx in (-1, 0, 1) for dz in (-1, 0, 1)}
    clear = {(x, y, z) for (x, y, z) in cave if (x, z) in free}
    d.dripstones(cave, "dripstone", density=0.07, max_len=6, seed=seed, keep_clear=clear)
    big = [(x, y, z) for (x, y, z) in solids.lowest_cells(cave) if solids.hash01(x, y, z, seed + 3) < 0.012
           and all((x + dx, z + dz) not in free for dx in (-1, 0, 1) for dz in (-1, 0, 1))]
    for x, y, z in big:                                                           # a few tall columns of dripstone
        h = sum(1 for k in range(30) if (x, y + k, z) in cave)
        if h > 6:
            d.fill([(x, y + k, z) for k in range(h)], Mat("dripstone"))
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                d.dripstone(x + dx, y, z + dz, max(1, h // 3))
                d.dripstone(x + dx, y + h - 1, z + dz, max(1, h // 4), hanging=True)
    return cave, spots


def path_cells(points: list[tuple[int, int]], width: int = 2) -> list[tuple[int, int]]:
    """The (x, z) cells within `width` of a polyline of axis-aligned points (a walkway through a cave)."""
    out = set()
    for (ax, az), (bx, bz) in itertools.pairwise(points):
        for x in range(min(ax, bx) - width, max(ax, bx) + width + 1):
            for z in range(min(az, bz) - width, max(az, bz) + width + 1):
                out.add((x, z))
    return sorted(out)


def level_one(d: Design) -> None:
    """Level 1 (row 90): east to the first cavern with the den, west to an ore stope, south to a store room."""
    y = LEVELS[0]
    station(d, y)
    drift(d, [(SX + 10, 80), (126, 80)], y)
    path = [(126, 80), (148, 80)]
    _, (den, chest, lamp) = dripstone_cave(d, 140, y + 7, 80, (15, 10, 18), y, 11, path_cells(path),
                                           [(144, 70), (150, 88), (134, 90)])
    d.den(*den, species="native", count=3, radius=10)
    d.chest(*chest, table="mine_ore")
    d.put(*lamp, "lantern")
    drift(d, [(SX - 7, 80), (26, 80), (26, 120)], y)
    stope(d, 18, y, 121, 34, 131, seed=1)
    drift(d, [(52, 80), (52, 40)], y)
    box(d, 46, y, 30, 58, y + 4, 39, CAVITY)                                      # the store room
    d.fill([(x, y + 4, z) for x in range(46, 59) for z in (30, 39)], LOG.shaped(sc.beam("x")))
    for x, z in ((47, 31), (48, 31), (47, 32)):
        d.put(x, y, z, Mat("wood_crate"))
    d.chest(57, y, 38, table="mine_ore", chance=0.8)
    d.put(52, y + 3, 34, "lantern")


def stope(d: Design, x0: int, y: int, z0: int, x1: int, z1: int, seed: int) -> None:
    """An ore stope: a wide room with ore showing in its walls and ceiling, timber props, a chest and a lantern."""
    box(d, x0, y, z0, x1, y + 5, z1, CAVITY)
    walls = [(x, yy, z) for x in range(x0 - 1, x1 + 2) for yy in range(y, y + 7) for z in range(z0 - 1, z1 + 2)
             if (x in (x0 - 1, x1 + 1) or z in (z0 - 1, z1 + 1) or yy == y + 6)]
    ores = Mix(("iron_ore", 4), ("copper_ore", 3), ("nickel_ore", 1), ("gold_ore", 1), seed=seed)
    d.scatter(walls, ores, 0.18, seed=seed + 5, where="empty")
    for x in range(x0 + 3, x1 - 1, 5):
        d.fill([(x, yy, (z0 + z1) // 2) for yy in range(y, y + 6)], LOG)
    d.chest(x1, y, z1, table="mine_ore")
    d.put(x0, y + 5, z0, "lantern")
    d.put(x0 + 2, y, z1, CART)


def level_two(d: Design) -> None:
    """Level 2 (row 58): north to the great cavern with its pool, south to a stope, east the decline to level 3."""
    y = LEVELS[1]
    station(d, y)
    drift(d, [(80, SZ + 10), (80, 122)], y)
    path = [(80, 122), (80, 144)]
    pool_spots = [(x, z) for x, z in solids.disc(92, 142, 8.5)]
    cave, (a, b, drip) = dripstone_cave(d, 80, y + 9, 137, (22, 12, 16), y, 22, path_cells(path, 3) + pool_spots,
                                        [(66, 150), (98, 128), (86, 136)])
    pool = solids.disc(92, 142, 6.5)
    for x, z in pool:                                                             # the pool: three deep in the middle
        depth = 3 if (x - 92) ** 2 + (z - 142) ** 2 < 16 else 1
        for yy in range(y - depth, y):
            d.put(x, yy, z, Mat("water"))
        d.fill([(x, yy, z) for yy in range(y, y + 3)], CAVITY)
    for x, z in solids.ring2d(92, 142, 7.5, 1):
        if (x, y, z) in cave:
            d.put(x, y - 1, z, Mat("dripstone"))
    d.sound(*drip, "amb_mine_drip", radius=56, volume=0.7)
    d.chest(*a, table="mine_ore")
    d.chest(*b, table="mine_ore", chance=0.6)
    drift(d, [(80, SZ - 7), (80, 30)], y)
    stope(d, 72, y, 18, 90, 29, seed=2)
    decline(d)


def decline(d: Design) -> None:
    """The decline: a timbered stair tunnel three wide from level 2's station east down to level 3."""
    top, bottom = LEVELS[1], LEVELS[2]
    steps = top - bottom - 1
    for k in range(steps):
        x = SX + 10 + k
        tread = top - 2 - k
        for z in range(84, 87):
            d.put(x, tread, z, Mat("stone", shape=sc.stairs(-1, 0), keep=True))
            d.fill([(x, tread + h, z) for h in range(1, 5)], CAVITY)
        if k % 4 == 0:
            d.fill([(x, tread + h, z) for z in (83, 87) for h in range(1, 4)], LOG)
            d.fill([(x, tread + 4, z) for z in range(83, 88)], LOG.shaped(sc.beam("z")))
        if k % 8 == 6:
            d.put(x, tread + 2, 88, "torch")
    box(d, SX + 10 + steps, bottom, 83, SX + 10 + steps + 4, bottom + 3, 87, CAVITY)  # the landing below


def level_three(d: Design) -> None:
    """Level 3 (row 22): west to the deepest cavern, east to the decline's foot and on north to the gold vein."""
    y = LEVELS[2]
    station(d, y)
    drift(d, [(SX - 7, 80), (40, 80)], y)
    path = [(40, 80), (10, 80)]
    _, (drip, chest) = dripstone_cave(d, 22, y + 9, 80, (16, 12, 21), y, 33, path_cells(path), [(24, 74), (12, 68)])
    d.sound(*drip, "amb_mine_drip", radius=44, volume=0.55)
    d.chest(*chest, table="mine_ore")
    drift(d, [(SX + 10, 80), (136, 80), (136, 86)], y, track=True)
    drift(d, [(112, 80), (112, 130)], y)
    box(d, 104, y, 131, 121, y + 6, 145, CAVITY)                                  # the gold vein
    walls = [(x, yy, z) for x in range(103, 123) for yy in range(y, y + 8) for z in range(130, 147)
             if x in (103, 122) or z in (130, 146) or yy == y + 7]
    d.scatter(walls, Mix(("gold_ore", 3), ("diamond_ore", 1), ("iron_ore", 2), seed=9), 0.22, seed=19, where="empty")
    d.chest(118, y, 143, table="ruin_rich")
    d.chest(106, y, 143, items="gold_ore:4-9,diamond_ore:1-2")
    d.put(112, y + 5, 138, "lantern")
    d.put(108, y, 133, CART)


# --- above ground ------------------------------------------------------------------------------------------------------
def yard(d: Design) -> None:
    """The yard: a gravel ground row, the track from the shaft house to the spoil heap, two carts, lamp posts."""
    d.fill([(x, G, z) for x in range(56, 114) for z in range(50, 98)], Mix(("scree", 3), ("dirt", 2), ("stone", 1), seed=41))
    track = [(79, G, z) for z in range(52, 72)] + [(x, G, 52) for x in range(62, 80)]
    d.fill(track, TRACK)
    for x, z in ((79, 60), (70, 52)):
        d.put(x, G + 1, z, CART)
    d.cone(60, 46, G + 1, G + 7, 8, Mix(("scree", 3), ("stone", 2), ("dirt", 1), seed=42))
    for x, z in ((70, 70), (90, 66), (66, 58), (110, 94)):
        d.fill([(x, G + k, z) for k in range(1, 5)], LOG.shaped(sc.post()))
        d.put(x, G + 5, z, "lantern")


def shaft_house(d: Design) -> None:
    """The shaft house over the collar: plank walls on a log frame, a door toward the yard, windows, a railing round
    the hole, a chest."""
    x0, z0, x1, z1 = 72, 72, 87, 87
    box(d, x0, G, z0, x1, G, z1, PLANK)                                           # the floor
    box(d, SX, G, SZ, SX + 3, G, SZ + 3, CLEAR)                                   # the collar
    for y in range(G + 1, G + 7):
        d.fill([(x, y, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1) if x in (x0, x1) or z in (z0, z1)],
               LOG if y == G + 6 else PLANK)
    for x, z in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        d.fill([(x, y, z) for y in range(G + 1, G + 7)], LOG)
    box(d, 79, G + 1, z0, 80, G + 3, z0, CLEAR)
    d.door(79, G + 1, z0, "hinge")
    for x in (74, 75, 84, 85):
        box(d, x, G + 3, z0, x, G + 4, z0, Mat("glass"))
        box(d, x, G + 3, z1, x, G + 4, z1, Mat("glass"))
    for z in (76, 77, 84, 85):
        box(d, x0, G + 3, z, x0, G + 4, z, Mat("glass"))
        box(d, x1, G + 3, z, x1, G + 4, z, Mat("glass"))
    roof(d, x0 - 1, z0 - 1, x1 + 1, z1 + 1, G + 7)
    rail = [(x, G + 1, z) for x in range(SX - 1, SX + 5) for z in range(SZ - 1, SZ + 5)
            if (x in (SX - 1, SX + 4) or z in (SZ - 1, SZ + 4)) and not (z == SZ + 4 and 78 <= x <= 80)]
    d.fill(rail, PLANK.shaped(sc.pack(sc.FENCE)))
    d.chest(85, G + 1, 86, table="mine_ore")
    d.put(74, G + 5, 86, "lantern")
    d.put(85, G + 5, 75, "lantern")


def roof(d: Design, x0: int, z0: int, x1: int, z1: int, y0: int, m=None) -> None:
    """A gable roof along x: each course one in on both z sides, the eaves as ramps, a ridge of slabs on top."""
    m = ROOF if m is None else m
    k = 0
    while z0 + k < z1 - k:
        y = y0 + k
        for x in range(x0, x1 + 1):
            d.put(x, y, z0 + k, m, shape=sc.ramp(0, 1))
            d.put(x, y, z1 - k, m, shape=sc.ramp(0, -1))
        d.fill([(x0, y, z) for z in range(z0 + k + 1, z1 - k)] + [(x1, y, z) for z in range(z0 + k + 1, z1 - k)], PLANK)
        k += 1
    if z0 + k == z1 - k:
        d.fill([(x, y0 + k, z0 + k) for x in range(x0, x1 + 1)], m, shape=sc.pack(sc.SLAB))


def headframe(d: Design) -> None:
    """The headframe: four log legs leaning in from the shaft house's corners to a platform 26 up, braced on every face,
    two sheave wheels on top, a back strut down to the engine house and the hoist rope."""
    cx = cz = 79.5
    top = G + 30

    def corner(h: int, sx: int, sz: int) -> tuple[int, int, int]:
        o = 6.5 - 3.0 * (h - (G + 1)) / (top - (G + 1))
        return (round(cx + sx * o), h, round(cz + sz * o))

    legs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sz in legs:
        d.line(corner(G + 1, sx, sz), corner(top, sx, sz), LOG)
    for h in range(G + 8, top + 1, 6):
        ring = [corner(h, *s) for s in legs]
        for a, b in zip(ring, ring[1:] + ring[:1]):
            d.member(a, b, LOG)
        if h + 6 <= top:
            up = [corner(h + 6, *s) for s in legs]
            for i in range(4):
                d.line(ring[i], up[(i + 1) % 4], LOG)
    box(d, round(cx - 4), top, round(cz - 4), round(cx + 4), top, round(cz + 4), PLANK)
    box(d, SX, top, SZ, SX + 3, top, SZ + 3, CLEAR)
    for z in (78, 81):                                                            # the two sheave wheels
        rim = solids.ring2d(cx, top + 5, 4.5, 1)
        d.fill([(x, y, z) for x, y in rim], Mat("iron_block", tint=0x4A4038))
        for y in range(top + 1, top + 6):
            d.put(79, y, z, Mat("iron_block", tint=0x4A4038, shape=sc.post()))
        d.fill([(x, top + 5, z) for x in range(76, 84)], Mat("iron_block", tint=0x4A4038, shape=sc.beam("x")))
    for z in (76, 83):                                                            # the back struts
        d.line((82, top, z), (99, G + 9, z), LOG)
    d.line((84, top + 9, 80), (98, G + 6, 80), Mat("iron_block", tint=0x3A3A3A, shape=sc.post()))


def engine_house(d: Design) -> None:
    """The engine house: stone walls, a mahogany gable roof, a brick chimney, the winding drum, the engine and a chest."""
    x0, z0, x1, z1 = 96, 72, 110, 88
    box(d, x0, G, z0, x1, G, z1, STONE)
    for y in range(G + 1, G + 8):
        d.fill([(x, y, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1) if x in (x0, x1) or z in (z0, z1)],
               Mix(("stone", 3), ("moss_stone", 1), seed=43))
    roof(d, x0 - 1, z0 - 1, x1 + 1, z1 + 1, G + 8)
    box(d, 103, G + 1, z0, 104, G + 3, z0, CLEAR)
    d.door(103, G + 1, z0, "hinge")
    box(d, x0, G + 4, 79, x0, G + 6, 81, CLEAR)                                    # the rope's opening toward the shaft
    for z in (75, 76, 84, 85):
        box(d, x1, G + 3, z, x1, G + 5, z, Mat("glass"))
    for x in (99, 100, 106, 107):
        box(d, x, G + 3, z1, x, G + 5, z1, Mat("glass"))
    d.fill(solids.cylinder_along("z", 99, G + 3, 77, 83, 2.2), Mat("wood_planks"))  # the winding drum
    box(d, 98, G + 1, 78, 100, G + 1, 82, Mat("iron_block", tint=0x4A4038))
    box(d, 104, G + 1, 82, 107, G + 3, 85, Mat("broken_machine"))
    box(d, 107, G + 1, 74, 109, G + 22, 76, Mix(("ancient_brick", 4), ("stone", 1), seed=44))  # the chimney
    box(d, 108, G + 1, 75, 108, G + 22, 75, CLEAR)
    d.chest(108, G + 1, 86, table="mine_ore")
    d.put(98, G + 6, 74, "lantern")
    d.put(108, G + 6, 80, "lantern")


def build() -> Design:
    d = Design("old_mine", "Old Mine", nameKey="landmark.old_mine.name", ruin="always", ruinProfile="underground",
               requires=["atmosphere"], groundRow=G, weight=1, minTerrainGeneration=39)
    shaft(d)
    level_one(d)
    level_two(d)
    level_three(d)
    yard(d)
    shaft_house(d)
    engine_house(d)
    headframe(d)
    ladder(d)
    d.normalize(y=True)  # drop the empty margins round the caves and under the sump (the ground row moves along)
    return d


if __name__ == "__main__":
    build().write()
