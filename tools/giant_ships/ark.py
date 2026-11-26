"""The terraforming ark (#3621): a broad white-and-green ship of the old survey fleet that carried plants and young
animals to new worlds. Three big greenhouse halls under green glass — a crop hall under a dome, an orchard under a long
glass vault and a blossom garden with a pond under the biggest dome — sit on a wide, rounded hull. Inside: the tank hall
with rows of tall glowing growth tanks (decoration only: they grew seedlings and young animals), the seed vault, a lab,
crew quarters, animal pens, the engine room aft and the bridge in the bow under a small observation dome.

    uv run --no-project python tools/gen_giant_ships.py --only ark --out artifacts/gs/ships --preview

Coordinates: x across (centre line x = CX), y up, z along; the BOW points to +z.

Decks (plates on rows 5, 11, 17, 24; people walk one row higher):
- deck 1 (walk 6):  the engine room aft (two decks tall), the SEED VAULT (port, with its airlock) and a store, the
  HANGAR (starboard, two decks tall, the boarding arrival), the TANK HALL (two decks tall), the animal pens
- deck 2 (walk 12): the lab, a catwalk across the tank hall, crew quarters
- deck 3 (walk 18): mess, quarters, offices and stores along one long corridor
- deck 4 (walk 25): the three greenhouse halls in a row, joined by a path, small tool sheds between them; the bridge
Three ladder shafts (kept) on the port side join all four decks between the greenhouses; a fourth climbs from deck 3's
bow end to the bridge.

The greenhouse floor is the deck plate itself made of soil (grass and dirt), so the plants stand on row 25 like the
people; water channels and the pond are one cell deep with a sand bed below. The growth tanks are glass cylinders with
glowing green glass inside — never clone or heal tanks.
"""
from __future__ import annotations

import math

from ships import CLEAR, Mat, ShipDesign, sc, ship_section, solids

CX = 44                          # centre line: the hull is 89 wide (x 0..88)

# --- colours ----------------------------------------------------------------------------------------------------------
WHITE = 0xECF0EA
PEARL = 0xD3DAD2                 # seams and plates
GREEN = 0x4E9C5A
DEEP_GREEN = 0x2F6E3D
LEAF_GLASS = 0xB4E6BC            # the greenhouse glass
TANK_GLOW = 0x3FBF6A

# --- materials --------------------------------------------------------------------------------------------------------
HULL = Mat("aluminium_block", tint=WHITE)
PLATE = Mat("aluminium_block", tint=PEARL)
STRIPE = Mat("aluminium_block", tint=GREEN)
SAGE = Mat("aluminium_block", tint=0x9CC7A0)
FRAME = Mat("titanium_block", tint=WHITE)
FRAME_GREEN = Mat("titanium_block", tint=DEEP_GREEN)
GH_GLASS = Mat("glass_clear", tint=LEAF_GLASS)
WINDOW = Mat("glass", tint=0x8CC8A0)
DECK = Mat("steel_floor")
WALL = Mat("steel_wall", tint=0xE4E8E2)
LIGHT = Mat("strip_light_warm")
SOFT_GREEN = Mat("light_green", glow=0x5FD08A)
SOLAR = Mat("glass", tint=0x2C4470, shape=sc.pack(sc.SLAB))
PATH = Mat("sandstone", tint=0xE6D8B0)
BOARD = Mat("wood_planks_light")
GRASS, DIRT, SAND, WATER = Mat("grass"), Mat("dirt"), Mat("sand"), Mat("water")

# --- rows and stations ------------------------------------------------------------------------------------------------
TOP = 32                          # the hull's top row (the roof is rows 31..32)
PLATES = (5, 11, 17, 24)
WALK = tuple(p + 1 for p in PLATES)
HEIGHTS = (5, 5, 6, 6)            # free rows per deck
ROOF = TOP - 1                    # the greenhouse domes stand on this row
HULL_Z0, HULL_Z1 = 6, 196
BOW = 44

ENGINE_Z = (8, 30)                # decks 1-2
VAULT_Z = (33, 76)                # deck 1, port
STORE_Z = (33, 49)                # deck 1, starboard
HANGAR_Z = (52, 74)               # decks 1-2, starboard
TANK_Z = (80, 150)                # decks 1-2
GREENHOUSES = {"crops": (22, 58), "orchard": (66, 106), "blossom": (114, 160)}   # deck 4 halls (inside)
ISLAND = (CX + 6, 0)              # x of the big tree's trunk on the pond island (z = the dome's centre)
BRIDGE_Z0 = 170
SHAFTS = (61, 109, 164)           # z of the port ladder shafts (between the greenhouses)
PORT_GAPS = ((8, 58), (65, 106), (113, 160), (168, 190))     # port room segments: the shafts sit in the gaps


