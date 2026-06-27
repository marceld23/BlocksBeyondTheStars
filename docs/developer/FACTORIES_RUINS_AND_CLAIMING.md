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
StampSettlement → StampRuins → StampFactories → StampWreck → StampVaults → StampDataCubes
                                                                          → StampNetFragments → StampChests
```

| Feature | Stamper | Flag | Rarity (per body) |
|---|---|---|---|
| Ruins | `StampRuins` (`GameServerRuins.cs`) | `PlaceRuins` | ~0–2, mostly none; skipped on airless worlds |
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
`machine_block` housing plus a `factory_pipe` stack. A `factory_terminal` block sits by the door. Size,
depth, machine count and machine archetypes are seeded per instance, so no two look alike. Stamped with
the shared `StampSettlementBlocks` (terrain carve + stepped foundation + block stamp).

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
the static housings:

- **press** — a piston head that hammers up/down (`localPosition` lerp),
- **rotor** — a 4-spoke wheel that spins (`localRotation`),
- **conveyor** — parts scrolling along a band,

each with a pulsing status light. Motion is procedural and continuous (ambient-running), camera-proximity
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

---

## 4. Treasure chests

`StampChests` scatters 0–2 standalone lootable caches per body, away from spawn and clear of settlements.
A chest reuses the structure-loot container flow (`SpawnStructureLoot("chest", ...)` →
`StoredContainer`), so it is spawned once, recorded in `WorldMetadata.GeneratedLoot`, and never re-spawns
after being looted. Chest loot is richer than generic salvage and is the rare world source of an **access
code** (~14 % per chest).

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
| Config flags | `Shared/Configuration/ServerConfig.cs` (`PlaceFactories/PlaceRuins/PlaceChests`) |
| Factory gen | `WorldGeneration/FactoryGenerator.cs` |
| Ruin decay | `WorldGeneration/SettlementGenerator.cs` (ruined branch) |
| Stampers | `GameServer/GameServerFactories.cs`, `GameServerRuins.cs`, `GameServerChests.cs` |
| Tracking | `GameServer/WorldManager.cs` (`FactoryInstance`, `RuinsStamped`) |
| Crafting/protection | `GameServer/GameServer.cs` (`HandleCraft` roster gate, mine/place `IsFactoryProtected`, `Disassemble` exclusion, `StationAvailable`) |
| Claiming | `GameServer/GameServerFactories.cs` (`ClaimFactory`), `Shared/State/WorldMetadata.cs` (`StructureClaim`) |
| Networking | `Networking/Messages/FactoryMessages.cs`, `Networking/NetCodec.cs` (tags 172/173) |
| Client | `client/.../FactoryView.cs`, `WorldRig.cs`, `PlayerController.cs` (E-claim), `CraftingTechShipUI.cs` (factory station), `GameBootstrap.cs`, `NetworkClient.cs` |
| Data | `data/blocks.json` (`factory_terminal`, `machine_block`, `factory_pipe`), `data/items.json` (`access_code`), `data/recipes.json` (factory recipes + `market_buy_access_code`), `data/locales/{en,de}.json` |
| Tests | `tests/.../FactoryStructureTests.cs`, `FactoryClaimTests.cs`, `FactoryCraftingTests.cs`, `RuinsAndChestsTests.cs` |
