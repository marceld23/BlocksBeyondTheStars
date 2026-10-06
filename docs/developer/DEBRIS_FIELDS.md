# Debris fields, combat debris, one-time salvage, raiders with real hulls

Epic #2351 (sub-issues #2352–#2359), branch `feat/debris-fields`, 2026-10-06. Status: implemented (see
[../../TODO.md](../../TODO.md)). No terrain-generation bump: placement runs after the galaxy generator with its own
salt, so existing galaxies keep every system, body, lane and wormhole. No protocol bump: the new entity kinds are
strings on the wire and every new field is contractless-additive (`NetCombatEntity.Yaw`, `NetBody.Salvaged`,
`SpaceWarpFx.Raider`). No save bump: the new `WorldMetadata.SpaceSalvage` dictionary is an additive field and the
package adds no blocks.

## 1. Goal

Space had raiders and one salvageable wreck per system, but no place that was *about* salvage — and the wreck rebuilt
itself with full loot on every flight. This package adds **debris fields** as a star-map location with loot, makes
salvage pay **once per galaxy**, lets destroyed ships leave **combat debris**, and gives raiders **real voxel hulls**
from the existing ship designs. Decisions (maintainer, 2026-10-06): a location of its own on the chart; capsule loot
and the wreck's payout once per galaxy; themes include a freighter accident, a shipyard scrapyard, a broken satellite,
alien debris and an old battle; drifting debris taps the **shield only**; destroyed drones, saucers, cruisers and
raiders leave fragments; raiders keep exploding (a ship coming apart, never a person — see
[../user/PARENTS.md](../user/PARENTS.md)).

## 2. Data — `data/space_salvage.json` (#2352)

`SpaceSalvageDefinition` (Shared) — optional like `wormholes.json`; a data folder without it gets defaults that
match what shipped before. Validated in `GameContent.Validate()`: every loot item exists, chances are in 0..1,
ranges are sane, every theme's blocks exist.

| Section | What it holds |
|---|---|
| `fields` | fragment / capsule count ranges, the field radius (28), the approach-read range (45), the shield tap (3 points every ~7 s, min speed 0.5), `archetypeOdds` (Belt / Desolate 1.5, PirateHaven 2.0, Hub 0.5) |
| `themes` | `freighter`, `shipyard`, `satellite`, `alien`, `battle`: a weight (+ per-archetype multipliers), the fragment palette (`hull`, `accent`, `scorch`, `scorchShare`), `fragmentLoot` and `capsuleLoot` rows (`item`, `min`, `max`, `chance`), the lore site key |
| `combatDebris` | fragments per destroyed hostile (2–3), the per-instance cap (12), the scrap table |
| `wreck` | the space wreck's payout as `base` + `cells / perCells` (+ `extraMax`, `chance`) rows for human and alien hulls — the numbers that used to be hard-coded in `SpaceWreckSalvage` |

World option `WorldDescription.DebrisFields` (`Frequency`, default *Normal*): CLI key `debris-fields`, client
`WorldCreationOptions.DebrisFields`, a slider on the **advanced** page of the world options (the main page grid is
full — `WorldOptionsLayoutTests` pins it). A save from before the feature reads the default and gains its fields,
exactly like the wormholes did.

## 3. Placement (#2353) — `DebrisFieldPlacer`

The template is `WormholePlacer`. `GameServer.BuildGalaxy` runs `DebrisFieldPlacer.Place(systems, seed, frequency,
fixedCount, settings, archetypeOf)` right **after** the wormhole placer:

- Only the fixed `sys0 … sys(fixedCount-1)` systems are candidates; the finale and any hand-made system never are,
  and a growing galaxy never gains or moves a field.
- Odds = `Frequency.Probability()` × the system archetype's multiplier, rolled with the salt `debrisfield:`. At most
  one field per system, as a `CelestialKind.DebrisField` body with id `<sys>-d`, a coined ship name and a position
  between the orbits with `BodyClearance` (220 system units) from every planet / moon / asteroid body (twelve
  candidate angles, the roomiest wins when every angle is crowded).
- Why after the wormholes: an end's radius is measured over *all* bodies of its system. Why `SeparateFromBodies`
  skips the new kind: `EnsureStartSystemStation` runs later and treats every non-free-floater as an obstacle — a
  debris body would have moved the start station in an existing save.
- `ThemeFor(seed, bodyId, archetype, settings)` rolls the theme seed-pure, so the server derives it on demand and
  stores nothing.

`DebrisFieldTests.Placer_*` pin: seed-stable, appended never moved, at most one per system, Off = none, never in a
story system, clearance kept; `Galaxy_GainsFields_WithoutMovingAnyOtherBody` compares a galaxy with the option Off
against one with Frequent body for body.

## 4. The field in flight (#2353) — `GameServerDebrisFields`

`AddDebrisFields(instance, anchor)` in `CreateSpaceInstance`, after the wormholes. For each `DebrisField` body of
the anchor's system, at the body's flight-view position (star-map delta × `SystemBodyLayout.FlightViewScale`, a
seeded height of −12..+12):

