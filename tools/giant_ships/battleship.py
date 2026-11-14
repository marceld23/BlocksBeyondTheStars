"""The spine-cannon battleship (#3620): a long, heavy, long-decommissioned escort ship of the old survey fleet, built
around ONE huge main gun. The barrel is a walkable tunnel (9 wide, 7 high inside) that runs along the ship's spine from
the breech chamber in the aft citadel out past the bow to a flared, ringed muzzle.

    uv run --no-project python tools/gen_giant_ships.py --only battleship --out artifacts/gs/ships --preview

Coordinates: x across (the centre line is x = CX), y up, z along; the BOW points to +z.

Silhouette, stern to bow (the far view is one cell per 4^3 blocks, so the big steps come first):
- a wide, flat-bottomed LOWER HULL (rows 0..25) whose sides flare out from a narrow keel and rise straight, ending in a
  long raked knife prow;
- a narrower UPPER HULL (rows 20..37) on top of it, stepping down to a sloped FORWARD STEP;
- the aft CITADEL (rows 20..54) that houses the breech; on it a compact COMMAND BLOCK (the bridge, a wrap-round glass
  band) with an antenna array, a dish and a sensor dome on its roof;
- the light gunmetal BARREL with dark bands lying on the spine (rows 36..50), carried past the prow on two pylons;
- eight triple-barrelled TURRETS on raised mounts on the shoulders, RADIATOR BANKS (rows of thin heat fins) between
  them; five cold engine bells aft.

Inside (decks 6 rows apart; people walk one row above a plate):
- deck 1 (walk 7):  the reactor hall aft (two decks tall), holds amidships; hull doors to the ground on both sides
- deck 2 (walk 13): crew cabins; the side HANGAR amidships on the port side (two decks tall) = the boarding arrival
- deck 3 (walk 19): engineering aft, the armoury (empty racks) and the stores
- deck 4 (walk 25): offices in the citadel, the mess and the quarters in the upper hull
- deck 5 (walk 31): officers' rooms aft; the long gun gallery under the barrel with a ladder up into the barrel
- breech chamber (walk 37): the copper coil housing round the breech, a stair up into the barrel
- bridge (walk 56) in the command block, a hatch up to its roof (walk 63)
Four ladder shafts (all `keep`) join the decks: aft (up to the bridge), amidships, forward and in the bow; ladders join
the bridge and its roof, the gun gallery and the barrel, and deck 2 and the barrel through a pylon.

The look belongs to the SPS survey fleet (the carrier's family): blue-grey gunmetal plating with lighter grey panels, a
darker belly, a muted blue band with a light line along the flanks; copper and bronze only on machinery inside (the
breech coils, the reactor). No emblems, figures, letters or symbols, no franchise look. The ship was retired long ago;
everyone left it in good order.
"""
from __future__ import annotations

import math

from ships import CLEAR, Mat, ShipDesign, polygon, sc, shipgeom, solids

CX = 34                         # centre line (odd beam: a 3-wide corridor and the barrel floor sit centred)
WIDTH = 2 * CX + 1

# --- colours (tints): the SPS fleet's blue-grey family ---------------------------------------------------------------
GUNMETAL = 0x5A636D              # the hull's base plating
PANEL_LIGHT = 0x7E8892           # lighter grey panels
SEAM_TONE = 0x454D56             # seams every 12 cells
BELLY_TONE = 0x48515A            # the darker underside
FRAME_TONE = 0x3C4652            # frames, mounts, bands
STRIPE_TONE = 0x3E6A8E           # the muted blue band along the flanks
LINE_TONE = 0xC4C9CE             # the light line beside it
BARREL_TONE = 0x7C848D           # the barrel: a lighter gunmetal, so the gun reads from far away
SLATE = 0x40434B                 # interior racks and fittings
SOOT = 0x2A2D32                  # dark interior furniture
BRONZE = 0xA8703A                # machinery only (breech door, drum caps)
COPPER = 0xC0743E                # machinery only (breech coils, reactor rings)
EMBER = 0x6A3010                 # a faint, dying glow (coils, reactor)

# --- materials --------------------------------------------------------------------------------------------------------
HULL = Mat("iron_block", tint=GUNMETAL)
PLATE_B = Mat("iron_block", tint=PANEL_LIGHT)
SEAM = Mat("iron_block", tint=SEAM_TONE)
BELLY = Mat("iron_block", tint=BELLY_TONE)
FRAME = Mat("iron_block", tint=FRAME_TONE)
STRIPE = Mat("iron_block", tint=STRIPE_TONE)
LINE = Mat("iron_block", tint=LINE_TONE)
RADIATOR = Mat("titanium_block", tint=0xA9B0B7)
TRIM = Mat("bronze_block", tint=BRONZE)          # machinery accents inside only
COPPER_M = Mat("copper_block", tint=COPPER)
COIL_GLOW = Mat("copper_block", tint=COPPER, glow=EMBER)
BARREL = Mat("iron_block", tint=BARREL_TONE)
BARREL_DARK = Mat("iron_block", tint=SEAM_TONE)
DECK = Mat("steel_floor")
WALL = Mat("steel_wall")
CARGO = Mat("cargo_floor")
DEAD_RED = Mat("light_red_off")                 # navigation lights, long switched off: red to port ...
DEAD_GREEN = Mat("light_green_off")             # ... green to starboard
DEAD_WARM = Mat("strip_light_warm_off")
WARM = Mat("strip_light_warm")
GUIDE = Mat("strip_light_cyan", glow=0x2A6A78)
GLASS = Mat("glass", tint=0x41606E)
BRIDGE_GLASS = Mat("glass_clear", tint=0x9CC4D0)
PANEL = Mat("lab_panel")
ENGINE = Mat("engine_panel", tint=SLATE)
WOOD = Mat("wood_planks_mahogany")

