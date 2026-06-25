#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Validate a story pack under data/stories/<id>/ (implementation plan P1).

A story pack is hand-authored JSON:

    data/stories/<id>/story.json               -- pack config + the ordered beat arc
    data/stories/<id>/locales/en.json|de.json  -- the beat text (bilingual DE+EN)

This checks the pack is internally consistent (ids in order, thresholds monotonic, every beat has a
textKey) and that every locale key the pack references resolves in BOTH languages. With --write it also
rewrites story.json pretty-printed (stable 2-space formatting).

Usage:
    python tools/merge_story.py data/stories/vega_protocol
    python tools/merge_story.py data/stories/vega_protocol --write
"""
import json
import pathlib
import sys


def fail(msg: str) -> "None":
    print("ERROR:", msg)
    sys.exit(1)


def main() -> "None":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    write = "--write" in sys.argv[1:]
    if len(args) != 1:
        fail("usage: merge_story.py <pack-dir> [--write]")

    pack = pathlib.Path(args[0])
    story_file = pack / "story.json"
    if not story_file.exists():
        fail(f"{story_file} not found")

    story = json.loads(story_file.read_text(encoding="utf-8"))
    if not story.get("id"):
        fail("pack has no 'id'")

    beats = story.get("beats", [])
    if not beats:
        fail("pack has no beats")

    keys = set()
    if story.get("nameKey"):
        keys.add(story["nameKey"])

    last_threshold = None
    for i, beat in enumerate(beats):
        if beat.get("index") != i:
            fail(f"beat {i} has out-of-order index {beat.get('index')}")
        threshold = beat.get("threshold", 0)
        if last_threshold is not None and threshold < last_threshold:
            fail(f"beat {i} threshold {threshold} is below the previous {last_threshold}")
        last_threshold = threshold
        if not beat.get("textKey"):
            fail(f"beat {i} has no textKey")
        keys.add(beat["textKey"])

    # Every referenced locale key must resolve in BOTH languages.
    for code in ("en", "de"):
        loc_file = pack / "locales" / f"{code}.json"
        if not loc_file.exists():
            fail(f"missing locale file {loc_file}")
        loc = json.loads(loc_file.read_text(encoding="utf-8"))
        missing = sorted(k for k in keys if k not in loc)
        if missing:
            fail(f"{code}.json is missing keys: {missing}")

    if write:
        story_file.write_text(json.dumps(story, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("(rewrote story.json pretty-printed)")

    print(f"OK: '{story['id']}' - {len(beats)} beats, {len(keys)} locale keys, en+de complete.")


if __name__ == "__main__":
    main()
