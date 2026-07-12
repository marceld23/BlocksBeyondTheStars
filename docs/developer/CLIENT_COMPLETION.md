# Client — how it works + what remains

Status: implemented (the game is playable; many features shipped) — see TODO.md for live Done/Open status. Date: 2026-06-19.

This doc describes how the Unity client is structured today and where its systems live. The headline
fact that shaped the whole effort: **the .NET server is feature-complete and fully authoritative
(M0–M19+).** Almost everything in the client is UI + rendering + scene wiring that consumes protocol
messages that already exist — the client sends intents and renders state, it never decides outcomes.

## Overview

From the main menu the player can start Singleplayer (or Host Game / Join Server), load into a textured
voxel planet, walk/mine/place/craft, build and fly a ship, take missions, fly into space, fight, and
dock with friends — all validated by the authoritative server. The original "vertical slice" bar (menu →
Singleplayer → walk/mine/place on a shaded world → Esc → menu) was cleared long ago; the work since has
deepened that loop across crafting, ships, space, multiplayer, audio and art.

## How it works

**Scene wiring.** `AppShell` runs the shell flow (splash → menu → settings → loading → in-game) and, on
launch, `WorldRig` builds the entire in-game rig **in code** — server link (`GameBootstrap`), chunk
material, first-person player (CharacterController + camera + `PlayerController`), HUD. The launcher
scene needs only an `AppShell` GameObject. Esc tears the world down cleanly (disconnect, stop local
server, unlock cursor).

**Singleplayer hosting = Option A (bundle the server, child process).** `BlocksBeyondTheStars.Shared` /
`WorldGeneration` / `Networking` are netstandard2.1 and load in Unity directly, but `GameServer` +
`Persistence` are net10.0 with native SQLite, so they cannot run in the Mono runtime. Instead the
published `GameServer` exe is bundled under `StreamingAssets/server/` (`scripts/publish-local-server.ps1`)
and `LocalServerLauncher` starts it bound to `127.0.0.1` on a free port, then the normal `NetworkClient`
connects; it is stopped on quit. This uses the real server unchanged, identical to multiplayer.
(Option B — a true in-process server retargeted to netstandard2.1 — was left as a later optimization.)
"Host Game" (open-to-LAN of the existing saves) and "Join Server" use the same client.

**Rendering.** `ChunkMesher` does per-face culling and UV-maps into the procedural `BlockTextureAtlas`
(a 16×16 grid of 64×64-px tiles, generated in code, plus a Sobel-derived normal atlas); blocks render
through the dual-pipeline `BlockAtlas` shader under
URP (see `URP_MIGRATION.md`). The player avatar, remote players, creatures, NPCs and enemies are
code-built blocky models.

**Gameplay UI** is IMGUI today: a hotbar (1–9 + scroll, drives `SelectHotbarIntent`) and a `GameMenu`
(Tab) with Inventory+cargo, Crafting, Tech (blueprint unlock), Ship (module build), Star map, Mission
log, and a Space console (launch/return, entity list with Fire). Server feedback (`CraftResult`,
`ActionRejected`, `RespawnNotice`, `ServerRules`, `ServerMessage`) surfaces as HUD toasts. All strings
are bilingual DE/EN via the `Localizer` (en/de parity enforced by content tests).

**The ship as a place.** The server stamps a hollow walk-in voxel ship hull at the landing zone; it
streams as normal chunks and the player starts inside. `AboardShip` is derived authoritatively from
standing in the hull and gates cargo crafting / module build / oxygen regen. A `ShipPlacement` message
drives a HUD compass to the ship; the hull is mining-protected. Stations (cockpit/medbay/workshop/cargo/
quarters) send positions (`ShipStations`); standing next to one shows "Press E" and sends a
`UseStationIntent` the server validates by proximity + module.

