#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Emits the vstest --filter for a narrow CI lane (ci.yml `lane-tests`).

A pull request whose whole non-doc diff stays inside a small, well-understood set of files does
not need the sharded test matrix: only the test classes that can observe those files can change
their result. .github/workflows/detect-doc-only.yml decides which lane a PR is on; this script
owns the class set of each lane defined here. (The `locales` lane predates it and keeps its own
script, scripts/locale-test-filter.py.)

Lanes:
  whatsnew — the diff is data/whatsnew.json and/or data-online/whatsnew/*.json, the in-game
             "What's new?" feed (German + English) and its per-language files. The game server
             never loads either: the content loader reads named files and sub-folders of data/,
             never the folder as a whole, and no product code outside the Unity client mentions
             them. What is left are the tests that open the files themselves.

How a lane's class set is found:
  * A marker scan: every test class in a server-suite file whose source matches the lane's
    marker (the path fragment a reader normally writes). Self-maintaining — a second test
    that opens the feed joins the lane by existing.
  * HAND_EXTRAS per lane, for a consumer the scan cannot see: a class that reaches the files
    through a shared helper (the marker is then in the helper, and Helpers/ is not scanned), or
    one that only lists a folder. None today. Found by the corruption experiment: overwrite the
    lane's files with invalid content, run the full fast tier, and every failing class belongs
    in the lane. (That finds readers of the CONTENT. A test that only reacts to a file appearing
    or disappearing needs the same experiment with a file added and one removed.) `verify` fails
    on an entry that no longer exists, so the list cannot rot silently.

The lane job builds and runs the SERVER suite only (tests/BlocksBeyondTheStars.Tests). `verify`
therefore fails when a Client.Tests file matches the marker: such a test would be skipped without
anyone noticing, and the lane has to grow a second test run first. It is a plain text match, so
a Client.Tests file that merely QUOTES the path (a wiring guard over the Unity script's URL
constants) trips it too — then either build the lane's second run or word the guard differently.

`verify` runs in two places: in the lane job itself, and on shard 1 of every pull request's
matrix (tests.yml, `lane-filter-guard`) — so the PR that breaks a lane's filter is the one that
goes red, not the next release PR.

Safety framing (same as the locale lane): an over-broad set costs seconds; an under-broad set is
caught downstream — a push to main runs the COMPLETE suite and release.yml re-runs it on the
tagged commit. A miss can redden main after the merge but cannot reach a release.

Filter tokens are dot-anchored (`FullyQualifiedName~.<Class>.`) exactly like
scripts/partition-tests.py, so a token can never substring-match a longer class name.

Usage:
  lane-test-filter.py whatsnew              # print the --filter expression
  lane-test-filter.py whatsnew verify       # sanity-check the set (ci.yml runs this first)
  lane-test-filter.py ran TestResults/x.trx # fail unless the run executed at least one test
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SERVER_TESTS = REPO_ROOT / "tests" / "BlocksBeyondTheStars.Tests"
CLIENT_TESTS = REPO_ROOT / "tests" / "BlocksBeyondTheStars.Client.Tests"

# Same declaration regex as partition-tests.py: one file may declare several classes, and
# non-test classes ride along harmlessly (their tokens match nothing).
CLASS_RE = re.compile(
    r"^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)",
    re.MULTILINE,
)

TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


@dataclass(frozen=True)
class Lane:
    """One narrow lane: how its readers are recognised, and the readers a scan cannot see."""

    marker: re.Pattern[str]
    hand_extras: list[str] = field(default_factory=list)


LANES: dict[str, Lane] = {
    # "whatsnew.json" is the feed's file name (a reader builds the path from DataDir() + that
    # name); "data-online" is the folder of the per-language files. Keep HAND_EXTRAS sorted.
    "whatsnew": Lane(marker=re.compile(r"whatsnew\.json|data-online")),
}


def scan(directory: Path, marker: re.Pattern[str], recursive: bool) -> tuple[set[str], set[str], list[str]]:
    """Returns (classes in marker-matching files, all classes, the matching file names)."""
    hit_classes: set[str] = set()
    all_classes: set[str] = set()
    hit_files: list[str] = []
    paths = directory.rglob("*.cs") if recursive else directory.glob("*.cs")
    for path in sorted(paths):
        if {"bin", "obj"} & set(path.relative_to(directory).parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        classes = CLASS_RE.findall(text)
        all_classes.update(classes)
        if marker.search(text):
            hit_classes.update(classes)
            hit_files.append(path.name)
    return hit_classes, all_classes, hit_files


def lane_bucket(name: str) -> list[str]:
    """The lane's class set, or SystemExit with the reason it cannot be trusted."""
    lane = LANES[name]
    # Test classes live in the project root only (partition-tests.py discovers them the same way).
    scanned, all_classes, _ = scan(SERVER_TESTS, lane.marker, recursive=False)

    missing = [c for c in lane.hand_extras if c not in all_classes]
    if missing:
        raise SystemExit(f"error: HAND_EXTRAS of lane '{name}' no longer exist as classes: {missing}")
    if not scanned:
        # Never emit an empty filter: `dotnet test` would report "no test matches" and exit 0.
        raise SystemExit(f"error: the marker scan of lane '{name}' found no test class — the scan is broken "
                         f"or the guarding tests moved")

    _, _, client_hits = scan(CLIENT_TESTS, lane.marker, recursive=True)
    if client_hits:
        raise SystemExit(f"error: Client.Tests reads the files of lane '{name}' ({', '.join(client_hits)}), but the "
                         f"lane job runs the server suite only — add a Client.Tests run to ci.yml's lane-tests "
                         f"before this lane may skip the matrix")

    return sorted(scanned | set(lane.hand_extras))


def cmd_ran(trx_files: list[Path]) -> int:
    """Fails unless the given trx files record at least one executed test and no failure to run any."""
    if not trx_files:
        print("error: no trx file given — did the test step write its results?", file=sys.stderr)
        return 1
    executed = 0
    for trx in trx_files:
        if not trx.exists():
            print(f"error: {trx} does not exist — did the test step write its results?", file=sys.stderr)
            return 1
        counters = ET.parse(trx).getroot().find(f"{TRX_NS}ResultSummary/{TRX_NS}Counters")
        if counters is None:
            print(f"error: {trx} has no result counters", file=sys.stderr)
            return 1
        executed += int(counters.get("executed", "0"))
    if executed == 0:
        print("error: the lane filter matched no test — a narrow lane must never pass on an empty run",
              file=sys.stderr)
        return 1
    print(f"lane run executed {executed} test(s)")
    return 0


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print(__doc__, file=sys.stderr)
        return 2

    if args[0] == "ran":
        return cmd_ran([Path(a) for a in args[1:]])

    name = args[0]
    if name not in LANES:
        print(f"error: unknown lane '{name}' (known: {', '.join(sorted(LANES))})", file=sys.stderr)
        return 2
    mode = args[1] if len(args) > 1 else "filter"
    if mode not in ("filter", "verify"):
        print(f"error: unknown mode '{mode}' (filter | verify)", file=sys.stderr)
        return 2

    bucket = lane_bucket(name)
    if mode == "verify":
        print(f"lane '{name}' covers {len(bucket)} class(es): {', '.join(bucket)}")
        return 0
    print("(" + "|".join(f"FullyQualifiedName~.{cls}." for cls in bucket) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
