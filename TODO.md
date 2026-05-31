# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **86 passing**. Repo pushed to `origin/main` (private).

## Done (committed & pushed)

| Milestone | Summary |
|---|---|
| M0–M8 | .NET solution; Shared data model + data-driven content + bilingual i18n; seed world gen; SQLite persistence; networking (LiteNetLib + loopback + MessagePack codec); authoritative game server (tick loop, mine/place/craft/blueprint validators); admin API; self-hosting publish scripts; Unity client scaffold |
| M9 | Game modes (Survival/Creative) + server rules + presets |
| M10 | Heal-tank respawn in Medbay + death penalty + salvage capsule |
| M11 | World description + deterministic procedural universe + star map |
| M12 | Admin roles + server-authoritative logged cheats |
| M13 | Mission system (no AI): system + player missions, reward depot |
| M14 | Admin extension editor + content packs (missions) |
| M15 | WebSocket gateway + composite transport + web portal + WebGL feasibility doc |
| M16 | Optional Python AI mission backend (off by default) + decision doc |
| M17 | Personal landing zones + extended space settings (rules) |
| M18 | Ship docking: request/response/undock intents, handshake gated by `ShipDocking` rule, `docking_module` requirement, guest access, undock-on-disconnect |
| M19 | Free space flight + combat (PvE slice): concept doc, ship hull/shield, ship-weapon blueprints/modules, local space instances, NPC drones + asteroids, planet enemies — all rule-gated, no permanent ship loss; ship-module build flow |

Also done: MIT license, father-son README, all spec docs moved to local `plans/`,
commit author = `marceld23 <marcel.duetscher@gmail.com>`.

### M18 notes (implementation details)

- Messages: `DockRequestIntent`/`DockResponseIntent`/`UndockIntent` (client→server, codec
  tags 14–16); `DockRequestNotice`/`DockStatus` (server→client, tags 64–65).
- [GameServerDocking.cs](src/Spacecraft.GameServer/GameServerDocking.cs): `RequestDock` /
  `RespondDock` / `Undock` / `AreDocked` / `HasGuestAccess`; gated by `Rules.ShipDocking`
  (Off→reject, Free→auto-accept, RequestRequired/FriendsOnly→handshake); requires the shared
  ship's `docking_module`; `ClearDocking` on disconnect. FriendsOnly currently behaves like
  RequestRequired until a friends system exists.
- Dockings are in-memory only (they can't outlive a session → no `docking` table needed).
- `GameServer.AddLocalPlayer(name)` adds a joined session with a synthetic negative
  connection id, so 2-player docking is testable despite the single-client `LoopbackLink`.
- Tests in [DockingTests.cs](tests/Spacecraft.Tests/DockingTests.cs): request→accept,
  reject, Off-rejects, Free auto-dock, undock, missing-module, disconnect-undocks.

### M19 notes (implementation details)

- Concept-first: [docs/SPACE_COMBAT_CONCEPT.md](docs/SPACE_COMBAT_CONCEPT.md) — a small
  server-authoritative **PvE slice**; PvP ship combat + per-player ships deferred (one shared
  ship today). No permanent ship loss (§8.5).
- `ShipState.Hull`/`Shield` (persisted); maxima derived from modules (`hull_plating`,
  `shield_generator`) in [GameServerSpaceCombat.cs](src/Spacecraft.GameServer/GameServerSpaceCombat.cs).
- Data: `hull_plating`, `shield_generator`, `asteroid_breaker`, `ship_cannon_1`,
  `laser_cannon_2` blueprints + modules + en/de locales. Weapon modules carry `weapon_*` stats
  (`weapon_class` 0=tool, 1=combat).
- Messages: build-module / enter-space / leave-space / fire-weapon / attack-entity intents
  (tags 17–21); ship-combat-status / space-state / entity-destroyed / space-closed /
  planet-enemy-list / planet-enemy-defeated (tags 66–71).
- [GameServerSpaceCombat.cs]: space instances (orbit) with asteroids + NPC drones/UFOs gated
  by `SpaceCombat`/`SpaceNpcEnemies`/`AlienUfos`; `FireWeapon` gated by `ShipWeapons` +
  `AsteroidDestruction` (tools may mine asteroids even when weapons are off, §7.4); shield-then-
  hull damage; `DisableShip` recovers the ship to base + respawns the player (no loss). Also the
  `BuildShipModule` flow (workshop + blueprint + materials), reusable for all modules.
- [GameServerEnemies.cs](src/Spacecraft.GameServer/GameServerEnemies.cs): planet enemies gated
  by `PlanetEnemies` + Survival (off in Creative, §12.4); timed spawn near surface players,
  proximity damage, `AttackEntity` to kill (tool-tier scaled) with loot.
- Tests in [SpaceCombatTests.cs](tests/Spacecraft.Tests/SpaceCombatTests.cs): free-flight
  gating, asteroid/drone spawn, asteroid breaker loot, weapons-off rejection, cannon kill,
  ship-defeat recovery, build-module flow, planet-enemy spawn/damage/kill.
- **Deferred** (see concept doc): PvP ship combat, per-player ships, large cruisers/bosses,
  module-level damage, ammo/energy/overheat, salvage/boarding, real flight physics, Unity
  flight/combat UI (only `NetworkClient` hooks added).

## Pending

- **M20** — Client shell, assets & UX. Decisions doc `docs/CLIENT_SHELL_AND_ASSETS.md`
  (splash/menu/settings/textures/sounds/asset structure, MVP-vs-later, placeholder
  strategy, NOTICES); Unity scaffold for splash/main menu/settings/loading.

## How to resume

```powershell
dotnet build Spacecraft.sln
dotnet test                      # expect all green (86)
git log --oneline -5             # latest = M19 free space flight + combat
```
Then start **M20** (client shell, assets & UX) — decisions doc
`docs/CLIENT_SHELL_AND_ASSETS.md`, then a Unity scaffold for splash/menu/settings/loading.
Note: this sandbox blocks real sockets, so live WebSocket/2-player network tests are
exercised via in-process/loopback, not real ports.
