"""The singer statue: a giant light-blue Sounder in a folded robe holding a golden MICROPHONE high, as if it sang to the sky.

Liberty-style, made for the Sounders' world and in their own shape (#3660): the Sounders raised it, so the figure is one
of them (Shared SounderRules, client CreatureBuilder.Sounder.cs) — a thin body in a long folded robe that flares to the
hem, the woven sash round the waist with its long end hanging at the side, FOUR thin arms in two pairs, the chest slot
that holds a Song Disc, a small head with NO eyes, the open singing mouth and the long hare ears standing up where the
original's crown of rays would be. The upper right arm raises the gilded microphone (it glows a little at night, like a
torch would), the upper left holds a tablet with a music note up beside the head, and the lower pair is spread wide
and open, welcoming — so all four arms read from the ground. Everything is the Sounders' light blue: the skin and the robe painted on pale travertine so the dye
reads bright, the sash a deeper blue. It stands on a light-blue stone pedestal with an arched portal, windows and a
spiral stair inside up to a balcony at the figure's feet, and the pedestal stands on an eleven-pointed star terrace with
a stair up its front. A sound marker on the balcony plays the Sounders' humming choir, louder the closer you come.

Intact (`ruin: never`) and protected; a fixed landmark of Soaniri only (`planetTypes: sounder_world`). The box stays
100 x 141 x 100, so a world that stamped the old statue keeps a matching protection box and choir marker.
Built in figure space (x right, y up, z = the way it looks), then placed facing -Z (the preview's front view): the
raised microphone arm appears on the viewer's left, like the original's torch.
"""
from __future__ import annotations

import math

import solids
from _figures import blob, front_cells, inverse_apply, mul, raster, rot_x, rot_z
from geom import Design, Mat, Mix, Sculpture

# The Sounders' colours (SounderRules.SkinTones / SashTones), dyes on pale travertine.
SKIN = 0x8FC6EA             # the sky-blue skin
SKIN_LIGHT = 0x9CD3F0       # ice-blue streaks so the painted skin is not one flat colour
SKIN_DARK = 0x6F9AB6        # hands, chin and the slot's rim: the skin at 0.78 (the builder's SkinDark)
ROBE = 0xA9D6F2             # the robe, a paler blue than the skin
ROBE_LIGHT = 0xBFE3F7       # streaks where the rain washed it
ROBE_FOLD = 0x7FB2D6        # the fold valleys
SASH = 0x2F6FA8             # the woven sash, deep blue (SashTones[0])
EAR_INNER = 0xC4ECE0        # the ears' pale mint inner panels
EAR_GLOW = 0x6FDDB0         # their soft glow (the Soundi statue's mint glow)
TABLET = 0xD6EEFA           # the tablet, a frost-white blue
GOLD_GLOW = 0xFFD27A

# The pedestal and the terrace, light blue as well.
PED_STONE = 0x9CC9E8        # the pedestal's walls
PED_TRIM = 0xCBE7F8         # pilasters, cornice trim, balustrade, stairs
TERRACE = 0xB7DCF2          # the star terrace
TERRACE_DARK = 0x8DBFE0     # its darker flecks and the parapet

STAR_R, STAR_IN, STAR_H = 50, 37, 8       # the star terrace: outer and inner radius, rows 0..STAR_H-1
PED = 38                                  # the pedestal's width at its foot
PED_TOP = 44                              # the balcony slab's row (the figure stands on it)
SIZE = 2 * STAR_R + 1                     # the footprint (x and z)
C = STAR_R                                # the centre column (x and z)
Cell = tuple[int, int, int]

