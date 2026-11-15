"""The LABYRINTH BUILDERS' statues (#3645): the prop templates of the civilization `labyrinth_builders`, written to
data/civilizations/labyrinth_builders/<key>.json. The labyrinth itself is procedural (the `maze_ruin` source); it only
sets `prop pool=statue` markers in its halls and dead ends and one `prop pool=statue_large` in its centre, and the
server places one of these statues there.

The Labyrinth Builders were bipeds with a BIG SQUARE HEAD — a cube as wide as their shoulders or wider — two arms and two
legs. Their statues are of the dark labyrinth stone (`maze_stone`) with engraved accents (`maze_stone_carved`: a belt, a
band across the face, the plinth's edge). They are not run through the ruin pass; their weathering is authored and gentle:
a chipped edge here and there, and one statue lost an arm, which lies at its feet.

    lb_statue_standing       statue        8 high, standing, its left arm broken off and lying at its feet
    lb_statue_pointing       statue        8 high, pointing ahead with its right arm
    lb_statue_arms_raised    statue        8 high, both arms raised in a V
    lb_statue_sitting        statue        7 high, sitting on the ground, legs stretched out, hands on its knees
    lb_statue_kneeling       statue        6 high (a smaller figure), kneeling on both knees
    lb_statue_large          statue_large  18 high, standing on a low plinth, for the maze's centre
    lb_statue_large_raised   statue_large  18 high, both arms raised, on a low plinth

Every statue faces -Z (the server turns a prop by a hash of its cell). Run:

    uv run --no-project python tools/landmarks/civ_labyrinth_statues.py [--preview] [--out FOLDER]
    uv run --no-project python tools/gen_landmarks.py civ_labyrinth_statues   # the collective call does the same
"""
from __future__ import annotations

import sys
from typing import NamedTuple

import civ
import solids
from civ import CivDesign, Mat, expect, sc
from geom import Design

CIV = "labyrinth_builders"
STONE = Mat("maze_stone")
CARVED = Mat("maze_stone_carved")
SLAB = sc.pack(sc.SLAB)

NAMES: dict[str, tuple[str, str]] = {
    "lb_statue_standing": ("Standing Statue", "Stehende Statue"),
    "lb_statue_pointing": ("Pointing Statue", "Zeigende Statue"),
    "lb_statue_arms_raised": ("Statue with Raised Arms", "Statue mit erhobenen Armen"),
    "lb_statue_sitting": ("Sitting Statue", "Sitzende Statue"),
    "lb_statue_kneeling": ("Kneeling Statue", "Kniende Statue"),
    "lb_statue_large": ("Great Statue", "Große Statue"),
    "lb_statue_large_raised": ("Great Statue with Raised Arms", "Große Statue mit erhobenen Armen"),
}


class Build(NamedTuple):
    """A figure's proportions in blocks: leg width, the gap between the legs, torso depth, arm width, head size (a cube),
    leg height, torso height. The torso is 2 * lw + gw wide, the head is centred on it (as wide or wider)."""
    lw: int
    gw: int
    td: int
    aw: int
    hs: int
    lh: int
    th: int

    @property
    def tw(self) -> int:
        return 2 * self.lw + self.gw


NORMAL = Build(lw=1, gw=2, td=2, aw=1, hs=4, lh=2, th=2)   # torso 4 wide, head 4 x 4 x 4: 8 high standing
SMALL = Build(lw=1, gw=1, td=1, aw=1, hs=3, lh=1, th=1)    # torso 3 wide, head 3 x 3 x 3
LARGE = Build(lw=2, gw=2, td=4, aw=2, hs=8, lh=4, th=4)    # torso 6 wide, head 8 x 8 x 8: 16 high standing


