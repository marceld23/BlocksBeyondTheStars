# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the block texture set (approved batch) via OpenAI images — seamless 64px pixel-art tiles.

One API call per texture; resumable (existing out/textures/<key>.png skipped) and tolerant of single
failures. After generating, run `bundle_textures.py --from-out` to write the chosen tiles into
client/Assets/Resources/textures as raw RGBA32 .bytes (the client decodes them with
Texture2D.LoadRawTextureData; LoadImage isn't available from the client asmdef). Logged in NOTICES.md.

Usage:
    uv run gen_textures.py
    uv run gen_textures.py --dry-run
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
import time
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out/textures")
STYLE = "seamless tileable pixel-art texture, top-down flat, retro 16-bit voxel game block tile, no text, no words"

TEXTURES = [
    ("stone", "rough grey stone rock surface with subtle cracks"),
    ("dirt", "brown soil dirt earth with small pebbles"),
    ("grass", "lush green grass top with blades"),
    ("sand", "fine yellow desert sand grains"),
    ("mud", "dark wet brown mud"),
    ("basalt", "dark grey volcanic basalt rock"),
    ("ice", "pale blue cracked ice"),
    ("iron_ore", "grey stone with rusty orange iron ore flecks"),
    ("copper_ore", "grey stone with bright orange copper ore veins"),
    ("titanium_ore", "grey stone with silvery white titanium ore flecks"),
    ("silicate", "pale sandy silicate mineral rock"),
    ("carbon", "black carbon coal rock with shiny flecks"),
    ("wood_log", "brown tree bark wood log surface with vertical wood grain"),
    ("tree_leaves", "dense leafy green tree foliage canopy with small overlapping leaves, top-down"),
    ("iron_wall", "grey sci-fi metal hull plate with rivets and panel seams"),
    ("crystal", "pale glowing blue crystal facets"),
    ("glass", "clear pale blue glass pane with a faint reflection"),
    ("lava", "dark cooled crust with glowing orange molten lava cracks"),
    ("water", "blue rippling water surface"),
    ("fire", "bright orange and yellow flames, licking tongues of fire with hot white centres, on a solid pure black background, top-down"),
    ("ash", "dark grey and black charred ash, soot and burnt embers, fine powdery texture"),
    # Item 21 — world variety: new surface + deep-crust blocks for the new world types.
    ("snow", "fresh clean white snow surface with a faint sparkle and soft powdery drifts"),
    ("salt", "white and pale grey crystalline salt flat crust with polygonal dried cracks"),
    ("mycelium", "purple-grey fungal mycelium soil threaded with pale filaments and tiny spores, top-down"),
    ("alien_grass", "alien turf of violet and magenta grass blades with a faint eerie glow, top-down"),
    ("deepslate", "very dark grey-blue deepslate stone with fine layered banding"),
    ("granite", "speckled pink and grey granite rock with glinting mineral flecks"),
    # Item 21 V3 — alien flora archetypes + giant-mushroom structure blocks.
    ("flora_tendril", "writhing alien tendril plant with violet finger-like fronds, top-down"),
    ("flora_bulb", "glowing bulbous alien pod plant with a luminous teal sac, top-down"),
    ("flora_gasbloom", "alien plant with translucent gas-filled bladders and pink veins, top-down"),
    ("flora_alienfern", "exotic alien fern with iridescent blue-violet feathery fronds, top-down"),
    ("flora_shardbloom", "flower of sharp glowing cyan crystal shards on a thin stem, top-down"),
    ("mushroom_stem", "pale fibrous giant mushroom stalk surface with vertical fibres"),
    ("mushroom_cap", "domed red-orange giant mushroom cap with white speckles, top-down"),
    ("flora_plant", "lush green leafy plant with fronds and small bush foliage, top-down"),
    ("flora_crystal", "cluster of glowing cyan crystal shards growing from rock"),
    # Complete flora set — biome-appropriate species (full colour; the atlas tints nothing).
    ("flora_fern", "lush green fern with feathery fronds, top-down"),
    ("flora_flower", "small wildflowers with pink and yellow blossoms on green stems, top-down"),
    ("flora_bush", "round leafy green bush with small red berries, top-down"),
    ("flora_vine", "tangled green climbing vines with leaves, top-down"),
    ("flora_mushroom", "cluster of small mushrooms with red caps and white stems, top-down"),
    ("flora_cactus", "green desert cactus with spines and small flowers, top-down"),
    ("flora_dryshrub", "dry brittle brown desert shrub with bare twigs, top-down"),
    ("flora_reed", "tall thin green and teal marsh reeds and cattails, top-down"),
    ("flora_glowcap", "bioluminescent glowing cyan mushrooms in the dark, top-down"),
    ("flora_frostflower", "pale icy blue crystalline frost flower with frosty petals, top-down"),
    ("flora_emberbloom", "charred dark plant with glowing orange ember blossoms, top-down"),
    # Aquatic flora (the planet flora tint re-colours these on the client; the tile gives the pattern).
    ("flora_kelp", "tall ribbon-like green seaweed kelp blades swaying underwater, top-down"),
    ("flora_lily", "round flat green lily pads floating on water with a small white blossom, top-down"),
    # Task 6 — more flora variety.
    ("flora_palm", "small tropical palm plant with broad green fan fronds, top-down"),
    ("flora_moss", "soft green moss patch carpeting rock with tiny fronds, top-down"),
    ("flora_orchid", "exotic purple and pink orchid blossoms on slender green stems, top-down"),
    ("flora_succulent", "fleshy green rosette succulent plant like aloe and agave, top-down"),
    ("flora_pitcher", "carnivorous pitcher plant with red-veined tubular traps, top-down"),
    ("flora_puffball", "cluster of round pale puffball mushrooms on a swamp floor, top-down"),
    ("flora_lichen", "crusty pale grey-green lichen growth spreading on frosty rock, top-down"),
    ("flora_coral", "branching pink and orange coral reef growth, top-down"),
    ("flora_seagrass", "tufts of bright green underwater seagrass blades on the seabed, top-down"),
    ("flora_sporepod", "bulbous alien spore pods with a faint glowing teal sheen, top-down"),
    ("flora_thornbush", "spiky tangled thorn bush with dark twigs and small leaves, top-down"),
    ("flora_bellflower", "drooping clusters of blue and violet bell-shaped flowers, top-down"),
    ("flora_ashweed", "scorched grey-black volcanic weed with brittle ashen stalks, top-down"),
    ("flora_glowvine", "bioluminescent glowing green-cyan vine with luminous leaves in the dark, top-down"),
    # Flora variety V2 — fill the thin biomes (rock / ice / snow / salt / ash) + signature tall grass.
    ("flora_grasstuft", "tall waving lush green grass blades in a tuft, top-down"),
    ("flora_rockflower", "hardy small flower with pink and white blossoms growing from grey rock, top-down"),
    ("flora_snowbush", "low hardy shrub with dark green leaves dusted in white snow and frost, top-down"),
    ("flora_icereed", "tall brittle pale blue frozen ice reeds and crystalline stalks, top-down"),
    ("flora_saltgrass", "wiry sparse pale grey-green salt grass tufts on a white salt crust, top-down"),
    ("flora_cinderbush", "charred dark volcanic bush with smouldering glowing orange embers, top-down"),
    # Distinct tree crowns for the new conifer + palm archetypes.
    ("pine_needles", "dense dark green pine conifer needle foliage canopy, small overlapping needles, top-down"),
    ("palm_frond", "broad green tropical palm tree fronds, long radiating feathered leaves, top-down"),
    # Task 5 — new metal / rare-earth / raw-resource ores (grey host rock with the metal's veins).
    ("gold_ore", "grey stone with bright shiny yellow gold ore veins"),
    ("silver_ore", "grey stone with bright metallic silver-white ore veins"),
    ("aluminium_ore", "reddish-brown bauxite rock with dull silvery aluminium ore flecks"),
    ("tin_ore", "grey stone with dull pale silvery tin ore flecks"),
    ("nickel_ore", "grey stone with pale greenish-silver nickel ore veins"),
    ("cobalt_ore", "grey stone with deep blue cobalt ore veins"),
    ("lithium_ore", "pale grey rock with soft pinkish-white lithium ore streaks"),
    ("uranium_ore", "dark grey rock with faintly glowing yellow-green uranium ore flecks"),
    ("platinum_ore", "grey stone with bright silvery-white platinum ore flecks"),
    ("lead_ore", "dark grey stone with dull bluish-grey lead ore flecks"),
    ("zinc_ore", "grey stone with bluish-silver zinc ore crystals"),
    ("tungsten_ore", "dark grey rock with hard dark metallic tungsten ore flecks"),
    ("sulfur_ore", "grey rock with bright yellow sulfur crystal deposits"),
    ("neodymium_ore", "dark grey rock with purple-grey rare-earth neodymium ore veins"),
    # Task 5 — craftable building blocks from the new alloys.
    ("steel_wall", "brushed steel sci-fi wall plate with panel seams and bolts"),
    ("bronze_block", "polished bronze metal block with a warm golden-brown sheen"),
    ("brass_block", "polished brass metal block with a bright yellow-gold sheen"),
    # Task 5 Stage 3 — buildable world objects (placeable functional + decorative blocks).
    ("workbench", "a sci-fi metal workbench worktop with hand tools, a vice and scattered parts, top-down"),
    ("forge", "a stone and metal forge furnace with glowing orange molten metal inside, top-down"),
    ("steel_floor", "an industrial steel floor grating panel with diamond tread plate, top-down"),
    ("metal_panel", "a riveted dark metal wall panel with seams and bolts, top-down"),
    ("concrete", "a plain light grey concrete surface with a subtle rough texture, top-down"),
    ("crate", "a sci-fi metal storage crate container box with a hinged lid and corner latches, top-down"),
    ("door_hinge", "a closed metal hinged door panel set in a frame, with a round handle and visible hinges, front view"),
    ("door_slide", "a closed sci-fi sliding blast door with a centre seam and glowing status light, front view"),
    ("asteroid_rock", "a rough grey pitted space asteroid rock surface, cratered stony texture with mineral flecks, seamless"),
    ("radio_beacon", "a sci-fi radio beacon transmitter tower, a slim metal pole on a base with a glowing cyan antenna ring and blinking status light, front view"),
    ("base_core", "a sci-fi base foundation cornerstone block, a carved grey stone slab with a glowing teal-cyan claim emblem and faint engraved energy lines, top-down"),
    ("beam_block", "a sci-fi teleporter pad, a dark metal floor plate with a glowing cyan hexagonal grid, concentric light rings and small status lights around the rim, top-down"),
    # Materialvielfalt — dead-end fixes + new tiers + metal storage blocks.
    ("detoxifier", "a sci-fi chemical detox station, a metal vat of bubbling glowing green fluid with pipes and valves, top-down"),
    ("diamond_ore", "dark grey rock with glittering pale-blue and white embedded diamond crystals"),
    ("insulated_wall", "a matte dark polymer-coated wall panel with rubber seams and rounded bolts, top-down"),
    ("iron_block", "a solid polished grey iron metal block, seamless"),
    ("copper_block", "a solid polished orange-brown copper metal block, seamless"),
    ("gold_block", "a solid polished bright yellow gold metal block, seamless"),
    ("silver_block", "a solid polished metallic silver-white metal block, seamless"),
    ("aluminium_block", "a solid brushed pale silver aluminium metal block, seamless"),
    ("tin_block", "a solid dull pale silvery tin metal block, seamless"),
    ("nickel_block", "a solid polished pale greenish-silver nickel metal block, seamless"),
    ("cobalt_block", "a solid polished deep blue cobalt metal block, seamless"),
    ("platinum_block", "a solid polished bright silvery-white platinum metal block, seamless"),
    ("lead_block", "a solid dull bluish-grey lead metal block, seamless"),
    ("zinc_block", "a solid bluish-silver zinc metal block, seamless"),
    ("tungsten_block", "a solid hard dark metallic grey tungsten metal block, seamless"),
    ("titanium_block", "a solid brushed silvery-grey titanium metal block, seamless"),
]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the block texture set (approved batch).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", default=None,
                    help="Generate only these comma-separated keys (e.g. a single approval test tile).")
    args = ap.parse_args()

    textures = TEXTURES
    if args.only:
        wanted = {k.strip() for k in args.only.split(",")}
        textures = [t for t in TEXTURES if t[0] in wanted]

    print(f"[tex] {len(textures)} textures")
    if args.dry_run:
        for i, (key, desc) in enumerate(textures, 1):
            print(f"  {i:2d}. {key:16s} {desc}")
        return

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key)
    OUT.mkdir(parents=True, exist_ok=True)

    done = skipped = failed = 0
    fails: list[str] = []
    total = len(textures)

    for i, (block, desc) in enumerate(textures, 1):
        out = OUT / f"{block}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{total}] {block}: skip (exists)")
            continue

        prompt = f"{STYLE}, of {desc}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024", quality="low", n=1)
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((64, 64), Image.NEAREST)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{total}] {block}: ok ({out.stat().st_size} bytes)")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {block}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            fails.append(block)

    print(f"\n[tex] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[tex] failed: " + ", ".join(fails))


if __name__ == "__main__":
    main()
