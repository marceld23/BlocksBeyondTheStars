"""Earth's two lattice towers (#3982): the Tech Eiffel Tower of Paris and the Sky Signal Tower of Tokyo.

Both are one recipe — a tapering box lattice on four splayed legs, joined by lattice arches, the legs merging into one
shaft; observation decks round it; a glass lift tube with a spiral stair up the middle; a mast on top — with their own
curve, colours and decks:

- `earth_eiffel_tower` (about 246 high on a 101 square): the iron lattice on four arched legs with stone piers, three
  platforms (the first with a glass floor ring round its well), glowing cyan light bands under every platform and on the
  lift tube, a glass cabin on the top platform and a crown of glowing antennas round the mast.
- `earth_tokyo_tower` (about 200 high on a 61 square): a slimmer lattice painted in bands of signal orange and white that
  grow shorter toward the top, a two-storey main deck and a small top deck, both glazed all round, and the banded mast.

Intact, lit and protected (an inhabited city's landmark); pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

import itertools
import math
from collections.abc import Callable
from dataclasses import dataclass, field

import shapecode as sc
import solids
from _earth import earth_design, fence_rail, glow, square_edge, square_ring
from geom import CLEAR, Design, Mat, Mix

KEYS = ("earth_eiffel_tower", "earth_tokyo_tower")
PLAZA = 1                 # the paving is row 0; a tower's h = 0 is row 1
TUBE = 4                  # the lift tube's half size: glass walls at +-4, the spiral stair (radius 3.5) inside


@dataclass
class Deck:
    """A deck at h: a gallery between `ring_in` and `outer` (half sizes), walkways to the lift tube, railings; `cabin` =
    a glazed cabin round the tube `cabin` rows high (0 = an open deck), `kiosks` = two glass pavilions on the gallery,
    `glass_ring` = the gallery's inner edge laid in clear glass (a glass floor round the well)."""
    h: int
    outer: float
    ring_in: float
    cabin: int = 0
    kiosks: bool = False
    glass_ring: bool = False


@dataclass
class Spec:
    key: str
    name: str
    c: int                                  # the centre column (x and z)
    w: Callable[[float], float]             # the tower's outer half width at h
    leg: Callable[[float], float]           # one leg's width at h (until `merge`)
    merge: int                              # where the legs end and the one shaft begins
    top: int                                # the lattice's top row (h)
    mast: int                               # the mast's tip (h)
    decks: list[Deck]
    chord: object                           # materials (a material or a function of the cell)
    brace: object
    deck_m: object
    skirt: object                           # the band under every deck's rim
    arch_top: int                           # the arches' crown (h)
    tube_bands: object = None               # the light bands round the lift tube (None = none)
    subdiv_low: int = 2
    subdiv_shaft: int = 2
    piers: object = None
    extras: list = field(default_factory=list)


# --- the lattice -------------------------------------------------------------------------------------------------------
def _line(d: Design, a, b, m) -> None:
    d.line(tuple(round(v) for v in a), tuple(round(v) for v in b), m)


def lattice(d: Design, corners, levels: list[int], subdiv: int, chord, brace, sample: int = 2) -> None:
    """A tapering box lattice: `corners(h)` gives the section's four (x, z) corners in order, `levels` the rows (h) of its
    tie frames. Each face is cut into `subdiv` bays: a chord up every bay line, a tie across every bay at every level and
    a zigzag diagonal through every bay and panel (the Eifelnyde's lattice, eifelnyde.py)."""
    h0, h1 = levels[0], levels[-1]

    def point(h: float, i: int, t: float) -> tuple[float, float, float]:
        (ax, az), (bx, bz) = corners(h)[i], corners(h)[(i + 1) % 4]
        return (ax + (bx - ax) * t, PLAZA + h, az + (bz - az) * t)

    rows = sorted(set(range(h0, h1, sample)) | set(levels))
    for i in range(4):
        for s in range(subdiv):
            for a, b in itertools.pairwise(rows):
                _line(d, point(a, i, s / subdiv), point(b, i, s / subdiv), chord)
        for n, h in enumerate(levels):
            for s in range(subdiv):
                pa, pb = point(h, i, s / subdiv), point(h, i, (s + 1) / subdiv)
                d.member(tuple(round(v) for v in pa), tuple(round(v) for v in pb), brace)
                if n + 1 < len(levels):
                    t0, t1 = (s / subdiv, (s + 1) / subdiv) if (n + s) % 2 == 0 else ((s + 1) / subdiv, s / subdiv)
                    _line(d, point(h, i, t0), point(levels[n + 1], i, t1), brace)


