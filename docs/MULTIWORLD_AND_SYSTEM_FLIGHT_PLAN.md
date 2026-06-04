# Multi-world + system-scale flight — implementation plan

**Requirement (2026-06-04):** in multiplayer, players must be able to be on **different planets** — and
even in **different star systems** — at the same time, and within a system they must be able to **fly
between planets manually and land on any of them**. This is the design plan (not yet implemented). It
combines the deferred "per-player worlds" (B) with "system-scale flight" (A).

## 1. Where we are today (constraints)

- **One active world.** The server holds a single `ServerWorld _world` and a single shared `ShipState
  _ship` (`GameServer.cs`). `SwitchActiveWorld` rebuilds the world and **displaces every player**
  (`HandleTravel`). Two players cannot be on different planets.
- **`_world` / `_ship` spread.** `_world` is referenced in ~12 server files (heaviest:
  `GameServerShipStructure`, `GameServer`, `GameServerFluids`, `GameServerWrecks`); `_ship` in ~10
  (heaviest: `GameServerSpaceCombat`, `GameServer`). Concentrated, not pervasive — manageable.
- **Space flight is an arena.** A per-location space instance keyed `"space:"+locationId`, ±130 units,
  decoupled from the voxel world (`GameServerSpaceCombat`, `SpaceView`). Landing (`LeaveSpaceIntent`,
  no destination) always returns to the active world.
- **Bodies have no positions.** `CelestialBody` (`Galaxy.cs`) has only Id/Name/Kind/PlanetType/SystemId —
  no X/Y/Z within a system. Nothing to fly *to*.

**Big enabler already in place:** persistence is **location-scoped** — `LoadChunkEdits(planet, chunk)`,
`landing_zone (player_id, location_id)`, `SetLocationStatus(locationId)` (`SqliteWorldRepository`). The
save DB can already hold many locations; only the in-memory single-world limit blocks multi-world.

## 2. Target architecture

- **WorldManager**: `Dictionary<string locationId, LoadedWorld>` where `LoadedWorld` bundles a
  `ServerWorld` + its per-world runtime systems (creatures, enemies, flora, fluids, containers,
  structures, weather/time, wreck). Loaded on demand, **unloaded (and saved) when no players remain**
  (refcount). Memory scales with *occupied* locations, not the whole universe.
- **Per-player location**: each `PlayerSession` has a `CurrentLocationId`. Mining/placing/streaming/
  presence operate on `WorldFor(session)`. `WorldReset` becomes **per-player** (sent to the moving player,
  not broadcast).
- **Per-player ship**: `ShipState` moves from a single shared field to **one per player** (cargo, modules,
  hull/shield). A ship is stamped into its owner's current world at their landing zone. Docking/crew
  grants cross-ship cargo/module access (extends existing `GameServerDocking`).
- **Per-system flight space**: the space instance becomes keyed by **systemId**, holding all of the
  system's bodies at their system coordinates plus asteroids/stations. Players in the same system share
  it and see each other; different systems are different flight spaces.
- **Presence scoping**: a player only sees remote players / NPCs / creatures in the same world (or same
  flight space). Entity/presence messages carry a scope id so clients render only their world.

## 3. Phased plan (each phase builds + tests green before the next)

**P1 — Body positions (data model).** Add seeded `SystemX/Y/Z` (or orbital radius+angle) to
`CelestialBody`; populate deterministically in `UniverseGenerator`. Add position to `NetBody`
(contractless-safe). No behaviour change yet. *Low effort, low risk.*

**P2 — WorldManager indirection (no behaviour change).** Introduce `WorldFor(locationId)` and route every
`_world` access through it; initially the manager holds exactly one world, so the game behaves identically.
Convert the per-world subsystems (`GameServerCreatures/Enemies/Flora/Fluids/Containers/Settlements/
Weather/Wrecks/ShipStructure`) to take a `LoadedWorld`/locationId instead of reading the global. This is
the **big, careful refactor** — done as a pure indirection so it can land safely. *High effort, low risk
(behaviour-preserving), large test surface — add per-world unit tests.*

**P3 — Multiple loaded worlds + per-player location.** Give sessions `CurrentLocationId`; load/unload
worlds by refcount; stream chunks from the player's world; scope presence/entities per world; make
`WorldReset` per-player. `SwitchActiveWorld` (global) is replaced by `MovePlayerToLocation(session, id)`.
Now two players can stand on different planets. *High effort, moderate risk (lifecycle + networking).*

**P4 — Per-player ship.** Split `_ship` into per-player `ShipState`; stamp each into its owner's world;
update cargo/modules/space-combat/trade/missions to use the owner's ship; docking grants guest access.
*Moderate–high effort; touches the ~10 `_ship` files.*

**P5 — System-scale flight + land-anywhere.** Rekey the space instance by systemId; render all bodies at
their P1 coordinates; enlarge the volume; detect when the ship enters a body's **approach zone** and show
"Land on <body>"; `LeaveSpaceIntent` gains a **destination body id** → `MovePlayerToLocation` to that body
(per-player, no global reset). Fly + land on any planet in the system. *Moderate effort; reuses P3.*

**P6 — Inter-system travel.** Hyperjump (`jump_generator`) moves the player's flight space to another
system's space (their own, others stay put). Star map drives system selection; arrival drops you into the
target system's flight space. *Low–moderate effort on top of P5.*

**P7 — Cross-world multiplayer polish + persistence.** Star map shows every player's current system/body;
presence lists are world-scoped; save-on-unload + load-on-first-entry verified; disconnect handling;
chat stays global (or add local/system channels later). *Moderate effort; hardening + tests.*

## 4. Networking changes
- `NetBody`: + system coordinates (P1).
- `WorldReset`: addressed to one player instead of broadcast (P3).
- Presence / NPC / creature / space-entity messages: + a world/flight-space scope id so clients filter (P3/P5).
- `LeaveSpaceIntent`: + destination body id (P5).
- `PlayerPresence` / star map: + current location id per player (P7).
All additive (contractless-safe).

## 5. Risks & mitigations
- **Breadth of `_world`/`_ship`.** Mitigated by P2 doing a behaviour-preserving indirection first, before
  any semantic change.
- **Memory of multiple worlds.** Refcount unload + save-on-empty; only occupied locations stay resident.
- **Determinism / save compatibility.** Persistence is already location-scoped; new fields are additive;
  keep per-location seeding (`_meta.Seed ^ StableHash(locationId)`).
- **Networking entity leakage across worlds.** Scope ids + per-player `WorldReset`; add tests that a player
  in world A never receives world B's entities.
- **Testing.** Add multi-world server tests (two sessions, two locations: edits isolated, presence
  isolated, unload-on-empty saves, land-on-approach switches only the mover).

## 6. Rough effort
P1 ~small · P2 ~large (the refactor) · P3 ~large · P4 ~medium · P5 ~medium · P6 ~small · P7 ~medium.
Sequencing matters: **P2 is the keystone** — once world access is an indirection, P3–P6 are incremental.

## 7. Status
Planned, not started. Tracked from [TODO.md](../TODO.md). Supersedes the earlier "approach A only" note —
the firm requirement (simultaneous different planets/systems in multiplayer) makes the multi-world core
(B) mandatory, with the system-flight layer (A) on top.
