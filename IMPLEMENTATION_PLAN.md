# Spacecraft – Implementation Plan

> Technical guiding principle (from `technische_anforderungen.md`):
> **The Unity client is presentation and input. The .NET server is the truth of the
> game world.**

This document describes *how* Spacecraft is built, in what order, and where we
deliberately draw boundaries. It is the working basis — completed items are checked
off here.

> Note: `anforderungen.md` and `technische_anforderungen.md` are the original specs
> (German, input only). All other project documentation and code comments are in
> **English**. In-game player-facing text is **bilingual (German + English)** via a
> localization system.

---

## 0. Technology decisions

| Area | Decision | Reason |
|---|---|---|
| Client | **Unity 2022 LTS + C#** | Required; 3D, UI, input, Windows |
| Server | **.NET 8 (LTS), standalone console host** | Required; no Unity headless |
| Admin/API | **ASP.NET Core 8 (Minimal API)** | Required; lightweight |
| DB default | **SQLite** (`Microsoft.Data.Sqlite`) | portable, Pi-friendly |
| DB optional | PostgreSQL (later, via persistence abstraction) | large servers |
| Realtime net | **LiteNetLib** (UDP, reliable+unreliable) | standard for Unity↔.NET self-hosting, MIT |
| Serialization | **MessagePack** (network) + **System.Text.Json** (configs/definitions/locales) | compact / readable |
| Tests | **xUnit** | standard |
| Shared TFM | **netstandard2.1** | consumable by Unity *and* .NET 8 |
| Localization | **key-based + per-locale JSON** (`data/locales/en.json`, `de.json`) | bilingual game |

**Key consequence:** `Spacecraft.Shared` and `Spacecraft.WorldGeneration` target
`netstandard2.1` so that the *exact same* game logic/generation runs in Unity (client
prediction) and on the server (authoritative).

---

## 1. Solution structure (modules)

```text
SpaceCraft/
  Spacecraft.sln
  src/
    Spacecraft.Shared/          (netstandard2.1)  data models, definitions, localization, protocol DTOs
    Spacecraft.WorldGeneration/ (netstandard2.1)  seed-based chunk generation, noise
    Spacecraft.Persistence/     (net8.0)          SQLite repo, savegame layout, autosave
    Spacecraft.Networking/      (netstandard2.1)  transport abstraction, LiteNetLib impl, messages
    Spacecraft.GameServer/      (net8.0)          tick loop, authoritative simulation, console host
    Spacecraft.Api/             (net8.0)          ASP.NET Core admin web UI + API
    Spacecraft.Tools/           (net8.0)          backup/export/debug CLI
  tests/
    Spacecraft.Tests/           (net8.0)          unit/integration tests
  client/                       Unity project (opened separately in the Unity Editor)
  data/                         data-driven JSON definitions (blocks, items, recipes, ...)
  data/locales/                 localization resources (en.json, de.json)
  docs/                         ADRs, protocol docs, self-hosting guide
```

Dependency direction (no cycles):

```text
Shared  ←  WorldGeneration
Shared  ←  Networking
Shared, WorldGeneration, Persistence, Networking  ←  GameServer
Shared, Persistence  ←  Api
Shared, Persistence  ←  Tools
Shared  ←  (Unity client)
```

---

## 2. Architecture principles (non-negotiable)

1. **Server is authoritative.** Client sends *intents*; server validates and returns
   *state*. (`technische_anforderungen.md` §7, §15)
2. **Singleplayer = local server.** In SP mode the client runs the same GameServer
   in-process. No duplicated game logic. (§8.2)
3. **World = seed + parameters + deltas.** Only player changes are persisted, not
   every natural block. (§11)
4. **Data-driven.** Blocks, items, recipes, modules, tech tree, planets = JSON in
   `data/`. (§20)
5. **Raspberry Pi friendly.** No server-side rendering/physics engine; load is
   configurable. (§9)
6. **Atomic saves.** Temp file + atomic swap; autosave + backups. (§16.2)
7. **Bilingual game text.** No hardcoded player-facing strings; localization keys +
   `data/locales/*.json`, English fallback.

---

## 3. Data model (Spacecraft.Shared)

- **Primitives:** typed `BlockId`/`ItemId` numeric handles (palette-mapped from string keys).
- **Geometry:** `Vector3i`, `ChunkCoord`, world constants (chunk size 16³).
- **World data:** `ChunkData` (flat `ushort[]` palette indices), indexing helpers.
- **Definitions (data-driven, loaded from JSON):** `BlockDefinition`,
  `ItemDefinition`, `RecipeDefinition`, `BlueprintDefinition` (tech),
  `ShipModuleDefinition`; later `PlanetType`, `Biome`, `Hazard`.
