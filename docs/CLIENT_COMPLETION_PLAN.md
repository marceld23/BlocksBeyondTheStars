# Client Completion Plan — from scaffold to a playable game

Roadmap for finishing the Unity client. **Goal: a playable game** — launch the client,
start singleplayer (or join a server), and actually play the loop the server already
supports: explore, mine, craft, build/upgrade the ship, take missions, fly into space,
fight, dock with friends.

The headline fact that shapes this plan: **the server is essentially feature-complete and
fully authoritative (M0–M19).** Almost everything below is *client-side UI + rendering +
scene wiring* that consumes protocol messages **that already exist**. We are not inventing
gameplay; we are giving the player eyes, hands and menus for systems the server already runs.

## Definition of "playable"

A first **vertical slice** (M21) is the bar for "it's a game you can actually play":

> From the main menu, click **Singleplayer** → a world loads → you walk around a textured
> blocky planet, mine and place blocks, and your inventory/vitals update — all validated by
> the authoritative server. Quit back to the menu cleanly.

Everything after M21 deepens that loop (crafting/ship/space/multiplayer/audio/art) until it
is a *complete* game.

## Current state (what already exists)

- **Server:** world gen, mining/placing, crafting + blueprints, ship modules + build flow,
  cargo, missions, star map, docking, free-space flight + PvE combat, planet enemies, admin —
  all unit-tested (86 tests). Talks an authoritative intent→state protocol (`NetCodec`).
- **Client scaffold:** `NetworkClient` (transport + codec + typed events for every message),
  `ClientWorld` (chunk cache), `ChunkMesher` (per-face culling, **placeholder colours**),
  `GameBootstrap` (connect/join, mesh chunks, own vitals), `PlayerController` (WASD + look +
  jump + raycast mine/place, `PlaceItem` hardcoded), `Hud` (vitals IMGUI), and the M20 shell
  (`AppShell` splash→menu→settings→loading→in-game, `ClientSettings`).

## Gaps to "complete"

1. No assembled **in-game scene** (AppShell spawns a bare `GameBootstrap` with no player rig,
   camera, light, chunk material or HUD).
2. **Singleplayer** currently means "connect to 127.0.0.1" — no server is actually hosted by
   the client (see the architecture decision below).
3. **Placeholder colours**, no texture atlas.
4. No gameplay **UI**: hotbar, inventory, crafting/tech tree, ship-module build, cargo, star
   map, mission log, docking prompts, space-combat HUD.
5. **Settings not applied** (view distance, mouse sensitivity, fullscreen, language→already
   wired; the rest not).
6. No **other-player** rendering (multiplayer presence), no nameplates.
7. No **space-flight/combat** or **planet-enemy** rendering (server M19 hooks exist in
   `NetworkClient`, nothing draws them yet).