| Entity | Kind | What it is |
|---|---|---|
| marker | `DebrisField`, id = body id | chart / radar / waypoint / scanner target; the flight recorder. Not a fire target, never hostile |
| fragments (8–12) | `Debris`, id `e<n>`, `FieldId` = body id | small voxel `SpaceStructure`s (kind `debris`, 3–10 cells from a random walk in a 3×2×3 box: hull + accent + scorch share), `Hull == cells` (min 4 — the starter laser pops one in a shot); carved by the laser and the EVA pick exactly like an asteroid, under the `AsteroidDestruction` rule; theme fragment loot. Rebuilt per instance like belt rocks |
| capsules (1–2) | `SalvageCapsule`, id `<body>-c<i>`, `FieldId` = body id | collected like a `ResourceDrop` (`IsCollectableDrop`: passive tractor range, sweep, locked pull, or flying through at 3 units without a beam); theme capsule loot, rolled seeded per capsule; **once per galaxy** |

Every roll is the asteroid family's xorshift (`NextAsteroidRand`) seeded from the body — no `Random`, no trig — so
the same field greets every pilot. The join-time design send (`EnterSpace`), `HandleStructureEdit` (`isAsteroid`),
`WeaponAllowedAgainst` and the carve / remove paths in `FireWeapon` all go through `IsCarvedKind` (asteroid, wreck,
debris).

**Approach + scanner.** `CheckSpaceWreckApproach` (called on every ship pose) reads the recorder the first time a
pilot comes within `fields.approachRange` of a marker: `ScanDebrisField` → `Award` (knowledge 6, ledger
`debris:<body>`), `TryRevealLoreText(theme.lore)` (sites `debris_<theme>` plus the `debris` fallback in
`lore_sites.json`), `MarkSpaceWreckVisited` (now accepts a debris body: charted for everyone, a Places entry, the
star map refreshed), and the VEGA tip `debris_signal` retires. The ship scanner reaches the same method
(`IsScannableSpaceObject`, `ScannedIdsIn`). The tip itself is a server-side context tip like `wreck_signal`.

## 5. Salvage pays once (#2354) — the ledger

`WorldMetadata.SpaceSalvage : Dictionary<string,int>` (additive):

- `wreck:<bodyId>` → the wreck's remaining hull cells, **0 = salvaged**. `AddSpaceWrecks` skips a salvaged wreck and
  trims a half-carved one to its stored count (`PeelOutermost`, the carve order shared with `CarveAsteroidToHull`,
  silent at creation); `HullMax` stays the original count, `Hull` the remaining cells, and the payout follows the
  whole hull. Both carve paths note the count (`NoteWreckHull`); reaching zero — by laser (`FireWeapon`) or EVA pick
  (`HandleStructureEdit`, which used to remove the wreck **without paying**) — runs `CompleteWreckSalvage` (ledger 0,
  `SaveMetadata`, `BroadcastStarMap`) and `PayEntityLoot` (the loot path factored out of `FireWeapon`: tractor →
  floating drop, else backpack + hold + leftovers as a drop + the `+n → where` toasts).
- `debris:<bodyId>` → a bitmask of collected capsules (`OnSalvageCapsuleCollected` when `StowDrop` empties one).
- Written at instance teardown (`PersistSpaceSalvageLedger` in `LeaveSpace`) and on every completion; the autosave
  writes the metadata anyway. The galaxy itself stays seed-pure (world = seed + parameters + deltas).
- `NetBody.Salvaged` tells the chart and the travel screen ("salvaged" instead of "fly there to salvage").

Before this, the wreck was rebuilt with full salvage on every flight, once per launch body of its system —
`SpaceWreckTests.SpaceWreck_IsTheSameHull_OnEveryEntry` even pinned it. The new contract is pinned by
`SpaceWreck_KeepsItsCarvedState_AcrossReentry_AndARestart_AndStaysGoneOnceSalvaged` and
`DebrisFieldTests.Capsule_PaysOnce_AcrossReentry_AndARestart`.

## 6. The shield taps (#2355)

In the per-pilot space tick, next to the asteroid ram: `TickDebrisBump` runs when the pilot's pose is inside a
marker's radius (`InsideDebrisField`), not on an EVA, moving at least `bumpMinSpeed`. Every `bumpIntervalSeconds`
(× 0.8–1.2 jitter, `PilotSim.DebrisBumpCooldown`) the shield loses `bumpShield` points, clamped at zero — the hull is
**never** touched; a shieldless ship just hears the clank. One toast per flight (`@srv.space.debris_bump`,
`PilotSim.DebrisBumpToasted`). `SendShipCombatStatus` carries the drop; the client already plays `ship_shield_hit`
and the camera jolt on any shield drop, and adds the 3-D `debris_bump` clank + a few sparks when it sees no attacker
in `SpaceTargeting.AttackRange` and the ship inside a field (`SpaceView.InDebrisFieldNow`, reading the radius from the
shared content). `DebrisFieldTests.DriftingDebris_TapsTheShield_ButNeverTheHull_AndOnlyInsideTheField`.

