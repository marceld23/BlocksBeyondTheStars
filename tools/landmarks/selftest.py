#!/usr/bin/env python3
"""Self-check of the landmark toolkit (no pytest needed):

    uv run --no-project python tools/landmarks/selftest.py

Builds every primitive and checks cell counts, shapes and orientations; checks that the shape codes and the block font
still mirror their C# originals (BlockShape.cs, MonumentFont.cs); regenerates the three shipped samples and compares
them byte for byte with data/landmarks; renders a preview PNG and reads it back. Exit code 1 on any failure.
"""
from __future__ import annotations

import importlib
import itertools
import math
import re
import struct
import sys
import tempfile
import traceback
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
blockfont = importlib.import_module("blockfont")
sc = importlib.import_module("shapecode")
solids = importlib.import_module("solids")
geom = importlib.import_module("geom")
preview = importlib.import_module("preview")
templatefile = importlib.import_module("templatefile")
Design, Mat, Mix, CLEAR, CAVITY = geom.Design, geom.Mat, geom.Mix, geom.CLEAR, geom.CAVITY
REPO = templatefile.REPO

TESTS = []


def test(fn):
    TESTS.append(fn)
    return fn


def eq(a, b, what: str = "") -> None:
    if a != b:
        raise AssertionError(f"{what}: expected {b!r}, got {a!r}")


def ok(cond: bool, what: str) -> None:
    if not cond:
        raise AssertionError(what)


def flood(cells: set, start, limit, neighbours) -> set:
    """Cells NOT in `cells` reachable from `start` inside the box `limit` (lo, hi tuples)."""
    lo, hi = limit
    seen = {start}
    todo = [start]
    while todo:
        p = todo.pop()
        for d in neighbours:
            q = tuple(p[i] + d[i] for i in range(len(p)))
            if q in seen or q in cells or any(q[i] < lo[i] or q[i] > hi[i] for i in range(len(q))):
                continue
            seen.add(q)
            todo.append(q)
    return seen


# --- the C# mirrors -----------------------------------------------------------------------------------------------------
@test
def shape_enum_matches_csharp():
    text = (REPO / "src/BlocksBeyondTheStars.Shared/World/BlockShape.cs").read_text(encoding="utf-8")
    body = text[text.index("public enum BlockShape"):]
    body = body[:body.index("}")]
    found = {re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower(): int(v) for name, v in re.findall(r"(\w+)\s*=\s*(\d+),", body)}
    eq(found, sc.SHAPES, "BlockShape enum")
    ok("((upFace & 0x7) << 8) | ((shape & 0x3F) << 2) | (yaw & 0x3)" in text, "ShapeCode.Pack bit layout changed")
    for yaw, d in ((1, "(-1, 0)"), (2, "(0, -1)"), (3, "(1, 0)")):
        ok(f"{yaw} => {d}," in text, f"YawDirection({yaw}) changed")
        eq(sc.yaw_direction(yaw), tuple(int(v) for v in d.strip("()").split(",")), f"yaw_direction({yaw})")
    eq(sc.yaw_direction(0), (0, 1), "yaw_direction(0)")
    for face, d in ((1, (0, -1, 0)), (2, (1, 0, 0)), (3, (-1, 0, 0)), (4, (0, 0, 1)), (5, (0, 0, -1))):
        ok(f"{face} => ({d[0]}, {d[1]}, {d[2]})," in text, f"FaceDirection({face}) changed")
        eq(sc.face_direction(face), d, f"face_direction({face})")


@test
def pack_and_unpack():
    eq(sc.pack(sc.RAMP, 3, 1), (1 << 8) | (5 << 2) | 3, "pack(ramp, 3, -Y)")
    eq(sc.stalactite(), (sc.UP_NY << 8) | (sc.CONE << 2), "stalactite = dripstone cone with its up face turned down")
    for shape in sc.SHAPES.values():
        for yaw in range(4):
            for up in range(6):
                d = sc.pack(shape, yaw, up)
                eq((sc.shape_of(d), sc.yaw_of(d), sc.up_of(d)), (shape, yaw, up), "unpack")
    eq(sc.pack("ramp", 2), sc.pack(sc.RAMP, 2), "pack by name")
    for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        eq(sc.yaw_direction(sc.yaw_toward(dx, dz)), (dx, dz), "yaw_toward is the inverse of yaw_direction")


