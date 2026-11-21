"""The guide tower still standing — and leaning: thirteen blocks out of true at its tip.

The whole mast is there, bent over toward +x from its first deck up; its corner chords hold (they are kept), the braces,
the ring beams and the decks are what the years take. The ladder runs up the spine to the top deck, where a chest waits
between three emergency lamps under what is left of the lantern's frame; half the frame and the antenna's tip are gone.
"""
from _sps_tower_parts import CX, CZ, GROUND, TIP, TOP, TowerDesign, half, upright

LEAN = 13       # blocks out of true at the tip


def lean(y: int) -> int:
    return round(LEAN * (y - 6) / (TIP - 6)) if y > 6 else 0


def build() -> TowerDesign:
    d = TowerDesign("sps_tower_leaning", "Leaning guide tower")
    up = upright(lean)
    d.mast(up, GROUND, TOP, spine_to=TOP + 1, keep_chords=True)
    d.beacon(up, whole=False)
    # The face that looks away from the lean lost its braces and ring beams above the fourth deck.
    for (x, y, z), look in list(d.plain.items()):
        if y > 62 and not look[5] and (z - CZ) <= -(half(y) - 0.6) and abs(x - lean(y) - CX) < half(y) - 0.6:
            d.clear(x, y, z)
    d.relay_room()
    for y in (24, 42, 60, 78):
        d.deck_lamps(up, y)
    d.head(up)
    d.guard_post()
    return d


if __name__ == "__main__":
    build().write()
