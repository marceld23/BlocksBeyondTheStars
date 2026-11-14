"""The CARRIER (#3618): a long, flat space carrier of the old SPS survey fleet "Meridian" — a giant ship nobody can build;
the game shows it only as a wreck (crashed on a planet or drifting in space), long abandoned, everyone got away.

    uv run --no-project python tools/gen_giant_ships.py --only carrier --out artifacts/gs/ships --preview

Coordinates: x across, y up, z along; the BOW points to +z. 57 x 72 x 236 (hull 226 long + engine bells).

What it is made of (bottom to top):
- a lofted slab hull: flat stern for the engines, an ogive bow whose keel rises while the flight deck stays level,
  sides flaring outward to the deck; grey panels, a darker belly, a blue stripe along both sides;
- the double bottom (rows 2..5, mostly buried on a planet), reached by one maintenance ladder;
- DECK C (walk row 7): the crew deck — a corridor down the middle with quarters, the mess hall, the galley, a medbay,
  a rec room, a store room and the stair hall; at the stern the ENGINE ROOM, two decks tall, with three reactors;
- DECK B (walk row 13): the lower hangar with parked small ships;
- DECK A (walk row 21): the upper hangar (aft and forward halves), the workshop at the stern, the big lift down from
  the flight deck, the open deck-edge lift on the port side and the LAUNCH TUNNEL running out through the bow;
- the FLIGHT DECK on top (walk row 30): dark steel with lane markings, an angled landing lane over a port sponson,
  catapult light strips, landing rings, edge lights;
- the ISLAND on the starboard side: five storeys, a glass bridge band, a mast with a radar dish and antennas.
Four ladder shafts (all `keep`) join every deck; the island shaft runs from deck C up to the bridge's roof.
"""
import json
from pathlib import Path

from ships import CLEAR, Mat, Mix, ShipDesign, sc, ship_section, solids

LAYOUTS = Path(__file__).resolve().parents[2] / "data" / "ship_layouts"

# --- materials (concrete blocks only; mixes only on the visible skin) ----------------------------------------------
HULL = Mat("iron_block")                                                  # grey hull, the shell's inner layer
SKIN = Mix(Mat("iron_block"), (Mat("iron_block", tint=0x8E949C), 0.14), (Mat("titanium_block", tint=0xA4AAB2), 0.06),
           seed=11)
BELLY = Mat("iron_block", tint=0x5E636A)                                  # the darker underside
STRIPE = Mat("iron_block", tint=0x2E64B0)                                 # the dyed band along both sides
STRIPE_LINE = Mat("iron_block", tint=0xE8E8E2)
FLIGHT = Mat("metal_panel")                                               # the dark flight deck
WHITE = Mat("concrete", tint=0xF2F2EC)                                    # lane markings
YELLOW = Mat("concrete", tint=0xF2C230)
RED = Mat("concrete", tint=0xD04040)
BLACK = Mat("metal_panel", tint=0x202226)
DECK = Mat("steel_floor")
HANGAR_FLOOR = Mat("steel_floor", tint=0x7C8288)
WALL = Mat("steel_wall")
TRIM = Mat("iron_block", tint=0x4A5562)                                   # frames, masts, mounts
DARK = Mat("tungsten_block")                                              # closed hangar doors, tunnel lining
LIGHT = Mat("light_white", glow=0xFFF2D0)                                 # warm white ceiling lights
EDGE_LIGHT = Mat("light_white", glow=0xFFF6E0)
GUIDE = Mat("strip_light_cyan")                                           # tunnel guide lights, catapult slots
ENGINE_GLOW = Mat("light_white", glow=0x66CCFF)
GLASS = Mat("glass")
TABLE = Mat("metal_panel", shape=sc.pack("table"))
# Inert look-alikes for devices (a giant wreck is minable, so it never holds a device whose item only a blueprint
# makes): a glowing blue screen, the medbay's glowing tank, a workshop fabricator's dyed casing.
SCREEN = Mat("glass", tint=0x2A5A8A, glow=0x4AA0E0)
TANK = Mat("glass_clear", tint=0x9FF0D0, glow=0x60E0B0)
CASING = Mat("steel_wall", tint=0x4A6A8A)

# --- the frame of the ship ------------------------------------------------------------------------------------------
CX = 32                    # centre line (odd beam + flare keep it on a whole cell)
LEN = 226                  # hull slices z 0..225
TOP = 29                   # the flight deck row (the hull's outermost top row)
DECK_C, DECK_B, DECK_A = 6, 12, 20          # deck plates
WALK_C, WALK_B, WALK_A, WALK_F = 7, 13, 21, 30   # walk rows: crew deck, lower hangar, upper hangar, flight deck
GROUND = 5                 # on a planet rows 0..4 lie in the rock (the double bottom), never a deck

ENGINE_Z1 = 40             # the engine room: z 2..40, decks C+B (walk 7..19)
WORKSHOP_Z1 = 38           # deck A aft: the workshop z 2..38, its wall at 39
HANGAR_A_DIV = 106         # the wall between the aft and the forward upper hangar (106..107)
HANGAR_A_END = 177         # the forward upper hangar's front wall (the launch tunnel starts here)
HANGAR_B_END = 174         # the lower hangar's front wall (the forward shaft's doorway lies in it)
TUNNEL_X = (CX - 3, CX + 3)    # the launch tunnel: 7 wide, rows 21..26 inside
LIFT = (CX - 7, 150, CX + 6, 163)   # the big lift: x0, z0, x1, z1 (14 x 14)
ISLAND = (CX + 9, 112, CX + 24, 146)  # x0, z0, x1, z1 of the island's lower block
ISLAND_FLOORS = (34, 39, 44)         # the island's storey floors above the flight deck (walk rows 35, 40, 45)
BRIDGE_FLOOR = 49                    # the bridge: walk row 50, a glass band on rows 51..52, the roof on row 54
MAST_TOP = 71
PORT_LIFT = (60, 75)       # the open deck-edge lift on the port side (z range), rows 21..25

TABLE_CARRIER = "giant_carrier"
TABLE_CACHE = "giant_cache"


