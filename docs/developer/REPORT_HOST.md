# ReportHost — the bug-report inbox

A standalone service (`src/BlocksBeyondTheStars.ReportHost/`) that receives the game's **F1 player
feedback** and **automatic crash reports**, stores them, and serves them back to the developer. It
speaks **exactly the wire contract the game already uses** against the original Wix/Velo endpoint, so
neither the client (`FeedbackUploader`) nor the server (`CrashReportUploader`) needs any change — you
only point an endpoint URL (and key) at it.

It is deliberately **independent of the game/hosted-worlds deployment**: one small container, one data
volume, no Docker socket, no game protocol. A self-hoster can run their own instance (or none — every
credential defaults to off, and nothing in the game phones home without an operator-set key).

```
game client (F1 feedback) ──┐                                   ┌── admin UI  /admin        (Basic Auth)
                            ├── POST /api/bugreport ──► SQLite ──┼── read API /api/reports   (x-report-read-key)
game server (crash flush) ──┘    (x-bugreport-key)    + files    └── screenshots
```

## Endpoints

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /api/bugreport` | header `x-bugreport-key` | Ingest — same JSON body the game sends to Wix; responds `{ ok, bugReportId }` |
| `GET /api/reports?since=&status=&category=&source=&limit=&cursor=` | header `x-report-read-key` | Delta-sync list: `{ items, nextCursor, hasMore }`, ascending `createdAt` |
| `GET /api/reports/{id}` | read key | One report (full, incl. parsed `reportJson`) |
| `GET /api/reports/{id}/screenshot` | read key | The screenshot image |
| `PATCH /api/reports/{id}` `{"status":"new\|triaged\|done"}` | admin Basic Auth | Triage from scripts/CI |
| `DELETE /api/reports/{id}` | admin Basic Auth | Permanent delete (incl. screenshot) |
| `GET /admin`, `/admin/report/{id}` | admin Basic Auth | Server-rendered admin UI: list, filters, detail, screenshot, status buttons, delete |
| `GET /admin/export?status=&category=` | admin Basic Auth | One-click JSON file download of everything matching the filters (the UI's "Download JSON" button) |
| `GET /healthz` | none | Liveness |

List items are camelCase (`id`, `title`, `description`, `email`, `gameVersion`, …, `status`,
`createdAt` ISO-8601 UTC, `screenshotUrl` or null, `reportJson` as a parsed object). `since` and the
keyset `cursor` (`<createdUnix>:<id>`, returned as `nextCursor`) are both exclusive, so a puller that
stores the last `createdAt`/cursor never re-fetches or skips rows.

A typical pull loop (PowerShell):

```powershell
$h = @{ 'x-report-read-key' = $env:BBS_REPORTS_READ_KEY }
$page = Invoke-RestMethod "https://reports.example.com/api/reports?since=2026-07-01T00:00:00Z&limit=100" -Headers $h
$page.items | ForEach-Object { "{0}  {1}  {2}" -f $_.createdAt, $_.category, $_.title }
# follow $page.nextCursor while $page.hasMore
```

## Configuration (`BBS_REPORTS_*` env vars)

| Variable | Default | Meaning |
|---|---|---|
| `BBS_REPORTS_BIND` / `BBS_REPORTS_PORT` | `127.0.0.1` / `31418` | Bind address/port (the Docker image defaults bind to `0.0.0.0`) |
| `BBS_REPORTS_DATA_DIR` | `reporthost` (`/data` in Docker) | Holds `reports.db` + `screenshots/` |
| `BBS_REPORTS_WRITE_KEY` | *(empty = ingest rejects everything)* | The `x-bugreport-key` clients must present — a spam gate, not a secret (it ships in the client) |
| `BBS_REPORTS_READ_KEY` | *(empty = read API off)* | Independently rotatable key for pull scripts / CI |
| `BBS_REPORTS_ADMIN_USER` / `BBS_REPORTS_ADMIN_PASSWORD` | *(empty = admin UI off)* | Basic-Auth credentials for `/admin` and the mutating API |
| `BBS_REPORTS_MAX_BODY_BYTES` | `4000000` | Kestrel request cap (fits report + ~1.5 MB screenshot) |
| `BBS_REPORTS_INGEST_PER_MINUTE` | `10` | Per-IP fixed-window rate limit (`0` = off) |
| `BBS_REPORTS_RETENTION_DAYS` | `0` (keep forever) | Prune reports + screenshots after N days — reports can carry an e-mail, so this is also a privacy lever |
| `BBS_REPORTS_TRUST_PROXY` | `false` | Rate-limit on the first `X-Forwarded-For` entry — only behind a trusted proxy |

Everything fails closed: with no keys configured the service stores nothing, serves nothing and admin
is off. Ingest guards: description required (max 5000 chars), oversized/broken screenshots are dropped
while the report is kept (mirroring the client), screenshots are stored as files (never base64 in the
database), `429` on rate limit, `413` past the body cap.

Incoming reports are bucketed at ingest: `category` = `crash` when the payload's `reportJson.kind` is
set (as `CrashReportWriter` does), otherwise `feedback`; `source`/`kind` are lifted out of
`reportJson` for filtering.

## Running it

```bash
# local dev (all surfaces on, loopback only)
BBS_REPORTS_WRITE_KEY=dev BBS_REPORTS_READ_KEY=dev-read \
BBS_REPORTS_ADMIN_USER=dev BBS_REPORTS_ADMIN_PASSWORD=dev \
  dotnet run --project src/BlocksBeyondTheStars.ReportHost

