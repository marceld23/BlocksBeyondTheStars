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
pack has beats), every condition parses and names existing packs/chapters/steps/blueprints/lore texts, mission-bridge
targets exist in data/missions.json, event keys are published (warning), 'ui:' only in a player pack's step objectives,
marker providers, legacy keys (tutorial only), alternatives, a world chapter's story missions (#2476: ids that are no
chapter's, a title, feasibility, an alternative of the same chapter, do steps with a chip text without a {0}, lead
bridges naming a lead a player can follow), a tutorial pays no rewards, every locale key exists in
English and German (pack table or base table), every cutscene binding resolves in the pack's cutscenes.json (#2467),
every cutscene follows the rules (ids, tier 1-4, cameras, anchors (a diorama shows core or star_map, #2477), caption
styles, grades, shot lengths, a T1/T2 never
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
CHAPTER_EVENTS = {"guardian_revealed", "guardian_defeated"}  # StoryChapterEvents (#2470)
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
CAMERAS = [
    "player_view", "overlay", "static", "orbit", "crane", "sky", "chart",
    "dolly", "pan", "flyover", "space_vista", "diorama",  # #2477: moves and synthetic stages
]
NON_TAKEOVER_CAMERAS = {"player_view", "overlay"}
ANCHORS = ["player", "ship", "planet", "station", "star_map", "heal_tank", "core", "custom"]
DIORAMA_ANCHORS = ["core", "star_map"]  # #2477: what a diorama can show
CAPTION_STYLES = ["subtitle", "title_card", "speaker", "reading_frame", "credits"]
GRADES = ["none", "memory", "flashback"]
# #2475: the chapter-lead vocabulary v1 (StoryLeadTypes) - the words, the ones that need an argument, the unique sites.
LEAD_TYPES = [
    "fragment", "unique_site", "monument", "settlement_help", "machines", "heal_tank", "research", "module", "finale",
    "finale_jump", "relay", "explore",
]
LEAD_WITH_ARGUMENT = {"unique_site", "research", "module"}
LEAD_UNIQUE_SITES = ["observatory", "shrine", "derelict"]
LEAD_OBJ_PREFIX = "story.vega.obj."
LEAD_BUILT_IN_KEYS = [
    LEAD_OBJ_PREFIX + k
    for k in ("fragment", "fragment_here", "settlement_help", "explore", "explore_peaceful", "finale_jump")
]
MEMORY_TRIGGERS = ["lead", "monument"]
ARG_SOURCES = ["body", "system"]  # #2473: what fills {0} besides the bound chapter (CutsceneArgSources)
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
    "fragments_left", "observatory_reachable", "derelict_reachable", "settlements", "vaults", "space_wrecks",
]

KNOWN_FIELDS = {
    "pack": {
        "id", "nameKey", "kind", "scope", "priority", "shortNameKey", "descriptionKey", "narrator", "defaultEnabled",
        "introCutscene", "gates", "escalate", "start", "chapters", "insights", "finale", "fragmentWeight",
        "killWeight", "milestoneWeight", "killContributionCap", "beats", "fragments", "memories", "coreArguments",
        "flavourLines", "missionThreads", "loreSites", "npcThreads", "characters", "dialogs", "epilogueTitle",
        "epilogueTitleKey", "epilogueTextKey", "graduation", "leadBonusMilestones", "normaliseUnavailableDrivers",
        "memoryTriggers",
    },
    "graduation": {"lineKey", "counter", "suggest", "cutscene"},
    "narrator": {"id", "nameKey", "icon", "color", "voice", "lineStart"},
    "escalate": {"hintAfter", "markerAfter", "offerSkipAfter"},
    "start": {"after", "earlyOptIn"},
    "chapter": {
        "id", "titleKey", "summaryKey", "optional", "requires", "offerWhen", "autoCompleteIf", "autoStart",
        "openCutscene", "closeCutscene", "fromBeat", "fromEvent", "leads", "steps", "skippable", "missions",
    },
    "mission": {"id", "titleKey", "summaryKey", "feasibleIf", "alternative", "skippable", "steps"},
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
    "cutscene": {"id", "tier", "chapter", "template", "replaceLineKeys", "music", "arg", "shots", "options"},
    "shot": {"camera", "duration", "captions", "grade", "sfx"},
    "camera": {"type", "anchor"},
    "caption": {"key", "style", "kickerKey", "speaker", "at", "hold"},
    "sfx": {"cue", "at"},
    "cutscene_options": {
        "holdWorld", "hideHud", "lockInput", "skippable", "minDisplay", "chainNext", "cooldownExempt", "replayable",
        "returnOnSkip",
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


def lead_problem(lead_type) -> str | None:
    """What is wrong with a chapter lead's type, or None (StoryLeadTypes.Problem)."""
    text = lead_type if isinstance(lead_type, str) else ""
    kind, colon, arg = text.partition(":")
    if not kind:
        return "the lead has no type"
    if kind not in LEAD_TYPES:
        return f"unknown lead type '{kind}' ({', '.join(LEAD_TYPES)})"
    if kind in LEAD_WITH_ARGUMENT and not arg:
        return f"the lead type '{kind}' needs an argument ('{kind}:<id>')"
    if kind not in LEAD_WITH_ARGUMENT and colon:
        return f"the lead type '{kind}' takes no argument"
    if kind == "unique_site" and arg not in LEAD_UNIQUE_SITES:
        return f"unknown unique site '{arg}' ({', '.join(LEAD_UNIQUE_SITES)})"
    return None


def lead_default_key(kind: str, arg: str) -> str:
    """The built-in chip text key of a lead without its own objKey (StoryLeads.ObjKey)."""
    if kind == "unique_site":
        return f"{LEAD_OBJ_PREFIX}site.{arg}"
    if kind == "finale_jump":
        return f"{LEAD_OBJ_PREFIX}finale_jump"
    return LEAD_OBJ_PREFIX + kind


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
        if word == "lore":
            key = self.read_id()
            if not key:
                self.fail("the lore key is missing")
            atoms.append(("lore", "", key))
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
        self.fail(f"unknown condition '{word}:' (step, chapter, pack, blueprint, world, lore, all, any, not)")


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
        self.modules = {m.get("key") for m in self.load_list(data_dir / "ship_modules.json")}
        self.blocks = {b.get("key") for b in self.load_list(data_dir / "blocks.json")}
        self.packs: dict[str, dict] = {}
        self.folders_by_id: dict[str, list[pathlib.Path]] = {}
        self.lore_keys: set[str] = set()  # #2476: every pack's lore_sites.json keys (lore: conditions)
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
                lore = folder / "lore_sites.json"
                if lore.exists():
                    try:
                        self.lore_keys |= {e.get("key") for e in self.load_list(lore) if isinstance(e, dict) and e.get("key")}
                    except (json.JSONDecodeError, OSError):
                        pass

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

        # #2475: the arc's pacing extras - the lead bonus, the peaceful-world normalisation, the memory triggers.
        bonus = pack.get("leadBonusMilestones", 0)
        if not isinstance(bonus, int) or isinstance(bonus, bool) or bonus < 0 or bonus > 10:
            r.error(pid, "leadBonusMilestones", f"{bonus} is out of range (0-10 milestones per chapter)")
        for ti, trigger in enumerate(pack.get("memoryTriggers") or []):
            if trigger not in MEMORY_TRIGGERS:
                r.error(pid, f"memoryTriggers[{ti}]", f"unknown memory trigger '{trigger}' ({', '.join(MEMORY_TRIGGERS)})")
        if player_pack and (bonus or pack.get("normaliseUnavailableDrivers") or pack.get("memoryTriggers")):
            r.warn(pid, "leadBonusMilestones",
                   "leadBonusMilestones, normaliseUnavailableDrivers and memoryTriggers are read for world packs only")
        if not player_pack and any((c or {}).get("leads") for c in (pack.get("chapters") or []) if isinstance(c, dict)):
            for built_in in LEAD_BUILT_IN_KEYS:
                key("chapters.leads", built_in, CHIP)

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
                if form == "lore":
                    if self.lore_keys and ident not in self.lore_keys:
                        r.error(pid, path, f"lore text '{ident}' does not exist in any lore_sites.json")
                    continue
                if form not in ("step", "chapter", "pack"):
                    continue
                target = pack if atom_pack == pid else self.packs.get(atom_pack)
                if target is None:
                    r.error(pid, path, f"pack '{atom_pack}' is not installed")
                    continue
                chapters = [c for c in (target.get("chapters") or []) if isinstance(c, dict)]
                missions = [m for c in chapters for m in (c.get("missions") or []) if isinstance(m, dict)]
                all_steps = [s for c in chapters for s in (c.get("steps") or [])] + [s for m in missions for s in (m.get("steps") or [])]
                if form == "step" and not any(s.get("id") == ident for s in all_steps if isinstance(s, dict)):
                    r.error(pid, path, f"pack '{atom_pack}' has no step '{ident}'")
                if form == "chapter" and not any(c.get("id") == ident for c in chapters + missions):
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
        mission_ids: set[str] = set()
        all_chapter_ids = {c.get("id") for c in (pack.get("chapters") or []) if isinstance(c, dict)}

        def check_steps(owner_path: str, owner_word: str, owner_id: str, steps: list, line_budget: tuple[int, int], mission: bool) -> None:
            """One chapter's steps, or (#2476) one story mission's (do steps with a chip text; lead bridges allowed)."""
            for si, step in enumerate(steps):
                sp = f"{owner_path}.steps[{si}]"
                self.unknown_fields(pid, sp, step, "step")
                sid = step.get("id") or ""
                if not sid or not is_key_safe(sid, allow_dot=True) or sid in step_ids:
                    r.error(pid, sp, f"step id '{sid}' is empty, not key-safe or used twice in the pack")
                step_ids.add(sid)
                skind = str(step.get("kind", "do")).lower()
                if skind not in STEP_KINDS:
                    r.error(pid, f"{sp}.kind", f"unknown kind '{skind}' (do, read, react)")
                elif mission and skind != "do":
                    r.error(pid, f"{sp}.kind", "a mission's steps are tasks: kind 'do'")
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
                elif otype.lower() == "lead":
                    # #2476: a lead bridge - a world story's mission step that is followed like the chapter lead it names.
                    target = objective.get("target", "")
                    problem = lead_problem(target)
                    lead_kind, _, lead_arg = str(target).partition(":")
                    if not mission:
                        r.error(pid, f"{sp}.objective.type", "a lead bridge is read in a world story's missions only")
                    elif problem:
                        r.error(pid, f"{sp}.objective.target", problem)
                    elif lead_kind in ("explore", "finale", "finale_jump"):
                        r.error(pid, f"{sp}.objective.target", f"'{target}' cannot be a mission step (explore is never done; the finale is chapter 7's own set piece)")
                    elif lead_kind == "research" and self.blueprints and lead_arg not in self.blueprints:
                        r.error(pid, f"{sp}.objective.target", f"blueprint '{lead_arg}' does not exist")
                    elif lead_kind == "module" and self.modules and lead_arg not in self.modules:
                        r.error(pid, f"{sp}.objective.target", f"ship module '{lead_arg}' does not exist")
                elif otype:
                    r.error(pid, f"{sp}.objective.type", f"unknown objective type '{otype}' (Mission, Lead, or none)")
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
                    r.error(pid, f"{sp}.alternative", f"'{alternative}' is not another step of {owner_word} '{owner_id}'")
                for hook in ("onStart", "onComplete"):
                    if hook in step:
                        self.unknown_fields(pid, f"{sp}.{hook}", step[hook], "hook")
                        cutscene(f"{sp}.{hook}.cutscene", (step[hook] or {}).get("cutscene"))
                if "escalate" in step:
                    self.unknown_fields(pid, f"{sp}.escalate", step["escalate"], "escalate")
                reveal = (step.get("onStart") or {}).get("reveal", "")
                if reveal:
                    if reveal in ("nearest_settlement", "nearest_food_plant", "nearest_station") or (
                        reveal.startswith("unique_site:") and reveal[len("unique_site:"):] in LEAD_UNIQUE_SITES
                    ):
                        pass
                    elif reveal.startswith("nearest_ore:") and len(reveal) > len("nearest_ore:"):
                        block = reveal[len("nearest_ore:"):]
                        if self.blocks and block not in self.blocks:
                            r.error(pid, f"{sp}.onStart.reveal", f"block '{block}' does not exist")
                    else:
                        r.error(pid, f"{sp}.onStart.reveal", f"unknown marker provider '{reveal}' (nearest_ore:<block>, nearest_settlement, nearest_food_plant, nearest_station, unique_site:<site>)")
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
                chip_en = self.chip_text_en(pack_dir, explain.get("chipKey"))
                if mission and not explain.get("chipKey"):
                    r.error(pid, f"{sp}.explain.chipKey", "a mission step needs a chip text - it is the step's line in the Journal")
                elif mission and chip_en is not None and re.search(r"\{\d+\}", chip_en):
                    r.error(pid, f"{sp}.explain.chipKey", f"'{explain.get('chipKey')}' needs a {{0}} - a mission step's own chip text is shown without one")
                elif skind == "do" and not explain.get("chipKey") and not any(v.get("chipKey") for v in variants):
                    r.warn(pid, f"{sp}.explain.chipKey", "a do step without a chip text leaves the chip to the next source")
                for vi, variant in enumerate(variants):
                    vp = f"{sp}.variants[{vi}]"
                    self.unknown_fields(pid, vp, variant, "variant")
                    condition(f"{vp}.when", variant.get("when"))
                    key(f"{vp}.startKey", variant.get("startKey"), line_budget)
                    key(f"{vp}.hintKey", variant.get("hintKey"), line_budget)
                    key(f"{vp}.doneKey", variant.get("doneKey"), line_budget)
                    key(f"{vp}.chipKey", variant.get("chipKey"), CHIP)

        def check_missions(cp: str, chapter: dict) -> None:
            """#2476: a world chapter's story missions - ids that are no chapter's, a title, feasibility, an alternative of
            the same chapter, and steps by the mission rules."""
            missions = chapter.get("missions") or []
            for mi, mission in enumerate(missions):
                mp = f"{cp}.missions[{mi}]"
                self.unknown_fields(pid, mp, mission, "mission")
                if not isinstance(mission, dict):
                    continue
                if player_pack:
                    r.warn(pid, mp, "story missions are read for world packs only")
                mid = mission.get("id") or ""
                if not mid or not is_key_safe(mid, allow_dot=True) or mid in all_chapter_ids or mid in mission_ids:
                    r.error(pid, mp, f"mission id '{mid}' is empty, not key-safe, used twice or a chapter's id")
                mission_ids.add(mid)
                if not mission.get("titleKey"):
                    r.error(pid, mp, "the mission has no titleKey")
                key(f"{mp}.titleKey", mission.get("titleKey"))
                key(f"{mp}.summaryKey", mission.get("summaryKey"))
                conditions(f"{mp}.feasibleIf", mission.get("feasibleIf"))
                alternative = mission.get("alternative", "")
                if alternative and (alternative == mid or not any(isinstance(o, dict) and o.get("id") == alternative for o in missions)):
                    r.error(pid, f"{mp}.alternative", f"'{alternative}' is not another mission of chapter '{chapter.get('id')}'")
                steps = mission.get("steps") or []
                if not steps:
                    r.error(pid, f"{mp}.steps", "a mission needs at least one step")
                check_steps(mp, "mission", mid, steps, LINE, True)

        last_from_beat = -1
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
            from_beat = chapter.get("fromBeat", -1)
            from_event = chapter.get("fromEvent") or ""
            if not player_pack:
                # #2470: a world pack's chapters are reached by the arc - a beat or an arc event.
                bad_beat = not isinstance(from_beat, int) or from_beat < -1 or from_beat >= len(beats)
                if bad_beat:
                    r.error(pid, f"{cp}.fromBeat", f"beat {from_beat} does not exist (0-{len(beats) - 1}, or -1 for none)")
                    from_beat = -1
                if from_event and from_event not in CHAPTER_EVENTS:
                    r.error(pid, f"{cp}.fromEvent", f"unknown arc event '{from_event}' ({', '.join(sorted(CHAPTER_EVENTS))})")
                if from_beat < 0 and not from_event and not bad_beat:
                    r.warn(pid, cp, "a world-pack chapter without fromBeat or fromEvent is never reached")
                if 0 <= from_beat < last_from_beat:
                    r.error(pid, f"{cp}.fromBeat", f"beat {from_beat} comes before the previous chapter's beat {last_from_beat} - chapters follow the beats in order")
                last_from_beat = max(last_from_beat, from_beat)
            elif (isinstance(from_beat, int) and from_beat >= 0) or from_event:
                r.warn(pid, cp, "fromBeat / fromEvent are read for world packs only")
            for li, lead in enumerate(chapter.get("leads") or []):
                lp = f"{cp}.leads[{li}]"
                self.unknown_fields(pid, lp, lead, "lead")
                if not isinstance(lead, dict):
                    continue
                problem = lead_problem(lead.get("type"))
                if problem:
                    r.error(pid, f"{lp}.type", problem)
                    continue
                if player_pack:
                    r.warn(pid, lp, "leads are read for world packs only")
                lead_kind, _, lead_arg = str(lead.get("type")).partition(":")
                if lead_kind == "research" and self.blueprints and lead_arg not in self.blueprints:
                    r.error(pid, f"{lp}.type", f"blueprint '{lead_arg}' does not exist")
                if lead_kind == "module" and self.modules and lead_arg not in self.modules:
                    r.error(pid, f"{lp}.type", f"ship module '{lead_arg}' does not exist")
                if lead.get("objKey"):
                    key(f"{lp}.objKey", lead.get("objKey"), CHIP)
                else:
                    key(f"{lp}.type", lead_default_key(lead_kind, lead_arg), CHIP)
            steps = chapter.get("steps") or []
            if player_pack and not steps:
                r.warn(pid, cp, "a chapter of a player pack without steps completes at once")
            line_budget = BASIC_LINE if tutorial and not optional else LINE
            check_steps(cp, "chapter", cid, steps, line_budget, False)
            check_missions(cp, chapter)

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
            cutscene("graduation.cutscene", graduation.get("cutscene"))  # #2473

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

    def chip_text_en(self, pack_dir: pathlib.Path, chip_key) -> str | None:
        """The English text of a chip key (the pack's table over the base table), or None."""
        if not isinstance(chip_key, str) or not chip_key:
            return None
        table = self.load_table(pack_dir / "locales" / "en.json")
        return table.get(chip_key, self.base_en.get(chip_key))

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
            arg_source = cut.get("arg") or ""
            if arg_source and arg_source not in ARG_SOURCES:
                r.error(pid, f"{cp}.arg",
                        f"unknown argument source '{arg_source}' ({', '.join(ARG_SOURCES)}, or none for the bound chapter)")
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
                elif ctype == "diorama" and anchor not in DIORAMA_ANCHORS:
                    r.error(pid, f"{sp}.camera.anchor", f"a diorama shows {' or '.join(DIORAMA_ANCHORS)} (found '{anchor}')")
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
            # #2474: a line given back on a skip must be one the scene takes out of the panel in the first place.
            replaced = cut.get("replaceLineKeys") or []
            for li, line in enumerate(options.get("returnOnSkip") or []):
                if not line or line not in replaced:
                    r.error(pid, f"{cp}.options.returnOnSkip[{li}]", f"'{line}' is not one of the scene's replaceLineKeys")

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