def build() -> ShipDesign:
    d = ShipDesign("carrier", "Carrier", weight=3, groundRow=GROUND)
    hull = _hull(d)
    _sponson(d, hull)
    _decks(d, hull)
    rooms = _crew_deck(d, hull)
    _engine_room(d, hull)
    _upper_deck(d, hull)
    _launch_tunnel(d, hull)
    _flight_deck(d, hull)
    _island(d)
    _shafts(d)
    _hull_openings(d, hull)
    _parked_ships(d)
    _furniture(d, rooms)
    _engines(d)
    _loot_and_ways_in(d, hull, rooms)
    # Greebles last: vents, pipes and boxes on the sides and the belly (never on the flight deck).
    d.greebles(hull, seed=23, density=0.006, faces=("left", "right", "down"), m=TRIM,
               region=(-99, 1, 4, 199, TOP - 3, LEN - 8), avoid=_port_lift_cells(hull))
    d.normalize()
    return d


# --- hull -----------------------------------------------------------------------------------------------------------
def _hull(d: ShipDesign):
    """The slab hull: a flat bottom 27 wide, a round bilge, sides flaring to 47 at the deck. Straight from the flat
    stern to the bow, whose last 70 slices taper (ogive) about the deck: the flight deck stays level, the keel rises."""
    sec = ship_section(CX, keel=0, deck=TOP, beam=39, bottom=27, bilge=0.25, flare=8)
    hull = d.loft([(0, sec), (LEN - 1, sec)], HULL, wall=2, skin=SKIN, bow=70, tip=0.42, curve="ogive", anchor="deck")
    outer = hull.outer()
    d.restyle([p for p in outer if p[1] <= 3], tint=BELLY.tint)
    d.fill([p for p in outer if p[1] in (22, 23) and 4 <= p[2] <= LEN - 12], STRIPE, "blocks")
    d.fill([p for p in outer if p[1] == 25 and 4 <= p[2] <= LEN - 16], STRIPE_LINE, "blocks")
    return hull


def _sponson(d: ShipDesign, hull) -> None:
    """The angled-deck sponson: the flight deck bulges out over the port side amidships (rows 26..29), carried by
    braces from the hull side."""
    poly = solids.polygon([(CX - 23, 40), (CX - 31, 92), (CX - 31, 128), (CX - 23, 150)])
    for x, z in poly:
        r = hull.row(TOP, z)
        if r is None or x >= r[0]:
            continue
        d.put(x, TOP, z, FLIGHT)
        d.put(x, TOP - 1, z, HULL)
        d.put(x, TOP - 2, z, SKIN)
        if x <= CX - 29 or z % 12 == 0:
            d.put(x, TOP - 3, z, TRIM)
    for z in range(48, 146, 12):
        r = hull.row(17, z)
        if r is not None:
            d.line((r[0] - 1, 17, z), (CX - 29, TOP - 3, z), TRIM)


def _decks(d: ShipDesign, hull) -> None:
    d.deck(hull, DECK_C, DECK)
    d.deck(hull, DECK_B, DECK, z0=ENGINE_Z1 - 4)        # z 36..40 = the engine room's catwalk
    d.deck(hull, DECK_A, DECK)
    # Hangar floors in a lighter steel.
    d.fill([p for p in hull.row_cells(DECK_B) if p[2] > ENGINE_Z1], HANGAR_FLOOR, "blocks")
    d.fill([p for p in hull.row_cells(DECK_A) if WORKSHOP_Z1 < p[2] < HANGAR_A_END], HANGAR_FLOOR, "blocks")


def _bulkhead(d: ShipDesign, hull, z: int, y0: int, y1: int, m=WALL, x0: int = -99, x1: int = 999) -> None:
    """A wall across the hull's inside at z, rows y0..y1."""
    d.fill([(x, y, z) for y in range(y0, y1 + 1) for x in range(max(x0, CX - 30), min(x1, CX + 30) + 1)
            if hull.contains((x, y, z), 2)], m)


# --- deck C: the crew deck ------------------------------------------------------------------------------------------
def _crew_deck(d: ShipDesign, hull) -> dict:
    """A corridor down the middle (z 42..170) with rooms on both sides, a cross corridor at its forward end (to the
    forward shaft), the engine-room door at its aft end. Returns the rooms by name."""
    _bulkhead(d, hull, ENGINE_Z1 + 1, WALK_C, WALK_B + 6)               # the engine room's front wall (decks C + B)
    cor = d.corridor(hull, WALK_C, ENGINE_Z1 + 2, 170, width=3, height=4, walls=WALL, ceiling=WALL, light=LIGHT,
                     light_every=6)
    seg1 = d.rooms(hull, cor, per_side=2, depth=17, a0=ENGINE_Z1 + 2, a1=84, height=5, walls=WALL)
    seg2 = d.rooms(hull, cor, per_side=1, depth=17, a0=84, a1=126, height=5, walls=WALL)
    seg3 = d.rooms(hull, cor, per_side=2, depth=17, a0=126, a1=170, height=5, walls=WALL)
    d.corridor(hull, WALK_C, CX - 14, CX + 14, axis="x", centre=172, width=3, height=4, walls=WALL, ceiling=WALL,
               light=LIGHT, light_every=7)
    d.fill(solids.box(CX - 1, WALK_C, 170, CX + 1, WALK_C + 3, 170), CLEAR)  # the main corridor runs into it
    d.slide_door(CX - 1, WALK_C, ENGINE_Z1 + 1, "z")                      # corridor -> engine room
    left = [r for r in seg1 + seg2 + seg3 if r.side == "left"]
    right = [r for r in seg1 + seg2 + seg3 if r.side == "right"]
    names = dict(zip(("quarters_1", "quarters_2", "mess", "rec", "store"), left))
    names.update(zip(("quarters_3", "medbay", "galley", "stairs", "quarters_4"), right))
    # Portholes into the rooms, two cells square.
    d.window_band(hull, WALK_C + 1, WALK_C + 2, every=5, width=2, side="both", start=ENGINE_Z1 + 4, end=168)
    return names


