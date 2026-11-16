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
  client   — the diff is the client side: client/**, src/BlocksBeyondTheStars.Client.Core/**,
             tests/BlocksBeyondTheStars.Client.Tests/** and the code-health baseline. The server
             test project does not reference Client.Core (`verify` checks the project graph), so
             its compiled inputs are unchanged and a server test can only notice such a diff by
             READING repository files. Every route out of data/ is a marker here — the TestPaths
             members that hand out the repository root, a self-made walk up from the output
             folder, a step to a parent folder — so the lane runs every server class that reads
             anything outside data/, whatever it reads. ci.yml runs the whole Client.Tests suite
             beside it (`lane-client-tests`); nothing is selected there.

How a lane's class set is found:
  * A marker scan: every test class in a server-suite file whose source matches the lane's
    marker (the path fragment or call a reader normally writes). Self-maintaining — a second
    test that opens the files joins the lane by existing.
  * HAND_EXTRAS per lane: classes the lane must contain whatever the scan says, and consumers
    the scan cannot see. `verify` fails on an entry that no longer exists, so the list cannot
    rot silently.
  * KNOWN_HELPERS per lane: a marker-matching file with no test of its own is a helper, and the
    classes that reach the files THROUGH it carry no marker. Each such file must be listed with
    the reason its users are covered; `verify` fails on a helper nobody looked at, and on a
    listed one that is gone. Look at what the helper reads: if it is a lane file, add its users
    to HAND_EXTRAS; then list it.
  * follow_members (the `client` lane): a marker-matching file that DOES declare tests can still
    lend a static member to other classes (`StoryPackValidationTests.Context(…)` checks packs
    against the client's music and audio folders for a dozen classes). Every file that uses
    such a member joins the lane, transitively; `harmless_members` names the ones that read
    nothing, each with its reason.

The corruption experiment checks a class set from the outside: overwrite the lane's files with
invalid content in a throw-away commit of a PR that runs the full matrix; every class that turns
red belongs in the lane. (That finds readers of the CONTENT. A test that only reacts to a file
appearing or disappearing needs the same with a file added and one removed.) Done for `whatsnew`
(2026-11-16: 13 750 tests, the three failures all in WhatsNewContentTests); NOT done for
`client`, which rests on the project graph and the helper rule above.

A lane whose ci.yml job runs the server suite only (`whatsnew`) fails `verify` when a
Client.Tests file matches the marker: such a test would be skipped without anyone noticing, and
the lane has to grow a second test run first. It is a plain text match, so a Client.Tests file
that merely QUOTES the path (a wiring guard over the Unity script's URL constants) trips it too —
then either build the lane's second run or word the guard differently.

`verify` runs in two places: in the lane job itself, and on shard 1 of every pull request's
matrix (tests.yml, `lane-filter-guard`) — so the PR that breaks a lane's filter is the one that
goes red, not the next PR that would have taken the lane.

Safety framing (same as the locale lane): an over-broad set costs seconds; an under-broad set is
caught downstream — a push to main runs the COMPLETE suite and release.yml re-runs it on the
tagged commit. A miss can redden main after the merge but cannot reach a release.

Filter tokens are dot-anchored (`FullyQualifiedName~.<Class>.`) exactly like
scripts/partition-tests.py, so a token can never substring-match a longer class name.

Usage:
  lane-test-filter.py <lane>                # print the --filter expression
  lane-test-filter.py <lane> verify         # sanity-check the set (ci.yml runs this first)
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
SERVER_TESTS_PROJECT = SERVER_TESTS / "BlocksBeyondTheStars.Tests.csproj"

# Same declaration regex as partition-tests.py: one file may declare several classes, and
# non-test classes ride along harmlessly (their tokens match nothing).
CLASS_RE = re.compile(
    r"^\s*(?:public|internal)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_]\w*)",
    re.MULTILINE,
)

# A file with one of these declares a test; a marker-matching file without is a helper.
TEST_ATTRIBUTE_RE = re.compile(r"\[\s*(?:Fact|Theory)\b")

PROJECT_REFERENCE_RE = re.compile(r"<ProjectReference\s+Include=\"([^\"]+)\"")

TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


@dataclass(frozen=True)
class Lane:
    """One narrow lane: how its readers are recognised, and what the scan cannot see."""

    marker: re.Pattern[str]
    # Classes the lane must contain whatever the scan says. Keep sorted.
    hand_extras: list[str] = field(default_factory=list)
    # Marker-matching files without a test of their own -> why the classes using them are covered.
    known_helpers: dict[str, str] = field(default_factory=dict)
    # True when ci.yml runs the whole Client.Tests suite for this lane (then a Client.Tests file
    # matching the marker is expected, not a hole).
    runs_client_tests: bool = False
    # Project files (by name) that must stay OUT of the server test project's reference closure:
    # the lane's claim that a diff of that project cannot change the server test binary.
    unreferenced_projects: list[str] = field(default_factory=list)
    # True: a file that uses a static member of a class declared in a marker-matching test file
    # (`StoryPackValidationTests.Context(…)`) joins the lane, and so on transitively — the member
    # may read the lane's files for its caller, who carries no marker. Known helpers are not
    # followed (their entry says why).
    follow_members: bool = False
    # "Class.Member" -> why using it does not make a class a reader of the lane's files.
    harmless_members: dict[str, str] = field(default_factory=dict)


LANES: dict[str, Lane] = {
    # "whatsnew.json" is the feed's file name (a reader builds the path from DataDir() + that
    # name); "data-online" is the folder of the per-language files.
    "whatsnew": Lane(marker=re.compile(r"whatsnew\.json|data-online")),
    # Every way a server test gets at a file outside data/: the TestPaths members that return or
    # walk the repository root, a walk of its own from the output folder or the source path, and
    # any step to a parent folder (Path.GetDirectoryName(TestPaths.DataDir()) is the repository
    # root too). `"client",` / `"client")` is the path segment itself — the comma/bracket keeps
    # prose like <paramref name="client"/> out. Deliberately broad: a class that only reads
    # tools/ or docs/ rides along for a few seconds.
    "client": Lane(
        marker=re.compile(
            r"RepoRoot\(|SourceFiles\(|ClientIconsDir\(|PlayerControllerSources\("
            r"|AppContext\.BaseDirectory|GetCurrentDirectory\(|CallerFilePath"
            r"|GetParent\(|GetDirectoryName\(|\"\.\.\""
            r"|\"client\"\s*[,)]"
        ),
        hand_extras=[
            "BugReportPathsTests",     # walks up from the output folder itself, not through TestPaths
            "CodeHealthRatchetTests",  # measures client/ and src/ through CodeHealthScanner; reads the baseline
        ],
        known_helpers={
            "TestPaths.cs": "defines the members the marker looks for; every caller carries the marker itself",
            "CodeHealthScanner.cs": "walks client/ and src/ sources for CodeHealthRatchetTests (hand-listed)",
            "AdminTestWorld.cs": "RepoRoot() only for the git-ignored bugreports/server folder",
            "StoryPackFixtures.cs": "RepoRoot() only for the server suite's own Fixtures/story_packs",
        },
        runs_client_tests=True,
        unreferenced_projects=["BlocksBeyondTheStars.Client.Core.csproj"],
        # StoryPackValidationTests.Context(…) checks story packs against client/Music and the
        # client's audio clips for a dozen other classes — they join the lane through this rule.
        follow_members=True,
        harmless_members={
            "ServerConfigEnvironmentCollection.Name": "the xunit collection's name, a constant",
            "ServerConfigTests.WithEnvironment": "sets and restores environment variables, reads no file",
            "StoryPackValidationTests.Shipped": "the content of data/, loaded once",
            "StoryPackValidationTests.Strict": "JSON serializer options",
        },
    ),
}


def source_files(directory: Path, recursive: bool) -> list[Path]:
    paths = directory.rglob("*.cs") if recursive else directory.glob("*.cs")
    return sorted(p for p in paths if not {"bin", "obj"} & set(p.relative_to(directory).parts))


def scan(directory: Path, marker: re.Pattern[str]) -> tuple[set[str], set[str], list[str], list[str]]:
    """Scans a test project.

    Returns (classes of marker-matching root files, all root classes, every marker-matching file,
    the marker-matching files that declare no test). Test classes live in the project root only
    (partition-tests.py discovers them the same way); sub-folders such as Helpers/ are searched
    for the marker all the same, because a helper there can read the lane's files for its users.
    """
    hit_classes: set[str] = set()
    all_classes: set[str] = set()
    hit_files: list[str] = []
    helpers: list[str] = []
    for path in source_files(directory, recursive=True):
        text = path.read_text(encoding="utf-8-sig")
        in_root = path.parent == directory
        classes = CLASS_RE.findall(text) if in_root else []
        all_classes.update(classes)
        if not marker.search(text):
            continue
        name = path.relative_to(directory).as_posix()
        hit_files.append(name)
        hit_classes.update(classes)
        if not in_root or not TEST_ATTRIBUTE_RE.search(text):
            helpers.append(name)
    return hit_classes, all_classes, hit_files, helpers


def member_users(directory: Path, seed_files: set[str], not_followed: set[str], harmless: set[str]) -> set[str]:
    """Classes of the root files that use a static member of a seed file's class, transitively.

    `Class.Member` is matched as text (`(?<![\\w.])Class\\.Member`), so a mention in a comment
    joins too — over-selection, which only costs time. Members listed as harmless do not count.
    """
    texts = {p.name: p.read_text(encoding="utf-8-sig") for p in source_files(directory, recursive=False)}
    classes = {name: CLASS_RE.findall(text) for name, text in texts.items()}
    joined = set(seed_files)
    frontier = {f for f in seed_files if f in texts} - not_followed
    while frontier:
        found: set[str] = set()
        for source in frontier:
            for cls in classes[source]:
                use = re.compile(rf"(?<![\w.]){re.escape(cls)}\.([A-Z]\w*)")
                for name, text in texts.items():
                    if name not in joined and any(f"{cls}.{member}" not in harmless for member in use.findall(text)):
                        found.add(name)
        joined |= found
        frontier = found
    return {cls for name in joined - seed_files for cls in classes[name]}


def referenced_projects(project: Path, seen: set[Path] | None = None) -> set[Path]:
    """Every project file in the reference closure of the given one (itself included)."""
    seen = set() if seen is None else seen
    project = project.resolve()
    if project in seen:
        return seen
    seen.add(project)
    for include in PROJECT_REFERENCE_RE.findall(project.read_text(encoding="utf-8-sig")):
        referenced_projects(project.parent / include.replace("\\", "/"), seen)
    return seen


def lane_bucket(name: str) -> list[str]:
    """The lane's class set, or SystemExit with the reason it cannot be trusted."""
    lane = LANES[name]
    scanned, all_classes, hit_files, helpers = scan(SERVER_TESTS, lane.marker)

    missing = [c for c in lane.hand_extras if c not in all_classes]
    if missing:
        raise SystemExit(f"error: HAND_EXTRAS of lane '{name}' no longer exist as classes: {missing}")
    if not scanned:
        # Never emit an empty filter: `dotnet test` would report "no test matches" and exit 0.
        raise SystemExit(f"error: the marker scan of lane '{name}' found no test class — the scan is broken "
                         f"or the guarding tests moved")

    unknown = sorted(set(helpers) - set(lane.known_helpers))
    if unknown:
        raise SystemExit(f"error: lane '{name}': {', '.join(unknown)} match(es) the lane's marker but declare(s) no "
                         f"test — a helper. The classes that use it carry no marker, so the lane cannot see them. "
                         f"Check what it reads: if it is one of the lane's files, add its users to the lane's "
                         f"hand_extras; then list the file under known_helpers in scripts/lane-test-filter.py")
    stale = sorted(set(lane.known_helpers) - set(helpers))
    if stale:
        raise SystemExit(f"error: known_helpers of lane '{name}' no longer match the marker or are gone: {stale} "
                         f"— remove them from scripts/lane-test-filter.py")

    gone = sorted(m for m in lane.harmless_members if m.split(".")[0] not in scanned)
    if gone:
        raise SystemExit(f"error: harmless_members of lane '{name}' name classes the marker scan no longer finds: "
                         f"{gone} — remove them from scripts/lane-test-filter.py")
    if lane.follow_members:
        scanned |= member_users(SERVER_TESTS, set(hit_files), set(lane.known_helpers), set(lane.harmless_members))

    if lane.unreferenced_projects:
        closure = {p.name for p in referenced_projects(SERVER_TESTS_PROJECT)}
        linked = sorted(set(lane.unreferenced_projects) & closure)
        if linked:
            raise SystemExit(f"error: lane '{name}' assumes the server test project does not reference "
                             f"{', '.join(linked)}, but it does now — a diff of that project changes the server "
                             f"test binary, so the lane may no longer skip the matrix for it")

    if not lane.runs_client_tests:
        _, _, client_hits, _ = scan(CLIENT_TESTS, lane.marker)
        if client_hits:
            raise SystemExit(f"error: Client.Tests reads the files of lane '{name}' ({', '.join(client_hits)}), but "
                             f"the lane job runs the server suite only — add a Client.Tests run to ci.yml's "
                             f"lane-tests before this lane may skip the matrix")

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
