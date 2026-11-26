"""The LINER (#3619): a space cruise liner of the old SPS survey fleet "Meridian" — a giant ship nobody can build; the
game shows it only as a wreck (crashed on a planet or drifting in space). Everyone left in the lifeboats long ago, so
the lifeboat bays along its sides stand empty.

    uv run --no-project python tools/gen_giant_ships.py --only liner --out artifacts/gs/ships --preview

Coordinates: x across, y up, z along; the BOW points to +z. 61 x 68 x 208 (hull 200 long + engine bells).

What it is made of (bottom to top):
- a smooth cream hull: a lower hull with a round bilge, a sharp bow and a rounded stern, joined to a superstructure as
  wide as the hull with rounded top edges and a rounded front; a dark blue bottom, a gold stripe, rows of windows;
- DECK 1 (walk row 5): the engine room at the stern, crew cabins and store rooms;
- DECKS 2 and 3 (walk rows 11, 17): passenger cabins off two long corridors (outboard cabins with a window, inboard
  cabins between the corridors), cross corridors at the ladder shafts;
- DECK 4 (walk row 23): the promenades along both sides with the EMPTY lifeboat bays, the theatre (two decks tall,
  a stage at the stern with a backstage and a hidden cache under it), the lounge, the reception behind the port
  airlock, the dining hall and the galley;
- DECK 5 (walk row 29): more cabins;
- the LIDO DECK (walk row 38) under three big glass domes: two swimming pools (3 deep; on airless worlds and in space
  the game turns their water to ice — then they are skating rinks), a garden with trees and a splash pool, loungers,
  parasols, a bar, a water slide, and the bridge at the front with wings over both sides;
- on the roof: a sun deck with railings, a raked funnel aft, a mast with antennas over the bridge.
Four ladder shafts (all `keep`) run from deck 1 to the lido deck, one of them at the stern.
"""
from ships import (
    CLEAR,
    Mat,
    Mix,
    ShipDesign,
    rounded_rect,
    sc,
    ship_section,
    shipgeom,
    solids,
)

# --- materials ------------------------------------------------------------------------------------------------------
CREAM = Mat("iron_block", tint=0xF3EBDA)
HULL = CREAM
SKIN = Mix(CREAM, (Mat("iron_block", tint=0xEAE1CC), 0.15), (Mat("iron_block", tint=0xF8F4EA), 0.12), seed=7)
WHITE = Mat("iron_block", tint=0xF8F6F0)
BOOT = Mat("iron_block", tint=0x24406E)                  # the dark blue bottom
GOLD = Mat("iron_block", tint=0xC8A040)                  # the stripe along both sides
FUNNEL = Mat("iron_block", tint=0x2C5AA0)
WALL = Mat("concrete", tint=0xEDE6D6)                    # cream inner walls
DECK = Mat("steel_floor")
WOOD = Mat("wood_planks_light")                          # lido deck planks, the promenades
TEAK = Mat("wood_planks_light", tint=0xC8A07A)           # the sun deck on the roof
CABIN_FLOOR = Mat("wood_planks")
CARPET = Mat("concrete", tint=0x8C2F39)                  # corridor carpets
TILE = Mat("concrete", tint=0x7FC8E0)                    # pool tiles
RIM = Mat("concrete", tint=0xF4F4F0)                     # pool rims, loungers
WATER = Mat("water")
GLASS = Mat("glass")
FRAME = Mat("aluminium_block")
LIGHT = Mat("light_white", glow=0xFFD9A0)               # warm lights everywhere
DARK = Mat("tungsten_block")
TABLE = Mat("wood_planks_mahogany", shape=sc.pack("table"))
HAZARD_Y = Mat("concrete", tint=0xF2C230)
HAZARD_K = Mat("metal_panel", tint=0x202226)
# An inert look-alike for a display (a giant wreck is minable, so it never holds a device whose item only a blueprint
# makes): a glowing blue screen.
SCREEN = Mat("glass", tint=0x2A5A8A, glow=0x4AA0E0)

# --- the frame of the ship ------------------------------------------------------------------------------------------
CX = 30                    # centre line
LEN = 200                  # hull slices z 0..199
GROUND = 4                 # on a planet rows 0..3 lie in the rock (the bilge)
PLATES = (4, 10, 16, 22, 28, 34)          # deck plates: decks 1..5 walk on 5, 11, 17, 23, 29; 34 = deck 5's ceiling
WALK1, WALK2, WALK3, WALK4, WALK5 = 5, 11, 17, 23, 29
LIDO_PLATE, LIDO = 37, 38  # the lido deck (rows 35..36 between: the pools' tanks)
ROOF = 45                  # the superstructure's top row (roof wall 44..45); people walk on the roof at 46
SUPER_Z = (8, 172)        # the superstructure's extent along z
ENGINE_Z1 = 28             # the engine room: z 2..28 on deck 1
SHAFTS = (66, 112, 166)    # cross corridors (z - 1 .. z + 1) and the shafts just forward of them
STERN_CROSS = 12           # the stern cross corridor; the stern shaft lies just aft of it (z 8..10)
DOMES = (48, 94, 140)      # dome centres along z
DOME_R, DOME_RZ, DOME_RY = 21, 20, 18
THEATRE = (14, 60)         # deck 4 + 5 at the stern
BAYS = (34, 72, 132, 154)  # lifeboat bays along both sides (z start, 14 long) on deck 4
BAY_LEN = 14
AIRLOCK_Z = 106            # the reception's port airlock

TABLE_LINER = "giant_liner"
TABLE_CACHE = "giant_cache"