# --- the engine room (decks C + B at the stern) ---------------------------------------------------------------------
def _engine_room(d: ShipDesign, hull) -> None:
    """Two decks tall (walk 7..19), z 2..40: three reactors with glowing bands, pipes to the engines in the stern, a
    console row and a catwalk along the front wall (z 36..40, row 12) with the door to the lower hangar."""
    d.slide_door(CX - 1, WALK_B, ENGINE_Z1 + 1, "z")                      # catwalk -> lower hangar
    rail = sc.pack("fence")
    for x in range(CX - 20, CX + 21):
        if hull.contains((x, WALK_B, ENGINE_Z1 - 5), 2) and not CX - 2 <= x <= CX + 1:
            d.put(x, WALK_B, ENGINE_Z1 - 5, Mat("steel_wall", shape=rail))
    band = Mat("strip_light_cyan")
    for rx in (CX - 11, CX, CX + 11):
        d.cylinder(rx, 18, WALK_C, DECK_A - 1, 3.5, Mat("machine_block"))
        d.cylinder(rx, 18, WALK_C, WALK_C, 4.5, Mat("engine_panel"))          # the base ring
        for y in (WALK_C + 3, WALK_C + 7):
            d.cylinder(rx, 18, y, y, 3.5, band, wall=1)
        d.tube([(rx, WALK_C + 2, 14), (rx, WALK_C + 2, 2)], Mat("factory_pipe"), radius=1.0, kind="poly")
        d.tube([(rx, WALK_C + 9, 22), (rx, WALK_C + 9, 30)], Mat("factory_pipe"), radius=1.0, kind="poly")
    for x in range(CX - 9, CX + 10, 4):                                   # consoles facing the reactors
        d.put(x, WALK_C, 29, Mat("factory_terminal"))
        d.put(x, WALK_C, 31, Mat("metal_panel", shape=sc.pack("chair", sc.yaw_toward(0, 1))))


# --- deck A: the workshop and the upper hangar ----------------------------------------------------------------------
def _upper_deck(d: ShipDesign, hull) -> None:
    """The workshop wall (z 39), the divider between the aft and the forward hangar (a wide opening in the middle),
    the hangar's front wall with the launch tunnel's mouth, the lower hangar's front wall, ceiling lights."""
    _bulkhead(d, hull, WORKSHOP_Z1 + 1, WALK_A, TOP - 2)
    d.slide_door(CX - 9, WALK_A, WORKSHOP_Z1 + 1, "z")
    d.slide_door(CX + 8, WALK_A, WORKSHOP_Z1 + 1, "z")
    for z in (HANGAR_A_DIV, HANGAR_A_DIV + 1):
        _bulkhead(d, hull, z, WALK_A, TOP - 2)
        d.fill(solids.box(CX - 7, WALK_A, z, CX + 7, WALK_A + 5, z), CLEAR)
    _bulkhead(d, hull, HANGAR_A_END, WALK_A, TOP - 2)
    _bulkhead(d, hull, HANGAR_B_END, WALK_B, DECK_A - 1)
    # Ceiling lights: the upper hangar in its roof (row 28), the lower hangar in deck A's plate (row 20).
    for z in range(WORKSHOP_Z1 + 6, HANGAR_A_END, 9):
        for x in range(CX - 16, CX + 17, 8):
            if d.is_block(x, TOP - 1, z):
                d.put(x, TOP - 1, z, LIGHT)
            if z > ENGINE_Z1 + 2 and z < HANGAR_B_END and d.is_block(x, DECK_A, z + 4):
                d.put(x, DECK_A, z + 4, LIGHT)
    for z in range(6, WORKSHOP_Z1, 8):                                    # workshop and engine room
        for x in (CX - 8, CX + 8):
            d.put(x, TOP - 1, z, LIGHT)
            d.put(x, DECK_A, z, LIGHT)


