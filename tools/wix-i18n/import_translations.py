# /// script
# requires-python = ">=3.11"
# dependencies = ["requests"]
# ///
"""Import translated website texts into Wix Multilingual.

Reads a translations file (same shape as the audit's todo-<locale>.json but
with each field's "text" replaced by the translation), then creates or
updates the translation content per item. DRY-RUN by default: shows exactly
what would be written; nothing is sent without --apply.

Fields are written with published=true so they go live on the locale's site
(the locale itself stays invisible while its visibility is HIDDEN).

Usage (from tools/wix-i18n/):
  uv run import_translations.py out/translations-it.json          # dry run
  uv run import_translations.py out/translations-it.json --apply  # write!
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import requests

sys.stdout.reconfigure(encoding="utf-8")

from audit_translations import API, load_env, make_session, text_preview


def fetch_existing_keys(s: requests.Session, locale: str) -> set[tuple[str, str]]:
    keys: set[tuple[str, str]] = set()
    cursor = None
    while True:
        paging: dict = {"limit": 100}
        body: dict = {"search": {"cursorPaging": paging}}
        if cursor:
            paging["cursor"] = cursor
        else:
            body["search"]["filter"] = {"locale": locale}
        r = s.post(f"{API}/translation-content/v1/contents/search", json=body)
        r.raise_for_status()
        d = r.json()
        keys |= {(c["schemaId"], c["entityId"]) for c in d.get("contents", [])}
        pm = d.get("pagingMetadata", {})
        cursor = pm.get("cursors", {}).get("next")
        if not pm.get("hasNext"):
            return keys


def field_text(f: dict, base_dir: Path) -> str:
    """A field carries its translation inline ("text") or in a file ("textFile")."""
    if "textFile" in f:
        return (base_dir / f["textFile"]).read_text(encoding="utf-8").strip()
    return f["text"]


def content_payload(item: dict, locale: str, base_dir: Path) -> dict:
    return {
        "schemaId": item["schemaId"],
        "entityId": item["entityId"],
        "locale": locale,
        "fields": {key: {"textValue": field_text(f, base_dir), "published": True}
                   for key, f in item["fields"].items()},
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("file", type=Path, help="translations JSON (list of todo items)")
    ap.add_argument("--apply", action="store_true", help="actually write to Wix")
    args = ap.parse_args()

    items = json.loads(args.file.read_text(encoding="utf-8"))
    base_dir = args.file.resolve().parent
    if not items:
        sys.exit("Nothing to import.")
    locales = {i["locale"] for i in items}
    if len(locales) != 1:
        sys.exit(f"Mixed locales in one file: {locales}")
    locale = locales.pop()

    env = load_env()
    s = make_session(env)
    existing = fetch_existing_keys(s, locale)

    creates = [i for i in items if (i["schemaId"], i["entityId"]) not in existing]
    updates = [i for i in items if (i["schemaId"], i["entityId"]) in existing]
    n_fields = sum(len(i["fields"]) for i in items)
    print(f"{locale}: {len(creates)} creates, {len(updates)} updates, {n_fields} fields total")

    if not args.apply:
        for i in items:
            action = "CREATE" if i in creates else "UPDATE"
            for key, f in i["fields"].items():
                print(f"  {action} {i['schema']} `{i['entityId']}` {key}: "
                      f"{text_preview(field_text(f, base_dir))}")
        print("\nDry run — re-run with --apply to write.")
        return

    failures = 0
    for item in creates:
        r = s.post(f"{API}/translation-content/v1/contents",
                   json={"content": content_payload(item, locale, base_dir)})
        if r.status_code != 200:
            failures += 1
            print(f"FAILED create {item['schema']} {item['entityId']}: "
                  f"{r.status_code} {r.text[:200]}")
    for start in range(0, len(updates), 100):
        batch = updates[start:start + 100]
        r = s.post(f"{API}/translation-content/v1/bulk/contents/update-by-key",
                   json={"contents": [{"content": content_payload(i, locale, base_dir)}
                                      for i in batch]})
        if r.status_code != 200:
            failures += 1
            print(f"FAILED bulk update ({len(batch)} items): {r.status_code} {r.text[:200]}")

    print(f"Done: {len(creates)} creates, {len(updates)} updates, {failures} failures.")
    if failures:
        sys.exit(1)


if __name__ == "__main__":
    main()
