"""The guide tower as a stump: the mast snapped off above its second deck and lies beside the foot.

The foot stands to about a third of its height — relay room, the first two decks, the ladder up to the second one with a
chest on it — and ends in four ragged corner stubs. The rest of the mast, sixty rows of it with the lantern's frame and the
antenna, lies along +x where it fell, its lower chords sunk into the ground. Three of its emergency lamps still sit on it.
"""
from _sps_tower_parts import CX, CZ, GROUND, TOP, TowerDesign, lying, upright

BREAK = 38      # the row the mast snapped at
LIES_AT = 27    # the x of the fallen piece's broken end


def build() -> TowerDesign:
    d = TowerDesign("sps_tower_stump", "Fallen guide tower")
    up = upright()
    d.mast(up, GROUND, BREAK + 2, spine_to=25)
    d.tear(30, seed=5)
    d.relay_room()
    d.deck_lamps(up, 24)
    d.chest(CX - 1, 25, CZ + 1, table="ruin_rich", block="crate")

    lie = lying(BREAK, LIES_AT)
    d.mast(lie, BREAK, TOP)
    d.beacon(lie)
    d.trim()
    placed = 0
    for x in range(LIES_AT + (TOP - BREAK) - 2, LIES_AT + 6, -1):   # from the head back along the mast, three lamps
        if placed < 3 and (LIES_AT + (TOP - BREAK) - 2 - x) % 17 == 0:
            placed += 1 if d.lamp_on_top(x, CZ) or d.lamp_on_top(x, CZ + 1) else 0
    d.guard_post()
    return d


if __name__ == "__main__":
    build().write()
