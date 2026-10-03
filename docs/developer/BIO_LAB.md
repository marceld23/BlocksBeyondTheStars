# The bio lab — seeds, samples, substances, changed gear and breeding

Epic #2212 (parts #2200–#2211), branch `feat/bio-lab`, 2026-10-03. Status: implemented (see
[../../TODO.md](../../TODO.md) for the live Done/Open status). No protocol bump (three new messages and additive
fields on existing ones, protocol stays **8**) and no terrain-generation bump (nothing here draws from the world
generator's random stream).

## 1. Goal

Everything a player harvests — a plant, an animal, an ore vein — belongs to a **species** (or a **deposit**) with a
seed. The bio lab turns that seed into something a player can work with:

- **Samples** carry the seed. A harvest yields what it always did, plus one sample.
- **Analysis** in the bio lab shows what a species carries: one **substance** with an effect, a strength and
  sometimes a catch.
- **Synthesis** mixes samples into **preparations** that give a status effect for a while.
- **Changing** a tool or a piece of gear with a material and a coating shifts its values.
- **Cloning and crossing** in the Crystal Net's clone tank grows animals from samples — also on other worlds — and
  crosses two species into a new one; the lab raises **seedlings** of plants, including crosses with a new form.

The design rule: **properties are generated, reactions are computed.** There is no recipe list. What a species
carries follows from where and how it lives (planet → biome → species), so a player can search with reason — heat
protection comes from what survives beside a lava lake.

## 2. Design principles

- **The server is the truth.** Samples, the species register, research, effects, changed items and bred plants are
  server state. The client sends one intent (`BioLabIntent`) and draws what it is told. A profile is a *pure
  function* of seed and context in `Shared`, so the client derives the same numbers for its tooltips without a
  round trip — it never decides anything with them.
- **No stack fragmentation.** A seeded stack per species would split every backpack. Harvested items stay exactly
  as they were; the seed rides on a separate **sample** item that lives in its own container, the **sample case**.
- **Derived, never stored.** A profile is not saved anywhere. The register stores the *context* (tags, scarcity
  points, toxicity, carrier) once per species; a balance change in `BioRules` reaches every sample ever taken.
- **Integer hashing only.** All derivation goes through `BioHash` (SplitMix-style 64-bit mixing) — no floats, no
  `System.Random` in the profile path — so server (.NET 10) and client (Unity/Mono, WebGL) agree bit for bit.
- **Bounded.** Fixed strength levels 1–15 on a flattening curve, caps below what researched gear gives, at most
  three effects at once, hard caps on register, research and bred plants.
- **Rarity is found, never rolled.** A rarity tier comes from real hurdles (a rare planet type, a hard habitat, a
  dangerous animal); the seed moves it by one tier at most.

## 3. Code map

| Where | What |
|---|---|
| `Shared/Bio/BioHash.cs` | integer hashing, the seed of a creature / plant / deposit / authored species / cross |
| `Shared/Bio/BioEnums.cs` | `BioKind`, `BioEffect`, `BioSideEffect`, `BioCarrier`, `BioThermal`, `BioForm`, `BioReaction`, `BioTag`, `MatTrait`, `ModStat` — all **append-only** (ids ride in item keys and saves) |
| `Shared/Bio/BioRules.cs` | every number: curve, caps, rarity bands, the context table, side effects, the 8×8 reaction table, forms, durations |
| `Shared/Bio/BioContexts.cs` | planet / biome / species → `BioContext` (tags + scarcity points) |
| `Shared/Bio/BioProfile.cs` | `BioProfiles.Derive(seed, context)` and `BioProfiles.Cross(a, b, childSeed, differentWorlds)` |
| `Shared/Bio/MaterialProfile.cs` | fixed material traits (`labTraits`) + the origin values of a deposit |
| `Shared/Bio/Synthesis.cs` | the mixer: `Synthesis.Compute` → `Compound`; the 10-hex key payload |
| `Shared/Bio/PlayerEffects.cs` | running effects: factors for movement, combat, drains; `Tick`, `Check`, `Add` |
| `Shared/Bio/ItemMods.cs` | changed items: the `u` key tag, `ToolMods`, `GearMods`, `ItemModRules.Compute` |
| `Shared/Bio/BioSpecies.cs` | the register entry, `FloraGenome`, `FloraForm` (the packed look of a bred plant) |
| `Shared/Bio/BioItems.cs`, `BioNames.cs` | item keys, blueprints, key helpers; coined substance and compound names |
| `GameServer/GameServerBio.cs` | register, research books, the book sent to clients, samples, the sampler |
| `GameServer/GameServerBioLab.cs` | the lab's five actions, preparations, the effect tick, the shield |
| `GameServer/GameServerBioBreeding.cs` | clone tank on samples, guest species, crosses, bred plants |
| `Networking/Messages/BioMessages.cs` | `BioBook`, `BioLabIntent`, `BioLabResult`, `NetEffect`, `NetBioSpecies` |
| `Client.Core/BioClientState.cs` | the client's mirror: sample case, effects, book; derives profiles locally |
| `client/…/Scripts/BioLabUi.cs`, `BioSenses.cs` | the lab screen; night sight and perception |

## 4. Seeds

A seed is a `uint`, never 0. Nothing new is rolled — every seed is folded from data that already exists:

| Subject | Seed | Stable because |
|---|---|---|
| rolled animal species | `BioHash.CreatureSeed(species.VoiceSeed)` | the roster gives each species its voice seed |
| authored animal (`au_<key>`) | `BioHash.AuthoredSeed(key)` | the same on every world and in every save |
| plant / tree species | `BioHash.FloraSeed(rosterSeed, planetKey, blockKey)` | one species per flora block per world |
| deposit | `BioHash.DepositSeed(rosterSeed, materialItem)` | one deposit per material per world |
| cross | `BioHash.CrossSeed(a, b)` (commutative) | the same pair always gives the same child |

The seed rides in the item key behind the tag `x` (`ItemKey.WithSeed`, `ItemKey.Seed`): `bio_sample#x1a2b3c4d`.
A preparation uses the same tag for its 10-hex compound payload; a changed tool or piece of gear uses the tag `u`
with 6 hex digits. `Inventory` and `MaterialPool` compare keys exactly, so equal samples and equal preparations
stack and different ones never mix.

## 5. Samples and the sample case

- `PlayerState.SampleCase` is a second `Inventory` (24 slots, 20 per species). It is part of the player snapshot
  and travels in `InventoryUpdate.Samples` (sent only when its signature changed — `SamplesUnchanged` otherwise).
- **Sources** (`GameServerBio.cs`):
  - harvesting a plant or felling a natural tree → one sample of the species (`BioOnBlockBroken`);
  - mining a natural ore, crystal, salt or sulfur block → a **mineral sample** of the deposit: always for the first
    block, afterwards for one cell in eight, decided by a hash of the cell. Only cells the world made count
    (`naturalDeposit` — a block a player placed yields nothing, so placing and re-mining is no sample farm);
  - defeating an animal → one sample with its loot (`BioOnCreatureDefeated`);
  - a companion's regular gift → one sample (`BioOnCompanionGift`);
  - the **sampler** gadget → one sample from a living animal without harm: 6 blocks reach (48 for a giant), a
    hostile animal must be in stasis, the same animal gives one every 5 minutes (`UseBioSampler`).
- A full case never holds a harvest up: the sample is simply not taken, VEGA says so once.

## 6. The species register and the research book

- **Register** (`bio:register`, one per save): `BioSpeciesEntry` per seed — kind, coined name, origin body, the
  `BioContext`, and what breeding needs: the `CreatureSpecies` snapshot of an animal (the same snapshot a companion
  carries), the `FloraGenome` of a plant, the material item of a deposit, the parents and generation of a cross.
  Written 20 s after a change at the latest and with every save; capped at 512 entries.
- **Research** (`bio:research:<playerId>`, one per player): analysed seeds, tried mix signatures, changed items.
  Capped at 512 each. Written on change only.
- Both live in the new generic **`named_blob (key, json)`** table (`IWorldRepository.SaveNamedBlob` /
  `LoadNamedBlob`; SQLite, PostgreSQL and the in-memory repository). They are deliberately **not** part of the
  player record, which is rewritten on every autosave.
- **To the client:** `BioBook` — full on join, then deltas. A species is sent when the player holds a sample of it
  or has analysed it (plus the parents of a cross, so the client can derive the child's profile). An entry the
  player has not analysed arrives *without* its context: the client can name it but not read it.

## 7. Profiles

`BioProfiles.Derive(seed, context)`:

1. **Effect** — every one of the 19 effects has weight 1; each `BioTag` of the context adds a bonus from
   `BioRules.Affinities` (lava → heat ward +24, many legs → grip +8, water → breath +10 …). A strong context decides
   the effect in about half of all cases; the rest stays a surprise.
2. **Rarity** — `RarityOfPoints(context.RarityPoints)` (≥3 uncommon, ≥5 rare, ≥7 epic, ≥9 legendary), moved one tier
   down (15 %) or up (15 %) by the seed.
3. **Level** — drawn inside the tier's band (1–3, 3–6, 6–9, 9–12, 12–15).
4. **Side effect** — chance 20 % + 10 % per tier; one of the two "opponents" of the effect; level 1–3. Never damage.
5. **Thermal behaviour**, **substance group** (plants 0–4, animals 3–7), **base duration** (60/120/240/480 s),
   **toxicity** and **carrier** (from the context), and a coined **substance name**.

What an effect gives at a level is `BioRules.Magnitude` = cap × curve; the curve flattens, so level 15 is about
twice level 5. Caps (`BioRules.Cap`): speed +25 %, jump +40 %, mining +30 %, melee +30 %, cooldown −25 %, oxygen
and hunger drain −40 %, wards 0.40, grip 0.30, shield 30 points, regeneration 1.5 HP/s, perception 48 blocks.

**Materials** (`MaterialProfiles`): the fixed traits come from data (`labTraits` in `data/items.json`, e.g.
copper `{ "conductive": 3 }`) and are the same everywhere. A deposit adds its **origin values**: a purity 1–5, one
fixed trait a level up or down (60 %), and a trace trait at level 1 (one deposit in three, biased by the world:
heat-proof on hot worlds, cold-proof on cold ones, unstable on toxic ones). The origin never changes what a
material *is*, only how much of it — and it acts **in the lab only**: ordinary crafting recipes do not look at it.
Material without an origin (an ingot, an alloy, forge-made ore) acts with exactly its fixed traits at purity 3.

## 8. The lab (`BioLabIntent` → `BioLabResult`)

The player must stand at a placed **bio lab** block (`NearStationBlock`). Actions:

| Action | Needs | Costs | Gives |
|---|---|---|---|
| `Analyse` (0) | blueprint `bio_lab` (the block) | one sample | the species' context in the book; knowledge points once (2 + 2 per rarity tier, or + purity above 3 for a deposit) |
| `Mix` (1) | a plain extract: nothing more; carrier / stabiliser / modifier: blueprint `bio_synthesis` | every input, **also when the mix fails** | one preparation, or nothing |
| `Change` (2) | blueprint `bio_tuning` | the material (+ the coating) | the item, changed in place in its slot |
| `WashOff` (3) | — | — | the plain item back |
| `Seedling` (4) | — | one plant sample | one seedling |

In a creative world everything is unlocked and free.

**Mixing** (`Synthesis.Compute`): active sample + carrier + stabiliser + modifier.

- The **carrier** item decides the **form** (`labCarrier` in `data/items.json`): water → injector (full strength,
  half as long), plant fibre / polymer → gel (a third of the strength, four times as long), berries / grain / fruit
  → bar (half, twice as long, also feeds), salt → capsule (two thirds), oil / lubricant → **coating** (not taken —
  it goes onto a tool or gear). No carrier = a plain extract in injector form.
- The **modifier** is a second sample; the two substance groups react by the fixed 8×8 table
  (`BioRules.Reaction`): **amplify** (+2 levels), **inhibit** (−2), **couple** (the modifier's effect joins at half
  strength as a second effect), **transmute** (the effect turns into its partner, e.g. heat ward ↔ cold ward), or
  nothing. The table is the same in every save — a rule learned once holds everywhere.
- The **stabiliser** is a mineral sample or a plain material: it raises stability (8 + 4 × purity − 10 × unstable),
  and when it carries the trait that counters the side effect (`BioRules.Counter`) it removes the catch for about a
  quarter of the strength. A heat-proof stabiliser steadies a heat-sensitive substance, a cold-proof one a
  cold-sensitive one.
- **Stability** starts at 50; a matching form adds 8, toxicity costs 12 per level. Below 30 the mix **fails**
  (inputs gone, the book remembers the attempt); above 70 the side effect is one level weaker.
- A **toxic** sample is washed as part of the mix when a detoxifier stands by and one carbon is at hand.
- The same inputs always give the same result. The book stores the *signature* of every attempt
  (`Synthesis.Signature`); the client shows the result of a known signature before mixing and "reaction unknown"
  for a new one.

The compound rides in the preparation's key (`prep_injector#x<10 hex>`): effect, level, side effect + level,
thermal behaviour, duration (in 15 s units), second effect + level. Equal results stack.

## 9. Status effects

- `PlayerState.Effects` (`ActiveEffect`: effect, level, seconds left, side effect, thermal) and `PlayerState.Shield`;
  both persisted in the player snapshot and sent in `PlayerStateUpdate.Effects` / `.Shield`.
- Taking a preparation (`ConsumeItem` → `TryTakePreparation`): at most **three** effects; the same effect again
  refreshes it when the new one is at least as strong, a weaker one is refused and not used up.
- **Where an effect acts** — always inside the formula the gear already uses, under that formula's cap:
  armour, thermal insulation, corrosion resistance, fall protection and oxygen in `GameServerEquipment.cs`; mining
  power in `BreakBlockCore`; melee damage and cooldown in `GameServerEnemies.cs`; oxygen drain, hunger drain and
  natural healing in the survival tick; regeneration and suit energy in `TickBioEffects`.
- The **shield** is a cushion of up to 30 points that takes damage before health does (`AbsorbWithShield` in
  `Mitigate`); it goes when its effect ends.
- **Stealth** makes hostiles ignore the player (`PlayerState.IgnoredByHostiles`) and is the one effect capped at a
  quarter of the duration, 60 s at most.
- A heat-sensitive effect runs out twice as fast above 40 °C, a cold-sensitive one below −5 °C (`PlayerEffects.Tick`).
- **Client side** (movement is the client's): walking speed, jump height and climbing grip read
  `BioClientState.MoveFactor` / `JumpFactor` / `GripBonus`; night sight and perception are drawn by `BioSenses`.

## 10. Changed tools and gear

`ItemModRules.Compute(tool, hasCooldown, hasRange, usesEnergy, material, coating)` → `ItemMods`: two gains and one
drawback, each a stat with a level 1–3, packed into six hex digits behind the key tag `u`.

- The material's strongest usable trait decides **which** value changes (`ToolStat` / `GearStat`: hard → power or
  armour, conductive → energy or oxygen, magnetic → range or grip …); its level on this world, the deposit's purity
  and the coating decide **how much**.
- The coating's effect may add a second gain (`CoatingStat`).
- Anything beyond a small change (total ≥ 3 levels) costs a **drawback**: a tool gets a longer cooldown, a higher
  energy use, less range or less power; gear gets heavier (a slow-down capped at 15 %) — unless the material is
  light.
- **Never touched:** the tool tier (the progression gate), the mining radius and ignition. There is no wear; a
  change is overwritten by the next one or washed off.
- Read path: `ToolMods.Effective(def, itemKey)` (cached per key) is used wherever the server knows the item key
  (`ActiveTool`); `GearMods.Bonus(wornKeys, stat)` joins the suit formulas. Worn checks are base-aware (`Wears`),
  and `MaterialPool` accepts a changed item where a recipe asks for the plain one (an upgrade recipe takes a
  changed drill).
- Changeable: drills, weapons and worn pieces with a suit stat (`BioChangeable`).

## 11. Cloning, crossing, bred plants

**Clone tank** (see [CRYSTAL_NET.md](CRYSTAL_NET.md) §7): its species list now also offers the animal samples in
the owner's sample case (`AppendSampleChoices`, choice string `g:<seed hex>`).

- A species that is not native to the current world is registered as a **guest** (`GuestSpeciesOf`): the snapshot
  from the register, id `gx<seed hex>`, at home in no biome, group size 1. It then moves, renders, scans and tames
  like any other. So an animal can be grown on another world; a water or lava animal needs water or lava within 8
  blocks of the tank.
- Never grown: hostile species and giants.
- **Cost:** one sample per parent and matter dust (2 for a clone, 4 for a cross). Caps: 6 living clones per owner
  (unchanged) and 16 per world (`CrystalNetRules.MaxLivingClonesPerWorld`).
- **Crossing** (blueprint `bio_crossing`): the tank's second choice (`x=` in its config) names the partner. Two
  animals or two plants, never a giant, at most generation 3. `EnsureCross` creates the register entry once;
  `CrossCreatures` builds the body: body plan, habitat, limbs and yield from one parent (so a cross is always a
  body the client can build and the server can move), colours and ornaments from the other, size and speed mixed,
  one pair in eight shows a trait neither parent has. A cross is **never hostile** and inherits no special
  behaviour. Authored (school-club) species cross like any other.
- The cross's substance profile is `BioProfiles.Cross`: the effect of one parent, the stronger parent's level − 1,
  + 1 for parents of different worlds, + 1 for different substance groups; a tier above both parents needs both to
  be at least rare and from different worlds. Toxicity is the lower of the two.
- When the tank is done it releases the animal and hands the owner a sample of the new species; two plant samples
  for a plant cross. From then on the tank clones what it made.

**Bred plants.** A plant cross has a `FloraGenome` (body block, crown block, layout, size, tint, glow, toxic).
The lab turns a plant sample into a **seedling** (`seedling#x<seed>`), which places the single block
**`flora_hybrid`**:

- the voxel's **tint** carries the colour, its **glow channel** carries the packed form (`FloraForm.Pack`, bit 23
  marks a form) — the client mesher reads both and builds the plant from its parents' shapes; `ClientWorld` treats
  the glow channel of this block as a form, not as a light, and adds the plant's own light when it has one;
- which species stands in a cell is remembered per world (`bio:plants:<locationId>`, keyed by the canonical cell,
  capped at 256 per world), so a harvest yields the right sample and the regrowth (90 s) puts the same plant back;
- it may be planted (`BredPlantRefusal`) on any natural plant ground that is not tainted, on a **hydro tray** or in
  a **flower pot**, wherever the air is breathable or the world's atmosphere is not corrosive — on any world, in a
  base, on a station;
- a harvest yields what the body parent's form yields (the poisonous twin for a toxic cross) plus one sample.

## 12. Messages

| Message | Direction | Content |
|---|---|---|
| `BioLabIntent` (283) | client → server | `Action`, `Sample`, `SampleMineral`, `Carrier`, `Stabiliser`, `MaterialItem`, `Modifier`, `TargetItem`, `CoatingItem` |
| `BioLabResult` (284) | server → client | `Action`, `Success`, `MessageKey`, `ItemKey`, `Stability`, `Failed`, `Knowledge` |
| `BioBook` (282) | server → client | `Full`, `Species[]` (`NetBioSpecies`), `Reactions[]`, `Changes[]` |
| `InventoryUpdate` | server → client | + `Samples`, `SamplesUnchanged` |
| `PlayerStateUpdate` | server → client | + `Effects` (`NetEffect[]`), `Shield` |

All additions are additive under contractless MessagePack; an older client ignores them.

## 13. Data

- `data/items.json`: `labTraits` on 47 materials, `labCarrier` on the carrier items; the items `bio_lab`,
  `bio_sample`, `mineral_sample`, `seedling`, `bio_sampler`, `prep_injector|gel|bar|capsule|coating`.
- `data/blocks.json`: `bio_lab` (machine), `flora_hybrid` (flora).
- `data/recipes.json`: `bio_lab`, `bio_sampler` (workshop).
- `data/blueprints.json`: `bio_lab` → `bio_synthesis` → `bio_tuning`; `bio_crossing` (needs `bio_lab` and
  `clone_tank`).
- Locale keys: `bio.*`, `ui.bio.*`, `srv.bio.*`, `srv.crystal.clone_*` / `cross_*`, `vega.hint.*`.

Adding a material to the lab is a data change (`labTraits`); adding a carrier too (`labCarrier`). A new effect is
an append to `BioEffect` plus its rows in `BioRules` (cap, affinities, opponents, transmutation partner) and its
locale keys — and the place in the server where it acts.

## 14. Performance and limits

| Limit | Value | Where |
|---|---|---|
| strength levels | 1–15 | `BioRules.MaxLevel` |
| effects at once | 3 | `BioRules.MaxActiveEffects` |
| sample case | 24 slots × 20 | `BioRules.SampleCaseSlots`, `SampleStack` |
| register entries per save | 512 | `BioRules.MaxRegisterEntries` |
| research entries per player | 512 each | `BioRules.MaxResearchEntries` |
| bred plants per world | 256 | `BioRules.MaxBredPlantsPerWorld` |
| cross generations | 3 | `BioRules.MaxCrossGeneration` |
| living clones per world | 16 | `CrystalNetRules.MaxLivingClonesPerWorld` |

- The per-tick cost is one pass over at most three effects per player. Profiles are derived on demand (a handful of
  integer hashes); changed tool values are cached per key.
- Nothing is written on a tick: register and research are written on change, bred plants on plant / final removal.
- The sample case is sent only when it changed; the book is sent as deltas.
- The world generator is untouched: no chunk is generated differently, no generation bump.

## 15. Tests

- `tests/BlocksBeyondTheStars.Tests/BioRulesTests.cs` — the pure rules: determinism, the curve and caps, rarity
  bands, the reaction table's symmetry, key payload round trips, synthesis, material profiles, item changes,
  crosses.
- `tests/BlocksBeyondTheStars.Tests/BioLabTests.cs` — against a real `GameServer`: sampling, the natural-deposit
  rule, analysis, mixing and failing, effects in the gear formulas, the shield, changed tools, the clone tank on
  samples and on another world, crossing, seedlings and regrowth, persistence across a restart.
- `tests/BlocksBeyondTheStars.Client.Tests/BredPlantLightTests.cs` — the client treats a bred plant's glow channel
  as a form.
- `NetCodecTests` — the three new messages in the golden list.

## 16. Client notes

- **`BioLabUi`** — a modal panel (sample case on the left; pages Analyse, Mix, Change). Previews are computed locally
  with the same `Shared` code (`Synthesis.Compute`, `ItemModRules.Compute`). A mix preview is shown only for a
  signature the book already holds (or for a plain extract of an analysed species) — anything else reads "reaction
  unknown"; the Change preview is always shown. The panel opens on
  Interact at a `bio_lab` block within the server's reach. It also holds the static text helpers the inventory, the
  HUD and the Codex reuse.
- **HUD** — pooled effect rows inside the vitals panel (label, time, side effect); the shield as a suffix of the
  health row. Rows are relabelled only when what they show changed.
- **Inventory** — a "samples" category for the sample case; a preparation's card shows its effect and a **Take**
  button; a changed item shows its gains and its drawback. `IconResolver` looks the lab's items up by base key and
  tints a preparation by its effect.
- **Movement** — `PlayerController` multiplies the walk vector by `BioClientState.MoveFactor(gearWeight)`, the jump
  by `sqrt(JumpFactor)`, adds the grip bonus under the climbing cap, and reads tool values through
  `ToolMods.Effective`.
- **Bred plants** (`ChunkMesher`) — the block `flora_hybrid` is built from its packed form: body and crown take the
  tiles of the parent blocks, in the layouts plain / tiered / star / fan, at the form's size; a potted one is drawn
  smaller and replaces the pot's own flower. The colour is applied in the shader's dye mode (not the flora-tint
  mode, which is switched off on stations and in space). `ClientWorld.SetFormBlocks` tells the light scan that this
  block's glow channel is a form (`ClientWorld.FormLight` gives the light of a glowing one).
- **Senses** (`BioSenses`, created on first use by `BioSenses.Ensure`) — night sight raises the shader globals
  `_Sc_Light` and `_Sc_Indoor` to a floor (two global writes per frame, no extra pass); perception marks living
  things through terrain with the thermal-vision overlay (drones are left out, micro-fauna is included).
- **Sounds** — six bundled clips (see [SOUND_DESIGN.md](SOUND_DESIGN.md) §14), each with a synthesised stand-in in
  `ProceduralAudio`.
