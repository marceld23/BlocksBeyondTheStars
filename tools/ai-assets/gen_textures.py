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