@test
def orientation_matrices():
    mats = {sc.matrix(y, u) for y in range(4) for u in range(6)}
    eq(len(mats), 24, "distinct orientations")
    for yaw in range(4):
        m = sc.matrix(yaw, 0)
        eq((m[0][2], m[2][2]), sc.yaw_direction(yaw), f"yaw {yaw} sends local +Z to yaw_direction")
    for up in range(6):
        m = sc.matrix(0, up)
        eq((m[0][1], m[1][1], m[2][1]), sc.face_direction(up), f"up-face {up} sends local +Y there")
    for yaw in range(4):
        for t in range(4):
            eq(sc.rotate_descriptor(sc.pack(sc.RAMP, yaw), t), sc.pack(sc.RAMP, (yaw + t) % 4), "turning a ramp turns its yaw")
    eq(sc.mirror_descriptor(sc.ramp(1, 0), "x"), sc.ramp(-1, 0), "mirror x flips a ramp rising +x")
    eq(sc.mirror_descriptor(sc.ramp(0, 1), "x"), sc.ramp(0, 1), "mirror x keeps a ramp rising +z")
    eq(sc.mirror_descriptor(sc.ramp(0, 1), "z"), sc.ramp(0, -1), "mirror z flips a ramp rising +z")
    hung = sc.mirror_descriptor(sc.pack(sc.CONE), "y")
    eq((sc.shape_of(hung), sc.up_of(hung)), (sc.CONE, sc.UP_NY), "mirror y hangs a cone")
    eq(sc.occupancy(hung), sc.occupancy(sc.stalactite()), "... the same form as a stalactite")
    eq(sc.rotate_descriptor(sc.stalactite(), 1) & 0x700, sc.UP_NY << 8, "a turned stalactite still hangs")


@test
def occupancy_orientation():
    def count(desc, axis, value):
        return sum(1 for c in sc.occupancy(desc) if c[axis] == value)
    ok(count(sc.ramp(0, 1), 2, 3) > count(sc.ramp(0, 1), 2, 0), "a ramp is high at its +z side")
    ok(count(sc.ramp(1, 0), 0, 3) > count(sc.ramp(1, 0), 0, 0), "ramp(1, 0) is high at its +x side")
    ok(count(sc.ramp(-1, 0), 0, 0) > count(sc.ramp(-1, 0), 0, 3), "ramp(-1, 0) is high at its -x side")
    ok(count(sc.stairs(0, -1), 2, 0) > count(sc.stairs(0, -1), 2, 3), "stairs(0, -1) climb toward -z")
    ok(count(sc.stalactite(), 1, 3) > count(sc.stalactite(), 1, 0), "a stalactite is wide at the top")
    ok(count(sc.pack(sc.CONE), 1, 0) > count(sc.pack(sc.CONE), 1, 3), "a cone is wide at the bottom")
    down = sc.ramp_down(0, 1)
    eq(count(down, 1, 3), 16, "a hung ramp covers the ceiling")
    ok(count(down, 2, 3) > count(down, 2, 0), "ramp_down(0, 1) is deep at its +z side")
    for axis, desc in ((0, sc.beam("x")), (2, sc.beam("z")), (1, sc.beam("y")), (1, sc.post())):
        eq({c[axis] for c in sc.occupancy(desc)}, {0, 1, 2, 3}, f"bar along axis {axis}")
        eq(len(sc.occupancy(desc)), 16, "a slim bar is 2 x 2 x 4 sub-cells")