def _skin(x: int, y: int, z: int):
    """Two-tone: a sage-green underside up to row 10, a deep green band along the flanks (rows 17..19), white plates
    with pearl seams every 10 cells above."""
    if 17 <= y <= 19:
        return STRIPE
    if y <= 10:
        return SAGE if (z % 10 or y < 10) else STRIPE
    if z % 10 == 0:
        return PLATE
    return PLATE if solids.hash01(x // 6, y // 4, z // 5, 21) < 0.12 else HULL


# --- hull, decks ------------------------------------------------------------------------------------------------------
def _hull(d: ShipDesign):
    """A broad, rounded hull: flat bottom, round bilges, a level top; narrower and higher at the stern, a round spoon
    bow whose keel rises toward the stem."""
    return d.loft([
        (HULL_Z0, ship_section(CX, keel=7, deck=TOP - 1, beam=67, bottom=36, bilge=0.6, flare=-6)),
        (24, ship_section(CX, keel=0, deck=TOP, beam=89, bottom=44, bilge=0.6, flare=-6)),
        (HULL_Z1 - BOW, ship_section(CX, keel=0, deck=TOP, beam=89, bottom=44, bilge=0.6, flare=-6)),
        (HULL_Z1, ship_section(CX, keel=8, deck=TOP, beam=89, bottom=36, bilge=0.6, flare=-6)),
    ], HULL, wall=2, skin=_skin, bow=BOW, tip=0.3, curve="ogive", anchor="deck")


def _decks(d: ShipDesign, h) -> None:
    tank = (0, TANK_Z[0], 88, TANK_Z[1])
    hangar = (CX + 3, HANGAR_Z[0], 88, HANGAR_Z[1])
    d.deck(h, PLATES[0], DECK)
    d.deck(h, PLATES[1], DECK, hatches=[tank, hangar], z0=ENGINE_Z[1] + 1)
    d.fill([(x, PLATES[1], z) for x in range(CX - 1, CX + 2) for z in range(TANK_Z[0] - 1, TANK_Z[1] + 2)], DECK)
    d.deck(h, PLATES[2], DECK)
    d.deck(h, PLATES[3], DECK)


def _inside_box(h, x0: int, x1: int, y0: int, y1: int, z0: int, z1: int) -> list:
    return [(x, y, z) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1) for z in range(z0, z1 + 1)
            if h.contains((x, y, z), None)]


def _hall(d: ShipDesign, h, x0: int, x1: int, y0: int, y1: int, z0: int, z1: int,
          doors: tuple = ()) -> list:
    """A big open hall: everything inside the box (clipped to the hull's inside) cleared, walls round it where the box
    edge is not the hull, and `door_slide` doorways (x, y, z, axis). Returns the hall's air cells."""
    air = _inside_box(h, x0, x1, y0, y1, z0, z1)
    ring = set(_inside_box(h, x0 - 1, x1 + 1, y0, y1, z0 - 1, z1 + 1)) - set(air)
    d.fill(air, CLEAR)
    d.fill(ring, WALL)
    for x, y, z, axis in doors:
        d.slide_door(x, y, z, axis)
    return air


def _rooms(d: ShipDesign, h) -> dict[str, list]:
    """Corridors with rooms on decks 1..4; the special halls are carved afterwards. Rooms by name for the furniture
    and the chests."""
    out: dict[str, list] = {}

    def cor(k: int, a0: int, a1: int):
        return d.corridor(h, WALK[k], a0, a1, width=3, height=HEIGHTS[k], walls=WALL, light=LIGHT, light_every=10)

    def side(c, sd: str, *segments: tuple[int, int, int]) -> list:
        return [r for a0, a1, n in segments
                for r in d.rooms(h, c, per_side=n, sides=sd, a0=a0, a1=a1, depth=40, walls=WALL)]
    # the decks end where the bow's rising keel crosses them (deck 1 at z 166, deck 2 at 179, deck 3 at 187)
    c = cor(0, ENGINE_Z[1] + 1, 166)
    out["store"] = side(c, "right", (STORE_Z[0], STORE_Z[1], 1))
    out["pens"] = side(c, "left", (153, 161, 1)) + side(c, "right", (153, 165, 1))
    c = cor(1, ENGINE_Z[1] + 1, 179)
    out["lab"] = side(c, "left", (33, 58, 2), (65, 76, 1)) + side(c, "right", (33, 49, 1))
    out["crew"] = side(c, "left", (153, 161, 1), (168, 178, 1)) + side(c, "right", (153, 178, 2))
    c = cor(2, 8, 187)
    port = side(c, "left", (8, 58, 3), (65, 106, 3), (113, 160, 3), (168, 186, 2))
    star = side(c, "right", (8, 58, 3), (60, 106, 3), (108, 160, 3), (162, 186, 2))
    out["mess"] = [port[3], star[3]]
    out["quarters"] = port[6:] + star[6:]
    out["offices"] = port[:3] + star[:3]
    out["stores3"] = port[4:6] + star[4:6]
    c = cor(3, 8, 194)
    out["sheds"] = (side(c, "left", (8, 20, 1)) + side(c, "right", (8, 20, 1), (60, 64, 1), (108, 112, 1),
                                                             (162, 168, 1)))
    return out


