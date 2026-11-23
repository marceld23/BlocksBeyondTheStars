"""The G.D.S. tower of New York (#3982): `earth_gds_tower`, an active high-rise of the G.D.S. — 44 x 194 x 44 with its
plaza, the building 40 x 40, its roof at row 175, the mast's light at row 193.

Walls in the G.D.S. red (`iron_wall` tinted 0x8A1C24, as the G.D.S. city's houses — CityGenerator.Red) with a band of
their purple (0x3A1F5C, CityGenerator.Purple) every third storey and at the crown, purple-tinted windows all round. A
two-storey glass lobby with sliding doors and a reception desk, guarded by the G.D.S. guardians (`guard_post` markers)
at the door and inside; above it the trader floor with four vendors behind their counters; twenty-six office storeys
with desks and ceiling lights (a guardian on every sixth); a setback, five more storeys, and on the roof the glazed
observation deck with a terrace round it and the mast. A ladder shaft in the core reaches every floor. The letters stay
unexplained: no sign spells them out. Intact, lit and protected; pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

import shapecode as sc
import solids
from _earth import earth_design, fence_rail, glow, ladder_shaft, square_edge
from geom import CLEAR, Design, Mat

KEYS = ("earth_gds_tower",)
RED = 0x8A1C24                  # CityGenerator.Red
PURPLE = 0x3A1F5C               # CityGenerator.Purple
WALL = Mat("iron_wall", tint=RED)
WALL_DARK = Mat("iron_wall", tint=0x6E141C)
BAND = Mat("iron_wall", tint=PURPLE)
WINDOW = Mat("glass", tint=0x7048B0)
FLOOR = Mat("steel_floor")
CORE_WALL = Mat("iron_wall", tint=PURPLE)
LOW = (2, 41)                   # the lower section's walls (x and z)
HIGH = (7, 36)                  # the setback section's walls
LOBBY_TOP = 10                  # the lobby's ceiling slab (walk rows 1..9)
STOREY = 5
SETBACK = 145                   # the lower section's roof slab
ROOF = 175                      # the upper section's roof slab: the observation deck stands on it
DECK = (12, 31)                 # the observation deck's walls
MAST_TOP = 193
CORE = (20, 21)                 # the ladder shaft (two ladders along x at z = 21, doorways to -z)


def slabs() -> list[int]:
    return list(range(LOBBY_TOP, ROOF + 1, STOREY))


def section(lo: int, hi: int, y0: int, y1: int, d: Design, storey_index) -> None:
    """The walls of one section from row y0 to y1: red with purple-tinted windows (two wide, two between), a purple
    band on the slab row of every third storey, darker corner pilasters."""
    for y in range(y0, y1 + 1):
        ring = square_edge((lo + hi) / 2, (lo + hi) / 2, y, (hi - lo) / 2)
        k, row = storey_index(y)
        for x, yy, z in ring:
            u = x if z in (lo, hi) else z
            corner = (x in (lo, hi) and z in (lo, hi)) or u in (lo + 1, hi - 1)
            if row == 0:
                m = BAND if k % 3 == 0 else WALL
            elif corner:
                m = WALL_DARK
            elif 1 <= row <= 3 and (u - lo) % 4 in (1, 2):
                m = WINDOW
            else:
                m = WALL
            d.put(x, yy, z, m)


def storey_of(y: int) -> tuple[int, int]:
    """(storey index, the row within it: 0 = the slab) for a row above the lobby."""
    return (y - LOBBY_TOP) // STOREY, (y - LOBBY_TOP) % STOREY


def shell(d: Design) -> None:
    """The plaza, the lobby, both sections with their slabs, the crown band."""
    d.box(0, 0, 0, LOW[1] + 2, 0, LOW[1] + 2, Mat("concrete", tint=0x6A6670))
    d.fill(square_edge((LOW[0] + LOW[1]) / 2 + 0, (LOW[0] + LOW[1]) / 2, 0, (LOW[1] - LOW[0]) / 2 + 2), BAND)
    lo, hi = LOW
    # The lobby: glass between red piers, two storeys high.
    for y in range(1, LOBBY_TOP):
        for x, yy, z in square_edge((lo + hi) / 2, (lo + hi) / 2, y, (hi - lo) / 2):
            u = x if z in (lo, hi) else z
            d.put(x, yy, z, WALL_DARK if (u - lo) % 6 == 0 or y == LOBBY_TOP - 1 else Mat("glass", tint=0x9C80D0))
    section(lo, hi, LOBBY_TOP, SETBACK, d, storey_of)
    section(HIGH[0], HIGH[1], SETBACK + 1, ROOF, d, storey_of)
    for y in slabs():
        a, b = (LOW if y <= SETBACK else HIGH)
        d.box(a + 1, y, a + 1, b - 1, y, b - 1, FLOOR)
    d.box(lo + 1, 0, lo + 1, hi - 1, 0, hi - 1, Mat("steel_floor", tint=0x5A4A6A))
    d.box(lo, SETBACK, lo, hi, SETBACK, hi, BAND)                                      # the setback's terrace
    fence_rail(d, square_edge((lo + hi) / 2, (lo + hi) / 2, SETBACK + 1, (hi - lo) / 2), Mat("iron_block", tint=PURPLE))
    d.fill(square_edge((HIGH[0] + HIGH[1]) / 2, (HIGH[0] + HIGH[1]) / 2, ROOF - 1, (HIGH[1] - HIGH[0]) / 2),
           Mat("iron_wall", tint=PURPLE, glow=0x7A3AC8))                               # the glowing crown band
    d.box(HIGH[0], ROOF, HIGH[0], HIGH[1], ROOF, HIGH[1], BAND)


def entrance(d: Design) -> None:
    """Sliding doors in the front (-z) glass, guardian posts outside and in, the reception desk, two guardian heads on
    plinths flanking the door."""
    z = LOW[0]
    for x0 in (18, 24):
        for x in (x0, x0 + 1):
            for y in (1, 2, 3):
                d.clear(x, y, z)
        d.door(x0, 1, z, "slide")
    for x in (15, 28):                                                                # the plinths and heads
        d.put(x, 1, z - 1, Mat("iron_wall", tint=PURPLE))
        d.put(x, 2, z - 1, Mat("gds_guardian_head"))
    for x, zz in ((16, z - 2), (27, z - 2)):
        d.npc(x, 1, zz, "guard_post")
    for x, zz in ((13, z + 4), (30, z + 4), (13, 30), (30, 30)):
        d.npc(x, 1, zz, "guard_post")
    d.box(16, 1, 12, 27, 1, 12, Mat("shop_counter"))                                 # the reception desk
    for x in (14, 29):
        for y in range(1, LOBBY_TOP):
            d.put(x, y, 16, Mat("iron_wall", tint=PURPLE), shape=sc.pack(sc.CYLINDER))


def trader_floor(d: Design) -> None:
    """The first storey above the lobby: four vendors behind counters, one in each corner, and a guardian."""
    y = LOBBY_TOP + 1
    for (cx, cz, theme) in ((8, 8, "traders"), (35, 8, "ore_exchange"), (8, 35, "data_exchange"), (35, 35, "researchers")):
        sx = 1 if cx < 20 else -1
        sz = 1 if cz < 20 else -1
        for k in range(5):                                                            # an L of counter
            d.put(cx + sx * 3, y, cz + sz * k - sz, Mat("shop_counter"))
            d.put(cx + sx * k - sx, y, cz + sz * 3, Mat("shop_counter"))
        d.vendor(cx, y, cz, theme)
        d.put(cx - sx, y + 3, cz - sz, "lantern")
    d.npc(16, y, 16, "guard_post")


def offices(d: Design) -> None:
    """Desks and chairs on the office floors, ceiling lights on every floor, a guardian on every sixth."""
    for i, y in enumerate(slabs()[1:-1], start=1):
        a, b = (LOW if y < SETBACK else HIGH)
        if y == SETBACK:
            continue
        for lx in range(a + 6, b - 3, 8):
            for lz in range(a + 6, b - 3, 8):
                if abs(lx - 21) > 4 or abs(lz - 21) > 4:
                    d.put(lx, y + STOREY - 1, lz, "lantern")
        for dx in range(a + 4, b - 3, 8):
            for dz in (a + 3, b - 4):
                d.put(dx, y + 1, dz, Mat("wood_planks_light", shape=sc.pack(sc.TABLE)))
                d.put(dx + 1, y + 1, dz, Mat("wood_planks_light", shape=sc.pack(sc.TABLE)))
                d.put(dx, y + 1, dz + (1 if dz < 20 else -1), Mat("metal_panel", shape=sc.pack(sc.CHAIR, 0 if dz < 20 else 2)))
        if i % 6 == 0:
            d.npc(CORE[0], y + 1, CORE[1] - 4, "guard_post")


def deck(d: Design) -> None:
    """The observation deck on the roof: a glazed room with a red frame, a terrace round it with a railing, guardians,
    a chest, and the mast with its red light."""
    a, b = DECK
    y0 = ROOF + 1
    for y in range(y0, y0 + 5):
        for x, yy, z in square_edge((a + b) / 2, (a + b) / 2, y, (b - a) / 2):
            corner = x in (a, b) and z in (a, b)
            d.put(x, yy, z, WALL if corner or y == y0 + 4 else Mat("glass_clear"))
    d.box(a, y0 + 5, a, b, y0 + 5, b, BAND)
    for x in (20, 21):
        for y in (y0, y0 + 1, y0 + 2):
            d.clear(x, y, a)
    d.door(20, y0, a, "slide")
    fence_rail(d, square_edge((HIGH[0] + HIGH[1]) / 2, (HIGH[0] + HIGH[1]) / 2, y0, (HIGH[1] - HIGH[0]) / 2),
               Mat("iron_block", tint=PURPLE))
    d.npc(a + 3, y0, a + 3, "guard_post")
    d.npc(b - 3, y0, b - 3, "guard_post")
    d.chest(b - 3, y0, a + 3, items="crystal:1-2,data_fragment:2-3,circuit_board:1")
    for x, z in ((a + 4, b - 4), (b - 4, a + 4)):
        d.put(x, y0 + 4, z, "lantern")
    d.fill(solids.box(20, y0 + 6, 20, 22, y0 + 10, 22), WALL_DARK)
    d.fill([(21, y, 21) for y in range(y0 + 11, MAST_TOP)], Mat("iron_block", tint=0x3A3A40), shape=sc.post())
    d.put(21, MAST_TOP, 21, glow("light_red", 0xFF3020))
    for x, z in ((HIGH[0], HIGH[0]), (HIGH[1], HIGH[0]), (HIGH[0], HIGH[1]), (HIGH[1], HIGH[1])):
        d.put(x, ROOF + 1, z, glow("light_red", 0xFF3020))


def build() -> Design:
    d = earth_design("earth_gds_tower", "G.D.S. Tower")
    shell(d)
    entrance(d)
    trader_floor(d)
    offices(d)
    deck(d)
    floors = [1] + [y + 1 for y in slabs()]
    ladder_shaft(d, CORE[0], CORE[1], floors, opening="-z", walls=CORE_WALL)
    for y in floors[:-1]:                                                             # the slab under every landing
        d.fill(solids.box(CORE[0] - 1, y - 1, CORE[1] - 3, CORE[0] + 2, y - 1, CORE[1] - 2), FLOOR, where="empty")
    return d


if __name__ == "__main__":
    build().write()