@test
def font_matches_csharp():
    text = (REPO / "src/BlocksBeyondTheStars.WorldGeneration/MonumentFont.cs").read_text(encoding="utf-8")
    found = {}
    for ch, rows in re.findall(r"\['(.)'\] = R\(([^)]*)\)", text):
        found[ch] = tuple(int(v.strip(), 0) for v in rows.split(","))
    eq(found, blockfont.GLYPHS, "MonumentFont glyphs")
    ok(not set(blockfont.EXTRA) & set(blockfont.GLYPHS), "EXTRA only adds characters")
    eq(blockfont.line_width("BLOCKS KING"), 65, "line width")


# --- solids --------------------------------------------------------------------------------------------------------------
@test
def discs_and_cylinders():
    cx, r = solids.span(0, 24)
    eq((cx, r), (11.5, 12.0), "span")
    d = solids.disc(cx, cx, r)
    eq((min(x for x, _ in d), max(x for x, _ in d)), (0, 23), "a 24-wide disc")
    d7 = solids.disc(3, 3, 3.5)
    eq((min(x for x, _ in d7), max(x for x, _ in d7)), (0, 6), "a 7-wide disc")
    eq(len(solids.cylinder(3, 3, 0, 9, 3.5)), 10 * len(d7), "cylinder = disc x rows")
    tube = solids.cylinder(11.5, 11.5, 0, 0, 12, wall=2)
    flat = {(x, z) for x, _, z in tube}
    inside = flood(flat, (11, 11), ((-1, -1), (24, 24)), ((1, 0), (-1, 0), (0, 1), (0, -1)))
    ok((-1, -1) not in inside, "a ring wall is closed (no face path from the middle to the outside)")
    for a in range(0, 360, 7):  # every ray from the centre crosses at least `wall` cells of the wall
        hits = {(round(11.5 + t * math.cos(math.radians(a))), round(11.5 + t * math.sin(math.radians(a))))
                for t in [i / 4 for i in range(60)]} & flat
        ok(len(hits) >= 2, f"wall thickness on ray {a}")
    ell = solids.ring2d(50, 40, 40, 3, rz=30)
    eq((min(x for x, _ in ell), max(x for x, _ in ell), min(z for _, z in ell), max(z for _, z in ell)), (10, 90, 10, 70), "elliptic ring")
    along = solids.cylinder_along("x", 5, 5, 0, 9, 2.5)
    eq({p[0] for p in along}, set(range(10)), "cylinder along x")


@test
def spheres_domes_cones():
    eq(len(solids.sphere(0, 0, 0, 5)), 515, "lattice points in a radius-5 ball")
    ball = solids.sphere(0, 0, 0, 6)
    hull = solids.sphere(0, 0, 0, 6, wall=1)
    ok(hull < ball and (0, 0, 0) not in hull, "a hollow sphere")
    inside = flood(hull, (0, 0, 0), ((-8, -8, -8), (8, 8, 8)), solids.N6)
    ok((-8, -8, -8) not in inside, "the hollow sphere is closed")
    dome = solids.dome(0, 0, 10, 5)
    eq((min(p[1] for p in dome), max(p[1] for p in dome)), (10, 15), "a dome stands on its row and reaches row y0 + ry")
    shell = solids.dome(0, 0, 10, 8, wall=1)
    eq({p for p in shell if p[1] == 10} - solids.extrude(solids.ring2d(0, 0, 8, 1), 10, 10), set(), "a hollow dome has no floor")
    cone = solids.cone(0, 0, 0, 9, 5)
    eq(len([p for p in cone if p[1] == 9]), 1, "a cone ends in one cell")
    eq(len([p for p in cone if p[1] == 0]), len(solids.disc(0, 0, 5)), "cone base")
    down = solids.cone(0, 0, 9, 0, 5)
    eq(len([p for p in down if p[1] == 0]), 1, "an upside-down cone points down")
    cap = solids.capsule((0, 0, 0), (10, 0, 0), 2)
    ok((0, 0, 0) in cap and (10, 0, 0) in cap and (5, 2, 0) in cap and (5, 3, 0) not in cap, "capsule")


