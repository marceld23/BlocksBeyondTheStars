"""The suspension bridge: a Golden-Gate-style bridge in international orange, seated across a river, a lake's narrows or a
valley (seat "span": the middle third of its length over the gap, both ends on the banks at the deck row).

256 long (x) and 26 wide (z): two Art-Deco pylons 112 high with stepped legs and portal struts, the deck on row 40 (a road
between two walkways, carried by stiffening trusses on rows 36..39), two main cables with the classic sag and vertical
hangers every four blocks, side spans down to the anchorage blocks on the banks, and approach piers under the side
spans. Inside one leg of each pylon a ladder climbs to the top, where a chest waits beside the red warning lights.
The pylons stand on concrete piers that reach row 0; the stamp carries them on down to the river bed.

The ground row (35, the banks' level) lies just under the trusses, so the deck and its trusses hang in the air over the
gap — nothing of the span is anchored in the ground, and the lattice breaks of the ruin pass (revision 2) let a section of
the main span drop into the river. On the banks an embankment carries the road's ends, with a short stair down to the
ground at each end.
"""
import shapecode as sc
import solids
from geom import CLEAR, Design, Mat, Mix

LENGTH, WIDTH = 256, 26
DECK = 40                 # the deck row
GROUND = DECK - 5         # the ground row (the banks' level): just under the trusses' lower chord (DECK - 4)
RAMP = 4                  # the stair at each end: RAMP steps from the ground up to the deck
EMBANK = 14               # the embankment under the road's ends (x < EMBANK and mirrored), up to the anchorage
TOP = 110                 # the pylons' cap row
PYLONS = (64, 191)        # the pylons' centre columns (x)
CABLE_Z = (2, WIDTH - 3)  # the two cable planes (the legs' middles)
ANCHOR = (14, 27)         # the anchorage block on the left bank (x range); mirrored on the right

ORANGE = Mix(Mat("iron_block", tint=0xC0362C), (Mat("iron_block", tint=0xB3402F), 0.25), seed=31)
ORANGE_FLAT = Mat("iron_block", tint=0xC0362C)
CONCRETE = Mat("concrete")
ROAD = Mix(Mat("concrete", tint=0x6E7173), (Mat("concrete", tint=0x64686A), 0.3), seed=32)


def box(d: Design, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m) -> set:
    """A box with its corners in any order."""
    return d.fill(solids.box(x0, y0, z0, x1, y1, z1), m)


def mirror_x(x: int) -> int:
    return LENGTH - 1 - x


