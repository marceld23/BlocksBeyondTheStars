#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The pioneers' pixel-skin pool (#2682): data/pioneer_skins.json.

A pioneer NPC wears a pixel skin from this pool, picked and recoloured by its seed on the client
(src/BlocksBeyondTheStars.Client.Core/PixelSkins.cs). A skin is exactly what a player paints in the game's
Appearance editor: a 32x32 face and the body paintings of torso, arms, legs and helmet (32x32 chunks, one
base32 palette symbol per pixel, "0" = transparent), plus five base colours as palette indices.

Usage (from the repository root, no dependencies):

    uv run --no-project python tools/gen_pioneer_skins.py
        Rebuilds the generated skins (deterministic) and keeps every hand-made skin already in the file.

    uv run --no-project python tools/gen_pioneer_skins.py --from-settings <client_settings.json> --key justus_ranger \\
        --author Justus [--outfit "My outfit"]
        Adds (or replaces) a skin painted in the game: the applied look of the settings file, or one of its saved
        outfits (--hair <palette index> gives the pioneer hair; 0 = bald). Where the file lives and how to paint a good one: docs/developer/WILDERNESS_FOLK.md section 8.

    uv run --no-project python tools/gen_pioneer_skins.py --check
        Validates the file (the same rules as the content test).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
POOL = REPO / "data" / "pioneer_skins.json"

ALPHABET = "0123456789abcdefghijklmnopqrstuv"
SIZE = 32
CHUNKS = {"torso": 4, "arm": 8, "leg": 8, "helmet": 5}

# The face/body-paint palette (client FacePalette.Colors) — index 0 is transparent.
PALETTE = [
    None,
    (20, 18, 24),
    (74, 64, 78),
    (150, 150, 158),
    (240, 240, 236),
    (120, 72, 40),
    (214, 160, 110),
    (247, 206, 170),
    (196, 60, 50),
    (232, 150, 64),
    (238, 206, 76),
    (96, 176, 90),
    (70, 150, 210),
    (150, 96, 200),
    (240, 150, 190),
    (60, 200, 200),
    (46, 44, 54),
    (104, 100, 112),
    (198, 198, 204),
    (70, 40, 22),
    (176, 122, 74),
    (150, 96, 58),
    (120, 30, 28),
    (236, 122, 104),
    (168, 92, 26),
    (250, 226, 150),
    (48, 110, 56),
    (158, 214, 120),
    (34, 84, 140),
    (148, 202, 240),
    (92, 52, 132),
    (206, 166, 236),
]

# Palette indices by name.
BLACK, SLATE, GREY, WHITE, BROWN, TAN, LIGHT = 1, 2, 3, 4, 5, 6, 7
YELLOW, PINK, CYAN, CHARCOAL, MIDGREY, LIGHTGREY = 10, 14, 15, 16, 17, 18
DARKBROWN, MIDBROWN, DEEP, ORANGE, DARKORANGE, LIGHTYELLOW = 19, 20, 21, 9, 24, 25

# The recolourable families (dark, mid, light) — PixelSkins.GarmentFamilies / HairFamilies.
RED = (22, 8, 23)
GREEN = (26, 11, 27)
BLUE = (28, 12, 29)
PURPLE = (30, 13, 31)
HAIR_BROWN = (19, 5, 20)
HAIR_DARK = (16, 2, 17)
HAIR_GOLDEN = (24, 10, 25)


class Grid:
    """A palette-index canvas, row 0 at the top."""

    def __init__(self, w: int = SIZE, h: int = SIZE, fill: int = 0) -> None:
        self.w, self.h = w, h
        self.px = [[fill] * w for _ in range(h)]

    def set(self, x: int, y: int, c: int) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = c

    def rect(self, x0: int, y0: int, x1: int, y1: int, c: int) -> None:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def outline(self, x0: int, y0: int, x1: int, y1: int, c: int) -> None:
        for x in range(x0, x1 + 1):
            self.set(x, y0, c)
            self.set(x, y1, c)
        for y in range(y0, y1 + 1):
            self.set(x0, y, c)
            self.set(x1, y, c)

    def encode(self) -> str:
        return "".join(ALPHABET[c] for row in self.px for c in row)


def strip(chunks: list[Grid]) -> str:
    """A part payload: the chunks one after another, each row-major from its top."""
    return "".join(g.encode() for g in chunks)


# ---------------------------------------------------------------- faces


