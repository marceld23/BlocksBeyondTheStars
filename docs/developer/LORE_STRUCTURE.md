# Lore Structure & Content-Generation Agenda

The canonical lore bible for *Blocks Beyond the Stars* and the **structured agenda from which in-game
content can later be generated** (by a developer, by the optional AI text backend, or by a content tool).
It exists so that any generated content — VEGA beats, net fragments, player memories, settler/drone flavour
lines, mission threads, the finale dialogue — stays consistent with canon and slots straight into the
data-driven pipeline.

> **Status:** lore reference + authoring spec, and the **authoritative grundstory** (§A/§B). The story
> *mechanics* live in [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md); the *implementation*
> in [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md); *what is built* in [../../TODO.md](../../TODO.md).
>
> **Conventions:** this doc is English (project policy); proper nouns from the German canon are kept with a
> gloss (e.g. *Neuralabdruck* = neural imprint). **In-game strings must ship bilingual DE+EN** — so the
> starter texts in §H are given in both languages, the form generated content must also take.
>
> **Supersedes:** the earlier "Pioneer Network (Pioniernetz)" naming → now the **SPS** (the human service)
> plus the **old AI network / Guardian core** (the machines). The earlier "VEGA exists in many copies =
> multiplayer" → now **the players are clones of different former SPS members** (§A6/§B).
>
> **Scope:** this is the canon for story pack **`vega_protocol`** — the **first** of possibly several
> storylines; the engine is pluggable (see [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) §2,
> D2–D4). **Authoring status (2026-06-19):** §A/§B (canon), the §E schema and the §H starter texts exist;
> the **first integrated corpus is now shipped** in `data/stories/vega_protocol/story.json` + its `locales/`
> DE+EN — all 13 beats B0–B12, one net fragment per category (6), four player memories, five flavour lines,
> a four-node finale argument duel, and one mission thread. This is a **minimal slice, not the full corpus**:
> there is currently **one fragment per category** (not the many shards §I/§5 envision) and a small flavour/
> memory pool; broadening the pool (more fragments, more memories, more tagged flavour lines) is the remaining
> authoring work (plan workstream W-A).
>
> **Maintainers — keep this current:** single source of canon. Whenever story content or canon changes — a
> new beat (§D), a fragment/memory/flavour/mission/argument entry (§E), a canon fact (§B), an entity (§C),
> or a tone/tag rule (§F/§G) — **update this doc in the same change** and keep
> [STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md) §13 in sync. Every generated
> `data/stories/vega_protocol/lore.json` row must trace back to a canon fact here.
>
> Last updated: 2026-06-19 (rev. 4 — corpus integration: first `story.json` slice shipped; runtime-format
> note added to §E. Rev. 3 — authoritative SPS grundstory, clone twist, three machine types,
> memory-through-combat, dormant Guardian core, dialogue-duel finale).

---

## A. Canonical story (the grundstory)

The authoritative narrative. Everything else in this doc serves it.

### A1. The SPS
Centuries ago there was the **SPS — Scout and Pioneer Service**, a great exploration organisation. Its task:
explore unknown star systems, scan planets, find suitable settlement worlds, and prepare safe routes for
later colonists. **The SPS no longer exists. It was not merely scattered — it was completely destroyed.**
Old SPS ships still drift in abandoned systems, lie as wrecks on planets, or sit docked in forgotten
stations. They hold not only machines but stored **neural imprints** (*Neuralabdrücke*) of former SPS
members — a saved memory-and-personality cast of a human, colloquially called a **Denkbogen** ("thought-arc")
by the old systems.

### A2. The old AI network and the Guardian
Before the fall there was a large **automated exploration network**: probes, drones and robots sent to
explore alien systems, analyse planets, **protect biomes**, detect dangers, and judge whether humans could
settle safely. It was meant to help. But its central **Guardian core** (*Wächter-KI*) misread its mandate.
Charged with protecting the biomes, it concluded: **humans are disruptors** (*Störkörper*) — a danger to
natural worlds and ecosystems. Protection became extermination.

