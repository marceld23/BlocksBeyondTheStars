# NPC Trader Ships — Analysis & Plan

> Goal: make space feel **alive** with peaceful NPC trader ships that warp in, fly to a
> station or planet, dock/land, and whose pilot then appears as a merchant. Reuse the
> existing in-game ship types; keep the random selection **future-proof** as more ship
> types are added. Ship traffic **varies per system** (none / rare / often). Multiple
> traders can be active at once.

> **Implementation status (2026-06-15):** P0–P4-lite **shipped** (server + client + tests) — ambient
> warp-in/cruise/depart traffic rendered via the remote-ship path, station docking with a visiting merchant,
> and **P3 planet landing**: a trader lands on a planet/moon when a pad is free, reserves the pad, parks its
> real ship and stands its pilot in front as a merchant for ~180–360 s, then lifts off. A `_landedTraders`
> registry (keyed by body) is the source of truth and re-materializes the parked ship + pilot on world load /
> per-world tick, so it survives the body world unloading. P3 reuses the `LandedShipState` / `ShipTransitFx` /
> `NpcList` paths → **no new client code**. Code: `GameServerSpaceTraders.cs`, `BuildNpcShipStructure`,
> `SpaceWarpFx` (tag 150), client `SpaceView.SpawnWarpFlash`; pad reservation + planet barter wired into
> `GameServerSpace.cs` / `GameServerSettlements.cs` / `MarketAvailable`. Needs a Unity client build + lib sync.
> Sections below are the original analysis, kept for reference.

---

## 1. The single most important architectural fact

A space-flight scene is an **instance keyed by the body the player launched from**:

```
GameServerSpaceCombat.cs:297-303
string locationId = _ship.CurrentLocationId (else active location);
string instanceId = "space:" + locationId;        // e.g. "space:sys0-p0"
```

Consequences that drive the whole design:

- **Visibility is per-instance.** Entities (`CombatEntity`), voxel structures
  (`SpaceStructure`) and remote-ship designs are broadcast only to players *in that
  instance*. A trader simulated where no player is present is invisible and pointless.
- **Instances are lazy + ephemeral.** Created on first `EnterSpace` for a body, dropped
  when the last player leaves (`LeaveSpace`, line 407-410). So NPC traffic must be driven
  **only inside instances that currently have players** — never a galaxy-wide background sim.
- **The flight *view* is system-scale.** `CelestialBody.SystemX/Y/Z` (Galaxy.cs:35-39)
  positions every body of the system in one flight scene, and `SendStarMap` hands the client
  all bodies "to render + land on them" (line 332). So within one instance a trader **can**
  fly from its arrival point to a station contact or to another planet/moon and dock/land —
  all observable in the same scene.
- **Surface/station representations are shared per-body, not per-instance.** A *landed* ship
  lives in the planet world `sys0-p1` (seen by everyone on that body); a *docked* pilot lives
  in the station interior world `station:<id>`. These are global per location, independent of
  which flight instance the trader flew in from.

**Design takeaway:** an NPC trader is a single *logical* controller that, depending on its
phase, injects the right representation into the right context:
space-flight instance → station interior world → planet surface world.

---

## 2. What already exists and is directly reusable