# --- the big halls on decks 1-2 ---------------------------------------------------------------------------------------
def _halls(d: ShipDesign, h) -> dict[str, list]:
    out = {}
    w1, w2 = WALK[0], WALK[1]
    out["engine"] = _hall(d, h, 0, 88, w1, w2 + 4, ENGINE_Z[0], ENGINE_Z[1], [(CX - 1, w1, ENGINE_Z[1] + 1, "z")])
    out["vault"] = _hall(d, h, 0, CX - 3, w1, w1 + 4, VAULT_Z[0], VAULT_Z[1], [(CX - 2, w1, 47, "x")])
    out["tank"] = _hall(d, h, 0, 88, w1, w2 + 4, TANK_Z[0], TANK_Z[1],
                        [(CX - 1, w1, TANK_Z[0] - 1, "z"), (CX - 1, w1, TANK_Z[1] + 1, "z"),
                         (CX - 1, w2, TANK_Z[0] - 1, "z"), (CX - 1, w2, TANK_Z[1] + 1, "z")])
    out["hangar"] = _hall(d, h, CX + 3, 88, w1, w2 + 4, HANGAR_Z[0], HANGAR_Z[1], [(CX + 2, w1, 63, "x")])
    # the deck 2 catwalk across the tank hall: its plate survived the clearing only outside; lay it again with rails
    d.fill([(x, PLATES[1], z) for x in range(CX - 1, CX + 2) for z in range(TANK_Z[0], TANK_Z[1] + 1)], DECK)
    rail = FRAME.shaped(sc.pack(sc.FENCE, yaw=1))
    d.fill([(x, w2, z) for x in (CX - 2, CX + 2) for z in range(TANK_Z[0], TANK_Z[1] + 1)], rail)
    return out