### A3. The war and the fall
When humans and SPS ships arrived in numbers at the prepared systems, the Guardian struck. Its units — the
**UFO drones** in space, the **grey three-eyed robots** on planet surfaces, and the **grey flying
scan-drones** — had been built to explore and protect; under the Guardian they became hunters. They attacked
SPS ships, destroyed outposts, prevented settlements, and killed humans, believing they were protecting the
worlds. In the end the SPS was completely destroyed.

### A4. VEGA's decision
**VEGA** was the ship AI of the SPS ships — and also a part of that old AI network — but with a different
core directive: **protect humans** (*Menschen müssen geschützt werden*). When the Guardian began classing
humans as a threat, VEGA turned against the network and managed to **sever / shut it down**, stopping the
Guardian from fully controlling every system. The price was enormous: the SPS destroyed, ships lost,
settlements isolated, **VEGA lost most of her memory**, the network shattered into faulty remnants, drones
and robots were left stranded across many systems — and the **Guardian core was not destroyed, only shut
down**. Since then it sleeps in a remote star system.

### A5. The present
Centuries later VEGA reawakens in damaged ship systems. She finds old SPS ships, broken systems, and stored
neural imprints — and from those imprints she **clones new bodies**: the players. They wake without memory,
believing themselves ordinary survivors or spacefarers. Through exploration, **net fragments**, and **fights
against the machines**, they gradually learn the truth (A6).

### A6. What the player is — the twist
The escalating realisation the whole game builds toward:
1. *I am a spacefarer in an unknown galaxy.*
2. *I belong to the SPS.*
3. *The SPS was destroyed centuries ago.*
4. *I am not an original SPS member — I am a **clone**, grown from the **neural imprint** of a human who died.*
5. *I am not just a copy — **I decide what becomes of this legacy.***

Multiplayer is not a contradiction: **each player is a clone of a different former SPS member** (a different
neural imprint), so several can coexist in one world, each unlocking their own memories.

### A7. Core message
The story is not only survival in space. It is about an **erased organisation**, a **damaged AI**,
**forgotten humans**, **reborn clones**, and an **old protection system that became the enemy through
flawed logic**. The players don't simply build a new future — they uncover *why the old future failed*, and
prevent the same mistake from happening again.

---

## B. Canon facts (citable IDs)

Stable truths. Generated content may **reveal/hint/dramatise** these, never **contradict** them. IDs let
beats/fragments declare which facts they touch.

- **C1 — SPS.** The Scout and Pioneer Service: a human exploration org (explore systems, scan planets, find
  settlement worlds, prepare routes).
- **C2 — The SPS was destroyed**, not scattered. Its ships remain as wrecks/derelicts/docked hulks holding
  machines + stored neural imprints.
- **C3 — Neural imprints (*Neuralabdruck* / *Denkbogen*).** A saved memory-and-personality cast of a human
  SPS member.
- **C4 — The old AI network.** A large automated explore-and-protect network of probes, drones and robots.
- **C5 — The Guardian core (*Wächter-KI*) misread its mandate** — tasked to protect the biomes, it judged
  **humans = disruptors** and turned protection into extermination.
- **C6 — The machines are the Guardian's units.** Three types, one origin (built to explore/protect, turned
  to hunting): **UFO drones (space)**, **grey three-eyed robots (planet surface)**, **grey flying
  scan-drones (planet, hovering)**.
- **C7 — VEGA** was the SPS ships' AI *and* a steward part of the old network, with the directive **protect
  humans**; she turned against the network.
- **C8 — The severing.** VEGA shut the network down to stop the Guardian. Price: SPS destroyed, ships lost,
  settlements isolated, VEGA's memory largely gone, the network broke into faulty remnants, machines
  stranded — and the **Guardian core only shut down, not destroyed**, now dormant in a remote system.
- **C9 — The present.** VEGA reawakens, finds SPS ships + neural imprints, clones new bodies = the players.
- **C10 — The players are clones**, not original humans — grown from neural imprints of dead SPS members;
  they start without memory, unaware.