def build() -> ShipDesign:
    d = ShipDesign("liner", "Liner", weight=3, groundRow=GROUND, civilization="sps")
    hull = _hull(d)
    _decks(d, hull)
    _windows(d, hull)
    _engine_room(d, hull)
    cabins = {}
    cabins[1] = _cabin_deck(d, hull, WALK1, ENGINE_Z1 + 2, 184, crew=True)
    d.slide_door(CX - 10, WALK1, ENGINE_Z1 + 1, "z")                 # both corridors -> the engine room
    d.slide_door(CX + 8, WALK1, ENGINE_Z1 + 1, "z")
    cabins[2] = _cabin_deck(d, hull, WALK2, STERN_CROSS + 3, 184, stern=True)
    cabins[3] = _cabin_deck(d, hull, WALK3, STERN_CROSS + 3, 184, stern=True)
    cabins[5] = _cabin_deck(d, hull, WALK5, THEATRE[1] + 2, 170)
    deck4 = _deck4(d, hull)
    _lido(d, hull)
    _roof(d, hull)
    _bays(d, hull)
    _engines(d)
    _shafts(d)
    _loot_and_ways_in(d, hull, cabins, deck4)
    d.normalize()
    return d


# --- hull -----------------------------------------------------------------------------------------------------------
def _hull(d: ShipDesign):
    """The lower hull (rows 0..27): a flat bottom 37 wide, a round bilge, 55 wide. Forward of z 145 it narrows through
    stations to a sharp bow whose forefoot rises (a raked stem); its first 16 slices round the stern. The
    superstructure (rows 27..45, z 8..172) is as wide, with rounded top edges and a rounded front. One hull: the walls
    between them disappear."""
    def low(beam: float, keel: float = 0) -> object:
        return ship_section(CX, keel=keel, deck=27, beam=beam, bottom=beam * 0.67, bilge=0.3)
    bow = shipgeom.loft([(0, low(55)), (145, low(55)), (170, low(50)), (185, low(37, 1)), (194, low(21, 4)),
                         (LEN - 1, low(7, 9))])
    stern = shipgeom.loft([(0, low(55)), (LEN - 1, low(55))], stern=16, tip=0.55, curve="ogive", taper_axes="x")
    # The bow comes from the stations, the round stern from the taper: take each half from its own loft.
    lower = shipgeom.Hull({z: (bow.slices[z] if z >= LEN // 2 else stern.slices[z]) for z in bow.slices}, bow.axis_x)
    sup = rounded_rect(CX, 36, 55, 19, 5)
    upper = shipgeom.loft([(SUPER_Z[0], sup), (SUPER_Z[1], sup)], bow=12, stern=8, tip=0.45, curve="blunt",
                          taper_axes="x")
    hull = d.add_hull(lower | upper, HULL, wall=2, skin=SKIN)
    outer = hull.outer()
    d.fill([p for p in outer if p[1] >= 28], WHITE, "blocks")
    d.fill([p for p in outer if p[1] <= 6], BOOT, "blocks")
    d.fill([p for p in outer if p[1] in (20, 21)], GOLD, "blocks")
    return hull


def _decks(d: ShipDesign, hull) -> None:
    for y in PLATES:
        d.deck(hull, y, DECK)
    d.deck(hull, LIDO_PLATE, WOOD)
    # The theatre is two decks tall: no deck 5 floor over it.
    t0, t1 = THEATRE
    d.fill(solids.box(CX - 17, 28, t0, CX + 17, 28, t1), CLEAR)


def _windows(d: ShipDesign, hull) -> None:
    """Portholes on deck 1, cabin windows on decks 2, 3 and 5, tall promenade windows on deck 4, a glass band round
    the lido deck."""
    d.window_band(hull, WALK1 + 1, WALK1 + 1, every=7, width=1, start=ENGINE_Z1 + 4, end=182)
    for walk in (WALK2, WALK3, WALK5):
        d.window_band(hull, walk + 1, walk + 2, every=7, width=3, start=14, end=182)
    d.window_band(hull, WALK4 + 1, WALK4 + 3, every=4, width=3, start=14, end=182)
    d.window_band(hull, LIDO + 1, LIDO + 3, every=6, width=5, start=SUPER_Z[0] + 4, end=SUPER_Z[1] - 4)
    d.window_band(hull, WALK5 + 1, WALK5 + 3, side="front", every=3, width=2)
    d.window_band(hull, WALK5 + 1, WALK5 + 2, side="back", every=4, width=2)
    d.window_band(hull, LIDO + 1, LIDO + 3, side="back", every=4, width=3)


def _bulkhead(d: ShipDesign, hull, z: int, y0: int, y1: int, m=WALL, x0: int = -99, x1: int = 999) -> set:
    return d.fill([(x, y, z) for y in range(y0, y1 + 1) for x in range(max(x0, CX - 30), min(x1, CX + 30) + 1)
                   if hull.contains((x, y, z), 2)], m)


# --- deck 1: the engine room ----------------------------------------------------------------------------------------
def _engine_room(d: ShipDesign, hull) -> None:
    """Deck 1 at the stern (z 2..28): two reactors in glowing rings, pipes to the engines, a console row."""
    _bulkhead(d, hull, ENGINE_Z1 + 1, WALK1, WALK1 + 4)
    for rx in (CX - 8, CX + 8):
        d.cylinder(rx, 14, WALK1, WALK1 + 4, 3.5, Mat("machine_block"))
        d.cylinder(rx, 14, WALK1 + 2, WALK1 + 2, 3.5, Mat("strip_light_warm"), wall=1)
        d.tube([(rx, WALK1 + 1, 10), (rx, WALK1 + 1, 2)], Mat("factory_pipe"), radius=1.0, kind="poly")
    for x in range(CX - 6, CX + 7, 4):
        d.put(x, WALK1, 24, Mat("factory_terminal"))
    for z in range(6, ENGINE_Z1, 8):
        for x in (CX - 14, CX, CX + 14):
            if d.is_block(x, PLATES[1], z):
                d.put(x, PLATES[1], z, LIGHT)


# --- the cabin decks ------------------------------------------------------------------------------------------------
def _cabin_deck(d: ShipDesign, hull, walk: int, z0: int, z1: int, crew: bool = False, stern: bool = False) -> list:
    """Two corridors (x CX-9 and CX+9) from z0 to z1 with cabins outboard and back to back between them, cross
    corridors at the shafts (with `stern` also one at the stern shaft, aft of z0). Returns the cabins (outboard
    first)."""
    lcor = d.corridor(hull, walk, z0, z1, centre=CX - 9, width=3, height=4, walls=WALL, ceiling=WALL,
                      floor=CARPET, light=LIGHT, light_every=7)
    rcor = d.corridor(hull, walk, z0, z1, centre=CX + 9, width=3, height=4, walls=WALL, ceiling=WALL,
                      floor=CARPET, light=LIGHT, light_every=7)
    cuts = [z for z in SHAFTS if z0 + 3 < z < z1 - 4]
    out_bounds = [z0 - 1] + [z for c in cuts for z in (c - 2, c + 2)] + [z1 + 1]
    in_bounds = [z0 - 1] + [z for c in cuts for z in (c - 2, c + 4)] + [z1 + 1]
    rooms = []
    for bounds, depth, sides in ((out_bounds, 14, ("left", "right")), (in_bounds, 6, ("right", "left"))):
        for a0, a1 in zip(bounds[0::2], bounds[1::2]):
            per = (a1 - a0) // 7
            if per < 1:
                continue
            for cor, side in zip((lcor, rcor), sides):
                rooms += d.rooms(hull, cor, per_side=per, depth=depth, sides=side, a0=a0, a1=a1, height=5,
                                 walls=WALL, floor=CABIN_FLOOR)
    for c in cuts:
        d.corridor(hull, walk, CX - 10, CX + 10, axis="x", centre=c, width=3, height=4, walls=WALL, ceiling=WALL,
                   floor=CARPET)
        for x in [*range(lcor.c0, lcor.c1 + 1), *range(rcor.c0, rcor.c1 + 1)]:   # the long corridors run on through
            d.fill([(x, y, z) for y in range(walk, walk + 4) for z in (c - 2, c + 2)], CLEAR)
    if stern:
        c = STERN_CROSS
        d.corridor(hull, walk, CX - 10, CX + 10, axis="x", centre=c, width=3, height=4, walls=WALL, ceiling=WALL,
                   floor=CARPET)
        for x in [*range(lcor.c0, lcor.c1 + 1), *range(rcor.c0, rcor.c1 + 1)]:
            d.fill([(x, y, c + 2) for y in range(walk, walk + 4)], CLEAR)
    if crew:
        for i, r in enumerate(rooms):
            (_store if i % 3 == 2 else _crew_cabin)(d, r)
    else:
        for r in rooms:
            _cabin(d, r)
    return rooms


def _floor_free(d: ShipDesign, room, x: int, z: int) -> bool:
    p = (x, room.y0, z)
    if p not in room.cells or d.get(*p) is not None or not d.is_block(x, room.y0 - 1, z):
        return False
    dx, _, dz = room.door
    return abs(x - dx) + abs(z - dz) > 2


def _far(room) -> int:
    """The x of the room's wall side away from the corridor."""
    return room.x0 if room.side == "left" else room.x1


def _cabin(d: ShipDesign, room) -> None:
    """A passenger cabin: a bed against the far wall (under the window), a small table and a chair, a wardrobe, a rug
    in a colour picked from the cabin's place."""
    fx = _far(room)
    s = 1 if room.side == "left" else -1
    zs = sorted({c[2] for c in room.floor})
    bz = zs[len(zs) // 2]
    for z in (bz - 1, bz):
        if _floor_free(d, room, fx, z):
            d.put(fx, room.y0, z, Mat("bed"))
    tx = fx + 2 * s
    if _floor_free(d, room, tx, zs[0]):
        d.put(tx, room.y0, zs[0], TABLE)
        if _floor_free(d, room, tx + s, zs[0]):
            d.put(tx + s, room.y0, zs[0], Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(s, 0))))
    if _floor_free(d, room, fx, zs[-1]):
        d.put(fx, room.y0, zs[-1], Mat("wood_crate"))
    rugs = (0xB04A4A, 0x4A7AB0, 0x5A9A5A, 0xC89A3A)
    tint = rugs[(room.z0 // 7 + (1 if room.side == "left" else 0)) % len(rugs)]
    for c in room.floor:
        if d.get(*c) is None and abs(c[0] - fx) in (1, 2):
            d.put(c[0], c[1] - 1, c[2], Mat("concrete", tint=tint))


def _crew_cabin(d: ShipDesign, room) -> None:
    fx = _far(room)
    for z in sorted({c[2] for c in room.floor})[1:-1]:
        if _floor_free(d, room, fx, z):
            d.put(fx, room.y0, z, Mat("crew_bunk"))
            d.put(fx, room.y0 + 2, z, Mat("crew_bunk"))


def _store(d: ShipDesign, room) -> None:
    fx = _far(room)
    for i, z in enumerate(sorted({c[2] for c in room.floor})):
        if _floor_free(d, room, fx, z):
            d.put(fx, room.y0, z, Mat("crate" if i % 2 else "transport_crate"))
            if i % 3 == 0:
                d.put(fx, room.y0 + 1, z, Mat("crate"))


# --- deck 4: promenades, lifeboat bays, the public rooms ------------------------------------------------------------
def _deck4(d: ShipDesign, hull) -> dict:
    """The promenades run down both sides (x 5..11 from the hull); between them the public rooms, separated by cross
    walls with a double door in the middle and doors to both promenades. Returns the rooms' boxes by name."""
    y0, y1 = WALK4, WALK4 + 4
    for sgn in (-1, 1):
        x = CX + sgn * 18
        d.fill([(x, y, z) for y in range(y0, y1 + 1) for z in range(12, 185) if hull.contains((x, y, z), 2)], WALL)
    for p in hull.row_cells(PLATES[3]):
        if abs(p[0] - CX) >= 19 and 12 <= p[2] <= 184:
            d.put(*p, WOOD)
    rooms = {
        "theatre": (THEATRE[0], THEATRE[1]),
        "lobby_aft": (THEATRE[1] + 2, 70),
        "lounge": (72, 100),
        "reception": (102, 124),
        "dining": (126, 150),
        "galley": (152, 162),
        "lobby_fwd": (164, 172),
        "forward": (174, 184),
    }
    for name, (a, b) in rooms.items():
        top = WALK5 + 4 if name == "theatre" else y1
        _bulkhead(d, hull, b + 1, y0, top, x0=CX - 17, x1=CX + 17)
        if name != "forward":
            d.slide_door(CX + 4, y0, b + 1, "z")                 # beside the centre line: the shafts stand there
            mid = (a + b) // 2
            for sgn in (-1, 1):
                d.slide_door(CX + sgn * 18, y0, mid, "x")
    _bulkhead(d, hull, THEATRE[0] - 1, y0, WALK5 + 4, x0=CX - 17, x1=CX + 17)
    for sgn in (-1, 1):                                          # the theatre's side walls reach up through deck 5
        d.fill([(CX + sgn * 18, y, z) for y in range(y1 + 1, WALK5 + 5) for z in range(THEATRE[0] - 1, THEATRE[1] + 2)
                if hull.contains((CX + sgn * 18, y, z), 2)], WALL)
    _theatre(d)
    _lounge(d, *rooms["lounge"])
    _reception(d, *rooms["reception"])
    _dining(d, *rooms["dining"])
    _galley(d, *rooms["galley"])
    for z in range(14, 184, 6):                                   # warm lights along the promenades
        for sgn in (-1, 1):
            if d.is_block(CX + sgn * 22, PLATES[4], z):
                d.put(CX + sgn * 22, PLATES[4], z, LIGHT)
    return rooms


def _theatre(d: ShipDesign) -> None:
    """A stage near the stern end (two rows high, a red backdrop, two steps up at both sides, footlights) with a
    backstage behind it and a door to the stern shaft, rows of seats facing it with an aisle down the middle, lights in
    the high ceiling."""
    t0, t1 = THEATRE
    s0, s1 = t0 + 2, t0 + 8                                       # the stage; z t0..t0+1 = backstage
    d.fill(solids.box(CX - 13, WALK4, s0, CX + 13, WALK4 + 1, s1), Mat("wood_planks_mahogany"))
    d.fill(solids.box(CX - 13, WALK4 + 2, s0, CX + 13, WALK5 + 3, s0), Mat("concrete", tint=0xA02030))
    for sgn in (-1, 1):
        for z in (s0 + 3, s0 + 4):
            d.put(CX + sgn * 14, WALK4, z, Mat("wood_planks_mahogany"))
            d.put(CX + sgn * 14, WALK4 + 1, z, Mat("wood_planks_mahogany", shape=sc.stairs(-sgn, 0)))
            d.put(CX + sgn * 15, WALK4, z, Mat("wood_planks_mahogany", shape=sc.stairs(-sgn, 0)))
    for x in range(CX - 12, CX + 13, 3):                          # footlights along the stage's front edge
        d.put(x, WALK4 + 1, s1, LIGHT)
    for z in range(s1 + 3, t1 - 1, 2):
        for x in range(CX - 15, CX + 16):
            if abs(x - CX) > 1:
                d.put(x, WALK4, z, Mat("concrete", tint=0xA02030, shape=sc.pack("chair")))
    for z in range(t0 + 6, t1, 7):
        for x in range(CX - 12, CX + 13, 8):
            d.put(x, WALK5 + 4, z, LIGHT)
    d.slide_door(CX - 1, WALK4, t0 - 1, "z")                       # backstage -> the stern shaft


def _lounge(d: ShipDesign, a: int, b: int) -> None:
    """Sofas round low tables on rugs, a bar along the starboard side, plants."""
    for cz in range(a + 5, b - 3, 9):
        for cx in (CX - 9, CX + 3):
            d.fill(solids.box(cx - 2, WALK4 - 1, cz - 2, cx + 2, WALK4 - 1, cz + 2), Mat("concrete", tint=0x6A4FA0))
            d.put(cx, WALK4, cz, TABLE)
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                d.put(cx + 2 * dx, WALK4, cz + 2 * dz, Mat("wood_planks_mahogany",
                                                            shape=sc.pack("bench", sc.yaw_toward(dx, dz))))
    d.fill(solids.box(CX + 13, WALK4, a + 3, CX + 13, WALK4, b - 3), Mat("wood_planks_mahogany", shape=sc.pack("slab")))
    for z in range(a + 4, b - 3, 3):
        d.put(CX + 11, WALK4, z, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(-1, 0))))
    for x, z in ((CX - 16, a + 1), (CX - 16, b - 1), (CX + 16, a + 1), (CX + 16, b - 1)):
        d.put(x, WALK4, z, Mat("flower_pot", shape=sc.pack(sc.POT)))


def _reception(d: ShipDesign, a: int, b: int) -> None:
    """The reception lobby behind the port airlock: a curved desk, armchairs, potted plants, a chandelier of lights."""
    for z in range(a + 3, a + 12):
        d.put(CX - 5, WALK4, z, Mat("wood_planks_mahogany", shape=sc.pack("slab") if z in (a + 3, a + 11) else 0))
    d.put(CX - 3, WALK4, a + 7, Mat("lab_panel"))
    for z in (b - 4, b - 7):
        for x in (CX - 12, CX - 9):
            d.put(x, WALK4, z, Mat("concrete", tint=0x3A6A9A, shape=sc.pack("chair", sc.yaw_toward(0, 1))))
    for x, z in ((CX - 16, a + 1), (CX - 16, b - 1), (CX + 8, a + 1), (CX + 8, b - 1)):
        d.put(x, WALK4, z, Mat("flower_pot", shape=sc.pack(sc.POT)))
    for x in (CX - 10, CX + 10):
        d.put(x, WALK4 + 4, (a + b) // 2, LIGHT)


def _dining(d: ShipDesign, a: int, b: int) -> None:
    """Long tables down the dining hall with chairs on both sides, a walkway across its middle."""
    mid = (a + b) // 2
    for tx in range(CX - 13, CX + 14, 5):
        if abs(tx - CX) <= 1:
            continue
        for z in range(a + 2, b - 1):
            if abs(z - mid) <= 1:
                continue
            d.put(tx, WALK4, z, TABLE)
            d.put(tx - 1, WALK4, z, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(-1, 0))))
            d.put(tx + 1, WALK4, z, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(1, 0))))
    for z in range(a + 3, b, 6):
        for x in (CX - 8, CX + 8):
            d.put(x, WALK4 + 4, z, LIGHT)