@test
def lines_and_walks():
    ln = solids.line((0, 0, 0), (10, 4, -3))
    eq((ln[0], ln[-1], len(ln)), ((0, 0, 0), (10, 4, -3), 11), "Bresenham ends and length")
    ok(all(max(abs(a[i] - b[i]) for i in range(3)) == 1 for a, b in itertools.pairwise(ln)), "26-connected")
    w = solids.walk((0, 0, 0), (10, 4, -3))
    eq((w[0], w[-1], len(w)), ((0, 0, 0), (10, 4, -3), 18), "walk ends and length")
    ok(all(sum(abs(a[i] - b[i]) for i in range(3)) == 1 for a, b in itertools.pairwise(w)), "face-connected")
    l18 = geom._line18((0, 0, 0), (7, 7, 7))
    ok(all(sum(abs(a[i] - b[i]) for i in range(3)) <= 2 for a, b in itertools.pairwise(l18)), "edge-connected")


@test
def arches():
    eq([sum(1 for u, v in solids.arch_profile(4, 6) if v == row) for row in range(6)], [4, 4, 4, 4, 4, 2], "round 4 x 6")
    eq(len(solids.arch_profile(5, 7)), 33, "round 5 x 7")
    eq(len(solids.arch_profile(3, 4, "flat")), 12, "flat")
    pointed = solids.arch_profile(6, 10, "pointed")
    top = max(v for _, v in pointed)
    eq(sorted(u for u, v in pointed if v == top), [2, 3], "a pointed arch meets in the middle")
    eq({(5 - u, v) for u, v in pointed}, pointed, "symmetric")
    seg = solids.arch_profile(8, 6, "segmental", rise=2)
    eq(sum(1 for _, v in seg if v == 3), 8, "segmental: full width up to the spring line")
    eq(len(solids.arch("z", 0, 0, 0, 2, 4, 6)), 3 * 22, "an arch through a wall 3 thick")
    eq(len(solids.arcade("x", 0, 0, 0, 0, 3, 4, 6, 2)), 3 * 22, "an arcade of three")
    e = solids.Ellipse(0, 0, 10, 10)
    ok(abs(e.perimeter - 2 * math.pi * 10) < 0.05, "ellipse perimeter")
    a = e.arc(*e.point(17.0))
    ok(abs(a - 17.0) < 0.05, "arc(point(s)) = s")
    wall = solids.cylinder(0, 0, 0, 7, 20.5, wall=2)
    holes = solids.ring_openings(wall, solids.Ellipse(0, 0, 19.5, 19.5), 0, 8, 3, 6)
    ok(0 < len(holes) < len(wall), "ring openings")
    eq({(-x, y, z) for x, y, z in holes} == holes, True, "eight openings are mirror symmetric")


@test
def pyramids_and_shapes():
    eq([(xb - xa + 1) for _, xa, _, xb, _ in solids.pyramid_layers(0, 0, 0, 9)], [9, 7, 5, 3, 1], "layers")
    eq(len(solids.pyramid(0, 0, 0, 9)), 81 + 49 + 25 + 9 + 1, "pyramid cells")
    d = Design("t", "t")
    d.pyramid(0, 0, 0, 9, "sandstone", smooth=True)
    eq(d.get(0, 0, 0)[4], sc.pack(sc.PYRAMID), "corner")
    eq(d.get(4, 0, 0)[4], sc.ramp(0, 1), "south rim rises inward")
    eq(d.get(8, 0, 4)[4], sc.ramp(-1, 0), "east rim rises inward")
    eq(d.get(4, 4, 4)[4], sc.pack(sc.PYRAMID), "apex")
    eq(d.get(4, 0, 4)[4], 0, "inside stays cube")
    d2 = Design("t", "t")
    d2.pyramid(0, 0, 0, 21, "sandstone", top=5, wall=2)
    eq(d2.get(10, 3, 10), None, "a hollow pyramid")
    ok(d2.is_block(10, 8, 10), "its top platform is solid")


