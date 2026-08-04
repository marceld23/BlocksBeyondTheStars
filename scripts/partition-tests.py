#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Deterministic test sharding for the CI matrix (ci.yml).

The server suite (tests/BlocksBeyondTheStars.Tests) is CPU-bound and scales ~linearly with
cores, but a single GitHub runner only has 4. ci.yml therefore fans the suite out over N
runners; this script decides which test CLASS runs on which shard and emits the matching
vstest --filter expression.

How it partitions:
  * Test classes are discovered by parsing class declarations out of every .cs file in the
    test project (NOT from file names — one file may declare several classes, and a class a
    filter misses would be SILENTLY untested). Non-test classes ride along harmlessly: their
    filter tokens simply match nothing.
  * Each class gets a weight from scripts/test-shard-weights.json (summed trx seconds from a
    real CI run; unknown/new classes get a default) and classes are greedy-packed onto the
    lightest shard. Deterministic: same inputs → same assignment on every shard's runner.
  * The emitted filter ORs `FullyQualifiedName~.<Class>.` tokens. The dots anchor the token
    between namespace and method (`~` is substring matching — a bare `~FloraTests` would also
    match FloraTintTests).

Safety net: `verify` cross-checks the partition against `dotnet test --list-tests` output —
every discovered test must be matched by EXACTLY one shard's filter, so an oddly named or
nested class (whose FQN uses `Outer+Inner`, which no token matches) fails the build loudly
instead of silently never running. ci.yml runs this on shard 1 of every build.

Usage:
  partition-tests.py filter --shard 2 --shards 4      # print shard 2's --filter expression
  partition-tests.py verify --shards 4 --list-file listed.txt
  partition-tests.py show --shards 4                  # human-readable assignment + weights
"""

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEST_DIR = REPO_ROOT / "tests" / "BlocksBeyondTheStars.Tests"
WEIGHTS_FILE = REPO_ROOT / "scripts" / "test-shard-weights.json"

# Weight (seconds) for classes without an entry in the weights file — roughly the suite's
# median class cost, so brand-new test classes don't skew a shard until weights are refreshed.
DEFAULT_WEIGHT = 10.0

CLASS_RE = re.compile(r"^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)", re.MULTILINE)


def discover_classes() -> list[str]:
    """All class names declared in the test project (excluding bin/obj)."""
    names: set[str] = set()
    for path in sorted(TEST_DIR.glob("*.cs")):
        names.update(CLASS_RE.findall(path.read_text(encoding="utf-8-sig")))
    if not names:
        raise SystemExit(f"error: no classes found under {TEST_DIR} — wrong checkout?")
    return sorted(names)


def assign(shards: int) -> list[list[str]]:
    """Greedy bin-packing: heaviest class first onto the currently lightest shard."""
    weights = json.loads(WEIGHTS_FILE.read_text(encoding="utf-8")) if WEIGHTS_FILE.exists() else {}
    buckets: list[list[str]] = [[] for _ in range(shards)]
    loads = [0.0] * shards
    # Sort by (-weight, name): deterministic even among equal weights.
    for cls in sorted(discover_classes(), key=lambda c: (-weights.get(c, DEFAULT_WEIGHT), c)):
        target = loads.index(min(loads))
        buckets[target].append(cls)
        loads[target] += weights.get(cls, DEFAULT_WEIGHT)
    return buckets


def shard_filter(bucket: list[str]) -> str:
    return "(" + "|".join(f"FullyQualifiedName~.{cls}." for cls in sorted(bucket)) + ")"


FQN_RE = re.compile(r"^[A-Za-z_]\w*(?:\.\w+){2,}$")


def parse_listed_tests(list_file: Path) -> list[str]:
    """FQNs from `dotnet test --list-tests` output.

    Deliberately does NOT key off the "The following Tests are available:" header — that line is
    localized (German SDKs print "Die folgenden Tests sind verfügbar:"). Instead: any indented
    line whose text (with theory arguments stripped) is a plain dotted identifier chain counts.
    Build chatter never matches (those lines contain spaces, '->', ellipses, …).
    """
    tests = []
    for line in list_file.read_text(encoding="utf-8-sig").splitlines():
        if not line.startswith(" "):
            continue
        candidate = line.strip().split("(", 1)[0]  # drop theory arguments
        if FQN_RE.match(candidate):
            tests.append(candidate)
    if not tests:
        raise SystemExit(f"error: no tests parsed from {list_file} — did --list-tests run?")
    return tests


def cmd_verify(shards: int, list_file: Path) -> int:
    buckets = assign(shards)
    tokens = [[f".{cls}." for cls in bucket] for bucket in buckets]
    bad = []
    for fqn in parse_listed_tests(list_file):
        hits = [i + 1 for i, toks in enumerate(tokens) if any(t in fqn for t in toks)]
        if len(hits) != 1:
            bad.append((fqn, hits))
    if bad:
        print(f"{len(bad)} test(s) not matched by exactly one shard filter:")
        for fqn, hits in bad[:20]:
            print(f"  shards {hits or '[]'}: {fqn}")
        print("Nested test classes (Outer+Inner) or a class whose name occurs as a namespace "
              "segment break the partition — rename or extend partition-tests.py.")
        return 1
    print(f"All {len(parse_listed_tests(list_file))} listed tests map to exactly one of {shards} shards.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["filter", "verify", "show"])
    parser.add_argument("--shards", type=int, required=True, help="total shard count")
    parser.add_argument("--shard", type=int, help="1-based shard index (filter)")
    parser.add_argument("--list-file", type=Path, help="dotnet test --list-tests output (verify)")
    args = parser.parse_args()

    if args.command == "filter":
        if not args.shard or not 1 <= args.shard <= args.shards:
            raise SystemExit("error: --shard must be in 1..--shards")
        print(shard_filter(assign(args.shards)[args.shard - 1]))
        return 0

    if args.command == "verify":
        if not args.list_file:
            raise SystemExit("error: verify needs --list-file")
        return cmd_verify(args.shards, args.list_file)

    weights = json.loads(WEIGHTS_FILE.read_text(encoding="utf-8")) if WEIGHTS_FILE.exists() else {}
    for i, bucket in enumerate(assign(args.shards), start=1):
        load = sum(weights.get(c, DEFAULT_WEIGHT) for c in bucket)
        print(f"shard {i}: {len(bucket)} classes, ~{load:.0f}s weighted")
        for cls in sorted(bucket):
            print(f"    {weights.get(cls, DEFAULT_WEIGHT):7.1f}s  {cls}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
