# Blocks Beyond the Stars

### 🌐 [Website](https://www.blocksbeyondthestars.com/en) &nbsp;·&nbsp; ⭐ [Rate on Itch.io](https://jumavegames.itch.io/blocks-beyond-the-stars) &nbsp;·&nbsp; ⬇️ [Releases](https://github.com/marceld23/BlocksBeyondTheStars/releases) &nbsp;·&nbsp; [Contribute / report a bug](CONTRIBUTING.md)

**Contents:**
[What is it?](#what-is-it-the-short-pitch) ·
[Screenshots](#screenshots) ·
[About this project](#about-this-project) ·
[Project Status](#project-status) ·
[System requirements](#system-requirements) ·
[Windows security notice](#windows-security-notice) ·
[Guiding principle](#guiding-principle) ·
[Tech stack](#tech-stack) ·
[Repository layout](#repository-layout) ·
[Build, test, run](#build-test-run) ·
[Adding content](#adding-content-data-driven) ·
[Status](#status)

> 🎮 **Blocks Beyond the Stars is also available on itch.io** — get the game at
> **[jumavegames.itch.io/blocks-beyond-the-stars](https://jumavegames.itch.io/blocks-beyond-the-stars)**.
> The home page with all the details lives at [www.blocksbeyondthestars.com/en](https://www.blocksbeyondthestars.com/en).

A block-based 3D space crafting game for Windows. You start with a small spaceship,
explore procedurally generated planets, mine resources, craft gear, research blueprints,
and grow your ship — built from day one as a client/server game so multiplayer and
self-hosting come naturally.

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
is shared, persistent, and self-hostable.

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
- 📻 **Talk to other players** by text **and live voice** over your radio — upgrade it from planet-wide to system-wide to galaxy-wide reach
- 🎨 Make it yours: design your avatar's face, **dye any material**, paint with light
- 🕹️ Collect **DataQubes** — arcade minigames — and read the in-game **Codex** wiki
- 📖 Follow the exciting **VEGA Protocol** story (optional, narrated by your ship AI) to its finale
- 🎵 Set the mood with a built-in music library

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/en/start_screen.png" width="100%" alt="Main menu"><br><sub>Main menu</sub></td>
    <td width="50%"><img src="docs/screenshots/en/planet_surface.png" width="100%" alt="On a planet surface"><br><sub>On a planet surface</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/screenshots/en/space_flight.png" width="100%" alt="Space flight"><br><sub>Space flight between worlds</sub></td>
    <td width="50%"><img src="docs/screenshots/en/cockpit_hud.png" width="100%" alt="Ship cockpit"><br><sub>The ship cockpit</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/screenshots/en/cockpit_menu.png" width="100%" alt="In-game menu"><br><sub>The in-game menu (crafting, tech, ship, map…)</sub></td>
    <td width="50%"></td>
  </tr>
</table>

**Many different worlds** — every planet type has its own terrain, flora and sky:

<table>
  <tr>
    <td width="25%"><img src="docs/screenshots/en/surface_jungle.png" width="100%" alt="Jungle world"><br><sub>Jungle</sub></td>
    <td width="25%"><img src="docs/screenshots/en/surface_lava.png" width="100%" alt="Lava world"><br><sub>Lava</sub></td>
    <td width="25%"><img src="docs/screenshots/en/surface_ice.png" width="100%" alt="Ice world"><br><sub>Ice</sub></td>
    <td width="25%"><img src="docs/screenshots/en/surface_crystal.png" width="100%" alt="Crystal world"><br><sub>Crystal</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="docs/screenshots/en/surface_fungal.png" width="100%" alt="Fungal world"><br><sub>Fungal</sub></td>
    <td width="25%"><img src="docs/screenshots/en/surface_skylands.png" width="100%" alt="Skylands world"><br><sub>Skylands</sub></td>
    <td width="25%"><img src="docs/screenshots/en/surface_ocean.png" width="100%" alt="Ocean world"><br><sub>Ocean</sub></td>
    <td width="25%"></td>
  </tr>
</table>

<sub>Generated from the live game — see [docs/screenshots/](docs/screenshots/README.md).</sub>

## About this project

**Blocks Beyond the Stars — JuMaVe Games**

A family project:

- **Justus Dütscher** (son) — ideas, game design, playtesting
- **Marcel Dütscher** (father) — technical lead and AI prompting
- **Verena Dütscher** (mother) — game balancing and playtesting

We're hoping for community support! Get involved and join in — your name could soon be here too!
See **[CONTRIBUTING.md](CONTRIBUTING.md)** for how to play, report bugs, or send a pull request,
and our short **[Code of Conduct](CODE_OF_CONDUCT.md)** (the gist: be kind to one another).

*(This is the same credit shown in the game's Credits screen — `ui.credits.body`.)*

> **Status & docs:** [TODO.md](TODO.md) is the single Done/Open status doc; player operation is in
> [docs/user/USER_MANUAL.md](docs/user/USER_MANUAL.md); building and verifying builds is in
> [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md); the system overview is in
> [docs/developer/ARCHITECTURE.md](docs/developer/ARCHITECTURE.md) and every developer doc is indexed in
> [docs/developer/README.md](docs/developer/README.md). (The original German requirement specs under `plans/`
> were consolidated and removed.)
> Docs and code comments are **English**. In-game text is **bilingual (German + English)**.

## Project Status

Blocks Beyond the Stars is a free in-development game. Bugs, missing content and compatibility issues are expected. Multiplayer and server behavior may change between versions.

The software is provided as-is under the terms of the license included in this repository.

## System requirements

The **game client is Windows-only**, but the **server is cross-platform** — so a Linux/macOS machine,
a NAS or a VPS (including via Docker) can host a world that Windows players join.

**Game client (to play)**

- **Windows 10/11 (64-bit).**
- A GPU with DirectX 11+ support (Unity 6 / URP) and a few GB of free disk for the client + worlds.
- The client always talks to a server: a local one started automatically in singleplayer / "Host
  Game", or a remote dedicated server.

**Dedicated server (to host)**

- **OS-independent.** Self-contained packages (no .NET install needed) ship for **Windows x64,
  Linux x64 and Linux ARM64**; build them with `scripts/publish-server.ps1` / `.sh`.
- Or run the **Docker image** on any Docker host — Linux, macOS, Windows (Docker Desktop / WSL2), a
  NAS or a VPS. Pull it from GHCR (`ghcr.io/marceld23/blocks-beyond-the-stars-server`) or build it
  locally. See [SELF_HOSTING.md §10](docs/developer/SELF_HOSTING.md#10-running-in-docker).
- Lightweight: **no GPU**, modest CPU/RAM. On low-power ARM64 boards prefer an SSD over a
  microSD/eMMC for the world database.
- From source you only need the **.NET 8 SDK** (see [Build, test, run](#build-test-run)).

## Windows security notice

This Windows build is **currently not digitally signed**. Because of that, Windows 11 /
Microsoft Defender SmartScreen may show a warning such as *"Windows protected your PC"* the
first time you start the game.

If you downloaded the game from this GitHub page (or from the
[official releases](https://github.com/marceld23/BlocksBeyondTheStars/releases)) and trust the
source, you can choose **"More info"** and then **"Run anyway"** to start it. If you do not trust
the download source, do not run the game.

Blocks Beyond the Stars uses a local/server-based multiplayer architecture. On first launch,
**Windows Defender Firewall** may ask for permission more than once — for example for the game
client and for the local/server component. To play, host, or connect to multiplayer sessions, you
may need to allow these components through the firewall.

- **Recommended:** allow access for **private networks only**, unless you know you need public
  network access.
- Please **do not disable your firewall**.

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
| Database | SQLite (default, portable); PostgreSQL later |
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
src/BlocksBeyondTheStars.Client.Core/     Unity-free client logic (NetworkClient, ClientWorld), netstandard2.1
tests/BlocksBeyondTheStars.Tests/         xUnit tests (server/shared)
tests/BlocksBeyondTheStars.Client.Tests/  headless client<->server integration tests
client/                         Unity project (scripts + scaffold + Assets/Tests; open in the Unity Editor)
ai-backend/                     optional Python LLM service (mission/NPC/ship-AI text) — game runs without it
tools/                          editor-export merge tools (Python) + AI asset generation (tools/ai-assets)
data/                           data-driven content (blocks, items, recipes, blueprints, modules, planets)
data/locales/                   localization (en.json, de.json)
docs/user/                      player-facing manual (USER_MANUAL.md)
docs/developer/                 architecture, design/how-it-works docs, ADRs (docs/developer/adr/) — see its README.md index
scripts/                        build-client.ps1 + publish scripts for self-hosting packages
```

## Build, test, run

Requires the **.NET 8 SDK**.

```powershell
dotnet build BlocksBeyondTheStars.sln       # build everything
dotnet test                       # run all .NET tests (server/shared + headless client<->server)
./scripts/run-tests.ps1           # selectable test runner (adds the Unity Editor suites with -Suites All)
dotnet run --project src/BlocksBeyondTheStars.GameServer   # start a local dedicated server (UDP 31415)
dotnet run --project src/BlocksBeyondTheStars.Api          # start the admin UI (http://127.0.0.1:31416)
```

The playable Windows client is built with `scripts/build-client.ps1` (publishes the shared libs +
the bundled server and runs a Unity batch build; requires the Unity Editor). See
[docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md) for the full build guide, how to verify a build is
fresh, and known build pitfalls. The Unity client is tested against the **real** game server — the approach is
documented in [docs/developer/CLIENT_TESTING.md](docs/developer/CLIENT_TESTING.md).

Server configuration lives in `config/server.json` (created on first run) and is editable
via the admin UI. See [docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md).

### Admin dashboard

`BlocksBeyondTheStars.Api` is a standalone web host serving the server admin dashboard at
**`http://127.0.0.1:31416/`** (`adminBindAddress`/`adminPort` in `config/server.json`):
status, config editing, backups, log tail and mission/content tools — optionally gated by
an admin password. Start it with `dotnet run --project src/BlocksBeyondTheStars.Api`, or in a
server package run `BlocksBeyondTheStars.Api(.exe)` from the install folder (next to the game
server, so both share `config/server.json`). URL, auth and the full HTTP API are documented
in [docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md) §5.

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
returns. It is also **bundled into the Docker image** and starts automatically when you mount its
`.env` (otherwise no Python process runs). See [ai-backend/README.md](ai-backend/README.md),
[docs/developer/AI_MISSION_BACKEND.md](docs/developer/AI_MISSION_BACKEND.md) and
[SELF_HOSTING.md §10](docs/developer/SELF_HOSTING.md#10-running-in-docker).

### Self-hosting packages

```powershell
./scripts/publish-server.ps1            # win-x64, linux-x64, linux-arm64
```
Produces self-contained, single-file packages (no .NET install needed on the host) under
`artifacts/`. On Linux/macOS use `scripts/publish-server.sh`.

You can also run the server (game server + admin/portal/download UI, plus the optional bundled AI text
backend) as a **Docker container** on any OS. Each tagged release publishes a multi-arch image to GHCR,
so you can just pull it — `docker pull ghcr.io/marceld23/blocks-beyond-the-stars-server:latest` — or
build it locally with `docker compose up -d`. The self-hosting guide has the full setup plus a
step-by-step **local test in Docker Desktop**:
[docs/developer/SELF_HOSTING.md §10](docs/developer/SELF_HOSTING.md#10-running-in-docker)
([try it locally](docs/developer/SELF_HOSTING.md#try-it-locally-docker-desktop)).

Players can also download and install the Windows client **from the running server's own web page**:
`scripts/publish-client-installer.ps1` builds a [Velopack](https://velopack.io) installer + auto-update
feed, the admin host serves it at `/download` + `/updates`, and the `/portal` page links it. See
[docs/developer/SELF_HOSTING.md](docs/developer/SELF_HOSTING.md) §9.

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
for dynamic dialogue/mission text. Self-hostable dedicated server.
Currently 584 xUnit tests pass.

See [TODO.md](TODO.md) for the current Done/Open status, the
[user manual](docs/user/USER_MANUAL.md) for controls/mechanics/commands, and [AGENTS.md](AGENTS.md)
for contributor rules.
