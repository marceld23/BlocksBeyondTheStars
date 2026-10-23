# Story engine ("The VEGA Protocol") — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-10-12.

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
scaled by a per-world density multiplier (Sparse/Normal/Dense = 0.65/1.0/1.5) — and, since #2475, on a world without
machines by the peaceful-world factor (one wrapper, `StoryScale`; see "Chapter leads, the chapter bonus and
peaceful-world pacing"). Beats fire when the score crosses a threshold (not on a specific find), so a linear arc works in
a random world. Diminishing returns / a kill cap stop combat grind from trivialising pacing.

**Three progress drivers** all feed the same score:
- **Net fragments** — text-only pickups placed deterministically on planet surfaces (datacube-style) from
  the active pack's still-needed, weighted pool; reach-checked pickup opens an archive-text reader and
  advances the arc. Distinct from `ServerDataCube` knowledge mini-games.
- **Machine kills** — defeating a Guardian machine (planet enemies + space drones/UFOs; organic fauna
  excluded) advances the story and has a chance to drop a **player memory** (a per-player, non-contradictory
  personal unlock; each player is a different imprint).
- **Milestones** — repeatable: mission turn-in (settlement helped; not a teaching chain — `countsForStory: false`, the
  Crystal workshop since #2475) and first-discovery system mapping (never
  the player's first, spawn system — #2460: every new identity used to add +2 just by joining);
  **once per save** (#1105, `RecordStoryMilestone(onceKey)`, keys persisted in `StoryState.MilestoneKeys`):
  first base founded, first player station commissioned, first self-built ship commissioned, first companion
  tamed, first hyperjump, first vault opened (`vault:first`, #1155), first monument scanned per world
  (`monument:<body>`). Pacing note: fragments cap
  at 6 (18 pts) and kills at 40, so before #1105 the remaining ~130 of the 204 needed came almost entirely
  from delivery turn-ins; the once-firsts add roughly 10–20 pts per playthrough from building, taming and
  exploring — they widen the arc's inputs without making any of them farmable. Since #2475 the chapter leads add a
  once-per-save bonus per chapter (`lead:<chapter>:1..3`, at most 36 pts before B12).

**Beats** are spoken through the existing VEGA narrator pipeline (`ShipAiLine`, Kind 2) — always in VEGA's
voice: a pack has no narrator/speaker field yet (the story-packs manifest adds one, #2459), so no new beat UI.
A beat's text must not assume where the player is: any score event can cross a threshold — a drone kill in
space, a mission turn-in, a mapped system — so B1 speaks of "a signal out there", not "this wreck" (#2463).
Per-player "seen beats" reuse `PlayerState.Milestones` (`story:<id>:beat:N`)
— no schema migration. Milestones are append-only (since #2469 even the tutorial restart keeps every key — it replays
the tutorial pack's chapters; only a replay's own keys go when it ends). Latecomers get a join-time catch-up of
earned-but-unseen beats (`CatchUpStoryLines`),
plus the Guardian-system reveal line while the finale is open (`story:<id>:finale:revealed`, #2460). Since #2470 a beat
reaches a player only once their **story track** has started (see "VEGA as its own pack" below): until then it is held —
no key, no knowledge, no line — and the track start delivers the backlog as one recap.

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
   calls `MarkGuardianDefeated`. Since #2478 only a player standing on the core world answers (B50), and the client shows
   the duel as the cutscene director's dialogue mode with its own answer buttons there (see below).
6. **Pacification → Remnant Protocol (#1206):** the one-way `guardianDefeated` flag despawns the live planet
   machines of every loaded world at the moment of the win (the dramatic beat; #2478, B51 — it used to be only the active
   world cursor) and then acts as a *factor*, not a switch: the planet
   cap halves (floor one, `PlanetEnemyCap`), refills run at twice the interval (`RemnantPaceFactor`), only
   scan-drones spawn (`RemnantEra`); the finale gauntlet stays down; ambient space waves survive only in
   pirate havens and the frontier-danger tier (`RemnantSpaceHostile`); bandit ships keep only pirate havens
   (`BanditShipsAllowedIn`), so the raider bounty stays offerable. It does **not** touch the admin threat
   sliders; PlanetEnemies *Off* still means zero. A death in the boss system respawns the clone on the world
   it launched from (no death-loop).

**Ending catch-up.** Winning gives the finale achievement to everyone aboard and delivers the ending (with the
`finale_resolved` and epilogue lines) to every player whose story track has started (#2470) — since #2478 as the
stand-down set piece and the `resolution` scene on the cutscene director for a client that plays cutscenes, else as
`StoryResolved` (never both; `DeliverResolution`). "Watched" is the cutscene ledger key `story:<id>:cut:resolution`
(stamped by the old cinematic when sent, by the director's receipt); "handled" is that key or its `:sent` key, so an
unconfirmed ending comes back through the cutscene catch-up, never as a second delivery. `story:<id>:resolved` is kept as
the legacy key older builds read. On a later join a started player who has not had it gets it once — the achievement
only when they had heard the arc's last beat **before** that join (#2460, E23; #2478: also an observer who had heard it,
when observing ends — `ExitSpectate`; the achievement counts once, `GrantFinaleAchievement`). A player who first meets the
story after the win is not played the ending at the door (N4): it waits for their track start (after the basic training),
and on a hosted or arcade world (personal chapter 1) it stays in the Story tab.

**Knowledge level** (`WorldKnowledgeLevel`, gates lore, flavour lines and NPC threads): 0–3 follow the arc's
progress (1 > 0 %, 2 ≥ 33 %, 3 ≥ 66 %); **4 is the win alone** (#2460) — its lines say the machines went still.

## Join snapshot, story rows and observers (#2460)

- **Join snapshot.** `PlayerSession.Join` (`JoinSnapshot`) is taken once at the very top of the join, before
  `SetupPlayerShip`, the Creative grants and the beat catch-up change the player state: `Veteran` (knowledge,
  blueprints or missions on a world whose *saved* Creative option — `_meta`, not the launch flag — is off),
  `HadOnboardingIntro`, `Newcomer`, and the `story:` keys the player carried (`HadStoryKey`). The prologue reads it
  (since #2470: it plays for a player who arrived with no key of the story at all, `StoryOnJoin`) — re-evaluating after
  the catch-up had paid beat knowledge skipped the prologue and the tutorial for every newcomer on a progressed world —
  and so do the ending catch-up, the story track's veteran rule and, since #2469, the tutorial pack's one-time migration
  (a save from before VEGA's lessons). Later packs reuse it.
- **Active story row.** A save holds one `story_state` row per pack it ever ran. `PersistStoryState` stamps the
  active row (`StoredStoryState.Active = true`, `UpdatedUtc`), an admin switch clears `Active` on the row it left,
  and `SelectActiveStoryRow` picks: rows of installed packs (or `none`) over unknown ones → the active marker →
  the newest stamp → for older saves the fresh-save choice, else `none`, else the default pack. A row of an
  uninstalled pack never costs real progress. Additive JSON fields — no save-version bump.
- **Switching the main story keeps progress (#2506).** `SetActiveStory` (the admin's `StorySelectIntent`, the in-game
  "Main story" row) no longer starts the chosen pack over: the story being left is persisted one last time and steps back
  (`DeactivateStoryRow`); a pack the save already ran is **restored** from its row (`ApplyStoryRow` — fragments, kills,
  milestones, revealed beats, once-keys, the Guardian flags), marked active and stamped by the next persist; a pack the
  save never ran starts fresh (B0) as before; `none` works as before. Re-selecting the running story is a no-op (it used
  to reset in place) — except while the save's own pack failed to load (#2489), where choosing the running fallback adopts
  it. Restoring a pack that only the built-in stand-in can run re-activates its row without writing the stand-in over it
  (the hold stays). The switch also writes `GameRules.StoryId` into the save's rules (like a live rule edit), so rules
  and active row agree; at a restart the active row still decides. Players' own keys (`story:<id>:*`, step progress,
  owed `:sent` scenes) are never touched, so on switch-back `StoryTrackOnStorySwitch` meets the online players: the
  migration and prologue for newcomers, the start rule, and for a player whose track had started before the catch-up
  (`CatchUpStoryLines`, the missed ending) and a fresh help clock for the story's running mission steps. A restored
  revealed-but-unwon finale puts the Guardian system back (`EnsureGuardianSystemInGalaxy`). Everyone online gets a
  system line (`srv.story.switched_off` / `srv.story.switched_on`, server-localized).
- **While no story runs (`none`)** nothing of the left story runs: the beat/insight/relay/Guardian lines, the lead
  listener (`SyncLeadListener`), the chip's story objective, story missions (`StoryPackEnabledFor` is false for the world
  pack), chapter offers, the Journal's world-story part, NPC story flavour (knowledge level 0). The world's traces of the
  story are hidden, not removed: net fragments are not sent or pickable (`SendNetFragments`, `HandleNetFragmentFound`,
  the `fragment_signal` POI, `world:fragment_on_world`); the Guardian system stays in the galaxy (no one is stranded)
  but is off the star map and no travel or hyperjump leads there (`GuardianSystemHidden`), its gauntlet does not spawn
  and the `guardian_core` POI is off; a scene of the story still waiting in a player's cutscene queue stays there with
  the lines it holds (`WaitsForItsWorldStory`) and plays when the story is back. The Remnant era follows the restored
  `GuardianDefeated` flag, so a won world's calmer machines return with the story. Known limits: fragments are not
  stamped on worlds first loaded while `none` ran (they roll at the next load), and an owed scene of a player who joined
  while `none` ran waits for their next join. The client shows the "Main story" row only when the admin's
  `ServerRules.MainStory` is filled (sent to world admins only, by a server with this behaviour) — an older server would
  reset the story on `StorySelectIntent`.
- **A pack that fails to load (#2489).** `ContentLoader.LoadStoryPacks` reads each pack folder under its own
  error handling: a `JsonException`/`IOException` in `story.json`, `lore_sites.json` or `cutscenes.json` is reported
  through the loader's `error` callback (the dedicated server logs it as an ERROR naming the pack folder and the file)
  and skips the whole pack, its locale files included — one malformed pack used to abort the whole content load. The
  skipped folder names (and the id, when the manifest parsed) land in `GameContent.FailedStoryPacks`. If the save's
  own story is such a pack (the row `SelectActiveStoryRow` would pick with it installed, or a fresh save's rule), the
  server runs the default story for the session but `PersistStoryState` writes nothing (`_storyRowHeldFor`), so a
  restart with the pack repaired carries on with the save's own row; an admin's story switch ends the hold (and
  deactivates the held row like any switched-away one) — unless it switches back to a pack only the stand-in can run,
  which re-holds that pack's restored row (#2506). A pack that is simply not installed keeps the old rule.
- **Observers.** A fleet admin in observer mode (`/spectate`) is not a story listener (`StoryListeners()`): no
  beats, story lines, finale rewards or VEGA tick while observing; `MarkArrivedOnBody` books a `/goto` arrival
  silently (no milestone, no frontier growth, no knowledge); `player_near` never names them. `ExitSpectate`
  runs the catch-up. Since #2493 a **visible** fleet admin's `/goto` is booked the same way (`HandleTravel`'s
  `adminTeleport`: no hyperjump achievement or milestone, `MarkArrivedOnBody(silent: true)`); the admin's finale
  shortcut (`goto_core`), which drives the arc on purpose, keeps the full arrival.

## VEGA as its own pack: prologue, story track, held content, chapters, migration (#2470)

The arc stays world-shared (`StoryState`: score, beats, finale — unchanged). What is per player is **when the story
reaches them**: the **story track** `story:<id>:started`. Server code: `GameServerStoryTrack.cs`; the pure chapter
arithmetic: `Shared/Story/StoryChapters.cs`.

**Manifest** (`data/stories/vega_protocol/story.json`, new fields only; every one mirrored in
`StoryRegistry.BuildVegaProtocol` and compared by `StoryPackLoadTests`' no-drift test): `kind: story`, `scope: world`,
`shortNameKey: story.vega.short`, `narrator` (vega, `ui.vega.name`, `icon_vega`, `#5FD8FF`, the three chatter clips,
`ai_blip`), `introCutscene: prologue`, `start: { after: [chapter:tutorial:b6], earlyOptIn: true }`, the chapters below and
`insights[]` (the companion-ward and shape-anomaly insights: event, `minBeat` 5, line, once-key — the hard-coded "six
beats" check is gone; `RevealStoryInsight`).

| # | Chapter id | Title (EN / DE) | Reached by |
|---|---|---|---|
| 0 | `awakening` | Awakening / Erwachen | B0 (the prologue: its `openCutscene` is the intro) |
| 1 | `signature` | A Familiar Signature / Eine vertraute Signatur | B1 |
| 2 | `service` | The Service / Der Dienst | B2 (B2–B4) |
| 3 | `guardian` | The Guardian / Der Wächter | B5 |
| 4 | `verdict` | The Verdict / Das Urteil | B6 (B6–B8) |
| 5 | `what_you_are` | What You Are / Was du bist | B9 (B9–B10) |
| 6 | `sleeps` | It Still Sleeps / Er schläft noch | B11 |
| 7 | `core` | The Guardian Core / Der Wächterkern | B12 (+ the Guardian reveal) |
| 8 | `resolution` | Resolution / Auflösung | `fromEvent: guardian_defeated` |

Titles are `story.vega.ch.<id>`. A chapter is **reached** when the arc revealed its `fromBeat` or raised its `fromEvent`
(`StoryChapterEvents`: `guardian_revealed`, `guardian_defeated`); the **world's chapter** is the last one reached; a
player's chapter is **derived, never stored**: `max(1, world)` once their track started (`StoryChapters.TrackChapterIndex`).
The validator (C# and Python) refuses a world chapter whose `fromBeat` is outside the arc, an unknown `fromEvent`, and
chapters out of beat order. The condition atom `chapter:vega_protocol:<id>` holds once the player's chapter is at or past
it. Chapter openings (`openCutscene`, bound since #2474 — `ch1.open` … `ch7.open`, see
[CUTSCENES.md](CUTSCENES.md#vegas-chapter-scenes-and-beat-cards-2474)) play at the beat that starts the chapter, before
its line; the beats that open no chapter (B1, B3, B4, B7, B8, B10) carry a beat card (beat `cutscene`).

**Prologue** — the cold open of the game (synthesis §5.3 "Prolog vs. Spur"). It plays once (ledger
`story:<id>:cut:prologue`, sent or seen) for a player who arrived with **no** `story:<id>:*` key (the join snapshot): a
newcomer, or everyone on a world just switched from "none" to the story (`StoryTrackOnStorySwitch` for the players
online). Never on a tutorial restart; replayable from the Story tab ("Prologue — Awakening"). It runs in `StoryOnJoin`,
before the beat catch-up and before the tutorial pack starts. B0 — VEGA's first words — is its speaker page and counts as
heard with it (no second "VEGA wakes" line). A `cutscenes1` client gets the director scene (see
[CUTSCENES.md](CUTSCENES.md#the-vega-prologue-2470)); an older client (or the built-in fallback pack, which carries no
cutscenes) gets the five old full-screen pages (Kind 4): `vega.prologue.1..3`, B0, and the call to adventure
`story.vega.call` as server-composed text. The prologue keys live in the pack locale now (names kept).

**Start rule** (`StoryTrackStartReason`, evaluated at the join after the player packs, at 1 Hz, after every player-pack
advance, on arrival in the finale system, when observer mode ends and on a story switch):
- the pack's `start.after` holds (VEGA: the basic training's last part done or skipped) — or no tutorial pack still runs
  its mandatory part for the player (none installed, tutorial `Off`, an `OptIn` world they never opted into, opted out);
- a beat beyond B0 already heard (a player from before the pack);
- a veteran by the join snapshot who is not in the middle of the basic training;
- "Start the story now": `StoryPackIntent { PackId: <world story>, Action: startPack }` (pause-whitelisted), allowed by
  `start.earlyOptIn`;
- the player stands in the Guardian system (never leave a player in the finale without context).
A "none" world never starts a track.

**Held until the track starts** (synthesis §4.6 sender table):

| Content | Sender | Held how |
|---|---|---|
| beat lines + knowledge | `RevealBeatTo` | no key, no knowledge — credited at the start |
| Guardian reveal (jump-generator line) | `SpeakGuardianRevealedTo` | key not written — spoken at the start while the finale is open |
| finale resolution + epilogue lines | `SpeakStoryLineToStarted` | not spoken — the ending at the start carries them |
| insights | `RevealStoryInsight` | once-key not written — fires at the next occasion |
| relay insights (`vega.relay.*`) | `SpeakRelayInsightOnce` | not spoken — `StoryStateMessage.RelayInsightKeys` puts them in the log silently |
| hack meter, duel nodes | `BroadcastToFinaleAudience` | only players in the Guardian system, started players and observers |
| live ending | `WinDuel` | deferred to the start (the achievement is everyone's aboard, as before) |
| fragment / lore / memory readers, NPC threads, dialogs, VEGA's old memory arc | player-triggered | **not** held |
| star-map marker of the Guardian system | `BroadcastStarMap` | **not** held (world state) |

The chip's story ranks (4 finale, 5 lead) and a pinned VEGA also need a started track, so the story never flashes on the
chip during the prologue, a read step's line or the gap between two lessons. VEGA's `fragment_signal` tip waits for the
track too.

**Track start** (`StartStoryTrack`, §5.3 "Spurstart-Folge"):
1. every revealed beat the player has not heard is heard now, its knowledge credited now; the story state follows (log,
   chapter, relay insights);
2. "The story so far" (`__recap`, titles of chapters 1 … current − 1) when the world is past chapter 1 and the player
   catches up on held beats (a player from before the pack lived through them — no recap);
3. only the CURRENT chapter's opening (#2474) — its scene shows the beat that starts the chapter; chapter 1's card shows
   the title only (a track may start before B1), so B1's card follows it at once when B1 was crossed
   (`PlayOpeningBeatAfterTitle`). A client without cutscenes (stamped seen) or the built-in fallback pack (no scenes) hears
   the newest beat as a line instead. The recap chains straight into the pack's next chapter scene on server and client
   (`StoryCutsceneIds.AnyOfPack`), never into the intro;
4. with the finale open the Guardian reveal (the jump-generator line — held with `ch7.open`, which shows it);
5. on a won world the ending (unless watched) — with the achievement for whoever had heard B12 before this join;
6. the chip shows the story's objective.

**Hosted and arcade worlds** (E29; `PersonalChapterOneFor`: the instance runs behind the WorldHost join gate, or the
player is an arcade guest with an install id): a newcomer catching up gets a **personal chapter 1** — chapter 1's opening
and B1's card (#2474), no automatic recap, no automatic ending; the Story tab shows "The crew of this world is in
chapter N" (`StoryStateMessage.ShowCrewChapter`) and "The story so far" on demand.

**Migration** (`MigrateStoryTrack`, lazy at the join, idempotent, marker `story:<id>:migrated:v1`; synthesis §4.10):
- a player without any key of the story is a newcomer to it — nothing to migrate (the prologue rule takes them);
- P1–P3: the old prologue pages (or the old veteran path) count as the prologue seen; mid-tutorial players start the track
  after the basic training, the others by the start rule at this join (all stages → training through; a heard beat ≥ 1);
- P2–P4: the openings of chapters before the world's and the scenes of heard beats count as seen; the current chapter's
  opening stays owed and plays once at the start; P4 (finale open) gets the reveal line and the finale chip;
- P5: `story:<id>:resolved` from an older build meant "watched" → `story:<id>:cut:resolution`;
- P6: a "none" world migrates nothing and writes no marker; P7: `vega:mem:N` stay (the `mk3parts` flag never counts);
  P8: several story rows — `SelectActiveStoryRow` (#2460); P9: players the old multiplayer bug auto-skipped get the
  tutorial pack's one refresher offer (`MigrateOfficialTutorial`) — the story adds none, so it is offered once.

**VEGA-pack-bound extras.** VEGA's old memory arc (`ai_memory_fragment` → `vega.mem.N`) speaks VEGA's memories only while
the VEGA story runs (`VegaStoryActive`); otherwise the same redemptions read as neutral archive lines
(`vega.mem.archive.1..3`) — knowledge and the Mk3 parts unchanged, no score, no cutscenes, never held. The relay insights
are silent without the VEGA story (their once-guard stays unspent, so a world switched to VEGA later still hears them) and
the `guardian_core` landing hint without a story. The LLM banter's situation mentions VEGA's memory only under the VEGA
story.

**Client.** The Story tab (`CraftingTechShipUI.BuildStoryList`) lists the chapters (`StoryChapterList`, Client.Core):
reached ones titled with the beats heard in them, future ones "???", only the prologue before the track starts; before
the start a hint and **Start the story now**; the crew status line; **The story so far** (the recap card, played
locally) from chapter 2 on. "Watch the ending again" waits for the player's own track. Relay insights from
`RelayInsightKeys` join the VEGA log.

Test seams: `StartStoryTrackForTest` (the full start sequence — legacy story tests use it for "a player in the story"),
`TryStartStoryTrackForTest`, `StoryTrackStartReasonForTest`, `StoryOnJoinForTest`, `MigrateStoryTrackForTest`,
`WorldChapterIdForTest`, `TrackChapterIdForTest`, `WinDuelForTest`, `FinaleAudienceForTest`, `SpeakRelayInsightForTest`.
Tests: `VegaStoryPackTests`; Client.Tests `StoryChapterListTests`.

## Chapter leads, the chapter bonus and peaceful-world pacing (#2475)

Synthesis §4.7–§4.9, option O2: **the score stays the clock, the chapter leads are the compass.** A lead never holds a
beat back — beats still reveal by score. Code: `src/BlocksBeyondTheStars.GameServer/GameServerStoryLeads.cs`; vocabulary
and rules: `src/BlocksBeyondTheStars.Shared/Story/StoryLeads.cs` (`StoryLeadTypes`, `StoryLeads`, `StoryMemoryTriggers`).

**Data.** Each world-story chapter lists its leads (`chapters[].leads[]`, `{ "type": "<word>[:<arg>]", "objKey": "…" }`;
`objKey` optional — the built-in `story.vega.obj.*` key of the word otherwise). Pack fields: `leadBonusMilestones`
(VEGA 3), `normaliseUnavailableDrivers` (VEGA true), `memoryTriggers` (VEGA `["lead", "monument"]`). All three default to
the old behaviour and are mirrored in `StoryRegistry.BuildVegaProtocol` (no-drift test). VEGA's leads:

| Chapter | Leads |
|---|---|
| 1 signature | `fragment` |
| 2 service | `unique_site:observatory`, `fragment` |
| 3 guardian | `monument`, `settlement_help` |
| 4 verdict | `unique_site:derelict`, `heal_tank` |
| 5 what_you_are | `research:radar_array`, `settlement_help` |
| 6 sleeps | `research:jump_generator`, `module:jump_generator` |
| 7 core | `finale` (becomes `finale_jump` without a jump generator outside the Guardian system) |
| 8 resolution | `relay` (post-game, no bonus) |

**Vocabulary v1** (feasible = can be followed from where the player is; followed = the per-player key is written):

| Word | Feasible | Followed by | Marker |
|---|---|---|---|
| `fragment` | an unfound net fragment on the player's current world | a NEW fragment find (`story:fragment`) | the fragment's shared map signal, as before (`RevealFragmentSignals`) |
| `unique_site:observatory\|shrine\|derelict` | the site exists in this save (a surface site's world pinned it, or was never edited — a world stamped before #1129 recorded a skip and never gets it) and the player can reach it (same system, a jump generator, landed there before, instant travel) | landing there / boarding the derelict (`visit:body`, `station:boarded`), or evidence: landed there before | a lead marker on the site's world, else the space waypoint for a body of the player's system |
| `monument` | a monument on the player's world whose runes they have not read (scan ledger) | a first reading of a monument (`scan:any` = monument) | a lead marker at the monument |
| `settlement_help` | a settlement on the player's world | a board or chain job turned in that counts for the story (`mission:turned_in`) | a lead marker at the nearest settlement |
| `machines` | the world's rules field machines | a machine destroyed (`defeat:any` machine / space machine) | — |
| `heal_tank` | the player's ship is landed on their world | standing within 3.5 m of its heal tank (1 Hz check) | a lead marker at the tank |
| `research:<bp>` | the blueprint exists | `research:any`, or evidence: already researched | — |
| `module:<key>` | the module exists | `module:built`, or evidence: already fitted | — |
| `finale` / `finale_jump` | the finale is open | (the win changes the chapter) | — (the Guardian system is on the star map) |
| `relay` | the finale is won | `relay:commissioned` / `lane:linked` | — |
| `explore` | always | never | — |

**The lead marker** (#2742, `MarkCurrentLead`): when the current lead (or a story mission's lead bridge on the chip) has a
spot on the player's world, it gets a persistent private **story marker** — the same `NetMarker` (`Story = true`,
`Ping = true` for older clients) as a step's help marker, id `story:lead:<lead identity>`, `LabelKey` = the lead's
objective key with `LabelArg` filling its `{0}` (the body of a unique site), `PackId` = the world story. It lives on the
lead cache (`StoryLeadCache.MarkerLocationId` / `MarkerAt` / `MarkerKey` / `MarkerArg` / `MarkerFor`, RAM only), is part
of `StoryMarkerSignature` (so `SyncStoryMarkers` pushes every change), shows only to its player while they are on its
body, is hidden while the VEGA hints are muted (un-muting brings it back), and goes when the lead is followed
(`CompleteLead`) or the current lead changes. Until #2742 it was a 30-second private ping raised once per lead, usually gone
before VEGA's line was read and never back; the private-ping path is removed.

**Resolution** (`LeadsFor(s)`): only for a player whose story track started; the current chapter's effective leads
(`StoryLeads.EffectiveLeads`: the chapter's own list, then the stand-ins `fragment`, `settlement_help` it does not list —
numbered on, so `story:<id>:lead:<chapter>:<n>` stays stable) are resolved per player and cached in
`PlayerSession.Story.StoryLeads`. The cache is rebuilt only when `StoryLeadSignature` changed — the chapter, the location,
in space or not, the fragments on the player's world, the save's found fragments, the machine rule, the finale flags, the
player's landed bodies / blueprints / scan ledger counts, the ship, the locale, instant travel, and an epoch bumped by
`module:built`, `ship:commissioned` and every followed lead. No per-tick galaxy scans: the 1 Hz path compares the
signature (O(1)); the unique sites' bodies are cached per galaxy. The **current** lead = the first open, feasible one; none
→ the `explore` fallback (`story.vega.obj.explore` / `explore_peaceful` — rules-aware); after the win there is no
fallback (the chip clears once the relays are done, as before).

**Following a lead** (`StoryLeadListener` on the bus for `story:fragment`, `visit:body`, `station:boarded`, `scan:any`,
`mission:turned_in`, `defeat:any`, `research:any`, `module:built`, `ship:commissioned`, `relay:commissioned`,
`lane:linked` — registered only while the world story has leads; plus `TickStoryLeads` at 1 Hz in the story pass for the
evidence and the heal tank — see [the 1 Hz story pass](#the-1-hz-story-pass-2477)): one action books the first open lead it
fulfils. The fragment hooks now raise
`story:fragment` **before** `RecordStoryFragment`, so a find that crosses into the next chapter counts for the chapter it
was made in. `CompleteLead` writes `story:<id>:lead:<chapter>:<n>` for that player only (in MP each player follows their
own leads; other players' chips keep theirs), may unlock a memory (below), pays the bonus and moves the chip and the
Journal on. No line is spoken — the chip, the meter and the Journal's ✔ show it.

**The chapter bonus** (`PayLeadBonus`): the first lead anyone follows in a chapter that lists leads adds
`leadBonusMilestones` milestones **once per save** (keys `lead:<chapter>:1..n` in `StoryState.MilestoneKeys` — the
existing once-key bookkeeping, untouched by older builds) and only while the arc's last beat is not revealed. For VEGA
that is at most the six chapters before B12 = 18 milestones = 36 points (Normal with combat: 73 → 55 milestones to the
finale).

**Pacing — the one wrapper.** Every reader of the score uses `StoryScale` = the density scale × `StoryDriverNormalisation`:
the beat reveal (`AdvanceStory`), the meter (`BuildStoryState`), the chip counter, the knowledge level and the admin's
`reveal_finale`. On a world whose rules field no machines (`StoryMachinesActive` false: planet enemies off or Creative,
and no space hostiles — the peaceful, family and peaceful-creative presets) and a pack with `normaliseUnavailableDrivers`,
the factor is `StoryEngine.UnavailableKillNormalisation` = `T / (T − cap·Wk)` from the pack (VEGA 204/164 ≈ 1.244), so a
peaceful world needs the same work as one with combat (93 → 73 milestones; with the bonus ≈ 55). The meter, the chip and
the knowledge level read `StoryProgressShown` = `StoryEngine.DisplayProgress` — clamped to the last revealed beat's
threshold (switching hostiles on mid-save lowers the factor; beats stay, the meter never runs backwards) and to the
target. The meter and the knowledge level show that whole-arc score; the **chip counter** (#2537) shows only the way to
the next beat — `StoryEngine.NextBeatProgress`: the points since the last revealed beat's threshold out of the gap to the
next one (score 8, B1 = 6 heard, B2 = 14 → 2/8), and no counter (0/0) once every beat is revealed — because the arc's
score next to a single task ("Find the net fragment (8/204)") read as a fragment count. `HandleSetWorldRules` runs `AdvanceStory` when the factor changed. **An existing peaceful save** crosses one or two
beats at its first start with this build (`LoadStoryState` → `AdvanceStory`); the players' join catch-up absorbs them
(two or more missed beats → silently into the Journal with one line, the recap and the current chapter's opening; one →
its card).

**Memories without machines** (`TryAlternativeMemory`): where no machine can drop a personal memory, a followed lead
(`memoryTriggers: lead`) or a first monument reading that follows no lead (`monument`) unlocks the player's next memory —
only for a player whose story track started.

**Chip and keys** (`StoryObjectiveFor`, rank 5 "VEGA lead" of the §5.3 order; the finale words carry source `finale` and
are never muted, the rest `story`): a current client (`packs1`) reads `story.vega.obj.*` (pack locale, ≤ 45 EN / 53 DE,
no "Story:" prefix — the chip's "VEGA:" label names the story); an older client — and any world story without leads —
keeps today's `story.obj.*` keys (`StoryObjectiveKey`, unchanged). Before a player's track starts only the finale shows
(rank 1, in the Guardian system).

**Journal.** `NetStoryPack.Leads` (`NetStoryLead{ChapterId, Index, Type, Key, Arg, Done, Current, StandIn}`, additive) on
the world story's entry: the current chapter's own leads (done or open) plus the stand-in or "explore" while it is on the
chip. The VEGA page lists them under "Leads in this chapter" (`JournalPackPage.LeadsOf` / `LeadLine`: ✔ followed, ● on the
chip, ○ open).

**E21.** Tutorial bookkeeping never calls `RecordStory*` (the step runtime has no story hook; real world firsts made during
the lessons still count through their own hooks). `MissionDefinition.CountsForStory` (default true): the Crystal workshop's
six radio steps set `false` — their turn-ins add no milestone and thread no fragment; the chapter bonus balances the 12
points per player they used to bring.

Test seams: `LeadsForTest`, `CurrentLeadForTest`, `LeadResolutionsForTest`, `TickStoryLeadsForTest`,
`StoryObjectiveForTest`, `StoryScaleForTest`, `StoryProgressShownForTest`, `JournalLeadsForTest`,
`RecordUniqueSiteSkipForTest`, `PickUpNetFragmentForTest(id, playerId)`. Tests: `VegaChapterLeadsTests`; Client.Tests
`JournalViewTests` (lead rows, a real client receiving them).

## Story missions per chapter (#2476)

Synthesis §4.8 option O3, layered on O2: **VEGA hands each player a short story mission per chapter** — a titled task list
in her voice that points at the chapter's story drivers. Beats still reveal by score: a mission never holds one back.
Code: `src/BlocksBeyondTheStars.GameServer/GameServerStoryMissions.cs` (+ small hooks in `GameServerStoryPacks.cs`,
`GameServerStoryLeads.cs`); model: `StoryMission` in `Shared/Story/StoryPackManifest.cs`.

**Shape (the choice).** The missions live in the world story's manifest (`chapters[].missions[]`, each
`{ id, titleKey, summaryKey?, feasibleIf[], alternative?, skippable?, steps[] }`), and the server compiles them once into a
**per-player sub-track of the world story** — a `CompiledPack` with the story's own id (`IsStoryMissions`) whose optional
chapters are the missions — run by the player-pack step runtime of #2466 (activation, the event bus, the mission bridge,
`feasibleIf` / `alternative` / `alreadyDoneIf`, escalation, completion). A separate companion pack was the other option; it
was not taken because it would need its own enable/start rules tied to the world story, a hidden Journal category and its
own locale, while the sub-track gets "runs while the player's VEGA track runs" for free (`story:<id>:started` is the
track key; `StoryPackEnabledFor` is the world story) and keeps the texts and the Journal on the VEGA page. Keys: the
mission is a chapter of the story's id — `story:<id>:ch:<mission>:active|done|skipped`, its steps
`story:<id>:step:<step>[:skipped]`, the running counters `<id>:<step>` in `PlayerState.StepProgress` — per player, so in
multiplayer everyone does their own. The sub-track never starts, migrates, graduates or is stamped "done" (`AdvancePack`
hands it to `AdvanceStoryMissions`). Mission ids must not be chapter ids. The built-in fallback (`BuildVegaProtocol`) carries
no missions — like the cutscenes, their texts live in the pack, which is what the fallback stands in for.

**Steps.** Ordinary pack steps (kind `do`, every one with a `chipKey` — it is its Journal line), verified by events
(`lore:read`, `scan:any` …), a mission bridge (`{type: Mission, target}`), or a **lead bridge** (`{type: Lead, target:
<lead word>}`, #2476 — not `explore`, not the finale's words): followed exactly like that chapter lead (the lead listener's
events via `LeadFulfilledBy`, the lead's evidence — blueprint researched, module fitted, site landed on before — and the heal
tank's spot), shown with the lead's own placed text on the chip where it can be followed from here (`Visit the Sealed
Observatory on Kepler-3 b`, `Find the net fragment on …` — the fragment's map signal comes with it), else with the step's own
chip text, and marked like the lead (a lead marker / the space waypoint). A step's `onStart.reveal` may also name
`unique_site:<site>` (the site's spot while the player is on its world).

**Handing out** (`AdvanceStoryMissions`, from the 1 Hz `TickStoryMissions` and after every mission event): for a `packs1`
player whose track started, the missions of their current chapter (`TrackChapterIndex`) run one at a time in data order.
The next one starts when none of the chapter runs, **no basic training runs for the player** and **no story card or scene
waits in their cutscene queue** (the chapter's scene comes first, then her briefing — the first step's start line). A
mission whose `feasibleIf` does not hold is closed as skipped silently and its `alternative` (a mission of the same chapter
that runs only standing in) starts instead; a step that cannot be done here gives way to its step `alternative` — at
activation, and for the running step also later when its world fact goes away (the last fragment found by someone else).
**Letting go:** when the player's chapter moves on, a running mission of an earlier chapter is closed — done when its steps
are through, otherwise skipped with no further line. A mission none of whose steps was done (each impossible here) closes
as skipped. Never handed out at the join itself; never to an older client (no `packs1`: no lines, no state — it keeps its
`story.obj.*` chip).

**Chip** (`StoryObjectiveFor` → `MissionObjectiveFor`): the running mission's step **takes the chapter lead's place** (rank 5
"VEGA lead", source `story`, the story's id as the pack — "VEGA: …"); leads keep counting in the background and come back
when the mission is through. Never over the open finale (its lead stays on the chip, source `finale`, never muted). The
basic training (rank 3) and the finale in its system (rank 1) still rank above; a pin on the world story shows the mission.

**Bonus.** A mission finished by playing counts as following a lead of its chapter: `PayLeadBonus(chapter)` — once per save
and chapter (the same `lead:<chapter>:1..n` keys, so never on top of a lead's), never after the arc's last beat. Nothing
else is paid (no items, no knowledge, no memory trigger).

**Journal / wire / actions.** `NetStoryPack.Missions` (additive, the world story's entry): `NetStoryMission{Id, ChapterId,
TitleKey, SummaryKey, State active|done, Skippable, Steps: NetStoryMissionStep{Id, Key, Arg, State done|active|upcoming,
Progress, Target, OnChip}}` — the current chapter's running and done missions; skipped steps and unused stand-ins are left
out; the running step carries the chip's text. The VEGA page shows them under "Missions in this chapter" above the leads
(`JournalPackPage.MissionsOf` / `MissionTitle` / `MissionStepLine` / `MissionActions`), a new running mission lights the
Journal badge, and "Skip mission" sends `StoryPackIntent{PackId = <world story>, Action = skipChapter, ChapterId =
<mission>}` (`skipStep` works too) — handled by `ApplyStoryMissionIntent` (already pause-whitelisted). `/story` shows
`mission=<id>/<step>, missionsDone=n/m`.

**VEGA's missions** (`data/stories/vega_protocol/story.json`, texts `story.vega.m.*` in the pack locale, en + de):

| Chapter | Mission | Steps |
|---|---|---|
| 1 signature | *Follow the Signal* | find a net fragment (lead `fragment`, `world:fragments_left`) — stand-in: help a settlement (lead `settlement_help`, `world:settlements`) |
| 2 service | *The Sealed Observatory* (`world:observatory_reachable`) | visit it (lead `unique_site:observatory`) → read the post's last log (`lore:read` observatory; `alreadyDoneIf lore:observatory_log`; marker `unique_site:observatory` after 5 s) |
| 2 service | *The Deep Archive* — stands in (`world:vaults`) | read the notes in a buried vault (`lore:read` vault; `alreadyDoneIf lore:vault_plaque`) |
| 3 guardian | *What the Stones Remember* | read a monument's runes (lead `monument`) → help a settlement (lead `settlement_help`, `world:settlements`) |
| 4 verdict | *The Long Quiet* (`world:derelict_reachable`) | board the derelict (lead `unique_site:derelict`) → find its cargo manifest (`lore:read` derelict; `alreadyDoneIf lore:derelict_manifest`) |
| 4 verdict | *Ships That Remember* — stands in (`world:free_flight`, `world:space_wrecks`) | scan a wreck drifting in space (`scan:any` wreck) |
| 5 what_you_are | *Eyes on Far Stars* | research the radar array → build it into the ship (leads `research:` / `module:radar_array`) |
| 6 sleeps | *The Jump Generator* | research the jump generator → build it into the ship |
| 7 core | — | the finale is the chapter's mission (its lead, never muted, and its set pieces) |
| 8 resolution | *Survey Orders* (epilogue) | do the first survey order from a station board (mission bridge `relay_survey_1`, `world:space_stations`) — stand-in: link the stars with relays (lead `relay`) |

Test seams: `TickStoryMissionsForTest`, `StoryMissionIdsForTest`, `MissionChipStepForTest`, `RunningMissionsForTest`,
`JournalMissionsForTest`, `MissionLeadResolutionsForTest`, `StoryMissionStatusForTest`. Tests: `VegaStoryMissionsTests`;
Client.Tests `JournalViewTests` (mission rows).

## The 1 Hz story pass (#2477)

Once a second per joined, non-observing player, `TickStoryPass` (`GameServerShipAi.cs`) runs the story's bookkeeping in
this order: the player packs (`TickStoryPacksFor`: start, advance, offers, help escalation on active play time), the world
story's start rule (`TryStartStoryTrack`), the chapter leads (`TickStoryLeads`: evidence, the heal tank, the marker), VEGA's
story missions (`TickStoryMissions`: hand-out, letting go, evidence, re-checks, escalation), the objective chip
(`RefreshVegaObjective`) and the story help markers (`SyncStoryMarkers`, #2570: a closed step's marker leaves the client's
map and compass). Queued cutscenes are not part of it — `TickCutscenes` flushes them every server tick (#2468).

Two callers, one per player and second:

- `TickShipAi`, inside the per-world loop, for the players of each loaded world — the active cursor is their world, which a
  ground marker (`RevealTarget`, a story help marker or a lead marker) needs.
- `TickStoryOutsideWorlds` (#2477), after the per-world loop like `TickCutscenes`, for everyone the loop did not reach: a
  pilot cruising after an in-flight hyperjump occupies a body whose world was never loaded, and until #2477 their evidence,
  markers and mission hand-outs waited for the landing (events still booked leads and steps). `GameServer.Tick` records the
  worlds it ticks (`_storyTickedWorlds`); the same `VegaAdvisorAccum` drives both callers, so a player is never passed twice.
  Everything in the pass reads the player's own world by id or guards against the cursor (no ground marker in space), and
  the leads stay lazily resolved — the outside pass adds one set lookup per player and tick.

Neither runs while the world is held. Test: `VegaChapterLeadsTests.APilotOnAnUnloadedWorld_GetsTheStoryPass_FromTheServerTick_OnceASecond`
(the evidence booked in space, both players passed exactly once a second).

## The finale's set pieces, the duel dialogue and the ending on the director (#2478)

The finale flow above is presented with the cutscene system (details, tiers and shots: [CUTSCENES.md](CUTSCENES.md#vegas-finale-2478)).
The pack binds a scene per stage in `finale.cutscenes` (`StoryFinaleStages`: `arrival`, `descent`, `breach`, `duel`,
`standdown`, `resolution`; mirrored in `StoryRegistry`); `PlayFinaleCutscene(session, stage, anchor)` plays one — once per
player by the ledger, only once their story track has started, never for an observer:

| Moment (server) | Scene | Who |
|---|---|---|
| the flight warps into the Guardian system (`JumpFlightToSystem`), the core still waiting | `fin.arrival` (card, now or never; chains to the descent) | the pilot |
| landing on the core world (`ShipAiWorldFlavour`, before the never-muted `guardian_core` hint, which it shows) — not once the duel runs | `fin.descent` (anchor: the shaft mouth, `CoreShaftAnchor`) | the lander |
| the hack completes (`BeginCoreDuel`, before the first node) | `fin.breach` (anchor: the column, `CoreColumnAnchor`) on the core world, the banner `fin.duel` elsewhere | the finale's audience (`InFinaleAudience`) |
| the duel is won (`WinDuel` → `DeliverResolution`) or a missed win catches up (join, track start, end of observing) | `fin.standdown` → `resolution` | every started player |

- **The duel on the server (B50, E28):** `HandleCoreDialogueChoice` accepts an answer only from a player on the core world
  (`OnGuardianCoreWorld`: the location is the core body and the player is not in its flight — a pilot over the core is
  anchored on the same body); `CoreDialogueChoiceIntent` passes a held singleplayer world (`PausedMayHandle` — the client's
  dialogue holds it). The node message carries `NodeCount` and the column (`HasAnchor`, `AnchorX/Y/Z`, additive), and
  whoever lands on the core world or joins while the duel runs gets the current node (`SendDuelNodeTo`).
- **The duel on the client:** the director's dialogue mode (`CutsceneDirectorDuel.cs`, `DuelDialogueScreen.cs`; rules in
  Client.Core `CoreDuel.cs`) — only on the core world, the answers on the dialogue's own buttons, never the hotbar digits;
  elsewhere nothing but the banner. `FinaleView` keeps the duel's state (music, the "face the core again" hint) and no
  longer draws the IMGUI duel box.
- **The ending:** see "Ending catch-up" above; the recap of a join backlog never folds a finale scene (`IsFinaleScene`).
- **B51:** `MarkGuardianDefeated` clears the planet machines of every loaded world and marks each one's enemy list dirty.
- **B10 (latecomers):** checked — the Guardian reveal (#2460), the relay insights (silently in the log at the track start,
  `RelayInsightKeys`, #2470), the resolution line and the achievement (above) all reach a latecomer; nothing left to add.

Test seams: `JumpIntoGuardianSystemForTest`, `CoreColumnAnchorForTest`, `CoreShaftAnchorForTest`,
`PlanetEnemyCountOnForTest`. Tests: `VegaFinaleSetPiecesTests`; Client.Tests `FinaleSetPiecesClientTests`.

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
teaching verbs (the second block of the table); the deep dives add theirs with steps 18–21. The table below adds where
each key is raised; the complete, generated list with every key's meaning (from its doc comment) is
[STORY_VOCABULARY.md](STORY_VOCABULARY.md#game-events-objectiveevent) (#2483 — `StoryVocabularyDocTests` fails when it
drifts from the code), and the authors' path is [STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md).

| Key | Argument (amount) | Raised at | File |
|---|---|---|---|
| `mine:any` | block key | a block mined by hand, next to `OnBlockMined` | `GameServer.cs` (`BreakBlockCore`) |
| `build:any` | block key | a block placed, next to `OnAchievementBuild` | `GameServerPlacement.cs` (`FinishPlacement`) |
| `build:any` | `blueprint_paste` (placed count) | a blueprint paste | `GameServerBlueprints.cs` |
| `build:rotated` | block key | a form/prop block placed with an explicit rotate-key turn (`Yaw` 0–3 or an up-face) | `GameServerPlacement.cs` (`FinishPlacement`) |
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
| `station:commissioned` | station id | a player station commissioned — raised for its **owner** (online), like the achievement; an offline owner gets the message and the achievement at the next join (`station:told:<id>`, #2492), not this event | `GameServerPlayerStations.cs` |
| `base:founded` | body id | a base core placed | `GameServerBases.cs` (`PlaceBase`) |
| `tame:any` | species id (`au_sreekmakra`) | a creature tamed; the shapeshifter following a tame raises its own | `GameServerTaming.cs`, `GameServerSreekmakra.cs` (`BondSreekmakra`) |
| `loot:any` | container kind | a world container looted empty (never a player crate) | `ContainerSystem.cs` (`LootContainer`) |
| `defeat:any` | `creature` \| `giant` \| `machine` \| `bandit` (planet), `space_machine` \| `raider` (space) | a hostile defeated by the player | `GameServerEnemies.cs`, `GameServerSpaceCombat.cs` |
| `wormhole:any` | destination system id | a flight through a wormhole | `GameServerWormholes.cs` |
| `relay:commissioned` | station id | an SPS relay completed by the player's delivery | `GameServerRelays.cs` |
| `lane:linked` | `<systemA>\|<systemB>` | a jump lane opened by the player's contribution | `GameServerRelays.cs` |
| `crystal:<event>` | — | a Crystal Net circuit event of the circuit's owner (`CircuitEvents`: `lamp_by_switch`, `net_on`, `machine_job` …) | `GameServerCrystalQuests.cs` (`OnCrystalCircuitEvent`) |
| `story:fragment` | fragment key | a story fragment handed over (picked up, mission thread, NPC dialog, NPC thread) | `GameServerNetFragments.cs`, `GameServerMissions.cs`, `GameServerNpcDialogs.cs`, `GameServerNpcThreads.cs` |
| `insight:companion_ward` | — | a companion made a machine stand down near the player (at most every 30 s) | `GameServerEnemies.cs` |
| `insight:shape_noncube` | base item key | a non-cube form crafted | `GameServer.cs` (`RaiseShapeCrafted`) |
| `lore:read` | site kind (`monument`, `vault`, `observatory`, `derelict`, `data_terminal` …) | #2476: a lore text new to the player opened in the reader (a rune inscription, a vault plaque, the observatory's last log, a wreck's manifest) | `GameServerLore.cs` (`TryRevealLoreText`) |
| **#2469 — the basic training's verbs** | | | |
| `move:walked` | — (whole metres) | walking on a surface (no speeder, no train; a report over 4 blocks from the last one never counts) — measured only while a step listens | `GameServer.cs` (`HandleMove`) → `TutorialSystem.cs` (`TutorialMovementEvents`) |
| `move:jump` | `jump` \| `crouch` | a jump (upward speed > 2.5 blocks/s, not climbing) or the start of a crouch, at most once a second — only while a step listens | same |
| `hotbar:select` | slot number (1-based) | another quick-bar slot picked (no event when the slot does not change, nor for the client's `Resync` of its slot after a join or world change, #2525) | `GameServerDispatch.cs` (`HandleSelectHotbar`) |
| `item:gained` | base item key (count) | a mined or harvested block's drops reach the player | `GameServer.cs` (`BreakBlockCore`) |
| `item:moved` | — | an item moved between two backpack/quick-bar slots | `GameServer.cs` (`HandleMoveItem`) |
| `gear:equipped` | base item key | a piece of gear put on | `EquipmentSystem.cs` (`HandleEquipItem`) |
| `cargo:stowed` | item key, or `all` | items stowed into the ship's hold (one item or "stow all") | `GameServerCargo.cs` (`MoveCargo`) |
| `inventory:full` | — | the backpack has no free slot (the 1 Hz advisor check, raised right before the `invfull` hint) | `GameServerShipAi.cs` (`TickShipAi`) |
| `ship:returned` | `menu` \| `teleport` \| `walk` | "Back to my ship", the suit teleporter, or walking back aboard / within 10 blocks of the landed ship after having been 40+ blocks away (the walk check runs at 1 Hz only while a step listens) | `GameServerReturnToShip.cs`, `GameServerTeleport.cs`, `TutorialSystem.cs` (`TutorialStateEvents`) |
| `lamp:toggled` | `on` \| `off` | the suit lamp switched | `GameServerShipAiContext.cs` (`HandleSetLamp`) |
| `suit:auto_feed` | item key | the suit fed a stored ration to a starving player | `GameServer.cs` (`TryAutoEatRation`) |
| `suit:climate` | `cold` \| `heat` | the suit's climate control started fighting the weather | `TemperatureSystem.cs` (`TickTemperature`) |
| `player:respawned` | — | any respawn (the relocation half of a death) | `GameServer.cs` (`CompleteRespawn`) |
| `mine:refused` | block key | a swing refused by the tool tier while a better tool exists (where the `tier_gate` tip is armed) | `GameServerShipAiContext.cs` (`NoteTierGate`) |
| `data:found` | `cache` \| `fragment` | a data cache in the advisor probe's reach, or a data fragment in the backpack or the own ship's hold (1 Hz, only while a step listens) | `GameServerShipAiContext.cs` (`TickVegaContextTips`), `TutorialSystem.cs` (`TutorialStateEvents`) |
| `flight:flown` | — (whole flight units) | the own ship flying (a pose jump over 60 units — a hyperjump, a warp, a fresh entry — never counts) — measured only while a step listens | `GameServerSpaceCombat.cs` (`ShipMove`) → `TutorialSystem.cs` (`TutorialFlightEvents`) |
| `mission:board_accepted` | mission id | a job the player picked: a board's, a mission chain's or a stand-alone board job taken at a board (`MissionAcceptCountsAsTrade`, B58; #2491: `supply_cables` — never a story character's errand) | `GameServerMissions.cs`, `GameServerMissionChains.cs` |
| `visit:world` | body id | the first arrival on a planet, moon or asteroid (`visit:body` counts stations too) | `GameServer.cs` (`MarkArrivedOnBody`) |
| **#2479 — the deep dives "Research" and "Flying"** | | | |
| `minigame:played` | game key | a finished run of a DataQube game from the catalogue (a replay too; an abandoned run never) | `GameServerDataCubes.cs` (`HandleMinigameResult`) |
| `gadget:used` | gadget item key | a gadget did its work (a refused or empty use raises nothing) | `GameServerGadgets.cs` (`HandleUseGadget`) |
| `planet:scanned` | body id | a world's overview card read (the ship scanner on a planet, or the Map tab's planet scan) | `GameServerPlanetScan.cs` (`HandlePlanetScan`) |
| `asteroid:destroyed` | — | the player's ship weapon broke an asteroid (a split counts) | `GameServerSpaceCombat.cs` (`FireWeapon`) |
| `interior:entered` | — | stepped from the helm (or in from a spacewalk) into the floating ship's interior | `GameServerShipInterior.cs` (`EnterShipInterior`) |
| `eva:started` | `hatch` \| `flight` | a spacewalk began: out through the hatch of the floating ship, or from the flight view | `GameServerShipInterior.cs` (`ReturnToFlight`), `GameServerSpaceCombat.cs` (`HandleSetEva`) |
| `player:zero_g` | — | floated up above a planet's atmosphere line on foot (raised right before the `zero_g` hint it absorbs) | `GameServer.cs` (`UpdateAboveAtmosphere`) |
| `station:left` | station id | undocked from a boarded station | `GameServerSpaceStations.cs` (`LeaveStation`) |
| `marker:set` | `marker` \| `ping` | a planet-map marker set (or moved), or a spot pinged | `MarkerSystem.cs` (`SetMarker`, `RaisePing`) |
| `salvage:found` | `wreck` \| `debris` \| `capsule` | a derelict or debris field reached for the first time, or a salvage capsule emptied into the hold | `GameServerSpaceWrecks.cs` (`MarkSpaceWreckVisited`), `GameServerSpaceCombat.cs` (`StowDrop`) |
| **#2480 — the deep dives "Ship building" and "Base building"** | | | |
| `ship:repaired` | `all` \| `cell` | the own ship repaired: the console's "repair everything affordable" (something was repaired), or one hole refilled | `GameServerShipRepair.cs` (`RepairShipAll`, `RepairShipCell`) |
| `ship:edited` | block key | a block built onto or into the parked ship on foot (authored or commissioned self-built; a door too) | `GameServerSpaceStructure.cs` (`HandleLandedShipEdit`) |
| `ship:keel_placed` | fleet id | a ship keel laid (a self-built ship's construction site founded) | `GameServerCustomShips.cs` (`HandleShipCorePlace`) |
| `ship:switched` | ship id | the active ship switched in the fleet (a refused switch raises nothing) | `GameServerShips.cs` (`HandleSwitchShip`) |
| `base:built` | block key | a block placed in the build zone of a base the player owns or shares with an ally or a crewmate (the core itself not) — tested only while someone listens | `GameServerPlacement.cs` (`FinishPlacement`) |
| `base:sealed` | body id | a base the player owns or shares holds air in a supplied sealed room on their world — the 1 Hz check, only while one of the player's own steps waits | `GameServerTutorialBuilding.cs` (`TutorialBaseStateEvents`, from `TutorialStateEvents`) |
| `base:walled` | body id | the player's feet stand in a fenced-in part of such a base (the wall fill) — same | same |
| `door:opened` | door kind (`wood`, `hinge`) | a hand-operated door opened by the player (closing raises nothing) | `GameServerDoors.cs` (`HandleDoorInteract`) |
| `spawn:set` | `bed` \| `heal_tank` \| `crew_bunk` | a bed, a heal tank or a crew bunk became the spawn point | `GameServerHealTanks.cs` (`HandleSetSpawnPoint`) |
| `station:core_deployed` | station structure id | a station core deployed on a spacewalk | `GameServerPlayerStations.cs` (`DeployStationCore`) |
| `station:zero_g` | `on` \| `off` | zero-g construction mode switched aboard a player station (a press that flips nothing raises nothing) | `GameServerStationGravity.cs` (`HandleSetStationZeroG`) |
| **#2481 — the deep dives "Crystal net"** | | | |
| `craft:station` | the recipe's station, lower case (`workshop`, `refinery`, `factory`, `campfire`, `market`, `algaetank` …) (batch count) | a recipe crafted, next to `craft:any` — also a free Creative craft; the argument string is built only while someone listens | `GameServer.cs` (`HandleCraft`, `CraftStationArg`) |
| `factory:claimed` | the factory's name | a spawned factory claimed with an SPS code (a refused claim raises nothing) | `GameServerFactories.cs` (`ClaimFactory`) |
| **#2482 — the deep dives "More to discover"** | | | |
| `creature:fed` | the food's item key | the Feed action threw a piece to a begging animal (a refused throw raises nothing) | `GameServerFoodBegging.cs` (`HandleThrowFood`) |
| `companion:cared` | `named` \| `fed` | a companion named, or fed (a refused feed raises nothing) | `GameServerTaming.cs` (`HandleSetCompanionName`), `GameServerCompanionBond.cs` (`HandleFeedCompanion`) |
| `plant:any` | block key | a flora block placed — a sapling, a seed, a bred seedling (next to `build:any`) | `GameServerPlacement.cs` (`ApplyFloraPlacementEffects`) |
| `sample:taken` | `plant` \| `animal` \| `mineral` | a sample went into the sample case (a harvest, the sampler, a defeat, a companion's gift), raised right before the `first_sample` hint | `GameServerBio.cs` (`GiveSample`) |
| `biolab:used` | `analyse` \| `mix` \| `change` \| `wash` \| `seedling` | a bio-lab action worked (the single result exit, success only) | `BioLabSystem.cs` (`LabResult`) |
| `npc:talked` | the NPC's role | the player talked to someone — a greeting or a conversation (not to a sleeper) | `GameServerNpcDialogs.cs` (`HandleTalkToNpc`) |
| `npc:answered` | the NPC's role | an answer picked in a conversation | `GameServerNpcDialogs.cs` (`HandleNpcDialogChoice`) |
| `npc:people` | — | the "People you know" list requested (the client asks while it shows the list) | `GameServerNpcSocial.cs` (`HandleRequestKnownNpcs`) |
| `move:climbed` | — (whole metres) | rising on a wall in the climbing pose (a report over 4 blocks from the last one never counts) — measured only while a step listens | `GameServer.cs` (`HandleMove`) → `TutorialSystem.cs` (`TutorialMovementEvents`) |
| `move:airborne` | `jetpack` \| `glider` | the worn jetpack fired, or the worn glider's wing opened | `EquipmentSystem.cs` (`HandleSetJetpack`, `HandleSetGliding`) |
| `vehicle:boarded` | `speeder` \| `boat` \| `train` | the player boarded their speeder or boat, or a train | `VehicleSystem.cs` (`HandleEnterSpeeder`), `GameServerRails.cs` (`HandleEnterTrain`) |
| `chat:sent` | — | a chat line went out on the radio (never a slash command, a refused or a filtered-out line) | `GameServer.cs` (`HandleChat`) |
| `ally:joined` | `alliance` \| `crew` | an alliance formed (both partners) or a crew joined (the newcomer and the founder) | `GameServerAlliances.cs` (`EstablishAlliance`), `CrewSystem.cs` (`RespondCrewInvite`) |
| `paint:block` | block key | the paint tool painted (or wiped) a design on a placed block | `GameServerPaint.cs` (`HandlePaintBlock`) |
| `poi:reached` | — | within 48 blocks of a buried vault's entrance or a crashed wreck (the 1 Hz advisor check, raised right before the `poi` hint) | `GameServerShipAi.cs` (`TickShipAi`, `NearVegaPoi`) |

Since #2479 the in-flight jump (`JumpFlightToSystem`) raises `hyperjump:any` after the warp-in's flight state and star map
went out (it was raised before the warp), so a card a step plays for it lands on the warp-in like the finale's arrival card.

**`ui:<id>` (client teaching flags, synthesis §3.6, #2469).** Tag **298 `UiTutorialEvent { Id }`** (C→S) reports a UI
moment a step teaches — `vega_continue`, `menu_open`, `codex_open`, `map_open`, `waypoint_set`, `progress_open` (#2479, the
Achievements tab) (`GameEventKeys.UiEvents`);
`HandleUiTutorialEvent` raises `ui:<id>` on the bus for a known id at most every 0.25 s of server time. Teaching only: no
step but one waiting for exactly that flag listens, it pays nothing, unlocks nothing, counts for no story and is never
stored; it is never a condition (the parser refuses `ui:`). Pause-whitelisted (`PausedMayHandle`). Clients:
`NetworkClient.SendUiTutorialEvent` (the same flag at most every 0.5 s) from `VegaPanel` (a line read through),
`GameMenu` (menu opened, Codex, Map tab, Achievements tab) and `SpaceMap` (chart opened, waypoint set).

**Conditions** (`Shared/Story/StoryCondition.cs`). One small grammar for a step's `requires` / `feasibleIf` /
`alreadyDoneIf`, a chapter's `offerWhen`, a pack's `start.after` and its `gates`, parsed once at load by
`StoryConditionParser.Parse(text, worldPredicates)` (or `TryParse`, `ParseList` for a JSON list = one implicit `all`)
into a `StoryCondition` tree and evaluated purely over an `IStoryConditionContext`:

```
condition := atom | all:[c, c, …] | any:[c, c, …] | not:c
atom      := step:<pack>:<step> | chapter:<pack>:<chapter> | pack:<pack>:started|done|enabled|optedout
           | blueprint:<key> | world:<predicate> | lore:<key>
```

`lore:<key>` (#2476) holds when the player has read that lore text (`lore_sites.json` entry; the reader's `lore:<key>`
milestone) — e.g. a mission step's `alreadyDoneIf: ["lore:observatory_log"]`. A world story's mission ids count as
`chapter:` targets (they share the chapter keys) and their steps as `step:` targets.

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
`new_world_in_system` (`TutorialSystem.cs`) and `all_blueprints` (`CreativeGrantsActive`, `GameServerShipAi.cs`);
since #2476 the story missions' feasibility checks
`fragments_left` (the world story still has an unfound fragment), `observatory_reachable` / `derelict_reachable` (the
unique site exists in this save and the player can reach it — the lead's rule), `settlements`, `vaults`, `space_wrecks`
(the world options are not off); since #2479 the tutorial's deep dives: `data_cache_near` / `scanner_unknown` (the moment
VEGA's 1 Hz tip pass found that situation for the player — `PlayerSession.Vega.VegaTipLive`, the candidate ids of the last
pass, kept whatever the hints switch, the gates or the cooldowns say; no probe of its own), `jump_ready`, `arcade_ready`,
`monument_unread`, `asteroid_mining`, `docked`, `job_active`, `forge_ready`, `wormhole_known`, `debris_fields`; since #2480
the building courses' `tier_gate` (a swing the tool TIER refused within the last minute — the right kind of tool, too
weak; a wrong kind or an empty hand does not count, #2525 — `PlayerSession.Tutorial.TierGateRefusedAt`),
`module_affordable`, `hull_damaged`, `keel_laid`, `ship_built`, `base_here` (an own, allied or crew base on the player's body,
E32), `base_core_ready`, `night_outside` (the player's own world's clock), `station_core_ready`, `station_owned`
(`GameServerTutorialBuilding.cs`); since #2481 the crystal-net courses' `factory_near` (the tip pass's moment, like
`data_cache_near`), `factory_here` (a factory on the player's body), `access_code`, `factory_owned` (a claim in the save's
metadata), `machine_circuit` (the `crystal:machine_job` achievement counter), `machine_catchup` (the *Machines catch up*
rule is not 0); since #2482 the "More to discover" courses' `creatures` (the Creature abundance rule is not Off),
`tameable_near`, `settlement_near`, `ruin_near`, `treasure_near`, `wreck_signal` (the tip pass's moments, like
`data_cache_near`; `wreck_signal` also takes the manual-steering variant), `herd_begging` (an animal came to beg within the
last minute — `PlayerSession.Tutorial.HerdBeggedAt`, stamped where VEGA's `feed_herd` hint fires), `companion_owned`,
`seed_carried` (an item whose block is a plant), `hostile_near` (a machine, a hostile animal or a hostile robber within 40
blocks on the player's own world), `bandit_hail` (a robber's demand is pending), `climbing` (the pose), `train_here` (a
train the player may board on their world), `multiplayer` (the server hosts more than one player — a singleplayer host
passes `--max-players 1`), `radio_worn`, `planet_wrecks` (`GameServerTutorialMore.cs`). A predicate reads the player's
own body/system, never the active world cursor. A new predicate is a name in `StoryWorldPredicates` plus one line in
`GameServer.WorldPredicates`.

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
extras in `GameServerTutorial.cs` and `TutorialSystem.cs`). **Since #2469 the official tutorial pack
(`data/stories/tutorial/`) is the basic training** — the hard-coded nine-stage onboarding chain (`VegaStages`,
`VegaIsVeteran`, `ShipAiComplete`, the `ShipAiOn<Stage>` hooks, `VegaMineCount`) and the client's copy of the stage list
are gone. `ShipAiOnJoin` only plays the prologue for a newcomer (once, keyed `story:<story>:cut:prologue` — the cutscene
ledger's seen form, so step 11's prologue cutscene treats a player who heard the pages as one who saw it) and sends the
chip. The test packs live under `tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/` (never `data/stories/`);
`StoryPackFixtures.ContentWith` drops the shipped player packs so a fixture runs alone.

**Which packs run.** Every installed pack whose effective scope is `player` and that has chapters is compiled once
(`PlayerPacks`, highest `priority` first): conditions are parsed against the server's `world:` predicates, chapters and
steps indexed; a pack with a structural or condition error is logged and not run. A pack runs for a player when it is
**enabled** (`StoryPackEnabledFor`, = `pack:<id>:enabled`) and **started** (`story:<pack>:started`). Enabled: the player
has not opted out, and for a `tutorial` pack the world rule `GameRules.TutorialPolicy` says so (`Auto` → yes, `Courses` →
yes, `OptIn` → once the player started it, `Off` → no); any other player pack by its `defaultEnabled` or once started. A
pack starts by itself when enabled and its `start.after` holds (empty = at once) — a `Courses` world starts the tutorial
with its mandatory part skipped silently (#2498), and on an `Auto` world a newcomer with a `choices1` client is first asked
whether they want the training (#2499, both in TUTORIAL_PACK); `StoryPackIntent{startPack}` starts an opt-in tutorial
(refused while the policy is `Off`). A tutorial's start also writes `vega:intro` (older builds read it as "the intro was
shown"). The world story (VEGA) is not run here — its per-player track is step 11.

**Chapters.** A non-optional chapter runs once the previous non-optional chapter is done or skipped (and its own
`requires` holds); `autoStart: false` waits for `startChapter`. An optional chapter (deep dive) runs once the player
started it (`story:<pack>:ch:<c>:active` — the one key beyond §5.6, so a started deep dive survives a rejoin) and is
**offered** once when `offerWhen` holds (`story:<pack>:ch:<c>:offered`, line `vega.chapter.offer` with the chapter title as
`{0}`) — never while the pack's mandatory chapters still run (no offers during basic training), at most one offer per
90 s of play (since #2479 a graduation that suggests chapters counts as an offer moment: `Graduate` stamps
`LastChapterOfferAt`), and never (nor suggested at a graduation) when every counting step is already through for the player
— done, or covered by its evidence (`alreadyDoneIf`, a bridged mission turned in, a lead bridge's evidence;
`ChapterThroughByEvidence`, #2481: the Crystal workshop course is never offered to someone who did the whole chain), and
never (nor suggested, nor started) while its `visibleIf` fails (#2482, `ChapterVisible`: a course the world's rules rule out —
the Journal leaves it out too, unless the player runs, replays, finished or skipped it); `declineChapter` stamps `:declined` ("Later") and keeps it startable. `autoCompleteIf` holding when a chapter
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

**Help escalation** (§3.1), on active play time only (`PlayerSession.Story.StoryPackPlaySeconds`, ticked by the 1 Hz story
pass — [below](#the-1-hz-story-pass-2477) — which does not run while the world is held), per session, each stage once,
progress restarts the clock:
`hintAfter` (default 180 s) → the step's `hintKey` line; `markerAfter` (360 s) on a "where" step (one with
`onStart.reveal`) → a **story help marker** at the provider's target (`nearest_ore:<block>`: the nearest such block in a 33³
probe; `nearest_settlement`, `nearest_food_plant`, `unique_site:*`) that only this session sees — RAM, never persisted,
never broadcast, never `RevealPoi` — plus `vega.step.marked_spot` ("I've marked your goal — you can see it on your compass
and on the map", `StoryNarratorLines.StepMarkedSpot`, a transient line the client keeps out of VEGA's notes). Since #2570
the marker is **no 30-second ping**: it lives on the step's escalation state (`StepEscalationState.MarkerLocationId` /
`MarkerAt`, `GameServer.RaiseStoryMarker`, `GameServerStoryMarkers.cs`) and so exactly as long as the step is open —
completing or skipping the step, its chapter or its story drops it, a story the player switched off hides it. It shows
while the player is on the body it was raised on (gone elsewhere, back on return) and reaches the client inside the
normal marker view as a `NetMarker` with `Story = true`, `LabelKey` = the step's chip key and `PackId` (also `Ping = true`,
so an older client draws a ping); the planet map draws a violet goal flag labelled with that objective and the compass a
flag blip with a "Goal 120 m" caption (`StoryMarkerText`). The view is pushed when the marker goes up, after every Journal
action (`HandleStoryPackIntent`) and otherwise by the 1 Hz story pass (`SyncStoryMarkers` compares what the client was
last sent). The old `vega.step.marked` ("Follow the pulse!") stays in the locales only for older servers. **During the
basic training only the step on the objective chip gets a marker** (`MarkerFitsTheChip`: `LiveVegaChip`'s pack and step) —
no marker for an open extra (a deferred CORE step) or another story's step while the training holds the chip; a refusal
is not spent. A CORE "where" step of a non-optional chapter therefore sets its own `markerAfter` below its `hintAfter`
(b2.3, the berries: 120 s against 180 s — #2742; pinned by
`TutorialPackTests.EveryCoreWhereStepOfTheBasicTraining_ReachesItsMarkerBeforeItLeavesTheChip`). Nothing found → no marker
and the marker is **not spent** (#2490: `StepEscalationState.MarkerSent` is set only when a marker went up; a miss records
`MarkerMissAt` — the player's place as `ground|space|station` + body — and `MarkerMissTime`, and the probe runs again at
once on a new place or after `MarkerRetrySeconds` = 30 s of play on the same one — at most one probe per 30 s per chip
step). This is what lets the story mission's observatory marker (`unique_site:*` answers only on the site's own world)
wait for the landing there instead of being wasted off-site;
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
mandatory part calls `Graduate`: the pack's `graduation.cutscene` (#2473, before the line, once per player — the tutorial's
`tut.graduation` card), `graduation.lineKey` (a `packs1` client), `Advance(graduation.counter)` (the
tutorial's "Graduate" achievement), `graduation.suggest` chapters marked offered. A replay graduates only at its last
mandatory chapter (a full restart). Skips (`SkipPack`, `SkipChapter`, the admin's chapter jump) and the migration never do.

**One teaching voice** (#2469, G12). A step's `hintRefs` hold VEGA's matching once-hints (`ShipAiHintOnce`) and context
tips (`TickVegaContextTips`) through `HintHeldByTutorial`, and `SpeakStepStart` books them as told
(`MarkHintsToldByStep`); since #2459 the client keeps its own lessons (`VegaPanel.SayLocal`) quiet while a running chapter's
step that names them is active or ahead (`VegaText.LessonCoveredByStoryStep`), and a course offer due at a tip's moment —
or made a moment ago — stands in for the hints and tips the course names ("the tip becomes the offer": `OfferAbsorbsHint`,
`RecentOfferTeaches`, `MarkTipsToldByOffer`; never a safety tip); details in
[TUTORIAL_PACK.md](TUTORIAL_PACK.md#one-teaching-voice-g12).

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
`JoinRequest.Features` (`ClientFeatures.Packs1` / `Cutscenes1` / `Choices1`; `NetworkClient.Features` announces `packs1`,
`cutscenes1` once `NetworkClient.PlaysCutscenes` is set — the Unity client does when its cutscene director exists, #2468 —
and `choices1` once `NetworkClient.ShowsChoices` is set — the Unity client always, its VEGA panel draws `ShipAiLine.Choices`, #2499),
`ShipAiLine.PackId` / `Speaker` (the line's pack and narrator) / `ObjectivePackId`, `SetWorldRulesIntent.TutorialPolicy`,
`ServerRules.TutorialPolicy`. Persistence (additive, no save bump): `PlayerSnapshot.StepProgress` (null while empty) and
`TrackedStoryId` — an older build drops both on re-save, which costs a running count and the pin, nothing else
(`StoryPackRuntimeTests.AnOlderBuild_DroppingTheCounters_CostsOnlyTheRunningCount`).

**Admin** (world management, not a cheat — the role is the gate, above the cheats switch; the slash parsing in the
client comes with step 12): `AdminCommandIntent{Command = "tutorial", StringArg = "skip" | "restart" | "chapter <id>" |
"policy auto|courses|optin|off", TargetPlayer = <name or empty for yourself>}` — without an installed tutorial pack every verb but
`policy` answers `tutorial_none` (#2469). `story_status` (`/story`) now also lists the world's tutorial rule and the
player packs of the caller (or `TargetPlayer`), and is a read-only report above the cheats switch.

**Validation** (synthesis §5.9): `StoryPackValidator` (Shared) — run by `StoryPackValidationTests` for every
`data/stories/*` pack and the test packs — and its twin `tools/validate_story_pack.py` for authors without .NET (the
successor of `merge_story.py`; allows packs without beats, never rewrites a file, reports every problem at once, warns on
unknown fields). The checks are listed in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#validation).

## Networking (NetCodec tags)

| Tag | Message | Dir | Purpose |
|---|---|---|---|
| 139 | `StoryStateMessage` | S→C | active story, progress %, counts, kills, beats seen, flags; additive `HeardBeats` (this player's beat indexes) since #2462; `Started`, `Chapter`, `WorldChapter`, `ShowCrewChapter`, `RelayInsightKeys` since #2470 |
| 140 | `NetFragmentFoundIntent` | C→S | player opened a fragment at a position |
| 141 | `NetFragmentRevealed` | S→C | category + archive text key |
| 142 | `PlayerMemoryRevealed` | S→C | a personal memory unlocked by a machine kill |
| 143 | `GuardianSystemRevealed` | S→C | adds the finale system to the star map |
| 144 | `CoreDialogueMessage` | S→C | the core's claim + rebuttals (duel); additive `NodeCount`, `HasAnchor` + `AnchorX/Y/Z` (the column) since #2478 |
| 145 | `CoreDialogueChoiceIntent` | C→S | the player's chosen rebuttal — only from the core world (#2478, B50); passes a held SP world |
| 146 | `CoreHackIntent` | C→S | channel one hack tick |
| 147 | `StorySelectIntent` | C→S | admin sets the active story (#2506: a story switched back comes back with its progress; re-selecting the running one is a no-op) |
| 148 | `NetFragmentList` | S→C | fragments on the current world |
| 149 | `CoreHackProgress` | S→C | hack channel progress + complete |
| 220 | `StoryResolved` | S→C | play the resolution cinematic (win, join catch-up, replay) (#1124); additive `EpilogueTitleKey` since #2461; since #2478 only to a client that plays no cutscenes (the others get the `resolution` scene) |
| 221 | `RequestStoryResolutionIntent` | C→S | Story tab "watch the ending again" (#1124) |
| 295 | `CutscenePlay` | S→C | play a pack's cutscene (`PackId`, `CutsceneId`, `Arg`, `HasAnchor` + `AnchorX/Y/Z`, `Replay`); only to `cutscenes1` clients (#2467, [CUTSCENES.md](CUTSCENES.md)) |
| 296 | `CutsceneSeenIntent` | C→S | the client is through with these scenes (`PackId`, `Ids[]`, `Skipped`, `Unsupported`) → the seen ledger; passes a held world (#2467) |
| 297 | `StoryPackIntent` | C→S | the player's control of a player pack (track, opt out, start/skip/replay a chapter, skip a step) (#2466); `replayCutscene` + `CutsceneId` (#2467); `startPack` with the world story's id = "Start the story now" (#2470) |
| 298 | `UiTutorialEvent` | C→S | a UI moment a tutorial step teaches (`Id`: `vega_continue`, `menu_open`, `codex_open`, `map_open`, `waypoint_set`, `progress_open`) → `ui:<id>` on the event bus; teaching only, rate-limited, passes a held world (#2469) |
| 299 | `ObjectiveList` | S→C | the player's objectives in the chip order, one row per story, at most `MaxItems` = 3, the chip's first: `NetObjective{Source, PackId, StepId, Key, Arg, Progress, Target, Pinned}`; only to `packs1` clients (#2471, "The Journal" below) |
| 300 | `StoryPackStates` | S→C | the Journal's stories: `NetStoryPack{Id, Kind, Scope, NameKey, ShortNameKey, DescriptionKey, Enabled, Started, Done, OptedOut, Tracked, Startable, Blocked, MandatoryRunning, Badge, Chapters[], Steps[]}` + `TrackedPackId`, `TutorialPolicy`; only to `packs1` clients (#2471) |

Beat speech reuses `ShipAiLine`. Every message is `Register()`'d in NetCodec.

### The objective chip, the beat list and the story UI (#2462)

All fields below are additive (contractless MessagePack: an older peer ignores them, a missing one reads as its
default) — no `Protocol.Version` bump.

| Field | Meaning | Older peer |
|---|---|---|
| `ShipAiLine.ObjectiveSource` | where the chip's objective comes from — `ObjectiveSources.Tutorial` / `Story` / `Finale` / `Mission`, empty = no objective. Set in ONE place, `SendVegaWithObjective` (every VEGA line carries the chip; the 1 Hz refresh compares the whole `VegaChip`) | an older client keeps its `story.obj.` prefix guess; a new client facing an older server uses the same guess (`ObjectiveSourceRules.Effective`, which now also treats `story.obj.finale` as finale) |
| `ShipAiLine.TutorialActive` | this player's tutorial still runs, whatever the chip shows — the Character tab's button offers *Skip* then, *Restart* otherwise (B03) | read only when `ObjectiveSource` is set |
| `StoryStateMessage.HeardBeats` | the beat indexes this player has heard (`story:<id>:beat:<n>` milestones), ascending | empty — the client keeps the beats heard live this session |
| `StoryStateMessage.SeenCutscenes` | #2468: this player's confirmed cutscenes of every pack, `<pack>/<id>` sorted (`story:<pack>:cut:<id>` milestones, not the `:sent` form) — the Story tab's "Watch again" list ([CUTSCENES.md](CUTSCENES.md)) | empty — the client lists what it confirmed this session |
| `StoryStateMessage.Started` / `Chapter` / `WorldChapter` | #2470: this player's story track started; their (derived) chapter id; the world's chapter id — filled by every current server, so an empty `WorldChapter` means an older server | the client treats the track as started and lists chapters from the heard beats |
| `StoryStateMessage.ShowCrewChapter` | #2470: the Story tab shows "The crew of this world is in chapter N" (waiting players, hosted/arcade newcomers) | false — no line |
| `StoryStateMessage.RelayInsightKeys` | #2470: the relay insights this save has spoken (to a started player) — they join the VEGA log silently | empty — the log keeps what was spoken live |
| `StoryStateMessage.NoteKeys` | #2471: the world story's non-beat narrator lines this player heard (Guardian reveal, insights, resolution line — from their once keys) — the Journal's "VEGA's notes" keep them across a rejoin | empty — the notes keep what was spoken this session |
| `ShipAiLine.ObjectivePackId` | #2466; since #2471 also the world story's id on VEGA's own chip — the client labels the chip with the story's short name ("Training: …", "VEGA: …") instead of "Objective:" | an older client ignores it |
| `ShipAiLine.SpaceWaypointId` | #2471: a space-flight nav target the line points at (the dock lesson's station, `nearest_station`) — the client makes it its space waypoint when it has none | ignored |
| `ShipAiLine.Choices` | #2499: the line's buttons, only to a client that announced `ClientFeatures.Choices1` (`NetworkClient.ShowsChoices`; the Unity client). Each entry is `ShipAiChoices.Encode` = `labelKey␟action␟packId[␟chapterId]` (U+001F), action `continue` (nothing sent) or a `StoryPackActions` word; the first is the primary (the continue control), the second the secondary (a deliberate click/tap/hold). A pick goes back as an ordinary `StoryPackIntent`. Used by the tutorial's first-start question and the course offers ([TUTORIAL_PACK.md](TUTORIAL_PACK.md#the-first-start-question-and-the-offer-buttons-2499)) | empty — the plain line; a `choices1`-less client never gets one |
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
- **A story the client lacks (#2500).** `UnknownStoryPacks` (Client.Core, one per world session on GameBootstrap) notes the
  pack ids of `StoryPackStates` and `StoryStateMessage` this client's content does not have and makes a one-time notice due —
  VEGA's panel queues `ui.story.pack_missing` (never muted, not kept in the notes). The panel's lines, the chip and the "Show
  all objectives" rows resolve keys through `UnknownStoryPacks.Text`: a missing key of such a pack (a line's `PackId` the
  client lacks, or no pack id while any story is missing — the world story's beats carry none) reads as
  `ui.story.entry_missing`; a missing key of a known pack stays `[key]` (a content bug). The Journal's task lines
  (`JournalPackPage.StepLine(…, has)`) use the same note.
- **Client one-shot lessons** (`VegaPanel.SayLocal`): callers pass the setter of their `ClientSettings.*HintShown` flag as
  `onShown` (#2551) — it runs when the line comes up on screen, or at once when the lesson counts as given, never for a
  line muted by the hints switch (#2462, B31) or dropped unseen. Since #2469 the flight lessons the tutorial's B6 steps
  teach (`VegaText.LessonCoveredByTutorial`) count as given without being spoken while the basic training runs.
- **VEGA's speech queue** (#2551, `VegaLineQueue` in Client.Core, tested in `Client.Tests/VegaLineQueueTests`). A client
  lesson is bound to its moment: one still waiting unseen after `LessonExpirySeconds` (30 s, real time — a folded tab, a
  pause or a scene included) is dropped and its flag stays unspent, so it comes again the next time the situation does;
  saying a lesson that already waits keeps its place and restarts its wait (the wall probe, the first flight and a nearby
  wormhole keep asking while their situation lasts). A story pack's line (`ShipAiLine.Kind` 0 with a `PackId` — the current
  tutorial or course step's start, hint, done line or skip offer) goes ahead of the hints still waiting (Kind 1, Kind 5 and
  client lessons step back behind it in their own order); story beats, dialogue, system notices and given-back cutscene
  narration are never dropped and keep their arrival order. Server hints never expire — the server books them as told.
- **Pause.** `PausedMayHandle` serves `SetNpcCallsIntent`, `SkipOnboardingIntent`, `RequestStoryResolutionIntent` and
  `StorySelectIntent` while the world is held (each with a dispatch test in `StoryUiFixesTests`); since #2470 also
  `StoryPackIntent` `startPack` ("Start the story now", dispatch test in `VegaStoryPackTests`); since #2471 every
  `StoryPackIntent` action (the Journal can be opened from the pause menu's settings — one dispatch test per action in
  `StoryJournalTests.WhileHeld_EveryJournalAction_IsServed`); since #2478 `CoreDialogueChoiceIntent` (the duel dialogue holds a
  singleplayer world — `VegaFinaleSetPiecesTests.WhileHeld_ADuelAnswerIsServed`).

### The Journal and the objective tracker (#2471)

The Story tab grew into the **Journal** (player-facing word: *stories*, never "pack"). Server side:
`JournalSystem.cs` (facade `GameServerJournal.cs`); client rules:
`src/BlocksBeyondTheStars.Client.Core/Journal/JournalView.cs` (`JournalCategories`, `JournalPackPage`, `ObjectiveTracker`,
`TutorialSwitch` — Unity-free and unit-tested in `Client.Tests/JournalViewTests`); Unity: `CraftingTechShipUI` (Story
mode = the Journal), `VegaPanel` (chip label + "Show all objectives"), `GameBootstrap` (`StoryPackStates`, `Objectives`,
`JournalVersion`, forwarded from Client.Core `StoryClientState`, #2933), `ChatUi` (`/tutorial`, `/story`).

- **`StoryPackStates` (tag 300)** per receiver: the tutorial packs, then the world story (a short entry — names, the
  player's track as `Started`/`Startable`, the finale won as `Done`/`Badge`, the pin; no chapters: the arc stays on tag
  139), then the other player packs. Per player pack: `Enabled` (`StoryPackEnabledFor`), `Started`, `Done`, `OptedOut`,
  `Tracked`, `Startable` (`CanStartPackOnRequest`), `Blocked` (a tutorial while the world's `TutorialPolicy` is `Off`),
  `MandatoryRunning`, `Badge` (done and every mandatory chapter `done`, not skipped); each chapter's state
  (`StoryChapterStates`: `done | skipped | active | replay | offered | declined | available | upcoming | locked`, locked
  with `RequiresKeys` — the title of a chapter not yet done, the name of a blueprint not owned); the active DO steps of
  the running chapters (`ChipKey`/`StartKey` with variants, `MoreGuide`, `Tier`, `Progress`/`Target`, `Index`/`Count` =
  "step i of n" among the chapter's tasks (a stand-in at its primary's place), `Skippable`, `Extra` = an open extra, `OnChip`).
- **`ObjectiveList` (tag 299)**: `ChipCandidates(s)` — the §5.3 chip order as a lazy sequence (finale in the Guardian
  system, pin, basic training, VEGA started → finale/lead, deep dive / mission bridge, every other pack by priority) —
  the first row of each story, at most three; `LiveVegaChip` is its first element. `StepId` lets the Journal take its live
  progress from it; `Pinned` marks the tracked story.
- **When they go out** (only to `packs1`, never to observers): `PushJournal(s, force: true)` at the end of the join and on
  `StoryPackIntent{requestStates}` (the Journal tab opened); after every other `StoryPackIntent` at once (only what
  changed); otherwise coalesced — `MarkJournalDirty(s)` from `SendVegaWithObjective` (every VEGA line and chip update)
  and the runtime's state changes (step start/complete/skip, chapter complete, pack start/done, graduation, offers,
  replays), flushed by `TickJournal` (a post-world step of the tick schedule, every joined player, in space too) at
  most once per `JournalPushGapSeconds` = 1 s, and only the message whose bytes differ from the last send. The states are
  compared with every step counter zeroed, so a progress tick never re-sends them — the objective list carries live
  progress.
- **Actions** (`StoryPackActions`, all validated, all pause-whitelisted): `track`, `untrack`, `optOut`, `optIn` (starts a
  pack that would not start by itself — an opt-in world's tutorial: the Character tab's "Tutorial: on"), `startPack`,
  `skipPack`, `restartPack` (`RestartPack`, refused while the world's tutorial is off), `startChapter`, `declineChapter`
  ("Later"; optional, not running, not through), `skipChapter` (only a running chapter now), `restartChapter` (a done
  chapter is replayed; since #2471 a RUNNING chapter, or a running replay, starts over from its first step the same way),
  `replayChapter` (done or skipped only), `skipStep` (the pack must run), `replayCutscene`, `requestStates`.
  **Replay of a chapter that was not through:** a step completed (or skipped) for the first time inside a replay is also
  stamped `done`/`skipped`, and the chapter completes for real at the end of the replay (`CompleteChapter` writes the done
  key, plays the closer, may graduate) — so "Start part over" on a running part never loses progress.
- **Chip label.** The story's short name replaces "Objective:" — `ObjectiveTracker.ShortNameKey(ObjectivePackId, states,
  local packs)` (server list first, the client's own copy of the pack second, else `ui.vega.objective`). The mute stays
  with `ObjectiveSource` (only `story` follows the VEGA hints switch; synthesis E22's "speech only once 'don't track'
  exists" is not active — there is no per-story "don't show" switch for the world story yet).
- **"Show all objectives"** (`ClientSettings.ShowAllObjectives`, default off; Character tab + pause-menu Settings → Comfort): the
  list's rows after the chip's in up to two strips under the chip, hidden while the chat is open; a story row follows
  the hints switch like the chip (`ObjectiveTracker.ExtraRows`).
- **Notes.** "VEGA's notes" = the VEGA log (`GameBootstrap.VegaLogKeys`): the `VegaJournal` snapshot + `RelayInsightKeys`
  + `NoteKeys` + this session's lines; lines that only point at the Journal (`VegaText.IsTransientLine`:
  `vega.journal.news`, `vega.step.marked_station`) are shown but never kept.
- **Catch-up (B09).** A started `packs1` player who missed `CatchUpCollapseThreshold` = 2 or more beats gets them heard
  silently (knowledge credited, the story state re-sent) and ONE system line `vega.journal.news` ({0} = how many);
  one missed beat still speaks, an older client still gets every line. #2474: the current chapter's opening is committed
  **before** any line (a line it shows waits with it — never read twice); a `cutscenes1` client whose missed beats crossed
  into a later chapter also gets "The story so far" (titles of chapters 1 … current − 1, `PlayCatchUpRecap`) leading into
  that opening; a single missed beat plays its beat card. The Journal line stays (it names the count and lights the
  Journal's badge).
- **Station marker (B6.4).** Reveal provider `nearest_station` (`StoryRevealProviders.NearestStation`, validator C# +
  Python): in flight the `SpaceStation` contact of the player's instance nearest to their ship, landed the first station
  of `StationContactsForCurrentSystem(body)`; the marker is the line `vega.step.marked_station` ({0} = the station's name)
  carrying `ShipAiLine.SpaceWaypointId` — the client's space waypoint (radar + edge arrow), never a ground ping. The
  tutorial's b6.4 uses it.
- **Admin.** `/tutorial skip|restart [player]`, `/tutorial chapter <id> [player]`, `/tutorial policy auto|courses|optin|off`
  (`AdminChatCommand.Tutorial` → `AdminCommandIntent{tutorial}`), `/story [player]` (`story_status` with the target).

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
  `StoryPackValidator` (`StoryPackValidator.cs`: `Validate`, the budgets, the shared helpers; one partial per section —
  `.Run` (the per-pack run state and its condition/key/voice checks), `.Pack` (identity, beats, pacing, start and gates,
  graduation, rewards), `.Texts`, `.Chapters` (chapters, leads, missions), `.Steps`, `.Cutscenes`);
  `Shared/Localization/LineText.cs` (line composition, shared with the client). Server:
  `GameServerGameEvents.cs` (event bus), `GameServerStoryConditions.cs` (condition context + world predicates),
  `GameServerStoryPacks.cs` (the player-pack runtime, chip helpers), `GameServerStoryPackActions.cs` (the player's
  own story controls, the tutorial button and the `/tutorial` / `/story` admin cases), `TutorialSystem.cs` (#2469: the
  teaching verbs, `UiTutorialEvent`, the probes behind the tutorial's predicates) behind its facade
  `GameServerTutorial.cs` (which keeps the hint hold and the migration of the old onboarding),
  `GameServerTutorialBuilding.cs` (#2480: the ship and base building courses' predicates, the own-or-allied base tests,
  the 1 Hz sealed-room and wall-ring checks), `GameServerStoryTrack.cs` (#2470: the world story's per-player track — prologue, start rule, track start, migration);
  `Shared/Story/StoryChapters.cs` (#2470: world-pack chapters, `StoryChapterEvents`); Client.Core `StoryChapterList.cs`
  (#2470: the Story tab's chapter rows); `JournalSystem.cs` behind its facade `GameServerJournal.cs` (#2471:
  `StoryPackStates`, `ObjectiveList`, the coalesced pushes, the world story's notes) and Client.Core
  `Journal/JournalView.cs` (#2471: the Journal's and the tracker's rules).
- Data: `data/stories/tutorial/` (#2469) — the basic training (`story.json`, `locales/en.json` + `de.json`); content table
  in [TUTORIAL_PACK.md](TUTORIAL_PACK.md#the-tutorial-pack-datastoriestutorial-2469).
- Server: `GameServerStory.cs` (state load, Record fragment/kill/milestone, beat reveal, admin selection,
  knowledge level), `GameServerNetFragments.cs` (placement + pickup), `GameServerStoryFinale.cs` (reveal,
  hack, duel, gauntlet, chamber stamping, respawn, pacification; #2478 the set pieces, the ending's delivery).
- Persistence: `story_state` table (`StoredStoryState` + `IWorldRepository.SaveStoryState`/`ListStoryStates`).
- Data: `data/stories/vega_protocol/` — `story.json` (beats, fragments, memories, flavour, threads, duel),
  `lore_sites.json` (environmental lore, #1111) and `locales/<code>.json` in all 14 languages (EN + DE mandatory and
  complete; the twelve community tables are guarded by `CommunityLocaleTests`). There is no separate `lore.json`
  catalogue — the authoring schema in LORE_STRUCTURE §E maps onto `story.json`. Validator:
  `tools/validate_story_pack.py` (#2466, replaces `merge_story.py`).
- Client: `Mode.Story`/`Tab.Story` — the Journal since #2471 (one page per story, VEGA's notes, the player's notes; the
  VEGA page = the old Story Log: meter + re-readable beats/fragments/memories); the client's story state is
  Client.Core `StoryClientState.cs` (#2933: the story state, relay network and net fragments, the Story Log lists and
  beat list, the Journal's stories/objectives/version and its new-offer badge, the unknown stories, the VEGA log; unit-tested
  in `Client.Tests/StoryClientStateTests`), held by `GameBootstrap.Story.cs`, which forwards its members under the old
  names (`GameBootstrap.VegaLogKeys`, `StoryLogFragments`, `NewStoryUnseen`, …) and keeps the reader panel and the waypoint;
  `WorldEntities.cs` three-eyed-robot retheme + `BuildDrone` scan-drone; `FinaleView` (hack bar, gauntlet banner, breach and resume hints — #2478: the duel itself is
  `CutsceneDirectorDuel.cs` + `DuelDialogueScreen.cs`, rules in Client.Core `Cutscenes/CoreDuel.cs`);
  `ClientMusic` finale contexts → `music_boss_*` tracks.
- World options: `GameRules.StoryId` + `GameRules.StoryDensity` (`--story` / `--story-density`) and
  `GameRules.TutorialPolicy` (`--tutorial auto|courses|optin|off`, #2466; `courses` = "Short courses only", #2498), set
  when the world is created and baked into its rules.
  The world-creation panel shows them as ONE row "Stories: VEGA · Tutorial Auto ›" that opens the **Stories** page
  (#2472, `UiWorldOptions.BuildStories`): main story = every installed pack with `StoryRegistry.CanBeWorldStory` (default
  first, then priority, capped at `WorldStoryOptions.MaxChoices`) plus "None (sandbox)", the chosen pack's
  `descriptionKey` under the list, the pace slider, and the tutorial rule with one line per choice. The model, its
  non-default-only arguments and the summary text are `WorldStoryOptions` (Client.Core, unit-tested); the player word is
  "Stories" / "Geschichten", never "pack". Live edits: the density (#2472, `SetWorldRulesIntent.StoryDensity`, re-sent in
  `ServerRules.StoryDensity`; a change runs `AdvanceStory`, so a faster pace reveals what its score covers at once and a
  slower one never takes a beat back) and the tutorial rule (`SetWorldRulesIntent.TutorialPolicy`, admin
  `tutorial policy …`) — both rows in the in-game world rules (Character tab, admin). The main story is switched live by the
  admin's `StorySelectIntent` (#2506, see "Switching the main story keeps progress" above) from the **Main story** row above
  them: the choices are `WorldStoryOptions.SwitchChoices` (the client's world stories plus the server's running one, "None"
  last), shown only when `ServerRules.MainStory` is filled (`ShowsMainStoryRow`), and leaving a running story asks first
  (`SwitchNeedsConfirm` → `CraftingTechShipUI.ShowStorySwitchDialog`). Hosted/glitch worlds pass no story
  flag (VEGA + `Auto`). Admin QA cmds: `advance_story`, `reveal_finale`, `story_status`, `reveal_lore`, `goto_core`,
  `tutorial`.

## Story and VEGA text conventions (#2463)

- **Canon.** Every VEGA line follows [LORE_STRUCTURE.md](LORE_STRUCTURE.md) — including VEGA's older memory arc
  (`vega.mem.1..10`, redeemed from `ai_memory_fragment`, still in the core locale; since #2470 bound to the VEGA story —
  a world without it hears the neutral `vega.mem.archive.*` lines). Its texts were
  reconciled with the pack in one approved canon pass: the Meridian is an SPS survey fleet; the fleet did not scatter,
  it was stopped ship by ship; VEGA's captain cut her ship loose at VEGA's own request; the last fragment brings back
  her schematics, not every memory. The player memory `mem_the_order` is "you were in the room when the order went
  out" — every clone remembers being present, nobody gave *the* order (no chosen one). The SPS has one long form,
  **Scout and Pioneer Service**, in every language.
- **Keys.** Player text never names a key; it uses `{key:Action}` tokens (an `InputAction` name). `VegaPanel`, the
  HUD prompts and the `@srv.*` server messages (`GameBootstrap.ResolveServerToken`) resolve them through
  `HudUi.KeyTokenGlyph` — the binding, the pad button (the Actions list for an
  action without one), or on a tablet the layout's own button (`VegaText.TouchControlLabel`: USE, MAP, ≡ …) and else
  "the on-screen action menu" — in a short HUD prompt, button or toast (`HudUi.ExpandKeyTokens`) "ACT", the button
  that opens that list (#2502). `VegaLineBudgetTests` (Client.Tests) fails on a token that names no `InputAction` and
  on a literal key in the VEGA lines (core, both packs) and the converted prompts (since #2502 also the HUD, flight
  and button texts, with a wider pattern: "G:", "(E)", "[P]", mouse buttons, WASD, Space, Ctrl). A text the server
  `Localize`s itself (`npc.streamer.ask`) never carries a token — the client would show the braces.
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
| `nextMissionId` | the following step (drives the giver's `npc.call.chain_next` radio nudge after turn-in — or, for a `vega` giver, VEGA's own `vega.chain.next` / `vega.chain.next_research` line at once) |
| `giverRole` | `quartermaster` (default) · `vendor` · `settler` · `character:<id>` (an authored recurring face — remembers the player globally) · `vega` (#2481, B55: VEGA hands the chain out — radio surface only; no NPC relationship on accept; after a turn-in VEGA says in her panel what comes next, without a radio item or the NPC-calls preference; quiet while a running story-pack step bridges onto that next job — its start line is the follow-up. The Crystal workshop) |
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

Add a pack under `data/stories/<id>/` — no engine edits. The authoring path (folder, manifest, conditions, events,
cutscenes, texts, validation, what still needs C#, the limits of v1) is [STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md),
with the generated vocabulary tables in [STORY_VOCABULARY.md](STORY_VOCABULARY.md); the pilot side story `side_pilot`
(a test pack under `tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/`, #2483) proves that a player pack needs no
code and runs next to the basic training and VEGA's story. A second **world** story with its own finale is engine work
(one world story per save in v1). A world pack is "story-complete" only once its beats, fragments, memories, flavour and
`coreArguments` are authored + translated (DE+EN), per LORE_STRUCTURE.

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
  a `story.obj.*` line (gated client-side by the VEGA-hints setting) — since #2475 the chapter's lead
  (`story.vega.obj.*`) for a current client.
- Leads (#2475) that need a galaxy index are not in v1: fragments or monuments on other worlds, the nearest data
  terminal, an unknown neighbouring system. The `machines` lead exists in the vocabulary but no VEGA chapter lists it.
  A followed lead speaks no line.
- Environmental lore (#1111): `data/stories/<pack>/lore_sites.json` — weighted, knowledge-gated texts per
  site kind (monument scan / structure loot), revealed once per player (`PlayerState.Milestones`
  `lore:<key>`), listed in the Codex "Lore" chapter. NPC story threads (#1112): `npcThreads[]` in the
  pack — role + relationship stage + knowledge gate, once per player, may hand over a fragment.
- Numeric tuning (`Wf/Wk/Wm`, kill cap, thresholds, memory-drop chance, gauntlet HP) stays data-driven.

## Appendix — boss/finale music

Five staged instrumental tracks (`music_boss_{approach,gauntlet,hack,dialogue,resolution}.mp3`, generated in
Suno.ai per [MUSIC_TRACKS.md](MUSIC_TRACKS.md)) are wired to the matching `ClientMusic` finale contexts.