def panels(h0: int, h1: int, width) -> list[int]:
    """Tie levels from h0 to h1: square-ish panels, as high as a bay is wide (at least 4 rows)."""
    out, h = [h0], h0
    while h < h1:
        h = min(h1, h + max(4, round(width(h))))
        if h1 - h < 3:
            h = h1
        out.append(h)
    return sorted(set(out))


def legs(d: Design, s: Spec) -> None:
    """The four legs, each a box lattice from its pier up to the merge, then the one square shaft up to the top."""
    c = s.c
    for sx in (-1, 1):
        for sz in (-1, 1):
            def corners(h: float, sx=sx, sz=sz):
                o, i = s.w(h), s.w(h) - s.leg(h)
                return [(c + sx * i, c + sz * i), (c + sx * o, c + sz * i), (c + sx * o, c + sz * o), (c + sx * i, c + sz * o)]
            if s.piers is not None:                   # a stone pier under each leg
                o, i = s.w(0) + 1, s.w(0) - s.leg(0) - 1
                xs = sorted((round(c + sx * i), round(c + sx * o)))
                zs = sorted((round(c + sz * i), round(c + sz * o)))
                d.fill(solids.box(xs[0], PLAZA, zs[0], xs[1], PLAZA + 3, zs[1]), s.piers)
            low = min(s.merge, s.decks[0].h)          # below the first deck two bays a face, one above it
            lattice(d, corners, panels(4, low, lambda h: s.leg(h) / s.subdiv_low), s.subdiv_low, s.chord, s.brace)
            if low < s.merge:
                lattice(d, corners, panels(low, s.merge + 4, lambda h: s.leg(h)), 1, s.chord, s.brace)

    def shaft(h: float):
        o = s.w(h)
        return [(c - o, c - o), (c + o, c - o), (c + o, c + o), (c - o, c + o)]
    lattice(d, shaft, panels(s.merge, s.top, lambda h: 2 * s.w(h) / s.subdiv_shaft), s.subdiv_shaft, s.chord, s.brace)


def arches(d: Design, s: Spec) -> None:
    """The four great arches between the legs, each a double lattice rib in the slanted face."""
    c, hs, hc = s.c, 6, s.arch_top
    for face in range(4):
        def at(t: float, off: float, face=face) -> tuple[float, float, float]:
            h = hs + (hc - hs) * (1 - (2 * t - 1) ** 2) - off
            inner0 = s.w(hs) - s.leg(hs)
            u = -inner0 + 2 * inner0 * t
            v = -s.w(h) + 0.5
            x, z = (u, v) if face == 0 else (-v, u) if face == 1 else (-u, -v) if face == 2 else (v, -u)
            return (c + x, PLAZA + h, c + z)
        n = 48
        for off in (0, 3):
            pts = [at(k / n, off * (1 - abs(2 * k / n - 1)) ** 0.5) for k in range(n + 1)]
            for a, b in itertools.pairwise(pts):
                _line(d, a, b, s.chord)
        for k in range(2, n - 1, 3):
            t = k / n
            _line(d, at(t, 0), at(t + 1.5 / n, 3 * (1 - abs(2 * t - 1)) ** 0.5), s.brace)


