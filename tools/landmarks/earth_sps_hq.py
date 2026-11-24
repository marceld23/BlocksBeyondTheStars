"""The SPS HEADQUARTERS in the Amazon (#3986): the ruined home of the Scout and Pioneer Service, deep in the rain forest —
five landmark templates that stand side by side and join through corridors at their edges (the complex frame, the places
and the joints are in `_sps_hq.py`; the underground parts in `_sps_hq_below.py`). All five are pin-only: Earth's location
list places them, with the same ground row and no turn.

- `earth_sps_hq_main` (160 x 61 x 120, ground row 23): the headquarters building in the Service's blue-grey, overgrown, a
  ruin (masonry). A two-storey lobby with a jungle tree growing through it, the reception desk under the big blue SPS
  letters, offices in both wings and on every floor, a kept spiral stair to the roof, the map room on the first floor with
  a big wall map of the Earth (one blue dot glows where the headquarters stand), and a kept stair from the lobby down to the
  basement tunnel that leads north into the server halls. A wide corridor in the east wall leads into the hangar.
- `earth_sps_hq_hangar` (200 x 59 x 120, ground row 1): the hangar under a vaulted roof whose east half has fallen in (its
  ruin zone decays hard, jungle grows in the sunlit half), three small SPS ships parked inside (built with the ship toolkit,
  their hulls holed and overgrown), gantries along both long walls with kept ladders, crane beams, fuel tanks and a great
  open hangar door in the east wall with its two door panels pushed aside. The great hall's door is in the north wall.
- `earth_sps_hq_hall` (96 x 48 x 40, ground row 1): the great hall — intact and protected, only a whisper of moss at the
  gate. At its west end the huge metal gate (24 wide, 30 high) stands open, both wings swung in; in the middle lies a
  fallen guardian giant (the `guardian_giant` marker: the server lays the 14-block body, lights out, and it never wakes),
  its feet toward the gate; at the far end a pedestal (9 x 9 x 3) carries a broken glass sphere as big as the giant's head —
  its lower half-shell still standing, the shards on the pedestal and the floor.
- `earth_sps_hq_servers`, `earth_sps_hq_core`: see `_sps_hq_below.py`.

    uv run --no-project python tools/gen_landmarks.py earth_sps_hq
    uv run --no-project python tools/landmarks/preview.py earth_sps_hq_hall --cut y=6
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, fence_rail
from _sps_hq import (
    BAND,
    DESK,
    FLOOR,
    FRAME,
    GLASS,
    GRASS,
    JOINT_HANGAR_HALL,
    JOINT_MAIN_HANGAR,
    JOINT_MAIN_SERVERS,
    LAMP_BLUE,
    LAMP_DEAD,
    LEAVES,
    MOSS,
    PALE,
    PAVING,
    PLACES,
    SCREEN,
    SLAB,
    STAIR,
    STEEL,
    WALL,
    WALL_DARK,
    WALL_MOSSY,
    carve_or_clear,
    console,
    h01,
    leaf_patch,
    local,
    put_free,
    scatter_rubble,
    tree,
)
from _sps_hq_below import core, servers
from geom import CAVITY, CLEAR, Design, Mat, Mix
from ships import ShipDesign, ship_section

KEYS = ("earth_sps_hq_main", "earth_sps_hq_hangar", "earth_sps_hq_servers", "earth_sps_hq_core", "earth_sps_hq_hall")
MAIN, HANGAR, HALL = KEYS[0], KEYS[1], KEYS[4]


def _box(d: Design, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m, where: str | None = None) -> set:
    return d.fill(solids.box(x0, y0, z0, x1, y1, z1), m, where)


def _expect_size(d: Design) -> None:
    _, _, w, ln, _ = PLACES[d.header["key"]]
    b = d.bounds()
    assert b is not None and b[3] + 1 == w and b[5] + 1 == ln and min(b[0], b[1], b[2]) >= 0, \
        f"{d.header['key']}: bounds {b}, expected {w} x {ln} (PLACES in _sps_hq.py)"


# ===================================================================================================== the main building
G = PLACES[MAIN][4]                 # 23: the ground row; the basement lies under it
X0, X1, Z0, Z1 = 4, 155, 4, 115     # the building's outer walls
SLABS = (G, G + 9, G + 18, G + 27)  # ground floor, first floor, second floor, roof (walk rows one above)
ATRIUM = (50, 109, 5, 44)           # the two-storey lobby (x0, x1, z0, z1)
MAP_ROOM = (50, 109, 66, 114)       # the map room on the first floor, two storeys high
STAIR_CORE = (26.5, 60.5)           # the kept spiral stair (centre x, z)
DOWN_X = JOINT_MAIN_SERVERS["x"]    # the stair to the basement and its tunnel run along these x
WORLD_MAP = (                       # 52 x 13, north up: '#' land, '.' sea, '*' the headquarters
    "...........#####....######....###..#############....",
    "..########.########..####....#####################..",
    "...#############.....##.....###########################"[:52],
    ".....##########..........######.##############.#....",
    "........######..........##########.#############....",
    ".............##.###.....###########.###.###.##......",
    "...............##*####......#######.....######......",
    "................######.......######...##.....#......",
    ".................####........####....#######........",
    ".................###.................#######.##.....",
    ".................##.......................###.#.....",
    "....................................................",
    "####################################################",
)


def _main_shell(d: Design) -> None:
    """The plaza, the slabs, the outer walls with their window bands and blue bands, the roof's parapet."""
    d.fill(solids.box(0, G, 0, 159, G, 119), PAVING)
    for y in SLABS:
        _box(d, X0, y, Z0, X1, y, Z1, SLAB if y < SLABS[-1] else WALL_DARK)
    wall = solids.box_walls(X0, G, Z0, X1, SLABS[-1] + 2, Z1)
    d.fill(wall, lambda x, y, z: BAND if y in SLABS[1:] else WALL_MOSSY if y < G + 8 else WALL)
    for walk in (s + 1 for s in SLABS[:-1]):
        for x in range(X0 + 2, X1 - 1):
            if x % 6 in (2, 3, 4) and not ATRIUM[0] <= x <= ATRIUM[1]:
                _box(d, x, walk + 2, Z0, x, walk + 5, Z0, GLASS)
            if x % 6 in (2, 3, 4):
                _box(d, x, walk + 2, Z1, x, walk + 5, Z1, GLASS)
        for z in range(Z0 + 2, Z1 - 1):
            if z % 6 in (2, 3, 4):
                _box(d, X0, walk + 2, z, X0, walk + 5, z, GLASS)
                _box(d, X1, walk + 2, z, X1, walk + 5, z, GLASS)


