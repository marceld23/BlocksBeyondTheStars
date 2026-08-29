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
| `PATCH /api/reports/{id}` `{"status":"new\|triaged\|waiting_for_player\|player_replied\|done"}` | admin Basic Auth | Triage from scripts/CI |
| `DELETE /api/reports/{id}` | admin Basic Auth | Permanent delete (incl. screenshot + reply thread) |
| `POST /api/reports/{id}/replies` `{"text","question":bool,"fixedInVersion"?}` | admin Basic Auth | Developer answer / follow-up question (#1327) — scriptable twin of the detail-page form |
| `GET /api/replies?key=&since=` | header `x-bugreport-key` | **Client poll**: threads of this reply key with unread developer entries (CORS on) |
| `POST /api/replies/ack` `{"key","replyIds":[]}` | header `x-bugreport-key` | Client marks shown developer entries read |
| `POST /api/replies` `{"key","reportId","text"}` | header `x-bugreport-key` | The player's in-game answer to a question (max 3 per report) |
| `GET /admin`, `/admin/report/{id}` | admin Basic Auth | Server-rendered admin UI: list, filters, detail, screenshot, status buttons, reply thread + form, delete |
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
| `BBS_REPORTS_INGEST_PER_MINUTE` | `10` | Per-IP fixed-window rate limit for `POST /api/bugreport` only (`0` = off) |
| `BBS_REPORTS_REPLY_PER_MINUTE` | `30` | Per-**reply-key** fixed-window rate limit for the player reply routes `/api/replies*` (`0` = off) — separate from ingest on purpose (#1352): every install polls for answers, so a LAN class behind one NAT must never spend the report budget on polls |
| `BBS_REPORTS_RETENTION_DAYS` | `0` (keep forever) | Prune reports + screenshots after N days — reports can carry an e-mail, so this is also a privacy lever |
| `BBS_REPORTS_TRUST_PROXY` | `false` | Rate-limit on the first `X-Forwarded-For` entry — only behind a trusted proxy |

Everything fails closed: with no keys configured the service stores nothing, serves nothing and admin
is off. Ingest guards: description required (max 5000 chars), oversized/broken screenshots are dropped
while the report is kept (mirroring the client), screenshots are stored as files (never base64 in the
database), `429` on rate limit, `413` past the body cap.

Incoming reports are bucketed at ingest: `category` = `crash` when the payload's `reportJson.kind` is
set (as `CrashReportWriter` does), otherwise `feedback`; `source`/`kind` are lifted out of
`reportJson` for filtering.

## Reply threads — answering players in-game (#1327)

Every report row has a **reply thread** (`report_reply`: `author dev|player`, `text`, `is_question`,
`seen_unix`) and two extra columns: `reply_key` and `fixed_in_version`. The thread is what the game client
pulls and shows (see [PLAYER_FEEDBACK](PLAYER_FEEDBACK.md)).

**Who may read a thread — the reply key.** The client sends `replyKey` with each report: lowercase-hex
`SHA256("bbs-reply:" + <install secret>)` (`Shared/Feedback/FeedbackReplyKey.cs`, shared by client and
inbox). The secret is the install's name-claim token (desktop, play.*) or the Glitch install id (arcade);
the key is one-way, so a leaked key reads replies but can never claim a name. A **client-direct** report
that arrives **without** a well-formed key (pre-#1327 client) gets one derived from its `playerId` at
ingest, and `BackfillReplyKeys()` does the same once at startup for rows stored before the feature — older
reporters become answerable as soon as they update. **Server forwards** (`reportJson.source == "server"`:
`/bump`, paint/shape reports, crashes) are the exception (#1359): their `playerId` is the public player
**name**, and a key derived from that would be guessable by anyone who knows the name — and is never what
the client polls with. Such a row carries only the key the client passed through `/bump`
(`BumpReport.ReplyKey`, #1359 clients) or none at all; `RevokeNameDerivedServerKeys()` blanks the
name-derived keys older stores had already stamped, once at startup. A row without a key → the admin
page says so instead of offering a form. `PUT`-style overwrite/clear: `ReportStore.SetReplyKey` (no HTTP
route on purpose).

**Operator side.** The detail page shows the conversation and a form: a textarea, a *this is a question*
checkbox and a *fixed in version* field. A plain answer leaves the status alone; a **question** flips the
report to `waiting_for_player`. `POST /api/reports/{id}/replies` is the JSON twin for scripts.

**Player side (all gated by the write key + a per-reply-key limiter — `BBS_REPORTS_REPLY_PER_MINUTE`, *not* the
per-IP ingest limiter, see #1352 — CORS-enabled for browser builds):**
`GET /api/replies?key=…&since=…` returns
`{ items: [ { reportId, title, status, fixedInVersion, createdUnix, replies: [ { id, author, text, isQuestion, createdUnix, seen } ], unseenIds: [] } ] }`
— only threads with an unread developer entry (created after `since`, unix seconds, optional).
`POST /api/replies/ack` marks those ids read (scoped to the key — foreign ids are ignored). `POST /api/replies`
appends the player's answer: requires the key to own the report, at least one developer entry to answer
(no unsolicited threads), and at most **3** player answers per report (`409 reply_limit`); it flips the status
to `player_replied` and pings `BBS_REPORTS_NOTIFY_URL` like a new report. Text is capped like a description
and HTML-encoded on render — it is hostile input like everything else a player types.

**Read API.** `GET /api/reports/{id}` now includes `fixedInVersion` and `replies` (the key itself is never
exposed). Delete and retention pruning remove threads with their report.

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
- **Singleplayer** — `LocalServerLauncher` passes the client's baked-in feedback key to the bundled
  server as `BBS_CRASH_REPORT_KEY` (release builds only; dev builds carry an empty key), so SP server
  crashes upload automatically. No new exposure: it is the same spam-gate key the client already ships.
- **`/bump` snapshots** — any server with a configured sink (SP, fleet, opted-in self-hosts) also
  forwards each `/bump`/F1 snapshot to the inbox, wire-shaped like a crash report but **without**
  `reportJson.kind` so it is filed under category *feedback* with `source: "server"` and
  `reportJson.reportType: "bump"`. When the client sent a screenshot it rides along as a top-level
  `screenshot` node (base64 JPG), so it stores + shows in the admin detail view exactly like an F1
  screenshot — the server forward is the reliable path for it, since the client-direct F1 upload may not
  run on older builds. Oversized shots are dropped upstream (2 MB cap) / by the ReportHost base64 cap,
  keeping the report either way. The reporter's `replyKey` rides along as a top-level node when the client
  sent one with the `/bump` (#1359), so both halves of one report share the thread credential. The local
  `bumps/` file stays authoritative.
- **One F1 report = two rows, one admin row** — an in-game F1 report reaches the inbox twice by design
  (client-direct + server forward; see above). Ingest keeps both (it must never drop a player report) and the
  read API returns both; only the admin list collapses the pair (`ReportHostPages.GroupDuplicates`) into one
  row with a `+1` link to the other half: same category and version, stamped within 8 s, one description
  containing the other, and the **same reporter** — by reply key when both halves carry one, by player
  **name** otherwise. Not by `playerId`: the client row carries the install token, the server row the
  player name, so the two never agree (the original #618 check compared exactly that and never paired a
  single report in production — #1359).
- **`/reportpaint` / `/reportshape` reports** (#938) — any server with a configured sink also forwards
  each in-game paint/shape report to the inbox, shaped like a `/bump` (no `reportJson.kind` → category
  *feedback*, `source: "server"`) with `reportJson.reportType: "paint-report"` / `"shape-report"` and
  the design id, owner, reporter and block position under `reportJson.report`. Before this, those
  reports only existed as a row in the world's own `paint_report` table plus a server log line —
  invisible to the fleet operator. The local row stays authoritative; the wipe commands
  (`/paintwipe #id`, `/shapewipe #id`) work from the forwarded id.
- **Client F1 feedback** — the endpoint is the `FeedbackUploader.DefaultEndpoint` constant (by design
  the client always reports to the *official* inbox, from any server). Since the cutover it points at
  `https://reports.blocksbeyondthestars.de/api/bugreport`; the CI secret `BBS_BUGREPORT_API_KEY`
  (release environment) must hold that deployment's `BBS_REPORTS_WRITE_KEY`. Builds released before
  the cutover keep posting to the legacy Wix endpoint until players update.
- Smoke test: `scripts/test-feedback-endpoint.ps1` can be pointed at
  `http://localhost:31418/api/bugreport` with the write key.

## Operator push notifications (#938)

`BBS_REPORTS_NOTIFY_URL` (empty = off) makes the inbox POST one short plain-text message per stored
report — body + `Title`/`Tags` headers, which is the [ntfy](https://ntfy.sh) publish contract, so an
ntfy topic URL works out of the box (and most generic webhook receivers too). Fire-and-forget: one
attempt, 10 s timeout, failures swallowed — the stored report is the source of truth.
Note: in-game F1 feedback arrives twice by design (client-direct + server `/bump` forward), so those
ping twice. Use a dedicated, unguessably-named topic; the messages contain report titles and player
names.

## Code map

| Concern | File |
|---|---|
| Entry point (env → store → run) | `src/BlocksBeyondTheStars.ReportHost/Program.cs` |
| Wiring / endpoints | `src/BlocksBeyondTheStars.ReportHost/ReportHostApp.cs` (`Create(config, store, notifier, args)` — the tests start the same app in-process) |
| Config (env) | `src/BlocksBeyondTheStars.ReportHost/ReportHostConfig.cs` |
| Payload parsing/validation | `src/BlocksBeyondTheStars.ReportHost/ReportIngest.cs` |
| SQLite + screenshot store | `src/BlocksBeyondTheStars.ReportHost/ReportStore.cs` |
| Admin pages (server-rendered) | `src/BlocksBeyondTheStars.ReportHost/ReportHostPages.cs` |
| Rate limiter / Basic Auth | `IngestRateLimiter.cs` / `BasicAuth.cs` |
| Tests | `tests/BlocksBeyondTheStars.Tests/ReportHostTests.cs` (store, parsing, pages) · `ReportHostHttpTests.cs` (the real app over HTTP on a loopback port: reply routes, limiter split) |
| Image / compose | `Dockerfile.reports` / `docker-compose.reports.yml` |

The admin pages HTML-encode every stored string — report content is hostile input rendered in the
operator's browser.

## Privacy

Reports may include an optional player e-mail plus a screenshot. Use `BBS_REPORTS_RETENTION_DAYS` for
automatic expiry, and the admin UI's delete (or `DELETE /api/reports/{id}`) for individual removal —
deletion always removes the screenshot file too (and the reply thread).

The player-facing wording lives on the portal privacy page (`WorldHostPortalPages.Privacy`, #1329): the
`privacy.summary.reports` paragraph in all 14 portal locales plus the German authoritative sections 2 and 5
describe what an in-game report carries, the one-way reply key, in-game answers, retention ("kept until we
delete them — no automatic expiry at the moment", which matches the official inbox's `RETENTION_DAYS=0`
default) and deletion on request via the legal e-mail address. If you enable retention on the official
inbox, update that paragraph.
