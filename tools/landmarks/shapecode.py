"""The per-cell SHAPE DESCRIPTOR of the game, mirrored for the landmark scripts.

Mirrors `src/BlocksBeyondTheStars.Shared/World/BlockShape.cs`: the `BlockShape` enum and `ShapeCode.Pack`, which packs
a shape index (bits 2..7), a yaw (bits 0..1, quarter turns) and an up-face (bits 8..10, which world face the shape's
local +Y points to) into one int — the `shape` value of a template cell. `selftest.py` parses the C# file and checks
that the numbers here still match.

Orientation, exactly as the client mesher (`BlockShapeGeometry`) applies it: first the yaw turns the form about the
vertical cell axis (yaw 1 turns local +Z to world -X, see `yaw_direction`), then the up-face tilts it so local +Y
points at that face. A ramp and a staircase rise toward their local +Z; a beam lies along local X; a cone stands on
its base — with up-face -Y (`UP_NY`) it hangs from the ceiling, which is how a stalactite is made.

Besides packing, this module knows every one of the 24 orientations as a rotation matrix, so a cell can be turned or
mirrored together with the building it belongs to (`rotate_descriptor`, `mirror_descriptor`), and it carries a coarse
4 x 4 x 4 occupancy of every built-in form for the preview renderer (`occupancy`).
"""
from __future__ import annotations

# --- the BlockShape enum --------------------------------------------------------------------------------------------
CUBE = 0
SLAB = 1          # bottom half-height box
PYRAMID = 2       # square base tapering to an apex
DOME = 3          # half-sphere, flat side down
SPHERE = 4        # ball centred in the cell
RAMP = 5          # wedge rising toward local +Z
STAIRS = 6        # two steps rising toward local +Z
CONE = 7          # circular base tapering to an apex
CYLINDER = 8      # round column, full height
PANEL = 9         # quarter-height floor plate
POST = 10         # slim square column (pillars, railings)
BEAM = 11         # horizontal bar along local X (yaw 1 = along Z)
LOW_RAMP = 12     # half-height wedge rising toward local +Z
QUARTER_CUBE = 13 # 0.5^3 cube in a corner
TABLE = 14
CHAIR = 15        # backrest toward local +Z
FENCE = 16        # posts + rails along local X
SHEET = 17        # 1/16 plate (rugs)
POT = 18
BED_FOOT = 61
BED_HEAD = 62
BENCH = 63        # backrest toward local +Z

SHAPES: dict[str, int] = {
    "cube": CUBE, "slab": SLAB, "pyramid": PYRAMID, "dome": DOME, "sphere": SPHERE, "ramp": RAMP, "stairs": STAIRS,
    "cone": CONE, "cylinder": CYLINDER, "panel": PANEL, "post": POST, "beam": BEAM, "low_ramp": LOW_RAMP,
    "quarter_cube": QUARTER_CUBE, "table": TABLE, "chair": CHAIR, "fence": FENCE, "sheet": SHEET, "pot": POT,
    "bed_foot": BED_FOOT, "bed_head": BED_HEAD, "bench": BENCH,
}

# --- up-faces (ShapeCode.FaceDirection order) -----------------------------------------------------------------------
UP_PY = 0  # +Y, the default
UP_NY = 1  # -Y: upside down (hanging)
UP_PX = 2
UP_NX = 3
UP_PZ = 4
UP_NZ = 5

_FACE_DIRS = {0: (0, 1, 0), 1: (0, -1, 0), 2: (1, 0, 0), 3: (-1, 0, 0), 4: (0, 0, 1), 5: (0, 0, -1)}
_YAW_DIRS = {0: (0, 1), 1: (-1, 0), 2: (0, -1), 3: (1, 0)}


def shape_index(shape: int | str) -> int:
    """A shape index from an int or a name of `SHAPES` ("ramp", "post", ...)."""
    if isinstance(shape, str):
        try:
            return SHAPES[shape]
        except KeyError:
            raise ValueError(f"unknown shape '{shape}' (one of {', '.join(SHAPES)})") from None
    return int(shape)


