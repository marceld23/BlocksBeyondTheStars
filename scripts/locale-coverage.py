#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Writes data/locale_coverage.json — locale code → fraction of the English key set the locale covers.

Why a manifest (#1522): the game parses only English + the active locale at start; the other tables load
on first use. The settings language picker (GameContent.SelectableLocales) still has to know which
community languages clear the 45 % bar, and it must answer one click without parsing twelve tables — so
the coverage is computed here, at development time, and shipped as a tiny file. A locale the manifest
does not list is measured at runtime instead, so a missing entry degrades to the old behaviour.

Coverage rule (mirrors GameContent.LocaleCoverage): for every key in the merged English table (base
data/locales/en.json + each story pack's locales/en.json), the locale counts as covered when its merged
table has a non-blank value. LocaleCoverageManifestTests fails when this file drifts from the tables.

Usage:
  locale-coverage.py            # rewrite data/locale_coverage.json
  locale-coverage.py --check    # exit 1 when the committed file differs from the tables
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOCALES = ROOT / "data" / "locales"
STORIES = ROOT / "data" / "stories"
OUT = ROOT / "data" / "locale_coverage.json"
TOLERANCE = 0.0005


def merged_table(code: str) -> dict[str, str] | None:
    base = LOCALES / f"{code}.json"
    files = [base] if base.exists() else []
    if STORIES.exists():
        for pack in sorted(STORIES.iterdir()):
            f = pack / "locales" / f"{code}.json"
            if f.exists():
                files.append(f)
    if not files:
        return None
    table: dict[str, str] = {}
    for f in files:
        table.update(json.loads(f.read_text(encoding="utf-8")))
    return table


def compute() -> dict[str, float]:
    english = merged_table("en") or {}
    result: dict[str, float] = {}
    for file in sorted(LOCALES.glob("*.json")):
        code = file.stem
        if code == "coverage":
            continue
        if code == "en":
            result[code] = 1.0
            continue
        table = merged_table(code) or {}
        covered = sum(1 for key in english if str(table.get(key, "")).strip())
        result[code] = round(covered / len(english), 4) if english else 0.0
    return result


def main(argv: list[str]) -> int:
    fresh = compute()
    if "--check" in argv:
        current = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else {}
        drift = {c: (current.get(c), v) for c, v in fresh.items() if abs((current.get(c) or 0.0) - v) > TOLERANCE}
        if drift or set(current) != set(fresh):
            print("coverage.json is stale — run scripts/locale-coverage.py:", drift or "locale set changed")
            return 1
        print("coverage.json matches the locale tables")
        return 0
    OUT.write_text(json.dumps(fresh, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(ROOT)}: " + ", ".join(f"{c} {v:.1%}" for c, v in fresh.items()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
