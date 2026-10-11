#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Validate story packs under data/stories/<id>/ (#2466, the successor of merge_story.py).

A story pack is hand-authored JSON:

    data/stories/<id>/story.json               -- the pack manifest (+ the beat arc for a world story)
    data/stories/<id>/locales/en.json|de.json  -- the pack's texts (English and German are mandatory)
    data/stories/<id>/cutscenes.json           -- optional cutscene definitions (step 8 of the story packs)

The checks are the same as the C# StoryPackValidator (StoryPackValidationTests), for authors without .NET:
ids (unique, not reserved, key-safe), kind/scope (a tutorial is player-scoped, a player pack has no beats, a world
pack has beats), every condition parses and names existing packs/chapters/steps/blueprints, mission-bridge targets
exist in data/missions.json, event keys are published (warning), 'ui:' only in a player pack's step objectives,
marker providers, legacy keys (tutorial only), alternatives, a tutorial pays no rewards, every locale key exists in
English and German (pack table or base table), every cutscene binding resolves in the pack's cutscenes.json (#2467),
every cutscene follows the rules (ids, tier 1-4, cameras, anchors, caption styles, grades, shot lengths, a T1/T2 never
takes the camera, only a T3/T4 that takes it may hold the world or freeze the controls, chainNext references, chapters,
music in client/Music and sounds in client/Assets/Resources/audio, caption budgets: subtitle/speaker 220 EN / 264 DE,
title card 60 / 72, reading frame 600 / 720), the short name fits (<= 10 characters) and the text budget
(basic-training lines <= 120 EN / 145 DE, other step lines <= 200 / 240, chip <= 45 / 53; a {key:...} token counts 3,
a {0} slot 10). Packs without beats are fine. Unknown fields are reported as warnings (typos). Every problem is
reported at once. The tool NEVER rewrites a file.

Usage:
    uv run --no-project python tools/validate_story_pack.py                          # every pack in data/stories
    uv run --no-project python tools/validate_story_pack.py data/stories/vega_protocol
    uv run --no-project python tools/validate_story_pack.py <pack-dir> --strict      # warnings fail too

Exit code 0 = no errors (warnings allowed unless --strict), 1 = errors.
"""
from __future__ import annotations

import json
import pathlib
import re
import sys

RESERVED_IDS = {"none", "mem", "insight", "obj", "vega", "track", "cut"}
KINDS = {"story", "tutorial", "side"}
SCOPES = {"", "world", "player"}
STEP_KINDS = {"do", "read", "react"}
STEP_TIERS = {"must", "core", "opt"}
GATES = {"opportunityTips", "banter", "stationNear"}
LEGACY_STAGES = ["mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land"]
PACK_STATES = {"started", "done", "enabled", "optedout"}
SHORT_NAME_MAX = 10
BASIC_LINE = (120, 145)
LINE = (200, 240)
CHIP = (45, 53)
TOKEN_WIDTH = 3
SLOT_WIDTH = 10
MAX_DEPTH = 16

# Cutscenes (#2467) -- the same vocabularies as Shared/Story/CutsceneDefinition.cs.
CUTSCENE_TIERS = {1, 2, 3, 4}  # 1 banner, 2 card, 3 staged scene, 4 set piece
CAMERAS = ["player_view", "overlay", "static", "orbit", "crane", "sky", "chart"]
NON_TAKEOVER_CAMERAS = {"player_view", "overlay"}
ANCHORS = ["player", "ship", "planet", "station", "star_map", "heal_tank", "core", "custom"]
CAPTION_STYLES = ["subtitle", "title_card", "speaker", "reading_frame", "credits"]
GRADES = ["none", "memory", "flashback"]
RECAP_ID = "__recap"
CAPTION_LINE = (220, 264)
TITLE_CARD = (60, 72)
READING_FRAME = (600, 720)
SHOT_MAX_SECONDS = 120

# Fallbacks when the C# sources are not next to the data folder (they are read from there when present).
FALLBACK_PREDICATES = [
    "station_in_system", "settlement_reachable", "airless", "fragment_on_world", "player_near", "enemies",
    "space_enemies", "bandits", "free_flight", "hunger", "oxygen", "hazards", "creative", "creative_flight",
    "instant_travel", "return_to_ship", "starter_teleporter", "base_visitors", "space_stations", "story_active",
    "food_plants_near", "trees_near", "shape_block_carried", "research_ready", "all_blueprints", "new_world_in_system",
]

KNOWN_FIELDS = {
    "pack": {
        "id", "nameKey", "kind", "scope", "priority", "shortNameKey", "descriptionKey", "narrator", "defaultEnabled",
        "introCutscene", "gates", "escalate", "start", "chapters", "insights", "finale", "fragmentWeight",
        "killWeight", "milestoneWeight", "killContributionCap", "beats", "fragments", "memories", "coreArguments",
        "flavourLines", "missionThreads", "loreSites", "npcThreads", "characters", "dialogs", "epilogueTitle",
        "epilogueTitleKey", "epilogueTextKey", "graduation",
    },
    "graduation": {"lineKey", "counter", "suggest"},
    "narrator": {"id", "nameKey", "icon", "color", "voice", "lineStart"},
    "escalate": {"hintAfter", "markerAfter", "offerSkipAfter"},
    "start": {"after", "earlyOptIn"},
    "chapter": {
        "id", "titleKey", "summaryKey", "optional", "requires", "offerWhen", "autoCompleteIf", "autoStart",
        "openCutscene", "closeCutscene", "fromBeat", "fromEvent", "leads", "steps", "skippable",
    },
    "lead": {"type", "objKey"},
    "step": {
        "id", "kind", "tier", "legacyKey", "objective", "requires", "feasibleIf", "alternative", "alreadyDoneIf",
        "variants", "explain", "onStart", "onComplete", "escalate", "skippable", "hintRefs",
    },
    "objective": {"event", "arg", "required", "type", "target"},
    "explain": {"startKey", "chipKey", "hintKey", "doneKey", "moreGuide"},
    "variant": {"when", "startKey", "chipKey", "hintKey", "doneKey"},
    "hook": {"cutscene", "reveal"},
    "insight": {"event", "minBeat", "textKey", "onceKey"},
    "finale": {"type", "cutscenes"},
    "beat": {"index", "title", "textKey", "threshold", "knowledgeReward", "cutscene"},
    "cutscene_file": {"cutscenes"},
    "cutscene": {"id", "tier", "chapter", "template", "replaceLineKeys", "music", "shots", "options"},
    "shot": {"camera", "duration", "captions", "grade", "sfx"},
    "camera": {"type", "anchor"},
    "caption": {"key", "style", "kickerKey", "speaker", "at", "hold"},
    "sfx": {"cue", "at"},
    "cutscene_options": {
        "holdWorld", "hideHud", "lockInput", "skippable", "minDisplay", "chainNext", "cooldownExempt", "replayable",
    },
}


class Report:
    """Collects every problem of one run."""

    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def error(self, pack: str, path: str, message: str) -> None:
        self.errors.append(f"ERROR   {pack}: {path}: {message}")

    def warn(self, pack: str, path: str, message: str) -> None:
        self.warnings.append(f"WARNING {pack}: {path}: {message}")


def load_jsonc(path: pathlib.Path):
    """Loads JSON that may carry // comments and trailing commas (the content loader accepts both)."""
    text = path.read_text(encoding="utf-8-sig")
    out = []
    i, n = 0, len(text)
    in_string = False
    while i < n:
        c = text[i]
        if in_string:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_string = False
            i += 1
            continue
        if c == '"':
            in_string = True
            out.append(c)
            i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            end = text.find("*/", i + 2)
            i = n if end < 0 else end + 2
            continue
        out.append(c)
        i += 1
    cleaned = re.sub(r",(\s*[\]}])", r"\1", "".join(out))
    return json.loads(cleaned)