def pack(shape: int | str, yaw: int = 0, up: int = UP_PY) -> int:
    """ShapeCode.Pack(shape, yaw, upFace): ((up & 7) << 8) | ((shape & 0x3F) << 2) | (yaw & 3)."""
    return ((up & 0x7) << 8) | ((shape_index(shape) & 0x3F) << 2) | (yaw & 0x3)


def shape_of(descriptor: int) -> int:
    return (descriptor >> 2) & 0x3F


def yaw_of(descriptor: int) -> int:
    return descriptor & 0x3


def up_of(descriptor: int) -> int:
    return (descriptor >> 8) & 0x7


def yaw_direction(yaw: int) -> tuple[int, int]:
    """Where local +Z points after `yaw` quarter turns (ShapeCode.YawDirection): 0 +Z, 1 -X, 2 -Z, 3 +X."""
    return _YAW_DIRS[yaw & 3]


def yaw_toward(dx: int, dz: int) -> int:
    """The yaw that points local +Z along a horizontal unit step (ShapeCode.YawToward); 0 for anything else."""
    return {(-1, 0): 1, (0, -1): 2, (1, 0): 3}.get((dx, dz), 0)


def face_direction(up: int) -> tuple[int, int, int]:
    """The unit direction an up-face points in (ShapeCode.FaceDirection)."""
    return _FACE_DIRS.get(up, (0, 1, 0))


def face_from_direction(dx: int, dy: int, dz: int) -> int:
    """The up-face along a unit direction, -1 for anything else (ShapeCode.FaceFromDirection)."""
    for face, d in _FACE_DIRS.items():
        if d == (dx, dy, dz):
            return face
    return -1


# --- ready-made descriptors -----------------------------------------------------------------------------------------
def ramp(dx: int = 0, dz: int = 1, low: bool = False) -> int:
    """A ramp whose HIGH side points along (dx, dz); `low` = the half-height incline."""
    return pack(LOW_RAMP if low else RAMP, yaw_toward(dx, dz))


def ramp_down(dx: int = 0, dz: int = 1) -> int:
    """A ramp hung upside down (its slope faces the floor) whose deep side points along (dx, dz) — for the underside
    of an overhang or an arch. The tilt to -Y also mirrors world Z, so the yaw is taken with Z the other way round."""
    return pack(RAMP, yaw_toward(dx, -dz), UP_NY)


def stairs(dx: int = 0, dz: int = 1) -> int:
    """A staircase cell that climbs toward (dx, dz)."""
    return pack(STAIRS, yaw_toward(dx, dz))


def beam(axis: str = "x") -> int:
    """A slim bar along a world axis: 'x', 'z' or 'y' (a vertical bar is the beam tilted onto +X)."""
    if axis == "x":
        return pack(BEAM, 0)
    if axis == "z":
        return pack(BEAM, 1)
    if axis == "y":
        return pack(BEAM, 0, UP_PX)
    raise ValueError(f"beam axis must be x, y or z, not '{axis}'")


def post() -> int:
    return pack(POST)


def hanging(shape: int | str) -> int:
    """The form upside down (up-face -Y): a hanging cone is a stalactite, a hanging dome a lamp shade."""
    return pack(shape, 0, UP_NY)


def stalactite() -> int:
    return hanging(CONE)


# --- orientation as rotation matrices --------------------------------------------------------------------------------
Matrix = tuple[tuple[int, int, int], tuple[int, int, int], tuple[int, int, int]]