# --- the deck ----------------------------------------------------------------------------------------------------------
def deck(d: Design) -> None:
    """The road (two lanes and a dashed centre line), the walkways, railings, lamp posts and the stiffening trusses."""
    box(d, 0, DECK, 3, LENGTH - 1, DECK, WIDTH - 4, ROAD)
    for z in (3, 4, WIDTH - 5, WIDTH - 4):                                         # walkways
        box(d, 0, DECK, z, LENGTH - 1, DECK, z, Mat("steel_floor"))
    d.fill([(x, DECK, WIDTH // 2 - 1) for x in range(LENGTH) if x % 6 < 3]
           + [(x, DECK, WIDTH // 2) for x in range(LENGTH) if x % 6 < 3], Mat("concrete", tint=0xEDE6D2))
    for z in CABLE_Z:                                                              # the fascia, the railing
        box(d, 0, DECK, z, LENGTH - 1, DECK, z, ORANGE_FLAT)
        d.fill([(x, DECK + 1, z) for x in range(LENGTH)], ORANGE_FLAT.shaped(sc.pack(sc.FENCE)), where="empty")
        truss(d, z)
    for x in range(2, LENGTH, 8):                                                  # floor beams under the deck
        d.fill([(x, DECK - 1, z) for z in range(CABLE_Z[0], CABLE_Z[1] + 1)], ORANGE_FLAT.shaped(sc.beam("z")))
    for x in range(12, LENGTH - 8, 24):                                            # lamp posts on the walkways
        for z in (3, WIDTH - 4):
            d.fill([(x, y, z) for y in range(DECK + 1, DECK + 5)], ORANGE_FLAT.shaped(sc.post()))
            d.put(x, DECK + 5, z, "lantern")


def truss(d: Design, z: int) -> None:
    """A stiffening truss under the deck's edge: chords on rows DECK-4 and DECK-1, posts every four, zigzag diagonals."""
    lo, hi = DECK - 4, DECK - 1
    d.fill([(x, lo, z) for x in range(LENGTH)], ORANGE_FLAT.shaped(sc.beam("x")))
    d.fill([(x, hi, z) for x in range(LENGTH)], ORANGE_FLAT)
    for x in range(0, LENGTH - 1, 4):
        d.fill([(x, y, z) for y in range(lo, hi)], ORANGE_FLAT.shaped(sc.post()))
        a, b = (x, x + 4) if (x // 4) % 2 == 0 else (x + 4, x)
        if max(a, b) < LENGTH:
            d.line((a, lo, z), (b, hi, z), ORANGE_FLAT)


# --- the pylons --------------------------------------------------------------------------------------------------------
def leg_section(y: int) -> tuple[int, int]:
    """A leg's half-length along x and its width along z at row y: it steps in at the struts like the original."""
    if y < 72:
        return 3, 5
    if y < 92:
        return 2, 5
    return 2, 4


STRUTS = ((30, 33), (66, 69), (84, 87), (99, 101), (TOP - 3, TOP))   # rows of the portal struts (the first under the deck)


def pylon(d: Design, px: int, ladder: bool) -> None:
    """One pylon: a concrete pier to row 0, two stepped legs with recessed panels, portal struts, a cap with saddles."""
    box(d, px - 6, 0, 0, px + 6, 12, WIDTH - 1, CONCRETE)                         # the pier (in the water)
    box(d, px - 5, 13, 1, px + 5, 13, WIDTH - 2, Mat("concrete", shape=sc.pack(sc.SLAB)))
    for side, (z0, z1) in enumerate(((0, 4), (WIDTH - 5, WIDTH - 1))):
        for y in range(14, TOP + 1):
            hx, wz = leg_section(y)
            za, zb = (z0, z0 + wz - 1) if side == 0 else (z1 - wz + 1, z1)
            box(d, px - hx, y, za, px + hx, y, zb, ORANGE)
            outer = za if side == 0 else zb
            strut = any(a <= y <= b for a, b in STRUTS) or y in (DECK, DECK + 1)
            if not strut and hx >= 2:                                             # the recessed panel on the outer face
                d.fill([(x, y, outer) for x in range(px - hx + 1, px + hx)], CLEAR)
                d.fill([(x, y, outer + (1 if side == 0 else -1)) for x in range(px - hx + 1, px + hx)], ORANGE_FLAT)
        if ladder and side == 0:
            leg_shaft(d, px, z0)
    for a, b in STRUTS:                                                           # the portal struts between the legs
        hx = leg_section(a)[0]
        box(d, px - hx + 1, a, 5, px + hx - 1, b, WIDTH - 6, ORANGE)
        if b - a >= 3:
            for x in (px - hx + 1, px + hx - 1):
                box(d, x, a + 1, 7, x, b - 1, WIDTH - 8, CLEAR)
                box(d, x + (1 if x < px else -1), a + 1, 7, x + (1 if x < px else -1), b - 1, WIDTH - 8, ORANGE_FLAT)
    for z in CABLE_Z:                                                             # the saddles and the warning lights
        box(d, px - 1, TOP + 1, z, px + 1, TOP + 1, z, ORANGE_FLAT)
        d.put(px, TOP + 2, z, ORANGE_FLAT.shaped(sc.pack(sc.DOME)))
    for z in (0, WIDTH - 1):
        d.put(px - 2, TOP + 1, z, Mat("light_red", glow=0xFF2A1A))


def leg_shaft(d: Design, px: int, z0: int) -> None:
    """A ladder shaft (3 x 2) in the leg from a doorway on the road side up to a hatch in the cap; a lantern every 16
    rows and a chest on top beside the saddle."""
    for y in range(DECK + 1, TOP):
        box(d, px - 1, y, z0 + 2, px + 1, y, z0 + 3, CLEAR)
        d.put(px - 1, y, z0 + 3, "ladder")
        if (y - DECK) % 16 == 8:
            d.put(px + 1, y, z0 + 2, "lantern")
    d.put(px - 1, TOP, z0 + 3, "ladder")                                         # the hatch
    box(d, px, DECK + 1, z0 + 4, px + 1, DECK + 3, z0 + 4, CLEAR)                # the doorway from the road
    d.door(px, DECK + 1, z0 + 4, "hinge")
    d.chest(px + 1, TOP + 1, z0 + 1, table="ruin_rich", chance=0.8)


# --- the cables and the anchorages -------------------------------------------------------------------------------------
def cables(d: Design) -> None:
    """The two main cables (the main span sags to three rows above the deck), the side spans down to the anchorages, and
    the hangers every four blocks."""
    a0, a1 = ANCHOR[1] - 3, PYLONS[0]
    sag_main = TOP + 2 - (DECK + 3)
    for z in CABLE_Z:
        top_l, top_r = (PYLONS[0], TOP + 2, z), (PYLONS[1], TOP + 2, z)
        d.cable(top_l, top_r, sag_main, ORANGE_FLAT)
        d.hangers(top_l, top_r, sag_main, DECK, ORANGE_FLAT, every=4, skip=3)
        for (ax, px) in ((a0, a1), (mirror_x(a0), PYLONS[1])):
            p0, p1 = (ax, DECK + 8, z), (px, TOP + 2, z)
            if ax > px:
                p0, p1 = p1, p0
            d.cable(p0, p1, 5, ORANGE_FLAT)
            d.hangers(p0, p1, 5, DECK, ORANGE_FLAT, every=4, skip=3)


def anchorage(d: Design, x0: int, x1: int) -> None:
    """A stepped concrete anchorage block on each side of the road, the cable entering its top; a bench and a lamp."""
    for z0, z1 in ((0, 4), (WIDTH - 5, WIDTH - 1)):
        for y in range(DECK - 6, DECK + 8):
            inset = max(0, (y - DECK - 2)) // 2
            box(d, x0 + inset, y, z0, x1 - inset, y, z1, CONCRETE)
        box(d, x0, DECK + 1, z0, x1, DECK + 1, z1, Mat("concrete", tint=0xB8B3A6))


def approach_piers(d: Design) -> None:
    """Concrete piers under the side spans, from row 0 (piles below that in water) up under the road, a collar at the
    ground row."""
    for x in (36, 50):
        for px in (x, mirror_x(x) - 2):
            box(d, px, 0, 4, px + 2, DECK - 1, WIDTH - 5, CONCRETE)
            box(d, px - 1, GROUND, 3, px + 3, GROUND, WIDTH - 4, Mat("concrete", shape=sc.pack(sc.SLAB)))


def bank_ends(d: Design) -> None:
    """The road's ends on the banks: a concrete embankment from the ground row up under the deck (instead of the truss),
    and at each end a stair of RAMP steps from the ground up to the road, the full width of the bridge."""
    for x0, x1, step in ((0, EMBANK - 1, 1), (mirror_x(EMBANK - 1), LENGTH - 1, -1)):
        box(d, x0, GROUND, 0, x1, DECK - 1, WIDTH - 1, CONCRETE)  # the truss there gives way to the embankment
        edge = x0 if step > 0 else x1
        for k in range(RAMP):
            x = edge + k * step
            box(d, x, GROUND + 1 + k, 0, x, DECK + 1, WIDTH - 1, CLEAR)
            box(d, x, GROUND + 1 + k, 0, x, GROUND + 1 + k, WIDTH - 1, Mat("concrete", shape=sc.stairs(step, 0)))


def build() -> Design:
    d = Design("suspension_bridge", "Suspension Bridge", nameKey="landmark.suspension_bridge.name", ruin="always",
               ruinProfile="lattice", ruinStrength=0.5, requires=["atmosphere"], groundRow=GROUND, seat="span", weight=1,
               minTerrainGeneration=39)
    deck(d)
    approach_piers(d)
    for px in PYLONS:
        pylon(d, px, ladder=True)
    anchorage(d, *ANCHOR)
    anchorage(d, mirror_x(ANCHOR[1]), mirror_x(ANCHOR[0]))
    cables(d)
    bank_ends(d)
    d.chest(ANCHOR[0] - 3, DECK + 1, 4, table="ruin_common", hidden=True)       # a crate under a loose stone
    return d


if __name__ == "__main__":
    build().write()