def _main_lobby(d: Design) -> None:
    """The two-storey lobby: a glass front with the entrance under a canopy, the reception desk with dead screens under
    the big SPS letters, a rain-forest tree grown up through its ceiling and the roof."""
    ax0, ax1, az0, az1 = ATRIUM
    _box(d, ax0, SLABS[1], az0, ax1, SLABS[1], az1, CLEAR)
    d.fill(solids.box(ax0, G + 1, Z0, ax1, SLABS[2] - 1, Z0),
           lambda x, y, z: FRAME if x % 6 == 2 else BAND if y == SLABS[1] else GLASS)
    _box(d, 76, G + 1, Z0, 83, G + 7, Z0, CLEAR)                         # the entrance
    _box(d, 74, G + 8, 0, 85, G + 8, Z0 - 1, FRAME)                      # its canopy, on two posts
    for px in (74, 85):
        _box(d, px, G + 1, 1, px, G + 7, 1, FRAME)
    d.fill(solids.box(ax0 - 1, G + 1, az0, ax0 - 1, SLABS[2] - 1, az1 + 1), WALL)   # the lobby's side and back walls
    d.fill(solids.box(ax1 + 1, G + 1, az0, ax1 + 1, SLABS[2] - 1, az1 + 1), WALL)
    d.fill(solids.box(ax0, G + 1, az1 + 1, ax1, SLABS[2] - 1, az1 + 1), WALL)
    for x0, z0, along in ((ax0 - 1, 20, "z"), (ax1 + 1, 20, "z"), (58, az1 + 1, "x"), (100, az1 + 1, "x")):
        _box(d, x0, G + 1, z0, x0 + (1 if along == "x" else 0), G + 3, z0 + (1 if along == "z" else 0), CLEAR)
    # the reception: a curved desk facing the entrance, dead screens on it, the Service's letters on the wall behind
    cx, cz = 84.5, 36.0
    for x, z in solids.ring2d(cx, cz, 8, 1):
        if z <= cz:
            _box(d, x, G + 1, z, x, G + 2, z, DESK)
            if (x + z) % 3 == 0:
                d.put(x, G + 3, z, SCREEN, shape=sc.pack(sc.SLAB))
    d.put(round(cx), G + 3, round(cz - 8), LAMP_DEAD)
    w, _ = Design.text_size("SPS", 2)
    d.text("SPS", round(80 - w / 2), G + 4, az1, BAND.with_(keep=True), dir="+x", scale=2)
    # the tree: a trunk up through a hole in the first-floor ceiling and the roof, its crown over the roof
    tx, tz = 62, 26
    for y in (SLABS[2], SLABS[3]):
        d.fill([(x, y, z) for x, z in solids.disc(tx + 0.5, tz + 0.5, 2.5)], CLEAR)
    tree(d, tx, G + 1, tz, height=SLABS[3] - G + 3, crown=7, seed=3)
    leaf_patch(d, tx, G + 1, tz, 6, seed=4, density=0.35)
    console(d, 66, G + 1, 12, "-z")
    console(d, 100, G + 1, 30, "+x")


