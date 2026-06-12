# Self-Hosting a Blocks Beyond the Stars Server

Blocks Beyond the Stars is designed so players can host their own server on a Windows PC, a Linux box,
a VPS, or a Raspberry Pi 5 — without installing .NET (the packages are self-contained).

## 1. Get a server package

Download or build a package for your platform:

| Platform | Package |
|---|---|
| Windows x64 | `blocks-beyond-the-stars-server-win-x64.zip` |
| Linux x64 | `blocks-beyond-the-stars-server-linux-x64.zip` |
| Linux ARM64 / Raspberry Pi 5 | `blocks-beyond-the-stars-server-linux-arm64.zip` |

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

On a Raspberry Pi 5, prefer an SSD over a microSD for the world database to reduce wear
and improve autosave performance.

## 3. Configuration (`config/server.json`)

Created on first run; editable directly or through the admin UI.

| Key | Meaning | Default |
|---|---|---|
| `serverName` | Display name | `Blocks Beyond the Stars Server` |
| `worldName` | Save folder under `saves/` | `world_001` |
| `gameplayPort` | UDP port for the game (open/forward this) | `31415` |
| `adminPort` | HTTP port for the admin UI | `31416` |
| `maxPlayers` | Connection cap | `4` |
| `serverPassword` | Required to join (empty = none) | `""` |
| `whitelistEnabled` / `whitelist` | Restrict who may join | `false` / `[]` |
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

## 4. Ports & networking

- **Gameplay**: UDP `gameplayPort` (default 31415). Forward this on your router for
  internet play.
- **Admin UI**: HTTP `adminPort` (default 31416), bound to `127.0.0.1` by default so it
  is **not** reachable from outside. Only change `adminBindAddress` if you understand the
  risk, and always set an `adminPassword` first.

## 5. Admin UI

Open `http://<adminBindAddress>:<adminPort>/`. If an `adminPassword` is set, enter it once
in the dashboard. You can view status, edit configuration, create backups, and tail logs.

## 6. Backups & saves

- A world lives in `saves/<worldName>/world.db` (SQLite) with `backups/` and `logs/`
  alongside — fully portable; copy the folder to move or back up a world.
- Backups are transactionally consistent copies (`VACUUM INTO`). Create them from the
  admin UI, the Tools CLI (`BlocksBeyondTheStars.Tools backup saves <world>`), or on a schedule.
- The world is `seed + parameters + player edits`: the procedural terrain is regenerated,
  only your changes are stored, keeping saves small.

## 7. Singleplayer

Singleplayer runs the **same** server in-process via an in-memory loopback transport, so
there is no separate code path — what works solo works in multiplayer.

## 8. Optional AI backend (dynamic LLM text)

The server can use the optional Python service in [`ai-backend/`](../ai-backend/) for
dynamic flavour text: **NPC greetings** (personal speech bubbles at vendors and mission
boards), **mission-board flavour text**, occasional **VEGA ship-AI banter**, and
admin-generated missions (`/ai <prompt>` in chat). The game is fully playable without it —
every AI text has a localized scripted fallback (DE+EN), and with `aiLevel = Off` (the
default) the server never contacts the backend.

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
details: [ai-backend/README.md](../ai-backend/README.md); design:
[AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md).