def csharp_constants(source: pathlib.Path, class_name: str) -> list[str]:
    """The string constants of one C# static class (the published vocabularies), [] when the file is missing."""
    if not source.exists():
        return []
    text = source.read_text(encoding="utf-8")
    start = text.find(f"class {class_name}")
    if start < 0:
        return []
    depth = 0
    body_start = text.find("{", start)
    end = body_start
    for end in range(body_start, len(text)):
        if text[end] == "{":
            depth += 1
        elif text[end] == "}":
            depth -= 1
            if depth == 0:
                break
    body = text[body_start:end]
    return re.findall(r'public const string \w+ = "([^"]*)";', body)


def ui_event_ids(source: pathlib.Path) -> list[str]:
    """The ids of GameEventKeys.UiEvents: the constants named Ui...Id ([] when the source is missing)."""
    if not source.exists():
        return []
    return re.findall(r'public const string Ui\w+Id = "([^"]*)";', source.read_text(encoding="utf-8"))


def is_key_safe(value: str, allow_dot: bool = False) -> bool:
    pattern = r"[A-Za-z0-9_.\-]+" if allow_dot else r"[A-Za-z0-9_\-]+"
    return bool(value) and re.fullmatch(pattern, value) is not None


def shown_length(text: str) -> int:
    """Characters a line shows: a {key:...} token counts TOKEN_WIDTH, a {0} slot SLOT_WIDTH."""
    text = re.sub(r"\{key:[^}]*\}", "\x01" * TOKEN_WIDTH, text)
    text = re.sub(r"\{[0-9]\}", "\x02" * SLOT_WIDTH, text)
    return len(text)


class ConditionError(Exception):
    pass


class ConditionParser:
    """The story-condition grammar of StoryConditionParser (Shared/Story/StoryCondition.cs)."""

    ID = re.compile(r"[A-Za-z0-9_.\-]*")

    def __init__(self, text: str, predicates: set[str]) -> None:
        self.text = text
        self.pos = 0
        self.predicates = predicates

    def fail(self, what: str):
        raise ConditionError(f'"{self.text}": {what} (at position {self.pos})')

    def spaces(self) -> None:
        while self.pos < len(self.text) and self.text[self.pos].isspace():
            self.pos += 1

    def read_id(self) -> str:
        m = self.ID.match(self.text, self.pos)
        self.pos = m.end()
        return m.group(0)

    def expect(self, ch: str, what: str) -> None:
        if self.pos >= len(self.text) or self.text[self.pos] != ch:
            self.fail(what)
        self.pos += 1

    def parse(self):
        self.spaces()
        atoms: list[tuple] = []
        self.condition(0, atoms)
        self.spaces()
        if self.pos < len(self.text):
            self.fail("unexpected text after the condition")
        return atoms

    def pack_id(self, form: str) -> str:
        pack = self.read_id()
        if not pack:
            self.fail(f"the pack id of '{form}:' is missing")
        if pack.lower() in RESERVED_IDS:
            self.fail(f"'{pack}' is a reserved pack id")
        return pack

    def condition(self, depth: int, atoms: list) -> None:
        if depth > MAX_DEPTH:
            self.fail(f"nested deeper than {MAX_DEPTH} levels")
        start = self.pos
        word = self.read_id()
        if not word:
            self.fail("a condition is missing")
        self.expect(":", f"'{word}' must be followed by ':'")
        if word in ("all", "any"):
            self.spaces()
            self.expect("[", "a group needs '[' ... ']'")
            self.spaces()
            if self.pos < len(self.text) and self.text[self.pos] == "]":
                self.fail("an empty group never says what it waits for")
            while True:
                self.spaces()
                self.condition(depth + 1, atoms)
                self.spaces()
                if self.pos < len(self.text) and self.text[self.pos] == ",":
                    self.pos += 1
                    continue
                self.expect("]", "expected ',' or ']'")
                return
        if word == "not":
            self.condition(depth + 1, atoms)
            return
        if word in ("step", "chapter"):
            pack = self.pack_id(word)
            self.expect(":", f"'{word}:{pack}' needs ':<{word}>'")
            ident = self.read_id()
            if not ident:
                self.fail(f"the {word} id is missing")
            atoms.append((word, pack, ident))
            return
        if word == "pack":
            pack = self.pack_id(word)
            self.expect(":", f"'pack:{pack}' needs ':started', ':done', ':enabled' or ':optedout'")
            state = self.read_id()
            if state not in PACK_STATES:
                self.fail(f"unknown pack state '{state}' (started, done, enabled, optedout)")
            atoms.append(("pack", pack, state))
            return
        if word == "blueprint":
            key = self.read_id()
            if not key:
                self.fail("the blueprint key is missing")
            atoms.append(("blueprint", "", key))
            return
        if word == "world":
            name = self.read_id()
            if name not in self.predicates:
                self.fail(f"unknown world predicate '{name}' (known: {', '.join(sorted(self.predicates))})")
            atoms.append(("world", "", name))
            return
        self.pos = start
        if word == "ui":
            self.fail("'ui:' events are client claims and never a condition")
        self.fail(f"unknown condition '{word}:' (step, chapter, pack, blueprint, world, all, any, not)")