def draw_face(spec: dict) -> str:
    g = Grid()
    _skin, shade = spec["skin"], spec["shade"]
    hd, hm, hl = spec["hair"]
    style = spec.get("style", "short")

    # Hair.
    if style == "cap":
        cd, cm, cl = spec["cap"]
        g.rect(0, 0, 31, 5, cm)
        g.rect(0, 0, 31, 0, cd)
        g.rect(12, 2, 19, 3, cl)  # the badge
        g.rect(0, 6, 22, 7, cd)  # the brim
        g.rect(0, 8, 2, 13, hm)
        g.rect(29, 8, 31, 13, hm)
    else:
        g.rect(0, 0, 31, 5, hm)
        for x in range(3, 29, 5):
            g.set(x, 1, hl)
            g.set(x + 1, 2, hl)
        if style == "fringe":
            g.rect(0, 6, 31, 7, hm)
            for x in range(1, 31, 4):
                g.set(x, 8, hm)
                g.set(x + 1, 8, hd)
        elif style == "spiky":
            for x in range(0, 32, 4):
                g.rect(x, 6, x + 1, 7, hm)
                g.set(x, 8, hd)
        elif style == "long":
            g.rect(0, 6, 31, 6, hm)
            g.rect(0, 6, 3, 29, hm)
            g.rect(28, 6, 31, 29, hm)
            g.rect(3, 7, 3, 29, hd)
            g.rect(28, 7, 28, 29, hd)
        elif style == "bun":
            g.rect(0, 6, 31, 6, hd)
            g.rect(0, 6, 1, 14, hm)
            g.rect(30, 6, 31, 14, hm)
        else:  # short
            g.rect(0, 6, 31, 6, hm)
            g.rect(0, 6, 2, 13, hm)
            g.rect(29, 6, 31, 13, hm)
            g.rect(0, 14, 1, 16, hd)
            g.rect(30, 14, 31, 16, hd)

    if spec.get("headband"):
        bd, bm, _ = spec["headband"]
        g.rect(0, 7, 31, 8, bm)
        g.rect(0, 9, 31, 9, bd)

    # Brows, eyes, nose, mouth.
    brow = hd if style != "cap" else HAIR_DARK[0]
    g.rect(7, 11, 11, 11, brow)
    g.rect(20, 11, 24, 11, brow)
    for ex in (7, 20):
        g.rect(ex, 13, ex + 4, 15, WHITE)
        g.rect(ex + 2, 13, ex + 3, 15, BLACK)
        g.set(ex + 2, 13, WHITE)  # the shine
    g.rect(15, 18, 16, 19, shade)
    g.set(11, 22, BLACK)
    g.set(20, 22, BLACK)
    g.rect(12, 23, 19, 23, BLACK)
    g.rect(13, 24, 18, 24, PINK)

    if spec.get("blush"):
        g.rect(5, 19, 6, 19, PINK)
        g.rect(25, 19, 26, 19, PINK)
    if spec.get("freckles"):
        for x, y in ((6, 18), (8, 19), (10, 18), (21, 18), (23, 19), (25, 18)):
            g.set(x, y, shade)
    if spec.get("glasses"):
        for ex in (6, 19):
            g.outline(ex, 12, ex + 6, 16, BLACK)
        g.rect(13, 13, 18, 13, BLACK)
    if spec.get("goggles"):
        g.rect(0, 7, 31, 8, SLATE)
        for ex in (6, 19):
            g.rect(ex, 5, ex + 6, 9, BLACK)
            g.rect(ex + 1, 6, ex + 5, 8, CYAN)
            g.set(ex + 2, 6, WHITE)
    if spec.get("beard"):
        g.rect(3, 20, 6, 31, hm)
        g.rect(25, 20, 28, 31, hm)
        g.rect(3, 25, 28, 31, hm)
        g.rect(10, 21, 21, 22, hm)  # the moustache
        g.rect(12, 23, 19, 23, BLACK)
        g.rect(13, 24, 18, 24, PINK)
        for x in range(5, 27, 3):
            g.set(x, 29, hd)
    return g.encode()


# ---------------------------------------------------------------- the body

FRONT, LEFT, BACK, RIGHT = 0, 1, 2, 3  # the torso strip as the game lays it out (#2750: the figure's own left first)


