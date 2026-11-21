"""The escort cruiser's boarded hull (#3902): the mid-sized warship of the Service's escort wing that the Guardian core
took (canon C22). The flying boss is drawn by the client; THIS is the hull a player walks through after the fight — the
server registers it as a wreck where the boss fell (pin-only: no planet and no galaxy pass ever rolls it).

    uv run --no-project python tools/gen_giant_ships.py --only escort_cruiser --out artifacts/gs/ships --preview

Coordinates: x across (the centre line is x = CX), y up, z along; the BOW points to +z.

Silhouette (a child's drawing was the model): a boxy blue-grey hull with a knife prow, a bridge block on the fore deck
with a window band and red lamps beside the glass, and ONE long barrel lying on the spine — out of a housing amidships,
over the bridge's roof and on past the bow. Two engine bells aft, two small turrets on the aft deck.

Inside (people walk one row above a plate):
- deck 1 (walk 3):  the engine room aft (behind a sliding door), a short corridor with holds, and forward of it the
  ROBOT BAY — one empty hall across the whole beam, clearly the largest room behind a doorway;
- deck 2 (walk 8):  the main deck — a corridor along the centre line with cabins on both sides; the port hull door
  amidships opens through a short passage into it (the FIRST entrance = the boarding arrival in space);
- bridge (walk 14): in the bridge block; the relay console stands against the forward wall with a lamp on a post on
  each side (markers `sps_console` / `sps_lamp` — the server places the blocks, so the body carries neither).
Two ladder shafts (all `keep`): aft from deck 1 to deck 2, forward from deck 2 up to the bridge. The bridge's floor, the
posts and the wall behind the console are `keep` too: whatever the fight tore open, the console still stands. So is the
robot bay's shell with its three red ceiling lamps: the hall stays a closed room, where the wreck's guardian bay stands.

The look belongs to the Service's fleet (the battleship's family): blue-grey gunmetal plating with lighter panels, a
darker belly, a muted blue band with a light line along the flanks. No emblems, letters or symbols, no franchise look.
"""
from __future__ import annotations

import math

from ships import CLEAR, Mat, ShipDesign, polygon, sc, shipgeom, solids

CX = 15                         # centre line (odd beam: a 3-wide corridor sits centred)

# --- colours (tints): the Service fleet's blue-grey family (the battleship's) -----------------------------------------
GUNMETAL = 0x5A636D
PANEL_LIGHT = 0x7E8892
SEAM_TONE = 0x454D56
BELLY_TONE = 0x48515A
FRAME_TONE = 0x3C4652
STRIPE_TONE = 0x3E6A8E
LINE_TONE = 0xC4C9CE
BARREL_TONE = 0x7C848D
SLATE = 0x40434B
COPPER = 0xC0743E

# --- materials --------------------------------------------------------------------------------------------------------
HULL = Mat("iron_block", tint=GUNMETAL)
PLATE_B = Mat("iron_block", tint=PANEL_LIGHT)
SEAM = Mat("iron_block", tint=SEAM_TONE)
BELLY = Mat("iron_block", tint=BELLY_TONE)
FRAME = Mat("iron_block", tint=FRAME_TONE)
STRIPE = Mat("iron_block", tint=STRIPE_TONE)
LINE = Mat("iron_block", tint=LINE_TONE)
BARREL = Mat("iron_block", tint=BARREL_TONE)
COPPER_M = Mat("copper_block", tint=COPPER)
DECK = Mat("steel_floor")
WALL = Mat("steel_wall")
CARGO = Mat("cargo_floor")
LIGHT = Mat("light_white")                       # the ruin pass switches every lamp off; the fresh wreck relights a share red
RED = Mat("light_red")                           # the bridge's window lamps and the navigation lights
GLASS = Mat("glass", tint=0x41606E)
BRIDGE_GLASS = Mat("glass_clear", tint=0x9CC4D0)
PANEL = Mat("lab_panel")
ENGINE = Mat("engine_panel", tint=SLATE)
WOOD = Mat("wood_planks_mahogany")

