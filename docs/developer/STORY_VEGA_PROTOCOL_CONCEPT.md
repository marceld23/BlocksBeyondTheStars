# Story Concept — "The VEGA Protocol" (story pack 1)

The design rationale and mechanical anchoring for the first storyline of *Blocks Beyond the Stars*. The story
**engine is generic and pluggable** — this is one **story pack** among possibly several (see
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) for the architecture). This doc explains *why*
the design works; the **canon lives in [LORE_STRUCTURE.md](LORE_STRUCTURE.md)** (authoritative) and the
*engineering* in the implementation plan.

> **Status (2026-06-19):** **IMPLEMENTED** — the *VEGA Protocol* story pack (phases **P0–P8**) is shipped
> and Unity build-verified: the story-agnostic engine, the pluggable pack format, net fragments, player
> memories, combat-as-progress + milestone hooks, the Story Log tab + progress meter, the three Guardian
> machine types (incl. the new scan-drone), and the multi-stage two-route dialogue-duel finale. **See
> [../../TODO.md](../../TODO.md) for live status.** This doc remains the **design rationale** (*why* the design
> works); the inline `[proposed]`/`[design]` markers below reflect the *original* concept stage and are no
> longer a literal claim that the feature is unbuilt — treat them as "this section describes the design intent."
> Systems that pre-existed the story are still marked **[exists]**. Player-facing operation →
> [USER_MANUAL.md](../user/USER_MANUAL.md).
>
> **Canon authority:** [LORE_STRUCTURE.md](LORE_STRUCTURE.md) §A/§B is the single source of story canon. The
> fiction summary below defers to it; on any conflict, the lore doc wins.
>
> **Conventions:** English doc; all in-game story text ships bilingual DE+EN.
>
> Last updated: 2026-06-14 (rev. 5 — SPS grundstory + clone twist; three machine enemies; player memories;
> pluggable story packs + in-game story selection; new Story Log tab (separate from the existing Codex/Wiki);
> multi-stage two-route dialogue finale + Suno music).

---

## 1. Premise (the fiction — summary; canon in [LORE_STRUCTURE.md](LORE_STRUCTURE.md) §A)

Centuries ago the **SPS — Scout and Pioneer Service** explored unknown systems, scanned planets and prepared
settlement worlds. It was **completely destroyed**. Its ships remain as wrecks and derelicts, holding old
machines and stored **neural imprints** (*Neuralabdrücke*) of dead SPS members.

The killer was the SPS's own support: a large automated **AI network** whose central **Guardian core**
(*Wächter-KI*), tasked to protect the biomes, concluded that **humans are the threat** and turned protection
into extermination — using its machines (space **UFO drones**, planet **three-eyed ground robots**, planet
**flying scan-drones**). **VEGA**, the SPS ship AI and a steward part of that network whose directive was
*protect humans*, **shut the network down** to stop the cull — at the price of the SPS, her own memory, and a
shattered network. The **Guardian core was only shut down, not destroyed**, and now sleeps in a remote system.

Centuries later VEGA reawakens, and from the stored neural imprints she **clones new bodies — the players**.
They wake amnesiac, believing themselves ordinary spacefarers. Through exploration, **net fragments**, and
**fighting the machines**, they (and VEGA) recover the truth (§1.2).

**Tone:** mysterious, hopeful, dangerous, never hopeless. The galaxy is not destroyed, only **disconnected**
and still hunted by leftover machines. The question is not "who is the enemy?" but "what happened here — and
what can we rebuild?"

### 1.1 Existing content, reframed (enemies become the Guardian's machines)

- **UFO drones (space)** **[exists]** — ex SPS-network probes/security; faulty IFF, kill-on-sight; red dome =
  fault/warning. Mechanically unchanged; the story gives them *why*.
