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
| M20 | Client shell, assets & UX: decisions doc + NOTICES, bilingual shell locale strings, Unity scaffold (AppShell splash→menu→settings→loading→in-game, ClientSettings local persistence) |

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

### M20 notes (implementation details)

- Decisions doc [docs/CLIENT_SHELL_AND_ASSETS.md](docs/CLIENT_SHELL_AND_ASSETS.md) answers the
  `anf_textures.md` question catalogue (branding, splash, menu, settings, texture/audio style,
  asset folder layout, MVP-vs-later) + [NOTICES.md](NOTICES.md) (no bundled third-party assets
  yet; placeholders only; library attribution).
- Bilingual shell locale strings (`ui.splash.*`, `ui.menu.*`, `ui.settings.*`, `ui.loading.*`,
  `ui.credits.*`) in `data/locales/{en,de}.json`.
- Unity scaffold (IMGUI, presentation-only): `AppShell` (splash→menu→settings→loading→in-game
  state machine; owns localizer + settings; spawns `GameBootstrap`), `SplashScreen`, `MainMenu`,
  `SettingsScreen`, `LoadingScreen`, and `ClientSettings` (local JSON persistence; quality
  presets incl. Potato/Pi, audio buses, mouse, language, accessibility flags).
- Client-only settings never touch authoritative server rules. Real textures/models/audio,
  animated background, controller support and uGUI polish are deferred (asset-production later).

## Pending — finish the client into a playable game

Full roadmap: [docs/CLIENT_COMPLETION_PLAN.md](docs/CLIENT_COMPLETION_PLAN.md). The server is
feature-complete (M0–M19); the remaining work is client-side UI + rendering + scene wiring that
consumes protocol messages that already exist.

- **M21 — Playable vertical slice ⭐ — code DONE, needs in-Editor playtest.** `WorldRig` builds
  the in-game rig in code (player + camera + chunk material + HUD) from `AppShell.LaunchGame`;
  Singleplayer hosts the bundled server as a child process (Option A); robust join-on-connected
  + spawn snap; curated per-block palette + per-face shading via a built-in vertex-colour shader;
  settings applied (sensitivity/invert-Y/view distance); Esc returns to menu. Real 32×32 texture
  atlas deferred to M27. *Next: publish the local server + press Play to playtest.*
- **M22 — core gameplay UI — code DONE, needs playtest.** HUD hotbar (1-9 + scroll, drives the
  placed item via `SelectHotbarIntent`); `GameMenu` (Tab) with Inventory/cargo, Crafting,
  Tech (blueprint unlock) and Ship (module build) tabs, all over existing intents; server
  feedback as a HUD toast. IMGUI for now; uGUI polish + item drag-move deferred.
- **M23a — the ship as a place — code DONE, needs playtest.** Server stamps a walk-in voxel
  ship hull at the landing zone (player starts inside); `AboardShip` derived from being inside
  it (gates cargo/crafting); HUD shows "aboard" + a minimap/compass that always points to the
  ship with distance. `PlaceStarterShip` config flag (on by default). Hull is mining-protected.
  **Stations (M23a-2):** interior markers + "Press E" interaction — heal-tank heals, quarters
  sets respawn, workshop/cargo → Tab menu, cockpit → star map (soon). Server-validated.
- **M23b — player avatar, customization & third-person camera — code DONE, needs playtest.**
  Code-built blocky humanoid (`PlayerAvatar`), per-part colours in Settings (Character section),
  first/third-person toggle (V). Networked appearance (others see it) deferred to M24; armor
  overrides + animation later.
- **M23 — navigation, missions & feedback — code DONE, needs playtest.** Star-map tab (opened
  from the cockpit), mission-log tab (accept/turn-in), respawn/rules/craft feedback as HUD
  toasts — all over existing protocol.
- **M24** — multiplayer presence (render other players + nameplates), docking UI, join/host polish.
- **M24 — multiplayer presence — presence DONE (needs LAN playtest); docking-UI + join-polish
  pending.** Server broadcasts PlayerPresence (pos + colours) + PlayerLeft; clients send colours
  on join; `RemotePlayers` renders other players as coloured avatars with nameplates. Still to
  do: docking request/accept UI, protocol-mismatch/disconnect handling in the shell.
- **M25 — space flight & combat client — code DONE, needs playtest.** Ship hull/shield HUD,
  Space console tab (launch/return + fire at entities), planet enemies rendered as blocks +
  attack with F. Singleplayer enables free flight + PvE via launcher flags. 3D flyable cockpit
  deferred.
- **(NEW, planned) M25b — real space view + launch/landing sequences:** "Launch into space"
  should show an actual view — cockpit / third-person ship in a starfield space scene / or board
  & walk inside the sealed ship and use stations (V cycles). Plus scripted launch & landing
  animations. Client-side presentation over existing protocol. See CLIENT_COMPLETION_PLAN.
- **(NEW, planned) Ships: types/designs/expandable interiors/multiple owned + switching** —
  data-driven `ships.json`, craftable ship types, rooms-per-module interiors, owned-ships list
  with an active ship + `SwitchShipIntent`, a Hangar UI. See CLIENT_COMPLETION_PLAN.
- **M26 — audio — procedural SFX DONE.** Audio module enabled; `ClientAudio` plays code-generated
  tones for mine/place/craft/reject/ship-hit via the master×SFX bus. Recorded SFX + music later.
- **M27 — art, icons & polish — in progress.** Procedural **block texture atlas** done
  (`BlockTextureAtlas` + UV mapping + `Spacecraft/BlockAtlas` shader, no image files). Still:
  UI icons/symbols (can also be procedural), tool/hand visuals, mining feedback, uGUI polish.
- **M28** — Windows player build + optional WebGL Lite *(needs a Unity batchmode build script)*.
  Note: real hand-/AI-authored art & audio files remain an asset task; everything so far is
  generated in code (textures, avatars, SFX) — no bundled binary assets.

Later/optional: Option B true in-process SP server (retarget to netstandard2.1); per-player ships
+ PvP ship combat.

## How to resume

```powershell
dotnet build Spacecraft.sln
dotnet test                      # expect all green (86)
git log --oneline -5             # latest = M20 client shell, assets & UX
```
All milestones from the local plan (M0–M20) are now implemented on the server/shared side
with a Unity scaffold. Next work is open scope (see Pending). Note: this sandbox blocks real
sockets, so live WebSocket/2-player network tests are exercised via in-process/loopback, not
real ports; the Unity client scripts are not compiled by `dotnet` (Editor required).
