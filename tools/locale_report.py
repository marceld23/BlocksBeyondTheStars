#!/usr/bin/env python3
"""Report translation coverage per language and per key group for data/locales/*.json.

Community languages are translated incrementally, one key group per pull request, while `main` keeps
adding keys underneath. This tool makes that drift visible instead of leaving it to be eyeballed:

  * per-language coverage, overall and per key group, against en.json (the source of truth)
  * which groups are finished, in progress, or untouched — so a contributor can pick the next batch
  * the exact keys that are MISSING from a group (--missing), ready to paste into a locale file
  * defects CI also fails on: invented keys, changed {0}/{item} placeholder sets, blank values
  * soft nits CI tolerates: key order not mirroring en.json, values identical to English

English is the fallback for every missing key (GameContent.CreateLocalizer), so a partial language is
a supported state — this report says how partial, not whether it is allowed.

Usage:
  uv run --no-project python tools/locale_report.py                    # summary for every language
  uv run --no-project python tools/locale_report.py it                 # one language, group detail
  uv run --no-project python tools/locale_report.py it --missing ui    # the untranslated keys of a group
  uv run --no-project python tools/locale_report.py --markdown         # CI-comment friendly table
  uv run --no-project python tools/locale_report.py --check            # exit 1 on hard defects only

Stdlib only. Group = the key prefix up to the first dot, except `ui.*`, which is split one level
deeper (ui.portal, ui.settings, …) because `ui` alone is over half the table and useless as a batch.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# A Windows console defaults to cp1252 and would die on the box-drawing bars and the arrows in the
# report — force UTF-8 so the same output works in PowerShell, in bash and in a CI log.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO_ROOT = Path(__file__).resolve().parent.parent
LOCALE_DIR = REPO_ROOT / "data" / "locales"
SOURCE_LANG = "en"

# Languages with their own strict completeness tests — reported, but never flagged as "incomplete".
MANDATORY = ("en", "de")

PLACEHOLDER = re.compile(r"\{[A-Za-z0-9_]+\}")


def load(lang: str) -> dict[str, str]:
    path = LOCALE_DIR / f"{lang}.json"
    if not path.exists():
        raise SystemExit(f"no locale file for '{lang}' at {path}")
    with path.open(encoding="utf-8") as fh:
        return json.load(fh)


def languages() -> list[str]:
    """Every locale file present, source language first, then alphabetically."""
    found = sorted(p.stem for p in LOCALE_DIR.glob("*.json"))
    return [SOURCE_LANG] + [lang for lang in found if lang != SOURCE_LANG]


def group_of(key: str) -> str:
    """The reporting bucket for a key: `ui.*` splits one level deeper, everything else by prefix."""
    parts = key.split(".")
    if parts[0] == "ui" and len(parts) > 1:
        return f"ui.{parts[1]}"
    return parts[0]


def groups(table: dict[str, str]) -> dict[str, list[str]]:
    out: dict[str, list[str]] = {}
    for key in table:
        out.setdefault(group_of(key), []).append(key)
    return out


def defects(lang: str, table: dict[str, str], source: dict[str, str]) -> dict[str, list[str]]:
    """Hard defects (CI fails on these — see tests/CommunityLocaleTests.cs) and soft nits."""
    orphans = sorted(k for k in table if k not in source)
    blanks = sorted(k for k, v in table.items() if not v.strip())
    placeholders = []
    identical = []
    for key, value in table.items():
        if key not in source:
            continue
        if set(PLACEHOLDER.findall(source[key])) != set(PLACEHOLDER.findall(value)):
            placeholders.append(key)
        elif value == source[key]:
            identical.append(key)

    # Soft: key order relative to en.json, which keeps future diffs readable.
    shared = [k for k in table if k in source]
    expected = [k for k in source if k in table]
    return {
        "orphans": orphans,
        "blanks": blanks,
        "placeholders": sorted(placeholders),
        "identical": sorted(identical),
        "order": [] if shared == expected else ["key order does not mirror en.json"],
    }


def bar(done: int, total: int, width: int = 24) -> str:
    if total == 0:
        return " " * width
    filled = round(width * done / total)
    return "█" * filled + "·" * (width - filled)


def report_summary(source: dict[str, str], langs: list[str]) -> None:
    print(f"Source: en.json — {len(source)} keys\n")
    print(f"{'lang':<6}{'keys':>7}{'coverage':>11}  {'':<24}  defects")
    for lang in langs:
        if lang == SOURCE_LANG:
            continue
        table = load(lang)
        translated = sum(1 for k in table if k in source)
        pct = 100.0 * translated / len(source) if source else 0.0
        d = defects(lang, table, source)
        hard = len(d["orphans"]) + len(d["blanks"]) + len(d["placeholders"])
        note = "clean" if hard == 0 else f"{hard} HARD"
        if lang in MANDATORY and translated < len(source):
            note += f", {len(source) - translated} missing (must be complete!)"
        print(f"{lang:<6}{translated:>7}{pct:>10.1f}%  {bar(translated, len(source))}  {note}")
    print("\nRun with a language code for the per-group breakdown, e.g. tools/locale_report.py it")


def report_language(lang: str, source: dict[str, str]) -> None:
    table = load(lang)
    src_groups = groups(source)
    translated_total = sum(1 for k in table if k in source)
    pct = 100.0 * translated_total / len(source) if source else 0.0

    print(f"{lang}.json — {translated_total} of {len(source)} keys ({pct:.1f}%)\n")
    print(f"{'group':<22}{'done':>6}{'/':^3}{'total':<7}{'':<26}")

    done_groups, partial, untouched = [], [], []
    for group in sorted(src_groups, key=lambda g: (-len(src_groups[g]), g)):
        keys = src_groups[group]
        done = sum(1 for k in keys if k in table)
        marker = "✓" if done == len(keys) else (" " if done else "·")
        print(f"{group:<22}{done:>6}{'/':^3}{len(keys):<7}{bar(done, len(keys))} {marker}")
        (done_groups if done == len(keys) else partial if done else untouched).append(group)

    print(f"\nfinished: {len(done_groups)} group(s)"
          + (f" — {', '.join(done_groups)}" if done_groups else ""))
    if partial:
        print(f"in progress: {', '.join(partial)}")
    print(f"untouched: {len(untouched)} group(s)"
          + (f" — {', '.join(untouched[:12])}{' …' if len(untouched) > 12 else ''}" if untouched else ""))

    d = defects(lang, table, source)
    print()
    for label, keys, severity in (
        ("invented keys (not in en.json)", d["orphans"], "HARD"),
        ("blank values", d["blanks"], "HARD"),
        ("placeholder set changed", d["placeholders"], "HARD"),
        ("identical to English", d["identical"], "soft"),
        ("key order", d["order"], "soft"),
    ):
        if keys:
            shown = ", ".join(keys[:8]) + (" …" if len(keys) > 8 else "")
            print(f"[{severity}] {label}: {len(keys)} — {shown}")
    if not any(d[k] for k in ("orphans", "blanks", "placeholders")):
        print("no hard defects — this file would pass CI")


def report_missing(lang: str, source: dict[str, str], prefix: str) -> None:
    table = load(lang)
    missing = [k for k in source if k not in table and (group_of(k) == prefix or k.startswith(prefix))]
    if not missing:
        print(f"nothing missing in '{prefix}' for {lang} 🎉")
        return

    print(f"# {len(missing)} key(s) missing from {lang}.json in '{prefix}' — English text as the reference.")
    print("# Paste into the locale file and translate the values; keep this order to mirror en.json.")
    for key in missing:
        print(f'  {json.dumps(key, ensure_ascii=False)}: {json.dumps(source[key], ensure_ascii=False)},')


def report_markdown(source: dict[str, str], langs: list[str]) -> None:
    """The CI job-summary view: one table, plus a collapsed per-group breakdown per community language."""
    print(f"**Locale coverage** — against `en.json` ({len(source)} keys)\n")
    print("| language | keys | coverage | hard defects |")
    print("|---|---:|---:|---|")
    for lang in langs:
        if lang == SOURCE_LANG:
            continue
        table = load(lang)
        translated = sum(1 for k in table if k in source)
        d = defects(lang, table, source)
        hard = len(d["orphans"]) + len(d["blanks"]) + len(d["placeholders"])
        pct = 100.0 * translated / len(source) if source else 0.0
        print(f"| `{lang}` | {translated} | {pct:.1f}% | {'none' if hard == 0 else f'**{hard}**'} |")
    print("\n<sub>English is the per-key fallback, so a partial language is a supported state. "
          "Hard defects are what `CommunityLocaleTests` fails on — this report never fails a build.</sub>")

    src_groups = groups(source)
    for lang in langs:
        if lang in MANDATORY:
            continue
        table = load(lang)
        print(f"\n<details><summary>{lang}.json — per key group</summary>\n")
        print("| group | done | total |")
        print("|---|---:|---:|")
        for group in sorted(src_groups, key=lambda g: (-len(src_groups[g]), g)):
            keys = src_groups[group]
            done = sum(1 for k in keys if k in table)
            if done or len(keys) >= 10:  # hide the long tail of untouched one-key groups
                print(f"| `{group}` | {done} | {len(keys)} |")
        print("\n</details>")


def check(source: dict[str, str], langs: list[str]) -> int:
    failed = 0
    for lang in langs:
        if lang == SOURCE_LANG:
            continue
        d = defects(lang, load(lang), source)
        for label in ("orphans", "blanks", "placeholders"):
            if d[label]:
                failed += len(d[label])
                print(f"{lang}: {label}: {', '.join(d[label][:10])}", file=sys.stderr)
    if failed:
        print(f"{failed} hard defect(s) — see tests/BlocksBeyondTheStars.Tests/CommunityLocaleTests.cs",
              file=sys.stderr)
    return 1 if failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("lang", nargs="?", help="language code for the per-group breakdown (e.g. it)")
    parser.add_argument("--missing", metavar="GROUP",
                       help="print the untranslated keys of a group with their English text")
    parser.add_argument("--markdown", action="store_true", help="markdown table (for CI comments)")
    parser.add_argument("--check", action="store_true", help="exit 1 if any language has hard defects")
    args = parser.parse_args()

    source = load(SOURCE_LANG)
    langs = languages()

    if args.check:
        return check(source, langs)
    if args.markdown:
        report_markdown(source, langs)
    elif args.missing:
        if not args.lang:
            parser.error("--missing needs a language, e.g. tools/locale_report.py it --missing item")
        report_missing(args.lang, source, args.missing)
    elif args.lang:
        report_language(args.lang, source)
    else:
        report_summary(source, langs)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