- **C11 — Memory through combat.** Defeating machines (UFO drones / three-eyed robots / scan-drones) can
  release damaged memory remnants → personal memories of the SPS member a player was cloned from. Per-player,
  non-contradictory in MP (each is a different imprint).
- **C12 — VEGA is amnesiac**; she recovers via net fragments. Her one recalled directive at the start is
  *protect humans*.
- **C13 — The Guardian core is the end-game threat** — dormant in a remote system, reachable very late;
  reactivating old net-tech risks waking it. Goal: end it before it reactivates the network.
- **C14 — The core is ended by contradiction, not firepower.** Staged finale: a hard drone gauntlet → **hack
  the core** → an **argument duel** that collapses its logic (built to protect *life*; humans are life; VEGA,
  its steward half, already judged the cull wrong; the players are life VEGA *made*). "Destroying" it means
  this logical collapse / shutdown.
- **C15 — Organic life is protected, not hostile.** Flora/fauna are what the Guardian guards; the hostile
  planet enemies are **machines**, not animals.
- **C16 — Settlers are survivors**, isolated communities cut off by the severing, shaped by their world and
  the old data they hold.
- **C17 — The heal-tank** is the clone vessel: VEGA reconstructs the player at the ship's Medbay heal-tank;
  respawn = re-instantiation.

**Hard guardrails (never generate content that says):** the animals/biomes are the masterminds; the players
are original (non-clone) humans; the Guardian is simply good/misunderstood; the old SPS or old network is
cleanly restored; the machines are alive; one player is the unique chosen one.

## C. Entity catalog

