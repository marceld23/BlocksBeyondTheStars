# Story packs: manifest, step reference and the tutorial pack

How a **player story pack** is written — the tutorial ("Basic training" B1–B6, later the deep-dive chapters C1–C18) and
future side stories — and what the shipped tutorial pack contains. The pack is data only (`data/stories/<id>/`); the engine
that runs it per player is the **player-pack runtime** (#2466), described in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-player-pack-runtime-2466).

> **Status.** Since story packs step 10 (#2469) the basic training **is** the tutorial pack `data/stories/tutorial/`: the
> old hard-coded nine-stage VEGA onboarding (`VegaStages`, `VegaIsVeteran`, the client's copy of the stage list) is gone.
> The deep-dive chapters of the parts "Research" and "Flying" — C2–C5 — came with step 18 (#2479,
> [Deep dives](#the-deep-dives-research-and-flying-c2c5-2479)); C1 and C6–C18 follow with steps 19–21. The tutorial's cards
> (`tut.welcome`, `tut.strip` …) came with step 14 (#2473, [Cards](#cards-2473)); VEGA's own pack and the hand-over to its
> chapter 1 with step 11. Test packs for the runtime live in `tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/`.

## Folder

```
data/stories/<id>/
  story.json          the manifest (below)
  locales/en.json     every key the pack uses, English  — mandatory
  locales/de.json     the same keys, German             — mandatory (community languages optional)
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
| `narrator` | `{ id, nameKey, icon, color, voice[], lineStart }` | `{ id: "vega" }` | who speaks (`ShipAiLine.Speaker`); an unknown id uses VEGA's style |
| `defaultEnabled` | bool | `true` | non-tutorial player packs: runs for everyone unless the player opts out |
| `introCutscene` | cutscene id | — | played when the pack starts for the player — not on "I know the game", not for a veteran the pack completes for silently ([CUTSCENES.md](CUTSCENES.md#bindings--where-a-pack-plays-a-scene)) |
| `gates` | `{ opportunityTips, banter, stationNear }` → condition | — | tutorial only: when the global advisor systems open (see [Gates](#gates)) |
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
| `objective` | `{ event, arg, required }` — a game event (`GameEventKeys`, a `prefix*` pattern, or `ui:<id>` — only here), `arg` exact or `a\|b` alternatives (empty = any), counted since activation; or `{ type: "Mission", target }` — a mission bridge to `data/missions.json`; or (#2476, a world story's missions only) `{ type: "Lead", target: "<lead word>" }` — a lead bridge, followed like that chapter lead (its events and evidence, its placed chip text and marker; not `explore`, not the finale's words) |
| `legacyKey` | tutorial only: `vega:stage:<mine\|craft\|eat\|scan\|unlock\|launch\|dock\|trade\|land>` — double-written on completion and skip for older builds, read once by the migration, and the old keys an outdated client follows |
| `requires` | condition[] before the step activates (default: the chapter order); a react step is armed only while it holds |
| `feasibleIf` | condition[] — false at activation ⇒ skipped silently, the `alternative` stands in (no dead ends); an alternative that is infeasible too is skipped as well |
| `alternative` | another step of the same chapter that runs only standing in for this one (a `do` or a `read` step) |
| `alreadyDoneIf` | condition[] — completes the step silently at activation (not during a replay) |
| `variants` | `[{ when, startKey, chipKey, hintKey, doneKey }]` — whole-line texts per world rule; first match wins; empty fields fall back |
| `explain` | `{ startKey, chipKey, hintKey, doneKey, moreGuide }` — start line, chip text, escalation hint, completion line, Codex article |
| `onStart` | `{ cutscene, reveal }` — the scene plays before the start line (not in a replay); `reveal` makes it a "where" step: `nearest_ore:<block>`, `nearest_settlement` or `nearest_food_plant` for the help marker (a ping for this player only, never `RevealPoi`), or `nearest_station` (#2471: the nearest station becomes the player's space waypoint, in flight or landed), or `unique_site:<observatory\|shrine\|derelict>` (#2476: the site's spot while the player is on its world) |
| `onComplete` | `{ cutscene }` — plays before the done line when the step completes non-silently (it owned the chip, or a react step) |
| `escalate` | per-step override of the pack's escalation |
| `skippable` | default `true` |
| `hintRefs` | #2469 ("one teaching voice", G12): VEGA's once-hints and context tips this step teaches itself (`invfull`, `lamp_off` …). A `do`/`read` step holds them while it is active in, or still ahead in, a mandatory chapter of a running pack (an active step of a started deep dive too) — not as an open extra past its hint time, not after its chapter is through. When the step's line is shown they count as told (a once-hint's flag is burned, a tip's first telling is booked and its cooldown starts); a skipped step gives them back untold. React steps hold nothing — their event is raised right before the hint they absorb, so the step speaks first |

Steps pay **no rewards** (maintainer decision; the validator refuses any reward field in a tutorial pack). The graduation's
achievement is the one visible reward and carries no items.

## Conditions

One grammar everywhere (`StoryConditionParser`, synthesis §5.3):

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate> | lore:<key>
```

`lore:<key>` (#2476): the player has read that lore text (a `lore_sites.json` key).

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
`PlayerSession.VegaTipLive`, no extra probe — the "moment of need" a course is offered at), `jump_ready` (the active ship
has a jump generator fitted), `arcade_ready` (the player owns a DataQube game, or a data cube waits on their world),
`monument_unread` (a rune monument on the player's world they have not read), `asteroid_mining` (the Asteroid destruction
rule is not Off), `docked` (aboard a station), `job_active` (a board or settlement-chain job in progress), `forge_ready`
(a forge crafted before or carried, or a refinery fitted), `wormhole_known` (a wormhole opens in a system the player
knows), `debris_fields` (the world option is not off). `ui:` is never a condition.

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

`GameRules.TutorialPolicy` = `Auto` (default; starts for every new player, skippable at every level) · `OptIn` (listed,
starts on the player's request) · `Off` (not offered). Set at world creation (the world-creation **Stories** page, #2472:
"Auto / On request / Off" with one line each → `--tutorial auto|optin|off`, emitted only when not `Auto`, baked into the
save's rules), changed live by the admin (the *Tutorial* row of the in-game world rules → `SetWorldRulesIntent.TutorialPolicy`,
or the admin command `tutorial policy …`). A save without the field reads `Auto`; no server preset switches it off
(`family` = `Auto`); hosted and glitch worlds pass no flag (`Auto`).

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
each); alternatives within the chapter; no empty `hintRefs`; the graduation (its line in both languages within the line
budget, a `<verb>:<thing>` counter, at most three suggested chapters that are optional chapters of the pack); **no rewards
in a tutorial**; every key in English **and** German; every cutscene binding resolves in the pack's `cutscenes.json` and
the cutscenes follow their own rules ([CUTSCENES.md](CUTSCENES.md#validation)); the short name ≤ 10 characters; the text
budget — basic-training lines (a tutorial's non-optional chapters) ≤ 120 EN / 145 DE, other step lines ≤ 200 / 240, chip
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
| b1.3 | "I'm VEGA; the bar shows your goal" | `ui:vega_continue` | M · read | VEGA story → premise variant | — |
| b1.4 | quick bar: slot 2 then slot 1 | `hotbar:select` × 2 | M · do | — | — |
| b1.5 | the menu `{key:UiMenu}` | `ui:menu_open` | M · do | — | — |
| b1.6 | the Codex | `ui:codex_open` | C · do | VEGA story → premise variant | — |
| **B2 Staying alive** | | | | | |
| b2.1 | three bars; air refills aboard (absorbs `breathe`) | `ui:vega_continue` | M · read | no oxygen / no hunger / both → whole-line variants | — |
| b2.2 | eat food `{key:Place}` | `eat:any` | M · do | `feasibleIf hunger` | eat |
| b2.3 | bushes drop berries; marker `nearest_food_plant` | `item:gained` `berries` × 3 | C · do | `feasibleIf hunger, food_plants_near` | — |
| b2.4 | the suit's ration dispenser | `suit:auto_feed` | C · react | `requires hunger` | — |
| b2.5 | the compass's ship blip, the way back (absorbs `ship_far`) | `ship:returned` (`menu`/`teleport`/`walk`) | M · do | hint variants: starter teleporter, no "Back to my ship" | — |
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
counter `tutorial:graduated`, no items), the suggested deep dives `c3` (Pilot) and `c4` (Stations, trade and jobs) marked
offered — in the Journal, without a line; the third place is free for the workshop course (C1, step 19: one id more in
`graduation.suggest`). The suggestions count as that moment's offer, so the next offer VEGA makes by herself waits the usual
90 s. The hand-over to VEGA's chapter 1 is step 11. Since #2473 the graduation card `tut.graduation` shows the achievement and
the closing line itself.

### The deep dives "Research" and "Flying" (C2–C5, #2479)

Optional chapters after the basic training (synthesis §3.2/§3.4): a player starts them from the Journal (**Start**, or
**Later** to put an offer off — it stays listed and startable, VEGA never offers it again). VEGA offers a course once, at
its moment of need, with `vega.chapter.offer` ("New short course: {0}…") — never while the basic training runs, at most one
offer every 90 s of play. Every **do** step of a course counts (tier `core`/`opt` only marks what the synthesis calls
optional); read and react steps never block. Each course opens with the card `tut.course` ("Short course" + its title) and
closes with the part banner `tut.done`; two firsts get their own card (below). No legacy keys, no rewards; an older client
(no `packs1`) never runs a course.

| Course | Needs (`requires`) | Offered (`offerWhen`) |
|---|---|---|
| **C2 Knowledge and research** (part Research) | `chapter:tutorial:b5` | `any:[world:data_cache_near, world:scanner_unknown]` — the tips' moment |
| **C3 Pilot** (Flying) | `chapter:tutorial:b6`, `world:free_flight` | suggested at the graduation |
| **C4 Stations, trade and jobs** (Flying) | `chapter:tutorial:b6` | suggested at the graduation |
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
| c2.6 | the terrain scanner | `gadget:used` `terrain_scanner` | do | `feasibleIf blueprint:terrain_scanner`, else **c2.6a** (read: "its blueprint needs titanium plates — a forge makes them"); absorbs `scanner_idle`, `scanner_unknown`. Step 19 can widen the condition by data (`any:[blueprint:terrain_scanner,chapter:tutorial:c1]`) |
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

A course shows at most two cards: its course card and at most one first (a test counts them). The basic training's budget
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

### One teaching voice (G12)

Steps name the hints and tips they teach in `hintRefs` (table above). Client-only lessons (E11): while the tutorial runs
for the player (`ShipAiLine.TutorialActive`), the flight lessons the B6 steps teach — `chart_waypoint`, `ship_scanner`,
`ship_scan_hold`, `anomaly_scan` — are not spoken and count as given (`VegaText.LessonCoveredByTutorial`): the step lines
carry them per player on the server, so a shared school PC no longer decides who heard them. The other client lessons
(planet card, target lock, wormhole, climbing, the bio lab tabs, the pad/touch context list B1.7) stay client lessons until
a deep-dive chapter covers them.

### Migration of existing players

Lazy, once per player (`story:tutorial:migrated:v1`, `MigrateOfficialTutorial`), before the pack starts — the legacy keys
are evidence only, never live state, and keep being written:

| Evidence | Becomes |
|---|---|
| all nine `vega:stage:*` (a finished chain, or the old veteran auto-skip) | the whole basic training done, silently (no line, no card, no achievement); with **thin evidence** — fewer than three of `mine:any`, `craft:any`, `build:any`, `scan:any`, `research:any` in the achievement counters — one line `story.tutorial.refresher` offers a refresher (Settings → Restart tutorial; E30, never forced) |
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

### The Journal, settings, skip, replay (#2471)

The player controls the tutorial in the **Journal** (the Tab menu's Story tab grew into it; client rules in
`Client.Core/Journal/JournalView.cs`, server view in `GameServerJournal.cs` — see STORY_IMPLEMENTATION "The Journal and
the objective tracker"):
- **The tutorial's page:** its status (running, off, blocked by the world rule, not started, finished — a ★ badge only when
  every mandatory part was played, not skipped); **What to do now** — the running part's tasks as "Step i of n: …" (live
  counter from `ObjectiveList`), **Skip step** (`skipStep`) and **More in the Codex** (`explain.moreGuide`: a Codex chapter
  id opens that chapter, any other id — a guide article — the Guide chapter), the part's **Skip part** (`skipChapter`,
  running parts only) and **Start part over** (`restartChapter`: the part walks again from its first step; what was done
  stays done, a step done for the first time in it is stamped done, and the part completes for real at the end); then every
  part with its state and **Replay** (`replayChapter`) for a finished one, **Start** / **Later** (`startChapter` /
  `declineChapter`) for an offered deep dive, **Start** for an available or put-off one; at the top **Track**, **Skip all —
  I know the game** (`skipPack`), **Restart from the beginning** (`restartPack`: every mandatory part replays from part 1 —
  keys stay, so gates and conditions keep holding; never a veteran check, never the prologue; refused while the world has
  the tutorial off) and **Switch off** / **Switch on** (`optOut` / `optIn`).
- **Settings:** the old "Skip / Restart tutorial" button is **Tutorial: on / off** (`TutorialSwitch`: on → `optOut`,
  off → `optIn`, which also starts an opt-in world's tutorial; shown as "Off in this world" while the world rule is `Off`,
  left out when the world has no tutorial) plus **Open the Journal**. A server without the Journal messages (older) keeps
  the old button (`SkipOnboardingIntent`, still served: skip = `skipPack` + `vega.skip`, restart = `RestartPack`).
- **The chip** reads "Training: …" (the pack's `shortNameKey`) instead of "Objective: …"; the tutorial's chip is never
  muted.
- **Stuck on b6.4 (dock):** after `markerAfter` the step's `onStart.reveal: nearest_station` sets the nearest station as the
  player's space waypoint (`vega.step.marked_station`) — in flight or still landed.

The admin command `/tutorial skip|restart [player]`, `/tutorial chapter <id> [player]`, `/tutorial policy auto|optin|off`
(chat, parsed by `AdminChatCommand.Tutorial`) does the same for a named player; without a tutorial pack it answers
`tutorial_none`. `/story [player]` lists the player packs with their state, running chapter and chip step.

## Adding a chapter

1. Add the chapter to `data/stories/tutorial/story.json` (a deep dive: `"optional": true`, an `offerWhen` condition, maybe
   `requires`); give every step an id `<chapter>.<n>`, an `objective` from `GameEventKeys` (or a mission bridge), an
   `explain` with at least `startKey` and `chipKey`, and `feasibleIf` / `alternative` wherever a world could make it
   impossible.
2. Need a verb nobody raises yet? Add the key to `GameEventKeys` (+ `All`), raise it with one `RaiseGameEvent` line next to
   the existing hook of the handler, and list it in STORY_IMPLEMENTATION's event table.
3. Write the texts by hand in `locales/en.json` and `de.json` (budgets above; `{key:…}` tokens only; never add them to the
   community locales — the translation pass does).
4. A deep dive the graduation should suggest goes into `graduation.suggest` (at most three).
5. Run the validator (both tools) and the walkthrough tests (`TutorialPackTests`); a new file goes into the WebGL
   `FallbackManifest`.