def emblem(g: Grid, kind: str, x: int, y: int) -> None:
    """A small 6x6 badge with its top-left corner at (x, y)."""
    if kind == "star":
        for dx, dy in (
            (2, 0),
            (3, 0),
            (2, 1),
            (3, 1),
            (0, 2),
            (1, 2),
            (2, 2),
            (3, 2),
            (4, 2),
            (5, 2),
            (1, 3),
            (2, 3),
            (3, 3),
            (4, 3),
            (1, 4),
            (4, 4),
            (0, 5),
            (5, 5),
        ):
            g.set(x + dx, y + dy, YELLOW)
        g.set(x + 2, y + 2, LIGHTYELLOW)
    elif kind == "leaf":
        for dx, dy in (
            (3, 0),
            (4, 0),
            (2, 1),
            (3, 1),
            (4, 1),
            (1, 2),
            (2, 2),
            (3, 2),
            (4, 2),
            (1, 3),
            (2, 3),
            (3, 3),
            (2, 4),
            (1, 5),
        ):
            g.set(x + dx, y + dy, GREEN[2])
        g.set(x + 2, y + 3, GREEN[0])
        g.set(x + 3, y + 2, GREEN[0])
    elif kind == "flower":
        for dx, dy in ((2, 0), (0, 2), (4, 2), (2, 4), (1, 1), (3, 1), (1, 3), (3, 3)):
            g.set(x + dx, y + dy, PINK)
        g.set(x + 2, y + 2, YELLOW)
    elif kind == "gear":
        g.outline(x, y, x + 5, y + 5, GREY)
        g.rect(x + 2, y + 2, x + 3, y + 3, LIGHTGREY)
        for dx, dy in (
            (2, -1),
            (3, -1),
            (-1, 2),
            (-1, 3),
            (6, 2),
            (6, 3),
            (2, 6),
            (3, 6),
        ):
            g.set(x + dx, y + dy, GREY)


