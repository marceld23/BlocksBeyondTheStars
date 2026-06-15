# Creature Taming & Companions — Design / Analysis

Status: **IMPLEMENTED 2026-06-15** (server + tests + client wiring + icons; needs a Unity client build).
P1–P3 of [§12](#12-suggested-phasing) shipped together: content, taming ritual, companions, knowledge reward,
the HUD prompt, the Companions menu tab, and companion rendering. 598/598 tests green. The four product
decisions ([§11](#11-decisions-locked-2026-06-15)) were all taken as the locked baseline. P4 (functional roles,
portable companions, the study trickle, dye/accessories) remains future work.

**Shipped scope:** home-planet-resident companions · translator-led bonding ritual · dedicated
"Begleiter/Companions" menu tab · passive invulnerable followers.

**Key files:** server `GameServerTaming.cs` (+ hooks in `GameServerCreatures.cs`, `GameServerGadgets.cs`),
`Shared/State/TamedCreature.cs`, `CreatureBehaviour.FollowStep`; net `TameRespondIntent`/`TameProgress`/
`TameResult`/`CompanionList`/`SetCompanionNameIntent`/`ReleaseCompanionIntent` (+ `RequestCompanionsIntent`);
client `HudUi` taming prompt, `CraftingTechShipUI` Companions tab, `CreatureView`/`CreatureBuilder` tint+nameplate;
data `creature_translator` + 3 bait items/recipes/blueprint/locales; tests `CreatureTamingTests.cs`.

Pitch: a player can **tame wild creatures** with a handheld **Creature Translator**. A tamed creature
becomes a named **companion** that lives on the planet where it was tamed, follows the player while they
are on that world, is saved per player, and is listed in the in-game menu. Taming gets harder (and is
partly randomised) by species and by the individual animal's "personality".

---

## 1. Goal (player-facing)

- Find a wild creature → use the **Creature Translator** to read its mood/needs → respond correctly
  (feed the right bait, hold still, back off) → earn its trust → **tame** it.
- Name it. It now **follows you on that planet** and re-appears whenever you return to that world.
- See all your companions in a dedicated **menu panel** (name, species portrait, home planet, bond).
- Some species are easy (placid grazers); some are hard (skittish, territorial, pack hunters); two
  animals of the *same* species can still differ, so there is trial-and-error, not a fixed recipe.

## 2. Existing systems this builds on (the hard constraints)

| System | Where | What it means for taming |
|---|---|---|
| **Creatures are transient** | `GameServerCreatures.cs` | Wild creatures are **never persisted** — they regenerate per visit, seeded from `worldSeed XOR hash(planetKey)` (`CreatureGenerator.cs:25`). A tamed creature must become **new player-state**, not a saved wild creature. |
| **Species roster is per-world** | `CreatureGenerator.cs` | `sp0` on planet A ≠ `sp0` on planet B. A companion must store its **full species descriptor snapshot**, not just `"sp3"`, so it survives independently of any world's roster. |
| **Rich species traits** | `Shared/Definitions/CreatureSpecies.cs:55` | `Habitat` (Land/Water/Lava/Air/Cave/Amphibian), `Activity`, `Temperament` (Passive/Skittish/Territorial/Aggressive/PackHunter), `MaxHealth/Speed/AttackDamage/Size`, appearance (legs/eyes/horns/glow/two-tone color), `BiomeAffinity`, `DropItem`. → drives **taming difficulty**. |
| **Runtime creature = `CombatEntity`** | `GameServerSpaceCombat.cs:24` | Has `FrozenTimer`, `ProvokeTimer`, `SizeScale`, `Hull`. We add `OwnerId` + a `Follow` behaviour. |
| **Per-individual variance** | `GameServerCreatures.cs` (`FaunaSizeScale`), `CreatureGenerator.cs` (biome color shift) | Already seeds size (0.7–1.3) + biome color per individual from the entity id → we extend this seed into a hidden **personality** (preferred bait, patience, trust gain). |
| **Behaviour engine** | `Shared/Definitions/CreatureBehaviour.cs:19` | `Step()` already supports "move toward nearest player" (Aggressive) and "flee" (Skittish). A **Follow-owner** mode is a small variant (approach owner, keep ~3–5 blocks, don't path into ships). |
| **Stasis precedent** | `GameServerGadgets.cs:134` (`UseStasisProjector`) | A gadget already freezes creatures in a radius via `FrozenTimer`. The translator can reuse this to "hold attention" during a taming step. |
| **Gadget pipeline** | `GameServerGadgets.cs:42` | `UseGadgetIntent{GadgetKey,X,Y,Z}` → validate (is gadget / owned / energy / cooldown) → effect → deduct suit energy + set cooldown. Targets by **aim point + radius** (no entity id today). Our translator either picks the nearest creature to the aim point, or we extend the intent with a creature id. |
| **Persistence = JSON blob** | `Persistence/Snapshots.cs`, `SqliteWorldRepository.cs:191` | `PlayerState` ⇄ `PlayerSnapshot` serialised to the `player.json` column. Adding `List<TamedCreature>` needs **no DB schema change** — just snapshot mapping (precedent: `LandedBodies`, `KnownSystems`, `NpcMemory`). |
| **Menu tabs** | client `GameMenu.cs:18` (`Tab` enum) + `CraftingTechShipUI.cs:27` (`Mode` enum) | Tabs: Inventory/Crafting/Tech/Ship/Map/Missions/Character/Alliances/Story. Add **Companions**. Rename widget (`AddRenameRow`, `CraftingTechShipUI.cs:2322`) + "new content" badge (`UiKit.AddBadge`) already exist. |
| **Content = JSON + locale** | `data/items.json`, `recipes.json`, `blueprints.json`, `data/locales/{en,de}.json` | New tool + bait items follow `field_medkit`/`stasis_projector` exactly. Bilingual DE+EN required. |
| **Naming collision** | `GameServerShipAi.cs`, `Messages.cs:1309` | The ship AI **VEGA** already owns the term *"companion panel"* in the HUD. Use **"Menagerie / Begleiter / Pets"** for creatures to avoid confusion. |

**No existing tame/capture/pet code exists** — this is greenfield on top of the above.

## 3. Core design

### 3.1 Residence model (see [§11](#11-open-decisions) — must confirm)
Recommended default, matching the request ("auf diesem Planeten dann bei ihm"):
**home-planet resident.** A companion belongs to the body it was tamed on. It is present only when the
player is on that body; elsewhere it is stored. This keeps scope contained (no cross-world creature
streaming) and is fictionally clean ("your animals live on your homestead world").

Alternatives considered: a *portable menagerie* (summon any companion anywhere) or *one active
companion you carry + a stored stable*. These need an extra "active companion travels with you" path
and raise questions about creatures surviving in space / on hostile worlds. Deferred to a later phase.

### 3.2 Data model — `TamedCreature` (new, player-state)
Stored in `PlayerState.TamedCreatures : List<TamedCreature>` (persisted in the player blob).

```
TamedCreature {
  string  Id            // stable guid, our own — not the wild entity id
  string  HomeBodyId    // locationId of the planet/moon it lives on
  string  CustomName    // player-given; defaults to a coined name (NameGenerator) or "<Species> #n"
  SpeciesSnapshot Species // full descriptor: traits + appearance (so it renders/behaves w/o a world roster)
  uint    IndividualSeed // size, color shift, personality — reproduces the exact individual look
  int     Bond          // 0..100 trust/affection (room for growth: perks, evolutions, dye)
  long    TamedAtUtc    // stamped by server
  // optional later: int? DyeRgb, string? Accessory
}
```
`SpeciesSnapshot` is a serialisable subset of `CreatureSpecies` (everything `NetCreature` already
carries). This is the key decoupling: a companion is **self-contained**, independent of any world seed.

Capacity: cap companions (e.g. **6 per planet**, soft total cap) to bound persistence + spawn load.

### 3.3 Companion lifecycle
- **Tamed →** create `TamedCreature`, persist, immediately spawn it as a companion `CombatEntity`
  (`OwnerId=player`, `Behaviour=Follow`, non-hostile, invulnerable, **not** counted against the wild cap).
- **Player arrives on a body / world loads →** spawn each resident companion near the player (reuse the
  existing safe spawn-ring), Follow behaviour, `Tamed` flag set in `NetCreature`.
- **Player leaves the body / goes to space →** despawn companion entities (data stays persisted).
- **Follow behaviour** (`CreatureBehaviour`): move toward owner if > ~4 blocks; idle-wander within
  ~3 blocks; teleport-snap if > ~30 blocks or stuck (same anti-cliff/anti-ship guards as wild movement);
  never path into ships/bases. Habitat height rules already exist (water animals porpoise, air hover).

## 4. The Creature Translator (and bait)

A **gadget** item exactly like `stasis_projector` (`data/items.json`):
```json
{ "key": "creature_translator", "nameKey": "item.creature_translator.name", "category": "tool",
  "maxStack": 1, "descriptionKey": "item.creature_translator.desc",
  "tool": { "kind": "gadget", "tier": 2, "energyPerUse": 3.0 } }
```
- **Recipe** (`recipes.json`, workshop, blueprint-gated) + **blueprint** (`blueprints.json`, with a
  `knowledgeCost` so you must scan some fauna first — thematically you need data to build a translator)
  + **locale** keys in `en.json`/`de.json`. Mirror `field_medkit` line-for-line.
- **Bait/treats** consumables (`category:"consumable"`), e.g. `forage_bait` (herbivores),
  `meat_bait` (carnivores), `lure_pheromone` (skittish), `calm_emitter` (aggressive). Different species
  prefer different bait; the translator hints at the *category*, not the exact item.

### Why a translator (fiction → mechanic)
"You can't tame what you can't understand." The translator **decodes the creature's current signal**
(mood + need) into readable text, and lets you pick a **response**. Correct responses build trust;
wrong ones spook or provoke. This makes the device central rather than a re-skinned capture ball.

## 5. Taming interaction (difficulty + randomization)

Recommended mechanic: **translator-led bonding ritual** (multi-step, hidden trust meter).

1. **Decode** — equip translator, aim at a creature, use it (`UseGadgetIntent` → new handler).
   Server replies with the creature's current **mood** + a **need hint** (`hungry / wary / curious /
   territorial / hostile`) and the **bait category** it craves. Hints are fuzzy, not exact.
2. **Respond** — the player acts: offer a bait item, hold still (re-use stasis to "hold attention"),
   approach slowly, or back off. Each correct response raises hidden **trust**; wrong response lowers it,
   triggers **flee** (skittish) or **provoke** (territorial/aggressive — pack rally for PackHunter).
3. **Repeat** until trust ≥ threshold → **tamed** → name prompt → companion spawns.

### Difficulty knobs (per species)
- **Temperament** is the master dial: Passive (few steps, forgiving) → Skittish (must approach slow,
  flees on error) → Territorial (must never provoke) → Aggressive → PackHunter (hardest; nearby kin
  interfere). Map to: required trust, trust gained per correct step, penalty per error, patience window.
- **Habitat / exotic-ness**: Cave/Lava/Air + `Glows` + large `Size` species cost more steps / rarer bait.
- **Translator tier / knowledge**: a higher-tier translator (or more scanned fauna) reveals clearer
  hints, shrinking the guesswork on hard species.

### Randomization (per individual)
From `IndividualSeed` (the entity-id hash already used for size/color), derive a hidden **personality**:
- preferred bait *item* within the hinted category (so same-species animals differ),
- patience (how fast it loses interest), trust-gain multiplier, spook threshold.
The translator surfaces **partial** info; the player still experiments. Two identical-looking grazers
can tame differently → emergent, replayable.

## 5.1 Knowledge reward (taming feeds research)

A successful tame grants **knowledge points** — bonding with an alien animal teaches you about it, the
same currency you earn by scanning. This hooks into the existing system without new mechanics:

- `PlayerState.KnowledgePoints` is a **permanent threshold** that gates blueprint unlocks and is *never
  spent* (`PlayerState.cs:52`). Taming adds to it like a first scan does.
- **Anti-grind (mirror the scan model):** scanning only pays on *new* subjects via the `Scanned` set
  (`"creature:sp0"`, `PlayerState.cs:65`). Taming reuses this idea with a new `TamedSpecies` set keyed by
  a stable species signature: the **full knowledge bonus is paid once per species** you tame. Taming a
  *second* animal of an already-known species gives only a **small repeatable trickle** (or nothing) —
  so you can't farm progression by re-taming placid grazers.
- **Scaled by taming difficulty:** the first-tame bonus scales with the same difficulty inputs as §5 —
  `Temperament` (Passive → PackHunter), exotic habitat (Cave/Lava/Air), `Glows`, large `Size`. Hard,
  rare species are worth markedly more knowledge than easy ones, so effort is rewarded.
- **Codex synergy (optional):** a first tame can also mark that species as `Scanned` (fill the scan
  codex), since you've clearly studied it up close. Kept as a toggle to avoid double-paying.
- Award path: in the tame-success handler (`GameServerTaming.cs`), add the points, push the updated
  total to the client via the existing inventory/state sync (the HUD/menu/tech tree already read
  `KnowledgePoints` back from there — same path scanning and minigames use,
  `GameServerDataCubes.cs:167`). No new message needed for the number itself; a short "+N knowledge"
  toast can ride the tame-result message.

*Deferred variant (P4):* a slow **"study" trickle** while a companion accompanies you (a few points per
in-game day, hard-capped per species) — honors the "yields knowledge over time" feel but needs an
AFK-grind cap, so it is out of the P1 scope.

## 6. Recognition — how the player spots their companion

A companion must be unmistakable vs. wild fauna:
- **Floating nameplate** with its `CustomName` above it (wild creatures have none).
- **Friendly tint / soft halo ring** under it (the renderer already does a hostile **red** tint and an
  asleep dim — add a calm **cyan/green** friendly tint + a faint ground ring).
- Optional: a small **collar** accent block on the model, or a gentle idle particle.
- `NetCreature` gains `OwnerId` (+ implicit `Tamed = OwnerId != ""`); the client renders the nameplate/
  halo when `Tamed` and friendly. Reuses existing `CreatureView`/`CreatureBuilder` tint paths.
- Optional HUD/compass chip: "🐾 <name> nearby".

## 7. Naming

- On successful tame, show a small **name dialog** (reuse `AddInput` / `AddRenameRow` pattern,
  `characterLimit` ~24). Default = a coined name from `NameGenerator` (already coins species names) or
  `"<SpeciesName> #n"`.
- **Rename later** from the Companions menu (new `SetCompanionName` message → updates `CustomName`).
- Bilingual: only UI chrome is localised; the player's chosen name is free text.

## 8. Menu placement (explicitly requested)

Recommended: a **dedicated "Companions / Begleiter" tab** (parallel to Alliances), because it is a
growing collection with per-item actions — folding it into Character would crowd that panel.

- **Tab**: add `Companions` to `GameMenu.Tab` (`GameMenu.cs:18`) and `CraftingTechShipUI.Mode`
  (`CraftingTechShipUI.cs:27`); add the tab button + locale key in `BuildHeader` (`:394`); add a
  `case Mode.Companions: BuildCompanionsList()` in the rebuild switch (`:655`).
- **Sidebar** categories: e.g. *On this planet* / *All* / *Released*.
- **List**: one card per companion — procedural portrait (reuse `CreatureBuilder` into a small
  render-texture, or a simple icon first), `CustomName`, species, home planet, bond bar, online dot
  if present on the current world.
- **Detail**: rename row, "locate/whistle" (re-summon next to player if on the home world), **release**
  (untame → optionally re-wilds), bond/traits readout.
- **Badge**: set a `NewCompanionUnseen` flag → `UiKit.AddBadge` on the tab until opened (existing path).
- **HUD**: optional small active-companion chip near the hotbar (`HudUi.cs`).

Data to the menu: a new `CompanionList` message (player's `TamedCreature`s) refreshed on change.

## 9. Companion role / stakes (see [§11](#11-open-decisions))
Recommended start: **passive, invulnerable followers** (decoration + charm, zero combat risk). Defer
functional roles (defend you, fight, haul cargo, sniff out ore, produce a resource over time) and
"companions can die" to a later phase — they multiply AI + balance + UI work.

## 10. File-by-file change list

**Content (data, no code):**
- `data/items.json` — `creature_translator` (gadget) + bait consumables.
- `data/recipes.json` — recipes (workshop, blueprint-gated).
- `data/blueprints.json` — `creature_translator` blueprint (+ `knowledgeCost`).
- `data/locales/en.json`, `data/locales/de.json` — names/descriptions + all new UI strings (DE+EN).

**Shared:**
- `Shared/State/PlayerState.cs` — `List<TamedCreature> TamedCreatures` + `HashSet<string> TamedSpecies`
  (first-tame knowledge bookkeeping, mirrors `Scanned`).
- `Shared/Definitions/` — new `TamedCreature` + `SpeciesSnapshot` types; `CreatureBehaviour` gains a
  `Follow` mode; `CombatEntityKind`/behaviour wiring for owned creatures.
- `Shared/Definitions/CreatureSpecies.cs` — helper to/from `SpeciesSnapshot`.

**Persistence:**
- `Persistence/Snapshots.cs` — `PlayerSnapshot.TamedCreatures` + `TamedCreatureDto` + `TamedSpecies`;
  `StateMapper` to/from. (No SQLite schema change — rides the player JSON blob.)

**Networking:**
- `Networking/Messages.cs` — `TameDecodeIntent`, `TameRespondIntent`, `TameProgress` (mood/need/trust),
  `TameResult`, `CompanionList`, `SetCompanionName`, `ReleaseCompanion`; extend `NetCreature` with
  `OwnerId`. 
- `Networking/NetCodec.cs` — **`Register(...)` every new message** (else send silently no-ops).

**Server:**
- new `GameServerTaming.cs` — decode/respond handlers, trust accumulation, difficulty+personality from
  species+seed, success → create+persist `TamedCreature` + **award difficulty-scaled knowledge (full
  bonus once per species via `TamedSpecies`, small trickle after)**, spawn/despawn companions on world
  enter/leave, Follow tick, rename, release, `CompanionList` builder. (partial `GameServer`.)
- `GameServerCreatures.cs` — exclude companions from the wild cap; broadcast `OwnerId`.
- `GameServerGadgets.cs` — route `creature_translator` to the taming handler.

**Client (Unity):**
- `GameMenu.cs`, `CraftingTechShipUI.cs` — Companions tab + panels + rename/release.
- `CreatureView.cs` / `CreatureBuilder.cs` — friendly tint, ground ring, floating nameplate for owned.
- `HudUi.cs` — taming interaction prompt (mood readout + response buttons) + optional companion chip.
- `GameBootstrap` — hold `CompanionList`, `NewCompanionUnseen` flag; new send methods.

**Tests:** taming success/fail by temperament; personality variance from seed; persistence round-trip;
companion spawns on its home body only; capacity cap; **first-tame grants knowledge scaled by difficulty,
re-taming same species does not re-pay the full bonus** (`TamedSpecies` gate); locale parity (en/de).

## 11. Decisions (LOCKED 2026-06-15)

1. **Residence → home-planet resident.** A companion lives on the body it was tamed on; present only when
   the player is on that world, otherwise stored. No cross-world creature streaming in scope.
2. **Taming mechanic → translator-led bonding ritual.** Decode mood/need → respond (bait / hold still /
   approach) → hidden trust meter → tame. (Not feed-with-chance, not stasis-capture.)
3. **Menu placement → dedicated "Begleiter/Companions" tab** (parallel to Alliances). HUD chip deferred.
4. **Companion role → passive, invulnerable followers** (charm/decoration, zero combat risk). Functional
   roles (defend/fight/haul/gather) and mortality are deferred to **P4**.

## 12. Suggested phasing
- **P1** — content (translator + bait), `TamedCreature` state + persistence, decode/respond + trust,
  tame → spawn one Follow companion on its home world, nameplate + friendly tint, **first-tame knowledge
  reward** (difficulty-scaled, once per species via `TamedSpecies`). (server + tests)
- **P2** — Companions menu tab (list/rename/release) + badge; name dialog; HUD prompt polish.
- **P3** — difficulty curve + per-individual personality tuning; bait variety; translator-tier hint clarity.
- **P4 (optional)** — functional roles, portable/active companion, bonding perks, dye/accessories.
