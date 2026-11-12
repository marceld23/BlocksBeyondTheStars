"""The singer statue: a giant robed figure in green copper holding a golden MICROPHONE high, as if it sang to the sky.

Liberty-style, made for the Sounders' world: a figure of verdigris copper in a folded robe with a draped sash, a crown
of seven rays, the right arm raised with a gilded microphone (it glows a little at night, like a torch would), the left
arm holding a tablet with a music note. It stands on a pale stone pedestal with an arched portal, windows and a spiral
stair inside up to a balcony at the figure's feet, and the pedestal stands on an eleven-pointed star terrace with a
stair up its front. A sound marker on the balcony plays the Sounders' humming choir, louder the closer you come.

Intact (`ruin: never`) and protected; a fixed landmark of Soaniri only (`planetTypes: sounder_world`).
Built in figure space (x right, y up, z = the way it looks), then placed facing -Z (the preview's front view): the
raised microphone arm appears on the viewer's left, like the original's torch.
"""
from __future__ import annotations

import math

import solids
from _figures import blob, front_cells, raster
from geom import Design, Mat, Mix, Sculpture

# The colours of the copper skin (dyes on verdigris_copper) and the gilded microphone.
PATINA = 0x7CC9A8           # the main patina
PATINA_LIGHT = 0x9EDBC2     # streaks where the rain washed it
PATINA_DARK = 0x4F937A      # the fold valleys, the face's eyes and the open singing mouth
SASH = 0xA8E2CA             # the draped sash, lighter than the robe
GOLD_GLOW = 0xFFD27A

STAR_R, STAR_IN, STAR_H = 50, 37, 8       # the star terrace: outer and inner radius, rows 0..STAR_H-1
PED = 38                                  # the pedestal's width at its foot
PED_TOP = 44                              # the balcony slab's row (the figure stands on it)
SIZE = 2 * STAR_R + 1                     # the footprint (x and z)
C = STAR_R                                # the centre column (x and z)
Cell = tuple[int, int, int]


# ---------------------------------------------------------------- the figure (figure space, y = 0 at the robe's hem)
def _robe_radius(y: float) -> tuple[float, float]:
    """The robe's half widths (x, z) at height y: a flared hem, falling straight, narrowing to the waist."""
    t = max(0.0, min(1.0, y / 42.0))
    rx = 10.4 - 2.4 * t + (0.5 if y < 2 else 0.0)
    rz = 8.0 - 1.8 * t + (0.4 if y < 2 else 0.0)
    return rx, rz


def _fold(x: float, y: float, z: float) -> float:
    """-1..1: where the robe's vertical folds lie (they wander a little with the height)."""
    a = math.atan2(x, z)
    return math.cos(11 * a + 0.35 * math.sin(y / 7.0))


def robe() -> tuple[set[Cell], set[Cell]]:
    """The robe from the hem to the waist with its folds: (all cells, the cells in a fold valley)."""
    def inside(x: float, y: float, z: float) -> bool:
        rx, rz = _robe_radius(y)
        k = 1 + 0.07 * _fold(x, y, z)
        return (x / (rx * k)) ** 2 + (z / (rz * k)) ** 2 <= 1.0

    cells = raster(inside, (-12, 0, -10), (12, 43, 10))
    valleys = {p for p in cells if _fold(*p) < -0.55}
    return cells, valleys