def _main_stair_down(d: Design) -> None:
    """The kept stair from the back of the lobby down to the basement, the landing at its foot and the basement tunnel
    north to the edge — where the server halls' level 1 goes on (`JOINT_MAIN_SERVERS`)."""
    _, base, _ = local(MAIN, 0, JOINT_MAIN_SERVERS["y"], 0)          # the basement's walk row (3)
    steps = G - base + 1                                                # down from the lobby's slab row to the basement
    top_z = 35
    foot = top_z + steps
    d.room(DOWN_X[0] - 4, base, foot, DOWN_X[1] + 4, base + 4, foot + 8, floor=SLAB, walls=WALL, ceiling=SLAB)
    tunnel_z0 = foot + 9
    _box(d, DOWN_X[0] - 1, base - 1, tunnel_z0, DOWN_X[1] + 1, base + 4, 119, WALL)
    _box(d, DOWN_X[0], base - 1, tunnel_z0, DOWN_X[1], base - 1, 119, SLAB)
    _box(d, DOWN_X[0], base, tunnel_z0, DOWN_X[1], base + JOINT_MAIN_SERVERS["h"] - 1, 119, CAVITY)
    for k in range(steps):
        z, y = top_z + steps - 1 - k, base + k
        for x in range(DOWN_X[0], DOWN_X[1] + 1):
            d.put(x, y, z, STAIR, shape=sc.stairs(0, -1))
            carve_or_clear(d, [(x, y + h, z) for h in range(1, 5)], G)
        for x in (DOWN_X[0] - 1, DOWN_X[1] + 1):
            d.fill([(x, y + h, z) for h in range(5) if y + h < G], WALL)
    fence_rail(d, [(DOWN_X[0] - 1, G + 1, z) for z in range(top_z, top_z + 6)]
               + [(DOWN_X[1] + 1, G + 1, z) for z in range(top_z, top_z + 6)]
               + [(x, G + 1, top_z + 6) for x in range(DOWN_X[0] - 1, DOWN_X[1] + 2)], Mat("iron_block", tint=PALE))
    for z in range(tunnel_z0, 120):
        d.put(DOWN_X[0], base + 3, z, Mat("cable_tray"))
        if z % 14 == 0:
            d.put(DOWN_X[1] + 1, base + 2, z, LAMP_BLUE.with_(keep=True))
    d.chest(DOWN_X[1] + 3, base, foot + 6, table="ruin_common")


def _offices(d: Design, x0: int, x1: int, z0: int, z1: int, walk: int, seed: int) -> None:
    """Offices along a corridor running along z in the middle of x0..x1: rooms on both sides, ten cells apart, each with a
    doorway, a desk, a chair and a dead screen; some with a chest, some with rubble or a fern growing in the corner."""
    cx = (x0 + x1) // 2
    ceiling = walk + 7
    for side, (rx0, rx1) in (("w", (x0, cx - 2)), ("e", (cx + 2, x1))):
        door_x = rx1 if side == "w" else rx0
        _box(d, door_x, walk, z0, door_x, ceiling, z1, WALL_DARK)
        for rz in range(z0, z1 + 1, 10):
            _box(d, rx0, walk, rz, rx1, ceiling, rz, WALL_DARK)
            if rz + 10 > z1:
                continue
            _box(d, door_x, walk, rz + 4, door_x, walk + 2, rz + 5, CLEAR)
            v = h01(rx0, walk, rz, seed)
            dx = (rx0 + rx1) // 2
            d.put(dx, walk, rz + 3, DESK, shape=sc.pack(sc.TABLE))
            d.put(dx + 1, walk, rz + 3, DESK, shape=sc.pack(sc.TABLE))
            d.put(dx, walk, rz + 2, Mat("wood_planks", tint=0x6A5A48), shape=sc.pack(sc.CHAIR, 2))
            d.put(dx, walk + 1, rz + 3, SCREEN, shape=sc.pack(sc.PANEL))
            if v < 0.07:
                d.chest(rx0 + 1, walk, rz + 8, table="ruin_common", chance=0.6)
            elif v < 0.4:
                scatter_rubble(d, [(x, walk, z) for x in range(rx0 + 1, rx1) for z in range(rz + 1, rz + 9)],
                               seed=int(v * 1000), density=0.18)
            elif v < 0.6:
                put_free(d, [(rx0 + 1, walk, rz + 1), (rx0 + 1, walk, rz + 2)], Mat("flora_fern"))


