"""Earth's concert hall of Sydney (#3982): `earth_sydney_opera`, the "Harbour Shell Hall" — sail-like white shell roofs
on a broad granite podium by the water, about 110 x 64 x 165.

The podium (ten rows) is climbed from the land side (-z) by a grand stair eighty blocks wide. On it stand two rows of
shells, the great concert hall and the smaller theatre side by side, each a small shell opening toward the stair (the
glazed entrance) and three nested shells growing taller toward the harbour (+z), their mouths glazed in warm amber glass;
a pair of little shells at the stair's west end holds the restaurant. Each shell is a pointed sail: from the side a
quarter ellipse rising from its back to its open mouth, from the front a pointed arch, two cells thick, tiled white
with cream chevrons. Inside the concert hall a stage with organ pipes faces tiered seats; the foyer behind the
seats looks out over the harbour through the big glass mouth. Intact, lit and protected (an inhabited city's landmark);
pin-only — placed by Earth's location list only.
"""
from __future__ import annotations

import math

import shapecode as sc
import solids
from _earth import earth_design, fence_rail, glow
from geom import CLEAR, Design, Mat

KEYS = ("earth_sydney_opera",)
Cell = tuple[int, int, int]
WIDTH = 110
PODIUM = (0, 9)                     # the podium's rows; people walk on row 10
P_Z = (20, 164)                     # the podium's z extent
BASE = PODIUM[1] + 1                # the shells stand on this row
STAIR_X = (15, 94)

TILE = Mat("travertine", tint=0xF6F4EE)
TILE_CREAM = Mat("travertine", tint=0xE4DBC2)
GLASS = Mat("glass", tint=0xD8B878)
GRANITE = Mat("granite", tint=0xB89C88)
GRANITE_DARK = Mat("granite", tint=0x9C8270)
WOOD = Mat("wood_planks_mahogany")
SEAT = Mat("wood_planks_mahogany", tint=0xA03028)
WARM = glow("strip_light_warm", 0xFFC878)


class Shell:
    """One sail-shaped shell standing on row BASE: its back foot at z_back, its open mouth `length` further along
    `facing` (+1 = toward +z), `half` wide at the foot, `height` high at the mouth's apex; the mouth leans `lean`
    cells forward at the top."""

    def __init__(self, cx: float, z_back: int, length: int, half: float, height: int, facing: int = 1,
                 lean: float = 4.0) -> None:
        self.cx, self.z_back, self.length, self.half, self.height = cx, z_back, length, half, height
        self.facing, self.lean = facing, lean
        self.z_mouth = z_back + facing * length

    def v(self, y: float, z: float) -> float:
        """The distance behind the (leaning) mouth plane, along the shell (0 = in the mouth, `length` = the back)."""
        mouth = self.z_mouth + self.facing * self.lean * (y - BASE) / self.height
        return (mouth - z) * self.facing

    def inside(self, x: int, y: int, z: int) -> bool:
        if y < BASE:
            return False
        u = abs(x - self.cx) / self.half
        if u >= 1:
            return False
        v = self.v(y, z)
        if v < 0:
            return False
        s = (1 - u) ** 0.6
        return (v / self.length) ** 2 + ((y - BASE) / self.height) ** 2 <= s * s

    def solid(self) -> set[Cell]:
        zs = sorted((self.z_back, self.z_mouth + self.facing * math.ceil(self.lean)))
        return {(x, y, z) for x in range(math.floor(self.cx - self.half), math.ceil(self.cx + self.half) + 1)
                for y in range(BASE, BASE + self.height + 1) for z in range(zs[0], zs[1] + 1) if self.inside(x, y, z)}

    def open_side(self, n: Cell) -> bool:
        """A neighbour that does not count as outside: below the shell's foot or beyond its mouth."""
        return n[1] < BASE or self.v(n[1], n[2]) < 0


def boundary(cells: set[Cell], shell: Shell) -> set[Cell]:
    out = set()
    for x, y, z in cells:
        for n in ((x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1)):
            if n not in cells and not shell.open_side(n):
                out.add((x, y, z))
                break
    return out


def shells(d: Design, group: list[Shell]) -> tuple[set[Cell], set[Cell]]:
    """Writes a group of overlapping shells: each one's skin two thick (tiles with cream chevrons) and its mouth glazed,
    then clears what lies inside any of them, so the group is one hall. Returns (the inside, the mouth glass left)."""
    inside: set[Cell] = set()
    glass: set[Cell] = set()
    for s in group:
        solid = s.solid()
        skin = boundary(solid, s)
        skin |= boundary(solid - skin, s)
        inner = solid - skin
        d.fill(skin, lambda x, y, z, s=s: TILE_CREAM if round(s.v(y, z) + abs(x - s.cx)) % 7 == 0 else TILE)
        mouth = {}
        for x, y, z in inner:                                   # the front-most inner cell of each (x, y): the mouth
            k = (x, y)
            if k not in mouth or (z - mouth[k]) * s.facing > 0:
                mouth[k] = z
        panes = {(x, y, z) for (x, y), z in mouth.items()}
        d.fill(panes, GLASS)
        glass |= panes
        inside |= inner - panes
    d.fill(inside, CLEAR)
    glass -= inside
    return inside, glass


