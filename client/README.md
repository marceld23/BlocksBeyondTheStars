# Spacecraft Unity Client

The Unity client is **presentation and input only** — it renders the world the .NET server
reports and sends player *intents*. It never decides game outcomes.

## Requirements

- Unity **6 LTS** (6000.4.9f1; see `ProjectSettings/ProjectVersion.txt`). The project was
  started on 2022.3 LTS and migrated to Unity 6 — on first open Unity finalizes the project
  files (and the exact editor-revision hash) automatically.
- A running Spacecraft server (`dotnet run --project ../src/Spacecraft.GameServer`).

## One-time setup

The client references the **same** shared libraries as the server. Build and import them,
and copy the data-driven content into `StreamingAssets`:

```powershell
../scripts/sync-client-libs.ps1
```

This places `Spacecraft.Shared.dll`, `Spacecraft.WorldGeneration.dll`,
`Spacecraft.Networking.dll` (and their dependencies) into `Assets/Plugins/`, and the
`data/` content (definitions + `locales/`) into `Assets/StreamingAssets/data/`.

> If Unity reports a duplicate of a `System.*` assembly it already ships, delete that one
> DLL from `Assets/Plugins/`.

### Singleplayer hosting (optional but recommended)

"Singleplayer" launches the bundled dedicated server as a child process bound to loopback
(Option A — `GameServer`/`Persistence` are net8.0 + native SQLite and can't run inside Unity).
Publish the server into the client once:

```powershell
../scripts/publish-local-server.ps1
```

This builds a self-contained `Spacecraft.GameServer` into `Assets/StreamingAssets/server/`.
The client reuses `StreamingAssets/data` and writes saves under the user's persistent data
path. Without this step, use **Join Server** against a manually started server instead.

## Scene setup

### Launcher scene (front-end shell)

1. Open the project in Unity Hub (point it at this `client/` folder).
2. Create a scene with a single empty GameObject carrying **`AppShell`**. Press Play: it shows
   the splash, then the main menu (Singleplayer / Join / Settings / Credits / Quit), reads and
   writes local settings, and spawns the in-game `GameBootstrap` on launch.

### In-game (driven by the shell, or set up directly for testing)

`AppShell.LaunchGame` adds a **`GameBootstrap`** configured from the menu + settings. To test
the world without the shell, build the scene manually instead:

   - An empty GameObject with **`GameBootstrap`** (set Host/Port/PlayerName; assign a
     material for `ChunkMaterial`; tick **German** for the German locale).
   - A player GameObject with a **`CharacterController`**, a child **Camera**, and the
     **`PlayerController`** component (assign `Game` = the bootstrap object and `Camera`).
   - The **`Hud`** component (assign `Game`).

Press Play. The client connects, joins, receives chunks, meshes them, and you can walk
around and left/right-click to mine/place (the server validates every action).

## Scripts (`Assets/Spacecraft/Scripts/`)

| Script | Role |
|---|---|
| `AppShell` | Front-end state machine: splash → menu → settings → loading → in-game; owns local settings + localizer; starts/stops the local server for Singleplayer |
| `LocalServerLauncher` | Launches/stops the bundled dedicated server as a child process for Singleplayer (Option A) |
| `ClientSettings` | Local-only display/audio/input/comfort settings, persisted as JSON |
| `SplashScreen` / `MainMenu` / `SettingsScreen` / `LoadingScreen` | IMGUI shell screens driven by `AppShell` |
| `NetworkClient` | Wraps the shared transport + codec; sends intents, raises typed state events |
| `ClientWorld` | Client-side cache of server chunks (a view, not the source of truth) |
| `ChunkMesher` | Builds a culled block mesh for a chunk |
| `GameBootstrap` | Loads content, connects, turns chunk/state messages into rendered objects |
| `PlayerController` | FPS movement + raycast mine/place (sends intents only) |
| `Hud` | Minimal localized HUD for health/oxygen/energy |

## What is intentionally minimal here

Real textures (blocks use placeholder colours), 3D models, audio/music, an animated menu
background, and full uGUI/UI Toolkit inventory/crafting/star-map screens. The shell uses IMGUI
like the HUD. The networking, world streaming, localization, the front-end shell flow and the
authoritative-action flow are wired and working. See `docs/CLIENT_SHELL_AND_ASSETS.md`.