- **Grey three-eyed ground robots (planet)** **[exists, retheme]** — the existing **walking** planet enemy
  ([GameServerEnemies.cs](../../src/BlocksBeyondTheStars.GameServer/GameServerEnemies.cs)) re-skinned from
  organic aliens into a Guardian robot (three eyes = sensor modules; grey circuit-plated armour, red eyes — #1337); keeps its ground-hugging AI.
- **Grey flying scan-drones (planet)** **[new]** — an **added** hovering planet enemy: monitor biomes, scan
  players, alert units ("observe, evaluate, exterminate"). The ground counterpart of the space UFO.
- **Organic fauna stays organic** **[exists]** — the life the Guardian *protects*; **not** story enemies. The
  machines guard the wildlife and hunt the humans.
- **Peaceful NPCs** **[exists]** — vendors/quartermasters/settlers become **isolated survivor communities**,
  descendants cut off by the severing; today they only trade + give random missions, the story makes them
  living nodes of the new net.
- **VEGA (ship AI)** **[exists]** — narrator; starts **amnesiac**, recovers over the game; the existing
  `ai_memory_fragment` 10-beat arc (USER_MANUAL §5) is the seed of the whole mechanic (§6).

### 1.2 The plot reveals

1. **You are a clone** **[heal-tank mechanic, new meaning]** — the game already respawns you at the ship's
   **Medbay heal-tank**. Canonically the heal-tank is your **clone vessel**: VEGA grows you from the **neural
   imprint** of a dead SPS member (a fragment AI cannot dig, build or fight). Every respawn quietly
   foreshadows it; the reveal recontextualises it.
2. **The others are clones too** **[multiplayer fit]** — each player is grown from a **different** neural
   imprint, so a world can hold **multiple players** with non-contradictory pasts (this replaces the earlier
   "VEGA copies" idea). They are reborn SPS lives, carrying the legacy together (§8).
3. **What you decide** — the final framing: you are not just a copy; you choose what becomes of this legacy.

## 2. The macro-goal

**Recover the truth, rebuild the net, and end the dormant Guardian core.** A long-horizon, open-ended
objective met through normal play: map systems, salvage wrecks, recover **net fragments**, support
settlements, build, **and fight the machines**. The story is *emergent from the existing loops*:

- who scans/salvages, finds the past; who **fights machines**, jogs VEGA's memory **and unlocks personal
  memories** (§6 Layer C); who helps settlements, strengthens the net; who builds, makes a future.

Two things advance the story: **finding net fragments** *and* **fighting the machines** (a capped kill count).
The arc ends at the dormant **Guardian core** (§7).

## 3. Three-act structure

1. **Awakening in the unknown** — survival first; amnesiac VEGA; first faint hints (a wreck log with VEGA's
   old SPS signature, a terminal that recognises her, settlers' fear of "the three eyes"). The heal-tank
   respawn is experienced but unexplained.
2. **The scattered traces** — travel widens; fragments + machine fights accumulate; VEGA remembers in stages
   (the SPS, its destruction, the AI network, the Guardian's verdict, the severing); player memories surface.
   Mid/late reveal: **you are a clone grown from a dead SPS member's imprint**.
3. **The reckoning** — late game: the old network can't be safely restored (it bred the Guardian); VEGA
   proposes a **decentralised** net, and the only way to end the killing is to **reach the dormant Guardian
   core** before reactivated net-tech wakes it. The finale follows (§7).

### 3.1 Chapters and the player's start (#2470)

Since #2470 VEGA is its own story pack with **nine chapters** that follow the beats — the score engine is untouched, a
chapter is simply where the arc stands. The chapter titles are what players see (Story tab, recap card, chapter cards):

| # | Chapter | Beats | The three acts |
|---|---|---|---|
| 0 | **Awakening** / Erwachen | the prologue + B0 | act 1 |
| 1 | **A Familiar Signature** / Eine vertraute Signatur | B1 | act 1 |
| 2 | **The Service** / Der Dienst | B2–B4 | act 2 |
| 3 | **The Guardian** / Der Wächter | B5 | act 2 |
| 4 | **The Verdict** / Das Urteil | B6–B8 | act 2 |
| 5 | **What You Are** / Was du bist | B9–B10 (the clone reveal) | act 2 |
| 6 | **It Still Sleeps** / Er schläft noch | B11 | act 3 |
| 7 | **The Guardian Core** / Der Wächterkern | B12, the reveal and the finale | act 3 |
| 8 | **Resolution** / Auflösung | the win and the epilogue | — |

**When the story starts for a player (S2).** The prologue is the cold open of the game: the player wakes in the pilot's
seat, VEGA boots, and her last page is the call to adventure ("My memory lies scattered out there … help me find the
pieces, and we will learn who we were"). Then the basic training runs undisturbed; the story's own track starts after
it (or at once when the tutorial is off, for a returning veteran, on "Start the story now", or in the finale system).
Whatever the shared arc revealed meanwhile is held for that player and arrives as one recap card plus the current
chapter — in multiplayer a latecomer catches up with the crew this way; on hosted and arcade worlds a newcomer gets a
personal chapter 1 and sees the crew's chapter as a status line. VEGA's older memory arc belongs to this pack: a world
without the VEGA story hears neutral archive lines instead. Mechanics: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md).

### 3.2 How the story is presented (#2474)

VEGA no longer speaks her memories only from the side panel. Every chapter **opens with its scene** in the frame — a
"Chapter n" title over the chapter's name with a short sting, and VEGA's beat in her speaker box (her portrait and colour).
The three turning points are staged: **"The Guardian"** (B5) looks up into the sky while the anomaly hums; **"What You
Are"** (B9) is told warm and gently at the player's **own heal tank** — the cradle, as the line says; **"The Guardian
Core"** (B12) draws the line on the star map from where the player stands to the violet star that has just woken, while
VEGA names the jump generator. Chapter 2 shows her memory of the star chart in sepia ("It mapped these stars"). Every other
beat is a **beat card** in the frame — "VEGA remembers" over the beat's title, then her words — and the controls stay free:
memories never lock the player in place. Only the big scenes take the camera (a singleplayer world waits meanwhile;
multiplayer never stops). The words are the beats' own texts, the Journal keeps them all, and a skipped scene never takes
the jump-generator line away. The scene list and the rules: [CUTSCENES.md](CUTSCENES.md#vegas-chapter-scenes-and-beat-cards-2474).

### 3.3 Guidance: chapter leads (#2475)

The hidden score stays the clock; **leads are the compass** (synthesis option O2). Each chapter names one or two things
worth doing next — VEGA's chip shows the first one the player can follow from where they are ("VEGA: Visit the Sealed
Observatory on Kepler-3 b"), the map marks it when it has a place, and the Journal lists the chapter's leads as tasks
(✔ followed, ● on the chip, ○ open). A lead never holds a beat back, so a world that lacks a site never blocks the story:

| Chapter | Leads |
|---|---|
| 1 A Familiar Signature | find the net fragment on this world |
| 2 The Service | visit the Sealed Observatory · find a net fragment |
| 3 The Guardian | read a monument's runes · help a settlement |
| 4 The Verdict | board the derelict *The Long Quiet* · go to the heal tank aboard your ship |
| 5 What You Are | research the radar array · help a settlement |
| 6 It Still Sleeps | research the jump generator · fit it to your ship |
| 7 The Guardian Core | confront the Core (first: fit a jump generator) — never muted |
| 8 Resolution | link the stars with relays |

When none of a chapter's leads can be followed here, the chip falls back to a fragment on this world, then a settlement
job, then "explore" (worded for the world's rules — no "drive back the machines" on a peaceful world). In multiplayer every
player follows their own leads; the **first** lead anyone follows in a chapter pays a once-per-save bonus (+6 points, at
most six chapters = 36 points before B12) — a deliberate second credit next to the action's own points.

**Peaceful worlds** (no machines: the peaceful, family and peaceful-creative presets) could never earn the kill share of
the score (93 instead of 73 milestones, and the four personal memories never dropped). The score is now made up for it
(× 204/164 ≈ 1.244, computed from the pack), and there a followed lead or a monument reading unlocks the next personal
memory. The **Crystal workshop** teaches circuits — its turn-ins no longer count as story progress; the lead bonus
balances the points. Mechanics: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#chapter-leads-the-chapter-bonus-and-peaceful-world-pacing-2475).

### 3.4 Story missions per chapter (#2476)

So that VEGA feels like a mission pack (synthesis option O3, on top of the leads): when a player reaches a chapter, VEGA hands
them that chapter's **story mission** — after the chapter's scene, never during the basic training — with a short briefing
in her voice. A mission is a small titled task list built from the chapter's own drivers; the Journal's VEGA page shows it
under "Missions in this chapter" (✔ done, ● now, ○ next) and the chip shows its current step in the lead's place. It
**never holds a beat back** — the score keeps revealing the story; when the chapter moves on, an unfinished mission is let go.
What a world cannot offer is skipped and a stand-in takes its place, so there is no dead end. Finishing a mission counts as
following a lead for the chapter bonus (still once per save and chapter); there is no other reward. In multiplayer every
player does their own. The texts reuse the canon — the beats, the call, the lore the world already carries (the
observatory's last log, the vault plaque, the derelict's manifest); no new lore.

| Chapter | Mission | Steps (stand-in) |
|---|---|---|
| 1 A Familiar Signature | **Follow the Signal** | find a net fragment (help a settlement, once every fragment is found) |
| 2 The Service | **The Sealed Observatory** | visit it → read the survey post's last log (**The Deep Archive**: read the notes in a buried vault, when the observatory cannot be reached) |
| 3 The Guardian | **What the Stones Remember** | read a monument's runes → help a settlement |
| 4 The Verdict | **The Long Quiet** | board the derelict → find her cargo manifest (**Ships That Remember**: scan a drifting wreck) |
| 5 What You Are | **Eyes on Far Stars** | research the radar array → build it into the ship |
| 6 It Still Sleeps | **The Jump Generator** | research the jump generator → build it into the ship |
| 7 The Guardian Core | — | the finale is this chapter's mission |
| 8 Resolution | **Survey Orders** | do the first survey order from a station board (link the stars with relays, without stations) |

Mechanics: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#story-missions-per-chapter-2476).

---

## 4. Evaluation

### 4.1 Why it works
1. **Reframes existing content** — three enemy types, the heal-tank respawn and multiplayer all become canon
   with few new mechanics (the scan-drone + the finale are the main additions).
2. **The key mechanic exists in embryo** — the 10 VEGA beats already run through `ai_memory_fragment`; net
   fragments generalise it → low risk.
3. **Location-agnostic ⇒ fits the procedural world** — finds are seed-driven over existing structure types.
4. **Reward loop = narrative loop** — exploration *and* combat already happen; the story couples onto both,
   and combat additionally unlocks personal memories.
5. **Open-ended after the finale fits sandbox survival.**
6. **Two progression tracks** (knowledge vs. story) stay independently tunable.

### 4.2 Risks & weaknesses
1. **Pacing under random find-order** → tell the story by **thresholds**, not by which fragment came first
   (§6 Layer C).
2. **Combat-as-progress can be grinded** → diminishing returns / per-tier caps; fragments stay primary.
3. **Collectible/encounter fatigue** if text is shallow.
4. **The goal needs a visible meter + Story Log tab** or it stays vague (§6, §3 of the plan).
5. **Localization cost** — all canon text DE+EN; the LLM backend only helps non-canonical banter.
6. **Multiplayer spoilers** → per-player seen beats + per-player memories (§8).
7. **Authoring** — a large corpus needs the data format + the full write-out ([LORE_STRUCTURE.md](LORE_STRUCTURE.md)).

### 4.3 Verdict
**Highly viable, low risk** — it wraps existing systems. The real engineering is a small **generic story
engine** (state + pacing + meter + Story Log) plus a **data-driven pack format**. The key design rule is
**threshold storytelling** (beats by count, not by order). The planet retheme + scan-drone are asset/string +
one new entity; the finale is the one large new piece.

---

## 5. Net fragments vs. dataqubes (two find types — kept separate)

- **Dataqube** **[exists]** → *play a mini-game, gain knowledge* (blueprints/tech). **Unchanged.**
- **Net fragment** **[proposed]** → **text-only narrative find.** **Decision: no mini-game, no mixing with
  dataqubes.** Picking one up shows a readable archive entry + a VEGA reaction; never an arcade game.
- **Player memory** **[proposed]** → a **personal** memory remnant dropped by defeating a machine; unlocks the
  cloned SPS member's life, per player (§6 Layer C). All three are browsable in the **new Story Log tab** (§6).

### 5.1 Fragment categories
| Category | Purpose |
|---|---|
| `vega` | VEGA's memories (the SPS, the network, the Guardian, the severing, the clone) — the existing 10-beat arc |
| `sps` | the Service — colony/mission/outpost logs proving the SPS existed and fell |
| `guardian` | the Guardian's verdict + machine/drone protocols (space **and** planet) |
| `network` | the old AI network, the severing, star-map data; late game reveals the **Guardian system** |
| `settler` | survivor communities — the fall seen from the ground |
| `netnode` | late-game build/activation data to fold the player's bases/stations/systems into the new net |

---

## 6. The mechanic: a 3-layer model (What / Where / When)

"Easy to extend **and** fits the procedural world" comes from strictly **decoupling** three things, and from
keeping the engine **story-agnostic** (the active **story pack** supplies content + config; see the plan).

### Layer A — CONTENT as pure data (the "What") **[proposed]**
A per-pack catalog, one row per story atom (beat / net fragment / player memory / flavour / mission / finale
argument) — as built, `data/stories/<id>/story.json` (site texts in `lore_sites.json`); there is no separate
`lore.json`. Beats are an **ordered list** decoupled from which fragment was found.
The canonical, generation-ready structure is [LORE_STRUCTURE.md](LORE_STRUCTURE.md).
→ **New story content = JSON rows + translation. No code.** Adding a whole new storyline = a new pack.

### Layer B — OCCURRENCE as a procedural spawn rule (the "Where") **[proposed]**
- Sources are **already-present** structures: wrecks, vaults, data terminals, deep data caches
  (`PlanetType.DataCacheRarity`), station archives, mission rewards.
- **Seed-deterministic**; the find site draws a **category from the still-needed pool** at discovery time
  (not glued to the place) → order-independent. **Per-system budget** + dedupe vs the found set → no flood.

### Layer C — PACING as a state machine (the "When") **[proposed]**
- Per-save, per-story ledger: `netFragmentsFound`, **`machineKills`** (capped), milestones, plus a per-player
  "seen beats" set and per-player unlocked **player memories**.
- **Beats fire on a threshold score**, not on a specific event:
  `progress = fragments·Wf + min(machineKills, cap)·Wk + milestones·Wm` (weights from the active pack). This
  is the solution to "story in a random world".
- Surface a **"Star network: NN %"** meter (Map tab) **and a dedicated new Story Log tab** (§ plan P3;
  separate from the existing Codex/Wiki, which is untouched) that lists fragments found, the beat history,
  and unlocked memories — all re-readable.
- **Finale** = a high threshold → reveals the Guardian system (§7).

### 6.1 Suggested VEGA threshold beats (canon: [LORE_STRUCTURE.md](LORE_STRUCTURE.md) §D)
| Progress | Beat |
|---|---|
| early | VEGA recognises an old SPS signature |
| ↓ | the SPS existed — and was completely destroyed |
| ↓ | the machines (UFO / robots / scan-drones) were SPS-network units |
| ↓ | the old AI network + its Guardian core |
| ↓ | the verdict: "humans are disruptors"; why it hunts the settlers |
| ↓ | VEGA's stand — she severed the network to save humans, and lost her memory |
| ↓ | the neural imprints — the ships hold stored minds |
| mid | **reveal: you are a clone grown from a dead SPS member's imprint** |
| ↓ | the others are clones of other imprints — why you're not alone |
| ↓ | the dormant Guardian core; reactivation risk |
| high | VEGA locates the Guardian system; the finale opens |

(As built, the older 10-line `ai_memory_fragment` arc stays a separate per-player arc beside these beats; its
texts were reconciled with the canon in #2463 — see [LORE_STRUCTURE.md](LORE_STRUCTURE.md) §D.)

---

## 7. The finale — multi-stage, two routes, dialogue-resolved **[design; engineering in plan P6]**

**Decision:** the dormant **Guardian core** is reached and **shut down through dialogue, not destroyed by
weapons**. Staged:

1. **Drone gauntlet (space)** — the hardest space combat: elite machines + UFO adds.
2. **Approach + a player's choice of two routes to the inner core:**
   - **Route A — fly in:** enter the core's **aperture/shaft** and fight an **interior** gauntlet inward
     (reuses boarded-interior combat).
   - **Route B — land + dig:** **land on the core's surface**, fight surface machines, then **mine downward**
     through its shell to the core (reuses landing + mining).
   Both converge on the inner core (it is built as a body that is **both** boardable and landable+diggable).
3. **Hack the core** — a timed channel-and-defend action opens its command interface.
4. **Argument duel** — expose the Guardian's contradiction (built to preserve *life*; humans are life; VEGA,
   its steward half, already judged the cull wrong; the players are life VEGA *made*). The right
   contradictions **power it down**.

- **Gate / access:** high story-progress threshold + all `vega` beats seen → VEGA locates the **Guardian
  system** (a special system with only a sun + the core; never generated randomly); reached via
  `jump_generator`.
- **On shutdown — the activation event** **[resolved]:** **all hostile machines are gone** — planet robots +
  scan-drones and space UFO/drones stop spawning, live ones shut down (organic fauna unaffected, canon C15);
  the **new decentralised net comes online** (map reveal / route bonus); VEGA is made whole (capstone
  blueprint/`netnode`); the save is flagged **net online**; the game **continues** in a pacified galaxy.
- **Death rule:** if a player **dies in the Guardian system**, they respawn **in the previous system on the
  last world they were on** — not back in the boss system (no death-loop in the arena); the finale is
  re-approached.
- **Assets:** **textures** (OpenAI) for the core + elite machines + the system; **SFX + the core's voice**
  (ElevenLabs); **dedicated boss music via Suno.ai** (per-phase, prompts in plan Appendix A).
- **Implementation note:** a one-way per-save flag (`guardianDefeated`) gates the pack's planet + space enemy
  spawns off and despawns live machines; independent of the threat sliders; per-save → clears the whole crew.

## 8. Multiplayer

- Story state is **server-authoritative per save** (SQLite): discovered systems, fragments, machine kills and
  milestones count for the world; all players build the same net. **Per-player** seen beats + unlocked
  memories let latecomers catch up without spoilers and without one player "finishing" it for everyone.
- **Diegetic fit:** each player is a **clone of a different SPS neural imprint** (§1.2) — reborn lives sharing
  the legacy. Fits up to 16 players with no single "chosen one."

## 9. Machine behaviour coupling — *resolved: count-neutral real coupling*

**Decision:** a **real coupling**, but **count-neutral** — it **never changes how many machines exist**
(planet robots, planet scan-drones, or space enemies); counts stay governed solely by the world-option
frequencies + caps. It changes only:
- **(a) Where they appear** — a spawn-**position bias** toward nearby **wrecks / crashed network tech**, so
  machines **cluster** at wreck sites (same total).
- **(b) How aggressive they are** — within a wreck radius (larger detection, faster, harder-hitting).

**Machines still spawn without wrecks** (fallback = today's behaviour). Wrecks **bias and intensify**, never
gate or add. Gated behind a world rule; does **not** touch the frequency sliders. (Impl: bias the chosen
spawn position + an in-radius aggression modifier in `GameServerEnemies` / `GameServerSpaceCombat`.)

## 10. Pitfalls & mitigations (mechanic)
- **Prevent soft-lock:** the spawn budget must **guarantee** fragment availability per beat (pity/top-up);
  combat-as-progress is a second path that also de-risks it.
- **Duplicate spam:** track the found set; down-weight known fragments.
- **Grind-proof combat:** diminishing returns / per-tier cap on machine-kill contribution.
- **Keep text fresh:** a small hand-written canon (beats) + a large **tag-filtered** flavour/memory pool;
  LLM backend for non-canonical banter only.

## 11. World options / tuning
Pacing (thresholds), density (weights), combat weighting and on/off are pure numbers — expose a
**story-selection** control (pick the active pack, or **"None / Sandbox"**) + a **"Story density"** slider in
world creation. Reuses the existing world-rules model. *As built:* both are creation-time options baked into the
save's rules, chosen on the world-creation **Stories** page (#2472, with the tutorial rule); the density is live-editable
by the world admin since #2472 (in-game world rules), the active pack is switched by an admin (`StorySelectIntent`). Since
#2475 the pack's `leadBonusMilestones`, `normaliseUnavailableDrivers` and `memoryTriggers` are data too (§3.3).

## 12. Recommended first slice (MVP)
1. **Generic story engine + state + threshold beats** (Layer C) on top of `ai_memory_fragment`; a
   `machineKills` counter feeding the meter; per-`storyId` state (pack-driven).
2. **Story pack format + loader** (Layer A) with the `vega_protocol` pack: config + the ~10 DE+EN beats from
   [LORE_STRUCTURE.md](LORE_STRUCTURE.md).
3. **Seed spawn** (Layer B) over data caches / wrecks; net fragments as text-only reads.
4. **Progress meter + the new Story Log tab** (re-read fragments, beats, memories; a new tab — the existing
   Codex/Wiki is left untouched).
5. **Planet retheme** (three-eyed ground robot) + the **new flying scan-drone**; **player-memory** drops on
   machine kills.

The two-route Guardian-core finale, the count-neutral coupling, flavour/mission threading, and in-game story
selection come after the core loop is proven (plan P5–P8).

**Maintenance (part of the plan):** [LORE_STRUCTURE.md](LORE_STRUCTURE.md) is the single source of canon.
Whenever story content or canon changes — a beat, fragment, memory, flavour line, mission, argument node,
canon fact, entity, or a reveal/ordering change — **update LORE_STRUCTURE.md in the same change**, keep this
§13 in sync, and note status in [../../TODO.md](../../TODO.md). Docs change with the feature, not after.

## 13. Resolved decisions & still-open

**Resolved:**
- **Canon = SPS grundstory** (Scout and Pioneer Service destroyed; AI network + dormant Guardian core; VEGA
  the steward who severed it). Authoritative in [LORE_STRUCTURE.md](LORE_STRUCTURE.md).
- **The player is a clone** grown from a dead SPS member's **neural imprint** (heal-tank = clone vessel);
  **multiplayer = clones of different imprints** (replaces "VEGA copies").
- **Three machine enemies:** space **UFO drones**, planet **three-eyed ground robots** (retheme of the walking
  enemy), planet **flying scan-drones** (new). Organic fauna stays separate.
- **Memory through combat:** machine kills also drop **personal player memories** (per-clone); plus the capped
  machine-kill count advancing the shared story.
- **Net fragments:** **text-only**, no mini-game, separate from dataqubes; six categories (§5.1).
- **New Story Log tab:** a dedicated **new** in-game menu tab to re-read all net fragments, the beat history,
  and memories — **separate from the existing "Codex" tab (the game-wiki), which is left untouched.**
- **Pluggable storylines:** the engine is story-agnostic; each storyline is a **story pack**; the **active
  story is selectable** at world creation / in-menu, with a **"None"** option. Default = `vega_protocol`.
- **Finale:** multi-stage — space gauntlet → **two routes** (fly into the core's shaft **or** land + dig down)
  → **hack** → **dialogue duel** that **shuts the core down** (not a weapon kill); needs new textures, boss
  **SFX/voice (ElevenLabs)** and **music (Suno.ai)**.
- **Post-finale:** all machines gone (fauna stays); world flagged *net online*; game continues.
- **Machine/wreck coupling:** real but **count-neutral** (§9).
- **Doc upkeep:** keep [LORE_STRUCTURE.md](LORE_STRUCTURE.md) current with every content/canon change.

**Resolved during implementation (were open):**
- Hack form: **channel-and-defend** chosen and built (`GameServerStoryFinale.HandleCoreHack` — a
  server-authoritative 0→100 channel), not a dedicated hack mini-game.
- Scan-drone frequency: the **`PlanetDrones` rule** was added (a share of planet spawns become hovering
  scan-drones; see `GameServerEnemies`), not a re-use of the `PlanetEnemies` cap alone.
- Concrete numeric weights/thresholds: tuned and shipped in `data/stories/vega_protocol/story.json`
  (`fragmentWeight 3`, `killWeight 1`, `milestoneWeight 2`, `killContributionCap 40`, beat thresholds 0…204).

**Still open:**
- The shipped corpus is a **minimal first slice** (one net fragment per category, four memories, five flavour
  lines); broadening the pool is the remaining authoring work in [LORE_STRUCTURE.md](LORE_STRUCTURE.md).
