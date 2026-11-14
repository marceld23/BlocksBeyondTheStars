"""The toolkit demo (not a shipped ship — the leading underscore keeps the generator from running it unasked): a hull
120 long that shows every giant-ship helper in the order a real ship is built. Read it top to bottom.

    uv run --no-project python tools/gen_giant_ships.py --only _demo_hull --out artifacts/gs/ships --preview

Layout (x across, y up, z along; the BOW points to +z):
- a lofted hull: a narrow stern, a long parallel midbody, a bow that tapers to a point while the top stays level;
- deck A (plate row 5, walk row 6): a corridor along the centre line with three rooms on each side behind sliding doors;
- deck B (plate row 9, walk row 10): one long hall under a glass dome, windows down both sides;
- a ladder shaft from deck A to deck B, two hull doors with `entrance` markers, chests, a sound;
- a gun turret and a hollow cannon on the bow deck, a curved conduit, two engine bells at the stern, greebles.
"""
from ships import Mat, Mix, ShipDesign, ship_section

# --- materials: concrete blocks only (no material tokens), mixed only on the visible skin --------------------------
HULL = Mat("titanium_block")                                            # the shell's inner layer: long runs, small file
SKIN = Mix(Mat("titanium_block"), (Mat("aluminium_block"), 0.12), (Mat("titanium_block", tint=0x9AA4B0), 0.1), seed=3)
DECK = Mat("steel_floor")
WALL = Mat("steel_wall")
TRIM = Mat("iron_block", tint=0x4A5562)                                  # door frames, dome ribs, gun mounts
LIGHT = Mat("light_white", glow=0xFFF2D0)
ENGINE_GLOW = Mat("light_white", glow=0x66CCFF)

CX = 18                    # the centre line: an odd beam (37) keeps it on a whole cell, so a 3-wide corridor is centred
KEEL, TOP = 2, 22          # the outermost rows of the midbody
DECK_A, DECK_B = 5, 9      # deck plates; people walk one row higher
WALK_A, WALK_B = DECK_A + 1, DECK_B + 1


def build() -> ShipDesign:
    # groundRow: on a planet the keel lies buried up to here (rows below it are in the rock); weight among the ships.
    d = ShipDesign("_demo_hull", "Demo Hull", weight=1, groundRow=6, pinOnly=True)

    # 1. The hull: four stations, interpolated smoothly (a monotone curve: equal stations give a straight midbody).
    #    The last 34 slices taper to the bow about the deck (the top stays level, the keel rises), the first 6 round
    #    off the stern. A 2-thick shell, a mixed skin. Where the keel rises above a deck, the deck simply ends.
    hull = d.loft([
        (0, ship_section(CX, keel=5, deck=TOP - 1, beam=27)),
        (20, ship_section(CX, keel=KEEL, deck=TOP, beam=37, bilge=0.4)),
        (80, ship_section(CX, keel=KEEL, deck=TOP, beam=37, bilge=0.4)),
        (119, ship_section(CX, keel=9, deck=TOP, beam=19)),
    ], HULL, wall=2, skin=SKIN, bow=34, stern=6, tip=0.15, anchor="deck")

    # 2. Decks: floor plates filling the hull's inside on one row each.
    d.deck(hull, DECK_A, DECK)
    d.deck(hull, DECK_B, DECK)

    # 3. Deck A: a corridor along the centre line (its ceiling is deck B's plate, lights set into it), then three rooms
    #    on each side. depth=14 lets the rooms reach the hull, which becomes their outer wall (clipped to the inside).
    cor = d.corridor(hull, WALK_A, 14, 84, width=3, height=3, light=LIGHT, light_every=8)
    rooms = d.rooms(hull, cor, per_side=3, depth=14, a0=20, a1=80, walls=WALL)

    # 4. The way up: a ladder shaft just past the corridor's bow end (still ahead of the rising keel), its doorway
    #    facing back down the corridor (-z). Walls, ladder and landings are `keep`: the ruin pass never cuts deck B off.
    d.lift_shaft(CX - 1, 87, [WALK_A, WALK_B], opening="-z", width=2, walls=WALL)

    # 5. Windows: portholes into the deck A rooms, a band of tall windows along deck B, a bridge window in the bow.
    d.window_band(hull, WALK_A + 1, WALK_A + 1, every=5, width=1, start=22, end=78)
    d.window_band(hull, WALK_B + 3, WALK_B + 5, every=4, width=2, start=16, end=96)
    d.window_band(hull, WALK_B + 2, WALK_B + 4, side="front", every=1, width=1, start=CX - 2, end=CX + 2)

    # 6. A glass dome on the roof over deck B; clear_inside opens the roof beneath it into the hall.
    d.glass_dome(CX, 52, TOP - 1, 9, ry=7, ribs=8, rings=1, frame=TRIM)

    # 7. The ways in: hull doors cut through the wall onto a deck, an `entrance` marker on the floor just inside.
    #    The first one written is the boarding arrival in space: a deck A room on the left side.
    d.hull_door(hull, "left", WALK_A, 28, frame=TRIM)
    d.hull_door(hull, "right", WALK_B, 64, frame=TRIM)

    # 8. Loot and sound: chests in the far corner of two rooms (one hidden under a stone), a sound in the corridor.
    d.chest(*rooms[0].corner(2), table="ruin_common")
    d.chest(*rooms[4].corner(2), table="ruin_rich", hidden=True)
    d.sound(CX, WALK_A, 60, "amb_mine_drip", radius=40, volume=0.4)

    # 9. Armament: a turret on the foredeck and a long hollow cannon (bore 3 x 3) on a mount behind it.
    d.turret(CX, TOP + 1, 92, r=4, barrels=2, length=14, facing="+z", m=TRIM)
    d.cylinder(CX, 70, TOP + 1, TOP + 2, 3, TRIM)
    d.tube([(CX, TOP + 5, 66), (CX, TOP + 5, 84)], TRIM, radius=2.5, hollow=1, kind="poly")

    # 10. A curved conduit along the port side (a Catmull-Rom path through four points, radius 1).
    d.tube([(0, 14, 30), (-2, 17, 45), (-2, 17, 60), (0, 14, 75)], Mat("factory_pipe"), radius=1.0)

    # 11. Engines: two bells opening backwards from the stern with a glowing throat.
    for x in (CX - 6, CX + 6):
        d.nozzle(x, 12, 0, 4, 6, m=Mat("engine_nozzle"), glow=ENGINE_GLOW)

    # 12. Greebles last: boxes, masts and pipes from seed 7. They stand only on hull blocks that are not glass and
    #     never cover a cell that is already there (windows, doors, the dome and the guns stay free).
    d.greebles(hull, seed=7, density=0.012, m=Mat("iron_block", tint=0x6C7580))

    # 13. Optional ruin zones (at most 16): here the bow breaks up harder than the rest. A zone names its own profile
    #     (crash, hull or lattice) and strength; cells in no zone decay with the wreck's own.
    d.ruin_zone(0, 0, 95, 36, 30, 119, profile="crash", strength=0.7)

    # The nozzles and the conduit reach below x = 0 / z = 0: shift the ship back to 0 (hulls, markers and zones move).
    d.normalize()
    return d