def torso_chunk(side: int, o: dict) -> Grid:
    td, tm, tl = o["top"]
    g = Grid(fill=tm)
    style = o["style"]
    skin = o["skin"]

    # Shading at the chunk's edges (the box's corners) and the seam down the sides.
    g.rect(0, 0, 0, 21, td)
    g.rect(31, 0, 31, 21, td)
    if side in (RIGHT, LEFT):
        g.rect(15, 0, 16, 21, td)

    if style == "checked":
        for y in range(22):
            for x in range(32):
                if (x // 4 + y // 4) % 2 == 0:
                    g.set(x, y, td)
                if x % 4 == 0 or y % 4 == 0:
                    g.set(x, y, tl if (x // 4 + y // 4) % 2 else tm)
    elif style == "striped":
        sd, sm = o["stripe"]
        for y in range(22):
            if (y // 3) % 2 == 1:
                g.rect(0, y, 31, y, sm if y % 3 else sd)
    elif style in ("overalls", "apron"):
        shirt = o["shirt"]
        g.rect(0, 0, 31, 21, shirt)
        if side == FRONT:
            g.rect(9, 7, 22, 21, tm)
            g.rect(9, 0, 11, 7, tm)
            g.rect(20, 0, 22, 7, tm)
            g.outline(9, 7, 22, 21, td)
            g.rect(13, 10, 18, 14, td)
            g.rect(14, 11, 17, 13, tl)
            if style == "overalls":
                g.set(10, 7, YELLOW)
                g.set(21, 7, YELLOW)
        elif side == BACK:
            # The straps cross on the back.
            for i in range(10):
                g.rect(8 + i, i, 9 + i, i, tm)
                g.rect(22 - i, i, 23 - i, i, tm)
            if style == "apron":
                g.rect(10, 13, 21, 13, td)  # the apron strings
    elif style == "vest":
        shirt = o["shirt"]
        if side == FRONT:
            g.rect(10, 0, 21, 21, shirt[1])
            g.rect(15, 0, 16, 21, shirt[0])
            g.rect(4, 10, 8, 13, td)
            g.rect(23, 10, 27, 13, td)
        else:
            g.rect(0, 0, 31, 2, shirt[1])
    elif style == "coat" and side == FRONT:
        g.rect(14, 0, 17, 21, td)  # the coat's opening
        for y in range(4, 22, 4):
            g.set(13, y, tl)
            g.set(18, y, tl)

    if style in ("jacket", "coat") and side == FRONT:
        g.rect(15, 2, 16, 21, GREY)  # the zipper
        g.rect(15, 8, 16, 9, LIGHTGREY)
        g.rect(4, 13, 9, 16, td)  # pockets
        g.rect(22, 13, 27, 16, td)
        g.rect(4, 13, 9, 13, tl)
        g.rect(22, 13, 27, 13, tl)

    # The collar (a V of skin at the neck) and a scarf.
    if side == FRONT and style not in ("overalls", "apron"):
        g.rect(10, 0, 21, 1, tl)
        for i in range(3):
            g.rect(14 - (3 - i) // 2, i, 17 + (3 - i) // 2, i, skin)
    if o.get("scarf"):
        sd, sm, sl = o["scarf"]
        g.rect(0, 0, 31, 3, sm)
        g.rect(0, 3, 31, 3, sd)
        if side == FRONT:
            g.rect(20, 4, 23, 13, sm)
            for x in range(20, 24):
                g.set(x, 14, sl if x % 2 else sd)
    if o.get("band") and side in (FRONT, BACK):
        bm, bl = o["band"]
        for x in range(32):
            g.set(x, 17 + (x // 2) % 2, bm)
            g.set(x, 19 - (x // 2) % 2, bl)

    if o.get("emblem") and side == FRONT:
        emblem(g, o["emblem"], 21 if style != "vest" else 4, 4)
    if o.get("backpack") and side == BACK:
        bd, bm = DARKBROWN, MIDBROWN
        g.rect(8, 3, 23, 19, bm)
        g.outline(8, 3, 23, 19, bd)
        g.rect(8, 3, 23, 7, bd)
        g.rect(15, 6, 16, 7, YELLOW)

    # The waist: a belt (or the coat's hem), then the trousers on the pelvis.
    ld, lm, _ = o["legs"]
    if style == "coat":
        g.rect(0, 22, 31, 26, tm)
        g.rect(0, 26, 31, 26, td)
        g.rect(0, 27, 31, 31, lm)
    elif style == "overalls":
        g.rect(0, 22, 31, 31, tm)
        g.rect(0, 22, 31, 22, td)
    else:
        g.rect(0, 22, 31, 23, DARKBROWN)
        if side == FRONT:
            g.rect(14, 22, 17, 23, YELLOW)
        g.rect(0, 24, 31, 31, lm)
        if side in (RIGHT, LEFT):
            g.rect(15, 24, 16, 31, ld)
    return g


def arm_chunk(face: int, o: dict) -> Grid:
    sd, sm, sl = o["sleeve"]
    g = Grid(fill=sm)
    skin = o["skin"]
    short = o.get("short_sleeves", False)
    if face == 3:  # the inner side, a touch darker
        g.rect(0, 0, 31, 24, sd)
    if o["style"] == "striped":
        tsd, tsm = o["stripe"]
        for y in range(25):
            if (y // 3) % 2 == 1:
                g.rect(0, y, 31, y, tsm if y % 3 else tsd)
    if o["style"] == "checked":
        for y in range(25):
            for x in range(32):
                if (x // 4 + y // 4) % 2 == 0:
                    g.set(x, y, sd)
    if short:
        g.rect(0, 10, 31, 31, skin)
        g.rect(0, 9, 31, 9, sl)
    else:
        g.rect(0, 22, 31, 24, sl if o.get("light_cuffs") else sd)
        g.rect(0, 25, 31, 26, skin)
    hand = o.get("gloves") or skin
    g.rect(0, 27, 31, 31, hand)
    if o.get("gloves") and face == 0:
        g.rect(0, 27, 31, 27, DARKBROWN)
    return g


def leg_chunk(face: int, o: dict) -> Grid:
    ld, lm, ll = o["legs"]
    g = Grid(fill=lm)
    skin = o["skin"]
    if face == 3:
        g.rect(0, 0, 31, 26, ld)
    if face == 1 and o.get("leg_stripe"):
        g.rect(14, 0, 17, 26, ll)
    if face == 0 and o.get("knee_patch"):
        g.rect(9, 14, 22, 18, ld)
        g.outline(9, 14, 22, 18, ll)
    if o.get("shorts"):
        g.rect(0, 12, 31, 22, skin)
        g.rect(0, 11, 31, 11, ld)
        g.rect(0, 23, 31, 26, WHITE)
        g.rect(0, 23, 31, 23, LIGHTGREY)
    bd, bm = o["boots"]
    g.rect(0, 27, 31, 30, bm)
    g.rect(0, 31, 31, 31, BLACK)
    g.rect(0, 27, 31, 27, bd)
    if face == 0:
        for x in range(12, 20, 3):
            g.set(x, 28, WHITE)
            g.set(x + 1, 29, WHITE)
    return g


def body(o: dict) -> tuple[str, str, str]:
    torso = strip([torso_chunk(side, o) for side in (FRONT, LEFT, BACK, RIGHT)])
    arms = strip([arm_chunk(face, o) for _limb in range(2) for face in range(4)])
    legs = strip([leg_chunk(face, o) for _limb in range(2) for face in range(4)])
    return torso, arms, legs


# ---------------------------------------------------------------- the generated skins


def generated_skins() -> list[dict]:
    """The generated pool: eight explorers with their own outfit, face and hair (all recoloured per pioneer)."""
    defs = [
        {
            "key": "trailblazer",
            "skin": LIGHT,
            "shade": TAN,
            "hair": HAIR_BROWN,
            "style": "short",
            "freckles": True,
            "top": GREEN,
            "sleeve": GREEN,
            "legs": (DARKBROWN, MIDBROWN, BROWN),
            "boots": (CHARCOAL, SLATE),
            "outfit": "jacket",
            "emblem": "leaf",
            "backpack": True,
            "knee_patch": True,
        },
        {
            "key": "stargazer",
            "skin": TAN,
            "shade": DEEP,
            "hair": HAIR_DARK,
            "style": "long",
            "headband": RED,
            "top": PURPLE,
            "sleeve": PURPLE,
            "legs": (CHARCOAL, MIDGREY, LIGHTGREY),
            "boots": (BLACK, CHARCOAL),
            "outfit": "coat",
            "emblem": "star",
            "light_cuffs": True,
        },
        {
            "key": "tinkerer",
            "skin": LIGHT,
            "shade": TAN,
            "hair": HAIR_GOLDEN,
            "style": "spiky",
            "goggles": True,
            "top": BLUE,
            "sleeve": (MIDGREY, LIGHTGREY, WHITE),
            "shirt": LIGHTGREY,
            "legs": BLUE,
            "boots": (DARKBROWN, MIDBROWN),
            "outfit": "overalls",
            "gloves": MIDBROWN,
            "emblem": None,
            "knee_patch": True,
        },
        {
            "key": "trapper",
            "skin": DEEP,
            "shade": DARKBROWN,
            "hair": HAIR_DARK,
            "style": "short",
            "beard": True,
            "top": RED,
            "sleeve": RED,
            "legs": BLUE,
            "boots": (DARKBROWN, MIDBROWN),
            "outfit": "checked",
            "backpack": True,
        },
        {
            "key": "sailor",
            "skin": LIGHT,
            "shade": TAN,
            "hair": HAIR_GOLDEN,
            "style": "cap",
            "cap": RED,
            "blush": True,
            "top": BLUE,
            "sleeve": BLUE,
            "stripe": (WHITE, WHITE),
            "legs": (BLUE[0], BLUE[0], BLUE[1]),
            "boots": (BLACK, CHARCOAL),
            "outfit": "striped",
            "leg_stripe": True,
        },
        {
            "key": "botanist",
            "skin": TAN,
            "shade": DEEP,
            "hair": HAIR_BROWN,
            "style": "bun",
            "glasses": True,
            "top": GREEN,
            "sleeve": (LIGHTGREY, WHITE, WHITE),
            "shirt": WHITE,
            "legs": PURPLE,
            "boots": (DARKBROWN, MIDBROWN),
            "outfit": "apron",
            "emblem": "flower",
            "short_sleeves": False,
            "light_cuffs": True,
        },
        {
            "key": "scout",
            "skin": DEEP,
            "shade": DARKBROWN,
            "hair": HAIR_GOLDEN,
            "style": "fringe",
            "blush": True,
            "top": (DARKORANGE, ORANGE, LIGHTYELLOW),
            "sleeve": GREEN,
            "shirt": GREEN,
            "legs": (DARKBROWN, MIDBROWN, BROWN),
            "boots": (DARKBROWN, MIDBROWN),
            "outfit": "vest",
            "emblem": "gear",
            "short_sleeves": True,
            "shorts": True,
        },
        {
            "key": "nomad",
            "skin": LIGHT,
            "shade": TAN,
            "hair": HAIR_DARK,
            "style": "long",
            "scarf": RED,
            "top": BLUE,
            "sleeve": BLUE,
            "legs": (DARKBROWN, MIDBROWN, BROWN),
            "boots": (SLATE, MIDGREY),
            "outfit": "coat",
            "band": (YELLOW, LIGHTYELLOW),
            "freckles": True,
        },
    ]
    skins = []
    for d in defs:
        outfit = {
            "style": d["outfit"],
            "top": d["top"],
            "sleeve": d["sleeve"],
            "legs": d["legs"],
            "boots": d["boots"],
            "skin": d["skin"],
            "shirt": d.get("shirt"),
            "stripe": d.get("stripe"),
            "scarf": d.get("scarf"),
            "band": d.get("band"),
            "emblem": d.get("emblem"),
            "backpack": d.get("backpack", False),
            "gloves": d.get("gloves"),
            "short_sleeves": d.get("short_sleeves", False),
            "light_cuffs": d.get("light_cuffs", False),
            "shorts": d.get("shorts", False),
            "knee_patch": d.get("knee_patch", False),
            "leg_stripe": d.get("leg_stripe", False),
        }
        if outfit["style"] == "vest":
            outfit["shirt"] = d["sleeve"]
        torso, arms, legs = body(outfit)
        face = draw_face(d)
        hair_index = d["cap"][1] if d.get("style") == "cap" else d["hair"][1]
        skins.append(
            {
                "key": d["key"],
                "author": "generated",
                "skin": d["skin"],
                "hair": hair_index,
                "torso": d["top"][1],
                "arms": d["sleeve"][1],
                "legs": d["legs"][1],
                "facePixels": face,
                "torsoPixels": torso,
                "armPixels": arms,
                "legPixels": legs,
                "helmetPixels": "",
            }
        )
    return skins


# ---------------------------------------------------------------- importing a painted skin


def nearest_index(color: dict | None, fallback: int) -> int:
    """The palette index nearest a Unity colour ({"r":0..1,"g":..,"b":..})."""
    if not isinstance(color, dict):
        return fallback
    rgb = tuple(round(float(color.get(k, 0.0)) * 255) for k in ("r", "g", "b"))
    best, best_d = fallback, None
    for i, p in enumerate(PALETTE):
        if p is None:
            continue
        d = sum((a - b) ** 2 for a, b in zip(rgb, p))
        if best_d is None or d < best_d:
            best, best_d = i, d
    return best


def skin_from_settings(
    path: Path,
    key: str,
    author: str,
    outfit_name: str | None,
    hair: int = HAIR_BROWN[1],
) -> dict:
    """A pool entry from a client_settings.json: the applied look, or the saved outfit of that name.

    A player's look has no hair (players wear the suit's helmet); the pioneer gets ``hair`` (a palette index, 0 = bald).
    """
    settings = json.loads(path.read_text(encoding="utf-8"))
    look = settings
    if outfit_name:
        outfits = [
            o for o in settings.get("Outfits", []) if o.get("Name") == outfit_name
        ]
        if not outfits:
            names = (
                ", ".join(o.get("Name", "?") for o in settings.get("Outfits", []))
                or "none"
            )
            raise SystemExit(
                f"no saved outfit named '{outfit_name}' in {path} (outfits: {names})"
            )
        look = outfits[0]
    skin = nearest_index(look.get("SkinColor"), LIGHT)
    face = (look.get("FacePixels") or "").lower()
    entry = {
        "key": key,
        "author": author,
        "skin": skin,
        "hair": hair,
        "torso": nearest_index(look.get("TorsoColor"), BLUE[1]),
        "arms": nearest_index(look.get("ArmColor"), BLUE[1]),
        "legs": nearest_index(look.get("LegColor"), SLATE),
        "facePixels": face,
        "torsoPixels": (look.get("TorsoPixels") or "").lower(),
        "armPixels": (look.get("ArmPixels") or "").lower(),
        "legPixels": (look.get("LegPixels") or "").lower(),
        "helmetPixels": (look.get("HelmetPixels") or "").lower(),
    }
    if len(face) == 16 * 16:  # an old 16x16 face: upscale it like the game does
        entry["facePixels"] = "".join(
            face[(y // 2) * 16 + x // 2] for y in range(SIZE) for x in range(SIZE)
        )
    return entry


# ---------------------------------------------------------------- the file


def validate(pool: dict) -> list[str]:
    """The content test's rules: unique keys, palette indices, exact painting lengths, palette symbols."""
    problems = []
    skins = pool.get("skins")
    if not isinstance(skins, list) or not skins:
        return ["the pool has no skins"]
    lengths = {
        "facePixels": SIZE * SIZE,
        "torsoPixels": 4 * SIZE * SIZE,
        "armPixels": 8 * SIZE * SIZE,
        "legPixels": 8 * SIZE * SIZE,
        "helmetPixels": 5 * SIZE * SIZE,
    }
    seen = set()
    for s in skins:
        key = s.get("key", "")
        if not key or not re.fullmatch(r"[a-z0-9_]+", key):
            problems.append(f"bad key {key!r}")
        if key in seen:
            problems.append(f"{key}: appears twice")
        seen.add(key)
        for prop in ("skin", "torso", "arms", "legs"):
            if not isinstance(s.get(prop), int) or not 1 <= s[prop] <= 31:
                problems.append(f"{key}: {prop} must be a palette index 1..31")
        if not isinstance(s.get("hair"), int) or not 0 <= s["hair"] <= 31:
            problems.append(f"{key}: hair must be a palette index 0..31")
        for prop, length in lengths.items():
            pixels = s.get(prop, "")
            if pixels and len(pixels) != length:
                problems.append(
                    f"{key}: {prop} has {len(pixels)} pixels, expected {length} (or none)"
                )
            if any(c not in ALPHABET for c in pixels):
                problems.append(f"{key}: {prop} holds a symbol outside the palette")
    return problems


def load_pool(pool: Path = POOL) -> dict:
    if pool.exists():
        return json.loads(pool.read_text(encoding="utf-8"))
    return {"skins": []}


def write_pool(skins: list[dict], pool: Path = POOL) -> None:
    lines = [
        "{",
        (
            '  "_comment": "Pixel skins the pioneers wear (#2682). Generated by tools/gen_pioneer_skins.py; add a skin '
            'painted in the game with its --from-settings (docs/developer/WILDERNESS_FOLK.md section 8).",'
        ),
        '  "skins": [',
    ]
    for i, s in enumerate(skins):
        body_text = json.dumps(s, ensure_ascii=False, separators=(", ", ": "))
        lines.append("    " + body_text + ("," if i < len(skins) - 1 else ""))
    lines += ["  ]", "}", ""]
    pool.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "--from-settings",
        type=Path,
        help="client_settings.json to take a painted skin from",
    )
    parser.add_argument(
        "--outfit",
        help="a saved outfit's name in that file (default: the applied look)",
    )
    parser.add_argument("--key", help="the new skin's key (a-z, 0-9, _)")
    parser.add_argument("--author", default="player", help="who painted it")
    parser.add_argument(
        "--hair",
        type=int,
        default=HAIR_BROWN[1],
        help="the pioneer's hair as a palette index (0 = bald; default 5 = brown)",
    )
    parser.add_argument(
        "--check", action="store_true", help="only validate the pool file"
    )
    parser.add_argument(
        "--pool",
        type=Path,
        default=POOL,
        help="the pool file (default: data/pioneer_skins.json)",
    )
    args = parser.parse_args()

    if args.check:
        problems = validate(load_pool(args.pool))
        for p in problems:
            print("  " + p)
        print("OK" if not problems else f"{len(problems)} problem(s)")
        return 0 if not problems else 1

    pool = load_pool(args.pool)
    generated = {s["key"]: s for s in generated_skins()}
    hand_made = [
        s
        for s in pool.get("skins", [])
        if s.get("author") != "generated" and s.get("key") not in generated
    ]
    if args.from_settings:
        if not args.key or not re.fullmatch(r"[a-z0-9_]+", args.key):
            raise SystemExit("--key is required: lower-case letters, digits and _ only")
        entry = skin_from_settings(
            args.from_settings, args.key, args.author, args.outfit, args.hair
        )
        hand_made = [s for s in hand_made if s.get("key") != args.key] + [entry]
        print(f"added '{args.key}' by {args.author}")

    skins = list(generated.values()) + hand_made
    problems = validate({"skins": skins})
    if problems:
        for p in problems:
            print("  " + p, file=sys.stderr)
        return 1
    write_pool(skins, args.pool)
    print(f"wrote {args.pool}: {len(skins)} skins ({len(hand_made)} hand-made)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