def _launch_tunnel(d: ShipDesign, hull) -> None:
    """A square tunnel 7 wide and 6 high (rows 21..26) from the forward hangar out through the bow: dark lining,
    cyan guide lights in the floor edges and the walls, a hazard-striped frame round the mouth."""
    x0, x1 = TUNNEL_X
    z_end = LEN - 1
    for z in range(HANGAR_A_END, z_end + 1):
        for y in range(DECK_A, TOP - 1):
            for x in range(x0 - 1, x1 + 2):
                inside = x0 <= x <= x1 and WALK_A <= y <= TOP - 3
                d.put(x, y, z, CLEAR if inside else DARK)
        if z % 3 == 0:
            d.put(x0, DECK_A, z, GUIDE)
            d.put(x1, DECK_A, z, GUIDE)
        if z % 6 == 0:
            d.put(x0 - 1, WALK_A + 2, z, GUIDE)
            d.put(x1 + 1, WALK_A + 2, z, GUIDE)
            d.put(CX, TOP - 2, z, LIGHT)
    for y in range(DECK_A - 1, TOP):                                      # the mouth's frame
        for x in range(x0 - 2, x1 + 3):
            if (x in (x0 - 2, x1 + 2) or y in (DECK_A - 1, TOP - 1)) and hull.contains((x, y, z_end)):
                d.put(x, y, z_end, YELLOW if (x + y) // 2 % 2 == 0 else BLACK)


# --- the flight deck ------------------------------------------------------------------------------------------------
def _flight_deck(d: ShipDesign, hull) -> None:
    """The top row in dark steel, then the markings: the axial line, the angled landing lane, landing rings, catapult
    slots, a touch-down threshold, edge lights, and the big lift's hatch with a hazard border."""
    top = [p for p in d.plain if p[1] == TOP and d.is_block(*p)]
    d.fill(top, FLIGHT)
    deck2d = {(x, z) for x, _, z in top}

    def paint(cells2d, m) -> None:
        d.fill([(x, TOP, z) for x, z in cells2d if (x, z) in deck2d], m)

    def dashed(x0: float, z0: float, x1: float, z1: float, m, on: int = 5, off: int = 3, thick: float = 1.0) -> None:
        n = max(abs(x1 - x0), abs(z1 - z0))
        cells = set()
        for i in range(int(n) + 1):
            if i % (on + off) < on:
                t = i / n
                cells |= solids.segment2d(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, x0 + (x1 - x0) * t,
                                          z0 + (z1 - z0) * t, thick)
        paint(cells, m)

    # The angled landing lane: a white dashed centre line, yellow edges, a red-and-white threshold at the stern.
    dashed(CX - 6, 6, CX - 23, 136, WHITE)
    paint(solids.segment2d(CX + 1, 6, CX - 16, 136, 1.0), YELLOW)
    paint(solids.segment2d(CX - 13, 6, CX - 30, 136, 1.0), YELLOW)
    for i, x in enumerate(range(CX - 12, CX + 1)):
        paint({(x, z) for z in range(8, 13)}, RED if i % 2 == 0 else WHITE)
    # Arresting wires across the landing lane (pale grey bars a little above its threshold).
    for z in (20, 26, 32, 38):
        lane = solids.segment2d(CX - 13 - (z - 6) * 17 / 130, z, CX + 1 - (z - 6) * 17 / 130, z, 1.0)
        paint(lane, Mat("concrete", tint=0xB8BEC6))
    # The axial launch line and two catapult slots on the forward deck, each with a blast deflector behind its start.
    dashed(CX - 4, 150, CX - 4, LEN - 4, WHITE)
    for cx in (CX - 11, CX + 4):
        paint({(cx, z) for z in range(168, LEN - 4)}, GUIDE)
        paint({(cx - 1, z) for z in range(168, 172)} | {(cx + 1, z) for z in range(168, 172)}, YELLOW)
        d.fill([(x, WALK_F, 166) for x in range(cx - 3, cx + 4)], Mat("metal_panel", tint=0x6A7078,
                                                                         shape=sc.ramp(0, -1)))
    # Landing rings on the starboard side, aft of the island.
    for z in (34, 62, 90):
        paint(solids.ring2d(CX + 13, z, 5, 1), YELLOW)
        paint({(CX + 13, z)}, YELLOW)
    # Edge lights every 5 cells along both deck edges (also round the sponson).
    for (x, z) in deck2d:
        if z % 5 == 0 and ((x - 1, z) not in deck2d or (x + 1, z) not in deck2d):
            d.put(x, TOP, z, EDGE_LIGHT)
    # The big lift: the hatch opens the deck down into the forward hangar; the platform rests at the hangar floor.
    x0, z0, x1, z1 = LIFT
    d.fill(solids.box(x0, TOP - 1, z0, x1, TOP, z1), CLEAR)
    d.fill(solids.box(x0, DECK_A, z0, x1, DECK_A, z1), Mat("lift_platform"))
    ring = [(x, z) for x in range(x0 - 1, x1 + 2) for z in range(z0 - 1, z1 + 2)
            if x in (x0 - 1, x1 + 1) or z in (z0 - 1, z1 + 1)]
    for x, z in ring:
        m = YELLOW if (x + z) // 2 % 2 == 0 else BLACK
        d.put(x, TOP, z, m)
        d.put(x, DECK_A, z, m)
    for x, z in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):          # the lift's motor posts in the hangar corners
        d.fill([(x, y, z) for y in range(WALK_A, TOP - 1)], Mat("machine_block"))


# --- the island -----------------------------------------------------------------------------------------------------
def _island(d: ShipDesign) -> None:
    """Five storeys on the starboard side (walk rows 30, 35, 40, 45, 50): flight control, the chart room, the crew
    rest, the radio room and, on top, the bridge with a glass band all round and an overhang; the mast house, a tall
    mast with two yards, a radar dish facing forward and antennas on the roof."""
    x0, z0, x1, z1 = ISLAND
    wall = Mix(Mat("iron_block"), (Mat("iron_block", tint=0x8E949C), 0.2), seed=5)
    d.fill(solids.box_walls(x0, WALK_F, z0, x1, BRIDGE_FLOOR - 1, z1), wall)
    for y in ISLAND_FLOORS:
        d.fill(solids.box(x0, y, z0, x1, y, z1), DECK)
    for y in (f - 3 for f in ISLAND_FLOORS + (BRIDGE_FLOOR,)):        # windows on all four sides
        for z in range(z0 + 2, z1 - 1):
            if (z - z0) % 3 != 2:
                d.fill([(x0, y, z), (x0, y + 1, z), (x1, y, z), (x1, y + 1, z)], GLASS)
        for x in range(x0 + 2, x1 - 1):
            if (x - x0) % 3 != 2:
                d.fill([(x, y, z0), (x, y + 1, z0), (x, y, z1), (x, y + 1, z1)], GLASS)
    d.slide_door(x0, WALK_F, 124, "x")                               # the flight deck doors
    d.slide_door(x0, WALK_F, 132, "x")
    # The bridge storey: one cell wider all round, two cells longer at both ends; a glass band on its middle rows.
    bx0, bx1, bz0, bz1 = x0 - 1, x1 + 1, z0 - 2, z1 + 2
    roof = BRIDGE_FLOOR + 5
    d.fill(solids.box(bx0, BRIDGE_FLOOR, bz0, bx1, BRIDGE_FLOOR, bz1), TRIM)
    d.fill(solids.box_walls(bx0, BRIDGE_FLOOR + 1, bz0, bx1, roof - 1, bz1), wall)
    band = [(x, y, z) for x, y, z in solids.box_walls(bx0, BRIDGE_FLOOR + 2, bz0, bx1, BRIDGE_FLOOR + 3, bz1)
            if not (x in (bx0, bx1) and z in (bz0, bz1))]
    d.fill(band, GLASS)
    d.fill(solids.box(bx0, roof, bz0, bx1, roof, bz1), TRIM)
    d.fill(solids.box(bx0, BRIDGE_FLOOR - 1, bz0, bx1, BRIDGE_FLOOR - 1, bz1), Mat("iron_block", tint=0x3E454E),
           "blocks")
    # A ladder from the bridge up through the roof (to the mast and a hidden cache).
    d.ladder(x1 - 1, bz0 + 1, BRIDGE_FLOOR + 1, roof)
    # The mast house, the mast with two yards and a red top light, a radar dish facing forward, antennas.
    mx0, mx1, mz0, mz1 = x0 + 4, x1 - 4, 122, 134
    d.fill(solids.box(mx0, roof + 1, mz0, mx1, roof + 4, mz1), TRIM)
    d.fill(solids.box(mx0 + 1, roof + 3, mz0 - 1, mx1 - 1, roof + 3, mz0 - 1), GLASS)
    mx = (mx0 + mx1) // 2
    post = Mat("iron_block", tint=0x4A5562, shape=sc.post())
    d.fill([(mx, y, 128) for y in range(roof + 5, MAST_TOP)], post)
    d.put(mx, MAST_TOP, 128, Mat("light_red"))
    for y, half in ((MAST_TOP - 6, 5), (MAST_TOP - 3, 3)):
        d.fill([(x, y, 128) for x in range(mx - half, mx + half + 1) if x != mx],
               Mat("iron_block", tint=0x4A5562, shape=sc.beam("x")))
        d.put(mx - half, y, 128, Mat("light_red"))
        d.put(mx + half, y, 128, Mat("light_green"))
    _dish(d, mx, roof + 8, mz1 + 1, 4, Mat("aluminium_block"), roof + 4)
    for ax, az, h in ((x0 + 2, z0 + 2, 5), (x1 - 2, z1 - 2, 4), (x0 + 2, z1 - 3, 3)):
        d.fill([(ax, y, az) for y in range(roof + 1, roof + 1 + h)], post)
        d.put(ax, roof + 1 + h, az, Mat("light_red"))


