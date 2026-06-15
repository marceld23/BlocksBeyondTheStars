# Blocks Beyond the Stars

A block-based 3D space crafting game for Windows. You start with a small spaceship,
explore procedurally generated planets, mine resources, craft gear, research blueprints,
and grow your ship — built from day one as a client/server game so multiplayer and
self-hosting come naturally.

> **Naming note:** the game was renamed on 2026-06-12 from its former working title "Spacecraft" —
> display title, solution, namespaces, binaries and asset paths all use **Blocks Beyond the Stars** /
> `BlocksBeyondTheStars` now. Old client installs migrate their settings/saves automatically on first start.

## What is it? (the short pitch)

You wake aboard **your own spaceship** at the edge of an unknown universe. Out there are
**many star systems** — each with its own sun, its own planets, moons and asteroid fields.
**Every world is unique**: from cold, airless rocks adrift in space — barren but rich with
ore — to lush, **heavily forested** worlds teeming with life, with frozen tundra, lava
fields, fungal groves, floating skylands and dive-able oceans in between. And every alien
world grows **its own flora and fauna**. Land on any of them and start digging — every
block can be mined, reshaped and rebuilt. **Tame** the wild creatures you meet into loyal,
named companions, and craft a **hover speeder** to range far across the surface.

Gather resources and **knowledge** to unlock blueprints, smelt and craft, and grow your
ship — then fly real, system-scale routes between worlds and **jump from one star system to
the next**. Dock at **space stations**, walk into **villages and cities** full of NPCs to
trade and take missions, and build your own — **your own planet bases and even your own
space stations**, designed block by block in the in-game **ship editor** and a **structure
editor** for crafting your own space stations, villages and cities. Along the way you collect
**DataQubes** (each one a little arcade
minigame), and, if you want, you can follow **The VEGA Protocol** — an optional, genuinely
exciting story narrated by your ship's AI *VEGA* that builds to a real finale.

Play **solo**, **host a world for friends**, or **join a dedicated server**. In multiplayer
you can **form alliances** and then share bases and structures with your allies — the world
is shared, persistent, and self-hostable (even on a Raspberry Pi).

**In one session you might:**

- 🪐 Explore **unique** planets, moons and asteroids across many star systems — each with its own sun, terrain and **alien flora & fauna**
- ⛏️ Mine, smelt and craft, and gather **knowledge** to unlock new blueprints
- 🚀 Grow and redesign your ship, then fly between worlds and **jump between star systems**
- 🛰️ Dock at **space stations** and walk into **villages & cities** full of NPCs — trade, take missions
- 🏗️ Build your **own bases and your own space stations**; link teleporter pads, build with colored light
- 🛠️ Design your own **ships, space stations, villages & cities** in the in-game **ship & structure editors**
- 🐾 **Tame wild creatures** into named, loyal companions that travel with you
- 🛻 Craft a **hover speeder** to race across planet surfaces
- 🤝 In multiplayer, **form alliances** and share bases and structures with your allies
- 🎨 Make it yours: design your avatar's face, **dye any material**, paint with light
- 🕹️ Collect **DataQubes** — arcade minigames — and read the in-game **Codex** wiki
- 📖 Follow the exciting **VEGA Protocol** story (optional, narrated by your ship AI) to its finale
- 🎵 Set the mood with a built-in music library

## About this project

**Blocks Beyond the Stars — JuMaVe Games**

A family project:

- **Justus Dütscher** (son) — ideas, game design, playtesting
- **Marcel Dütscher** (father) — technical lead and AI prompting
- **Verena Dütscher** (mother) — game balancing and playtesting

We're hoping for community support! Get involved and join in — your name could soon be here too!

