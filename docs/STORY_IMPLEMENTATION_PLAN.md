# Story Implementation Plan — pluggable storylines (full build-out)

The complete engineering plan for the story system. The **engine is story-agnostic**: each storyline is a
swappable **story pack** (content + config), and the world picks which one is active. The first pack is
**"The VEGA Protocol"** (design in [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md); canon
in [LORE_STRUCTURE.md](LORE_STRUCTURE.md)). Phased so each phase is independently mergeable and (where
possible) `dotnet test`-verifiable before any Unity build.

> **Status:** implementation plan, nothing built yet. *What is built* is tracked in [../TODO.md](../TODO.md)
> — update per phase. This doc is the *how*; the concept doc the *why*; the lore doc the *what* (canon).
>
> **Conventions:** English doc; every in-game string ships **bilingual DE+EN** (locale parity enforced by a
> test). Server stays authoritative.
>
> Last updated: 2026-06-14 (rev. 4 — pluggable storylines + in-game story selection; new Story Log tab
> (separate from the existing Codex/Wiki); SPS/clone canon; three machine types + player memories;
> multi-stage two-route dialogue-duel finale + boss-system respawn rule + Suno music).

---

## 1. Goal & scope

Deliver a **generic story engine** plus the first full storyline. The engine: a per-save, per-story state;
threshold-paced narrator beats; text-only **net fragments** found in the world; **player memories** dropped
by defeating machines; a visible progress meter; a **new Story Log tab** to re-read everything (separate from the existing
Codex/Wiki); and a staged
**finale**. It must support **adding further storylines** and **selecting the active story** in-game.

The first pack ("The VEGA Protocol"): amnesiac **VEGA** recovering the **SPS** (Scout and Pioneer Service)
truth; the player revealed as a **clone** grown from a dead SPS member's **neural imprint**; planet enemies
as **black three-eyed ground robots** + **black flying scan-drones**, space as **UFO drones**, all remnants
of the **Guardian core** (*Wächter-KI*); a finale that ends the dormant core by **exposing its
contradiction**, pacifying the galaxy.

**Non-goals:** new combat system (boss reuses ship combat); new networking transport; replacing dataqube
mini-games.

## 2. Architecture decisions (locked)

- **D1 — Story state is per save *and* per story** (`storyId`). Lives on `GameServer` (mirrors the
  server-wide alliance graph), persisted. Only *live machines* and the *wreck coupling* are per-body
  (`LoadedWorld`).
- **D2 — Pluggable storylines (the core requirement).** The engine is **story-agnostic**: state, pacing,
  fragment spawn, beats, Story Log and finale are generic and **driven by the active story pack's config +
  content**. No VEGA-specific logic in the engine. Adding a storyline = adding a pack, not editing the
  engine.
- **D3 — A story pack** = `data/stories/<id>/` containing: `story.json` (id, display name DE+EN, narrator
  identity, score weights/thresholds, fragment categories, milestone set, finale config, enemy/asset hooks)
  + `lore.json` (beats, fragments, player memories, flavour, mission threads, finale `core_argument`) +
  locale entries. A `StoryPack`/`StoryDefinition` model loads it into `GameContent`; a `StoryRegistry` lists
  installed packs. Merge tool `tools/merge_story.py`.
- **D4 — The active story is a per-world choice.** Chosen at **world creation** (a world option) and shown
  in the in-game **Story tab**; switchable only by the world admin; a **"None / Sandbox"** option disables
  story entirely. Default pack = `vega_protocol`. Persisted with the world; story-state keys on
  `(save, storyId)`.
- **D5 — Reuse the narrator pipeline.** Beats are spoken via the existing `SendVegaLine(...)` → `ShipAiLine`
  message/panel ([GameServerShipAi.cs](../src/BlocksBeyondTheStars.GameServer/GameServerShipAi.cs)); the
  *speaker identity* is a pack field (VEGA for pack 1). No new dialogue UI for beats.
- **D6 — Per-player "seen beats" reuse `PlayerState.Milestones`** (persisted `HashSet<string>`), keys
  `story:<storyId>:beat:<n>` — the `vega:stage:*` pattern. No schema migration (like `FacePixels`).
- **D7 — The existing memory-fragment arc is pack 1's `vega` track.** `ai_memory_fragment` (10 beats, +3
  knowledge, grants `ai_core_mk3`) is generalised into the engine, not replaced.
- **D8 — Net fragments are text-only pickup events**, separate from `ServerDataCube` mini-games.
- **D9 — Pacing by threshold score** (weights from the active pack):
  `progress = fragments·Wf + min(machineKills, killCap)·Wk + milestones·Wm`. Beats fire on crossing
  thresholds, not on a specific fragment — makes a linear arc work in a random world.
- **D10 — Three machine enemy types feed the kill counter:** space **UFO drones**, planet **black
  three-eyed ground robots** (retheme of the existing walking enemy), planet **black flying scan-drones**
  (new hovering enemy). Organic fauna is excluded.