def _galley(d: ShipDesign, a: int, b: int) -> None:
    """Counters, stoves and cold stores; the kitchen of a ship that fed thousands."""
    for z in range(a + 1, b):
        for x in (CX - 15, CX + 15):
            d.put(x, WALK4, z, Mat("metal_panel", shape=sc.pack("slab")) if z % 4 else Mat("forge_off"))
    for x in range(CX - 10, CX + 11, 4):
        d.put(x, WALK4, (a + b) // 2, Mat("metal_panel"))
        d.put(x, WALK4, (a + b) // 2 + 1, Mat("metal_panel", shape=sc.pack("slab")))
    for z in (a + 1, b - 1):
        d.fill([(CX - 12, y, z) for y in (WALK4, WALK4 + 1)], Mat("insulated_wall"))
        d.fill([(CX + 12, y, z) for y in (WALK4, WALK4 + 1)], Mat("insulated_wall"))


# --- the lido deck --------------------------------------------------------------------------------------------------
def _pool(d: ShipDesign, cx: int, cz: int, a: float, b: float, depth: int = 3) -> set:
    """A rounded pool sunk into the lido deck: water `depth` rows deep under the deck level, tiled walls and floor,
    a white rim on the deck row, steps out at the stern end. Returns the water's (x, z) cells."""
    inside = {(x, z) for x in range(int(cx - a) - 1, int(cx + a) + 2) for z in range(int(cz - b) - 1, int(cz + b) + 2)
              if ((x - cx) / a) ** 4 + ((z - cz) / b) ** 4 <= 1}
    rim = {(x + dx, z + dz) for x, z in inside for dx in (-1, 0, 1) for dz in (-1, 0, 1)} - inside
    bottom = LIDO_PLATE - depth
    for x, z in inside:
        d.put(x, bottom, z, TILE)
        for y in range(bottom + 1, LIDO_PLATE + 1):
            d.put(x, y, z, WATER)
    for x, z in rim:
        for y in range(bottom, LIDO_PLATE):
            d.put(x, y, z, TILE)
        d.put(x, LIDO_PLATE, z, RIM)
    z0 = min(z for _, z in inside)
    for x in range(cx - 1, cx + 2):                               # steps out at the stern end
        for k in range(depth):
            d.put(x, bottom + 1 + k, z0 + depth - 1 - k, Mat("concrete", tint=0xF4F4F0, shape=sc.stairs(0, -1)))
            for y in range(bottom + 1, bottom + 1 + k):
                d.put(x, y, z0 + depth - 1 - k, RIM)
    return inside


def _lounger(d: ShipDesign, x: int, z: int, along: int = 1) -> None:
    d.put(x, LIDO, z, Mat("concrete", tint=0xF4F4F0, shape=sc.pack("slab")))
    d.put(x, LIDO, z + along, Mat("concrete", tint=0xF4F4F0, shape=sc.ramp(0, along, low=True)))


def _parasol(d: ShipDesign, x: int, z: int, tint: int) -> None:
    d.fill([(x, y, z) for y in (LIDO, LIDO + 1)], Mat("wood_planks_light", shape=sc.post()))
    d.fill([(x + dx, LIDO + 2, z + dz) for dx in (-1, 0, 1) for dz in (-1, 0, 1)],
           Mat("concrete", tint=tint, shape=sc.pack("slab")))


def _tree(d: ShipDesign, x: int, z: int, h: int = 4) -> None:
    """A small tree in a round planter."""
    d.fill({(x + dx, LIDO, z + dz) for dx in (-1, 0, 1) for dz in (-1, 0, 1)}, Mat("grass"))
    d.fill([(x, y, z) for y in range(LIDO + 1, LIDO + 1 + h)], Mat("wood_log"))
    d.sphere(x, LIDO + h + 1, z, 2.2, Mat("tree_leaves"), where="empty")


def _lido(d: ShipDesign, hull) -> None:
    """Under the aft dome a big pool with loungers and parasols; under the middle dome a garden with trees, flower
    beds, benches and a splash pool; under the forward dome a pool with a water slide; a bar between the domes; the
    bridge at the front."""
    d1, d2, d3 = DOMES
    water = _pool(d, CX, d1, 9, 7)
    for z in range(d1 - 9, d1 + 10, 3):
        for x in (CX - 15, CX + 15):
            _lounger(d, x, z)
    for x, z, t in ((CX - 12, d1 - 12, 0xD04040), (CX + 12, d1 - 12, 0x4080D0), (CX - 12, d1 + 12, 0xF2C230),
                    (CX + 12, d1 + 12, 0x40A060)):
        _parasol(d, x, z, t)
    # The garden.
    for x, z in ((CX - 12, d2 - 10), (CX + 12, d2 - 10), (CX - 12, d2 + 10), (CX + 12, d2 + 10), (CX - 16, d2),
                 (CX + 16, d2)):
        _tree(d, x, z)
    _pool(d, CX, d2, 3.5, 3.5, depth=2)
    flowers = ("flora_flower", "flora_tulip", "flora_daisy", "flora_lupine", "flora_bush", "flora_fern")
    for i, (x0, z0) in enumerate(((CX - 6, d2 - 14), (CX + 3, d2 - 14), (CX - 6, d2 + 12), (CX + 3, d2 + 12))):
        d.fill(solids.box(x0, LIDO, z0, x0 + 3, LIDO, z0 + 1), Mat("grass"))
        for k, (x, z) in enumerate((x, z) for x in range(x0, x0 + 4) for z in (z0, z0 + 1)):
            d.put(x, LIDO + 1, z, Mat(flowers[(i + k) % len(flowers)]))
    for z in (d2 - 5, d2 + 5):
        for x in (CX - 7, CX + 7):
            d.put(x, LIDO, z, Mat("wood_planks_light", shape=sc.pack("bench", sc.yaw_toward(1 if x > CX else -1, 0))))
    # The forward pool and its slide: a little tower with a ladder, a chute of ramps down into the water.
    water |= _pool(d, CX, d3, 8, 6)
    sx, sz = CX + 6, d3 - 13
    d.fill(solids.box(sx - 1, LIDO, sz - 2, sx + 1, LIDO + 4, sz), Mat("concrete", tint=0xF2C230))
    d.ladder(sx, sz - 3, LIDO, LIDO + 5)
    for k in range(6):
        d.put(sx, LIDO + 4 - k, sz + 1 + k, Mat("concrete", tint=0x40B0E0, shape=sc.ramp(0, -1)))
        for dx in (-1, 1):
            d.put(sx + dx, LIDO + 5 - k, sz + 1 + k, Mat("concrete", tint=0x2080C0, shape=sc.post()))
    for z in range(d3 - 6, d3 + 7, 3):
        for x in (CX - 15, CX + 15):
            _lounger(d, x, z)
    # The bars between the domes: a counter, stools, a light over it.
    for bz in ((d1 + d2) // 2, (d2 + d3) // 2):
        d.fill(solids.box(CX + 12, LIDO, bz - 3, CX + 12, LIDO, bz + 3), Mat("wood_planks_mahogany",
                                                                             shape=sc.pack("slab")))
        for z in range(bz - 3, bz + 4, 2):
            d.put(CX + 10, LIDO, z, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(-1, 0))))
    for z in range(SUPER_Z[0] + 6, SUPER_Z[1] - 8, 6):            # lights in the roof between the domes
        for x in (CX - 18, CX + 18):
            if d.is_block(x, ROOF - 1, z):
                d.put(x, ROOF - 1, z, LIGHT)
    _cafe_and_planters(d, hull)
    _bridge(d, hull)


def _cafe_and_planters(d: ShipDesign, hull) -> None:
    """The lido's aft end is a cafe (round tables with chairs, potted plants); flower boxes and deck chairs facing
    the windows line both sides between the domes; a shuttle pad with bollards on the foredeck."""
    for cz in range(SUPER_Z[0] + 6, DOMES[0] - DOME_RZ - 1, 5):
        for cx in range(CX - 14, CX + 15, 7):
            d.put(cx, LIDO, cz, TABLE)
            for dx, dz in ((1, 0), (-1, 0)):
                d.put(cx + dx, LIDO, cz + dz, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(dx, dz))))
    for z in range(SUPER_Z[0] + 6, SUPER_Z[1] - 12, 9):
        for sgn in (-1, 1):
            x = max((x for x in range(CX, CX + sgn * 30, sgn) if hull.contains((x, LIDO, z), 2)), key=abs)
            if d.get(x, LIDO, z) is None and d.get(x, LIDO, z + 1) is None:
                d.put(x, LIDO, z, Mat("grass"))
                d.put(x, LIDO, z + 1, Mat("grass"))
                d.put(x, LIDO + 1, z, Mat("flora_flower"))
                d.put(x, LIDO + 1, z + 1, Mat("flora_bush"))
            for k in (3, 5):
                if d.get(x - sgn, LIDO, z + k) is None:
                    d.put(x - sgn, LIDO, z + k, Mat("concrete", tint=0x4A7AB0,
                                                    shape=sc.pack("chair", sc.yaw_toward(-sgn, 0))))
    # The shuttle pad on the foredeck: a yellow ring, a cross of white lights, four bollards at the bow.
    pz = 184
    ring = solids.ring2d(CX, pz, 7, 1)
    d.fill([(x, 27, z) for x, z in ring if d.is_block(x, 27, z)], HAZARD_Y)
    for k in range(-4, 5, 2):
        for x, z in ((CX + k, pz), (CX, pz + k)):
            if d.is_block(x, 27, z):
                d.put(x, 27, z, LIGHT)
    for x, z in ((CX - 9, 176), (CX + 9, 176), (CX - 6, 192), (CX + 6, 192)):
        if d.is_block(x, 27, z):
            d.fill([(x, 28, z), (x, 29, z)], Mat("metal_panel", tint=0x3A4250, shape=sc.pack("cylinder")))


