"""Earth's city gate of Berlin (#3982): `earth_brandenburg_gate`, at about full scale — 62 wide, 31 high (the chariot on
top included) and 13 deep with its steps; a third of the school club's golden bird gate in every direction.

Twelve Doric columns of light sandstone, six at the front and six at the back, with walls between them make five
passages, the middle one the widest. Over them the entablature with its triglyph frieze and cornice, then the stepped
attic with a relief panel on both faces and, on its raised middle, the chariot sculpture in green copper: four horses
abreast, rearing a little, the chariot behind them and the goddess of victory in it holding up her staff with a
wreath. Two small guardhouses with porticos flank the gate. A ladder in the west end pier climbs to the roof terrace
round the attic. Intact, lit and protected (an inhabited city's landmark); pin-only — placed by Earth's location list.
"""
from __future__ import annotations

import shapecode as sc
import solids
from _classical import lights
from _earth import earth_design
from geom import CLEAR, Design, Mat, Sculpture

KEYS = ("earth_brandenburg_gate",)
WIDTH = 62
GATE = (8, 53)                       # the gate body
PIERS = ((8, 11), (50, 53))
COLUMNS = (11, 18, 26, 35, 43, 50)   # the column axes (the outer two at the piers' inner corners)
FRONT_Z, BACK_Z = 2, 10              # the column rows
DEPTH = (1, 11)
MID = 30.5
# Rows.
SHAFT = (1, 14)
CAPITAL = 15
ARCHITRAVE = 16
FRIEZE = (17, 18)
CORNICE = 19
ATTIC = (20, 22)
PLINTH = 23
QUADRIGA = 24                        # the sculpture's base row

STONE = Mat("sandstone", tint=0xD9CEB6)
DRESSED = Mat("travertine", tint=0xE6DDC8)
FLUTE = Mat("travertine", tint=0xCFC5AE)
TRIGLYPH = Mat("sandstone", tint=0xB4A88E)
PANEL = Mat("sandstone", tint=0xC2B596)
COPPER = Mat("verdigris_copper", tint=0x5FA68C)
COPPER_DARK = Mat("verdigris_copper", tint=0x4A8A72)


def stylobate(d: Design) -> None:
    """Two steps of dressed stone under the gate and the guardhouses."""
    d.box(0, 0, 0, WIDTH - 1, 0, DEPTH[1] + 1, DRESSED)
    for x in range(GATE[0], GATE[1] + 1):              # the step's front and back edge as stairs
        d.put(x, 0, 0, DRESSED, shape=sc.stairs(0, 1))
        d.put(x, 0, DEPTH[1] + 1, DRESSED, shape=sc.stairs(0, -1))


def column(d: Design, cx: int, cz: int) -> None:
    """A Doric column three across (a rounded plus), fluted, with a square capital slab."""
    shaft = solids.extrude(solids.disc(cx, cz, 1.2), SHAFT[0], SHAFT[1])
    d.fill(shaft, lambda x, y, z: FLUTE if (x + z + y) % 2 == 0 and (x, z) != (cx, cz) else DRESSED)
    d.box(cx - 1, CAPITAL, cz - 1, cx + 1, CAPITAL, cz + 1, DRESSED)


def body(d: Design) -> None:
    """The end piers (the west one hollow with a ladder), the walls between the passages, twelve columns and the
    entablature: architrave, triglyph frieze, cornice."""
    for x0, x1 in PIERS:
        d.box(x0, 1, DEPTH[0] + 1, x1, CAPITAL, DEPTH[1] - 1, STONE)
    for cx in COLUMNS[1:-1]:
        d.box(cx - 1, 1, FRONT_Z + 1, cx + 1, CAPITAL, BACK_Z - 1, STONE)
    for cx in COLUMNS:
        for cz in (FRONT_Z, BACK_Z):
            column(d, cx, cz)
    x0, x1 = GATE
    d.box(x0, ARCHITRAVE, DEPTH[0], x1, FRIEZE[1], DEPTH[1], STONE)
    for x in range(x0, x1 + 1):                        # triglyphs over every column and between
        if (x - x0) % 4 in (0, 1):
            for y in FRIEZE:
                d.put(x, y, DEPTH[0] - 1, TRIGLYPH)
                d.put(x, y, DEPTH[1] + 1, TRIGLYPH)
    d.box(x0 - 1, CORNICE, DEPTH[0] - 1, x1 + 1, CORNICE, DEPTH[1] + 1, DRESSED)
    # The passages' ceilings show the coffers: a darker cell every other one.
    for x in range(x0, x1 + 1):
        for z in range(DEPTH[0] + 1, DEPTH[1]):
            if (x + z) % 2 == 0:
                d.put(x, ARCHITRAVE, z, Mat("sandstone", tint=0xC8BCA2))


def attic(d: Design) -> None:
    """The attic over the gate body, set back so a roof terrace runs round it (the pier ladder comes up there), a relief panel on both faces; the raised plinth on its
    middle under the chariot."""
    x0, x1 = GATE[0] + 3, GATE[1] - 3
    d.box(x0, ATTIC[0], DEPTH[0] + 1, x1, ATTIC[1], DEPTH[1] - 1, STONE)
    d.box(x0 - 1, ATTIC[1], DEPTH[0], x1 + 1, ATTIC[1], DEPTH[1], DRESSED)          # its cornice
    for z in (DEPTH[0], DEPTH[1]):
        d.box(x0 + 6, ATTIC[0], z, x1 - 6, ATTIC[0] + 1, z, PANEL)                  # the relief panels
        for x in range(x0 + 7, x1 - 6, 3):
            d.put(x, ATTIC[0] + 1, z, Mat("sandstone", tint=0xA8997A))
    d.box(20, PLINTH, DEPTH[0] + 1, 41, PLINTH, DEPTH[1] - 1, DRESSED)
    d.box(22, PLINTH, DEPTH[0] + 2, 39, PLINTH, DEPTH[1] - 2, STONE)


