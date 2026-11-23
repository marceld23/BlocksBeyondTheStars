"""Earth's three pyramids of Cairo (#3982), at full scale (one block a metre): the ancient wonders, so — unlike Earth's
other landmarks — ruins (`ruin: always`, masonry, a low strength), every passage lined with kept stone and its own
gentle ruin zone, so the way in always stays walkable.

- `earth_pyramid_great` (231 wide, 139 high — its tip is gone, a flat top with a marker pole where it once ended at 146):
  a smooth casing of white limestone that has crumbled away in patches to show the stepped core. A stair climbs the front
  face to the entrance; a long passage leads in to a junction, straight on to the queen's chamber, up the ascending
  passage and the tall corbelled grand gallery to the king's chamber of red granite with its stone coffin.
- `earth_pyramid_second` (215 wide, 137 high): the stepped core with the cap of smooth casing still on its top, an
  entrance low on the front face and a passage to the burial chamber under its middle.
- `earth_pyramid_third` (105 wide, 62 high): the smallest, its lowest courses cased in pink granite, a passage down to a
  panelled antechamber and the burial chamber.

Torches in the passages are kept, so they still burn in the ruin. Pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

from collections.abc import Callable

import shapecode as sc
import solids
from _classical import flight
from _earth import earth_design
from geom import CLEAR, Design, Mat

KEYS = ("earth_pyramid_great", "earth_pyramid_second", "earth_pyramid_third")
MARGIN = 7                      # cells in front of the base for the entrance stair
CORE = Mat("sandstone", tint=0xC9AA7C)
CORE_DARK = Mat("sandstone", tint=0xB5966A)
CASING = Mat("travertine", tint=0xEEE5CF)
PINK_GRANITE = Mat("granite", tint=0xC48E7E)
GRANITE = Mat("granite", tint=0x9A5E52)
DRESSED = Mat("travertine", tint=0xDCCFAF)
TORCH = Mat("torch", keep=True)
Cell = tuple[int, int, int]


class Pyramid:
    """One pyramid on a square base 2 * half + 1 wide, centred on (half, half + MARGIN), its slope reaching a point at
    row `height`, built up to row `top` - 1 (a flat top when top < height). Every carved passage is remembered: its
    lining is kept at the end."""

    def __init__(self, d: Design, half: int, height: int, top: int) -> None:
        self.d, self.half, self.height, self.top = d, half, height, top
        self.cx, self.cz = half, half + MARGIN
        self.carved: set[Cell] = set()

    def r(self, y: int) -> int:
        """The layer's half size on row y."""
        return round(self.half * (1 - y / self.height))

    def face_z(self, y: int) -> int:
        """The front (-z) face's z on row y."""
        return self.cz - self.r(y)

    def body(self, cased: Callable[[int, int, int], bool], casing: Mat) -> None:
        """Solid layers of core stone; each layer's rim in the casing (a ramp rising inward where the next layer steps
        in, a pyramid form on the corners) where `cased`, else the bare core in alternating courses."""
        d, cx, cz = self.d, self.cx, self.cz
        for y in range(self.top):
            r = self.r(y)
            d.fill(solids.box(cx - r, y, cz - r, cx + r, y, cz + r), CORE)
            inset = y + 1 < self.top and self.r(y + 1) < r
            rim = [(x, z) for x in range(cx - r, cx + r + 1) for z in (cz - r, cz + r)] + \
                  [(x, z) for z in range(cz - r + 1, cz + r) for x in (cx - r, cx + r)]
            for x, z in rim:
                if not cased(x, y, z):
                    d.put(x, y, z, CORE_DARK if y % 2 else CORE)
                    continue
                if not inset:
                    d.put(x, y, z, casing)
                    continue
                sides = ([(1, 0)] if x == cx - r else [(-1, 0)] if x == cx + r else []) + \
                        ([(0, 1)] if z == cz - r else [(0, -1)] if z == cz + r else [])
                d.put(x, y, z, casing, shape=sc.pack(sc.PYRAMID) if len(sides) == 2 else sc.ramp(*sides[0]))

    def carve(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int) -> set[Cell]:
        cells = solids.box(x0, y0, z0, x1, y1, z1)
        self.d.fill(cells, CLEAR)
        self.carved |= cells
        return cells

    def entrance(self, walk: int, x: int) -> int:
        """A stair up the front face from the ground to the entrance on row `walk` (three wide at x - 1..x + 1), a
        dressed portal with a gable of two leaning slabs; returns the z where the passage enters the core."""
        d = self.d
        z_in = self.face_z(walk - 1)
        z0 = z_in - walk
        treads = flight(d, x - 1, 1, z0, 0, 1, walk - 1, DRESSED, width=3)
        for tx, ty, tz in treads:
            for yy in range(0, ty):
                if (tx, yy, tz) not in d.plain:
                    d.put(tx, yy, tz, DRESSED)
        d.fill(solids.box(x - 1, 0, z0 - 2, x + 1, 0, z0 - 1), DRESSED)
        d.fill(solids.box(x - 1, 0, z_in - 1, x + 1, walk - 1, z_in - 1), DRESSED)       # the landing at the portal
        self.carve(x - 1, walk, z0 + walk - 2, x + 1, walk + 2, z_in + 2)
        for k, yy in enumerate((walk + 3, walk + 4)):                       # the gable over the portal
            gz = self.face_z(yy) - 1
            d.put(x - 2 + k, yy, gz, DRESSED, shape=sc.ramp(1, 0))
            d.put(x + 2 - k, yy, gz, DRESSED, shape=sc.ramp(-1, 0))
        return z_in

    def torches(self, cells: list[Cell]) -> None:
        for p in cells:
            self.d.put(*p, TORCH)

    def finish(self, lining: Mat | None = None) -> None:
        """Keeps every block next to a carved cell (faces only), so the ruin pass leaves the passages walkable; with
        `lining` the passages' walls, floors and ceilings are dressed in it too."""
        d = self.d
        around = set()
        for x, y, z in self.carved:
            for n in ((x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1)):
                if n not in self.carved and d.is_block(*n):
                    around.add(n)
        if lining is not None:
            plain = {p for p in around if d.get(*p)[1] in ("sandstone",) and d.get(*p)[4] == 0}
            d.restyle(plain, block=lining.block, tint=lining.tint)
        d.keep(around)