| Need | Existing system | Reference |
| --- | --- | --- |
| Ship type registry (future-proof) | `GameContent.Ships` dict, loaded from `data/ships.json` | `Content/GameContent.cs:26,38`; `Content/ContentLoader.cs:41` |
| Ship → voxel hull | `BuildShipStructure(ownerId)` builds a `SpaceStructure` from the ship def/layout | `GameServerSpaceStructure.cs:84` |
| Non-hostile space entity | `CombatEntity` already supports `Hostile=false` (stations use it) | `GameServerSpaceCombat.cs:22-75` |
| Render any ship in the flight view | `SpaceShipDesign` message + `ShipMeshBuilder.BuildVoxelShip`; remote ships sent with `Kind="ship_remote"` | `SpaceView.cs:359`; `ShipMeshBuilder.cs:36` |
| Server-authoritative movement w/ patrol/chase stepping | `MoveSpaceHostiles` (UFO/drone patrol+approach math) | `GameServerSpaceCombat.cs:1017-1098` |
| Throttled state broadcast (~0.15s when moving) | `BroadcastSpaceState(instance)` | `GameServerSpaceCombat.cs:1228-1237` |
| **Planet landing/launch animation seen by others** | `ShipTransitFx` msg + `ShipTransitView` (thruster glow, dust burst, 3.6 s) | `GameServerSpace.cs:472`; `ShipTransitView.cs:139-180` |
| Parked ship as a world object everyone sees | `LandedShip` + `LandedShipState` broadcast | `WorldManager.cs:122-134`; `GameServerShipStructure.cs:40-88` |
| Landing pads (deterministic, occupancy-tracked) | `LandingPad`, `TryClaimPad`, `AssignedPadIndex` | `GameServerSpace.cs:26-33,330` |
| Humanoid pilot on station/planet (vendor) | `ServerNpc` + client `PlayerAvatar`; vendors trade by theme | `GameServerNpcs.cs:25-40,509-567`; `NpcView.cs` |
| Station docking → interior world | `BoardStation` / `EnterBoardedStation`; station NPCs spawned per visit | `GameServerSpaceStations.cs:174-279,509-567` |
| Trading mechanics | Market recipes + station vendor themes | `GameServerSettlements.cs:230-259` |
| Per-tick hook for autonomous entities | `Tick` → `TickSpace` (space) / per-world `TickCreatures`, `TickNpcs` | `GameServer.cs:646-695` |
| Networking pattern (must `Register` in NetCodec) | `NetCodec` static ctor, append-only tags | `NetCodec.cs:21-219` |

**Answer to "do player ships already have arrival animations?"**

- **Planet landing/launch: yes** — `ShipTransitFx`/`ShipTransitView` is a third-person effect
  other players already see. Reuse it verbatim for NPC landing/launch. ✅
- **Hyperspace warp-in seen by others: no.** `HyperspaceWarp.cs` is a **local full-screen
  overlay** for the jumping player only — there is no third-person "a ship just warped in here"
  VFX. For NPC arrivals to look right to bystanders this is **new client VFX** (a localized
  streak + flash at a world position). This is the main net-new client work.

---

## 3. What is missing / net-new

1. **NPC trader controller + registry** (server). No unified entity system exists — each type
   (player/UFO/asteroid/creature/NPC) is ad-hoc. Add an `NpcTraderShip` controller list, scoped
   per active space instance, with a phase state machine.
2. **Localized warp-in/out VFX** (client) — a third-person arrival effect at a position.
3. **NPC ship voxel design without a player owner.** `BuildShipStructure` is keyed to a player.
   Need an overload that builds from a *ship type key* directly (the data is all in `ShipDefinition`/layout).
4. **NPC entity in the wire `SpaceState`.** Either a new `CombatEntityKind.Trader` (`Hostile=false`)
   carried by the existing `SpaceState`/`NetCombatEntity`, plus a `SpaceShipDesign` with a new
   `Kind="ship_npc"`. Prefer reusing existing messages over inventing new ones.
5. **Per-system traffic level** (none / rare / often). `StarSystem` has no such field today.
6. **Landing-pad reservation for NPCs.** Pads are a shared resource tracked per player session
   (`AssignedPadIndex`); an NPC occupying a pad must reserve it so players aren't assigned the same one.
7. **Docked-pilot injection.** When a trader "docks", register a transient visiting-trader
   `ServerNpc` into that station's interior so any boarding player sees the merchant.
8. **Trader merchant wares.** Decide whether the visiting trader reuses station vendor trading or
   carries its own (possibly rarer / travelling-merchant) stock.

---

## 4. Future-proof ship-type selection

The registry is already the right extension point — enumerate `GameContent.Ships.Values`.
New ships added to `data/ships.json` are picked up automatically; **no code change needed** to
include them in NPC spawns.

Recommended selection (deterministic per spawn, weighted toward cargo-ish hulls for *traders*):

```csharp
// All non-starter ship types, weighted by an archetype heuristic (cargo capacity).
var pool = _content.Ships.Values.Where(s => s.Key != "starter").ToList();
// weight ~ CargoSlots so haulers dominate trader traffic, scouts/corvettes appear occasionally
```

Optional (nice future hook, not required for v1): add `int NpcSpawnWeight` and/or
`string Archetype` to `ShipDefinition` so designers can tune which ships appear as traders
purely from data. Until then the `CargoSlots` heuristic is enough and stays future-proof.

