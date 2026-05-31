# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **75 passing**. Repo pushed to `origin/main` (private).

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

## Pending

- **M19** — Free space flight + combat + enemies. *Concept-first*: write
  `docs/SPACE_COMBAT_CONCEPT.md`, then a server MVP (local space instances, ship-weapon
  blueprints, shield/hull, simple NPC drones + planet enemies gated by M17 rules, no
  permanent ship loss). Unity flight/combat UI = scaffold only.
- **M20** — Client shell, assets & UX. Decisions doc `docs/CLIENT_SHELL_AND_ASSETS.md`
  (splash/menu/settings/textures/sounds/asset structure, MVP-vs-later, placeholder
  strategy, NOTICES); Unity scaffold for splash/main menu/settings/loading.

## How to resume

```powershell
dotnet build Spacecraft.sln
dotnet test                      # expect all green (75)
git log --oneline -5             # latest = M18 ship docking
```
Then start **M19** (free space flight + combat + enemies) — concept-first: write
`docs/SPACE_COMBAT_CONCEPT.md`, then a server MVP. Note: this sandbox blocks real sockets, so
live WebSocket/2-player network tests are exercised via in-process/loopback, not real ports.
