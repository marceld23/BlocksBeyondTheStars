# Story packs: manifest and step reference (tutorial pack)

How a **player story pack** is written — the tutorial ("Basic training" B1–B6 and the deep-dive chapters C1–C18) and
future side stories. The pack is data only (`data/stories/<id>/`); the engine that runs it per player is the
**player-pack runtime** (#2466), described in [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-player-pack-runtime-2466).

> **Status.** The runtime, the manifest, the validator and the wire are in place (story packs step 7, #2466). The
> tutorial pack's content — its chapters, steps, texts, the ~50 new game events and the migration of existing players —
> comes with step 10 (#2469), which also retires the old nine-stage VEGA onboarding (`VegaStages`). Until then no player
> pack ships; the runtime is exercised by the test packs in `tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/`.

## Folder

```
data/stories/<id>/
  story.json          the manifest (below)
  locales/en.json     every key the pack uses, English  — mandatory
  locales/de.json     the same keys, German             — mandatory (community languages optional)
  cutscenes.json      optional cutscene definitions (format: CUTSCENES.md)
```

Locale keys use the pack's namespace `story.<id>.*` (the tutorial: `story.tutorial.*`). Key names in text only as
`{key:Action}` tokens (an `InputAction` name). A new file under `data/` must also be added to the WebGL
`FallbackManifest` in `client/Assets/BlocksBeyondTheStars/Scripts/StreamingAssetsCache.cs`.

## Pack fields

Every field is optional and defaults to today's behaviour.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `id` | string | — | pack id: letters, digits, `_`, `-`; not reserved (`none, mem, insight, obj, vega, track, cut`) |
| `nameKey` | key | — | the pack's name |
| `kind` | `tutorial` \| `story` \| `side` | `story` | a `tutorial` follows the world rule `TutorialPolicy` and is always player-scoped |
| `scope` | `world` \| `player` | `world` with beats, else `player` | where progress lives; a player pack has no beats and is never the world story |
| `priority` | int | 0 | Journal order and chip tie-break between player packs (higher first) |
| `shortNameKey` | key | — | ≤ 10 characters EN/DE; replaces "Objective:" on the chip while this pack's step is on it |
| `descriptionKey` | key | — | one line for world creation / the Journal |
| `narrator` | `{ id, nameKey, icon, color, voice[], lineStart }` | `{ id: "vega" }` | who speaks (`ShipAiLine.Speaker`); an unknown id uses VEGA's style |
| `defaultEnabled` | bool | `true` | non-tutorial player packs: runs for everyone unless the player opts out |
| `introCutscene` | cutscene id | — | played when the pack starts for the player — not on "I know the game", not for a veteran the pack completes for silently ([CUTSCENES.md](CUTSCENES.md#bindings--where-a-pack-plays-a-scene)) |
| `gates` | `{ opportunityTips, banter, stationNear }` → condition | — | tutorial only: when the global advisor systems open (step 10 rewires them) |
| `escalate` | `{ hintAfter, markerAfter, offerSkipAfter }` seconds | 180 / 360 / 600 | the help escalation defaults; `0` switches a stage off |
| `start` | `{ after: condition[], earlyOptIn }` | at the first join | when the pack starts for a player; `earlyOptIn` allows `startPack` before `after` holds |
| `chapters` | Chapter[] | [] | below |
| `insights` | `[{ event, minBeat, textKey, onceKey }]` | [] | world packs (read from step 16) |
| `finale` | `{ type, cutscenes{…} }` | — | world packs (presentation bindings) |

## Chapter fields

| Field | Meaning |
|---|---|
| `id`, `titleKey`, `summaryKey` | identity and Journal texts (`b1`…`b6`, `c1`…`c18` in the tutorial) |
| `optional` | a deep dive: not needed for the pack to be done, offered instead of started |
| `requires` | condition[] before the chapter is available (default: the previous non-optional chapter done or skipped) |
| `offerWhen` | condition[] — offers an optional chapter once (never during the mandatory chapters) |
| `autoCompleteIf` | condition[] — veteran evidence: completes the chapter silently (ignored while it is replayed) |
| `autoStart` | default `true` for non-optional, `false` for optional chapters |
| `openCutscene`, `closeCutscene` | cutscene bindings: the opener when the chapter's first step is presented, the closer when a chapter the player was shown is done (not skipped, replayed or completed silently); a `template` scene gets one instance per chapter with the chapter's title as its argument ([CUTSCENES.md](CUTSCENES.md)) |
| `skippable` | default `true` |
| `steps` | Step[] in order |
| `fromBeat`, `fromEvent`, `leads` | world packs only (VEGA chapters, steps 11 and 16) |

## Step fields

| Field | Meaning |
|---|---|
| `id` | unique in the pack, carries its chapter (`b3.1`); the done key is `story:<pack>:step:<id>` |
| `kind` | `do` (default) · `read` (spoken, never blocks, done on `ui:vega_continue` or the next do step) · `react` (armed from the chapter start, never blocks, speaks when its event fires) |
| `tier` | `must` (default; the chapter waits) · `core` (waits until its hint time, then an open extra that still counts) · `opt` (never blocks a non-optional chapter). An optional chapter waits for every do step |
| `objective` | `{ event, arg, required }` — a game event (`GameEventKeys`, a `prefix*` pattern, or `ui:<id>` — only here), `arg` exact or `a\|b` alternatives (empty = any), counted since activation; or `{ type: "Mission", target }` — a mission bridge to `data/missions.json` |
| `legacyKey` | tutorial only: `vega:stage:<mine\|craft\|eat\|scan\|unlock\|launch\|dock\|trade\|land>` — double-written on completion and skip for older builds, read once by the migration, and the old keys an outdated client follows |
| `requires` | condition[] before the step activates (default: the chapter order) |
| `feasibleIf` | condition[] — false at activation ⇒ skipped silently, the `alternative` stands in (no dead ends) |
| `alternative` | another step of the same chapter that runs only standing in for this one |
| `alreadyDoneIf` | condition[] — completes the step silently at activation (not during a replay) |
| `variants` | `[{ when, startKey, chipKey, hintKey, doneKey }]` — whole-line texts per world rule; first match wins; empty fields fall back |
| `explain` | `{ startKey, chipKey, hintKey, doneKey, moreGuide }` — start line, chip text, escalation hint, completion line, Codex article |
| `onStart` | `{ cutscene, reveal }` — the scene plays before the start line (not in a replay); `reveal` makes it a "where" step: `nearest_ore:<block>` or `nearest_settlement` for the help marker |
| `onComplete` | `{ cutscene }` — plays before the done line when the step completes non-silently (it owned the chip, or a react step) |
| `escalate` | per-step override of the pack's escalation |
| `skippable` | default `true` |

Steps pay **no rewards** (maintainer decision; the validator refuses any reward field in a tutorial pack).

## Conditions

One grammar everywhere (`StoryConditionParser`, synthesis §5.3):

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate>
```

`world:` predicates: `station_in_system`, `settlement_reachable`, `airless`, `fragment_on_world`, `player_near`,
`enemies`, `space_enemies`, `bandits`, `free_flight`, `hunger`, `oxygen`, `hazards`, `creative`, `creative_flight`,
`instant_travel`, `return_to_ship`, `starter_teleporter`, `base_visitors`, `space_stations`, `story_active`. `ui:` is never
a condition.

## Per-player state

| Key / field | Meaning |
|---|---|
| `story:<pack>:started` / `:optout` / `:done` | pack state |
| `story:<pack>:ch:<c>:done` / `:skipped` / `:offered` / `:declined` / `:replay` / `:active` | chapter state (`:active` = a started optional chapter) |
| `story:<pack>:step:<s>` / `…:skipped` | step done / skipped |
| `story:<pack>:replay:<s>` | step done again during its chapter's replay (removed when the replay ends) |
| `story:<pack>:migrated:v1` | the one-time migration ran |
| `story:<pack>:cut:<id>:sent` / `story:<pack>:cut:<id>` | a cutscene committed to the player / confirmed by the client (the seen ledger, [CUTSCENES.md](CUTSCENES.md#the-seen-ledger-server-gameservercutscenescs)) |
| `PlayerState.StepProgress["<pack>:<s>"]` | running counter of an active step (only active steps) |
| `PlayerState.TrackedStoryId` | the pinned story ("" = automatic chip order) |

## The world rule

`GameRules.TutorialPolicy` = `Auto` (default; starts for every new player, skippable at every level) · `OptIn` (listed,
starts on the player's request) · `Off` (not offered). Set at world creation (`--tutorial auto|optin|off`, baked into the
save's rules), changed live by the admin (`SetWorldRulesIntent.TutorialPolicy`, admin command `tutorial policy …`). A
save without the field reads `Auto`.

## Validation

```bash
uv run --no-project python tools/validate_story_pack.py                       # every pack in data/stories
uv run --no-project python tools/validate_story_pack.py data/stories/<id>     # one pack (--strict: warnings fail)
dotnet test tests/BlocksBeyondTheStars.Tests --filter FullyQualifiedName~StoryPackValidationTests
```

Both run the same checks (`StoryPackValidator` in C#): ids unique, key-safe and not reserved; `kind`/`scope` (tutorial ⇒
player, a player pack has no beats, a world pack has beats); every condition parses and names existing packs, chapters,
steps and blueprints; mission-bridge targets exist in `data/missions.json`; event keys are published (a warning — a pack
may be written before its event ships); `ui:` only in a player pack's step objectives; marker providers and their
blocks; legacy keys (tutorial only, one of the nine stages, once each); alternatives within the chapter; **no rewards in a
tutorial**; every key in English **and** German; every cutscene binding resolves in the pack's `cutscenes.json` and the
cutscenes follow their own rules ([CUTSCENES.md](CUTSCENES.md#validation)); the
short name ≤ 10 characters; the text budget — basic-training lines (a tutorial's non-optional chapters) ≤ 120 EN /
145 DE, other step lines ≤ 200 / 240, chip ≤ 45 / 53 (a `{key:…}` token counts 3, a `{0}` slot 10). The C# test also
reads every pack strictly, so a misspelt field fails; the Python tool reports it as a warning. Neither tool ever changes a
file.

## Content (step 10)

The tutorial's chapters, steps and texts (synthesis §3.3/§3.4, d1 §3), its new game events, the three gates, the
migration of existing players' `vega:stage:*` keys and the retirement of the old onboarding chain are filled in here
with step 10.
