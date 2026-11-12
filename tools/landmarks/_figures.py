"""Shared helpers of the figure landmarks (the Soundi statue, the singer statue): solids sampled from a predicate in a
figure's own space, super-ellipsoid blobs and Unity-style rotations. Not a landmark script (the leading underscore keeps
tools/gen_landmarks.py from running it)."""
from __future__ import annotations

import math
from collections.abc import Callable, Iterable

Cell = tuple[int, int, int]
Vec = tuple[float, float, float]


def box_cells(lo: Vec, hi: Vec) -> Iterable[Cell]:
    """Every integer point in the box lo..hi (widened to whole cells)."""
    for x in range(math.floor(lo[0]), math.ceil(hi[0]) + 1):
        for y in range(math.floor(lo[1]), math.ceil(hi[1]) + 1):
            for z in range(math.floor(lo[2]), math.ceil(hi[2]) + 1):
                yield x, y, z


def raster(pred: Callable[[float, float, float], bool], lo: Vec, hi: Vec) -> set[Cell]:
    """The cells of the box lo..hi whose centre satisfies `pred`."""
    return {p for p in box_cells(lo, hi) if pred(*p)}


def blob(c: Vec, r: Vec, p: float = 2.4) -> set[Cell]:
    """A super-ellipsoid around c with radii r (exponent p: 2 = an ellipsoid, higher = plumper and boxier)."""
    lo = (c[0] - r[0], c[1] - r[1], c[2] - r[2])
    hi = (c[0] + r[0], c[1] + r[1], c[2] + r[2])
    return raster(lambda x, y, z: sum(abs((q - cc) / rr) ** p for q, cc, rr in zip((x, y, z), c, r)) <= 1.0, lo, hi)


def rot_x(a: float):
    """Unity's rotation about +X by a degrees (left-handed: +a tips up toward forward, forward toward down)."""
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return ((1, 0, 0), (0, c, -s), (0, s, c))


def rot_z(a: float):
    """Unity's rotation about +Z by a degrees (+a turns right toward up, up toward left)."""
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return ((c, -s, 0), (s, c, 0), (0, 0, 1))


def mul(a, b):
    """The matrix product a b (apply b first)."""
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))


def inverse_apply(m, v: Vec) -> Vec:
    """m transposed times v: a world offset into the rotated frame of m."""
    return (sum(m[k][0] * v[k] for k in range(3)), sum(m[k][1] * v[k] for k in range(3)), sum(m[k][2] * v[k] for k in range(3)))


def front_cells(solid: Iterable[Cell], points: Iterable[tuple[int, int]], toward: int = 1) -> set[Cell]:
    """For each (x, y) the solid's front-most cell along z (`toward` +1 = the largest z, -1 the smallest) — where paint
    on a face goes (a smile, eyes, a mouth)."""
    best: dict[tuple[int, int], int] = {}
    wanted = set(points)
    for x, y, z in solid:
        if (x, y) in wanted:
            k = (x, y)
            if k not in best or z * toward > best[k] * toward:
                best[k] = z
    return {(x, y, z) for (x, y), z in best.items()}
