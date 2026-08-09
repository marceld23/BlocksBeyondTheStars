# /// script
# requires-python = ">=3.11"
# dependencies = ["requests"]
# ///
"""Audit Wix Multilingual translation coverage of the website.

Read-only. Dumps all translation schemas and per-locale contents from the
Wix Translation Content API, then writes into out/ (gitignored):

  schemas.json           raw schema catalog
  contents-<locale>.json raw content dump per locale
  report.md              human-readable gap report per target locale
  todo-<locale>.json     items/fields that need a translation, with the
                         primary-language source text (input for
                         import_translations.py after translating)

A field needs translation when the primary-locale content has a non-empty
text value (schema field types SHORT_TEXT / LONG_TEXT / HTML, not
displayOnly) and the target locale is missing the content item or that
field. Items whose primary content was updated after the translation are
reported as stale.

Usage (from tools/wix-i18n/):
  uv run audit_translations.py                 # targets: all secondary locales
  uv run audit_translations.py --targets it    # single target locale
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import requests

SCRIPT_DIR = Path(__file__).resolve().parent
OUT_DIR = SCRIPT_DIR / "out"
API = "https://www.wixapis.com"
TEXT_TYPES = {"SHORT_TEXT", "LONG_TEXT", "HTML"}
TAG_RE = re.compile(r"<[^>]+>")


def load_env() -> dict[str, str]:
    """Read WIX_API_KEY / WIX_SITE_ID from .env here or tools/devblog/.env."""
    for candidate in (SCRIPT_DIR / ".env", SCRIPT_DIR.parent / "devblog" / ".env"):
        if candidate.exists():
            env = {}
            for line in candidate.read_text(encoding="utf-8").splitlines():
                if "=" in line and not line.lstrip().startswith("#"):
                    k, v = line.split("=", 1)
                    env[k.strip()] = v.strip()
            if "WIX_API_KEY" in env and "WIX_SITE_ID" in env:
                return env
    sys.exit("No .env with WIX_API_KEY/WIX_SITE_ID found (tools/wix-i18n/ or tools/devblog/)")


def make_session(env: dict[str, str]) -> requests.Session:
    s = requests.Session()
    s.headers.update({
        "Authorization": env["WIX_API_KEY"],
        "wix-site-id": env["WIX_SITE_ID"],
        "Content-Type": "application/json",
    })
    return s


def fetch_locales(s: requests.Session) -> list[dict]:
    r = s.post(f"{API}/locales/v2/locale/query", json={"query": {}})
    r.raise_for_status()
    return r.json().get("locales", [])


def fetch_schemas(s: requests.Session) -> dict[str, dict]:
    schemas: dict[str, dict] = {}
    cursor = None
    while True:
        params: dict = {"paging.limit": 100}
        if cursor:
            params["paging.cursor"] = cursor
        r = s.get(f"{API}/translation-schema/v1/schemas/site", params=params)
        r.raise_for_status()
        d = r.json()
        for sc in d.get("schemas", []):
            schemas[sc["id"]] = sc
        pm = d.get("pagingMetadata", {})
        cursor = pm.get("cursors", {}).get("next")
        if not pm.get("hasNext"):
            return schemas


def fetch_contents(s: requests.Session, locale: str) -> list[dict]:
    items: list[dict] = []
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
        items += d.get("contents", [])
        pm = d.get("pagingMetadata", {})
        cursor = pm.get("cursors", {}).get("next")
        if not pm.get("hasNext"):
            return items


def text_preview(value: str, limit: int = 90) -> str:
    text = TAG_RE.sub(" ", value)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:limit] + ("…" if len(text) > limit else "")


def translatable_fields(content: dict, schema: dict | None) -> dict[str, dict]:
    """Field key -> {text, type} for non-empty primary text fields."""
    result: dict[str, dict] = {}
    schema_fields = (schema or {}).get("fields", {})
    for key, fval in content.get("fields", {}).items():
        text = fval.get("textValue")
        if not text or not text.strip():
            continue
        sfield = schema_fields.get(key, {})
        ftype = sfield.get("type", "SHORT_TEXT")
        if ftype not in TEXT_TYPES or sfield.get("displayOnly"):
            continue
        if ftype == "HTML" and not TAG_RE.sub("", text).strip():
            continue  # markup without any text content
        result[key] = {"text": text, "type": ftype}
    return result


def schema_label(schema: dict | None) -> str:
    if not schema:
        return "unknown schema"
    return schema.get("displayName") or schema["key"]["entityType"]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--targets", help="comma-separated target locales (default: all secondary)")
    args = ap.parse_args()

    env = load_env()
    s = make_session(env)
    OUT_DIR.mkdir(exist_ok=True)

    locales = fetch_locales(s)
    primary = next(l["id"] for l in locales if l.get("primaryLocale"))
    secondary = [l["id"] for l in locales if not l.get("primaryLocale")]
    targets = args.targets.split(",") if args.targets else secondary
    print(f"Primary locale: {primary}; targets: {', '.join(targets)}")

    schemas = fetch_schemas(s)
    (OUT_DIR / "schemas.json").write_text(
        json.dumps(schemas, indent=1, ensure_ascii=False), encoding="utf-8")

    contents: dict[str, dict[tuple[str, str], dict]] = {}
    for locale in [primary, *targets]:
        rows = fetch_contents(s, locale)
        contents[locale] = {(c["schemaId"], c["entityId"]): c for c in rows}
        (OUT_DIR / f"contents-{locale}.json").write_text(
            json.dumps(rows, indent=1, ensure_ascii=False), encoding="utf-8")
        print(f"{locale}: {len(rows)} content items")

    report = ["# Wix translation gap report", ""]
    for target in targets:
        missing_items: list[dict] = []
        missing_fields: list[dict] = []
        identical: list[dict] = []
        stale: list[dict] = []
        todo: list[dict] = []

        for (schema_id, entity_id), de in contents[primary].items():
            schema = schemas.get(schema_id)
            de_fields = translatable_fields(de, schema)
            if not de_fields:
                continue
            tgt = contents[target].get((schema_id, entity_id))
            entry = {
                "schemaId": schema_id,
                "entityId": entity_id,
                "schema": schema_label(schema),
                "entityType": (schema or {}).get("key", {}).get("entityType", ""),
                "parentEntityId": de.get("parentEntityId"),
                "locale": target,
                "fields": {},
            }
            if tgt is None:
                entry["fields"] = {k: v for k, v in de_fields.items()}
                missing_items.append(entry)
                todo.append(entry)
                continue
            tgt_fields = tgt.get("fields", {})
            gaps = {k: v for k, v in de_fields.items()
                    if not tgt_fields.get(k, {}).get("textValue", "").strip()}
            same = {k: v for k, v in de_fields.items()
                    if k not in gaps
                    and tgt_fields.get(k, {}).get("textValue", "").strip() == v["text"].strip()
                    and len(v["text"].strip()) > 3}
            if gaps:
                entry["fields"] = gaps
                missing_fields.append(entry)
                todo.append(entry)
            if same:
                identical.append({**entry, "fields": same})
            if not gaps and de.get("updatedDate", "") > tgt.get("updatedDate", ""):
                stale.append({**entry, "deUpdated": de.get("updatedDate"),
                              "targetUpdated": tgt.get("updatedDate")})

        report += [f"## {primary} → {target}", "",
                   f"- Primary items with translatable text: "
                   f"{sum(1 for k, c in contents[primary].items() if translatable_fields(c, schemas.get(k[0])))}",
                   f"- Missing entirely in {target}: {len(missing_items)}",
                   f"- Existing but with untranslated fields: {len(missing_fields)}",
                   f"- Translation identical to {primary} (copied, not translated?): {len(identical)}",
                   f"- Possibly stale ({primary} edited after translation): {len(stale)}", ""]
        for title, rows in (("Missing items", missing_items),
                            ("Untranslated fields", missing_fields),
                            (f"Identical to {primary}", identical)):
            if not rows:
                continue
            report.append(f"### {title}")
            report.append("")
            for e in sorted(rows, key=lambda x: (x["schema"], x["entityId"])):
                for key, f in e["fields"].items():
                    report.append(f"- **{e['schema']}** `{e['entityId']}` "
                                  f"[{f['type']}] {text_preview(f['text'])}")
            report.append("")
        if stale:
            report.append("### Possibly stale")
            report.append("")
            for e in sorted(stale, key=lambda x: (x["schema"], x["entityId"])):
                report.append(f"- **{e['schema']}** `{e['entityId']}` "
                              f"({primary} {e['deUpdated']} > {target} {e['targetUpdated']})")
            report.append("")

        todo_path = OUT_DIR / f"todo-{target}.json"
        todo_path.write_text(json.dumps(todo, indent=1, ensure_ascii=False), encoding="utf-8")
        print(f"{target}: {len(missing_items)} missing items, {len(missing_fields)} partial, "
              f"{len(stale)} stale -> {todo_path.name}")

    (OUT_DIR / "report.md").write_text("\n".join(report), encoding="utf-8")
    print(f"Report: {OUT_DIR / 'report.md'}")


if __name__ == "__main__":
    main()
