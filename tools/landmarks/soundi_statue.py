"""The Soundi statue: the Sounders' little friend as a giant painted stone figure on a crystal-lit obsidian pedestal.

The body follows the game's own Soundi (client CreatureBuilder.Soundi.cs, Shared SoundiRules) part by part, scaled to
one creature unit U = 16 blocks: four short chubby legs with round mint paws, a round fluffy barrel of a body with
tufts on the back and the flanks and a mint chest patch, the big round head with fluffy cheeks and a crown tuft, the
mint muzzle with a little pink nose and the wide violet smile, NO eyes, the huge pointed ears (taller than the head,
leaning a little out and back) with glowing mint insides, and the bushy tail carried up with a mint tip. Colours are
the creature's own (fur #8FCBF2, mint #BFF2CF), painted on pale travertine so the dye reads bright. The figure is a
hollow shell; it stands intact (`ruin: never`) and protected on every world with air.

Built in creature space (x right, y up, z = the way it looks), then placed with the face toward -Z (the preview's
front view) on the pedestal.
"""
from __future__ import annotations

import solids
from _figures import blob, box_cells, front_cells, inverse_apply, mul, rot_x, rot_z
from geom import Design, Mat, Mix, Sculpture

U = 16                      # one creature unit in blocks (SoundiRules' proportions are in units)
FUR = 0x8FCBF2              # SoundiRules.Fur
FUR_SHADE = 0x7EB3D5        # the fur at 0.88 (the builder's FurShade): the tufts and cheeks
FUR_LIGHT = 0x9FD6F7        # a lighter stroke so the painted fur is not one flat colour
MINT = 0xBFF2CF             # SoundiRules.Mint
MINT_GLOW = 0x6FDDB0        # the inner ears' soft glow
SMILE = 0x3B295C            # the builder's smile (0.23, 0.16, 0.36)
NOSE = 0xE68FB8             # the builder's nose (0.9, 0.56, 0.72)

PED_W, PED_L, PED_H = 43, 55, 12   # the pedestal (x, z, rows); the figure stands on its top row
STAIR_W = 7                        # the staircase up the back of the pedestal
Cell = tuple[int, int, int]


# ---------------------------------------------------------------- the figure (creature space)
def ear(side: int) -> tuple[set[Cell], set[Cell]]:
    """One huge pointed ear (the builder's three tapering tiers as one smooth blade): its cells and its mint inner panel
    (the front face). `side` -1 = left, +1 = right."""
    head = (0.0, 26.0, 12.3)
    e = 1.2 * U
    pivot = (head[0] + side * 0.3 * e, head[1] + 0.38 * e, head[2] - 0.04 * e)
    rot = mul(rot_x(-6.0), rot_z(-side * 14.0))          # Euler(-6, 0, -sx * 14): out and a little back
    tall = 1.5 * U
    half0 = 0.25 * e                                        # the bottom tier is 0.5 e wide
    blade, inner = set(), set()
    reach = tall + 2
    for x, y, z in box_cells((pivot[0] - reach, pivot[1] - 3, pivot[2] - reach), (pivot[0] + reach, pivot[1] + reach, pivot[2] + reach)):
        a, v, t = inverse_apply(rot, (x - pivot[0], y - pivot[1], z - pivot[2]))
        if v < -3 or v > tall:
            continue
        hw = half0 * max(0.0, 1 - max(v, 0) / tall) ** 0.8 + 0.45
        if abs(a) > hw or abs(t) > 1.35:
            continue
        blade.add((x, y, z))
        if t > 0.35 and abs(a) <= 0.62 * hw and 1.0 <= v <= 0.72 * tall:
            inner.add((x, y, z))
    return blade, inner