def gallery(p: Pyramid, x: int, walk: int, z: int, steps: int) -> None:
    """The grand gallery: a stair three wide between two side benches, climbing `steps` rows, corbelled in from seven
    wide to three, eight rows high; torches on its walls."""
    d = p.d
    for k in range(steps + 1):
        y, zz = walk + k, z + k
        for h, half in enumerate((3, 3, 3, 3, 2, 2, 1, 1)):
            p.carve(x - half, y + h, zz, x + half, y + h, zz)
        d.put(x - 3, y - 1, zz, DRESSED)
        d.put(x + 3, y - 1, zz, DRESSED)
        d.put(x - 3, y, zz, DRESSED, shape=sc.stairs(0, 1))
        d.put(x + 3, y, zz, DRESSED, shape=sc.stairs(0, 1))
        if k % 5 == 2:
            p.torches([(x - 3, y + 2, zz), (x + 3, y + 2, zz)])
    treads = flight(d, x - 1, walk, z, 0, 1, steps, DRESSED, width=3, clear=0)
    p.carved -= treads


def chamber(p: Pyramid, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, gable: bool = False) -> set[Cell]:
    """A burial chamber: the box carved, optionally with a gabled roof of leaning slabs along x."""
    cells = p.carve(x0, y0, z0, x1, y1, z1)
    if gable:
        w = (x1 - x0) // 2
        for k in range(w):
            p.carve(x0 + k + 1, y1 + 1 + k, z0, x1 - k - 1, y1 + 1 + k, z1)
            for z in range(z0, z1 + 1):
                p.d.put(x0 + k, y1 + 1 + k, z, DRESSED, shape=sc.ramp_down(-1, 0))
                p.d.put(x1 - k, y1 + 1 + k, z, DRESSED, shape=sc.ramp_down(1, 0))
    return cells


def sarcophagus(d: Design, x0: int, y: int, z0: int, x1: int, z1: int, m: Mat) -> None:
    d.box(x0, y, z0, x1, y + 1, z1, m)
    d.box(x0 + 1, y + 1, z0 + 1, x1 - 1, y + 1, z1 - 1, CLEAR)


# --- the great pyramid ------------------------------------------------------------------------------------------------
def great() -> Design:
    d = earth_design("earth_pyramid_great", "Great Pyramid", ruin="always", ruinProfile="masonry", ruinStrength=0.25)
    p = Pyramid(d, 115, 146, 139)
    p.body(lambda x, y, z: y < 134 and solids.value_noise(x / 11, y / 6, z / 11, 7) < 0.6, CASING)
    cx, cz = p.cx, p.cz
    walk = 17
    p.entrance(walk, cx)
    ax = cx + 4                                         # the ascending passage and the gallery run beside the queen's
    junction = cz - 70
    p.carve(cx - 1, walk, p.face_z(walk), cx + 1, walk + 2, junction + 4)             # the entrance passage
    p.carve(cx - 1, walk, junction, ax + 1, walk + 3, junction + 4)                   # the junction
    p.torches([(cx - 1, walk + 1, junction - 20), (cx + 1, walk + 1, junction - 8), (cx - 1, walk + 2, junction + 2)])
    # Straight on: the queen's passage and chamber (its gabled roof) under the middle.
    p.carve(cx - 1, walk, junction + 5, cx + 1, walk + 2, cz - 4)
    chamber(p, cx - 3, walk, cz - 3, cx + 3, walk + 4, cz + 3, gable=True)
    p.torches([(cx - 1, walk + 1, cz - 20), (cx - 3, walk + 2, cz), (cx + 3, walk + 2, cz)])
    d.chest(cx + 2, walk, cz + 2, table="ruin_common")
    # Up: the ascending passage (9 rows) and the grand gallery (17 rows) to the king's level.
    z = junction + 1
    treads = flight(d, ax - 1, walk, z, 0, 1, 9, DRESSED, width=3, clear=0)
    for tx, ty, tz in treads:
        p.carve(tx, ty + 1, tz, tx, ty + 3, tz)
    king = walk + 9 + 17
    gallery(p, ax, walk + 9, z + 9, 17)
    zk = z + 9 + 18
    p.carve(ax - 1, king, zk, ax + 1, king + 2, cz - 16)                             # the horizontal passage
    p.carve(ax - 2, king, cz - 15, ax + 2, king + 4, cz - 11)                        # the antechamber
    kc = chamber(p, ax - 5, king, cz - 10, ax + 5, king + 5, cz - 5)                # the king's chamber
    d.fill({(x, y, z) for x, y, z in solids.shell(solids.box(ax - 6, king - 1, cz - 11, ax + 6, king + 6, cz - 4), 1)
            if (x, y, z) not in kc and (x, y, z) not in p.carved}, GRANITE)
    sarcophagus(d, ax + 2, king, cz - 9, ax + 4, cz - 7, GRANITE)
    p.torches([(ax - 1, king + 1, zk + 8), (ax - 5, king + 2, cz - 7), (ax + 5, king + 2, cz - 9)])
    d.chest(ax - 4, king, cz - 6, items="gold_ingot:2-4,crystal:1-2")
    d.chest(ax - 4, king, cz - 9, table="ruin_rich")
    # The marker pole on the flat top, where the tip once was.
    d.fill([(cx, y, cz) for y in range(139, 146)], Mat("wood_log"), shape=sc.post())
    p.finish(DRESSED)
    d.ruin_zone(cx - 8, walk - 1, 0, ax + 7, king + 7, cz - 3, "masonry", 0.05)
    return d


