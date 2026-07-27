#!/usr/bin/env python3
"""Export the devblog release posts into data/whatsnew.json (the in-game "What's new?" feed).

The devblog drafts (devblog-artikel.md = German, devblog-artikel-en.md = English) are private,
git-IGNORED working files — they must never be committed. This script extracts ONLY the release
posts ("## Version X.Y.Z – Title") from both language files and writes them, bilingual and
newest-first, to data/whatsnew.json — which IS committed and doubles as:
  * the online feed the client fetches raw from GitHub (main branch), and
  * the offline fallback bundled into StreamingAssets by the data/ sync at build time.

Release procedure (see AGENTS.md): write the DE+EN release posts, run this script, commit the
refreshed data/whatsnew.json BEFORE tagging the release.

Usage:
  python tools/devblog/export_whatsnew.py            # from the repo root (devblog files present)
  python tools/devblog/export_whatsnew.py --source-dir <dir> --out <file>

Stdlib only; a version is exported only when the post exists in BOTH languages.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

HEADING = re.compile(r"^## Version (\d+\.\d+\.\d+) [–-] (.+?)\s*$")
# Metadata lines injected by the devblog workflow — never part of the player-facing body.
META = re.compile(r"^\*\*(Kategorie|Category|Veröffentlicht|Published):\*\*")
DATE_LINE = re.compile(r"^\*[^*]+\*\s*$")  # the italic post-date line, e.g. *July 27, 2026*
PUBLISHED_DATE = re.compile(r"^\*\*(?:Veröffentlicht|Published):\*\*\s*(\d{4}-\d{2}-\d{2})")


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


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source-dir", type=Path, default=script_dir,
                    help="directory holding devblog-artikel.md + devblog-artikel-en.md")
    ap.add_argument("--out", type=Path, default=script_dir.parent.parent / "data" / "whatsnew.json")
    args = ap.parse_args()

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
    return 0


if __name__ == "__main__":
    sys.exit(main())
