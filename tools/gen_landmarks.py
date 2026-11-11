#!/usr/bin/env python3
"""Generates the shipped LANDMARK templates (#3483) into data/landmarks/<key>.json.

A landmark is one giant building that stands on a planet on its own, usually as a ruin the server derives from the
intact design (the shared ruin pass, a different ruin per seed). The files this script writes use the COMPACT template
body (#3485): a palette plus run-length rows ("y,z:x.count.palette x.count.palette"), with the cells that carry
settings (markers) listed one by one. They stay editable in the in-game structure editor (town editor, "Use as:
Landmark"): LOAD shows them, SAVE writes the same format.

Today the script writes three small SAMPLES, all `pinOnly` — never rolled for a world, only stamped by an admin with
`/landmark <key>` — so the framework can be play-tested before the school club's real buildings exist:

  sample_tower   a ruined watchtower with two chests, a sound and a den           (profile masonry)
  sample_mine    a hut over a shaft, tunnels and a cavern, carved into the rock   (profile underground, ground row)
  sample_store   an inhabited store with a keeper who only sells inside           (protected, as built)

Run it after editing (stdlib only):  uv run --no-project python tools/gen_landmarks.py
It rewrites the files it owns and writes LF line endings.
"""
from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "data" / "landmarks"


class Design:
    """A landmark under construction: plain cells (a block or a cavity) by position, plus marker cells."""

    def __init__(self, key: str, name: str, **header) -> None:
        self.header = {"key": key, "name": name, "landmark": True, **header}
        self.plain: dict[tuple[int, int, int], tuple] = {}
        self.markers: list[dict] = []

    # --- writing cells -------------------------------------------------------------------------------------------
    def block(self, x: int, y: int, z: int, block: str, tint: int = 0, glow: int = 0, shape: int = 0, keep: bool = False) -> None:
        self.plain[(x, y, z)] = ("block", block, tint, glow, shape, keep)

    def cavity(self, x: int, y: int, z: int) -> None:
        self.plain[(x, y, z)] = ("cavity", "", 0, 0, 0, False)

    def clear(self, x: int, y: int, z: int) -> None:
        self.plain.pop((x, y, z), None)

    def box(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, block: str, **kw) -> None:
        """A solid box, corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.block(x, y, z, block, **kw)

    def shell(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int, block: str, **kw) -> None:
        """The four walls of a box (no floor, no roof), corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    if x in (x0, x1) or z in (z0, z1):
                        self.block(x, y, z, block, **kw)

    def carve(self, x0: int, y0: int, z0: int, x1: int, y1: int, z1: int) -> None:
        """A cavity box (underground: tunnels and caverns), corners inclusive."""
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.cavity(x, y, z)

    def marker(self, x: int, y: int, z: int, marker: str, settings: str = "") -> None:
        cell = {"x": x, "y": y, "z": z, "kind": "marker", "id": marker}
        if settings:
            cell["config"] = settings
        self.markers.append(cell)

    # --- the file ------------------------------------------------------------------------------------------------
    def template(self) -> dict:
        xs = [p[0] for p in self.plain] + [m["x"] for m in self.markers]
        ys = [p[1] for p in self.plain] + [m["y"] for m in self.markers]
        zs = [p[2] for p in self.plain] + [m["z"] for m in self.markers]
        palette: list[dict] = []
        index: dict[tuple, int] = {}
        rows: list[str] = []
        cells = sorted(self.plain.items(), key=lambda kv: (kv[0][1], kv[0][2], kv[0][0]))
        i = 0
        while i < len(cells):
            (_, y, z), _ = cells[i]
            runs = []
            while i < len(cells) and cells[i][0][1] == y and cells[i][0][2] == z:
                (x, _, _), look = cells[i]
                count = 1
                while (i + count < len(cells) and cells[i + count][0] == (x + count, y, z) and cells[i + count][1] == look):
                    count += 1
                if look not in index:
                    index[look] = len(palette)
                    kind, block, tint, glow, shape, keep = look
                    entry = {"kind": kind, "id": block}
                    entry.update({k: v for k, v in (("tint", tint), ("glow", glow), ("shape", shape)) if v})
                    if keep:
                        entry["keep"] = True
                    palette.append(entry)
                runs.append(f"{x}.{count}.{index[look]}")
                i += count
            rows.append(f"{y},{z}:" + " ".join(runs))
        return {
            **self.header,
            "width": max(xs) + 1,
            "height": max(ys) + 1,
            "length": max(zs) + 1,
            "palette": palette,
            "rows": rows,
            "cells": self.markers,
        }

    def write(self) -> None:
        OUT.mkdir(parents=True, exist_ok=True)
        t = self.template()
        path = OUT / f"{t['key']}.json"
        with path.open("w", encoding="utf-8", newline="\n") as f:
            json.dump(t, f, ensure_ascii=False, separators=(",", ":"))
            f.write("\n")
        print(f"{path.name}: {t['width']}x{t['height']}x{t['length']}, {len(self.plain)} cells, "
              f"{len(t['palette'])} palette entries, {path.stat().st_size} bytes")


