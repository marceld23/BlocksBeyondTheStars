"""Eifelnyde: a lattice tower in the style of the Eiffel tower on top of a stepped sandstone pyramid.

The pyramid (141 x 60, steps two rows high) is solid stone with passages inside: an entrance on the front face, an entry
hall with two side chambers, a grand gallery climbing to the great chamber in its heart, and a spiral stair that rises from
the middle of that chamber through the summit and on up the tower's core to its very top. The tower stands on the
summit plaza on four curved, splayed legs (box lattices on stone piers) joined by four lattice arches under the first
platform; the legs merge above the second platform into one slender shaft that carries the top platform with its glass
lantern and the spire. Two ruin zones: the pyramid decays as masonry, the tower as a lattice.
"""
import itertools
import math

import shapecode as sc
import solids
from geom import CLEAR, Design, Mat, Mix

C = 70                      # the centre column (x and z)
BASE = 141                  # the pyramid's base (x 0..140)
PLAZA = 60                  # the first row above the summit: the tower's h = 0
P1, P2, PTOP = 36, 73, 168  # the platforms' deck rows above the plaza
SPIRE = 189                 # the spire's tip above the plaza (row 249)
CHAMBER = 28                # the great chamber's floor row

BRONZE = Mat("bronze_block")
BRONZE_DARK = Mat("bronze_block", tint=0x6B4A32)
IRON = Mat("iron_block", tint=0x7A5A40)
CHORD = Mix(BRONZE, (Mat("bronze_block", tint=0x7A4426), 0.3), (Mat("bronze_block", tint=0x5E4634), 0.15), seed=21)
DECK = Mat("steel_floor")
STAIR = Mat("travertine", keep=True)  # kept: the pyramid's inner stairs and the legs' stairs up to the first platform
TREAD = Mat("travertine")             # free: the stairs higher up fall with the frame that carries them


def box(d: Design, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m) -> set:
    """A box with its corners in any order."""
    return d.fill(solids.box(x0, y0, z0, x1, y1, z1), m)


def w(h: float) -> float:
    """The tower's outer half-width at h rows above the plaza (an exponential curve like the original's)."""
    return 3 + 36.5 * math.exp(-h / 56)


def leg(h: float) -> float:
    """One leg's width at h."""
    return 4 + 12.5 * math.exp(-h / 45)


def inner(h: float) -> float:
    """A leg's inner edge (distance of its inner corner from the centre line)."""
    return w(h) - leg(h)


# --- the pyramid -------------------------------------------------------------------------------------------------------
def pyramid(d: Design) -> None:
    core = Mat("sandstone")
    d.pyramid(0, 0, 0, BASE, core, step=1, rise=2, top=83)
    skin = Mix(Mat("sandstone"), (Mat("sandstone", tint=0xD9B07A), 0.35), (Mat("sandstone", tint=0xB98A55), 0.2),
               seed=11)
    rim = Mix(Mat("travertine"), (Mat("travertine", tint=0xE6D2A8), 0.4), seed=12)
    for (y, xa, za, xb, zb) in solids.pyramid_layers(0, 0, 0, BASE, step=1, rise=2, top=83):
        ring = {(x, y, z) for x in range(xa, xb + 1) for z in (za, zb)} | {(x, y, z) for z in range(za, zb + 1) for x in (xa, xb)}
        d.fill(ring, rim if y % 2 == 1 else skin)
    top = {(x, 59, z) for x in range(29, 112) for z in range(29, 112)}
    paving = Mix(Mat("travertine"), (Mat("sandstone", tint=0xE3C391), 0.5), seed=13)
    d.fill(top, lambda x, y, z: Mat("travertine") if (x - C) % 8 == 0 or (z - C) % 8 == 0 else paving.at(x, y, z))