# --- rows -------------------------------------------------------------------------------------------------------------
LOW_TOP = 25                      # the lower hull's top row (the shoulders people stand on are row 26)
UP_TOP = 37                       # the upper hull's top row
CIT_TOP = 54                      # the citadel's top row
PLATES = (6, 12, 18, 24, 30)      # deck plates; walk rows are one higher
WALK = tuple(p + 1 for p in PLATES)
BREECH_PLATE = 36
BRIDGE_WALK = 56
ROOF_WALK = 63                    # on the command block's roof
BARREL_Y = 43                     # the barrel's axis
BARREL_R = 7.5                    # its outer radius; the wall is 3 thick (bore radius 4.5)
BARREL_FLOOR = BARREL_Y - 3       # the plate inside the bore (walk row 41)
BREECH_Z, MUZZLE_Z = 40, 249      # the barrel's open breech end and its mouth

# --- z stations -------------------------------------------------------------------------------------------------------
LOW_Z0, LOW_Z1 = 8, 226           # the lower hull, stern to prow
PROW = 52                         # slices of the knife prow's taper
UP_Z0, UP_Z1 = 22, 184            # the upper hull
STEP_Z0, STEP_Z1 = 182, 198       # the forward step
CIT_Z0, CIT_Z1 = 12, 62           # the citadel
TOWER_Z0, TOWER_Z1 = 18, 44       # the command block
HANGAR_Z0, HANGAR_Z1 = 104, 124   # the port hangar (inside; walls at z0 - 1 and z1 + 1)
TURRETS = (77, 112, 144, 173)     # turret centres on both shoulders
RADIATORS = ((60, 70), (87, 104), (121, 137), (153, 166))     # radiator banks between the turrets (z ranges)


def _box_section(cx: float, y0: int, y1: int, half: float, top: float = 0.0):
    """A box section from row y0 to y1, `half` = the rim's half width, its upper edges chamfered by `top` cells: the
    stepped, architectural blocks of the ship."""
    yb, yt = y0 - 0.5, y1 + 0.5
    right = [(cx + half, yb), (cx + half, yt - top), (cx + half - top, yt)]
    pts = []
    for p in right + [(2 * cx - x, y) for x, y in reversed(right)]:
        if not pts or pts[-1] != p:
            pts.append(p)
    return polygon(pts)


def _lower_section(keel: float, half: float, bottom_half: float, flare: float):
    """The lower hull: a flat keel `2 * bottom_half` wide, sides flaring out over `flare` rows to `half`, then rising
    straight to the top row with a two-cell chamfer."""
    yb, yt = keel - 0.5, LOW_TOP + 0.5
    right = [(CX + bottom_half, yb), (CX + half, yb + flare), (CX + half, yt - 2), (CX + half - 2, yt)]
    return polygon(right + [(2 * CX - x, y) for x, y in reversed(right)])