def figure() -> Sculpture:
    """The Soundi, standing on its four paws at y = 0, looking along +z."""
    fur = Mix(Mat("travertine", tint=FUR), (Mat("travertine", tint=FUR_LIGHT), 0.25), seed=7)
    shade = Mat("travertine", tint=FUR_SHADE)
    mint = Mat("travertine", tint=MINT)
    s = Sculpture()
    leg_len = 0.5 * U
    body_y = leg_len + 0.9 * 0.45 * U                       # SoundiRules.LegLength + 45 % of the body height
    blen, bhigh, bwide = 1.15 * U, 0.9 * U, 1.0 * U

    # Four short chubby legs (hip -> foot) with round mint paws.
    for z in (0.36 * U, -0.36 * U):
        for x in (-0.3 * U, 0.3 * U):
            s.capsule((x, leg_len + 1, z), (x, 2.0, z), 2.7, fur, 2.4)
            s.add(blob((x, 1.3, z + 1.0), (3.2, 1.7, 3.6), 2.2), mint)

    # The round fluffy barrel, a rounding over it, tufts on the back and the flanks.
    torso = blob((0, body_y, 0), (bwide / 2, bhigh / 2, blen / 2), 2.6)
    s.add(torso, fur)
    s.add(blob((0, body_y, 0), (bwide * 0.54, bhigh * 0.4, blen * 0.43), 2.6), fur)
    for i in range(3):
        z = (0.35 - 0.35 * i) * blen
        lift = 0.6 if i == 1 else 0.0
        s.add(blob((0, body_y + bhigh * 0.5 + lift, z), (bwide * 0.28, 1.9, blen * 0.13), 2.2), shade)
        s.add(blob((0, body_y - bhigh * 0.28, z), (bwide * 0.56, 1.5, blen * 0.12), 2.2), shade)
    # The mint chest patch, painted on the front of the barrel.
    chest = {p for p in torso if p[2] >= blen / 2 - 2.5
             and ((p[0] / (bwide * 0.31)) ** 2 + ((p[1] - (body_y - 0.08 * bhigh)) / (bhigh * 0.31)) ** 2) <= 1}
    s.add(chest, mint)

    # The tail, carried up behind in three links that grow fluffier toward the mint tip.
    root = (0.0, body_y + 0.28 * bhigh, -blen / 2 + 1)
    p1, p2, tip = (0.0, root[1] + 3.5, root[2] - 7.0), (0.0, root[1] + 10.5, root[2] - 11.0), (0.0, root[1] + 18.5, root[2] - 10.5)
    s.capsule(root, p1, 2.6, fur, 3.6)
    s.capsule(p1, p2, 3.6, fur, 4.4)
    s.capsule(p2, tip, 4.4, fur, 3.6)
    s.add({p for p in solids.capsule(p2, tip, 4.4, 3.6) | solids.sphere(*tip, 3.6) if p[1] >= tip[1] - 3}, mint)

    # The big round head: skull, its rounding, fluffy cheeks, a crown tuft.
    head = (0.0, body_y + 0.72 * U, 0.77 * U)
    e = 1.2 * U
    skull = blob(head, (e / 2, e * 0.44, e * 0.41), 2.4)
    s.add(skull, fur)
    s.add(blob(head, (e * 0.53, e * 0.36, e * 0.35), 2.4), fur)
    for side in (-1, 1):
        s.add(blob((head[0] + side * e * 0.47, head[1] - e * 0.16, head[2] + e * 0.08), (e * 0.13, e * 0.19, e * 0.22), 2.2), shade)
    s.add(blob((head[0], head[1] + e * 0.47, head[2] + e * 0.06), (e * 0.16, e * 0.07, e * 0.12), 2.2), shade)

    # The face: the mint muzzle, the little pink nose, the mint chin. No eyes.
    front = head[2] + e * 0.41
    muzzle_c = (0.0, head[1] - e * 0.17, front - 0.6)
    muzzle = blob(muzzle_c, (e * 0.28, e * 0.18, 1.9), 2.2)
    s.add(muzzle, mint)
    s.add(blob((0.0, head[1] - e * 0.05, front + 1.2), (1.5, 1.0, 1.0), 2.0), Mat("travertine", tint=NOSE))
    s.add(blob((0.0, head[1] - e * 0.37, front - 1.0), (e * 0.2, 1.1, 1.6), 2.2), mint)

    # The huge ears with their glowing mint insides.
    for side in (-1, 1):
        blade, inner = ear(side)
        s.add(blade, fur)
        s.add(inner, Mat("travertine", tint=MINT, glow=MINT_GLOW))

    # The wide gentle smile: painted on the muzzle's front-most cells along a flat middle and two corners turned up.
    solid = s.solid()
    smile_y = head[1] - e * 0.2
    curve = [(x, smile_y) for x in range(-2, 3)]
    for side in (-1, 1):
        for k in range(1, 4):
            curve.append((side * (2 + k), smile_y + 0.62 * k))
    smile = front_cells(solid, [(x, round(y)) for x, y in curve])
    s.add(smile, Mat("carved_obsidian", tint=SMILE))
    return s


