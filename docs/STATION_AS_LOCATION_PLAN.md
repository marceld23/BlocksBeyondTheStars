# Space stations as their own locations — Plan

**Status: ✅ shipped (2026-06-05).** S1–S7 implemented: boarding is a real `WorldReset` world transition into
the station's own void world; you land inside on solid ground with NPCs + life support, no planet visible.
Tests: `BoardStation_PutsPlayerInOwnVoidWorld_OnSolidGround_WithLifeSupport`,
`LeaveStation_TravelsBackToThePlanet`, `VoidPlanet_GeneratesEmptySpace`.

**Goal:** boarding a space station puts the player **inside the station, floating in space as its own
place** — no planet visible, no weather, no clouds, constant interior lighting (no time-of-day), breathable
(life support), walkable, with NPCs (vendor / quartermaster / dockhands). Written in English per project
doc policy.

---

## 1. Root-cause analysis (why boarding currently drops you to the planet)

Today `BoardStation` (`GameServerSpaceStations.cs`) stamps the station structure into the **planet world**
at a high Y (`Origin = (900+i·120, 400, 900)`) and teleports the player there, but it does **not** do a real
world transition. Compare with the working planet-travel path (`GameServer.HandleTravel`, ~lines 330–396):

| Step | `HandleTravel` (works) | `BoardStation` (falls through) |
|---|---|---|
| Active world cursor | `Serve(session)` | (set by `OnPayload` only) |
| Leave space instance | `LeaveSpace(id)` | `instance.Players.Remove` ✓ |
| Load destination world | `LoadWorld(type, id)` | ✗ stays in the planet world |
| Change `CurrentLocationId` | ✓ | ✗ unchanged |
| **`session.SentChunks.Clear()`** | ✓ (line 383) | ✗ **missing** |
| **`WorldReset`** to client | ✓ (line 385) | ✗ **missing** |

Because there is **no `WorldReset` + no `SentChunks.Clear`**, the client never does its clean reset
(clear chunks → `ServerSpawn=null` → re-snap once chunks stream). Instead it relies on the fragile
"`StationName` changed → snap" path; the far-away station chunks don't reliably stream in time, so gravity
pulls the player down until the planet terrain finally streams far below — the **"black → fall → planet"**
symptom. Sharing the planet world is also why weather/clouds/day-night/atmosphere bleed into the station.

**The robust fix is to model a station as its own location and board it exactly like travelling to another
body — the proven path that never falls through.**

Confirmed enablers (already in place):
- Multi-world infra: `WorldManager` / `LoadedWorld`, `LoadWorld`, per-player `WorldReset`, `Serve`,
  `JoinedInActiveWorld`, unload-on-empty.
- NPCs are per-world (`_npcs => _worlds.Active.Npcs`) — they scope to a station world automatically.
- The client `OnWorldReset` clears chunks + nulls `ServerSpawn` and re-snaps — the robust reposition.
- `WorldGenerator.Generate` is a pure function of the `PlanetType`; an all-air world just needs an early out.

---

## 2. Plan (phases S1–S7)

### S1 — Void world type + generation
- Add `public bool Void { get; set; }` to `PlanetType`.
- In `WorldGenerator.Generate`, if `planet.Void` return the empty (all-air) `ChunkData` immediately (no
  terrain, caves, ore, flora).
- Add a `Test:` assert that a Void planet generates an all-air chunk.

### S2 — The station planet type
- Define an `orbital_station` `PlanetType`: `Void=true`, `SpaceSky=true`, `Atmosphere="breathable"`
  (station life support → no oxygen drain), `FloraDensity=0`, `CreatureAbundance="none"`, `CaveThreshold=0`,
  weather disabled, a fixed (non-advancing) time-of-day.