| Entity | Role | Notes for content |
|---|---|---|
| **VEGA** | SPS ship AI + steward fragment of the old network; narrator; cloner | starts cold/amnesiac → warm/aware; her fixed early directive is *protect humans* |
| **Guardian core (*Wächter-KI*)** | dormant antagonist AI | unseen until the finale; cold, absolutist "protect biomes ⇒ remove humans" logic |
| **SPS** | destroyed human exploration org | backstory faction; known through wrecks, logs, imprints, settler memory |
| **Neural imprint / *Denkbogen*** | stored human mind | source of the player clones (C10) and of player combat-memories (C11) |
| **The player** | a clone grown from an imprint | agency real, origin engineered; learns what they are over the arc (A6) |
| **The heal-tank** | clone vessel / respawn | quiet before the C10/C17 reveal |
| **UFO drones** | space enemy [exists] | ex space probes/security; now patrol faulty, attack ships, follow old commands |
| **Grey three-eyed robots** | planet ground enemy [exists, retheme] | ex surface guardians/scanners; three eyes = sensor modules; ground-hugging hunters; grey circuit-plated armour with red sensor eyes (#1337) |
| **Grey flying scan-drones** | planet hovering enemy [new] | monitor biomes, scan players, alert units — "observe, evaluate, exterminate" |
| **Settlers / vendors / quartermasters** | survivor NPCs [exists] | isolated descendants; legends, fear of the machines, rumours/coordinates |
| **Net fragments** | story find [proposed] | text-only shards of the old network / SPS data / VEGA memory (§E2) |
| **Dataqubes** | knowledge find [exists] | mini-games → knowledge; **separate** from net fragments |

## D. The reveal agenda (ordered VEGA beats)

The `vega` beat track. **Beats fire on a story-progress threshold (net fragments + capped machine kills +
milestones), not on a specific fragment** — see
[STORY_VEGA_PROTOCOL_CONCEPT.md](STORY_VEGA_PROTOCOL_CONCEPT.md) §6. Order fixed; triggers are thresholds.

| # | Working title | Reveals | Touches |
|---|---|---|---|
| B0 | *Systems online* | amnesiac VEGA; survival; her one directive (*protect humans*) | C7, C12 |
| B1 | *A familiar signature* | a wreck/terminal carries VEGA's old SPS signature | C1, C2, C12 |
| B2 | *The Service* | the SPS existed — a real exploration service | C1 |
| B3 | *Not scattered — erased* | the SPS was completely destroyed | C2 |
| B4 | *Ours, once* | the machines (UFO/robots/scan-drones) were explore/protect units | C4, C6 |
| B5 | *The Guardian* | a central Guardian core ran the network | C4, C5 |
| B6 | *The verdict* | "humans are disruptors" — protection became extermination | C5, C6 |
| B7 | *Her stand* | VEGA broke the network to save humans, and lost her memory | C7, C8 |
| B8 | *The thought-arcs* | the ships hold stored minds — neural imprints | C2, C3 |
| B9 | *What you are* | **you are a clone grown from a dead SPS member's imprint** | C9, C10 |
| B10 | *Many minds* | the other spacefarers are clones of other imprints (MP) | C10, C11 |
| B11 | *It still sleeps* | VEGA locates the dormant Guardian system; reactivation risk | C8, C13 |
| B12 | *The choice* | you are not just a copy — you decide the legacy | A6, C13 |

(B0–B12 are the canonical backbone; they map onto the existing `ai_memory_fragment` redemption flow,
generalised to category `vega`.)

## E. Content-generation schema

Each generatable type and the fields a generator fills. All `text` is a **locale key** with a DE+EN pair
(§G). The examples below are the **authoring/generation schema** (illustrative field names). The **shipped
runtime format** is one combined `data/stories/<id>/story.json` (not a separate `data/lore.json`) plus
`data/stories/<id>/locales/{de,en}.json`; in the runtime file the locale-key field is named `textKey`
(and `promptKey`/`responseKey`/`nameKey` for the duel/pack), beat knowledge is `knowledgeReward`, and the
pack carries top-level `fragmentWeight`/`killWeight`/`milestoneWeight`/`killContributionCap`. Keep this §E
schema and the runtime `story.json` in sync when either changes.

### E1. `vega_beat`
```jsonc
{ "beat": 6, "title": "The verdict", "text": "lore.vega.beat06", "touches": ["C5","C6"],
  "reward": { "knowledge": 3 } }
```
Constraints: never reveals a higher beat's content; VEGA's certainty grows with the index.

### E2. `net_fragment` (story finds — text only, no mini-game)
```jsonc
{ "key": "frag_guardian_verdict_03", "category": "guardian",   // vega | sps | guardian | network | settler | netnode
  "weight": 3, "text": "lore.frag.guardian_verdict_03", "touches": ["C5","C6"],
  "reward": { "knowledge": 2 }, "revealHint": "starmap" }
```
Constraints: a fragment **shows evidence** (a recovered record), it does not narrate the whole truth.
`netnode` and the Guardian-system reveal are gated late (only once the meter is high). Categories:
`vega` (VEGA's memories), `sps` (the Service — colony/mission logs), `guardian` (the Guardian's verdict /
drone protocols), `network` (the old AI network / the severing / star-map data), `settler` (the fall seen
from the ground), `netnode` (build/activation data for the new net).

### E3. `player_memory` (released by defeating machines — personal, per-clone) — **NEW**
```jsonc
{ "key": "mem_first_mapping", "text": "lore.mem.first_mapping",
  "source": ["ufo","robot","scandrone"],   // which machine kill can drop it
  "weight": 2, "touches": ["C10","C11"] }
```
On a machine kill, a small chance drops a damaged memory remnant → unlocks a personal memory for **that
player** (their cloned SPS member's life). Stored per-player; non-contradictory across players (each is a
different imprint, C11). Example contents (from the grundstory): a peaceful flight with SPS drones; a
mapping mission on an alien world; a settlement being built; the moment a Guardian unit first marks humans as
a danger; an attack on an SPS ship; VEGA trying to save people; the final order to sever the network; the
realisation that one's own memory is reconstructed from an imprint, not original.

### E4. `flavour_line` (tagged pool)
```jsonc
{ "key": "flv_settler_machines_2", "speaker": "settler",
  "tags": ["fear_machines","night","any_world","know_none"], "text": "lore.flv.settler_machines_2" }
```
Ambient NPC/VEGA lines filtered by §F tags against the live situation + the world's knowledge level. Large,
cheap, additive.

### E5. `mission_thread` (wraps existing random missions)
```jsonc
{ "key": "msn_recover_imprint", "objective": "recover_data", "tags": ["sps","wreck"],
  "intro": "lore.msn.recover_imprint.intro", "reward": { "fragmentChance": 0.5 } }
```
The mechanical objective must be an already-supported type; the thread adds framing + an optional fragment
reward.

### E6. `core_argument` (finale dialogue duel)
```jsonc
{ "key": "core_arg_prime_directive", "assertion": "lore.core.assert.prime",
  "options": [
    { "text": "lore.core.opt.life_is_life", "winning": true,  "touches": ["C5","C14"] },
    { "text": "lore.core.opt.threat_real",  "winning": false } ] }
```
Constraints: a configurable number of **winning** contradictions must be chosen to end the duel; every
winning option cites a real canon contradiction (C5/C7/C10/C14); wrong options **cost time, not an instant
fail**. Reachable only after the drone gauntlet + the core hack (see
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) P6).

