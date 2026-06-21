# Self-Hosting a Blocks Beyond the Stars Server

Blocks Beyond the Stars is designed so players can host their own server on a Windows PC, a Linux box,
or a VPS — without installing .NET (the packages are self-contained).

## 1. Get a server package

Download or build a package for your platform:

| Platform | Package |
|---|---|
| Windows x64 | `blocks-beyond-the-stars-server-win-x64.zip` |
| Linux x64 | `blocks-beyond-the-stars-server-linux-x64.zip` |
| Linux ARM64 | `blocks-beyond-the-stars-server-linux-arm64.zip` |

Build them yourself from a checkout with the .NET 8 SDK:

```powershell
./scripts/publish-server.ps1                 # Windows
```
```bash
./scripts/publish-server.sh                  # Linux/macOS
```

## 2. Run

```text
1. Unzip the package.
2. Run the server executable:
     Windows:  BlocksBeyondTheStars.GameServer.exe
     Linux:    ./BlocksBeyondTheStars.GameServer
3. A default config/server.json is created on first launch.
4. (Optional) Run the admin UI:  BlocksBeyondTheStars.Api(.exe)
5. Friends connect to your IP on the gameplay port.
```

On low-power ARM64 boards, prefer an SSD over a microSD/eMMC for the world database to
reduce wear and improve autosave performance.

## 3. Configuration (`config/server.json`)

Created on first run; editable directly or through the admin UI.

| Key | Meaning | Default |
|---|---|---|
| `serverName` | Display name | `Blocks Beyond the Stars Server` |
| `worldName` | Save folder under `saves/` | `world_001` |
| `gameplayPort` | UDP port for the game (open/forward this) | `31415` |
| `adminPort` | HTTP port for the admin UI | `31416` |
| `maxPlayers` | Connection cap | `12` |
| `serverPassword` | Required to join (empty = none) | `""` |
| `whitelistEnabled` / `whitelist` | Restrict who may join | `false` / `[]` |
| `adminPlayers` | Player names granted the Admin role on join (CLI: `--admins "a,b"`) | `[]` |
| `adminPassword` | Required for admin API calls | `""` |
| `autoSaveIntervalMinutes` | Autosave cadence | `5` |
| `backupIntervalMinutes` | Backup cadence | `60` |
| `viewDistanceChunks` | Chunk stream radius | `4` |
| `tickRate` | Simulation Hz (10–20 recommended) | `15` |
| `seed` | World seed (0 = derive from world name) | `0` |
| `startPlanet` | Starting planet type | `rocky` |
| `adminBindAddress` | Admin UI bind address | `127.0.0.1` |
| `aiLevel` | Optional AI text backend: `Off`, `Suggest` (AI missions land as drafts), `Auto` (published) — see §8 | `Off` |
| `aiBackendUrl` | Base URL of the optional AI backend | `http://127.0.0.1:8077` |

### Environment-variable overrides (containers)