class Figure:
    """A figure under construction in its own frame: x across (its torso from 0 to tw - 1), y up from `y0`, z forward
    is -Z (the torso from 0 to td - 1). Every part method fills boxes; `head`, `torso`, `legs`, `arm` know the build."""

    def __init__(self, b: Build, y0: int = 0) -> None:
        self.b = b
        self.y0 = y0
        self.d = Design.part()

    def box(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, m=STONE) -> set:
        return self.d.fill(solids.box(x0, self.y0 + y0, z0, x1, self.y0 + y1, z1), m)

    def leg_xs(self) -> list[tuple[int, int]]:
        b = self.b
        return [(0, b.lw - 1), (b.tw - b.lw, b.tw - 1)]

    def arm_x(self, side: int) -> tuple[int, int]:
        """The x span of the left (-1, low x) or right (+1) arm."""
        b = self.b
        return (-b.aw, -1) if side < 0 else (b.tw, b.tw + b.aw - 1)

    def torso(self, y: int) -> int:
        """The torso from row y, its lowest row carved (a belt); returns the shoulder row."""
        b = self.b
        self.box(0, y, 0, b.tw - 1, y + b.th - 1, b.td - 1)
        if b.th > 1:
            self.box(0, y, 0, b.tw - 1, y, b.td - 1, CARVED)
        return y + b.th - 1

    def head(self, y: int, dz: int = 0) -> tuple[int, int, int]:
        """The cube head from row y (moved forward by -dz when bowed), a carved band across its face at eye height;
        returns its low corner."""
        b = self.b
        x0, z0 = (b.tw - b.hs) // 2, (b.td - b.hs) // 2 + dz
        self.box(x0, y, z0, x0 + b.hs - 1, y + b.hs - 1, z0 + b.hs - 1)
        eye = y + (b.hs * 5) // 8
        if b.hs >= 6:  # a big head: two deep eye sockets and a carved band under them
            for ex in (x0 + 1, x0 + b.hs - 3):
                self.box(ex, eye, z0, ex + 1, eye, z0, CARVED)
                self.d.clear(ex, self.y0 + eye, z0)
                self.d.clear(ex + 1, self.y0 + eye, z0)
                self.box(ex, eye, z0 + 1, ex + 1, eye, z0 + 1, CARVED)
            self.box(x0, eye - 2, z0, x0 + b.hs - 1, eye - 2, z0, CARVED)
        else:
            self.box(x0, eye, z0, x0 + b.hs - 1, eye, z0, CARVED)    # a carved visor across the face
        return (x0, y, z0)

    def standing_legs(self, toes: bool = True) -> None:
        b = self.b
        for x0, x1 in self.leg_xs():
            self.box(x0, 0, 0, x1, b.lh - 1, b.td - 1)
            if toes:
                self.box(x0, 0, -1, x1, 0, -1, STONE.shaped(SLAB))    # the feet stick out in front

    def hanging_arm(self, side: int, shoulder: int) -> set:
        b = self.b
        x0, x1 = self.arm_x(side)
        return self.box(x0, b.lh - b.lh // 2, 0, x1, shoulder, b.td - 1)


def statue(key: str, pool: str) -> CivDesign:
    """A statue prop: never ruined, open to players, standing on the ground (row 0)."""
    return CivDesign(key, NAMES[key][0], civilization=CIV, pool="prop", propPool=pool, ruin="never",
                     protection="open", groundRow=0)


def finish(key: str, pool: str, f: Figure) -> CivDesign:
    d = statue(key, pool)
    d.stamp(f.d, 0, 0, 0)
    return d


# --- the five statues of the halls ------------------------------------------------------------------------------------------
def lb_statue_standing() -> CivDesign:
    """Standing, arms at its sides — the left one broken off below the shoulder; the piece lies at its feet."""
    b = NORMAL
    f = Figure(b)
    f.standing_legs()
    sh = f.torso(b.lh)
    f.hanging_arm(+1, sh)
    lx0, lx1 = f.arm_x(-1)
    f.box(lx0, sh, 0, lx1, sh, b.td - 1)                               # the stump
    f.box(lx0 - 1, 0, -3, lx0, 0, -3)                                  # the broken arm, lying on the ground
    f.box(lx0 + 1, 0, -3, lx0 + 1, 0, -3, STONE.shaped(SLAB))          # (its hand)
    hx, hy, hz = f.head(sh + 1)
    f.d.clear(hx, hy + b.hs - 1, hz)                                   # a chipped corner of the head
    return finish("lb_statue_standing", "statue", f)


def lb_statue_pointing() -> CivDesign:
    """Standing, its right arm stretched out ahead, pointing the way."""
    b = NORMAL
    f = Figure(b)
    f.standing_legs()
    sh = f.torso(b.lh)
    f.hanging_arm(-1, sh)
    rx0, rx1 = f.arm_x(+1)
    f.box(rx0, sh, -3, rx1, sh, b.td - 1)                              # the arm, level with the shoulder
    f.box(rx0, sh, -4, rx1, sh, -4, STONE.shaped(sc.beam("z")))        # a pointing finger
    hx, hy, hz = f.head(sh + 1)
    f.d.put(hx + b.hs - 1, hy + b.hs - 1, hz + b.hs - 1, STONE.shaped(sc.stairs(0, -1)))   # a chipped edge
    return finish("lb_statue_pointing", "statue", f)


def raised_arms(f: Figure, sh: int, reach: int) -> set:
    """Both arms raised in a V, `reach` rows above the shoulders: the shoulder beside the torso's top, the upper arm one
    arm's width further out (clear of the head), the forearm further out still; the parts overlap a row so they hold
    together face to face. Returns the cells of the right hand's top row."""
    b = f.b
    step = max(1, b.aw // 2)
    half = reach // 2
    top = set()
    for side in (-1, 1):
        x0, x1 = f.arm_x(side)
        f.box(x0, sh - b.aw + 1, 0, x1, sh, b.td - 1)                                  # the shoulder
        u0, u1 = x0 + side * b.aw, x1 + side * b.aw
        f.box(u0, sh - b.aw + 1, 0, u1, sh + half, b.td - 1)                           # the upper arm
        f0, f1 = u0 + side * step, u1 + side * step
        cells = f.box(f0, sh + half, 0, f1, sh + reach, b.td - 1)                      # the forearm
        if side > 0:
            top = {c for c in cells if c[1] == f.y0 + sh + reach}
    return top


def lb_statue_arms_raised() -> CivDesign:
    """Standing, both arms raised in a V beside its head; one hand chipped."""
    b = NORMAL
    f = Figure(b)
    f.standing_legs()
    sh = f.torso(b.lh)
    hand = raised_arms(f, sh, b.hs)
    f.head(sh + 1)
    f.d.restyle([min(hand)], shape=SLAB)                               # the chipped hand
    return finish("lb_statue_arms_raised", "statue", f)


def lb_statue_sitting() -> CivDesign:
    """Sitting on the ground, legs stretched out ahead, feet up, hands resting on its knees."""
    b = NORMAL
    f = Figure(b)
    f.box(0, 0, 0, b.tw - 1, 0, b.td - 1)                              # the hips on the ground
    sh = f.torso(1)
    for x0, x1 in f.leg_xs():
        f.box(x0, 0, -4, x1, 0, -1)                                    # the legs, stretched out
        f.box(x0, 1, -4, x1, 1, -4, STONE.shaped(sc.stairs(0, 1)))     # the feet, toes up
    for side in (-1, 1):
        x0, x1 = f.arm_x(side)
        f.box(x0, sh, 0, x1, sh, b.td - 1)                             # the upper arm ...
        f.box(x0, 1, -2, x1, sh - 1, 0)                                # ... down to the hand on the knee
    hx, hy, hz = f.head(sh + 1)
    f.d.clear(hx + b.hs - 1, hy + b.hs - 1, hz)                        # a chipped corner
    return finish("lb_statue_sitting", "statue", f)


def lb_statue_kneeling() -> CivDesign:
    """A smaller figure kneeling on both knees, sitting back on its heels, hands on its thighs."""
    b = SMALL
    f = Figure(b)
    for x0, x1 in f.leg_xs():
        f.box(x0, 0, -1, x1, 0, 2)                                     # the shins on the ground, knees in front
        f.box(x0, 0, 3, x1, 0, 3, STONE.shaped(SLAB))                  # the feet behind
    f.box(0, 1, -1, b.tw - 1, 1, b.td, CARVED)                         # the thighs and hips, folded on the heels
    sh = f.torso(2)
    for side in (-1, 1):
        x0, x1 = f.arm_x(side)
        f.box(x0, 1, -1, x1, sh, 0)                                    # arms down, hands on the thighs
    hx, hy, hz = f.head(sh + 1)
    f.d.put(hx, hy + b.hs - 1, hz, STONE.shaped(SLAB))                 # a chipped edge
    return finish("lb_statue_kneeling", "statue", f)


# --- the two great statues for the maze's centre ---------------------------------------------------------------------
def plinth(f: Figure, margin: int, chip: bool) -> None:
    """A low plinth (two rows) under the figure, its top edge carved; with `chip` one corner is broken."""
    b = f.b
    pts = list(f.d.plain)
    xs = [p[0] for p in pts]
    zs = [p[2] for p in pts]
    x0, x1, z0, z1 = min(xs) - margin, max(xs) + margin, min(zs) - margin, max(zs) + margin
    f.d.fill(solids.box(x0, 0, z0, x1, 1, z1), STONE)
    f.d.fill(solids.box_walls(x0, 1, z0, x1, 1, z1), CARVED)
    if chip:
        f.d.clear(x0, 1, z0)
        f.d.put(x0 + 1, 1, z0, STONE.shaped(sc.stairs(1, 0)))
    del b


def lb_statue_large() -> CivDesign:
    """Eighteen high: a great figure standing on a low plinth, arms at its sides, its head a cube of eight."""
    b = LARGE
    f = Figure(b, y0=2)
    f.standing_legs()
    sh = f.torso(b.lh)
    f.hanging_arm(-1, sh)
    f.hanging_arm(+1, sh)
    hx, hy, hz = f.head(sh + 1)
    f.d.put(hx + b.hs - 1, f.y0 + hy + b.hs - 1, hz, STONE.shaped(sc.stairs(-1, 0)))   # a chipped edge
    plinth(f, 2, chip=True)
    return finish("lb_statue_large", "statue_large", f)


def lb_statue_large_raised() -> CivDesign:
    """Eighteen high: a great figure on a low plinth, both arms raised to the top of its head."""
    b = LARGE
    f = Figure(b, y0=2)
    f.standing_legs()
    sh = f.torso(b.lh)
    raised_arms(f, sh, b.hs)
    f.head(sh + 1)
    plinth(f, 2, chip=False)
    return finish("lb_statue_large_raised", "statue_large", f)


# --- the collective call -----------------------------------------------------------------------------------------------
def build() -> list[CivDesign]:
    """Every Labyrinth Builders statue, each checked against its size and pool."""
    out = []
    for make, pool, hi, foot in ((lb_statue_standing, "statue", (7, 8), (5, 8)),
                                 (lb_statue_pointing, "statue", (7, 8), (5, 9)),
                                 (lb_statue_arms_raised, "statue", (7, 8), (5, 10)),
                                 (lb_statue_sitting, "statue", (5, 7), (5, 8)),
                                 (lb_statue_kneeling, "statue", (4, 6), (4, 7)),
                                 (lb_statue_large, "statue_large", (14, 18), (10, 18)),
                                 (lb_statue_large_raised, "statue_large", (14, 18), (10, 22))):
        d = make()
        print("  " + expect(d, foot, (hi[0] - 1, hi[1] - 1), (0, 0), ("maze_stone", "maze_stone_carved"), {}))
        assert d.header["propPool"] == pool and not d.markers
        out.append(d)
    keys = [d.header["key"] for d in out]
    assert len(set(keys)) == len(keys) and all(k.startswith("lb_") for k in keys)
    return out


def main(argv: list[str]) -> int:
    import argparse

    import preview
    ap = argparse.ArgumentParser(description="Build the Labyrinth Builders' statues.")
    ap.add_argument("--out", help="folder (default data/civilizations/labyrinth_builders)")
    ap.add_argument("--preview", action="store_true", help="also render artifacts/civ-previews/<key>.png")
    args = ap.parse_args(argv)
    civ.seed_preview_colours(preview)
    for d in build():
        path = d.write(args.out)
        if args.preview:
            print(f"  preview -> {preview.preview(path, None, scale=16, iso_scale=16)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