def _bridge(d: ShipDesign, hull) -> None:
    """The bridge at the front of the lido deck: a wall with a door to the lido, consoles along the front glass, the
    captain's chair, and wings reaching out over both sides with glass on three faces."""
    zb = DOMES[2] + DOME_RZ + 1                                   # just forward of the last dome
    _bulkhead(d, hull, zb, LIDO, ROOF - 2, m=WHITE)
    d.slide_door(CX - 1, LIDO, zb, "z")
    d.slide_door(CX + 6, LIDO, zb, "z")
    for x in range(CX - 14, CX + 15, 2):
        z = max(z for z in range(zb, SUPER_Z[1] + 1) if hull.contains((x, LIDO, z), 2))
        d.put(x, LIDO, z, SCREEN)
    d.put(CX + 4, LIDO, zb + 3, Mat("wood_planks", shape=sc.pack("chair", sc.yaw_toward(0, 1))))
    d.window_band(hull, LIDO + 1, LIDO + 3, side="front", every=1, width=1)
    w0, w1 = zb + 1, zb + 5
    for sgn in (-1, 1):
        xin = CX + sgn * 27
        xout = CX + sgn * 30
        xs = range(min(xin, xout), max(xin, xout) + 1)
        d.fill([(x, y, z) for x in xs for y in (LIDO - 1, ROOF - 1) for z in range(w0, w1 + 1)], WHITE)
        d.fill([(x, y, z) for x in xs for y in range(LIDO, ROOF - 1) for z in (w0, w1)], GLASS)
        d.fill([(xout, y, z) for y in range(LIDO, ROOF - 1) for z in range(w0, w1 + 1)], GLASS)
        d.fill([(x, y, z) for x in xs for y in range(LIDO, ROOF - 1) for z in range(w0 + 1, w1)
                if x != xout], CLEAR)
        d.fill([(CX + sgn * 26, y, z) for y in range(LIDO, ROOF - 2) for z in range(w0 + 1, w1)], CLEAR)
        d.put(xout - sgn, LIDO, w0 + 2, SCREEN)


