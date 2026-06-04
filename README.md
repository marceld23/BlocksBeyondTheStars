# Spacecraft

A block-based 3D space crafting game for Windows. You start with a small spaceship,
explore procedurally generated planets, mine resources, craft gear, research blueprints,
and grow your ship — built from day one as a client/server game so multiplayer and
self-hosting come naturally.

## About this project

Spacecraft is a **father-and-son project**:

- **Justus (age 10)** is the **Product Owner** — he comes up with the game and writes the
  user requirements.
- **Marcel (papa)** is the **technical translator** — he turns Justus's ideas into precise
  technical requirements.
- The **AI (Claude Code, Anthropic's Claude Opus)** produces the technical implementation
  from those requirements.

> **Status & docs:** [TODO.md](TODO.md) is the single Done/Open status doc; player operation is in
> [docs/USER_MANUAL.md](docs/USER_MANUAL.md); deeper design notes are under [docs/](docs/). (The
> original German requirement specs under `plans/` were consolidated and removed.)
> Docs and code comments are **English**. In-game text is **bilingual (German + English)**.

## Guiding principle

> **The Unity client is presentation and input. The .NET server is the truth of the game world.**

The client sends *intents*; the server validates them authoritatively and broadcasts the
resulting *state*. The client never decides resources, inventory, crafting, ship state,
oxygen, damage, blueprints or travel.

## Tech stack

| Area | Choice |
|---|---|
| Client | Unity 6 LTS (6000.4.x) + C# (Windows) — see [`client/`](client/) |
| Server | .NET 8, standalone console host (no Unity runtime) |
| Admin UI | ASP.NET Core 8 minimal API + HTML dashboard |
| Database | SQLite (default, portable, Raspberry Pi friendly); PostgreSQL later |
| Realtime net | LiteNetLib (UDP) + MessagePack |
| Shared logic | `netstandard2.1` so the same code runs in Unity *and* the server |

## Repository layout

```
src/Spacecraft.Shared/          data models, data-driven definitions, localization, protocol DTOs
src/Spacecraft.WorldGeneration/ seed-based deterministic chunk generation
src/Spacecraft.Persistence/     SQLite repository, savegame layout, autosave, backups
src/Spacecraft.Networking/      transport abstraction (LiteNetLib + loopback), messages, codec
src/Spacecraft.GameServer/      authoritative tick loop + console host
src/Spacecraft.Api/             admin web UI + API
src/Spacecraft.Tools/           validate/info/backup CLI
tests/Spacecraft.Tests/         xUnit tests
client/                         Unity project (scripts + scaffold; open in the Unity Editor)
data/                           data-driven content (blocks, items, recipes, blueprints, modules, planets)
data/locales/                   localization (en.json, de.json)
docs/                           self-hosting guide, ADRs
scripts/                        publish scripts for self-hosting packages
```

## Build, test, run

Requires the **.NET 8 SDK**.

```powershell
dotnet build Spacecraft.sln       # build everything
dotnet test                       # run all tests
dotnet run --project src/Spacecraft.GameServer   # start a local dedicated server (UDP 31415)
dotnet run --project src/Spacecraft.Api          # start the admin UI (http://127.0.0.1:31416)
```

Server configuration lives in `config/server.json` (created on first run) and is editable
via the admin UI. See [docs/SELF_HOSTING.md](docs/SELF_HOSTING.md).

### Tools CLI

```powershell
dotnet run --project src/Spacecraft.Tools -- validate data
dotnet run --project src/Spacecraft.Tools -- info saves world_001
dotnet run --project src/Spacecraft.Tools -- backup saves world_001
```

### Self-hosting packages

```powershell
./scripts/publish-server.ps1            # win-x64, linux-x64, linux-arm64 (Raspberry Pi 5)
```
Produces self-contained, single-file packages (no .NET install needed on the host) under
`artifacts/`. On Linux/macOS use `scripts/publish-server.sh`.

## Adding content (data-driven)

Blocks, items, recipes, blueprints, ship modules and planets are JSON in `data/`; no code
changes are needed to add content. Player-facing names use localization keys resolved from
`data/locales/{en,de}.json`. Validate with `Spacecraft.Tools validate`.

## Status

MVP backend is implemented and tested end-to-end (join → mine → inventory → save/load,
crafting, blueprint gating, game modes, respawn, universe, cheats, missions, admin content,
WebSocket gateway). See [TODO.md](TODO.md) for the current Done/Open status, the
[user manual](docs/USER_MANUAL.md) for controls/mechanics/commands, and [AGENTS.md](AGENTS.md)
for contributor rules.
