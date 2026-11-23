# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 1 (PAID): generates the greyscale forms and the masks of the calm ground, rock, bark, plank and metal tiles
with the OpenAI image API.

The pictures are CANDIDATES for a contact sheet: they are written to tools/ai-assets/out/calm_tiles/forms/ (git-ignored)
and never into the game. A candidate that exists is never generated again (no second bill, nothing overwritten). The
tiles carry form only: every prompt asks for a greyscale surface; colour is added afterwards from a ramp
(make_sheet.py, build_tiles.py). Image generation costs money and is not reproducible; no test runs this.

Run from the repository root (the key is read from tools/ai-assets/.env):
    uv run --project tools/ai-assets python tools/ai-assets/calm_tiles/gen_calm_tiles.py \
        --only sand,stone --variants a,b [--model gpt-image-2.6-sunburst] [--quality low] [--dry-run]
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
RAW = REPO / "tools" / "ai-assets" / "out" / "calm_tiles" / "forms"

STYLE = ("seamless tileable game texture that repeats without visible seams, flat orthographic view straight onto the "
         "surface, perfectly even lighting with no directional light, no cast shadows and no vignette, black-and-white "
         "greyscale only with no colour at all, stylised soft hand-painted look like smooth matte modelling clay, low "
         "contrast, mid-grey overall, only a few large soft shapes, no fine detail, no grit, no noise, no pixel art, "
         "no outlines, no text, the surface fills the whole image edge to edge")

# key -> variant -> what the surface is. Two motifs per tile, so there is a choice on the contact sheet.
TILES: dict[str, dict[str, str]] = {
    "sand": {
        "a": "fine smooth sand with a few very soft shallow wind ripples",
        "b": "fine smooth sand, almost plain, with a handful of tiny soft round dimples scattered loosely",
    },
    "stone": {
        "a": "smooth weathered rock with a few broad soft lighter and darker patches and two faint hairline cracks",
        "b": "smooth rock made of four or five large rounded slabs fitted together, soft shallow grooves between them",
    },
    "dirt": {
        "a": "soft crumbly earth with a few large rounded clumps and three or four small smooth pebbles",
        "b": "soft packed earth, almost plain, with gentle broad lumps and a few tiny round holes",
    },
    "grass": {
        "a": "a short dense lawn seen from straight above, soft rounded tufts of grass in gentle light and dark patches",
        "b": "a mossy soft lawn seen from straight above, a few big soft cushions of grass with slightly darker gaps",
        "c": "a soft velvety lawn seen from straight above, almost plain, with gentle cloudy lighter and darker areas "
             "and a few tiny soft tufts",
    },
    "snow": {
        "a": "smooth fresh snow with two or three very soft shallow drifts",
        "b": "smooth fresh snow, almost plain, with a few tiny soft dimples and one gentle wave",
        # The game turns a ground block's top face in 90 degree steps, so a motif with a direction becomes a patchwork.
        "c": "smooth fresh snow, almost plain, with a few broad soft round hollows and gentle cloudy shading, no "
             "lines, no waves, no ripples, looking the same from every side",
    },
    "wood_log": {
        "a": "tree bark with five or six broad vertical ridges and soft deep vertical furrows between them, the ridges "
             "run from the top edge to the bottom edge",
        "b": "tree bark made of large chunky rectangular bark plates stacked in vertical columns with soft dark gaps "
             "between the plates",
        "c": "smooth tree bark, almost plain, with three or four long soft vertical grooves of different lengths and "
             "one small round knot, the grooves run vertically",
    },
    "basalt": {
        "a": "volcanic basalt rock made of a few large angular facets with soft bevelled edges",
        "b": "volcanic rock with large hexagonal column tops fitted together like a honeycomb, soft shallow joints",
        "c": "smooth volcanic rock, almost plain, with a few broad soft dents and two soft rounded ridges",
    },
    "mud": {
        "a": "wet smooth mud with a few soft rounded bumps and two shallow puddle shapes",
        "b": "drying mud with a few large soft polygonal plates and shallow rounded cracks between them",
    },
    # ---- round 2 (2026-11-22): the other single-material grounds and rocks, and the other barks. One motif each.
    # Lessons of round 1: the game turns the top of stone-like ground in 90 degree steps, so those motifs must look the
    # same from every side; "almost plain" reads calmest; a bark must run from the top edge to the bottom edge.
    "deepslate": {"a": "dense dark slate rock, almost plain, with a few broad soft flat facets and gentle cloudy "
                       "shading, no lines, looking the same from every side"},
    "granite": {"a": "smooth granite, almost plain, with soft round flecks of slightly lighter and darker grey "
                     "scattered evenly, looking the same from every side"},
    "sandstone": {"a": "soft sandstone with three or four broad very soft horizontal sediment bands of slightly "
                       "different brightness, the bands run from the left edge to the right edge"},
    "scree": {"a": "loose rock rubble seen from above, a handful of large soft rounded stones of different sizes "
                   "packed together with soft darker gaps between them"},
    "ash": {"a": "soft powdery ash, almost plain, with gentle cloudy lighter and darker areas and a few tiny soft "
                 "crumbs"},
    "salt": {"a": "a dry salt flat crust seen from above, a few large soft polygonal plates with shallow soft "
                  "raised ridges between them"},
    "peat": {"a": "soft dark peat soil, almost plain, with a few soft fibrous clumps and tiny round holes"},
    "coral_rock": {"a": "porous coral rock with soft round pores of different sizes scattered loosely, smooth between "
                        "the pores"},
    "sulfur_stone": {"a": "crusty mineral rock made of soft rounded crystal lumps of different sizes with a few "
                          "small darker pores between them"},
    "ice": {"a": "smooth ice, almost plain, with two or three long soft cracks and gentle cloudy lighter areas"},
    "alien_grass": {"a": "soft alien turf seen from straight above, small soft round cushions and buds of different "
                         "sizes packed loosely, gentle light and dark patches"},
    "mycelium": {"a": "soft fungal ground seen from above, almost plain, with a loose net of a few soft pale threads "
                      "and tiny soft dots"},
    "wood_log_look2": {"a": "deeply furrowed tree bark with long interlacing vertical ridges that form narrow tall "
                            "diamond shapes, soft deep furrows, the ridges run from the top edge to the bottom edge"},
    "wood_log_look3": {"a": "smooth tree bark, almost plain, with a few short soft horizontal marks and gentle "
                            "vertical shading"},
    "wood_log_look4": {"a": "smooth tree bark with soft horizontal bands and a few wide soft horizontal stripes "
                            "like the bark of a cherry tree, the bands run from the left edge to the right edge"},
    "wood_log_look5": {"a": "fibrous tree bark made of many long soft vertical strips of different widths that "
                            "overlap slightly, the strips run from the top edge to the bottom edge"},
    "wood_log_look6": {"a": "tree bark made of large irregular rounded plates fitted together like puzzle pieces "
                            "with soft darker gaps between the plates"},
    "giant_log": {"a": "the bark of an ancient giant tree with very deep wide vertical furrows and thick rounded "
                       "ridges, the ridges run from the top edge to the bottom edge"},
    "mushroom_stem": {"a": "the smooth soft stalk of a giant mushroom, almost plain, with faint long vertical fibres "
                           "running from the top edge to the bottom edge"},
}

