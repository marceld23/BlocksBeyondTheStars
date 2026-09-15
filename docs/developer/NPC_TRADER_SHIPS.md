# Peaceful NPC trader ships — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-09-15 (#1904).

## Overview

Ambient civilian trader traffic makes space feel **alive**: peaceful NPC ships warp in, cruise the system,
dock at a station or land on a planet, and their pilot then appears as a merchant you can barter with. Traders
are invulnerable scenery, transient (no DB), and multiple can be active at once. Ship types are picked from
the content registry, so new ships are included automatically.

## How it works

**The key constraint — per-launch-body instancing.** A space-flight scene is an instance keyed by the body
the player launched from (`"space:" + locationId`). Entity visibility, voxel structures and remote-ship
designs are broadcast only to players *in that instance*, and instances are created lazily on first
`EnterSpace` and dropped when the last player leaves. So traders are simulated **only inside flight instances
that currently have players** — never a galaxy-wide background sim. (Two players "in the same system" but
launched from different bodies are in different instances and won't share a trader.)

**Traffic per system.** A deterministic `TrafficFor(systemId)` buckets each system into None / Rare / Often
from seed + system id (stations lean busier). This drives a per-instance spawn scheduler with a concurrent
cap (Rare ≤1, Often ≤3) and arrival cadence (~25–70 s Often, ~90–240 s Rare).

**Ship-type choice (future-proof).** Picks any non-starter type from `GameContent.Ships`, weighted by cargo
capacity so haulers dominate. New ship types in `data/ships.json` appear in trader traffic with no code change.

**Lifecycle** (`GameServerSpaceTraders.cs`): warp **in** at the system edge → **cruise** toward a station or
through the inner system → **dock** (station) or **land** (planet) or warp **out**.

**Rendered with zero new flight-view code.** A trader is **never** a `CombatEntity` (so it can't be locked,
shot, damaged, and never harms anyone). It rides the existing **remote-ship** path: a synthetic
`NetSpacePlayer` pose (`npc:<id>`) plus a `"ship_remote"` `SpaceShipDesign` built by `BuildNpcShipStructure`
(reusing the real player-ship voxel pipeline), so it shows the actual in-game hull of its type.

**Pilot → merchant (station).** On docking, the pilot is registered as a visiting trader at that station;
boarding the station spawns it as a `"traders"`-theme vendor beside the trade post, so the existing
station-vendor barter works against it (lingers 420–720 s, `TraderDwellMin`/`TraderDwellMax`; the expiry is
checked when the station's NPCs spawn).

**Pilot → merchant (planet landing).** A trader heading inward **lands on a planet/moon if a pad is free**:
it **reserves the pad** (so players are never assigned it), plays `ShipTransitFx` descent, parks its real
voxel ship on the pad via `LandedShipState`, and stands its pilot in front as a `"traders"` merchant
(`MarketAvailable`/`VendorThemeAt` extended to the landed pilot, 4-block reach). A `_landedTraders` registry
(keyed by body) is the source of truth and re-materializes the parked ship + pilot on world load / per-world
tick, so it survives the body world unloading. One landed trader per body at a time. Reuses
`LandedShipState`/`ShipTransitFx`/`NpcList` for the hull, pilot and descent/launch animation.

**Landed stay + hold (#1904).** A landed trader rolls a dwell of 600–900 s (`TraderLandDwellMin`/`Max`). When it
runs out, `TickLandedTraders` does **not** lift off while any player on the body (not in space, not an observer)
is within `TraderStayNearRadius` (32 blocks) of the pilot or the parked hull — the tick stamps
`LastPlayerNearAt` every time someone is near, and an expired trader departs only once that lies
`TraderLeaveGraceSeconds` (30 s) back. Trading needs the pilot within 4 blocks, so a trade is never cut off.
There is deliberately **no hard cap**: a held trader blocks only its own pad and its body's single trader slot
(`PickLandableBody` / `TryLandTraderOnBody` skip that body, other traders land elsewhere). Bodies nobody is on
— or whose world is not loaded (a pilot who hyperjumped in occupies it from orbit) — are cleaned up by
`SweepExpiredLandedTraders` the moment the dwell ends, hull included (#1680); a last known position never pins a
trader.

**Planet-map marker (#1904).** While a trader's hull stands on the active body, `BuildPlanetPois` appends a live
`NetPoi` of type `trader_ship` at the pilot, named with `poi.trader_ship` ("Trader ship {0}") in the receiver's
language. It is derived from `_landedTraders` on every build — never revealed, never persisted. The client's
`WorldMap.PoiLook` draws it as the `map_ship` icon in trade gold (drawn after the pads, with a name label), the
HUD compass shows a matching blip, and the thermal optic lists it like any POI.

**Map refresh on land + lift-off (#1904).** Setting down (`MaterializeLandedTraderHere` from the per-world tick)
and lifting off (`DepartLandedTrader`) both call `BroadcastLandedTraderMapChange` → `BroadcastLandingPads` +
`BroadcastPlanetPois`, so players already on the body see the pad taken/free and the marker appear/vanish
(before, the pad list stayed a stale world-entry snapshot — the #1020 class of bug). A world (re)load does not
broadcast: the arriving player's world-entry snapshot already carries both lists.

**Warp FX for bystanders.** Hyperspace warp-in/out had no third-person VFX (`HyperspaceWarp` is a local
full-screen overlay only). A new `SpaceWarpFx` message (tag 150) drives a localized cyan-white burst in the
flight view so other players **see** arrivals and departures.

## Design decisions (locked)

- **Wares → the shared `"traders"` barter list** — a trader's pilot is a `"traders"`-theme vendor, so it offers
  the `marketTheme: "traders"` recipes in `data/recipes.json` (the same list a traders-themed settlement or
  station vendor has). A distinct travelling-merchant stock was the original idea and stays deferred (below).
- **Stay → long, and never under a customer (#1904)** — landed 10–15 min, docked 7–12 min; an expired landed
  trader waits while players are near, with no hard cap.
- **Persistence → transient** — in-memory only, respawn per session like UFOs/asteroids; no DB schema change.
- **Hostility → invulnerable** — peaceful scenery, not targetable, can't be damaged.
- **Traffic source → deterministic-from-seed** for v1; an authored `StarSystem` field could be added later.

## Key files / classes

- Server: `GameServerSpaceTraders.cs` (controller, scheduler, lifecycle, `TrafficFor`, `BuildNpcShipStructure`).
  Pad reservation + planet barter wired into `GameServerSpace.cs` / `GameServerSettlements.cs` /
  `MarketAvailable` / `VendorThemeAt`.
- Networking: `SpaceWarpFx` (tag 150), registered in `NetCodec`; traders otherwise reuse `SpaceShipDesign`
  (`Kind="ship_remote"`), `NetSpacePlayer` (carrying the pilot's name, so flight-view nameplates label traders),
  `LandedShipState`, `ShipTransitFx`, `NpcList`, `LandingPadList` and `PlanetPoiList` (`trader_ship` POI).
- Client: `SpaceView.SpawnWarpFlash` (warp flash); landing/launch reuses `ShipTransitView`; the map marker is
  `WorldMap.PoiLook("trader_ship")` and the compass blip `HudUi.TryGetLandedTrader`.
- Tests: `SpaceTraderTests`, `LandedTraderStayTests` (stay ranges, hold + grace, map marker + re-broadcast).

## Known remaining gaps / deferred

- Needs a Unity client build + lib sync.
- Some ship layouts are parametric boxes (no custom voxel layout), so those NPC ships look like the same
  boxes player ships do until more `data/ship_layouts/*.json` exist.
- Distinct travelling-merchant stock vs. reusing the shared `"traders"` barter list is a polish layer.
- A docked (station) trader has no near-player hold: its dwell only decides whether its merchant is spawned when
  the station's NPCs are (re)populated, and a merchant already standing there stays until that happens again.