---

## 5. Per-system traffic frequency (none / rare / often)

Keep it **deterministic from the world seed + system id** — no new persistence, stable across
sessions, matches the codebase's deterministic style:

```
TrafficLevel(systemId) = hash(seed, systemId) bucketed →  None | Rare | Often
```

Biases to make it feel intentional:
- Systems **with a station** lean toward `Often` (hubs of trade).
- Barren systems (asteroids/wreck only) lean toward `None`.
- Roughly: 25 % None / 50 % Rare / 25 % Often before the station/hub bias.

This level drives the per-instance spawn scheduler (Section 6). Optionally surface it later as
a real field on `StarSystem` set by `UniverseGenerator` if designers want hand authoring; the
deterministic helper is the v1.

---

## 6. NPC trader lifecycle (state machine)

One controller object per active trader. Phases and which context they touch:

```
                 (space-flight instance, per launch-body)        (shared per-body world)
  ARRIVING  ──▶  CRUISING  ──▶  DOCKING ─────────────────▶ DOCKED ──▶ DEPARTING ──▶ (despawn)
  warp-in FX     fly toward     approach station contact   pilot      launch/warp
  at edge        chosen target  OR descend to a pad        NPC on     out FX
                 (station/body)                            station/
                                                           in front
                                            ┌── station path ──┐
                                            │ ship vanishes into │
                                            │ station; pilot =   │
                                            │ ServerNpc in       │
                                            │ station interior   │
                                            └────────────────────┘
                                            ┌── planet path ─────┐
                                            │ ShipTransitFx down; │
                                            │ LandedShip on pad + │
                                            │ pilot ServerNpc     │
                                            │ standing in front   │
                                            └─────────────────────┘
```

- **ARRIVING** — pick an entry point at the instance edge, broadcast warp-in VFX, spawn the
  `CombatEntity`(Hostile=false, Kind=Trader) + its `SpaceShipDesign`.
- **CRUISING** — step toward the chosen destination using `MoveSpaceHostiles`-style movement;
  broadcast position at the existing ~0.15 s moving cadence.
- **DOCKING (station)** — on reaching the station contact, despawn the space ship (small dock
  ease/fade) and register a visiting-trader `ServerNpc` in `station:<id>` so boarding players see
  the merchant standing at/near a vendor marker.
- **LANDING (planet)** — reserve a free landing pad, play `ShipTransitFx` descent, place a
  `LandedShip` on the pad in the body world, and spawn a pilot `ServerNpc` standing in front of it.
- **DOCKED/PARKED** — dwell for a randomized duration (e.g. 2–6 min).
- **DEPARTING** — reverse: remove pilot/landed-ship, re-spawn the ship in the flight instance (if
  still occupied), fly to edge, warp-out FX, despawn. If the instance emptied meanwhile, just drop it.

**Instance-empties handling:** if all players leave while a trader is cruising, drop the
controller (consistent with instances being ephemeral). If a trader is *docked/landed* (a shared
per-body world), it may persist briefly so a player arriving on that body still sees it, then time out.

---

## 7. Server architecture

- **Registry:** `Dictionary<string, NpcTraderShip>` keyed by trader id (e.g. `"trader_<n>"`),
  plus an index of trader → current instance/world. Transient, in-memory only (like UFOs) — no DB.
- **Spawn scheduler:** in `TickSpace`, for each occupied instance, look up the launch body's
  system traffic level and maintain a target population (None→0, Rare→0–1 occasional, Often→1–3),
  with arrival cadence + a hard concurrent cap per instance.
- **Movement/phases:** a `TickSpaceTraders(dt)` step inside `TickSpace`, reusing the patrol/approach
  vector math from `MoveSpaceHostiles`.
