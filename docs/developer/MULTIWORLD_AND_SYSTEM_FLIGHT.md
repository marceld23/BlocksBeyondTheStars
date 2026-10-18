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
- **Weather outlives residency (#2173).** Every body of an occupied system carries an ambient `WeatherSim`
  (`GameServerAmbientWeather`): a loading world ADOPTS its body's sim instead of starting fresh, and an unloading
  world leaves it in the ambient table, where it keeps advancing at 1 Hz. So the weather seen from orbit is the
  weather you land in. See [PLANET_VIEW_AND_WEATHER.md](PLANET_VIEW_AND_WEATHER.md).
- **An Active cursor, not parallel execution.** GameServer reaches per-world state through forwarding
  members that all read `_worlds.Active.*` (e.g. `_world => _worlds.Active.World`, `_creatures`,
  `_fluidLevel`). The Tick loop walks `OccupiedLocations()` and calls `SetActiveWorld(locId)` before
  ticking each one, and incoming messages set the cursor to the sender's world first. So worlds are
  resident simultaneously but processed one-at-a-time via the cursor — not truly concurrent.
  Since #2235 this also holds for the two pieces that used to be server-wide: every `ServerWorld` owns its
  **`WorldGenerator`** (`ServerWorld.Generator`, cloned from the startup template; `GameServer._generator` reads
  the active world's, and a temporary `_bodyGeneratorOverride` serves other-body reads such as the planet
  overview or the ambient weather of a body nobody stands on), and the station staff check runs on
  `LoadedWorld.SinceStationStaffCheck`, so two loaded stations no longer share one timer.
- **Per-player location.** Each session carries a `CurrentLocationId`; mining/placing/streaming/
  presence all operate on the world resolved for that session (the cursor is pointed at it first).
  `WorldReset` is sent to the moving player only, never broadcast. `SwitchActiveWorld` still exists but
  is now only the startup/default-body helper (sets `_meta.ActiveLocationId` + loads it); per-player
  travel is handled by `LoadWorld` + `SetCurrent` moving just that one player.
- **Per-player ship.** Ship state (cargo, modules, hull/shield) is per player. A player's ship is
  placed into their current world as a structure OBJECT at their own landing pad; each `LoadedWorld`
  keeps a `Dictionary<string, LandedShip> LandedShips` keyed by player id (there is no
  `WorldManager.ShipStamps` / ship cursor). Two players on one planet each get a separate ship at
  distinct start positions. Docking and trading share one range check (`PlayersTogether`, each with its own
  range): on foot the same world and the positions there, in flight the two players' poses in ONE flight space
  (ship, or suit on a spacewalk) — `State.Position` keeps the stale surface spot while flying — and one flying,
  one on foot never counts as together (#2711, #2720). Docking alone (`CanDockTogether`, request and accept alike)
  also counts a player who walks inside their own ship while it floats parked as out in that flight space, at the
  parked ship's pose (#2723) — so a pilot docks with a friend who already stepped inside, and two friends each
  walking their own floating ship dock too. Trading keeps the plain rule.
- **Walking inside the ship in space.** `EnterShipInterior` loads the pilot's own void world `shipint:<id>` with
  the ship structure placed in it and records the way back in `_inShipInterior` (the instance, the ship's parked pose,
  the body under the flight). While that record exists the hull floats **parked** for the other pilots and keeps the
  flight instance loaded (#2431). The record is session state: a disconnect forgets it (`ForgetShipInterior`, #2722 —
  it used to survive a rejoin, so walking out of the landed ship on the planet started a spacewalk and the planet air
  did not count), and a join drops anything a previous session left behind.
- **The docking visit (#2723, `GameServerShipVisits`).** Docking means a visit and nothing else (no shared cargo, no
  coupled flight). Ship interiors are per-owner worlds, but presence, doors and block broadcasts are per world, so a
  guest can share the host's `shipint:<host>`. `VisitShipIntent { HostId }` (NetCodec 304) is allowed while docked, in
  one flight instance, within `DockRange`, with the host's ship floating parked (host inside it or on a spacewalk —
  a host at the helm is refused and told a friend wants to come over). The visit parks the guest's ship exactly like
  stepping inside it (`ParkShipForWalkabout` — the guest keeps their OWN `_inShipInterior` record), places the host's
  hull in the host's world when the host is out on a spacewalk, and loads the guest at the host's heal tank with
  `WorldReset.ShipOwnerId = <host>` (empty in the own interior and everywhere else). Visits live in memory only
  (`_shipVisits`, guest → host; docking is 1:1, so one guest per host) — no save change, no protocol bump
  (`DockStatus.CanVisit` tells a client the server can do visits).
  - *Home:* `EnterShipIntent` (or the pause menu's `ReturnToShipIntent`) and walking out of the host's hull load the
    guest's own interior from that kept record; `ExitShipIntent` or a launch goes straight to the guest's own helm.
  - *Sent home before the host's ship moves:* the host takes the helm or launches (`ReturnToFlight`, `EnterSpace`),
    switches ships, leaves the flight (also by boarding a station or a derelict from a spacewalk), dies or quits, either
    side undocks (the host's U is "send home"), plus a 1 Hz check (host joined, still docked, ship parked in the guest's
    instance, same active ship). Stepping out through the airlock is no move — `EnterSpace(airlock: true)` keeps the
    guest aboard. A dying or quitting guest's visit is just forgotten — the respawn or the quit moves them. The host's
    "a friend wants to come over" line is throttled per guest.
  - *One hull per interior:* a `shipint:` world only ever holds its owner's ship, so the landing-pad occupancy check
    ignores everyone there (`PadOccupiedByOther`) — a guest holding the same pad index used to push the host's hull to
    another pad when the host came back in.
  - *Shared coordinates:* every ship interior uses the same coordinates, so a pose the client sent from the interior it
    just left passes the spawn gate. `MoveIntent.WorldId` (stamped by `NetworkClient` with the last JoinAccepted /
    WorldReset) lets the server drop poses from a world it already moved the player out of; for an older client a pose
    from outside the hull is ignored for a moment after any switch into a ship interior (`StaleShipInteriorPose`).
  - *Look only:* the dispatch refuses world edits from a guest (`@srv.visit.look_only` — the void around the hull
    included), ship stations refuse a guest, and `ShipLabAboard` ignores a guest's own lab module; travel and teleports
    ask the guest to go home first. The guest's air follows "inside a parked hull" like any visitor on a planet
    (`UpdateAboard` measures the host's hull), so an unsealed extension holds none.
- **Per-launch-body flight space.** The space instance is keyed by the **launch body's location id**
  (`"space:" + locationId`), not by system id. Players who launch from the same body share that flight
  space and see each other; the flight view still receives the whole system's bodies (`SendStarMap`) so
  you can fly toward and land on any of them. (A system-wide shared flight space is not yet the model.)
  Pilots of the system's OTHER instances reach a pilot only as a navigation target (#2737):
  `SpaceState.SystemPilots`, their poses converted into the receiver's frame with the star-map offset of the
  two anchor bodies × `SystemBodyLayout.FlightViewScale` — lockable and on the radar, never a ship in the
  scene (see SPACE_COMBAT_CONCEPT.md → *Target lock*).
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
- **Wormholes (#2242)** are the second way between systems: a `WormholeTransitIntent` (NetCodec 285) moves just
  that player's flight space to the twin system, next to the other rift, like a hyperjump without the module —
  see [WORMHOLES.md](WORMHOLES.md).
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
- `GameServerShipInterior` (walking inside the floating ship, `shipint:<id>`) and `GameServerShipVisits` (the docking
  visit, #2723) with `GameServerDocking` (`CanDockTogether`).
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