def _main_rooms(d: Design) -> None:
    """The wings' offices on every floor, the back offices, the map room and the kept spiral stair up to the roof."""
    sx, sz = STAIR_CORE
    keep_out = (sx - 7, sx + 7, sz - 7, sz + 7)
    for s in SLABS[:-1]:
        walk = s + 1
        _offices(d, X0 + 1, ATRIUM[0] - 2, Z0 + 1, int(keep_out[2]) - 1, walk, seed=walk)
        _offices(d, X0 + 1, ATRIUM[0] - 2, int(keep_out[3]) + 1, Z1 - 1, walk, seed=walk + 1)
        _offices(d, ATRIUM[1] + 2, X1 - 1, Z0 + 1, Z1 - 1, walk, seed=walk + 2)
        if s == SLABS[0]:
            _offices(d, ATRIUM[0], ATRIUM[1], ATRIUM[3] + 12, Z1 - 1, walk, seed=walk + 3)
        elif s == SLABS[2]:                                               # over the corridor; the map room is two storeys
            _offices(d, ATRIUM[0], ATRIUM[1], ATRIUM[3] + 2, MAP_ROOM[2] - 2, walk, seed=walk + 3)
    treads = d.spiral_stair(sx, sz, G + 1, SLABS[3] - 1, 1.5, 5.5, STAIR, steps_per_turn=20, newel=FRAME.with_(keep=True))
    d.keep(treads)
    _map_room(d)


def _map_room(d: Design) -> None:
    """The map room on the first floor, two storeys high: a big wall map of the Earth on its north wall (kept), a round
    map table in the middle, a doorway from the corridor."""
    mx0, mx1, mz0, mz1 = MAP_ROOM
    walk = SLABS[1] + 1
    _box(d, mx0, SLABS[2], mz0, mx1, SLABS[2], mz1, CLEAR)
    _box(d, mx0, walk, mz0 - 1, mx1, SLABS[3] - 1, mz0 - 1, WALL)
    _box(d, 78, walk, mz0 - 1, 81, walk + 3, mz0 - 1, CLEAR)
    _box(d, mx0, walk, mz0 - 20, mx1, walk + 7, mz0 - 2, CLEAR)          # the corridor in front of it
    sea, land = Mat("concrete", tint=0x1F3A5C, keep=True), Mat("concrete", tint=0x6E8A62, keep=True)
    here = Mat("concrete", tint=0x7FD4FF, glow=0x66CCFF, keep=True)
    top = SLABS[3] - 2
    x_map = (mx0 + mx1) // 2 - 26
    assert all(len(line) == 52 for line in WORLD_MAP), "every row of the wall map is 52 wide"
    _box(d, x_map - 1, top - 14, Z1 - 1, x_map + 52, top + 1, Z1 - 1, FRAME.with_(keep=True))
    for row, line in enumerate(WORLD_MAP):
        for col, ch in enumerate(line):
            d.put(x_map + col, top - row, Z1 - 1, here if ch == "*" else land if ch == "#" else sea)
    cx, cz = (mx0 + mx1) / 2, (mz0 + mz1) / 2
    d.fill([(x, walk, z) for x, z in solids.disc(cx, cz, 6)], DESK, shape=sc.pack(sc.TABLE))
    d.fill([(x, walk + 1, z) for x, z in solids.disc(cx, cz, 4)], GLASS.with_(keep=True), shape=sc.pack(sc.PANEL))
    d.chest(mx1 - 2, walk, mz0 + 2, table="ruin_rich")


def _main_overgrowth(d: Design) -> None:
    """Creepers on the walls, moss, leaves and young trees on the roof, ferns on the plaza."""
    for x, y, z in solids.box_walls(X0 - 1, G + 1, Z0 - 1, X1 + 1, G + 16, Z1 + 1):
        doorway = (75 <= x <= 84 and z < Z0) or (Z0 + 50 <= z <= Z0 + 61 and x > X1)  # the entrance, the hangar corridor
        if h01(x, y, z, 21) < 0.05 + 0.1 * (y < G + 6) and not doorway:
            put_free(d, [(x, y, z)], LEAVES)
    for k in range(9):
        leaf_patch(d, 20 + 15 * k, SLABS[3] + 1, 15 + (k * 37) % 90, 5 + k % 3, seed=30 + k, density=0.6, rows=2)
    for tx, tz in ((20, 15), (95, 100), (140, 30)):
        tree(d, tx, SLABS[3] + 1, tz, height=5, crown=3, seed=tx + tz, keep=False)
    for x, z in ((20, 1), (45, 2), (110, 1), (150, 60), (2, 50)):
        put_free(d, [(x, G + 1, z)], Mat("flora_fern"))


