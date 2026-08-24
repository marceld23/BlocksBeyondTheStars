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
  * Each class gets a weight (summed trx seconds from real CI runs; unknown/new classes get a
    default) plus a small per-class floor, and classes are greedy-packed onto the lightest shard.
    Deterministic: same inputs → same assignment on every shard's runner. Shard 1 starts pre-loaded
    with SHARD1_FIXED_COST because tests.yml also gives it Client.Tests and the verify step.
  * Weights are TIER-AWARE (#1067): scripts/test-shard-weights.json holds the fast tier (every
    test without [Trait("Category","Slow")]) and scripts/test-shard-weights-slow.json the Slow
    tests' seconds per class. `--tier fast` (PR gate, `--filter Category!=Slow`) packs on the
    fast weights alone; `--tier full` (main pushes, the release gate) adds the Slow seconds —
    without that, the handful of multi-minute Slow tests all landed on whichever shard the
    fast weights happened to pick, and one shard ran 20+ min while the others idled.
  * The emitted filter ORs `FullyQualifiedName~.<Class>.` tokens. The dots anchor the token
    between namespace and method (`~` is substring matching — a bare `~FloraTests` would also
    match FloraTintTests).

Safety nets, both on shard 1 of every build (`verify`, cross-checked against `dotnet test
--list-tests` output):
  * every discovered test must be matched by EXACTLY one shard's filter, so an oddly named or
    nested class (whose FQN uses `Outer+Inner`, which no token matches) fails the build loudly
    instead of silently never running; and
  * `--weight-drift-guard` (PR gate only) fails once more than UNWEIGHTED_BUDGET of the predicted
    load comes from classes with no measured weight. Stale weights do not announce themselves —
    the packer keeps printing a perfectly balanced split while one shard runs twice as long as
    another — so the guard is what turns "refresh the weights" from folklore into a red build.

Refreshing the weights (when shard durations drift apart) — the SOURCE MATTERS, and getting it
wrong quietly costs most of the benefit. A trx `duration` is wall clock measured under
maxParallelThreads 4, so it carries the contention of the run it came from: in a FULL-tier run the
ordinary tests share the thread pool with multi-minute Slow tests, and the fast-tier seconds
measured there mispredict the PR gate badly (measured out-of-sample: a partition packed on
full-tier fast weights left a 1.53x spread on the next PR run, one packed on fast-tier weights
1.01x). So take each tier's numbers from a run of that tier:

  * the Slow seconds from a FULL-tier run (a push to main or a release — the only place they run), and
  * the fast seconds from a FAST-tier run (any pull request).

A test present in several trx files takes its value from the LAST file given, so passing the full
run FIRST and the fast run SECOND gives exactly that split in one command — the Slow tests keep
their full-tier numbers (they appear nowhere else) while every fast test is overwritten by the PR
run's measurement:

  gh run download <full-tier run id> -D full/ && gh run download <PR run id> -D fast/
  dotnet test tests/BlocksBeyondTheStars.Tests --list-tests --filter Category=Slow > slow.txt
  partition-tests.py weights --trx full/*/server.trx fast/*/server.trx --slow-list slow.txt --write

The same LAST-file-wins rule lets a fresh local run override a single stale class.

Do not expect miracles from re-packing alone: at ~4100 fast-tier CPU-seconds over 4 runners a
PERFECTLY balanced gate still takes ~6 min. Balance buys back the tail, not the suite.

Usage:
  partition-tests.py filter --shard 2 --shards 4 --tier fast   # shard 2's --filter expression
  partition-tests.py verify --shards 4 --tier full --list-file listed.txt [--weight-drift-guard]
  partition-tests.py show --shards 4 --tier full               # human-readable assignment + weights
  partition-tests.py weights --trx a.trx b.trx --slow-list slow.txt [--write]
