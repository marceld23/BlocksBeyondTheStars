# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Draws the Codex's Crystal Net circuit diagrams (#2259).

Each diagram is a side view on a block grid: the devices are the game's own item icons
(client/Assets/Resources/icons/item_<key>.png), conduits are violet bars, an ON wire glows,
arrows show which way a gate sends. The pictures carry no words — the article around them
names the parts — so one image serves every language.

Output: data/wiki/img/circuit_<name>.png (synced into StreamingAssets with the rest of data/,
read by the in-game Codex through an <img src="..."> tag in the article body).

Run from the repository root:  py tools/wiki/gen_circuit_diagrams.py
"""

from __future__ import annotations

import itertools
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ICONS = os.path.join(ROOT, "client", "Assets", "Resources", "icons")
OUT = os.path.join(ROOT, "data", "wiki", "img")

CELL = 64
MARGIN = 24
BG = (11, 22, 38, 255)
GRID = (28, 44, 66, 255)
GROUND = (92, 74, 52, 255)
GROUND_TOP = (122, 98, 66, 255)
WALL = (88, 96, 110, 255)
WALL_EDGE = (120, 128, 142, 255)
WIRE_OFF = (96, 70, 160, 255)
WIRE_ON = (178, 140, 255, 255)
GLOW = (178, 140, 255, 90)
ARROW = (120, 235, 255, 255)
LAMP_ON = (255, 226, 120, 255)
LAMP_OFF = (90, 86, 70, 255)
DOOR = (64, 190, 210, 255)
PLATFORM = (150, 160, 175, 255)
MOON = (230, 236, 255, 255)
SUN = (255, 200, 60, 255)


class Diagram:
    """A side-view block grid: x to the right, y up (row 0 at the bottom)."""

    def __init__(self, cols: int, rows: int):
        self.cols = cols
        self.rows = rows
        w = cols * CELL + 2 * MARGIN
        h = rows * CELL + 2 * MARGIN
        self.img = Image.new("RGBA", (w, h), BG)
        self.glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.draw = ImageDraw.Draw(self.img)
        for c in range(cols + 1):
            x = MARGIN + c * CELL
            self.draw.line([(x, MARGIN), (x, MARGIN + rows * CELL)], fill=GRID, width=1)
        for r in range(rows + 1):
            y = MARGIN + r * CELL
            self.draw.line([(MARGIN, y), (MARGIN + cols * CELL, y)], fill=GRID, width=1)

    def box(self, x: int, y: int) -> tuple[int, int, int, int]:
        left = MARGIN + x * CELL
        top = MARGIN + (self.rows - 1 - y) * CELL
        return left, top, left + CELL, top + CELL

    def centre(self, x: float, y: float) -> tuple[float, float]:
        return MARGIN + (x + 0.5) * CELL, MARGIN + (self.rows - 0.5 - y) * CELL

    def ground(self, y: int, x0: int = 0, x1: int | None = None) -> None:
        for x in range(x0, (self.cols if x1 is None else x1 + 1)):
            l, t, r, b = self.box(x, y)
            self.draw.rectangle([l, t, r - 1, b - 1], fill=GROUND)
            self.draw.rectangle([l, t, r - 1, t + 8], fill=GROUND_TOP)

    def wall(self, x: int, y: int) -> None:
        l, t, r, b = self.box(x, y)
        self.draw.rectangle([l, t, r - 1, b - 1], fill=WALL, outline=WALL_EDGE, width=2)

    def icon(self, key: str, x: int, y: int, faded: bool = False) -> None:
        path = os.path.join(ICONS, f"item_{key}.png")
        if not os.path.exists(path):
            sys.exit(f"missing icon: {path}")
        im = Image.open(path).convert("RGBA").resize((CELL - 8, CELL - 8), Image.LANCZOS)
        if faded:
            alpha = im.getchannel("A").point(lambda a: a * 35 // 100)
            im.putalpha(alpha)
        l, t, _, _ = self.box(x, y)
        self.img.alpha_composite(im, (l + 4, t + 4))

    def wire(self, cells: list[tuple[int, int]], on: bool) -> None:
        """A conduit line through these cells (each cell a conduit block)."""
        colour = WIRE_ON if on else WIRE_OFF
        for x, y in cells:
            l, t, r, b = self.box(x, y)
            self.draw.rectangle([l + 6, t + 6, r - 7, b - 7], fill=(40, 30, 70, 255), outline=colour, width=3)
            cx, cy = self.centre(x, y)
            self.draw.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], fill=colour)
            if on:
                gd = ImageDraw.Draw(self.glow)
                gd.rectangle([l - 4, t - 4, r + 4, b + 4], fill=GLOW)
        for (ax, ay), (bx, by) in itertools.pairwise(cells):
            if abs(ax - bx) + abs(ay - by) == 1:
                p, q = self.centre(ax, ay), self.centre(bx, by)
                self.draw.line([p, q], fill=colour, width=8)

    def link(self, a: tuple[int, int], b: tuple[int, int], on: bool) -> None:
        """A short wire stub between two touching cells (a device and its conduit)."""
        p, q = self.centre(*a), self.centre(*b)
        self.draw.line([p, q], fill=WIRE_ON if on else WIRE_OFF, width=6)

    def arrow(self, x: int, y: int, dx: int, dy: int) -> None:
        cx, cy = self.centre(x, y)
        ex, ey = cx + dx * CELL * 0.5, cy - dy * CELL * 0.5
        sx, sy = cx + dx * CELL * 0.18, cy - dy * CELL * 0.18
        self.draw.line([(sx, sy), (ex, ey)], fill=ARROW, width=5)
        px, py = -dy, -dx  # perpendicular in screen space
        tip = (ex + dx * 10, ey - dy * 10)
        self.draw.polygon([tip, (ex + px * 9, ey + py * 9), (ex - px * 9, ey - py * 9)], fill=ARROW)

    def lamp(self, x: int, y: int, on: bool) -> None:
        l, t, r, b = self.box(x, y)
        self.draw.rectangle([l + 10, t + 10, r - 11, b - 11], fill=(60, 60, 70, 255), outline=WALL_EDGE, width=2)
        cx, cy = self.centre(x, y)
        colour = LAMP_ON if on else LAMP_OFF
        self.draw.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], fill=colour)
        if on:
            gd = ImageDraw.Draw(self.glow)
            gd.ellipse([cx - 60, cy - 60, cx + 60, cy + 60], fill=(255, 226, 120, 70))

    def door(self, x: int, y: int, open_: bool) -> None:
        l, t, _, _ = self.box(x, y + 1)
        _, _, r, b = self.box(x, y)
        if open_:
            self.draw.rectangle([l + 4, t + 4, l + 14, b - 4], fill=DOOR)
        else:
            self.draw.rectangle([l + 14, t + 4, r - 15, b - 4], fill=DOOR, outline=(170, 240, 250, 255), width=2)

    def platform(self, x0: int, x1: int, y: int) -> None:
        for x in range(x0, x1 + 1):
            l, t, r, _ = self.box(x, y)
            self.draw.rectangle([l + 1, t + 40, r - 2, t + 60], fill=PLATFORM, outline=(200, 205, 215, 255), width=2)

    def moon(self, x: int, y: int) -> None:
        cx, cy = self.centre(x, y)
        self.draw.ellipse([cx - 14, cy - 14, cx + 14, cy + 14], fill=MOON)
        self.draw.ellipse([cx - 6, cy - 18, cx + 18, cy + 8], fill=BG)

    def sun(self, x: int, y: int) -> None:
        cx, cy = self.centre(x, y)
        self.draw.ellipse([cx - 12, cy - 12, cx + 12, cy + 12], fill=SUN)

    def save(self, name: str) -> None:
        glow = self.glow.filter(ImageFilter.GaussianBlur(10))
        out = Image.alpha_composite(self.img, glow)
        os.makedirs(OUT, exist_ok=True)
        path = os.path.join(OUT, f"circuit_{name}.png")
        out.convert("RGB").save(path, optimize=True)
        print(f"wrote {os.path.relpath(path, ROOT)}  ({os.path.getsize(path) // 1024} KB)")


def night_light() -> None:
    """A daylight sensor set to night, a wire, a lamp: it switches on by itself after dark."""
    d = Diagram(7, 4)
    d.ground(0)
    d.moon(1, 3)
    d.icon("daylight_sensor", 1, 1)
    d.wire([(2, 1), (3, 1), (4, 1), (4, 2)], on=True)
    d.link((1, 1), (2, 1), on=True)
    d.lamp(5, 2, on=True)
    d.link((4, 2), (5, 2), on=True)
    d.save("night_light")


def doorbell() -> None:
    """A step plate outside, a wire under the wall, a chime inside."""
    d = Diagram(8, 4)
    d.ground(0)
    for y in (1, 2, 3):
        d.wall(4, y)
    d.icon("step_plate", 1, 1)
    d.wire([(1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0)], on=False)
    d.link((1, 1), (1, 0), on=False)
    d.icon("chime", 6, 1)
    d.link((6, 0), (6, 1), on=False)
    d.save("doorbell")


def secret_door() -> None:
    """A hidden switch, a wire along the wall, two phase blocks that open together."""
    d = Diagram(8, 5)
    d.ground(0)
    for y in (1, 2, 3, 4):
        if y not in (1, 2):
            d.wall(5, y)
    d.icon("crystal_switch", 1, 1)
    d.wire([(1, 2), (1, 3), (2, 3), (3, 3), (4, 3)], on=True)
    d.link((1, 1), (1, 2), on=True)
    d.link((4, 3), (5, 3), on=True)
    d.icon("phase_block", 5, 1, faded=True)
    d.icon("phase_block", 5, 2, faded=True)
    d.save("secret_door")


def airlock() -> None:
    """One switch, two doors: the wire holds the inner door, a NOT gate locks the outer one the other way round."""
    d = Diagram(9, 5)
    d.ground(0)
    for y in (3, 4):
        d.wall(2, y)
        d.wall(6, y)
    d.door(2, 1, open_=True)
    d.door(6, 1, open_=False)
    # The switch touches the inner door; its wire climbs to a NOT block, which sends down into a short wire of its
    # own beside the outer door (two separate networks: ON holds the inner door open, the NOT's OFF locks the outer).
    d.icon("crystal_switch", 3, 1)
    d.wire([(3, 2), (3, 3), (4, 3)], on=True)
    d.link((3, 1), (3, 2), on=True)
    d.icon("logic_block", 5, 3)
    d.link((4, 3), (5, 3), on=True)
    d.arrow(5, 3, 0, -1)
    d.wire([(5, 2)], on=False)
    d.save("airlock")


def lift() -> None:
    """A lift motor at the bottom, the platform rides the shaft, a lift stop beside each floor."""
    d = Diagram(7, 8)
    d.ground(0)
    d.icon("lift_motor", 3, 0)
    for y in range(1, 8):
        d.wall(0, y)
        d.wall(6, y)
    d.ground(3, 0, 1)
    d.ground(6, 0, 1)
    d.ground(3, 5, 6)
    d.icon("lift_stop", 1, 4)
    d.icon("lift_stop", 1, 7)
    d.platform(2, 4, 3)
    for y in (2, 4, 5, 6):
        cx, cy = d.centre(3, y)
        d.draw.line([(cx, cy - 18), (cx, cy + 18)], fill=ARROW, width=3)
    d.save("lift")


def lucky_dice() -> None:
    """A button, a dice block that passes a pulse only now and then, a lamp that celebrates."""
    d = Diagram(8, 3)
    d.ground(0)
    d.icon("crystal_button", 1, 1)
    d.wire([(2, 1)], on=True)
    d.link((1, 1), (2, 1), on=True)
    d.icon("dice_block", 3, 1)
    d.link((2, 1), (3, 1), on=True)
    d.arrow(3, 1, 1, 0)
    d.wire([(4, 1), (5, 1)], on=False)
    d.lamp(6, 1, on=False)
    d.link((5, 1), (6, 1), on=False)
    d.save("lucky_dice")


def main() -> None:
    night_light()
    doorbell()
    secret_door()
    airlock()
    lift()
    lucky_dice()


if __name__ == "__main__":
    main()
