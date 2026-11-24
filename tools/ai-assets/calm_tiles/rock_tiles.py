# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Round 6 (2026-11-24): natural rock and ore tiles. A player found the calm rocks flat: granite had lost its mix of
pink, white and grey, and the ores read as paint on the rock (one form coloured with one or two flat colours,
build_tiles.py). This round redraws every rock and ore tile as natural stone made of up to three materials, each with
its own grain, and seats the ore in the rock (a darker rim, a lighter glint, shades of its own colour).

Steps (run from the repository root; the OpenAI key is read from tools/ai-assets/.env):

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/rock_tiles.py --generate [--only a,b]
    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/rock_tiles.py --build [--bundle]

--generate is PAID: one image-edit call per tile with the tile from BEFORE the calm set as the reference (its colours
are the ones players remember), or one generate call where a rock had no bundled tile (bedrock, obsidian were painted
in code). A picture that exists in out/calm_tiles/rocks is never generated again. --build reduces the pictures to 64 px,
heals the seams, keeps the mean colour of the previous tile (the far terrain and the map read it) and sets the contrast
to a share of the previous tile's; it writes the PNGs and the approval sheets (previous | now | new), and with --bundle
the .bytes into client/Assets/Resources/textures. Image generation costs money and is not reproducible; no test runs it.
"""
from __future__ import annotations

import argparse
import base64
import io
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from build_tiles import heal_rgb
from make_sheet import wall
from PIL import Image
from tilelib import OUT, PREVIOUS, REPO, SHEETS, TEXTURES, TILE, luma, sheet

RAW = OUT / "rocks"            # 1024 px pictures from the model (git-ignored)
BUILT = OUT / "rocks_built"    # finished 64 px tiles as PNG
CALM = OUT / "rocks_calm"      # the calm tiles this round replaces (copied before the first --bundle)

STYLE = ("in a soft hand-painted game style that still reads as real stone: natural mineral grains and gentle tonal "
         "variation inside every material, a little depth, moderate contrast, no harsh black lines, no pixel grid, no "
         "dithering, no noise, no outlines, no text. Even flat lighting straight from the front, no cast shadows, no "
         "light direction, no vignette. A seamless tileable texture that repeats without visible seams and looks the "
         "same from every side; the surface fills the whole image edge to edge")

EDIT = ("Redraw this pixel-art block texture as {what}, {style}. Keep the colours of the reference. Do not keep the "
        "cobblestone, brick or tile pattern of the reference: the rock is one continuous natural surface.")

ORE = ("natural grey rock with a fine grain of three greys (like plain stone, no cobbles) with the {ore} of the "
       "reference embedded in it as {shape}, in the colour of the reference: the deposits sit inside the rock with a "
       "slightly darker rim where they meet it, a soft lighter glint and two or three shades of their own colour, so "
       "they read as minerals grown in the stone, not as paint on its surface; about one fifth of the surface is ore")

SHAPES = {
    "flecks": "irregular nuggets and grains of different sizes",
    "veins": "branching veins of different widths with a few small grains beside them",
    "crystals": "small angular crystals of different sizes, some in little clusters",
    "streaks": "short streaks and lenses of different lengths",
}

# key -> what the surface is. Three materials where the rock has them; colours always from the reference.
ROCKS: dict[str, str] = {
    "granite": "granite made of three interlocking minerals: pink salmon feldspar crystals, milky white and light grey "
               "quartz grains and small dark grey to black mica flecks, evenly mixed in a coarse crystalline grain",
    "stone": "plain grey rock with a natural grain of three greys (mid grey, lighter grey and a few darker specks) and "
             "two or three faint shallow cracks",
    "basalt": "dark volcanic basalt with a fine dense grain of dark greys, a few tiny lighter mineral specks and small "
              "round gas pores",
    "deepslate": "dark slate rock with a fine dense grain, faint thin layers and a few tiny lighter specks",
    "sandstone": "sandstone of fine sand grains in warm sandy tones with soft bands of slightly different shades",
    "scree": "loose rock rubble seen from above: rounded and angular stones of different sizes and three or four "
             "shades packed together with darker gaps",
    "dripstone": "wet cave limestone with soft rounded flow ridges, a fine calcite grain and creamy and tan tones",
    "travertine": "travertine limestone with soft layers, small rounded pores and a fine grain in cream and beige tones",
    "coral_rock": "porous coral rock with round pores of different sizes and a fine chalky grain",
    "sulfur_stone": "crusty sulfur rock of rounded crystal lumps in several yellow tones with small darker pores",
    "moss_stone": "grey rock with a natural grain of three greys, partly overgrown by soft cushions of moss in two or "
                  "three greens",
    "tainted_stone": "grey rock with a natural grain of three greys, crossed by the thin poisoned veins of the reference",
    "silicate": "silicate rock of quartz and feldspar grains in light tones with a few darker specks, a coarse "
                "crystalline grain",
    "asteroid_rock": "rough asteroid rock with a fine grain, small shallow craters and a few tiny metallic specks",
}

# Rocks that had no bundled tile (painted in code): generated from text, with their colour named.
NEW_ROCKS: dict[str, str] = {
    "bedrock": "a seamless texture of very dark, dense bedrock: fused dark grey and near-black stone with a fine grain "
               "and a few lighter grey specks",
    "obsidian": "a seamless texture of obsidian, black volcanic glass with a deep violet cast: smooth curved "
                "conchoidal facets, faint glassy sheen and a few tiny grey specks",
}

# ore key -> (name in the prompt, shape)
ORES: dict[str, tuple[str, str]] = {
    "iron_ore": ("iron ore", "flecks"), "copper_ore": ("copper ore", "veins"), "titanium_ore": ("titanium ore", "flecks"),
    "gold_ore": ("gold", "veins"), "silver_ore": ("silver", "veins"), "aluminium_ore": ("aluminium ore", "flecks"),
    "tin_ore": ("tin ore", "flecks"), "nickel_ore": ("nickel ore", "veins"), "cobalt_ore": ("cobalt ore", "veins"),
    "lithium_ore": ("lithium ore", "streaks"), "uranium_ore": ("uranium ore", "flecks"),
    "platinum_ore": ("platinum", "flecks"), "lead_ore": ("lead ore", "flecks"), "zinc_ore": ("zinc ore", "crystals"),
    "tungsten_ore": ("tungsten ore", "flecks"), "sulfur_ore": ("sulfur", "crystals"),
    "neodymium_ore": ("neodymium ore", "veins"), "diamond_ore": ("diamonds", "crystals"),
    "carbon": ("carbon (coal)", "flecks"),
    "tainted_iron_ore": ("poisoned iron ore", "flecks"), "tainted_copper_ore": ("poisoned copper ore", "veins"),
    "tainted_titanium_ore": ("poisoned titanium ore", "flecks"), "tainted_diamond_ore": ("poisoned diamonds", "crystals"),
}

KEYS = list(ROCKS) + list(NEW_ROCKS) + list(ORES)

# Contrast of a new tile as a share of the previous tile's (luminance spread). The previous pixel-art tiles were loud;
# the calm set went to a sixth of that. 0.6 keeps the grain readable and stays calmer than before.
CONTRAST = 0.6
# An ore must stay as easy to spot as it is now: its spread is 0.85 x the previous one, and never below the calm tile's.
ORE_CONTRAST = 0.85
# Fallback spread for a rock without a previous tile (luminance standard deviation 0..1).
NEW_SPREAD = 0.05


def prompt_for(key: str) -> str:
    if key in NEW_ROCKS:
        return f"{NEW_ROCKS[key]}, {STYLE}."
    if key in ORES:
        name, shape = ORES[key]
        return EDIT.format(what=ORE.format(ore=name, shape=SHAPES[shape]), style=STYLE)
    return EDIT.format(what=ROCKS[key], style=STYLE)


def previous(key: str) -> Image.Image | None:
    path = PREVIOUS / f"{key}.bytes"
    if not path.exists():
        return None
    return Image.frombytes("RGBA", (TILE, TILE), path.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM).convert("RGB")


def current(key: str) -> Image.Image | None:
    """The calm tile this round replaces. Copied to out/calm_tiles/rocks_calm on first read, so a build after --bundle
    still compares with (and takes the ore contrast floor from) the calm tile, not with its own result."""
    keep = CALM / f"{key}.bytes"
    if not keep.exists():
        path = TEXTURES / f"{key}.bytes"
        if not path.exists():
            return None
        CALM.mkdir(parents=True, exist_ok=True)
        keep.write_bytes(path.read_bytes())
    return Image.frombytes("RGBA", (TILE, TILE), keep.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM).convert("RGB")


def generate(args: argparse.Namespace, keys: list[str]) -> None:
    RAW.mkdir(parents=True, exist_ok=True)
    jobs = [k for k in keys if not (RAW / f"{k}.png").exists()]
    print(f"[rocks] {len(keys)} keys, {len(jobs)} to generate, model {args.model}, quality {args.quality}")
    if args.dry_run:
        for k in jobs:
            print(f"  {k}: {prompt_for(k)}")
        return
    if not jobs:
        return

    from dotenv import load_dotenv
    load_dotenv(REPO / "tools" / "ai-assets" / ".env")
    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        sys.exit("OPENAI_API_KEY is not set.")
    from openai import OpenAI
    client = OpenAI(api_key=api_key)

    def run(k: str) -> str:
        last = ""
        for attempt in (1, 2, 3):
            try:
                ref = previous(k)
                if ref is None:
                    resp = client.images.generate(model=args.model, prompt=prompt_for(k), size="1024x1024",
                                                  quality=args.quality, n=1, background="opaque")
                else:
                    buf = io.BytesIO()
                    ref.resize((1024, 1024), Image.NEAREST).save(buf, format="PNG")
                    buf.seek(0)
                    buf.name = f"{k}.png"
                    resp = client.images.edit(model=args.model, image=buf, prompt=prompt_for(k), size="1024x1024",
                                              quality=args.quality, n=1)
                (RAW / f"{k}.png").write_bytes(base64.b64decode(resp.data[0].b64_json))
                return f"{k}: ok"
            except Exception as exc:  # noqa: BLE001
                last = str(exc)[:160]
                time.sleep(4 * attempt)
        return f"{k}: FAILED {last}"

    done = 0
    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        for line in pool.map(run, jobs):
            done += 1
            print(f"[{done}/{len(jobs)}] {line}", flush=True)


def spread(rgb: np.ndarray) -> float:
    return float(luma(rgb).std())


def build_one(key: str) -> Image.Image:
    """A 1024 px picture as a 64 px tile: seams healed, the previous mean colour, CONTRAST x the previous spread (an ore
    ORE_CONTRAST x, at least the calm tile's)."""
    a = np.asarray(Image.open(RAW / f"{key}.png").convert("RGB"), dtype=np.float64)
    a = heal_rgb(a, key)
    n = a.shape[0] // TILE
    small = a.reshape(TILE, n, TILE, n, 3).mean(axis=(1, 3))
    ref = previous(key)
    mean = small.reshape(-1, 3).mean(axis=0)
    if ref is not None:
        old = np.asarray(ref, dtype=np.float64)
        target_mean = old.reshape(-1, 3).mean(axis=0)
        target_spread = spread(old) * CONTRAST
        if key in ORES:
            calm = current(key)
            floor = spread(np.asarray(calm, dtype=np.float64)) if calm is not None else 0.0
            target_spread = max(spread(old) * ORE_CONTRAST, floor)
    else:
        target_mean = mean
        target_spread = NEW_SPREAD
    now = spread(small)
    k = target_spread / now if now > 1e-6 else 1.0
    out = target_mean + (small - mean) * k
    return Image.fromarray(np.clip(out + 0.5, 0, 255).astype(np.uint8), "RGB")


def build(args: argparse.Namespace, keys: list[str]) -> None:
    BUILT.mkdir(parents=True, exist_ok=True)
    tiles: dict[str, Image.Image] = {}
    for k in keys:
        if not (RAW / f"{k}.png").exists():
            print(f"{k}: no picture yet (run --generate)")
            continue
        tiles[k] = build_one(k)
        tiles[k].save(BUILT / f"{k}.png")

    blank = Image.new("RGB", (TILE * 6, TILE * 6), (28, 30, 36))
    rows = []
    for k, tile in tiles.items():
        prev, now = previous(k), current(k)
        rows.append((k, [wall(prev) if prev else blank, wall(now) if now else blank, wall(tile)]))
    for i in range(0, len(rows), 10):
        sheet(rows[i:i + 10], ["before the look (v2026.11.26)", "now (calm set)", "NEW (proposal)"],
              SHEETS / f"rocks-{i // 10 + 1}.png", "Rock and ore tiles: 3 x 3 blocks each", label_w=200)

    if args.bundle:
        for k, tile in tiles.items():
            raw = tile.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes()
            assert len(raw) == TILE * TILE * 4
            (TEXTURES / f"{k}.bytes").write_bytes(raw)
        print(f"bundled {len(tiles)} tiles into {TEXTURES}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--generate", action="store_true", help="PAID: make the missing pictures")
    ap.add_argument("--build", action="store_true", help="build the 64 px tiles and the sheets")
    ap.add_argument("--bundle", action="store_true", help="with --build: write the .bytes into the game's textures")
    ap.add_argument("--only", default=None, help="comma-separated keys (default: every rock and ore)")
    ap.add_argument("--model", default="gpt-image-2.6-sunburst")
    ap.add_argument("--quality", default="low")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    keys = [k.strip() for k in args.only.split(",")] if args.only else KEYS
    unknown = [k for k in keys if k not in KEYS]
    if unknown:
        sys.exit(f"unknown keys: {', '.join(unknown)}")
    if args.generate:
        generate(args, keys)
    if args.build:
        build(args, keys)


if __name__ == "__main__":
    main()