## 7. Combat debris (#2356)

`FireWeapon`, right after the destroyed entity is removed and **before** the raider's hull structure goes:
`SpawnCombatDebris` scatters `combatDebris` fragments (2–3) within 4 units of the kill. A raider's are
`CutFragmentFrom` its own hull (a connected 3–8 cell piece with blocks, dye and shape copied, re-centred); drones,
saucers and cruisers get the generic dark palette. Combat fragments carry an empty `FieldId`; the per-instance cap
(12) removes the oldest first. `DebrisFieldTests.DestroyedDrone_LeavesWreckageFragments`.

## 8. Raiders with real hulls (#2357) — `GameServerBanditShips`

`SpawnBanditShip` picks a design (`PickRaiderDesign`: every content ship with a layout except the starter, weighted
`600 − cells` so a scout is common and a hammerhead rare), builds it with the trader path
(`BuildNpcShipStructure(RaiderStructureId(id), key)` → structure `ship:bandit:<entityId>`, kind `ship`, ownerless),
dyes it (`ApplyRaiderLivery`: every tintable plating cell near-black `0x33313A`, the outermost cells of each upper row
rust-red `0x8C3828` — per-cell dye rides the design message, so the client needs no paint rule), scales the hull
points with the cells (`clamp(40 + cells/6, 55, 120)`), sends it to every pilot as `ship_remote` (late joiners get
every `ship` structure in `EnterSpace`) and removes it on warp-out or death. `CombatEntity.Yaw` is set from the course
by `FaceCourse` in the approach, the leave and `MoveSpaceHostiles`; `NetCombatEntity.Yaw` carries it. A destroyed
raider with an open demand reports `destroyed` (client text `ui.bandit.destroyed`) instead of `fled`. The warp-in
flash is flagged `Raider` for its sting.

`DisableShip` used to take the **first** structure of kind `ship` as the defeated pilot's own hull — with a trader
or a raider in the instance that could be the wrong one. It now prefers `ship:<current pilot>`.

Client: `SpaceView.BuildBanditShipModel(parent, e)` builds the voxel hull from `RemoteShipDesignFor("bandit:<id>")`
(`ShipMeshBuilder.BuildVoxelShip` at `FlightShipScale`, a red plume per `ExhaustPoints`), else the wedge, and
`SyncEntities` swaps the wedge for the hull when the design arrives late; the model turns to `Yaw`; the lock bracket
radius follows the design (`RaiderDesignRadius`); `SpaceFx.Explosion` flings the hull's chunk meshes.

## 9. Client (#2358)

- `OnStructureDesign` whitelist + `debris`; `Debris` entities are skipped like asteroids (the structure renders);
  marker and capsule models in `SpaceView.Debris.cs` (`DebrisBeaconView`: a slow turn, a pulsing beacon, no material
  changes).
- Radar: the marker rim-pinned in copper with the ▲/▼ height mark; fragments copper, capsules teal. Chart: the marker
  with `name · Debris field` and a click-snap waypoint. Travel screen: kind label, "fly there to salvage" / "salvaged",
  detail texts, no travel button.
- `Client.Core/SpaceTargeting`: `DebrisField` is a navigation + static + scannable kind, `Debris` a mining kind (fire
  target, `WeaponSuits` for the breaker and the starter laser, rock tier in the mining cycle), `SalvageCapsule` a
  collectable kind (`IsCollectableKind`, used by the tractor's locked pull and the passive sweep).
- Sounds: `debris_bump`, `salvage_capsule`, `raider_warp_in` (ElevenLabs via `gen_sound.py`, prompts in
  `gen_batch.py`, SOUND_DESIGN §19, NOTICES).

## 10. Texts (#2359)

18 keys in `data/locales/*.json` (subjects, scanner readout + five theme traits, chart / travel texts, the world
option, the raider outcome, the toast, the VEGA tip) and six lore texts in `data/stories/vega_protocol/locales/*.json`,
EN + DE by hand, the twelve community languages via `tools/translate_locale.py`. USER_MANUAL (mysteries, bandits,
the T key) and PARENTS updated; the life-pod sentence corrected (no tractor beam is needed to rescue a pod).

## 11. Tests

`DebrisFieldTests` (placer ×3, galaxy untouched, field in flight, recorder, fragment mining, capsule once + restart,
shield taps, combat debris, raider hull + livery + heading + wreckage), `SpaceWreckTests` (carved state across
re-entry + restart, gone once salvaged, payout once), `ContentTests.SpaceSalvageData_*`, `SpaceTargetingTests`
kind lists. Test seams: `SpaceSalvageLedgerForTest`, `DebrisFragmentCountForTest`, `StructureDyedCellCountForTest`.