# ---- round 3 (2026-11-22): replacements for weak candidates of round 2, more
# single-material tiles, and MASKS. A mask is a black-and-white picture that says where a tile's second material
# lies (ore in rock, moss on stone, the marks of a birch): form x two colour slots instead of a coloured picture.
TILES.update({
    "deepslate_r": {"a": "dense dark rock, almost plain, with three or four broad soft rounded bumps and gentle "
                         "cloudy shading, no lines, no waves, no ripples, looking the same from every side"},
    "wood_log_look3_r": {"a": "smooth tree bark, almost plain, with six or seven short soft horizontal marks of "
                              "different lengths scattered loosely, no vertical lines"},
    "bone": {"a": "a dense heap of old dry bones seen from above, a few large soft rounded bone ends and shafts "
                  "packed together with soft darker gaps"},
    "dripstone": {"a": "smooth wet cave rock, almost plain, with a few soft rounded flow ridges and drip bumps"},
    "tar": {"a": "thick tar, almost plain, with two or three soft slow bubbles and gentle smooth swells"},
    "travertine": {"a": "soft travertine stone with a few broad very soft horizontal layers and small rounded "
                        "pores, the layers run from the left edge to the right edge"},
    "concrete": {"a": "a plain concrete surface, almost plain, with gentle cloudy shading and a few tiny soft pores"},
    "wood_planks": {"a": "four horizontal wooden planks stacked on top of each other, each plank spans the full "
                         "width from the left edge to the right edge, soft shallow gaps between the planks, faint "
                         "long soft wood grain along each plank"},
    "metal_block": {"a": "a smooth polished metal plate, almost plain, with a very soft broad diagonal sheen and a "
                         "subtle soft rounded bevel along all four edges"},
    "crystal": {"a": "large angular crystal facets fitted together like the surface of a cut gem, soft flat faces "
                     "of slightly different brightness"},
    # The metal blocks differ by FORM where their colours are close (six of them are greys).
    "metal_brushed": {"a": "a smooth brushed metal plate, almost plain, with faint long soft horizontal brushing "
                           "lines and a subtle soft rounded bevel along all four edges"},
    "metal_panels": {"a": "a metal plate divided into four equal square panels by two soft shallow grooves that "
                          "cross in the middle, a subtle soft rounded bevel along all four edges"},
    "metal_studs": {"a": "a smooth metal plate with one soft round stud in each of its four corners and a subtle "
                         "soft rounded bevel along all four edges"},
    "mask_veins": {"a": "a few thick winding veins that branch once or twice like rivers on a map, covering about "
                        "one fifth of the image"},
    "mask_flecks": {"a": "about twelve soft rounded nuggets of different sizes scattered loosely, covering about one "
                         "fifth of the image"},
    "mask_crystals": {"a": "about eight angular crystal shapes like small cut gems of different sizes scattered "
                           "loosely, covering about one fifth of the image"},
    "mask_streaks": {"a": "five or six soft diagonal streaks of different lengths, covering about one fifth of the "
                          "image"},
    "mask_patches": {"a": "three or four large soft irregular patches like cushions of moss, covering about one "
                          "third of the image"},
    "mask_speckles": {"a": "many small soft round speckles of different sizes scattered evenly, covering about one "
                           "third of the image"},
    "mask_birch": {"a": "the dark marks of birch bark: several short thin horizontal dashes of different lengths "
                        "and two or three eye-shaped knots, covering about one eighth of the image"},
    "mask_poplar": {"a": "the marks of poplar bark: a few small diamond shapes and short horizontal dashes arranged "
                         "in loose vertical rows, covering about one eighth of the image"},
    "mask_flakes": {"a": "the patches of peeling plane tree bark: five or six large irregular rounded flakes like "
                         "camouflage, covering about half of the image"},
    "mask_drips": {"a": "a few soft drips and runs flowing straight down from the top edge, of different lengths, "
                        "covering about one fifth of the image"},
    "mask_tufts": {"a": "about ten small star-shaped tufts like little grass rosettes scattered loosely, covering "
                        "about one fifth of the image"},
    "mask_threads": {"a": "a loose net of thin soft threads that branch and join, covering about one sixth of the "
                          "image"},
    "mask_puddles": {"a": "a few soft round puddles of different sizes and two thin veins joining them, covering "
                          "about one fifth of the image"},
})