- **Localization:** `LocalizationKey` on definitions; `Localizer` loads
  `data/locales/{locale}.json`, English fallback.
- **Runtime state:** `ItemStack`, `Inventory`, `PlayerState`, `ShipState`.
- **Content registry:** `GameContent` loads + validates all definitions and locales
  and cross-checks references (e.g. a recipe references an existing item).

## 4. Network protocol (Spacecraft.Networking)

- **Transport abstraction:** `IServerTransport`/`IClientTransport` with a
  `LiteNetLibTransport` (UDP) and a `LoopbackTransport` (in-memory, for SP + tests).
- **Channels:** reliable-ordered (actions, world deltas), unreliable (positions).
- **Messages (MessagePack):**
  - Client→Server (intents): `JoinRequest`, `MoveIntent`, `MineBlockIntent`,
    `PlaceBlockIntent`, `CraftIntent`, `TransferIntent`, `TravelIntent`, `ScanIntent`.
  - Server→Client (state): `JoinAccepted`, `ChunkData`, `BlockChanged`,
    `InventoryUpdate`, `PlayerStateUpdate`, `CraftResult`, `ActionRejected`.
- Protocol version field in the handshake → client/server compatibility.

## 5. World generation (Spacecraft.WorldGeneration)

- Deterministic from `(planetSeed, planetType, chunkCoord)`.
- Value/Perlin noise (own dependency-free impl for netstandard2.1).
- Height field + biomes + ore distribution + simple caves.
- Identical on client (preview/prediction) and server (truth).

## 6. Persistence (Spacecraft.Persistence)

- Savegame layout exactly per §10.3.
- SQLite schema: `world_meta`, `chunk_delta`, `players`, `inventories`,
  `ship_state`, `unlocked_blueprints`, `containers`, `outposts`.
- `IWorldRepository` (swappable → PostgreSQL later).
- Autosave scheduler, atomic writes, rotating backups.

## 7. Game server (Spacecraft.GameServer)

- Fixed tick loop (default 15 Hz, configurable 10–20). (§7.2)
- Per tick: collect intents → validate → mutate world → broadcast deltas.
- **Authoritative validators** (§7.3): mining, placement, crafting, transfer, travel,
  ship expansion, oxygen/environment ticks.
- Chunk manager: load/unload by player proximity, configurable limits. (§18.2)
- Console host with `server.json` config + logging.

## 8. Admin API (Spacecraft.Api)

- ASP.NET Core Minimal API + simple HTML UI.
- Functions per §13.2: status, players, start/stop, config, whitelist, logs, backup,
  export, shutdown/restart, load.
- Auth via admin password; default bind local/LAN only (§13.3).

## 9. Tools (Spacecraft.Tools)

- CLI for backup, export, world inspection, definition validation.

## 10. Unity client (client/)

- Unity Editor cannot run in this environment: we create **scripts + project scaffold
  + a guide**; opening/building happens in the Unity Editor.
- Modules: connection (Networking DLL), chunk mesher (greedy meshing), player
  controller, camera, hotbar/inventory/crafting UI, cockpit star map, client prediction,
  localized UI strings.

---

## 11. Order of work (milestones)

### M0 – Scaffold ✅
- [x] Create solution + all projects, wire references
- [x] `.gitignore`, `.editorconfig`, `Directory.Build.props`, git repo init
- [x] Builds clean + server smoke test

### M1 – Shared data model, definitions & localization ✅
- [x] Primitives, geometry, definition types
- [x] JSON definitions in `data/` (base blocks, items, recipes, blueprints, modules)
- [x] Localization (`en.json`, `de.json`) + `Localizer`
- [x] Content registry + validation + tests

### M2 – World generation ✅
- [x] Data-driven planet types + noise + deterministic chunk generation + tests

### M3 – Persistence ✅
- [x] SQLite schema + repository + atomic save/load + backups + tests

### M4 – Networking ✅
- [x] Transport abstraction + loopback + messages + codec + serialization tests
- [x] LiteNetLib transport

### M5 – Game server (authoritative core) ✅
- [x] Tick loop, player join, chunk streaming, console host
- [x] Validators: mining, placement, inventory, crafting, blueprint unlock
- [x] Oxygen/environment base tick
- [x] Integration test: loopback client mines block → inventory grows → save/load