- **D11 — Player memories** drop (chance) on machine kills → personal, per-player unlocks (the cloned SPS
  member's life); non-contradictory in MP (each player = a different imprint).
- **D12 — Finale pacification is a one-way per-save flag** (`guardianDefeated` for pack 1) set on **winning
  the finale dialogue duel** (not a weapon kill); it gates the pack's enemy spawns off and **does not** touch
  the admin threat sliders.
- **D13 — Count-neutral coupling.** Wrecks bias machine *position* + *aggression* only, never counts; they
  still spawn without wrecks; applies per population.
- **D14 — Content authoring is its own workstream (W-A).** The engine can ship before the full corpus; a
  pack is "complete" only once its beats/fragments/memories/flavour/argument are authored + translated. The
  canon + structure + starter texts live in [LORE_STRUCTURE.md](LORE_STRUCTURE.md), **which still needs the
  full corpus written out** before integration.

## 3. Shared technical artifacts (built in P0–P1, used throughout)

### 3.1 NetCodec tags (indicative — confirm next-free against NetCodec at impl time; existing run to 138)
| Tag | Message | Dir | Purpose |
|---|---|---|---|
| 139 | `StoryStateMessage` | S→C | active storyId, progress %, per-category counts, machineKills, beats seen, flags |
| 140 | `NetFragmentFoundIntent` | C→S | player opened a fragment at a world position |
| 141 | `NetFragmentRevealed` | S→C | category + archive text key (reader + Story Log) |
| 142 | `PlayerMemoryRevealed` | S→C | a personal memory unlocked by a machine kill (reader + Story Log) |
| 143 | `GuardianSystemRevealed` | S→C | adds the finale system to the star map |
| 144 | `CoreDialogueMessage` | S→C | the core's assertion + counter-argument options (finale duel) |
| 145 | `CoreDialogueChoiceIntent` | C→S | the player's chosen counter-argument |
| 146 | `CoreHackIntent` | C→S | channel one core-hack tick (server owns the increment) |
| 147 | `StorySelectIntent` | C→S | admin sets the active story (world option / Story tab) |
| 148 | `NetFragmentList` | S→C | net fragments on the current world (surface, datacube-style) |
| 149 | `CoreHackProgress` | S→C | core-hack channel progress (0..100) + complete (opens the duel) |

Beat speech reuses `ShipAiLine`. **Every new message must be `Register()`d in NetCodec** or it silently
no-ops. The Story Log tab reads the player's full found-list from `StoryStateMessage` (or a dedicated
`StoryLogRequest` if the payload grows).

### 3.2 Persistence
- **Table `story_state`** keyed by `storyId` (+ the save) + `StoredStoryState`; columns: active story,
  per-category fragment counts, `machine_kills`, `milestones`, `beats_revealed`, finale flags, the
  found-fragment set (dedupe). Save/Load via `IWorldRepository`, loaded at start — pattern of
  `StoredAlliance`/`StoredBeam`.
- **Active-story selection** persisted with the world/save record.
- **Per-player** seen beats + unlocked player-memories ride in `PlayerState.Milestones` — no new table, no
  migration.

### 3.3 Data: story packs (`data/stories/<id>/`)
`StoryPack`/`StoryDefinition` in `Shared/Definitions` load `story.json` + `lore.json` into `GameContent`; a
`StoryRegistry` enumerates installed packs. `lore.json` types (schema in [LORE_STRUCTURE.md](LORE_STRUCTURE.md)
§E): `vega_beat`, `net_fragment` (category/weight/text/reward/revealHint), `player_memory` (NEW; source
machine kinds), `flavour_line`, `mission_thread`, `core_argument` (finale duel). `tools/merge_story.py`
folds authored JSON + locale placeholders, mirroring the other merge tools.

---

## 4. Phase plan

Each phase lists **server / data / net / persistence / client / tests / build / DoD**, sized **S/M/L**.
"⚙️ Unity" = needs a client build ([scripts/build-client.ps1](../scripts/build-client.ps1)).

### P0 — Generic story engine + state + pacing — **M** (server-only)
> **✅ P0 COMPLETE (2026-06-14).** Landed: the story-agnostic pacing engine in `Shared/Story/`
> (`StoryDefinition`/`StoryBeat`/`StoryState`/`StoryEngine`/`StoryRegistry`, code-defined `vega_protocol`
> B0–B12 pack + `none` sandbox sentinel); `story_state` persistence (`StoredStoryState` +
> `IWorldRepository.SaveStoryState`/`ListStoryStates` + SQLite table); `GameServerStory.cs`
> (`LoadStoryState` at start, `RecordStoryFragment`/`RecordStoryMachineKill`/`RecordStoryMilestone` →
> threshold beat reveal via `ShipAiLine` kind 2, per-player catch-up on join with
> `story:<id>:beat:N` milestones, admin `SetActiveStory` + "none"); networking `StoryStateMessage` (139) +
> `StorySelectIntent` (147), registered + dispatched + sent on join. **21 story tests; 542 total green, 0
> failed.** ⏭ **Next: P1** (story pack data format + loader — move the code-defined pack to
> `data/stories/vega_protocol/` and author the B0–B12 text DE+EN).
- **Server:** new partial `GameServerStory.cs` with the **story-agnostic** engine: per-`storyId`
  `StoryState`; `RecordFragment`, `RecordMachineKill`, `RecordMilestone`, `RecomputeBeats`; pacing reads the
  **active pack's** weights/thresholds (D9). `StoryRegistry` + active-story selection (per-world, persisted,
  default `vega_protocol`, plus "None"). Beat reveal writes `story:<id>:beat:N` + speaks via the pack's
  narrator (D5).
- **Persistence:** `story_state` (§3.2). **Net:** `StoryStateMessage` (139), `StorySelectIntent` (147).
- **Tests:** thresholds fire correct beats; kill cap; idempotent reveals; persistence round-trip; **two
  packs** (a dummy second pack proves the engine is generic); story selection + "None" disables.
- **DoD:** the engine drives any pack's beats deterministically and survives save/load; a second (stub) pack
  works without engine edits. **No Unity build.**

### P1 — Story pack format + loader + the VEGA pack scaffold — **S–M** (server-only)
> **✅ P1 COMPLETE (2026-06-14).** Story-pack data format + loader: `data/stories/vega_protocol/story.json`
> (config + the B0–B12 arc) + `locales/{en,de}.json` (bilingual beat text), discovered by
> `ContentLoader.LoadStoryPacks` and installed into `GameContent.Stories` (each pack's locale files merged
> into the shared tables; the built-in `StoryRegistry` pack is kept as a fallback). `GameServerStory` now
> resolves packs via `_content` (`DefaultStory`/`TryGetStory`). `tools/merge_story.py` validates a pack
> (sequential ids, monotonic thresholds, en+de key coverage). **4 new tests (data pack loads, matches the
> built-in = no drift, bilingual text resolves); 546 total green.** ⏭ **Next: P2** (net fragments findable
> in the world — including the surface, datacube-style source — + the text reader).
- **Data:** `data/stories/vega_protocol/{story.json,lore.json}` + `StoryPack` loader + `tools/merge_story.py`.
  Author the pack **config** (narrator=VEGA, weights, categories, finale config) and the **B0–B12** beats +
  starter fragments/memories with **DE+EN** keys (from LORE_STRUCTURE §D/§H). Map `ai_memory_fragment` onto
  the pack's `vega` track (D7).
