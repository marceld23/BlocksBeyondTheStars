# AGENTS.md — Guide for AI Agents Working on Blocks Beyond the Stars

This file orients any AI agent (or developer) contributing to **Blocks Beyond the Stars**
(renamed 2026-06-12 from the former working title; solution, namespaces, binaries and paths
all use `BlocksBeyondTheStars`). Read it before making changes.

## What Blocks Beyond the Stars is

A block-based 3D space crafting game for Windows. The player starts with a small
spaceship, explores procedurally generated planets, mines resources, crafts gear,
researches blueprints, and grows the ship. The current status (Done/Open) lives in
[TODO.md](TODO.md); player-facing operation in [docs/USER_MANUAL.md](docs/USER_MANUAL.md).
(The original German requirement specs under `plans/` were consolidated and removed.)

## Golden architecture rule

> **The Unity client is presentation and input. The .NET server is the truth of the
> game world.**

The client sends *intents*; the server validates them authoritatively and replies
with *state*. Never make the client authoritative over resources, inventory,
crafting, ship state, oxygen, damage, blueprints, or travel. This keeps multiplayer,
LAN/self-hosting and anti-cheat correct by construction.

## Tech stack (decided)

- **Client:** Unity 6 LTS (6000.4.x), C#. Lives in `client/` (open in the Unity Editor).
- **Server:** .NET 8, standalone console host. **No Unity runtime on the server.**
- **Admin/API:** ASP.NET Core 8 (Minimal API).
- **DB:** SQLite by default (portable, Raspberry Pi friendly); PostgreSQL later.
- **Realtime networking:** LiteNetLib (UDP). MessagePack for wire serialization.
- **Shared/WorldGeneration target `netstandard2.1`** so the *same* code runs in both
  Unity and the .NET server. Everything else targets `net8.0`.

## Repository layout

```
src/BlocksBeyondTheStars.Shared/          data models, data-driven definitions, localization, protocol DTOs
src/BlocksBeyondTheStars.WorldGeneration/ seed-based deterministic chunk generation
src/BlocksBeyondTheStars.Persistence/     SQLite repository, savegame layout, autosave
src/BlocksBeyondTheStars.Networking/      transport abstraction (LiteNetLib + loopback), messages
src/BlocksBeyondTheStars.GameServer/      authoritative tick loop + console host
src/BlocksBeyondTheStars.Api/             admin web UI + API
src/BlocksBeyondTheStars.Tools/           backup/export/debug CLI
tests/BlocksBeyondTheStars.Tests/         xUnit tests
client/                         Unity project
ai-backend/                     optional Python LLM service (missions, NPC/ship-AI text); offline-safe
tools/                          editor-export merge tools + AI asset generation (tools/ai-assets)
data/                           data-driven JSON definitions (blocks, items, recipes, ...)
data/locales/                   localization resource files (en.json, de.json)
docs/                           user manual, self-hosting guide, design/plan docs, ADRs
scripts/                        build-client.ps1 + publish scripts
```

Dependency direction (no cycles): `Shared` ← everything; `WorldGeneration`,
`Persistence`, `Networking` ← `GameServer`.

## Hard rules for contributors (human or AI)

1. **Language of text:**
   - *Documentation and code comments → English only.* Even though the spec docs
     and chat are German.
   - *In-game player-facing text → bilingual (German + English).* Never hardcode
     player-facing strings; use localization keys + `data/locales/*.json`. Default
     fallback locale is English.
2. **Server is authoritative** — see the golden rule above.
3. **Data-driven content** — blocks, items, recipes, ship modules, tech nodes,
   planets live in `data/*.json`, not hardcoded in logic. Adding content should not
   require touching game logic.
4. **World = seed + parameters + deltas.** Only persist player changes, never every
   natural block.
5. **Raspberry Pi friendly** — no rendering/physics engine on the server; keep CPU,
   RAM and disk-write load low and configurable.
6. **Atomic saves** — write to a temp file then swap; autosave + rotating backups.
7. **Keep `Shared`/`WorldGeneration` netstandard2.1-clean** so Unity can consume them.

## Build & test

```powershell
dotnet build BlocksBeyondTheStars.sln      # build everything
dotnet test                      # run all xUnit tests
dotnet run --project src/BlocksBeyondTheStars.GameServer   # start a local server
./scripts/build-client.ps1       # full Windows client (shared libs + bundled server + Unity batch build)
```

To confirm a client rebuild actually happened, check the `BlocksBeyondTheStars.Client.dll` timestamp in the
build output (the `.exe` timestamp is not reliable).

## Project conventions

- C#: `LangVersion=latest`, nullable enabled, 4-space indent, Allman braces
  (see `.editorconfig`). Records/`init` work on netstandard2.1 via the
  `IsExternalInit` polyfill in `BlocksBeyondTheStars.Shared/Compatibility`.
- The author is JAM Software; follow sensible, consistent C# conventions.

## Roadmap

See [TODO.md](TODO.md) for the current Done/Open status (the single status doc).