- Define it so the **universe generator never assigns it to a celestial body** (either keep it out of the
  universe's planet pool, or synthesize the `PlanetType` in code rather than `planets.json`). Verify
  `UniverseGenerator` can't pick it.

### S3 — Station environment (no weather / clouds / day-night)
- The station world's `WorldEnvironment`: `SpaceSky=true`, `Weather="clear"`, cloud density `0`,
  `TimeOfDay` fixed + not advanced, breathable. The client already gates clouds/weather/day-night on `env`
  (clouds off at density 0, no rain when clear, constant light when time-of-day is fixed, black sky from
  `SpaceSky`); the lit-interior fill stays on. **Remove** the client `StationName→space-sky` hack from
  `Sky.cs` once the station env drives it.

### S4 — `LoadWorld` skips planet content for Void worlds
- In `LoadWorld`, when `planet.Void`: skip `StampSettlement` / `StampWreck` / `InitFlora` / `InitFluids` /
  `InitCreatures` / `LoadLandingZones`. Keep an `InitWeather`-equivalent that sets the fixed station env (S3).

### S5 — Boarding = per-player travel into the station world
Rewrite `BoardStation` to mirror `HandleTravel`:
1. range/validation checks as today (still requires being in space + close enough).
2. `Serve(session)`; `LeaveSpace(playerId)` (drop the space instance).
3. Remember the return planet: `_boardedReturnLoc[playerId] = session.CurrentLocationId`.
4. `LoadWorld("orbital_station", "station:"+station.Id)` (load/create the void station world; sets Active).
5. `StampStation` into **this** world at a clean origin (e.g. `(8, 64, 8)`) with a solid hangar floor at the
   spawn; `SpawnStationNpcs` (now scoped to the station world).
6. `session.CurrentLocationId = "station:"+id`; `session.State.Position = station.Spawn`; `AboardShip=false`.
7. `session.SentChunks.Clear()`.
8. Send `WorldReset { PlanetType="orbital_station", PlanetName=station.Name, SystemName=… }` +
   `SendPlayerState` + `SendInventory` + `SendNpcs` + `SendEnvironment`. (`SpaceClosed` may be folded in or
   kept to tear down the space view — `SpaceView` already tears down on `WorldReset`/hyperjump.)

### S6 — Leaving = travel back to the planet (the ship)
Rewrite `LeaveStation` to mirror travel back:
- `LoadWorld(returnPlanetType, returnLoc)`; `StampShip`; `Position = _healTank`; `AboardShip=true`;
  `CurrentLocationId = returnLoc`; `SentChunks.Clear()`; `WorldReset` to the planet; unload the station world
  if empty. (Optionally return to **orbit** instead of the surface — decide during implementation.)

### S7 — Movement, tests, cleanup
- Movement + NPCs already work in any world (the void world streams just the station; the player walks it;
  vendor trading via `NearVendor` keeps working).
- **Remove the high-orbit hack:** the planet-world `Origin` Y, the planet-world floor pad, and the client
  `StationName→space-sky` override (now the station world's env handles it).
- **Tests:** the existing station tests stamp into `_world` — update them to the station world. Add a
  `Board_PutsPlayerInStationWorld_OnSolidGround_WithNpcs` integration test.
- **Persistence:** decide whether station worlds persist their edits (probably ephemeral — regenerate on
  board, key by `station:<id>` if persisted).

---

## 3. Risks / open questions
- **Universe generation must never pick `orbital_station`** as a planet/moon — verify the pool.
- **Space-view → station transition:** confirm `SpaceView` tears down on the station `WorldReset` (it does
  for hyperjump/world-reset; the boarding animation finishes first, then the reset fires).
- **Persistence of station edits / multiple players** in one station (works like any shared world via the
  per-world cursor; per-player ship-stamp isn't needed — there's no ship in the station).
- **Return target on leave:** surface (ship) vs. back to orbit — pick one (surface/ship is simplest, matches
  today).

## 4. Bottom line
Boarding becomes "travel to the station location"; the station is a void world with a space sky, no
weather/clouds, fixed interior lighting, life support, the station structure on solid ground, and its NPCs.
This reuses the robust `WorldReset` reposition (no fall-through) and cleanly isolates the station from the
planet — exactly the "stations are their own units in orbit" model.
