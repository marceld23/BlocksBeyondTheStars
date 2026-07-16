#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Guardrail for the fast PR test tier: fail when any test in the given .trx files exceeds a
duration budget.

The PR gate runs with --filter Category!=Slow, so every test that reaches this check is supposed
to be fast. A test that blows the budget belongs in the Slow tier ([Trait("Category", "Slow")])
or needs a real fix — without this check the fast tier silently decays back to a slow one (it
grew from ~6 to ~15 min once before anyone noticed).

The budget is generous on purpose: trx durations are wall-clock, and parallel worldgen/sim tests
inflate the measured time of perfectly fast async tests (thread-pool contention — a 9 ms test has
measured 37 s inside the full suite), so borderline values are normal and only order-of-magnitude
offenders should trip this.

Usage: check-test-durations.py --max-seconds 120 TestResults/*.trx
"""

import argparse
import sys
import xml.etree.ElementTree as ET

TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def duration_seconds(value: str) -> float:
    hours, minutes, seconds = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", nargs="+", help=".trx result files to check")
    parser.add_argument("--max-seconds", type=float, default=120.0,
                        help="per-test duration budget (default: 120)")
    args = parser.parse_args()

    offenders = []
    total = 0
    for path in args.trx:
        for result in ET.parse(path).getroot().iter(f"{TRX_NS}UnitTestResult"):
            raw = result.get("duration")
            if raw is None:
                continue
            total += 1
            seconds = duration_seconds(raw)
            if seconds > args.max_seconds:
                offenders.append((seconds, result.get("testName") or "<unnamed>"))

    if offenders:
        print(f"{len(offenders)} test(s) exceeded the {args.max_seconds:.0f}s fast-tier budget:")
        for seconds, name in sorted(offenders, reverse=True):
            print(f"  {seconds:7.1f}s  {name}")
        print("Tag them [Trait(\"Category\", \"Slow\")] (full runs on main/release still cover them) "
              "or make them faster.")
        return 1

    print(f"All {total} tests within the {args.max_seconds:.0f}s fast-tier budget.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