def _main_joints(d: Design) -> None:
    """The wide corridor in the east wall into the hangar."""
    (jx, _), (jz0, jz1) = JOINT_MAIN_HANGAR["x"], JOINT_MAIN_HANGAR["z"]
    _, y, _ = local(MAIN, 0, JOINT_MAIN_HANGAR["y"], 0)
    _box(d, X1 + 1, G, jz0 - 1, jx, G, jz1 + 1, SLAB)
    _box(d, X1 + 1, y + JOINT_MAIN_HANGAR["h"], jz0 - 1, jx, y + JOINT_MAIN_HANGAR["h"], jz1 + 1, FRAME)
    for z in (jz0 - 1, jz1 + 1):
        _box(d, X1 + 1, y, z, jx, y + JOINT_MAIN_HANGAR["h"] - 1, z, WALL)
    _box(d, X1, y, jz0, jx, y + JOINT_MAIN_HANGAR["h"] - 1, jz1, CLEAR)


def main_building() -> Design:
    d = earth_design(MAIN, "SPS Headquarters", ruin="always", ruinProfile="masonry", ruinStrength=0.45, groundRow=G)
    _main_shell(d)
    _main_lobby(d)
    _main_rooms(d)
    _main_stair_down(d)
    _main_joints(d)
    _main_overgrowth(d)
    d.put(130, SLABS[3] + 1, 95, FRAME)                                   # a broken antenna mast on the roof
    d.fill(solids.box(130, SLABS[3] + 2, 95, 130, SLABS[3] + 10, 95), Mat("iron_block", tint=PALE))
    d.sound(80, G + 2, 25, "amb_forest", radius=70, volume=0.3)
    # the lobby and the map room decay gently (their desk, letters and map stay readable); the rest is a proper ruin
    d.ruin_zone(ATRIUM[0] - 1, G + 1, Z0, ATRIUM[1] + 1, SLABS[3] + 2, ATRIUM[3] + 1, profile="masonry", strength=0.2)
    d.ruin_zone(MAP_ROOM[0], SLABS[1], MAP_ROOM[2] - 1, MAP_ROOM[1], SLABS[3] + 2, Z1, profile="masonry", strength=0.15)
    _expect_size(d)
    return d


# ============================================================================================================ the hangar
HX1, HZ1 = 199, 119                 # the hangar's far corner
WALL_TOP = 40                       # the walls' top row; the vault rises 18 rows over it
DOOR = (20, 99, 36)                 # the great door in the east wall: z0, z1, top row
HULL_TINT, PANEL_TINT, BELLY_TINT = 0x5A636D, 0x7E8892, 0x48515A


def _vault_y(z: int) -> int:
    t = (z - HZ1 / 2) / (HZ1 / 2 + 1)
    return WALL_TOP + round(18 * math.sqrt(max(0.0, 1 - t * t)))


def _hangar_shell(d: Design) -> None:
    """Foundation, floor (moss and grass in the sunlit east half), walls with buttresses, the vaulted roof — its east half
    fallen in, a few ribs still standing — and the great door with its panels pushed aside."""
    _box(d, 0, 0, 0, HX1, 0, HZ1, WALL_DARK)
    sunlit = Mix(SLAB, (MOSS, 0.2), (GRASS, 0.25), (Mat("dirt"), 0.08), seed=41)
    d.fill(solids.box(0, 1, 0, HX1, 1, HZ1),
           lambda x, y, z: BAND if z in (19, 100) else sunlit if x > 105 + 10 * math.sin(z / 9) else SLAB)
    walls = solids.box_walls(0, 2, 0, HX1, WALL_TOP, HZ1)
    d.fill(walls, lambda x, y, z: BAND if y in (16, WALL_TOP) else WALL_MOSSY if y < 8 else WALL)
    for x in range(10, HX1, 10):
        for z0 in (1, HZ1 - 2):
            _box(d, x, 2, z0, x + 1, WALL_TOP, z0 + 1, FRAME)
    for x in range(HX1 + 1):
        fallen = x > 100 and h01(x, 0, 0, 5) < 0.5 + (x - 100) / 180
        for z in range(HZ1 + 1):
            y, y2 = _vault_y(z), _vault_y(min(HZ1, z + 1))
            rib = x % 10 == 0
            if x > 100 and not rib and solids.value_noise(x / 14, 0, z / 14, 7) > 0.35 - (x - 100) / 400:
                continue                                                  # the fallen half: holes in the panels
            if rib and fallen and abs(z - HZ1 / 2) < 20 + 30 * h01(x, 1, 0, 6):
                continue                                                  # a rib broken off mid-span
            if z in (0, HZ1):
                y2 = WALL_TOP + 1                                         # the vault's foot sits on the wall
            for yy in range(min(y, y2), max(y, y2) + 1):
                d.put(x, yy, z, FRAME if rib else STEEL)
    dz0, dz1, top = DOOR
    _box(d, HX1, 2, dz0, HX1, top, dz1, CLEAR)
    _box(d, HX1, top + 1, dz0, HX1, top + 2, dz1, BAND)
    for z0, z1 in ((3, dz0 - 2), (dz1 + 2, HZ1 - 3)):
        d.fill(solids.box(HX1 - 3, 2, z0, HX1 - 2, top, z1), lambda x, y, z: BAND if y % 9 == 4 else FRAME)


