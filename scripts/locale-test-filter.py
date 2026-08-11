#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Emits the vstest --filter for the locale guard job (ci.yml `locale-tests`).

When a PR touches NOTHING but data/locales/*.json, ci.yml skips the 4-shard test matrix and
runs only the test classes that can actually observe a locale edit. This script owns that set.

The set is the union of two sources:
  * A marker scan: every test class in a file that reads the locale tables directly
    (TestLocales.Load / CreateLocalizer / a "locales/" path). Self-maintaining.
  * HAND_EXTRAS: classes that consume locale VALUES indirectly — the text travels through the
    game server (e.g. NpcHintTests asserts a hint built from an en.json template) — which no
    source scan can find. Found empirically; regenerate after big test-suite changes with the
    mutation experiment: REPLACE every value in all data/locales/*.json with junk (keep the
    {n} placeholders; merely appending is too weak — Contains() assertions survive it), run
    the full fast tier, and every failing class belongs here (or in the marker set). A class
    listed here that stops existing fails `verify`, so the list cannot rot silently.

Empirical status (2026-08-11): under that mutation the fast tier fails only in ChatHelpTextTests
and ContentTests (both marker-visible), and Client.Tests passes 195/195 — so skipping it on
locales-only PRs is proven safe. NpcHintTests' locale-consuming methods are all Slow-tier (they
never ran on PRs to begin with); it stays listed so the class remains covered even if those
methods ever lose the Slow trait, which no source scan would notice.

Filter tokens are dot-anchored (`FullyQualifiedName~.<Class>.`) exactly like
scripts/partition-tests.py, so a token can never substring-match a longer class name.

Safety framing: an over-broad set costs seconds; an under-broad set is caught downstream — a
push to main runs the COMPLETE suite (ci.yml two-tier gate), and release.yml re-runs it on the
tagged commit. A miss here can redden main after merge but cannot reach a release.

Usage:
  locale-test-filter.py            # print the --filter expression
  locale-test-filter.py verify     # sanity-check the set (ci.yml runs this before testing)
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

TEST_DIR = Path(__file__).resolve().parent.parent / "tests" / "BlocksBeyondTheStars.Tests"

# Same declaration regex as partition-tests.py: one file may declare several classes, and
# non-test classes ride along harmlessly (their tokens match nothing).
CLASS_RE = re.compile(
    r"^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)",
    re.MULTILINE,
)

# Direct locale-table readers, findable in test source.
MARKER_RE = re.compile(r"TestLocales\.Load|CreateLocalizer|locales/")

# Indirect consumers (locale values reach the assertion through the game server), found via the
# mutation experiment described above. Keep sorted.
HAND_EXTRAS = [
    "NpcHintTests",
]


def affected_classes() -> tuple[set[str], set[str]]:
    """Returns (marker-scanned classes, all known classes in the test project)."""
    scanned: set[str] = set()
    all_classes: set[str] = set()
    for path in sorted(TEST_DIR.glob("*.cs")):
        text = path.read_text(encoding="utf-8-sig")
        classes = CLASS_RE.findall(text)
        all_classes.update(classes)
        if MARKER_RE.search(text):
            scanned.update(classes)
    return scanned, all_classes


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else "filter"
    scanned, all_classes = affected_classes()

    missing = [c for c in HAND_EXTRAS if c not in all_classes]
    if missing:
        print(f"HAND_EXTRAS entries no longer exist as classes: {missing}", file=sys.stderr)
        return 1
    if len(scanned) < 3:
        # The guards (CommunityLocaleTests, ContentTests, …) are marker-visible; finding fewer
        # means the scan itself broke — never emit a near-empty filter silently.
        print(f"marker scan found only {len(scanned)} classes — scan is broken", file=sys.stderr)
        return 1

    bucket = sorted(scanned | set(HAND_EXTRAS))
    if mode == "verify":
        print(f"locale filter covers {len(bucket)} classes: {', '.join(bucket)}")
        return 0
    print("(" + "|".join(f"FullyQualifiedName~.{cls}." for cls in bucket) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