_IDENTITY: Matrix = ((1, 0, 0), (0, 1, 0), (0, 0, 1))
_YAW1: Matrix = ((0, 0, -1), (0, 1, 0), (1, 0, 0))  # one quarter turn: x' = -z, z' = x (local +Z -> world -X)
# The client's tilt quaternions (BlockShapeGeometry.UpFaceRot) as integer matrices.
_TILT: dict[int, Matrix] = {
    0: _IDENTITY,
    1: ((1, 0, 0), (0, -1, 0), (0, 0, -1)),   # Euler(180, 0, 0)
    2: ((0, 1, 0), (-1, 0, 0), (0, 0, 1)),    # Euler(0, 0, -90): local +Y -> +X
    3: ((0, -1, 0), (1, 0, 0), (0, 0, 1)),    # Euler(0, 0, 90): local +Y -> -X
    4: ((1, 0, 0), (0, 0, -1), (0, 1, 0)),    # Euler(90, 0, 0): local +Y -> +Z
    5: ((1, 0, 0), (0, 0, 1), (0, -1, 0)),    # Euler(-90, 0, 0): local +Y -> -Z
}


def _mul(a: Matrix, b: Matrix) -> Matrix:
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))  # type: ignore[return-value]


def _apply(m: Matrix, v: tuple[float, float, float]) -> tuple[float, float, float]:
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2])


def matrix(yaw: int, up: int) -> Matrix:
    """The rotation a (yaw, up-face) pair applies to a form: the yaw first, then the tilt."""
    y = _IDENTITY
    for _ in range(yaw & 3):
        y = _mul(_YAW1, y)
    return _mul(_TILT[up if up in _TILT else 0], y)


_BY_MATRIX: dict[Matrix, tuple[int, int]] = {}
for _up in range(6):
    for _yaw in range(4):
        _BY_MATRIX.setdefault(matrix(_yaw, _up), (_yaw, _up))


def orientation_of(m: Matrix) -> tuple[int, int]:
    """The (yaw, up-face) pair whose rotation is `m` (one of the 24 cube rotations)."""
    return _BY_MATRIX[m]


def world_turn(turns: int) -> Matrix:
    """The world rotation of `turns` quarter turns about +Y, in the yaw's own sense (+Z -> -X -> -Z -> +X)."""
    m = _IDENTITY
    for _ in range(turns & 3):
        m = _mul(_YAW1, m)
    return m


def turn_offset(dx: int, dz: int, turns: int) -> tuple[int, int]:
    """A horizontal offset turned like `world_turn`: one turn maps (dx, dz) to (-dz, dx)."""
    for _ in range(turns & 3):
        dx, dz = -dz, dx
    return dx, dz


_MIRROR: dict[str, Matrix] = {
    "x": ((-1, 0, 0), (0, 1, 0), (0, 0, 1)),
    "y": ((1, 0, 0), (0, -1, 0), (0, 0, 1)),
    "z": ((1, 0, 0), (0, 1, 0), (0, 0, -1)),
}


def _with_orientation(descriptor: int, yaw: int, up: int) -> int:
    rest = descriptor & ~0x7FF  # paint design and cell index ride along unchanged
    return rest | ((up & 0x7) << 8) | (shape_of(descriptor) << 2) | (yaw & 0x3)


def rotate_descriptor(descriptor: int, turns: int) -> int:
    """The descriptor of the same form after the building it stands in turned `turns` quarter turns (`world_turn`)."""
    if shape_of(descriptor) == CUBE or turns & 3 == 0:
        return descriptor
    yaw, up = orientation_of(_mul(world_turn(turns), matrix(yaw_of(descriptor), up_of(descriptor))))
    return _with_orientation(descriptor, yaw, up)


def mirror_descriptor(descriptor: int, axis: str) -> int:
    """The descriptor of the form after a mirror across a plane normal to `axis`. A mirror is no rotation, so the form
    is also mirrored in its own X — exact for every built-in that is symmetric across its local X (ramps, stairs,
    beams, chairs, benches, beds, cones ...); a quarter cube keeps its corner only approximately."""
    if shape_of(descriptor) == CUBE:
        return descriptor
    m = _mul(_mul(_MIRROR[axis], matrix(yaw_of(descriptor), up_of(descriptor))), _MIRROR["x"])
    yaw, up = orientation_of(m)
    return _with_orientation(descriptor, yaw, up)