def podium(d: Design) -> None:
    """The granite podium with a darker band, the grand stair up from the land side, the forecourt, the lit rim and a
    railing round its edge but at the stair."""
    d.box(0, PODIUM[0], P_Z[0], WIDTH - 1, PODIUM[1], P_Z[1], GRANITE)
    d.box(0, PODIUM[1] - 1, P_Z[0], WIDTH - 1, PODIUM[1] - 1, P_Z[1], GRANITE_DARK)
    d.box(0, 0, 0, WIDTH - 1, 0, P_Z[0] - 11, Mat("concrete", tint=0xB0A89C))          # the forecourt
    d.stairs(STAIR_X[0], 0, P_Z[0] - 10, 0, 1, 10, GRANITE, width=STAIR_X[1] - STAIR_X[0] + 1, fill_below=True)
    edge = solids.box(0, BASE, P_Z[0], WIDTH - 1, BASE, P_Z[1]) - solids.box(1, BASE, P_Z[0] + 1, WIDTH - 2, BASE, P_Z[1] - 1)
    fence_rail(d, {p for p in edge if not (STAIR_X[0] <= p[0] <= STAIR_X[1] and p[2] == P_Z[0])}, Mat("iron_block", tint=0xD8D8D0))
    for x in range(0, WIDTH, 6):                                                       # lamps along the rim
        for z in (P_Z[0], P_Z[1]):
            if not (STAIR_X[0] <= x <= STAIR_X[1] and z == P_Z[0]):
                d.put(x, PODIUM[1], z, WARM)
    for z in range(P_Z[0], P_Z[1] + 1, 6):
        d.put(0, PODIUM[1], z, WARM)
        d.put(WIDTH - 1, PODIUM[1], z, WARM)


def doorway(d: Design, x: int, z: int, width: int = 3) -> None:
    """Clears a doorway `width` wide and three high through a glazed mouth at (x.., z)."""
    for dx in range(width):
        for y in range(BASE, BASE + 3):
            for dz in (-1, 0, 1):
                if d.get(x + dx, y, z + dz) is not None and d.get(x + dx, y, z + dz)[1] == "glass":
                    d.clear(x + dx, y, z + dz)


def concert_hall(d: Design, cx: int, group: list[Shell]) -> None:
    """The stage with organ pipes at the back, tiers of seats rising toward the harbour, lamps on the aisles, the foyer."""
    z_stage = group[1].z_back + 4
    d.box(cx - 9, BASE, z_stage, cx + 9, BASE + 1, z_stage + 7, WOOD)                    # the stage
    for i, x in enumerate(range(cx - 8, cx + 9, 2)):                                     # the organ
        h = 6 + (8 - abs(x - cx)) // 2 + (i % 2)
        d.fill([(x, y, z_stage) for y in range(BASE + 2, BASE + 2 + h)], Mat("silver_block"), shape=sc.pack(sc.CYLINDER))
    z0 = z_stage + 10
    for k in range(9):                                                                   # the tiers
        zt, yt = z0 + 2 * k, BASE + k
        d.box(cx - 11, BASE, zt, cx + 11, yt, zt + 1, WOOD)
        for x in range(cx - 10, cx + 11):
            if abs(x - cx) > 1:
                d.put(x, yt + 1, zt + 1, SEAT, shape=sc.pack(sc.CHAIR, 2))
        for x in (cx - 1, cx, cx + 1):                                                   # the middle aisle: steps
            d.put(x, yt, zt, WOOD, shape=sc.stairs(0, 1))
        d.put(cx - 12, yt + 1, zt, WARM)
        d.put(cx + 12, yt + 1, zt, WARM)
    for x in (cx - 8, cx + 8):
        d.put(x, BASE + 2, z_stage + 6, "lantern")


def build() -> Design:
    d = earth_design("earth_sydney_opera", "Harbour Shell Hall")
    podium(d)
    concert = [Shell(32, 54, 16, 13, 24, facing=-1, lean=2), Shell(32, 46, 34, 18, 38),
               Shell(32, 66, 38, 19, 47), Shell(32, 90, 42, 19, 54)]
    theatre = [Shell(80, 60, 14, 11, 20, facing=-1, lean=2), Shell(80, 52, 30, 15, 31),
               Shell(80, 70, 34, 16, 39), Shell(80, 92, 36, 16, 45)]
    restaurant = [Shell(9, 44, 12, 7, 15, facing=-1, lean=2), Shell(9, 36, 12, 6, 11)]
    for group, cx in ((concert, 32), (theatre, 80), (restaurant, 9)):
        shells(d, group)
        front = group[0]
        doorway(d, cx - 1, front.z_mouth)
        back = group[-1]
        doorway(d, cx - 1, back.z_mouth + math.ceil(back.lean / 2) - 1)
    concert_hall(d, 32, concert)
    d.box(70, BASE, 72, 90, BASE + 1, 78, WOOD)                                         # the theatre's stage
    for x in (72, 88):
        d.put(x, BASE + 2, 75, "lantern")
    d.put(9, BASE, 41, Mat("wood_planks", shape=sc.pack(sc.TABLE)))                    # the restaurant
    d.put(6, BASE, 40, "lantern")
    d.chest(36, BASE + 2, 53, items="crystal:1,data_fragment:1-2,gold_ingot:1")
    d.chest(11, BASE, 39, table="kitchen_food")
    d.chest(84, BASE + 2, 76, table="ruin_common", chance=0.7)
    return d


if __name__ == "__main__":
    build().write()
