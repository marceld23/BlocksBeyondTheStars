# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Merge an item+recipe export bundle (from the in-game Item & Recipe editor) into the game data.

The editor writes a bundle to <persistentDataPath>/content_exports/<key>/content.json. This tool folds
it into the repo's data so the designed item becomes a real, craftable item:

  - data/items.json            -> the ItemDefinition (with tool/effect stats)
  - data/recipes.json          -> the RecipeDefinition (<key>_recipe)
  - data/blueprints.json       -> the BlueprintDefinition (<key>_bp), only if blueprint gating is on
  - data/locales/{en,de}.json  -> placeholder item.<key>.name/.desc (+ blueprint names) if missing

Plain stdlib JSON (no deps). The dev reviews the resulting diff and commits it.

Usage:
    python tools/merge_recipe.py <path-to-export-bundle-dir>
"""
import json
import sys
from pathlib import Path

from content_edit import add_locale_key, upsert_entry

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/merge_recipe.py <export-bundle-dir>")

    b = json.loads((Path(sys.argv[1]) / "content.json").read_text(encoding="utf-8"))
    key = b["key"]

    # ---- item ----
    item = {
        "key": key,
        "nameKey": f"item.{key}.name",
        "descriptionKey": f"item.{key}.desc",
        "category": b.get("category", "material"),
        "maxStack": b.get("maxStack", 1024),
    }
    if b.get("placesBlock"):
        item["placesBlock"] = b["placesBlock"]
    if b.get("category") == "tool":
        item["tool"] = {
            "kind": b.get("toolKind", "drill"),
            "tier": b.get("tier", 1),
            "miningPower": b.get("miningPower", 1),
            "damage": b.get("damage", 0),
            "range": b.get("range", 0),
            "energyPerUse": b.get("energy", 0),
        }
        # Optional tool properties: only written when set, so the JSON stays as terse as the shipped entries.
        if b.get("miningRadius"):
            item["tool"]["miningRadius"] = b["miningRadius"]
        if b.get("cooldownSeconds"):
            item["tool"]["cooldownSeconds"] = b["cooldownSeconds"]
    for src, dst in (("consumeHealth", "consumeHealth"), ("consumeHunger", "consumeHunger"),
                     ("armor", "armorResistance"), ("oxygen", "oxygenBonus")):
        if b.get(src):
            item[dst] = b[src]
    if b.get("scan", 1) not in (1, 1.0):
        item["scanKnowledgeMultiplier"] = b["scan"]

    upsert_entry(DATA / "items.json", key, item)

    # ---- recipe ----
    recipe = {
        "key": f"{key}_recipe",
        "station": b.get("station", "workshop"),
        "inputs": [{"item": a["item"], "count": a["count"]} for a in b.get("inputs", [])],
        "outputs": [{"item": key, "count": b.get("outputCount", 1)}],
    }
    # Market barters are posted per vendor theme; empty means every vendor offers it.
    if b.get("station") == "market" and b.get("marketTheme"):
        recipe["marketTheme"] = b["marketTheme"]
    if b.get("hasBlueprint"):
        recipe["requiredBlueprint"] = f"{key}_bp"
    upsert_entry(DATA / "recipes.json", f"{key}_recipe", recipe)

    # ---- blueprint (optional) ----
    if b.get("hasBlueprint"):
        bp = {
            "key": f"{key}_bp",
            "nameKey": f"blueprint.{key}_bp.name",
            "descriptionKey": f"blueprint.{key}_bp.desc",
            "category": "Custom",
            "prerequisites": [],
            "unlockCost": [{"item": a["item"], "count": a["count"]} for a in b.get("unlockCost", [])],
            "knowledgeCost": b.get("knowledgeCost", 0),
        }
        upsert_entry(DATA / "blueprints.json", f"{key}_bp", bp)

    # ---- locale placeholders (only if missing) ----
    for code in ("en", "de"):
        p = DATA / "locales" / f"{code}.json"
        add_locale_key(p, f"item.{key}.name", b.get("name", key))
        add_locale_key(p, f"item.{key}.desc", b.get("desc", ""))
        if b.get("hasBlueprint"):
            add_locale_key(p, f"blueprint.{key}_bp.name", f"{b.get('name', key)} blueprint")
            add_locale_key(p, f"blueprint.{key}_bp.desc", b.get("desc", ""))

    print(f"merged item '{key}' (+ recipe{' + blueprint' if b.get('hasBlueprint') else ''}) into data/.")
    print("review the diff, translate the placeholder locale strings, and commit.")
    if not b.get("placesBlock"):
        # A block-placing item borrows its block's atlas tile (IconResolver); anything else needs a real icon
        # or it shows up in the inventory with the generic category fallback.
        print(f"  ! no icon yet: generate client/Assets/Resources/icons/item_{key}.png "
              f"(see tools/ai-assets/gen_icons.py) or the item shows a placeholder icon.")


if __name__ == "__main__":
    main()