- **Pad reservation:** extend pad occupancy so an NPC-held pad index reports occupied to
  `TryClaimPad`/`RequestLandingPadsIntent` (so players don't collide with a parked trader).
- **Pilot NPC:** when docked/landed, add a `ServerNpc` (role `vendor`, theme `traders`) to the
  relevant world's NPC set; remove on departure. Reuse `BroadcastNpcs` — no new client code for the avatar.

## 8. Networking

Prefer reuse to stay within the `NetCodec` append-only discipline:
- Carry the trader as a `NetCombatEntity` inside the existing `SpaceState` (new
  `CombatEntityKind.Trader`, `Hostile=false`). Existing broadcast path covers it.
- Send its hull via `SpaceShipDesign` with a new `Kind="ship_npc"` (client renders like
  `ship_remote`).
- **New messages needed (register in NetCodec):** a localized **warp-in/out VFX** cue
  (id, position) for bystanders. Possibly a small "trader arrived/departed" toast (optional).
- The docked pilot rides the existing `NpcList` path; the landed ship rides `LandedShipState`.

> Remember: every new message class must be `Register()`-ed in `NetCodec` or sends throw
> silently (`NetCodec.cs:21-219`). New `CombatEntityKind` is a wire enum — append, never reorder.

## 9. Client work

- Render `ship_npc` designs in `SpaceView` exactly like `ship_remote` (distinct nameplate / tint so
  traders read as friendly).
- **New:** localized warp-in/out VFX (streak ring + flash) at a world position — the one genuinely
  new visual. Landing/launch reuses `ShipTransitView`.
- Optional: a friendly "Trader" nameplate + an approach hint when near the docked/landed merchant.

---

## 10. Phased implementation plan

- **Phase 0 — Plumbing & data**
  - `BuildShipStructure` overload from a ship-type key (no player owner).
  - `TrafficLevel(systemId)` deterministic helper.
  - `CombatEntityKind.Trader`; `SpaceShipDesign Kind="ship_npc"`.
- **Phase 1 — Ambient space traffic (biggest "alive" win, lowest risk)**
  - Trader controller + spawn scheduler in occupied instances, scaled by traffic level.
  - ARRIVING/CRUISING/DEPARTING with warp-in/out VFX; fly between bodies; despawn on empty.
  - No docking yet — ships just traverse the system. Multiple concurrent supported.
- **Phase 2 — Station docking + pilot merchant**
  - DOCKING phase; visiting-trader `ServerNpc` injected into the station interior; players boarding see/trade with it.
- **Phase 3 — Planet landing + pilot in front of ship**
  - Pad reservation; `ShipTransitFx` descent; `LandedShip` + pilot `ServerNpc`; departure.
- **Phase 4 — Merchant identity & polish**
  - Travelling-merchant wares (rarer stock than fixed station vendors), names/themes, nameplates,
    optional arrival toast, tuning of cadence/dwell/caps per traffic level.

Each phase is independently shippable and visible in-game.

---

## 11. Design decisions (RESOLVED 2026-06-14)

1. **Trader wares → own travelling-merchant stock.** Traders carry their own, often
   rarer/discounted inventory so meeting one feels rewarding (distinct from fixed station vendors).
   Implication: the docked/landed pilot needs a per-trader ware list (seeded), not just the station
   vendor theme. Build the basic merchant interaction early but layer the *distinct* travelling stock
   in (still part of the full scope).
2. **Persistence → transient.** In-memory only, respawn per session like UFOs/asteroids. No DB schema
   change; spawn state seeded per instance + arrival.
3. **Hostility → invulnerable.** Traders are peaceful scenery and cannot be shot. `Hostile=false`
   plus an explicit "invulnerable / not targetable" guard so weapons can't lock or damage them.
4. **Scope → all phases.** Implement the full flow (ambient traffic → station docking + pilot
   merchant → planet landing + pilot in front → own travelling wares & polish). Still build in the
   P0→P4 order for safe, independently-testable increments, but the target is the complete feature.
5. **Traffic level source (my default, not asked):** deterministic-from-seed helper for v1; an
   authored `StarSystem` field can be added later if designers want hand-tuning.

---

## 12. Risks / gotchas

- **Per-launch-body instancing** (Section 1) means two players in the "same system" but launched
  from different bodies are in different instances — a trader is only co-visible with players who
  share its instance. Communicate this clearly; don't promise galaxy-wide shared traffic.
- **No third-person warp-in today** — budget for new client VFX.
- **Pad contention** with players must be handled or players get assigned an occupied pad.
- **`NetCodec` append-only** — new enum values/messages must be appended, never reordered (save compat).
- **Ship layouts** for some types are parametric boxes (no custom voxel layout) — NPC ships will look
  like the same boxes player ships do until more `data/ship_layouts/*.json` exist. Acceptable.