# Docker
BBS_REPORTS_WRITE_KEY=... BBS_REPORTS_ADMIN_USER=me BBS_REPORTS_ADMIN_PASSWORD=... \
  docker compose -f docker-compose.reports.yml up -d
```

The compose file maps the port to `127.0.0.1` only. For a public deployment, terminate TLS in a
reverse proxy and forward to the container (Caddyfile site):

```
reports.example.com {
    reverse_proxy reports:31418
}
```

## Pointing the game at it

- **Server crash reports** — configurable today, no code change:
  `BBS_CRASH_REPORT_ENDPOINT=https://reports.example.com/api/bugreport` and
  `BBS_CRASH_REPORT_KEY=<write key>` (or `CrashReportEndpoint`/`CrashReportApiKey` in `server.json`).
  Self-hosters can run their own inbox this way; the default stays **off** (no phone-home).
- **Hosted-worlds fleet** (#363) — WorldHost forwards its `BBS_WH_CRASH_REPORT_KEY` (and the optional
  `BBS_WH_CRASH_REPORT_ENDPOINT` override) into every world container as the two variables above, so
  fleet crashes land in the inbox too. Worlds pick the key up on their next wake. Empty = off, same
  no-phone-home default as everywhere else.
- **Client F1 feedback** — the endpoint is the `FeedbackUploader.DefaultEndpoint` constant (by design
  the client always reports to the *official* inbox, from any server). Since the cutover it points at
  `https://reports.blocksbeyondthestars.de/api/bugreport`; the CI secret `BBS_BUGREPORT_API_KEY`
  (release environment) must hold that deployment's `BBS_REPORTS_WRITE_KEY`. Builds released before
  the cutover keep posting to the legacy Wix endpoint until players update.
- Smoke test: `scripts/test-feedback-endpoint.ps1` can be pointed at
  `http://localhost:31418/api/bugreport` with the write key.

## Code map

| Concern | File |
|---|---|
| Wiring / endpoints | `src/BlocksBeyondTheStars.ReportHost/Program.cs` |
| Config (env) | `src/BlocksBeyondTheStars.ReportHost/ReportHostConfig.cs` |
| Payload parsing/validation | `src/BlocksBeyondTheStars.ReportHost/ReportIngest.cs` |
| SQLite + screenshot store | `src/BlocksBeyondTheStars.ReportHost/ReportStore.cs` |
| Admin pages (server-rendered) | `src/BlocksBeyondTheStars.ReportHost/ReportHostPages.cs` |
| Rate limiter / Basic Auth | `IngestRateLimiter.cs` / `BasicAuth.cs` |
| Tests | `tests/BlocksBeyondTheStars.Tests/ReportHostTests.cs` |
| Image / compose | `Dockerfile.reports` / `docker-compose.reports.yml` |

The admin pages HTML-encode every stored string — report content is hostile input rendered in the
operator's browser.

## Privacy

Reports may include an optional player e-mail plus a screenshot. Use `BBS_REPORTS_RETENTION_DAYS` for
automatic expiry, and the admin UI's delete (or `DELETE /api/reports/{id}`) for individual removal —
deletion always removes the screenshot file too.
