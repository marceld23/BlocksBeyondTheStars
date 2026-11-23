"""Earth's two needle towers (#3982): the TV tower of Berlin and the tall tower of Toronto.

- `earth_berlin_tv_tower` (about 201 high): a slender concrete shaft rising from a low pavilion with a zigzag roof,
  a steel sphere with two rows of windows (the observation deck and the cafe above it), the shaft going on through the
  sphere and a red-and-white antenna needle with a red light on its tip.
- `earth_toronto_tower` (about 250 high): a hexagonal concrete core on three tapering legs (a Y in plan), the
  disc-shaped main pod — its lower deck with a ring of glass floor to look straight down, a restaurant deck above, both
  glazed all round — a small sky pod higher up and the steel needle on top.

Both have a spiral stair up their hollow shaft with a doorway onto every deck, lanterns all the way and a chest or two
on the decks. Intact, lit and protected (an inhabited city's landmark); pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, glow, ring_lights
from geom import CLEAR, Design, Mat

KEYS = ("earth_berlin_tv_tower", "earth_toronto_tower")
CONCRETE = Mat("concrete", tint=0xC9C6BE)
STAIR = Mat("concrete", tint=0x9C9890)
RED = Mat("iron_block", tint=0xD8402C)
WHITE = Mat("iron_block", tint=0xF2F0EA)
RED_LIGHT = glow("light_red", 0xFF3020)


def doorways(d: Design, c: int, y: int, r: float, sides=((0, -1), (1, 0), (0, 1), (-1, 0)), height: int = 3) -> None:
    """A doorway three wide through the round wall at radius r on each side, its sill on row y."""
    for ox, oz in sides:
        for t in (-1, 0, 1):
            for k in range(math.floor(r) - 1, math.ceil(r) + 2):
                x, z = (c + t, c + oz * k) if ox == 0 else (c + ox * k, c + t)
                for yy in range(y, y + height):
                    d.clear(x, yy, z)


def shaft_lanterns(d: Design, c: int, y0: int, y1: int) -> None:
    """Lanterns on the shaft's inner wall every 12 rows, turning a quarter each time: the last free cell outside the
    stair (radius 3.5) before the wall, along the axis."""
    for i, y in enumerate(range(y0 + 6, y1, 12)):
        ox, oz = ((1, 0), (0, 1), (-1, 0), (0, -1))[i % 4]
        k = 4
        while (c + ox * (k + 1), y, c + oz * (k + 1)) not in d.plain and k < 12:
            k += 1
        if (c + ox * k, y, c + oz * k) not in d.plain:
            d.put(c + ox * k, y, c + oz * k, "lantern")


def needle(d: Design, c: int, y0: int, y1: int, thick: int, band: int = 6) -> None:
    """A red-and-white needle: a 3 x 3 stem for `thick` rows, then a slim post, painted in bands; a red light on top."""
    for y in range(y0, y1):
        m = RED if ((y - y0) // band) % 2 == 0 else WHITE
        if y < y0 + thick:
            d.fill(solids.box(c - 1, y, c - 1, c + 1, y, c + 1), m)
        else:
            d.put(c, y, c, m, shape=sc.post())
    d.put(c, y1, c, RED_LIGHT)


# --- Berlin -----------------------------------------------------------------------------------------------------------
B_C = 20
B_SPHERE_Y, B_SPHERE_R = 118, 11.5
B_DECK1, B_DECK2 = 112, 119          # the floors' rows inside the sphere (walk one row above)


def berlin_shaft_r(y: int) -> float:
    """The shaft's outer radius: 7.6 at the foot narrowing to 5.6 under the sphere (a wall two thick round the stair),
    4.6 above it."""
    if y > B_SPHERE_Y:
        return 4.6
    return 7.6 - 2.0 * min(1.0, y / 105)


def berlin_pavilion(d: Design) -> None:
    """The low pavilion round the foot: a ring of glass between concrete piers, a zigzag roof, the paving."""
    c = B_C
    d.disc(c, c, 0, 19.5, Mat("concrete", tint=0x8C8A86))
    ring = solids.ring2d(c, c, 17, 1)
    for y in range(1, 6):
        d.fill({(x, y, z) for x, z in ring}, lambda x, y, z: CONCRETE if round(math.degrees(math.atan2(z - c, x - c))) % 30 < 6
               else Mat("glass"))
    d.fill({(x, 6, z) for x, z in solids.disc(c, c, 17.5) - solids.disc(c, c, 6.5)}, CONCRETE)
    for x, z in solids.disc(c, c, 18) - solids.disc(c, c, 16.5):           # the zigzag folds on the rim
        a = math.atan2(z - c, x - c)
        if math.cos(12 * a) > 0:
            d.put(x, 7, z, CONCRETE)
            if math.cos(12 * a) > 0.7:
                d.put(x, 8, z, CONCRETE)
    doorways(d, c, 1, 17, sides=((0, -1), (0, 1)))
    ring_lights(d, c, c, 4, 15.8, 8)


def berlin() -> Design:
    d = earth_design("earth_berlin_tv_tower", "Berlin TV Tower")
    c = B_C
    berlin_pavilion(d)
    top = B_SPHERE_Y + 28
    for y in range(1, top):
        if B_SPHERE_Y - B_SPHERE_R < y < B_SPHERE_Y + B_SPHERE_R - 1:   # inside the sphere the shaft is open
            continue
        d.cylinder(c, c, y, y, berlin_shaft_r(y), CONCRETE, wall=2)
    d.disc(c, c, top, 4.6, CONCRETE)
    # The sphere: steel panels, two window bands, the floors, a glowing band under it.
    cy, r = B_SPHERE_Y, B_SPHERE_R
    skin = solids.shell(solids.sphere(c, cy, c, r), 1)
    panel = Mat("aluminium_block", tint=0xB4BCC4)
    d.fill(skin, lambda x, y, z: Mat("glass", tint=0x9FB8C8) if B_DECK1 + 1 <= y <= B_DECK1 + 4 or B_DECK2 + 1 <= y <= B_DECK2 + 3
           else panel if (x + z + y) % 7 else Mat("aluminium_block", tint=0x9AA4AE))
    for fy in (B_DECK1, B_DECK2):
        rr = math.sqrt(max(0.0, r * r - (fy - cy) ** 2)) - 0.6
        d.fill({(x, fy, z) for x, z in solids.disc(c, c, rr) - solids.disc(c, c, 3.5)}, Mat("steel_floor"), where="empty")
        ring_lights(d, c, c, fy + 1, rr - 1.2, 8, phase=0.5)
    d.fill({(x, round(cy - r) + 1, z) for x, z in solids.disc(c, c, 5.5) - solids.disc(c, c, 4.4)}, glow("light_white", 0xFFF4D8))
    # The way up: a spiral stair from the foot to the cafe floor, doorways at the foot.
    d.spiral_stair(c, c, 1, B_DECK2, 1.5, 3.5, STAIR, steps_per_turn=14, newel=STAIR)
    doorways(d, c, 1, berlin_shaft_r(1), sides=((0, -1), (0, 1)))
    shaft_lanterns(d, c, 1, B_DECK1 - 4)
    needle(d, c, top + 1, 200, 26)
    d.chest(c + 7, B_DECK1 + 1, c, table="ruin_common")
    d.chest(c - 6, B_DECK2 + 1, c + 2, items="crystal:1-2,data_fragment:1,circuit_board:1")
    return d


# --- Toronto ----------------------------------------------------------------------------------------------------------
T_C = 22
T_POD = 150                         # the main pod's underside starts here
T_GLASS = 154                       # the glass-floor deck (walk 155)
T_REST = 161                        # the restaurant deck (walk 162)
T_SKY = 202                         # the sky pod's floor (walk 203)
LEG_ANGLES = (math.pi / 2, math.pi / 2 + 2 * math.pi / 3, math.pi / 2 + 4 * math.pi / 3)   # +z, and 120 degrees on


def hexagon(c: float, r: float) -> set[tuple[int, int]]:
    return solids.polygon([(c + r * math.cos(k * math.pi / 3 + math.pi / 6), c + r * math.sin(k * math.pi / 3 + math.pi / 6))
                           for k in range(6)])


def toronto_core_r(y: int) -> float:
    return 6.2 if y < T_SKY + 8 else 3.6


def toronto_legs(d: Design) -> None:
    """The three legs: tapering concrete fins from the core out along the Y, from the ground up to the pod."""
    c = T_C
    for a in LEG_ANGLES:
        ux, uz = math.cos(a), math.sin(a)
        for y in range(1, T_POD - 2):
            reach = 13 * (1 - y / (T_POD + 6)) ** 1.15
            if reach < 0.8:
                break
            r0, r1 = 5.0, 5.5 + reach
            thick = 4.2 - 1.4 * y / T_POD
            cells = solids.segment2d(c + r0 * ux, c + r0 * uz, c + r1 * ux, c + r1 * uz, thick)
            d.fill({(x, y, z) for x, z in cells - hexagon(c, 5.2)}, CONCRETE)


def toronto_pod(d: Design) -> None:
    """The main pod: a cone underneath, the glass-floor deck behind slanted windows, the restaurant deck, the roof."""
    c = T_C
    under = solids.cone(c, c, T_POD, T_GLASS - 1, 8, 15)
    d.fill(solids.shell(under, 2), CONCRETE)
    inner = solids.disc(c, c, 10.5)
    d.fill({(x, T_GLASS, z) for x, z in solids.disc(c, c, 15) - inner}, Mat("glass_clear"))     # the glass floor
    d.fill({(x, T_GLASS, z) for x, z in inner - hexagon(c, 6.2)}, Mat("steel_floor"))
    d.fill({(x, T_GLASS, z) for x, z in solids.ring2d(c, c, 16, 1)}, glow("light_white", 0xF0F4FF))   # a light band
    for y in range(T_GLASS + 1, T_REST):                   # windows leaning out
        r = 15.5 + 0.25 * (y - T_GLASS)
        d.fill({(x, y, z) for x, z in solids.ring2d(c, c, r, 1)}, Mat("glass", tint=0x5C6878))
    d.fill({(x, T_REST, z) for x, z in solids.disc(c, c, 17.5) - hexagon(c, 6.2)}, Mat("steel_floor"))
    d.fill({(x, T_REST, z) for x, z in solids.ring2d(c, c, 17.5, 1)}, CONCRETE)
    for y in range(T_REST + 1, T_REST + 5):
        d.fill({(x, y, z) for x, z in solids.ring2d(c, c, 17, 1)}, Mat("glass", tint=0x5C6878))
    roof = solids.cone(c, c, T_REST + 5, T_REST + 10, 17.5, 7)
    d.fill(solids.shell(roof, 1, open_bottom=True), CONCRETE)
    d.fill({(x, T_REST + 5, z) for x, z in solids.ring2d(c, c, 17.5, 1)}, Mat("concrete", tint=0x8C8A86))
    for fy in (T_GLASS, T_REST):
        ring_lights(d, c, c, fy + 1, 9.0, 6, phase=0.5)


def toronto_sky_pod(d: Design) -> None:
    c = T_C
    d.fill(solids.shell(solids.cone(c, c, T_SKY - 4, T_SKY - 1, 4, 8.5), 1), CONCRETE)
    d.fill({(x, T_SKY, z) for x, z in solids.disc(c, c, 8.5) - hexagon(c, 6.2)}, Mat("steel_floor"))
    for y in range(T_SKY + 1, T_SKY + 4):
        d.fill({(x, y, z) for x, z in solids.ring2d(c, c, 8.5, 1)}, Mat("glass", tint=0x5C6878))
    d.fill({(x, T_SKY + 4, z) for x, z in solids.disc(c, c, 8.5)}, CONCRETE)
    ring_lights(d, c, c, T_SKY + 1, 7.2, 4, phase=0.5)


def toronto() -> Design:
    d = earth_design("earth_toronto_tower", "Toronto Tower")
    c = T_C
    d.disc(c, c, 0, 22, Mat("concrete", tint=0x8C8A86))
    toronto_legs(d)
    top = T_SKY + 16
    for y in range(1, top):
        r = toronto_core_r(y)
        ring = hexagon(c, r) - hexagon(c, r - 1.2)
        d.fill({(x, y, z) for x, z in ring}, CONCRETE)
    d.fill({(x, top, z) for x, z in hexagon(c, 3.6)}, CONCRETE)
    toronto_pod(d)
    toronto_sky_pod(d)
    d.spiral_stair(c, c, 1, T_SKY, 1.5, 3.5, STAIR, steps_per_turn=14, newel=STAIR)
    for fy in (T_GLASS, T_REST, T_SKY):
        doorways(d, c, fy + 1, 5.6)
    doorways(d, c, 1, 5.6, sides=((0, -1),))
    shaft_lanterns(d, c, 1, T_POD - 4)
    # The needle: a tapering steel mast on the core's cap, a red light on its tip.
    for y in range(top + 1, 249):
        r = 2.4 - 1.6 * (y - top) / (249 - top)
        m = Mat("iron_block", tint=0x9AA0A8) if (y // 8) % 2 else Mat("iron_block", tint=0xC8CCD2)
        if r >= 1.4:
            d.disc(c, c, y, r, m)
        else:
            d.put(c, y, c, m, shape=sc.post())
    d.put(c, 249, c, RED_LIGHT)
    d.chest(c + 9, T_GLASS + 1, c - 2, table="ruin_common")
    d.chest(c - 6, T_SKY + 1, c + 3, items="crystal:1-2,data_fragment:1-2,power_cell:1")
    return d


def build() -> list[Design]:
    return [berlin(), toronto()]


if __name__ == "__main__":
    for design in build():
        design.write()