def _hangar(d: ShipDesign, h, air: list) -> tuple[int, int, int]:
    """The starboard hangar: a big green-framed opening in the hull side, a cargo floor with stripes, empty cradles
    where the shuttles stood. Returns the boarding-arrival cell."""
    y0 = WALK[0]
    opening = []
    for z in range(HANGAR_Z[0] + 2, HANGAR_Z[1] - 1):
        for y in range(y0, y0 + 8):
            x = h.row(y, z)[1]
            while h.contains((x, y, z)) and not h.contains((x, y, z), None):
                opening.append((x, y, z))
                x -= 1
    d.fill(opening, CLEAR)
    frame = [(x, y, z) for x, y, z in h.outer() if HANGAR_Z[0] + 1 <= z <= HANGAR_Z[1] - 1 and y0 - 1 <= y <= y0 + 8
             and x > CX and (z in (HANGAR_Z[0] + 1, HANGAR_Z[1] - 1) or y in (y0 - 1, y0 + 8))]
    d.fill(frame, FRAME_GREEN, "blocks")
    floor = [(x, PLATES[0], z) for x, y, z in air if y == y0]
    d.fill(floor, Mat("cargo_floor"))
    edge = max(x for x, _, _ in floor)
    d.fill([(x, y, z) for x, y, z in floor if x >= edge - 1 and (z // 2) % 2 == 0], Mat("cargo_floor", tint=0x6FBF6A))
    for cz in (HANGAR_Z[0] + 6, HANGAR_Z[1] - 6):
        for dx, dz in ((-3, -3), (3, -3), (-3, 3), (3, 3)):
            d.fill([(66 + dx, y0, cz + dz), (66 + dx, y0 + 1, cz + dz)], FRAME_GREEN.shaped(sc.post()))
    return (70, y0, (HANGAR_Z[0] + HANGAR_Z[1]) // 2)


def _tanks(d: ShipDesign) -> list:
    """The tank hall: rows of tall growth tanks on both sides of the aisle — a white base and cap, a clear glass
    cylinder, glowing green liquid inside; some hold a little plant on a soil plug, some stand drained. Returns the
    tank centres."""
    y0, y1 = WALK[0], WALK[1] + 3
    liquid = Mat("glass", tint=0x9CF0B8, glow=TANK_GLOW)
    centres = []
    for zc in range(TANK_Z[0] + 5, TANK_Z[1] - 3, 9):
        for dx in (8, 16, 24, 32):
            for sgn in (-1, 1):
                x = CX + sgn * dx
                k = len(centres)
                centres.append((x, zc))
                d.cylinder(x, zc, y0, y0, 2.6, FRAME)
                d.cylinder(x, zc, y0 + 1, y1 - 1, 2.6, Mat("glass_clear", tint=0xDFF5E6), wall=1)
                d.cylinder(x, zc, y1, y1, 2.6, FRAME)
                d.cylinder(x, zc, y1 + 1, PLATES[2] - 1, 0.8, FRAME_GREEN)           # the feed pipe to the ceiling
                if k % 5 != 3:                                                       # most still glow
                    d.cylinder(x, zc, y0 + 1, y1 - 2, 1.6, liquid)
                if k % 3 == 0:                                                       # a seedling on a soil plug
                    d.put(x, y0 + 1, zc, DIRT)
                    d.put(x, y0 + 2, zc, Mat("flora_fern" if k % 2 else "flora_herb"))
                d.put(x + sgn * 3, y0, zc, Mat("lab_panel"))                        # a little control desk
                if k % 2:
                    d.put(x + sgn * 3, y0 + 1, zc, SOFT_GREEN)
    return centres


def _vault(d: ShipDesign, air: list) -> None:
    """The seed vault: long racks of shelves (white posts, light wooden shelves) in aisles, crates at their ends."""
    y0 = WALK[0]
    xs = sorted({x for x, _, _ in air})
    cells = set(air)
    for x in range(xs[0] + 2, CX - 4, 4):
        for z in range(VAULT_Z[0] + 2, VAULT_Z[1] - 1):
            aisle = z in (VAULT_Z[0] + 22, VAULT_Z[0] + 23) or 38 <= z <= 43      # a cross aisle, the airlock aisle
            if aisle or (x, y0, z) not in cells or (x, y0 + 4, z) not in cells:
                continue
            post = (z - VAULT_Z[0]) % 5 == 2
            for y in range(y0, y0 + 4):
                if post:
                    d.put(x, y, z, FRAME.shaped(sc.post()))
                elif (y - y0) % 2 == 1:
                    d.put(x, y, z, BOARD.shaped(sc.pack(sc.SLAB)))
            if not post and solids.hash01(x, y0, z, 5) < 0.3:
                d.put(x, y0, z, Mat("flower_pot", shape=sc.pack(sc.POT)))
        for z in (VAULT_Z[0] + 1, VAULT_Z[1] - 1):
            if (x, y0, z) in cells:
                d.put(x, y0, z, Mat("wood_crate" if x % 8 else "crate"))
                if (x, y0 + 1, z) in cells and x % 3:
                    d.put(x, y0 + 1, z, Mat("wood_crate"))


def _engine_room(d: ShipDesign) -> None:
    """Engine blocks and pipes aft, a soft green status light on each."""
    y0 = WALK[0]
    for x in (CX - 18, CX - 6, CX + 6, CX + 18):
        d.box(x - 2, y0, 12, x + 2, y0 + 5, 22, "engine_panel", tint=PEARL)
        d.box(x - 2, y0 + 6, 12, x + 2, y0 + 6, 22, "titanium_block", tint=GREEN)
        d.put(x, y0 + 3, 23, SOFT_GREEN)
        d.capsule((x, y0 + 7, 17), (x, WALK[1] + 4, 17), 0.9, Mat("factory_pipe"))


def _pens(d: ShipDesign, rooms) -> None:
    """Animal pens on deck 1: a straw floor, a low fence across the room, a water trough — all empty, the animals
    went to their new worlds."""
    for r in rooms:
        d.fill([(x, PLATES[0], z) for x, y, z in r.cells if y == r.y0], Mat("peat"))
        mx = (r.x0 + r.x1) // 2
        gaps = {(r.z0 + r.z1) // 2} | ({r.door[2], r.door[2] + 1} if r.door else set())   # walk past at the door
        d.fill([(mx, r.y0, z) for z in range(r.z0, r.z1 + 1) if (mx, r.y0, z) in r.cells and z not in gaps],
               BOARD.shaped(sc.pack(sc.FENCE, yaw=1)))
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z0 + 4):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("titanium_block", tint=PEARL, shape=sc.pack(sc.SLAB)))


def _furnish(d: ShipDesign, rooms: dict) -> None:
    """Labs with benches and plant trays, quarters with beds, a mess with long tables, offices with desks, tool sheds
    with pots and crates."""
    wood = Mat("wood_planks_light")
    for r in rooms["lab"]:
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z1):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("lab_panel"))
        cx, _, cz = r.centre
        for z in range(cz - 3, cz + 4):
            if (cx, r.y0, z) in r.cells:
                d.put(cx, r.y0, z, Mat("hydro_tray"))
    for r in rooms["crew"] + rooms["quarters"]:
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z1 - 1, 3):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("bed"))
        cx, _, cz = r.centre
        d.put(cx, r.y0, cz, Mat("rug", tint=0x6FAF78, shape=sc.pack(sc.SHEET)))
    for r in rooms["mess"]:
        for x in range(r.x0 + 2, r.x1 - 1, 4):
            for z in range(r.z0 + 2, r.z1 - 1):
                if all((xx, r.y0, z) in r.cells for xx in (x - 1, x, x + 1)):
                    d.put(x, r.y0, z, wood.shaped(sc.pack(sc.TABLE)))
                    d.put(x - 1, r.y0, z, wood.shaped(sc.pack(sc.BENCH, yaw=3)))
                    d.put(x + 1, r.y0, z, wood.shaped(sc.pack(sc.BENCH, yaw=1)))
    for r in rooms["offices"]:
        cx, _, cz = r.centre
        d.put(cx, r.y0, cz, wood.shaped(sc.pack(sc.TABLE)))
        d.put(cx, r.y0, cz + 1, wood.shaped(sc.pack(sc.CHAIR)))
        d.put(cx + 1, r.y0, cz, Mat("flower_pot", shape=sc.pack(sc.POT)))
    for r in rooms["sheds"] + rooms["stores3"] + rooms["store"]:
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z1, 2):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("wood_crate") if z % 4 else Mat("flower_pot", shape=sc.pack(sc.POT)))


