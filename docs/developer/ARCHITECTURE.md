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
- **The .NET 10 server (`src/`) is the truth of the game world.** It owns the world, players,
  ships, inventory, crafting, combat, travel and persistence. It validates every intent and
  replies with authoritative *state*. No Unity runtime ever runs on the server.

This is the **golden rule** (see [../../AGENTS.md](../../AGENTS.md)): the client never decides
outcomes. The same model powers singleplayer (a child-process server on `127.0.0.1`),
LAN/self-hosting and multiplayer with one code path, and makes anti-cheat correct by
construction.

## Solution & projects

The .NET solution `BlocksBeyondTheStars.sln` holds twelve projects under `src/` plus two
xUnit test projects. `Shared`, `WorldGeneration` and `Networking` target **`netstandard2.1`** so
the *same compiled code* runs both inside Unity and on the server, and so does the Unity-free
client logic in `Client.Core`; `GameServer` and `Persistence` build a second `netstandard2.1`
flavour that the browser (WebGL) client runs as its in-process singleplayer server. Everything
else targets **`net10.0`** (the Windows launcher `net10.0-windows`).

| Project | Target | Role |
|---|---|---|
| `BlocksBeyondTheStars.Shared` | `netstandard2.1` | Data models, data-driven definitions, geometry, localization, protocol DTOs, game rules, story engine. Depends on nothing in the solution. |
| `BlocksBeyondTheStars.WorldGeneration` | `netstandard2.1` | Seed-deterministic chunk/galaxy/flora/settlement/creature generation. |
| `BlocksBeyondTheStars.Networking` | `netstandard2.1` | Transport abstraction + concrete transports, message classes, `NetCodec` registry. Refs LiteNetLib + MessagePack; WebGL uses a JSON envelope at the WebSocket edge. |
| `BlocksBeyondTheStars.Persistence` | `net10.0` + `netstandard2.1` | `IWorldRepository` + SQLite/PostgreSQL implementations, savegame paths/snapshots. Refs Microsoft.Data.Sqlite + Npgsql (`net10.0` only; the `netstandard2.1` flavour keeps the abstraction, the snapshots and the managed `MemoryWorldRepository`). |
| `BlocksBeyondTheStars.GameServer` | `net10.0` (Exe) + `netstandard2.1` | Authoritative tick loop + console host; the `netstandard2.1` library (without `Program.cs`) is the browser build's in-process server. |
| `BlocksBeyondTheStars.Client.Core` | `netstandard2.1` | Unity-free client logic (`NetworkClient` + its dispatch table, `ClientWorld`, interpolators, …), shared by the Unity player and the headless client tests. See [CLIENT_TESTING.md](CLIENT_TESTING.md). |
| `BlocksBeyondTheStars.Api` | `net10.0` (Web) | ASP.NET Core minimal-API admin/portal/distribution. |
| `BlocksBeyondTheStars.WorldHost` | `net10.0` (Web) | Hosted-worlds control plane: accounts, world registry, wake-on-demand and join tokens for one container per world, the public portal. See [HOSTED_WORLDS.md](HOSTED_WORLDS.md). |
| `BlocksBeyondTheStars.ReportHost` | `net10.0` (Web) | The bug-report inbox for F1 feedback and crash reports. See [REPORT_HOST.md](REPORT_HOST.md). |
| `BlocksBeyondTheStars.Tools` | `net10.0` (Exe) | Backup/export/debug CLI. |
| `BlocksBeyondTheStars.Launcher` | `net10.0-windows` | WinForms + Velopack instant-splash launcher that starts the Unity exe. |
| `BlocksBeyondTheStars.Launcher.Console` | `net10.0` (Exe) | Cross-platform console launcher for Linux (SkiaSharp splash, Velopack). See [LINUX_PORT.md](LINUX_PORT.md). |
| `tests/BlocksBeyondTheStars.Tests` | `net10.0` | xUnit tests across all server-side projects. |
| `tests/BlocksBeyondTheStars.Client.Tests` | `net10.0` | Headless client ↔ server tests: the real `NetworkClient` against the real in-process `GameServer`. |

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
            Client.Core ──► Shared, Networking, WorldGeneration
            WorldHost, ReportHost ──► Shared
            Launcher ──► (no solution refs; Velopack only)
            Launcher.Console ──► (no solution refs; Velopack + SkiaSharp)