class Validator:
    def __init__(self, data_dir: pathlib.Path, report: Report) -> None:
        self.data = data_dir
        self.report = report
        repo = data_dir.parent
        shared = repo / "src" / "BlocksBeyondTheStars.Shared" / "Story"
        self.predicates = set(csharp_constants(shared / "StoryCondition.cs", "StoryWorldPredicates") or FALLBACK_PREDICATES)
        # Event keys are the constants with a ':' (the others are argument words such as "launch" or "flight").
        self.events = {e for e in csharp_constants(shared / "GameEventKeys.cs", "GameEventKeys") if ":" in e and not e.endswith(":")}
        self.events |= {
            "crystal:" + e
            for e in csharp_constants(repo / "src" / "BlocksBeyondTheStars.Shared" / "Missions" / "Mission.cs", "CircuitEvents")
        }
        # #2469: the teaching-flag ids a client reports (the GameEventKeys.Ui*Id constants).
        self.ui_events = set(ui_event_ids(shared / "GameEventKeys.cs"))
        self.base_en = self.load_table(data_dir / "locales" / "en.json")
        self.base_de = self.load_table(data_dir / "locales" / "de.json")
        self.missions = {m.get("id") for m in self.load_list(data_dir / "missions.json")}
        self.blueprints = {b.get("key") for b in self.load_list(data_dir / "blueprints.json")}
        self.blocks = {b.get("key") for b in self.load_list(data_dir / "blocks.json")}
        self.packs: dict[str, dict] = {}
        self.folders_by_id: dict[str, list[pathlib.Path]] = {}
        stories = data_dir / "stories"
        if stories.exists():
            for folder in sorted(p for p in stories.iterdir() if p.is_dir()):
                story = folder / "story.json"
                if story.exists():
                    try:
                        manifest = load_jsonc(story)
                    except (json.JSONDecodeError, OSError):
                        continue
                    if isinstance(manifest, dict) and manifest.get("id"):
                        self.packs.setdefault(manifest["id"], manifest)
                        self.folders_by_id.setdefault(manifest["id"], []).append(folder.resolve())

    @staticmethod
    def load_table(path: pathlib.Path) -> dict:
        return load_jsonc(path) if path.exists() else {}

    @staticmethod
    def load_list(path: pathlib.Path) -> list:
        if not path.exists():
            return []
        value = load_jsonc(path)
        return value if isinstance(value, list) else []

    def unknown_fields(self, pack_id: str, path: str, obj, level: str) -> None:
        if not isinstance(obj, dict):
            if obj is not None:
                self.report.error(pack_id, path, f"expected an object, found {type(obj).__name__}")
            return
        known = {k.lower() for k in KNOWN_FIELDS[level]}
        for key in obj:
            if key.lower() not in known:
                self.report.warn(pack_id, f"{path}.{key}" if path else key, "unknown field (a typo?)")

    def list_of_strings(self, pack_id: str, path: str, value) -> list[str]:
        if value is None:
            return []
        if not isinstance(value, list) or not all(isinstance(v, str) for v in value):
            self.report.error(pack_id, path, "expected a list of condition strings")
            return []
        return value

    def validate(self, pack_dir: pathlib.Path) -> None:
        r = self.report
        story_file = pack_dir / "story.json"
        label = pack_dir.name
        if not story_file.exists():
            r.error(label, "story.json", "not found")
            return
        try:
            pack = load_jsonc(story_file)
        except json.JSONDecodeError as ex:
            r.error(label, "story.json", f"not valid JSON: {ex}")
            return
        if not isinstance(pack, dict):
            r.error(label, "story.json", "the manifest must be a JSON object")
            return
        pid = pack.get("id") or ""
        label = pid or label
        if not pid:
            r.error(label, "id", "the pack has no id")
            return
        if not is_key_safe(pid):
            r.error(label, "id", f"'{pid}' may only use letters, digits, '_' and '-'")
        if pid.lower() in RESERVED_IDS:
            r.error(label, "id", f"'{pid}' is a reserved pack id ({', '.join(sorted(RESERVED_IDS))})")
        same = [p for p in self.folders_by_id.get(pid, []) if p != pack_dir.resolve()]
        if same:
            r.error(label, "id", f"the id is used by another folder too: {', '.join(p.name for p in same)}")

        self.unknown_fields(pid, "", pack, "pack")
        for level, field in (("narrator", "narrator"), ("escalate", "escalate"), ("start", "start"), ("finale", "finale"),
                             ("graduation", "graduation")):
            if field in pack:
                self.unknown_fields(pid, field, pack[field], level)

        kind = pack.get("kind", "story")
        scope = pack.get("scope", "")
        if kind not in KINDS:
            r.error(pid, "kind", f"unknown kind '{kind}' (story, tutorial, side)")
        if scope not in SCOPES:
            r.error(pid, "scope", f"unknown scope '{scope}' (world, player)")
        tutorial = kind == "tutorial"
        if tutorial and scope == "world":
            r.error(pid, "scope", "a tutorial is always player-scoped")
        beats = pack.get("beats") or []
        effective = "player" if tutorial or scope == "player" else "world" if scope == "world" else ("world" if beats else "player")
        player_pack = effective == "player"
        if player_pack and beats:
            r.error(pid, "beats", "a player pack carries no beats - it can never be the world story")
        if not player_pack and not beats:
            r.error(pid, "beats", "a world pack needs beats to be the world story")

        keys: list[tuple[str, str, int, int]] = []

        def key(path: str, value, budget: tuple[int, int] = (0, 0)) -> None:
            if isinstance(value, str) and value:
                keys.append((path, value, budget[0], budget[1]))

        last = None
        for i, beat in enumerate(beats):
            self.unknown_fields(pid, f"beats[{i}]", beat, "beat")
            if beat.get("index") != i:
                r.error(pid, f"beats[{i}]", f"index {beat.get('index')} is out of order (expected {i})")
            threshold = beat.get("threshold", 0)
            if last is not None and threshold < last:
                r.error(pid, f"beats[{i}]", f"threshold {threshold} is below the previous {last}")
            last = threshold
            if not beat.get("textKey"):
                r.error(pid, f"beats[{i}]", "the beat has no textKey")
            key(f"beats[{i}].textKey", beat.get("textKey"))

        cutscene_defs = self.load_cutscenes(pack_dir, pid)
        cutscenes = {c.get("id") for c in cutscene_defs if isinstance(c, dict) and c.get("id")}

        def cutscene(path: str, value) -> None:
            if not value:
                return
            if str(value).startswith("__"):
                r.error(pid, path, f"'{value}' is built in - the server plays it, a pack never binds it")
            elif "@" in str(value) or value not in cutscenes:
                r.error(pid, path, f"cutscene '{value}' is not defined in the pack's cutscenes.json")

        def condition(path: str, text) -> None:
            if not isinstance(text, str) or not text.strip():
                r.error(pid, path, "an empty condition")
                return
            try:
                atoms = ConditionParser(text, self.predicates).parse()
            except ConditionError as ex:
                r.error(pid, path, str(ex))
                return
            for form, atom_pack, ident in atoms:
                if form == "blueprint":
                    if self.blueprints and ident not in self.blueprints:
                        r.error(pid, path, f"blueprint '{ident}' does not exist")
                    continue
                if form not in ("step", "chapter", "pack"):
                    continue
                target = pack if atom_pack == pid else self.packs.get(atom_pack)
                if target is None:
                    r.error(pid, path, f"pack '{atom_pack}' is not installed")
                    continue
                chapters = target.get("chapters") or []
                if form == "step" and not any(s.get("id") == ident for c in chapters for s in (c.get("steps") or [])):
                    r.error(pid, path, f"pack '{atom_pack}' has no step '{ident}'")
                if form == "chapter" and not any(c.get("id") == ident for c in chapters):
                    r.error(pid, path, f"pack '{atom_pack}' has no chapter '{ident}'")

        def conditions(path: str, value) -> None:
            for i, text in enumerate(self.list_of_strings(pid, path, value)):
                condition(f"{path}[{i}]", text)

        conditions("start.after", (pack.get("start") or {}).get("after"))
        for gate, text in (pack.get("gates") or {}).items():
            condition(f"gates.{gate}", text)
            if gate not in GATES:
                r.warn(pid, f"gates.{gate}", f"unknown gate (known: {', '.join(sorted(GATES))})")
            if not tutorial:
                r.warn(pid, f"gates.{gate}", "only a tutorial pack's gates are read")

        if not pack.get("nameKey"):
            r.error(pid, "nameKey", "the pack has no nameKey")
        key("nameKey", pack.get("nameKey"))
        key("descriptionKey", pack.get("descriptionKey"))
        key("narrator.nameKey", (pack.get("narrator") or {}).get("nameKey"))
        key("epilogueTitleKey", pack.get("epilogueTitleKey"))
        key("epilogueTextKey", pack.get("epilogueTextKey"))
        if pack.get("shortNameKey"):
            key("shortNameKey", pack.get("shortNameKey"), (SHORT_NAME_MAX, SHORT_NAME_MAX))
        elif player_pack and pack.get("chapters"):
            r.warn(pid, "shortNameKey", 'a player pack without a short name shows "Objective:" on the chip')
        for field in ("fragments", "memories", "flavourLines", "loreSites", "npcThreads"):
            for i, entry in enumerate(pack.get(field) or []):
                key(f"{field}[{i}].textKey", entry.get("textKey"))
        lore_file = pack_dir / "lore_sites.json"
        if lore_file.exists():
            for i, entry in enumerate(self.load_list(lore_file)):
                key(f"lore_sites.json[{i}].textKey", entry.get("textKey"))
        for i, arg in enumerate(pack.get("coreArguments") or []):
            key(f"coreArguments[{i}].promptKey", arg.get("promptKey"))
            for j, choice in enumerate(arg.get("choices") or []):
                key(f"coreArguments[{i}].choices[{j}].textKey", choice.get("textKey"))
                key(f"coreArguments[{i}].choices[{j}].responseKey", choice.get("responseKey"))
        for i, dialog in enumerate(pack.get("dialogs") or []):
            for j, node in enumerate(dialog.get("nodes") or []):
                key(f"dialogs[{i}].nodes[{j}].promptKey", node.get("promptKey"))
                for k, choice in enumerate(node.get("choices") or []):
                    key(f"dialogs[{i}].nodes[{j}].choices[{k}].textKey", choice.get("textKey"))
                    key(f"dialogs[{i}].nodes[{j}].choices[{k}].responseKey", choice.get("responseKey"))
        for i, insight in enumerate(pack.get("insights") or []):
            self.unknown_fields(pid, f"insights[{i}]", insight, "insight")
            if not insight.get("event"):
                r.error(pid, f"insights[{i}]", "an insight needs an event")
            key(f"insights[{i}].textKey", insight.get("textKey"))

        cutscene("introCutscene", pack.get("introCutscene"))
        for i, beat in enumerate(beats):
            cutscene(f"beats[{i}].cutscene", beat.get("cutscene"))
        for stage, value in ((pack.get("finale") or {}).get("cutscenes") or {}).items():
            cutscene(f"finale.cutscenes.{stage}", value)

        chapter_ids: set[str] = set()
        step_ids: set[str] = set()
        legacy_keys: set[str] = set()
        for ci, chapter in enumerate(pack.get("chapters") or []):
            cp = f"chapters[{ci}]"
            self.unknown_fields(pid, cp, chapter, "chapter")
            cid = chapter.get("id") or ""
            if not cid or not is_key_safe(cid, allow_dot=True) or cid in chapter_ids:
                r.error(pid, cp, f"chapter id '{cid}' is empty, not key-safe or used twice")
            chapter_ids.add(cid)
            if not chapter.get("titleKey"):
                r.error(pid, cp, "the chapter has no titleKey")
            key(f"{cp}.titleKey", chapter.get("titleKey"))
            key(f"{cp}.summaryKey", chapter.get("summaryKey"))
            conditions(f"{cp}.requires", chapter.get("requires"))
            conditions(f"{cp}.offerWhen", chapter.get("offerWhen"))
            conditions(f"{cp}.autoCompleteIf", chapter.get("autoCompleteIf"))
            optional = bool(chapter.get("optional"))
            if chapter.get("offerWhen") and not optional:
                r.warn(pid, f"{cp}.offerWhen", "only an optional chapter is offered")
            cutscene(f"{cp}.openCutscene", chapter.get("openCutscene"))
            cutscene(f"{cp}.closeCutscene", chapter.get("closeCutscene"))
            for li, lead in enumerate(chapter.get("leads") or []):
                self.unknown_fields(pid, f"{cp}.leads[{li}]", lead, "lead")
                key(f"{cp}.leads[{li}].objKey", lead.get("objKey"), CHIP)
            steps = chapter.get("steps") or []
            if player_pack and not steps:
                r.warn(pid, cp, "a chapter of a player pack without steps completes at once")
            line_budget = BASIC_LINE if tutorial and not optional else LINE
            for si, step in enumerate(steps):
                sp = f"{cp}.steps[{si}]"
                self.unknown_fields(pid, sp, step, "step")
                sid = step.get("id") or ""
                if not sid or not is_key_safe(sid, allow_dot=True) or sid in step_ids:
                    r.error(pid, sp, f"step id '{sid}' is empty, not key-safe or used twice in the pack")
                step_ids.add(sid)
                skind = str(step.get("kind", "do")).lower()
                if skind not in STEP_KINDS:
                    r.error(pid, f"{sp}.kind", f"unknown kind '{skind}' (do, read, react)")
                if str(step.get("tier", "must")).lower() not in STEP_TIERS:
                    r.error(pid, f"{sp}.tier", f"unknown tier '{step.get('tier')}' (must, core, opt)")
                objective = step.get("objective") or {}
                self.unknown_fields(pid, f"{sp}.objective", objective, "objective")
                otype = str(objective.get("type", ""))
                event = objective.get("event", "")
                if otype.lower() == "mission":
                    target = objective.get("target", "")
                    if not target:
                        r.error(pid, f"{sp}.objective", "a mission bridge needs a target mission")
                    elif self.missions and target not in self.missions:
                        r.error(pid, f"{sp}.objective", f"mission '{target}' does not exist in data/missions.json")
                elif otype:
                    r.error(pid, f"{sp}.objective.type", f"unknown objective type '{otype}' (Mission, or none)")
                elif not event:
                    if skind != "read":
                        r.error(pid, f"{sp}.objective", "a do or react step needs an event or a mission bridge")
                else:
                    self.check_event(pid, f"{sp}.objective.event", event, player_pack)
                if int(objective.get("required", 1)) < 1:
                    r.error(pid, f"{sp}.objective.required", "required must be at least 1")
                legacy = step.get("legacyKey", "")
                if legacy:
                    stage = legacy[len("vega:stage:"):] if legacy.startswith("vega:stage:") else None
                    if not tutorial:
                        r.error(pid, f"{sp}.legacyKey", "only a tutorial pack may name legacy keys")
                    elif stage not in LEGACY_STAGES:
                        r.error(pid, f"{sp}.legacyKey", f"'{legacy}' is not one of the legacy stages (vega:stage:{'|'.join(LEGACY_STAGES)})")
                    elif legacy in legacy_keys:
                        r.error(pid, f"{sp}.legacyKey", f"'{legacy}' is used by two steps")
                    legacy_keys.add(legacy)
                conditions(f"{sp}.requires", step.get("requires"))
                conditions(f"{sp}.feasibleIf", step.get("feasibleIf"))
                conditions(f"{sp}.alreadyDoneIf", step.get("alreadyDoneIf"))
                alternative = step.get("alternative", "")
                if alternative and (alternative == sid or not any(s.get("id") == alternative for s in steps)):
                    r.error(pid, f"{sp}.alternative", f"'{alternative}' is not another step of chapter '{cid}'")
                for hook in ("onStart", "onComplete"):
                    if hook in step:
                        self.unknown_fields(pid, f"{sp}.{hook}", step[hook], "hook")
                        cutscene(f"{sp}.{hook}.cutscene", (step[hook] or {}).get("cutscene"))
                if "escalate" in step:
                    self.unknown_fields(pid, f"{sp}.escalate", step["escalate"], "escalate")
                reveal = (step.get("onStart") or {}).get("reveal", "")
                if reveal:
                    if reveal in ("nearest_settlement", "nearest_food_plant"):
                        pass
                    elif reveal.startswith("nearest_ore:") and len(reveal) > len("nearest_ore:"):
                        block = reveal[len("nearest_ore:"):]
                        if self.blocks and block not in self.blocks:
                            r.error(pid, f"{sp}.onStart.reveal", f"block '{block}' does not exist")
                    else:
                        r.error(pid, f"{sp}.onStart.reveal", f"unknown marker provider '{reveal}' (nearest_ore:<block>, nearest_settlement, nearest_food_plant)")
                for hi, ref in enumerate(self.list_of_strings(pid, f"{sp}.hintRefs", step.get("hintRefs"))):
                    if not ref.strip():
                        r.error(pid, f"{sp}.hintRefs[{hi}]", "an empty hint id")
                explain = step.get("explain") or {}
                self.unknown_fields(pid, f"{sp}.explain", explain, "explain")
                key(f"{sp}.explain.startKey", explain.get("startKey"), line_budget)
                key(f"{sp}.explain.hintKey", explain.get("hintKey"), line_budget)
                key(f"{sp}.explain.doneKey", explain.get("doneKey"), line_budget)
                key(f"{sp}.explain.chipKey", explain.get("chipKey"), CHIP)
                variants = step.get("variants") or []
                if skind == "do" and not explain.get("chipKey") and not any(v.get("chipKey") for v in variants):
                    r.warn(pid, f"{sp}.explain.chipKey", "a do step without a chip text leaves the chip to the next source")
                for vi, variant in enumerate(variants):
                    vp = f"{sp}.variants[{vi}]"
                    self.unknown_fields(pid, vp, variant, "variant")
                    condition(f"{vp}.when", variant.get("when"))
                    key(f"{vp}.startKey", variant.get("startKey"), line_budget)
                    key(f"{vp}.hintKey", variant.get("hintKey"), line_budget)
                    key(f"{vp}.doneKey", variant.get("doneKey"), line_budget)
                    key(f"{vp}.chipKey", variant.get("chipKey"), CHIP)

        self.validate_cutscenes(pid, cutscene_defs, chapter_ids, key)

        # #2469: the graduation (a player pack finished by playing).
        graduation = pack.get("graduation") or {}
        if graduation:
            if not player_pack:
                r.warn(pid, "graduation", "only a player pack graduates anybody")
            key("graduation.lineKey", graduation.get("lineKey"), BASIC_LINE if tutorial else LINE)
            counter = graduation.get("counter", "")
            if counter and (":" not in counter or not is_key_safe(counter.replace(":", "_"))):
                r.error(pid, "graduation.counter", f"'{counter}' is not an achievement counter ('<verb>:<thing>')")
            suggest = self.list_of_strings(pid, "graduation.suggest", graduation.get("suggest"))
            if len(suggest) > 3:
                r.error(pid, "graduation.suggest", f"at most 3 suggested chapters ({len(suggest)} named)")
            optional_ids = {c.get("id") for c in (pack.get("chapters") or []) if c.get("optional")}
            for gi, chapter_id in enumerate(suggest):
                if chapter_id not in optional_ids:
                    r.error(pid, f"graduation.suggest[{gi}]", f"'{chapter_id}' is not an optional chapter of this pack")

        # Every key in English and German (the pack's own table over the base table), within its budget.
        pack_en = self.load_table(pack_dir / "locales" / "en.json")
        pack_de = self.load_table(pack_dir / "locales" / "de.json")
        if not (pack_dir / "locales" / "en.json").exists() and any(k for _, k, _, _ in keys if k not in self.base_en):
            r.error(pid, "locales/en.json", "missing (English is mandatory)")
        if not (pack_dir / "locales" / "de.json").exists() and any(k for _, k, _, _ in keys if k not in self.base_de):
            r.error(pid, "locales/de.json", "missing (German is mandatory)")
        for path, k, max_en, max_de in keys:
            text_en = pack_en.get(k, self.base_en.get(k))
            text_de = pack_de.get(k, self.base_de.get(k))
            if text_en is None:
                r.error(pid, path, f"key '{k}' is missing in English")
            if text_de is None:
                r.error(pid, path, f"key '{k}' is missing in German")
            if max_en and text_en is not None and shown_length(text_en) > max_en:
                r.error(pid, path, f"'{k}' is {shown_length(text_en)} characters in English (budget {max_en})")
            if max_de and text_de is not None and shown_length(text_de) > max_de:
                r.error(pid, path, f"'{k}' is {shown_length(text_de)} characters in German (budget {max_de})")

        if tutorial:
            self.find_rewards(pid, pack, "$")

    def check_event(self, pid: str, path: str, event: str, player_pack: bool) -> None:
        if event.startswith("ui:"):
            if not player_pack:
                self.report.error(pid, path, "'ui:' teaching flags exist only in a player pack's step objectives")
            elif not is_key_safe(event[3:]):
                self.report.error(pid, path, f"'{event}' is not a valid teaching flag (ui:<id>)")
            elif self.ui_events and event[3:] not in self.ui_events:
                self.report.warn(pid, path, f"'{event}' is no flag a client reports ({', '.join(sorted(self.ui_events))})")
            return
        star = event.find("*")
        if star >= 0 and star != len(event) - 1:
            self.report.error(pid, path, f"'{event}': a wildcard may only end the event ('craft:*')")
            return
        if not self.events:
            return
        known = any(k.startswith(event[:-1]) for k in self.events) if star >= 0 else event in self.events
        if not known:
            self.report.warn(pid, path, f"event '{event}' is not raised by the server yet (GameEventKeys)")

    def find_rewards(self, pid: str, value, path: str) -> None:
        if isinstance(value, dict):
            for k, v in value.items():
                child = f"{path}.{k}"
                if "reward" in k.lower() and v not in (None, False, 0, "", [], {}):
                    self.report.error(pid, child, "a tutorial pays no rewards")
                self.find_rewards(pid, v, child)
        elif isinstance(value, list):
            for i, v in enumerate(value):
                self.find_rewards(pid, v, f"{path}[{i}]")
        elif isinstance(value, str) and path.endswith(".consequence") and value.startswith("gift:"):
            self.report.error(pid, path, "a tutorial pays no rewards (gift consequence)")

    def load_cutscenes(self, pack_dir: pathlib.Path, pid: str | None = None) -> list:
        """The cutscene definitions of the pack's cutscenes.json ({"cutscenes": [...]} or a bare list); [] without a file.
        With a pack id the file's top-level fields are checked too."""
        path = pack_dir / "cutscenes.json"
        if not path.exists():
            return []
        try:
            value = load_jsonc(path)
        except json.JSONDecodeError as ex:
            self.report.error(pid or pack_dir.name, "cutscenes.json", f"not valid JSON: {ex}")
            return []
        if isinstance(value, dict):
            if pid:
                self.unknown_fields(pid, "cutscenes.json", value, "cutscene_file")
            value = value.get("cutscenes", [])
        if not isinstance(value, list):
            if pid:
                self.report.error(pid, "cutscenes.json", "expected a list of cutscenes")
            return []
        return value

    def other_pack_cutscene_ids(self, pack_id: str):
        """The cutscene ids of another installed pack, or None when no such pack is installed."""
        folders = self.folders_by_id.get(pack_id)
        if not folders:
            return None
        return {c.get("id") for c in self.load_cutscenes(folders[0]) if isinstance(c, dict) and c.get("id")}

    def validate_cutscenes(self, pid: str, defs: list, chapter_ids: set[str], key) -> None:
        """The cutscene rules of the C# StoryPackValidator (synthesis 6.2/6.3): ids, tiers, cameras, anchors, caption styles,
        grades, shot lengths, caption/cue times, caption keys and budgets, the tier rules (a T1/T2 never takes the camera;
        only a T3/T4 that takes it may hold the world or freeze the controls), chains, chapters, music and sounds."""
        r = self.report
        repo = self.data.parent
        music_dir = repo / "client" / "Music"
        audio_dir = repo / "client" / "Assets" / "Resources" / "audio"
        ids = {d.get("id") for d in defs if isinstance(d, dict)}
        seen: set[str] = set()
        for ci, cut in enumerate(defs):
            cp = f"cutscenes[{ci}]"
            if not isinstance(cut, dict):
                r.error(pid, cp, "expected an object")
                continue
            self.unknown_fields(pid, cp, cut, "cutscene")
            cid = cut.get("id") or ""
            if not cid or not is_key_safe(cid, allow_dot=True) or cid in seen:
                r.error(pid, cp, f"cutscene id '{cid}' is empty, not key-safe or used twice")
            elif cid.startswith("__"):
                r.error(pid, cp, f"'{cid}': ids starting with '__' are built in")
            seen.add(cid)
            tier = cut.get("tier", 2)
            if tier not in CUTSCENE_TIERS:
                r.error(pid, f"{cp}.tier", f"unknown tier {tier} (1 banner, 2 card, 3 scene, 4 set piece)")
                tier = 2
            chapter = cut.get("chapter") or ""
            if chapter and chapter not in chapter_ids:
                r.error(pid, f"{cp}.chapter", f"the pack has no chapter '{chapter}'")
            for i, line in enumerate(cut.get("replaceLineKeys") or []):
                key(f"{cp}.replaceLineKeys[{i}]", line)
            music = cut.get("music") or ""
            if music and music_dir.exists() and not (music_dir / f"{music}.mp3").exists():
                r.error(pid, f"{cp}.music", f"music track '{music}' does not exist in client/Music")
            shots = cut.get("shots") or []
            if not shots:
                r.error(pid, f"{cp}.shots", "a cutscene needs at least one shot")
            takeover = False
            for si, shot in enumerate(shots):
                sp = f"{cp}.shots[{si}]"
                if not isinstance(shot, dict):
                    r.error(pid, sp, "expected an object")
                    continue
                self.unknown_fields(pid, sp, shot, "shot")
                camera = shot.get("camera") or {}
                self.unknown_fields(pid, f"{sp}.camera", camera, "camera")
                ctype = camera.get("type", "player_view") if isinstance(camera, dict) else ""
                anchor = camera.get("anchor", "") if isinstance(camera, dict) else ""
                if ctype not in CAMERAS:
                    r.error(pid, f"{sp}.camera.type", f"unknown camera '{ctype}' ({', '.join(CAMERAS)})")
                elif ctype not in NON_TAKEOVER_CAMERAS:
                    takeover = True
                    if tier < 3:
                        kind = "banner" if tier == 1 else "card"
                        r.error(pid, f"{sp}.camera.type", f"a T{tier} {kind} never takes the camera (player_view or overlay)")
                if anchor and anchor not in ANCHORS:
                    r.error(pid, f"{sp}.camera.anchor", f"unknown anchor '{anchor}' ({', '.join(ANCHORS)})")
                duration = shot.get("duration", 0)
                if not isinstance(duration, (int, float)) or not 0 < duration <= SHOT_MAX_SECONDS:
                    r.error(pid, f"{sp}.duration", f"a shot lasts more than 0 and at most {SHOT_MAX_SECONDS} seconds (found {duration})")
                    duration = 0
                grade = shot.get("grade", "none")
                if grade not in GRADES:
                    r.error(pid, f"{sp}.grade", f"unknown grade '{grade}' ({', '.join(GRADES)})")
                for xi, sfx in enumerate(shot.get("sfx") or []):
                    xp = f"{sp}.sfx[{xi}]"
                    self.unknown_fields(pid, xp, sfx, "sfx")
                    cue = (sfx or {}).get("cue", "") if isinstance(sfx, dict) else ""
                    if not cue:
                        r.error(pid, xp, "a sound cue needs a clip name")
                    elif audio_dir.exists() and not any(p.suffix != ".meta" for p in audio_dir.glob(f"{cue}.*")):
                        r.error(pid, xp, f"sound '{cue}' does not exist in client/Assets/Resources/audio")
                    at = (sfx or {}).get("at", 0) if isinstance(sfx, dict) else 0
                    if at < 0 or (duration > 0 and at >= duration):
                        r.error(pid, f"{xp}.at", f"the cue at {at} s lies outside the {duration} s shot")
                for ki, caption in enumerate(shot.get("captions") or []):
                    kp = f"{sp}.captions[{ki}]"
                    self.unknown_fields(pid, kp, caption, "caption")
                    if not isinstance(caption, dict):
                        continue
                    style = caption.get("style", "subtitle")
                    if style not in CAPTION_STYLES:
                        r.error(pid, f"{kp}.style", f"unknown caption style '{style}' ({', '.join(CAPTION_STYLES)})")
                    if not caption.get("key"):
                        r.error(pid, kp, "a caption needs a key")
                    budget = {"title_card": TITLE_CARD, "reading_frame": READING_FRAME, "credits": (0, 0)}.get(style, CAPTION_LINE)
                    key(f"{kp}.key", caption.get("key"), budget)
                    if caption.get("kickerKey"):
                        if style != "title_card":
                            r.warn(pid, f"{kp}.kickerKey", "only a title_card shows a kicker")
                        key(f"{kp}.kickerKey", caption.get("kickerKey"), TITLE_CARD)
                    if caption.get("speaker") and style != "speaker":
                        r.warn(pid, f"{kp}.speaker", "only a speaker caption names a speaker")
                    at = caption.get("at", 0)
                    if at < 0 or (duration > 0 and at >= duration):
                        r.error(pid, f"{kp}.at", f"the caption at {at} s lies outside the {duration} s shot")
                    if caption.get("hold", 0) < 0:
                        r.error(pid, f"{kp}.hold", "hold must not be negative (0 = until the shot ends)")
            options = cut.get("options") or {}
            self.unknown_fields(pid, f"{cp}.options", options, "cutscene_options")
            if not isinstance(options, dict):
                continue
            camera_taken = tier >= 3 and takeover
            if options.get("holdWorld") is True and not camera_taken:
                r.error(pid, f"{cp}.options.holdWorld", "only a T3/T4 scene that takes the camera may hold the world")
            if options.get("lockInput") is True and not camera_taken:
                r.error(pid, f"{cp}.options.lockInput", "only a T3/T4 scene that takes the camera may freeze the controls")
            if options.get("minDisplay", 0) < 0:
                r.error(pid, f"{cp}.options.minDisplay", "minDisplay must not be negative")
            for ni, ref in enumerate(options.get("chainNext") or []):
                self.check_chain(pid, f"{cp}.options.chainNext[{ni}]", cid, ref, ids)

    def check_chain(self, pid: str, path: str, from_id: str, ref, own_ids: set) -> None:
        """A chainNext entry: __recap, an id of the same pack, or <pack>/<id> (a warning when that pack is not installed)."""
        r = self.report
        if not ref or not isinstance(ref, str):
            r.error(pid, path, "an empty chain reference")
            return
        if ref == RECAP_ID:
            return
        if "/" not in ref:
            if ref == from_id:
                r.error(pid, path, "a cutscene cannot chain to itself")
            elif "@" in ref or ref not in own_ids:
                r.error(pid, path, f"cutscene '{ref}' is not defined in the pack")
            return
        pack, cid = ref.split("/", 1)
        if pack == pid:
            self.check_chain(pid, path, from_id, cid, own_ids)
            return
        other = self.other_pack_cutscene_ids(pack)
        if other is None:
            r.warn(pid, path, f"pack '{pack}' is not installed - the chain applies only when it is")
        elif cid != RECAP_ID and cid not in other:
            r.error(pid, path, f"pack '{pack}' has no cutscene '{cid}'")


