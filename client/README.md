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

**One GameObject is all you need.** Create a scene with a single empty GameObject carrying
**`AppShell`** and press Play.

1. Open the project in Unity Hub (point it at this `client/` folder).
2. New empty scene → `GameObject → Create Empty` → `Add Component → AppShell`.
3. Press **Play**: splash → main menu (Singleplayer / Join / Settings / Credits / Quit).
   On **Singleplayer** (after `publish-local-server.ps1`) or **Join**, `AppShell` builds the
   whole in-game rig in code via **`WorldRig`** — server link (`GameBootstrap`), a chunk
   material from the bundled vertex-colour shader, a first-person player (CharacterController +
   camera + `PlayerController`) and the `Hud`. No manual player/camera/material wiring needed.

In-game: WASD + mouse to move/look, **left-click mine / right-click place**, **Esc** returns to
the menu (and stops the local singleplayer server). The server validates every action; the
client only renders the authoritative world it streams back.

> Use an otherwise-empty launcher scene — `WorldRig` disables any pre-existing scene cameras so
> only the player camera renders in-game.

## Building a Windows player (.exe)

For a self-contained singleplayer build, sync content + bundle the server, then build:

```powershell
../scripts/sync-client-libs.ps1
../scripts/publish-local-server.ps1
../scripts/build-client.ps1            # uses Unity 6000.4.x in batch mode
```

`build-client.ps1` runs `Spacecraft/Build Windows Player` (an editor menu item too) which
generates the launcher scene (one `AppShell` object) and writes `Build/Windows/Spacecraft.exe`.
Everything under `Assets/StreamingAssets/` (data + the published server) is bundled automatically.

## Scripts (`Assets/Spacecraft/Scripts/`)

| Script | Role |
|---|---|
| `AppShell` | Front-end state machine: splash → menu → settings → loading → in-game; owns local settings + localizer; starts/stops the local server for Singleplayer |
| `LocalServerLauncher` | Launches/stops the bundled dedicated server as a child process for Singleplayer (Option A) |
| `WorldRig` | Builds the in-game rig in code (player + camera + chunk material + HUD) so no manual scene wiring is needed |
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