"""

import argparse
import collections
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEST_DIR = REPO_ROOT / "tests" / "BlocksBeyondTheStars.Tests"
WEIGHTS_FILE = REPO_ROOT / "scripts" / "test-shard-weights.json"
SLOW_WEIGHTS_FILE = REPO_ROOT / "scripts" / "test-shard-weights-slow.json"

# Weight (seconds) for classes without an entry in the weights file — roughly the suite's
# median class cost, so brand-new test classes don't skew a shard until weights are refreshed.
DEFAULT_WEIGHT = 10.0

# Packing floor added to EVERY class weight. Not a measurement: it exists so that a class whose
# measured seconds round to 0 is not *free* to the packer. Without it, greedy packing dumps every
# near-zero class onto whichever shard is momentarily the lightest (adding 0 never makes it stop
# being the lightest) — that is how one shard ended up with 91 classes / 804 tests while another
# ran 54 / 455 even though both were predicted at ~850 s.
CLASS_FLOOR = 0.5

# Shard 1 also restores, builds and runs the small Client.Tests suite plus the partition-
# completeness check (see tests.yml), which shards 2..N skip entirely. Pre-loading its bin with
# that fixed cost makes the packer hand it correspondingly less server work; otherwise shard 1
# systematically finishes minutes before the tail shard. Expressed in the same unit as the
# weights (summed per-test seconds ≈ 3.8× wall clock at maxParallelThreads 4): ~16 s wall.
SHARD1_FIXED_COST = 60.0

# Share of the predicted load that may come from classes the weight files have never seen before
# `verify --weight-drift-guard` fails. Weights go stale silently: a feature batch adds test classes,
# each is guessed at DEFAULT_WEIGHT, and the packer keeps reporting a perfectly balanced partition
# while one shard runs twice as long as another. At 5 % the guard trips well before that hurts.
UNWEIGHTED_BUDGET = 0.05

CLASS_RE = re.compile(r"^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)", re.MULTILINE)


def discover_classes() -> list[str]:
    """All class names declared in the test project (excluding bin/obj)."""
    names: set[str] = set()
    for path in sorted(TEST_DIR.glob("*.cs")):
        names.update(CLASS_RE.findall(path.read_text(encoding="utf-8-sig")))
    if not names:
        raise SystemExit(f"error: no classes found under {TEST_DIR} — wrong checkout?")
    return sorted(names)


def _load(path: Path) -> dict[str, float]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def load_weights(tier: str) -> dict[str, float]:
    """Per-class seconds for the given tier: fast weights, plus the Slow seconds on the full tier.

    A class only listed in the Slow file (every test in it is Slow) still gets DEFAULT_WEIGHT on the
    fast tier — its filter matches nothing there, so the cost is a rounding error either way.
    """
    weights = dict(_load(WEIGHTS_FILE))
    if tier == "full":
        for cls, secs in _load(SLOW_WEIGHTS_FILE).items():
            weights[cls] = weights.get(cls, 0.0) + secs
    return weights


def weight_of(cls: str, weights: dict[str, float]) -> float:
    """The class's packing cost: its measured seconds (or the default guess) plus the class floor."""
    return weights.get(cls, DEFAULT_WEIGHT) + CLASS_FLOOR


def assign(shards: int, tier: str) -> list[list[str]]:
    """Greedy bin-packing: heaviest class first onto the currently lightest shard."""
    weights = load_weights(tier)
    buckets: list[list[str]] = [[] for _ in range(shards)]
    # Shard 1 starts out already loaded with the extra suite tests.yml gives it.
    loads = [SHARD1_FIXED_COST] + [0.0] * (shards - 1)
    # Sort by (-weight, name): deterministic even among equal weights.
    for cls in sorted(discover_classes(), key=lambda c: (-weight_of(c, weights), c)):
        target = loads.index(min(loads))
        buckets[target].append(cls)
        loads[target] += weight_of(cls, weights)
    return buckets


TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def _trx_seconds(text: str) -> float:
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def cmd_weights(trx_files: list[Path], slow_list: Path | None, write: bool) -> int:
    """Rebuild both weight files from real trx runs (later files override earlier ones per test)."""
    slow_tests: set[str] = set(parse_listed_tests(slow_list)) if slow_list else set()
    # Keyed by the UnitTest display name, which carries the theory arguments (each [InlineData]
    # case is its own result; TestMethod/@name does NOT include them). Slow membership is checked
    # on the argument-less FQN, which is what --list-tests prints.
    per_test: dict[str, tuple[str, str, float]] = {}  # display name -> (class, fqn, seconds)
    for trx in trx_files:
        root = ET.parse(trx).getroot()
        methods = {}
        for ut in root.iter(f"{TRX_NS}UnitTest"):
            tm = ut.find(f"{TRX_NS}TestMethod")
            methods[ut.get("id")] = (tm.get("className"), tm.get("name"), ut.get("name"))
        for res in root.iter(f"{TRX_NS}UnitTestResult"):
            cls_fqn, name, display = methods[res.get("testId")]
            fqn = f"{cls_fqn}.{name.split('(', 1)[0]}"
            per_test[display] = (cls_fqn.split(".")[-1], fqn, _trx_seconds(res.get("duration", "0:0:0")))
    fast: dict[str, float] = collections.defaultdict(float)
    slow: dict[str, float] = collections.defaultdict(float)
    for cls, fqn, secs in per_test.values():
        (slow if fqn in slow_tests else fast)[cls] += secs
    fast_out = {c: round(s, 1) for c, s in sorted(fast.items())}
    slow_out = {c: round(s, 1) for c, s in sorted(slow.items()) if s >= 0.05}
    print(f"{len(per_test)} tests from {len(trx_files)} trx file(s); {len(fast_out)} classes fast "
          f"({sum(fast_out.values()):.0f}s), {len(slow_out)} classes with Slow tests ({sum(slow_out.values()):.0f}s)")
    if slow_list and not slow:
        print("warning: none of the listed Slow tests appear in the trx files — was that a fast-tier run?")
    if write:
        WEIGHTS_FILE.write_text(json.dumps(fast_out, indent=2) + "\n", encoding="utf-8", newline="\n")
        SLOW_WEIGHTS_FILE.write_text(json.dumps(slow_out, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"wrote {WEIGHTS_FILE.name} + {SLOW_WEIGHTS_FILE.name}")
    else:
        for cls, secs in sorted(slow_out.items(), key=lambda kv: -kv[1])[:10]:
            print(f"  slow {secs:7.1f}s  {cls}  (fast {fast_out.get(cls, 0.0):.1f}s)")
    return 0


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


def check_weight_drift(shards: int, tier: str, listed: list[str], guard: bool) -> int:
    """Report test classes the weight files have never seen — the silent cause of shard imbalance.

    Only classes that actually CONTAIN tests count: discover_classes() also picks up helpers,
    base classes and collection markers, which legitimately never appear in a trx. The listed
    FQNs (`Namespace.Class.Method`) give exactly the real ones.
    """
    weights = load_weights(tier)
    # On the fast tier a class whose every test is Slow has no fast entry BY DESIGN (its filter
    # matches nothing there) — it was measured, so it is not drift. Both files count as "known".
    known = set(weights) | set(_load(SLOW_WEIGHTS_FILE))
    classes = {fqn.rsplit(".", 2)[-2] for fqn in listed}
    unweighted = sorted(c for c in classes if c not in known)
    total = sum(weight_of(c, weights) for c in classes) + SHARD1_FIXED_COST
    guessed = sum(weight_of(c, weights) for c in unweighted)
    share = guessed / total if total else 0.0
    if not unweighted:
        print(f"Weights cover all {len(classes)} test classes.")
        return 0
    print(f"{len(unweighted)} of {len(classes)} test classes have no weight and are guessed at "
          f"{DEFAULT_WEIGHT:.0f}s: {share:.1%} of the predicted load ({', '.join(unweighted[:12])}"
          f"{', …' if len(unweighted) > 12 else ''})")
    if not guard or share <= UNWEIGHTED_BUDGET:
        # A single new test class per PR is normal — say so and move on.
        print(f"::notice::{len(unweighted)} test class(es) without a shard weight "
              f"({share:.1%} of the predicted load, budget {UNWEIGHTED_BUDGET:.0%}).")
        return 0
    print(f"::error::Shard weights are stale: {share:.1%} of the predicted load is guesswork "
          f"(budget {UNWEIGHTED_BUDGET:.0%}). The partition reports a balanced split it cannot "
          f"deliver — refresh scripts/test-shard-weights*.json (see partition-tests.py header).")
    return 1


def cmd_verify(shards: int, tier: str, list_file: Path, weight_drift_guard: bool) -> int:
    buckets = assign(shards, tier)
    tokens = [[f".{cls}." for cls in bucket] for bucket in buckets]
    listed = parse_listed_tests(list_file)
    bad = []
    for fqn in listed:
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
    print(f"All {len(listed)} listed tests map to exactly one of {shards} shards.")
    return check_weight_drift(shards, tier, listed, weight_drift_guard)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["filter", "verify", "show", "weights"])
    parser.add_argument("--shards", type=int, help="total shard count (filter/verify/show)")
    parser.add_argument("--shard", type=int, help="1-based shard index (filter)")
    parser.add_argument("--tier", choices=["fast", "full"], default="fast",
                        help="fast = PR gate (Category!=Slow), full = whole suite incl. Slow (default: fast)")
    parser.add_argument("--list-file", type=Path, help="dotnet test --list-tests output (verify)")
    parser.add_argument("--weight-drift-guard", action="store_true",
                        help="verify: fail when more than UNWEIGHTED_BUDGET of the predicted load "
                             "comes from classes with no measured weight (PR gate only)")
    parser.add_argument("--trx", type=Path, nargs="+", help="trx result files to derive weights from (weights)")
    parser.add_argument("--slow-list", type=Path, help="--list-tests --filter Category=Slow output (weights)")
    parser.add_argument("--write", action="store_true", help="write the weight files instead of only printing (weights)")
    args = parser.parse_args()

    if args.command == "weights":
        if not args.trx:
            raise SystemExit("error: weights needs --trx <file>...")
        return cmd_weights(args.trx, args.slow_list, args.write)

    if not args.shards or args.shards < 1:
        raise SystemExit("error: --shards is required")

    if args.command == "filter":
        if not args.shard or not 1 <= args.shard <= args.shards:
            raise SystemExit("error: --shard must be in 1..--shards")
        print(shard_filter(assign(args.shards, args.tier)[args.shard - 1]))
        return 0

    if args.command == "verify":
        if not args.list_file:
            raise SystemExit("error: verify needs --list-file")
        return cmd_verify(args.shards, args.tier, args.list_file, args.weight_drift_guard)

    weights = load_weights(args.tier)
    for i, bucket in enumerate(assign(args.shards, args.tier), start=1):
        load = sum(weight_of(c, weights) for c in bucket) + (SHARD1_FIXED_COST if i == 1 else 0.0)
        unweighted = sum(1 for c in bucket if c not in weights)
        extra = f", +{SHARD1_FIXED_COST:.0f}s Client.Tests/verify" if i == 1 else ""
        print(f"shard {i}: {len(bucket)} classes ({unweighted} unweighted), ~{load:.0f}s weighted{extra}")
        for cls in sorted(bucket):
            print(f"    {weights.get(cls, DEFAULT_WEIGHT):7.1f}s  {cls}{'' if cls in weights else '  (guessed)'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
