# Factories, Ruins & Structure Claiming

> Status: **Implemented.** Rare procedural **factories** (industrial halls with animated machines and a
> roster-limited production terminal), randomised **ruins** of fallen settlements, standalone **treasure
> chests**, and an **access-code claiming** system that turns a spawned factory into an editable player
> base. All four are deterministic from the world seed (only the claim is a persisted player delta).

This is the *how it fits together* reference. For the surrounding world-gen pipeline see
[WORLD_GENERATION.md](WORLD_GENERATION.md); for the protection/ownership model it builds on see
[STATION_AS_LOCATION.md](STATION_AS_LOCATION.md); for the crafting system see
[CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md).

---

## 1. Where it plugs into world generation

All surface structures are stamped from `GameServer.LoadWorld` (`GameServer.cs`), each behind a
`ServerConfig.Place*` flag. The new stampers join the existing chain:

```
StampSettlement → StampRuins → StampBanditCamps → StampMonuments → StampFactories → StampWreck
                → StampVaults → StampDataCubes → StampNetFragments → StampChests
```

| Feature | Stamper | Flag | Rarity (per body) |
|---|---|---|---|
| Ruins | `StampRuins` (`GameServerRuins.cs`) | `PlaceRuins` | ~0–2, mostly none; skipped on airless worlds |
| Monuments | `StampMonuments` (`GameServerMonuments.cs`) | `PlaceMonuments` | 0–3, one per archetype; airless worlds **included** |
| Factories | `StampFactories` (`GameServerFactories.cs`) | `PlaceFactories` | ~0–2, mostly none; skipped on airless worlds |
| Chests | `StampChests` (`GameServerChests.cs`) | `PlaceChests` | ~0–2, mostly none |

Every count/position is a hash of `seed ^ StableHash("<kind>:" + locationId)`, so adding any one of them
leaves the rest of the universe unchanged. Placement reuses the settlement allocator
(`TryPlaceSettlement` + the reserved-footprint list) so structures never overlap pads, wrecks or each
other.

---

## 2. Factories

A factory is an industrial hall (`FactoryGenerator.cs`, built on the generic `SettlementStructure`
voxel+marker container): metal walls + glass windows, a door, and **one or more machine bays**, each a
3×3×(4–5) `machine_block` housing sculpted from stock blocks — a dark `engine_panel` plinth course, glass
inspection windows in the side faces, an amber `cargo_floor` work strip on the floor in front, a
`factory_pipe` on the roof (the machine anchor) plus exhaust pipes on the back corners rising into the
ceiling (#1053). A `factory_terminal` block sits by the door. Size, depth, machine count and machine
archetypes are seeded per instance, so no two look alike. Stamped with the shared `StampSettlementBlocks`
(terrain carve + stepped foundation + block stamp). All three factory blocks have bundled tiles (#1050).

### 2.1 The production roster — "never everything"

Each factory instance seed-picks a **roster**: 1–4 of the factory recipes (`FactoryInstance.Roster`),
re-derived from the instance seed every session. A factory with roster size 1 makes a single thing; a
richer one makes several — never the whole factory-recipe set. The machine count tracks the roster size.

Factory recipes live in `data/recipes.json` with `"station": "factory"`. The twist: a factory recipe
turns **cheaper, less-rare raw materials into the same output as a base recipe, but consumes more of
them** in a single step (e.g. `factory_steel`: `6 iron_ore + 3 nickel_ore + 2 carbon → 1 steel`, versus
the base chain that refines ingots first). They are deliberately **excluded from disassembly**
(`GameServer.Disassemble`) so a cheap-bulk craft can't be reversed for a surplus.

### 2.2 Operating a terminal

`StationAvailable(Factory)` requires the player to stand within reach of a `factory_terminal` block
(off-ship only — factories are world structures, never a ship module). `HandleCraft` then checks the
**roster of the specific factory** the terminal belongs to (`FactoryTerminalNear`): a recipe not on
*this* factory's roster is refused. The client crafting menu mirrors the gate (`CanCraft` →
`FactoryView.PlayerAtTerminal`), but the server is authoritative. Operating a terminal is **public** —
claiming is a separate mechanic and is *not* required to produce.

### 2.3 Animated machines (client)

The server sends a `FactoryList` (`FactoryMessages.cs`, NetCodec tag 172) on world entry: each
`NetFactory` carries the terminal position, roster, claim state and a `NetMachine[]` (archetype +
anchor). `FactoryView.cs` (modelled on `DoorView`/`StationDecorView`) overlays animated GameObjects on
the static housings. The anchor is the **centre of the roof-top pipe block**; the geometry hangs on the
housing's **front (−Z) face** towards the hall door, in local units (housing top = y −0.5, front face =
z −1.5; the housing is ≥ 4 tall so y −0.5 … −4.5 is always real housing — #1052 moved it there, the parts
used to sit inside the pipe block and were invisible):

