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
  `AboardShip=false` → `Streaming.SentChunks.Clear()` → send `WorldReset` + player state + inventory + NPCs +
  environment. The client's `OnWorldReset` clears chunks, nulls the spawn and re-snaps once chunks
  stream — the robust reposition.
- **Leaving = travel back.** `LeaveStation` loads the return planet world, restores the ship and
  position, re-snaps via `WorldReset`, and unloads the station world if empty. It can undock back into
  **space flight** around the orbited planet rather than dropping to the surface.
- **Player-built stations** reuse the same machinery: `StampPlayerStation` stamps the player's own
  cells into the `orbital_station` void world, registers the build as a boardable `CelestialBody`, and
  persists it as a `space_structure`. Stations are peaceful — void worlds hard-skip enemy/creature/flora
  ticks. The one deliberate exception (#2635): a generated derelict's interior world runs its guardian bay
  (`TickEnemies` → `TickDerelictGuardians`) — guardians only, from the bay only, inside the hull only, gated like the
  planet machines; see [DERELICT_STATIONS.md](DERELICT_STATIONS.md#guardians--the-guardian-bay-2635).
- **Arrival on a player station (#2606).** `PlacePlayerStationArrival` runs on **every** boarding (also
  when the stamp is already done for this run): from the `station_core`'s world cell (build cell 0,0,0)
  it takes the nearest standable cell (feet + two free cells above, same deck preferred), a **sealed air
  pocket first** (#1833), any standable cell only when the build holds no air anywhere; the top of the
  core itself comes last. With the core mined the search starts from the bounding-box centre's bottom
  layer instead. No pad is cut on the first boarding any more (the old 3×3 iron pad at the box centre sat
  in the vacuum of a non-convex build and was later folded into it, #1559); a pad is cut only when no
  cell of the build is standable at all. The box's chunks are loaded first — a re-boarding in the same
  run finds the station world freshly reloaded, and the search reads without loading.
- **Ship decor.** Boarding sends an **empty** `ShipStations` list (and the server sends an empty list
  wherever no ship is parked, instead of nothing), so the client drops the previous ship's room decor
  (`StationDecorView`) and its interaction prompts; leaving sends the parked ship's list again.

## Player-station doors and the refreshed interior (#2607)

Screelit: "on the ground a door is a door — in space a door becomes a block!" A door built onto a player station on a
spacewalk used to be a solid door BLOCK (the station's "airlock" for commissioning, an airtight wall, solid aboard). And
the interior was stamped once per server run, so walls closed from outside after the first boarding reached the inside
only after a restart (his station had no air until he flew away and came back). `GameServerStationDoors.cs`:

- **Door records.** A spacewalk door on a station is a doorway + a record, like a ship's (#2119): the cell stays air,
  kind and wall axis live in `SpaceStructure.PlacedDoorAxes`/`DoorKinds` (axis by the jambs, else the suit's heading —
  a station's grid is the flight frame). `SpaceShipDesign.DoorX…DoorAxisX` carries them, so the flight view draws the
  door; the aim stops on its three-high doorway, and mining it hands the door back (`TakeStationDoor`). Nothing is
  built into a doorway. **Only new doors**: door blocks from before stay blocks; `StationHasAirlock` takes a door block
  or a record, and a record counts as the one block toward the commission minimum that its door block used to be (a
  core, ten hull blocks and a door still commission).
- **Persistence — telling a record from an old door block.** The row's `x:y:z:block` cell list is unchanged (a door
  block in it is a block). The records go to the per-cell **structure-edit store** under the station's id, which no
  station used before: the door block id in the cell, the axis in the shape field (`PlacedDoorAxisX/Z`, the ship's own
  encoding). `PersistStationDoors` rewrites the whole set (delete + insert, one transaction) whenever a commissioned
  station's records change and at the commission; `ApplyPersistedStationDoors` restores them in `LoadPlayerStations`
  (a record whose cell holds a block in the build is dropped). No save-version bump; an older server ignores the
  edits, and a rename (which rewrote the row on its own and dropped the stamp anchor — now `PersistStation`) cannot
  lose them. An uncommissioned station writes none, so a later deploy that reuses its id inherits no stale door.
- **The interior on every boarding.** `StampPlayerStation` no longer returns early once stamped. Order: the doors
  (`SyncStationDoors`: a record without a door in the station world is hung there — its cell cleared, the door stored
  as a player door; a door the world holds without a record, i.e. one built aboard, becomes a record; then the
  registry reloads), the absorb pass (#1559), the top-up stamp, the bounds (the air search reaches the door cells), the
  arrival (#2606) and the placeables. A door built or taken out ABOARD updates the records at once
  (`SyncStationDoorRecordsFromWorld`, from `RefreshStationBoundsAfterDoorChange`), so the hull seen from outside shows
  it. No arrival (core or door) is ever placed in a doorway (`InDoorway`: a door fills the feet or head cell — a door
  entity is air to the standing test). Players already aboard get the top-up and doorway clears as `BlockChanged`, and
  the rebuilt door registry (fresh ids) as a `DoorList` broadcast.
- **Outside edits reach the interior at once.** Once a station has an interior (`Materialised`), every spacewalk
  edit — a block built or mined, a door hung or taken — is written into the interior world straight away
  (`MirrorStationCellOutside` / `MirrorStationDoorOutside`): through the loaded world while someone is aboard (they
  see it live, and the station box — the air and gravity reach — grows with the edit), else into its stored edits
  (keyed by the canonical cell of the station world's circumference). That keeps the absorb pass from taking a wall
  mined from outside back into the build; a commissioned station's mined cell is now persisted with its row too. An
  edit that moves the cell box re-sends the station design (`ResendStationDesignIfBoxChanged`): the flight view draws
  the hull around the box centre of the last design it got, the server measures in the current box.
- **Boarding at the door.** `BoardStation` on a spacewalk looks for the station door nearest the suit within
  `StationDoorBoardReach` (8, above the client's 6-block aim reach plus half the doorway) of its opening — flight
  position `station position + cell − cell-box centre`, as the client draws it — and `EnterBoardedStation(…, throughDoor)` puts the player on that door's sealed inside after the
  stamp (`StationDoorArrival`: one or two cells in across the wall, standable with headroom, in a sealed pocket), else
  at the core arrival. The board intent is unchanged; the client only prefers the station over a nearer ship on E
  when the aim rests on one of its doors.
- **Client.** The spacewalk door preview shows at stations whose owner is the player or an ally
  (`SpaceShipDesign.OwnerId`, `SpaceDoors.MayBuildOnStation`); a closed energy door shows its field in space on ship
  and station, the preview too (`SpaceDoors.ShowsFieldInSpace`); the parked ship's cabin/interior shows the door
  preview with the ship's cells as jambs — only on the player's own parked ship or construction site, where the server
  builds.
- Tests: `StationDoorTests` (record + commission, core + ten walls + a door, an old door block, a working door aboard
  + air, restart + rename, taking it out, the doorway refusal, the design, outside build → inside on the next boarding,
  a wall mined from outside across a restart, the live edit + the grown air reach for a boarder, a door built aboard →
  record, board at the door, at a door after the hull grew, no arrival in a door beside the core or in an airlock's
  second door, ship docking at the core, a door into the void); Client.Tests `SpaceDoorsTests`.

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

## Derelict stations (#2632)

The broken old stations of [DERELICT_STATIONS.md](DERELICT_STATIONS.md) are boarded through this same path but ride
the PLAYER-station half of it: their build is an ownerless `SpaceStructure` in `_playerStationCells`, stamped by
`StampPlayerStation` into `station:derelict:…`, with no air at all (dead life support, #2675), the gravity volume and
the spacewalk / drift rescue — and none of the NPC half (no crew, no markers, no mission board, no protection).

Two EVA ↔ station rules are derelict-only (#2636, `GameServerDerelictSpacewalk.cs`):

- **Leaving the hull is a spacewalk.** A generated derelict's boarder 2 blocks clear of the hull's cell box (the void
  check, deferred to after the world loop) leaves through `LeaveStation(playerId, spacewalkFrom)`: the undock path with
  the ship's float spot in `_dockedFromEva` (the spot remembered at a spacewalk boarding when it lies 12+ blocks clear
  of the hull — it is the SUIT's spot, see `BoardStation` — else off the hull's face toward the launch point) and the
  suit's own pose — `EnterSpace(…, evaStart)` seeds the suit's `PlayerPoses`
  there and `SpaceState.HasEvaStart/EvaStart*` tells the flight view, which starts the EVA at that spot instead of
  behind the ship. The mapping is the door rule inverted: interior position → build cell (`− Origin + StampMin`) →
  flight (`position + cell − box centre`), held within 6 blocks of the box, raised to the suit's eye (+1.5). No flight
  possible (free flight off, an unflyable ship) → the pull-back as before. The Long Quiet keeps the pull-back.
- **Boarding through an opening.** On a spacewalk, `BoardStation` also searches from the suit through open build cells
  (air, no doorway; at most 6 steps, the suit within 3 blocks of the box) for a deck under a roof inside the box — a
  breach, the open hangar, a torn panel — and the nearer of that deck and a door in reach wins
  (`EnterBoardedStation(…, throughBreach)` → `DerelictBreachArrival`, the standable cell nearest to it). Neither in
  reach: the usual arrival in the sealed command room.

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
  and the arrival is searched beside the core (see "Arrival on a player station" above) instead of re-cutting
  a pad into the build. Doors built inside remain door
  entities (persisted per world), count as airtight for the station pocket and — since #2607 — are the station's
  door records too. EVA hull edits reach a loaded interior live and an unloaded one through its stored edits (#2607).
  Still open: door BLOCKS built on a spacewalk before #2607 stay solid blocks (by decision, no migration).
- Residual "flora into space" on **pre-fix persisted** station worlds is hardened separately
  (structure-stamp enclosure checks + re-clearing old stamped worlds) — see TODO.md.
