# /// script
# requires-python = ">=3.11"
# ///
"""Filter a todo-<locale>.json down to real, visitor-facing content.

Drops known template leftovers: items whose schema no longer exists on the
site (deleted template collections), the template job-application form, the
Translation-Manager pseudo page title and "Image Title" placeholder values.

Usage: uv run curate_todo.py es fr ...   (writes out/todo-<locale>-curated.json)
"""
import json
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
OUT = Path(__file__).resolve().parent / "out"

TEMPLATE_ENTITIES = {
    "fde85b74-ac43-4dc1-aa38-c47fee4407fb",  # Bewerbungsformular (template job form)
    "masterPage",
}
PLACEHOLDER_TEXTS = {"Image Title"}


def curate(locale: str) -> None:
    todo = json.loads((OUT / f"todo-{locale}.json").read_text(encoding="utf-8"))
    curated = []
    for item in todo:
        if item["schema"] == "unknown schema" or item["entityId"] in TEMPLATE_ENTITIES:
            continue
        fields = {k: f for k, f in item["fields"].items()
                  if f["text"].strip() not in PLACEHOLDER_TEXTS}
        if fields:
            curated.append({**item, "fields": fields})
    n_fields = sum(len(i["fields"]) for i in curated)
    chars = sum(len(f["text"]) for i in curated for f in i["fields"].values())
    (OUT / f"todo-{locale}-curated.json").write_text(
        json.dumps(curated, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{locale}: {len(curated)} items, {n_fields} fields, {chars} chars")


for loc in sys.argv[1:]:
    curate(loc)