- **press** — a piston cylinder, a wide head with rod hammering down onto an anvil (`localPosition`),
- **rotor** — a 2.4 m vertical flywheel (axle, four spokes, eight-segment rim) spinning about Z (`localRotation`),
- **conveyor** — a belt bed with two turning drive rollers and parts scrolling along it,

each on two mounting rails with a pulsing status light top-right. Motion is procedural and continuous (ambient-running), camera-proximity
gated for frame time. Materials use the project `LitColor` shader (never Standard — it strips in player
builds). `FactoryView` is attached in `WorldRig.cs` alongside the other entity views.

**Audio.** A positional machine hum (`factory_hum`, ElevenLabs-generated) loops while the player stands near
a running factory (`FactoryView.PlayWorkingHum`), and each factory craft plays a heavy press-stamp clunk
(`factory_craft`) in `CraftingTechShipUI.OnCraftResult` — so the machines actually sound like they work.

---

## 3. Ruins

`StampRuins` places 0–2 fallen-city ruins per (non-airless) world. A ruin is a town/city run through the
heavy decay pass in `SettlementGenerator.Generate(..., ruined: true)`: **height-graded collapse** (ground
walls mostly survive, roofs almost all gone), **one spared building** left half-standing as a tower, and
**rubble + flora overgrowth** reclaiming the ground. Every ruin differs (spared plot + thresholds are
seeded).

Unlike settlements/stations/factories, ruins are **not protected and not tracked as structures** — they
are just terrain (freely mineable) plus the occasional scavenge cache. Because they're mineable they are
stamped **once** (guarded by `LoadedWorld.RuinsStamped`) and then live on as persisted block edits, so a
reload never resurrects blocks the player cleared.

### 3.1 Monuments (#522–#527)

Ruins are *statistically* eroded architecture, so they can never produce a deliberate silhouette.
`StampMonuments` (`GameServer/GameServerMonuments.cs`) adds the authored counterpart: 0–3 relics per body,
one per archetype (`arcade`, `gate`, `circle`, `obelisk`, `altar`) from `WorldGeneration/MonumentGenerator.cs`.
Each is built intact, then run through an erosion pass plus a **settle** pass (a stone with nothing under
it, nothing corbelled under its shoulder and nothing beside it falls) so what survives still reads as the
thing it was. It is the first procedural generator to emit per-cell **shapes and glow** — arches are
`Ramp`/`Cylinder`/`Slab` forms through the existing `SettlementStructure` modifiers, not new geometry.

Differences from every other surface feature:

- **Airless bodies are allowed** (the raisers are long gone); only void worlds and the finale body are skipped.
- **No foundation plate.** `StampMonumentBlocks` clears and plinths *only the columns that carry stone*, so a
  circle stands in the landscape rather than on a plaza.
- **Placement is decided once and persisted** as `StampedFeatures` entries
  (`monument@<index>:<archetype>:<x>:<y>:<z>`) and only replayed afterwards. It cannot be re-rolled per load
  like a settlement's, because the placement gate skips footprints players have built in (#527,
  `FootprintHasPlayerEdits` → `IWorldRepository.HasPlayerBlockEdits`) — a re-roll after somebody mines a rune
  would move the instance off its own stones.
- **Runes are the scan subject.** Scanning a `rune_stone` while standing at a monument
  (`GameServerScanning.MonumentForScan` → `MonumentNear`, resolved from the server-authoritative player
  position, so no protocol change) awards `KnowledgeMonument` = 8 under the ledger key
  `monument:<locationId>:<archetype>` — once per body per archetype. Away from a monument the same block is
  an ordinary 1-point material scan.