```

`Shared` is the universal leaf. `WorldGeneration`, `Persistence` and `Networking` each depend
only on `Shared`. `GameServer` composes all four. The Unity client consumes the
`netstandard2.1` DLLs (`Shared`, `WorldGeneration`, `Networking`, `Client.Core`, and the
library flavours of `Persistence` + `GameServer`, which `scripts/sync-client-libs.ps1` copies into
`Plugins/`) — keeping those projects netstandard-clean is mandatory (records/`init` work there via
the `IsExternalInit` polyfill in `Shared/Compatibility`).

## Server runtime

`GameServer` is one `sealed partial class`: the core in `GameServer.cs` plus more than 200
`GameServer*.cs` feature partials. A feature slice with state of its own lives in a top-level
**subsystem class** instead (`CrewSystem`, `BioLabSystem`, `EquipmentSystem`, `MarkerSystem`,
`JournalSystem`, `TemperatureSystem`, `TutorialSystem`, `VehicleSystem`, `NpcWorkSystem`, … — the
`*System.cs` files of the project): it reaches the server only through its own host interface on
top of the shared `IServerHost`, its old `GameServerX.cs` file shrank to a facade that keeps every
name the other partials, the dispatch table, the tick schedule and the tests call (including the
`*ForTest` seams), and `GameServer.Systems.cs` holds them and builds them near the end of the
constructor (`ConstructSystems`, right before the tick schedules). The shape and the recipe are in
[ADR 0019](adr/0019-gameserver-subsystems.md) and
[DEVELOPER.md § Extracting a GameServer subsystem](DEVELOPER.md#extracting-a-gameserver-subsystem).
It is **single-threaded and tick-driven** by design (`GameServer.Run` / `GameServer.Tick`).

- **Host** — `GameServer/Program.cs` loads `config/server.json` (with `BBS_*` environment and CLI
  overrides such as the client's `--port/--saves/--data/--usercontent`; every option is one row of
  `ServerConfigOptions.All`), loads data-driven content, opens the configured repository, builds
  the transport, then `Start()` + `Run()`. `Ctrl-C` only *requests* a stop; the run loop drains and
  saves on the tick thread so a save never races a live tick.
- **Tick loop** — `Run()` sleeps to a configured `TickRate`. Each `Tick(dt)` polls the
  transport, ticks each *occupied* world with an "active world cursor" set (environment, fauna,
  enemies, presence, fluids, fire, …), and periodically autosaves. A "ship cursor" + "world
  cursor" select the player currently being served, so per-player ship/world state resolves
  through forwarding properties. **The order of the guarded tick systems is one table**,
  `GameServerTickSchedule.cs`, in four phases: `Held` (a paused world — only moderation,
  maintenance, the hosted lifecycle and spectator streaming run), `PreWorld`, `PerWorld` (once per
  occupied world, with the cursor set) and `PostWorld`, each an ordered list of named steps. The
  step names are the tick-timing and tick-fault bucket names, so they stay stable; a new system is
  one entry at the position it should run (`TickScheduleTests` pins the anchors, not the full order).
- **Sessions** — `PlayerSession` holds a connection's player and its core flags; the RAM-only
  bookkeeping of the subsystems is grouped into components (`session.Story`, `session.Npc`,
  `session.Streaming`, `session.Hazards`, … — one `PlayerSession<Group>.cs` each), never persisted.
- **Admin commands** — `GameServerAdminCommands.cs`: one table of verbs (`AdminCommandTable`), each
  behind one of three gates checked in order: the role (world admin, fleet admin, or a base owner
  asking `/basewalls`), then the fleet admin for `/spectate` and `/goto`, then the world's cheat
  rule for the cheat commands. A new command is one table line (plus a method when its body is more
  than one call) and its verb and gate in `AdminCommandGateTests`, which pins every verb's gate.
- **Block placement** — `GameServerPlacement.cs`: `HandlePlace` resolves the intent, then walks the
  ordered, named `PlacementRules`; the first rule that refuses wins and sends the one
  `ActionRejected` "place", so a refused placement costs nothing. Only then is the item paid for and
  the effect steps (cell writes, flora, block-kind hooks, Crystal Net and rail graph, broadcast,
  devices) run in a fixed order. `PlacementRuleOrderTests` pins the rule order.
- **Networking** — `Networking/Transport/` defines `IServerTransport`/`IClientTransport`
  carrying raw `NetCodec`-encoded payloads; events fire during `Poll()` so the server stays
  single-threaded. Concrete transports: `LiteNetLibTransport` (UDP, native clients),
  `WebSocketServerTransport` (browser clients, same gameplay port), `LoopbackTransport`
  (in-process), and `CompositeServerTransport` (runs UDP + WebSocket together). UDP is the
  default; WebSocket is opt-in (`EnableWebSocket`). Browser clients use the JSON `NetCodec`
  envelope at this edge; native clients keep MessagePack.
- **Persistence** — `IWorldRepository` behind `WorldRepositoryFactory`. SQLite
  (`SqliteWorldRepository`, WAL mode, `synchronous=NORMAL`) is the portable default for local,
  singleplayer and self-hosted worlds; PostgreSQL (`PostgreSqlWorldRepository`) is an opt-in hosted
  backend selected by `databaseProvider` / `BBS_DATABASE_PROVIDER=postgresql` plus a connection string.
  Both backends store world
  metadata, **only player block-edit deltas** (`block_edit`, keyed by planet+xyz with
  tint/glow/shape), player/ship JSON blobs, containers, doors, beacons, beams, bases,
  alliances, story state, space structures + their per-cell edits, location statuses and
  player/admin missions. `RunInTransaction` batches bursty writes into one commit; autosave +
  backups via `CreateBackup`. See [SELF_HOSTING.md](SELF_HOSTING.md).
- **WorldGen** — `WorldGeneration/WorldGenerator.cs` (+ its `WorldGenerator.*.cs` partials, one per
  seam: relief, landmarks, overhangs, underground, craters, calibration, fluids, columns, stamps, biomes,
  flora — #1644; regimes + the generation-1 relief rolls — #1645; the chunk fill stages that
  `Generate` calls, in `WorldGenerator.ChunkFill.cs` — #2892) is **seed-deterministic**: given a seed,
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
`Scripts/`; the Unity-free logic underneath — `NetworkClient`, `ClientWorld`, the interpolators — is
the `Client.Core` assembly, see [CLIENT_TESTING.md](CLIENT_TESTING.md)):

- **`AppShell.cs`** — front-end state machine (splash → menu → settings → save-select →
  loading → in-game); owns `ClientSettings` + `Localizer` and the `LocalServerLauncher`.
- **`WorldRig.cs`** — code-only scene builder that constructs the in-game rig: server link +
  `GameBootstrap`, first-person player, HUD, post FX, views; tears down on return to menu.
  The views other components look up (HUD, VEGA's panel, cutscene director, doors, …) are registered
  in `GameBootstrap.Views` (`RigViews`, one entry per view, null until the view exists and again once
  it is destroyed); components reach them through `Game.Views.X`, not static singletons. The modal
  in-world panels register themselves in `GameBootstrap.Context` when WorldRig sets their `Game`
  (see [UI_PANELS.md](UI_PANELS.md)). The only `Instance` singletons left are the services
  `ClientAudio`, `FxKit`, `ScreenLabelLayer` and `UrpScenePost` (the latter also lives in the menu
  shell).
- **`GameBootstrap.cs`** — creates the `NetworkClient`, loads `GameContent` from
  StreamingAssets, builds the procedural `BlockTextureAtlas`, and subscribes to all server
  events. It is a partial class: `GameBootstrap.cs` keeps the shared fields, `Start`, `Update`,
  `OnWorldReset` and `OnDestroy`, and each concern is a `GameBootstrap.<Concern>.cs` file (chunk
  streaming, player state, environment, stations, structures, entities, space, knowledge, story,
  messages, menus, plus the chunk pipeline). `Start` subscribes through the `Wire*()` methods
  (`WireSession` and `WireContent` in the core file, `WireChunkStreaming`, `WirePlayerState`, … in
  the concern files) in a fixed order before it connects; `Update` polls the network, advances the
  clocks and ends with `TickSkyScan`, `TickConnect` and `TickChunkStreaming`, in that order. It also
  holds the rig's two registries, `Context` (`ClientContext`, the modal panels) and `Views`
  (`RigViews`, the other views). The story state is the Unity-free `StoryClientState` in Client.Core
  (#2933: the story progress, the Story Log, the Journal's stories, objectives and badge, the VEGA
  log; tested in `Client.Tests/StoryClientStateTests`), like `BioClientState` for the bio lab:
  `GameBootstrap.Story.cs` holds one as a field initializer, forwards its members under their old
  names and passes the content to each handler. Two extractions were dropped by decision (#2929): the station
  affordances stay a partial (two short handlers whose events have no subscriber), and so does the
  chunk pipeline (Unity-bound, reads host state throughout, WorldRig sets its `FarView` before
  `Start`, and the player and the far terrain query it from their own updates).
- **`PlayerController.cs`** — the first-person controller (walk, look, mine and place as intents).
  `PlayerController.cs` keeps the wiring, the shared body state, `Update` (the on-foot key polls) and
  the spawn gate; each concern (movement, footing, climbing, rope, glider, aim, placement, interaction,
  stations, combat, gear, scanner, rest, view) is a `PlayerController.<Concern>.cs` partial. Driving a
  speeder or boat (`PlayerVehicleDrive`) and riding the monorail (`PlayerTrainRider`) are sibling
  components the controller adds in `Awake` and calls at the old call sites; the screenshot/clip pose
  search (`PlayerCapture`) is added on first use through `PlayerController.Capture`, so normal play
  never creates it. The movement gear stays in partials, not components, by decision (#2929): its
  testable rules already live in Client.Core (`RopeRig`, `ClimbGrip`, `ClimbProbe`) and Shared
  (`SuitEquipment`) with tests, and the step order inside `Move()` (pull-up, rope before wall climb,
  the glide-refusal latch, the fall-start reset, then jetpack and glider) is behaviour.
- **Voxels** — `ChunkMesher.cs` builds per-chunk meshes with face culling, opaque/transparent
  submeshes, per-voxel tint/glow flood-fill (TEXCOORD3) and non-cube block shapes, plus a
  separate collision mesh; `BlockTextureAtlas.cs` paints a 32×32-tile atlas (+ derived normal
  map) entirely in code (the per-block looks are the `CodeTiles` table in
  `BlockTextureAtlas.CodeTiles.cs`; how the slots are shared out between block ids, extra tiles,
  animation strips and derived tiles is `AtlasBands` in `Client.Core`); `ClientWorld` (`Client.Core`)
  caches received chunks read-only.
- **UI** — **uGUI / Canvas-based** (a procedural sci-fi toolkit in `UiKit.cs`, not raw IMGUI):
  `HudUi`, `UiMainMenu`, `UiSettings`, `UiSaveSelect`, `UiLoading`, the in-game editors,
  `CraftingTechShipUI`, `ChatUi`, `ScreenLabelLayer` (nameplates). The big screens are partial
  classes split by concern: `HudUi.cs` composes the HUD and each widget is a `HudUi.<Widget>.cs`
  file (hotbar, vitals, compass, toasts, scan panel, …; the toast timing rules are the pure
  `HudToastPolicy.cs`). The widgets stay partials by decision (#2929): when a HUD rule needs a test,
  extract the *rule* into a pure class (like `HudToastPolicy`, or `DamageFeedback` and `HitDirection`
  in Client.Core); widget classes are reopened only when a second consumer needs a widget as an
  object. `CraftingTechShipUI.cs` is the screen's shell with one
  `CraftingTechShipUI.<Part>.cs` file per tab or page plus the shared `.Recipes`, `.Stations` and
  `.Scaffolding` parts. `UiMainMenu.Build` is a short orchestrator over a private `Builder` with one
  `Build*` method per block of the menu, called in draw order — the order is behaviour (the boot
  stagger, raycasts and gamepad focus follow the hierarchy), pinned by
  `MainMenuHierarchyEditModeTests`; the menu's native-only Official-worlds overlay is the partial
  class `UiOfficialWorlds` (`UiOfficialWorlds.cs` + `.Rules`, `.Account`, `.Worlds`). New screens
  use the shared builders instead of copying them: `UiKit`
  (`AddScrollViewport`, `MeasureHeight`, `AddCenteredPanel`, `AddStretchChild`, `Tr`), and for the
  free-fly editor scenes the static `EditorSceneKit` / `EditorPaletteKit` (camera, floor, cell picking,
  right panel, palette panel, `Slug`, …); the form and tool-look editors share the `LayerCanvasEditor`
  base; modal in-world panels derive from `UiPanel` and are looked up through `GameBootstrap.Context`
  (see [UI_PANELS.md](UI_PANELS.md)), the rig's other views through `GameBootstrap.Views`. See
  [UI_AND_RENDER_CONCEPT.md](UI_AND_RENDER_CONCEPT.md).
- **`SpaceView.cs`** — the free-flight space scene and the **"ship as a place" model**: the own
  ship is a client-side voxel grid you can pilot, EVA, walk and rebuild; parked ships render via
  `LandedShipView` (not part of the world block grid). `SpaceView.cs` keeps the enter/exit and
  per-frame shell; the rest is split into `SpaceView.<Concern>.cs` partials (`Flight`, `Docking`,
  `Combat`, `Targeting`, `Eva`, `Scene`, `Hud`, `LandMap`, `Models`, …). See
  [STATION_AS_LOCATION.md](STATION_AS_LOCATION.md).
- **Presence & A/V** — `RemotePlayers.cs` interpolates other players (`RemoteEntityInterpolator`);
  `CreatureView.cs`, `NpcView.cs` and `WorldEntities.cs` draw animals, NPCs, machines and bandits from buffered
  server-clock snapshots along Hermite curves (`MotionInterpolator`, see [ENTITY_MOTION.md](ENTITY_MOTION.md));
  `ClientAudio.cs`/`ClientMusic.cs` route SFX + context music.
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
content should not require touching game logic. `GameContent.cs` holds the registry (block ids,
lookups, the locale tables and the setters the loader fills); the cross-reference checks are in
`GameContent.Validation.cs`, the station/settlement template pools, kits and weighted picks in
`StructureCatalog.cs` and the craft-depth graph behind the craft-list order in `CraftingGraph.cs`
(`GameContent` keeps forwarding members for both). User-authored structure templates dropped under
a `usercontent/` dir by the in-game editors are merged into the pools automatically.

In-game text is **never hardcoded**: it uses localization keys resolved against
`data/locales/*.json` (`Shared/Localization`, English fallback per missing key). `en.json` +
`de.json` are the mandatory pair every new key must land in; the twelve community languages (`fr`,
`es`, `it`, `pt`, `pl`, `tr`, `nl`, `ru`, `uk`, `ja`, `ko`, `zh`) are translated incrementally and may
trail the pair by the newest keys, which read in English until they are translated (see
[TRANSLATION_GUIDE.md](TRANSLATION_GUIDE.md)). All docs/code comments are English.

## Networking protocol (intents → state)

`Networking/NetCodec.cs` is a stable **tag↔type registry**. Native payloads are a one-byte
message-type tag followed by a MessagePack *contractless* body (no serialization attributes,
compact wire format). Browser WebSocket payloads use a reserved JSON envelope tag whose body
contains the same registered message tag plus JSON. Ids are append-only and never reused; a
message class that isn't `Register()`'d silently fails to send.

- **Client → server = intents** (tags 1+): `JoinRequest`, `MoveIntent`, `MineBlockIntent`,
  `PlaceBlockIntent`, `CraftIntent`, `DockRequestIntent`, `TravelIntent`, `FireWeaponIntent`, …
- **Server → client = state** (tags 50+): `JoinAccepted`, `ChunkDataMessage`, `BlockChanged`,
  `InventoryUpdate`, `PlayerStateUpdate`, `CraftResult`, `ActionRejected`, …

`GameServer.OnPayload` decodes, rejects gameplay intents before join, sets the world+ship
cursor to the sender, and dispatches inside a try/catch so one bad handler can't take down the
single-threaded tick. `Dispatch` runs the observer, pause and guest gates, then looks the intent's
exact type up in one registry — `GameServerDispatch.cs`, one `On<T>` line per handled intent; the
client mirrors it in `Client.Core/NetworkClientDispatch.cs` (server message → typed event). A new
message is one `On<T>` line there; `IntentDispatchRegistryTests` / `ClientDispatchRegistryTests` pin
the number of handled types, check that each is a `NetCodec`-registered, sealed message, and that
every registered message is handled by exactly one side (apart from voice and `JoinRequest`, which
`OnPayload` answers before dispatch).
**World = seed + parameters + deltas**: clients receive generated chunks
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
  Linux ARM64 — no rendering/physics on the server). Optional WebSocket
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
8. **Docs/comments English; in-game text localized** via locale keys (`en`+`de` mandatory).

## Where to find what

| Path | Contents |
|---|---|
| `src/BlocksBeyondTheStars.Shared/` | Models, definitions, geometry, localization, story engine, game rules |
| `src/BlocksBeyondTheStars.WorldGeneration/` | Deterministic world/galaxy/flora/settlement/creature gen |
| `src/BlocksBeyondTheStars.Networking/` | Transports, `Messages/<Domain>Messages.cs`, `NetCodec` |
| `src/BlocksBeyondTheStars.Persistence/` | `IWorldRepository`, SQLite repo, save paths |
| `src/BlocksBeyondTheStars.GameServer/` | `GameServer*` partials (core + facades), `*System` subsystem classes (`GameServer.Systems.cs`, `IServerHost.cs`), `PlayerSession*` components, tick schedule, host (`Program.cs`) |
| `src/BlocksBeyondTheStars.Api/` | Admin UI, portal, download/update feed |
| `src/BlocksBeyondTheStars.WorldHost/` | Hosted-worlds control plane (accounts, world registry, portal) |
| `src/BlocksBeyondTheStars.ReportHost/` | Bug-report inbox (F1 feedback, crash reports) |
| `src/BlocksBeyondTheStars.Tools/` | Backup/export/debug CLI |
| `src/BlocksBeyondTheStars.Launcher/` | Instant-splash Velopack launcher (Windows) |
| `src/BlocksBeyondTheStars.Launcher.Console/` | Console launcher (Linux) |
| `src/BlocksBeyondTheStars.Client.Core/` | Unity-free client logic (`NetworkClient` + `NetworkClientDispatch`, `ClientWorld`, interpolators, …) |
| `client/Assets/BlocksBeyondTheStars/Scripts/` | Unity client (`AppShell`, `WorldRig`, `GameBootstrap`, `PlayerController`, `ChunkMesher`, `HudUi`, `SpaceView`, `UiPanel`, `LocalServerLauncher`, …); a large class is split into `Class.Part.cs` partial files |
| `client/Assets/BlocksBeyondTheStars/Shaders/` | URP custom shaders |
| `data/` + `data/locales/` | Data-driven content + localization |
| `ai-backend/` | Optional Python LLM service |
| `tests/BlocksBeyondTheStars.Tests/` | xUnit tests |
| `tests/BlocksBeyondTheStars.Client.Tests/` | Headless client ↔ server tests |
| `docs/` | This file + the detail docs linked above |
| `scripts/` | `build-client.ps1` + publish scripts |
| `../TODO.md` · `../AGENTS.md` | Status (single source of truth) · contributor rules |
