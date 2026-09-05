# Multi-world & system flight — how it works

Status: implemented (see TODO.md for live Done/Open status) · 2026-06-19

## Overview

In multiplayer, players can stand on **different planets** — and even be in **different star
systems** — at the same time. Within a system you fly **between planets manually and land on any of
them**; between systems you hyperjump. This combines two originally separate ideas: per-player worlds
(the multi-world core) and system-scale flight (the layer on top). The full P1–P7 design is shipped.

## How it works

- **Many worlds resident at once.** A `WorldManager` holds a `Dictionary<locationId, LoadedWorld>`
  (`_loaded`) plus an `Active` cursor. Each `LoadedWorld` bundles a `ServerWorld` plus its per-world
  runtime systems (creatures, enemies, flora, fluids, containers, structures, weather/time, wreck).
  Worlds load on demand (`GetOrCreate`) and are dropped from memory (`Unload`) when the last player
  leaves (the caller checks `OccupiedLocations()` before unloading), so memory scales with *occupied*
  locations, not the whole universe. The class header still describes the original single-world seam;
  the multi-world dictionary, tick and unload are now live on top of it.
- **An Active cursor, not parallel execution.** GameServer reaches per-world state through forwarding
  members that all read `_worlds.Active.*` (e.g. `_world => _worlds.Active.World`, `_creatures`,
  `_fluidLevel`). The Tick loop walks `OccupiedLocations()` and calls `SetActiveWorld(locId)` before
  ticking each one, and incoming messages set the cursor to the sender's world first. So worlds are
  resident simultaneously but processed one-at-a-time via the cursor — not truly concurrent.
- **Per-player location.** Each session carries a `CurrentLocationId`; mining/placing/streaming/
  presence all operate on the world resolved for that session (the cursor is pointed at it first).
  `WorldReset` is sent to the moving player only, never broadcast. `SwitchActiveWorld` still exists but
  is now only the startup/default-body helper (sets `_meta.ActiveLocationId` + loads it); per-player
  travel is handled by `LoadWorld` + `SetCurrent` moving just that one player.
- **Per-player ship.** Ship state (cargo, modules, hull/shield) is per player. A player's ship is
  placed into their current world as a structure OBJECT at their own landing pad; each `LoadedWorld`
  keeps a `Dictionary<string, LandedShip> LandedShips` keyed by player id (there is no
  `WorldManager.ShipStamps` / ship cursor). Two players on one planet each get a separate ship at
  distinct start positions. Docking grants guests cross-ship cargo/module access.
- **Per-launch-body flight space.** The space instance is keyed by the **launch body's location id**
  (`"space:" + locationId`), not by system id. Players who launch from the same body share that flight
  space and see each other; the flight view still receives the whole system's bodies (`SendStarMap`) so
  you can fly toward and land on any of them. (A system-wide shared flight space is not yet the model.)
- **Land anywhere in the system.** Bodies carry seeded positions (added to `CelestialBody` /
  `NetBody`). In flight the ship detects a body's approach zone, offers "Land on <body>", and leaving
  space with a destination body id moves just that player to that body — no global reset. Leaving
  without a destination returns you to where you launched.
- **Inter-system travel = hyperjump.** The star map + the `jump_generator` ship module drive system
  selection (`HyperjumpSystemIntent`, NetCodec 117 → `HyperjumpToSystem`); the jump moves the player's
  flight space to the target system (others stay put), arriving in flight anchored on the target's
  first landable body. The warp VFX (`HyperspaceWarp`, client) covers the transition; it is triggered
  by the client's `HyperjumpStarted` event. The flight chart's **Hyperspace tab** (`SpaceMap` +
  `GalaxyChartWidget`, #1603) draws every system at its `MapX/MapY` — stars only, in the server's
  per-system `StarColor` (#1604) — and offers the same `HyperjumpSystemIntent` from a selected star; the
  finale system is placed out past every other star (`GuardianFinaleMapPosition`, #1605) now that map
  positions are visible. Pure projection/name rules live in `Shared/World/GalaxyChartLayout.cs`.
- **Scoped presence.** A player only sees remote players / NPCs / creatures in the same world (or same
  flight space). This is enforced server-side: presence/entity broadcasts filter recipients on matching
  `CurrentLocationId` (see `GameServerPresence.cs`) rather than carrying a scope-id field on the wire.

## Key files & classes

- `WorldManager` / `LoadedWorld` / `LandedShip` (`src/BlocksBeyondTheStars.GameServer/WorldManager.cs`)
  — the resident-worlds dictionary + `Active` cursor (`GetOrCreate` / `SetActive` / `Unload`) and the
  per-player `LandedShips` map.
- `GameServer.cs` — per-session `CurrentLocationId`, `SwitchActiveWorld` (startup default), `LoadWorld`,
  `SetActiveWorld`, `OccupiedLocations`, and the per-occupied-world `Tick` loop.
- The per-world subsystems: `GameServerCreatures / Enemies / Flora / Fluids / Containers /
  Settlements / Weather / Wrecks / ShipStructure` — each reads `_worlds.Active.*`.
- `GameServerSpaceCombat` / `SpaceView` (client) — the per-launch-body flight space.
- `Galaxy.cs` (`CelestialBody.SystemX/Y/Z`) + universe generation — seeded per-body system coordinates.
- Networking: `NetBody` (system coords), addressed `WorldReset`, presence scoping by `CurrentLocationId`
  (server-side filter), destination body id on `LeaveSpaceIntent`, `HyperjumpSystemIntent` (NetCodec 117).

## Design notes

- **The WorldManager indirection (P2) was the keystone.** It was landed as a pure
  behaviour-preserving seam — every `_world` access routed through `WorldFor(...)` while the manager
  still held a single world — so the later multi-world and per-ship changes were incremental and
  low-risk on top of it.
- **Memory** is bounded by refcount unload + save-on-empty; only occupied locations stay resident.
- **Save compatibility / determinism:** persistence was already location-scoped
  (`LoadChunkEdits(planet, chunk)`, `landing_zone`, `SetLocationStatus`), so the multi-world model
  needed only additive fields; per-location seeding stays `seed ^ StableHash(locationId)`.
- **No cross-world entity leakage** is enforced by the per-world Active cursor + `CurrentLocationId`
  recipient filtering + per-player `WorldReset`, with tests that a player in world A never receives
  world B's entities.

## Known gaps / deferred

- Cross-world chat stays **global**; local/system chat channels are a possible later add.
- Larger-fleet space combat (remote ships/players fully rendered in the flight view, cruisers/bosses)
  is tracked separately, not part of this plan.
