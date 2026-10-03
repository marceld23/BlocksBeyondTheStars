#!/usr/bin/env python3
"""Export the devblog release posts into the in-game "What's new?" feed.

The devblog drafts (devblog-artikel.md = German, devblog-artikel-en.md = English) are private,
git-IGNORED working files — they must never be committed. This script extracts ONLY the release
posts ("## Version X.Y.Z – Title") from both language files and writes them, bilingual and
newest-first, to data/whatsnew.json — which IS committed and doubles as:
  * the online feed the client fetches raw from GitHub (main branch), and
  * the offline fallback bundled into StreamingAssets by the data/ sync at build time.

Release procedure (see AGENTS.md): write the DE+EN release posts, run this script, commit the
refreshed data/whatsnew.json BEFORE tagging the release.

The other twelve languages
--------------------------
German and English live in data/whatsnew.json. Every other language gets one file,
data-online/whatsnew/<code>.json, which the client fetches online for the player's language and
lays over the feed by version (see data-online/README.md — that folder is deliberately not bundled).
The texts come from the devblog translation store — by default the built posts of the git-ignored
blog-sync tooling, analysis/devblog-mehrsprachig-2026-10-03/out/<code>/ in the main checkout (see the
release section of AGENTS.md) — one JSON file per translated post, carrying at least

    {"version": "2026.10.3", "title": "Version 2026.10.3 – …", "markdown": "…post body…"}

Only release posts (non-empty "version") that are also in the feed are exported. A language file is
MERGED, never shrunk: entries already committed stay unless the store has a text for the same
version, so running the export on a machine with an incomplete store loses nothing. The store itself
is private working data and stays git-ignored, like the drafts.

Usage:
  python tools/devblog/export_whatsnew.py            # from the repo root (devblog files present)
  python tools/devblog/export_whatsnew.py --source-dir <dir> --out <file>
  python tools/devblog/export_whatsnew.py --languages-only --translations <dir>
        # language files only; the versions come from the committed data/whatsnew.json, so this
        # works in any checkout (no private drafts needed) and never blocks a release

Stdlib only; a version is exported only when the post exists in BOTH languages.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

HEADING = re.compile(r"^## Version (\d+\.\d+\.\d+) [–-] (.+?)\s*$")
# A translated post's title: the same "Version X.Y.Z – Title" form, without the markdown heading marks.
TRANSLATED_TITLE = re.compile(r"^Version (\d+\.\d+\.\d+) [–-] (.+?)\s*$")
# Metadata lines injected by the devblog workflow — never part of the player-facing body.
META = re.compile(r"^\*\*(Kategorie|Category|Veröffentlicht|Published):\*\*")
DATE_LINE = re.compile(r"^\*[^*]+\*\s*$")  # the italic post-date line, e.g. *July 27, 2026*
PUBLISHED_DATE = re.compile(r"^\*\*(?:Veröffentlicht|Published):\*\*\s*(\d{4}-\d{2}-\d{2})")
LANGUAGE_CODE = re.compile(r"^[a-z]{2}$")
FEED_LANGUAGES = ("de", "en")  # carried by data/whatsnew.json itself


def parse_posts(path: Path) -> dict[str, dict[str, str]]:
    """Returns {version: {title, body, date}} for every release post in one language file."""
    posts: dict[str, dict[str, str]] = {}
    version = title = None
    body: list[str] = []
    date = ""

    def flush() -> None:
        nonlocal version, title, body, date
        if version:
            # Trim the leading/trailing blank lines the section boundaries leave behind.
            text = "\n".join(body).strip("\n").strip()
            posts[version] = {"title": title, "body": text, "date": date}
        version, title, body, date = None, None, [], ""

    for line in path.read_text(encoding="utf-8").splitlines():
        m = HEADING.match(line)
        if m:
            flush()
            version, title = m.group(1), m.group(2)
            continue
        if version is None:
            continue
        if line.strip() == "---" or line.startswith("## ") or line.startswith("# "):
            flush()
            continue
        pm = PUBLISHED_DATE.match(line)
        if pm:
            date = pm.group(1)
        body_started = any(l.strip() for l in body)
        if META.match(line) or (not body_started and (DATE_LINE.match(line) or not line.strip())):
            continue
        body.append(line)
    flush()
    return posts


def semver_key(v: str) -> tuple[int, ...]:
    return tuple(int(p) for p in v.split("."))


def translated_body(markdown: str) -> str:
    """The post body as the feed wants it: no leading blank lines, no leading post-date line."""
    lines = markdown.strip("\n").split("\n")
    while lines and (not lines[0].strip() or DATE_LINE.match(lines[0])):
        lines.pop(0)
    return "\n".join(lines).strip()


def load_translated_posts(lang_dir: Path) -> dict[str, dict[str, str]]:
    """Returns {version: {title, body}} for the translated RELEASE posts of one language folder."""
    posts: dict[str, dict[str, str]] = {}
    for file in sorted(lang_dir.glob("*.json")):
        try:
            data = json.loads(file.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as e:
            print(f"WARN: {file} is unreadable, skipped: {e}", file=sys.stderr)
            continue
        if not isinstance(data, dict) or not data.get("version"):
            continue  # not a post file, or an article that is no release post
        m = TRANSLATED_TITLE.match(str(data.get("title", "")))
        body = translated_body(str(data.get("markdown", "")))
        if not m or m.group(1) != data["version"] or not body:
            print(f"WARN: {file} has version {data['version']} but no matching "
                  f"'Version {data['version']} – …' title or no body, skipped", file=sys.stderr)
            continue
        posts[data["version"]] = {"title": m.group(2), "body": body}
    return posts


def export_languages(translations: Path, out_dir: Path, versions: list[str]) -> None:
    """Writes one language file per folder of the translation store (merged with the committed file)."""
    if not translations.is_dir():
        print(f"No translation store at {translations} — language files left as they are.")
        return
    for lang_dir in sorted(p for p in translations.iterdir() if p.is_dir()):
        code = lang_dir.name
        if not LANGUAGE_CODE.match(code) or code in FEED_LANGUAGES:
            continue
        target = out_dir / f"{code}.json"
        merged: dict[str, dict[str, str]] = {}
        if target.exists():
            for e in json.loads(target.read_text(encoding="utf-8")).get("entries", []):
                merged[e["version"]] = {"title": e["title"], "body": e["body"]}
        kept = len(merged)
        merged.update(load_translated_posts(lang_dir))
        entries = [{"version": v, "title": merged[v]["title"], "body": merged[v]["body"]}
                   for v in versions if v in merged]  # feed order (newest first), feed versions only
        if not entries:
            continue
        out_dir.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps({"language": code, "entries": entries}, ensure_ascii=False, indent=2)
                          + "\n", encoding="utf-8", newline="\n")
        print(f"Wrote {len(entries)} of {len(versions)} release posts in '{code}' to {target}"
              f" ({kept} were already there)")


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    repo = script_dir.parent.parent
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--source-dir", type=Path, default=script_dir,
                    help="directory holding devblog-artikel.md + devblog-artikel-en.md")
    ap.add_argument("--out", type=Path, default=repo / "data" / "whatsnew.json")
    ap.add_argument("--translations", type=Path,
                    default=repo / "analysis" / "devblog-mehrsprachig-2026-10-03" / "out",
                    help="devblog translation store: <dir>/<code>/*.json (git-ignored working data)")
    ap.add_argument("--languages-out", type=Path, default=repo / "data-online" / "whatsnew",
                    help="directory of the per-language files")
    ap.add_argument("--languages-only", action="store_true",
                    help="write only the per-language files; versions come from the existing --out file")
    args = ap.parse_args()

    if args.languages_only:
        if not args.out.exists():
            print(f"ERROR: {args.out} not found — the feed defines which versions exist.", file=sys.stderr)
            return 1
        feed = json.loads(args.out.read_text(encoding="utf-8"))
        export_languages(args.translations, args.languages_out, [e["version"] for e in feed["entries"]])
        return 0

    de_file = args.source_dir / "devblog-artikel.md"
    en_file = args.source_dir / "devblog-artikel-en.md"
    for f in (de_file, en_file):
        if not f.exists():
            print(f"ERROR: {f} not found — run from a checkout that has the (git-ignored) devblog drafts.",
                  file=sys.stderr)
            return 1

    de = parse_posts(de_file)
    en = parse_posts(en_file)
    both = sorted(set(de) & set(en), key=semver_key, reverse=True)
    only = sorted((set(de) ^ set(en)), key=semver_key)
    if only:
        print(f"WARN: release post missing in one language, skipped: {', '.join(only)}", file=sys.stderr)
    if not both:
        print("ERROR: no release post found in both languages — nothing to export.", file=sys.stderr)
        return 1

    entries = [{
        "version": v,
        "date": de[v]["date"] or en[v]["date"],
        "title_de": de[v]["title"],
        "title_en": en[v]["title"],
        "body_de": de[v]["body"],
        "body_en": en[v]["body"],
    } for v in both]

    args.out.write_text(json.dumps({"entries": entries}, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8")
    print(f"Wrote {len(entries)} release posts ({both[-1]} … {both[0]}) to {args.out}")
    export_languages(args.translations, args.languages_out, both)
    return 0


if __name__ == "__main__":
    sys.exit(main())