### M6 – Admin API ✅
- [x] Status/config/backup/logs endpoints + HTML UI + auth + tests

### M7 – Self-hosting packages ✅
- [x] Tools CLI (validate/info/backup)
- [x] Publish scripts: win-x64, linux-x64, linux-arm64 (Pi 5) + README + guide + ADR

### M8 – Unity client scaffold ✅
- [x] Project structure, scripts, Shared linkage (sync script), guide

---

## 12. Non-goals for this phase (per §22)
No central server list, no online account, no MMO, no space combat, no enemy AI,
no crew/modding system, no cloud saves. The architecture stays open to them.

---

## 13. Definition of Done (MVP, §21)
- Server generates a world from a seed, loads/unloads chunks, validates
  mining/placement/crafting, manages inventory + cargo, saves & loads, autosaves,
  runs locally & on LAN.
- Server publishable for win-x64 / linux-x64 / linux-arm64.
- Client scaffold + shared data model in place.
- All tests green.

---

## 14. Extended scope (additional requirement docs)

Four further requirement docs were added after the MVP and are planned in here. They
build on the authoritative-server foundation; most are server-side and unit-testable
without Unity. Source docs: `anf_admin_einstellungen.md`, `anf_admin_blueprinf.md`,
`anf_mission_editor.md`, `anf_webclient.md`.

### M9 – Game modes & server rules (`anf_admin_einstellungen.md`)
- [ ] `GameMode` (Survival/Creative) + `GameRules` (PvP, aggressive aliens, weapon mode,
  environmental hazards, death penalty, keep-inventory/keep-ship, admin cheats) in config
- [ ] Server presets (peaceful-creative, family, coop-survival, dangerous, pvp)
- [ ] Server enforces rules authoritatively (creative = free/instant crafting, etc.)
- [ ] Client receives the active rule set on join; tests

### M10 – Heal-tank respawn & death (`anf_admin_blueprinf.md` §2–4)
- [ ] Medbay heal-tank as the respawn point (not the bed)
- [ ] Death triggers (health 0, oxygen depletion); respawn at ship heal-tank, restore health
- [ ] Death penalty per rules (keep-inventory vs. salvage capsule); tests

### M11 – World description & procedural universe (`anf_admin_einstellungen.md` §8–9)
- [ ] `WorldDescription` (star-system count, planets/system, moons, asteroids, stations,
  planet-type frequencies, resource/danger density) — data + admin-editable
- [ ] Deterministic universe generation (systems → bodies) from seed + description
- [ ] Persist generated locations + generation status (generated/discovered/visited)
- [ ] "Generated once → stable" guarantee; tests

### M12 – Admin roles & cheats (`anf_admin_einstellungen.md` §10–12)
- [ ] Player roles (world admin = creator, admin, moderator, player)
- [ ] Server-authoritative, admin-only, logged cheats: teleport, give item, set time,
  set weather, fly, god mode, instant build (gated by `adminCheats`)
- [ ] Cheat audit log; tests

### M13 – Mission system, no AI (`anf_mission_editor.md` §1, `anf_admin_blueprinf.md` §6)
- [ ] Data-driven mission definitions + runtime mission state (accept/track/complete)
- [ ] Server-validated objective types (collect/mine/deliver/travel/scan/build/...)
- [ ] Reward depot + system reward for creators; player-created missions
- [ ] Persistence; tests

### M14 – Admin extension editor & content packs (`anf_admin_blueprinf.md` §5–13)
- [ ] Per-world content overlay: admin missions, blueprints, recipes, reward tables
- [ ] Validation, draft/activate, export/import content packs (JSON)
- [ ] Admin API endpoints + UI; tests

### M15 – Web portal & WebSocket transport (`anf_webclient.md`)
- [ ] `WebSocketServerTransport` so browser clients use the same protocol over WS
- [ ] Server web portal (`/` download native / `/play` / status) via the API
- [ ] Feasibility doc for the Unity WebGL Lite client (`docs/WEBCLIENT_FEASIBILITY.md`)

### M16 – Optional Python AI mission backend (`anf_mission_editor.md` §3–16)
- [ ] Separate Python service scaffold (FastAPI) producing a validated `MissionPlan`
- [ ] Server-side AI client with strict validation + fallback (game works without AI)
- [ ] Config levels (off / text-only / suggest / auto); decision doc

> Order: M9 → M16. Unity-side work (WebGL build, browser UI) is documented/feasibility
> only here since the Unity Editor can't run in this environment; all server logic is
> implemented and tested.
