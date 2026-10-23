# Story packs: manifest, step reference and the tutorial pack

How a **player story pack** is written — the tutorial ("Basic training" B1–B6, later the deep-dive chapters C1–C18) and
future side stories — and what the shipped tutorial pack contains. The pack is data only (`data/stories/<id>/`); the engine
that runs it per player is the **player-pack runtime** (#2466), described in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-player-pack-runtime-2466). Start with the authoring guide
[STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md) (#2483: the path for an author, the pilot side story as the worked
example); this document is its field reference, and [STORY_VOCABULARY.md](STORY_VOCABULARY.md) lists every event, condition
form and world predicate generated from the code.

> **Status.** Since story packs step 10 (#2469) the basic training **is** the tutorial pack `data/stories/tutorial/`: the
> old hard-coded nine-stage VEGA onboarding (`VegaStages`, `VegaIsVeteran`, the client's copy of the stage list) is gone.
> The deep-dive chapters of the parts "Research" and "Flying" — C2–C5 — came with step 18 (#2479,
> [Deep dives](#the-deep-dives-research-and-flying-c2c5-2479)), the parts "Ship building" and "Base building" — C1, C6, C7,
> C17 — with step 19 (#2480, [Ship and base building](#the-deep-dives-ship-building-and-base-building-c1-c6-c7-c17-2480));
> the part "Crystal net" — C8 (a bridge onto VEGA's Crystal workshop jobs) and C14 — with step 20 (#2481,
> [Crystal net](#the-deep-dives-crystal-net-c8-c14-2481)); the part "More to discover" — C9–C13, C15, C16 and C18 — with step 21
> (#2482, [More to discover](#the-deep-dives-more-to-discover-c9c13-c15-c16-c18-2482)), which also brought the chapter field
> `visibleIf` (a course the world's rules rule out is left out). The tutorial's cards
> (`tut.welcome`, `tut.strip` …) came with step 14 (#2473, [Cards](#cards-2473)); VEGA's own pack and the hand-over to its
> chapter 1 with step 11. Test packs for the runtime live in `tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/`.

## Folder

```
data/stories/<id>/
  story.json          the manifest (below)
  locales/en.json     every key the pack uses, English  — mandatory
  locales/de.json     the same keys, German             — mandatory (community languages optional)
  locales/<code>.json the twelve community languages    — the tutorial pack ships all twelve (#2459)
  cutscenes.json      optional cutscene definitions (format: CUTSCENES.md)
```

Locale keys use the pack's namespace `story.<id>.*` (the tutorial: `story.tutorial.*`). Key names in text only as
`{key:Action}` tokens — an `InputAction` name or one of the pseudo-actions below. A new file under `data/` must also be
added to the WebGL `FallbackManifest` in `client/Assets/BlocksBeyondTheStars/Scripts/StreamingAssetsCache.cs`.

### Key tokens

`{key:Action}` names a control, never a key: every device reads it its own way (one resolver: `HudUi.KeyTokenGlyph` on the
client, `VegaText.TouchControlLabel` for the tablet's buttons, the server's bracket words `ui.vega.act.<Action>` for an
older client). Besides the rebindable `InputAction`s (`Interact`, `UiMenu`, `ToggleLamp`, `RotateShape`, `FlightMap`, …)
the **pseudo-actions** of the continuous controls are allowed (#2469, `VegaText.PseudoActions`):

| Token | Keyboard | Pad | Touch |
|---|---|---|---|
| `Move` | WASD (`ui.key.move_keys`) | Left stick | the left stick |
| `Look` | Mouse (`ui.key.look_mouse`) | Right stick | Swipe (`ui.key.touch_look`) |
| `Jump` | Space (`ui.key.jump_keys`) | bottom face button | JUMP |
| `Crouch` | Ctrl/C (`ui.key.crouch_keys`) | B | DOWN |
| `Mine`, `Scan` | LMB | RB | MINE |
| `Place` | RMB | LB | PLACE |
| `Hotbar` | 1–9 | D-pad ◄ ► | ◄ ► |

A new token action needs its bracket word `ui.vega.act.<Action>` in English and German (`LegacyLineTextTests`), and
`VegaLineBudgetTests` checks that every token names a real action or pseudo-action.

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
| `narrator` | `{ id, nameKey, icon, color, voice[], lineStart }` | `{ id: "vega" }` | who speaks (`ShipAiLine.Speaker`); an unknown id uses VEGA's style. #2495: `voice` = the chatter clips (`Resources/audio`) a cutscene speaker box plays while it types (empty: VEGA's `vega_chatter_1..3` for `vega`, none for another id); the validators require every `voice` clip and `lineStart` to exist |
| `defaultEnabled` | bool | `true` | non-tutorial player packs: runs for everyone unless the player opts out |
| `introCutscene` | cutscene id | — | played when the pack starts for the player — not on "I know the game", not for a veteran the pack completes for silently ([CUTSCENES.md](CUTSCENES.md#bindings--where-a-pack-plays-a-scene)) |
| `gates` | `{ opportunityTips, banter, stationNear }` → condition, plus `holdHints: { <hint id>: condition }` | — | tutorial only: when the global advisor systems open, and (#2549) which one-time VEGA hints wait until the training reaches their topic (see [Gates](#gates)) |
| `escalate` | `{ hintAfter, markerAfter, offerSkipAfter }` seconds | 180 / 360 / 600 | the help escalation defaults; `0` switches a stage off |
| `start` | `{ after: condition[], earlyOptIn }` | at the first join | when the pack starts for a player; `earlyOptIn` allows `startPack` before `after` holds |
| `graduation` | `{ lineKey, counter, suggest[], cutscene }` | — | #2469: when a player finishes the mandatory chapters **by playing** (not skipped, not by migration evidence): the graduation cutscene (#2473, before the line, once per player), the closing line (a client with `packs1`; an older client of a tutorial hears `vega.done`), the achievement counter advanced once (`data/achievements.json` says which achievement watches it), up to three optional chapters marked offered (only those that are available and not done, declined or running). The end of a full restart (the last mandatory chapter's replay) graduates again; replaying one earlier chapter does not |
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
| `visibleIf` | #2482, optional chapters only: condition[] — the chapter exists for the player only while it holds (a course the world's rules can rule out: combat, animals, settlements, more than one player). While it fails the chapter is left out of the Journal and never offered, suggested or started; one the player runs, replays, finished or skipped stays listed. A mandatory chapter with it is a validator error |
| `autoCompleteIf` | condition[] — veteran evidence: completes the chapter silently (ignored while it is replayed; never a graduation) |
| `autoStart` | default `true` for non-optional, `false` for optional chapters |
| `openCutscene`, `closeCutscene` | cutscene bindings: the opener when the chapter's first step is presented, the closer when a chapter the player was shown is done (not skipped, replayed or completed silently); a `template` scene gets one instance per chapter with the chapter's title as its argument ([CUTSCENES.md](CUTSCENES.md)) |
| `skippable` | default `true` |
| `steps` | Step[] in order |
| `fromBeat`, `fromEvent`, `leads` | world packs only (VEGA chapters, steps 11 and 16) |
| `missions` | world packs only (#2476): the chapter's story missions `[{ id, titleKey, summaryKey, feasibleIf, alternative, skippable, steps }]` — run per player by this runtime as optional chapters of the story's id, one at a time, handed out when the player's track is in the chapter; every step a `do` step with a `chipKey` (no `{0}`); see [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#story-missions-per-chapter-2476) |

## Step fields

| Field | Meaning |
|---|---|
| `id` | unique in the pack, carries its chapter (`b3.1`); the done key is `story:<pack>:step:<id>` |
| `kind` | `do` (default) · `read` (spoken, never blocks, done on `ui:vega_continue` or the next do step) · `react` (armed from the chapter start, never blocks, speaks when its event fires; stays armed after the pack is done until it fired once) |
| `tier` | `must` (default; the chapter waits) · `core` (waits until its hint time, then an open extra that still counts) · `opt` (never blocks a non-optional chapter). An optional chapter waits for every do step |
| `objective` | `{ event, arg, required }` — a game event (`GameEventKeys`, a `prefix*` pattern, or `ui:<id>` — only here), `arg` exact or `a\|b` alternatives (empty = any), counted since activation; or `{ type: "Mission", target }` — a mission bridge to `data/missions.json`: the step completes when that mission is **turned in** (one turned in before completes it silently at activation — a chain the player already runs is adopted); the mission stays where it is, the player takes and hands it in as always, and it keeps its own rewards (#2481, the Crystal workshop course); or (#2476, a world story's missions only) `{ type: "Lead", target: "<lead word>" }` — a lead bridge, followed like that chapter lead (its events and evidence, its placed chip text and marker; not `explore`, not the finale's words) |
| `legacyKey` | tutorial only: `vega:stage:<mine\|craft\|eat\|scan\|unlock\|launch\|dock\|trade\|land>` — double-written on completion and skip for older builds, read once by the migration, and the old keys an outdated client follows |
| `requires` | condition[] before the step activates (default: the chapter order); a react step is armed only while it holds |
| `feasibleIf` | condition[] — false at activation ⇒ skipped silently, the `alternative` stands in (no dead ends); an alternative that is infeasible too is skipped as well |
| `alternative` | another step of the same chapter that runs only standing in for this one (a `do` or a `read` step) |
| `alreadyDoneIf` | condition[] — completes the step silently at activation (not during a replay) |
| `variants` | `[{ when, startKey, chipKey, hintKey, doneKey, midKey }]` — whole-line texts per world rule; first match wins; empty fields fall back |
| `explain` | `{ startKey, chipKey, hintKey, doneKey, midKey, moreGuide }` — start line, chip text, escalation hint, completion line, half-way line, Codex article. `midKey` (#2550) is spoken once per activation at the objective's half-way moment, before it completes — today only the walk-back objective `ship:returned`: the moment the player is far enough from the ship (it also restarts the help clock, like progress); other objectives never speak it |
| `onStart` | `{ cutscene, reveal }` — the scene plays before the start line (not in a replay); `reveal` makes it a "where" step: `nearest_ore:<block>`, `nearest_settlement` or `nearest_food_plant` for the help marker (for this player only, never `RevealPoi`; on the map and the compass until the step is done or skipped, #2570), or `nearest_station` (#2471: the nearest station becomes the player's space waypoint, in flight or landed), or `unique_site:<observatory\|shrine\|derelict>` (#2476: the site's spot while the player is on its world) |
| `onComplete` | `{ cutscene }` — plays before the done line when the step completes non-silently (it owned the chip, or a react step) |
| `escalate` | per-step override of the pack's escalation |
| `skippable` | default `true` |
| `hintRefs` | #2469 ("one teaching voice", G12): VEGA's once-hints and context tips this step teaches itself (`invfull`, `lamp_off` …). A `do`/`read` step holds them while it is active in, or still ahead in, a mandatory chapter of a running pack (an active step of a started deep dive too) — not as an open extra past its hint time, not after its chapter is through. When the step's line is shown they count as told (a once-hint's flag is burned, a tip's first telling is booked and its cooldown starts); a skipped step gives them back untold. React steps hold nothing — their event is raised right before the hint they absorb, so the step speaks first. #2459: also the client-only lessons the step teaches (`climb`, `planet_card` …, kept quiet by the client while the step's chapter runs with the step active or ahead), and a course's offer stands in for the hints and tips its steps name (see "The tip becomes the offer" below) |

Steps pay **no rewards** (maintainer decision; the validator refuses any reward field in a tutorial pack). The graduation's
achievement is the one visible reward and carries no items. The rule covers the pack's **own** fields: a mission a step
bridges onto keeps the rewards it has in `data/missions.json` (#2481 — the Crystal workshop's six jobs pay their items and
knowledge with or without the course; removing them would take something away from everybody who plays the chain).

## Conditions

One grammar everywhere (`StoryConditionParser`, synthesis §5.3):

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate> | lore:<key>
```

`lore:<key>` (#2476): the player has read that lore text (a `lore_sites.json` key).

The complete list of predicates with their meanings, generated from the code, is
[STORY_VOCABULARY.md → World predicates](STORY_VOCABULARY.md#world-predicates-worldpredicate) (#2483); the history below says
which step brought which.

`world:` predicates: `station_in_system`, `settlement_reachable`, `airless`, `fragment_on_world`, `player_near`,
`enemies`, `space_enemies`, `bandits`, `free_flight`, `hunger`, `oxygen`, `hazards`, `creative`, `creative_flight`,
`instant_travel`, `return_to_ship`, `starter_teleporter`, `base_visitors`, `space_stations`, `story_active` and, since
#2469, `food_plants_near` (a plant whose harvest is food within a short walk), `trees_near` (a block that drops wood
within a short walk), `shape_block_carried` (a shaped material or a prop block in the backpack), `research_ready` (the
knowledge covers a blueprint researchable now without a data fragment), `all_blueprints` (Creative or the head-start
option hands every blueprint out), `new_world_in_system` (a planet, moon or asteroid of the player's system never landed
on). The two probes read loaded blocks around the player and run only when a step activates. Since #2476 (the story
missions): `fragments_left` (the world story has an unfound net fragment), `observatory_reachable`, `derelict_reachable`
(the unique site exists in this save and the player can reach it), `settlements`, `vaults`, `space_wrecks` (the world
options are not off). Since #2479 (the deep dives): `data_cache_near` and `scanner_unknown` (VEGA's once-a-second tip pass
found that situation for the player — a data cache in reach, digging a lot without the terrain scanner; read from
`PlayerSession.Vega.VegaTipLive`, no extra probe — the "moment of need" a course is offered at), `jump_ready` (the active
ship has a jump generator fitted), `arcade_ready` (the player owns a DataQube game, or a data cube waits on their world),
`monument_unread` (a rune monument on the player's world they have not read), `asteroid_mining` (the Asteroid destruction
rule is not Off), `docked` (aboard a station), `job_active` (a board or settlement-chain job in progress), `forge_ready`
(a forge crafted before or carried, or a refinery fitted), `wormhole_known` (a wormhole opens in a system the player
knows), `debris_fields` (the world option is not off). Since #2480 (ship and base building): `tier_gate` (a tool-tier gate
turned one of the player's swings away within the last minute — `PlayerSession.Tutorial.TierGateRefusedAt`, stamped by
`NoteTierGate`, whatever the tip's pacing or the hints switch say), `module_affordable` (a ship module the active ship lacks,
its blueprint owned, its materials in the backpack or the hold — the free starter modules never count), `hull_damaged` (the
active ship has lost hull points), `keel_laid` (a self-built ship exists in the fleet, finished or not), `ship_built` (a
self-built ship is commissioned), `base_here` (a base on the player's body that they own **or that an ally or a crewmate
owns** — E32), `base_core_ready` (a base core carried or crafted before), `night_outside` (on foot on a world while the
local sun is down there), `station_core_ready` (a station core carried, or its materials), `station_owned` (a commissioned
station of the player's). Since #2481 (the crystal net): `factory_near` (VEGA's tip pass found a factory terminal close to
the player — `PlayerSession.Vega.VegaTipLive`, like `data_cache_near`), `factory_here` (a spawned factory stands on
the player's world), `access_code` (an SPS code in the backpack), `factory_owned` (the player has claimed a factory,
anywhere in the save), `machine_circuit` (a machine of the player's finished a job a signal started — the achievement counter
`crystal:machine_job`), `machine_catchup` (the world rule *Machines catch up* is not 0). Since #2482 (More to discover):
`creatures` (the Creature abundance rule is not Off), `tameable_near`, `settlement_near`, `ruin_near`, `treasure_near`,
`wreck_signal` (VEGA's tip pass found that situation — `VegaTipLive`, like `data_cache_near`; `wreck_signal` takes the
manual-steering tip too), `herd_begging` (an animal came to beg the player within the last minute —
`PlayerSession.Tutorial.HerdBeggedAt`), `companion_owned` (a tamed animal of the player's), `seed_carried` (a sapling or a
seed in the backpack — an item whose block is a plant), `hostile_near` (a machine, a hostile animal or a hostile robber
within 40 blocks on the player's world), `bandit_hail` (a robber's demand waits for an answer), `climbing` (the climbing
pose now), `train_here` (a train the player may board runs on their world), `multiplayer` (the world is hosted for more
than one player), `radio_worn`, `planet_wrecks` (the world option is not off). `ui:` is never a condition.

## Per-player state

| Key / field | Meaning |
|---|---|
| `story:<pack>:started` / `:optout` / `:done` | pack state |
| `story:<pack>:ch:<c>:done` / `:skipped` / `:offered` / `:declined` / `:replay` / `:active` | chapter state (`:active` = a started optional chapter) |
| `story:<pack>:step:<s>` / `…:skipped` | step done / skipped |
| `story:<pack>:replay:<s>` | step done again during its chapter's replay (removed when the replay ends) |
| `story:<pack>:migrated:v1` | the one-time migration ran |
| `story:<pack>:cut:<id>:sent` / `story:<pack>:cut:<id>` | a cutscene committed to the player / confirmed by the client (the seen ledger, [CUTSCENES.md](CUTSCENES.md#the-seen-ledger-server-gameservercutscenescs)) |
| `vega:intro`, `vega:stage:<id>` | the legacy onboarding keys, double-written for older builds (the tutorial's start writes `vega:intro`, each step its `legacyKey`, a graduation or a skip all nine) |
| `PlayerState.StepProgress["<pack>:<s>"]` | running counter of an active step (only active steps) |
| `PlayerState.TrackedStoryId` | the pinned story ("" = automatic chip order) |

## The world rule

`GameRules.TutorialPolicy` = `Auto` (default; starts for every new player, skippable at every level) · `Courses`
("Short courses only", #2498, see below) · `OptIn` (listed, starts on the player's request) · `Off` (not offered — no
courses either). Set at world creation (the world-creation **Stories** page, #2472: "Auto / Short courses only / On
request / Off" with one line each — the page's order, from the most guidance to none, not the enum's → `--tutorial
auto|courses|optin|off`, emitted only when not `Auto`, baked into the save's rules), changed live by the admin (the
*Tutorial* row of the in-game world rules → `SetWorldRulesIntent.TutorialPolicy`, or the admin command `tutorial policy …`).
A save without the field reads `Auto`; no server preset switches it off (`family` = `Auto`); hosted and glitch worlds pass
no flag (`Auto`).

**Short courses only (`Courses`, #2498).** The pack runs as under `Auto` (`StoryPackEnabledFor` — so the courses are
offered and the Character tab's switch reads "on"), but `AdvancePack` starts a player's tutorial that has not started yet with its
mandatory part skipped **silently**: `StartPack(playIntro: false, legacyIntro: false)` + `SkipMandatory(legacySendOff:
false)` — no welcome card, no line (an older client hears neither the old intro nor `vega.done`), no achievement; the
legacy graduation keys are written as for a skip. The VEGA story's start rule (`chapter:tutorial:b6`) then holds, so the
join reads: prologue → story track start (reason `Training`) → `ch1.open` (the prologue's `chainNext` to `tut.welcome`
never fires — b1 is skipped). Only a pack that has not started takes this path: a live switch leaves a running training
alone, and anyone not yet started (an `OptIn` or `Off` world switched to `Courses`) gets the silent skip on the next
advance (`SetTutorialPolicy` / the live world-rules edit re-advance everyone). The player can still run the training from
the Journal ("Start basic training", below). The enum value is appended (int 3, saves store the number): an older server
reads it as `Auto` (its policy switch's default) — a graceful downgrade; only a `config/server.json` written with the
string `"Courses"` would not parse on an older server.

## Validation

```bash
uv run --no-project python tools/validate_story_pack.py                       # every pack in data/stories
uv run --no-project python tools/validate_story_pack.py data/stories/<id>     # one pack (--strict: warnings fail)
dotnet test tests/BlocksBeyondTheStars.Tests --filter FullyQualifiedName~StoryPackValidationTests
```

Both run the same checks (`StoryPackValidator` in C#): ids unique, key-safe and not reserved; `kind`/`scope` (tutorial ⇒
player, a player pack has no beats, a world pack has beats); every condition parses and names existing packs, chapters,
steps and blueprints; mission-bridge targets exist in `data/missions.json`; event keys are published (a warning — a pack
may be written before its event ships); `ui:` only in a player pack's step objectives, and one of the flags a client
reports (a warning otherwise); marker providers and their blocks; legacy keys (tutorial only, one of the nine stages, once
each); alternatives within the chapter; no empty `hintRefs`; `gates.holdHints` (#2549: a hint id without spaces and a
condition that parses; a warning outside a tutorial); the graduation (its line in both languages within the line
budget, a `<verb>:<thing>` counter, at most three suggested chapters that are optional chapters of the pack); **no rewards
in a tutorial** (in the pack's own document — a bridged mission keeps the rewards it has in `data/missions.json`); every key in English **and** German; every cutscene binding resolves in the pack's `cutscenes.json` and
the cutscenes follow their own rules ([CUTSCENES.md](CUTSCENES.md#validation)); the short name ≤ 10 characters; the text
budget — basic-training lines (start, hint, done and half-way lines of a tutorial's non-optional chapters) ≤ 120 EN /
145 DE, other step lines ≤ 200 / 240, chip
≤ 45 / 53 (a `{key:…}` token counts 3, a `{0}` slot 10). The C# test also reads every pack strictly, so a misspelt field
fails; the Python tool reports it as a warning. Neither tool ever changes a file.

## The tutorial pack (`data/stories/tutorial/`, #2469)

`kind: tutorial`, player scope, priority 100, narrator VEGA, short name "Training" / "Ausbildung", escalation 180 / 360 /
600 s. VEGA presents it in **neutral words** (maintainer decision): the amnesia premise appears only as whole-line variants
under `pack:vega_protocol:enabled` (b1.3 "what's left of me", b1.6 "I wrote it before the crash"). Every step is: explain →
try it → the server verifies it. Each part opens with a card that says "Part n of 6" (#2473, [Cards](#cards-2473)) — the
part's first step line no longer repeats it (G12).

### Content

Tier · kind: **M** = must, **C** = core · **do** / **read** / **react**. Event = what the step listens to
(`GameEventKeys`); variants swap the whole line.

| Step | Teaches | Event (arg × required) | Tier · kind | World rules / feasibility | Legacy |
|---|---|---|---|---|---|
| **B1 First steps** | | | | | |
| b1.1 | walk `{key:Move}`, look `{key:Look}` | `move:walked` × 8 | M · do | — | — |
| b1.2 | jump, crouch at edges | `move:jump` | C · do | `creative_flight` → "press twice to fly" | — |
| b1.3 | "I'm VEGA; right below my text you always see your goal" (the objective chip sits directly under VEGA's panel — #2548: never "the bar at the top") | `ui:vega_continue` | M · read | VEGA story → premise variant | — |
| b1.4 | quick bar: slot 2 then slot 1 | `hotbar:select` × 2 | M · do | — | — |
| b1.5 | the menu `{key:UiMenu}` | `ui:menu_open` | M · do | — | — |
| b1.6 | the Codex | `ui:codex_open` | C · do | VEGA story → premise variant | — |
| **B2 Staying alive** | | | | | |
| b2.1 | three bars; air refills aboard (absorbs `breathe`) | `ui:vega_continue` | M · read | no oxygen / no hunger / both → whole-line variants | — |
| b2.2 | eat food `{key:Place}` | `eat:any` | M · do | `feasibleIf hunger` | eat |
| b2.3 | bushes drop berries; marker `nearest_food_plant` (after 120 s, before the hint — #2742) | `item:gained` `berries` × 3 | C · do | `feasibleIf hunger, food_plants_near` | — |
| b2.4 | the suit's ration dispenser | `suit:auto_feed` | C · react | `requires hunger` | — |
| b2.5 | the compass's ship blip (top right): walk 40 m away, then find the way back (absorbs `ship_far`). #2550: `walk` measures like the compass's "Ship N m" — flat and seam-aware, to the landed ship's anchor (`ShipPlacement`), with the compass's rounding (`Mathf.RoundToInt`, a .5 to the even metre): the compass reading 40 on foot (≥ 39.5 m), then aboard or reading 10 (≤ 10.5 m); only while the player's **own** step waits (the event bus is shared), and the step's activation starts the walk afresh; VEGA's half-way line (`midKey` "Far enough — now find your way back") once when the 40 m are reached; the hints remind of the 40 m | `ship:returned` (`menu`/`teleport`/`walk`) | M · do | hint variants: starter teleporter, no "Back to my ship" | — |
| b2.6 | the lamp `{key:ToggleLamp}` (absorbs `night`, `lamp_off`) | `lamp:toggled` | C · do | — | — |
| b2.7 | the suit fights cold/heat; a roof helps | `suit:climate` | C · react | `requires hazards` | — |
| b2.8 | fainting is gentle | `player:respawned` | M · react | one line for every death rule | — |
| **B3 Mining and carrying** | | | | | |
| b3.1 | hold `{key:Mine}` | `mine:any` × 3 | M · do | — | mine |
| b3.2 | iron sits a few blocks down; marker `nearest_ore:iron_ore` | `mine:any` `iron_ore` × 4 | M · do | — | — |
| b3.3 | too hard for the drill → blueprints (absorbs the first `tier_gate`) | `mine:refused` | C · react | — | — |
| b3.4 | moving items in the backpack | `item:moved` | C · do | — | — |
| b3.5 | gear works only when worn | `gear:equipped` | M · do | — | — |
| b3.6 | full backpack → the cargo hold (absorbs `invfull`) | `inventory:full` | C · react | — | — |
| **B4 Crafting and building** | | | | | |
| b4.1 | the ship's workshop, 2 iron ingots | `craft:any` `iron_ingot` × 2 | M · do | `creative` → "free here" | craft |
| b4.2 | 2 ingots → 1 plate (absorbs `craftable_now`) | `craft:any` `iron_plate` | M · do | — | — |
| b4.3 | place blocks `{key:Place}` | `build:any` × 3 | M · do | — | — |
| b4.4 | turn shaped blocks `{key:RotateShape}` | `build:rotated` | C · do | `feasibleIf shape_block_carried`, else b4.4a (read: "later you'll …") | — |
| b4.5 | a campfire needs no station | `build:any` `campfire` | C · do | `feasibleIf trees_near` | — |
| **B5 Scanning and research** | | | | | |
| b5.1 | scan `{key:Scan}` | `scan:performed` | M · do | — | scan |
| b5.2 | new things pay knowledge | `scan:any` × 3 | C · do | `alreadyDoneIf research_ready` | — |
| b5.3 | research at your own cockpit (absorbs `blueprint_affordable`) | `research:any` | M · do | `feasibleIf not all_blueprints` (Creative: skipped) | unlock |
| b5.4 | bigger blueprints need data fragments (absorbs `data_cache_near`) | `data:found` (`cache`/`fragment`) | M · react | — | — |
| **B6 Lift-off** | | | | | |
| b6.1 | launch at the cockpit `{key:Interact}` | `space:enter` `launch` | M · do | `feasibleIf free_flight` | launch |
| b6.2 | steering, the chart `{key:FlightMap}` | `flight:flown` × 300 | C · do | `feasibleIf free_flight` | — |
| b6.3 | the ship scanner: hold until the ring is full | `scan:performed` `asteroid\|anomaly` | C · do | `feasibleIf free_flight` | — |
| b6.4 | docking `{key:Interact}`; marker `nearest_station` (the flight target, #2471) | `station:boarded` | M · do | `feasibleIf free_flight, station_in_system`, else b6.4a (trade with a settlement vendor, `trade:barter`) | dock |
| b6.5 | trading (absorbs `trader_near`) | `trade:*` | M · do | `alreadyDoneIf` b6.4a | trade |
| b6.6 | a job from a board | `mission:board_accepted` | C · do | — | — |
| b6.7 | landing on a new world (landing map) | `visit:world` | M · do | `feasibleIf free_flight, new_world_in_system`, else b6.7a (any landing from flight, `travel:landed` `flight`, `feasibleIf free_flight`) | land |
| b6.8 | map travel home | `travel:landed` `quick` | C · do | `feasibleIf free_flight or instant_travel`; `instant_travel` variant | — |

**Graduation:** `story.tutorial.graduated` ("Basic training complete! You can survive, build, research and fly — short
courses wait in your Journal." — neutral, without `vega.done`'s memory hook), the achievement **Graduate** (`graduate`,
counter `tutorial:graduated`, no items), the suggested deep dives `c3` (Pilot), `c4` (Stations, trade and jobs) and since
#2480 `c1` (Workshop and titanium) marked offered — in the Journal, without a line. The suggestions count as that moment's
offer, so the next offer VEGA makes by herself waits the usual 90 s. Since #2498 every **skip** of the basic training
("Skip all", the old Skip button in the Character tab, `/tutorial skip`, the silent skip of a `Courses` world — `SkipMandatory`) marks the
suggestions too (`SuggestCourses(skip: true)`), but only those without a moment of their own (`offerWhen`): c3 and c4 had
no other way to be offered to a skipper, while c1 keeps its offer at the tool-tier wall, which tells a player who skipped
more than a silent mark. A skip never graduates (no line, no counter). The hand-over to VEGA's chapter 1 is step 11. Since #2473 the graduation card `tut.graduation` shows the achievement and
the closing line itself.

### The first-start question and the offer buttons (#2499)

A VEGA line can carry **buttons** (`ShipAiLine.Choices`, see STORY_IMPLEMENTATION) for a client that announces
`choices1` — the Unity client does, a headless client or an older build does not and gets the plain lines.

- **The question.** When the basic training starts **by itself** for a real newcomer (`GameServerVegaChoices.cs`,
  `AsksBeforeTraining`: the official `tutorial` pack, world rule **Auto**, the join snapshot says newcomer, nothing of
  the pack done or skipped after the migration, not a training that would complete silently), VEGA asks once — in the
  first walk, right after b1's opener `tut.welcome` and before b1.1's start line (`AskSkipBeforeFirstStep` in
  `ActivateStep`): `story.tutorial.ask_skip` "Want me to show you the basics? If you know the game already, you can skip
  the training." with **[Let's go]** (`continue` — the continue control; nothing is sent) and **[I know the game —
  skip]** (`skipPack`). Never on a rejoin or a restart (the pack has started), on request (`OptIn`, "Start basic
  training"), on `Courses` (it skips by itself) or `Off`. The training does not wait for the answer: b1.1 is active and
  counts from the start — **doing nothing means the training runs**, and a later "Skip all" still works.
- **The answer.** The skip arrives as the ordinary `StoryPackIntent` (pause-whitelisted, validated like the Journal's).
  While the question is open for the session (`PlayerSession.Story.AskSkipOpenPack`) and the training still had
  something to skip, `SkipPack` sends `story.tutorial.skipped_hint` ("All right, no training. If you ever want it, it
  waits in your Journal.") before `AdvancePlayer`, so it comes ahead of the VEGA story's lines. The client drops the
  training's lines still waiting in the panel (`PackId == tutorial`). A Journal skip without the question gets no hint.
- **Course offers.** A client with `choices1` hears `vega.chapter.offer_choice` ("New short course: {0}. Want to try it
  now? It also waits in your Journal.") with **[Later]** (the continue control → `declineChapter`, the Journal's Later)
  and **[Start now]** (deliberate → `startChapter`). "Later" is the default on purpose: a player tapping through VEGA's
  lines never lands in a course they did not choose. Everyone else keeps `vega.chapter.offer`.
- **On the client** (`VegaPanel`, model in Client.Core `VegaChoices.cs`): the primary button replaces the continue pill,
  the secondary sits where the prologue's skip pill is. A click or tap picks a pill; the continue control (N, the pad's
  VEGA button, touch NEXT, the action list) picks the primary when let go and the **secondary when held 0.8 s**
  (`VegaChoiceInput.HoldSeconds`) — no stock pad button is free, and the cursor is captured on foot. Esc never answers.
  An entry the client cannot read leaves its place empty (a secondary never moves up onto the continue control).

### The deep dives "Research" and "Flying" (C2–C5, #2479)

Optional chapters after the basic training (synthesis §3.2/§3.4): a player starts them from the Journal (**Start**, or
**Later** to put an offer off — it stays listed and startable, VEGA never offers it again). VEGA offers a course once, at
its moment of need, with `vega.chapter.offer` ("New short course: {0}…"; since #2499 a client with buttons hears
`vega.chapter.offer_choice` with [Later] / [Start now] in the panel, see above) — never while the basic training runs, at most one
offer every 90 s of play, and (#2481) never a course the player has nothing left to do in: one whose every counting step is
done or covered by its evidence (`alreadyDoneIf`, a bridged mission turned in) is neither offered nor suggested — the
Journal reads it "done — nothing left to practise" without a **Start** (#2500, below). **The tip becomes the offer** (#2459, synthesis §4.3): a VEGA hint
or tip that a course teaches (one of its steps names it in `hintRefs`) is not spoken when that course's offer is due at the
same moment (`OfferAbsorbsHint`) or was made a moment ago (`RecentOfferTeaches`, 10 s of play) — the offer line is its
telling: a once-hint's flag is burned, a context tip's first telling booked and its cooldown started, and the offer takes
the tips' cadence slot. So the data cache, factory, settlement, animal, ruin, treasure and wreck tips, the tool-tier wall,
the bio lab and the comm radio's Crystal Net line no longer come right before or after "New short course: …". Safety
tips are never folded into an offer; a player with no offer due (the course done, put off or running) hears the tip as
before. Every **do** step of a course counts (tier `core`/`opt` only marks what the synthesis calls
optional); read and react steps never block. A **replay** walks every step anew and ignores `alreadyDoneIf`, except a
research step whose every blueprint is known already (it cannot be done twice — #2459, `StepSpentForReplay`): it passes
silently, like a bridged mission already turned in.

**Spent courses — "nothing left to practise" (#2500).** `ChapterSpentFor` (GameServerStoryPacks.cs) says a chapter has
nothing left: a finished one (done or skipped, not replaying) whose every counting step a replay passes at once
(`StepSpentForReplay` or `BridgedMissionTurnedIn` — the Crystal workshop with every job handed in), or a course not started
yet that is through by evidence (`ChapterThroughByEvidence`). Never a running or replaying chapter, a mandatory one still
ahead, or one without counting steps. The Journal gets it as the additive `NetStoryChapter.NothingLeft`: no Replay / Start /
Later (`JournalPackPage.ChapterActions`), the row reads `ui.journal.nothing_left` ("done — nothing left to practise") with
the ✔ (`ChapterIcon`), and such an offer asks for no attention (no • badge). The server half covers older Journals and a
click that raced the flag (`AnswerNothingLeft`, GameServerStoryPackActions.cs): a `startChapter`, `replayChapter` or
`restartChapter` of a spent chapter does not start it; VEGA says `vega.chapter.nothing_left` ("You've already done
everything in {0}.", {0} = the title; transient — not kept in VEGA's notes), and a course not started yet is stamped done
silently first (its counting steps `CompleteStep(silent)`, the chapter `CompleteChapter(silent)` — no card, no step line, no
graduation). A client that announces `choices1` (this build on) gets the key; an older one has no text for it and gets the
line composed in its language (`ComposeLegacyText`). Where the start would be refused anyway (the story switched off, not
admitted, ruled out by the world, an older client's course) nothing is said. The admin `/tutorial chapter` keeps its old
path.

Each course opens with the card `tut.course` ("Short course" + its title) and
closes with the part banner `tut.done`; two firsts get their own card (below). No legacy keys, no rewards; an older client
(no `packs1`) never runs a course.

| Course | Needs (`requires`) | Offered (`offerWhen`) |
|---|---|---|
| **C2 Knowledge and research** (part Research) | `chapter:tutorial:b5` | `any:[world:data_cache_near, world:scanner_unknown]` — the tips' moment |
| **C3 Pilot** (Flying) | `chapter:tutorial:b6`, `world:free_flight` | suggested at the graduation and on a skip (#2498) |
| **C4 Stations, trade and jobs** (Flying) | `chapter:tutorial:b6` | suggested at the graduation and on a skip (#2498) |
| **C5 Navigator** (Flying) | `chapter:tutorial:c3` (the pilot course first) | `any:[chapter:tutorial:c3, world:jump_ready]` — right after C3 (and the gap) |

Without free flight C3 and C5 are locked (not suggested, not startable); C4 then runs at a settlement. The forge C5 needs
for the lubricant is its own step (no blueprint needed), not a chapter prerequisite — evidence (`world:forge_ready`) skips it.

| Step | Teaches | Event (arg × required) | Kind | World rules / feasibility, absorbed hints |
|---|---|---|---|---|
| c2.1 | plants, animals, rocks teach different things | `scan:any` `flora\|tree\|creature\|block` × 3 | do | — |
| c2.2 | data caches deep in the rock hold 2 fragments; marker `nearest_ore:data_cache` | `mine:any` `data_cache` | do | absorbs `data_cache_near` |
| c2.3 | what a blueprint costs (the Blueprints tab) | `research:any` | do | `feasibleIf not:world:all_blueprints`; absorbs `blueprint_affordable` |
| c2.4 | DataQube games teach knowledge | `minigame:played` | do | `feasibleIf world:arcade_ready` |
| c2.5 | rune stones tell old stories | `scan:any` `monument` | do | `feasibleIf world:monument_unread` |
| c2.6 | the terrain scanner | `gadget:used` `terrain_scanner` | do | `feasibleIf any:[blueprint:terrain_scanner, chapter:tutorial:c1]` (#2480: the workshop course through — the step then runs with the variant line "research it, craft it, use it"), else **c2.6a** (read: "its blueprint needs titanium plates — a forge makes them"); absorbs `scanner_idle`, `scanner_unknown` |
| c2.7 | the Achievements tab | `ui:progress_open` | do | — |
| c3.1 | waypoints on the chart `{key:FlightMap}`, the autopilot `{key:FlightAutopilot}` | `ui:waypoint_set` | do | — |
| c3.2 | scan a world before landing (its card) | `planet:scanned` | do | — |
| c3.3 | the ship laser breaks asteroids, the tractor beam | `asteroid:destroyed` | do | `feasibleIf world:asteroid_mining`; absorbs `asteroid_near` |
| c3.4 | walk inside the flying ship `{key:FlightEnterInterior}` | `interior:entered` | do | — |
| c3.5 | a spacewalk through the hatch | `eva:started` | do | — |
| c3.6 | target lock `{key:FlightTargetNext}` | — | read | `feasibleIf world:space_enemies` |
| c3.7 | floating above the air | `player:zero_g` | react | absorbs `zero_g` |
| c4.1 | dock at a station; marker `nearest_station` | `station:boarded` | do | `feasibleIf any:[docked, free_flight ∧ station_in_system]`, `alreadyDoneIf world:docked`; else **c4.1a** (read: settlements trade and give jobs too); `onComplete` `tut.first_dock` |
| c4.2 | vendors have trades; researchers sell fragments | `trade:barter` | do | a station or a settlement in reach; absorbs `trader_near` |
| c4.3 | the mission board; survey jobs pay knowledge | `mission:board_accepted` | do | same; `alreadyDoneIf world:job_active` |
| c4.4 | undock `{key:Disembark}` | `station:left` | do | `feasibleIf world:docked` |
| c4.5 | hand a job in where it was taken | `mission:turned_in` | do | `feasibleIf world:job_active` |
| c5.1 | the planet map `{key:PlanetMap}`, markers, pings `{key:PingMarker}` | `marker:set` | do | — |
| c5.2 | systems; map travel | `travel:landed` `quick` | do | `feasibleIf any:[free_flight, instant_travel]`; `instant_travel` variant |
| c5.3 | a forge (no blueprint) | `craft:any` `forge` | do | `alreadyDoneIf world:forge_ready` |
| c5.4 | lubricant at the forge | `craft:any` `refine_oil_lubricant\|refine_bio_lubricant` | do | — |
| c5.5 | research the Radar Array | `research:any` `radar_array` | do | `alreadyDoneIf blueprint:radar_array` |
| c5.6 | research the Jump Generator | `research:any` `jump_generator` | do | `alreadyDoneIf blueprint:jump_generator` |
| c5.7 | fit the jump generator module | `module:built` `jump_generator` | do | `alreadyDoneIf world:jump_ready` |
| c5.8 | hyperjump via the chart's Hyperspace tab | `hyperjump:any` | do | absorbs `jump_ready`; `onComplete` `tut.first_jump` |
| c5.9 | wormholes are shortcuts | `wormhole:any` | do | `feasibleIf world:wormhole_known` |
| c5.10 | wrecks and debris fields | `salvage:found` | do | `feasibleIf any:[space_wrecks, debris_fields]`; absorbs `wreck_signal`, `debris_signal` |

Not taught here: C4.4 of the synthesis (a settlement's job chain, `chain_needs_1`) needs talking to people first — it belongs
to C11 Neighbours (step 21) and can be added there by data. The client-only lessons `planet_card`, `target_lock` and
`wormhole` stay client lessons (a course runs after the training, `ShipAiLine.TutorialActive` is off then).

### The deep dives "Ship building" and "Base building" (C1, C6, C7, C17, #2480)

The same rules as C2–C5 (above): optional, started from the Journal or at VEGA's one offer line, every **do** step counts,
the course card `tut.course` and the part banner `tut.done`, no legacy keys, no rewards, at most two cards per course.

| Course | Needs (`requires`) | Offered (`offerWhen`) |
|---|---|---|
| **C1 Workshop and titanium** (part Ship building) | `chapter:tutorial:b4` | `any:[world:tier_gate, all:[step:tutorial:c6.3, not:blueprint:ship_builder]]` — a swing the tool tier refused (the synthesis' `NoteTierGate` moment), or the ship builder course waiting for titanium; suggested at the graduation (third place — a skip leaves it to this moment, #2498) |
| **C6 Ship builder** (Ship building) | `chapter:tutorial:b5` | `any:[world:module_affordable, world:hull_damaged]` |
| **C7 Homestead** (Base building) | `chapter:tutorial:b4` | `any:[world:base_core_ready, all:[world:night_outside, not:world:base_here]]` — a base core crafted or carried, or a night out on foot with no base on that world (the synthesis' "two nights outside": the training spans several game days, so the first night after it stands in) |
| **C17 Station builder** (Ship building) | `chapter:tutorial:c3`, `world:free_flight` (the pilot course's spacewalk first) | `any:[world:station_core_ready, chapter:tutorial:c6]` |

**Hard prerequisites** (synthesis §3.2): titanium plates for C6 from the blueprint on — a player without the workshop course
(and without the Shipwright blueprint) hears c6.4 ("take the short course Workshop and titanium first") and the course waits
at c6.5 (`requires any:[chapter:tutorial:c1, blueprint:ship_builder]`) until C1 is through (or skipped); the chapter stays
active in the Journal meanwhile. C17 needs C3 (its spacewalk) and free flight; without them it is locked ("needs: Pilot").
**E7:** the titanium route is this course, not the basic training, and the forge stays blueprint-free. **E32:** a base an
ally or a crewmate owns on the player's world counts as theirs (`world:base_here`, the `base:built` zone test, the sealed
room and the wall ring all accept own or allied bases — `AreAllied` answers both alliances and crews).

| Step | Teaches | Event (arg × required) | Kind | World rules / feasibility, evidence, absorbed hints |
|---|---|---|---|---|
| c1.1 | titanium ore is too hard for the drill, and the Titanium Drill needs titanium plates itself — the way round | — | read | `creative` → "crafting is free here, but this is still the way"; absorbs `tier_gate` |
| c1.2 | the ship's trade console swaps 5 iron ore for 1 titanium ore (iron is the price; marker `nearest_ore:iron_ore`) | `craft:any` `market_iron_to_titanium` × 2 | do | — |
| c1.3 | a forge (6 stone, 4 iron plates, no blueprint) | `craft:any` `forge` | do | `alreadyDoneIf world:forge_ready` |
| c1.4 | placed on a world, the forge smelts 2 titanium ore into a plate | `craft:any` `titanium_plate` | do | — |
| c1.5 | titanium plates open bigger blueprints | — | read | — |
| c6.1 | ship modules at the workshop, one of each kind, half back when taken out | `module:built` | do | — |
| c6.2 | repair the hull at the console `{key:RepairWreck}`, 10 hull points per iron plate | `ship:repaired` | do | `feasibleIf world:hull_damaged`, else **c6.2a** (read: how it works); absorbs `hull_low` |
| c6.3 | build onto the parked ship, furnish the cabin | `ship:edited` | do | — |
| c6.4 | an own ship needs the Shipwright blueprint, and that needs titanium plates | — | read | `feasibleIf not:any:[chapter:tutorial:c1, blueprint:ship_builder]` (only while the titanium route is unknown) |
| c6.5 | research the Shipwright blueprint | `research:any` `ship_builder` | do | `requires any:[chapter:tutorial:c1, blueprint:ship_builder]`; `alreadyDoneIf blueprint:ship_builder` |
| c6.6 | craft and lay a ship keel | `ship:keel_placed` | do | `alreadyDoneIf world:keel_laid` |
| c6.7 | hull, helm, engine, door, sealed — commission at the helm `{key:Interact}` | `ship:commissioned` | do | `alreadyDoneIf world:ship_built`; `onComplete` **tut.ship_commissioned** |
| c6.8 | the fleet: switch ships in the Ship tab | `ship:switched` | do | — |
| c7.1 | a base core (6 stone, 2 iron plates, no blueprint) claims a home | `base:founded` | do | `alreadyDoneIf world:base_here` (an ally's base counts, E32); `onComplete` **tut.first_base** |
| c7.2 | the base zone: build freely, the air stays good, animals keep away | `base:built` × 10 | do | absorbs `base_walls`, `base_airless` — VEGA's two founding hints fire right after the founding, when this step is already active, so its line (with the base card) is the first word on walls and air (G12) |
| c7.3 | lights: torches, lanterns, lamps, a campfire | `base:built` `torch\|lantern\|campfire\|light_white\|light_red\|light_green\|strip_light_cyan\|strip_light_warm` × 2 | do | — |
| c7.4 | a wooden door, opened with `{key:Interact}` | `door:opened` | do | — |
| c7.5 | a sealed room holds air (stone, metal, glass; only an energy door keeps it in) | `base:sealed` | do | `feasibleIf world:airless, world:oxygen`, else **c7.5a** (read, `feasibleIf world:oxygen`); with no oxygen rule neither runs; absorbs `base_airless` |
| c7.6 | a closed wall ring, 2 high, keeps animals out further | `base:walled` | do | absorbs `base_walls` |
| c7.7 | a bed (or a heal tank) as the wake-up spot | `spawn:set` | do | — |
| c7.8 | residents and visitors | — | read | `feasibleIf world:base_visitors` |
| c17.1 | a station core, deployed on a spacewalk `{key:EvaDeployStation}` | `station:core_deployed` | do | `alreadyDoneIf world:station_owned` |
| c17.2 | a hull of a dozen blocks and a door as the airlock commission it | `station:commissioned` | do | `alreadyDoneIf world:station_owned` |
| c17.3 | zero-g building aboard `{key:ToggleStationZeroG}` | `station:zero_g` `on` | do | — |

`base:sealed` and `base:walled` are state checks: the 1 Hz `TutorialStateEvents` pass runs them only while one of the
player's own active steps waits for them (`StepWaitsFor`), on the player's own world — the air model (`RefreshBaseAir`, a
supplied sealed room of a base the player owns or shares) and the wall model (`RefreshBaseWalls`: the player's feet in a
fenced-in part of such a base) are the ones the game already runs, with their own caches. The wall model's one outside-in
fill answers every yard, courtyard **and room**, so on its own it also counted the room just sealed in c7.5 (c7.6 then
completed in the same pass) and a closed cave near the base; since #2488 the lesson's check (`StandsInOwnOrAlliedYard`, not
the animals' spawn rule) also wants **open sky** over the feet — nothing solid in the 32 loaded cells above the head
(`OpenSkyAbove`, a taller `Sheltered` scan; an unloaded cell counts as open). The c7.6 hint says so ("stand under the open
sky inside the ring").

Left out of the synthesis' tables (the issue's scope, and every do step of a course counts): C1.4–C1.6 (crates, taking things
apart, a workbench — the workbench needs steel and bronze) and C1.7's "upgrades use up the old tool" (not true for the
drills: the Titanium Drill's recipe takes no drill). They can be added by data later.

### The deep dives "Crystal net" (C8, C14, #2481)

The same rules as above (optional, the course card and the part banner, every do step counts, no legacy keys). C8 is a
**bridge**, not a copy (synthesis §3.7, d3 D9): VEGA's six radio jobs `crystal_workshop_1..6` stay in `data/missions.json` —
the Missions tab offers them (anywhere, once their blueprint is researched), the player takes them and hands them in there,
and each pays its items and knowledge as before. The course's steps c8.1–c8.6 are mission bridges onto them: a step completes
when its job is **turned in**. A job handed in before the course completes its step silently, so a chain the player already
runs is adopted where it stands (no line, no banner for what was done before), and the chain is never offered twice: the jobs
exist once, and VEGA never offers the course to a player who did every job already (the offer skips a course that is through
by evidence). A player who declines the course or plays without the tutorial keeps the chain exactly as it was — with VEGA's
follow-up after each job (below).

| Course | Needs (`requires`) | Offered (`offerWhen`) |
|---|---|---|
| **C8 Crystal workshop** (part Crystal net) | `chapter:tutorial:b5`, `blueprint:comm_radio` (the Journal says "needs: Comm Radio") | `blueprint:comm_radio` — the comm radio researched opens the Crystal Net tab (synthesis §3.2) |
| **C14 Factory and automation** (Crystal net) | `chapter:tutorial:b4` | `any:[world:factory_near, blueprint:fabricator]` — VEGA's `factory_near` tip moment, or the fabricator blueprint researched |

**Hard prerequisites** (synthesis §3.2): C14's machine lesson needs a machine circuit (C8.5 — the workshop's fifth job, or such
a circuit built before: `world:machine_circuit`) and a forge's goods (C1 — or a forge already at hand: `world:forge_ready`;
the fabricator needs lubricant and a workbench). Until then c14.4 says so and the course waits at c14.5, active in the Journal.

| Step | Teaches | Event (arg × required) | Kind | World rules / feasibility, evidence, absorbed hints |
|---|---|---|---|---|
| c8.0 | crystal wires carry one signal; research the Crystal Net (the conduit) | `research:any` `crystal_conduit` | do | `alreadyDoneIf blueprint:crystal_conduit`; absorbs `crystal_researched` |
| c8.1 | job 1 "First Light": a switch, conduits, a lamp `{key:Interact}` | mission bridge `crystal_workshop_1` | do | absorbs `crystal_first`; `onComplete` **tut.first_circuit** |
| c8.2 | job 2 "Doorbell": a step plate rings a chime | mission bridge `crystal_workshop_2` | do | variant `not:blueprint:crystal_sound` → "research it first" (start line and chip) |
| c8.3 | job 3 "Night Light": a daylight sensor switches a lamp | mission bridge `crystal_workshop_3` | do | variant without `crystal_sensors` |
| c8.4 | job 4 "Airlock": a NOT logic block opens a door; the door's lamp | mission bridge `crystal_workshop_4` | do | variant without `crystal_logic`; absorbs `crystal_door` |
| c8.5 | job 5 "Little Factory": a signal starts a machine | mission bridge `crystal_workshop_5` | do | variant without `fabricator` |
| c8.6 | job 6 "Secret Passage": a phase-block wall opens on a signal | mission bridge `crystal_workshop_6` | do | variant without `crystal_mechanics`; absorbs `crystal_mechanics`, `crystal_phase` |
| c14.1 | a factory's terminal crafts in bulk (cheap raw ore in) | `craft:station` `factory` | do | `feasibleIf world:factory_here`, else **c14.1a** (read: what a factory is good for); absorbs `factory_near` |
| c14.2 | an SPS code claims a factory `{key:Interact}`: your base, rebuildable | — | read | `feasibleIf not:world:factory_owned` |
| c14.3 | "this factory is yours now" | `factory:claimed` | react | `requires not:world:factory_owned` — claiming spends a rare code, so the course never asks for it |
| c14.4 | the assembly line needs a machine circuit and a forge's goods | — | read | spoken only while one of them is missing |
| c14.5 | a fabricator crafts by itself, wired to a switch | `crystal:craft` × 2 | do | `requires` the two prerequisites |
| c14.6 | machines catch up while you are away | — | read | variant `not:world:machine_catchup` ("machines rest on this world") |

Left out: the synthesis' C8.0a ("mine a crystal" — the research step's hint names where crystals come from) and C8.7 ("your
first powered net" — job 1 is that net). The step lines name each job and what it needs; the jobs' own descriptions in the
Missions tab keep the full recipe.

**VEGA's follow-up (B55).** The six jobs carry `"giverRole": "vega"` (`MissionChains.GiverVega`, radio surface only — the
content validator refuses another surface): accepting one builds no NPC relationship, and after a turn-in VEGA herself says in
her panel what comes next — `vega.chain.next` ("Well done! My next job for you waits in your Missions tab: “{0}”.") or
`vega.chain.next_research` when its blueprint is still missing ({1}) — at once, without a radio item, whatever the NPC-calls
preference, never muted with the hints (a mission line, kind 0); an older client gets it composed in its language. Before,
a coined quartermaster called on the radio 90 s later (only with a radio worn, and lost with "missions only" when deferred).
While the course runs, the next step's own start line is the follow-up, so VEGA's line stays quiet (one voice, G12). Other
chains keep their givers' radio nudge.

### The deep dives "More to discover" (C9–C13, C15, C16, C18, #2482)

The same rules as above (optional, the course card `tut.course` and the part banner `tut.done`, every do step counts, no legacy
keys, no rewards, at most two cards per course). These courses have no hard prerequisite beyond a part of the basic training;
each is offered once at its moment of need (synthesis §3.2) and is startable from the Journal any time after. Four of them
can be ruled out by the world (`visibleIf`, synthesis §3.5 "ausblenden"): such a course is not listed, offered, suggested or
startable at all — on a peaceful or family world the Journal simply has no Defence course.

| Course | Needs (`requires`) | Exists (`visibleIf`) | Offered (`offerWhen`) |
|---|---|---|---|
| **C9 Animal friends** | `chapter:tutorial:b5` | `world:creatures` | `any:[world:tameable_near, world:herd_begging]` — VEGA's tameable tip (a translator in hand) or a herd coming to beg |
| **C10 Gardener and lab** | `chapter:tutorial:b4` | — | `any:[world:seed_carried, blueprint:bio_lab]` — a seed or a sapling carried, or the bio lab researched |
| **C11 Neighbours** | `chapter:tutorial:b1` | `world:settlements` | `world:settlement_near` — VEGA's settlement tip (close by, not yet inside) |
| **C12 Defence** | `chapter:tutorial:b3` | `any:[world:enemies, world:bandits, all:[world:space_enemies, world:free_flight]]` | `any:[world:bandit_hail, world:hostile_near]` — a robber's hold-up, or something hostile close by |
| **C13 On the move** | `chapter:tutorial:b1` | — | `any:[world:climbing, blueprint:climbing_gloves, blueprint:glider]` — the first climb after the training, or the gear researched |
| **C15 Together** | `chapter:tutorial:b1` | `world:multiplayer` | `world:player_near` — another (visible) player close by; an invisible observer never counts (#2460 N2) |
| **C16 Creative tools** | `chapter:tutorial:b4` | — | `any:[blueprint:paint_tool, blueprint:shape_tool]` |
| **C18 Explorer** | `chapter:tutorial:b3` | — | `any:[world:ruin_near, world:treasure_near, world:wreck_signal]` — VEGA's ruin, treasure or wreck tip |

**E9 (Defence):** strictly opt-in — never part of the basic training, never a graduation suggestion, offered only at its own
moment, and only on a world whose rules have something hostile; worded "fend off / chase away" ("abwehren / verjagen" — a test
keeps cruel words out); "walking away" is named as the other choice in the step's own line. **C15** never names anybody: no
line of the course has a slot, and the offer line carries the course title only.

| Step | Teaches | Event (arg × required) | Kind | World rules / feasibility, evidence, absorbed hints |
|---|---|---|---|---|
| c9.1 | most animals are calm; the readout says how one behaves and what it loves, "Aggressive" = keep your distance | `scan:performed` `creature` | do | variant `not:world:enemies` ("the animals on this world are all peaceful") |
| c9.2 | begging herds: hold food, wait, `{key:FeedCreature}` throws a piece | `creature:fed` | do | absorbs `feed_herd` |
| c9.3 | taming: its favourite food a few times, or the creature translator | `tame:any` | do | `alreadyDoneIf world:companion_owned`; absorbs `favourite_food`, `tameable_near`; `onComplete` **tut.first_tame** |
| c9.4 | the companion: a name in the Creatures tab, or food — a strong bond makes it fetch | `companion:cared` (`named`\|`fed`) | do | `feasibleIf world:companion_owned` |
| c10.1 | seeds and saplings are hand recipes; plant one on soil `{key:Place}` (pot or hydro tray in a base) | `plant:any` | do | absorbs `plant_refused` |
| c10.2 | a campfire is a little kitchen (a hot meal fills you up better; torches too) | `craft:station` `campfire` | do | variant `not:world:hunger` ("a little workshop": torches, water, salt) |
| c10.3 | the suit's sample case fills by itself when you harvest | `sample:taken` | do | absorbs `first_sample` (`sample_case_full` stays VEGA's — it comes when it happens) |
| c10.4 | research the bio lab at the cockpit | `research:any` `bio_lab` | do | `alreadyDoneIf blueprint:bio_lab` |
| c10.5 | build and place the lab (or its ship module), analyse a sample; done line: mix next | `biolab:used` | do | absorbs `bio_lab_unlocked`, `first_analysis` |
| c11.1 | people live in settlements: walk up, `{key:Interact}` (#1869); marker `nearest_settlement` | `npc:talked` | do | absorbs `settlement_near` |
| c11.2 | answers matter: stranger → acquaintance → friend | `npc:answered` | do | — |
| c11.3 | friends tell you things and call you; the Character tab's "People you know" | `npc:people` | do | — |
| c11.4 | the doctor heals, the sage sells knowledge, traders swap, the board has jobs | — | read | — |
| c12.1 | robbers: pay and they leave, or say no and chase them away; camps for a reward | — | read | `feasibleIf world:bandits` |
| c12.2 | your choice: walk away, or fend off what attacks you `{key:PrimaryFire}`; a tool beats bare hands; hint: armour, stasis, fainting is gentle | `defeat:any` `creature\|giant\|machine\|bandit` | do | `feasibleIf any:[world:enemies, world:bandits]` |
| c12.3 | hostile ships shoot from afar: fly close, shields up, chase one away | `defeat:any` `space_machine\|raider` | do | `feasibleIf world:space_enemies, world:free_flight` |
| c13.1 | climb almost any wall: jump at it, push, hold `{key:Jump}`; gloves hold longer | `move:climbed` × 3 (metres) | do | — |
| c13.2 | the energy rope gun; done line: reel in `{key:Place}`, hang, `{key:Crouch}` drops it | `gadget:used` `energy_rope_gun` | do | `feasibleIf blueprint:energy_rope_gun`, else **c13.2a** (read: what it will do); absorbs `rope_gun` (its how-to is the done line) |
| c13.3 | jetpack (hold `{key:Jump}`) or glider (opens while you fall) | `move:airborne` | do | `feasibleIf any:[blueprint:jetpack, blueprint:glider]` (no stand-in: c13.2a and c13.6 name the gear) |
| c13.4 | a boat needs no blueprint (workshop), board with `{key:Interact}`; a speeder the same over land; `{key:StowVehicle}` | `vehicle:boarded` `boat\|speeder` | do | — |
| c13.5 | a monorail runs here: a stop, the cab `{key:Interact}` | `vehicle:boarded` `train` | do | `feasibleIf world:train_here`, else **c13.5a** (read: build one, towns may have trains) |
| c13.6 | beam blocks link your places, lifts carry you up and down | — | read | — |
| c15.1 | chat `{key:OpenChat}`; the radio's reach: world, system, galaxy | `chat:sent` | do | variant `not:world:radio_worn` (research, craft and wear a radio first); absorbs `player_near` |
| c15.2 | trading face to face `{key:RequestTrade}` | `trade:player` | do | — |
| c15.3 | allies share bases and stations and never hurt each other; a crew | `ally:joined` | do | — |
| c15.4 | a ping `{key:PingMarker}` — allies and crew on the world see it | `marker:set` `ping` | do | — |
| c15.5 | be kind; mute someone unkind, `/report` | — | read | — |
| c16.1 | the paint tool paints a picture on a placed block `{key:Place}` | `paint:block` | do | — |
| c16.2 | forms from the slot actions `{key:HotbarAction}` (Form); the shaping tool designs your own | `craft:shape` | do | — |
| c16.3 | colours from the same actions (dye free, glow a crystal per block) | `craft:tint` | do | — |
| c16.4 | the blueprint tool copies a build and pastes it block by block | `build:any` `blueprint_paste` | do | — |
| c16.5 | your look (Character tab) and photos (Photos tab) | — | read | — |
| c18.1 | chests and caches `{key:LootContainer}` | `loot:any` | do | absorbs `treasure_near` |
| c18.2 | buried ruins and crashed wrecks hold old tech — walk up to one | `poi:reached` | do | `feasibleIf any:[world:vaults, world:planet_wrecks]`, else **c18.2a** (read: where else chests hide); absorbs `poi`, `ruin_near` |
| c18.3 | settlers' tips mark hidden chests; data cubes hold games `{key:Interact}` | — | read | — |
| c18.4 | wrecks in space: follow the signal, salvage inside | `salvage:found` | do | `feasibleIf world:free_flight, any:[world:space_wrecks, world:debris_fields]`; absorbs `wreck_signal`, `wreck_signal_manual`, `debris_signal` |

`move:climbed` is measured like walking: only while a step listens, only rising in the climbing pose, never a report more than
4 blocks from the last one. `poi:reached` is the 1 Hz advisor check behind VEGA's `poi` hint (a vault entrance or the crashed
wreck within 48 blocks), raised right before it. Left out (the issue's scope; every do step of a course counts): the
synthesis' C16.5 as a do step (the client sends its colours at every join, so "changed the look" cannot be told apart — a
read now), C12.2 armour as a do step (now the fend-off step's hint), C12.4 camp bounties (a sentence in the robbers' line),
C18.3 "follow a revealed marker" (a read: chests a settler reveals are found and looted like any other).

### Cards (#2473)

The parts' cards live in `data/stories/tutorial/cutscenes.json` (format, tiers and pacing: [CUTSCENES.md](CUTSCENES.md#the-tutorial-cards-2473)).
Cards, not cinema: no card takes the camera, holds the world or freezes the controls, and a card shows titles only.

| Binding | Scene | When the player sees it |
|---|---|---|
| `b1.openCutscene` | `tut.welcome` | part 1 starts — right after VEGA's prologue (its `chainNext` names `tutorial/tut.welcome`), or on its own without the story; "Basic training · six short parts", then "Part 1 of 6 · First steps". A migrated save that starts at a later part gets that part's card instead; a veteran the pack completes for silently gets nothing |
| `b2`…`b6.openCutscene` | `tut.strip` (template) | a part starts: "Basic training · Part n of 6" + the part's title — after the 90 s cooldown, and dropped if the part is over by then |
| every part's `closeCutscene` | `tut.done` (template, banner) | a part the player was shown is done (not when skipped, replayed or completed silently): "Part complete!" + the part's title |
| `b6.1.onComplete` | `tut.first_launch` | the first launch: a letterbox over the launch rise, "now or never" |
| `b6.7.onComplete` | `tut.first_landing` | the first landing on a new world: the camera circles the ship, the world's name as the title (not for the stand-in `b6.7a`) |
| `graduation.cutscene` | `tut.graduation` | the graduation: "★ Achievement · Graduate" and VEGA's closing line |
| every deep dive's `openCutscene` (#2479) | `tut.course` (template) | a course the player started: "Short course" + its title |
| `c4.1.onComplete` (#2479) | `tut.first_dock` | the stations course's docking: "Docked" + the station's name, then what waits inside — after the station's loading veil (not "now or never") |
| `c5.8.onComplete` (#2479) | `tut.first_jump` | the navigator course's first hyperjump: "New system" + its name on the warp-in, then "Unknown stars show “?” …" — now or never |
| `c7.1.onComplete` (#2480) | `tut.first_base` | the homestead course's base founded: a short scene (T3) — the camera rises over the player at the new core, "Base founded · Your home", then "Walls keep animals away — and on airless worlds, sealed rooms hold air"; a singleplayer world holds; not for a base that counted as evidence |
| `c6.7.onComplete` (#2480) | `tut.ship_commissioned` | the ship builder course's own ship commissioned: the camera circles the **new** ship (scaled to its size), "Commissioned · Your own ship", then "Built by you, block by block — and it flies"; a singleplayer world holds; not for a ship built long ago |
| `c9.3.onComplete` (#2482) | `tut.first_tame` | the animal friends course's first tame (its favourite food or the translator): a short banner (T1, 4 s) "Tamed · A new friend!" with a chime — the camera and the controls stay the player's; not for a companion the player had before the course, never in a replay |
| `c8.1.onComplete` (#2481) | `tut.first_circuit` | the crystal workshop course's first job handed in (a switch lit a lamp): a short banner (T1, 4 s) "First circuit · It works!" with a chime — the camera and the controls stay the player's; it waits only while a menu is open (the hand-in happens in the Missions tab); not for a job handed in before the course |

A course shows at most two cards: its course card and at most one first (a test counts them; a banner like
`tut.first_circuit` or `tut.first_tame` is no card). The basic training's budget
(eight heavier moments) is untouched: the docking card lives in the stations course, not in b6.4.

Each scene plays once per player (`story:tutorial:cut:<id>`, a template once per part, `tut.strip@b3`): "I know the game",
a restart and a replayed part show no card again, and a client without cutscenes gets the lines only (the cards count as
seen at once).

The teaching verbs above (`move:walked` … `visit:world`) are listed with their raise sites in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) ("Game events"); each is raised next to the existing hook of its handler
and measures nothing unless a step listens (walk, jump, flight distance and the 1 Hz walk-back and fragment checks).

### UI teaching flags (tag 298)

`UiTutorialEvent { Id }` (client → server) reports a UI moment a step teaches: `vega_continue` (the VEGA panel's last page
of a line read through), `menu_open` (the Tab menu opened), `codex_open` (`GameMenu.OpenWiki`), `map_open` (the Map tab or
the flight chart), `waypoint_set` (a click on the flight chart), `progress_open` (#2479: the Achievements tab — `GameMenu`'s
tab switch) — the list is `GameEventKeys.UiEvents`. The server raises
`ui:<id>` on its event bus, where only an active step waiting for exactly that flag listens. A flag is **teaching only**:
it never pays, never unlocks, never counts for the story, is never stored and never appears in a condition. Rate-limited
(the client sends the same flag at most every 0.5 s, the server accepts it at most every 0.25 s); passes a held
singleplayer world (`PausedMayHandle` — the pause menu's Codex, VEGA's continue during a hold). An older server drops the
tag.

### Gates

The tutorial says when the global advisor systems open (`TutorialGate(session, gate)`; "tutorial off" — world rule `Off`,
`OptIn` not started, or the player opted out — opens every gate; no tutorial pack ⇒ the reader's own default):

| Gate | Condition | Reader |
|---|---|---|
| `opportunityTips` | `all:[step:tutorial:b2.2, step:tutorial:b3.1, step:tutorial:b4.1]` — eating, mining and crafting done or skipped (the moment the old "scan" stage marked) | `OpportunityTipsOpen` → the `AfterScanStage` context tips and the progression sweep (`craftable_now`, `blueprint_affordable`) |
| `banter` | `pack:tutorial:done` — the basic training done or skipped | `TickVegaBanterFor` (VEGA's LLM smalltalk) |
| `stationNear` | `not:step:tutorial:b6.4` — while the docking step is open | the `station_near` tip |

**Held hints (`gates.holdHints`, #2549).** Some one-time VEGA hints are about things the training only gets to later — the
first crystal mined spoke of the comm radio and the Crystal Net while a player was still learning to walk back to the
ship. `holdHints` maps a hint id (the `<id>` of `vega:hint:<id>`, a once-hint or a context tip) to the condition that
**releases** it. While that condition fails for a player whose basic training runs, VEGA keeps the hint back **without
spending it** — the once-flag stays unset (a context tip's telling is not counted either), so the hint comes at its next
occasion after the release: the next crystal, the next second near a ruin, the next refused swing. Readers:
`ShipAiHintOnce` (the once-hints) and the context-tip pass (`TickVegaContextTips`) — `TutorialHoldsHint` in
`GameServerTutorial.cs`. The tool-tier tip's arming (the refused swing) is dropped while it is held, as for a muted
panel: that tip is only worth hearing at the wall. Unlike the gates above, a hold applies only while the tutorial pack
**runs** for the player — never with the world rule `Off`, `OptIn` not started, the player switched off, the pack done
(graduated, or skipped — a skip also marks every mandatory chapter skipped, which `chapter:` counts), a `Courses` world
(skipped at the start), or an older client (no `packs1`: its nine legacy lessons never mark the chapters these conditions
ask for). Those players keep today's behaviour. Unlike `hintRefs` (a step that teaches the hint itself and books it as
told when its line is shown), a held hint is simply postponed.

| Hint | Released by | Why |
|---|---|---|
| `crystal_mined` | `chapter:tutorial:b5` | the Crystal Net, blueprints and the comm radio — after scanning and research (B5) |
| `poi` | `chapter:tutorial:b5` | ruins and wrecks hold old tech — after B5 (the explorer course c18 teaches them) |
| `tier_gate` | `chapter:tutorial:b2` | rock too hard for the drill — once B3 runs, where b3.3 explains it at the next wall |

### One teaching voice (G12)

Steps name the hints and tips they teach in `hintRefs` (table above). Client-only lessons (E11): while the tutorial runs
for the player (`ShipAiLine.TutorialActive`), the flight lessons the B6 steps teach — `chart_waypoint`, `ship_scanner`,
`ship_scan_hold`, `anomaly_scan` — are not spoken and count as given (`VegaText.LessonCoveredByTutorial`): the step lines
carry them per player on the server, so a shared school PC no longer decides who heard them. The deep dives cover the
other client lessons the same way (#2459): the course step that teaches one names it in its `hintRefs` — `c3.1`
`chart_waypoint`, `c3.2` `planet_card` + `ship_scanner` + `ship_scan_hold`, `c3.6` `target_lock`, `c5.9` `wormhole`,
`c10.5` `bio_lab_tabs`, `c13.1` `climb` — and while that course runs (or is replayed) with the step active or still ahead,
the client keeps the lesson quiet and counts it as given (`VegaText.LessonCoveredByStoryStep`, reading its copy of the pack
and the server's `StoryPackStates`); an offered, finished or put-off course covers nothing. Only the pad/touch context list
(B1.7) stays a plain client lesson. The server books such an id as told when the step speaks (a harmless
`vega:hint:<id>`; the tips log then lists the lesson). A Client.Tests guard fails when a new `SayLocal("vega.hint.…")`
lesson is taught by no tutorial step.

### Migration of existing players

Lazy, once per player (`story:tutorial:migrated:v1`, `MigrateOfficialTutorial`), before the pack starts — the legacy keys
are evidence only, never live state, and keep being written:

| Evidence | Becomes |
|---|---|
| all nine `vega:stage:*` (a finished chain, or the old veteran auto-skip) | the whole basic training done, silently (no line, no card, no achievement); with **thin evidence** — fewer than three of `mine:any`, `craft:any`, `build:any`, `scan:any`, `research:any` in the achievement counters — one line `story.tutorial.refresher` offers a refresher (Character tab → Restart tutorial; E30, never forced) |
| no `vega:intro`, no stage, no tutorial key, but the join snapshot's veteran evidence (knowledge, blueprints, missions on a non-Creative world — a save from before VEGA's lessons) | the basic training skipped silently, with VEGA's old "systems online" (`vega.veteran`) |
| a step's `legacyKey` | that step done (b2.2 eat, b3.1 mine, b4.1 craft, b5.1 scan, b5.3 unlock, b6.1 launch, b6.4 dock, b6.5 trade, b6.7 land) |
| `vega:intro` | b1.3, b1.5, b1.6 (VEGA, the menu and the Codex were introduced) |
| any stage | b1.1, b1.2, b1.4 (walking, jumping, the quick bar) |
| counters `mine:iron_ore` · `craft:iron_ingot` · `craft:iron_plate` · `build:any` · `craft:campfire` | b3.2 · b4.1 · b4.2 · b4.3 · b4.5 |
| gear worn beyond the starter lamp | b3.5 |
| a board or chain job taken | b6.6 |
| every counting step of a chapter evidenced | the chapter done (its extras do not replay) |

The newcomer decision (the prologue, the migration's veteran case) is the join snapshot taken at the top of the join
(#2460); a Creative world's hand-outs never count.

### Older clients (no `packs1`)

The legacy tutorial mode: the pack's start sends the old intro lines (`vega.intro.1/2/menu/codex`), the steps with a
`legacyKey` speak `vega.s.<id>.start/.done` without `Text` (the old client localizes them in its 14 languages), the chip
shows `vega.obj.<id>` exactly while such a step owns it, and steps without a legacy key do not run (so the pack is not done
for such a client — it finishes the new lessons once updated). The nine old lessons therefore run in the new order: eat,
mine, craft, scan, unlock, launch, dock, trade, land.

For such a client the nine old lessons **are** the basic training (#2487): a mandatory chapter whose legacy MUST steps are
done counts as through (`MandatoryChapterThrough` — the same `LegacyChapterPassable` that lets it walk on to the next
chapter) wherever the server asks "does the training still run?": the VEGA story's start rule (`StoryStartConditionHolds`,
so the track starts with the reason "basic training through or off"), the veteran path (`TutorialPackRunning`), the chip's
tutorial flag and the `banter` gate. Without it the story never started for an older client and VEGA never chatted. Nothing
is written: the same player on a current client later gets the new lessons (the story keeps running). On a `Courses` world
(#2498) an older client gets the silent skip like everyone else — the pack is done, the nine legacy keys are written (its
onboarding reads finished), the story starts at the join, and it hears neither the old intro lines nor `vega.done`.

### The Journal, settings, skip, replay (#2471)

The player controls the tutorial in the **Journal** (the Tab menu's Story tab grew into it; client rules in
`Client.Core/Journal/JournalView.cs`, server view in `JournalSystem.cs` — see STORY_IMPLEMENTATION "The Journal and
the objective tracker"):
- **The tutorial's page:** its status (running, off, blocked by the world rule, not started, finished — a ★ badge only when
  every mandatory part was played, not skipped); **What to do now** — the running part's tasks as "Step i of n: …" (live
  counter from `ObjectiveList`), **Skip step** (`skipStep`) and **More in the Codex** (`explain.moreGuide`: a Codex chapter
  id opens that chapter, any other id — a guide article — the Guide chapter), the part's **Skip part** (`skipChapter`,
  running parts only) and **Start part over** (`restartChapter`: the part walks again from its first step; what was done
  stays done, a step done for the first time in it is stamped done, and the part completes for real at the end); then every
  part with its state and **Replay** (`replayChapter`) for a finished one, **Start** / **Later** (`startChapter` /
  `declineChapter`) for an offered deep dive, **Start** for an available or put-off one — none of them for a part with
  nothing left to practise (#2500, `NothingLeft`: "done — nothing left to practise"); at the top **Track**, **Skip all —
  I know the game** (`skipPack`), **Restart from the beginning** (`restartPack`: every mandatory part replays from part 1 —
  keys stay, so gates and conditions keep holding; never a veteran check, never the prologue; refused while the world has
  the tutorial off) and **Switch off** / **Switch on** (`optOut` / `optIn`). #2498: when every mandatory part reads
  *skipped* and none *done* (`JournalPackPage.SkippedWhole` — after "Skip all" or on a `Courses` world) the restart button
  reads **Start basic training** (`ui.journal.start_basic_training`, the same `restartPack`). A skipped part replayed to
  its end by play is stamped *done* (its skipped key goes, `CompleteChapter`), so after the whole training the button reads
  "Restart" again and the ★ is earned; skipping the replay keeps it skipped. Replays play no part cards.
- **Character tab** (called "Settings" before #2505): the old "Skip / Restart tutorial" button is **Tutorial & courses: on / off** (named so since #2498 — it
  switches the training and the short courses together; `TutorialSwitch`: on → `optOut`,
  off → `optIn`, which also starts an opt-in world's tutorial; shown as "Off in this world" while the world rule is `Off`,
  left out when the world has no tutorial) plus **Open the Journal**. A server without the Journal messages (older) keeps
  the old button (`SkipOnboardingIntent`, still served: skip = `skipPack` + `vega.skip`, restart = `RestartPack`).
- **The chip** reads "Training: …" (the pack's `shortNameKey`) instead of "Objective: …"; the tutorial's chip is never
  muted.
- **Stuck on b6.4 (dock):** after `markerAfter` the step's `onStart.reveal: nearest_station` sets the nearest station as the
  player's space waypoint (`vega.step.marked_station`) — in flight or still landed.
- **Stuck on a "where" step on the ground** (iron b3.2, the berries b2.3 — a CORE step, which leaves the chip at its hint
  time (180 s), so since #2742 its own `escalate: { markerAfter: 120 }` puts the marker up first; `TutorialPackTests` pins
  that for every such step): after `markerAfter` VEGA marks the provider's
  target — a violet goal flag on the planet map labelled with the step's chip text and a flag blip on the compass with
  "Goal 120 m" — and says so (`vega.step.marked_spot`: "I've marked your goal — you can see it on your compass and on the
  map"). Since #2570 the marker stays until the step is done or skipped (it used to be a 30-second ping that was usually
  gone by the time the line was read). **During the basic training only the step on the chip gets a marker**: an open
  extra (a CORE step past its hint time) or another story's step gets none while the training holds the chip.
  Details: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) (help escalation).

The admin command `/tutorial skip|restart [player]`, `/tutorial chapter <id> [player]`, `/tutorial policy auto|courses|optin|off`
(chat, parsed by `AdminChatCommand.Tutorial`) does the same for a named player; without a tutorial pack it answers
`tutorial_none`. `/story [player]` lists the player packs with their state, running chapter and chip step.

## Adding a chapter

1. Add the chapter to `data/stories/tutorial/story.json` (a deep dive: `"optional": true`, an `offerWhen` condition, maybe
   `requires`, and `visibleIf` when a world's rules can rule the whole course out); give every step an id `<chapter>.<n>`, an `objective` from `GameEventKeys` (or a mission bridge), an
   `explain` with at least `startKey` and `chipKey`, and `feasibleIf` / `alternative` wherever a world could make it
   impossible.
2. Need a verb nobody raises yet? Add the key to `GameEventKeys` (+ `All`), raise it with one `RaiseGameEvent` line next to
   the existing hook of the handler, and list it in STORY_IMPLEMENTATION's event table.
3. Write the texts by hand in `locales/en.json` and `de.json` (budgets above; `{key:…}` tokens only), then bring the new
   keys into the twelve community files in the same PR: `translate_locale.py <code> --source
   data/stories/tutorial/locales/en.json --file data/stories/tutorial/locales/<code>.json` plus a hand check
   ([TRANSLATION_GUIDE.md](TRANSLATION_GUIDE.md)). A changed English text: delete the key from the twelve files first.
4. A deep dive the graduation should suggest goes into `graduation.suggest` (at most three).
5. Run the validator (both tools) and the walkthrough tests (`TutorialPackTests`); a new file goes into the WebGL
   `FallbackManifest`.