- **Tests:** pack loads; **locale parity** (en/de) green; B0–B12 ordering.
- **DoD:** the first storyline is data; adding lines needs no code. **No Unity build.** *(Full corpus =
  workstream W-A.)*

### P2 — Net fragments findable (Layer B) + reader — **M** · ✅ server + client build-verified
> **✅ P2 COMPLETE — server + Unity client build-verified (2026-06-14).** *(Optional follow-ups only: a proper
> re-readable reader panel — today folds into the P3 Story Log tab; structure-placed fragments + pity/budget —
> combat already de-risks soft-lock.)* Landed (server): `StoryFragment` model +
> 6 authored fragments (one per lore category, DE+EN) in the pack; `GameServerNetFragments.cs` — a
> deterministic, **datacube-style surface placement** drawing from the active pack's still-needed pool
> (weighted), idempotent per resident world, deduped vs the found-set; **pickup**
> (`NetFragmentFoundIntent`, reach-checked) → `NetFragmentRevealed` (archive text) + `RecordStoryFragment`
> (advances the arc) + removal/rebroadcast; `NetFragmentList` sent on world entry + join. Tags **140/141/148**
> registered + dispatched. **4 new tests (determinism, valid+unique keys, pickup→story, no re-offer after
> relaunch); 550 total green.**
> **✅ Client wired + build-verified (2026-06-14 — Unity headless build compiles clean):** `NetFragmentView.cs` renders the
> scattered **category-tinted shards** (added in `WorldRig`); `PlayerController` E-pickup →
> `SendNetFragmentFound`; `NetworkClient` + `GameBootstrap` wire all four story messages
> (`StoryStateMessage`/`NetFragmentList`/`NetFragmentRevealed`/`PlayerMemoryRevealed`) — a revealed
> fragment/memory shows as a toast and its text is kept in a client **Story Log** (`StoryLogFragments/
> Memories/Beats`) for the P3 tab. `ui.netfragment.prompt` added DE+EN. **⏳ Remaining (⚙️ Unity):** a proper
> re-readable reader (folds into the **P3 Story Log tab**). **Structure-placed fragments** + **pity / budget**
> are follow-ups (combat already de-risks soft-lock).
- **Server:** in [GameServerStructureLoot.cs](../src/BlocksBeyondTheStars.GameServer/GameServerStructureLoot.cs)
  + wreck/vault/data-cache/station-archive generation, place fragment pickups for the **active pack**:
  seed-deterministic category draw from the still-needed pool, per-system budget, dedupe vs found set,
  **pity/top-up guarantee**. On pickup → `NetFragmentFoundIntent` → `RecordStoryFragment` + push
  `NetFragmentRevealed`.
- **Surface finds (datacube-style):** besides structures, net fragments also spawn **scattered on planet
  surfaces** like the existing `ServerDataCube`s — same seed-deterministic placement + walk-up pickup — but
  open a **text reader** instead of a mini-game, feeding the same `RecordStoryFragment` hook. Reuse the
  DataCube placement/pickup pattern ([GameServerDataCubes.cs](../src/BlocksBeyondTheStars.GameServer/GameServerDataCubes.cs)),
  not a new system; they stay text-only and distinct from dataqubes (knowledge mini-games).
- **Net:** 140/141. Register. **Client:** a minimal **Fragment Reader** panel (title + archive text).
- **Tests:** placement determinism, pool draw, dedupe, pity under starved RNG.
- **DoD:** fragments appear, read as text only, advance the meter. **Needs Unity build.**

### P3 — Beats + progress meter + **new Story Log tab** + milestones — **M** · ✅ server + client build-verified
> **✅ P3 COMPLETE — server + Unity client build-verified (2026-06-14).** *(Optional follow-ups only: reader
> polish — wrapping/scroll height is estimated; the base/station-built milestones.)*
> Server: `RecordStoryMilestone` is hooked into **mission
> turn-in** (settlement helped — `GameServerMissions`) and **new system mapped** (`MarkArrivedOnBody` /
> `MarkSystemKnown`, first discovery per player). Join-time beat catch-up + the meter payload
> (`StoryStateMessage`) already ship from P0. Test confirms mapping the start system records a milestone.
> **552 total green.**
> **✅ Client wired + build-verified (2026-06-14 — Unity headless build compiles clean):** a **new Story tab** (`Mode.Story` /
> `Tab.Story` / tab-bar entry, separate from the existing Codex/Wiki) renders `BuildStoryList` — the
> **"Star network: NN %" meter** + counters and re-readable sections for **beats / recovered fragments /
> personal memories** (reads `GameBootstrap.Story` + the `StoryLog*` buffers). `ui.tab.story` + `ui.story.*`
> added DE+EN. ⏳ Remaining: polish (paragraph wrapping/scroll height is estimated; tab-bar width is tight
> with 9 tabs) + the base/station-built milestones (small follow-up).
- **Server:** milestone hooks (`RecordMilestone`): system mapped, settlement helped (mission complete),
  first base/station built; join-time catch-up reveals earned-but-unseen beats.
- **Client — the new Story Log tab (the requested net-fragment tab):** a **brand-new in-game menu tab**
  (alongside Inventory/Map/…) for the active story showing: the **net fragments found** (grouped by category,
  each re-readable), the **narrator beat history** (every VEGA line so far, re-readable), the **player
  memories** unlocked, and the **"Star network: NN %"** progress meter + which story is active. Reads
  `StoryStateMessage` (+ reader payloads). A new `GameMenu.Tab` entry + a `CraftingTechShipUI`-style view
  (mirrors how the Alliances tab was added). **Do NOT touch or reuse the existing "Codex" tab — that is the
  in-game game-wiki and stays exactly as-is.** This is a separate, new tab.
