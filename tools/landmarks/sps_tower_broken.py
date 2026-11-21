"""The guide tower broken in the middle: the upper half folded over and hangs down to the ground.

The foot stands to its third deck — relay room, the ladder up to the highest platform that is left, a chest there. Above
it the mast buckled over its +x edge: the upper half hangs down the foot's side like a lowered arm, the end of its chords
on the ground and the lantern's frame dug in. Three emergency lamps still sit on the hanging piece.
"""
import math

from _sps_tower_parts import CX, CZ, GROUND, TOP, TowerDesign, folded, half, upright

BREAK = 44      # the row the mast buckled at


def build() -> TowerDesign:
    d = TowerDesign("sps_tower_broken", "Broken guide tower")
    up = upright()
    d.mast(up, GROUND, BREAK, spine_to=43)
    d.tear(BREAK - 6, seed=11, spare=lambda lx, lz: lx > 3)   # the side the upper half hangs on stays whole
    d.relay_room()
    for y in (24, 42):
        d.deck_lamps(up, y)
    d.chest(CX - 1, 43, CZ + 1, table="ruin_rich", block="crate")

    length = TOP - BREAK                                        # the chords' end comes to rest on the ground row
    fold = folded(BREAK, length)
    d.mast(fold, BREAK, TOP, plates=False)
    d.beacon(fold)
    d.trim()
    a = math.asin((BREAK - GROUND) / length)
    for u in (10, 22, 34):                                      # three lamps along the hanging piece
        x = round(CX + half(BREAK) + u * math.cos(a)) - 3
        _ = d.lamp_on_top(x, CZ) or d.lamp_on_top(x + 1, CZ) or d.lamp_on_top(x, CZ + 1)
    d.guard_post()
    return d


if __name__ == "__main__":
    build().write()