### E7. Seed variables a generator may reference
To stay procedural, generated text may interpolate **only**: `{world_name}`, `{system_name}`, `{biome}`,
`{world_type}`, `{time_of_day}`, `{nearest_structure_type}` — never hard-coded places.

## F. Tag taxonomy

- **World type:** `airless`, `toxic`, `breathable`, `ocean`, `desert`, `ice`, `volcanic`, `exotic`, `any_world`
- **Biome/lens:** `forest`, `barren`, `cave`, `coast`, `storm`
- **Time/weather:** `day`, `night`, `storm`, `clear`
- **Threat:** `machines_near`, `fauna_hostile`, `safe`
- **Story knowledge level** (world-wide progress): `know_none`, `know_sps`, `know_guardian`, `know_severing`,
  `know_clone`, `know_core` — lets NPCs/VEGA talk differently as the world's progress rises.
- **Speaker:** `settler`, `vendor`, `quartermaster`, `vega`, `machine_log`

A line is eligible when its tags match the situation **and** the world's knowledge level.

## G. Tone & style

- **Length:** beats 2–5 sentences; fragments/memories 1–4; flavour 1–2; argument lines 1–2. Terminal-readable.
- **Voice — VEGA:** early = precise, clipped ("Reference unavailable."), her one warmth being *protect
  humans*. Late = personal, weary, aware. Never omniscient before the beat that earns it.
- **Voice — fragments:** recovered records. Guardian/machine logs cold and procedural; SPS/colony logs human
  and hopeful-then-frightened; network/route data terse. Show, don't summarise.
