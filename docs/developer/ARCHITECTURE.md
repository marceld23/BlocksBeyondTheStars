# Architecture — Blocks Beyond the Stars

> **Status: living architecture overview.** Last reviewed 2026-06-19, verified against the
> actual source (not just other docs). This is the high-level map; for the current Done/Open
> status see [../../TODO.md](../../TODO.md), and for the hard contributor rules see
> [../../AGENTS.md](../../AGENTS.md). Deep dives are linked from each section.

## High-level overview

Blocks Beyond the Stars is a block-based 3D space crafting game built as **two cooperating
runtimes** that share code:

- **The Unity 6 client (`client/`) is presentation and input.** It renders the voxel world,
  the ship, space, the UI and audio, and turns the player's actions into *intents*.
- **The .NET 8 server (`src/`) is the truth of the game world.** It owns the world, players,
  ships, inventory, crafting, combat, travel and persistence. It validates every intent and
  replies with authoritative *state*. No Unity runtime ever runs on the server.

This is the **golden rule** (see [../../AGENTS.md](../../AGENTS.md)): the client never decides
outcomes. The same model powers singleplayer (a child-process server on `127.0.0.1`),
LAN/self-hosting and multiplayer with one code path, and makes anti-cheat correct by
construction.

## Solution & projects

The .NET solution `BlocksBeyondTheStars.sln` holds eight projects under `src/` plus the
xUnit test project. Two projects target **`netstandard2.1`** so the *same compiled code* runs
both inside Unity and on the server; everything else targets **`net8.0`** (the Launcher
`net8.0-windows`).

| Project | Target | Role |
|---|---|---|
| `BlocksBeyondTheStars.Shared` | `netstandard2.1` | Data models, data-driven definitions, geometry, localization, protocol DTOs, game rules, story engine. Depends on nothing in the solution. |
| `BlocksBeyondTheStars.WorldGeneration` | `netstandard2.1` | Seed-deterministic chunk/galaxy/flora/settlement/creature generation. |
| `BlocksBeyondTheStars.Networking` | `netstandard2.1` | Transport abstraction + concrete transports, message classes, `NetCodec` registry. Refs LiteNetLib + MessagePack. |
| `BlocksBeyondTheStars.Persistence` | `net8.0` | `IWorldRepository` + SQLite implementation, savegame paths/snapshots. Refs Microsoft.Data.Sqlite. |
| `BlocksBeyondTheStars.GameServer` | `net8.0` (Exe) | Authoritative tick loop + console host. |
| `BlocksBeyondTheStars.Api` | `net8.0` (Web) | ASP.NET Core minimal-API admin/portal/distribution. |
| `BlocksBeyondTheStars.Tools` | `net8.0` (Exe) | Backup/export/debug CLI. |
| `BlocksBeyondTheStars.Launcher` | `net8.0-windows` | WinForms + Velopack instant-splash launcher that starts the Unity exe. |
| `tests/BlocksBeyondTheStars.Tests` | `net8.0` | xUnit tests across all server-side projects. |

**Dependency direction (no cycles)** — verified from the `.csproj` `ProjectReference`s:

```
                 Shared  ◄───────────────────────────────────────────────┐
                  ▲  ▲  ▲                                                  │
                  │  │  └──────────────┐                                   │
   WorldGeneration│  │ Networking      │ Persistence                      │
                  ▲  ▲                 ▲                                   │
                  │  │                 │                                   │
                  └──┴───── GameServer ┴───────────────                    │
                                  ▲                                        │
                                  │ (tests only)                          │
            Api ──► Shared, Persistence                                    │
            Tools ──► Shared, Persistence, WorldGeneration ───────────────┘
            Launcher ──► (no solution refs; Velopack only)
```

`Shared` is the universal leaf. `WorldGeneration`, `Persistence` and `Networking` each depend
only on `Shared`. `GameServer` composes all four. The Unity client consumes the
`netstandard2.1` DLLs (`Shared`, `WorldGeneration`, `Networking`) — keeping those projects
netstandard-clean is mandatory (records/`init` work there via the `IsExternalInit` polyfill in
`Shared/Compatibility`).

