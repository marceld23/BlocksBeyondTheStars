"""The light-mount pass (#3626, #3627): every torch and lantern of a design hangs on what holds it.

Since #2683 a torch or lantern the player places stores the face it was put against: the form `PropShapes.LightMountShape`
(the slim post, 10) with an up-face pointing AWAY from the block that holds it — 0 standing on the floor, 1 hanging from
the ceiling (a lantern only; a torch never), 2..5 on a wall (`ShapeCode.FaceFromDirection` of the step from the wall into
the light's cell). The client draws such a torch leaning out of its wall on a little plate. A light WITHOUT that form —
which is what every authored landmark carried — stays the old upright rosette in the middle of its cell, so the mine's
and the pyramid's torches stood straight in the air in front of their walls.

`mount_lights` gives every torch / lantern cell (lit or `_off`) that carries no shape yet the descriptor of the surface
that holds it, from the design's own cells:

- a torch: a wall beside it (several: the one with open room in front, then `ShapeCode.WallFaces` order 2, 3, 4, 5),
  else the floor;
- a lantern: the ceiling, else the floor it stands on (a lantern authored on a deck or a table stands there), else a
  wall.

"Holds a light" mirrors `PropShapes.HoldsLight`: a block that is solid, not a liquid, not water or lava and not a slim
walk-through prop (`WalkThroughBlocks.IsProp`: small plants, torches, lanterns, the ladder, fire, the energy gate, open
twins). Below the template's ground row an empty cell that is not a cavity (and not a marker's) is the planet's own rock,
which holds a light too — unless the design flies in space (a giant ship), where nothing lies outside its hull.

A light with nothing to hold it keeps descriptor 0 and is returned as a problem, so the script that placed it can be
fixed; so is a light buried on all six sides (it is mounted, but nobody sees it). A light that already carries a shape
is left as authored. Pure functions; standard library only.
"""
from __future__ import annotations

try:  # imported as the package `landmarks.lightmount`
    from . import shapecode as sc
    from . import templatefile
except ImportError:  # imported from a script in tools/landmarks
    import shapecode as sc
    import templatefile

Cell = tuple[int, int, int]

LIGHT_KEYS = frozenset({"torch", "lantern", "torch_off", "lantern_off"})  # PropShapes.IsLightMountKey
LANTERN_KEYS = frozenset({"lantern", "lantern_off"})                     # PropShapes.IsLanternKey
LIGHT_MOUNT_SHAPE = sc.POST                                              # PropShapes.LightMountShape
FLOOR, CEILING = sc.UP_PY, sc.UP_NY
WALL_FACES = (sc.UP_PX, sc.UP_NX, sc.UP_PZ, sc.UP_NZ)                   # ShapeCode.WallFaces, the fallback order
_OPPOSITE = {sc.UP_PX: sc.UP_NX, sc.UP_NX: sc.UP_PX, sc.UP_PZ: sc.UP_NZ, sc.UP_NZ: sc.UP_PZ}

# WalkThroughBlocks.IsProp besides the small plants: the slim props a body walks through. FloraCatalog.IsPlantKey is the
# `flora_` prefix plus Soaniri's singing plants, whose keys carry none (#3422).
_PROP_KEYS = frozenset({"torch", "lantern", "torch_off", "lantern_off", "ladder", "fire", "energy_gate",
                        "phase_block_open", "force_field_off", "trapdoor_open", "lift_gate_open"})
_PLANT_KEYS = frozenset({"chime_reed", "bell_bloom", "bell_bloom_open", "harp_vine", "echo_flower"})


def descriptor(up_face: int) -> int:
    """`ShapeCode.Pack(PropShapes.LightMountShape, 0, upFace)` — a light fixed to the block opposite that up-face."""
    return sc.pack(LIGHT_MOUNT_SHAPE, 0, up_face)