def _dish(d: ShipDesign, cx: int, cy: int, z0: int, r: int, m, base: int) -> None:
    """A radar dish facing +z: a shallow paraboloid of radius r whose rim lies forward of its centre, on a stub
    standing on row `base`."""
    for x in range(cx - r, cx + r + 1):
        for y in range(cy - r, cy + r + 1):
            rho2 = (x - cx) ** 2 + (y - cy) ** 2
            if rho2 <= r * r + 0.5:
                d.put(x, y, z0 + round(rho2 / (2.4 * r)), m)
    d.fill([(cx, y, z0 - 1) for y in range(base, cy + 1)], TRIM)
    d.put(cx, cy, z0 + 3, Mat("light_white", glow=0xFFF2D0))


# --- the ways up (all kept: the ruin pass never cuts a deck off) ----------------------------------------------------
def _shafts(d: ShipDesign) -> None:
    # The island shaft: deck C (stair hall) -> lower hangar -> upper hangar -> island ground floor -> bridge -> roof.
    # It runs on through the bridge roof: a hatch house with a doorway onto the roof, whose landing is kept too.
    d.lift_shaft(CX + 11, 140, [WALK_C, WALK_B, WALK_A, WALK_F, 35, 40, 45, BRIDGE_FLOOR + 1, BRIDGE_FLOOR + 6],
                 opening="+x", walls=WALL)
    # The forward shaft: deck C's cross corridor -> the lower hangar's front wall -> the forward upper hangar.
    d.lift_shaft(CX - 12, HANGAR_B_END + 1, [WALK_C, WALK_B, WALK_A], opening="-z", walls=WALL)
    # The aft shaft: engine room floor -> engine room catwalk -> workshop. The catwalk from its landing to the lower
    # hangar's door is kept too.
    d.lift_shaft(CX - 1, 34, [WALK_C, WALK_B, WALK_A], opening="+z", walls=WALL)
    d.keep([(x, DECK_B, z) for x in (CX - 1, CX) for z in range(36, ENGINE_Z1 + 1)])
    # The midships shaft (a crash often breaks the hull amidships): the galley -> lower hangar -> aft upper hangar.
    d.lift_shaft(CX + 8, 102, [WALK_C, WALK_B, WALK_A], opening="-z", walls=WALL)
    # A maintenance ladder from the store room down into the double bottom (rows 2..5).
    d.ladder(CX - 10, 160, 2, DECK_C)
    d.fill([(CX - 11, y, 160) for y in range(2, DECK_C)], WALL, keep=True)


# --- hull openings: the open deck-edge lift, closed hangar doors ----------------------------------------------------
def _port_lift_cells(hull) -> set:
    out = set()
    for z in range(PORT_LIFT[0] - 1, PORT_LIFT[1] + 2):
        r = hull.row(DECK_A, z)
        if r:
            out |= {(x, y, z) for x in range(r[0] - 8, r[0] + 1) for y in range(DECK_A - 2, TOP - 2)}
    return out


def _hull_openings(d: ShipDesign, hull) -> None:
    """The port deck-edge lift stands open (rows 21..25: a way in on a planet) with its platform outside at the
    upper hangar's floor; closed hangar doors (dark panels) line both sides of the lower hangar."""
    z0, z1 = PORT_LIFT
    for z in range(z0, z1 + 1):
        for y in range(WALK_A, WALK_A + 5):
            r = hull.row(y, z)
            x = r[0]
            while hull.contains((x, y, z)) and not hull.contains((x, y, z), 2):
                d.put(x, y, z, CLEAR)
                x += 1
        r = hull.row(DECK_A, z)
        for x in range(r[0] - 6, r[0] + 2):
            d.put(x, DECK_A, z, Mat("lift_platform") if z0 < z < z1 else YELLOW)
        d.put(r[0] - 7, DECK_A, z, YELLOW)
    for z in range(z0, z1 + 1, 5):                                       # struts under the platform
        r = hull.row(DECK_A - 4, z)
        d.line((r[0] - 1, DECK_A - 4, z), (r[0] - 6, DECK_A - 1, z), TRIM)
    outer = hull.outer()
    for side_x in ("left", "right"):
        for za in (52, 82, 112, 142):
            if side_x == "left" and za in (52, 82):
                continue  # under the deck-edge lift: the port side keeps its plain hull there
            cells = [p for p in outer if WALK_B <= p[1] <= WALK_B + 5 and za <= p[2] <= za + 15
                     and ((p[0] < CX) if side_x == "left" else (p[0] > CX))]
            d.fill(cells, DARK, "blocks")
            frame = [p for p in outer if p[1] == WALK_B + 6 and za <= p[2] <= za + 15
                     and ((p[0] < CX) if side_x == "left" else (p[0] > CX))]
            d.fill(frame, Mat("strip_light_warm"), "blocks")