@test
def stairs_straight_and_spiral():
    d = Design("t", "t")
    end = d.stairs(0, 0, 0, 0, 1, 5, "stone", width=2)
    eq(end, (0, 5, 5), "landing")
    eq(len(d.plain), 10, "steps x width")
    eq(d.get(1, 3, 3)[4], sc.stairs(0, 1), "each step is the stairs form")
    d.stairs(10, 0, 0, -1, 0, 4, "stone", fill_below=True, shaped=False)
    eq(d.get(7, 0, 0)[1], "stone", "filled below")
    s = Design("t", "t")
    s.box(0, 8, 0, 12, 8, 12, "planks")  # a floor the stairwell must cut
    treads = s.spiral_stair(6, 6, 0, 31, 1.5, 5.5, "stone", steps_per_turn=16, newel="stone")
    ring = solids.disc(6, 6, 5.5) - solids.disc(6, 6, 1.5)
    for turn in range(2):
        cells = [(x, z) for x, y, z in treads if turn * 16 <= y < (turn + 1) * 16]
        eq(sorted(cells), sorted(ring), f"turn {turn} covers the ring once")
    ok(any(s.get(*p)[4] == 0 for p in treads) and any(s.get(*p)[4] for p in treads), "first cells are stairs forms")
    # climbable: from the first step every tread is reached by walking to a face neighbour at most one row up or down
    reached = {p for p in treads if p[1] == 0}
    todo = list(reached)
    while todo:
        x, y, z = todo.pop()
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for dy in (-1, 0, 1):
                q = (x + dx, y + dy, z + dz)
                if q in treads and q not in reached:
                    reached.add(q)
                    todo.append(q)
    eq(reached, set(treads), "every tread is reached from the bottom step")
    holes = [(x, z) for x, z in ring if not s.is_block(x, 8, z)]
    ok(holes, "the floor got its stairwell hole")
    ok(s.is_block(6, 20, 6), "newel")


@test
def truss_and_cables():
    d = Design("t", "t")
    cells = d.truss((10, 0, 10), (10, 40, 10), "iron_block", size=5, panel=8)
    for cx, cz in ((8, 8), (12, 8), (12, 12), (8, 12)):
        ok((cx, 0, cz) in cells and (cx, 40, cz) in cells, "chord ends")
    shapes = {d.get(*p)[4] for p in cells}
    ok(sc.post() in shapes and sc.beam("x") in shapes and sc.beam("z") in shapes, "posts and beams")
    leg = d.truss((0, 0, 0), (20, 60, 20), "iron_block", size=3, panel=6)
    ok(len(leg) > 100, "an inclined leg")
    c = Design("t", "t")
    path = c.cable((0, 50, 0), (100, 50, 0), 30, "iron_block")
    eq(min(p[1] for p in path), 20, "parabola sags 30")
    eq((path[0], path[-1]), ((0, 50, 0), (100, 50, 0)), "cable ends")
    ok(all(sum(abs(a[i] - b[i]) for i in range(3)) == 1 for a, b in itertools.pairwise(path)), "auto cable is face-connected")
    cat = solids.cable((0, 50, 0), (100, 50, 0), 30, "catenary")
    ok(abs(min(p[1] for p in cat) - 20) <= 1, "catenary sags 30")
    hang = c.hangers((0, 50, 0), (100, 50, 0), 30, 10, "iron_block", every=10, skip=10)
    eq(sorted({p[0] for p in hang}), list(range(10, 91, 10)), "hangers every 10, none at the towers")
    eq(min(p[1] for p in hang), 11, "hangers stop above the deck")
    ok(all(c.get(*p)[4] == sc.post() for p in hang), "hangers are posts")