def _hangar_joints(d: Design) -> None:
    """The doorways into the main building (west wall) and the great hall (north wall)."""
    _, y, z0 = local(HANGAR, JOINT_MAIN_HANGAR["x"][1], JOINT_MAIN_HANGAR["y"], JOINT_MAIN_HANGAR["z"][0])
    z1 = z0 + JOINT_MAIN_HANGAR["z"][1] - JOINT_MAIN_HANGAR["z"][0]
    _box(d, 0, y, z0, 1, y + JOINT_MAIN_HANGAR["h"] - 1, z1, CLEAR)
    x0, y, _ = local(HANGAR, JOINT_HANGAR_HALL["x"][0], JOINT_HANGAR_HALL["y"], 0)
    x1 = x0 + JOINT_HANGAR_HALL["x"][1] - JOINT_HANGAR_HALL["x"][0]
    _box(d, x0, y, HZ1 - 2, x1, y + JOINT_HANGAR_HALL["h"] - 1, HZ1, CLEAR)
    _box(d, x0 - 1, y + JOINT_HANGAR_HALL["h"], HZ1 - 2, x1 + 1, y + JOINT_HANGAR_HALL["h"], HZ1, BAND)


def _hangar_fittings(d: Design) -> None:
    """Gantries along both long walls (kept ladders up to them), two crane beams — the east one broken, half of it on the
    floor — fuel tanks along the north wall and a tank that rolled away, pipes."""
    for z0, edge, lz in ((1, 4, 1), (HZ1 - 3, HZ1 - 4, HZ1 - 1)):
        _box(d, 3, 16, z0, HX1 - 4, 16, z0 + 2, FLOOR)
        fence_rail(d, [(x, 17, edge) for x in range(3, HX1 - 3)], Mat("iron_block", tint=PALE))
        for lx in (8, 108, 188):
            d.fill(solids.box(lx, 2, lz, lx, 17, lz), Mat("ladder", keep=True))
    for cx, broken in ((60, False), (140, True)):
        z_end = 70 if broken else HZ1 - 3
        _box(d, cx, 34, 3, cx + 1, 35, z_end, FRAME)
        _box(d, cx, 26, 40, cx + 1, 33, 40, Mat("iron_block", tint=PALE))     # the hook's cable
        d.put(cx, 25, 40, FRAME, shape=sc.pack(sc.PYRAMID, up=sc.UP_NY))
        if broken:
            d.fill(solids.line((cx, 2, 72), (cx + 14, 2, HZ1 - 8)), FRAME)
            d.fill(solids.line((cx + 1, 3, 73), (cx + 15, 3, HZ1 - 7)), FRAME)
    tank = Mat("iron_block", tint=PALE)
    for tx in (14, 28, 42):
        d.fill(solids.cylinder(tx, HZ1 - 12, 2, 15, 4.5), lambda x, y, z: BAND if y in (5, 12) else tank)
        d.fill(solids.line((tx, 16, HZ1 - 12), (tx, 16, HZ1 - 5)), Mat("factory_pipe"))
    d.capsule((165, 6, 92), (180, 6, 96), 4.0, tank)
    d.chest(30, 2, HZ1 - 8, table="ruin_common")
    d.chest(178, 2, 40, table="ruin_rich")


def _ship(seed: int, length: int = 34) -> ShipDesign:
    """A small SPS ship, about 34 long (bow +z): a blue-grey hull with a light flank band, a cockpit glass at the bow, side
    windows, a hatch, two engine bells and three landing legs — holed by age and overgrown on top."""
    s = ShipDesign("part", "part")
    cx = 7
    hull_m = Mix(Mat("iron_block", tint=HULL_TINT), (Mat("iron_block", tint=PANEL_TINT), 0.12), seed=seed)
    hull = s.loft([
        (0, ship_section(cx, keel=5, deck=11, beam=9)),
        (6, ship_section(cx, keel=3, deck=12, beam=13, bilge=0.45)),
        (20, ship_section(cx, keel=3, deck=12, beam=13, bilge=0.45)),
        (length - 1, ship_section(cx, keel=7, deck=11, beam=5)),
    ], Mat("iron_block", tint=BELLY_TINT), wall=1, skin=hull_m, bow=12, stern=3, tip=0.25, anchor="deck")
    s.deck(hull, 5, FLOOR)
    s.restyle([p for p in s.block_cells() if p[1] == 9], tint=0x3E6A8E)
    s.window_band(hull, 9, 10, side="front", every=1, width=1, start=cx - 2, end=cx + 2)
    s.window_band(hull, 8, 8, every=5, width=2, start=8, end=24)
    _box(s, 0, 6, 12, 1, 8, 13, CLEAR)                                    # the hatch, port side
    for x in (cx - 3, cx + 3):
        s.nozzle(x, 8, 0, 2.5, 3, m=Mat("engine_nozzle"))
    for lx, lz in ((cx, length - 9), (cx - 4, 6), (cx + 4, 6)):
        s.fill(solids.box(lx, 0, lz, lx, 3, lz), Mat("iron_block", tint=PALE))
    for p in list(s.block_cells()):
        if p[1] > 6 and h01(*p, seed) < 0.06:
            s.put(*p, CLEAR)                                              # holes rusted through
    for x, y, z in solids.highest_cells(s.block_cells()):
        if h01(x, y, z, seed + 1) < 0.35:
            put_free(s, [(x, y + 1, z)], LEAVES)
    s.normalize()
    return s


