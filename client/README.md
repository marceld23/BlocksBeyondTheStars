# Spacecraft Unity Client

The Unity client is **presentation and input only** — it renders the world the .NET server
reports and sends player *intents*. It never decides game outcomes.

## Requirements

- Unity **2022.3 LTS** (see `ProjectSettings/ProjectVersion.txt`).
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

## Scene setup

1. Open the project in Unity Hub (point it at this `client/` folder).
2. Create a scene with:
   - An empty GameObject with **`GameBootstrap`** (set Host/Port/PlayerName; assign a
     material for `ChunkMaterial`; tick **German** for the German locale).
   - A player GameObject with a **`CharacterController`**, a child **Camera**, and the
     **`PlayerController`** component (assign `Game` = the bootstrap object and `Camera`).
   - The **`Hud`** component (assign `Game`).
3. Press Play. The client connects, joins, receives chunks, meshes them, and you can walk
   around and left/right-click to mine/place (the server validates every action).

## Scripts (`Assets/Spacecraft/Scripts/`)

| Script | Role |
|---|---|
| `NetworkClient` | Wraps the shared transport + codec; sends intents, raises typed state events |
| `ClientWorld` | Client-side cache of server chunks (a view, not the source of truth) |
| `ChunkMesher` | Builds a culled block mesh for a chunk |
| `GameBootstrap` | Loads content, connects, turns chunk/state messages into rendered objects |
| `PlayerController` | FPS movement + raycast mine/place (sends intents only) |
| `Hud` | Minimal localized HUD for health/oxygen/energy |

## What is intentionally minimal here

Textures (blocks use placeholder colours), full uGUI/UI Toolkit inventory/crafting/star-map
screens, client-side prediction, and audio. The networking, world streaming, localization
and authoritative-action flow are wired and working.