- **Client:** beats play through the existing VEGA panel; the meter also shows on the Map tab.
- **Tests:** beat ordering across mixed triggers; per-player seen-set; milestone counting; the Story Log tab
  lists fragments/beats/memories; latecomer catch-up.
- **DoD:** a guided arc + a visible goal + a re-readable Story Log (a new tab, existing Codex/Wiki untouched).
  **This + P0–P2 is the playable MVP.** **Needs Unity build.**

### P4 — Planet machines (three-eyed robots + flying scan-drones) + combat-as-progress + player memories — **M** · ✅ server + client build-verified
> **✅ P4 COMPLETE — server + Unity client build-verified (2026-06-14): three-eyed robot retheme + flying
> scan-drone + combat-as-progress + player memories.** *(Optional follow-ups only: robotic SFX — the machines
> still reuse the organic growl; a dedicated memory-reader panel — today a toast + the Story Log tab.)*
> Server: defeating a Guardian machine now advances the shared
> story: `RecordStoryMachineKill` is hooked into the **planet-enemy kill**
> ([GameServerEnemies.cs](../src/BlocksBeyondTheStars.GameServer/GameServerEnemies.cs), the `!isCreature`
> branch) and the **space hostile destroy** (`GameServerSpaceCombat`, `target.Hostile`); organic fauna is
> excluded. End-to-end test (kill a planet machine → exactly one kill recorded, the arc advances).
> **✅ Player memories also landed (server):** `StoryMemory` model + 4 authored memories (DE+EN) in the pack;
> `TryDropPlayerMemory` fires on each machine kill (34% chance) → unlocks the killer's **next** unfound
> memory in order (`PlayerState.Milestones` `story:mem:*`) + sends `PlayerMemoryRevealed` (142); per-player,
> non-contradictory in MP. **557 total green.**
> **✅ Client retheme done + build-verified (2026-06-14):** `WorldEntities.cs` now renders the planet enemy
> as the **black three-eyed Guardian robot** — dark-metal plating + mid-grey trim + a row of **three glowing
> RED sensor "eyes"** (the existing model already had 3 eyes; an optional `enemy_robot` plating tile is used
> if present). Headless Unity build green, 0 compile errors.
> **✅ Flying scan-drone COMPLETE — server + client, build-verified (2026-06-14):** new
> `CombatEntityKind.ScanDrone` + `GameRules.PlanetDrones` toggle (default on). Spawn is a **count-neutral mix**
> inside the existing planet-enemy population (`asDrone = PlanetDrones && (_planetEnemies.Count % 5) < 2`, ~2-in-5),
> so the total count stays governed by `PlanetEnemies`. Drones **hover** (`ScanDroneHover = 4` above `groundY`;
> `MovePlanetEnemy` keeps the offset), are lighter (hull 25, 3 dmg/s) and excluded from the `tougher` branch.
> Kills feed the story + memory drops like any machine. Client renders them via `WorldEntities.BuildDrone` — a
> small dark hovering pod + single **red scanner eye** + three sensor fins, with a bob + slow scanning-yaw
> animation (limbs skipped). Branch keys on the networked `NetCombatEntity.Kind == "ScanDrone"` string (no
> networking change). **557 total green;** headless Unity build green. ⏳ **Unity remainder for P4:** robotic SFX
> (today still reuses the organic growl) + a proper **memory reader** (today: toast + the Story Log tab lists them).
- **Server (progress):** increment `machineKills` in the kill paths of `AttackCombatEntity`
  ([GameServerEnemies.cs](../src/BlocksBeyondTheStars.GameServer/GameServerEnemies.cs)) and space
  (`GameServerSpaceCombat`) with **diminishing returns / per-tier cap** (D9); covers all three machine types;
  fauna excluded.
- **Server (ground robots):** retheme the existing walking planet enemy
  (`CombatEntityKind.Creature`/`AlienMonster`) into the **black three-eyed ground robot** — keep ground-hugging
  AI (`MovePlanetEnemy` stays on `groundY`); rename loot/strings; new model.
- **Server (flying scan-drones):** **new** kind (e.g. `CombatEntityKind.ScanDrone`) that **hovers** (Y-offset
  above `groundY`), ground counterpart of the UFO; own frequency control (recommend a new
  `GameRules.PlanetDrones` activity, independently toggleable; *decision below*), reusing
  `SpawnPlanetEnemyNear`/`MovePlanetEnemy` with a hover branch.
- **Server (player memories, D11):** on a machine kill, a small chance drops a damaged memory remnant →
  `PlayerMemoryRevealed` (142) → unlock a personal `player_memory` for **that** player (stored in
  `PlayerState.Milestones`), shown in the Story Log tab.
- **Client:** **two** new models in
  [WorldEntities.cs](../client/Assets/BlocksBeyondTheStars/Scripts/WorldEntities.cs) (three-eyed ground robot
  + flying scan-drone; mirror the UFO pipeline) + VFX; a small memory-reader.
- **Assets (blanket-approved):** textures for the three-eyed robot + the scan-drone (OpenAI); optional
  movement/attack SFX (ElevenLabs).
- **Tests:** kill cap across kinds; `ScanDrone` spawns + hovers; ground robots still ground-hug; memory-drop
  chance + per-player unlock; `EnemyMovementTests` green.
- **DoD:** two machine threats on planets, both feeding progress; fighting them also unlocks personal
  memories. **Needs Unity build** + assets.
- **Decision (resolved):** `PlanetDrones` shipped as a **bool toggle**, not a separate frequency slider, and
  the drone count is a **count-neutral mix inside** the existing `PlanetEnemies` cap (~2-in-5 of the population)
  — per the user's directive that adding drones must not change planet-enemy or space-enemy frequency. Toggling
  it off restores an all-ground-robot population at the same total count.