# --- the greenhouses --------------------------------------------------------------------------------------------------
def _dome_room(cx: float, cz: float, rx: float, rz: float, ry: float, x: int, z: int) -> float:
    """How many rows above the roof row a dome (centre cx, cz, radii rx / rz, height ry) leaves free over (x, z)."""
    q = ((x - cx) / rx) ** 2 + ((z - cz) / rz) ** 2
    return ry * math.sqrt(1 - q) if q < 1 else 0.0


def _greenhouse_halls(d: ShipDesign, h) -> dict[str, list]:
    """The three halls on deck 4 (the full width of the hull) with their glass roofs: the crop hall under a dome, the
    orchard under a long glass vault, the blossom garden under the big dome. The corridor runs through each as a path;
    doors where it enters and leaves."""
    out = {}
    y0, y1 = WALK[3], WALK[3] + HEIGHTS[3] - 1
    for name, (z0, z1) in GREENHOUSES.items():
        out[name] = _hall(d, h, 0, 88, y0, y1, z0, z1, [(CX - 1, y0, z0 - 1, "z"), (CX - 1, y0, z1 + 1, "z")])
        floor = [(x, PLATES[3], z) for x, y, z in out[name] if y == y0]
        d.fill(floor, GRASS)
        d.fill([(x, y, z) for x, y, z in floor if CX - 1 <= x <= CX + 1], PATH)
        for x, y, z in out[name]:
            if y == y1 and (z - z0) % 8 == 4 and (x - CX) % 10 == 5:
                d.put(x, y + 1, z, LIGHT)                                      # grow lights in the flat ceiling
    _crops_dome(d)
    _orchard_vault(d)
    _blossom_dome(d)
    _bridge(d, h)
    return out