*(This is the same credit shown in the game's Credits screen — `ui.credits.body`.)*

> **Status & docs:** [TODO.md](TODO.md) is the single Done/Open status doc; player operation is in
> [docs/USER_MANUAL.md](docs/USER_MANUAL.md); building and verifying builds is in
> [docs/DEVELOPER.md](docs/DEVELOPER.md); deeper design notes are under [docs/](docs/). (The
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
| Client | Unity 6 LTS (6000.4.x), URP + C# (Windows) — see [`client/`](client/) |
| Server | .NET 8, standalone console host (no Unity runtime) |
| Admin UI | ASP.NET Core 8 minimal API + HTML dashboard |
| Database | SQLite (default, portable, Raspberry Pi friendly); PostgreSQL later |
| Realtime net | LiteNetLib (UDP) + MessagePack |
| Shared logic | `netstandard2.1` so the same code runs in Unity *and* the server |

## Repository layout

```
src/BlocksBeyondTheStars.Shared/          data models, data-driven definitions, localization, protocol DTOs
src/BlocksBeyondTheStars.WorldGeneration/ seed-based deterministic chunk generation
src/BlocksBeyondTheStars.Persistence/     SQLite repository, savegame layout, autosave, backups
src/BlocksBeyondTheStars.Networking/      transport abstraction (LiteNetLib + loopback), messages, codec
src/BlocksBeyondTheStars.GameServer/      authoritative tick loop + console host
src/BlocksBeyondTheStars.Api/             admin web UI + API
src/BlocksBeyondTheStars.Tools/           validate/info/backup CLI
tests/BlocksBeyondTheStars.Tests/         xUnit tests
client/                         Unity project (scripts + scaffold; open in the Unity Editor)
ai-backend/                     optional Python LLM service (mission/NPC/ship-AI text) — game runs without it
tools/                          editor-export merge tools (Python) + AI asset generation (tools/ai-assets)
data/                           data-driven content (blocks, items, recipes, blueprints, modules, planets)
data/locales/                   localization (en.json, de.json)
docs/                           user manual, self-hosting guide, design/plan docs, ADRs
scripts/                        build-client.ps1 + publish scripts for self-hosting packages
```

## Build, test, run

Requires the **.NET 8 SDK**.

```powershell
dotnet build BlocksBeyondTheStars.sln       # build everything
dotnet test                       # run all tests
dotnet run --project src/BlocksBeyondTheStars.GameServer   # start a local dedicated server (UDP 31415)
dotnet run --project src/BlocksBeyondTheStars.Api          # start the admin UI (http://127.0.0.1:31416)
```

The playable Windows client is built with `scripts/build-client.ps1` (publishes the shared libs +
the bundled server and runs a Unity batch build; requires the Unity Editor). See
[docs/DEVELOPER.md](docs/DEVELOPER.md) for the full build guide, how to verify a build is
fresh, and known build pitfalls.

Server configuration lives in `config/server.json` (created on first run) and is editable
via the admin UI. See [docs/SELF_HOSTING.md](docs/SELF_HOSTING.md).

### Admin dashboard

`BlocksBeyondTheStars.Api` is a standalone web host serving the server admin dashboard at
**`http://127.0.0.1:31416/`** (`adminBindAddress`/`adminPort` in `config/server.json`):
status, config editing, backups, log tail and mission/content tools — optionally gated by
an admin password. Start it with `dotnet run --project src/BlocksBeyondTheStars.Api`, or in a
server package run `BlocksBeyondTheStars.Api(.exe)` from the install folder (next to the game
server, so both share `config/server.json`). URL, auth and the full HTTP API are documented
in [docs/SELF_HOSTING.md](docs/SELF_HOSTING.md) §5.

### Tools CLI

```powershell
dotnet run --project src/BlocksBeyondTheStars.Tools -- validate data
dotnet run --project src/BlocksBeyondTheStars.Tools -- info saves world_001
dotnet run --project src/BlocksBeyondTheStars.Tools -- backup saves world_001
```

### Optional AI backend (LLM)

[`ai-backend/`](ai-backend/) is a separate, optional Python service (FastAPI + LangChain/LangGraph,
provider-agnostic via the OpenAI-compatible chat API — LM Studio / OpenAI / Claude, chosen by env)
that writes mission texts and NPC/ship-AI dialogue. The game is fully playable without it — every
AI text has a scripted, localized fallback, and the C# server validates everything the service
returns. See [ai-backend/README.md](ai-backend/README.md) and
[docs/AI_MISSION_BACKEND.md](docs/AI_MISSION_BACKEND.md).

### Self-hosting packages

```powershell
./scripts/publish-server.ps1            # win-x64, linux-x64, linux-arm64 (Raspberry Pi 5)
```
Produces self-contained, single-file packages (no .NET install needed on the host) under
`artifacts/`. On Linux/macOS use `scripts/publish-server.sh`.

Players can also download and install the Windows client **from the running server's own web page**:
`scripts/publish-client-installer.ps1` builds a [Velopack](https://velopack.io) installer + auto-update
feed, the admin host serves it at `/download` + `/updates`, and the `/portal` page links it. See
[docs/SELF_HOSTING.md](docs/SELF_HOSTING.md) §9.

## Adding content (data-driven)

Blocks, items, recipes, blueprints, ship modules and planets are JSON in `data/`; no code
changes are needed to add content. Player-facing names use localization keys resolved from
`data/locales/{en,de}.json`. Validate with `BlocksBeyondTheStars.Tools validate`.

## Status

A fully playable client + server game: **multiple star systems** (each with its own sun, planets,
moons and asteroid fields), procedurally generated worlds that wrap east–west (walk around the
planet, seam-free), 18 planet types including exotic ones (skylands, fungal, corrupted, ocean,
salt flats, …) with their own flora and fauna, swimming/diving, creature taming, a craftable hover
speeder, mining → crafting → blueprints →
ship building, real system-scale space flight (with jumps between systems) with stations,
settlements and NPCs, peaceful NPC trader traffic, the
**"VEGA Protocol" story campaign** (a swappable, story-agnostic engine with lore fragments, three
Guardian machine types and a two-route finale), multiplayer with per-player ships, **player
alliances**, shared bases and trading, planet **bases + teleporter pads**, material dyeing and
colored-light building, in-game customization (avatar pixel-face editor, content/ship/station
editors), an in-game **Codex wiki + data-cube arcade minigames**, a built-in **music library**,
the VEGA ship-AI onboarding/advisor companion, world-creation options, and an optional LLM backend
for dynamic dialogue/mission text. Self-hostable dedicated server (Raspberry Pi friendly).
Currently 584 xUnit tests pass.

See [TODO.md](TODO.md) for the current Done/Open status, the
[user manual](docs/USER_MANUAL.md) for controls/mechanics/commands, and [AGENTS.md](AGENTS.md)
for contributor rules.