def _hangar_ships(d: Design) -> list[tuple[int, int, int, int]]:
    """Three ships parked with their bows toward the great door; returns their footprints (x0, z0, x1, z1)."""
    spots = []
    for k, (x, z) in enumerate(((30, 36), (72, 70), (124, 30))):
        ship = _ship(seed=11 + k)
        d.stamp(ship, x, 2, z, turns=3)
        b = solids.bounds(list(ship.plain))
        spots.append((x, z, x + b[5] - b[2], z + b[3] - b[0]))
    return spots


def _hangar_overgrowth(d: Design) -> None:
    """Jungle in the sunlit half: young trees, leaf patches, ferns; creepers on the walls."""
    for k, (tx, tz) in enumerate(((118, 70), (150, 20), (165, 60), (188, 104), (132, 104), (176, 12))):
        tree(d, tx, 2, tz, height=8 + 3 * (k % 3), crown=4 + k % 2, seed=60 + k, keep=False)
    for k in range(14):
        leaf_patch(d, 108 + (k * 23) % 88, 2, 6 + (k * 41) % 108, 3 + k % 3, seed=70 + k, density=0.5)
    for x, y, z in solids.box_walls(1, 2, 1, HX1 - 1, 14, HZ1 - 1):
        if h01(x, y, z, 81) < 0.04:
            put_free(d, [(x, y, z)], LEAVES)


def hangar() -> Design:
    d = earth_design(HANGAR, "SPS Hangar", ruin="always", ruinProfile="masonry", ruinStrength=0.3, groundRow=1)
    _hangar_shell(d)
    _hangar_joints(d)
    _hangar_fittings(d)
    spots = _hangar_ships(d)
    _hangar_overgrowth(d)
    d.sound(100, 3, 60, "amb_station_hangar", radius=110, volume=0.35)
    # the ships age gently (they are holed already), the fallen half of the roof decays hard, the rest as a ruin
    for x0, z0, x1, z1 in spots:
        d.ruin_zone(x0, 2, z0, x1, 20, z1, profile="masonry", strength=0.12)
    d.ruin_zone(101, 30, 0, HX1, 58, HZ1, profile="masonry", strength=0.85)
    _expect_size(d)
    return d


# ========================================================================================================= the great hall
HW, HL, HTOP = 95, 39, 40           # the hall's far x and z, its walls' top row
GATE = (8, 31, 31)                  # the gate opening in the west wall: z0, z1, top row
GIANT = (40, 2, 20)                 # the fallen giant's marker: it stands on the floor, the body lies toward +x
PEDESTAL = (80, 16)                 # the pedestal's low corner (9 x 9, rows 2..4)
SPHERE = (84, 7, 20)                # the sphere's centre (radius 2.5: as big as the giant's head)
SHARD = Mat("glass", tint=0xA8DCEA, keep=True)


def _hall_shell(d: Design) -> None:
    """Floor with a blue runner from the gate to the pedestal, two-thick walls, pillars with blue lamps, clerestory
    windows, the vaulted roof, the gate's frame and the doorway into the hangar."""
    _box(d, 0, 0, 0, HW, 0, HL, WALL_DARK)
    floor = Mat("concrete", tint=0x4C5560)
    d.fill(solids.box(0, 1, 0, HW, 1, HL), lambda x, y, z: BAND if 18 <= z <= 21 else floor)
    d.fill(solids.box_walls(0, 2, 0, HW, HTOP, HL, wall=2), lambda x, y, z: BAND if y in (14, HTOP) else STEEL)
    for x in range(12, HW - 3, 12):
        for z0 in (2, HL - 3):
            _box(d, x, 2, z0, x + 1, HTOP, z0 + 1, FRAME)
            _box(d, x, HTOP - 2, z0, x + 1, HTOP, z0 + 1, BAND)
            d.put(x, 12, z0 + 2 if z0 == 2 else z0 - 1, LAMP_BLUE)
        for z in (0, 1, HL - 1, HL):
            _box(d, x + 4, 28, z, x + 8, 35, z, GLASS)
    def vault(z: int) -> int:
        t = (z - HL / 2) / (HL / 2 + 1)
        return HTOP + 1 + round(6 * math.sqrt(max(0.0, 1 - t * t)))

    for x in range(HW + 1):
        for z in range(HL + 1):
            y, y2 = vault(z), HTOP + 1 if z in (0, HL) else vault(z + 1 if z < HL / 2 else z - 1)
            d.fill([(x, yy, z) for yy in range(min(y, y2), max(y, y2) + 1)], FRAME if x % 12 == 0 else STEEL)
    z0, z1, top = GATE
    _box(d, 0, 2, z0, 1, top, z1, CLEAR)
    _box(d, 0, 2, z0 - 2, 2, top + 3, z0 - 1, FRAME)
    _box(d, 0, 2, z1 + 1, 2, top + 3, z1 + 2, FRAME)
    _box(d, 0, top + 1, z0 - 2, 2, top + 3, z1 + 2, BAND)
    hx0, y, _ = local(HALL, JOINT_HANGAR_HALL["x"][0], JOINT_HANGAR_HALL["y"], 0)
    hx1 = hx0 + JOINT_HANGAR_HALL["x"][1] - JOINT_HANGAR_HALL["x"][0]
    _box(d, hx0, y, 0, hx1, y + JOINT_HANGAR_HALL["h"] - 1, 1, CLEAR)