**Space.** `SpaceView` builds a space scene on entry (black sky + starfield + planets + a code-built
flyable ship + the server's `SpaceState` entities), takes over the camera, and flies with WASD+mouse in
third-person / cockpit (V cycles); on-foot is frozen. Launch / landing sequences play on enter/return
(landing is now a fly-toward-planet shrink via `SpaceView.BeginLandingFlyAway`). Combat uses the
authoritative fire/hull/shield messages. Planet enemies render in 3D from `PlanetEnemyList` and are
attacked with F.

**Multiplayer presence.** The server broadcasts `PlayerPresence` (~10 Hz) with position + heading +
avatar colours; `RemotePlayers` renders each other player as a coloured blocky avatar with a nameplate,
interpolated to the latest position.

**Audio.** The Unity audio module is enabled with an `AudioListener` on the player camera; master/SFX/
music volumes come from `ClientSettings` via `ClientAudio`. Procedural SFX are generated in code (mining
vs placing, craft success/failure, rejection, ship hit) and music cross-fades by context (menu / planet /
space / combat) with ElevenLabs + Suno tracks and procedural fallbacks.

## Server-side feature slices (client wiring partly pending)

These gameplay systems are implemented + unit-tested on the **server**; many need their client UI/render
to be finished (tracked in TODO.md): docking UI; tractor-beam VFX + cargo-fill HUD; lootable
containers/corpses; landable asteroids travel-to-land; space stations (boardable slice done — client
docking/board UI + radar pending); planet settlements + crashed-ship wrecks (generator + stamping +
repair/claim done — NPC render/behaviour + client repair panel pending); player-to-player trading
(server done — two-column trade panel pending); gear disassembly (server done — crafting-tab button
pending); fluids / weather / day-night (server + client tint done — voxel clouds, rain/lightning
particles, ambient weather sound pending); multiple owned ships (slice done — fleet persistence +
expandable interiors pending); hyperspace system-to-system travel (planned).

## Key files

- `client/Assets/BlocksBeyondTheStars/Scripts/WorldRig.cs` — builds the in-game rig in code.
- `client/Assets/BlocksBeyondTheStars/Scripts/SpaceView.cs` — space scene, flight, launch/landing.
- `client/Assets/BlocksBeyondTheStars/Scripts/ClientSettings.cs` — settings + per-preset graphics.
- `client/Assets/BlocksBeyondTheStars/Editor/BuildScript.cs` — `BuildWindows` player build + always-included shaders.
- `scripts/build-client.ps1` — one-command build (sync libs + publish server + headless Unity batch build).
- `scripts/sync-client-libs.ps1`, `scripts/publish-local-server.ps1` — build prerequisites.

## Known gaps / deferred

- **Build & distribution:** the tooling is in place (`BuildScript.BuildWindows` +
  `scripts/build-client.ps1`); what remains is running it on a machine with Unity 6000.4.x to produce
  the self-contained `.exe`, then a smoke-test checklist + first-run polish. An optional WebGL "Lite"
  build is analysed in `WEBCLIENT_FEASIBILITY.md`.
- **Art polish (M27):** real UI icons replacing text/emoji placeholders; first-person held-tool
  viewmodel + use animations (mining swing, fire, etc.); avatar walk/idle animation + swappable cosmetic
  shapes; the broader unified sci-fi UI concept (`UI_AND_RENDER_CONCEPT.md`).
- **UI tech:** core UI is still IMGUI; a uGUI / UI Toolkit pass is the planned polish step.
- **Architecture note:** Option B (in-process server) is only revisited if the child process proves
  problematic; native SQLite under Unity is the main blocker there.

## Cross-cutting principles

- **Server stays authoritative** — the client only sends intents and renders state; every screen maps to
  existing intents/messages.
- **Bilingual UI** (DE/EN) via the `Localizer`; en/de key parity is enforced by content tests.
- **Asset discipline:** only permissive-licensed assets, each logged in `NOTICES.md`; synced/generated
  content stays git-ignored, project files stay versioned.
- **Testing:** server logic is unit-tested (keep the .NET suite green); add client EditMode/PlayMode
  tests where they pay off (`ChunkMesher`, `ClientSettings`, UI view-models) plus a manual playtest pass.
