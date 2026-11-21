# Free Space Flight & Combat — Concept & MVP

Concept and feasibility note for the space-flight / combat / enemy systems
(`anf_space_flight.md` §6–14). The spec explicitly asks for a **concept and feasibility
check before** building the full system (§6.5, §12); this document is that check, plus the
scope of the server MVP that ships with milestone **M19**.

> **Status note (2026-06-19):** this records the original **M19 MVP**. The MVP shipped as
> described (`GameServerSpaceCombat` + `GameServerEnemies`, rule-gated, server-authoritative),
> and the architecture/decisions below all still hold. Several items the "Deferred" list called
> out have since landed, so do not read that list as the current state. Notably now implemented:
> the **combat/flight is fully rendered** (`SpaceView`, not scaffold-only); **Alien UFO**
> behaviour with tracers/spotted-message (rule-gated); a **ship-loss option** —
> `GameRules.KeepShipOnDeath` (false ⇒ the defeated ship becomes a downed **wreck** you repair
> to fly again) on top of the default no-permanent-loss recovery; **salvage/wreck** boxes
> (`GameServerWrecks`); and peaceful **NPC trader ships** (`GameServerSpaceTraders`). Still
> deferred: per-player/multiple ships and PvP ship combat, module-level damage/overheating,
> large cruiser/boss events.
>
> **Status note (2026-10-06, #2351):** raiders (`GameServerBanditShips`) now fly **real voxel hulls** from the
> content ship designs in a dyed livery and face their course (`NetCombatEntity.Yaw`); a destroyed drone, saucer,
> cruiser or raider leaves **combat debris** — voxel fragments carved for scrap (`GameServerDebrisFields`); **debris
> fields** are a star-map location of their own with fragments, once-per-galaxy salvage capsules and a flight recorder,
> and their rubble taps the shield (never the hull); the space wreck's salvage pays **once** (a persisted ledger). See
> [DEBRIS_FIELDS.md](DEBRIS_FIELDS.md). The "salvage of defeated ships" item below is thereby landed in its PvE form;
> boarding a defeated ship remains deferred.

## Decision

**Build a small, fully server-authoritative PvE slice now; defer the rest.** The ship is too
central to progression to risk on an over-scoped combat system, and the spec recommends
**no permanent ship loss by default** (§8.5). So M19 delivers:

- Local **space instances** (orbit / asteroid field) that players enter and leave.
- A simple **shield + hull** model on the ship (§8.4 "for an early version, keep it simple:
  shield points, hull points").
- **Ship-weapon blueprints** (cannon, laser, asteroid breaker, shield generator) that only
  work when the server's rules allow them (§7.2).
- Simple **NPC drones** and destructible **asteroids** in space instances, gated by rules.
- **Planet enemies** that spawn, deal proximity damage and can be killed, gated by rules.
- **No permanent ship loss**: a defeated ship is disabled, the player respawns at the
  Medbay heal-tank, and the ship is recovered to its landing base with restored hull.

Everything below the line "Deferred" is intentionally **not** in the MVP.

## Why the architecture already supports it

- **Server is authoritative (§14).** Clients send intents; the server owns every outcome —
  whether a weapon hits, whether an NPC spawns, whether a ship is damaged. The existing
  intent→validate→broadcast pattern (`OnPayload`, `Reject`, `Broadcast`) extends directly.
- **Rules already exist.** M17 added `SpaceCombat`, `ShipWeapons`, `SpaceNpcEnemies`,
  `AlienUfos`, `PlanetEnemies`, `AsteroidDestruction`, `FreeSpaceFlight` to `GameRules`
  with presets. M19 enforces them; it does not invent new settings.
- **Data-driven content.** Weapons are blueprints + recipes + ship modules in `data/`, like
  every other craftable, so no code change is needed to tune or add weapons.
- **Deterministic tick.** The authoritative `Tick(dt)` loop already drives environment and
  chunk streaming; space/enemy simulation hangs off the same loop.

## Concept answers (from §6.5)

| Question | MVP answer |
|---|---|
| Control complexity | **Arcade**, server-validated. The client flies; the server owns combat state. |
| Camera | Client concern (third-person ship cam first). Out of server scope. |
| Instance size | Small bounded volumes (one orbit / field per location), unloaded when empty. |
| Concurrent ships | MVP: the player's single shared ship vs NPCs. Per-player ships = later. |
| Multiplayer sync | Server broadcasts entity state; clients render. No client authority. |
| Docking precision | Delivered in M18 (handshake, rule-gated). |
| Hit calculation | Server-side: target by entity id, range + cooldown + energy/ammo checks, apply damage. |
| How much combat | A light PvE slice. Combat never gates core progression (mining/crafting/building). |
| Per-server toggle | Yes — `SpaceCombat = Off` disables all of it; flight/docking can still exist. |

## MVP model (server)

- **SpaceEntity**: `Id`, `Kind` (Asteroid / Drone / Ufo / Cruiser), `Hull`, `Position`,
  `Hostile`, optional loot table. In-memory, per instance.
- **SpaceInstance**: `Id` (bound to a location), `Kind` (Orbit / AsteroidField), entities,
  the set of present player ids. Created on first entry, unloaded when the last player leaves.
  Hostiles hunt **per pilot** (#2285): each picks the nearest pilot (`CombatEntity.ChaseTargetId`), keeps it for a
  few seconds and then switches only to a pilot clearly nearer (`PickChaseTarget`); chase movement and the
  "spotted you" warning use that pilot's own pose, never the shared last-writer-wins `ShipPosition`.
- **Ship combat stats** on `ShipState`: `Hull` / `Shield` current values; `HullMax` /
  `ShieldMax` / `ShieldRegenPerSecond` derived from built modules (`hull_plating`,
  `shield_generator`). Shield regenerates out of combat; hull does not (needs repair / base).
- **Weapons**: ship modules with `weapon_*` stats (`weapon_damage`, `weapon_range`,
  `weapon_cooldown`, `weapon_energy`, and a `weapon_class`: Tool vs Combat). Firing is gated:
  - `SpaceCombat == Off` → no combat weapons fire at all.
  - `ShipWeapons` decides Tool-only (asteroid breaker) vs NPC vs PvP.
  - Asteroid mining is allowed even on weapons-off servers if `AsteroidDestruction` permits it
    (§7.4: tools ≠ combat weapons).
- **Engage range** (#2284): a hostile fires on a pilot only inside `SpaceCombatRules.EngageRange` (70, in `Shared`).
  The server's damage aura (`GameServerSpaceCombat.ShipEngageRange`) and the client's drawn enemy shots and
  "attacking" lock state (`SpaceTargeting.AttackRange`, `SpaceView.HostileFireRange`) read that one constant, so the
  two sides cannot drift (the client used to keep its own copy). The starter laser (`ship_laser_basic`,
  `weapon_range` 60 — was 45) reaches to within ten units of it: shot at from the edge of the aura, a pilot flies a
  little closer and shoots back instead of 25 units. `SpaceCombatTests` pins both numbers.
- **Rocks are solid, rams go by their real size** (#3420). In flight the client keeps the flown ship out of every voxel
  body that is not a station — asteroids, wreckage fragments (`debris`) and the wreck: `SpaceView.ResolveRockMove` runs
  `RockKeepOut` (Client.Core) for the rocks near the ship after the station hull check (`ResolveStationHullMove`, #1917).
  The ship is a sphere of `SpaceCombatRules.ShipRockRadius` (2.5, the station hull's radius too) against the rock's block
  boxes (`VoxelSurface`, Shared): a move into the rock ends at its surface with the inward part removed, so the ship stops
  softly and slides along — no bounce-back (flight has no velocity state, it is position + input), no jitter while it
  rests there; a long frame is checked in half-radius steps so a hitch cannot tunnel through a pebble, and a ship a rock
  appeared around is eased out at 6 u/s. A spacewalk keeps its own voxel collision; planet-like landable asteroid
  *bodies* keep their sphere keep-out. The server (`GameServerShipRams.RamSpeed`) never moves the ship — poses stay
  client-reported, as at station hulls — but measures the same surface: a pilot touches an asteroid while the ship's
  centre is within `ShipRockRadius` + 0.25 of its blocks (was: 3 units to the rock's centre, which a big rock's surface
  never reached), and what hurts is the **closing speed** (the part of the last tick's move that pointed into the
  surface; inside the blocks: towards the rock's middle). Over 3 u/s → `min(18, 0.8 × speed)` damage with the 0.8 s
  cooldown, as before; a slow touch, resting at the surface or sliding along it never hurts. The speed baseline
  (`PilotSim.LastPosition`) follows every tick — it used to freeze at the pre-impact spot while touching, which would
  re-read one fast hit after every cooldown now that a rammed ship rests at the surface. A suit on a spacewalk never
  rams; only asteroids ram (a wreck or a fragment stops the ship without damage). Tests: `RockKeepOutTests`
  (Client.Tests), the `#3420` block in `SpaceCombatTests`.
- **No permanent loss**: hull ≤ 0 ⇒ `DisableShip` ⇒ respawn player, restore hull to max,
  clear the instance, ship "recovered to base". PvP ship damage is **not implemented**: another
  player's ship is no combat target (a ship weapon only aims at the instance's combat entities), so
  it is never hit. The world rule `ShipDamageByPlayers` — like `AllowPlayerStructureDamage` — exists
  in `GameRules` and the presets but is **not read** by the server yet (#3225); setting it changes
  nothing until the feature is built.

## Target lock is client presentation (#2277, #2283)

The flight target lock (cycle with T / pad LB / touch TARGET, nearest enemy with R / R3, "target ahead" with the
right mouse button or from the context-actions list — pad L3 / touch ⋯, #2326; frame, edge arrow, threat ticks,
waypoint arrow) is **pure client state** — no intent, no
snapshot field, no protocol version. The server never learns what a player has locked. The lock only chooses which
target id the client writes into the intents it already sends:

- **`FireWeaponIntent.TargetEntityId`** — with the `AutoAim` rule on, the locked fire target is preferred inside
  ±40° of the nose (`SpaceTargeting.LockAssistConeDegrees`), and only when the weapon's `weapon_class` suits it
  (`SpaceTargeting.WeaponSuits` — no breaker onto a drone); the server's own arc stays ±60° (`ValidateSpaceAim`,
  `dot < 0.5`), so a lock-assisted shot is never refused for its angle. `AimValidationTests` pins that contract
  (40° lands, 70° is refused). With `AutoAim` off the lock is display-only — the boresight rule stays honest.
- **`TractorPullIntent.TargetEntityId`** — a locked salvage drop in reach is the one pulled.
- **`ScanEntityIntent.EntityId` / `PlanetScanIntent`** — the scanner reads a locked object in its range (the server
  checks only the range there anyway), but only with nothing on the nose: a nose-aligned target always wins, so an
  auto-lock on an attacker never steals the scan.

**Mining (#2327, #2328).** The lock serves the miner through the same client-only rules. With nothing locked, a shot
at an asteroid or the wreck locks it (`SpaceView.OnShotFired`, from `FireAt`). When the locked rock breaks and no
hostile is attacking, the lock moves to the nearest asteroid/wreck within the selected laser's range
(`SpaceTargeting.NearestMineable`), provided that laser can mine (`weapon_class` 0 or 2 — `SpaceTargeting.CanMine`;
`SpaceView.MiningCycleRange` is that range, 0 with the tractor, the scanner or a pure combat cannon selected). In the
same context the cycle key lets the three nearest asteroids within that range in (`SpaceTargeting.Order` with a
`miningRange`, capped by `MaxRocksInCycle`), after the hostiles and before the navigation points — never while a
hostile is attacking (`Order` drops them again). `IsCycleKind` still excludes asteroids. The server's
`AsteroidDestruction` rule still decides whether a shot breaks the rock; the client never reads it and only learns a
refusal from the reply, exactly as before.

**Friends anywhere in the system (#2737).** Another pilot is a navigation target only — none of the three intents
above ever takes a pilot (`TryLockAssist`, `BestFireTarget` and `LockedEnemyInSights` read entities). So
`SpaceTargeting.IsSystemWide` covers the pilot kind: a friend joins the cycle at any distance, is never range-released
and "target ahead" finds them; NPC traders (the trader kind) stay radar-limited. Flight instances are keyed by the
launch body (`"space:" + body`), so a friend who launched from another body flies in another instance. For them the
server fills the one exception to "no snapshot field": the additive `SpaceState.SystemPilots`
(`GameServer.SystemPilotsElsewhere`) — every pose (incl. EVA suits and parked ships) of the system's other instances,
converted into the receiver's frame with the transform of the belt rocks and wrecks (`(other anchor − own anchor) ×
SystemBodyLayout.FlightViewScale` on x/z, y unchanged; each instance caches its anchor body as `SpaceInstance.Anchor`)
and tagged with `NetSpacePlayer.AnchorBodyId`. It rides every `SpaceState`; a solo pilot, who gets no 5 Hz pose
broadcast, gets a refresh about once a second while such pilots exist (`SpaceInstance.SystemPilotSyncTimer`) and one
more after the last is gone (`SystemPilotsSent`), so the lock is released. The client adds them to the lock candidates
and the radar (a smaller, dimmer pink blip) and labels the lock "Flying near {body}" (`ui.space.target.other_flight`);
`SyncRemotePlayers` and the docking rules keep reading `Players` only, so they never become a ship in the scene.
`/bump` lists them as `space.systemPilots`. Tests: `SpaceTargetingTests`, `PilotVisibilityTests`
(`PilotsOfTheSystemsOtherFlights_…`), `SpaceFlightClientTests` (`AMateFlyingNearAnotherBody_…`).

A tampered client could always send any id; the server validates range, arc, rules, energy and cooldown exactly as
before, so the lock adds no attack surface. The pure rules (disposition, cycle order, lock range with 10 %
hysteresis, the attack newcomer watch behind the auto-lock, the edge-arrow placement incl. behind the camera) live in
`Client.Core/SpaceTargeting.cs` with `SpaceTargetingTests`; the Unity side is `SpaceView.Targeting.cs` plus the shared
bracket/arrow HUD classes in `SpaceTargetHud.cs` (the ship scanner's lock frame is the same class with its charge
ring). A server-aware lock (a `TargetLockIntent`) only becomes worth it with homing weapons or a lock-on time.

## Planet enemies (server)

- Gated by `PlanetEnemies` and disabled in Creative / when `PassiveCreatures`-only
  (§12.4 peaceful servers). Hostiles spawn near players on the surface, deal proximity
  damage to the player's vitals, and are killed by an `AttackEntityIntent` (using the held
  tool/weapon). Killing drops loot. Spawn rate scales with the `AlienActivity` setting.
- **The tank spider** (#3096, `GameServerTankSpiders`, `TankSpiderRules` in Shared, kind `TankSpider`, enemy
  `tank_spider` in `data/enemies.json`): a walking Guardian machine with a pressure tank where a spider has its abdomen.
  It hunts like the robots (sight-gated, out of deep water, around walls — `TankSpiderProfile`, a little quicker than a
  Guardian robot, still slower than a running player) but never bites: its `damagePerSecond` is 0 and the damage aura
  skips it. Once a player it may hurt (not ignored by hostiles, not warded by a companion) stands within
  `TriggerRange` (3 blocks across, 2.5 up or down) and in its sight, `ArmTankSpider` sets `CombatEntity.BurstAt` —
  uptime + `InflateSeconds` (1.5 s) — and it stops; the wire's `NetCombatEntity.Inflating` carries the warning (the
  client swells the tank, blinks its bands and plays the hiss). `BurstDueTankSpiders` runs every enemy tick **before**
  the "no surface target" return, so a fuse never waits for a player to step out of the ship: the spider is removed
  (`PlanetEnemyDefeated`, no loot, no story credit, the kill breather on the spawn timer), a `WorldFx` `tank_burst`
  (Radius `BurstRadius` 3.5) goes to the world and every player it may hurt within that sphere **and in the tank's
  sight** takes `burstDamage` (30) × `FalloffAt(distance)` (1 at the centre → `EdgeShare` 0.4 at the edge) through
  `HurtPlayer` (armour, the speeder's share, the hit marker, `@srv.death.tank_spider`). The cover rule is the sightline,
  not the colossus stomp's roof test (`Sheltered`): a ground-level blast is stopped by a wall in between, not by a roof
  overhead. The client pushes its own player by the same reach and the same sightline (`TankSpiderRules.KnockSpeed`/
  `KnockLift`, falling off like the damage). **No block ever changes** — there is no terrain path in the burst at all.
  Shot before it bursts it is an ordinary defeat (loot, story credit) and only pops. It spawns through the normal
  surface spawner (so never with `PlanetEnemies` Off, in Creative/Sandbox or on a void world) and only as a **refill**:
  never among the quick-start machines of a visit (`EnemyCapSeen`), never while another one is alive in the world, and
  then for `RefillShare` (35 %) of the walking refills, rolled stably per world and spawn ordinal (`TankSpiderRoll`).
  The derelict guardian bay never sends one. Tests: `TankSpiderTests`.

## The hunter shuttle (#3097)

Justus (10): "In space a hunter shuttle would be cool. It is bad at attacking in space, but when it lands and you find it,
there is a hunter and several robot spawners." One enemy across both halves of the game: a weak Guardian craft in space
that, if it gets away, leaves a robot camp on a planet.

**Gate** (`HunterShuttlesAllowed`, GameServerHunterShuttles.cs): the planet machines live (`PlanetEnemiesActive` —
Survival, `PlanetEnemies` not Off, so **never in Peaceful or Creative**), `SpaceNpcEnemies` not Off, space combat PvE/Both
and ship weapons usable against NPCs (the raider's lesson: no fight the pilot cannot win), the world's Danger not Off, not
in the Remnant era (the hunters went with the Guardian), never in the Guardian's finale system, and only in a system with a
planet it could land on.

**In space.** Once per flight instance (`RollHunterShuttle`, the raider's pattern) the space-hostiles slider rolls the odds
— Rare 8 %, Normal 15 %, Frequent 25 %, Extreme 35 %, × the Danger factor, at most 50 % — and a shuttle warps in 40–110 s
later, 150 units from the first pilot it may hunt (never a Sandbox pilot), with the `SpaceWarpFx` style `hunter_shuttle`
(the client's `hunter_shuttle_warp` chirp) and VEGA's `vega.hunter_shuttle.spotted`. Kind `HunterShuttle`, enemy
`hunter_shuttle` in `data/enemies.json`: hull 35, a gun of 2/s (the weakest in space), loot 2 data fragments + a circuit
board. Its hull is a real voxel design (`HunterShuttleHull`, in WorldGeneration since #3522: a stubby lander, 7 × 4 × 10, pale Guardian steel `0x7A8494`
with a red band `0xC8322A`, a cockpit screen over a red sensor eye, nozzles beside an open rear hatch) sent like the
raider's (`ship:bandit:<id>`, `ship_remote`), so the client's voxel-raider path draws it (`SpaceTargeting.IsVoxelHullShipKind`)
and its wreckage is cut from it (`LeavesCombatDebris`).

- **Skirmish**: the hostile movement (`HostileProfile`: aggro 170, stand-off 52, speed 9) brings it to a ring just inside
  the damage aura's 70, where `SkirmishHunterShuttle` circles its pilot sideways (5 u/s) — a moving target that pecks at
  the shields. Its patience (30–50 s) runs down all the while — at half pace while its pilot keeps beyond 120 — so left
  alone it lands all the same.
- **Flight**: hurt to half its hull, out of patience, or alone, it turns (`vega.hunter_shuttle.fleeing`), stops shooting
  (`DamagePerSecond` 0) and runs straight away at 15 u/s (`MoveSpaceHostiles` leaves it alone, `ShuttleFleeing`). It stays
  hostile and shootable: the pilot has 8 s (or until it is 300 out) to bring it down.
- **Shot down** (`OnHunterShuttleKilled`, from the space kill path): the usual machine defeat (loot, story credit,
  `DefeatSpaceMachine`), its hull structure goes, `vega.hunter_shuttle.down` — no camp.
- **Got away** (`LandHunterShuttle`): the warp flash out (no explosion), and `PickHunterLandingBody` picks the planet the
  pilot flies at (the instance's body) if it can, else one where a pilot here has a base, else any landable planet or moon
  (not void, not a gas world, `EnemyDensity` > 0, not the finale body) — never one with an open camp. `RegisterHunterSite`
  plans the camp; VEGA: `vega.hunter_shuttle.landed` ("… landed on {0} … marked on the planet map"), or
  `vega.hunter_shuttle.gone` when no planet can take it (or three camps are already open, `HunterSitesMaxOpen`).

**The camp** (GameServerHunterSites.cs, GameServerHunterSiteStamp.cs). Persisted in `WorldMetadata.HunterShuttleSites`
(body → `HunterShuttleSiteRecord`, additive JSON — no save-version bump, no new block):

- **Planned** (`Pending`): 180–320 blocks from the base of a pilot of that instance on the body (else any base there, else
  the body's first landing pad), in a seeded direction — decided in space while the body's world may not be loaded. The
  planet map shows the marker already (`AppendHunterSitePoi`, POI type `hunter_shuttle`).
- **Stamped** (`Active`): the per-world step `TickHunterSites` (1 Hz, a dictionary lookup on a body without a camp) stamps
  it the first time a player is on the body: `TryPickHunterSiteSpot` searches rings of 14 blocks around the plan, out to
  84 (a plan that fell into a lake moves to its shore), on the generator surface (no chunk loads) for the flattest dry spot (ground spread ≤ 4 over the hull and dock probes) outside
  every base zone, village and landing pad; then `StampHunterSite` writes the hull from existing blocks (`metal_panel` /
  `steel_floor` dyed, `glass`, `glass_clear`, `light_red`, `engine_nozzle`), clears the rest of the hull's box (the hill,
  the grass, a trunk), fills dips under the deck with the planet's ground (≤ 4 deep), puts the shuttle's own Guardian bay
  (`guardian_spawner`) at the far end of the hold facing the hatch, and one or two more on a steel plate beside the hull
  (skipped on wet ground) — **2–3 bays**. Every changed cell is broadcast (`BlockChanged`) and written as an ordinary
  server edit (unowned — the blocks are not protected, the bandit camps' rule). VEGA: `vega.hunter_site.landed_here`. No
  usable ground near the plan (or a gas world) → the record goes, no camp.
- **Awake** while a player on foot is within 56 of the hull (nothing at all otherwise): VEGA's `vega.hunter_site.near`
  once per player and camp; the shuttle's **hunter** (`NewPlanetMachine` tougher) stands guard behind the hatch — gone
  while a player stood within 64 of where it was, it was defeated and never comes back (`GuardDefeated`, persisted); gone
  with nobody near, the far-despawn took it and it returns. Each standing bay releases a machine beside itself (the
  derelict bay's ring, cue `guardian_bay` and spark): first 6 s (+5 s per bay) after someone came near, then every 45 s;
  at most **one** of its own alive (two on Frequent/Extreme), the clock held at zero at the cap so a destroyed robot is
  replaced a full interval later. Walking robots, every other release a scan-drone where it can hover (`PlanetDrones`).
  They are ordinary planet machines (movement, damage, kills, loot through `RunMachinePass`/`AttackEntity`, the
  far-despawn) and count toward the world's machine cap.
- **Cleared** when every bay is mined (`ClearHunterSite`): persisted, a `salvage` crate with `chest` loot appears in the
  hold (once — `GeneratedLoot`), `vega.hunter_site.cleared`, the marker goes. A camp from before a rule change (Peaceful
  switched on) only sleeps: no guard, no releases; it can still be cleared.

**Client.** `SpaceView` builds the voxel hull via `TryBuildVoxelHull` (shared with the raider) or the hand-built lander
(`BuildHunterShuttleModel`, also the Codex portrait in `EnemyPortraitRig`); lock-on name `ui.scan.subject.hunter_shuttle`,
scanner readout `ui.scan.machine.hunter_shuttle`; the camp marker is `WorldMap.PoiLook("hunter_shuttle")` (the ship icon
in Guardian red) plus a compass blip (`HudUi.TryGetPoi`). Tests: `HunterShuttleTests`.

## Manoeuvres, the heavy shot and weapon behaviours (#3762, 2026-11)

A fight used to be "fly within range, hold fire": hostile fire is a per-tick damage field around each hostile (the sum
of `damagePerSecond` of every hostile within `SpaceCombatRules.EngageRange`), hostiles stood still at their stand-off
ring, and every weapon was an instant hit on one target. This package keeps that simple positional model and adds the
rules that make flying matter. The server decides all of them; the client flies and draws by the same shared numbers
(`Shared/Definitions/ShipManeuverRules.cs`, `ShipWeaponRules.cs`, `SpaceCombatRules.cs`).

**Barrel roll (#3753).** The client sends `ShipManeuverIntent { Kind = Roll, On = to the right }`. The server
(`GameServerSpaceManeuvers.ShipManeuver`) grants it when the pilot flies a ship and the cooldown has passed (0.25 s of
slack for clock skew), and then keeps `PilotSim.EvadeUntil` for `RollEvadeSeconds` (0.75 s): the damage field and a
heavy shot skip a pilot inside that window, and the shield does not recharge by it. The cooldown is
`RollBaseCooldown / handling` (clamped 1.5–8 s) unless the ship's data sets `rollCooldown`; the module stat
`roll_cooldown` shortens it. The server relays the roll to the other pilots as an `ActionFx` of kind
`FxActionKinds.Roll` (only the server sends that kind). The client's picture is `Client.Core/Feel/ShipRollState`: one
full turn in `RollSeconds`, a slide of `RollSlideDistance` spread over it, the client's own cooldown clock.

**Boost (#3754).** Both sides run the same `ShipBoostMeter`: it drains while the boost is on, needs
`BoostStartShare` of a full charge to start and refills while it is off. The client sends the on/off edge
(`ShipManeuverKinds.Boost`); the server's meter decides how fast the ship may be. A boost that ran dry needs a new
press. Per ship: `boostPower`, `boostSeconds` in `data/ships.json` (0 = the defaults); module stats `boost_seconds`,
`boost_recharge`. A self-built ship derives both from its engines-to-mass speed (`ShipManeuverRules.CustomBoost`).
The four values travel to the client in `ShipCombatStatus` (an older server sends none → the defaults). Its picture
(#3925, client only) is `Client.Core/Feel/BoostLook` — the hot orange flame, speed lines and wider view; see
[VFX.md](VFX.md) → *Engines and boost*.

**Speed check (#3754).** `GameServer.ShipSpeedCheck` (the host sets it; a server embedded in a test leaves it off,
because tests place ships by hand) makes `ShipMove` pull a pose that lies further from the last accepted one than
`ShipManeuverRules.AllowedTravel` back onto that distance, toward the reported point. The allowance is the ship's top
speed (× boost power while the server's meter is on, and for a second after) × the time since the last pose (at most
5 s) × 1.4, plus 6 units — a flight at full speed, a roll's slide and a frame hitch never trip it, and a ship that
really is elsewhere is caught up with at flying speed instead of being stuck. A suit on a spacewalk is not checked.

**Hostiles that circle (#3756).** Inside the stand-off hold band `MoveSpaceHostiles` no longer skips a hostile: it
steps sideways around its quarry (`OrbitStep`: `OrbitSpeedShare` of its speed, a fixed direction per entity, a slow
rise and fall) and keeps its distance.

**The heavy shot (#3756).** An enemy tier with `heavyShotDamage` (`data/enemies.json`: the cruiser, the raider)
charges a shot at the pilot it hunts once that pilot is inside the engage range. `SpaceHeavyShot` announces the
charge (`HeavyChargeSeconds`) to the whole instance and then the outcome: a hit, or a miss when the pilot is inside
an evade window or beyond `HeavyReachFactor` × the engage range at that moment. The two enemies' steady damage went
down so a pilot who never rolls takes about what they took before.

**Weapon behaviours (#3757, #3758).** A module is a weapon when its stats carry `weapon_damage`
(`ShipWeaponRules.IsWeapon`) — the client's ship-systems bar reads the same rule
(`Client.Core/Feel/ShipWeaponView.InSystemsBar`), so a weapon added to the data is selectable without client code.
Further stats give a weapon its behaviour, all resolved in `GameServerSpaceWeapons`:

| Stat | Effect | Shipped in |
|---|---|---|
| `weapon_homing` 1 | fired at the locked target; no boresight check | `missile_launcher` |
| `weapon_flight_speed` | the damage lands when the shot has flown the distance (`SpaceInstance.Missiles`); wasted when the target is gone | `missile_launcher` |
| `weapon_targets` + `weapon_cone` | also hits the nearest hostiles inside the cone around the aimed one | `scatter_cannon` |
| `weapon_stun` | the hit hostile's shield drops and it neither moves nor fires for that long; then immune to a new stun for `StunImmuneSeconds` | `ion_pulse` |
| `weapon_auto` 1 | fires by itself at the nearest hostile in range; never in the systems bar | `point_defense` |

A ship's `weaponDamage` (`data/ships.json`) multiplies every weapon it fires (#3760).

**Shields, stations and wrecks (#3758, `GameServerSpaceShields`).** An enemy tier's `shield` absorbs ship-weapon
damage before the hull, does not recharge, and travels as the fraction `NetCombatEntity.Shield` so the client can
draw the bubble. A shot at a **station**: a mining tool is refused as before; a combat weapon's shot at an intact
station does nothing (the client draws it ending on the shield bubble — `SpaceCombatRules.StationHasShield`), at a
generated derelict or a giant wreck it costs its energy and cooldown and removes hull blocks where the nose ray meets
the hull (`HullBlocksFor(damage)`: 1–6), through the same mirror / persistence / broadcast path a spacewalker's pick
uses. The `Wreck` entity and wreckage fragments take every weapon's damage wherever ship weapons are allowed.

## The escort cruiser — the space boss (#3900, #3901, #3903, 2026-11)

The **escort cruiser** (`escort_cruiser` in `data/enemies.json`, canon C22) is the cruiser as a boss. On the wire it
is an ordinary space entity of kind `Cruiser` — an older client draws a big cruiser — with
`Scale = EscortCruiserRules.ModelScale` and the id a `BossStatus` names. Code: `GameServerEscortCruiser.cs` (arrival,
phases, leaving, the bar), `GameServerSpaceBossCredit.cs` (who gets what), `GameServerSpaceShipLoss.cs` (a downed
ship in a shared flight), `GameServerEscortCruiserWreck.cs` (the hook the wreck, #3902, hangs on); the numbers both
sides share are in `Shared/Definitions/EscortCruiserRules.cs`.

**The boss is data.** Its tier sets `boss: true` plus `enrageAtHull`, `enrageSpeed`, `enrageHeavyShotInterval` and
`reinforcements`; a tier without `boss` is never a boss. Movement uses the hostile pass of every space enemy with its
own profile (`HostileProfileOf`: notice 240, stand-off 48, speed 5 — a third of a ship's).

**The mega laser is the heavy shot made bigger** — no second attack system. Four more tier values turn a heavy shot
into it: `heavyShotCharge` (the warning, 2.8 s instead of 1.6), `heavyShotRange` (how close the hunted pilot must be
for the charge to start, 110 instead of the engage range; it still lands up to `HeavyReachFactor` × that),
`heavyShotMaxHullShare` (0.4: one hit takes at most 40 % of the target's **full** hull, and a ship above a quarter of
its hull always keeps one hull point — `SpaceCombatRules.HeavyHullDamage`) and `heavyShotStyle` (`mega_laser`: what the
client draws). `SpaceHeavyShot` gained additive fields for the aiming line and the beam: `Style`, `OriginX/Y/Z` (the
hostile's centre), `TargetX/Y/Z` (the aimed ship at the charge; the hit point; where a miss ended), `Range` and
`Blocked`. Three ways to make it miss: a barrel roll in the last moment, being beyond its reach when it fires, or an
asteroid on the line between the two (`HeavyShotBlocked`, `HeavyCoverRadius` × the rock's scale — only for a shot with
a style, the classic heavy shot is unchanged).

**Two phases.** At `enrageAtHull` of its hull it enrages once: its speed × `enrageSpeed`, a heavy shot every
`enrageHeavyShotInterval` seconds (a longer wait already running is cut short), `reinforcements` space drones called
(`CombatEntity.CalledByBoss` — they leave with it and do not count toward the sector's ambient wave memory), and
VEGA says so.

**Arrival from hyperspace.** `SummonEscortCruiser(pilotId, carriesEcho)` (the story; the admin verb `/cruiser`,
`/cruiser echo`) and the **rare ambush** share one path: VEGA's line `vega.cruiser.incoming`, and
`EscortCruiserRules.WarnSeconds` (12 s) later the warp-in — `SpaceWarpFx` with style `escort_cruiser`, 225 units from
the pilot it will hunt and at least 220 from every pilot. The ambush is rolled **once per flight instance**
(`EscortCruiserRules.AmbushChance`: 0.04 / 0.08 / 0.12 / 0.16 by the `SpaceNpcEnemies` slider × the world's Danger
factor, capped at 0.25) and arrives 60–150 s into the flight. Its gates (`EscortCruiserAmbushAllowedIn`): space combat
PvE, the space hostiles not Off, ship weapons that hit NPCs allowed **and** one fitted on a pilot's ship, Danger not
Off; never in the start system, the Guardian's finale system or a Desolate system; only once the world story reached
the chapter `sleeps` — or the world has no story, or the story is won. A summon needs only the rules
(`SpaceBossesAllowed`, the story predicate `world:space_bosses`). One boss per instance at a time.

**The story's summon (#3899, `GameServerEchoCallStory.cs`).** VEGA's mission *ECHO's Call* brings the cruiser that
carries her sister core: while a pilot with an open step of that mission flies, the story calls the summon after 25 s of
the flight — once per flight instance, never in the Guardian's finale system, and only while ECHO is still held (not once
she was freed in the save, not while her cruiser's wreck waits to be boarded). A pilot who flees or loses the ship keeps
the step, and a later flight brings the cruiser again. A cruiser that is already there or on its way (the ambush) becomes
hers instead of a second one coming — the first one a player meets carries ECHO (canon C22). Details:
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#the-sky-lights-missions-evidence-while-a-step-runs-and-echos-cruiser-3899).

**After the finale** the ambush stays possible wherever it was: like the guardian giants the cruisers never heard the
shutdown. It deliberately does **not** ask `RemnantSpaceHostile` (which thins out the ambient waves, #1206).

**Getting away.** With no pilot within twice its notice range for 45 s it jumps away (the same warp flash, departing;
`vega.cruiser.gone`), its called drones with it, and that instance never rolls again. A Sandbox or god-mode pilot is
never hunted (the rule of every space hostile); any pilot nearby keeps it from leaving.

**The boss bar (#3901).** `BossStatus` (id, name key, health and shield 0..1, phase, source `giant` | `space`, active,
defeated) goes to every pilot of the instance: at the warp-in (before the state that carries the entity), on a phase
change, when hull or shield changed (at most twice a second), once a second otherwise, and a last time with
`Active = false` (`Defeated` true when it fell, false when it left). The guardian giant keeps its own
`GuardianGiantStatus`; the client's bar reads both.

**Fights flown together (#3903).** Two rules of space combat did not fit a shared fight:

- *A downed ship.* `DisableShip` used to send every pilot of the instance home and unload it. With another pilot
  still flying there (`TryDisableOneShipOfSeveral`) only the downed ship's crew goes home — its pilot, and a friend
  docked aboard — and the flight goes on; the others get `srv.space.ally_ship_down`. A pilot alone in the instance
  takes the old path unchanged (home, the instance unloads). `KeepShipOnDeath` decides the ship's fate as before.
- *Loot and credit.* An ordinary hostile pays the pilot of the last shot. A boss remembers every pilot whose shot
  hurt it (`SpaceBossRuntime.Fighters`); when it falls, each of them still in the game — also one whose ship went down
  meanwhile — gets the kill credit, the events (`defeat:any` / `space_machine`, `cruiser:defeated` with the system id)
  and their own full copy of the loot, banked straight into backpack and hold (never as a floating drop a tractor
  beam of somebody else could take; what finds no room floats where it fell). The story's machine counter moves once.

**The wreck hook.** The kill calls `OnEscortCruiserDefeated(instance, boss, fighterIds, carriesEcho)` once — after the
credit, before the loot and before that kill's last `SpaceState` broadcast. The boss entity is out of the instance by
then; its `Position` and `Yaw` are where it fell.

**The wreck (#3902).** A beaten escort cruiser does not vanish: the hook leaves a boardable wreck where it fell
(`GameServerEscortCruiserWreck.cs`) — a derelict of its own family (`derelict:cruiser-<system>-<n>`, so no air, no shield,
anyone may board) built from the pin-only giant ship `escort_cruiser` with the *fresh* ruin option (holes and broken
glass, no soot, no scrap, some red lamps still burning), a contact of every orbit of that system, one per system (a new
one replaces the old), remembered in `WorldMetadata.CruiserWrecks`. The ordinary two or three debris fragments still
spawn at the spot. Aboard there are only robots (the guardian bay), the template's chests, a crate for every pilot who
fought, and on the bridge a console whose red light a hacking chip turns blue — on the story's cruiser VEGA's sister
core ECHO then speaks once and says goodbye. The whole of it is in
[GIANT_SHIPS.md § 14](GIANT_SHIPS.md#14-the-escort-cruisers-wreck-3902).

**The client: how the boss is recognised and drawn.** Everything the client decides is presentation; the numbers are
Unity-free in `Client.Core/Boss/` (`BossBarState`, `EscortCruiserLook`, `MegaLaserAim`) and `Client.Core/Music/BossMusicRules`
and tested headless (`BossBarStateTests`, `EscortCruiserLookTests`, and `EscortCruiserClientTests` against the real server).

- *Which cruiser is the boss.* `GameBootstrap.BossBar` (a `BossBarState`) takes every `BossStatus`. When the flight view
  first sees an entity of kind `Cruiser` it builds the boss model if a live status of source `space` names that id
  (`IsSpaceBoss` — the status arrives before the state, so this holds at first sight) or, as a fallback, if its `Scale`
  is at least `EscortCruiserLook.BossScaleFrom` (2); every other cruiser is the plain model as before.
- *The model* (`SpaceView.EscortCruiser.cs`, `BuildEscortCruiserModel` — also the Codex portrait): the cruiser's iron
  hull and Guardian plating, longer and heavier, a bridge with a row of red glowing windows and, above them, a long
  barrel pointing forward. The parts are placed by the constants of `EscortCruiserLook`, so
  `SpaceView.EscortCruiserMuzzleLocal` is the barrel's real front end; effects start at
  `model.TransformPoint(EscortCruiserMuzzleLocal)`. The server sends no heading for the cruiser, so the client turns
  the model toward the pilot it hunts (the mega laser's target while it charges, else the nearest pilot's ship).
  A small component on the model (`EscortCruiserRig`) keeps its scale and phase: at `Phase == 2` the windows pulse
  brighter and the engine plumes burn hotter (one flash and ring mark the change). The lock brackets
  (`TargetRadius`), the explosion and the plumes follow the model's scale; the lock label reads the boss's own name.
- *The mega laser* (`SpaceView.MegaLaser.cs`, `SpaceCombatFx.MegaLaserCharge` / `MegaLaserBeam`): a heavy shot whose
  `Style` is `mega_laser` takes this path, the classic heavy shot is untouched. Charge: a glow gathers at the muzzle
  and a thin aiming line runs to the targeted ship, following both, pulsing faster toward the shot — every pilot sees
  it, the targeted one clearly strongest; that pilot also gets `ui.space.mega_laser_warn` with the seconds left on
  the heavy shot's warning widget and hears the charge as a 2D cue. Hit: one thick beam muzzle → ship, the impact on
  shield or hull, a camera jolt (through `FxCamera`, which honours the shake and camera-motion settings). Miss: the
  beam ends on the rock (`Blocked`, at `Target*`), stops at `Range` when the ship flew out of reach, or passes the
  ship that rolled away and runs on.
- *Arrival and leaving*: `SpaceWarpFx.Style == "escort_cruiser"` draws the warp streak three times as big with the
  cue `cruiser_warp_in` / `cruiser_warp_out` (2D, softer with the distance — it arrives further out than a positional
  cue carries).
- *The boss bar* (`HudUi.BossBar.cs`) draws whatever `BossBarState.TryGetBar` names: the name from the status's name
  key, the hull, a thin shield strip while `Shield > 0`, the deeper pulsing red at phase 2, and the rage mark (the
  giant's status brings its share along; a generic boss's is `enrageAtHull` of its enemy data). A `BossStatus` boss is
  live until its last status, or until none came for 4 s. The guardian giant's bar is unchanged — its own
  `GuardianGiantFight.BossBar` feeds the same model. With two bosses at once the boss of the scene the player is in
  keeps the bar (in flight the space boss, on foot the giant), so it never flickers between them. While the bar
  speaks for the boss, no small health bar floats over its model.
- *The music*: `BossMusicRules.Plays` — the boss track (`music_boss_gauntlet`) loops while a space boss is live in the
  player's flight, as it does for a giant on foot; a cutscene's and the finale's own music are asked first and win.
- *Sounds*: `mega_laser_charge`, `mega_laser_fire`, `cruiser_warp_in`, `cruiser_warp_out` under
  `client/Assets/Resources/audio/`; each call falls back to an existing cue while its clip is missing
  (`heavy_shot_charge` / `heavy_shot_fire` / `raider_warp_in`). `AudioMixRules` gives the two prefixes the widest
  distance profile, because the boss fights from 50–130 units away.

## Deferred (explicitly not in M19)

- Per-player / multiple ships and **PvP ship combat** (needs a ship-ownership model first). On-foot PvP exists since
  #2972 (`GameServerPvp`, `PvpRules`: the `Pvp` modes, `FriendlyFire`, duels, kid-friendly guards); it stops at the
  ship's hull — nobody aboard, in space or on a spacewalk can be hit by a player.
- The hostiles' numbers (hull, damage, loot per tier) live in `data/enemies.json` since #2971 (`EnemyDefinition`,
  `GameServer.NewEnemy`); the Codex's *Enemies* chapter shows the same file. A tier may also carry something only now
  and then (#3894, `rareLoot`: item, count and a `chance` of 0..1 — the hunter's `hacking_chip`, 0.08): the server rolls
  it once when it builds the enemy (`GameServerRareLoot.cs`) and adds it to that enemy's loot, so every loot path hands
  it out like the fixed loot.
- Large cruisers / boss events (§10.5), Alien UFO special behaviour (§10.4 beyond a flag).
- Module-level damage, overheating, ammo types beyond a simple energy cost (§7.6, §8.4).
- Salvage / boarding of defeated ships (§8.5 hard-PvP path).
- Full flight physics, dogfight AI pathing — the client renders arcade flight; the server
  keeps a simple positional model.
- Unity flight/combat **UI is scaffold-only** in this milestone (hooks in `NetworkClient`).

## What ships in M19

- `docs/SPACE_COMBAT_CONCEPT.md` (this file).
- `ShipState` hull/shield + module-derived combat stats.
- Data: `ship_cannon_1`, `laser_cannon_2`, `asteroid_breaker`, `shield_generator`,
  `hull_plating` blueprints + recipes + modules, with en/de locale strings.
- Protocol: enter/leave-space, fire-weapon, attack-entity intents; space-state, entity-update,
  entity-destroyed, ship-combat-status, space-result notices + codec tags.
- `GameServerSpaceCombat` (instances, weapons, NPC drones, asteroids, ship defeat/recovery)
  and `GameServerEnemies` (planet enemies), both rule-gated and server-authoritative.
- Tests covering rule gating, firing, NPC damage, ship-defeat respawn, and planet enemies.
- Unity `NetworkClient` hooks for the new messages (rendering = later).