def _gate_wings(d: Design) -> None:
    """The two metal wings, each 12 wide and 30 high, swung in from their hinges at the frame (75 degrees open)."""
    z0, z1, top = GATE
    wing = Mat("iron_block", tint=0x4A525C)
    for hz, sign in ((z0, 1), (z1 + 1, -1)):
        a = math.radians(75)
        ex, ez = 2 + 12 * math.sin(a), hz + sign * 12 * math.cos(a)
        cells = d.wall_line(2.5, hz + 0.5 * sign, ex, ez, 2, top, 1.2, wing)
        d.restyle([p for p in cells if p[1] % 8 == 5], tint=0x3E6A8E)


def _pedestal_and_sphere(d: Design) -> None:
    """The pedestal (9 x 9 x 3) with steps on the gate side, the broken glass sphere on it (its lower half-shell, a jagged
    rim) and its shards on the pedestal and the floor — all kept."""
    px, pz = PEDESTAL
    d.fill(solids.box(px, 2, pz, px + 8, 4, pz + 8), lambda x, y, z: BAND if y == 4 and (x in (px, px + 8) or z in (pz, pz + 8))
           else FRAME)
    d.stairs(px - 3, 2, pz + 2, 1, 0, 3, STAIR, width=5)
    cx, cy, cz = SPHERE
    for x, y, z in solids.sphere(cx, cy, cz, 2.5, wall=1):
        if y < cy or (y == cy and h01(x, y, z, 3) < 0.55):
            d.put(x, y, z, SHARD)
    shapes = (sc.pack(sc.SLAB), sc.pack(sc.QUARTER_CUBE), sc.pack(sc.SHEET), sc.pack(sc.PANEL), sc.pack(sc.PYRAMID))
    for k in range(34):
        a, r = h01(k, 0, 0, 11) * 2 * math.pi, 3 + h01(k, 1, 0, 12) * 7
        x, z = round(cx + r * math.cos(a)), round(cz + r * math.sin(a))
        on_pedestal = px <= x <= px + 8 and pz <= z <= pz + 8
        y = 5 if on_pedestal else 2
        if x >= HW - 1 or not 2 <= z <= HL - 2 or (px - 3 <= x < px and pz + 2 <= z <= pz + 6):
            continue
        put_free(d, [(x, y, z)], SHARD, shape=shapes[k % len(shapes)])


def _hall_whisper(d: Design) -> None:
    """Only a whisper of decay: moss and a few leaves blown in at the gate, a creeper on the gate's frame."""
    for x, z in solids.disc(5, 20, 9):
        if h01(x, 1, z, 31) < 0.3 and 0 <= x and 2 <= z <= HL - 2:
            d.put(x, 1, z, MOSS)
    leaf_patch(d, 4, 2, 13, 2.5, seed=32, density=0.35)
    leaf_patch(d, 6, 2, 27, 2.0, seed=33, density=0.3)
    for y in range(18, 34):
        if h01(1, y, 5, 34) < 0.6:
            put_free(d, [(3, y, 5)], LEAVES)


def hall() -> Design:
    d = earth_design(HALL, "The Great Hall", groundRow=1)
    _hall_shell(d)
    _gate_wings(d)
    _pedestal_and_sphere(d)
    _hall_whisper(d)
    d.guardian_giant(*GIANT, facing=3)                                    # its feet toward the gate, never waking
    d.sound(60, 3, 20, "amb_giant_hull", radius=70, volume=0.3)
    _expect_size(d)
    return d


def build() -> list[Design]:
    return [main_building(), hangar(), servers(), core(), hall()]


if __name__ == "__main__":
    for design in build():
        design.write()
