# Writing a story pack — the authoring guide

Status: implemented (story packs step 22, #2483). Last updated 2026-10-12. Live Done/Open status: [../../TODO.md](../../TODO.md).

A **story pack** is a folder of JSON under `data/stories/<id>/`: a story (a side story, a tutorial chapter, the world's
narrative arc), its cutscenes and its texts. The engine that runs it is generic, so **a new story is data only** — no C#,
as long as it uses the existing vocabularies. That is proven, not promised: the pilot side story
[`side_pilot`](../../tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/side_pilot/) (a test pack the game never ships)
is dropped into a data folder, loaded by the real content loader, validated, started, played through with real gameplay
handlers and finished — alone, and at the same time as the basic training and VEGA's story — without one line of code
written for it (`SideStoryPilotTests`, `SideStoryPilotClientTests`).

This guide is the path for an author. The details live in three reference documents:

- [TUTORIAL_PACK.md](TUTORIAL_PACK.md) — every manifest, chapter and step field, the per-player state keys, the tutorial.
- [CUTSCENES.md](CUTSCENES.md) — `cutscenes.json`, tiers, cameras, bindings, the seen ledger, pacing, the client director.
- [STORY_VOCABULARY.md](STORY_VOCABULARY.md) — **generated** from the code: every event, condition form, world
  predicate, marker provider, camera type, anchor, caption style, grade, lead type and text budget, with its meaning.

How the server runs packs (the step runtime, the chip, the Journal, the wire) is in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-player-pack-runtime-2466).

## 1. What a pack is

| `kind` | `scope` | Runs | Examples |
|---|---|---|---|
| `side` | `player` | per player, as many at once as are installed | the pilot `side_pilot` |
| `tutorial` | `player` (always) | per player, under the world rule `TutorialPolicy` (Auto / Short courses only / On request / Off) | `data/stories/tutorial/` (basic training B1–B6 + the short courses) |
| `story` | `world` | one per save, chosen at world creation; progress shared by everyone, a per-player track on top | `data/stories/vega_protocol/` (VEGA) |