# --- the decks ---------------------------------------------------------------------------------------------------------
def deck(d: Design, s: Spec, k: Deck) -> int:
    """A deck: the gallery, walkways to the tube, railings, the light band under its rim; a glazed cabin or kiosks."""
    c, y = s.c, PLAZA + k.h
    lo, hi = round(c - k.outer), round(c + k.outer)
    floor = {(x, y, z) for x in range(lo, hi + 1) for z in range(lo, hi + 1)
             if max(abs(x - c), abs(z - c)) > k.ring_in or min(abs(x - c), abs(z - c)) <= 1
             or max(abs(x - c), abs(z - c)) <= TUBE + 1}
    d.fill(floor, s.deck_m)
    if k.glass_ring:
        d.fill(square_ring(c, c, y, k.ring_in + 3, k.ring_in), Mat("glass_clear"))
    edge = square_edge(c, c, y + 1, k.outer)
    fence_rail(d, edge, Mat("iron_block", tint=0x3A3A40))
    if k.ring_in > TUBE + 1:
        rail = {p for p in square_edge(c, c, y + 1, k.ring_in + 1) if min(abs(p[0] - c), abs(p[2] - c)) > 1}
        fence_rail(d, rail, Mat("iron_block", tint=0x3A3A40))
    d.fill({(x, y - 1, z) for (x, _, z) in edge}, s.skirt)
    d.fill({(x, y - 2, z) for (x, _, z) in edge}, s.chord)
    for x, z in ((lo + 1, lo + 1), (hi - 1, lo + 1), (lo + 1, hi - 1), (hi - 1, hi - 1)):
        d.put(x, y + 1, z, "lantern")
    if k.cabin:
        cabin(d, s, k, y)
    if k.kiosks:
        for sg in (-1, 1):
            kx0, kx1 = sorted((round(c + sg * (k.ring_in + 3)), round(c + sg * (k.outer - 2))))
            d.box(kx0, y + 1, c - 6, kx1, y + 4, c + 6, Mat("glass"))
            d.box(kx0 + 1, y + 1, c - 5, kx1 - 1, y + 4, c + 5, CLEAR)
            d.box(kx0, y + 5, c - 6, kx1, y + 5, c + 6, s.chord)
            door_x = kx0 if sg > 0 else kx1
            d.box(door_x, y + 1, c - 1, door_x, y + 3, c, CLEAR)
            d.put(round((kx0 + kx1) / 2), y + 4, c + 4, "lantern")
    return y


def cabin(d: Design, s: Spec, k: Deck, y: int) -> None:
    """A glazed cabin over the deck: glass walls round the gallery (a door on each side), a ceiling, inside lights;
    one row of frame every four, an upper floor when it is taller than eight."""
    c, r = s.c, round(k.outer - 2)
    top = y + k.cabin
    frame = s.chord
    for yy in range(y + 1, top):
        m = frame if (yy - y) % 5 == 0 else Mat("glass")
        d.fill(square_edge(c, c, yy, r), m)
    d.fill(square_ring(c, c, top, r + 1, -1), s.deck_m)
    d.fill(square_edge(c, c, top + 1, r + 1), frame)
    if k.cabin > 8:                                     # an upper floor, open round the tube
        mid = y + 5
        d.fill(square_ring(c, c, mid, r - 1, TUBE + 4), s.deck_m)
        d.fill(square_ring(c, c, mid, TUBE + 4, TUBE), s.deck_m, where="empty")
    for ox, oz in ((0, -1), (1, 0), (0, 1), (-1, 0)):  # the doors out to the rail
        for t in (-1, 0, 1):
            x, z = (c + t, round(c + oz * r)) if ox == 0 else (round(c + ox * r), c + t)
            for yy in range(y + 1, y + 4):
                d.clear(x, yy, z)
    for x, z in ((c - r + 1, c), (c + r - 1, c), (c, c - r + 1), (c, c + r - 1)):
        d.put(x, top - 1, z, "lantern")


