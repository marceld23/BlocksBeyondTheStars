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
| `gameplayPort` | UDP gameplay port for native clients; also the HTTP/WebSocket port when WebSocket is enabled | `31415` |
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
| `enableWebSocket` | Enable browser/WebGL WebSocket gameplay transport | `false` |
| `webSocketBindAddress` | WebSocket HTTP bind host (`+` for all interfaces/reverse proxies) | `localhost` |
| `databaseProvider` | Save backend: `sqlite` or `postgresql` | `sqlite` |
| `postgresConnectionString` | PostgreSQL connection string (prefer env/secret) | `""` |
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
| `BBS_FREE_FLIGHT` | `rules.freeSpaceFlight` | `BBS_SPACE_COMBAT` | `rules.spaceCombat` |
| `BBS_SHIP_WEAPONS` | `rules.shipWeapons` | `BBS_SPACE_NPCS` | `rules.spaceNpcEnemies` |
| `BBS_DATABASE_PROVIDER` (`BBS_DATABASE`) | `databaseProvider` | `BBS_POSTGRES_CONNECTION_STRING` (`DATABASE_URL`) | `postgresConnectionString` |
| `BBS_AI_LEVEL` | `aiLevel` | `BBS_AI_BACKEND_URL` | `aiBackendUrl` |

`BBS_FREE_FLIGHT=true` is useful for hosted WebGL realms where every player should be allowed to launch and fly
manually right away. It also upgrades older world metadata that was saved before free flight became the default,
while leaving the rest of that world's saved rules intact.

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

- **Native gameplay**: UDP `gameplayPort` (default 31415). Forward this on your router for
  desktop native clients.
- **Browser/WebGL gameplay**: when `enableWebSocket=true`, the server also listens for HTTP/WebSocket
  upgrades on `gameplayPort`. Browsers connect with `ws://` or `wss://`; reverse proxies and managed
  hosts must allow WebSocket upgrade traffic to the game server. Azure Container Apps should use HTTP/auto
  ingress to target port 31415 for WebGL, then clients use the app's `wss://...` URL. Native UDP clients
  still need a UDP-capable host.
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
JuMaVe Games + game logos, one-click client downloads (Windows `Setup.exe` at `/download`, Linux
`.AppImage` at `/download-linux`, experimental macOS zip at `/download-mac`) and the in-app update URL.
See §9 for distributing the client this way.

## 6. Backups & saves

- SQLite remains the default. A world lives in `saves/<worldName>/world.db` with `backups/` and
  `logs/` alongside — fully portable; copy the folder to move or back up a local/self-hosted world.
- PostgreSQL is opt-in for hosted dedicated servers: set `databaseProvider` to `postgresql` (or
  `BBS_DATABASE_PROVIDER=postgresql`) and provide `postgresConnectionString` through an environment
  secret such as `BBS_POSTGRES_CONNECTION_STRING`. Each world is isolated into its own schema named
  from the world name, while logs, bug reports and JSON backup exports still use `saves/<worldName>/`.
- SQLite backups are transactionally consistent `.db` copies (`VACUUM INTO`). PostgreSQL backups are
  JSON table-export snapshots (`*.postgresql.json`) for inspection/operator recovery workflows; the game does
  not yet include an importer that restores those snapshots. For production PostgreSQL operations, also use the
  provider's built-in point-in-time backups.
- Create backups from the admin UI, the Tools CLI (`BlocksBeyondTheStars.Tools backup saves <world>`),
  or on a schedule. The Tools CLI also honors `BBS_DATABASE_PROVIDER=postgresql` +
  `BBS_POSTGRES_CONNECTION_STRING`.
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
is password-gated; `/portal`, `/download`, `/download-linux`, `/download-mac` and `/updates` stay public so players can reach them.

**Players:**

1. Open `http://<server-ip>:<adminPort>/portal` (default port 31416) in a browser.
2. Click **Download the Windows client** (served from `/download`) and run the installer
   (per-user, no admin rights; an unsigned build shows a one-time SmartScreen "More info → Run anyway").
   On Linux, click **Download the Linux client (AppImage)** (served from `/download-linux`), then
   `chmod +x` the `.AppImage` and run it. On macOS, click **Download the macOS client (experimental)**
   (served from `/download-mac`), unzip it and clear the Gatekeeper quarantine
   (`xattr -dr com.apple.quarantine BlocksBeyondTheStars.app`) since the build is unsigned.
3. Launch the game and **Join** the server's IP on the gameplay port (default 31415).
4. **Auto-update:** in **Settings → Software update**, paste the update URL shown on the portal
   (`http://<server-ip>:<adminPort>/updates`) and use **Check for updates**. Publishing a higher
   version with the script above lets installed clients self-update from the server.

Updates only apply to an *installed* client (not a dev/Editor or portable-zip run). Each published
version must be higher than the last.

### Play in the browser (no install)

The server can also serve the **WebGL browser client** at `http://<server>:<adminPort>/play`. The portal's
**Play in the browser** button deep-links to it with the server host/port pre-filled (`/play?server_host=…&server_port=31415`).
No download, no install — players just open the link. Singleplayer/host are unavailable in the browser (it
only joins a hosted server over WebSocket), so `BBS_ENABLE_WEBSOCKET=true` is required.