def figure() -> Sculpture:
    """The singer, standing at y = 0 (the hem), looking along +z; the raised arm at -x."""
    skin = Mix(Mat("verdigris_copper", tint=PATINA), (Mat("verdigris_copper", tint=PATINA_LIGHT), 0.18), seed=11)
    dark = Mat("verdigris_copper", tint=PATINA_DARK)
    sash = Mat("verdigris_copper", tint=SASH)
    gold = Mat("gold_block", glow=GOLD_GLOW)
    s = Sculpture()

    cells, valleys = robe()
    s.add(cells, skin)
    s.add(valleys, dark)
    s.add(blob((0, 47, 0.3), (8.2, 9.0, 6.2), 2.2), skin)                  # the upper body
    s.capsule((-7.5, 54, 0), (7.5, 54, 0), 3.6, skin)                      # the shoulders
    s.capsule((6.5, 55, 3.2), (-7.5, 33, 6.8), 2.4, sash)                  # the sash, left shoulder to right hip
    s.capsule((0, 53, 0), (0, 58, 0.4), 2.6, skin)                         # the neck

    head = (0.0, 61.5, 0.6)
    s.add(blob(head, (4.2, 5.1, 4.5), 2.0), skin)
    s.add(blob((0, 62.5, -2.2), (3.8, 4.0, 3.2), 2.0), skin)               # the hair, gathered at the back
    s.add(blob((0, 61.5, 5.2), (0.7, 1.3, 0.9), 2.0), skin)                # the nose

    # The crown: a band round the head and seven rays fanning out over the front half, rising a little.
    band = {p for p in raster(lambda x, y, z: 3.6 <= math.hypot(x / 1.0, (z - 0.4) / 1.05) <= 4.9,
                                (-6, 64, -6), (6, 65, 7))}
    s.add(band, skin)
    for i in range(7):
        a = math.radians(-72 + 24 * i)
        out = (math.sin(a) * math.cos(math.radians(28)), math.sin(math.radians(28)), math.cos(a) * math.cos(math.radians(28)))
        p0 = (4.3 * math.sin(a), 65.0, 0.4 + 4.3 * math.cos(a))
        p1 = tuple(p0[k] + 9.5 * out[k] for k in range(3))
        s.capsule(p0, p1, 1.15, skin, 0.45)

    # The raised right arm with its falling sleeve, the fist and the golden microphone.
    shoulder, elbow, wrist = (-8.0, 54.5, 0.4), (-11.0, 67.5, 0.6), (-12.0, 79.5, 0.6)
    s.capsule(shoulder, elbow, 3.6, skin, 3.1)
    s.capsule(elbow, wrist, 2.6, skin, 2.1)
    s.add(blob((-12.0, 81.0, 0.6), (2.6, 2.6, 2.6), 2.2), skin)            # the fist
    s.capsule((-12.0, 82.0, 0.6), (-12.0, 89.0, 0.6), 1.35, dark)          # the handle
    s.capsule((-12.0, 88.5, 0.6), (-12.0, 89.5, 0.6), 2.3, gold)           # the grille's collar
    s.add(blob((-12.0, 92.6, 0.6), (3.4, 3.4, 3.4), 2.0), gold)            # the grille

    # The left arm, bent, holding a tablet against the hip; a music note on the tablet.
    s.capsule((8.0, 54.5, 0.0), (10.6, 44.0, 1.8), 3.0, skin, 2.6)
    s.capsule((10.6, 44.0, 1.8), (8.6, 41.5, 6.0), 2.4, skin, 2.0)
    tablet = {(x, y, z) for x in range(6, 13) for y in range(35, 53) for z in range(4, 7)}
    s.add(tablet, skin)
    note = [(1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (1, 2), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6), (2, 7), (3, 7), (4, 6), (4, 5)]
    s.add({(7 + u, 38 + v, 6) for u, v in note}, dark)

    # The face: two eyes and the open, singing mouth, painted dark on the front-most cells.
    solid = s.solid()
    face = [(-2, 62), (-1, 62), (1, 62), (2, 62), (-1, 59), (0, 59), (1, 59), (0, 58)]
    s.add(front_cells(solid, face), dark)
    return s


# ---------------------------------------------------------------- the star terrace and the pedestal
def terrace(d: Design) -> None:
    """The eleven-pointed star: a hollow terrace, its top on row STAR_H - 1, a low parapet round the rim, a stair up the
    front (into the notch between two points that faces -z)."""
    stone = Mix("travertine", ("concrete", 0.12), seed=5)
    star2d = solids.star(C, C, STAR_R, STAR_IN, points=11, rotation=0.0)    # a tip faces +z, a notch -z
    rim = star2d - solids.erode2d(star2d, 2)
    d.prism(rim, 0, STAR_H - 2, stone)
    d.prism(star2d, STAR_H - 1, STAR_H - 1, stone)
    d.prism(star2d - solids.erode2d(star2d, 1), STAR_H, STAR_H, "granite")  # the parapet
    # The stair: 7 wide, from the ground up to the terrace top, cut into the notch facing -z.
    notch_z = min(z for x, z in star2d if x == C)
    x0, z0 = C - 3, notch_z - 6
    d.stairs(x0, 0, z0, 0, 1, STAR_H, "travertine", width=7, fill_below=True)
    for k in range(STAR_H):
        for x in range(x0, x0 + 7):
            for y in range(k + 1, STAR_H + 1):
                d.clear(x, y, z0 + k)


