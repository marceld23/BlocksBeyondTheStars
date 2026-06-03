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

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"


def _load(p):
    return json.loads(Path(p).read_text(encoding="utf-8")) if Path(p).exists() else []


def _dump(p, obj):
    Path(p).write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def _replace(items, key, entry):
    items = [e for e in items if e.get("key") != key]
    items.append(entry)
    return items


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
        "maxStack": b.get("maxStack", 99),
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
    for src, dst in (("consumeHealth", "consumeHealth"), ("consumeHunger", "consumeHunger"),
                     ("armor", "armorResistance"), ("oxygen", "oxygenBonus")):
        if b.get(src):
            item[dst] = b[src]
    if b.get("scan", 1) not in (1, 1.0):
        item["scanKnowledgeMultiplier"] = b["scan"]

    items = _replace(_load(DATA / "items.json"), key, item)
    _dump(DATA / "items.json", items)

    # ---- recipe ----
    recipe = {
        "key": f"{key}_recipe",
        "station": b.get("station", "workshop"),
        "inputs": [{"item": a["item"], "count": a["count"]} for a in b.get("inputs", [])],
        "outputs": [{"item": key, "count": b.get("outputCount", 1)}],
    }
    if b.get("hasBlueprint"):
        recipe["requiredBlueprint"] = f"{key}_bp"
    recipes = _replace(_load(DATA / "recipes.json"), f"{key}_recipe", recipe)
    _dump(DATA / "recipes.json", recipes)

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
        blueprints = _replace(_load(DATA / "blueprints.json"), f"{key}_bp", bp)
        _dump(DATA / "blueprints.json", blueprints)

    # ---- locale placeholders (only if missing) ----
    for code in ("en", "de"):
        p = DATA / "locales" / f"{code}.json"
        loc = json.loads(p.read_text(encoding="utf-8"))
        loc.setdefault(f"item.{key}.name", b.get("name", key))
        loc.setdefault(f"item.{key}.desc", b.get("desc", ""))
        if b.get("hasBlueprint"):
            loc.setdefault(f"blueprint.{key}_bp.name", f"{b.get('name', key)} blueprint")
            loc.setdefault(f"blueprint.{key}_bp.desc", b.get("desc", ""))
        _dump(p, loc)

    print(f"merged item '{key}' (+ recipe{' + blueprint' if b.get('hasBlueprint') else ''}) into data/.")
    print("review the diff, translate the placeholder locale strings, and commit.")


if __name__ == "__main__":
    main()
