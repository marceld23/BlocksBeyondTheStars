# /// script
# requires-python = ">=3.11"
# ///
"""Print full texts for given entity ids (helper for translating).

Usage: uv run extract_texts.py [--locale de] comp-l0jod52i comp-mri7q69m ...
With no entity ids: prints all Site Pages (page titles) and Form field values.
"""
import json
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

args = sys.argv[1:]
locale = "de"
if args and args[0] == "--locale":
    locale = args[1]
    args = args[2:]
sys.argv = [sys.argv[0], *args]

OUT = Path(__file__).resolve().parent / "out"
contents = json.loads((OUT / f"contents-{locale}.json").read_text(encoding="utf-8"))
schemas = json.loads((OUT / "schemas.json").read_text(encoding="utf-8"))

wanted = set(sys.argv[1:])
for c in contents:
    schema = schemas.get(c["schemaId"], {})
    label = schema.get("displayName") or schema.get("key", {}).get("entityType", "?")
    if wanted and c["entityId"] not in wanted:
        continue
    if not wanted and label not in ("Site Pages", "Form"):
        continue
    print(f"=== {label} | schema={c['schemaId']} | entity={c['entityId']} ===")
    for key, f in c.get("fields", {}).items():
        tv = f.get("textValue")
        if tv and tv.strip():
            print(f"--- field {key} ---")
            print(tv)
    print()