# --- rows -------------------------------------------------------------------------------------------------------------
HULL_TOP = 13                     # the main hull's top row
PLATE_1, PLATE_2 = 2, 7           # deck plates
WALK_1, WALK_2 = PLATE_1 + 1, PLATE_2 + 1
ROOM_H = 4                        # rows of air on both decks
BRIDGE_FLOOR = 13                 # the bridge's floor plate (a second plate lies under it on row 12)
BRIDGE_WALK = 14
BRIDGE_TOP = 18                   # the bridge block's top row (air on rows 14..16)
BARREL_Y = 21                     # the barrel's axis
BARREL_R = 2.5

# --- z stations -------------------------------------------------------------------------------------------------------
Z0, Z1 = 6, 80                    # the main hull, stern to prow
PROW = 18                         # slices of the prow's taper
ENGINE_Z1 = 20                    # the engine room's last air row; its wall is on ENGINE_Z1 + 1
BAY_WALL = 45                     # the robot bay's aft wall; the bay runs from BAY_WALL + 1 to the bow
BRIDGE_Z0, BRIDGE_Z1 = 52, 70     # the bridge block (its inside: z 54..68, x 10..20)
HOUSING_Z0, HOUSING_Z1 = 30, 51   # the barrel's housing on the spine, aft of the bridge
BREECH_Z, MUZZLE_Z = 34, 88       # the barrel
PASSAGE_Z = 36                    # the port passage from the hull door to the deck 2 corridor (z 35..37)
SHAFT_X = 18                      # both ladder shafts: ladder on x = 18, doorway in the corridor's starboard wall
SHAFT_AFT_Z, SHAFT_FWD_Z = 24, 55


def _hull_section(half: float, bottom_half: float, keel: float = 0):
    """The main hull: a flat keel `2 * bottom_half` wide, sides flaring out over three rows to `half`, then rising
    straight to the top row with a two-cell chamfer."""
    yb, yt = keel - 0.5, HULL_TOP + 0.5
    right = [(CX + bottom_half, yb), (CX + half, yb + 3), (CX + half, yt - 2), (CX + half - 2, yt)]
    return polygon(right + [(2 * CX - x, y) for x, y in reversed(right)])


def _box_section(y0: int, y1: int, half: float, top: float):
    """A box section from row y0 to y1, its upper edges chamfered by `top` cells."""
    yb, yt = y0 - 0.5, y1 + 0.5
    right = [(CX + half, yb), (CX + half, yt - top), (CX + half - top, yt)]
    return polygon(right + [(2 * CX - x, y) for x, y in reversed(right)])