def guardhouse(d: Design, x0: int, x1: int, door_x: int) -> None:
    """A small guardhouse: a room with a door to the gate side's front, a portico of two columns, a flat roof with a
    cornice and a little gable."""
    zf, zb = 4, DEPTH[1]
    d.box(x0, 1, zf, x1, 8, zb, STONE)
    d.box(x0 + 1, 1, zf + 1, x1 - 1, 7, zb - 1, CLEAR)
    for y in range(1, 4):                                                          # the door
        d.clear(door_x, y, zf)
        d.clear(door_x + 1, y, zf)
    d.door(door_x, 1, zf)
    for cx in (x0 + 1, x1 - 1):
        d.fill([(cx, y, 1) for y in range(1, 8)], DRESSED, shape=sc.pack(sc.CYLINDER))
    d.box(x0, 8, 0, x1, 9, zb, DRESSED)
    for j, y in enumerate((10, 11)):
        d.box(x0 + 1 + 2 * j, y, 0, x1 - 1 - 2 * j, y, 1, STONE)
    d.put((x0 + x1) // 2, 6, zb - 1, "lantern")


def pier_ladder(d: Design) -> None:
    """The west pier is hollow: a door from the first passage, a ladder up to a hatch onto the roof terrace."""
    x0, x1 = PIERS[0]
    d.box(x0 + 1, 1, DEPTH[0] + 3, x1 - 1, CORNICE, DEPTH[1] - 3, CLEAR)
    for y in range(1, 4):                                                          # the door from the passage (+x side)
        d.clear(x1, y, 6)
    for y in range(1, ATTIC[0] + 1):
        d.put(x0 + 1, y, DEPTH[0] + 3, "ladder")
    d.put(x1 - 1, 3, DEPTH[1] - 3, "lantern")


def horse(s: Sculpture, x: float, z0: float, lean: float) -> None:
    """One horse facing -z: body, chest, neck and head up and forward, four legs, the front pair lifted a little."""
    y = QUADRIGA
    s.capsule((x, y + 3, z0 + 3.5), (x, y + 3.3, z0 + 0.8), 1.25, COPPER)                    # the body
    s.capsule((x, y + 3.8, z0 + 0.6), (x, y + 5.6, z0 - 0.6 - lean), 0.85, COPPER)          # the neck
    s.capsule((x, y + 5.8, z0 - 0.8 - lean), (x, y + 5.0, z0 - 2.1 - lean), 0.7, COPPER_DARK)  # the head
    for dx in (-0.5, 0.5):
        s.capsule((x + dx, y + 2.2, z0 + 3.6), (x + dx, y, z0 + 3.8), 0.45, COPPER)        # hind legs
        s.capsule((x + dx, y + 2.4, z0 + 1.0), (x + dx, y + 1.0 + lean, z0 - 0.4), 0.45, COPPER)  # front legs


def quadriga(d: Design) -> None:
    """The chariot sculpture on the attic's plinth, facing the front: four horses abreast, the chariot, the goddess with
    her staff and wreath."""
    s = Sculpture()
    for i, x in enumerate((25.0, 28.5, 32.5, 36.0)):
        horse(s, x, 4.0, 0.6 if i in (0, 3) else 0.2)
    s.box(27, QUADRIGA, 8, 34, QUADRIGA + 2, 9, COPPER_DARK)                                # the chariot
    for x in (26, 35):
        s.cylinder(x, 8.5, QUADRIGA, QUADRIGA + 1, 1.3, COPPER_DARK)                         # its wheels
    s.capsule((MID, QUADRIGA + 2, 8.5), (MID, QUADRIGA + 5, 8.5), 1.3, COPPER, 0.9)         # the goddess's robe
    s.sphere((MID, QUADRIGA + 6, 8.3), 0.9, COPPER)                                          # her head
    s.capsule((MID - 0.8, QUADRIGA + 6.5, 8.6), (MID - 2.2, QUADRIGA + 6.8, 8.2), 0.5, COPPER, 0.4)   # the wings
    s.capsule((MID + 0.8, QUADRIGA + 6.5, 8.6), (MID + 2.2, QUADRIGA + 6.8, 8.2), 0.5, COPPER, 0.4)
    s.write(d, wall=2)
    sx = round(MID + 1.5)
    d.fill([(sx, y, 8) for y in range(QUADRIGA + 3, QUADRIGA + 8)], COPPER_DARK, shape=sc.post())   # the staff
    d.fill({(sx + dx, QUADRIGA + 8, 8) for dx in (-1, 1)} | {(sx, QUADRIGA + 9, 8)}, COPPER)      # the wreath
    d.put(sx, QUADRIGA + 7, 8, COPPER)


def build() -> Design:
    d = earth_design("earth_brandenburg_gate", "Brandenburg Gate")
    stylobate(d)
    body(d)
    attic(d)
    guardhouse(d, 0, 6, 3)
    guardhouse(d, 55, 61, 57)
    pier_ladder(d)
    quadriga(d)
    # Lanterns on the walls of every passage, both sides, near both ends.
    lights(d, [(x, 5, z) for x in range(GATE[0], GATE[1] + 1) for z in (4, 8)
               if (x, 5, z) not in d.plain and ((x - 1, 5, z) in d.plain or (x + 1, 5, z) in d.plain)], "lantern")
    d.chest(2, 1, 9, table="ruin_common")
    d.chest(9, CORNICE + 1, DEPTH[1] - 1, items="crystal:1,gold_ingot:1-2", chance=0.8)
    return d


if __name__ == "__main__":
    build().write()