# The Sounder's body (SounderRules shares), H = soles to crown in blocks; the robe hides the long legs.
H = 74.0
LEG = 0.48 * H                            # the hip height (35.5)
TORSO = 0.30 * H                          # hips to shoulders (22.2)
SHOULDER_Y = LEG + 0.93 * TORSO           # the upper arm pair (56.1)
LOWER_ARM_Y = LEG + 0.62 * TORSO          # the lower arm pair (49.3)
HEAD_H = 0.14 * H                         # the small head (10.4)
HEAD = (0.0, LEG + TORSO + 0.08 * H + HEAD_H * 0.5, 0.4)   # the head's centre (68.8)
EAR = 0.24 * H                            # each hare ear's length (17.8)
WAIST_Y = 41.5                            # where the robe ends and the sash runs round


# ---------------------------------------------------------------- the figure (figure space, y = 0 at the robe's hem)
def _robe_radius(y: float) -> tuple[float, float]:
    """The robe's half widths (x, z) at height y: a wide flared hem narrowing to the Sounder's thin waist."""
    t = max(0.0, min(1.0, y / WAIST_Y)) ** 0.8
    rx = 10.4 - 4.6 * t + (0.5 if y < 2 else 0.0)
    rz = 8.0 - 3.4 * t + (0.4 if y < 2 else 0.0)
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

    cells = raster(inside, (-12, 0, -10), (12, int(WAIST_Y) + 1, 10))
    valleys = {p for p in cells if _fold(*p) < -0.55}
    return cells, valleys


def ear(side: int) -> tuple[set[Cell], set[Cell]]:
    """One long hare ear on the crown (the builder's base + bent tip as one blade): its cells and its pale inner panel on
    the front face. Up, a little back and out; the upper half curls back a little. `side` -1 = left, +1 = right."""
    root = (side * 2.4, HEAD[1] + HEAD_H * 0.42, HEAD[2] - 0.6)
    rot = mul(rot_x(-10.0), rot_z(-side * 12.0))
    blade, inner = set(), set()
    reach = EAR + 3
    for x in range(math.floor(root[0] - reach), math.ceil(root[0] + reach) + 1):
        for y in range(math.floor(root[1] - 2), math.ceil(root[1] + reach) + 1):
            for z in range(math.floor(root[2] - reach), math.ceil(root[2] + reach) + 1):
                a, v, t = inverse_apply(rot, (x - root[0], y - root[1], z - root[2]))
                if v < -2 or v > EAR:
                    continue
                bend = -0.022 * max(0.0, v - 0.5 * EAR) ** 2      # the tip curls back (-t)
                taper = 1.0 if v < 0.82 * EAR else max(0.0, (EAR - v) / (0.18 * EAR)) ** 0.6
                hw = 1.9 * (1 - 0.3 * max(v, 0) / EAR) * taper + 0.4
                if abs(a) > hw or abs(t - bend) > 1.05:
                    continue
                blade.add((x, y, z))
                if t - bend > 0.2 and abs(a) <= 0.6 * hw and 1.5 <= v <= 0.78 * EAR:
                    inner.add((x, y, z))
    return blade, inner


def _arm(s: Sculpture, shoulder, elbow, wrist, r: float, skin) -> None:
    """One thin Sounder arm: upper arm and forearm (a little thinner)."""
    s.capsule(shoulder, elbow, r, skin, r * 0.9)
    s.capsule(elbow, wrist, r * 0.85, skin, r * 0.75)