# --- decoration: parked small ships ---------------------------------------------------------------------------------
LAYOUT_BLOCKS = {"engine": "engine_nozzle", "light": "light_white"}


def _layout(key: str) -> list[tuple[int, int, int, str]]:
    """The block cells of a shipped small-ship layout (data/ship_layouts/ship_<key>.json), moved to start at 0;
    builder-only ids are mapped to plain blocks; stations and doors are left out (an empty, packed-up ship)."""
    t = json.loads((LAYOUTS / f"ship_{key}.json").read_text(encoding="utf-8"))
    cells = [(c["x"], c["y"], c["z"], LAYOUT_BLOCKS.get(c["id"], c["id"])) for c in t["cells"] if c["kind"] == "block"]
    mx, my, mz = (min(c[i] for c in cells) for i in range(3))
    return [(x - mx, y - my, z - mz, b) for x, y, z, b in cells]


def _park(d: ShipDesign, key: str, x: int, y: int, z: int, tint: int, floor_y: int) -> tuple[int, int, int, int]:
    """Parks a small ship with its first cell at (x, y, z), its hull dyed `tint`; paints a yellow parking box on the
    floor row round it. Returns its footprint (x0, z0, x1, z1)."""
    cells = _layout(key)
    for cx, cy, cz, b in cells:
        if b == "iron_wall":
            m = Mat("iron_wall", tint=tint)
        elif b == "engine_nozzle":
            m = Mat("engine_nozzle")
        else:
            m = Mat(b)
        d.put(x + cx, y + cy, z + cz, m)
    x1 = x + max(c[0] for c in cells)
    z1 = z + max(c[2] for c in cells)
    for px in range(x - 1, x1 + 2):
        for pz in range(z - 1, z1 + 2):
            if (px in (x - 1, x1 + 1) or pz in (z - 1, z1 + 1)) and d.is_block(px, floor_y, pz)                     and not d.get(px, floor_y, pz)[5]:                   # a shaft's kept landing stays as it is
                d.put(px, floor_y, pz, YELLOW)
    return x, z, x1, z1


def _parked_ships(d: ShipDesign) -> None:
    # Upper hangar, aft half (z 40..105): two big ones on the starboard side, two small ones to port (the deck-edge
    # lift's mouth, z 60..75 on the port side, stays clear).
    _park(d, "hammerhead", CX + 3, WALK_A, 46, 0xE07B39, DECK_A)
    _park(d, "thunderbolt", CX + 5, WALK_A, 72, 0x2FA39A, DECK_A)
    _park(d, "scout", CX - 15, WALK_A, 84, 0xE8C547, DECK_A)
    _park(d, "courier", CX - 15, WALK_A, 44, 0xE6E6E6, DECK_A)
    # Upper hangar, forward half (z 108..177): the lift and the tunnel lane stay clear.
    _park(d, "corvette", CX - 18, WALK_A, 114, 0xC94C4C, DECK_A)
    _park(d, "deathblock", CX + 4, WALK_A, 112, 0x4C7BC9, DECK_A)
    _park(d, "courier", CX - 17, WALK_A, 132, 0x5BAF5B, DECK_A)
    # The lower hangar (z 42..173): two rows, a lane down the middle.
    _park(d, "hauler", CX - 16, WALK_B, 50, 0xE8C547, DECK_B)
    _park(d, "corvette", CX - 17, WALK_B, 78, 0x2FA39A, DECK_B)
    _park(d, "thunderbolt", CX - 17, WALK_B, 104, 0xE07B39, DECK_B)
    _park(d, "scout", CX - 15, WALK_B, 132, 0xC94C4C, DECK_B)
    _park(d, "hauler", CX + 6, WALK_B, 56, 0x4C7BC9, DECK_B)
    _park(d, "deathblock", CX + 4, WALK_B, 86, 0xE6E6E6, DECK_B)
    _park(d, "scout", CX + 8, WALK_B, 116, 0x5BAF5B, DECK_B)
    _park(d, "courier", CX + 6, WALK_B, 154, 0xE07B39, DECK_B)


# --- furniture ------------------------------------------------------------------------------------------------------
def _free_floor(d: ShipDesign, room, x: int, z: int) -> bool:
    """Whether (x, walk row, z) is an empty floor cell of the room, away from its door."""
    p = (x, room.y0, z)
    if p not in room.cells or d.get(*p) is not None or not d.is_block(x, room.y0 - 1, z):
        return False
    dx, _, dz = room.door
    return abs(x - dx) + abs(z - dz) > 3


def _far_x(room) -> int:
    """The x of the room's floor row farthest from the corridor."""
    return room.x0 if room.side == "left" else room.x1


def _along_wall(room, inset: int = 0) -> list[tuple[int, int]]:
    """(x, z) cells along the room's far wall (the hull side), `inset` cells in from it."""
    s = 1 if room.side == "left" else -1
    out = []
    for z in range(room.z0, room.z1 + 1):
        xs = [c[0] for c in room.floor if c[2] == z]
        if xs:
            x = (min(xs) if room.side == "left" else max(xs)) + s * inset
            out.append((x, z))
    return out


def _quarters(d: ShipDesign, room) -> None:
    """Bunks two high along the hull side, lockers between them, a small table with two chairs in the middle."""
    for i, (x, z) in enumerate(_along_wall(room)):
        if i % 4 in (1, 2) and _free_floor(d, room, x, z):
            d.put(x, room.y0, z, Mat("crew_bunk"))
            d.put(x, room.y0 + 2, z, Mat("crew_bunk"))
        elif i % 4 == 3 and _free_floor(d, room, x, z):
            d.put(x, room.y0, z, Mat("crate"))
    cx, _, cz = room.centre
    if _free_floor(d, room, cx, cz):
        d.put(cx, room.y0, cz, TABLE)
        for dz in (-1, 1):
            if _free_floor(d, room, cx, cz + dz):
                d.put(cx, room.y0, cz + dz, Mat("metal_panel", shape=sc.pack("chair", sc.yaw_toward(0, dz))))


