# AGENTS.md — Guide for AI Agents Working on Blocks Beyond the Stars

This file orients any AI agent (or developer) contributing to **Blocks Beyond the Stars**.
Read it before making changes.

## What Blocks Beyond the Stars is

A block-based 3D space crafting game for Windows. The player starts with a small
spaceship, explores procedurally generated planets, mines resources, crafts gear,
researches blueprints, and grows the ship. The current status (Done/Open) lives in
[TODO.md](TODO.md); player-facing operation in [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md).
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
src/BlocksBeyondTheStars.Client.Core/     Unity-free client logic (NetworkClient, ClientWorld), netstandard2.1
tests/BlocksBeyondTheStars.Tests/         xUnit tests (server/shared)
tests/BlocksBeyondTheStars.Client.Tests/  headless client<->server integration (real NetworkClient vs real GameServer)
client/                         Unity project (incl. Assets/Tests EditMode/PlayMode suites)
ai-backend/                     optional Python LLM service (missions, NPC/ship-AI text); offline-safe
tools/                          editor-export merge tools + AI asset generation (tools/ai-assets)
data/                           data-driven JSON definitions (blocks, items, recipes, ...)
data/locales/                   localization resource files (en.json, de.json)
docs/user/                      player-facing manual (USER_MANUAL.md)
docs/developer/                 ARCHITECTURE.md + design/how-it-works docs + ADRs (docs/developer/adr/); see docs/developer/README.md index
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
8. **Unity-free client logic goes in `Client.Core`** (netstandard2.1, no `UnityEngine`) so the *same*
   `NetworkClient`/`ClientWorld` can be tested headless against the real server. Unity-coupled code
   (meshers, MonoBehaviours, views) stays in the client asmdef. A new shared DLL Unity consumes must be
   listed in **both** the client asmdef's `precompiledReferences` **and** `scripts/sync-client-libs.ps1`
   (miss either → the player build fails `CS0246` while the Editor still compiles). See
   [docs/developer/CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md).

## Build & test

```powershell
dotnet build BlocksBeyondTheStars.sln      # build everything
dotnet test                      # run all xUnit tests (server/shared + headless client<->server)
./scripts/run-tests.ps1          # selectable suites: Dotnet, ClientCore, UnityEdit, UnityPlay, All
dotnet run --project src/BlocksBeyondTheStars.GameServer   # start a local server
./scripts/build-client.ps1       # full Windows client (shared libs + bundled server + Unity batch build)
```

`run-tests.ps1` defaults to the fast .NET suites (`Dotnet` + `ClientCore`); the Unity Editor suites
(`UnityEdit` EditMode, `UnityPlay` PlayMode-vs-real-server-exe) are opt-in via `-Suites`. How the client is
tested against the real server is documented in
[docs/developer/CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md).

To confirm a client rebuild actually happened, check the `BlocksBeyondTheStars.Client.dll` timestamp in the
build output (the `.exe` timestamp is not reliable). The full build guide — pipeline details,
freshness verification and the known "works in the Editor, broken in the build" pitfalls — is in
[docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md).

## Project conventions

- C#: `LangVersion=latest`, nullable enabled, 4-space indent, Allman braces
  (see `.editorconfig`). Records/`init` work on netstandard2.1 via the
  `IsExternalInit` polyfill in `BlocksBeyondTheStars.Shared/Compatibility`.
- The author is JAM Software; follow sensible, consistent C# conventions.

## Before every commit and push (mandatory)

Documentation must never drift behind the code. Before *every* commit, and again before *every* push,
run through this checklist:

1. **Update [TODO.md](TODO.md) — always.** It is the single source of truth for Done/Open. Before
   committing, reflect the current state there: mark finished work ✅ (with the date and, once pushed,
   the commit hash), add anything newly discovered to the backlog, and correct any status that the change
   makes stale. A commit that changes behaviour but leaves TODO.md untouched is incomplete.
2. **Decide whether the docs in [docs/](docs/) need to change — decide this yourself.** Ask: does this
   change alter *how something works* in a way an existing doc describes (e.g. [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md),
   [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md), [docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md), a concept/design
   doc, or an ADR)? If yes, update that doc in the same commit. Player-facing controls/mechanics/commands
   changes **must** update `USER_MANUAL.md`.
3. **Decide whether a new doc is warranted.** If the change introduces a whole subsystem or a non-obvious
   "how it is done" that no existing doc covers, add a new doc under `docs/`. Keep `docs/` to *documentation
   and design/how-it-works notes* — not throwaway pre-implementation checklists (their status lives in
   TODO.md). When you add a new doc, **also update the root [README.md](README.md)** if it lists or links
   the docs, so the new doc is discoverable.
4. **When in doubt, ask the user.** If it is unclear whether a doc should be updated, rewritten, or newly
   created — or whether a stale plan should be archived — surface it and ask before committing, rather than
   guessing.

## Roadmap

See [TODO.md](TODO.md) for the current Done/Open status (the single status doc).
