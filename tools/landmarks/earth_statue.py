"""Earth's two great statues (#3982), figures built in their own space like the school club's statues (_figures.py):

- `earth_new_york_statue` ("Statue of the Stars", about 93 high): a giant robed figure in green copper on an
  eleven-pointed star terrace and a tall stone pedestal, raising a glowing golden torch high in its right hand and
  holding a tablet against its side with the left, a crown of seven rays on its head. A spiral stair climbs the pedestal
  through a hole in the balcony into the hollow robe; a ladder up the iron pylon in the figure's middle leads to a little
  platform inside the head, where windows in the crown look out over the water.
- `earth_rio_statue` ("Statue on the Mountain", 40 high, made to stand on a hilltop): a pale stone figure in a long
  tunic with its arms stretched wide, the sleeves hanging from them, on a square pedestal with a small quiet room inside,
  on a paved viewing terrace with a balustrade and floodlights.

Built in figure space (x right, y up, z = the way it looks), then placed facing -Z (the preview's front view): the
raised torch arm appears on the viewer's left. Intact, lit and protected (an inhabited city's landmark); pin-only —
placed by Earth's location list only.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, glow, ring_lights
from _figures import blob, front_cells, raster
from geom import CLEAR, Design, Mat, Mix, Sculpture

KEYS = ("earth_new_york_statue", "earth_rio_statue")
Cell = tuple[int, int, int]


def place(d: Design, s: Sculpture, cx: int, base: int, cz: int, wall: int = 2, open_bottom: bool = False) -> None:
    """Writes a figure-space sculpture turned to face -Z with its feet on row `base` over (cx, cz)."""
    placed = Sculpture()
    for cells, m in s.parts:
        placed.add({(cx + x, base + y, cz - z) for x, y, z in cells}, m)
    placed.write(d, wall=wall, open_bottom=open_bottom)


def _fold(x: float, y: float, z: float, n: int = 9) -> float:
    return math.cos(n * math.atan2(x, z) + 0.3 * math.sin(y / 5.0))


# --- the Statue of the Stars ------------------------------------------------------------------------------------------
NY_C = 30                           # the centre column
STAR_R, STAR_IN, STAR_H = 30, 22, 7  # the star terrace
PED = 11                            # the pedestal's half size
PED_TOP = 44                        # the balcony slab's row: the figure stands on it
COPPER = Mat("verdigris_copper", tint=0x6CB79C)
COPPER_LIGHT = Mat("verdigris_copper", tint=0x82C8AE)
COPPER_DARK = Mat("verdigris_copper", tint=0x4E9680)
FLAME = Mat("gold_block", glow=0xFFB838)
PED_STONE = Mat("granite", tint=0xC4B4A4)
PED_TRIM = Mat("travertine", tint=0xE2D8C8)
FORT = Mat("stone", tint=0xA8A49C)

WAIST = 15.0
SHOULDER = 24.5
HEAD = (0.0, 30.5, 0.3)


def ny_figure() -> Sculpture:
    """The robed figure, feet (the hem) on y = 0, looking along +z: robe with folds, torso, shoulders, a thick neck,
    the head with its crown band and seven rays, the raised right arm (at -x) with the torch, the tablet on the left."""
    skin = Mix(COPPER, (COPPER_LIGHT, 0.15), seed=13)
    s = Sculpture()

    def robe(x: float, y: float, z: float) -> bool:
        t = max(0.0, min(1.0, y / WAIST))
        rx, rz = 9.0 - 3.0 * t, 7.0 - 2.2 * t
        k = 1 + 0.06 * _fold(x, y, z)
        return (x / (rx * k)) ** 2 + (z / (rz * k)) ** 2 <= 1.0
    cells = raster(robe, (-10, 0, -8), (10, int(WAIST) + 1, 8))
    s.add(cells, skin)
    s.add({p for p in cells if _fold(*p) < -0.6}, COPPER_DARK)
    s.add(blob((0, WAIST + 5.0, 0.2), (5.6, 6.4, 4.2), 2.4), skin)                      # the torso
    s.capsule((-5.6, SHOULDER, 0), (5.6, SHOULDER, 0), 2.6, skin)                      # the shoulders
    s.capsule((-5.0, WAIST - 3.0, 4.6), (5.4, SHOULDER - 0.5, 3.0), 1.7, COPPER_DARK, 1.3)   # the toga's fold
    s.capsule((0, SHOULDER, 0), (0, HEAD[1] - 3.5, 0.2), 3.2, skin)                    # the neck (the ladder climbs it)
    s.add(blob(HEAD, (4.7, 5.0, 4.6), 2.2), skin)                                       # the head
    s.add(blob((0, HEAD[1] - 3.6, 2.4), (2.4, 1.4, 2.0), 2.2), skin)                   # the chin
    hx, hy, hz = HEAD
    band = {(x, y, z) for x, y, z in raster(lambda x, y, z: 4.2 <= math.hypot(x - hx, z - hz) <= 5.6,
                                           (-6, hy + 1.5, -6), (6, hy + 2.5, 6))}
    s.add(band, COPPER_DARK)                                                            # the crown's band
    for k in range(7):                                                                  # the seven rays
        a = math.radians(-72 + 24 * k)
        root = (hx + 4.2 * math.sin(a), hy + 3.2, hz + 1.2)
        tip = (hx + 11.5 * math.sin(a), hy + 3.2 + 7.0 * math.cos(a) ** 0.7, hz + 2.6)
        s.capsule(root, tip, 0.95, COPPER_LIGHT, 0.4)
    # The raised right arm and the torch.
    s.capsule((-5.8, SHOULDER, 0), (-7.6, SHOULDER + 6.8, 0.6), 2.0, skin, 1.8)
    s.capsule((-7.6, SHOULDER + 6.8, 0.6), (-8.2, SHOULDER + 13.2, 0.8), 1.8, skin, 1.5)
    s.add(blob((-8.2, SHOULDER + 14.6, 0.8), (1.9, 1.9, 1.9), 2.2), skin)
    s.capsule((-8.2, SHOULDER + 15.4, 0.8), (-8.2, SHOULDER + 18.8, 0.8), 1.4, COPPER_DARK, 1.8)
    s.capsule((-8.2, SHOULDER + 19.0, 0.8), (-8.2, SHOULDER + 19.4, 0.8), 2.4, Mat("gold_block"))
    s.add(blob((-8.2, SHOULDER + 21.6, 0.8), (1.8, 2.8, 1.8), 1.8), FLAME)
    # The left arm bent, the hand holding the tablet against the side.
    s.capsule((5.8, SHOULDER - 0.4, 0), (7.4, SHOULDER - 7.0, 1.4), 1.9, skin, 1.7)
    s.capsule((7.4, SHOULDER - 7.0, 1.4), (6.4, SHOULDER - 4.6, 4.2), 1.6, skin, 1.4)
    s.add({(x, y, z) for x in range(5, 9) for y in range(round(SHOULDER) - 14, round(SHOULDER) - 2) for z in (4, 5)},
          COPPER_LIGHT)                                                                 # the tablet
    solid = s.solid()
    eyes = [(x, round(hy) + 1) for x in (-2, -1, 1, 2)]
    s.add(front_cells(solid, eyes), COPPER_DARK)
    return s


def star_terrace(d: Design) -> None:
    """The eleven-pointed star of the old fort: walls, a paved top, a parapet; a stair up the front notch."""
    c = NY_C
    star2d = solids.star(c, c, STAR_R, STAR_IN, points=11)
    d.prism(star2d - solids.erode2d(star2d, 2), 0, STAR_H - 2, FORT)
    d.prism(star2d, STAR_H - 1, STAR_H - 1, Mat("concrete", tint=0xB8B2A6))
    d.prism(star2d - solids.erode2d(star2d, 1), STAR_H, STAR_H, FORT)
    notch_z = min(z for x, z in star2d if x == c)
    x0, z0 = c - 3, notch_z - 5
    d.stairs(x0, 0, z0, 0, 1, STAR_H, PED_TRIM, width=7, fill_below=True)
    for k in range(STAR_H):
        for x in range(x0, x0 + 7):
            for y in range(k + 1, STAR_H + 1):
                d.clear(x, y, z0 + k)
    for x, z in ((c - 4, notch_z + 1), (c + 4, notch_z + 1)):
        d.put(x, STAR_H, z, Mat("iron_block", tint=0x3A3A40, shape=sc.post()))
        d.put(x, STAR_H + 1, z, "lantern")


def ny_pedestal(d: Design) -> None:
    """The pedestal: a stepped foot, a shaft with corner pilasters and a portal, round windows, the cornice and the
    balcony with its balustrade; hollow, a spiral stair up the middle through the balcony into the robe."""
    c, y0 = NY_C, STAR_H - 1
    h = PED
    d.box(c - h - 1, y0, c - h - 1, c + h + 1, y0 + 4, c + h + 1, PED_TRIM)
    d.box(c - h, y0 + 5, c - h, c + h, PED_TOP - 5, c + h, PED_STONE)
    for k in (-h, h):
        for j in (-h, h):
            d.box(c + k - (1 if k < 0 else 0), y0 + 5, c + j - (1 if j < 0 else 0),
                  c + k + (1 if k > 0 else 0), PED_TOP - 5, c + j + (1 if j > 0 else 0), PED_TRIM)
    d.box(c - h - 1, PED_TOP - 4, c - h - 1, c + h + 1, PED_TOP - 2, c + h + 1, PED_TRIM)
    d.box(c - h - 1, PED_TOP - 1, c - h - 1, c + h + 1, PED_TOP, c + h + 1, PED_STONE)
    d.box(c - h + 2, y0 + 1, c - h + 2, c + h - 2, PED_TOP - 1, c + h - 2, CLEAR)        # hollow
    d.fill(solids.box(c - h - 1, PED_TOP + 1, c - h - 1, c + h + 1, PED_TOP + 1, c + h + 1)
           - solids.box(c - h, PED_TOP + 1, c - h, c + h - 0, PED_TOP + 1, c + h), PED_TRIM)  # the balustrade
    d.arch("z", c - 2, y0 + 1, c - h - 1, c - h + 2, 5, 7, kind="round")                  # the portal
    for row in (y0 + 14, y0 + 26):
        for axis, a0, a1 in (("z", c - h, c - h + 1), ("z", c + h - 1, c + h), ("x", c - h, c - h + 1), ("x", c + h - 1, c + h)):
            for u in (c - 6, c + 4):
                d.arch(axis, u, row, a0, a1, 3, 5, kind="round", m=Mat("glass"))
    for y in (y0 + 12, y0 + 24):                                                         # two band courses
        d.fill(solids.box(c - h - 1, y, c - h - 1, c + h + 1, y, c + h + 1) - solids.box(c - h, y, c - h, c + h, y, c + h),
               PED_TRIM)
    for y in range(y0 + 4, PED_TOP - 2, 9):
        for x, z in ((c - h + 2, c), (c + h - 2, c), (c, c + h - 2)):
            d.put(x, y, z, "lantern")


def ny_inside(d: Design) -> None:
    """The spiral stair to the robe, the pylon and ladder up to the head, the crown platform and its windows."""
    c = NY_C
    base = PED_TOP + 1
    d.spiral_stair(c, c, STAR_H, PED_TOP + 2, 1.5, 4.5, PED_TRIM, steps_per_turn=16, newel=Mat("iron_block"))
    head_floor = base + round(HEAD[1]) - 2
    for y in range(PED_TOP + 3, head_floor + 3):
        d.put(c, y, c, Mat("iron_block"))
        d.put(c + 1, y, c, "ladder")
    # The platform in the head: a floor round the ladder, the crown's front cells glazed.
    hx, hz = c, c - round(HEAD[2])
    floor = {(x, head_floor, z) for x, z in solids.disc(hx, hz, 3.2)} - {(c + 1, head_floor, c), (c, head_floor, c)}
    d.fill(floor, Mat("steel_floor"))
    for y in range(head_floor + 1, head_floor + 4):
        for x, z in solids.disc(hx, hz, 3.2):
            if (x, z) not in ((c, c), (c + 1, c)):
                d.clear(x, y, z)
    for x in range(hx - 3, hx + 4):
        for y in (head_floor + 1, head_floor + 2):
            for z in range(hz - 6, hz - 2):
                if d.is_block(x, y, z):
                    d.put(x, y, z, Mat("glass", tint=0xB8E0D8))
                    break
    d.put(hx - 2, head_floor + 1, hz + 2, "lantern")
    d.chest(hx + 2, head_floor + 1, hz + 1, items="crystal:1-2,data_fragment:1,gold_ingot:1-2")
    # A door out of the robe's back onto the balcony.
    for y in range(base, base + 3):
        for x in (c - 1, c, c + 1):
            for z in range(c + 4, c + 9):
                if d.is_block(x, y, z):
                    d.clear(x, y, z)


def new_york() -> Design:
    d = earth_design("earth_new_york_statue", "Statue of the Stars")
    star_terrace(d)
    ny_pedestal(d)
    place(d, ny_figure(), NY_C, PED_TOP + 1, NY_C, wall=2, open_bottom=True)
    ny_inside(d)
    d.chest(NY_C - 7, STAR_H, NY_C + 6, table="ruin_common")
    return d


# --- the Statue on the Mountain ---------------------------------------------------------------------------------------
RIO_C = 17
RIO_PED = (2, 9)                    # the pedestal's rows; the figure stands on row 10
STONE = Mat("travertine", tint=0xDCDCD4)
STONE_SHADE = Mat("travertine", tint=0xC6C6BE)


def rio_figure() -> Sculpture:
    """The figure (30 high): a long straight tunic, the torso, arms stretched wide with the sleeves hanging from them,
    the head bowed a little; feet on y = 0, looking along +z."""
    s = Sculpture()

    def tunic(x: float, y: float, z: float) -> bool:
        t = max(0.0, min(1.0, y / 19))
        rx, rz = 4.4 - 0.9 * t, 3.3 - 0.6 * t
        return (x / (rx * (1 + 0.05 * _fold(x, y, z, 7)))) ** 2 + (z / rz) ** 2 <= 1.0
    cells = raster(tunic, (-5, 0, -4), (5, 19, 4))
    s.add(cells, STONE)
    s.add({p for p in cells if _fold(*p, 7) < -0.6}, STONE_SHADE)
    s.add(blob((0, 21.0, 0), (3.8, 3.4, 2.7), 2.4), STONE)                              # the chest
    s.capsule((-13.5, 23.0, 0), (13.5, 23.0, 0), 1.5, STONE)                           # the arms
    for side in (-1, 1):
        s.add(blob((side * 14.3, 23.0, 0.3), (1.3, 1.4, 1.1), 2.2), STONE_SHADE)        # the hands
        sleeve = solids.polygon([(side * 3.5, 15.5), (side * 3.5, 22.5), (side * 11.0, 22.5), (side * 9.0, 20.5)])
        s.add({(x, y, z) for x, y in sleeve for z in (0, 1)}, STONE_SHADE)
    s.capsule((0, 23.5, 0), (0, 25.0, 0.3), 1.4, STONE)                                # the neck
    s.add(blob((0, 26.6, 0.7), (2.2, 2.6, 2.3), 2.2), STONE)                            # the head, bowed a little
    s.add(blob((0, 27.4, -0.4), (2.3, 2.2, 2.0), 2.2), STONE_SHADE)                    # the hair
    return s


def rio() -> Design:
    d = earth_design("earth_rio_statue", "Statue on the Mountain")
    c = RIO_C
    paving = Mat("stone", tint=0xB4B0A8)
    d.box(0, 0, 0, 2 * c, 1, 2 * c, paving)
    rail = solids.box(0, 2, 0, 2 * c, 2, 2 * c) - solids.box(1, 2, 1, 2 * c - 1, 2, 2 * c - 1)
    d.fill({p for p in rail if abs(p[0] - c) > 2}, Mat("travertine", tint=0xE4E0D6))         # gaps for the steps
    for x in range(c - 2, c + 3):                                                        # steps onto the terrace
        d.put(x, 1, 0, paving, shape=sc.stairs(0, 1))
        d.put(x, 1, 2 * c, paving, shape=sc.stairs(0, -1))
    d.box(c - 5, RIO_PED[0], c - 5, c + 5, RIO_PED[0], c + 5, STONE_SHADE)               # the pedestal's foot
    d.box(c - 4, RIO_PED[0] + 1, c - 4, c + 4, RIO_PED[1], c + 4, STONE)
    d.box(c - 3, RIO_PED[0] + 1, c - 3, c + 3, RIO_PED[0] + 4, c + 3, CLEAR)             # the quiet room
    for y in range(RIO_PED[0] + 1, RIO_PED[0] + 4):
        d.clear(c, y, c - 4)
    d.door(c, RIO_PED[0] + 1, c - 4)
    d.put(c, RIO_PED[0] + 1, c + 3, Mat("wood_planks", shape=sc.pack(sc.BENCH, 2)))
    d.put(c - 3, RIO_PED[0] + 3, c + 3, "lantern")
    d.chest(c + 3, RIO_PED[0] + 1, c + 3, table="ruin_common")
    place(d, rio_figure(), c, RIO_PED[1] + 1, c, wall=0)
    for x, z in ((2, 2), (2 * c - 2, 2), (2, 2 * c - 2), (2 * c - 2, 2 * c - 2)):       # the floodlights
        d.put(x, 2, z, glow("light_white", 0xFFF6E0))
    ring_lights(d, c, c, 2, 8.5, 6, phase=0.5)
    return d


def build() -> list[Design]:
    return [new_york(), rio()]


if __name__ == "__main__":
    for design in build():
        design.write()