# ---------------------------------------------------------------- the pedestal
def pedestal(d: Design) -> None:
    """A hollow carved-obsidian plinth with a stepped foot and a cornice, resonance crystals glowing in its corners and
    along its cornice, crystal clusters at the foot's corners and the name in crystal letters on the front."""
    stone = Mix("carved_obsidian", ("obsidian", 0.12), seed=3)
    x1, z1 = PED_W - 1, PED_L - 1
    d.box(0, 0, 0, x1, 1, z1, stone)                                   # the foot (two rows, the whole footprint)
    d.box(2, 2, 2, x1 - 2, PED_H - 2, z1 - 2, stone)                    # the body
    d.box(3, 2, 3, x1 - 3, PED_H - 3, z1 - 3, "carved_obsidian")        # (filled, then hollowed below)
    for y in range(2, PED_H - 2):
        for x in range(4, x1 - 3):
            for z in range(4, z1 - 3):
                d.clear(x, y, z)
    d.box(1, PED_H - 1, 1, x1 - 1, PED_H - 1, z1 - 1, stone)           # the cornice: the figure's floor
    crystal = Mat("resonance_crystal")
    for x, z in ((2, 2), (x1 - 2, 2), (2, z1 - 2), (x1 - 2, z1 - 2)):  # glowing crystal corners
        for y in range(2, PED_H - 1):
            d.put(x, y, z, crystal)
    for x in range(1, x1, 3):                                          # crystal studs along the cornice
        d.put(x, PED_H - 1, 1, crystal)
        d.put(x, PED_H - 1, z1 - 1, crystal)
    for z in range(1, z1, 3):
        d.put(1, PED_H - 1, z, crystal)
        d.put(x1 - 1, PED_H - 1, z, crystal)
    for x, z, dx, dz in ((0, 0, 1, 1), (x1, 0, -1, 1), (0, z1, 1, -1), (x1, z1, -1, -1)):  # clusters on the foot
        for (ox, oz), h in (((0, 0), 4), ((dx, 0), 2), ((0, dz), 3), ((dx, dz), 1)):
            for y in range(2, 2 + h):
                d.put(x + ox, y, z + oz, crystal)
    word = "SOUNDI"
    w, _ = Design.text_size(word)
    d.text(word, (PED_W - w) // 2, 3, 2, crystal, dir="+x")             # on the front face (it looks toward -z)
    # A staircase up the back, so a visitor can stand between the Soundi's paws.
    d.stairs((PED_W - STAIR_W) // 2, 0, z1 + PED_H, 0, -1, PED_H, stone, width=STAIR_W, fill_below=True)
    for x in range((PED_W - STAIR_W) // 2 - 1, (PED_W + STAIR_W) // 2 + 1):  # the cornice opens onto the stair
        d.put(x, PED_H - 1, z1, stone)


def build() -> Design:
    d = Design("soundi_statue", "Soundi Statue", nameKey="landmark.soundi_statue.name", minTerrainGeneration=39,
               ruin="never", protection="protected", requires=["atmosphere"], weight=1)
    pedestal(d)
    # Place the figure: creature x -> template x around the middle, creature z (the way it looks) -> template -z.
    cx, cz = PED_W // 2, PED_L // 2 + 1
    base = PED_H
    sculpture = figure()
    placed = Sculpture()
    for cells, m in sculpture.parts:
        placed.add({(cx + x, base + y, cz - z) for x, y, z in cells}, m)
    placed.cuts = {(cx + x, base + y, cz - z) for x, y, z in sculpture.cuts}
    placed.write(d, wall=2)
    return d


if __name__ == "__main__":
    build().write()