# --- the roof: domes, sun deck, funnel, mast ------------------------------------------------------------------------
def _roof(d: ShipDesign, hull) -> None:
    for cz in DOMES:
        d.glass_dome(CX, cz, ROOF - 1, DOME_R, ry=DOME_RY, rz=DOME_RZ, ribs=12, rings=2, frame=FRAME)
    # The sun deck: planks on the roof, a railing round its edge (not over the domes).
    top = {(x, z) for x, y, z in hull.cells if y == ROOF and d.is_block(x, ROOF, z)
           and d.get(x, ROOF, z)[1] == "iron_block"}
    for x, z in top:
        if not d.is_block(x, ROOF + 1, z):
            d.put(x, ROOF, z, TEAK)
    rail = Mat("aluminium_block", shape=sc.pack("fence"))
    for x, z in top:
        edge = any((x + dx, z + dz) not in top for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)))
        if edge and d.get(x, ROOF + 1, z) is None and d.is_block(x, ROOF, z):
            d.put(x, ROOF + 1, z, rail)
    # The funnel aft: an elliptic stack raked back, blue with a cream band and a dark top.
    for k, y in enumerate(range(ROOF + 1, ROOF + 18)):
        cz = 18 - k // 3
        ring = solids.ring2d(CX, cz, 7, 1, rz=8.5)
        m = DARK if y >= ROOF + 16 else (CREAM if ROOF + 11 <= y <= ROOF + 12 else FUNNEL)
        d.fill([(x, y, z) for x, z in ring], m)
        if y == ROOF + 1:
            d.fill([(x, y, z) for x, z in solids.disc(CX, cz, 7, 8.5)], FUNNEL)
    # The mast over the bridge: a post with two yards, antennas, a radar dish.
    mz = SHAFTS[2] + 2
    post = Mat("aluminium_block", shape=sc.post())
    d.fill([(CX, y, mz) for y in range(ROOF + 1, ROOF + 22)], post)
    d.put(CX, ROOF + 22, mz, Mat("light_red"))
    for y, half in ((ROOF + 15, 6), (ROOF + 19, 3)):
        d.fill([(x, y, mz) for x in range(CX - half, CX + half + 1) if x != CX],
               Mat("aluminium_block", shape=sc.beam("x")))
        d.put(CX - half, y, mz, Mat("light_red"))
        d.put(CX + half, y, mz, Mat("light_green"))
    d.disc(CX, mz - 3, ROOF + 1, 2.5, Mat("aluminium_block", shape=sc.pack("slab")))
    d.sphere(CX, ROOF + 3, mz - 3, 1.6, Mat("aluminium_block"))
    # A ladder up from the lido deck to the sun deck, beside the funnel.
    d.ladder(CX + 10, 20, LIDO, ROOF)
    d.fill([(CX + 11, y, 20) for y in range(LIDO, ROOF)], WALL, keep=True)
    d.keep([(x, LIDO_PLATE, z) for x in range(CX + 8, CX + 12) for z in range(19, 22)])   # a kept floor at its foot