MASK_STYLE = ("seamless tileable black and white mask texture that repeats without visible seams, flat orthographic "
              "view, pure black background, the shapes in pure white with soft slightly blurred edges, no grey "
              "shading, no texture, no noise, no outlines, no text, fills the whole image edge to edge")

ROUND2 = ["deepslate", "granite", "sandstone", "scree", "ash", "salt", "peat", "coral_rock", "sulfur_stone", "ice",
          "alien_grass", "mycelium", "wood_log_look2", "wood_log_look3", "wood_log_look4", "wood_log_look5",
          "wood_log_look6", "giant_log", "mushroom_stem"]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--only", default=None, help="comma-separated tile keys (default: all)")
    ap.add_argument("--variants", default="a,b")
    ap.add_argument("--model", default="gpt-image-2.6-sunburst")
    ap.add_argument("--quality", default="low")
    ap.add_argument("--tag", default="", help="suffix of the file name, to keep a model or quality test apart")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    keys = [k.strip() for k in args.only.split(",")] if args.only else list(TILES)
    variants = [v.strip() for v in args.variants.split(",")]
    jobs = [(k, v) for k in keys for v in variants if v in TILES[k]]
    print(f"[calm] {len(jobs)} candidates, model {args.model}, quality {args.quality}")
    if args.dry_run:
        for k, v in jobs:
            print(f"  {k}_{v}: {STYLE}, of {TILES[k][v]}")
        return

    from dotenv import load_dotenv
    load_dotenv(REPO / "tools" / "ai-assets" / ".env")
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    client = OpenAI(api_key=key)
    RAW.mkdir(parents=True, exist_ok=True)

    made = skipped = 0
    fails: list[str] = []
    for i, (k, v) in enumerate(jobs, 1):
        out = RAW / f"{k}_{v}{args.tag}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{len(jobs)}] {out.name}: skip (exists)")
            continue
        prompt = f"{MASK_STYLE}: {TILES[k][v]}" if k.startswith("mask_") else f"{STYLE}, of {TILES[k][v]}"
        for attempt in (1, 2):
            try:
                started = time.time()
                resp = client.images.generate(model=args.model, prompt=prompt, size="1024x1024",
                                              quality=args.quality, n=1, background="opaque")
                out.write_bytes(base64.b64decode(resp.data[0].b64_json))
                usage = getattr(resp, "usage", None)
                tokens = f", tokens {usage.input_tokens}+{usage.output_tokens}" if usage else ""
                print(f"[{i}/{len(jobs)}] {out.name}: ok ({time.time() - started:.0f} s{tokens})")
                made += 1
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{len(jobs)}] {out.name}: attempt {attempt} failed: {exc}")
                time.sleep(2)
        else:
            fails.append(out.name)

    print(f"[calm] made={made} skipped={skipped} failed={len(fails)}")
    if fails:
        print("[calm] failed: " + ", ".join(fails))


if __name__ == "__main__":
    main()