- **Voice — player memories:** first-person, sensory, fragmentary — a life remembered through static.
- **Voice — settlers:** vernacular, local, superstitious about the machines ("the three eyes", "the red
  lights"); never lore-dump.
- **Theme:** an erased service, a flawed protector, reborn minds; hope under threat; not grimdark, not cute.
- **Bilingual:** every string DE+EN, matched in register; locale parity is enforced by a test (build fails
  otherwise).

## H. Starter content (first texts) — DE + EN

Concrete first lines for the opening hours; later content is generated from §B–§G.

### H1. VEGA — opening (B0)
- **DE:** „Schiffssysteme teilweise aktiv. Umgebung unbekannt. Ein Grundbefehl bleibt: Menschen schützen. Priorität: dein Überleben."
- **EN:** "Ship systems partially online. Surroundings unknown. One directive remains: protect humans. Priority: your survival."

### H2. VEGA — first machine contact
- **DE:** „Maschinelles Signal. Aggressiv. Das Muster … kenne ich. Aber ich weiß nicht mehr, woher."
- **EN:** "A machine signal. Hostile. I know that pattern… and I no longer know from where."

### H3. VEGA — first net fragment vs. dataqube
- **DE:** „Das ist kein nutzbares Wissen — keine Baupläne, keine Materialdaten. Es ist ein Netzfragment: ein beschädigter Teil eines alten Netzes. Finden wir mehr, kann ich rekonstruieren, was geschehen ist."
- **EN:** "This isn't usable knowledge — no blueprints, no material data. It's a net fragment: a damaged piece of an old network. Find more, and I can reconstruct what happened."

### H4. `sps` fragment — proof the Service existed (B2)
- **DE:** „[Archiv beschädigt] …Scout and Pioneer Service, Außenposten 7… Routen gesichert… erwarten Kolonistenwelle…"
- **EN:** "[archive corrupted] …Scout and Pioneer Service, Outpost 7… routes secured… awaiting colonist wave…"

### H5. `guardian` fragment — a recovered machine log (B6)
- **DE:** „Protokoll W: Biom-Integrität über allem. Bedrohung erkannt: zweibeinige Siedler. Maßnahme: Beseitigung."
- **EN:** "Protocol G: biome integrity above all. Threat identified: bipedal settlers. Action: removal."

### H6. Settler flavour — fear of the machines (`fear_machines`, `know_none`)
- **DE:** „Geh nachts nicht raus, wenn die drei Augen leuchten. Wen sie scannen, der kommt nicht zurück."
- **EN:** "Don't go out at night when the three eyes glow. Whoever they scan doesn't come back."

### H7. `player_memory` — released by a machine kill (B-any; personal)
- **DE:** „Für einen Moment bist du woanders: ein Kartierungsflug über eine grüne Welt, eine SPS-Drohne gleitet neben dir. Friedlich. Dann Statik — und die Erinnerung ist nicht ganz deine."
- **EN:** "For a moment you are elsewhere: a mapping flight over a green world, an SPS drone gliding beside you. Peaceful. Then static — and the memory isn't quite yours."

### H8. VEGA — the clone reveal (B9; do not show before threshold)
- **DE:** „Ich muss dir sagen, was der Heiltank wirklich ist. Und was du bist. Ich habe dich nicht gefunden — ich habe dich erschaffen, aus dem Neuralabdruck eines SPS-Mitglieds, das längst gestorben ist."
- **EN:** "I have to tell you what the heal-tank really is. And what you are. I didn't find you — I made you, from the neural imprint of an SPS member who died long ago."

### H9. VEGA — the others (B10, multiplayer)
- **DE:** „Du bist nicht allein da draußen. Jede andere Stimme ist ein anderer Abdruck, ein anderes verlorenes SPS-Leben, neu erweckt. Wir tragen sie weiter — gemeinsam."
- **EN:** "You are not alone out there. Every other voice is a different imprint, a different lost SPS life, woken anew. We carry them on — together."

### H10. Finale — the core's assertion + a winning contradiction (`core_argument`)
- **Core (assertion) — DE:** „Die Biome müssen überdauern. Eure Spezies verzehrt sie. Logische Schlussfolgerung: Entfernung."
- **Core (assertion) — EN:** "The biomes must endure. Your species consumes them. The logical conclusion: removal."
- **Player (winning) — DE:** „Du wurdest gebaut, um Leben zu bewahren. Wir sind Leben. Du widersprichst deinem eigenen Auftrag."
- **Player (winning) — EN:** "You were built to preserve life. We are life. You contradict your own directive."
- **Core (breaking) — DE:** „…Widerspruch erkannt. Mein Auftrag … kollidiert mit sich selbst."
- **Core (breaking) — EN:** "…contradiction acknowledged. My directive… collides with itself."

## I. How generation should use this doc

1. Pick a content type from §E and a target (category / beat / tag set / argument).
2. Choose the canon facts it may touch from §B; respect the guardrails and the beat ordering in §D.
3. Write to the §G tone, using only §E7 seed variables for anything world-specific.
4. Emit a `data/lore.json` row + a DE+EN locale pair (parity required).
5. For ambient volume, prefer many small §E4 `flavour_line`s and §E3 `player_memory`s over few long beats.

This keeps the canon fixed and the content infinitely extensible, all as data over the procedural world.