# --- the core: a glass lift tube with a spiral stair ------------------------------------------------------------------
def core(d: Design, s: Spec) -> None:
    """The glass tube up the middle (iron corner posts, light bands every 12 rows) and the spiral stair in it, from the
    plaza to the last deck; a doorway out onto every deck and one at its foot facing -z."""
    c = s.c
    last = PLAZA + s.decks[-1].h
    for y in range(PLAZA, last + 4):
        ring = square_edge(c, c, y, TUBE)
        corner = {p for p in ring if abs(p[0] - c) == TUBE and abs(p[2] - c) == TUBE}
        band = s.tube_bands is not None and (y - PLAZA) % 12 == 6
        d.fill(ring - corner, s.tube_bands if band else Mat("glass"), where="empty")
        d.fill(corner, s.chord, where="empty")
    d.fill(square_ring(c, c, last + 4, TUBE, -1), s.chord)
    d.spiral_stair(c, c, PLAZA, last, 1.5, 3.5, Mat("steel_floor"), steps_per_turn=14, newel=Mat("iron_block"))
    for x in range(c - 1, c + 2):                       # the doorway at the foot (-z)
        for yy in range(PLAZA, PLAZA + 3):
            d.clear(x, yy, c - TUBE)
    for k in s.decks:
        y = PLAZA + k.h
        for ox, oz in ((0, -1), (1, 0), (0, 1), (-1, 0)):
            for t in (-1, 0, 1):
                x, z = (c + t, c + oz * TUBE) if ox == 0 else (c + ox * TUBE, c + t)
                for yy in range(y + 1, y + 4):
                    d.clear(x, yy, z)
    for y in range(PLAZA + 8, last, 16):                # lanterns on the newel
        d.put(c + 2, y, c, "lantern")


def plaza(d: Design, s: Spec, half: float) -> None:
    """The paving under the tower (a darker grid every eight blocks) and lamps at its corners."""
    c = s.c
    lo, hi = round(c - half), round(c + half)
    d.fill(solids.box(lo, 0, lo, hi, 0, hi),
           lambda x, y, z: Mat("concrete", tint=0x9A9690) if (x - c) % 8 == 0 or (z - c) % 8 == 0 else Mat("concrete"))
    for x, z in ((lo + 2, lo + 2), (hi - 2, lo + 2), (lo + 2, hi - 2), (hi - 2, hi - 2)):
        d.put(x, 1, z, Mat("iron_block", tint=0x3A3A40, shape=sc.post()))
        d.put(x, 2, z, Mat("iron_block", tint=0x3A3A40, shape=sc.post()))
        d.put(x, 3, z, "lantern")


# --- the two towers ----------------------------------------------------------------------------------------------------
EIFFEL_IRON = Mix(Mat("iron_block", tint=0x6C5742), (Mat("iron_block", tint=0x5E4B3A), 0.3), seed=31)
EIFFEL_BRACE = Mat("iron_block", tint=0x584636)
CYAN = glow("strip_light_cyan", 0x40E0FF)


def eiffel_spec() -> Spec:
    def w(h: float) -> float:
        return 3.5 + 42.5 * math.exp(-h / 56)

    def leg(h: float) -> float:
        return 4 + 14 * math.exp(-h / 45)
    return Spec("earth_eiffel_tower", "Tech Eiffel Tower", c=50, w=w, leg=leg, merge=88, top=214, mast=245,
                decks=[Deck(42, w(42) + 3, 14, kiosks=True, glass_ring=True), Deck(88, w(88) + 3, TUBE + 1),
                       Deck(198, 10, TUBE + 1, cabin=10)],
                chord=EIFFEL_IRON, brace=EIFFEL_BRACE, deck_m=Mat("steel_floor"), skirt=CYAN, arch_top=40,
                tube_bands=CYAN, piers=Mix("travertine", ("stone", 0.15), seed=3))


def eiffel_crown(d: Design, s: Spec) -> None:
    """Above the lattice: the mast up to the red light, a glowing halo on four spokes and eight antennas round it."""
    c, y0 = s.c, PLAZA + s.top
    d.fill(solids.box(c - 1, y0, c - 1, c + 1, y0 + 14, c + 1), EIFFEL_IRON)
    d.fill([(c, y, c) for y in range(y0 + 15, PLAZA + s.mast)], Mat("iron_block", tint=0x3A3A40, shape=sc.post()))
    d.put(c, PLAZA + s.mast, c, glow("light_red", 0xFF3020))
    halo = y0 + 8
    d.fill({(x, halo, z) for x, z in solids.disc(c, c, 7.5) - solids.disc(c, c, 6.5)}, glow("light_blue", 0x50D8FF))
    for ox, oz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        d.member((c + 2 * ox, halo, c + 2 * oz), (c + 6 * ox, halo, c + 6 * oz), EIFFEL_BRACE)
    for x, z, _ in solids.Ellipse(c, c, 7, 7).points(8, 0.5):
        x, z = round(x), round(z)
        d.fill([(x, y, z) for y in range(halo + 1, halo + 7)], Mat("iron_block", tint=0x3A3A40, shape=sc.post()))
        d.put(x, halo + 7, z, glow("light_blue", 0x50D8FF))


