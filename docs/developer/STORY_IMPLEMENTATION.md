# Story engine ("The VEGA Protocol") — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-10-11.

This doc describes the **story engine mechanics** — state, pacing, triggers, persistence, networking and the
finale flow. The *why* (design rationale) lives in [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md);
the *canon* (lore) lives in [LORE_STRUCTURE.md](LORE_STRUCTURE.md). This file does not duplicate the lore.

## Overview

The engine is **story-agnostic**: each storyline is a swappable **story pack** (content + config) and the
world picks which one is active (default `vega_protocol`, or `none`/sandbox). The engine drives a per-save,
per-story state through threshold-paced narrator beats, world-found text fragments, combat-driven progress,
personal player memories, a visible progress meter, a re-readable **Story Log tab**, and a multi-stage
dialogue-duel finale. Server stays authoritative; every in-game string ships bilingual DE+EN.

The first pack — *The VEGA Protocol* — is the amnesiac ship-AI VEGA recovering the SPS truth, the player
revealed as a clone, and the galaxy's machines (UFO drones, three-eyed ground robots, scan-drones) revealed
as remnants of the dormant Guardian core, pacified in the finale by exposing its contradiction.

## How it works

**State & pacing.** Story state is per save *and* per `storyId`, lives on `GameServer`, and is persisted.
Pacing is a threshold score: `progress = fragments·Wf + min(machineKills, killCap)·Wk + milestones·Wm`,
scaled by a per-world density multiplier (Sparse/Normal/Dense = 0.65/1.0/1.5). Beats fire when the score
crosses a threshold (not on a specific find), so a linear arc works in a random world. Diminishing returns
/ a kill cap stop combat grind from trivialising pacing.

**Three progress drivers** all feed the same score:
- **Net fragments** — text-only pickups placed deterministically on planet surfaces (datacube-style) from
  the active pack's still-needed, weighted pool; reach-checked pickup opens an archive-text reader and
  advances the arc. Distinct from `ServerDataCube` knowledge mini-games.
- **Machine kills** — defeating a Guardian machine (planet enemies + space drones/UFOs; organic fauna
  excluded) advances the story and has a chance to drop a **player memory** (a per-player, non-contradictory
  personal unlock; each player is a different imprint).