def arms(s: Sculpture, skin, dark) -> None:
    """The four arms (the builder's two pairs): the upper right raises the microphone, the upper left holds the tablet
    up beside the head, the lower pair is spread wide and open to the sides — welcoming. Each side shows two arms."""
    gold = Mat("gold_block", glow=GOLD_GLOW)
    # Upper right (at -x): straight up with the golden microphone.
    _arm(s, (-6.3, SHOULDER_Y, 0.0), (-8.8, SHOULDER_Y + 12.6, 0.5), (-10.0, SHOULDER_Y + 24.4, 0.6), 1.9, skin)
    s.add(blob((-10.0, 81.8, 0.6), (2.1, 2.3, 2.1), 2.2), dark)                # the hand round the handle
    s.capsule((-10.0, 83.0, 0.6), (-10.0, 88.6, 0.6), 1.3, Mat("carved_obsidian"))  # the handle
    s.capsule((-10.0, 88.4, 0.6), (-10.0, 89.4, 0.6), 2.2, gold)                # the grille's collar
    s.add(blob((-10.0, 92.4, 0.6), (3.3, 3.3, 3.3), 2.0), gold)                 # the grille
    # Upper left (at +x): out to the side and up, the hand holding the tablet up beside the head — the song it sings.
    _arm(s, (6.3, SHOULDER_Y, 0.0), (14.2, SHOULDER_Y + 3.4, 1.0), (18.6, SHOULDER_Y + 10.6, 2.6), 1.9, skin)
    s.add(blob((19.2, SHOULDER_Y + 12.2, 3.0), (1.8, 2.0, 1.8), 2.2), dark)    # the hand on the tablet's foot
    tablet = {(x, y, z) for x in range(16, 24) for y in range(round(SHOULDER_Y) + 12, round(SHOULDER_Y) + 29)
              for z in range(2, 5)}
    s.add(tablet, Mat("travertine", tint=TABLET))
    note = [(1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (1, 2), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6), (2, 7), (3, 7), (4, 6), (4, 5)]
    s.add({(18 + u, round(SHOULDER_Y) + 16 + v, 4) for u, v in note}, Mat("travertine", tint=SASH))
    # The lower pair: from just under the upper pair, spread wide and a little down to the sides, open hands — welcoming.
    for side in (-1, 1):
        shoulder = (side * 5.3, LOWER_ARM_Y - 1.5, 0.8)
        elbow = (side * 14.2, LOWER_ARM_Y - 7.0, 2.0)
        wrist = (side * 22.0, LOWER_ARM_Y - 10.6, 3.6)
        _arm(s, shoulder, elbow, wrist, 1.7, skin)
        s.add(blob((side * 24.0, LOWER_ARM_Y - 11.4, 4.0), (2.2, 1.0, 2.0), 2.2), dark)  # the open hand, palm forward


def body(s: Sculpture, skin) -> None:
    """The thin torso over the robe, the shoulders, the neck and the woven sash with its long hanging end."""
    s.add(blob((0, WAIST_Y + 2.5, 0.0), (4.7, 4.2, 3.5), 2.2), skin)          # the narrow waist
    s.add(blob((0, LEG + 0.72 * TORSO, 0.2), (5.8, 6.4, 3.9), 2.4), skin)      # the chest
    s.capsule((-6.0, SHOULDER_Y + 0.4, 0), (6.0, SHOULDER_Y + 0.4, 0), 2.5, skin)  # the shoulders
    s.capsule((0, SHOULDER_Y, 0), (0, HEAD[1] - HEAD_H * 0.45, 0.3), 2.0, skin)    # the thin neck
    sash = Mat("travertine", tint=SASH)
    s.add(blob((0, WAIST_Y, 0), (6.3, 1.9, 4.9), 2.6), sash)                  # the woven sash round the waist
    s.capsule((-3.4, WAIST_Y - 1.0, 4.4), (-4.4, WAIST_Y - 14.0, 6.0), 1.15, sash, 0.95)  # its long end, hanging


def head(s: Sculpture, skin, dark) -> None:
    """The small eyeless head: skull, crown, the chin dropped a little (singing), the hare ears with glowing insides."""
    hx, hy, hz = HEAD
    s.add(blob(HEAD, (4.2, 4.5, 4.5), 2.2), skin)                              # the skull
    s.add(blob((hx, hy + HEAD_H * 0.4, hz - 0.3), (3.4, 1.4, 3.6), 2.2), skin)  # the crown
    s.add(blob((hx, hy - HEAD_H * 0.36, hz + 1.8), (2.6, 1.3, 2.4), 2.2), dark)  # the chin
    for side in (-1, 1):
        blade, inner = ear(side)
        s.add(blade, skin)
        s.add(inner, Mat("travertine", tint=EAR_INNER, glow=EAR_GLOW))