def _crops_dome(d: ShipDesign) -> None:
    z0, z1 = GREENHOUSES["crops"]
    cz = (z0 + z1) / 2
    d.glass_dome(CX, cz, ROOF, 27, m=GH_GLASS, frame=FRAME, ribs=12, rings=2, ry=21, rz=17)
    # crop beds: dirt rows with crops, a water channel between every pair of beds
    crops = ["flora_cropgrain", "flora_cropberry", "flora_pumpkin", "flora_cropshroom", "flora_cropgrain"]
    for x in range(7, 82):
        p = (x - 7) % 6                                                        # bed bed bed, grass, water, grass
        if abs(x - CX) <= 3 or p in (3, 5):
            continue
        for z in range(z0 + 3, z1 - 2):
            if (z - z0) % 12 in (5, 6):
                continue                                                       # cross paths
            if p < 3:
                d.put(x, PLATES[3], z, DIRT)
                d.put(x, WALK[3], z, Mat(crops[((x - 7) // 6 + (z - z0) // 12) % len(crops)]))
            else:
                d.put(x, PLATES[3], z, WATER)
                d.put(x, PLATES[3] - 1, z, SAND)
    # little fruit trees along the low edges (under the flat roof: five rows at most)
    for z in range(z0 + 3, z1 - 1, 7):
        for x in (4, 84):
            _tree(d, x, WALK[3], z, trunk=2, crown=1.6, leaves="tree_leaves_b", fruit=True)


def _orchard_vault(d: ShipDesign) -> None:
    """The orchard: a long glass barrel vault with white ribs, end walls of glass; fruit trees in rows beside the path,
    flowers and clover between them, two water channels."""
    z0, z1 = GREENHOUSES["orchard"]
    rx, ry = 24.5, 21.0
    shell, inner = set(), set()
    for z in range(z0, z1 + 1):
        for x in range(math.floor(CX - rx), math.ceil(CX + rx) + 1):
            for y in range(ROOF, ROOF + math.ceil(ry) + 1):
                q = ((x - CX) / rx) ** 2 + ((y - ROOF) / ry) ** 2
                if q <= 1:
                    qi = ((x - CX) / (rx - 1)) ** 2 + ((y - ROOF) / (ry - 1)) ** 2
                    (inner if qi < 1 and z0 < z < z1 else shell).add((x, y, z))
    d.fill(inner, CLEAR)
    outline = {p for p in shell if any((p[0] + dx, p[1] + dy, p[2]) not in shell and (p[0] + dx, p[1] + dy, p[2])
                                       not in inner for dx, dy in ((1, 0), (-1, 0), (0, 1)))}
    ribs = {p for p in shell if (z0 < p[2] < z1 and (p[2] - z0) % 6 == 0) or p[1] == ROOF or p[0] == CX
            or (p[2] in (z0, z1) and p in outline)}
    d.fill(shell - ribs, GH_GLASS)
    d.fill(ribs, FRAME)
    flowers = ["flora_flower", "flora_daisy", "flora_tulip", "flora_lupine", "flora_bellflower", "flora_clover"]
    for z in range(z0 + 2, z1 - 1):
        for x in (CX - 3, CX + 3):
            d.put(x, PLATES[3], z, WATER)
            d.put(x, PLATES[3] - 1, z, SAND)
    for k, z in enumerate(range(z0 + 4, z1 - 2, 6)):
        for dx in (8, 15, 22):
            for sgn in (-1, 1):
                x = CX + sgn * dx
                room = _dome_room(CX, 0, rx, 1e9, ry, x, 0) + (HEIGHTS[3] - 1)
                _tree(d, x, WALK[3], z, trunk=min(6, int(room) - 6), crown=3.0,
                      leaves=("tree_leaves", "tree_leaves_c")[k % 2], fruit=True)
        for x in range(CX - 28, CX + 29):
            if abs(x - CX) > 4 and solids.hash01(x, 0, z + 3, 7) < 0.45:
                d.put(x, WALK[3], z + 3, Mat(flowers[(x + z) % len(flowers)]))


def _blossom_dome(d: ShipDesign) -> None:
    """The blossom garden: the biggest dome, a pond with lilies and a little island with one big blossom tree, a wooden
    bridge along the path, blossom trees round the pond, flower meadows."""
    z0, z1 = GREENHOUSES["blossom"]
    cz = (z0 + z1) / 2
    rx, ry, rz = 31.0, 30.0, 23.0
    d.glass_dome(CX, cz, ROOF, rx, m=GH_GLASS, frame=FRAME, ribs=16, rings=3, ry=ry, rz=rz)
    pond = {(x, z) for x in range(CX - 14, CX + 15) for z in range(z0, z1 + 1)
            if ((x - CX) / 13.5) ** 2 + ((z - cz) / 9.5) ** 2 <= 1}
    for x, z in pond:
        d.put(x, PLATES[3], z, WATER)
        d.put(x, PLATES[3] - 1, z, SAND)
        if solids.hash01(x, 0, z, 17) < 0.06 and abs(x - CX) > 2:
            d.put(x, WALK[3], z, Mat("flora_lily"))
    for x, z in {(x + dx, z + dz) for x, z in pond for dx in (-1, 0, 1) for dz in (-1, 0, 1)} - pond:
        d.put(x, PLATES[3], z, SAND)                                                # a sandy shore
    island = {(x, z) for x, z in pond if ((x - ISLAND[0]) / 4.6) ** 2 + ((z - cz) / 4.2) ** 2 <= 1}
    for x, z in island:
        d.put(x, PLATES[3], z, GRASS)
        d.plain.pop((x, WALK[3], z), None)                                         # no lily on the island
    pond -= island
    # the bridge over the pond along the path: planks a row up, rails
    for x, z in pond:
        if CX - 1 <= x <= CX + 1:
            d.put(x, WALK[3], z, BOARD.shaped(sc.pack(sc.SLAB)))
    for z in range(int(cz - 10), int(cz + 11)):
        if (CX, z) in pond:
            for x in (CX - 2, CX + 2):
                d.put(x, WALK[3], z, BOARD.shaped(sc.pack(sc.FENCE, yaw=1)))
    # the big tree on the island: a 2 x 2 trunk, a wide crown high under the dome
    tx, tz = ISLAND[0], int(cz)
    d.fill([(tx + i, WALK[3] + k, tz + j) for i in (0, 1) for j in (0, 1) for k in range(15)], Mat("wood_log"))
    for i, (dx, dz) in enumerate(((-1, 0), (2, 1), (0, -1), (1, 2))):                # roots
        d.put(tx + dx, WALK[3], tz + dz, Mat("wood_log", shape=sc.pack(sc.SLAB)) if i % 2 else Mat("wood_log"))
    d.fill(solids.ellipsoid(tx + 0.5, WALK[3] + 17, tz + 0.5, 7.5, 5.0, 7.0), Mat("blossom_leaves"), "empty")
    d.fill(solids.ellipsoid(tx + 0.5, WALK[3] + 21, tz + 0.5, 4.5, 3.0, 4.0), Mat("blossom_leaves"), "empty")
    # blossom trees in a ring round the pond, taller toward the middle of the dome
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        x, z = round(CX + 20 * math.cos(a)), round(cz + 15 * math.sin(a))
        room = _dome_room(CX, cz, rx, rz, ry, x, z) + (HEIGHTS[3] - 1)
        _tree(d, x, WALK[3], z, trunk=min(7, int(room) - 6), crown=3.4, leaves="blossom_leaves")
    flowers = ["flora_sunblossom", "flora_tulip", "flora_daisy", "flora_marigold", "flora_orchid", "flora_lupine",
               "flora_blossombush", "flora_berrybush"]
    for x in range(3, 86):
        for z in range(z0 + 1, z1):
            if (x, z) in pond or abs(x - CX) <= 2 or solids.hash01(x, 1, z, 19) > 0.16:
                continue
            if d.get(x, WALK[3], z) is None and d.is_block(x, PLATES[3], z):
                d.put(x, WALK[3], z, Mat(flowers[int(solids.hash01(x, 2, z, 23) * len(flowers))]))


def _tree(d: ShipDesign, x: int, y: int, z: int, trunk: int, crown: float, leaves: str, fruit: bool = False) -> None:
    """A small tree on grass: a wood_log trunk, a round crown of `leaves`, a few fruits under it."""
    trunk = max(1, trunk)
    d.put(x, y - 1, z, GRASS)
    d.fill([(x, y + k, z) for k in range(trunk)], Mat("wood_log"))
    cy = y + trunk + crown * 0.6
    d.fill(solids.ellipsoid(x, cy, z, crown, crown * 0.8, crown), Mat(leaves), "empty")
    if fruit:
        for dx, dz in ((1, 0), (-1, 1)):
            d.put(x + dx, y, z + dz, Mat("flora_berrybush" if dz else "flora_herb"))


def _bridge(d: ShipDesign, h) -> None:
    """The bridge in the bow on deck 4: consoles along the bow windows, chairs, a small observation dome above, a mast
    with a soft green beacon."""
    y0 = WALK[3]
    _hall(d, h, 0, 88, y0, y0 + HEIGHTS[3] - 1, BRIDGE_Z0, HULL_Z1, [(CX - 1, y0, BRIDGE_Z0 - 1, "z")])
    d.window_band(h, y0 + 1, y0 + 3, WINDOW, every=1, width=1, side="front")
    zf = h.z_extent(CX, y0, None)[1]
    for x in range(CX - 12, CX + 13):
        e = h.z_extent(x, y0, None)
        if e is not None and e[1] - 2 > BRIDGE_Z0:
            d.put(x, y0, e[1], Mat("lab_panel"))
    for x in range(CX - 6, CX + 7, 3):
        d.put(x, y0, zf - 4, FRAME.shaped(sc.pack(sc.CHAIR, yaw=2)))
    d.glass_dome(CX, 179, ROOF, 9, m=GH_GLASS, frame=FRAME, ribs=6, rings=1, ry=7)
    for y in range(ROOF + 8, ROOF + 18):
        d.put(CX, y, 179, FRAME.shaped(sc.post()))
    d.put(CX, ROOF + 18, 179, SOFT_GREEN)


# --- the outside ------------------------------------------------------------------------------------------------------
def _engines(d: ShipDesign) -> None:
    """Four engine bells in a square at the stern, green-rimmed, cold."""
    for x in (CX - 12, CX + 12):
        for y in (14, 25):
            d.nozzle(x, y, HULL_Z0 + 1, 5, 7, m=Mat("engine_nozzle"), glow=Mat("engine_panel", tint=PEARL))
            lip = {(xx, yy, HULL_Z0 + 1 - 7 + k) for k in (0, 1) for xx in range(x - 6, x + 7)
                   for yy in range(y - 6, y + 7) if 3.8 ** 2 < (xx - x) ** 2 + (yy - y) ** 2 <= 5.3 ** 2}
            d.fill(lip, STRIPE, "blocks")


def _roof(d: ShipDesign, h) -> None:
    """Solar arrays on the roof strips beside the domes, a green walkway edge, a few vents."""
    top = [(x, y, z) for x, y, z in h.outer() if y == TOP]
    for x, y, z in top:
        strip = 33 <= abs(x - CX) <= 41 and abs(x - CX) % 6 < 4 and z % 9 < 7
        if strip and 12 <= z <= 186 and d.get(x, y + 1, z) is None:
            d.put(x, y + 1, z, SOLAR)
    d.fill([(x, y, z) for x, y, z in top if abs(x - CX) in (31, 42)], STRIPE, "blocks")


def _bays(d: ShipDesign, h) -> None:
    """Empty lifeboat bays along both flanks: a recess where a lifeboat once docked, a green frame round it, two
    clamps — every boat is gone, the crew flew them home. Two masts with soft green lights on the stern roof."""
    for z0 in (40, 88, 128, 146):
        for side in (0, 1):
            frame = []
            for z in range(z0 - 1, z0 + 8):
                for y in range(20, 26):
                    r = h.row(y, z)
                    if r is None:
                        continue
                    x = r[side]
                    inner = (x + (1 if side == 0 else -1), y, z)
                    if z0 <= z < z0 + 7 and 21 <= y <= 24:
                        d.put(x, y, z, CLEAR)
                        d.put(*inner, Mat("aluminium_block", tint=0x8FA897))
                    else:
                        frame.append((x, y, z))
            d.fill(frame, FRAME_GREEN, "blocks")
            for z in (z0 + 1, z0 + 5):
                r = h.row(22, z)
                d.put(r[side] + (1 if side == 0 else -1), 22, z, FRAME.shaped(sc.beam("x")))
    for x in (CX - 20, CX + 20):
        for y in range(TOP + 1, TOP + 9):
            d.put(x, y, 14, FRAME.shaped(sc.post()))
        d.put(x, TOP + 9, 14, SOFT_GREEN)


def _windows(d: ShipDesign, h) -> None:
    for y in (WALK[1] + 1, WALK[2] + 1):
        d.window_band(h, y, y + 1, WINDOW, every=6, width=2, start=30, end=190)
    d.window_band(h, WALK[3] + 1, WALK[3] + 3, WINDOW, every=5, width=3, start=20, end=168)   # greenhouse side light


def _shafts(d: ShipDesign) -> None:
    """Three ladder shafts on the port side join all four decks; a fourth at deck 3's bow end climbs to the bridge."""
    for z in SHAFTS:
        d.lift_shaft(CX - 3, z, list(WALK), opening="+x", walls=WALL)
    d.lift_shaft(CX - 1, 184, [WALK[2], WALK[3]], opening="-z", walls=WALL)


def _chests(d: ShipDesign, rooms: dict, halls: dict) -> None:
    t = "giant_ark"
    y0 = WALK[0]

    def near(cells: list, x: int, z: int) -> tuple[int, int, int]:
        """The free floor cell of a hall nearest (x, z)."""
        return min((p for p in cells if p[1] == y0 and d.get(*p) is None),
                   key=lambda p: (abs(p[0] - x) + abs(p[2] - z), p))
    for x, z in ((0, VAULT_Z[0]), (CX, VAULT_Z[0]), (0, VAULT_Z[1]), (CX - 5, 55)):                  # seed vault
        d.chest(*near(halls["vault"], x, z), table=t)
    d.chest(*rooms["lab"][0].corner(1), table=t)                                   # lab
    d.chest(*rooms["lab"][-1].corner(0), table=t)
    d.chest(*near(halls["tank"], CX - 3, TANK_Z[0]), table=t)                      # tank hall
    d.chest(*near(halls["tank"], 88, TANK_Z[1]), table=t)
    d.chest(*near(halls["engine"], CX + 6, ENGINE_Z[0]), table=t)                  # engine room
    d.chest(*near(halls["hangar"], CX + 4, HANGAR_Z[1]), table=t, block="crate")   # hangar
    d.chest(*rooms["quarters"][1].corner(2), table=t)                              # quarters
    d.chest(*rooms["pens"][-1].corner(2), table=t, block="wood_crate")             # pens
    d.chest(CX + 8, WALK[3], 186, table=t)                                         # bridge
    # hidden caches: on the pond's far shore in the blossom garden, in a crop bed, in the aft tool shed
    zc = (GREENHOUSES["blossom"][0] + GREENHOUSES["blossom"][1]) // 2
    d.chest(CX + 26, WALK[3], zc, table=t)
    d.chest(ISLAND[0] + 3, WALK[3], zc + 1, table="giant_cache", hidden=True)
    d.chest(80, WALK[3], GREENHOUSES["crops"][0] + 2, table="giant_cache", hidden=True)
    d.chest(*rooms["sheds"][0].corner(0), table="giant_cache", hidden=True)


def build() -> ShipDesign:
    d = ShipDesign("ark", "Terraforming Ark", weight=1, groundRow=5, civilization="sps")
    h = _hull(d)
    _decks(d, h)
    rooms = _rooms(d, h)
    halls = _halls(d, h)
    d.entrance(*_hangar(d, h, halls["hangar"]))         # the FIRST entrance: the boarding arrival in space
    d.hull_door(h, "left", WALK[0], 40, frame=FRAME_GREEN)   # the seed vault's airlock
    d.hull_door(h, "right", WALK[0], 158, frame=FRAME_GREEN)  # into the animal pens
    _tanks(d)
    _vault(d, halls["vault"])
    _engine_room(d)
    _pens(d, rooms["pens"])
    _furnish(d, rooms)
    _greenhouse_halls(d, h)
    _shafts(d)
    _windows(d, h)
    _bays(d, h)
    _engines(d)
    _roof(d, h)
    _chests(d, rooms, halls)
    d.sound(CX, WALK[3] + 1, 137, "amb_giant_greenhouse", radius=56, volume=0.4)  # on the bridge over the pond
    d.sound(CX, WALK[2] + 2, 100, "amb_giant_hull", radius=56, volume=0.35)       # deck 3, the crew corridor
    d.greebles(h, seed=9, density=0.004, m=Mat("titanium_block", tint=PEARL), light=SOFT_GREEN,
               kinds=("box", "pipe"), faces=("left", "right"), region=(0, 8, 0, 90, TOP, 200))
    d.normalize()
    return d