`scope` defaults to `world` for a pack with beats and `player` for one without. A **player pack** has chapters of steps,
never beats, and can never become the save's story (the guard refuses `--story <player pack>`). The vocabulary tables of
[STORY_VOCABULARY.md](STORY_VOCABULARY.md#packs) list the kinds, scopes and reserved ids from the code.

**Several storylines at once.** A player can follow the basic training, VEGA's story and any number of side stories
together. They share one objective chip and one Journal:

- **The chip** shows one objective, in a fixed order (synthesis §5.3, `GameServer.ChipCandidates`): the finale while
  the player is in its system → the story the player **pinned** ("Track" in the Journal) → the basic training → the world
  story's finale → the world story's lead or mission → a tutorial short course or any mission-bridge step → the other
  player packs by `priority`. A side story is therefore on the chip only when nothing above it has an objective — or
  when the player pins it. Its steps count in the background either way.
- **The objective list** (tag 299, "Show all objectives") carries the first row of each story, up to three.
- **The Journal** lists every story as its own category, in the order tutorials → the world story → the other packs
  (`StoryPackStates`, tag 300), next to VEGA's notes and the player's notes.
- **Narrators.** Each pack speaks with its own `narrator` (`ShipAiLine.Speaker`); a client draws an unknown narrator in
  VEGA's panel style, so a new voice needs no code. Two narrators can talk in the same minute — so a side story should
  not start before the player can move and look around (see [§6](#6-starting-and-running-next-to-other-stories)).

## 2. The worked example: the pilot side story

`side_pilot` ("The Beacon Keeper", narrator Juno) is the smallest complete player pack. Copy it as a template.

```
tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/side_pilot/
  story.json          the manifest: two chapters of existing events, a start rule, a narrator
  cutscenes.json      a template T2 chapter card and a T1 banner
  locales/en.json     English (mandatory)
  locales/de.json     German (mandatory)
```

`story.json` (comments are allowed — the loader reads JSONC):

```jsonc
{
  "id": "side_pilot",
  "nameKey": "story.side_pilot.name",
  "shortNameKey": "story.side_pilot.short",          // ≤ 10 characters: the chip reads "Beacon: …"
  "descriptionKey": "story.side_pilot.desc",
  "kind": "side",
  "scope": "player",
  "priority": 10,
  "narrator": { "id": "juno", "nameKey": "story.side_pilot.juno", "color": "#FFC857" },
  "start": { "after": [ "any:[chapter:tutorial:b1, not:pack:tutorial:enabled]" ], "earlyOptIn": true },
  "chapters": [
    {
      "id": "p1", "titleKey": "story.side_pilot.ch.p1", "summaryKey": "story.side_pilot.ch.p1.sum",
      "openCutscene": "pilot.part",
      "steps": [
        { "id": "p1.1", "objective": { "event": "lamp:toggled", "arg": "on" },
          "explain": { "startKey": "…p1.lamp.start", "chipKey": "…p1.lamp.chip", "doneKey": "…p1.lamp.done" } },
        { "id": "p1.2", "kind": "read", "explain": { "startKey": "…p1.storm.start" } },
        { "id": "p1.3", "objective": { "event": "mine:any", "required": 3 },
          "variants": [ { "when": "world:creative", "startKey": "…p1.dig.start_creative" } ],
          "explain": { "startKey": "…", "chipKey": "…", "hintKey": "…", "doneKey": "…" } }
      ]
    },
    {
      "id": "p2", "titleKey": "story.side_pilot.ch.p2", "summaryKey": "story.side_pilot.ch.p2.sum",
      "openCutscene": "pilot.part",
      "steps": [
        { "id": "p2.1", "objective": { "event": "hotbar:select" }, "explain": { "startKey": "…", "chipKey": "…" } },
        { "id": "p2.2", "objective": { "event": "move:walked", "required": 8 },
          "onComplete": { "cutscene": "pilot.lit" },
          "explain": { "startKey": "…", "chipKey": "…", "doneKey": "…" } }
      ]
    }
  ]
}
```

What happens for a player (all of it asserted by `SideStoryPilotTests`):

1. **Start.** After part 1 of the basic training — or at once when the training does not run for this player (the
   world has the tutorial off, or no tutorial pack is installed). `earlyOptIn` lets the player press *Start* in the
   Journal before that.
2. **Chapter 1.** Its card `pilot.part@p1` ("The Beacon Keeper · Part 1 of 2 / A light in the dark") is committed and
   shown once the client is in the world; Juno asks for the lamp. The lamp switched on (the real `SetLampIntent`
   handler raises `lamp:toggled` `on`) completes `p1.1`; the read step `p1.2` and the dig step `p1.3` start; three
   mined blocks complete both, and the chapter.
3. **Chapter 2.** Its card `pilot.part@p2` (the same template, its own ledger entry) waits for the 90 s card cooldown.
   Another quick-bar slot, eight metres on foot — the banner `pilot.lit` ("Side story complete / The Beacon Keeper")
   plays, then Juno's thanks; the pack is done and wears its badge in the Journal.

To try it in a game: copy the folder to `data/stories/side_pilot/` of a repository checkout (the server reads `data/`;
`scripts/sync-client-libs.ps1` / `.sh` copies it into the client's `StreamingAssets/data/`), start a world, finish part 1
of the training or open the Journal. Admins see every player's pack state with `/story [player]` and preview a scene with
`/cutscene side_pilot pilot.part`; development builds open every scene of the loaded packs with **F9**.

## 3. Folder layout

```
data/stories/<id>/
  story.json          the manifest                                     — mandatory
  locales/en.json     every key the pack uses, English                 — mandatory
  locales/de.json     the same keys, German                            — mandatory
  locales/<code>.json the community languages (fr, es, it, pt, pl, tr, nl, ru, uk, ja, ko, zh)  — optional
  cutscenes.json      the pack's scenes                                — optional (format: CUTSCENES.md)
  lore_sites.json     environmental lore texts (world stories)         — optional (LORE_STRUCTURE.md)
```

- **The id** is letters, digits, `_` and `-` — it becomes part of milestone keys (`story:<id>:…`) and locale keys. Not
  one of the [reserved ids](STORY_VOCABULARY.md#reserved-pack-ids).
- **Locale keys** use the pack's namespace `story.<id>.*` (VEGA's grandfathered `story.vega.*`, `lore.*` … stay as they
  are). A pack key that overwrites a key of an earlier file is reported by the loader; never rename an existing key.
- **Load order**: packs load in ordinal folder order; a repeated id or a reserved id is reported (the later folder wins,
  a reserved one is skipped).

## 4. The manifest

| Group | Fields | Reference |
|---|---|---|
| identity | `id`, `nameKey`, `shortNameKey`, `descriptionKey`, `kind`, `scope`, `priority` | [Pack fields](TUTORIAL_PACK.md#pack-fields) |
| voice | `narrator { id, nameKey, icon, color, voice[], lineStart }`, `speakers[]` (cutscene voices) | [Pack fields](TUTORIAL_PACK.md#pack-fields), [CUTSCENES.md](CUTSCENES.md#cutscenesjson) |
| start | `defaultEnabled`, `start { after[], earlyOptIn }`, `introCutscene` | [§6](#6-starting-and-running-next-to-other-stories) |
| help | `escalate { hintAfter, markerAfter, offerSkipAfter }` | [Pack fields](TUTORIAL_PACK.md#pack-fields) |
| content | `chapters[]` with `steps[]` | [Chapter fields](TUTORIAL_PACK.md#chapter-fields), [Step fields](TUTORIAL_PACK.md#step-fields) |
| tutorial only | `gates`, `graduation`, step `legacyKey`, step `hintRefs` | [TUTORIAL_PACK.md](TUTORIAL_PACK.md) |
| world story only | `beats[]`, chapter `fromBeat` / `fromEvent` / `leads` / `missions`, `insights`, `finale`, `leadBonusMilestones`, `normaliseUnavailableDrivers`, `memoryTriggers`, `epilogueTitleKey` … | [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#vega-as-its-own-pack-prologue-story-track-held-content-chapters-migration-2470) |

Every field is optional and defaults to the behaviour without it. A misspelt field silently keeps its default — both
validators report unknown fields (the C# tests read every pack strictly).

## 5. Chapters, steps, missions, leads

- **Chapters** run in order. A non-optional chapter starts when the previous one is done or skipped (`requires`
  overrides that); an `optional` chapter is a short course — offered once by VEGA when its `offerWhen` holds, started
  from the Journal, hidden altogether while its `visibleIf` fails. `autoCompleteIf` completes a chapter silently for a
  veteran. `openCutscene` / `closeCutscene` stage a chapter's start and end.
- **Steps** are `do` (a verified action — the chip shows it), `read` (spoken, never blocks) or `react` (armed from the
  chapter start, speaks when its situation happens). Tier `must` holds the chapter, `core` holds it until its hint time,
  `opt` never holds a non-optional chapter.
- **An objective** is a game event (`objective.event` + optional `arg` alternatives `"a|b"` + `required`), counted
  from the moment the step activates; `alreadyDoneIf` completes it silently for a player who did it before; `feasibleIf`
  skips a step a world cannot offer, with `alternative` standing in (no dead ends); `variants` swap whole lines by world
  rule; `onStart.reveal` makes it a "where" step (a help marker for this player only); the help escalates by itself
  after minutes of play without progress (the hint line, the marker of a "where" step, the offer to skip).
- **Missions, not steps, pay.** A step pays nothing. A job someone gives the player with a reward is a mission in
  `data/missions.json`; a step may bridge onto it (`"objective": { "type": "Mission", "target": "<mission id>" }` — the
  step completes when the mission is turned in, and the mission keeps its own rewards).
- **World stories** add leads (the chapter's suggested ways forward on VEGA's chip: `fragment`, `unique_site:…`,
  `research:…` …) and per-chapter story missions (`missions[]`, steps that may bridge onto a lead) — see
  [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#chapter-leads-the-chapter-bonus-and-peaceful-world-pacing-2475) and
  [§ Story missions](STORY_IMPLEMENTATION.md#story-missions-per-chapter-2476). Beats still unlock by score, never behind
  a quest.

## 6. Starting and running next to other stories

| Field | Effect |
|---|---|
| `defaultEnabled` (default `true`) | the pack runs for everyone unless the player switches it off in the Journal; `false` = only once the player presses *Start* |
| `start.after` | conditions that must hold before it starts by itself (default: at the first join) |
| `start.earlyOptIn` | the Journal offers *Start* before `start.after` holds |
| `introCutscene` | plays when the pack starts (not when it would complete silently for a veteran) |
| `priority` | the order among the other player packs on the chip and in the Journal (higher first) |

Recommendations:

- **Let the basic training go first.** `"after": [ "any:[chapter:tutorial:b1, not:pack:tutorial:enabled]" ]` waits for
  part 1 of the training (moving, looking, the quick bar) and starts at once for a player the training does not run for.
  `chapter:<pack>:<chapter>` holds for a skipped chapter too.
- **Count on the background.** A side story's step counts whatever owns the chip; its lines are spoken when its steps
  start and finish. Keep them short and few — the player hears the training's voice too.
- **Do not depend on the world story's progress** for a player pack's start; the world story (`pack:<id>:enabled`) may be
  `none` on a sandbox world.

## 7. Conditions

One grammar everywhere — `requires`, `feasibleIf`, `alreadyDoneIf`, `visibleIf`, `offerWhen`, `autoCompleteIf`,
`start.after`, `gates` and `variants[].when`; a JSON list means "all of them":

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate> | lore:<key>
```

Examples: `world:free_flight`, `not:world:hunger`, `any:[blueprint:glider, world:climbing]`,
`all:[step:tutorial:c6.3, not:blueprint:ship_builder]`. The atoms, the pack states and all world predicates with their
meanings: [STORY_VOCABULARY.md → Conditions](STORY_VOCABULARY.md#conditions). `ui:` is never a condition. A condition
naming a pack, chapter, step, blueprint or lore text that does not exist is a validator error.

## 8. Events

A step's `objective.event` names a **game event** the server raises at a gameplay site
(`GameServer.RaiseGameEvent(actor, key, arg, amount)`). The key names the verb, the argument the thing — `mine:any` with
`"arg": "iron_ore"`, never `mine:iron_ore` — so the list does not grow with content. A step may name a prefix pattern
(`"trade:*"`). All 100+ keys with their arguments: [STORY_VOCABULARY.md → Game events](STORY_VOCABULARY.md#game-events-objectiveevent);
where each is raised (file, method):
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#game-events-story-conditions-and-the-world-story-guard-2465).

`ui:<id>` teaching flags (the Tab menu, the Codex, a map opened …) come from the client: allowed only as a player pack's
step objective, never paid, never stored ([the list](STORY_VOCABULARY.md#ui-teaching-flags-objectiveevent-uiid)).

## 9. Cutscenes

`cutscenes.json` holds the pack's scenes; the manifest **binds** them (`introCutscene`, chapter `openCutscene` /
`closeCutscene`, step `onStart.cutscene` / `onComplete.cutscene`, `graduation.cutscene`, a world story's beat `cutscene`
and `finale.cutscenes`). The server decides when a player gets a scene and keeps a per-player **seen ledger** (each
scene once per player; a `"template": true` scene once per binding, as `<id>@<owner>`); the client plays it from its own
copy of the file.

| Tier | Use it for |
|---|---|
| 1 banner | a short "it worked" moment (3–4 s, the player keeps playing) — never waits for the cooldown |
| 2 card | a chapter's title, a beat's card — the camera and the controls stay the player's; titles only (a subtitle or speaker page on a card waits for "continue" while the player walks on) |
| 3 scene | a staged moment on real objects (camera, singleplayer hold, frozen controls) — sparingly |
| 4 set piece | a multi-part sequence (VEGA's finale, the ending) |

- **Cameras, anchors, caption styles, grades, argument sources:** the generated tables in
  [STORY_VOCABULARY.md → Cutscenes](STORY_VOCABULARY.md#cutscenes-cutscenesjson); how each camera is staged and falls back:
  [CUTSCENES.md → Cameras](CUTSCENES.md#cameras). A T1/T2 scene only uses `player_view` or `overlay`.
- **The argument:** a chapter-bound scene gets the chapter's title as `{0}`, its place as `{1}` and the count as `{2}`
  ("Part 1 of 2"); `"arg": "body"` / `"system"` names where the player is.
- **Options:** `chainNext` (a follower that may come at once), `cooldownExempt` ("now or never"), `replayable` (the
  Journal's *Watch again*), `replaceLineKeys` / `returnOnSkip` (lines the scene shows itself), `skippable`,
  `minDisplay` — [CUTSCENES.md](CUTSCENES.md#cutscenesjson).
- **Pacing:** at most one unrequested T2+ scene per 90 s per player; a client in a menu or a fight gets it at the next
  safe moment. Budget the cards: the basic training shows eight in six parts, a short course at most two.
- Music (`client/Music/<track>.mp3`) and sound cues (`client/Assets/Resources/audio/<cue>.*`) must exist; new sounds are
  generated with the asset scripts (`tools/ai-assets`) and counted in `NOTICES.md`.

## 10. Texts and languages

- **Every key in English and German** — mandatory, checked by both validators and by `StoryPackGuardTests` (same key
  set in both files). Write the German by hand.
- **The twelve community languages follow.** AGENTS.md asks for English and German only; English fills every missing key
  of a community language, and `CommunityLocaleTests` guards a pack's community files (no invented keys, the same
  placeholders and tokens, no empty texts). The maintainer's practice is to translate new keys into all twelve in the
  same PR with the machine first pass plus a hand check:
  `uv run --no-project python tools/translate_locale.py <code> --source data/stories/<id>/locales/en.json --file data/stories/<id>/locales/<code>.json`
  (see [TRANSLATION_GUIDE.md](TRANSLATION_GUIDE.md)).
- **Controls only as tokens:** `{key:ToggleLamp}`, `{key:Move}`, `{key:Hotbar}` — never a literal key; every device reads
  the token its own way ([Key tokens](TUTORIAL_PACK.md#key-tokens)). A new token action needs its bracket word
  `ui.vega.act.<Action>` in the core tables.
- **Budgets** (shown characters; a token counts 3, a `{0}` slot 10): short name 10, a step line 200 EN / 240 DE (the basic
  training's 120 / 145), a chip 45 / 53, caption styles by style — the full table:
  [STORY_VOCABULARY.md → Text budgets](STORY_VOCABULARY.md#text-budgets). German runs 12–20 % longer: write English short.
- **Kid-friendly**: many players are children (the strategy makes kid-friendliness a hard constraint). One sentence and
  one action per line; nothing scary or cruel, no violence beyond "fend off", no real people's names, and a story never
  asks the player for anything personal.

## 11. WebGL: the fallback list

The browser build cannot list a folder, so every file under `data/` must also be named in the built-in `FallbackManifest`
of `client/Assets/BlocksBeyondTheStars/Scripts/StreamingAssetsCache.cs` (a WebGL build writes the generated list
`streaming_data_files.txt` itself; the built-in copy is the fallback).
`StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder` fails when a pack file is missing there. Whether
the browser loads the pack is only proven by a local WebGL build (PR CI builds no Unity).

## 12. Validation

```bash
uv run --no-project python tools/validate_story_pack.py                          # every pack in data/stories
uv run --no-project python tools/validate_story_pack.py data/stories/<id>        # one pack
uv run --no-project python tools/validate_story_pack.py data/stories/<id> --strict   # warnings fail too
dotnet test tests/BlocksBeyondTheStars.Tests -c Release --filter "FullyQualifiedName~StoryPackValidationTests|FullyQualifiedName~CutsceneContentTests"
```

The Python tool needs no .NET and never changes a file; `StoryPackValidator` (C#) is its twin and runs for every pack in
`data/stories/` (plus a strict read that names a misspelt field). What both check: ids and reserved ids, kind and scope,
every condition, mission-bridge targets, event keys (an unknown one is a warning — a pack may be written before its
event ships), `ui:` flags, marker providers, alternatives, cutscene bindings and the scenes' own rules, every key in
English and German, budgets — the list is in [TUTORIAL_PACK.md → Validation](TUTORIAL_PACK.md#validation) and
[CUTSCENES.md → Validation](CUTSCENES.md#validation). A pack outside `data/stories/` (a test fixture) is validated
against the repository's data.

A pack whose `story.json`, `lore_sites.json` or `cutscenes.json` is not valid JSON (or cannot be read) does not stop the
game (#2489): the server logs an ERROR naming the pack folder and the file and leaves that pack out, its texts included;
every other pack loads. A save whose world story is that pack runs the default story until the pack is fixed and keeps
its own progress for then. Run the validator before you ship.

A play-through test is the best proof that a pack works. `SideStoryPilotTests` is the template: boot a server with the
pack, join a player, drive real handlers (`SetLampIntent`, `MineBlockOnce`, `SelectHotbarIntent`, `MoveIntent` …) or
`RaiseGameEventForTest` for verbs that are hard to stage, and assert the milestones (`StoryPackKeys`), the chip
(`ObjectiveKeyForTest`), the lines (`ShipAiLine.LineKey`) and the Journal (`StoryPackStatesForTest`).

## 13. Reserved names

- **Pack ids:** see [the generated list](STORY_VOCABULARY.md#reserved-pack-ids) (`none` is the "no story" sentinel; the
  others are milestone families the story code writes).
- **Cutscene ids:** an id starting with `__` is built in (`__recap`, the recap card); `@` separates a template instance
  from its owner and `/` a pack from an id (`tutorial/tut.welcome` in `chainNext`) — neither may appear in an id.
- **Milestones:** everything under `story:<id>:…` (`started`, `done`, `optout`, `ch:<c>:…`, `step:<s>`, `cut:<id>`, …) is
  written by the runtime; a pack never names these keys itself — conditions read them through `step:`, `chapter:` and
  `pack:` atoms.
- **Locale keys:** `story.<id>.*` belongs to the pack; core keys (`ui.*`, `vega.*`, `srv.*`) live in `data/locales/`.

## 14. What still needs C#

| You want | What it takes |
|---|---|
| a verb nobody raises yet | one `RaiseGameEvent(session, key, arg, amount)` line at the gameplay handler, the key as a documented constant in `GameEventKeys` (+ `All`), a row in STORY_IMPLEMENTATION's event table, the vocabulary regenerated (below); the Python validator reads the constants by itself |
| a new `world:` predicate | the name as a documented constant in `StoryWorldPredicates` (+ `All`), one line in the server's `WorldPredicates` registry (`StoryConditionTests` holds both equal), the name in the Python tool's `FALLBACK_PREDICATES` |
| a new marker provider (`onStart.reveal`) | a constant in `StoryRevealProviders` (+ `IsKnown`), the target in `GameServer.RevealTarget`, the Python validator |
| a new camera type, anchor or caption style | the constant (+ `All`) in `CutsceneDefinition.cs`, the staging in Client.Core `CutsceneStaging` and the Unity director/rig (or the caption in `CutsceneScreen`), the Python validator's list, a budget for a new caption style (`StoryPackValidator`, the vocabulary's `CaptionBudget`) |
| a new `ui:` teaching flag | the id in `GameEventKeys.UiEvents`, the client sending it (`NetworkClient.SendUiTutorialEvent`) |
| a new `{key:Action}` token | an `InputAction` or a pseudo-action the resolver knows (`HudUi.KeyTokenGlyph` / `VegaText`), its bracket word `ui.vega.act.<Action>` |
| a lead type, a finale type, a second world story with its own finale | engine work (phase 10 of the plan) — talk to the maintainer first |

**Regenerate the vocabulary** after changing a constant or its `<summary>` (the summary *is* the documentation):

```bash
BBTS_UPDATE_STORY_VOCABULARY=1 dotnet test tests/BlocksBeyondTheStars.Tests -c Release --filter FullyQualifiedName~StoryVocabularyDocTests
```

(PowerShell: `$env:BBTS_UPDATE_STORY_VOCABULARY = "1"` first.) Without the variable `StoryVocabularyDocTests` fails while
[STORY_VOCABULARY.md](STORY_VOCABULARY.md) differs from the code, and while a listed constant has no `<summary>`.

## 15. Limits of v1

- **One world story per save**, many player packs. Several narrative arcs sharing one world (each with its own finale,
  world flags and knowledge levels) are phase 10 of the plan and not built; the finale code knows VEGA's Guardian core
  only.
- **Install on both sides.** The server runs the pack; texts and cutscenes resolve on the client from its own copy. A
  client without the pack shows the raw keys. Desktop: the client's `StreamingAssets/data/stories/`. The browser build and
  the hosted worlds ship official packs only (the operator vouches for what children read).
- **No scripts.** A pack is data; it cannot add a verb, a predicate or a rule — see §14.
- **No rewards** in a pack's own fields (a mission it bridges onto keeps its rewards); a tutorial's only reward is the
  graduation's achievement.
- **No device filter yet** (a step only for touch or pad), no per-pack "hide from the chip" switch besides the pin.

## Checklist for a new pack

1. Folder `data/stories/<id>/` with `story.json`, `locales/en.json`, `locales/de.json` (and `cutscenes.json`).
2. Every step: an `id` carrying its chapter, an `objective`, `explain.startKey` + `chipKey`; `feasibleIf` / `alternative`
   wherever a world could make it impossible.
3. A start rule that does not talk over the basic training.
4. Texts by hand in English and German, tokens for controls, within the budgets; the community languages in the same PR
   (maintainer practice) or left to the translation pass.
5. Every new file in the WebGL `FallbackManifest`.
6. `uv run --no-project python tools/validate_story_pack.py data/stories/<id> --strict` and `StoryPackValidationTests`
   green; a play-through test for anything beyond a handful of steps.
7. TODO.md and the manual (`docs/user/USER_MANUAL.md`) for anything players see.