### P5 — Count-neutral machine/wreck coupling — **S–M** (server-only)
> **✅ P5 COMPLETE (2026-06-14).** `GameRules.MachineWreckCoupling` (default on, live-editable). In
> `SpawnPlanetEnemyNear`: when a wreck (centroid of `LoadedWorld.WreckMarkers`) is within 64 of the target
> player, the spawn is **biased to cluster at the wreck** (4–13 blocks around it) and the machine spawns
> **angrier there** (×1.5 damage) — the spawn cadence + cap are untouched, so the **count is unchanged**.
> Off restores uniform golden-angle spawning. **3 tests (cluster on, not-clustered off, count-neutral);
> 555 total green.** (Note: enemy X is normalised to the per-world circumference — distance checks must be
> wrap-aware. Space-side coupling is a follow-up; space lacks a clear wreck anchor in the current model.)
- **Server (planet):** in `SpawnPlanetEnemyNear`, when a wreck (`LoadedWorld.WreckMarkers`/`WreckOrigin`) is
  in range, **bias the chosen spawn position toward it** (keep cadence + cap); in-radius **aggression
  modifier** in `MovePlanetEnemy` + damage. **Server (space):** same positional bias toward a wreck/derelict.
- **Rules:** a `GameRules` bool toggle (default on); **does not touch the frequency sliders**; applies
  independently to each population (ground robots, scan-drones, space UFOs).
- **Tests:** **count-neutrality** (same number with/without a wreck), bias near wrecks, baseline fallback,
  aggression only in-radius.
- **DoD:** wrecks feel dangerous; counts provably unchanged. **No Unity build.**