# --- the second pyramid -----------------------------------------------------------------------------------------------
def second() -> Design:
    d = earth_design("earth_pyramid_second", "Second Pyramid", ruin="always", ruinProfile="masonry", ruinStrength=0.2)
    p = Pyramid(d, 107, 143, 137)
    p.body(lambda x, y, z: y >= 113 + 5 * solids.value_noise(x / 8, 0, z / 8, 3), CASING)
    cx, cz = p.cx, p.cz
    walk = 12
    p.entrance(walk, cx)
    p.carve(cx - 1, walk, p.face_z(walk), cx + 1, walk + 2, cz - 6)
    chamber(p, cx - 7, walk, cz - 5, cx + 7, walk + 4, cz + 2, gable=True)
    sarcophagus(d, cx + 2, walk, cz - 1, cx + 5, cz + 1, GRANITE)
    p.torches([(cx - 1, walk + 1, cz - 40), (cx + 1, walk + 1, cz - 20), (cx - 7, walk + 2, cz - 2), (cx + 7, walk + 2, cz - 2)])
    d.chest(cx - 5, walk, cz + 1, table="ruin_rich")
    d.chest(cx - 5, walk, cz - 4, table="ruin_common")
    p.finish(DRESSED)
    d.ruin_zone(cx - 9, walk - 1, 0, cx + 9, walk + 13, cz + 4, "masonry", 0.05)
    return d


# --- the third pyramid ------------------------------------------------------------------------------------------------
def third() -> Design:
    d = earth_design("earth_pyramid_third", "Third Pyramid", ruin="always", ruinProfile="masonry", ruinStrength=0.3)
    p = Pyramid(d, 52, 65, 62)
    p.body(lambda x, y, z: y < 16 and solids.value_noise(x / 6, y / 4, z / 6, 5) < 0.7, PINK_GRANITE)
    cx, cz = p.cx, p.cz
    walk = 5
    p.entrance(walk, cx)
    p.carve(cx - 1, walk, p.face_z(walk), cx + 1, walk + 2, cz - 13)
    ante = p.carve(cx - 3, walk, cz - 12, cx + 3, walk + 3, cz - 7)                 # the panelled antechamber
    for x, y, z in solids.shell(solids.box(cx - 4, walk, cz - 13, cx + 4, walk + 3, cz - 6), 1):
        if (x, y, z) not in ante and (x + z) % 2 == 0 and d.is_block(x, y, z):
            d.put(x, y, z, PINK_GRANITE)
    p.carve(cx - 1, walk, cz - 6, cx + 1, walk + 2, cz - 3)
    chamber(p, cx - 4, walk, cz - 2, cx + 4, walk + 3, cz + 4, gable=True)
    sarcophagus(d, cx - 1, walk, cz + 1, cx + 1, cz + 3, Mat("basalt"))
    p.torches([(cx - 1, walk + 1, cz - 20), (cx - 3, walk + 2, cz - 10), (cx + 4, walk + 2, cz)])
    d.chest(cx + 3, walk, cz + 3, table="ruin_rich", chance=0.8)
    d.chest(cx + 2, walk, cz - 8, table="ruin_common")
    p.finish(DRESSED)
    d.ruin_zone(cx - 6, walk - 1, 0, cx + 6, walk + 9, cz + 6, "masonry", 0.05)
    return d


def build() -> list[Design]:
    return [great(), second(), third()]


if __name__ == "__main__":
    for design in build():
        design.write()