Every key above can also be set with a `BBS_*` environment variable, which is the natural way to
configure the [Docker image](#10-running-in-docker). Precedence is **`server.json` < environment <
command line**, so env vars override the file but the in-game host's CLI flags still win.

| Variable | Maps to | Variable | Maps to |
|---|---|---|---|
| `BBS_SERVER_NAME` | `serverName` | `BBS_ADMIN_PASSWORD` | `adminPassword` |
| `BBS_WORLD` | `worldName` | `BBS_ADMIN_BIND` | `adminBindAddress` |
| `BBS_PORT` (`BBS_GAMEPLAY_PORT`) | `gameplayPort` | `BBS_ENABLE_WEBSOCKET` | `enableWebSocket` |
| `BBS_ADMIN_PORT` | `adminPort` | `BBS_WEBSOCKET_BIND` | `webSocketBindAddress` |
| `BBS_MAX_PLAYERS` | `maxPlayers` | `BBS_SAVES` | `savesRoot` |
| `BBS_PASSWORD` (`BBS_SERVER_PASSWORD`) | `serverPassword` | `BBS_DATA` | `dataDir` |
| `BBS_ADMINS` | `adminPlayers` (comma-separated) | `BBS_USERCONTENT` | `userContentDir` |
| `BBS_SEED` | `seed` | `BBS_TICK_RATE` | `tickRate` |
| `BBS_START_PLANET` | `startPlanet` | `BBS_VIEW_DISTANCE` | `viewDistanceChunks` |
| `BBS_AI_LEVEL` | `aiLevel` | `BBS_AI_BACKEND_URL` | `aiBackendUrl` |

### Player names & name verification

A player's name keys their server-side state (inventory, position, role). Two protections
are built in: a name that is **currently online** cannot join a second time, and the first
join under a name **claims** it with a per-install client secret — later joins must present
the same secret or are rejected ("name belongs to another player"). Only a hash of the
secret is stored in the save. The very first player ever to join a fresh world becomes its
**WorldAdmin**; names listed in `adminPlayers` get the Admin role on join.

> **In-game hosting:** the client's main-menu **Host Game** runs this same server as a
> child process (any singleplayer save, "open to LAN" style) with `--max-players`,
> an optional `--password`, and the host's name in `--admins`.

## 4. Ports & networking

- **Gameplay**: UDP `gameplayPort` (default 31415). Forward this on your router for
  internet play.
- **Admin UI**: HTTP `adminPort` (default 31416), bound to `127.0.0.1` by default so it
  is **not** reachable from outside. Only change `adminBindAddress` if you understand the
  risk, and always set an `adminPassword` first.

## 5. Admin UI (dashboard + HTTP API)

The admin UI is its own small executable, **`BlocksBeyondTheStars.Api(.exe)`**, shipped in the
server package next to the game server. It reads the **same `config/server.json`**
(resolved relative to its own folder), so run it from the server install directory. Start
it and open the dashboard in a browser:

```text
http://127.0.0.1:31416/        i.e. http://<adminBindAddress>:<adminPort>/
```

The dashboard shows server/world status and lets you edit the configuration, create and
list backups, tail the server log, and manage admin missions / content packs. If an
`adminPassword` is configured, enter it in the dashboard's password field — it is sent as
an `X-Admin-Password` header with every API call. Without a password the UI relies on the
loopback bind and shows a warning in the status. Live operations on a *running* server
(kick/ban, start/stop) are intentionally not part of this UI.

From a source checkout the same UI runs with
`dotnet run --project src/BlocksBeyondTheStars.Api`. Note that it then resolves
`config/server.json` relative to its **own build folder** (`bin/Debug/net8.0/`), not the
game server's — for a shared config run both executables from one published install
directory.

### HTTP API

Everything the dashboard does is plain HTTP under `/api` (JSON; add the
`X-Admin-Password` header when a password is set):

| Route | Meaning |
|---|---|
| `GET /api/status` | Server/world snapshot (name, world, ports, players, backups, warnings) |
| `GET` / `PUT /api/config` | Read / replace `config/server.json` |
| `GET` / `POST /api/backups` | List / create a world backup |
| `GET /api/logs?lines=200` | Tail the server log |
| `GET` / `POST /api/missions`, `DELETE /api/missions/{id}` | Admin mission editor |
| `GET` / `POST /api/content-pack` | Export / import a content pack |

Example call without the dashboard:

```powershell
Invoke-RestMethod http://127.0.0.1:31416/api/status -Headers @{ 'X-Admin-Password' = '<password>' }
# or: curl -H "X-Admin-Password: <password>" http://127.0.0.1:31416/api/status
```

The host also serves a public-facing **`/portal`** landing page — a polished page with the
JuMaVe Games + game logos, a one-click client download and the in-app update URL — plus the
`/play` placeholder for the future browser client. See §9 for distributing the client this way.

## 6. Backups & saves

- A world lives in `saves/<worldName>/world.db` (SQLite) with `backups/` and `logs/`
  alongside — fully portable; copy the folder to move or back up a world.
- Backups are transactionally consistent copies (`VACUUM INTO`). Create them from the
  admin UI, the Tools CLI (`BlocksBeyondTheStars.Tools backup saves <world>`), or on a schedule.
- The world is `seed + parameters + player edits`: the procedural terrain is regenerated,
  only your changes are stored, keeping saves small.

## 7. Singleplayer & in-game hosting

Singleplayer launches the **same** dedicated server as a bundled child process bound to
loopback, so there is no separate code path — what works solo works in multiplayer. The
main menu's **Host Game** opens that exact path to friends: it raises the player cap, adds
an optional join password, and announces the host's LAN address in-game.

## 8. Optional AI backend (dynamic LLM text)

The server can use the optional Python service in [`ai-backend/`](../../ai-backend/) for
dynamic flavour text: **NPC greetings** (personal speech bubbles at vendors and mission
boards), **mission-board flavour text**, occasional **VEGA ship-AI banter**, and
admin-generated missions (`/ai <prompt>` in chat). The game is fully playable without it —
every AI text has a localized scripted fallback (DE+EN), and with `aiLevel = Off` (the
default) the server never contacts the backend.

> **Running in Docker?** The image bundles this backend and starts it automatically when you mount its
> `.env` — you don't run the steps below by hand. See [§10 → Optional AI text backend](#optional-ai-text-backend-in-the-container).

1. **Start the backend** (from a repo checkout; needs [uv](https://docs.astral.sh/uv/)):

   ```bash
   cd ai-backend
   uv run uvicorn app.main:app --host 127.0.0.1 --port 8077
   ```

2. **(Optional) Configure an LLM provider.** Copy `ai-backend/.env.example` to
   `ai-backend/.env` and set the OpenAI-compatible endpoint — **LM Studio** (self-hosted),
   **OpenAI**, or **Claude**, selected purely by env:

   ```bash
   BBTS_AI_BASE_URL=http://localhost:1234/v1   # e.g. LM Studio
   BBTS_AI_MODEL=local-model
   BBTS_AI_API_KEY=lm-studio                   # ignored by LM Studio
   ```

   With **no** provider configured the backend still works and returns deterministic
   bilingual template text.

3. **Point the game server at it** in `config/server.json`: set `aiLevel` to `Suggest`
   (valid AI missions are stored as inactive drafts for admin review) or `Auto` (valid AI
   missions are published immediately); `aiBackendUrl` defaults to
   `http://127.0.0.1:8077`. Any level other than `Off` also enables the greeting / board
   text / VEGA banter endpoints. Restart the server after the change.

4. **Verify**: `http://127.0.0.1:8077/health` should return
   `{ "status": "ok", "llm": true|false }` (`llm` says whether a model is configured).
   In-game, an admin can type `/ai <prompt>` in chat to generate a mission.

The C# server stays authoritative: AI mission plans are validated against the loaded
content and reward counts are clamped; greetings are flavour only. Backend errors are
logged and the game continues with the static text — it never crashes the server. Endpoint
details: [ai-backend/README.md](../../ai-backend/README.md); design:
[AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md).

## 9. Distributing the client from the server (installer + auto-update)

Players can install the Windows client straight from the server's own web page — no manual
zip hand-off. This uses [Velopack](https://velopack.io) (MIT) for the installer and updates.

**Build the installer** (needs a built client and the `vpk` CLI — auto-installed on first run):

```powershell
./scripts/sync-velopack-libs.ps1     # ONCE: vendor the Velopack runtime into the client, then refresh Unity
./scripts/build-client.ps1           # build the Windows player (includes the update runtime)
./scripts/publish-client-installer.ps1 -ServeDir <your server install dir>
```

That produces `BlocksBeyondTheStars-win-Setup.exe` plus an update feed
(`releases.win.json` + `*-full.nupkg`) and, with `-ServeDir`, copies them into the install's
`clients/` folder so the API serves them. Add `-Msi` to also build the machine-wide WiX MSI.

> The release version comes from `PlayerSettings.bundleVersion` (the single source of truth) unless you pass
> `-Version`; local/dev builds carry `0.1.0-dev`, which Velopack accepts (it requires `packVersion >= 0.0.1`).
> For *public* downloads you usually don't build by hand: pushing a git tag `vX.Y.Z` makes CI publish a GitHub
> Release with the Setup.exe + MSI + Portable.zip — see
> [DEVELOPER.md → Releases & versioning](DEVELOPER.md#releases-github-actions--versioning). The self-host
> portal below is the LAN/own-server channel; the two are complementary.

**On the host:** start `BlocksBeyondTheStars.Api`, and (for LAN/internet reach) bind it beyond loopback —
set `adminBindAddress` to the LAN IP or `0.0.0.0` **and** set an `adminPassword` first (§4). Only `/api/*`
is password-gated; `/portal`, `/download` and `/updates` stay public so players can reach them.

**Players:**

1. Open `http://<server-ip>:<adminPort>/portal` (default port 31416) in a browser.
2. Click **Download the Windows client** (served from `/download`) and run the installer
   (per-user, no admin rights; an unsigned build shows a one-time SmartScreen "More info → Run anyway").
3. Launch the game and **Join** the server's IP on the gameplay port (default 31415).
4. **Auto-update:** in **Settings → Software update**, paste the update URL shown on the portal
   (`http://<server-ip>:<adminPort>/updates`) and use **Check for updates**. Publishing a higher
   version with the script above lets installed clients self-update from the server.

Updates only apply to an *installed* client (not a dev/Editor or portable-zip run). Each published
version must be higher than the last.

## 10. Running in Docker

The dedicated server (game server **and** admin/portal/download UI, plus the optional AI text backend)
can run as a single Linux container. This is **optional**, but a tagged release **does** build and
publish the image to the GitHub Container Registry (GHCR), so you can just pull it:

```bash
docker pull ghcr.io/marceld23/blocks-beyond-the-stars-server:latest   # or a :X.Y.Z version tag
```

You can also build it yourself (`docker compose build` / `docker build`) or trigger the standalone
[`Docker`](../../.github/workflows/docker.yml) workflow from the Actions tab. The server is
Linux x64+ARM64 native, so the container runs on Linux, macOS, Windows (Docker Desktop / WSL2), a NAS
or a VPS. The **game client stays Windows-only** — the container hosts the server and hands the Windows
installer out via `/download`.

**One image, asymmetric processes.** [`Dockerfile`](../../Dockerfile) publishes the headless projects
onto the .NET 8 ASP.NET runtime image and runs them through
[`docker/entrypoint.sh`](../../docker/entrypoint.sh) under `tini` (PID 1). The split is deliberate:

- the **game server** is the critical foreground process — it receives the shutdown signal and saves
  the world before exiting;
- the **admin API** is a best-effort sidecar — it auto-restarts and never takes the game server (and
  the players on it) down with it;
- the **AI text backend** (Python) is baked in but only started when you provide its `.env` (below) —
  also a best-effort, auto-restarting sidecar.

`docker stop` sends `SIGTERM`; the entrypoint translates that into the `SIGINT` the server's clean
drain-and-save path listens for, so the world is always saved on shutdown (give it time with a
`stop_grace_period`/`--stop-timeout` of ~60 s).

### Try it locally (Docker Desktop)

A quick end-to-end test on your own machine (Docker Desktop on Windows/macOS/Linux):

1. **Run it** (throwaway — no volumes, so the world is discarded on `rm`):

   ```bash
   docker run -d --name bbts -p 31415:31415/udp -p 31416:31416/tcp \
     -e BBS_ADMIN_PASSWORD=test123 -e BBS_SERVER_NAME="Local Test" \
     ghcr.io/marceld23/blocks-beyond-the-stars-server:latest
   ```

   Docker pulls the image automatically. Add `-e BBS_FETCH_CLIENT=0` to skip the GitHub
   client-installer download for a pure offline server test.

2. **Check it's up** — open the admin dashboard at <http://localhost:31416/> and the public portal at
   <http://localhost:31416/portal>. `BBS_ADMIN_PASSWORD` gates the `/api/*` calls (the dashboard
   prompts for it); the dashboard/portal pages themselves are public.

3. **Connect the game** — launch the Windows client → **Join** → host **`127.0.0.1`**, port **`31415`**.

4. **Watch it** — `docker logs -f bbts`, or in **Docker Desktop → Containers → `bbts`** use the
   **Logs / Inspect / Exec** tabs.

5. **Stop cleanly** — `docker stop bbts` (SIGTERM → the world is saved), then `docker rm bbts`.

For a test that survives restarts, add the named volumes shown under [Volumes](#volumes-what-to-persist).

### Quick start (Compose)

```bash
docker compose up -d        # build + run (see docker-compose.yml)
docker compose logs -f      # follow the server log
docker compose down         # SIGTERM -> clean world save, then stop
```

Configure with the `BBS_*` environment variables (see §3). At minimum set `BBS_ADMIN_PASSWORD` before
exposing the admin port. Ports: **31415/udp** (native client), **31415/tcp** (browser WebSocket, only
when `BBS_ENABLE_WEBSOCKET=true`), **31416/tcp** (admin + portal + download).

### Or plain `docker run`

```bash
docker build -t bbts-server .
docker run -d --name bbts \
  -p 31415:31415/udp -p 31415:31415/tcp -p 127.0.0.1:31416:31416/tcp \
  -e BBS_ADMIN_PASSWORD=change-me -e BBS_MAX_PLAYERS=12 \
  -v bbts-saves:/app/saves -v bbts-config:/app/config -v bbts-clients:/app/clients \
  --stop-timeout 60 bbts-server
```

### Volumes (what to persist)

| Volume | Holds |
|---|---|
| `/app/saves` | SQLite world + `backups/` + `logs/` **and `/bump` bug reports** (`<world>/bumps/`) |
| `/app/config` | `server.json` (created on first run; env vars override it) |
| `/app/clients` | the published Windows installer the portal serves at `/download` |

### Client download from the container

A Linux container can't *build* the Windows installer, so on start the entrypoint pulls the newest
`*Setup.exe` from the latest GitHub Release into `/app/clients` (best-effort; controlled by
`BBS_FETCH_CLIENT=1`/`0` and `BBS_CLIENT_REPO`). The portal (`/portal`) and one-click `/download` then
work as in §9. Without it the portal still runs and `/download` reports "no installer published yet";
you can instead drop a `Setup.exe` into the `clients` volume yourself.

### Bug reports (`/bump`) in a container

The in-game **bug report / `/bump`** feature works unchanged from a container: the client sends the
report (with its screenshot) over the network and the server writes a JSON snapshot (+ the JPG) to
`saves/<world>/bumps/`. Because that lives on the **`saves` volume**, reports survive restarts and are
retrievable from the host — browse the volume, or `docker cp bbts:/app/saves/<world>/bumps ./bumps`.
(Reports only divert to a repo's `bugreports/server/` when the server runs from inside a git checkout,
which a normal container is not.)

### Optional AI text backend in the container

The Python AI backend from §8 is **bundled into the image** (its own venv) but **only starts when you
configure it** — otherwise no Python process runs. It is enabled when either:

- an **`ai-backend/.env`** file is mounted into the container at `/app/ai-backend/.env` (the normal
  way — copy [`ai-backend/.env.example`](../../ai-backend/.env.example) and fill in one provider), or
- a **`BBTS_AI_BASE_URL`** environment variable is set on the container.

Set `BBS_AI_BACKEND=1`/`0` to force it on/off explicitly. When it starts it listens on the in-container
`127.0.0.1:8077`, which is the game server's default `aiBackendUrl` — so you only need to turn the
server's usage on with **`BBS_AI_LEVEL=Suggest`** (AI missions as drafts) or **`Auto`** (published);
any non-`Off` value also enables NPC greetings / board flavour / VEGA banter (see §8). With no `.env`
and `BBS_AI_LEVEL=Off` (the defaults), the game is fully AI-free and no backend runs.

```yaml
# docker-compose.yml — enable the bundled AI backend
services:
  server:
    environment:
      BBS_AI_LEVEL: "Suggest"
    volumes:
      - ./ai-backend/.env:/app/ai-backend/.env:ro
```

> Bundling LangChain/LangGraph makes the image noticeably larger. The AI backend is still entirely
> optional and the game runs identically without it; only the image size grows.