def mount_of(key: str, shape: int) -> int:
    """`PropShapes.LightMountOf`: the up-face a stamped light is fixed by, or -1 (not a light, unstamped, or a torch
    claiming the ceiling)."""
    if key not in LIGHT_KEYS or sc.shape_of(shape) != LIGHT_MOUNT_SHAPE:
        return -1
    face = sc.up_of(shape)
    return -1 if face > 5 or (face == CEILING and key not in LANTERN_KEYS) else face


def holder_of(cell: Cell, up_face: int) -> Cell:
    """The cell that holds a light mounted by `up_face` (the up-face points away from it)."""
    dx, dy, dz = sc.face_direction(up_face)
    return cell[0] - dx, cell[1] - dy, cell[2] - dz


def holding_keys() -> set[str]:
    """The block keys of data/blocks.json that can hold a light (`PropShapes.HoldsLight`)."""
    keys = set()
    for b in templatefile.blocks():
        key = b["key"]
        if (b.get("solid", True) and not b.get("liquid", False) and key not in ("water", "lava")
                and key not in _PROP_KEYS and key not in _PLANT_KEYS and not key.startswith("flora_")):
            keys.add(key)
    return keys | set(templatefile.MATERIAL_TOKENS)  # a material token always resolves to a building block


class _World:
    """What the cells around a light are: the design's plain cells, its markers' cells, and the rock below the ground
    row (`rock_below` = the ground row, or 0 when nothing lies outside the design)."""

    def __init__(self, plain: dict, markers: list[dict], rock_below: int, holds: set[str]) -> None:
        self.plain = plain
        self.marked = {(m["x"], m["y"], m["z"]) for m in markers}
        self.rock_below = rock_below
        self.holds = holds

    def holds_light(self, p: Cell) -> bool:
        look = self.plain.get(p)
        if look is None:
            return p[1] < self.rock_below and p not in self.marked  # the planet's own rock
        return look[0] == "block" and look[1] in self.holds

    def wall_faces(self, cell: Cell) -> list[int]:
        """The up-faces of the walls beside `cell` that hold a light — those with open room in front first (a corner
        torch leans into the room, not along a wall), each group in `WALL_FACES` order."""
        faces = [f for f in WALL_FACES if self.holds_light(holder_of(cell, f))]
        return sorted(faces, key=lambda f: (self.holds_light(holder_of(cell, _OPPOSITE[f])), WALL_FACES.index(f)))

    def choose(self, key: str, cell: Cell) -> int:
        """The up-face the light at `cell` is fixed by, or -1 when nothing holds it."""
        walls = self.wall_faces(cell)
        floor = self.holds_light(holder_of(cell, FLOOR))
        if key in LANTERN_KEYS:
            if self.holds_light(holder_of(cell, CEILING)):
                return CEILING
            if floor:
                return FLOOR
            return walls[0] if walls else -1
        if walls:
            return walls[0]
        return FLOOR if floor else -1


def mount_lights(plain: dict, markers: list[dict], ground_row: int, space: bool = False) -> tuple[dict, list[str]]:
    """Mounts every unshaped torch / lantern of `plain` (position -> (kind, id, tint, glow, shape, keep), changed in
    place). Returns ({position: up-face} of the lights it mounted, [a problem line per light nothing holds])."""
    world = _World(plain, markers, 0 if space else ground_row, holding_keys())
    mounted: dict[Cell, int] = {}
    problems: list[str] = []
    for p in sorted(plain, key=lambda c: (c[1], c[2], c[0])):
        kind, key, tint, glow, shape, keep = plain[p]
        if kind != "block" or key not in LIGHT_KEYS or shape:
            continue
        face = world.choose(key, p)
        if all(world.holds_light(holder_of(p, f)) for f in range(6)):
            problems.append(f"{key} at {p} is buried: every side of its cell is solid, nobody can see it")
        if face < 0:
            problems.append(f"{key} at {p} has nothing to hang on or stand on (no wall, floor{' or ceiling' if key in LANTERN_KEYS else ''})")
            continue
        plain[p] = (kind, key, tint, glow, descriptor(face), keep)
        mounted[p] = face
    return mounted, problems