# --- the lifeboat bays and the airlock ------------------------------------------------------------------------------
def _bays(d: ShipDesign, hull) -> None:
    """Empty lifeboat bays in both sides on deck 4: the hull is open over 14 cells, a niche 5 deep behind it with two
    empty cradles and davit arms, a hazard edge, a warm light, a door into the promenade. The reception's airlock on
    the port side: a hull door with its outer frame."""
    for z0 in BAYS:
        z1 = z0 + BAY_LEN - 1
        for sgn in (-1, 1):
            xin = CX + sgn * 22                                   # the niche's back wall
            for z in range(z0, z1 + 1):
                for y in range(WALK4, WALK4 + 5):
                    r = hull.row(y, z)
                    xo = r[0] if sgn < 0 else r[1]
                    for x in range(min(xo, xin + sgn), max(xo, xin + sgn) + 1):
                        d.put(x, y, z, CLEAR)
                    d.put(xin, y, z, WALL)
                rp = hull.row(PLATES[3], z)
                d.put(rp[0] if sgn < 0 else rp[1], PLATES[3], z, HAZARD_Y if z % 2 else HAZARD_K)
            for z in (z0 - 1, z1 + 1):
                for y in range(WALK4, WALK4 + 5):
                    r = hull.row(y, z)
                    xo = r[0] if sgn < 0 else r[1]
                    for x in range(min(xo, xin), max(xo, xin) + 1):
                        if not hull.contains((x, y, z)) or hull.contains((x, y, z), 2):
                            d.put(x, y, z, WALL)
            for cz in (z0 + 3, z1 - 4):                           # empty cradles: two chocks each, davit arms
                d.fill([(xin + sgn * 2, WALK4, cz), (xin + sgn * 2, WALK4, cz + 1)], Mat("metal_panel", tint=0x3A4250,
                                                                                          shape=sc.pack("slab")))
                d.fill([(xin + sgn * k, WALK4 + 4, cz) for k in range(1, 6)], Mat("aluminium_block",
                                                                                   shape=sc.beam("x")))
            d.put(xin + sgn, WALK4 + 4, (z0 + z1) // 2, LIGHT)
            d.slide_door(xin, WALK4, (z0 + z1) // 2, "x")
    d.hull_door(hull, "left", WALK4, AIRLOCK_Z, width=2, height=3, frame=Mat("aluminium_block"), entrance=False)
    d.slide_door(CX - 18, WALK4, AIRLOCK_Z, "x")                    # straight on into the reception


# --- engines --------------------------------------------------------------------------------------------------------
def _engines(d: ShipDesign) -> None:
    stern = [p for p in d.plain if p[2] == 0 and d.is_block(*p) and ((p[0] - CX) / 15) ** 2 + ((p[1] - 16) / 10) ** 2
             <= 1]
    d.fill(stern, Mat("engine_panel"))
    for x in (CX - 7, CX + 7):
        d.nozzle(x, 14, 0, 5.5, 8, m=Mat("engine_nozzle"), glow=Mat("light_white", glow=0x66CCFF))
    for x in (CX - 10, CX + 10):
        d.nozzle(x, 22, 0, 2.5, 5, m=Mat("engine_nozzle"), glow=Mat("light_white", glow=0x66CCFF))


# --- the ways up ----------------------------------------------------------------------------------------------------
def _shafts(d: ShipDesign) -> None:
    """Four ladder shafts on the centre line: three just forward of a cross corridor (the doorway facing aft into it)
    from deck 1 to the lido deck, one at the stern. All `keep`."""
    for zs in SHAFTS:
        d.lift_shaft(CX - 1, zs + 3, [WALK1, WALK2, WALK3, WALK4, WALK5, LIDO], opening="-z", walls=WALL)
    # The stern shaft (the stern breaks off first in a crash, so it gets its own way up): the engine room, the stern
    # cross corridors of decks 2 and 3, the theatre's backstage, the lido's cafe. Deck 5 is the theatre's high hall.
    d.lift_shaft(CX - 1, STERN_CROSS - 3, [WALK1, WALK2, WALK3, WALK4, LIDO], opening="+z", walls=WALL)


# --- loot and the ways in -------------------------------------------------------------------------------------------
def _loot_and_ways_in(d: ShipDesign, hull, cabins: dict, deck4: dict) -> None:
    # The FIRST entrance is the boarding arrival in space: inside the reception's port airlock.
    d.entrance(hull.row(WALK4, AIRLOCK_Z, 2)[0], WALK4, AIRLOCK_Z)
    # On a planet the empty lifeboat bays stand open: one on each side.
    d.entrance(CX - 24, WALK4, BAYS[1] + 6)
    d.entrance(CX + 24, WALK4, BAYS[2] + 6)

    def chest(p, hidden: bool = False, block: str | None = None) -> None:
        d.chest(*p, table=TABLE_CACHE if hidden else TABLE_LINER, hidden=hidden, block=block)

    for deck, picks in ((2, (3, 22, 40)), (3, (10, 31)), (5, (6, 25)), (1, (4,))):
        rooms = cabins[deck]
        for i in picks:
            if i < len(rooms):
                chest(rooms[i].corner(1 if rooms[i].side == "left" else 2))
    chest((CX + 14, WALK4, deck4["galley"][0] + 2))                   # the galley
    chest((CX - 7, WALK4, deck4["reception"][0] + 5))                 # behind the reception desk
    chest((CX + 16, WALK4, deck4["dining"][1] - 1))                   # the dining hall
    chest((CX + 15, WALK4, deck4["lounge"][0] + 2))                   # behind the bar
    chest((CX + 12, WALK1, 6))                                        # the engine room
    chest((CX - 16, LIDO, DOMES[2] + 16))                             # the lido, by the bridge wall
    chest((CX + 8, LIDO, DOMES[2] + DOME_RZ + 3))                     # the bridge
    # Hidden caches: under the stage, on the sun deck beside the roof ladder, in a lifeboat bay beside its door.
    chest((CX + 4, WALK4, THEATRE[0] + 8), hidden=True)
    chest((CX + 9, ROOF + 1, 21), hidden=True)                        # beside the roof ladder's top
    chest((CX + 23, WALK4, BAYS[3] + 5), hidden=True)                 # beside the bay's door
    # The ambience: the creaks of a big empty hull midships on deck 4, water and air in the garden dome.
    d.sound(CX - 3, WALK4 + 2, 96, "amb_giant_hull", radius=64, volume=0.4)
    d.sound(CX, LIDO + 3, DOMES[1], "amb_giant_greenhouse", radius=40, volume=0.35)