def find_data_dir(pack_dir: pathlib.Path) -> pathlib.Path:
    if pack_dir.parent.name == "stories":
        return pack_dir.parent.parent
    return pathlib.Path(__file__).resolve().parent.parent / "data"


def main(argv: list[str]) -> int:
    strict = "--strict" in argv
    args = [a for a in argv if not a.startswith("--")]
    if args:
        pack_dirs = [pathlib.Path(a).resolve() for a in args]
    else:
        stories = pathlib.Path(__file__).resolve().parent.parent / "data" / "stories"
        pack_dirs = sorted(p for p in stories.iterdir() if p.is_dir() and (p / "story.json").exists())
    if not pack_dirs:
        print("No story packs found.")
        return 1

    report = Report()
    validators: dict[pathlib.Path, Validator] = {}
    for pack_dir in pack_dirs:
        data_dir = find_data_dir(pack_dir)
        validator = validators.setdefault(data_dir, Validator(data_dir, report))
        validator.validate(pack_dir)

    for line in report.errors + report.warnings:
        print(line)
    failed = bool(report.errors) or (strict and bool(report.warnings))
    names = ", ".join(p.name for p in pack_dirs)
    print(f"{'FAILED' if failed else 'OK'}: {len(pack_dirs)} pack(s) ({names}) - "
          f"{len(report.errors)} error(s), {len(report.warnings)} warning(s). No file was changed.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