### P6 — Finale: Guardian system + multi-stage two-route core confrontation + pacification — **L** · ⚙️ Unity
> **🟦 P6 — server backbone of the finale flow COMPLETE (2026-06-14).** The whole reveal → hack → duel →
> pacification chain now runs + persists server-side, fully tested; only the Unity encounter + world-gen remain.
> Landed:
> - **Boss music** generated + filed (`music_boss_*.mp3`, see [MUSIC_TRACKS.md](MUSIC_TRACKS.md)).
> - **Reveal gating** — when the arc completes (every beat revealed), `RevealGuardianSystemIfReady()` flips the
>   persisted `guardianSystemRevealed` flag **once**, speaks the reveal line to all, and broadcasts
>   `GuardianSystemRevealed` (143). Wired into `AdvanceStory`.
> - **Core hack** (stage 3, channel-and-defend) — `CoreHackIntent` (146) accumulates server-authoritative
>   progress → `CoreHackProgress` (149); reaching 100 opens the duel. Gated on revealed && !defeated.
> - **Argument duel** (stage 4) — data-driven from the pack's new `coreArguments` (4 authored contradiction
>   nodes, bilingual): `CoreDialogueMessage` (144) presents the core's claim + rebuttals; `CoreDialogueChoiceIntent`
>   (145) walks nodes — a **correct (contradiction) pick advances**, a **wrong one is dismissed and re-presents
>   the node** (the duel can stall but **never be lost** — weapons can't end the core). Clearing the last node
>   speaks the resolution line and calls `MarkGuardianDefeated` (pacification).
> - **Pacification gating** (unchanged) — `MarkGuardianDefeated()` sets the one-way `guardianDefeated` flag,
>   despawns live planet machines, persists + broadcasts; `PlanetEnemiesActive` + the space spawn both gate on it.
>
> New files: [GameServerStoryFinale.cs](../src/BlocksBeyondTheStars.GameServer/GameServerStoryFinale.cs),
> [GameServerFinaleTests.cs](../tests/BlocksBeyondTheStars.Tests/GameServerFinaleTests.cs) (6 tests: reveal-once,
> no-hack-before-reveal, hack-opens-duel, wrong-stalls, correct-path-wins, no-choice-before-hack). `CoreArgument`/
> `CoreArgumentChoice` added to [StoryDefinition.cs](../src/BlocksBeyondTheStars.Shared/Story/StoryDefinition.cs).
> **563 total green.**
>
> **🟦 P6 — Guardian-system generation + reveal-to-map + respawn rule COMPLETE (2026-06-14).** Reaching the
> finale + the death-loop guard now work server-side, tested:
> - **Guardian system on the map** — `EnsureGuardianSystemInGalaxy()` lazily appends a lone, landable
>   **Guardian Core** body (system id `guardian_finale`, far map corner) to `_galaxy.Systems` **only when
>   revealed** (and re-appends it after a restart for an already-revealed save, since the galaxy is
>   seed-regenerated each start — added *after* start-body selection so it never affects the spawn world).
>   `RevealGuardianSystemIfReady` now also `BroadcastStarMap()`s, so the system appears as a jump target the
>   moment the arc completes; reaching it needs the existing `jump_generator` (the hyperjump path already
>   requires it). Before reveal the system exists nowhere — zero impact on generation/map/tests.
> - **Respawn-at-prior-world** — on hyperjump INTO `guardian_finale` the world jumped from is recorded
>   (`_finaleReturn[playerId]`, runtime); `RecoverToShip` routes a death in the Guardian system back to that
>   world via `ResolveRespawnHome()` (re-homing the ship there, consuming the record) — no boss-arena
>   death-loop; the finale must be re-approached. **565 total green** (+2: reveal-adds-system, death-respawns-home).
>
> **🟦 P6 — Stage 1 space gauntlet COMPLETE (2026-06-14, server).** The finale system fields its own scripted
> **elite gauntlet** instead of the ambient hostiles: `CreateSpaceInstance` detects the `guardian_finale` anchor
> (strips the `space:` prefix → `IsGuardianSystemLocation`) and calls `SpawnGuardianGauntlet` — a heavy
> **Cruiser** (260 hull) flanked by 3 elite UFOs (95) + an 8-strong reinforced drone swarm (70), ringed beyond
> engage range so the approach is opt-in. Gated on combat-enabled && !defeated; each kill still feeds the story.
> **566 total green** (+1: gauntlet is an elite wave with a cruiser anchor).
>
> **🟦 P6 — Stages 3–4 client encounter UI LANDED (2026-06-14, ⚙️ Unity build-verified).** The hack + duel are
> now playable on the client, driven entirely by the server messages:
> - **NetworkClient wiring** — events `GuardianSystemRevealedReceived` (143), `CoreHackProgressReceived` (149),
>   `CoreDialogueReceived` (144) + sends `SendCoreHackTick` (146), `SendCoreDialogueChoice` (145), with dispatch.
> - **`FinaleView`** (new, IMGUI overlay, mounted by `WorldRig`; keyboard-driven so it never fights the FPS
>   cursor lock): on the Guardian Core, **hold `F`** to channel the breach (throttled `CoreHackIntent` ticks) →
>   a **hack bar** fills from `CoreHackProgress`; at 100% the **duel panel** opens — the core's claim + numbered
>   rebuttals, **press `1`/`2`/`3`** → `CoreDialogueChoiceIntent`; a correct contradiction advances, wrong ones
>   re-present the node; the win shows a resolution banner. Bilingual `ui.finale.*` keys (DE+EN).
> - **Server location gate** — `HandleCoreHack` now also requires the player to be in the Guardian system
>   (`IsGuardianSystemLocation(session.CurrentLocationId)`), so the breach only channels at the core. **567 green**
>   (+1: hack-only-at-core).
>
> **🟦 P6 — boss music wired (2026-06-14, ⚙️ Unity build-verified).** `ClientMusic` gained 5 **finale contexts**
> that override every other context and always play their dedicated boss track (even in Synth mode / combat —
> a scripted set-piece): **approach** (in the `guardian_finale*` system), **gauntlet** (in-system + in combat),
> **hack** (`FinaleView.Hacking`), **dialogue** (`FinaleView.DuelActive`), **resolution** (a ~32 s sting once
> `GuardianDefeated` flips, then normal music resumes). Phase is read from the story flags + the current
> location id + the `FinaleView` singleton; tracks cross-fade between phases and loop in place. Falls back to
> the matching synth mood if a track file is ever missing. (`music_boss_{approach,gauntlet,hack,dialogue,resolution}.mp3`.)
>
> **🟦 P6 — Stage 2 two routes + inner-core chamber COMPLETE (2026-06-14, server + data).** The finale body now
> has a real **inner core** to reach, two ways (`StampGuardianCoreChamber`, modelled on the vault stamper):
> - a buried **11×11 iron chamber** ~24 blocks down with a glowing **red core column** at its centre (the
>   terminal you breach);
> - **Route A (aperture):** a pre-carved 3×3 open **shaft** from the surface down into the chamber, ringed by
>   plating so the maw is visible — drop/descend straight in;
> - **Route B (dig):** mine down through the shell anywhere to reach it (mining is free; no bedrock);
> - both converge on the chamber. The **breach hack is now proximity-gated** (`IsAtCoreChamber`, within 7 of the
>   terminal — `_worlds.Active.CoreChamberCenter`), so you must actually get to the core; the surface Guardian
>   machines provide the "fight your way down". A **`guardian_core` POI** marks the aperture on the planet map.
> - **Per the design rule, the finale body carries NO procedural structures** — `LoadWorld` skips settlements/
>   wrecks/vaults/data-cubes/net-fragments on `guardian_finale-core`; the Guardian system itself is hand-built
>   (only the core body — no random stars/stations). **569 green** (+2: chamber-stamped-no-random-structures,
>   breach-gated-on-reaching-the-core).
> - **The finale area is RESERVED from the random generator (guaranteed).** The procedural generator only emits
>   `sys{i}` systems / `sys{i}-…` bodies; the `guardian_finale` / `guardian_finale-core` ids are reserved and
>   added only on story reveal — so random world/station generation can never "accidentally spawn the finale
>   area". Proven by `UniverseTests.Procedural_generation_never_collides_with_the_reserved_finale_area` (7 seeds
>   × 150-system galaxies). Hyperjump + body lookup are by id and the client never renders systems by MapX/MapY,
>   so the nominal map position can't clash either. **570 green** (+1).
> - **Note (interpretation):** Route A ships as a *pre-carved descent shaft* on the surface body (unified with
>   the dig world), not a separate station-boarded interior — both routes share one world + one chamber, which
>   is simpler and keeps "two ways to the core, player's choice" intact. A true boardable interior remains a
>   possible future upgrade.
>
> **🟦 P6 — core visuals built (2026-06-14, server block-art).** The chamber now reads as a dormant Guardian
> core, not plain blocks: a **glowing-red core heart** (light_red column) on a **metal-panel pedestal**, framed
> by four metal pillars + **glass windows**, a **plated steel floor**, and **red glow strips** set into the wall
> midpoints. The aperture shaft is **offset toward the +Z wall** so dropping in lands on open floor (not on the
> core), then you walk in to the heart. Existing blocks only (`iron_wall`/`steel_floor`/`metal_panel`/`glass`/
> `light_red`); 570 green; build-verified.
>
> **🟦 P6 — gauntlet HUD built (2026-06-14, ⚙️ Unity build-verified).** `FinaleView` now shows a top-screen
> **"Guardian gauntlet — N elite machines"** readout while engaging the elite wave (in the `guardian_finale*`
> flight space with hostiles present — counts Drone/Ufo/Cruiser from `Game.Space.Entities`). Bilingual
> `ui.finale.gauntlet` / `ui.finale.hostiles`.
>
> ⏳ **Remaining (⚙️ Unity, optional polish):** a **client-rendered boss model** for the core (the voxel
> structure already reads well); **robotic SFX** for the machines (they still reuse the organic growl).
The finale is **staged**, not just another drone fight: a hard gauntlet, a **hack** to open the core, then a
**dialogue duel** won by exposing the Guardian's contradiction — **weapons cannot destroy the core**.
- **Server (reveal):** score maxed **and** all `vega` beats seen → `RevealGuardianSystem` →
  `GuardianSystemRevealed` (143) places the system on the star map; reaching it needs a `jump_generator`.
