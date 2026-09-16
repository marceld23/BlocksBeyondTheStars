# Space stations as their own locations — how it works

Status: implemented (see TODO.md for live Done/Open status) · 2026-06-19

## Overview

Boarding a space station puts the player **inside the station, floating in space as its own place** —
no planet visible, no weather, no clouds, constant interior lighting (no day-night), breathable (life
support), walkable, with NPCs (vendor / quartermaster / dockhands). A station is modelled as its own
location and boarded exactly like travelling to another body, so it reuses the proven world-transition
path and is cleanly isolated from any planet.

## How it works

- **Void world type.** A `Void` flag on `PlanetType` makes `WorldGenerator.Generate` return an
  all-air world immediately (no terrain, caves, ore, flora).
- **The `orbital_station` planet type.** `Void=true`, `SpaceSky=true`, breathable atmosphere (life
  support → no oxygen drain), zero flora/creatures, weather disabled, a fixed non-advancing
  time-of-day. The universe generator never assigns it to a celestial body — it is a code-defined
  type, not part of the planet pool.
- **Station environment.** The station world's `WorldEnvironment` is `SpaceSky=true`, clear weather,
  cloud density 0, fixed time-of-day, breathable. The client gates clouds/weather/day-night on `env`,
  so the black sky, constant light and lit interior fill follow from the env. As a belt-and-braces
  fallback, `Sky.cs` *also* still treats a non-empty `Game.StationName` as "boarded" → space sky, no
  day/night and lit interior (so a boarded station looks right even before its env arrives); the env
  path and this `StationName` check coexist.
- **`LoadWorld` skips planet content** for void worlds: no settlement/wreck stamping, no
  flora/fluids/creatures/landing-zone init; it sets the fixed station env instead.
- **Boarding = per-player travel into the station world.** `BoardStation` mirrors `HandleTravel`:
  range/validation checks → leave the space instance → remember the return location →
  `LoadWorld("orbital_station", "station:"+id)` → stamp the station onto a clean origin with a solid
  hangar floor + spawn the station NPCs (scoped to this world) → set `CurrentLocationId`, position and
  `AboardShip=false` → `SentChunks.Clear()` → send `WorldReset` + player state + inventory + NPCs +
  environment. The client's `OnWorldReset` clears chunks, nulls the spawn and re-snaps once chunks
  stream — the robust reposition.
- **Leaving = travel back.** `LeaveStation` loads the return planet world, restores the ship and
  position, re-snaps via `WorldReset`, and unloads the station world if empty. It can undock back into
  **space flight** around the orbited planet rather than dropping to the surface.
- **Player-built stations** reuse the same machinery: `StampPlayerStation` stamps the player's own
  cells into the `orbital_station` void world (spawn on a guaranteed floor pad), registers the build as
  a boardable `CelestialBody`, and persists it as a `space_structure`. Stations are peaceful — void
  worlds hard-skip enemy/creature/flora ticks.

## In flight: the real hull (#1917)

Until 2026-09 every generated station flew as one hard-coded placeholder (`SpaceView.BuildStationModel`, scaled by
tier) while its interior was a different voxel build. Now a station looks the same from the cockpit as on foot:

- **Layout at first sight.** `AddStationContacts` calls `EnsureStationHull` for every generated station of the orbit.
  `EnsureStationStructure` (split out of `StampStation`) decides the layout — pinned kit replay, fresh template/kit
  pick, legacy roll or `StationGenerator` — and pins it. "Fresh" asks `_repo.HasAnyBlockEdits("station:<id>")` instead of
  the loaded world's `VirginAtLoad`, so the result is the same whether the station is first seen in flight or first
  boarded from the travel screen. `StampStation` only stamps the built structure.
- **The flown hull.** `StationHull.VisibleCells` keeps every block next to outside space plus what shows through
  see-through or shaped cells up to `WindowDepth = 12` steps inside; the cells (with tints and shapes) become a
  `SpaceStructure` of kind `station`, owner empty (EVA edits are refused), stored in `instance.Structures` and sent with
  the other voxel bodies as `SpaceShipDesign`. The client already drew player stations this way and skips the
  placeholder for any station with a design.
- **Dock point.** `StationHull.FindDock` finds the force-field cell nearest to the `hangar` marker with outside space in
  front and air behind; its patch's centre and outward direction ride on `SpaceShipDesign.HasDock/DockX..DockOutZ`.
  Fallbacks: the outer wall straight out from the marker, else the middle of the -Z face.
- **Layout in the orbit.** `LayoutHullCentres` puts the hulls 1:1 in the classic three lanes ahead of the launch point,
  each `StationHullGap = 30` behind the previous one, its lowest block at `StationHullFloorY = 44` or higher (clear of
  the largest planet sphere).
- **Boarding range** is measured to the hull box (`StationFlightDistance`, player builds included); NPC traders head for
  the dock point (`StationDockPoint`).
- **Client** (`SpaceView`): station hulls unload only 900 units from their box, the flight clamp grows to include every
  hull, the ship collides with hull cells (`ResolveStationHullMove`, radius 2.5), the dock prompt measures to the hull,
  `PlanDockApproach` flies to the point in front of the mouth (over or under the hull box when it is in the way) and
  into the mouth, fading only at the end; the autopilot and chart targets use that approach point.
- Tests: `StationHullTests` (exterior detail, replay, shell, dock), `StationHullServerTests` (pin at first sight, flown
  hull, board range, EVA refusal, the exterior upgrade of an old kit station, orbit layout).

## Key files & classes

- `GameServerSpaceStations.cs` — `BoardStation`, `LeaveStation`, `StampStation`, `StampPlayerStation`,
  station NPC spawning, multi-station iteration per system.
- `WorldGenerator.Generate` — early-out for `Void` worlds.
- `PlanetType` (`Void` flag) + the `orbital_station` definition.
- `LoadWorld` (`GameServer.cs`) — void-world content skip (`if (!planet.Void)`), per-world NPC scoping.
- Client `Sky.cs` + `OnWorldReset` — env-driven space sky / lit interior (with a `StationName` fallback),
  clean reposition.
- Tests: `BoardStation_PutsPlayerInOwnVoidWorld_OnSolidGround_WithLifeSupport`,
  `LeaveStation_TravelsBackToThePlanet`, `LeaveStation_UndocksBackIntoSpaceFlight`,
  `VoidPlanet_GeneratesEmptySpace`.

## Design notes

- The earlier bug was that `BoardStation` stamped the station into the **planet world** at a high Y
  and teleported there *without* a real world transition — no `WorldReset`, no `SentChunks.Clear()`.
  The station chunks never streamed in time, so gravity pulled the player down until far-below planet
  terrain finally loaded: the "black → fall → planet" symptom, with weather/clouds/day-night bleeding
  in from the shared planet world. Modelling the station as its own location fixed both at once.

## Known gaps / deferred

- NPC-station world edits are intentionally light (regenerate on board). Player-built stations persist as
  `space_structure`, and since #1481 their interior edits are written back into that cell grid through a
  stamp anchor persisted on the row (`stamped`, `smin_x/y/z`): the world and the grid map onto each other
  through `Origin + cell − StampMin`, a later boarding only tops up cells the world does not already hold,
  and the spawn pad is re-cut only when its spot stopped being standable. Doors built inside remain door
  entities (persisted per world) and count as airtight for the station pocket. What is still open: EVA
  hull edits made while the interior world is loaded reach it on the next boarding, not live.
- Residual "flora into space" on **pre-fix persisted** station worlds is hardened separately
  (structure-stamp enclosure checks + re-clearing old stamped worlds) — see TODO.md.