8. No **audio** (the audio module isn't even referenced; shell is silent by design).
9. No **player build** (.exe) / distribution.

## Key architecture decision — Singleplayer hosting

`Spacecraft.Shared` / `WorldGeneration` / `Networking` are **netstandard2.1** (already loaded
in Unity). But `Spacecraft.GameServer` and `Spacecraft.Persistence` are **net8.0** and
Persistence uses **native SQLite**, so they cannot simply be dropped into the Unity (Mono)
runtime. Two ways to make "Singleplayer" host a server:

- **Option A — bundle the server, launch as a child process (recommended for MVP).** Ship the
  published `Spacecraft.GameServer` executable inside the client (e.g. `StreamingAssets/server/`).
  On "Singleplayer", start it bound to `127.0.0.1` on a free port, then connect the normal
  `NetworkClient` to it; stop it on quit. **Pro:** uses the real server unchanged, works today,
  identical behaviour to multiplayer. **Con:** a child process + a few hundred ms startup.
- **Option B — true in-process server (later optimization).** Retarget `GameServer` +
  `Persistence` to `netstandard2.1`, add a Unity-safe persistence backend (in-memory or a
  managed SQLite / file store, since native `e_sqlite3` is awkward under Unity), and run the
  server in-process over the existing `LoopbackServerTransport`/`LoopbackClientTransport`.
  **Pro:** no child process, instant, single binary. **Con:** real porting work + a second
  persistence implementation.

**Plan: ship Option A in M21**; revisit Option B only if the child process proves
problematic. Multiplayer ("Join Server") already works against the standalone server today.

## Phased roadmap

Each phase is independently shippable and leaves the game in a working state. Phases are
ordered for the **fastest path to fun**; M21 is the milestone that makes it "a game".

### M21 — Playable vertical slice ⭐
- Assemble one **in-game scene/prefab**: player (CharacterController + camera + `PlayerController`),
  directional light, a **chunk material**, and the `Hud`, all spawned by `AppShell.LaunchGame`
  (replace the bare bootstrap).
- **Singleplayer via Option A**: a `LocalServerLauncher` that starts/stops the bundled server;
  publish step added to `scripts/` (extend `publish-server.ps1` / `sync-client-libs.ps1`).
- A first **block texture atlas (32×32)** + UV mapping in `ChunkMesher` (replaces flat colours).
- Apply core **settings**: mouse sensitivity → `PlayerController`, view distance → join/stream,
  fullscreen/quality on launch.
- Clean **return to menu** (disconnect, tear down world + local server).
- **Outcome:** menu → Singleplayer → walk/mine/place on a textured world → quit. *Playable.*

### M22 — Core gameplay UI (the survival/build loop)
- **Hotbar** (drives the selected item; replaces hardcoded `PlaceItem`) + `SelectHotbarIntent`.
- **Inventory** + **cargo** views (`InventoryUpdate`).
- **Crafting** + **blueprint unlock** UI (`CraftIntent`/`UnlockBlueprintIntent`,
  `CraftResult`), gated/feedback from server.
- **Ship-module build** UI (`BuildShipModuleIntent`) incl. weapons/defense from M19.
- Move from IMGUI to **uGUI/UI Toolkit** for these screens (HUD can stay light).

### M23 — Navigation, missions & feedback
- **Star map** screen (`RequestStarMap`/`StarMapData`).
- **Mission log**: list/accept/turn-in/create (`RequestMissions`, `Accept/TurnIn/CreateMissionIntent`,
  `MissionList`, `MissionResult`).
- **Death/respawn** feedback (`RespawnNotice`), **server rules** display (`ServerRules`),
  `ActionRejected`/`ServerMessage` toasts.

### M24 — Multiplayer presence
- Render **other players** from `PlayerStateUpdate` (avatar + nameplate), interpolation.
- **Docking** UI: request/accept/decline/undock (`DockRequest*`/`DockResponse*`/`Undock`,
  `DockRequestNotice`/`DockStatus`) — server M18 already enforces it.
- Join/host flow polish: **protocol-mismatch** warning (`JoinRejected`), disconnect/reconnect,
  server-unreachable handling.

### M25 — Space flight & combat (client for M19)
- **Enter/leave space** (`EnterSpaceIntent`/`LeaveSpaceIntent`, `SpaceState`/`SpaceClosed`).
- Render **space entities** (asteroids/drones/UFOs), a simple flight camera/controls.
- **Ship hull/shield HUD** (`ShipCombatStatus`), **fire weapons** (`FireWeaponIntent`,
  `SpaceEntityDestroyed`), ship-defeat/recovery feedback.
- **Planet enemies**: render + `AttackEntityIntent` (`PlanetEnemyList`/`PlanetEnemyDefeated`).

### M26 — Audio
- Add the Unity **audio module**; implement the master/music/sfx buses from `ClientSettings`.
- UI clicks, mining/placing, ship hums, docking, alarms, respawn, combat; menu music.
- Sourced as CC0/permissive only; record each in `NOTICES.md`.

### M27 — Art & polish
- Finalize the **texture atlas** (all material groups from `anf_textures.md`), module colour
  codes; first-person **tool/hand** visuals; mining/placing **feedback** (particles, outline).
- **Pause menu**, in-game settings, accessibility flags applied (reduced effects, larger UI),
  animated/main-menu background (optional).

### M28 — Build & distribution
- Windows **player build** (.exe) bundling the server (Option A), data and assets; first-run.
- Smoke-test checklist; optional **WebGL "Lite"** build (per `WEBCLIENT_FEASIBILITY.md`).
- Document the build steps; add a `scripts/build-client.ps1` if Unity CLI batchmode is used.

## Cross-cutting principles

- **Server stays authoritative.** The client only sends intents and renders state — never
  decides outcomes. Every new screen maps to existing intents/messages.
- **Bilingual UI** (DE/EN) via the `Localizer`; add `ui.*` locale keys as screens land
  (en/de parity is enforced by the content tests).
- **Asset discipline:** only permissive-licensed assets, each logged in `NOTICES.md`;
  synced/generated content stays git-ignored, project files stay versioned.
- **Testing:** server logic is already unit-tested; for the client add PlayMode/EditMode tests
  where they pay off (e.g. `ChunkMesher`, `ClientSettings`, UI view-models), plus a manual
  playtest checklist per phase. Keep the .NET suite green.

## Fastest path to "fun"

M21 → M22 → M24 gives a textured, build-and-mine multiplayer game with a real survival loop.
M23/M25 add depth (missions, space combat); M26/M27 add polish; M28 ships a build. If time is
tight, M21 alone is already a demoable, genuinely playable slice.

## Risks / open questions

- **SP hosting (A vs B):** child-process is fast to ship but means packaging the server with
  the client and managing its lifecycle; confirm acceptable before M21.
- **Native SQLite under Unity** is the main blocker for Option B — decide later.
- **UI tech:** IMGUI is fine for the shell/HUD but not for inventory/crafting; commit to
  uGUI or UI Toolkit at M22 and stay consistent.
- **Art scope:** a full atlas + audio is real production work; M21 ships a minimal atlas so the
  game is presentable without blocking on full art.
