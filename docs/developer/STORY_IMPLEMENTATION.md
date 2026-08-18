# Story engine ("The VEGA Protocol") — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-06-19.

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
- **Milestones** — repeatable: mission turn-in (settlement helped) and first-discovery system mapping;
  **once per save** (#1105, `RecordStoryMilestone(onceKey)`, keys persisted in `StoryState.MilestoneKeys`):
  first base founded, first player station commissioned, first self-built ship commissioned, first companion
  tamed, first hyperjump, first monument scanned per world (`monument:<body>`). Pacing note: fragments cap
  at 6 (18 pts) and kills at 40, so before #1105 the remaining ~130 of the 204 needed came almost entirely
  from delivery turn-ins; the once-firsts add roughly 10–20 pts per playthrough from building, taming and
  exploring — they widen the arc's inputs without making any of them farmable.

**Beats** are spoken through the existing VEGA narrator pipeline (`ShipAiLine`); the speaker identity is a
pack field, so no new beat UI. Per-player "seen beats" reuse `PlayerState.Milestones` (`story:<id>:beat:N`)
— no schema migration. Latecomers get a join-time catch-up of earned-but-unseen beats.

**Wreck coupling** (count-neutral): a nearby wreck biases machine *position* (cluster) and *aggression* only,
never the count — toggled by `GameRules.MachineWreckCoupling`.

**Finale** (staged, weapons cannot end the core):
1. Arc-complete (all beats seen) flips a one-way `guardianSystemRevealed` flag → broadcasts the lone landable
   `guardian_finale` system onto the star map (lazily appended to the galaxy on reveal, re-appended for
   revealed saves on restart; reserved from the procedural generator). Reaching it needs the `jump_generator`.
2. **Stage 1 — gauntlet:** the finale flight instance fields a scripted elite wave (heavy cruiser + elite
   UFOs + reinforced drone swarm), not ambient hostiles.
3. **Stage 2 — two routes to the inner core:** a buried iron chamber with a glowing red core column, reached
   either via a pre-carved descent shaft (Route A) or by digging down through the shell (Route B). Both
   converge; the breach is proximity-gated to the core terminal.
4. **Stage 3 — hack:** hold to channel a server-authoritative `CoreHackProgress`; at 100% the duel opens.
5. **Stage 4 — argument duel:** data-driven from the pack's `coreArguments`; a correct contradiction
   advances, a wrong pick re-presents the node (the duel can stall but never be lost). Clearing the last node
   calls `MarkGuardianDefeated`.
6. **Pacification:** the one-way `guardianDefeated` flag gates the pack's planet + space machine spawns off
   and despawns live machines (it does **not** touch the admin threat sliders). A death in the boss system
   respawns the clone on the world it launched from (no death-loop).

## Networking (NetCodec tags)

| Tag | Message | Dir | Purpose |
|---|---|---|---|
| 139 | `StoryStateMessage` | S→C | active story, progress %, counts, kills, beats seen, flags |
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

Beat speech reuses `ShipAiLine`. Every message is `Register()`'d in NetCodec.

## Key files / classes

- Engine (story-agnostic): `src/BlocksBeyondTheStars.Shared/Story/` — `StoryDefinition`, `StoryBeat`,
  `StoryState`, `StoryEngine`, `StoryRegistry`, `CoreArgument`/`CoreArgumentChoice`.
- Server: `GameServerStory.cs` (state load, Record fragment/kill/milestone, beat reveal, admin selection,
  knowledge level), `GameServerNetFragments.cs` (placement + pickup), `GameServerStoryFinale.cs` (reveal,
  hack, duel, gauntlet, chamber stamping, respawn, pacification).
- Persistence: `story_state` table (`StoredStoryState` + `IWorldRepository.SaveStoryState`/`ListStoryStates`).
- Data: `data/stories/vega_protocol/` (`story.json` + `locales/{en,de}.json`), `tools/merge_story.py` validator.
- Client: `Mode.Story`/`Tab.Story` Story Log tab (meter + re-readable beats/fragments/memories);
  `WorldEntities.cs` three-eyed-robot retheme + `BuildDrone` scan-drone; `FinaleView` (hack bar + duel panel);
  `ClientMusic` finale contexts → `music_boss_*` tracks.
- World options: `GameRules.StoryId` + `GameRules.StoryDensity` (`--story` / `--story-density`, world-creation
  panel). Admin QA cmds: `advance_story`, `reveal_finale`, `story_status`, `reveal_lore`, `goto_core`.

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