# --- a coarse occupancy for the preview ------------------------------------------------------------------------------
N = 4  # sub-cells per axis


def _local_occupied(shape: int, x: float, y: float, z: float) -> bool:
    """Whether the local point (cell coordinates 0..1, y up) lies inside the built-in form (BlockShapeGeometry)."""
    rx, rz = x - 0.5, z - 0.5

    def inside(x0: float, y0: float, z0: float, x1: float, y1: float, z1: float) -> bool:
        return x0 <= x <= x1 and y0 <= y <= y1 and z0 <= z <= z1

    if shape == SLAB:
        return y <= 0.5
    if shape == PYRAMID:
        return max(abs(rx), abs(rz)) <= 0.5 * (1 - y)
    if shape == DOME:
        return (rx * rx + rz * rz) / 0.25 + y * y <= 1
    if shape == SPHERE:
        return rx * rx + (y - 0.5) ** 2 + rz * rz <= 0.25
    if shape == RAMP:
        return y <= z
    if shape == STAIRS:
        return y <= 0.5 or z >= 0.5
    if shape == CONE:
        return (rx * rx + rz * rz) ** 0.5 <= 0.5 * (1 - y)
    if shape == CYLINDER:
        return rx * rx + rz * rz <= 0.25
    if shape == PANEL:
        return y <= 0.25
    if shape == POST:
        return inside(0.3, 0, 0.3, 0.7, 1, 0.7)
    if shape == BEAM:
        return inside(0, 0.3, 0.3, 1, 0.7, 0.7)
    if shape == LOW_RAMP:
        return y <= 0.5 * z
    if shape == QUARTER_CUBE:
        return x <= 0.5 and y <= 0.5 and z <= 0.5
    if shape == TABLE:
        return y >= 0.75 or (y <= 0.9 and (abs(rx) >= 0.25 and abs(rz) >= 0.25))
    if shape in (CHAIR, BENCH):
        return inside(0.1, 0.25, 0.1, 0.9, 0.5, 0.9) or inside(0.1, 0.4, 0.72, 0.9, 1, 0.9)
    if shape == FENCE:
        return inside(0, 0, 0.4, 0.25, 0.85, 0.6) or inside(0.75, 0, 0.4, 1, 0.85, 0.6) or inside(0, 0.2, 0.4, 1, 0.7, 0.6)
    if shape == SHEET:
        return y <= 0.25  # drawn as a plate: a true 1/16 sheet would vanish at this resolution
    if shape == POT:
        return inside(0.25, 0, 0.25, 0.75, 0.5, 0.75)
    if shape in (BED_HEAD, BED_FOOT):
        return y <= 0.5
    return True  # the cube, and player-designed forms the preview cannot know


_OCC_CACHE: dict[int, frozenset[tuple[int, int, int]]] = {}


def occupancy(descriptor: int) -> frozenset[tuple[int, int, int]]:
    """The occupied sub-cells (i, j, k in 0..N-1, world-aligned) of a shaped cell: the form's local occupancy turned by
    its yaw and up-face about the cell centre. A cube is every sub-cell."""
    key = descriptor & 0x7FF
    hit = _OCC_CACHE.get(key)
    if hit is not None:
        return hit
    shape = shape_of(key)
    m = matrix(yaw_of(key), up_of(key))
    half = (N - 1) / 2
    cells = set()
    for i in range(N):
        for j in range(N):
            for k in range(N):
                lx, ly, lz = (i + 0.5) / N, (j + 0.5) / N, (k + 0.5) / N
                if not _local_occupied(shape, lx, ly, lz):
                    continue
                wx, wy, wz = _apply(m, (i - half, j - half, k - half))
                cells.add((round(wx + half), round(wy + half), round(wz + half)))
    hit = frozenset(cells)
    _OCC_CACHE[key] = hit
    return hit