def sample_tower() -> Design:
    """A square watchtower, 16 x 41 x 16: brick walls, a floor every eight rows, windows, torches, a ladder."""
    d = Design("sample_tower", "Old Watchtower", nameKey="landmark.sample_tower.name", pinOnly=True, ruin="always",
               ruinProfile="masonry", requires=["atmosphere"])
    size, height = 16, 41
    for y in range(height):
        if y % 8 == 0:
            d.box(0, y, 0, size - 1, y, size - 1, "ancient_brick")
        else:
            d.shell(0, y, 0, size - 1, y, size - 1, "ancient_brick")
            if y % 8 == 4:
                for a in range(3, size - 3, 4):
                    for x, z in ((a, 0), (a, size - 1), (0, a), (size - 1, a)):
                        d.block(x, y, z, "glass")
            if y % 8 == 6:
                for x, z in ((size // 2, 1), (size // 2, size - 2)):
                    d.block(x, y, z, "torch")
    for y in range(1, height):  # the way up: a ladder in a corner, through a hole in every floor
        d.block(1, y, 1, "ladder")
    for y in (1, 2, 3):  # the doorway
        d.clear(size // 2, y, 0)
        d.clear(size // 2 - 1, y, 0)
    d.marker(4, 1, size - 4, "chest", "table=ruin_common")
    d.marker(size - 4, 33, 4, "chest", "table=ruin_rich;chance=0.7")
    d.marker(size // 2, 1, size // 2, "sound", "sound=amb_sounder;radius=64;volume=0.6")
    d.marker(size - 4, 9, size - 4, "den", "species=native;count=3;radius=10")
    return d


def sample_mine() -> Design:
    """A hut on the ground over a shaft, two tunnels and a cavern with stone pillars; 28 rows lie in the rock."""
    depth = 28
    d = Design("sample_mine", "Abandoned Mine", nameKey="landmark.sample_mine.name", pinOnly=True, ruin="always",
               ruinProfile="underground", groundRow=depth, requires=["atmosphere"])
    # The hut: a plank floor on the ground row, walls, a roof, a doorway.
    d.box(0, depth, 0, 9, depth, 9, "wood_planks")
    d.shell(0, depth + 1, 0, 9, depth + 3, 9, "wood_planks")
    d.box(0, depth + 4, 0, 9, depth + 4, 9, "wood_planks")
    for y in (depth + 1, depth + 2):
        d.clear(4, y, 0)
        d.clear(5, y, 0)
    d.block(8, depth + 2, 8, "torch")
    # The shaft with its ladder, from the hut floor down to the tunnel level.
    for y in range(3, depth + 1):
        d.cavity(5, y, 5)
        d.block(5, y, 6, "ladder")
    # Two tunnels (three high, three wide) with a timber frame every five blocks, and lamps.
    d.carve(4, 3, 4, 40, 5, 6)
    d.carve(20, 3, 6, 22, 5, 40)
    for x in range(8, 40, 5):
        for y in (3, 4):
            d.block(x, y, 4, "wood_log")
            d.block(x, y, 6, "wood_log")
        d.block(x, 5, 4, "wood_log")
        d.block(x, 5, 5, "wood_log")
        d.block(x, 5, 6, "wood_log")
    for x in range(10, 40, 10):
        d.block(x, 4, 5, "torch")
    # The cavern at the end of the second tunnel, with pillars of rock from floor to roof.
    d.carve(12, 3, 30, 34, 12, 46)
    for x, z in ((16, 34), (24, 42), (30, 36), (20, 40)):
        for y in range(3, 13):
            d.block(x, y, z, "stone")
    d.marker(30, 3, 44, "chest", "table=mine_ore")
    d.marker(38, 3, 5, "chest", "table=mine_ore;chance=0.5")
    return d


def sample_store() -> Design:
    """An inhabited roadside store, 14 x 7 x 12: a keeper behind a counter who sells only inside."""
    d = Design("sample_store", "Roadside Store", nameKey="landmark.sample_store.name", pinOnly=True, ruin="never",
               protected=True, requires=["atmosphere", "inhabited"])
    d.box(0, 0, 0, 13, 0, 11, "concrete")
    d.shell(0, 1, 0, 13, 4, 11, "wood_planks")
    d.box(0, 5, 0, 13, 5, 11, "wood_planks")
    for x in (3, 4, 9, 10):
        d.block(x, 2, 0, "glass")
        d.block(x, 3, 0, "glass")
    for y in (1, 2, 3):  # the doorway (two wide, three high) with a hinge door
        d.clear(6, y, 0)
        d.clear(7, y, 0)
    d.marker(6, 1, 0, "door_hinge")
    for x in range(3, 11):  # the counter
        d.block(x, 1, 7, "wood_planks")
    d.block(1, 4, 5, "lantern")
    d.block(12, 4, 5, "lantern")
    d.marker(7, 1, 9, "vendor", "theme=grocer;inside=1")
    return d


def main() -> None:
    for design in (sample_tower(), sample_mine(), sample_store()):
        design.write()


if __name__ == "__main__":
    main()