def pedestal(d: Design) -> None:
    """The pedestal: a stepped foot, a shaft with pilasters, round-arched windows and a portal, a cornice and the
    balcony slab at PED_TOP with a balustrade; hollow, with a spiral stair in a back corner and lanterns."""
    stone = "granite"                                               # a rosy granite, like the original's
    trim = "travertine"
    h = PED // 2
    y0 = STAR_H - 1                                                 # the pedestal's floor is the terrace's top
    d.box(C - h, y0, C - h, C + h, y0 + 5, C + h, stone)            # the foot
    s = h - 2                                                       # the shaft's half width
    d.box(C - s, y0 + 6, C - s, C + s, PED_TOP - 6, C + s, stone)   # the shaft
    for k in (-s, s):                                               # pilasters at the corners
        for j in (-s, s):
            d.box(C + k - (1 if k < 0 else 0), y0 + 6, C + j - (1 if j < 0 else 0),
                  C + k + (1 if k > 0 else 0), PED_TOP - 6, C + j + (1 if j > 0 else 0), trim)
    d.box(C - s - 1, PED_TOP - 5, C - s - 1, C + s + 1, PED_TOP - 3, C + s + 1, stone)   # the cornice
    d.box(C - h, PED_TOP - 2, C - h, C + h, PED_TOP, C + h, stone)  # the balcony (overhanging)
    d.box(C - h + 1, PED_TOP - 2, C - h + 1, C + h - 1, PED_TOP - 2, C + h - 1, trim)
    # Hollow it: the inside is one tall room from the foot to below the balcony slab.
    for y in range(y0 + 1, PED_TOP):
        for x in range(C - s + 2, C + s - 1):
            for z in range(C - s + 2, C + s - 1):
                d.clear(x, y, z)
    # The balustrade round the balcony.
    for x in range(C - h, C + h + 1):
        for z in range(C - h, C + h + 1):
            if x in (C - h, C + h) or z in (C - h, C + h):
                d.put(x, PED_TOP + 1, z, "travertine")
    # The portal on the front (-z) side: a round arch 3 wide and 5 high through the foot.
    d.arch("z", C - 1, y0 + 1, C - h, C - s + 2, 3, 5, kind="round")
    # Round-arched windows, three to a side, glazed.
    for row in (y0 + 12, y0 + 22):
        for u in (-8, -1, 6):
            d.arch("z", C + u, row, C - s, C - s + 1, 3, 6, kind="round", m=Mat("glass"))
            d.arch("z", C + u, row, C + s - 1, C + s, 3, 6, kind="round", m=Mat("glass"))
            d.arch("x", C + u, row, C - s, C - s + 1, 3, 6, kind="round", m=Mat("glass"))
            d.arch("x", C + u, row, C + s - 1, C + s, 3, 6, kind="round", m=Mat("glass"))
    # The spiral stair in the back-left corner, up through the balcony slab.
    sx, sz = C - 11, C + 11
    d.spiral_stair(sx, sz, y0 + 1, PED_TOP, 1.5, 4.6, Mat("travertine"), steps_per_turn=16,
                   newel=Mat("granite"))
    for x, z in solids.disc(sx, sz, 1.5):                          # the newel ends in the balcony slab
        for y in range(PED_TOP + 1, PED_TOP + 4):
            d.clear(x, y, z)
    # Lanterns on the inner walls, every eight rows.
    for y in range(y0 + 4, PED_TOP - 2, 8):
        for x, z in ((C + 4, C - s + 2), (C + 4, C + s - 2), (C - s + 2, C), (C + s - 2, C)):
            d.put(x, y, z, "lantern")


def build() -> Design:
    d = Design("singer_statue", "Singer Statue", nameKey="landmark.singer_statue.name", minTerrainGeneration=39,
               ruin="never", protection="protected", requires=["atmosphere"], planetTypes=["sounder_world"], weight=1)
    terrace(d)
    pedestal(d)
    sculpture = figure()
    placed = Sculpture()
    base = PED_TOP + 1
    for cells, m in sculpture.parts:
        placed.add({(C + x, base + y, C - z) for x, y, z in cells}, m)
    placed.write(d, wall=2)
    # The choir: on the balcony in front of the figure, heard from far across the meadows.
    d.sound(C, PED_TOP + 1, C - (PED // 2) + 2, "amb_singer_choir", radius=120, volume=0.7)
    return d


if __name__ == "__main__":
    build().write()
