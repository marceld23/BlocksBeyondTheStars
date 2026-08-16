#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Validate a story pack under data/stories/<id>/ (implementation plan P1).

A story pack is hand-authored JSON:

    data/stories/<id>/story.json               -- pack config + the ordered beat arc
    data/stories/<id>/locales/en.json|de.json  -- all pack text (bilingual DE+EN)

This checks the pack is internally consistent (ids in order, thresholds monotonic, every beat has a
textKey) and that every referenced beat, finale, insight, fragment, memory, argument and flavour key resolves
in BOTH languages. With --write it also rewrites story.json pretty-printed (stable 2-space formatting).

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
    if story["id"] != pack.name:
        fail(f"pack id '{story['id']}' does not match directory '{pack.name}'")
    if not story.get("nameKey"):
        fail("pack has no 'nameKey'")

    for field in ("finaleRevealTextKey", "finaleResolvedTextKey", "finaleSystemNameKey"):
        if not story.get(field):
            fail(f"pack has no '{field}'")

    beats = story.get("beats", [])
    if not beats:
        fail("pack has no beats")

    keys = {story["nameKey"]}

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

    # The engine is story-agnostic only when every story-owned line travels with the pack. Validate the
    # full surface, not just beats: an absent finale/fragment key otherwise appears as a raw [key] in-game.
    for field in (
        "finaleRevealTextKey",
        "finaleResolvedTextKey",
        "finaleSystemNameKey",
        "companionWardTextKey",
        "shapeAnomalyTextKey",
    ):
        if story.get(field):
            keys.add(story[field])

    fragment_keys = set()
    for i, fragment in enumerate(story.get("fragments", [])):
        fragment_key = fragment.get("key")
        if not fragment_key or fragment_key in fragment_keys:
            fail(f"fragment {i} has a missing or duplicate key '{fragment_key}'")
        fragment_keys.add(fragment_key)
        if not fragment.get("textKey") or not fragment.get("category"):
            fail(f"fragment '{fragment_key}' needs textKey + category")
        keys.add(fragment["textKey"])
        keys.add("lore.cat." + fragment["category"])

    memory_keys = set()
    for i, memory in enumerate(story.get("memories", [])):
        memory_key = memory.get("key")
        if not memory_key or memory_key in memory_keys:
            fail(f"memory {i} has a missing or duplicate key '{memory_key}'")
        memory_keys.add(memory_key)
        if not memory.get("textKey"):
            fail(f"memory '{memory_key}' has no textKey")
        keys.add(memory["textKey"])

    node_keys = set()
    for i, node in enumerate(story.get("coreArguments", [])):
        node_key = node.get("key")
        if not node_key or node_key in node_keys:
            fail(f"core argument {i} has a missing or duplicate key '{node_key}'")
        node_keys.add(node_key)
        if not node.get("promptKey"):
            fail(f"core argument '{node_key}' has no promptKey")
        keys.add(node["promptKey"])
        choices = node.get("choices", [])
        if not choices or sum(bool(choice.get("correct")) for choice in choices) != 1:
            fail(f"core argument '{node_key}' must have choices with exactly one correct answer")
        for j, choice in enumerate(choices):
            if not choice.get("textKey") or not choice.get("responseKey"):
                fail(f"core argument '{node_key}' choice {j} needs textKey + responseKey")
            keys.add(choice["textKey"])
            keys.add(choice["responseKey"])

    flavour_keys = set()
    for i, line in enumerate(story.get("flavourLines", [])):
        flavour_key = line.get("key")
        if not flavour_key or flavour_key in flavour_keys:
            fail(f"flavour line {i} has a missing or duplicate key '{flavour_key}'")
        flavour_keys.add(flavour_key)
        if not line.get("textKey"):
            fail(f"flavour line '{flavour_key}' has no textKey")
        keys.add(line["textKey"])

    for thread in story.get("missionThreads", []):
        fragment_key = thread.get("fragmentKey")
        if fragment_key and fragment_key not in fragment_keys:
            fail(f"mission thread references unknown fragment '{fragment_key}'")

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