- **Milestones** — repeatable: mission turn-in (settlement helped) and first-discovery system mapping (never
  the player's first, spawn system — #2460: every new identity used to add +2 just by joining);
  **once per save** (#1105, `RecordStoryMilestone(onceKey)`, keys persisted in `StoryState.MilestoneKeys`):
  first base founded, first player station commissioned, first self-built ship commissioned, first companion
  tamed, first hyperjump, first vault opened (`vault:first`, #1155), first monument scanned per world
  (`monument:<body>`). Pacing note: fragments cap
  at 6 (18 pts) and kills at 40, so before #1105 the remaining ~130 of the 204 needed came almost entirely
  from delivery turn-ins; the once-firsts add roughly 10–20 pts per playthrough from building, taming and
  exploring — they widen the arc's inputs without making any of them farmable.

**Beats** are spoken through the existing VEGA narrator pipeline (`ShipAiLine`, Kind 2) — always in VEGA's
voice: a pack has no narrator/speaker field yet (the story-packs manifest adds one, #2459), so no new beat UI.
A beat's text must not assume where the player is: any score event can cross a threshold — a drone kill in
space, a mission turn-in, a mapped system — so B1 speaks of "a signal out there", not "this wreck" (#2463).
Per-player "seen beats" reuse `PlayerState.Milestones` (`story:<id>:beat:N`)
— no schema migration. Milestones are append-only (since #2469 even the tutorial restart keeps every key — it replays
the tutorial pack's chapters; only a replay's own keys go when it ends). Latecomers get a join-time catch-up of
earned-but-unseen beats (`CatchUpStoryLines`),
plus the Guardian-system reveal line while the finale is open (`story:<id>:finale:revealed`, #2460).

**Wreck coupling** (count-neutral): a nearby wreck biases machine *position* (cluster) and *aggression* only,
never the count — toggled by `GameRules.MachineWreckCoupling`.

**Finale** (staged, weapons cannot end the core):
1. Arc-complete (all beats seen) flips a one-way `guardianSystemRevealed` flag → broadcasts the lone landable
   `guardian_finale` system onto the star map (lazily appended to the galaxy on reveal, re-appended for
   revealed saves on restart by `LoadStoryState` — the galaxy is built before the story loads, #2460; reserved
   from the procedural generator). Reaching it needs the `jump_generator`.
2. **Stage 1 — gauntlet:** the finale flight instance fields a scripted elite wave (heavy cruiser + elite
   UFOs + reinforced drone swarm), not ambient hostiles.
3. **Stage 2 — two routes to the inner core:** a buried iron chamber with a glowing red core column, reached
   either via a pre-carved descent shaft (Route A) or by digging down through the shell (Route B). Both
   converge; the breach is proximity-gated to the core terminal.
4. **Stage 3 — hack:** hold to channel a server-authoritative `CoreHackProgress`; at 100% the duel opens.
5. **Stage 4 — argument duel:** data-driven from the pack's `coreArguments`; a correct contradiction
   advances, a wrong pick re-presents the node (the duel can stall but never be lost). Clearing the last node
   calls `MarkGuardianDefeated`.
6. **Pacification → Remnant Protocol (#1206):** the one-way `guardianDefeated` flag despawns the live planet
   machines at the moment of the win (the dramatic beat) and then acts as a *factor*, not a switch: the planet
   cap halves (floor one, `PlanetEnemyCap`), refills run at twice the interval (`RemnantPaceFactor`), only
   scan-drones spawn (`RemnantEra`); the finale gauntlet stays down; ambient space waves survive only in
   pirate havens and the frontier-danger tier (`RemnantSpaceHostile`); bandit ships keep only pirate havens
   (`BanditShipsAllowedIn`), so the raider bounty stays offerable. It does **not** touch the admin threat
   sliders; PlanetEnemies *Off* still means zero. A death in the boss system respawns the clone on the world
   it launched from (no death-loop).

**Ending catch-up.** Winning plays `StoryResolved` (plus the finale achievement) to everyone aboard. On a later
join it plays once — with the `finale_resolved` line and the achievement — only for a player who had heard the
arc's last beat **before** that join (#2460, N4/E23); a player who first meets the story after the win gets
`story:<id>:resolved` set silently and can watch the ending from the Story tab.

**Knowledge level** (`WorldKnowledgeLevel`, gates lore, flavour lines and NPC threads): 0–3 follow the arc's
progress (1 > 0 %, 2 ≥ 33 %, 3 ≥ 66 %); **4 is the win alone** (#2460) — its lines say the machines went still.

## Join snapshot, story rows and observers (#2460)

- **Join snapshot.** `PlayerSession.Join` (`JoinSnapshot`) is taken once at the very top of the join, before
  `SetupPlayerShip`, the Creative grants and the beat catch-up change the player state: `Veteran` (knowledge,
  blueprints or missions on a world whose *saved* Creative option — `_meta`, not the launch flag — is off),
  `HadOnboardingIntro`, `Newcomer`, and the `story:` keys the player carried (`HadStoryKey`). The prologue (a
  newcomer's cold open, `ShipAiOnJoin`) reads it — re-evaluating "veteran" after the catch-up had paid beat knowledge
  skipped the prologue and the tutorial for every newcomer on a progressed world — and so do the ending catch-up and,
  since #2469, the tutorial pack's one-time migration (a save from before VEGA's lessons). Later packs reuse it.
- **Active story row.** A save holds one `story_state` row per pack it ever ran. `PersistStoryState` stamps the
  active row (`StoredStoryState.Active = true`, `UpdatedUtc`), an admin switch clears `Active` on the row it left,
  and `SelectActiveStoryRow` picks: rows of installed packs (or `none`) over unknown ones → the active marker →
  the newest stamp → for older saves the fresh-save choice, else `none`, else the default pack. A row of an
  uninstalled pack never costs real progress. Additive JSON fields — no save-version bump.
- **Observers.** A fleet admin in observer mode (`/spectate`) is not a story listener (`StoryListeners()`): no
  beats, story lines, finale rewards or VEGA tick while observing; `MarkArrivedOnBody` books a `/goto` arrival
  silently (no milestone, no frontier growth, no knowledge); `player_near` never names them. `ExitSpectate`
  runs the catch-up.

## Game events, story conditions and the world-story guard (#2465)

Groundwork for the story packs (#2459): the per-player step runtime of the tutorial and side packs (#2466) listens
to **game events** and decides with **conditions**. Nothing here changes what a player sees.

**The event bus.** `GameServer.RaiseGameEvent(PlayerSession actor, string key, string arg = "", int amount = 1)`
(`GameServerGameEvents.cs`) is one line at each gameplay site, **next to** the existing hooks — achievements keep
`OnAchievement*` → `Advance`, VEGA keeps `ShipAiOn*` and `RecordStory*`; the bus never replaces one, so their behaviour
cannot drift. It persists nothing (in particular nothing in `AchievementCounters`, which travels whole on every
achievement change). A consumer implements `IGameEventListener.OnGameEvent(actor, key, arg, amount)` and registers per
key with `AddGameEventListener(keyOrPattern, listener)` — an exact key or a prefix ending in `*` (`craft:*`, `*`);
`RemoveGameEventListener`, `HasGameEventListener(key)`. Cost: with no listener a raise is one integer compare; otherwise
one dictionary lookup (plus a short prefix scan while a wildcard listener exists). The listener arrays are
copy-on-write, so a listener may (un)register while an event is delivered; a listener that throws is logged and
dropped, never breaking the handler. **Nothing is raised** for an observer (`Spectating`), for a session that is not
joined, for a non-positive amount, and during the join's own placement (`PlayerSession.InJoinPlacement`, set from the
session's creation until the end of `HandleJoin` / `AddLocalPlayer` — the spawn body's first "visit", a station
re-boarded at the join or a resumed flight are not the player's doing).

**Vocabulary** (`Shared/Story/GameEventKeys.cs`; the key names the verb, the entity rides in the argument, so keys never
grow with content — `mine:any` + `iron_ore`, never `mine:iron_ore`). Step 10 (#2469) added the basic training's
teaching verbs (the second block of the table); the deep dives add theirs with steps 18–21.

| Key | Argument (amount) | Raised at | File |
|---|---|---|---|
| `mine:any` | block key | a block mined by hand, next to `OnBlockMined` | `GameServer.cs` (`BreakBlockCore`) |
| `build:any` | block key | a block placed, next to `OnAchievementBuild` | `GameServer.cs` (`HandlePlace`) |
| `build:any` | `blueprint_paste` (placed count) | a blueprint paste | `GameServerBlueprints.cs` |
| `build:rotated` | block key | a form/prop block placed with an explicit rotate-key turn (`Yaw` 0–3 or an up-face) | `GameServer.cs` (`HandlePlace`) |
| `craft:any` | recipe key (batch count) | a recipe crafted — also a free Creative craft, where the VEGA/achievement hooks do not run | `GameServer.cs` (`HandleCraft`) |
| `craft:tint` / `craft:shape` / `craft:paint` | base item key (count) | dye/glow, a form craft (built-in or player-designed), a paint design — every success path incl. Creative; never `craft:any` | `GameServer.cs` (`HandleTintCraft`, `ApplyShapeExchange`, `ApplyMultiCellExchange`), `GameServerPaint.cs` |
| `eat:any` | item key | real food eaten (hunger value > 0) | `GameServerCreatures.cs` (`ConsumeItem`) |
| `scan:performed` | readout kind (`flora`, `creature`, `tree`, `block`, `monument`, `asteroid`, `anomaly`, `wreck`, `wormhole`, `debris` …) | every scan, first or repeat, hand or ship scanner | `GameServerScanning.cs` (`Award`) |
| `scan:any` | readout kind | a first-time scan (the discovery the achievement counts) | `GameServerScanning.cs` (`Award`) |
| `research:any` | blueprint key | a blueprint researched | `GameServer.cs` (`HandleUnlock`) |
| `space:enter` | `launch` \| `hyperjump` \| `wormhole` \| `resume` | every entry into flight (`resume` = back at the helm without a take-off) | `GameServerSpaceCombat.cs` (`EnterSpace`) |
| `station:boarded` | station id | a station boarded | `GameServerSpaceStations.cs` (`EnterBoardedStation`) |
| `trade:barter` | recipe key (batch count) | a market barter off the ship | `GameServer.cs` (`HandleCraft`) |
| `trade:player` | the partner's player id | a completed player trade (both sides) | `GameServerTrade.cs` (`CloseTrade`) |
| `mission:accepted` | mission id | a board job or a chain step taken (board or conversation) | `GameServerMissions.cs`, `GameServerMissionChains.cs` |
| `mission:turned_in` | mission id | a mission turned in | `GameServerMissions.cs` (`HandleTurnInMission`) |
| `travel:landed` | `flight` \| `quick` \| `admin` | a landing from flight (also back onto the body you launched from), the map's quick travel — **and a parked ship flying a map trip by itself (#1614)** — or an admin move (`/goto`, `goto_core`). The join placement raises nothing | `GameServer.cs` (`HandleTravel`, `AdvanceTransit`), `GameServerSpaceCombat.cs` (`HandleLeaveSpace`) |
| `visit:body` | body id | the first arrival on a body (stations too) | `GameServer.cs` (`MarkArrivedOnBody`) |
| `visit:system` | system id | the first arrival in a star system — never the spawn system | `GameServer.cs` (`OnSystemMapped`) |
| `hyperjump:any` | destination system id | a jump between systems (travel screen or in flight) | `GameServer.cs` (`HandleTravel`), `GameServerSpaceCombat.cs` (`JumpFlightToSystem`) |
| `module:built` | module key | a ship module built | `GameServerSpaceCombat.cs` |
| `ship:commissioned` | ship id | a self-built ship commissioned | `GameServerCustomShips.cs` |
| `station:commissioned` | station id | a player station commissioned — raised for its **owner** (online), like the achievement | `GameServerPlayerStations.cs` |
| `base:founded` | body id | a base core placed | `GameServerBases.cs` (`PlaceBase`) |
| `tame:any` | species id (`au_sreekmakra`) | a creature tamed; the shapeshifter following a tame raises its own | `GameServerTaming.cs`, `GameServerSreekmakra.cs` (`BondSreekmakra`) |
| `loot:any` | container kind | a world container looted empty (never a player crate) | `GameServerContainers.cs` |
| `defeat:any` | `creature` \| `giant` \| `machine` \| `bandit` (planet), `space_machine` \| `raider` (space) | a hostile defeated by the player | `GameServerEnemies.cs`, `GameServerSpaceCombat.cs` |
| `wormhole:any` | destination system id | a flight through a wormhole | `GameServerWormholes.cs` |
| `relay:commissioned` | station id | an SPS relay completed by the player's delivery | `GameServerRelays.cs` |
| `lane:linked` | `<systemA>\|<systemB>` | a jump lane opened by the player's contribution | `GameServerRelays.cs` |
| `crystal:<event>` | — | a Crystal Net circuit event of the circuit's owner (`CircuitEvents`: `lamp_by_switch`, `net_on`, `machine_job` …) | `GameServerCrystalQuests.cs` (`OnCrystalCircuitEvent`) |
| `story:fragment` | fragment key | a story fragment handed over (picked up, mission thread, NPC dialog, NPC thread) | `GameServerNetFragments.cs`, `GameServerMissions.cs`, `GameServerNpcDialogs.cs`, `GameServerNpcThreads.cs` |
| `insight:companion_ward` | — | a companion made a machine stand down near the player (at most every 30 s) | `GameServerEnemies.cs` |
| `insight:shape_noncube` | base item key | a non-cube form crafted | `GameServer.cs` (`RaiseShapeCrafted`) |
| **#2469 — the basic training's verbs** | | | |
| `move:walked` | — (whole metres) | walking on a surface (no speeder, no train; a report over 4 blocks from the last one never counts) — measured only while a step listens | `GameServer.cs` (`HandleMove`) → `GameServerTutorial.cs` (`TutorialMovementEvents`) |
| `move:jump` | `jump` \| `crouch` | a jump (upward speed > 2.5 blocks/s, not climbing) or the start of a crouch, at most once a second — only while a step listens | same |
| `hotbar:select` | slot number (1-based) | another quick-bar slot picked (no event when the slot does not change) | `GameServer.cs` (`Dispatch`, `SelectHotbarIntent`) |
| `item:gained` | base item key (count) | a mined or harvested block's drops reach the player | `GameServer.cs` (`BreakBlockCore`) |
| `item:moved` | — | an item moved between two backpack/quick-bar slots | `GameServer.cs` (`HandleMoveItem`) |
| `gear:equipped` | base item key | a piece of gear put on | `GameServerEquipment.cs` (`HandleEquipItem`) |
| `cargo:stowed` | item key, or `all` | items stowed into the ship's hold (one item or "stow all") | `GameServerCargo.cs` (`MoveCargo`) |
| `inventory:full` | — | the backpack has no free slot (the 1 Hz advisor check, raised right before the `invfull` hint) | `GameServerShipAi.cs` (`TickShipAi`) |
| `ship:returned` | `menu` \| `teleport` \| `walk` | "Back to my ship", the suit teleporter, or walking back aboard / within 10 blocks of the landed ship after having been 40+ blocks away (the walk check runs at 1 Hz only while a step listens) | `GameServerReturnToShip.cs`, `GameServerTeleport.cs`, `GameServerTutorial.cs` (`TutorialStateEvents`) |
| `lamp:toggled` | `on` \| `off` | the suit lamp switched | `GameServerShipAiContext.cs` (`HandleSetLamp`) |
| `suit:auto_feed` | item key | the suit fed a stored ration to a starving player | `GameServer.cs` (`TryAutoEatRation`) |
| `suit:climate` | `cold` \| `heat` | the suit's climate control started fighting the weather | `GameServerTemperature.cs` (`TickTemperature`) |
| `player:respawned` | — | any respawn (the relocation half of a death) | `GameServer.cs` (`CompleteRespawn`) |
| `mine:refused` | block key | a swing refused by the tool tier while a better tool exists (where the `tier_gate` tip is armed) | `GameServerShipAiContext.cs` (`NoteTierGate`) |
| `data:found` | `cache` \| `fragment` | a data cache in the advisor probe's reach, or a data fragment in the backpack or the own ship's hold (1 Hz, only while a step listens) | `GameServerShipAiContext.cs` (`TickVegaContextTips`), `GameServerTutorial.cs` |
| `flight:flown` | — (whole flight units) | the own ship flying (a pose jump over 60 units — a hyperjump, a warp, a fresh entry — never counts) — measured only while a step listens | `GameServerSpaceCombat.cs` (`ShipMove`) → `TutorialFlightEvents` |
| `mission:board_accepted` | mission id | a job the player picked: a board's or a mission chain's (`MissionAcceptCountsAsTrade`, B58 — never a story character's errand) | `GameServerMissions.cs`, `GameServerMissionChains.cs` |
| `visit:world` | body id | the first arrival on a planet, moon or asteroid (`visit:body` counts stations too) | `GameServer.cs` (`MarkArrivedOnBody`) |

**`ui:<id>` (client teaching flags, synthesis §3.6, #2469).** Tag **298 `UiTutorialEvent { Id }`** (C→S) reports a UI
moment a step teaches — `vega_continue`, `menu_open`, `codex_open`, `map_open`, `waypoint_set` (`GameEventKeys.UiEvents`);
`HandleUiTutorialEvent` raises `ui:<id>` on the bus for a known id at most every 0.25 s of server time. Teaching only: no
step but one waiting for exactly that flag listens, it pays nothing, unlocks nothing, counts for no story and is never
stored; it is never a condition (the parser refuses `ui:`). Pause-whitelisted (`PausedMayHandle`). Clients:
`NetworkClient.SendUiTutorialEvent` (the same flag at most every 0.5 s) from `VegaPanel` (a line read through),
`GameMenu` (menu opened, Codex, Map tab) and `SpaceMap` (chart opened, waypoint set).

**Conditions** (`Shared/Story/StoryCondition.cs`). One small grammar for a step's `requires` / `feasibleIf` /
`alreadyDoneIf`, a chapter's `offerWhen`, a pack's `start.after` and its `gates`, parsed once at load by
`StoryConditionParser.Parse(text, worldPredicates)` (or `TryParse`, `ParseList` for a JSON list = one implicit `all`)
into a `StoryCondition` tree and evaluated purely over an `IStoryConditionContext`:

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate>
```

Ids are letters, digits, `_ . -` (a step id is `b2.2`); spaces around brackets and commas are fine. Content errors
(`StoryConditionException`, message names the problem and the position): unknown forms, unknown world predicates,
reserved pack ids (`none, mem, insight, obj, vega, track, cut` — `StoryRegistry.ReservedPackIds`), `ui:` claims, empty
groups, nesting deeper than 16. The server's context (`GameServerStoryConditions.cs`, `EvaluateStoryCondition`) reads the
§5.6 milestone keys (`StoryPackKeys`: `story:<pack>:step:<step>[:skipped]`, `story:<pack>:ch:<c>:done|skipped`,
`story:<pack>:started|done|optout`), the player's blueprints, `pack:<id>:enabled` (the world story, or an installed
player pack not opted out — since #2466 a tutorial also by the world's tutorial policy, another player pack by its
`defaultEnabled`) and the `world:` registry. The predicates
(`StoryWorldPredicates.All`, held equal to the server registry by `StoryConditionTests`): place — `station_in_system`
(= the dock lesson's skip rule), `settlement_reachable`, `airless`, `fragment_on_world`, `player_near`; rules —
`enemies`, `space_enemies`, `bandits`, `free_flight`, `hunger`, `oxygen`, `hazards`, `creative` (incl. a per-player mode
override), `creative_flight`, `instant_travel`, `return_to_ship`, `starter_teleporter`, `base_visitors`,
`space_stations`, `story_active`; since #2469 the tutorial's feasibility checks `food_plants_near`, `trees_near` (bounded
probes of the loaded blocks around the player, run when a step activates), `shape_block_carried`, `research_ready`,
`all_blueprints`, `new_world_in_system` (`GameServerTutorial.cs`). A predicate reads the player's own body/system, never
the active world cursor. A new predicate is a name in `StoryWorldPredicates` plus one line in `GameServer.WorldPredicates`.

**The world-story guard (N11).** A pack carries `kind` (`story` default, `tutorial`, `side`) and `scope` (`world` |
`player`; unset = `world` exactly when it has beats; a tutorial is always `player` — `StoryRegistry.EffectiveScope`).
Only a world-scoped pack with beats and a non-reserved id can be the save's story (`StoryRegistry.CanBeWorldStory`):
`GameContent.TryGetWorldStory` answers for `IsUsableStoryRow`, the stored-row and `Rules.StoryId` paths of
`LoadStoryStateFrom` and `SetActiveStory`, so `--story tutorial`, `StorySelectIntent{tutorial}` or a stored row naming a
player pack never make it the world story (the row is left untouched). `GameContent.SetStories` always installs the
built-in VEGA pack when the data has no usable `vega_protocol` (it used to fill in only when *no* pack loaded) and drops
reserved ids; `DefaultStory` is never a player pack. `TryGetStory` still finds any pack (texts, player packs).

**Pack loading (B70).** `ContentLoader` loads `data/stories/*` in **ordinal** folder order (the OS order decided which
pack's text won a shared locale key), skips a pack with a reserved id, reports a repeated id, and reports every pack
locale file that overwrites keys of an earlier file (English table only; the later file still wins) through the loader's
`warn` callback (the server logs it). `CommunityLocaleTests` enumerates every `data/stories/*/locales` folder: the
pack's community tables are held to the same guards (no invented keys, placeholders kept, no blanks, a known language),
measured against the pack's own `en.json`; `StoryPackGuardTests` also checks each pack's en/de key parity.

## The player-pack runtime (#2466)

The engine the tutorial pack and future side stories run on — per player, server-authoritative, data-only (synthesis §5,
option C). The manifest fields, the step reference and the shipped tutorial's content are in
[TUTORIAL_PACK.md](TUTORIAL_PACK.md); this section is how the server runs them (`GameServerStoryPacks.cs`, the tutorial's
extras in `GameServerTutorial.cs`). **Since #2469 the official tutorial pack (`data/stories/tutorial/`) is the basic
training** — the hard-coded nine-stage onboarding chain (`VegaStages`, `VegaIsVeteran`, `ShipAiComplete`, the
`ShipAiOn<Stage>` hooks, `VegaMineCount`) and the client's copy of the stage list are gone. `ShipAiOnJoin` only plays the
prologue for a newcomer (once, keyed `story:<story>:cut:prologue` — the cutscene ledger's seen form, so step 11's prologue
cutscene treats a player who heard the pages as one who saw it) and sends the chip. The test packs live under
`tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/` (never `data/stories/`); `StoryPackFixtures.ContentWith` drops the
shipped player packs so a fixture runs alone.

**Which packs run.** Every installed pack whose effective scope is `player` and that has chapters is compiled once
(`PlayerPacks`, highest `priority` first): conditions are parsed against the server's `world:` predicates, chapters and
steps indexed; a pack with a structural or condition error is logged and not run. A pack runs for a player when it is
**enabled** (`StoryPackEnabledFor`, = `pack:<id>:enabled`) and **started** (`story:<pack>:started`). Enabled: the player
has not opted out, and for a `tutorial` pack the world rule `GameRules.TutorialPolicy` says so (`Auto` → yes, `OptIn` →
once the player started it, `Off` → no); any other player pack by its `defaultEnabled` or once started. A pack starts by
itself when enabled and its `start.after` holds (empty = at once); `StoryPackIntent{startPack}` starts an opt-in tutorial
(refused while the policy is `Off`). A tutorial's start also writes `vega:intro` (older builds read it as "the intro was
shown"). The world story (VEGA) is not run here — its per-player track is step 11.

**Chapters.** A non-optional chapter runs once the previous non-optional chapter is done or skipped (and its own
`requires` holds); `autoStart: false` waits for `startChapter`. An optional chapter (deep dive) runs once the player
started it (`story:<pack>:ch:<c>:active` — the one key beyond §5.6, so a started deep dive survives a rejoin) and is
**offered** once when `offerWhen` holds (`story:<pack>:ch:<c>:offered`, line `vega.chapter.offer` with the chapter title as
`{0}`) — never while the pack's mandatory chapters still run (no offers during basic training), at most one offer per
90 s of play; `declineChapter` stamps `:declined` ("Later") and keeps it startable. `autoCompleteIf` holding when a chapter
runs completes it silently (veteran evidence; ignored during a replay). A non-optional chapter is **done** when every
`must` do step is done or skipped and nothing still holds it up (a CORE step before its hint time, an alternative standing
in); an optional chapter needs every do step. The pack is done (`story:<pack>:done`) when every non-optional chapter is.

**Steps.** The walk visits a running chapter's steps in order; one blocking do step at a time owns the chip (G4):

| Kind / tier | Behaviour |
|---|---|
| `do` · `must` | blocks the chapter until its objective is met (events counted **since activation**), it is skipped, or it is infeasible |
| `do` · `core` | blocks until its `hintAfter` time has passed (the hint line goes out), then becomes an **open extra**: it keeps listening and completes silently later; the chapter goes on |
| `do` · `opt` | never blocks a non-optional chapter |
| `read` | spoken at activation, never blocks; done on `ui:vega_continue` or when a later do step of the chapter completes |
| `react` | armed (listening, silent) from the chapter's start wherever it stands; never blocks; when its event fires it speaks its explanation and is done — it stays armed after the chapter until then |

At activation: `requires` false → the chapter waits there; `feasibleIf` false → the step is skipped silently (G7) and its
`alternative` (a step of the same chapter, which otherwise never runs) stands in at its place — or is skipped too when its
own `feasibleIf` fails (#2469); `alreadyDoneIf` true →
completed silently (not during a replay); a mission bridge whose mission is already turned in completes at once;
otherwise the counter starts (`PlayerState.StepProgress["<pack>:<step>"] = 0`), the help clock starts and the start
line is spoken. `variants` swap a whole text slot by condition (first match). A completed step stamps
`story:<pack>:step:<step>` (skipped: `…:skipped`) and, for a tutorial step with a `legacyKey`, double-writes that
`vega:stage:<id>` (completion and skip; graduation and `skipPack` write all nine stages + `vega:intro`). The done line is
spoken only when the step owned the chip (an early or open-extra completion is silent).

**Events.** The runtime registers on the game-event bus only for the keys of steps that are active for some joined
player (`SyncStepListeners`, after every transition and disconnect), so with nothing active a raise stays one integer
compare. `OnStepGameEvent` matches the actor's active steps by key (exact or `prefix*`) and argument (exact, `a|b`
alternatives, or any), adds the amount, completes and advances; a counter tick re-sends the chip. Counters persist with
the normal player save (no write per event); transitions write the player at once.

**Help escalation** (§3.1), on active play time only (`PlayerSession.StoryPackPlaySeconds`, ticked by the 1 Hz VEGA
advisor loop, which does not run while the world is held), per session, each stage once, progress restarts the clock:
`hintAfter` (default 180 s) → the step's `hintKey` line; `markerAfter` (360 s) on a "where" step (one with
`onStart.reveal`) → a **private ping** at the provider's target (`nearest_ore:<block>`: the nearest such block in a 33³
probe; `nearest_settlement`) that only this session sees — RAM, 30 s, never persisted, never broadcast, never
`RevealPoi` (`GameServer.RaisePrivatePing`, `ServerPing.SessionId`) — plus `vega.step.marked`; nothing found → no ping;
`offerSkipAfter` (600 s) on a `must` step → `vega.step.offer_skip`. Pack defaults in `escalate`, per-step override; `0`
switches a stage off.

**Skip, replay, opt-out** (all grant nothing): `skipStep`, `skipChapter` (remaining steps skipped, chapter skipped —
`chapter:` conditions hold after), `skipPack` ("I know the game": every mandatory chapter skipped, deep-dive offers stay).
`restartChapter` replays a done or skipped chapter **with the evidence off**: `story:<pack>:ch:<c>:replay` is set, its
steps run again recorded in `story:<pack>:replay:<step>`, while the chapter's done key and step keys **stay** — so gates
and `step:`/`chapter:` conditions keep holding during the replay (a replay never closes the tips again); when the replay
completes the replay keys go. The tutorial's restart (`/tutorial restart`, `SkipOnboardingIntent{Restart}`) replays every
mandatory chapter in order and never touches the world story (no prologue, E17). `optOut` stamps
`story:<pack>:optout`: lines and objectives stop, every key and counter stays; `optIn` resumes.

**Migration hook.** Before a pack first starts for a player (`MigratePackIfNeeded`, also from `StartPack`, so a skip or a
restart reads the old state first) `story:<pack>:migrated:v1` is written once; for a tutorial pack every step whose
`legacyKey` the player already holds counts as done, and the official tutorial maps the rest of the old onboarding state
(`MigrateOfficialTutorial`, #2469: a finished chain → done silently, with one E30 refresher offer on thin evidence; a
pre-lessons veteran → skipped with `vega.veteran`; otherwise per-step evidence from `vega:intro`, the stages, achievement
counters, worn gear and taken jobs — the table is in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#migration-of-existing-players)).
Legacy keys are evidence only — after the marker they are written (double-write) but never read.

**Gates.** `TutorialGate(session, gate)` answers for the global advisor systems: null when no tutorial pack has a say
(none installed, or the gate unset — the caller keeps its default), true when the tutorial is off for the world or the
player, else the gate's condition. Since #2469 the readers are wired: `OpportunityTipsOpen` (the `AfterScanStage` context
tips and the progression sweep; default open), `TickVegaBanterFor` (VEGA's banter; default open) and the `station_near`
tip (default open).

**Graduation** (#2469). A chapter completed by play (not skipped, not `autoCompleteIf` evidence) that finishes the
mandatory part calls `Graduate`: the pack's `graduation.lineKey` (a `packs1` client), `Advance(graduation.counter)` (the
tutorial's "Graduate" achievement), `graduation.suggest` chapters marked offered. A replay graduates only at its last
mandatory chapter (a full restart). Skips (`SkipPack`, `SkipChapter`, the admin's chapter jump) and the migration never do.

**One teaching voice** (#2469, G12). A step's `hintRefs` hold VEGA's matching once-hints (`ShipAiHintOnce`) and context
tips (`TickVegaContextTips`) through `HintHeldByTutorial`, and `SpeakStepStart` books them as told
(`MarkHintsToldByStep`); details in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#one-teaching-voice-g12).

**The chip** (synthesis §5.3, `LiveVegaChip`, still the one fill point `SendVegaWithObjective`): 1 the finale while in the
Guardian system (beats even a pin) → 2 the pinned story (`PlayerState.TrackedStoryId`, `StoryPackIntent{track|untrack}`;
the world story's id pins VEGA's lead) → 3 basic training (a tutorial pack's mandatory chapters) → 4 the VEGA finale →
5 the VEGA lead → 6 a tutorial deep-dive step or any mission-bridge step → 7 the other player packs by priority → 8 empty.
A pack step sends its `chipKey`, counter and target, source `tutorial` (tutorial pack), `mission` (mission bridge) or
`story` (other packs — muted with the hints like VEGA's lead), and the pack in `ShipAiLine.ObjectivePackId` (the client
shows the pack's short name instead of "Objective:" from step 12). `TutorialActive` is true while a tutorial pack's
mandatory part runs.

**Clients without `packs1`** (`JoinRequest.Features`; LAN, self-hosted, a hosted image ahead of Velopack — synthesis
§5.7): the **legacy tutorial mode**. A tutorial step with a `legacyKey` speaks the old `vega.s.<id>.start` / `.done`
**without** `Text` (the old client localizes them and fills its own keys), its chip is `vega.obj.<id>`, the start sends
the old `vega.intro.*` lines (when `vega:intro` was new) and the pack's end `vega.done`; tutorial steps without a legacy
stage and deep dives do not run for it (it may pass a chapter once its legacy MUST steps are done, so the old nine stages
run across the new chapters); a pack chip it could not localize is cleared. Every other pack line reaches it as
server-composed `Text` with an empty `LineKey` (`ComposeLegacyText`: the player's language, `{0}` filled, `{key:Action}`
tokens as device-neutral bracket words `ui.vega.act.<Action>` = "[Use]"/"[Benutzen]"). The composition is the shared
`Shared/Localization/LineText` (`ExpandKeyTokens`, `Compose`, `ComposeNeutral`) that the client's `VegaText` now delegates
to; `LegacyLineTextTests` holds every `vega.*`/`story.*` line brace-free when composed that way and every token action to
a bracket word in EN and DE. A local player (`AddLocalPlayer`) counts as `packs1`.

**Wire** (additive, no protocol bump): tag **297 `StoryPackIntent`** `{PackId, Action, ChapterId, StepId, CutsceneId}` with
`StoryPackActions` `track | untrack | optOut | optIn | startPack | skipPack | startChapter | declineChapter | skipChapter |
restartChapter | skipStep | replayCutscene` (#2467: `replayCutscene` + `CutsceneId` = watch a scene again); while a
singleplayer world is held only `track`, `untrack`, `optOut`, `optIn`, `skipPack` and `replayCutscene` get through
(`PausedMayHandle`, dispatch tests). Tags 295/296 are the cutscene messages — see [CUTSCENES.md](CUTSCENES.md). Fields:
`JoinRequest.Features` (`ClientFeatures.Packs1` / `Cutscenes1`; `NetworkClient.Features` announces `packs1`, and
`cutscenes1` once `NetworkClient.PlaysCutscenes` is set — the Unity client does when its cutscene director exists, #2468),
`ShipAiLine.PackId` / `Speaker` (the line's pack and narrator) / `ObjectivePackId`, `SetWorldRulesIntent.TutorialPolicy`,
`ServerRules.TutorialPolicy`. Persistence (additive, no save bump): `PlayerSnapshot.StepProgress` (null while empty) and
`TrackedStoryId` — an older build drops both on re-save, which costs a running count and the pin, nothing else
(`StoryPackRuntimeTests.AnOlderBuild_DroppingTheCounters_CostsOnlyTheRunningCount`).

**Admin** (world management, not a cheat — the role is the gate, above the cheats switch; the slash parsing in the
client comes with step 12): `AdminCommandIntent{Command = "tutorial", StringArg = "skip" | "restart" | "chapter <id>" |
"policy auto|optin|off", TargetPlayer = <name or empty for yourself>}` — without an installed tutorial pack every verb but
`policy` answers `tutorial_none` (#2469). `story_status` (`/story`) now also lists the world's tutorial rule and the
player packs of the caller (or `TargetPlayer`), and is a read-only report above the cheats switch.

**Validation** (synthesis §5.9): `StoryPackValidator` (Shared) — run by `StoryPackValidationTests` for every
`data/stories/*` pack and the test packs — and its twin `tools/validate_story_pack.py` for authors without .NET (the
successor of `merge_story.py`; allows packs without beats, never rewrites a file, reports every problem at once, warns on
unknown fields). The checks are listed in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#validation).

## Networking (NetCodec tags)

| Tag | Message | Dir | Purpose |
|---|---|---|---|
| 139 | `StoryStateMessage` | S→C | active story, progress %, counts, kills, beats seen, flags; additive `HeardBeats` (this player's beat indexes) since #2462 |
| 140 | `NetFragmentFoundIntent` | C→S | player opened a fragment at a position |
| 141 | `NetFragmentRevealed` | S→C | category + archive text key |
| 142 | `PlayerMemoryRevealed` | S→C | a personal memory unlocked by a machine kill |
| 143 | `GuardianSystemRevealed` | S→C | adds the finale system to the star map |
| 144 | `CoreDialogueMessage` | S→C | the core's claim + rebuttals (duel) |
| 145 | `CoreDialogueChoiceIntent` | C→S | the player's chosen rebuttal |
| 146 | `CoreHackIntent` | C→S | channel one hack tick |
| 147 | `StorySelectIntent` | C→S | admin sets the active story |
| 148 | `NetFragmentList` | S→C | fragments on the current world |
| 149 | `CoreHackProgress` | S→C | hack channel progress + complete |
| 220 | `StoryResolved` | S→C | play the resolution cinematic (win, join catch-up, replay) (#1124); additive `EpilogueTitleKey` since #2461 |
| 221 | `RequestStoryResolutionIntent` | C→S | Story tab "watch the ending again" (#1124) |
| 295 | `CutscenePlay` | S→C | play a pack's cutscene (`PackId`, `CutsceneId`, `Arg`, `HasAnchor` + `AnchorX/Y/Z`, `Replay`); only to `cutscenes1` clients (#2467, [CUTSCENES.md](CUTSCENES.md)) |
| 296 | `CutsceneSeenIntent` | C→S | the client is through with these scenes (`PackId`, `Ids[]`, `Skipped`, `Unsupported`) → the seen ledger; passes a held world (#2467) |
| 297 | `StoryPackIntent` | C→S | the player's control of a player pack (track, opt out, start/skip/replay a chapter, skip a step) (#2466); `replayCutscene` + `CutsceneId` (#2467) |
| 298 | `UiTutorialEvent` | C→S | a UI moment a tutorial step teaches (`Id`: `vega_continue`, `menu_open`, `codex_open`, `map_open`, `waypoint_set`) → `ui:<id>` on the event bus; teaching only, rate-limited, passes a held world (#2469) |

Beat speech reuses `ShipAiLine`. Every message is `Register()`'d in NetCodec.

### The objective chip, the beat list and the story UI (#2462)

All fields below are additive (contractless MessagePack: an older peer ignores them, a missing one reads as its
default) — no `Protocol.Version` bump.

| Field | Meaning | Older peer |
|---|---|---|
| `ShipAiLine.ObjectiveSource` | where the chip's objective comes from — `ObjectiveSources.Tutorial` / `Story` / `Finale` / `Mission`, empty = no objective. Set in ONE place, `SendVegaWithObjective` (every VEGA line carries the chip; the 1 Hz refresh compares the whole `VegaChip`) | an older client keeps its `story.obj.` prefix guess; a new client facing an older server uses the same guess (`ObjectiveSourceRules.Effective`, which now also treats `story.obj.finale` as finale) |
| `ShipAiLine.TutorialActive` | this player's tutorial still runs, whatever the chip shows — the Settings button offers *Skip* then, *Restart* otherwise (B03) | read only when `ObjectiveSource` is set |
| `StoryStateMessage.HeardBeats` | the beat indexes this player has heard (`story:<id>:beat:<n>` milestones), ascending | empty — the client keeps the beats heard live this session |
| `StoryStateMessage.SeenCutscenes` | #2468: this player's confirmed cutscenes of every pack, `<pack>/<id>` sorted (`story:<pack>:cut:<id>` milestones, not the `:sent` form) — the Story tab's "Watch again" list ([CUTSCENES.md](CUTSCENES.md)) | empty — the client lists what it confirmed this session |
| `MissionResult.Outcome` / `Title` / `FreeText` | `accepted` / `completed` / `posted` and the mission's title (locale key, or display text when `FreeText`) for the toast (B24) | empty outcome — the client's toast says "Mission updated" and names the mission from its mission list |
| `SetNpcCallsIntent.VegaHintsMuted` | the client's VEGA hints switch is off (sent at every join and from both settings screens): the server holds advisor hints (Kind 1), context tips and the bandit briefing back WITHOUT burning their once-flags (B31) | absent = hints on (today's behaviour) |

Rules on top of the wire:
- **Chip order.** The tutorial lesson keeps the chip unless the finale is open AND the player is in the Guardian
  system (`FinaleObjectiveOverridesTutorial(p)` = `FinaleOpen && IsGuardianSystemLocation(p.CurrentLocationId)`,
  B11 — it used to be world-wide). Once the tutorial is done the story objective is the finale anyway. Since #2466 this
  is the first rank of the full chip order (pin, basic training, VEGA finale/lead, deep dives, other packs) — see
  "The player-pack runtime" above.
- **Mute.** Only `story` follows the hints switch (`ObjectiveSourceRules.HiddenByHintsMute`); `tutorial`, `finale`,
  `mission` and any source a client does not know yet always show.
- **Beat list.** `StoryBeatLog` (Client.Core) rebuilds the Story tab's *Memories restored* list from `HeardBeats` plus
  the beats heard live (the join sends the snapshot BEFORE its catch-up lines), in arc order, and holds only pack beats
  (`StoryLog.BeatIndexOf`); other Kind-2 lines go to the VEGA log. Missing locale keys render as `ui.story.entry_missing`.
- **Client one-shot lessons** (`VegaPanel.SayLocal`) return whether the line was queued; callers set their
  `ClientSettings.*HintShown` flag only then. Since #2469 the flight lessons the tutorial's B6 steps teach
  (`VegaText.LessonCoveredByTutorial`) count as given without being spoken while the basic training runs.
- **Pause.** `PausedMayHandle` serves `SetNpcCallsIntent`, `SkipOnboardingIntent`, `RequestStoryResolutionIntent` and
  `StorySelectIntent` while the world is held (each with a dispatch test in `StoryUiFixesTests`).

## Cinematics: one cutscene state, skip on every device, the world-hold arbiter (#2461)

The client's story cinematics — the staged prologue (`VegaPanel` + `PrologueCinematic`), the ending
(`ResolutionCinematic`) and the shell intro (`IntroCinematic`) — share three pieces the cutscene director builds on:

- **`GameBootstrap.Cutscenes`** (`CutsceneGate`, `Client.Core`): a static set of open reasons (`prologue`,
  `resolution`, `intro`; `Begin`/`End`/`Set` are idempotent per reason, so overlapping scenes never clear each
  other); `GameBootstrap.CutsceneActive` is "any reason open". Readers: `HudUi` (crosshair included, plus the pickup
  fly-ins), `TouchControlsUi`, `ChatUi` (hidden, and Enter opens nothing), `ScreenLabelLayer` (nameplates and
  health bars), the IMGUI overlays of `FinaleView` and `WeatherFx`, `MemoryFlashback`, `PlayerInteractions`,
  `ContextActionsUi`/`HotbarActionUi`/`GameMenu`/`FeedbackUi` (nothing opens behind the frame), `PlayerController`
  and `SpaceView` (controls freeze like a menu), `AppShell` (Esc belongs to the scene) and `VegaPanel`, which yields
  to every scene except the prologue it presents itself (`ActiveOtherThan(Prologue)`): its canvas is off, nothing
  types or dequeues, and a received-but-pending ending (`ResolutionCinematic.Pending`) holds it too. The prologue's
  reason follows `SetPrologueChrome`; the world's reasons are cleared in `GameBootstrap.OnDestroy`.
- **Skip:** `CinematicFrame.SkipPressed()` (= `InputAction.UiCancel`: Esc, pad B) plus an on-screen button
  (`CinematicFrame.EnableSkipButton`; the prologue uses a skip pill in VEGA's panel), labelled per device by
  `CinematicFrame.SkipLabel` (`ui.cutscene.skip_key/_pad/_touch`). `CinematicFrame` also scales `SetFlash` by
  `FxKit.FlashScale` (Reduce flashes), scales its caption and hint fonts with `UiKit.UserScale`, and draws the hint
  above its black cover.
- **`GameBootstrap.WorldHold`** (`WorldHoldArbiter`, `Client.Core`): holders by name (`menu`, `feedback`,
  `feedback_reply`, `cutscene`) drive the one `PauseIntent` per session; the release goes out with the last holder.
  The server is unchanged. The director's SP hold should register as `WorldHoldArbiter.Cutscene`.

The ending: it waits for no menu, no loading veil and no other cutscene; on receipt it withdraws its epilogue line
from VEGA's queue (`VegaPanel.WithdrawLine` — the story log keeps it); its epilogue title is the pack's
`epilogueTitleKey` (`story.vega.epilogue.title`, mirrored in `StoryRegistry` and the no-drift test; the raw
`epilogueTitle` stays for older clients); `ClientMusic` plays the resolution track whenever the cinematic starts
(`ResolutionCinematic.Plays`) — no longer on the first sighting of `GuardianDefeated`, which replayed it at every
session start of a won save. The prologue's staging is retried for ~4 s while the ring streams in (`StagingRetry`),
its whole shot scales with the player's ship (`PrologueOrbit.Scale`), and the camera spot avoids the landed hull
(`CinematicStageScan` overloads with a separate spot sampler).

## Key files / classes

- Engine (story-agnostic): `src/BlocksBeyondTheStars.Shared/Story/` — `StoryDefinition`, `StoryBeat`,
  `StoryState`, `StoryEngine`, `StoryRegistry`, `CoreArgument`/`CoreArgumentChoice`; story packs (#2465):
  `StoryCondition`/`StoryConditionParser`/`StoryWorldPredicates`, `GameEventKeys`, `StoryPackKeys`; the pack manifest
  (#2466): `StoryPackManifest.cs` (`StoryChapter`, `StoryStep`, `StoryNarrator`, `StoryEscalation`, `StoryLegacy` …),
  `StoryPackValidator`; `Shared/Localization/LineText.cs` (line composition, shared with the client). Server:
  `GameServerGameEvents.cs` (event bus), `GameServerStoryConditions.cs` (condition context + world predicates),
  `GameServerStoryPacks.cs` (the player-pack runtime, chip helpers, `/tutorial`), `GameServerTutorial.cs` (#2469: the
  teaching verbs, `UiTutorialEvent`, the tutorial's predicates, the hint hold, the migration of the old onboarding).
- Data: `data/stories/tutorial/` (#2469) — the basic training (`story.json`, `locales/en.json` + `de.json`); content table
  in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#the-tutorial-pack-datastoriestutorial-2469).
- Server: `GameServerStory.cs` (state load, Record fragment/kill/milestone, beat reveal, admin selection,
  knowledge level), `GameServerNetFragments.cs` (placement + pickup), `GameServerStoryFinale.cs` (reveal,
  hack, duel, gauntlet, chamber stamping, respawn, pacification).
- Persistence: `story_state` table (`StoredStoryState` + `IWorldRepository.SaveStoryState`/`ListStoryStates`).
- Data: `data/stories/vega_protocol/` — `story.json` (beats, fragments, memories, flavour, threads, duel),
  `lore_sites.json` (environmental lore, #1111) and `locales/<code>.json` in all 14 languages (EN + DE mandatory and
  complete; the twelve community tables are guarded by `CommunityLocaleTests`). There is no separate `lore.json`
  catalogue — the authoring schema in LORE_STRUCTURE §E maps onto `story.json`. Validator:
  `tools/validate_story_pack.py` (#2466, replaces `merge_story.py`).
- Client: `Mode.Story`/`Tab.Story` Story Log tab (meter + re-readable beats/fragments/memories);
  `WorldEntities.cs` three-eyed-robot retheme + `BuildDrone` scan-drone; `FinaleView` (hack bar + duel panel);
  `ClientMusic` finale contexts → `music_boss_*` tracks.
- World options: `GameRules.StoryId` + `GameRules.StoryDensity` (`--story` / `--story-density`, world-creation
  panel), set when the world is created and baked into its rules. The density is not live-editable
  (`SetWorldRulesIntent` carries no story field); the active pack can be switched by an admin
  (`StorySelectIntent`, see "Active story row" above). `GameRules.TutorialPolicy` (`--tutorial auto|optin|off`, #2466) is
  baked the same way but live-editable (`SetWorldRulesIntent.TutorialPolicy`, admin `tutorial policy …`). Admin QA cmds:
  `advance_story`, `reveal_finale`, `story_status`, `reveal_lore`, `goto_core`, `tutorial`.

## Story and VEGA text conventions (#2463)

- **Canon.** Every VEGA line follows [LORE_STRUCTURE.md](LORE_STRUCTURE.md) — including VEGA's older memory arc
  (`vega.mem.1..10`, redeemed from `ai_memory_fragment`, still in the core locale and not story-gated). Its texts were
  reconciled with the pack in one approved canon pass: the Meridian is an SPS survey fleet; the fleet did not scatter,
  it was stopped ship by ship; VEGA's captain cut her ship loose at VEGA's own request; the last fragment brings back
  her schematics, not every memory. The player memory `mem_the_order` is "you were in the room when the order went
  out" — every clone remembers being present, nobody gave *the* order (no chosen one). The SPS has one long form,
  **Scout and Pioneer Service**, in every language.
- **Keys.** Player text never names a key; it uses `{key:Action}` tokens (an `InputAction` name). `VegaPanel`, the
  HUD prompts and the `@srv.*` server messages (`GameBootstrap.ResolveServerToken`) resolve them through
  `HudUi.KeyTokenGlyph` — the binding, the pad button (the Actions list for an
  action without one), or on a tablet the layout's own button (`VegaText.TouchControlLabel`: USE, MAP, ≡ …) and else
  "the on-screen action menu". `VegaLineBudgetTests` (Client.Tests) fails on a token that names no `InputAction` and
  on a literal key in the VEGA lines and the converted prompts.
- **Length.** One VEGA panel page holds about 220 characters. English VEGA lines (`vega.*`, `story.vega.*`) stay at or
  under 220 shown characters, German under 264 (`{key:…}` counts as 3, `{0}` as 10); the epilogue
  `story.vega.beat13` is exempt — the ending card shows it, not the panel.

## Mission chains schema (#1212)

Authored missions (`data/missions.json`, and a story pack's own missions once packs carry them) may form
chains. All fields are additive — a stand-alone mission leaves them empty; the vocabulary lives in
`Shared/Missions/MissionChains.cs` and the content validator rejects unknown values / dangling ids.

| Field | Meaning |
|---|---|
| `chainId` | groups the steps of one chain |
| `step` | 1-based position; two defs sharing `chainId` **and** `step` are *alternatives* — the feasible one with the lowest id is offered (e.g. `chain_needs_4a_camp` on bandit worlds, `chain_needs_4b_travel` elsewhere) |
| `prerequisites[]` | mission ids that must be turned in first |
| `nextMissionId` | the following step (drives the giver's `npc.call.chain_next` radio nudge after turn-in) |
| `giverRole` | `quartermaster` (default) · `vendor` · `settler` · `character:<id>` (an authored recurring face — remembers the player globally) |
| `minStage` | `""` · `known` · `trusted` — standing with the giver (NPC memory) |
| `surface` | `board` (default, at a mission board) · `dialog` (handed out by a dialogue choice with consequence `mission:<id>`) · `radio` (offered anywhere) |
| `offerAt` | `settlement` (default) · `station` · `any` — which boards offer a board step |
| `requiresStory` | `""` (default) · `guardian_defeated` — a gate on the **world**, not on one giver's opinion of the player (#1213). Checked first in `ChainStepAvailable`, so a gated chain is simply invisible until then |
| `repeatable` on the **last** step | restarts the WHOLE chain: `RestartChainIfRepeatable` drops every progress row of that chain on turn-in, so step 1 is offered again (#1213). It deliberately does **not** use `HandleTurnInMission`'s ordinary `def.Repeatable` branch — that removes only the one row, and a chain's rows are exactly what the next run's prerequisite check reads |

Per-player progress (`MissionProgress`) carries `chainId`, `acceptedFrom` (the place key the chain is bound
to — later steps are offered and turned in there) and `acceptedBodyId` (drives the relative Travel target
`other_body`). `Persistence/Snapshots.CloneProgress` copies them. The rule itself is
`GameServerMissionChains.ChainStepAvailable` — used for the Available list **and** enforced on accept, so a
client cannot skip ahead. Dialogue-granted steps: a dialogue whose choices hand out a mission is only picked
while that mission can be taken from that NPC; declining sets it aside for the session.

### Epilogue content — "SPS Survey Orders" (#1213)

The post-finale chain `relay_survey` (4 steps, `offerAt: station`, `requiresStory: guardian_defeated`) is what
the boards have to offer once the Guardian is down, so the ending opens the endgame instead of closing it —
the counterpart to the Remnant Protocol (#1206), which keeps the machines around to fight.

Two objective mechanics arrived with it:

- **`MissionObjectiveType.Contribute`** — event-tracked from `HandleContributeRelay` via `OnRelayContributed`.
  It cannot be a `Deliver`: Deliver is inventory-checked **and consumed** at turn-in, and items poured into a
  relay are already gone, so the objective would sit at 0 forever. Per-player, like Scan — the relay meter is
  shared world state, but the *order* belongs to the contributor. Not offerable to player-created missions.
- **Travel target `unlinked_system`** — satisfied by arriving in a system that holds no completed relay and is
  not the system the order was taken in (`ArrivedInUnlinkedSystem`). Like `other_body` it is resolved server
  side, because authored content cannot know a generated galaxy's ids.

The `Defeat` target `machine` counts Guardian machines — both kill sites go through the single guarded
`OnMachineDefeated`, which only fires in the `RemnantEra`: pre-win, the very same kills drive the story's own
pacing (`RecordStoryMachineKill`) and a board mission must not double-dip on that.

`ChainStepFeasible` keeps each step's promise: no unlinked system left, no relay left to build, or hostiles
switched off in the world rules → that step is never offered rather than offered and unachievable.

## Adding another storyline

Add a pack under `data/stories/<id>/` — no engine edits. A pack is "story-complete" only once its beats,
fragments, memories, flavour and `coreArguments` are authored + translated (DE+EN), per LORE_STRUCTURE.

## Known remaining gaps / deferred

- Client/world-gen: the two physical finale routes + in-world core console, and bespoke boss/core visuals
  (the voxel chamber already reads well) are follow-ups.
- ~~A proper re-readable Fragment/Memory reader panel~~ — shipped with #1110: `StoryReaderUi` (modal,
  chunked via `UiTextChunks`), Read buttons in the Story tab, and a rejoin-proof snapshot on
  `StoryStateMessage` (`FoundFragmentKeys` / `PlayerMemoryKeys` / `FoundLoreKeys`, built per receiver).
- ~~Pity/budget + structure-placed fragments~~ — shipped with #1109: `StoryState.BodiesWithoutFragment`
  (persisted; after two dry bodies the third guarantees a fragment, any find resets it), and
  `data_terminal`/`relic_cache` markers roll a fragment per residency (`TryPlaceStructureFragment` —
  deterministic from the marker position, found keys never return). VEGA's `fragment_signal` context tip
  gives a bearing and reveals a `fragment_signal` map POI; after the tutorial the objective chip carries
  a `story.obj.*` line (gated client-side by the VEGA-hints setting).
- Environmental lore (#1111): `data/stories/<pack>/lore_sites.json` — weighted, knowledge-gated texts per
  site kind (monument scan / structure loot), revealed once per player (`PlayerState.Milestones`
  `lore:<key>`), listed in the Codex "Lore" chapter. NPC story threads (#1112): `npcThreads[]` in the
  pack — role + relationship stage + knowledge gate, once per player, may hand over a fragment.
- Numeric tuning (`Wf/Wk/Wm`, kill cap, thresholds, memory-drop chance, gauntlet HP) stays data-driven.

## Appendix — boss/finale music

Five staged instrumental tracks (`music_boss_{approach,gauntlet,hack,dialogue,resolution}.mp3`, generated in
Suno.ai per [MUSIC_TRACKS.md](MUSIC_TRACKS.md)) are wired to the matching `ClientMusic` finale contexts.