def _skin(x: int, y: int, z: int):
    """The outer skin: gunmetal plates between darker seams every 12 cells, some panels lighter, a darker belly."""
    if z % 12 == 0:
        return SEAM
    if y <= 2:
        return BELLY
    return PLATE_B if solids.hash01(x // 5, y // 4, z // 12, 23) < 0.2 else HULL


def _hull(d: ShipDesign):
    """The main hull and the bridge block on its fore deck, one shell."""
    lower = shipgeom.loft([
        (Z0, _hull_section(12.5, 9.5, keel=1)),
        (14, _hull_section(15.5, 12.5)),
        (Z1, _hull_section(15.5, 12.5)),
    ], bow=PROW, tip=0.25, curve="linear", taper_axes="x")
    bridge = shipgeom.loft([(BRIDGE_Z0, _box_section(12, BRIDGE_TOP, 7.5, 2)),
                            (BRIDGE_Z1, _box_section(12, BRIDGE_TOP, 7.5, 2))])
    return d.add_hull(lower | bridge, HULL, wall=2, skin=_skin)


def _inside(h, cells) -> list:
    return [p for p in cells if h.contains(p, None)]


def _decks(d: ShipDesign, h) -> None:
    d.deck(h, PLATE_1, DECK)
    d.deck(h, PLATE_2, DECK)
    for y in (BRIDGE_FLOOR - 1, BRIDGE_FLOOR):            # the bridge's floor closes the main hull's roof under the block
        d.deck(h, y, DECK, x0=CX - 8, x1=CX + 8, z0=BRIDGE_Z0, z1=BRIDGE_Z1)


def _ceiling_lamp(d: ShipDesign, room) -> None:
    cx, _, cz = room.centre
    if d.is_block(cx, room.y1 + 1, cz):
        d.put(cx, room.y1 + 1, cz, LIGHT)


def _deck_1(d: ShipDesign, h) -> dict:
    """Deck 1: the engine room aft, a corridor with holds, the robot bay forward."""
    out: dict = {}
    rows = range(WALK_1, WALK_1 + ROOM_H)
    cor = d.corridor(h, WALK_1, ENGINE_Z1 + 2, BAY_WALL - 1, width=3, height=ROOM_H, walls=WALL, light=LIGHT, light_every=6)
    out["holds"] = (d.rooms(h, cor, per_side=2, sides="left", depth=14, walls=WALL)
                    + d.rooms(h, cor, per_side=1, sides="right", depth=14, walls=WALL, a0=SHAFT_AFT_Z + 3))
    # the two cross walls, each with ONE doorway: the engine room and the robot bay are rooms of their own
    for z in (ENGINE_Z1 + 1, BAY_WALL):
        d.fill(_inside(h, [(x, y, z) for x in range(2 * CX + 1) for y in rows]), WALL)
        d.slide_door(CX - 1, WALK_1, z, "z")
    # the engine room: a round reactor with copper rings, pipes along the floor to the engine bells
    cz = 13
    d.cylinder(CX, cz, WALK_1, WALK_1 + ROOM_H - 1, 3, ENGINE)
    for y in (WALK_1 + 1, WALK_1 + 3):
        d.cylinder(CX, cz, y, y, 3.6, COPPER_M)
    for x in (CX - 7, CX + 7):
        d.fill(_inside(h, [(x, WALK_1, z) for z in range(Z0 + 2, ENGINE_Z1)]), Mat("factory_pipe"))
        d.put(x, PLATE_2, cz, LIGHT)
    # the robot bay: one empty hall across the beam, pillars down both sides, a few lamps in its roof
    bay = _inside(h, [(x, y, z) for x in range(2 * CX + 1) for y in rows for z in range(BAY_WALL + 1, Z1 + 1)])
    d.fill(bay, CLEAR)
    d.fill([(x, PLATE_1, z) for x, y, z in bay if y == WALK_1], CARGO)
    for z in (51, 59, 67):
        for x in (CX - 6, CX + 6):
            if (x, WALK_1, z) in set(bay):
                d.fill([(x, y, z) for y in rows], FRAME.shaped(sc.pack(sc.CYLINDER)))
        d.put(CX, PLATE_2, z + 2, RED)                 # the bay's own lamps are red, and kept: they burn on in the wreck
    out["bay"] = bay
    return out


def _deck_2(d: ShipDesign, h) -> dict:
    """Deck 2, the main deck: the corridor, the cabins and the port passage to the hull door."""
    kw = {"depth": 14, "walls": WALL}
    cor = d.corridor(h, WALK_2, 10, 72, width=3, height=ROOM_H, walls=WALL, light=LIGHT, light_every=6)

    def side(sd: str, *segments: tuple[int, int, int]) -> list:
        return [r for a0, a1, n in segments for r in d.rooms(h, cor, per_side=n, sides=sd, a0=a0, a1=a1, **kw)]
    # the gaps: the passage on the port side, the two ladder shafts on the starboard side
    cabins = side("left", (10, PASSAGE_Z - 2, 2), (PASSAGE_Z + 2, 72, 3))
    cabins += side("right", (10, SHAFT_AFT_Z - 2, 1), (SHAFT_AFT_Z + 3, SHAFT_FWD_Z - 2, 2), (SHAFT_FWD_Z + 3, 72, 1))
    # the port passage: from the hull's inside face through the corridor's wall
    d.corridor(h, WALK_2, 0, CX - 2, axis="x", centre=PASSAGE_Z, width=3, height=3, walls=WALL)
    return {"cabins": cabins}


def _shafts(d: ShipDesign) -> None:
    """Two ladder shafts off the corridor's starboard wall (kept through the ruin pass): aft deck 1 <-> deck 2, forward
    deck 2 <-> the bridge. Neither opens into the robot bay."""
    d.lift_shaft(SHAFT_X, SHAFT_AFT_Z, [WALK_1, WALK_2], opening="-x", walls=WALL)
    d.lift_shaft(SHAFT_X, SHAFT_FWD_Z, [WALK_2, BRIDGE_WALK], opening="-x", walls=WALL)
    # on the bridge the strip between the shaft and the block's side wall is closed
    d.fill([(SHAFT_X + 2, y, z) for y in range(BRIDGE_WALK, BRIDGE_WALK + 3) for z in range(SHAFT_FWD_Z - 1, SHAFT_FWD_Z + 3)],
           WALL, keep=True)


def _bridge(d: ShipDesign, h) -> tuple:
    """The bridge: side consoles, empty chairs, and the place of the relay console against the forward wall with a lamp
    post on each side. Returns (console cell, lamp cells)."""
    x0, x1, z0, z1 = CX - 5, CX + 5, BRIDGE_Z0 + 2, BRIDGE_Z1 - 2          # the inside
    floor = [(x, BRIDGE_WALK, z) for x in range(x0, x1 + 1) for z in range(z0, z1 + 1)]
    assert all(h.contains(p, None) for p in floor), "the bridge's inside is not where the script expects it"
    for z in range(z0 + 6, z1 - 2, 2):
        d.put(x0, BRIDGE_WALK, z, PANEL)
        if z > SHAFT_FWD_Z + 3:
            d.put(x1, BRIDGE_WALK, z, PANEL)
    for x in (CX - 2, CX + 2):
        d.put(x, BRIDGE_WALK, z1 - 4, Mat("iron_block", tint=SLATE).shaped(sc.pack(sc.CHAIR, yaw=2)))
    d.put(CX, BRIDGE_TOP - 1, (z0 + z1) // 2, LIGHT)
    console = (CX, BRIDGE_WALK, z1)
    posts = [(CX - 2, BRIDGE_WALK, z1), (CX + 2, BRIDGE_WALK, z1)]
    lamps = [(x, y + 1, z) for x, y, z in posts]
    d.fill(posts, HULL, keep=True)
    for p in [console, (console[0], console[1] + 1, console[2])] + lamps + [(x, y + 1, z) for x, y, z in lamps]:
        d.put(*p, CLEAR)
    return console, lamps


def _keep_bridge(d: ShipDesign) -> None:
    """Kept through the ruin pass: the whole bridge floor (both plates) and the forward wall behind console and lamps."""
    d.keep_box(CX - 6, BRIDGE_FLOOR - 1, BRIDGE_Z0 + 1, CX + 6, BRIDGE_FLOOR, BRIDGE_Z1)
    d.keep_box(CX - 4, BRIDGE_FLOOR - 1, BRIDGE_Z1 - 1, CX + 4, BRIDGE_TOP, BRIDGE_Z1)
    # a lamp is never kept (the deck 2 corridor's ceiling lamps sit in the lower plate): it must die with the others
    d.restyle([p for p, v in d.plain.items() if v[0] == "block" and v[5] and v[1].startswith("light_")], keep=False)


def _keep_bay(d: ShipDesign, bay) -> None:
    """Kept through the ruin pass: the robot bay's shell — every block that touches the hall's air (faces, edges and
    corners): its deck, its roof with the three red lamps (a kept lamp is never switched off), the inner layer of the hull
    round it and the cross wall. So the fight never tears the hall open and the wreck's guardian bay always has its room;
    the outer layer of the hull still takes the hits."""
    air = set(bay)
    shell = {(x + i, y + j, z + k) for x, y, z in air for i in (-1, 0, 1) for j in (-1, 0, 1) for k in (-1, 0, 1)} - air
    d.keep([p for p in shell if d.is_block(*p)])


def _windows(d: ShipDesign, h) -> None:
    d.window_band(h, WALK_2 + 1, WALK_2 + 2, GLASS, every=6, width=1, start=13, end=70)       # portholes on deck 2
    d.window_band(h, BRIDGE_WALK + 1, BRIDGE_WALK + 2, BRIDGE_GLASS, every=1, width=1, side="front",
                  start=CX - 4, end=CX + 4)                                                    # the bridge's glass
    d.window_band(h, BRIDGE_WALK + 1, BRIDGE_WALK + 2, BRIDGE_GLASS, every=1, width=1, start=BRIDGE_Z0 + 8,
                  end=BRIDGE_Z1 - 3)
    # the red window lamps: on the block's face beside the glass, and one on each side wall
    for x in (CX - 6, CX + 6):
        d.fill([(x, y, BRIDGE_Z1) for y in (BRIDGE_WALK + 1, BRIDGE_WALK + 2)], RED, "blocks")
    for x in (CX - 7, CX + 7):
        d.fill([(x, BRIDGE_WALK + 2, BRIDGE_Z1 - 2)], RED, "blocks")
    # the Service's band along the flanks: a muted blue stripe with a light line under it; a light line round the bridge
    d.fill([p for p in h.outer() if p[1] == HULL_TOP - 2], STRIPE, "blocks")
    d.fill([p for p in h.outer() if p[1] == HULL_TOP - 3], LINE, "blocks")
    d.fill([p for p in h.outer() if p[1] == BRIDGE_WALK and p[2] >= BRIDGE_Z0], LINE, "blocks")


def _annulus(z0: int, z1: int, r0: float, r1: float) -> set:
    """The cells of a ring round the barrel's axis between radius r0 and r1, z0..z1."""
    out = set()
    for z in range(z0, z1 + 1):
        for x in range(math.floor(CX - r1), math.ceil(CX + r1) + 1):
            for y in range(math.floor(BARREL_Y - r1), math.ceil(BARREL_Y + r1) + 1):
                if r0 * r0 < (x - CX) ** 2 + (y - BARREL_Y) ** 2 <= r1 * r1:
                    out.add((x, y, z))
    return out


def _barrel(d: ShipDesign, h) -> None:
    """The long gun: a housing on the spine aft of the bridge, the barrel out of it over the bridge's roof and past the
    bow, dark bands, a strut on the fore deck and a ringed muzzle you can look into."""
    housing = [(x, y, z) for z in range(HOUSING_Z0, HOUSING_Z1 + 1) for y in range(HULL_TOP + 1, BARREL_Y + 3)
               for x in range(CX - 5, CX + 6)
               if abs(x - CX) <= 5 - max(0, y - (BARREL_Y - 1)) and y <= BARREL_Y + 2 - max(0, HOUSING_Z0 + 3 - z)]
    d.fill(housing, HULL, "empty")
    d.fill([p for p in housing if p[1] == HULL_TOP + 1], FRAME, "blocks")
    d.fill([p for p in housing if p[1] == BARREL_Y - 2 and abs(p[0] - CX) == 5], STRIPE, "blocks")
    d.tube([(CX, BARREL_Y, BREECH_Z), (CX, BARREL_Y, MUZZLE_Z)], BARREL, BARREL_R, hollow=1, kind="poly")
    d.fill([(x, y, z) for x, y, z in _annulus(BREECH_Z, HOUSING_Z1, 0, BARREL_R - 1)], FRAME)   # plugged inside the housing
    for z0 in range(HOUSING_Z1 + 5, MUZZLE_Z - 6, 9):
        d.fill(_annulus(z0, z0, BARREL_R, BARREL_R + 0.9), FRAME, "empty")
    d.fill(_annulus(MUZZLE_Z - 2, MUZZLE_Z, BARREL_R, BARREL_R + 0.9), FRAME, "empty")
    for z in (BRIDGE_Z1 + 5, BRIDGE_Z1 + 6):                                                    # the strut on the fore deck
        d.fill([(x, y, z) for y in range(HULL_TOP + 1, BARREL_Y - 2) for x in range(CX - 1, CX + 2)], FRAME, "empty")
    d.fill([(x, BRIDGE_TOP + 1, z) for x in (CX - 2, CX + 2) for z in range(BRIDGE_Z0 + 2, BRIDGE_Z1 - 1, 4)], FRAME, "empty")


def _outside(d: ShipDesign, h) -> None:
    """Two engine bells aft, two small turrets on the aft deck, navigation lights, greebles."""
    for x in (CX - 6, CX + 6):
        d.nozzle(x, 7, Z0 + 1, 3.5, 6, m=Mat("engine_nozzle"), glow=ENGINE)
    for x in (CX - 9, CX + 9):
        d.turret(x, HULL_TOP + 1, 22, r=3, barrels=2, length=7, barrel_r=1.0, spacing=2, facing="+z",
                 m=Mat("iron_block", tint=PANEL_LIGHT), barrel_m=BARREL)
    d.put(CX, BARREL_Y + 3, HOUSING_Z0 + 6, RED)
    d.greebles(h, seed=9, density=0.012, m=FRAME, light=RED, faces=("up",), kinds=("box", "pipe"),
               region=(0, HULL_TOP, 8, 2 * CX, HULL_TOP, 76))


def _furnish(d: ShipDesign, rooms: dict) -> None:
    for r in rooms["cabins"] + rooms["holds"]:
        _ceiling_lamp(d, r)
    for r in rooms["cabins"]:                                 # cabins: bunks along the far wall, a small table
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z1, 3):
            if (far, r.y0, z) in r.cells:
                d.put(far, r.y0, z, Mat("bed"))
        cx, _, cz = r.centre
        d.put(cx, r.y0, cz, WOOD.shaped(sc.pack(sc.TABLE)))
    for k, r in enumerate(rooms["holds"]):                    # holds: a few crates along the far wall
        far = r.x0 if r.side == "left" else r.x1
        for z in range(r.z0 + 1, r.z1):
            if (far, r.y0, z) in r.cells and solids.hash01(far, k, z, 5) < 0.5:
                d.put(far, r.y0, z, Mat("crate" if z % 3 else "transport_crate"))


def _check_bridge(d: ShipDesign, console, lamps) -> None:
    """The contract the server builds on: the console's cell free on a kept floor with two free cells, each lamp's cell
    free on a kept post with air above it."""
    def kept(p) -> bool:
        v = d.get(*p)
        return v is not None and v[0] == "block" and v[5]
    x, y, z = console
    assert d.get(x, y, z) is None and d.get(x, y + 1, z) is None and kept((x, y - 1, z)), "console cell"
    assert kept((x, y, z + 1)) and kept((x, y + 1, z + 1)), "the wall behind the console is not kept"
    for lx, ly, lz in lamps:
        assert d.get(lx, ly, lz) is None and d.get(lx, ly + 1, lz) is None and kept((lx, ly - 1, lz)), "lamp cell"
        assert kept((lx, ly - 2, lz)) and kept((lx, ly, lz + 1)), "lamp post / wall"


def build() -> ShipDesign:
    d = ShipDesign("escort_cruiser", "Escort Cruiser", weight=1, groundRow=3, pinOnly=True, ruinStrength=0.3)
    h = _hull(d)
    _decks(d, h)
    rooms = _deck_1(d, h)
    rooms.update(_deck_2(d, h))
    d.hull_door(h, "left", WALK_2, PASSAGE_Z - 1, frame=FRAME)   # the FIRST entrance: the boarding arrival in space
    _shafts(d)
    console, lamps = _bridge(d, h)
    _windows(d, h)
    _keep_bridge(d)
    d.hull_door(h, "right", WALK_1, 34, frame=FRAME)             # the other ways in: the starboard hold ...
    d.hull_door(h, "left", WALK_1, 27, frame=FRAME)              # ... and the aft port hold
    _barrel(d, h)
    _outside(d, h)
    _furnish(d, rooms)
    _keep_bay(d, rooms["bay"])
    # the bridge's markers: the server places the console and the two lamps (GiantShipRules.BridgeMarkers)
    d.marker(*console, "sps_console", "face=-z")
    for p in lamps:
        d.marker(*p, "sps_lamp")
    _check_bridge(d, console, lamps)
    # loot: the engine room, a hold, a cabin, the bridge; a hidden cache in the foremost cabin. None in the robot bay.
    t = "giant_battleship"
    d.chest(CX - 8, WALK_1, 10, table=t)
    d.chest(*rooms["holds"][1].corner(3), table=t, block="crate")
    d.chest(*rooms["cabins"][0].corner(0), table=t)
    d.chest(CX - 4, BRIDGE_WALK, BRIDGE_Z0 + 3, table=t)
    d.chest(*rooms["cabins"][-1].corner(2), table="giant_cache", hidden=True)
    d.sound(CX, WALK_2 + 2, 44, "amb_giant_hull", radius=64, volume=0.45)
    d.normalize()
    return d