def _mess(d: ShipDesign, room) -> None:
    """Long tables down the hall with benches on both sides, a gap in the middle to walk across."""
    s = -1 if room.side == "left" else 1
    for k in range(3):
        tx = CX + s * (6 + 4 * k)
        for z in range(room.z0 + 4, room.z1 - 3):
            if abs(z - (room.z0 + room.z1) // 2) <= 1:
                continue
            if _free_floor(d, room, tx, z):
                d.put(tx, room.y0, z, TABLE)
            for dx in (-1, 1):
                if _free_floor(d, room, tx + dx, z):
                    d.put(tx + dx, room.y0, z, Mat("wood_planks_light",
                                                   shape=sc.pack("bench", sc.yaw_toward(dx, 0))))


def _galley(d: ShipDesign, room) -> None:
    """Counters along the hull side, two stoves, cold stores, crates of supplies."""
    for i, (x, z) in enumerate(_along_wall(room)):
        if not _free_floor(d, room, x, z):
            continue
        if i % 9 in (0, 1):
            d.put(x, room.y0, z, Mat("insulated_wall"))
            d.put(x, room.y0 + 1, z, Mat("insulated_wall"))
        elif i % 9 == 4:
            d.put(x, room.y0, z, Mat("forge_off"))
        else:
            d.put(x, room.y0, z, Mat("metal_panel", shape=sc.pack("slab")))
    for i, (x, z) in enumerate(_along_wall(room, 4)):
        if i % 6 == 2 and _free_floor(d, room, x, z):
            d.put(x, room.y0, z, Mat("transport_crate"))


def _medbay(d: ShipDesign, room) -> None:
    for i, (x, z) in enumerate(_along_wall(room)):
        if not _free_floor(d, room, x, z):
            continue
        if i % 4 == 1:
            d.put(x, room.y0, z, Mat("stretcher"))
        elif i % 4 == 3:
            d.put(x, room.y0, z, Mat("medbay_panel"))
            d.put(x, room.y0 + 1, z, Mat("medbay_panel"))
    cx, _, cz = room.centre
    if _free_floor(d, room, cx, cz):                              # an empty healing tank: glowing glass on a base
        d.put(cx, room.y0, cz, Mat("metal_panel"))
        d.put(cx, room.y0 + 1, cz, TANK)
        d.put(cx, room.y0 + 2, cz, Mat("metal_panel", shape=sc.pack("slab")))


def _rec(d: ShipDesign, room) -> None:
    """The rec room: rugs, benches, a games corner with a computer."""
    cx, _, cz = room.centre
    for x in range(cx - 3, cx + 4):
        for z in range(cz - 4, cz + 5):
            if (x, room.y0, z) in room.cells and d.is_block(x, room.y0 - 1, z):
                d.put(x, room.y0 - 1, z, Mat("rug", tint=0x6A8FD0))
    wall = _along_wall(room)
    if len(wall) > 6:
        x, z = wall[2]
        if _free_floor(d, room, x, z) and _free_floor(d, room, x, z + 1):
            d.put(x, room.y0, z, Mat("gaming_pc"))
            d.put(x, room.y0 + 1, z + 1, Mat("gaming_monitor"))
            d.put(x, room.y0, z + 1, TABLE)
    for x, z in wall[6:-2:3]:
        if _free_floor(d, room, x, z):
            d.put(x, room.y0, z, Mat("wood_planks_light", shape=sc.pack("bench", sc.yaw_toward(1 if room.side == "left"
                                                                                            else -1, 0))))
    for x, z in (wall[0], wall[-1]):
        if _free_floor(d, room, x, z):
            d.put(x, room.y0, z, Mat("flower_pot"))


def _store(d: ShipDesign, room) -> None:
    for i, (x, z) in enumerate(_along_wall(room)):
        if _free_floor(d, room, x, z) and i % 5 != 4:
            d.put(x, room.y0, z, Mat("crate" if i % 2 else "transport_crate"))
            if i % 3 == 0:
                d.put(x, room.y0 + 1, z, Mat("crate"))


def _furniture(d: ShipDesign, rooms: dict) -> None:
    for name in ("quarters_1", "quarters_2", "quarters_3", "quarters_4"):
        if name in rooms:
            _quarters(d, rooms[name])
    _mess(d, rooms["mess"])
    _galley(d, rooms["galley"])
    _medbay(d, rooms["medbay"])
    _rec(d, rooms["rec"])
    _store(d, rooms["store"])
    _workshop(d)
    _island_rooms(d)
    _hangar_clutter(d)


def _workshop(d: ShipDesign) -> None:
    """Deck A at the stern: workbenches to port, fabricator casings (a dyed block under a glowing screen) and machines
    to starboard, crates, a scout in for repairs."""
    for z in (6, 12, 18, 24):
        d.put(CX - 17, WALK_A, z, Mat("workbench"))
        if z % 12:
            d.put(CX + 17, WALK_A, z, CASING)
            d.put(CX + 17, WALK_A + 1, z, SCREEN)
        else:
            d.put(CX + 17, WALK_A, z, Mat("machine_block"))
    for z in (8, 20):
        d.fill(solids.box(CX - 6, WALK_A, z, CX - 4, WALK_A + 1, z + 2), Mat("machine_block"))
        d.put(CX - 3, WALK_A, z + 1, Mat("factory_terminal"))
    for x in range(CX + 4, CX + 13, 2):
        d.put(x, WALK_A, 4, Mat("transport_crate"))
        d.put(x, WALK_A + 1, 4, Mat("crate"))
    _park(d, "scout", CX + 4, WALK_A, 14, 0xB8BCC4, DECK_A)                    # a scout in for repairs


def _island_rooms(d: ShipDesign) -> None:
    x0, z0, x1, z1 = ISLAND
    shaft = range(138, 144)                                          # the island shaft (z 139..142) stays clear
    # Flight control (ground floor): consoles at the port windows looking over the flight deck.
    for z in range(z0 + 3, z1 - 2, 4):
        if z in shaft or z in (124, 125, 132, 133):
            continue
        d.put(x0 + 1, WALK_F, z, Mat("factory_terminal"))
        d.put(x0 + 2, WALK_F, z, Mat("metal_panel", shape=sc.pack("chair", sc.yaw_toward(1, 0))))
    # The chart room (storey 1): a big table.
    d.fill(solids.box(x0 + 5, 35, z0 + 8, x0 + 8, 35, z0 + 16), TABLE)
    # The crew rest (storey 2): bunks along the starboard wall.
    for z in range(z0 + 2, z1 - 1, 3):
        if z not in shaft:
            d.put(x1 - 1, 40, z, Mat("crew_bunk"))
    # The radio room (storey 3): screens along the starboard wall, a desk.
    for z in range(z0 + 2, z1 - 1, 2):
        if z not in shaft:
            d.put(x1 - 1, 45, z, SCREEN)
    d.fill(solids.box(x0 + 4, 45, z0 + 6, x0 + 5, 45, z0 + 12), TABLE)
    # The bridge: consoles along the front and the port glass, the captain's chair.
    by = BRIDGE_FLOOR + 1
    for x in range(x0 + 1, x1, 2):
        d.put(x, by, z1 + 1, SCREEN)
    for z in range(z0, z1 + 1, 3):
        if z % 2 and z not in shaft:
            d.put(x0, by, z, Mat("factory_terminal"))
    d.put((x0 + x1) // 2, by, z1 - 3, Mat("metal_panel", shape=sc.pack("chair", sc.yaw_toward(0, 1))))
    d.put(x0 + 4, by, z0 + 6, Mat("lab_panel"))


def _hangar_clutter(d: ShipDesign) -> None:
    """Crates and parts left in the hangars; tool carts beside the parked ships."""
    for x, y, z in ((CX - 18, WALK_A, 98), (CX - 18, WALK_A, 100), (CX + 17, WALK_A, 168), (CX + 17, WALK_A, 170),
                    (CX - 5, WALK_B, 68), (CX + 17, WALK_B, 162), (CX + 16, WALK_B, 164), (CX - 18, WALK_B, 150)):
        d.put(x, y, z, Mat("transport_crate"))
        d.put(x, y + 1, z, Mat("crate"))
    for x, y, z in ((CX - 4, WALK_A, 60), (CX + 2, WALK_A, 128), (CX - 4, WALK_B, 120), (CX + 2, WALK_B, 70)):
        d.put(x, y, z, Mat("machine_block"))


# --- engines --------------------------------------------------------------------------------------------------------
def _engines(d: ShipDesign) -> None:
    """Three big dark bells and two smaller ones out of the flat stern, a dark engine panel round them."""
    stern = [p for p in d.plain if p[2] == 0 and d.is_block(*p) and 3 <= p[1] <= TOP - 3
             and abs(p[0] - CX) <= 20]
    d.fill(stern, Mat("engine_panel"))
    for x in (CX - 13, CX, CX + 13):
        d.nozzle(x, 14, 0, 6.5, 10, m=Mat("engine_nozzle"), glow=ENGINE_GLOW)
    for x in (CX - 17, CX + 17):
        d.nozzle(x, 24, 0, 3.5, 6, m=Mat("engine_nozzle"), glow=ENGINE_GLOW)


# --- loot, the ways in ----------------------------------------------------------------------------------------------
def _loot_and_ways_in(d: ShipDesign, hull, rooms: dict) -> None:
    # The FIRST entrance is the boarding arrival in space: the forward upper hangar, by the launch tunnel.
    d.entrance(CX + 6, WALK_A, 170)
    # On a planet: the open deck-edge lift on the port side and the launch tunnel's mouth in the bow.
    d.entrance(hull.row(WALK_A, 68, 2)[0], WALK_A, 68)
    d.entrance(CX, WALK_A, LEN - 3)

    def chest(p, hidden: bool = False, block: str | None = None) -> None:
        d.chest(*p, table=TABLE_CACHE if hidden else TABLE_CARRIER, hidden=hidden, block=block)

    chest((CX - 17, WALK_A, 4), block="crate")                    # workshop
    chest((CX + 17, WALK_A, 30))                                  # workshop
    chest((CX - 18, WALK_A, 96), block="crate")                   # upper hangar, aft
    chest((CX + 17, WALK_A, 166), block="crate")                  # upper hangar, forward
    chest((CX + 5, WALK_B, 100), block="crate")                   # lower hangar, by the midships shaft
    chest((CX + 17, WALK_B, 160))                                 # lower hangar
    chest((CX + 14, BRIDGE_FLOOR + 1, 143))                       # the bridge, by the shaft's landing
    chest((CX + 16, 35, 137))                                     # the chart room, by the shaft's landing
    chest((CX + 15, WALK_C, 30))                                  # engine room
    chest(rooms["quarters_1"].corner(1))
    chest(rooms["quarters_4"].corner(2))
    chest(rooms["mess"].corner(3))
    chest(rooms["galley"].corner(2))
    chest(rooms["medbay"].corner(1))
    # The ambience: the creaks of a big empty hull in both hangars.
    d.sound(CX, WALK_A + 2, 75, "amb_giant_hull", radius=72, volume=0.5)
    d.sound(CX, WALK_B + 2, 140, "amb_giant_hull", radius=56, volume=0.4)
    # Hidden caches (a stone on top): down in the double bottom, on the island roof beside the shaft's hatch house,
    # just inside the launch tunnel.
    chest((CX - 6, 2, 160), hidden=True)
    chest((CX + 15, BRIDGE_FLOOR + 6, 141), hidden=True)
    chest((TUNNEL_X[1], WALK_A, HANGAR_A_END + 2), hidden=True)