def _skin(x: int, y: int, z: int):
    """The outer skin: long gunmetal plates (6 x 4 x 11 cells between the seams, some lighter grey) with a darker seam
    every 12 cells along the hull and a darker belly — plates, not speckle, so the file keeps long runs and the far view
    stays calm."""
    if z % 12 == 0:
        return SEAM
    if y <= 8:
        return BELLY
    return PLATE_B if solids.hash01(x // 6, y // 4, z // 12, 11) < 0.2 else HULL


def _block(z0: int, z1: int, y0: int, y1: int, half: float, top: float, **kw):
    """A straight box hull from z0 to z1 (see `_box_section`)."""
    return shipgeom.loft([(z0, _box_section(CX, y0, y1, half, top)), (z1, _box_section(CX, y0, y1, half, top))], **kw)


# --- the hull ---------------------------------------------------------------------------------------------------------
def _hulls(d: ShipDesign):
    """The main hull (lower hull | upper hull | forward step | citadel, one shell) and the command block."""
    lower = shipgeom.loft([
        (LOW_Z0, _lower_section(5, 26.5, 14.5, 8)),
        (24, _lower_section(0, 31.5, 17.5, 10)),
        (LOW_Z1 - PROW, _lower_section(0, 31.5, 17.5, 10)),
        (LOW_Z1, _lower_section(15, 31.5, 17.5, 4)),
    ], bow=PROW, tip=0.05, curve="linear", taper_axes="x")
    upper = _block(UP_Z0, UP_Z1, 20, UP_TOP, 18.5, 3)
    step = _block(STEP_Z0, STEP_Z1, 20, 31, 14.5, 2, bow=7, tip=0.55, curve="linear", anchor="keel", taper_axes="y")
    citadel = _block(CIT_Z0, CIT_Z1, 20, CIT_TOP, 20.5, 4)
    main = d.add_hull(lower | upper | step | citadel, HULL, wall=2, skin=_skin)
    tower = d.add_hull(_block(TOWER_Z0, TOWER_Z1, 54, 62, 12.5, 3), HULL, wall=2, skin=_skin)
    return main, tower


# --- decks, corridors and rooms --------------------------------------------------------------------------------------
def _decks(d: ShipDesign, h) -> None:
    hangar = [(3, HANGAR_Z0, CX - 3, HANGAR_Z1)]
    d.deck(h, PLATES[0], DECK)
    d.deck(h, PLATES[1], DECK, z0=57)                     # the reactor hall below is two decks tall
    d.deck(h, PLATES[2], DECK, hatches=hangar)            # the hangar is two decks tall
    d.deck(h, PLATES[3], DECK)
    d.deck(h, PLATES[4], DECK)
    d.deck(h, BREECH_PLATE, DECK, z0=CIT_Z0, z1=CIT_Z1)   # the breech chamber's floor (citadel only)


def _deck_rooms(d: ShipDesign, h) -> dict[str, list]:
    """Corridor + rooms on decks 1..5 (the hangar and the midships shaft leave gaps). Returns the rooms by name for
    the furniture and the chests."""
    out: dict[str, list] = {}
    kw = {"width": 3, "height": 5, "walls": WALL, "light": DEAD_WARM, "light_every": 8}
    rk = {"depth": 28, "walls": WALL}

    def side(c, sd: str, *segments: tuple[int, int, int]) -> list:
        return [r for a0, a1, n in segments for r in d.rooms(h, c, per_side=n, sides=sd, a0=a0, a1=a1, **rk)]
    # deck 1: holds amidships (the reactor hall aft stays open)
    c = d.corridor(h, WALK[0], 58, 190, **kw)
    out["holds"] = d.rooms(h, c, per_side=5, **rk)
    # deck 2: crew cabins; the hangar on the port side
    c = d.corridor(h, WALK[1], 58, 200, **kw)
    out["cabins"] = (side(c, "left", (58, HANGAR_Z0 - 2, 3), (HANGAR_Z1 + 2, 200, 5))
                     + side(c, "right", (58, 96, 3), (103, 200, 6)))
    # deck 3: engineering aft, the armoury and the stores amidships
    c = d.corridor(h, WALK[2], 14, 206, **kw)
    out["engineering"] = d.rooms(h, c, per_side=2, a0=14, a1=56, **rk)
    out["armoury"] = side(c, "left", (58, HANGAR_Z0 - 2, 2)) + side(c, "right", (58, 96, 2))
    out["stores"] = side(c, "left", (HANGAR_Z1 + 2, 206, 4)) + side(c, "right", (103, 206, 5))
    # deck 4: the citadel's offices aft, the mess and the quarters in the upper hull
    c = d.corridor(h, WALK[3], 14, 190, **kw)
    out["offices"] = d.rooms(h, c, per_side=2, a0=14, a1=58, **rk)
    out["mess"] = side(c, "left", (62, 96, 1)) + side(c, "right", (62, 96, 1))
    out["quarters"] = side(c, "left", (98, 176, 4)) + side(c, "right", (103, 176, 4))
    # deck 5: officers' rooms aft; amidships the open gun gallery (no corridor walls)
    c = d.corridor(h, WALK[4], 14, 60, **kw)
    out["officers"] = d.rooms(h, c, per_side=2, a0=14, a1=58, **rk)
    return out


def _hangar(d: ShipDesign, h) -> tuple[int, int, int]:
    """The port hangar: two decks tall, open to space through a big framed door in the hull side, a sliding
    door into the deck 2 corridor; empty landing cradles and a few crates. Returns the boarding-arrival cell."""
    y0, y1 = WALK[1], WALK[2] + 4
    zm = (HANGAR_Z0 + HANGAR_Z1) // 2
    inside = [(x, y, z) for x in range(CX - 2) for y in range(y0, y1 + 1)
              for z in range(HANGAR_Z0, HANGAR_Z1 + 1) if h.contains((x, y, z), None)]
    for z in (HANGAR_Z0 - 1, HANGAR_Z1 + 1):              # the end walls
        d.fill([(x, y, z) for x in range(CX - 1) for y in range(y0, y1 + 1) if h.contains((x, y, z), None)], WALL)
    d.fill([(CX - 2, y, z) for y in range(y0, y1 + 1) for z in range(HANGAR_Z0, HANGAR_Z1 + 1)], WALL)
    d.fill(inside, CLEAR)
    floor = [(x, PLATES[1], z) for x, y, z in inside if y == y0]
    d.fill(floor, CARGO)
    # the big door: through the port wall, rows y0..y0+8, a frame round it
    opening = []
    for z in range(HANGAR_Z0 + 2, HANGAR_Z1 - 1):
        for y in range(y0, y0 + 9):
            x = h.row(y, z)[0]
            while h.contains((x, y, z)) and not h.contains((x, y, z), None):
                opening.append((x, y, z))
                x += 1
    d.fill(opening, CLEAR)
    frame = [(x, y, z) for x, y, z in h.outer() if HANGAR_Z0 + 1 <= z <= HANGAR_Z1 - 1 and y0 - 1 <= y <= y0 + 9
             and x < CX and (z in (HANGAR_Z0 + 1, HANGAR_Z1 - 1) or y in (y0 - 1, y0 + 9))]
    d.fill(frame, LINE, "blocks")
    # hazard stripes at the open edge, two empty landing cradles, a few crates
    x_edge = min(x for x, _, _ in floor)
    d.fill([(x, y, z) for x, y, z in floor if x <= x_edge + 1 and (z // 2) % 2 == 0], Mat("cargo_floor", tint=0xC8A030))
    for cz in (HANGAR_Z0 + 5, HANGAR_Z1 - 5):
        for dx, dz in ((-3, -3), (3, -3), (-3, 3), (3, 3)):
            d.fill([(18 + dx, y0, cz + dz), (18 + dx, y0 + 1, cz + dz)], FRAME.shaped(sc.post()))
    for p in ((CX - 4, y0, HANGAR_Z0 + 1), (CX - 4, y0, HANGAR_Z0 + 2), (CX - 4, y0 + 1, HANGAR_Z0 + 1),
              (CX - 5, y0, HANGAR_Z0 + 1)):
        d.put(*p, Mat("crate"))
    d.slide_door(CX - 2, y0, zm, "x")
    return (10, y0, zm)


# --- the gun ---------------------------------------------------------------------------------------------------------
def _annulus(z0: int, z1: int, r0: float, r1: float) -> set:
    """The cells of a ring round the barrel's axis between radius r0 and r1, z0..z1."""
    out = set()
    for z in range(z0, z1 + 1):
        for x in range(math.floor(CX - r1), math.ceil(CX + r1) + 1):
            for y in range(math.floor(BARREL_Y - r1), math.ceil(BARREL_Y + r1) + 1):
                if r0 * r0 < (x - CX) ** 2 + (y - BARREL_Y) ** 2 <= r1 * r1:
                    out.add((x, y, z))
    return out


def _barrel(d: ShipDesign) -> None:
    """The spine cannon: the walkable barrel from the breech to the muzzle, its floor and lights, dark bands, the
    saddle it rests in, two pylons past the prow and the flared, ringed muzzle."""
    main = d.tube([(CX, BARREL_Y, BREECH_Z), (CX, BARREL_Y, MUZZLE_Z - 14)], BARREL, BARREL_R, hollow=3, kind="poly")
    bell = d.tube([(CX, BARREL_Y, MUZZLE_Z - 14), (CX, BARREL_Y, MUZZLE_Z)], BARREL,
                  [(0.0, BARREL_R), (0.45, BARREL_R + 0.6), (1.0, 11.0)], hollow=3, kind="poly")
    bore = main.bore | bell.bore
    # the walkway: a plate across the bore, solid below it; cyan guide lights along its edges, a few working lamps
    d.fill([p for p in bore if p[1] < BARREL_FLOOR], BARREL_DARK)
    d.fill([p for p in bore if p[1] == BARREL_FLOOR], CARGO)
    for z in range(BREECH_Z + 6, MUZZLE_Z - 2, 10):
        row = sorted(x for x, y, zz in bore if y == BARREL_FLOOR and zz == z)
        d.fill([(row[0], BARREL_FLOOR, z), (row[-1], BARREL_FLOOR, z)], GUIDE)
        d.put(CX, BARREL_Y + 5, z, WARM if (z - BREECH_Z) % 30 == 6 else DEAD_WARM)
    # dark bands (only where nothing else is), then the saddle fairing the barrel rests in
    for z0 in range(70, 232, 26):
        d.fill(_annulus(z0, z0 + 1, BARREL_R + 0.1, BARREL_R + 1.2), FRAME, "empty")
    fairing = []
    for z in range(CIT_Z1 + 1, UP_Z1 + 1):
        for y in range(UP_TOP + 1, BARREL_Y):
            half = 9.5 - (y - UP_TOP - 1) * 0.45
            fairing += [(x, y, z) for x in range(CX - 10, CX + 11) if abs(x - CX) <= half and (x, y, z) not in bore]
    d.fill(fairing, HULL, "empty")
    d.fill([(x, y, z) for x, y, z in fairing if y == UP_TOP + 1 and abs(x - CX) >= 8], STRIPE, "blocks")
    # two pylons carry the barrel past the prow
    for z0 in (204, 217):
        cells = []
        for y in range(LOW_TOP - 1, BARREL_Y - 6):
            half = 3 + max(0, (32 - y)) * 0.2
            cells += [(x, y, z) for x in range(CX - 6, CX + 7) for z in range(z0, z0 + 3) if abs(x - CX) <= half]
        d.fill(cells, HULL, "empty")
        d.fill([(x, y, z) for x, y, z in cells if y == BARREL_Y - 8], FRAME)
    # the ringed muzzle: two raised rings round the bell and a broad lip at the mouth
    d.fill(_annulus(MUZZLE_Z - 9, MUZZLE_Z - 8, 9.3, 10.4), FRAME, "empty")
    d.fill(_annulus(MUZZLE_Z - 4, MUZZLE_Z - 3, 10.3, 11.6), STRIPE, "empty")
    d.fill(_annulus(MUZZLE_Z - 1, MUZZLE_Z, 10.0, 12.6), FRAME)


def _breech(d: ShipDesign) -> None:
    """The breech chamber: copper coils round the barrel's back end with a faint dying glow, a swung-open breech door
    beside it, copper drums along the walls, and a stair (kept) up into the open breech."""
    for k, z in enumerate(range(BREECH_Z + 2, CIT_Z1 - 1, 3)):
        ring = {p for p in _annulus(z, z, BARREL_R - 0.2, BARREL_R + 2.2) if p[1] > BREECH_PLATE}
        d.fill(ring, COIL_GLOW if k % 2 else COPPER_M)
        d.fill(_annulus(z + 1, z + 2, BARREL_R - 0.2, BARREL_R + 1.0), TRIM, "empty")
    door = {(CX + 9, y, z) for y in range(BARREL_Y - 6, BARREL_Y + 7) for z in range(BREECH_Z - 13, BREECH_Z)
            if (y - BARREL_Y) ** 2 + (z - BREECH_Z + 7) ** 2 <= 36.5}
    d.fill(door, TRIM)
    d.fill({(CX + 10, y, z) for (_, y, z) in door if abs(y - BARREL_Y) <= 2 and abs(z - BREECH_Z + 7) <= 2}, COPPER_M)
    steps = BARREL_FLOOR - BREECH_PLATE                   # steps on rows 37..40, the last level with the floor
    z0 = BREECH_Z - steps - 1
    d.stairs(CX - 1, BREECH_PLATE + 1, z0, 0, 1, steps, DECK, width=3, fill_below=True)
    d.fill([(x, BARREL_FLOOR, BREECH_Z - 1) for x in range(CX - 1, CX + 2)], DECK)
    d.keep([(x, y, z) for x in range(CX - 1, CX + 2) for y in range(BREECH_PLATE + 1, BARREL_FLOOR + 1)
            for z in range(z0, BREECH_Z)])
    for z in (18, 26, 52):
        for x in (19, 49):
            d.cylinder(x, z, BREECH_PLATE + 1, BREECH_PLATE + 6, 2.5, COPPER_M)
            d.disc(x, z, BREECH_PLATE + 7, 2.5, TRIM)
            d.put(x, BREECH_PLATE + 4, z + (3 if z < 40 else -3), DEAD_RED)


# --- shafts, windows, doors ------------------------------------------------------------------------------------------
def _shafts(d: ShipDesign) -> None:
    """Four ladder shafts (kept through the ruin pass): aft from the reactor hall to the bridge, amidships and forward
    beside the corridor, and one in the bow for decks 1-3 (so a bow broken off in a crash keeps its stairs); a ladder
    from the bridge up to a hatch on the roof, and two up into the barrel (from the gun gallery, and from deck 2 through
    a pylon)."""
    d.lift_shaft(24, 21, [WALK[0], WALK[2], WALK[3], WALK[4], BREECH_PLATE + 1, BRIDGE_WALK], opening="+z", walls=WALL)
    d.lift_shaft(CX + 3, 99, list(WALK), opening="-x", walls=WALL)
    d.lift_shaft(CX - 3, 170, list(WALK), opening="+x", walls=WALL)
    d.lift_shaft(CX + 3, 186, list(WALK[:3]), opening="-x", walls=WALL)  # the bow's own shaft (decks 1-3)
    d.lift_shaft(CX - 1, 34, [BRIDGE_WALK, ROOF_WALK], opening="-z", walls=WALL)
    lx, lz = CX + 3, 140                                   # gun gallery -> barrel, a wall column behind the ladder
    d.fill([(lx + 1, y, lz) for y in range(WALK[4], UP_TOP - 1)], WALL, keep=True)
    d.fill([(lx, y, lz + 1) for y in range(WALK[4], UP_TOP - 1)], WALL, keep=True)
    d.ladder(lx, lz, WALK[4], BARREL_FLOOR)
    # the bow -> barrel: a ladder from deck 2 up inside the first pylon, so the muzzle half has its own way in
    pz = 205
    d.fill([(CX, y, pz + 1) for y in range(WALK[1], LOW_TOP + 1)], WALL, keep=True)
    d.ladder(CX, pz, WALK[1], BARREL_FLOOR)


def _windows(d: ShipDesign, h, tower) -> None:
    for y in (WALK[1] + 1, WALK[2] + 1):                  # portholes on decks 2 and 3
        d.window_band(h, y, y + 1, GLASS, every=6, width=1, start=60, end=200)
    d.window_band(h, WALK[3] + 1, WALK[3] + 2, GLASS, every=5, width=2, start=64, end=180)
    d.window_band(h, WALK[4] + 1, WALK[4] + 2, GLASS, every=4, width=1, start=64, end=180)
    d.window_band(h, 45, 46, GLASS, every=4, width=3, start=16, end=58)          # a window ribbon on the citadel
    for sd in ("both", "front", "back"):
        d.window_band(tower, 57, 59, BRIDGE_GLASS, every=1, width=1, side=sd)    # the bridge's wrap-round glass
    d.fill([(x, y, z) for x, y, z in tower.outer() if y == 56], LINE, "blocks")
    # the SPS band along the flanks: a muted blue stripe with a light line under it, and one round the citadel
    d.fill([(x, y, z) for x, y, z in h.outer() if y == LOW_TOP - 2 and LOW_Z0 + 4 <= z], STRIPE, "blocks")
    d.fill([(x, y, z) for x, y, z in h.outer() if y == LOW_TOP - 3 and LOW_Z0 + 4 <= z], LINE, "blocks")
    d.fill([(x, y, z) for x, y, z in h.outer() if y in (50, 51) and z <= CIT_Z1], STRIPE, "blocks")


# --- the outside: radiators, turrets, engines, the antenna array ------------------------------------------------------
def _radiators(d: ShipDesign, h) -> None:
    """Radiator banks on both shoulders between the turrets: rows of thin heat fins (planes across the ship, one cell
    apart, rising toward the upper hull) on a low base, rails along their tops — the cold heat sinks of the reactor and
    the gun."""
    base_y = LOW_TOP + 1
    for z0, z1 in RADIATORS:
        x_in = CX - 19                                     # against the upper hull's side
        x_out = h.row(LOW_TOP, (z0 + z1) // 2)[0] + 2       # two cells in from the shoulder's edge
        for sgn in (1, -1):
            def m(x: int, s: int = sgn) -> int:
                return x if s == 1 else 2 * CX - x
            d.fill([(m(x), base_y, z) for x in range(x_out, x_in + 1) for z in range(z0, z1 + 1)], FRAME)
            for z in range(z0 + 1, z1, 2):
                d.fill([(m(x), y, z) for x in range(x_out + 1, x_in + 1) for y in range(base_y + 1, base_y + 8)
                        if y - base_y <= 4 + (x - x_out) * 3 // max(1, x_in - x_out)], RADIATOR)
            d.fill([(m(x_out + 1), base_y + 5, z) for z in range(z0, z1 + 1)], FRAME.shaped(sc.beam("z")))
            d.fill([(m(x_in), base_y + 8, z) for z in range(z0, z1 + 1)], FRAME.shaped(sc.beam("z")))


def _turrets(d: ShipDesign) -> None:
    """Eight triple-barrelled turrets on raised mounts with a blue ring on the shoulders, all facing the bow; a dead
    navigation light on each (red to port, green to starboard)."""
    for cz in TURRETS:
        for cx in (10, 2 * CX - 10):
            d.cylinder(cx, cz, LOW_TOP + 1, LOW_TOP + 3, 5.5, FRAME)
            d.disc(cx, cz, LOW_TOP + 4, 5.5, STRIPE)
            d.turret(cx, LOW_TOP + 5, cz, r=5, barrels=3, length=15, barrel_r=1.0, spacing=2, facing="+z",
                     m=Mat("iron_block", tint=PANEL_LIGHT), barrel_m=BARREL)
            d.put(cx, LOW_TOP + 11, cz, DEAD_RED if cx < CX else DEAD_GREEN)


def _engines(d: ShipDesign) -> None:
    """Three big engine bells at the lower hull's stern and two smaller ones at the citadel's, with dark lips, long
    cold."""
    bells = [(x, 15, LOW_Z0 + 1, 7.0, 9) for x in (CX - 17, CX, CX + 17)]
    bells += [(x, 44, CIT_Z0 + 1, 4.5, 6) for x in (CX - 10, CX + 10)]
    for x, y, z, r, n in bells:
        d.nozzle(x, y, z, r, n, m=Mat("engine_nozzle"), glow=ENGINE)
        lip = {(xx, yy, z - n + k) for k in (0, 1) for xx in range(math.floor(x - r - 1), math.ceil(x + r) + 2)
               for yy in range(math.floor(y - r - 1), math.ceil(y + r) + 2)
               if (r - 1.3) ** 2 < (xx - x) ** 2 + (yy - y) ** 2 <= (r + 0.3) ** 2}
        d.fill(lip, FRAME, "blocks")


def _antennas(d: ShipDesign) -> None:
    """The command block's roof: an antenna array (short masts of different heights, some with cross-bars, a small ball
    on each), a dish looking up, a sensor dome; dead navigation lights on the corners."""
    roof = ROOF_WALK
    mast = FRAME.shaped(sc.post())
    for x, z, n, bar in ((CX - 8, 21, 6, True), (CX - 4, 22, 4, False), (CX + 8, 23, 7, True), (CX + 4, 41, 5, False),
                         (CX - 8, 40, 3, False), (CX + 8, 40, 4, True)):
        d.fill([(x, roof + k, z) for k in range(n)], mast)
        if bar:
            d.fill([(x + i, roof + n - 2, z) for i in (-1, 1)], FRAME.shaped(sc.beam("x")))
        d.put(x, roof + n, z, LINE.shaped(sc.pack(sc.SPHERE)))
    dx, dz = CX - 5, 31                                    # the dish: a short post, a shallow bowl
    d.fill([(dx, roof + k, dz) for k in range(2)], mast)
    d.disc(dx, dz, roof + 2, 2.2, LINE)
    d.ring(dx, dz, roof + 3, roof + 3, 3.2, 1, LINE)
    d.disc(CX + 5, 30, roof, 2.5, FRAME)                  # the sensor dome on a low ring
    d.dome(CX + 5, 30, roof + 1, 2.5, Mat("glass", tint=0x5A7A90))
    for x, z in ((CX - 9, TOWER_Z0 + 1), (CX - 9, TOWER_Z1 - 1)):
        d.put(x, roof, z, DEAD_RED)
    for x, z in ((CX + 9, TOWER_Z0 + 1), (CX + 9, TOWER_Z1 - 1)):
        d.put(x, roof, z, DEAD_GREEN)


# --- interiors -------------------------------------------------------------------------------------------------------
def _reactor(d: ShipDesign) -> None:
    """The reactor hall aft on decks 1-2: a big round reactor with copper rings and a faintly glowing core behind
    glass, pipes along the floor."""
    cz = 38
    y0, y1 = WALK[0], PLATES[2] - 1
    d.cylinder(CX, cz, y0, y1, 6, ENGINE)
    for y in range(y0 + 1, y1, 3):
        d.cylinder(CX, cz, y, y, 6.6, COPPER_M)
    d.cylinder(CX, cz, y0 + 3, y0 + 7, 6, BRIDGE_GLASS, wall=1)
    d.cylinder(CX, cz, y0, y1, 5, Mat("glass", tint=0xD07030, glow=EMBER))
    for x in (CX - 14, CX + 14):
        d.capsule((x, y0, 20), (x, y0, 54), 1.2, Mat("factory_pipe"))


def _armoury(d: ShipDesign, rooms) -> None:
    """Empty weapon racks along the armoury walls: posts with a beam on top, nothing on them."""
    rack = Mat("iron_block", tint=SLATE)
    for r in rooms:
        far = r.x0 if r.side == "left" else r.x1
        for x in (far, far + (3 if r.side == "left" else -3)):
            for z in range(r.z0 + 1, r.z1, 2):
                if (x, r.y0, z) in r.cells:
                    d.put(x, r.y0, z, rack.shaped(sc.post()))
                    d.put(x, r.y0 + 1, z, rack.shaped(sc.post()))
                    d.put(x, r.y0 + 2, z, rack.shaped(sc.beam("z")))


def _cabins(d: ShipDesign, rooms) -> None:
    """Packed-up cabins: beds along the far wall, a rug and a small table; nothing left behind."""
    for k, r in enumerate(rooms):
        far = r.x0 if r.side == "left" else r.x1
        inner = far + (1 if r.side == "left" else -1)
        for z in range(r.z0 + 1, r.z1 - 1, 3):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("bed"))
        cx, _, cz = r.centre
        d.put(cx, r.y0, cz, WOOD.shaped(sc.pack(sc.TABLE)))
        if k % 2 == 0 and (inner, r.y0, r.z0 + 1) in r.cells:
            d.put(cx + (2 if r.side == "left" else -2), r.y0 - 0, cz, Mat("rug", tint=0x7A3A30))


def _mess(d: ShipDesign, rooms) -> None:
    """The mess: long tables with benches."""
    for r in rooms:
        for x in range(r.x0 + 2, r.x1 - 1, 4):
            for z in range(r.z0 + 2, r.z1 - 1):
                if all((xx, r.y0, z) in r.cells for xx in (x - 1, x, x + 1)):
                    d.put(x, r.y0, z, WOOD.shaped(sc.pack(sc.TABLE)))
                    d.put(x - 1, r.y0, z, WOOD.shaped(sc.pack(sc.BENCH, yaw=3)))
                    d.put(x + 1, r.y0, z, WOOD.shaped(sc.pack(sc.BENCH, yaw=1)))


def _holds(d: ShipDesign, rooms) -> None:
    """Holds and stores: stacks of crates along the far wall, most of them emptied long ago."""
    for k, r in enumerate(rooms):
        far = r.x0 if r.side == "left" else r.x1
        step = 1 if r.side == "left" else -1
        for i in range(3):
            for z in range(r.z0 + 1, r.z1, 1):
                if (far + step * i, r.y0, z) in r.cells and solids.hash01(far, i, z, 3 + k) < 0.55:
                    hgt = 1 + int(solids.hash01(far, i, z, 9 + k) * (3 - i))
                    blk = "crate" if (z + i) % 3 else "transport_crate"
                    d.fill([(far + step * i, r.y0 + j, z) for j in range(hgt)], Mat(blk))


def _offices(d: ShipDesign, rooms) -> None:
    """Offices and the officers' rooms: a desk with a console, a chair, shelves."""
    for r in rooms:
        cx, _, cz = r.centre
        d.put(cx, r.y0, cz, WOOD.shaped(sc.pack(sc.TABLE)))
        d.put(cx, r.y0, cz + 1, WOOD.shaped(sc.pack(sc.CHAIR)))
        d.put(cx + 1, r.y0, cz, PANEL.shaped(sc.pack(sc.SLAB)))
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 2, r.z1 - 1, 2):
            if (far, r.y0, z) in r.cells:
                d.fill([(far, r.y0 + j, z) for j in range(3)], WOOD.shaped(sc.pack(sc.SLAB)))


def _bridge(d: ShipDesign) -> None:
    """Consoles along the bridge glass, the helm chairs left empty, a chart table."""
    for x in range(CX - 9, CX + 10):
        d.put(x, BRIDGE_WALK, TOWER_Z1 - 3, PANEL)
    for x in range(CX - 4, CX + 5, 2):
        d.put(x, BRIDGE_WALK, TOWER_Z1 - 5, Mat("iron_block", tint=SLATE).shaped(sc.pack(sc.CHAIR, yaw=2)))
    for z in range(TOWER_Z0 + 6, TOWER_Z1 - 5, 3):
        d.put(CX - 9, BRIDGE_WALK, z, PANEL)
        d.put(CX + 9, BRIDGE_WALK, z, PANEL)
    d.box(CX - 3, BRIDGE_WALK, 24, CX + 3, BRIDGE_WALK, 27, "iron_block", tint=SOOT)        # the chart table


def _gallery(d: ShipDesign) -> None:
    """Deck 5 amidships: the long gun gallery under the barrel, pillars down both sides, lights in the roof."""
    for z in range(66, 178, 12):
        for x in (CX - 12, CX + 12):
            d.fill([(x, y, z) for y in range(WALK[4], WALK[4] + 5)], FRAME.shaped(sc.pack(sc.CYLINDER)))
        d.put(CX, WALK[4] + 5, z + 6, DEAD_WARM)


# --- loot ------------------------------------------------------------------------------------------------------------
def _chests(d: ShipDesign, rooms: dict) -> None:
    t = "giant_battleship"
    d.chest(CX - 11, WALK[0], 26, table=t)                                    # reactor hall
    d.chest(CX + 11, WALK[0], 50, table=t)
    d.chest(21, BREECH_PLATE + 1, 34, table=t)                                # breech chamber
    d.chest(47, BREECH_PLATE + 1, 58, table=t)
    d.chest(CX - 7, BRIDGE_WALK, 30, table=t)                                 # bridge
    d.chest(CX + 7, BRIDGE_WALK, TOWER_Z1 - 4, table=t)
    arm = rooms["armoury"]
    for r in (arm[0], arm[1], arm[-1]):                                       # armoury
        d.chest(*r.corner(1 if r.side == "left" else 0), table=t)
    d.chest(*rooms["cabins"][3].corner(1), table=t)                           # cabins, quarters, mess
    d.chest(*rooms["quarters"][5].corner(1), table=t)
    d.chest(*rooms["mess"][0].corner(1), table=t)
    d.chest(*rooms["holds"][7].corner(1), table=t, block="crate")             # a hold
    d.chest(CX - 4, WALK[1], HANGAR_Z1 - 1, table=t, block="crate")           # the hangar
    # hidden caches: far out in the barrel over the second pylon (the overhang beyond it often breaks in a crash), on the
    # command block's roof, at the far end of the gun gallery, in an officer's room
    d.chest(CX + 2, BARREL_FLOOR + 1, 214, table="giant_cache", hidden=True)
    d.chest(CX + 5, ROOF_WALK, 25, table="giant_cache", hidden=True)
    d.chest(CX - 14, WALK[4], 176, table="giant_cache", hidden=True)
    d.chest(*rooms["officers"][-1].corner(2), table="giant_cache", hidden=True)


def build() -> ShipDesign:
    d = ShipDesign("battleship", "Battleship", weight=3, groundRow=6)
    h, tower = _hulls(d)
    _decks(d, h)
    rooms = _deck_rooms(d, h)
    d.entrance(*_hangar(d, h))                             # the FIRST entrance: the boarding arrival in space
    _barrel(d)
    _breech(d)
    _shafts(d)
    _windows(d, h, tower)
    d.hull_door(h, "left", WALK[0], 86, frame=FRAME)       # ways in from the ground on a planet
    d.hull_door(h, "right", WALK[0], 150, frame=FRAME)
    _radiators(d, h)
    _turrets(d)
    _engines(d)
    _antennas(d)
    _reactor(d)
    _armoury(d, rooms["armoury"])
    _cabins(d, rooms["cabins"] + rooms["quarters"])
    _mess(d, rooms["mess"])
    _holds(d, rooms["holds"] + rooms["stores"])
    _offices(d, rooms["offices"] + rooms["officers"] + rooms["engineering"])
    _bridge(d)
    _gallery(d)
    _chests(d, rooms)
    d.sound(CX, WALK[0] + 2, 26, "amb_giant_hull", radius=64, volume=0.45)       # the reactor hall (aft of the reactor)
    d.sound(CX, WALK[4] + 2, 120, "amb_giant_hull", radius=64, volume=0.4)       # the gun gallery amidships
    d.greebles(h, seed=5, density=0.008, m=FRAME, light=DEAD_RED,
               region=(0, LOW_TOP, 0, WIDTH, 80, 260))
    d.normalize()
    return d