def entrance(d: Design) -> None:
    """The portal on the front (-z) face, the entry passage and hall with its two side chambers."""
    d.room(68, 1, 0, 72, 5, 14, cavity=False)                     # the passage: 5 wide, 5 high
    frame = Mat("travertine", tint=0xF2E2BC)
    for y in range(1, 8):                                          # the portal frame in the stepped face
        for x in (66, 67, 73, 74):
            z = y // 2
            d.put(x, y, z, frame)
    for x in range(66, 75):
        d.put(x, 6, 3, frame)
        d.put(x, 7, 3, frame)
    d.fill([(x, 6, 3) for x in range(68, 73)], frame.shaped(sc.ramp_down(0, 1)))
    d.room(60, 1, 15, 80, 7, 24, cavity=False)                    # the entry hall
    for x in (63, 77):                                             # two square pillars
        for z in (18, 21):
            box(d, x, 1, z, x, 7, z, Mat("travertine"))
    for (x0, x1), (c0, c1) in (((46, 56), (57, 59)), ((84, 94), (81, 83))):  # the side chambers and their passages
        d.room(x0, 1, 16, x1, 6, 24, cavity=False)
        d.room(c0, 1, 19, c1, 4, 21, cavity=False)
        d.chest((x0 + x1) // 2, 1, 23, table="ruin_common")
        d.put(x0, 1, 16, "torch")
        d.put(x1, 1, 24, "torch")
    for x, z in ((61, 15), (79, 15), (61, 24), (79, 24)):
        d.put(x, 1, z, "torch")


def gallery(d: Design) -> None:
    """The grand gallery: a stair three wide climbing from the hall to the great chamber, corbelled seven wide."""
    for k in range(CHAMBER):
        y, z = 1 + k, 25 + k
        for h, (xa, xb) in enumerate(((67, 73),) * 4 + ((68, 72),) * 2 + ((69, 71),) * 2):
            d.fill([(x, y + h, z) for x in range(xa, xb + 1)], CLEAR)
            if k == 0:
                for kk in range(1, 3):
                    d.fill([(x, y + h, z - kk) for x in range(xa, xb + 1)], CLEAR)
        d.fill([(x, y - 1, z) for x in (67, 68, 72, 73)], Mat("travertine"))   # the side benches
        d.put(67, y, z, Mat("travertine", shape=sc.stairs(0, 1)))
        d.put(73, y, z, Mat("travertine", shape=sc.stairs(0, 1)))
        if k % 6 == 3:
            d.put(66, y + 2, z, "torch")
            d.put(74, y + 2, z, "torch")
    d.stairs(68, 1, 25, 0, 1, CHAMBER, STAIR, width=5)
    # A hidden niche halfway up, behind the gallery's right wall: a chest under a loose stone.
    d.room(75, 15, 37, 78, 17, 40, cavity=False)
    d.room(74, 15, 38, 74, 16, 39, cavity=False)
    d.chest(77, 15, 39, table="ruin_rich", hidden=True)


def great_chamber(d: Design) -> None:
    """The great chamber in the pyramid's heart: pillars, a stone sarcophagus, lanterns and the treasure."""
    y0, y1 = CHAMBER + 1, CHAMBER + 9
    d.room(56, y0, 53, 84, y1, 87, cavity=False)
    for x in (59, 81):
        for z in range(57, 86, 7):
            d.column(x, z, y0, y1, Mat("travertine"), base="travertine", capital="travertine")
    box(d, 66, y0, 80, 74, y0 + 1, 84, Mat("blackened_sandstone"))              # the sarcophagus
    box(d, 67, y0 + 2, 81, 73, y0 + 2, 83, Mat("gold_block"))
    for x, z in ((57, 54), (83, 54), (57, 86), (83, 86)):
        d.put(x, y0, z, "lantern")
    d.chest(68, y0, 78, table="ruin_rich")
    d.chest(72, y0, 78, items="gold_ingot:2-4,crystal:1-2")
    d.chest(57, y0, 70, table="ruin_common")
    # The shaft up through the solid pyramid to the summit; the stair rises in it (see core_stairs).
    d.cylinder(C, C, y1 + 1, PLAZA - 1, 3.5, CLEAR)


# --- the tower -------------------------------------------------------------------------------------------------------
def _line(d: Design, a, b, m) -> None:
    d.line(tuple(round(v) for v in a), tuple(round(v) for v in b), m)


def lattice(d: Design, corners, levels: list[int], subdiv: int, chord=CHORD, tie=IRON, brace=IRON, faces=range(4),
            sample: int = 2) -> None:
    """A tapering box lattice: `corners(h)` gives the section's four (x, z) corners in order, `levels` the rows (h) of
    its tie frames. Each face is cut into `subdiv` bays: a chord up every bay line (followed every `sample` rows, so a
    curved leg stays curved), a tie across every bay at every level and a zigzag diagonal through every bay and panel."""
    h0, h1 = levels[0], levels[-1]

    def point(h: float, i: int, t: float) -> tuple[float, float, float]:
        (ax, az), (bx, bz) = corners(h)[i], corners(h)[(i + 1) % 4]
        return (ax + (bx - ax) * t, PLAZA + h, az + (bz - az) * t)

    rows = sorted(set(range(h0, h1, sample)) | set(levels))
    for i in faces:
        for s in range(subdiv):
            t = s / subdiv
            for a, b in itertools.pairwise(rows):
                _line(d, point(a, i, t), point(b, i, t), chord)
        for n, h in enumerate(levels):
            for s in range(subdiv):
                pa, pb = point(h, i, s / subdiv), point(h, i, (s + 1) / subdiv)
                d.member(tuple(round(v) for v in pa), tuple(round(v) for v in pb), tie)
                if n + 1 < len(levels):
                    up = levels[n + 1]
                    t0, t1 = (s / subdiv, (s + 1) / subdiv) if (n + s) % 2 == 0 else ((s + 1) / subdiv, s / subdiv)
                    _line(d, point(h, i, t0), point(up, i, t1), brace)


def panels(h0: int, h1: int, width) -> list[int]:
    """Tie levels from h0 to h1: square-ish panels, as high as a bay is wide (at least 4 rows)."""
    out, h = [h0], h0
    while h < h1:
        h = min(h1, h + max(4, round(width(h))))
        if h1 - h < 3:
            h = h1
        out.append(h)
    return sorted(set(out))


def legs(d: Design) -> None:
    """The four legs: a hollow stone pier on the plaza (a doorway on its inner side), then a box lattice up to the second
    platform (the stairs inside them come last, see leg_stair)."""
    for sx in (-1, 1):
        for sz in (-1, 1):
            def corners(h: float, sx=sx, sz=sz):
                o, i = w(h), inner(h)
                return [(C + sx * i, C + sz * i), (C + sx * o, C + sz * i), (C + sx * o, C + sz * o), (C + sx * i, C + sz * o)]
            x0, x1 = sorted((round(C + sx * (inner(0) - 1)), round(C + sx * (w(0) + 1))))
            z0, z1 = sorted((round(C + sz * (inner(0) - 1)), round(C + sz * (w(0) + 1))))
            pier = Mix("travertine", ("stone", 0.15), seed=3)
            box(d, x0, PLAZA, z0, x1, PLAZA + 2, z1, pier)
            box(d, x0 + 2, PLAZA, z0 + 2, x1 - 2, PLAZA + 2, z1 - 2, CLEAR)
            d.fill([(x, PLAZA + 3, z) for x in range(x0, x1 + 1) for z in (z0, z1)]
                   + [(x, PLAZA + 3, z) for z in range(z0, z1 + 1) for x in (x0, x1)], Mat("travertine", shape=sc.pack(sc.SLAB)))
            face = x1 if sx < 0 else x0                                # the pier's inner face in x (toward the centre)
            box(d, face, PLAZA, C + sz * 30, face + sx, PLAZA + 2, C + sz * 31, CLEAR)   # the doorway, 2 x 3
            d.put(face + 3 * sx, PLAZA, C + sz * 34, "lantern")
            lattice(d, corners, panels(4, P1, lambda h: leg(h) / 2), 2)
            lattice(d, corners, panels(P1, P2 + 4, lambda h: leg(h)), 1)


def leg_stair(d: Design, sx: int, sz: int) -> None:
    """The stairs inside a leg. From the plaza to the first platform's deck it climbs one row per step and turns at
    every step, alternately inward along x and along z, so it moves half a block inward per row in both — the lean of
    the leg there — in two lanes side by side. From the first to the second platform the leg leans less: a one-lane
    stair that winds round inside the leg and follows its middle (`_follow`). Every step is the stairs form facing the
    way it is climbed, and the three rows above it are cleared."""
    treads = {}
    s0 = 35
    for k in range(P1 + 1):
        ox, oz = s0 - (k + 1) // 2, s0 - k // 2
        move = (-1, 0) if k % 2 == 1 else (0, -1)
        for lx, lz in ((0, 0), (1, -1)):
            treads[(ox + lx, k, oz + lz)] = move
    for (ox, h, oz), move in _follow((17, 17), P1 + 1, P2).items():
        treads[(ox, h, oz)] = move
    cells = {(C + sx * ox, PLAZA + h, C + sz * oz): (sx * mx, sz * mz) for (ox, h, oz), (mx, mz) in treads.items()}
    for (x, y, z) in cells:
        for h in range(1, 4):
            if (x, y + h, z) not in cells:
                d.clear(x, y + h, z)
    for (x, y, z), (mx, mz) in cells.items():
        d.put(x, y, z, STAIR if y <= PLAZA + P1 else TREAD, shape=sc.stairs(mx, mz))


def _follow(start: tuple[int, int], h0: int, h1: int) -> dict:
    """A one-lane stair (offsets from the centre line, inward = smaller) from `start` on row h0 to row h1, one row per
    step, that stays inside the leg and as close to its middle as it can: a depth-first search over the four moves (never
    straight back), every column's next step at least four rows above its last (head room)."""
    def ok(o: int, h: int) -> bool:
        return inner(h) + 1.2 <= o <= w(h) - 1.2

    best: dict = {}

    def search(pos, h, last, used, path) -> bool:
        if h > h1:
            best.update(path)
            return True
        mid = w(h) - leg(h) / 2
        moves = [(-1, 0), (0, -1), (1, 0), (0, 1)]
        moves.sort(key=lambda m: (pos[0] + m[0] - mid) ** 2 + (pos[1] + m[1] - mid) ** 2)
        for m in moves:
            if last and m == (-last[0], -last[1]):
                continue
            q = (pos[0] + m[0], pos[1] + m[1])
            if not (ok(q[0], h) and ok(q[1], h)) or used.get(q, -99) > h - 4:
                continue
            prev = used.get(q)
            used[q] = h
            path[(q[0], h, q[1])] = m
            if search(q, h + 1, m, used, path):
                return True
            del path[(q[0], h, q[1])]
            if prev is None:
                del used[q]
            else:
                used[q] = prev
        return False

    if not search(start, h0, None, {start: h0 - 1}, {}):
        raise RuntimeError("no stair path inside the leg")
    return best


def arches(d: Design) -> None:
    """The four great arches between the legs, each a lattice rib in the slanted face, crowned under platform 1."""
    hs, hc = 10, P1 - 2
    for face in range(4):
        def at(t: float, off: float, face=face) -> tuple[float, float, float]:
            h = hs + (hc - hs) * (1 - (2 * t - 1) ** 2) - off
            u = -inner(hs) + 2 * inner(hs) * t
            # A rib follows the face plane: the face's distance from the centre at its row.
            v = -w(h) + 0.5
            x, z = (u, v) if face == 0 else (-v, u) if face == 1 else (-u, -v) if face == 2 else (v, -u)
            return (C + x, PLAZA + h, C + z)
        n = 48
        for off in (0, 3):
            pts = [at(k / n, off * (1 - abs(2 * k / n - 1)) ** 0.5) for k in range(n + 1)]
            for a, b in itertools.pairwise(pts):
                _line(d, a, b, BRONZE)
        for k in range(2, n - 1, 3):  # the web between the two ribs
            t = k / n
            _line(d, at(t, 0), at(t + 1.5 / n, 3 * (1 - abs(2 * t - 1)) ** 0.5), IRON)


def platform(d: Design, h: int, outer: float, ring_in: float, kiosks: bool) -> int:
    """A deck at h: an outer gallery between ring_in and outer, four walkways to the stair core, railings; returns its row."""
    y = PLAZA + h
    lo, hi = round(C - outer), round(C + outer)
    a, b = round(C - ring_in), round(C + ring_in)
    deck = {(x, y, z) for x in range(lo, hi + 1) for z in range(lo, hi + 1)
            if not (a < x < b and a < z < b) or abs(x - C) <= 1 or abs(z - C) <= 1 or max(abs(x - C), abs(z - C)) <= 5}
    deck -= {(x, y, z) for x, z in solids.disc(C, C, 3.5)}
    d.fill(deck, DECK)
    along_x, along_z = Mat("bronze_block", shape=sc.pack(sc.FENCE)), Mat("bronze_block", shape=sc.pack(sc.FENCE, 1))
    edge = {(x, y + 1, z) for x in range(lo, hi + 1) for z in (lo, hi)} | {(x, y + 1, z) for z in range(lo, hi + 1) for x in (lo, hi)}
    rails = ({(x, y + 1, z) for x in range(a, b + 1) for z in (a, b)} | {(x, y + 1, z) for z in range(a, b + 1) for x in (a, b)})
    rails = {p for p in rails if abs(p[0] - C) > 1 and abs(p[2] - C) > 1} if ring_in > 0 else set()
    for p in edge:
        d.put(*p, along_z if p[0] in (lo, hi) else along_x)
    for p in rails:
        d.put(*p, along_z if p[0] in (a, b) else along_x)
    skirt = {(x, yy, z) for yy in (y - 1, y - 2) for (x, _, z) in edge}  # the frieze under the deck's rim
    d.fill(skirt, lambda x, yy, z: BRONZE_DARK if (x + z + yy) % 2 == 0 else None)
    for x, z in ((lo + 1, lo + 1), (hi - 1, lo + 1), (lo + 1, hi - 1), (hi - 1, hi - 1)):
        d.put(x, y + 1, z, "lantern")
    if kiosks:
        for s in (-1, 1):
            kx0, kx1 = sorted((round(C + s * (ring_in + 2)), round(C + s * (outer - 2))))
            box(d, kx0, y + 1, C - 6, kx1, y + 4, C + 6, Mat("glass"))
            box(d, kx0 + 1, y + 1, C - 5, kx1 - 1, y + 4, C + 5, CLEAR)
            box(d, kx0, y + 5, C - 6, kx1, y + 5, C + 6, BRONZE_DARK)
            door_x = kx0 if s > 0 else kx1
            box(d, door_x, y + 1, C - 1, door_x, y + 3, C, CLEAR)
            d.put(round((kx0 + kx1) / 2), y + 4, C + 4, "lantern")
        d.chest(round(C + (ring_in + outer) / 2), y + 1, C - 4, table="ruin_common")
    return y


def shaft(d: Design) -> None:
    """Above the second platform the legs are one square shaft, tapering to the top platform."""
    def corners(h: float):
        o = w(h)
        return [(C - o, C - o), (C + o, C - o), (C + o, C + o), (C - o, C + o)]
    lattice(d, corners, panels(P2, PTOP, lambda h: w(h)), 2, sample=2)


def top(d: Design) -> None:
    """The top platform, its glass lantern, a dome and the spire with a red warning light."""
    y = platform(d, PTOP, 8, 0, False)
    d.ring(C, C, y + 1, y + 4, 6, 1, Mat("glass"))
    for x, z, _ in solids.Ellipse(C, C, 6, 6).points(8):
        d.column(round(x), round(z), y + 1, y + 4, BRONZE)
    d.disc(C, C, y + 5, 7, BRONZE_DARK)
    d.dome(C, C, y + 6, 5, BRONZE, ry=4, wall=1)
    d.fill([(C, yy, C) for yy in range(y + 10, PLAZA + SPIRE)], BRONZE.shaped(sc.post()))
    d.put(C, PLAZA + SPIRE, C, Mat("light_red", glow=0xFF3020))
    box(d, C - 1, y + 1, C + 5, C, y + 3, C + 6, CLEAR)                             # a door onto the deck
    d.chest(C + 4, y + 1, C - 3, items="gold_ingot:1-3,crystal:1-2,data_fragment:1-2")
    d.put(C - 4, y + 4, C + 3, "lantern")


def core_stairs(d: Design) -> None:
    """The way up from the great chamber: a spiral stair to the summit (railed in the chamber, a little pavilion over
    its exit on the plaza); the legs' stairs lead on to the second platform, and from there a second spiral in a cage
    of bronze bars climbs the tower's core to the top."""
    post = BRONZE.shaped(sc.post())
    cage = solids.ring2d(C, C, 4.5, 1)
    d.fill([(x, y, z) for x, z in cage for y in range(CHAMBER + 4, CHAMBER + 10)], post, where="empty")
    treads = d.spiral_stair(C, C, CHAMBER + 1, PLAZA - 1, 1.5, 3.5, STAIR, steps_per_turn=14, newel=STAIR)
    d.keep(treads)
    # The pavilion over the exit: four columns, a lintel ring, a pyramidion roof; a railing with a gap.
    for x, z in ((C - 5, C - 5), (C + 5, C - 5), (C - 5, C + 5), (C + 5, C + 5)):
        d.column(x, z, PLAZA, PLAZA + 4, Mat("travertine"), base="travertine", capital="travertine")
    d.fill([(x, PLAZA + 5, z) for x in range(C - 5, C + 6) for z in range(C - 5, C + 6)
            if max(abs(x - C), abs(z - C)) == 5], Mat("travertine"))
    d.pyramid(C - 5, C - 5, PLAZA + 6, 11, Mat("sandstone"), smooth=True, wall=1)
    # The railing round the hole, open where the stair's top step leads on (the next step's direction).
    top = [(x, z) for (x, y, z) in treads if y == PLAZA - 1]
    ahead = math.atan2(sum(z - C for _, z in top), sum(x - C for x, _ in top)) + 0.6
    rail = [(x, PLAZA, z) for x, z in cage
            if abs((math.atan2(z - C, x - C) - ahead + math.pi) % (2 * math.pi) - math.pi) > 0.55]
    d.fill(rail, post, where="empty")
    # The tower's core stair from the second platform to the top, and its cage.
    for y in range(PLAZA + P2 + 4, PLAZA + PTOP):
        d.fill([(x, y, z) for x, z in cage], post, where="empty")
    d.spiral_stair(C, C, PLAZA + P2 + 1, PLAZA + PTOP, 1.5, 3.5, TREAD, steps_per_turn=14, newel=TREAD)


def build() -> Design:
    d = Design("eifelnyde", "Eifelnyde", nameKey="landmark.eifelnyde.name", ruin="always", ruinProfile="masonry",
               ruinStrength=0.3, requires=["atmosphere"], weight=1, minTerrainGeneration=39)
    pyramid(d)
    entrance(d)
    gallery(d)
    great_chamber(d)
    legs(d)
    arches(d)
    platform(d, P1, w(P1) + 3, 12, True)
    platform(d, P2, w(P2) + 2.5, 7.5, False)
    d.chest(C + 10, PLAZA + P2 + 1, C + 10, table="ruin_rich", chance=0.7)
    shaft(d)
    top(d)
    for sx in (-1, 1):  # the stairs last: their head room cuts through the decks and the lattice
        for sz in (-1, 1):
            leg_stair(d, sx, sz)
    core_stairs(d)
    # The pyramid decays as masonry; the tower as a lattice, in two boxes that hug it (the pass drops its collapse spots
    # anywhere in a zone's box, so a box as wide as the pyramid would waste most of them on the air round the shaft). The
    # slender upper box is what the lattice BREAKS of ruin revision 2 cut through: a plane across the shaft, and the top
    # with its lantern and spire falls. Kept (STAIR) are only the pyramid's inner stairs and the legs' stairs up to the
    # first platform, so nothing above holds the falling top.
    d.ruin_zone(0, 0, 0, BASE - 1, PLAZA - 1, BASE - 1, "masonry", 0.3)
    d.ruin_zone(C - 42, PLAZA, C - 42, C + 42, PLAZA + P2 + 4, C + 42, "lattice", 0.4)
    d.ruin_zone(C - 17, PLAZA + P2 + 5, C - 17, C + 17, PLAZA + SPIRE, C + 17, "lattice", 0.4)
    return d


if __name__ == "__main__":
    build().write()