def figure() -> Sculpture:
    """The Sounder, standing at y = 0 (the hem), looking along +z; the raised arm at -x."""
    skin = Mix(Mat("travertine", tint=SKIN), (Mat("travertine", tint=SKIN_LIGHT), 0.18), seed=11)
    dark = Mat("travertine", tint=SKIN_DARK)
    s = Sculpture()

    cells, valleys = robe()
    s.add(cells, Mix(Mat("travertine", tint=ROBE), (Mat("travertine", tint=ROBE_LIGHT), 0.16), seed=17))
    s.add(valleys, Mat("travertine", tint=ROBE_FOLD))
    body(s, skin)
    head(s, skin, dark)
    arms(s, skin, dark)

    # Painted on the front-most cells: the chest slot (a raised dark rim round the obsidian slot that holds a Song Disc)
    # and the open singing mouth. No eyes — a Sounder has none.
    solid = s.solid()
    slot_y = round(LEG + 0.75 * TORSO)
    rim = [(x, y) for x in range(-2, 3) for y in range(slot_y - 3, slot_y + 3)]      # the builder's 0.46 w x 0.28 torso
    slot = [(x, y) for x in range(-1, 2) for y in range(slot_y - 2, slot_y + 2)]     # and 0.34 w x 0.2 torso
    s.add(front_cells(solid, rim), dark)
    s.add(front_cells(solid, slot), Mat("carved_obsidian"))
    mouth_y = round(HEAD[1] - HEAD_H * 0.2)
    mouth = [(x, y) for x in range(-2, 3) for y in (mouth_y - 1, mouth_y)]
    s.add(front_cells(solid, mouth), Mat("carved_obsidian"))
    return s


# ---------------------------------------------------------------- the star terrace and the pedestal
def terrace(d: Design) -> None:
    """The eleven-pointed star: a hollow terrace, its top on row STAR_H - 1, a low parapet round the rim, a stair up the
    front (into the notch between two points that faces -z)."""
    stone = Mix(Mat("travertine", tint=TERRACE), (Mat("travertine", tint=TERRACE_DARK), 0.12), seed=5)
    star2d = solids.star(C, C, STAR_R, STAR_IN, points=11, rotation=0.0)    # a tip faces +z, a notch -z
    rim = star2d - solids.erode2d(star2d, 2)
    d.prism(rim, 0, STAR_H - 2, stone)
    d.prism(star2d, STAR_H - 1, STAR_H - 1, stone)
    d.prism(star2d - solids.erode2d(star2d, 1), STAR_H, STAR_H, Mat("travertine", tint=TERRACE_DARK))  # the parapet
    # The stair: 7 wide, from the ground up to the terrace top, cut into the notch facing -z.
    notch_z = min(z for x, z in star2d if x == C)
    x0, z0 = C - 3, notch_z - 6
    d.stairs(x0, 0, z0, 0, 1, STAR_H, Mat("travertine", tint=PED_TRIM), width=7, fill_below=True)
    for k in range(STAR_H):
        for x in range(x0, x0 + 7):
            for y in range(k + 1, STAR_H + 1):
                d.clear(x, y, z0 + k)


def pedestal(d: Design) -> None:
    """The pedestal: a stepped foot, a shaft with pilasters, round-arched windows and a portal, a cornice and the
    balcony slab at PED_TOP with a balustrade; hollow, with a spiral stair in a back corner and lanterns."""
    stone = Mat("travertine", tint=PED_STONE)                       # a light-blue stone, the Sounders' colour
    trim = Mat("travertine", tint=PED_TRIM)
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
                d.put(x, PED_TOP + 1, z, trim)
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
    d.spiral_stair(sx, sz, y0 + 1, PED_TOP, 1.5, 4.6, trim, steps_per_turn=16, newel=stone)
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