@test
def text_signs():
    d = Design("t", "t")
    cells = d.text("BLOCKS KING", 0, 10, 0, "gold_block", dir="+x", scale=2, depth=1)
    w, h = Design.text_size("BLOCKS KING", 2)
    eq((w, h), (130, 14), "text size")
    eq(len(cells), len(blockfont.ink("BLOCKS KING")) * 4, "ink cells")
    eq((min(p[0] for p in cells), max(p[0] for p in cells), min(p[1] for p in cells), max(p[1] for p in cells)), (0, 129, 10, 23), "box")
    ok({p[2] for p in cells} == {0}, "one cell deep")
    side = d.text("A", 50, 0, 50, "stone", dir="-z", depth=2)
    eq({p[0] for p in side}, {50, 51}, "read from -X, depth toward +X")
    ok(max(p[2] for p in side) == 50 and min(p[2] for p in side) == 46, "advances toward -z")


@test
def transforms():
    d = Design("t", "t", groundRow=2)
    d.put(1, 0, 0, "stone", shape=sc.ramp(1, 0))
    d.chest(1, 1, 0, table="ruin_common")
    d.mirror("x", 0)
    eq(d.get(-1, 0, 0)[4], sc.ramp(-1, 0), "mirrored ramp")
    eq(len(d.markers), 2, "mirrored marker")
    d.rotate(1, 0, 0, select={(1, 0, 0)}, copy=True)
    eq(d.get(0, 0, 1)[4], sc.ramp(0, 1), "one turn takes +X to +Z")
    d.ruin_zone(-1, 0, 0, 1, 1, 1, "lattice", 0.4)
    d.translate(1, 3, 0)
    eq(d.header["groundRow"], 5, "ground row moves with the design")
    eq(d.header["ruinZones"][0]["x0"], 0, "zones move with the design")
    lean = Design("t", "t")
    lean.box(0, 0, 0, 0, 80, 0, "stone")
    lean.lean(8, 0, 0, 80, storey=10)
    eq([min(x for x, y, _ in lean.plain if y == row) for row in (0, 9, 10, 45, 80)], [0, 0, 1, 4, 8], "leaning storeys")
    part = Design.part()
    part.put(0, 0, 0, "stone")
    part.put(3, 0, 0, "stone", shape=sc.ramp(1, 0))
    big = Design("t", "t")
    got = big.stamp(part, 10, 0, 10, turns=1)
    eq(sorted(got), [(10, 0, 10), (10, 0, 13)], "stamped turned, low corner on (10, 0, 10)")
    eq(big.get(10, 0, 13)[4], sc.ramp(0, 1), "stamped shape turned along")
    sh = Design("t", "t")
    sh.box(0, 0, 0, 2, 5, 0, "stone")
    sh.shear(lambda y: (y // 2, 0), y0=2)
    eq(min(x for x, y, _ in sh.plain if y == 5), 2, "shear")
    eq(min(x for x, y, _ in sh.plain if y == 1), 0, "rows below y0 stay")


@test
def materials_and_where():
    d = Design("t", "t")
    mix = Mix("stone", ("sandstone", 3), seed=7)
    d.fill(solids.box(0, 0, 0, 19, 0, 19), mix)
    n = sum(1 for v in d.plain.values() if v[1] == "sandstone")
    ok(250 < n < 350, f"weighted mix ({n} of 400)")
    d2 = Design("t", "t")
    d2.fill(solids.box(0, 0, 0, 19, 0, 19), mix)
    eq(d.plain, d2.plain, "a mix is deterministic")
    d.fill(solids.box(0, 0, 0, 3, 3, 0), Mat("glass", tint=0x112233), where="blocks")
    eq(len([v for v in d.plain.values() if v[1] == "glass"]), 4, "where='blocks' only swaps existing cells")
    d.fill([(0, 5, 0)], lambda x, y, z: Mat("lamp", glow=0xFFAA00) if y > 3 else None)
    eq(d.get(0, 5, 0), ("block", "lamp", 0, 0xFFAA00, 0, False), "callable material")
    d.fill([(0, 0, 0)], CLEAR)
    eq(d.get(0, 0, 0), None, "CLEAR")
    d.put(0, -3, 0, CAVITY)
    eq(d.get(0, -3, 0)[0], "cavity", "CAVITY")
    eq(geom.Palette(wall=Mat("x")).wall, Mat("x"), "palette attribute")
    eq(geom.rgb(0xC0, 0x36, 0x2C), geom.hexcolor("#c0362c"), "colours")


@test
def underground():
    d = Design("t", "t", groundRow=30)
    tunnel = d.tunnel([(5, 5, 5), (30, 5, 5), (30, 5, 25)], width=3, height=3, floor="stone", frame="wood_log", every=4,
                      track="mine_track", light="torch", light_every=8)
    ok(len(tunnel) > 100, "tunnel carved")
    eq(d.get(10, 5, 5)[1], "mine_track", "track on the walkway")
    eq(d.get(13, 7, 5)[1], "wood_log", "a frame cap every four cells")
    eq(d.get(14, 6, 5)[0], "cavity", "headroom between frames")
    eq(d.get(14, 4, 5)[1], "stone", "floor")
    cave = d.cavern(60, 12, 60, 14, 8, 12, roughness=0.25, seed=3, floor=6)
    eq(min(p[1] for p in cave), 6, "flat cavern floor")
    drips = d.dripstones(cave, density=0.08, seed=5)
    shapes = {d.get(*p)[4] for p in drips}
    ok(sc.pack(sc.CONE) in shapes and sc.stalactite() in shapes, "stalagmites and stalactites")
    room = d.room(40, 2, 40, 45, 4, 45, floor="stone_bricks", walls="stone_bricks")
    eq(len(room), 6 * 3 * 6, "room carved")
    eq(d.get(39, 3, 42)[1], "stone_bricks", "room lining")
    shaft = d.shaft(5, 5, 6, 30, width=1, ladder="ladder")
    eq(len(shaft), 25, "shaft")


@test
def sculpture_hollow():
    s = geom.Sculpture()
    s.ellipsoid((20, 20, 20), 8, 12, 8, "verdigris_copper").sphere((20, 34, 20), 5, "verdigris_copper")
    s.capsule((20, 22, 20), (30, 34, 20), 2, "stone")
    s.mirror("x", 20)
    d = Design("t", "t")
    cells = d.fill([], "stone") | s.write(d, wall=2)
    ok((20, 20, 20) not in d.plain, "hollow inside")
    ok(any(v[1] == "stone" for v in d.plain.values()) and (10, 34, 20) in d.plain, "parts and their mirror")
    ok(len(cells) > 500, "a shell")


@test
def markers_and_header():
    d = Design("t", "T", nameKey="landmark.t.name", ruin="always", ruinProfile="masonry", requires=["atmosphere"],
               seat="span", protection="auto")
    d.box(0, 0, 0, 20, 0, 3, "stone")
    d.chest(1, 1, 1, table="ruin_rich", chance=0.7)
    d.sound(2, 1, 1, "amb_sounder", radius=64, volume=0.6)
    d.den(3, 1, 1, species="native", count=3, radius=10)
    d.vendor(4, 1, 1, theme="burgers", inside=True)
    d.door(5, 1, 1)
    d.put(6, 1, 1, "stone")
    d.chest(6, 1, 1, items="iron_plate:2-4,gold_ingot:1", hidden=True)
    eq([m.get("config", "") for m in d.markers],
       ["table=ruin_rich;chance=0.7", "sound=amb_sounder;radius=64;volume=0.6", "species=native;count=3;radius=10",
        "theme=burgers;inside=1", "", "items=iron_plate:2-4,gold_ingot:1;hidden=1"], "settings lines")
    eq(d.markers[4]["id"], "door_hinge", "door marker")
    eq(d.get(6, 1, 1), None, "a marker frees its cell")
    d.ruin_zone(0, 0, 0, 10, 0, 3, "lattice", 0.4)
    errors, _ = d.problems()
    eq(errors, [], "a valid header")
    t = d.template()
    eq([k for k in t][:9], ["key", "name", "landmark", "nameKey", "ruin", "ruinProfile", "requires", "seat", "protection"], "header order")
    eq(t["ruinZones"], [{"x0": 0, "y0": 0, "z0": 0, "x1": 10, "y1": 0, "z1": 3, "profile": "lattice", "strength": 0.4}], "zones")
    d.configure(seat="bridge", protection="locked")
    d.ruin_zone(0, 0, 0, 30, 0, 0)
    errors, _ = d.problems()
    eq(len(errors), 3, "bad seat, bad protection, zone outside")


@test
def template_roundtrip():
    d = Design("rt", "rt", groundRow=3)
    d.cylinder(10, 10, 0, 12, 6, Mat("travertine", keep=True), wall=1)
    d.dome(10, 10, 13, 6, Mat("glass", tint=0x3366AA))
    d.carve(5, 0, 5, 8, 2, 8)
    d.put(1, 1, 1, "stone", shape=sc.stalactite(), glow=0x00FF00)
    d.chest(10, 1, 10)
    t = d.template()
    cells, markers = templatefile.decode(t)
    eq(cells, d.plain, "rows decode to the same cells")
    eq(markers, d.markers, "markers")
    with tempfile.TemporaryDirectory() as tmp:
        path = d.write(tmp)
        back = Design.load(path)
        eq(back.plain, d.plain, "load(write(design))")


@test
def samples_byte_identical():
    sys.path.insert(0, str(HERE.parent))
    gen = importlib.import_module("gen_landmarks")
    with tempfile.TemporaryDirectory() as tmp:
        for build in (gen.sample_tower, gen.sample_mine, gen.sample_store):
            path = build().write(tmp)
            shipped = REPO / "data" / "landmarks" / path.name
            eq(path.read_bytes(), shipped.read_bytes(), f"{path.name} regenerates byte for byte")


@test
def preview_png():
    d = Design("pv", "Preview test", groundRow=2)
    d.cylinder(8, 8, 0, 20, 6, "travertine", wall=1)
    d.cone(8, 8, 21, 28, 7, "copper_block")
    d.truss((20, 0, 2), (20, 20, 2), "iron_block", size=3)
    d.carve(2, 0, 2, 5, 1, 5)
    d.chest(8, 1, 8)
    d.ruin_zone(0, 0, 0, 16, 28, 16, "masonry", 0.5)
    t = d.template()
    img = preview.render(t, cuts=("y=10", "z=8"))
    data = img.png()
    eq(data[:8], b"\x89PNG\r\n\x1a\n", "PNG signature")
    w, h = struct.unpack(">II", data[16:24])
    eq((w, h), (img.w, img.h), "IHDR size")
    idat = data[data.index(b"IDAT") + 4:data.index(b"IEND") - 8]
    eq(len(zlib.decompress(idat)), h * (1 + 3 * w), "pixel rows")
    eq(preview.block_colour("travertine"), preview._split(int({b["key"]: b for b in templatefile.blocks()}["travertine"]["color"])),
       "colour from blocks.json") if "travertine" in templatefile.block_keys() else None
    ok(sum(preview.block_colour("stone")) > 0, "texture average")
    line = templatefile.stats(t, 123)
    ok(line.startswith("pv: ") and "1 markers (chest 1)" in line and "1 ruin zones" in line, line)


def main() -> int:
    failed = 0
    for fn in TESTS:
        try:
            fn()
            print(f"ok    {fn.__name__}")
        except Exception as e:  # noqa: BLE001 - report every failing check, then fail the run
            failed += 1
            print(f"FAIL  {fn.__name__}: {e}")
            traceback.print_exc(limit=2)
    print(f"{len(TESTS) - failed}/{len(TESTS)} checks passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