Ruined settlements additionally get a **broken** central feature (`StampBrokenFeature`, #525): snapped
column stumps, an arch springer jutting into nothing, toppled rune stones.

---

## 4. Treasure chests

`StampChests` scatters 0–2 standalone lootable caches per body, away from spawn and clear of settlements.
A chest reuses the structure-loot container flow (`SpawnStructureLoot("chest", ...)` →
`StoredContainer`), so it is spawned once, recorded in `WorldMetadata.GeneratedLoot`, and never re-spawns
after being looted. Chest loot is richer than generic salvage and is the rare world source of an **access
code** (~14 % per chest).

### 4.1 NPC hints — how wrecks and chests get onto the map

Wrecks and chests are deliberately **not** in the `PlanetPoiList` a joining player receives — they stay
hidden until a settlement NPC shares them. When a player greets a vendor/quartermaster
(`EmitGreeting`), `TryEmitHint` (`GameServerNpcHints.cs`) rolls a chance (35 %) to replace the greeting
with a **location hint**: the NPC speaks a deterministic localized line with a rough distance + 8-way
direction (`npc.hint.wreck` / `npc.hint.treasure`, wrap-aware via `WrapDeltaX/Z`), and the target is
added to the POI list for **everyone on the world** (`BroadcastPlanetPois`). The wreck is shared with any
visitor; chest hints are reserved for players at relationship tier `known`+ (item 14 memory). Reveals
persist in `WorldMetadata.RevealedPois` — keys carry the location id (`"{locationId}|wreck"`,
`"{locationId}|chest:{x}:{y}:{z}"`) because coordinates repeat across a save's worlds. A claimed wreck
and a looted chest (container despawned) drop out of the list automatically. Hint lines intentionally
bypass the LLM greeting path: that cache is shared per relationship tier, so a cached line would replay
one player's coordinates to another player standing somewhere else.

---

## 5. Access codes & claiming

An **access code** (`access_code`, localised "SPS-Code" — a recovered *Scout & Pioneer Service* control
code) is a rare currency-like item, obtained two ways, both rare:

- **World find:** as a rare drop inside treasure chests (§4).
- **Trader purchase:** a steep `"traders"`-theme market recipe (`market_buy_access_code`).

Spawned factories are protected (read-only) by `IsFactoryProtected` in the mine/place guards. A factory is
**claimable**; standing at its terminal with a code and pressing E (`ClaimStructureIntent`, tag 173 →
`GameServer.ClaimFactory`) spends one code and claims it:

- the factory's `OwnerId` is set, and a `StructureClaim` (stable origin-derived key → owner) is recorded
  in `WorldMetadata.Claims` and persisted (`SaveMetadata`);
- `IsFactoryProtected` now defers to the claim — the **owner and their allies** (`AreAllied`) may rebuild
  it freely, everyone else stays read-only (mirroring the `IsBaseProtected` owner/ally model);
- on reload the factory re-derives from the seed and the persisted claim re-applies (matched by key).

One code claims one structure. The claim makes the factory the owner's editable base.

### Scope note

Claiming is implemented end-to-end for **factories**. The same `StructureClaim` model and owner/ally
protection pattern are designed to extend to spawned stations; ruins are already freely editable terrain
so they need no claim. Per-craft machine speed-up, a dedicated factory terminal screen, and station/ruin
claiming are the natural follow-ups.

---

## 6. Key files

| Area | Files |
|---|---|
| Config flags | `Shared/Configuration/ServerConfig.cs` (`PlaceFactories/PlaceRuins/PlaceChests/PlaceMonuments`) |
| Factory gen | `WorldGeneration/FactoryGenerator.cs` |
| Ruin decay | `WorldGeneration/SettlementGenerator.cs` (ruined branch + `StampBrokenFeature`) |
| Monuments | `WorldGeneration/MonumentGenerator.cs`, `GameServer/GameServerMonuments.cs`, `GameServer/GameServerScanning.cs` (rune scan) |
| Stampers | `GameServer/GameServerFactories.cs`, `GameServerRuins.cs`, `GameServerChests.cs` |
| Tracking | `GameServer/WorldManager.cs` (`FactoryInstance`, `RuinsStamped`, `MonumentInstance`) |
| Crafting/protection | `GameServer/GameServer.cs` (`HandleCraft` roster gate, mine/place `IsFactoryProtected`, `Disassemble` exclusion, `StationAvailable`) |
| Claiming | `GameServer/GameServerFactories.cs` (`ClaimFactory`), `Shared/State/WorldMetadata.cs` (`StructureClaim`) |
| NPC hints | `GameServer/GameServerNpcHints.cs` (`TryEmitHint`), `GameServerSettlements.cs` (`BuildPlanetPois`), `Shared/State/WorldMetadata.cs` (`RevealedPois`), `client/.../WorldMap.cs` (`PoiLook` `treasure`) |
| Networking | `Networking/Messages/FactoryMessages.cs`, `Networking/NetCodec.cs` (tags 172/173) |
| Client | `client/.../FactoryView.cs`, `WorldRig.cs`, `PlayerController.cs` (E-claim), `CraftingTechShipUI.cs` (factory station), `GameBootstrap.cs`, `NetworkClient.cs` |
| Data | `data/blocks.json` (`factory_terminal`, `machine_block`, `factory_pipe`), `data/items.json` (`access_code`), `data/recipes.json` (factory recipes + `market_buy_access_code`), `data/locales/{en,de}.json` |
| Tests | `tests/.../FactoryStructureTests.cs`, `FactoryClaimTests.cs`, `FactoryCraftingTests.cs`, `RuinsAndChestsTests.cs`, `MonumentTests.cs`, `NpcHintTests.cs` |