## Server runtime

`GameServer` is a single, large `sealed partial class` split across ~70 `GameServer*.cs`
feature files. It is **single-threaded and tick-driven** by design (`GameServer.Run` /
`GameServer.Tick`).

- **Host** — `GameServer/Program.cs` loads `config/server.json` (with CLI overrides such as
  the client's `--port/--saves/--data/--usercontent`), loads data-driven content, opens the
  SQLite repository, builds the transport, then `Start()` + `Run()`. `Ctrl-C` only *requests*
  a stop; the run loop drains and saves on the tick thread so a save never races a live tick.
- **Tick loop** — `Run()` sleeps to a configured `TickRate`. Each `Tick(dt)` polls the
  transport, ticks each *occupied* world with an "active world cursor" set (environment, fauna,
  enemies, presence, fluids, fire, …), and periodically autosaves. A "ship cursor" + "world
  cursor" select the player currently being served, so per-player ship/world state resolves
  through forwarding properties.
- **Networking** — `Networking/Transport/` defines `IServerTransport`/`IClientTransport`
  carrying raw `NetCodec`-encoded payloads; events fire during `Poll()` so the server stays
  single-threaded. Concrete transports: `LiteNetLibTransport` (UDP, the Windows client),
  `WebSocketServerTransport` (browser clients, same protocol/port), `LoopbackTransport`
  (in-process), and `CompositeServerTransport` (runs UDP + WebSocket together). UDP is the
  default; WebSocket is opt-in (`EnableWebSocket`).
- **Persistence** — `Persistence/SqliteWorldRepository.cs` behind `IWorldRepository`. SQLite
  in **WAL** mode (`synchronous=NORMAL`), portable. Stores world
  metadata, **only player block-edit deltas** (`block_edit`, keyed by planet+xyz with
  tint/glow/shape), player/ship JSON blobs, containers, doors, beacons, beams, bases,
  alliances, story state, space structures + their per-cell edits, location statuses and
  player/admin missions. `RunInTransaction` batches bursty writes into one commit; autosave +
  atomic backups via `CreateBackup`. A PostgreSQL impl can be added without touching the
  server. See [SELF_HOSTING.md](SELF_HOSTING.md).
- **WorldGen** — `WorldGeneration/WorldGenerator.cs` is **seed-deterministic**: given a seed,
  `PlanetType` and `ChunkCoord` it always yields the same blocks, so the procedural baseline is
  never stored. `UniverseGenerator` builds the galaxy of systems/bodies from the seed; flora,
  settlements, stations, creatures, wrecks and landing-pad flattening are all deterministic
  too. World circumference varies per body (asteroid/moon/planet) and drives noise + longitude
  wrap. See [MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md).
- **API** — `Api/Program.cs` is a minimal ASP.NET Core app over a server install dir, bound to
  localhost/LAN by default. `X-Admin-Password` gate on `/api/*`. Routes: `/` + `/api/*` admin
  dashboard (status/config/backups/logs/missions/content-pack), `/portal` landing page,
  `/download` (newest `*Setup.exe`, range-resumable), `/play` (future WebGL), `/updates`
  (Velopack auto-update feed). See [SELF_HOSTING.md](SELF_HOSTING.md).

## Client runtime

The Unity client lives in `client/Assets/BlocksBeyondTheStars/`. Key wiring (all under
`Scripts/`):

- **`AppShell.cs`** — front-end state machine (splash → menu → settings → save-select →
  loading → in-game); owns `ClientSettings` + `Localizer` and the `LocalServerLauncher`.
- **`WorldRig.cs`** — code-only scene builder that constructs the in-game rig: server link +
  `GameBootstrap`, first-person player, HUD, post FX, views; tears down on return to menu.
- **`GameBootstrap.cs`** — creates the `NetworkClient`, loads `GameContent` from
  StreamingAssets, builds the procedural `BlockTextureAtlas`, and subscribes to all server
  events.
- **Voxels** — `ChunkMesher.cs` builds per-chunk meshes with face culling, opaque/transparent
  submeshes, per-voxel tint/glow flood-fill (TEXCOORD3) and non-cube block shapes, plus a
  separate collision mesh; `BlockTextureAtlas.cs` paints a 16×16-tile atlas (+ derived normal
  map) entirely in code; `ClientWorld.cs` caches received chunks read-only.
- **UI** — **uGUI / Canvas-based** (a procedural sci-fi toolkit in `UiKit.cs`, not raw IMGUI):
  `HudUi`, `UiMainMenu`, `UiSettings`, `UiSaveSelect`, `UiLoading`, the in-game editors,
  `CraftingTechShipUI`, `ChatUi`, `ScreenLabelLayer` (nameplates). See
  [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md).
- **`SpaceView.cs`** — the free-flight space scene and the **"ship as a place" model**: the own
  ship is a client-side voxel grid you can pilot, EVA, walk and rebuild; parked ships render via
  `LandedShipView` (not part of the world block grid). See
  [STATION_AS_LOCATION.md](STATION_AS_LOCATION.md).
- **Presence & A/V** — `RemotePlayers.cs`, `NpcView.cs`, `CreatureView.cs` interpolate other
  actors toward the latest authoritative position; `ClientAudio.cs`/`ClientMusic.cs` route SFX +
  context music.
- **Rendering** — **URP with custom unlit shaders** in `Shaders/` (`BlockAtlas[.Transparent]`,
  `Starfield`, `Nebula`, `Atmosphere`, `SkyBodyPhase`, `HeatHaze`, `Visor`, post-process
  passes). The block shaders bypass the standard light loop; the URP asset has the opaque +
  depth textures enabled for screen-space effects. See [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md)
  and [ADVANCED_GRAPHICS.md](ADVANCED_GRAPHICS.md).
- **Hosting (Option A)** — `LocalServerLauncher.cs` starts the bundled server exe from
  `StreamingAssets/server/` as a child process bound to loopback, with the same CLI args used
  in dedicated hosting. Singleplayer and multiplayer run the *exact same* authoritative server.

For the client packaging/shell + asset pipeline see
[CLIENT_SHELL_AND_ASSETS.md](CLIENT_SHELL_AND_ASSETS.md) and [DEVELOPER.md](DEVELOPER.md).

## Shared data model & data-driven content

Content is **data-driven** (rule 3 in AGENTS.md): blocks, items, recipes, blueprints, ships,
ship modules, planets, missions and station/settlement templates live in `data/*.json`, not in
logic. `Shared/Content/ContentLoader.cs` loads + validates them into `GameContent`; adding
content should not require touching game logic. User-authored structure templates dropped under
a `usercontent/` dir by the in-game editors are merged into the pools automatically.

In-game text is **never hardcoded**: it uses localization keys resolved against
`data/locales/en.json` + `de.json` (`Shared/Localization`, English fallback). All docs/code
comments are English; player-facing text is bilingual.

## Networking protocol (intents → state)

`Networking/NetCodec.cs` is a stable **tag↔type registry**: each payload is a one-byte
message-type tag followed by a MessagePack *contractless* body (no serialization attributes,
compact wire format). Ids are append-only and never reused; a message class that isn't
`Register()`'d silently fails to send.

- **Client → server = intents** (tags 1+): `JoinRequest`, `MoveIntent`, `MineBlockIntent`,
  `PlaceBlockIntent`, `CraftIntent`, `DockRequestIntent`, `TravelIntent`, `FireWeaponIntent`, …
- **Server → client = state** (tags 50+): `JoinAccepted`, `ChunkDataMessage`, `BlockChanged`,
  `InventoryUpdate`, `PlayerStateUpdate`, `CraftResult`, `ActionRejected`, …

`GameServer.OnPayload` decodes, rejects gameplay intents before join, sets the world+ship
cursor to the sender, and dispatches inside a try/catch so one bad handler can't take down the
single-threaded tick. **World = seed + parameters + deltas**: clients receive generated chunks
plus persisted edits, never the full natural world.

## Multi-world & instancing

`WorldManager` + `LoadedWorld` let several voxel worlds be resident at once — one per occupied
celestial body — each with isolated runtime state (fauna, enemies, NPCs, flora, fluids, fire,
containers, stamped structures, landing pads). The tick iterates *occupied* worlds with an
active-world cursor; with a single player it collapses to one world and behaves like a flat
tick. Space "instances" are keyed by location and tick separately (`TickSpace`). See
[MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md).

## Self-hosting & multiplayer topology

One binary serves every topology:

- **Singleplayer** — client spawns the bundled server on `127.0.0.1` via
  `LocalServerLauncher`; stops it on exit.
- **LAN / dedicated** — run `BlocksBeyondTheStars.GameServer` directly (Windows, Linux x64,
  Linux ARM64 / Raspberry Pi 5 — no rendering/physics on the server). Optional WebSocket
  gateway on the gameplay port for browser clients. `BlocksBeyondTheStars.Api` provides admin +
  the client download/update feed.

Details: [SELF_HOSTING.md](SELF_HOSTING.md). Browser/WebGL feasibility:
[WEBCLIENT_FEASIBILITY.md](WEBCLIENT_FEASIBILITY.md).

## AI backend (optional)

`ai-backend/` is a **separate, optional** Python LLM service for mission generation and NPC/
ship flavour text. The C# server stays authoritative: `IAiMissionProvider` selects
`HttpAiMissionProvider` only when `AiLevel != Off`, otherwise a deterministic template/null
provider — so the game runs fully offline. Whatever the service returns is validated + clamped
by the server. See [AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md) and
[../../ai-backend/README.md](../../ai-backend/README.md).

## Key invariants / golden rules

1. **Server authoritative; client = presentation + intents.** Never make the client decide
   resources, inventory, crafting, ship, oxygen, damage, blueprints or travel.
2. **World = seed + parameters + deltas.** Persist only player changes, never natural blocks.
3. **Data-driven content** in `data/*.json`; adding content shouldn't touch logic.
4. **Keep `Shared`/`WorldGeneration` netstandard2.1-clean** so Unity can consume them.
5. **Atomic saves** (temp-then-swap), autosave + rotating backups, low CPU/RAM/disk for Pi.
6. **Single-threaded, tick-driven server**; handlers must not crash the tick.
7. **Append-only `NetCodec` ids**; new message classes must be `Register()`'d.
8. **Docs/comments English; in-game text bilingual** via locale keys.

## Where to find what

| Path | Contents |
|---|---|
| `src/BlocksBeyondTheStars.Shared/` | Models, definitions, geometry, localization, story engine, game rules |
| `src/BlocksBeyondTheStars.WorldGeneration/` | Deterministic world/galaxy/flora/settlement/creature gen |
| `src/BlocksBeyondTheStars.Networking/` | Transports, `Messages*`, `NetCodec` |
| `src/BlocksBeyondTheStars.Persistence/` | `IWorldRepository`, SQLite repo, save paths |
| `src/BlocksBeyondTheStars.GameServer/` | `GameServer*` partials, tick loop, host (`Program.cs`) |
| `src/BlocksBeyondTheStars.Api/` | Admin UI, portal, download/update feed |
| `src/BlocksBeyondTheStars.Tools/` | Backup/export/debug CLI |
| `src/BlocksBeyondTheStars.Launcher/` | Instant-splash Velopack launcher |
| `client/Assets/BlocksBeyondTheStars/Scripts/` | Unity client (`AppShell`, `WorldRig`, `GameBootstrap`, `ChunkMesher`, `HudUi`, `SpaceView`, `NetworkClient`, `LocalServerLauncher`, …) |
| `client/Assets/BlocksBeyondTheStars/Shaders/` | URP custom shaders |
| `data/` + `data/locales/` | Data-driven content + localization |
| `ai-backend/` | Optional Python LLM service |
| `tests/BlocksBeyondTheStars.Tests/` | xUnit tests |
| `docs/` | This file + the detail docs linked above |
| `scripts/` | `build-client.ps1` + publish scripts |
| `../TODO.md` · `../AGENTS.md` | Status (single source of truth) · contributor rules |
