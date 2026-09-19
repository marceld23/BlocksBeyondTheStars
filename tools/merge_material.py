# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Merge a material export bundle (from the in-game Material editor) into the game data.

The editor writes a bundle to <persistentDataPath>/material_exports/<key>/ containing:
  - material.json   -> mechanics, look, and world-placement settings
  - texture.bytes   -> a 64x64 RGBA32 raw tile (what you painted on the canvas)

This tool folds it into the repo so the material becomes a real, mineable, world-spawning block:

  - data/blocks.json                              -> the BlockDefinition (hardness, tool, drops, look)
  - data/items.json                               -> a matching item (so the block drops + can be placed)
  - data/planets.json                             -> an ore vein on every matching planet (by world type)
  - client/Assets/Resources/textures/<key>.bytes  -> the bundled block texture the atlas renders
  - data/locales/{en,de}.json                     -> placeholder block.<key>.name / item.<key>.name(.desc)

If material.json names a `sourceImage` PNG and Pillow is installed, that image is decoded + resized to
64x64 instead of the painted canvas. Otherwise the painted texture.bytes is used as-is.

Plain stdlib JSON (Pillow optional). The dev reviews the resulting diff, translates placeholders, commits.

Usage:
    python tools/merge_material.py <path-to-export-bundle-dir>
"""
import json
import shutil
import sys
from pathlib import Path

from content_edit import add_locale_key, load_entries, upsert_entry, upsert_ore_vein

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"
RES_TEX = REPO / "client" / "Assets" / "Resources" / "textures"
TILE = 64
# The block atlas has 32x32 = 1024 slots, but only the first band belongs to blocks: numeric block ids must stay
# below 400 (GameContent.AtlasTileCapacity / the client's AtlasBands.BlockEnd). Slot 0 is air.
BLOCK_SLOTS = 399


def _planet_matches(planet, world_type):
    atmo = (planet.get("atmosphere") or "toxic").lower()
    biomes = planet.get("biomes") or []
    if world_type == "airless":
        return atmo == "none"
    if world_type == "atmosphere":
        return atmo != "none"
    if world_type == "single_biome":
        return len(biomes) <= 1
    if world_type == "multi_biome":
        return len(biomes) >= 2
    return True  # "any"


def _write_texture(bundle, key, source_image):
    """Copy the painted tile (or decode the source PNG) into the bundled Resources folder."""
    RES_TEX.mkdir(parents=True, exist_ok=True)
    dst = RES_TEX / f"{key}.bytes"

    if source_image:
        try:
            from PIL import Image
        except ImportError:
            print(f"  ! sourceImage given but Pillow not installed; using the painted texture instead.")
            source_image = None
        else:
            img = Image.open(source_image).convert("RGBA").resize((TILE, TILE), Image.NEAREST)
            # LoadRawTextureData expects the first pixel to be the bottom-left row, so flip vertically.
            img = img.transpose(Image.FLIP_TOP_BOTTOM)
            dst.write_bytes(img.tobytes())
            print(f"  decoded sourceImage '{source_image}' -> {dst.relative_to(REPO)}")
            return

    raw = Path(bundle) / "texture.bytes"
    if not raw.exists():
        sys.exit(f"missing painted texture: {raw}")
    shutil.copyfile(raw, dst)
    print(f"  texture -> {dst.relative_to(REPO)}")


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/merge_material.py <export-bundle-dir>")

    bundle = Path(sys.argv[1])
    m = json.loads((bundle / "material.json").read_text(encoding="utf-8"))
    key = m["key"]

    # ---- block ----
    # `category` groups the block in the build palettes / crafting UI (every shipped block has one), and
    # `tintable` opts it into the always-available Dye/Glow + Shape actions.
    block = {
        "key": key,
        "nameKey": f"block.{key}.name",
        "category": m.get("category", "building"),
        "hardness": m.get("hardness", 3.0),
        "requiredTool": m.get("requiredTool", "none"),
        "minToolTier": m.get("minToolTier", 0),
        "drops": [{"item": key, "count": 1}],
        "gloss": round(m.get("gloss", 0.1), 3),
        "metal": round(m.get("metal", 0.0), 3),
        "emission": round(m.get("emission", 0.0), 3),
        "color": m.get("colorRgb", 0x8C8C91),
    }
    if m.get("tintable"):
        block["tintable"] = True
    existing = next((b for b in load_entries(DATA / "blocks.json") if b.get("key") == key), None)
    if existing is not None:
        # The editor cannot LOAD a block, so its form holds defaults, not this block's values. Re-merging an
        # existing key keeps what the form does not know (drops, flags, face slots, category) and only takes the
        # look the material editor is for (#1953) — it used to overwrite the whole definition.
        look = {k: block[k] for k in ("gloss", "metal", "emission", "color")}
        block = dict(existing)
        block.update(look)
        if m.get("tintable"):
            block["tintable"] = True
        print(f"  block '{key}' exists: kept its definition, updated the look only")
    is_new = upsert_entry(DATA / "blocks.json", key, block)
    count = len(load_entries(DATA / "blocks.json"))
    if is_new and count > BLOCK_SLOTS:
        print(f"  ! WARNING: {count} blocks exceed the {BLOCK_SLOTS} block slots of the texture atlas; "
              f"content validation will refuse to load. See AtlasBands in the client before adding more.")
    elif is_new:
        print(f"  atlas: {count}/{BLOCK_SLOTS} block slots used")

    # ---- item (so the block drops something + can be re-placed) ----
    item = {
        "key": key,
        "nameKey": f"item.{key}.name",
        "descriptionKey": f"item.{key}.desc",
        "category": "material",
        "maxStack": 1024,
        "placesBlock": key,
    }
    if is_new or not any(i.get("key") == key for i in load_entries(DATA / "items.json")):
        upsert_entry(DATA / "items.json", key, item)

    # ---- texture ----
    _write_texture(bundle, key, m.get("sourceImage"))

    if not is_new:
        print(f"updated the look + texture of existing block '{key}'; world placement and locales left alone.")
        return

    # ---- world placement (ore vein on every matching planet) ----
    world_type = m.get("worldType", "any")
    vein = {
        "block": key,
        "rarity": round(m.get("frequency", 0.06), 3),
        "minDepth": m.get("minDepth", 4),
        # Worlds run far deeper than the old 256 default since the worldgen overhaul; every shipped vein
        # goes to 2048, and a shallower band would keep the material out of most of the crust.
        "maxDepth": m.get("maxDepth", 2048),
    }
    touched, skipped = upsert_ore_vein(
        DATA / "planets.json", vein, lambda planet: _planet_matches(planet, world_type))
    if skipped:
        # orbital_station / ship_interior are not generated terrain and carry no ore list.
        print(f"  skipped (no ore list): {', '.join(skipped)}")

    # ---- locale placeholders (only if missing) ----
    for code in ("en", "de"):
        p = DATA / "locales" / f"{code}.json"
        add_locale_key(p, f"block.{key}.name", m.get("name", key))
        add_locale_key(p, f"item.{key}.name", m.get("name", key))
        add_locale_key(p, f"item.{key}.desc", m.get("desc", ""))

    print(f"merged material '{key}' into data/ (ore vein added to {len(touched)} planet(s), world type '{world_type}').")
    print("review the diff, translate the placeholder locale strings, and commit.")


if __name__ == "__main__":
    main()