The browser build is **not** baked into the server image (it needs Unity, which can't run in the image). You
get it onto the server one of two ways:

- **Mount a locally-built folder** (works today): build the WebGL player on a machine with Unity
  (`BlocksBeyondTheStars → Build WebGL Player`, or headless `-buildMethod …BuildScript.BuildWebGL`), then
  bind-mount `client/Build/WebGL` at `/app/webgl` (see the Docker section below).
- **Auto-fetch from a release** (`BBS_FETCH_WEBGL=1`): once a release ships a `webgl*.zip` asset, the
  entrypoint downloads and unzips it into `/app/webgl`. A mounted build always wins.

If no build is present, `/play` shows a friendly "not installed yet" page instead of a blank 404.

**The TLS rule (read this before exposing it publicly):** a browser will only open the gameplay WebSocket
if the scheme is allowed for the page it is on.

| Where the page is reached | Gameplay WebSocket | Works? |
|---|---|---|
| `http://localhost:31416/play` (same machine) | `ws://localhost:31415` | ✅ browsers exempt `localhost` |
| `http://<lan-ip>:31416/play` (home LAN over http) | `ws://<lan-ip>:31415` | ✅ page is http, so `ws://` is not mixed content |
| `https://<domain>/play` (public, https) | **must be `wss://`** | ❌ unless the WebSocket has TLS — an https page **cannot** open a plain `ws://` |

The server's WebSocket gateway has **no built-in TLS**, so **public browser play needs a TLS-terminating
reverse proxy in front of the gameplay port.** Use the ready-made Caddy setup
(`docker-compose.tls.yml` + `docker/Caddyfile`, see below) which auto-provisions a Let's Encrypt
certificate, or front it with Cloudflare Tunnel / a PaaS ingress (Fly.io, Railway, Azure Container Apps —
the latter is what the original Glitch web build used). localhost and LAN-over-http need none of this.

**Where to host it (rough cost):** you do **not** need Azure or any specific cloud, and SQLite (the default)
is fine — no managed database required.

| Option | Cost (approx.) | TLS | Notes |
|---|---|---|---|
| Home PC/NAS + Cloudflare Tunnel | free (electricity) | free (Cloudflare) | the machine must stay on while people play |
| Small EU VPS (e.g. Hetzner/Netcup) | ~€4–6/month | free (Caddy + Let's Encrypt) | the typical hobby choice; `docker-compose.tls.yml` is built for this |
| PaaS (Fly.io / Railway) | free–~€10/month | free (managed) | an always-on WebSocket server may exceed free tiers |
| Azure Container Apps + managed PostgreSQL | ~€20–40+/month | free (managed ingress) | only worth it for a large always-on realm |

You can also host just the **server** on a cheap VPS and serve the static browser build for free elsewhere
(itch.io, GitHub Pages, Glitch) pointed at it with `?server_host=…` — then you only pay for the server.

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

   To test a WebGL/browser client against the container, also publish the TCP/WebSocket gameplay port and
   enable the WebSocket listener:

   ```bash
   docker run -d --name bbts-web \
     -p 31415:31415/udp -p 31415:31415/tcp -p 31416:31416/tcp \
     -e BBS_ENABLE_WEBSOCKET=true -e BBS_WEBSOCKET_BIND=+ \
     -e BBS_ADMIN_PASSWORD=test123 -e BBS_SERVER_NAME="Local WebGL Test" \
     ghcr.io/marceld23/blocks-beyond-the-stars-server:latest
   ```

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
| `/app/clients` | the published clients the portal serves: Windows `*Setup.exe` at `/download` + Linux `*.AppImage` at `/download-linux` + experimental macOS `*-osx-*-Portable.zip` at `/download-mac` |
| `/app/webgl` | the Unity WebGL browser build served at `/play` — bind-mount a local `client/Build/WebGL`, or let `BBS_FETCH_WEBGL=1` fetch the release `webgl*.zip` |

### Browser play from the container

To serve the in-browser client (§"Play in the browser"), put a WebGL build on the `/app/webgl` volume —
either bind-mount a locally-built `client/Build/WebGL` (`-v "$PWD/client/Build/WebGL:/app/webgl:ro"`) or set
`BBS_ENABLE_WEBSOCKET=true` + `BBS_FETCH_WEBGL=1` to auto-pull it from the release. Then open
`http://localhost:31416/play`. For a **public** deployment (https), use `docker-compose.tls.yml` — it adds a
Caddy reverse proxy that auto-provisions TLS so `wss://` works (a plain `docker run` over the public internet
will fail the WebSocket on https). See the TLS table in §9.

```bash
BBS_DOMAIN=play.example.com BBS_ADMIN_PASSWORD=change-me \
  docker compose -f docker-compose.tls.yml up -d
```

### PostgreSQL instead of SQLite (optional)

SQLite (the default) needs no setup and is right for most self-hosted realms. For a larger/long-running
realm you can switch the authoritative world store to **PostgreSQL** with the overlay compose file — it adds
a `postgres:16-alpine` service and points the server at it (`BBS_DATABASE_PROVIDER=postgresql` +
`BBS_POSTGRES_CONNECTION_STRING`):

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
# public TLS: docker compose -f docker-compose.tls.yml -f docker-compose.postgres.yml up -d
```

Override `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` via a `.env` file or the shell. The world then
lives in Postgres (back it up with `pg_dump`); the `saves` volume still holds logs and `/bump` reports.

### Client download from the container

A Linux container can't *build* the clients, so on start the entrypoint pulls the newest `*Setup.exe`,
`*.AppImage` **and** the experimental `*-osx-*-Portable.zip` from the latest GitHub Release into
`/app/clients` (best-effort; each asset is fetched independently, so a missing one never blocks the
others — controlled by `BBS_FETCH_CLIENT=1`/`0` and `BBS_CLIENT_REPO`). The portal (`/portal`) and the
one-click `/download` (Windows) / `/download-linux` (Linux AppImage) / `/download-mac` (macOS zip) routes
then work as in §9. Without them the portal still runs and the download routes report "nothing published
yet"; you can instead drop a `Setup.exe` / `.AppImage` / macOS zip into the `clients` volume yourself.

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