def tokyo_band(y: int) -> Mat:
    """The Sky Signal Tower's paint: bands of signal orange and white, shorter toward the top."""
    bounds = [0, 28, 48, 64, 78, 90, 104, 116, 128, 138, 148, 156, 163, 170, 176, 182, 188, 194, 200]
    h = y - PLAZA
    for i, top in enumerate(bounds[1:]):
        if h < top:
            return Mat("iron_block", tint=0xE8542C) if i % 2 == 0 else Mat("iron_block", tint=0xF2F0EA)
    return Mat("iron_block", tint=0xE8542C)


def tokyo_spec() -> Spec:
    def w(h: float) -> float:
        return 2.5 + 26 * math.exp(-h / 62)

    def leg(h: float) -> float:
        return 3 + 9 * math.exp(-h / 30)
    paint = lambda x, y, z: tokyo_band(y)
    return Spec("earth_tokyo_tower", "Sky Signal Tower", c=32, w=w, leg=leg, merge=44, top=160, mast=199,
                decks=[Deck(88, w(88) + 6, TUBE + 1, cabin=11), Deck(146, w(146) + 5, TUBE + 1, cabin=6)],
                chord=paint, brace=paint, deck_m=Mat("steel_floor"), skirt=glow("strip_light_warm", 0xFFC870),
                arch_top=26, tube_bands=glow("strip_light_warm", 0xFFC870), subdiv_shaft=3,
                piers=Mat("concrete", tint=0xB8B4AC))


def tokyo_mast(d: Design, s: Spec) -> None:
    """The banded mast: a 3 x 3 stem, then a slim post, with a red light at the tip and red beacons half way."""
    c, y0 = s.c, PLAZA + s.top
    for y in range(y0, y0 + 22):
        d.fill(solids.box(c - 1, y, c - 1, c + 1, y, c + 1), tokyo_band(y))
    for y in range(y0 + 22, PLAZA + s.mast):
        d.put(c, y, c, tokyo_band(y), shape=sc.post())
    d.put(c, PLAZA + s.mast, c, glow("light_red", 0xFF3020))
    for ox, oz in ((2, 0), (-2, 0), (0, 2), (0, -2)):
        d.put(c + ox, y0 + 21, c + oz, glow("light_red", 0xFF3020))


def build_tower(s: Spec, crown, desc_half: float) -> Design:
    d = earth_design(s.key, s.name)
    plaza(d, s, desc_half)
    legs(d, s)
    arches(d, s)
    for k in s.decks:
        deck(d, s, k)
    core(d, s)
    crown(d, s)
    return d


def eiffel() -> Design:
    s = eiffel_spec()
    d = build_tower(s, eiffel_crown, s.w(0) + 4)
    c = s.c
    y1 = PLAZA + s.decks[0].h
    d.chest(c + round(s.decks[0].ring_in) + 6, y1 + 1, c - 4, table="ruin_common")
    d.chest(c + 5, PLAZA + s.decks[-1].h + 1, c + 6, items="crystal:1-2,data_fragment:1-2,iron_plate:2-4")
    return d


def tokyo() -> Design:
    s = tokyo_spec()
    d = build_tower(s, tokyo_mast, s.w(0) + 4)
    c = s.c
    d.chest(c + 7, PLAZA + s.decks[0].h + 1, c - 7, table="ruin_common")
    d.chest(c - 6, PLAZA + s.decks[1].h + 1, c + 6, items="crystal:1-2,data_fragment:1,copper_wire:2-4")
    return d


def build() -> list[Design]:
    return [eiffel(), tokyo()]


if __name__ == "__main__":
    for design in build():
        design.write()