- **Server (system):** a dedicated generator path — **only a sun + the Guardian core**, `Selectable = false`.
- **Stage 1 — drone gauntlet (space):** the hardest space combat — elite machines + UFO adds
  (`GameServerSpaceCombat`, tuned scripted waves; reuses ship combat).
- **Stage 2 — approach + two routes to the inner core (player's choice):**
  - **Route A — fly in:** enter the core's **aperture/shaft** and fight through an **interior** gauntlet to
    the innermost chamber (reuses interior/structure combat like a boarded station/ship interior).
  - **Route B — land + dig:** **land on the core's surface** (a special landable body), fight surface
    machines, then **mine downward** through its shell to reach the core (reuses the existing land + mining
    loop on a special structure).
  Both routes converge on the inner core. Build the core as a special body that is **both** boardable
  (interior) **and** landable + diggable, so either route works (reuses station-boarding + landing/mining
  rather than a new system).
- **Stage 3 — hack the core:** at the inner core, a timed **channel-and-defend** action opens its command
  interface (`CoreHackIntent`/`CoreHackProgress`, 146). *(Hack form is a decision — see below.)*
- **Stage 4 — argument duel:** the core is shut down **through dialogue, not damage** (`CoreDialogueMessage`
  144/`CoreDialogueChoiceIntent` 145). It states its logic; the player wins with **contradictions** (built to
  preserve *life*; humans are life; VEGA — its steward half — already judged the cull wrong; the players are
  life VEGA *made*; canon C5/C7/C10/C14). Branching duel from the pack's `core_argument` entries → **the core
  powers down** (pacification).
- **Server (death = no boss-system trap):** if a player **dies in the Guardian system**, they respawn **in
  the previous system, on the last world they were on** (the body they launched from into the finale) —
  **not** back in the Guardian system. Record each player's pre-jump return location on entering the Guardian
  system and override the normal heal-tank respawn for deaths there (the clone is re-grown at the ship's
  heal-tank on that prior world). Prevents a death-loop in the boss arena; the finale must be re-approached.
- **Server (pacification, D12):** on **winning the duel** set `guardianDefeated` (per save, persisted) → gate
  the pack's planet + space enemy spawns **off** + despawn live machines + final beat + capstone
  blueprint/`netnode` + map/travel bonus; flag the save **net online**. Game continues.
- **Assets:** **textures** (OpenAI) for the core + elite machines + the system; **SFX + the core's voice**
  (ElevenLabs) via `tools/ai-assets` (keys in `.env`, run with uv); and **dedicated boss music via Suno.ai**
  — staged per finale phase, **user-generated from Appendix A prompts**. ✅ **The 5 boss tracks are done +
  in place** (`client/Assets/Resources/music/music_boss_{approach,gauntlet,hack,dialogue,resolution}.mp3`,
  see [MUSIC_TRACKS.md](MUSIC_TRACKS.md)); P6 wires `ClientMusic` finale contexts to them. Dialogue text
  stays bilingual; voice is optional flavour over it.
- **Client:** the encounter (gauntlet HUD, hack bar, dialogue/argument panel), boss visuals + audio, finale
  beat, "net online" map state.
- **Tests:** reveal gating; **both approach routes** (fly-in interior, land + dig) reach the inner core;
  **death in the Guardian system respawns at the prior world** (no boss-system trap); **duel win** (not a
  kill) flips the flag; wrong arguments don't win; planet + space enemies cease; idempotent; persists;
  per-save (clears the crew).
- **DoD:** a reachable, winnable, **dialogue-resolved** finale that pacifies the galaxy. **Needs Unity build**
  + generated assets.
- **Decision:** hack as a **channel-and-defend** action (recommended) vs. a dedicated hack mini-game.

### P7 — Flavour pool + mission threading (content scale) — **M** (mostly data)
- Tag-filtered `flavour_line`s drive NPC greeting/idle lines in
  [GameServerNpcs.cs](../src/BlocksBeyondTheStars.GameServer/GameServerNpcs.cs), filtered by world tags + the
  world's **knowledge level** (`know_none…know_core`); the LLM backend may vary **non-canonical** banter
  only. `mission_thread`s wrap existing random missions + an optional fragment reward.
- **Tests:** tag eligibility; knowledge-level gating; locale parity. **DoD:** settlements/machine-logs feel
  alive and react to progress; scales by data.

### P8 — World options (story selection) + balancing + telemetry + polish — **M** · ⚙️ Unity
- **World options:** a **story-selection** control (pick the active pack, or "None") + a **Story density**
  slider, in world creation (`GameRules` + the world-creation UI), admin live-editable.
- **Balancing:** tune `Wf/Wk/Wm`, kill cap, thresholds, fragment weights, boss phases, coupling radius,
  memory-drop chance — as data where possible. **Telemetry:** extend `/bump` with story-state; an admin
  cheat to set/advance story progress for QA.
- **DoD:** tunable without code; the active story is selectable; QA can jump to any beat/finale. **Needs
  Unity build.**

### W-A — Content authoring (cross-phase workstream)
Author + translate the **full corpus** of the active pack from [LORE_STRUCTURE.md](LORE_STRUCTURE.md): the
B0–B12 beats, net fragments per category, player memories, settler flavour, the `core_argument` duel — all
DE+EN. **The lore in LORE_STRUCTURE must be fully written out before it can be integrated**; today it holds
the canon, the schema and starter texts only. This workstream runs alongside P1–P7 and gates a pack being
"story-complete."

---

## 5. Cross-cutting checklist (every phase)
- **NetCodec:** `Register()` every new message.
- **Locale parity:** DE+EN for every string; the parity test stays green.
- **Client builds:** after any client phase run `sync-client-libs` + `scripts/build-client.ps1`; verify the
  **`BlocksBeyondTheStars.Client.dll` timestamp** (not the `.exe`). New client plugin DLL → add to the
  client asmdef `precompiledReferences`.
- **Persistence:** additive only (new table + `PlayerState.Milestones` reuse) — no migration.
- **Docs:** update [../TODO.md](../TODO.md) per phase; keep [LORE_STRUCTURE.md](LORE_STRUCTURE.md) and
  [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md) §13 in sync when content/canon changes.

## 6. Sequencing & milestones
- **M-A — Engine + pack format:** P0 + P1 (server/data only, unit-tested). Generic engine proven with a stub
  second pack. Do first.
- **M-B — Playable MVP:** P2 + P3 (find → beat → meter → **Story Log**). + P4 retheme optional.
- **M-C — Threat + memories:** P4 + P5.
- **M-D — Finale:** P6.
- **M-E — Scale & options:** P7 + P8.
- **W-A — Authoring:** continuous, gated by LORE being fully written out.

Critical path: **P0 → P1 → P2 → P3**. P4/P5 depend on P0 (kill counter, coupling feed the score). P6 depends
on P0/P3. P7/P8 depend on P1 (content) + P0 (state). Multi-story (D2–D4) is built into P0/P1 and surfaced in
P8 — not a bolt-on.

## 7. Risk register
| Risk | Phase | Mitigation |
|---|---|---|
| Linear arc vs. random find-order | P0 | threshold score (D9); combat is a second driver |
| Soft-lock (RNG starves a category) | P2 | pity/top-up guarantee; combat path also advances |
| Combat grind trivialises pacing | P4 | diminishing returns / per-tier kill cap |
| Engine accidentally VEGA-specific | P0/P1 | prove genericity with a stub second pack before P2 |
| Localization volume (DE+EN) | P1/P7/W-A | small hand-written canon + large tag-filtered flavour/memory pools; LLM for non-canon only |
| MP spoilers / one player ends it for all | P3/P6 | per-player seen beats + per-player memories; pacification is a deliberate shared per-save reward |
| Forgetting NetCodec.Register | all | checklist + a registration test |
| Client/server lib drift | all ⚙️ | `sync-client-libs` + Client.dll timestamp check |
| Boss/finale + dialogue tuning | P6 | data-driven phases; telegraph the contradiction in earlier beats; wrong picks cost time, not a fail-out; P8 balancing |
| New planet/boss models + audio effort | P4/P6 | reuse the UFO model + OpenAI/ElevenLabs/Suno pipelines |
| Corpus not authored in time | W-A | engine ships first; a pack is gated "story-complete" only when authored |

## 8. Traceability (decision → phase)
| Decision | Phase |
|---|---|
| Pluggable storylines + in-game story selection | P0, P1, P8 (D2–D4) |
| SPS / amnesiac VEGA / clone twist / beats | P0, P1, P3, W-A |
| Net fragments text-only, separate from dataqubes | P2 |
| **New Story Log tab** (net fragments + beat history + memories; separate from the existing Codex/Wiki) | P3 |
| Combat advances story (capped) + player memories | P4 (D10/D11) |
| Three machine types: UFO + three-eyed robots + scan-drones | P4 |
| Count-neutral machine/wreck coupling | P5 |
| Finale: Guardian system → gauntlet → hack → argument duel | P6 |
| Post-finale: all machines gone, fauna stays, net online | P6 |
| Boss textures + SFX/voice + Suno music | P6 (Appendix A) |
| Multiplayer = clones of different imprints (shared per-save story) | P0, P3, P6 |
| Story density / selection world options | P8 |

## 9. Open numeric tuning (resolve during MVP/P8, not blocking)
- Score weights `Wf / Wk / Wm`; `killCap`; per-beat thresholds; memory-drop chance.
- Per-system fragment budget; category weights; pity interval.
- Coupling: wreck range, aggression multipliers, radius.
- Finale: gauntlet wave count/HP; **route A** interior length/enemy count vs. **route B** surface enemies +
  dig depth; hack channel duration; number of argument nodes + how many correct contradictions win the duel;
  scan-drone frequency + hover height; ground-robot vs. drone split.

---

## Appendix A — Boss/finale music (Suno.ai prompts)

The finale needs **dedicated soundtracks**, one per staged phase, generated by the user in **Suno.ai** and
folded in via the existing Suno-music pipeline ([MUSIC_TRACKS.md](MUSIC_TRACKS.md)). All **instrumental, no
vocals, loopable**. Copy a prompt into Suno's *Style of Music* field (with lyrics left empty / "Instrumental").

1. **Approach — "The Silent Sun"** (arriving at the Guardian system; ominous calm before):
   > Ominous ambient sci-fi approach, vast empty space, low sustained drones, a single distant sun, sparse lonely synth, slow, foreboding stillness, cinematic, instrumental, no vocals

2. **Stage 1 — "Guardian's Gauntlet"** (the hard drone combat):
   > Epic dark sci-fi battle, hybrid orchestral and industrial electronic, driving 140 BPM percussion, aggressive staccato strings, distorted synth bass, pulsing arpeggios, heroic brass stabs, relentless intense space dogfight, instrumental, loopable, no vocals

3. **Stage 2 — "Breach the Core"** (hack-and-defend, ticking tension):
   > Tense sci-fi countdown, minimal pulsing synth ostinato, ticking-clock percussion, glitchy digital textures, low dread drone, rising suspense, urgent but restrained, 120 BPM, instrumental, loopable, no vocals

4. **Stage 3 — "The Contradiction"** (the dialogue duel with the AI core; cerebral, escalating):
   > Eerie cerebral sci-fi confrontation, cold synth pads, fractured AI-voice textures, dissonant strings, sparse piano motif, slow menacing build to an emotional climax, philosophical and unsettling, instrumental, no vocals

5. **Resolution — "A Galaxy Reconnected"** (the core collapses; hopeful catharsis / pacification):
   > Hopeful cinematic sci-fi resolution, warm orchestral strings swelling, gentle piano, shimmering synth pads, uplifting and emotional, sense of relief and rebirth, slow majestic build, instrumental, no vocals
