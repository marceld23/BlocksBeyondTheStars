# Spacecraft — Progress & Next Steps

Resume point for development. Full milestone breakdown lives in `plans/IMPLEMENTATION_PLAN.md`
(local, git-ignored). Tests: **68 passing**. Repo pushed to `origin/main` (private).

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

Also done: MIT license, father-son README, all spec docs moved to local `plans/`,
commit author = `marceld23 <marcel.duetscher@gmail.com>`.

## In progress — M18: Ship docking

**Done so far:** docking blueprint + ship module (`docking_module`) added to
`data/blueprints.json`, `data/ship_modules.json`, and en/de locales (committed with this note).

**Remaining (start here tomorrow):**
1. Network messages: `DockRequestIntent {TargetPlayer}`, `DockResponseIntent {Requester, Accept}`,
   `UndockIntent` (client→server); `DockRequestNotice`, `DockStatus` (server→client) + codec tags.
2. `GameServerDocking.cs` (partial): pending-requests + active-dockings maps; methods
   `RequestDock(fromId,toId)`, `RespondDock(toId,fromId,accept)`, `Undock(id)`, `AreDocked(a,b)`;
   gate by `Rules.ShipDocking` (Off→reject, Free→auto-accept, RequestRequired/FriendsOnly→handshake);
   require ship module `docking_module`; guest-access flags; undock on disconnect.
3. Wire dispatch cases in `OnPayload`; persist active docking (optional `docking` table).
4. Add a **multi-client loopback** (or a public `AddLocalPlayer(name)` helper on GameServer)
   so 2-player docking can be unit-tested — current `LoopbackLink` is single-client.
5. Tests: request→accept docks both; reject; Off rejects; undock; disconnect undocks.

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
dotnet test                      # expect all green
git log --oneline -5             # latest = M18 data (docking module)
```
Then continue M18 from "Remaining" above. Note: this sandbox blocks real sockets, so
live WebSocket/2-player network tests are exercised via in-process/loopback, not real ports.
