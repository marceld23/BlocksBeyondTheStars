# AGENTS.md — Guide for AI Agents Working on Spacecraft

This file orients any AI agent (or developer) contributing to **Spacecraft**.
Read it before making changes.

## What Spacecraft is

A block-based 3D space crafting game for Windows. The player starts with a small
spaceship, explores procedurally generated planets, mines resources, crafts gear,
researches blueprints, and grows the ship. See `anforderungen.md` (game design,
German) and `technische_anforderungen.md` (technical requirements, German) for the
source-of-truth specs.

## Golden architecture rule

> **The Unity client is presentation and input. The .NET server is the truth of the
> game world.**

The client sends *intents*; the server validates them authoritatively and replies
with *state*. Never make the client authoritative over resources, inventory,
crafting, ship state, oxygen, damage, blueprints, or travel. This keeps multiplayer,
LAN/self-hosting and anti-cheat correct by construction.

## Tech stack (decided)

- **Client:** Unity 2022 LTS, C#. Lives in `client/` (open in the Unity Editor).
- **Server:** .NET 8, standalone console host. **No Unity runtime on the server.**
- **Admin/API:** ASP.NET Core 8 (Minimal API).
- **DB:** SQLite by default (portable, Raspberry Pi friendly); PostgreSQL later.
- **Realtime networking:** LiteNetLib (UDP). MessagePack for wire serialization.
- **Shared/WorldGeneration target `netstandard2.1`** so the *same* code runs in both
  Unity and the .NET server. Everything else targets `net8.0`.

## Repository layout

```
src/Spacecraft.Shared/          data models, data-driven definitions, localization, protocol DTOs
src/Spacecraft.WorldGeneration/ seed-based deterministic chunk generation
src/Spacecraft.Persistence/     SQLite repository, savegame layout, autosave
src/Spacecraft.Networking/      transport abstraction (LiteNetLib + loopback), messages
src/Spacecraft.GameServer/      authoritative tick loop + console host
src/Spacecraft.Api/             admin web UI + API
src/Spacecraft.Tools/           backup/export/debug CLI
tests/Spacecraft.Tests/         xUnit tests
client/                         Unity project
data/                           data-driven JSON definitions (blocks, items, recipes, ...)
data/locales/                   localization resource files (en.json, de.json)
docs/                           ADRs, protocol docs, self-hosting guide
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
dotnet build Spacecraft.sln      # build everything
dotnet test                      # run all xUnit tests
dotnet run --project src/Spacecraft.GameServer   # start a local server
```

## Project conventions

- C#: `LangVersion=latest`, nullable enabled, 4-space indent, Allman braces
  (see `.editorconfig`). Records/`init` work on netstandard2.1 via the
  `IsExternalInit` polyfill in `Spacecraft.Shared/Compatibility`.
- The author is JAM Software; follow sensible, consistent C# conventions.

## Roadmap

See `IMPLEMENTATION_PLAN.md` for milestones (M0–M8) and their checklists.
