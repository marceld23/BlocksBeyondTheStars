# Hosted Worlds — Control Plane, Routing & Lifecycle

Status: Phase 0 (server foundations) and Phase 1 (control-plane MVP) implemented; client/portal UX
(Phase 2) and quotas hardening/multi-host (Phase 3) are open. This document is the architecture
reference for the "hosted worlds" feature: players create persistent multiplayer worlds (optionally
from an uploaded singleplayer save, Phase 2) that run as **one dedicated-server container per world**
behind a control plane — the Minecraft-Realms model, adapted to our stack.

The three hosting tiers, side by side:

| Tier | Who runs it | Cost to us | Client entry |
|---|---|---|---|
| Singleplayer | bundled child-process server (ADR 0005) | none | unchanged |
| Self-hosting (LAN/Docker) | the player/community, SELF_HOSTING.md | none | unchanged |
| **Hosted worlds** | **our fleet host** | compute + egress | native menu "Official worlds" (Phase 2) |

## Components

```text
                    ┌──────────────────────────── VPS (Docker host) ────────────────────────────┐
 players ── https ─►│ Caddy (caddy-docker-proxy)                                                │
                    │   ├─ play.blocksbeyondthestars.de      → WorldHost (portal + API)         │
                    │   └─ w-<id>.play.blocksbeyondthestars.de → that world's WS gateway :31415 │
                    │                                                                           │
                    │ WorldHost (src/BlocksBeyondTheStars.WorldHost)                            │
                    │   accounts + sessions + world registry (SQLite)                           │
                    │   orchestrator: route-or-wake, join grants, reaper                        │
                    │   docker CLI: one container per world                                     │
                    │                                                                           │
                    │ bbs-world-<id> containers (the normal dedicated-server image)             │
                    │   volume bbs-world-<id>-saves:/app/saves                                  │
 native UDP ───────►│   host port 3200x → 31415/udp (gameplay), 127.0.0.1:3200x → /status probe │
                    └───────────────────────────────────────────────────────────────────────────┘
```

- **WorldHost** (`src/BlocksBeyondTheStars.WorldHost`) — the control plane: accounts (name +
  PBKDF2 password hash, deliberately no email — privacy-minimal for a kid-facing free tier), bearer
  sessions, the world registry, wake-on-demand allocation and join-token issuing. SQLite registry at
  `worldhost/worldhost.db`. Configured via `BBS_WH_*` env vars (see `WorldHostConfig`); **all quota
  values are operator config, never player-facing settings**: worlds/account (2), max players (12),
  idle minutes (20).
- **Per-world instances** — the unmodified dedicated-server image. The Phase-0 server features make
  them fleet-ready: `BBS_IDLE_SHUTDOWN_MINUTES` (empty world saves + exits → sleeping worlds cost
  ~nothing), `GET /status` on the WS gateway (live joined count for the reaper/allocator),
  `BBS_JOIN_TOKEN_SECRET` (only control-plane-vouched joins get in), `BBS_WORLD_OWNER` (the owner
  account gets WorldAdmin even on an uploaded save with a foreign first-joiner admin).
  **Containers run with `--restart=no`** — an auto-restart policy would wake idle-stopped worlds
  right back up.
- **Join flow** — `POST /api/worlds/{id}/join {playerName}` (Bearer session) → orchestrator ensures
  the instance runs (fast-path route, or `docker run` + poll `/status` until healthy, default 90 s
  budget) → returns a `JoinGrant`: `wssUrl` (browser), `nativeHost`/`nativePort` (desktop UDP) and a
  **10-minute HMAC join token** (`HostedJoinToken`, bound to world + account + player name — long
  enough to survive the browser deep-link's first WebGL download). The
  instance verifies tokens offline — a control-plane outage never locks players out of a running world.
- **Reaper** — every 30 s reconciles registry vs Docker: instances that exited themselves (idle
  shutdown is the normal path) are marked `stopped`, so lists stay truthful and the next join wakes them.

## Routing, DNS & certificates (decision + Strato specifics)

Decided: **wildcard subdomains** (`w-<id>.play.blocksbeyondthestars.de`), NOT path routing — the
browser client needs zero changes (it already picks ws/wss from the page and takes `server_host`
from the URL), and every world is its own origin. Port-per-instance is not browser-viable (mixed
content + non-443 ports are blocked in school/corporate networks).

DNS lives at **Strato, which has no DNS API**, so Caddy's DNS-challenge wildcard certificate is not
available directly. Two options, in preference order:

1. **On-demand TLS (default for MVP, no second provider needed).** At Strato: one wildcard **A
   record** `*.play` → the VPS IP (plus `play` itself). Caddy issues a certificate per subdomain on
   first request via HTTP-01. Abuse guard: Caddy's `on_demand_tls { ask }` is pointed at WorldHost's
   `GET /ask?domain=…`, which answers 200 **only** for the portal host and subdomains of worlds that
   exist in the registry — nobody can burn our Let's Encrypt rate limits by aiming random names at
   the IP. Trade-off: the first-ever join of a world pays ~1–2 s of certificate issuance.
2. **Subzone delegation (fallback if issuance latency/rate limits ever bite).** At Strato, delegate
   `play` via NS records to a free API-capable DNS zone (Cloudflare / Hetzner DNS); then Caddy's
   DNS-challenge mints one wildcard certificate and on-demand TLS is turned off.

Native desktop clients use UDP and bypass Caddy entirely: each world publishes its stable host port
(`3200x → 31415/udp`) and clients connect to `PublicHost:port` from the join grant. The TCP side of
that port binds to loopback only — it exists for WorldHost's `/status` probe; public wss goes
through Caddy.

## Client integration (implemented for the native client)

- **Native client (desktop):** the main menu offers BOTH worlds — self-hosting exactly as today
  (Singleplayer / Host (LAN) / Join-by-address) AND **"Official Worlds"** (`UiMainMenu`, native-only
  `#if` branch): sign in with the portal account (session stored in `ClientSettings.PortalSessionToken`,
  never the password; `PortalUrl` empty = the official portal, settable for self-hosted WorldHosts),
  list your worlds, one click joins — the join grant's `nativeHost:nativePort` + `joinToken` feed the
  existing connect path (`AppShell.HostedToken` → `GameBootstrap` → `JoinRequest.HostedToken`). The
  HTTP side is `Client.Core/Portal/PortalClient` (mirrors FeedbackUploader: HttpClient, sync, never
  throws, testable headless). Manual/SP/LAN joins explicitly clear the hosted token.
- **Full portal parity in the native client (#268-#270):** the web portal is OPTIONAL for desktop
  players. The Official Worlds overlay additionally offers: **account signup** with in-game rules
  display + acceptance (rules text/version from the anonymous `GET /api/terms`, single-sourced with
  the /rules page via `CommunityRules`), the **terms re-accept flow** after a rules-version bump
  (login's `termsOutdated` or any `terms_outdated` error opens the rules screen → `POST
  /api/accept-terms`), **create world** (name + optional join password), per-world **Manage** dialog
  (set/remove join password, stop, delete with type-the-name confirmation), **save backup
  round-trip** (download to / upload from `persistentDataPath/portal_saves/<worldId>-world.db` — no
  browser needed; world must be stopped, 50 MB cap), a **feedback** form (`POST /api/reports`,
  category `feedback`) and **account deletion** (type-the-name confirmation; deletes all worlds +
  saves, GDPR). Signup/login rate limits and all name/password validation stay server-side; the
  client mirrors only the cheap checks (mismatch, min length) and localizes the stable error codes
  (`ui.portal.err_<code>`).
- **In-game report button:** on hosted worlds (portal session + hosted join present) every player row
  in the Alliances → find-players list carries a "Report" button — one tap files a report via
  `POST /api/reports`, the button becomes the confirmation. Absent everywhere else.
- **`/report <player> [note]` chat command:** the same gate and endpoint, but from the chat box —
  files a category-`chat` report quoting the reported player's last 10 chat lines as evidence
  (`Client.Core/Portal/ReportChatCommand` holds the pure parse/compose logic, unit-tested headless).
  All report paths (button, command, portal web form) attach the world id — the Official-Worlds join
  threads it as `AppShell.HostedWorldId` → `GameBootstrap.HostedWorldId`, cleared alongside the
  hosted token on SP/LAN/manual joins; `CreateReport` blanks anything that is not a 12-hex id.
- **One-time welcome (MOTD):** on a hosted world's FIRST join of a player, the server sends one
  bilingual system line (be kind + rules + beta notice) — `PlayerState.HostedWelcomeShown` persists
  so it never repeats. Keyed on the join-token gate, so self-hosted servers are unaffected.
- **Web client:** NO server choice, ever. The browser client is always bound to whoever serves it:
  a self-hosted Docker's `/play` page points at that same installation's server (exactly as today,
  via the portal deep-link parameters), and the official portal's pages point at the official
  hosted worlds. The WebGL menu never grows a server picker.
- **Browser play (implemented):** the portal serves ONE central WebGL build at `/play`
  (`BBS_WH_WEBGL_DIR`, bind-mounted on the fleet — see deploy/README.md; mirrors the per-instance
  Api's serving incl. cache-bust stamping and .br/.gz encodings, kept testable in `PlayPage`). The
  My-Worlds **Play** button wakes the world, then deep-links
  `/play/?auto_join=1&player_name=…&server_host=<wssUrl>&hosted_token=…&world_id=…` — the client
  reads `hosted_token`/`world_id` from the page URL (`GlitchIntegration` → `AppShell.HostedToken`)
  so the instance's token gate admits the browser join. The portal pages themselves are fully
  localized (German default, `?lang=en` + DE/EN footer switcher, `bbs_lang` cookie) and carry the
  game logo + website favicon.

## Reserved developer names

`WorldHostConfig.ReservedNames` (override: `BBS_WH_RESERVED_NAMES`, comma-separated) protects the
developers' identities — default list: Marcel, Justus, Verena, juju, JuMaVe Games, FlashMiner,
JustusJulius, BloddyMary. Enforced at **two** layers:

1. **Account signup** — a reserved name registers only with the operator's claim code
   (`BBS_WH_RESERVED_CLAIM_CODE`, checked constant-time; unset ⇒ unclaimable). A successful claim
   permanently flags the account `is_developer`.
2. **In-game player name on hosted worlds** — the join grant refuses reserved names for
   non-developer accounts, so nobody can *play as* "Justus" on any hosted world regardless of what
   their account is called.

Matching is normalized — lowercase, with spaces/`-`/`_` stripped — so "ju ju", "J_ustus" and
"JuMaVeGames" are all caught. Self-hosted servers are out of scope (operators control their own
worlds; the per-save name-token claim still applies there).

## Community rules, beta notice, reports & bans (implemented server-side)

Not a legal EULA (the software is AGPL; this covers the hosted SERVICE) — a lightweight,
kid-friendly acceptance + enforcement loop:

- **Rules acceptance at signup** — the portal signup requires the checkbox and sends
  `acceptedTermsVersion`; the registry stores version + timestamp per account. The bilingual rules
  live at `/rules` (family project, be kind; **no hate, bullying, racism, insults → immediate
  ban**; never share personal data; beta notice). When the operator bumps
  `BBS_WH_TERMS_VERSION`, logins report `termsOutdated` and world actions are refused until the
  account re-accepts (`POST /api/accept-terms`).
- **Beta notice** — on the landing page, the worlds page and in the rules: hosted worlds/saves can
  break or disappear at any time; download backups (the export endpoint is the answer).
- **Player reports ("Spieler melden")** — `POST /api/reports` (Bearer): reported in-game name,
  category (chat / name / griefing / other), optional message (capped 500 chars), optional world.
  Reports are reviewed manually — nothing auto-punishes. Operator review via the admin API
  (`X-Admin-Token`, enabled by `BBS_WH_ADMIN_TOKEN`): `GET /api/admin/reports`,
  `POST /api/admin/reports/{id}/close`, `POST /api/admin/ban` (ban/unban + reason).
- **Bans** — `banned` flag + reason on the account; the join grant (the choke point every hosted
  entry passes) refuses banned accounts with the reason. Banned players can still file reports.
  A ban may be a **timeout** (`banned_until`, the admin form's default): it lifts itself, so nothing
  has to be remembered, and the player can be told the day they are welcome back. It also carries a
  canned `ban_reason_code` (chat / griefing / cheating / name / other) that the client and the portal
  render in the player's language, next to the operator's free-text detail (shown as written).
  Banning **kicks the account out of every world it is in right now** — the ban itself would only
  decide the next join (#496).
- **Notices** (`account_notice`) — the player's inbox: why they are banned, that a ban was lifted,
  that an operator deleted one of their worlds. Bans could be re-derived from the account row; a
  deleted world cannot, because the row is gone — so the notice is written at the moment of the
  action, with the world's name and the operator's optional reason. `POST /api/login` answers the
  moderation state plus the unread notices, `GET /api/notices` is the poll behind it (a ban landing
  mid-session never passes through the login again — sessions outlive it by weeks), and
  `POST /api/notices/ack` (`{"id":0}` = all) marks them read (#496).
- **The operator is never a moderation target** — an operator account (the developer flag, obtainable
  only with the secret claim code) cannot be banned, and neither a world ban, the owner's kick route nor
  the in-game `/kick` can touch a fleet admin. A banned operator would be locked out of the fleet they
  run with nobody left to lift it, and a world owner must not be able to switch off oversight of their
  own world (#495). The operator's join is also kept out of `world_visitor`, which is the list the owner
  reads. Unbanning always stays allowed.
- **Per-world bans (the owner's own lever)** — `world_ban`, enforced in the same join grant, matching
  on the account and on the in-game name (arcade guests have no account). Owner routes:
  `GET/POST /api/worlds/{id}/bans`, `DELETE /api/worlds/{id}/bans/{banId}`,
  `POST /api/worlds/{id}/kick`. The ban UI picks from `world_visitor`, a log of who entered a world
  under which name, written at the join grant (#497).
- **Kick** — `POST /kick` on the instance gateway, same `X-Announce-Token` as `/announce`. The server
  sends the player a `JoinRejected` (which the client renders as "back to the menu with this reason")
  and closes the pipe a second later, so the notice is out before the socket goes and a modified
  client cannot ignore it. A reason of the form `@<locale key>` is resolved in the player's language;
  anything else is operator/owner prose and shown verbatim. In-game twin for the world admin:
  `/kick <player>` — deliberately momentary, so there is exactly ONE lasting ban store (#497).
- Still open (client-side, Phase 2 rest): in-game report button on hosted worlds, first-join
  welcome MOTD, **Impressum + Datenschutzerklärung** pages before public launch (DSGVO — say the
  minimal-data story out loud: name + password hash, no email).

## Save upload / export (implemented)

The SP↔hosted round-trip. Instances bind-mount `<BBS_WH_WORLDS_DIR>/<worldId>/saves` at
`/app/saves` (a bind mount, not a named volume, precisely so WorldHost can do this):

- `POST /api/worlds/{id}/save` (owner, world stopped, raw `world.db` bytes): streamed to a temp
  file with a hard cap (`BBS_WH_UPLOAD_MAX_BYTES`, default 50 MB), then validated — SQLite magic
  header, `PRAGMA quick_check`, and the game schema anchor (`world_meta` table) — before it
  replaces the live file; the previous generation is kept as `world.db.bak`.
- `GET /api/worlds/{id}/save` (owner, world stopped): downloads the current `world.db` — the
  backup path the beta notice points players to, and the way back into singleplayer.

## Security notes

- WorldHost owns the Docker socket ⇒ root-equivalent. Everything that reaches `docker run` is
  server-generated (world ids: 12 hex chars, validated everywhere) or passed strictly as an **env
  value** via `ProcessStartInfo.ArgumentList` (argv-level, no shell) — display names never become
  arguments. Keep it that way.
- Sessions are stored hashed (SHA-256); passwords PBKDF2-SHA256/210k. Login failures are uniform
  (no name-exists oracle). Lost password = lost account for now — recovery UX is a Phase-2 concern.
- Join tokens live 120 s and name one world + one player; the per-world secret never leaves the
  host (it is injected into the instance's env).
- Deleting a world stops the container, removes its container object and drops the registry row but
  **keeps the saves volume** (operator-recoverable); automated retention/archival is Phase 3. Only
  the operator's `purge saves` on `/admin` and account self-deletion erase the files themselves.

## Operations quick reference

Deployment is codified in **`deploy/`** (compose files for caddy / worldhost / reports — the source
of truth for `/opt/bbs/` on the VPS, see `deploy/README.md`) and two GitHub Actions workflows:
`worldhost-image.yml` builds `ghcr.io/marceld23/blocks-beyond-the-stars-worldhost` on main pushes
(`:latest` + immutable `:sha-<short>`), and `deploy.yml` (manual dispatch, `production` environment
with approval gate, single `DEPLOY_SSH_KEY` secret) rsyncs `deploy/` to the host and runs
`remote-deploy.sh` with per-service health checks. All operator secrets (claim code, admin token,
report keys) live only in `/opt/bbs/<service>/.env` on the host — never in CI.

```bash
# One-time host setup: shared network + caddy-docker-proxy with on-demand TLS ask endpoint
docker network create bbs-hosted
# Caddy global option:  on_demand_tls { ask http://worldhost:31417/ask }   (deploy/caddy/BaseCaddyfile)

# WorldHost operator env → /opt/bbs/worldhost/.env (template: deploy/worldhost/.env.example)
BBS_WH_BASE_DOMAIN=play.blocksbeyondthestars.de
BBS_WH_PUBLIC_HOST=play.blocksbeyondthestars.de
BBS_WH_SERVER_IMAGE=ghcr.io/marceld23/blocks-beyond-the-stars-server:0.6.2   # fleet version pin (WP14)
# portal → game website link (defaults are the project's own site; set to "-" to drop the link):
#   BBS_WH_WEBSITE_URL=https://www.blocksbeyondthestars.com/, BBS_WH_WEBSITE_URL_EN=…/en
# quotas (operator policy): BBS_WH_MAX_WORLDS_PER_ACCOUNT=2, BBS_WH_MAX_PLAYERS=12, BBS_WH_IDLE_MINUTES=20
# glitch.fun arcade (optional, off without credentials): BBS_WH_GLITCH_ENABLED, BBS_WH_GLITCH_TITLE_ID,
#   BBS_WH_GLITCH_TITLE_TOKEN, BBS_WH_GLITCH_WORLDS=2, BBS_WH_GLITCH_MAX_PLAYERS=8,
#   BBS_WH_GLITCH_ALLOWED_ORIGINS — see "glitch.fun arcade channel" below
```

Instances the control plane starts carry caddy-docker-proxy labels
(`caddy=w-<id>.<domain>`, `caddy.reverse_proxy={{upstreams 31415}}`), so routing appears/disappears
with the container — no proxy config to maintain.

**Containerized WorldHost gotcha:** the orchestrator's `/status` probes cannot use host loopback
from inside a container — set `BBS_WH_PROBE_VIA_NETWORK=true` (the fleet compose does) so probes go
to `bbs-world-<id>:31415` on the shared docker network instead.

## Resource fences & capacity

Every world container runs with a hard memory cap (`BBS_WH_INSTANCE_MEMORY`, default 768m, also set
as `--memory-swap` so a capped world cannot swap-thrash the host; .NET's cgroup-aware GC applies
pressure before the OOM kill), a CPU ceiling (`BBS_WH_INSTANCE_CPUS`, default 2) and a pids cap. An
OOM-killed world is just a stopped world — the reaper reconciles it and the next join wakes it fresh.
`BBS_WH_MAX_ACTIVE` (default 10) bounds how many instances are awake at once, sized so the sum fits
the host; wake requests beyond it get the friendly `no_capacity` error the clients already localize.

## Fleet AI texts (optional)

`deploy/ai/` runs the Python ai-backend as an **internal-only** container (no published port, no
Caddy route — the provider API key never leaves `/opt/bbs/ai/.env`). When `BBS_WH_AI_BACKEND_URL`
is set (fleet: `http://ai:8077`), WorldHost passes it with `BBS_WH_AI_LEVEL` (default TextOnly) into
every world instance, enabling LLM NPC lines + board flavour. The game never blocks on AI: players
get an instant static line and the LLM line upgrades it asynchronously; timeouts are generous and
aligned (`BBTS_AI_TIMEOUT` 30 s < `BBS_AI_TIMEOUT_SECONDS` 35 s) so the backend's template fallback
always beats the server's deadline. Official fleet model: Mistral Small on OVHcloud AI Endpoints
(EU; ~0.5 s per line — Qwen3.5 was measured unusable there: forced reasoning, no think-off switch).

## Fleet crash reports (optional)

When `BBS_WH_CRASH_REPORT_KEY` is set (the ReportHost deployment's write key), WorldHost forwards it
into every world instance as `BBS_CRASH_REPORT_KEY`, so a crashed world uploads its queued crash
reports to the bug-report inbox (docs/developer/REPORT_HOST.md) on its next start. The endpoint
defaults to the official inbox inside the server; a self-hosted fleet sets
`BBS_WH_CRASH_REPORT_ENDPOINT` to point at its own ReportHost. Empty key (default) = crash upload
off — the usual no-phone-home stance. Like the announce token, running worlds only pick the key up
on their next wake.

## Operator admin UI

`/admin` on the portal domain (Basic Auth via `BBS_WH_ADMIN_USER`/`_PASSWORD`; off when unset — the
`X-Admin-Token` API for scripts is separate and unchanged): fleet instance overview (status, owner,
live player counts via `/status`, stop/wake/restart/**delete**), the open player-report queue (close as
reviewed/dismissed, reported names link to account lookup) and ban/unban with reason. The
bug-report inbox has its own `/admin` (ReportHost) including a filtered JSON bulk export.

**Deleting a world** (per row, folded into a `delete…` disclosure so it cannot be hit while aiming
for stop/wake): the world's **name must be typed** into the box, checked server-side — there is no
undo. Two buttons share that box: `delete` stops the instance and drops the registry row but leaves
the saves in the worlds directory, `purge saves` also erases them including the archive copy. Both
remove the container OBJECT as well (instances run without `--rm`, and a deleted world never wakes
again to clean up its own leftovers). Deleting a *running* world is allowed and blocks while
`docker stop` drains it, exactly like the stop button. Its port and the owner's world-quota slot
return to the pool automatically; open reports naming the world keep their (now orphaned) world id.
Arcade worlds show `reset…` instead: the glitch pool refills itself, so the world comes back empty
under a fresh id and the next free `Glitch Arcade <n>` name. Scriptable twin for bulk cleanup:
`DELETE /api/admin/worlds/{id}[?purge=true]` (`X-Admin-Token`).

**Server health card** (top of `/admin`): host load/RAM/disk plus per-container CPU/memory and the
fleet aggregates, filled asynchronously from `GET /admin/stats.json` (same Basic-Auth gate) because
the `docker stats` sample behind it takes ~1-2 s. Host numbers come from `/proc/meminfo` +
`/proc/loadavg` (host-wide inside a container) and `DriveInfo` resolved against the worlds bind
mount; on platforms without `/proc` (Windows dev) the fields are null and the card degrades.

## Public stats API

`GET /api/stats` (portal domain, no auth) returns exactly four aggregate numbers for the marketing
site / client — never names or ids:

```json
{ "worlds": { "created": 12, "active": 3 }, "players": { "registered": 45, "online": 7 }, "updatedUnix": 1783300000 }
```

Being public it is doubly guarded: the JSON snapshot is cached with single-flight rebuild
(`BBS_WH_STATS_CACHE_SECONDS`, default 30 — the per-instance `/status` probes behind `online` never
run per-request) and requests are rate limited per IP (`BBS_WH_STATS_PER_MINUTE`, default 30).
Responses carry `Access-Control-Allow-Origin: *` (so the website can fetch it client-side) and a
matching `Cache-Control`.

## Registry storage: SQLite now, Postgres when it earns it

The registry is SQLite on purpose: one host, one writer process, a tiny write volume, backup = one
file — and it matches the repo's SQLite-first philosophy. Note the distinction: **world saves** can
already live in PostgreSQL today (`BBS_DATABASE_PROVIDER=postgresql`, one schema per world — the
game supports it since PR #116); that is orthogonal to the registry. Move the REGISTRY to Postgres
when one of these becomes true: a second control-plane node (HA), multi-host placement (Phase 3),
or the wish to join registry + world data in one operational database. `HostRegistry` is a single
class behind plain methods, so swapping the backend later is contained.

## Phase 3 hardening (implemented)

- **Archive after inactivity.** `BBS_WH_ARCHIVE_MONTHS` (default 6, 0 = off): an hourly sweep moves
  a stopped world's saves to `<WorldsDir>/_archive/<id>/` and flips its status to `archived` once its
  last activity (join/wake or reaper stamp — `world.last_active_unix`) is older than the window.
  Joining an archived world **transparently restores it** (rename back + normal wake) — from the
  player's side it is just a world that takes a moment longer. Deleting stays separate; archive never
  destroys data. Ports remain allocated to archived worlds (the range bounds total worlds at
  `PortRangeSize`; revisit only if that ever pinches).
- **Rate limits** (fixed windows, in-memory, operator knobs): signups 5/h and logins 10/min per
  client IP (real IP via X-Forwarded-For — the app now honors forwarded headers from Caddy), save
  uploads 6/h and reports 10/h per account. Over-budget calls get HTTP 429 with a friendly text.
- **Blocked-name hygiene** (kid-facing): a short, unambiguous word list (operator-extendable via
  `BBS_WH_BLOCKED_WORDS`) is enforced on account names, world names AND in-game player names at the
  join grant — matched with the same normalization as reserved names, so separator tricks are caught.
  Deliberately minimal to avoid Scunthorpe-style false positives; the report button covers the rest.
- **Prometheus metrics.** `GET /metrics` (loopback only — Caddy does not route it): gauges
  (`bbs_accounts_total`, `bbs_worlds{status=…}`, `bbs_reports_open`) + counters (joins granted,
  wakes, reaped, archived, rate-limited).

## Legal pages, localization & account deletion (implemented 2026-07-05)

- **Localized errors via codes.** Every WorldHost error response carries a stable machine `code`
  (`banned`, `terms_outdated`, `name_reserved`, `name_blocked`, `world_wake_failed`, `rate_limited`,
  `world_limit`, `upload_too_large`, `save_invalid`, …) next to the English `error` text. The game
  client maps `code` → `ui.portal.err_<code>` locale keys (DE+EN); the portal maps it via a JS dict
  keyed on the browser language. `banned` keeps the operator's free-text reason. Unknown code → raw
  English fallback. (`PortalClient` exposes `Code`; `Program.CodeFor` is the single mapping point.)
- **Client parity.** The native "Official Worlds" menu now shows the beta warning, a one-line rules
  summary and a "View rules" button opening `/rules` in the browser — matching the portal.
- **`/impressum` (§5 DDG) + `/datenschutz` (DSGVO)**, footer-linked from every portal page. Operator
  data is config-driven (`BBS_WH_LEGAL_NAME` / `_ADDRESS` / `_EMAIL`) so a SELF-HOSTED WorldHost
  serves ITS operator's details, never the project authors'; unset → an explicit "not configured"
  notice, never wrong data. The privacy page states the data-minimal reality (name + PBKDF2 hash, no
  email, no tracking, no third-party embeds/fonts; transient IPs for rate limits/logs only).
- **Account self-deletion (DSGVO Art. 17).** `DELETE /api/account` + a double-confirm portal button:
  stops + deletes all the account's worlds (registry rows + live and archived saves on disk), deletes
  the reports it filed and its sessions, then the account. Available to banned accounts too.

## glitch.fun arcade channel (implemented; ships with v0.7.8)

A small pool of **persistent multiplayer worlds that exist ONLY for the glitch.fun platform**
(Devin Dixon's browser-first storefront). They live on world channel `glitch` in the same registry
and fleet, but are invisible to every portal surface: not in the public browser, not in any
account's world list — only the admin dashboard shows them (badge `glitch`, owner `glitch.fun`).
Note on the published Baumhaus principle: this channel is the publicly-amended exception — a
separate arcade context under Glitch's platform accounts and rules; the portal/family fleet keeps
the password-and-word-of-mouth rule unchanged (devblog follow-up published with the launch).

Flow (mirrors Glitch's Aegis contract; the WebGL build is uploaded to and served by Glitch):

1. Glitch serves the build at `play.glitch.fun/game/<titleId>/…?install_id=<uuid>` (iframe).
2. The client (with a baked `PortalUrl`, no title token) posts the install id to
   **`POST /api/glitch/session`**. The gateway validates the install server-to-server against
   `api.glitch.fun` (title token stays in `/opt/bbs/worldhost/.env`), refuses banned installs,
   assigns a stable player name (Glitch `user_name` → sanitized + 3-hex suffix of the install id;
   reserved/blocked names fall back to `Explorer`), picks an arcade world with headroom (waking a
   sleeping one on demand) and mints the normal HMAC join token for the synthetic guest identity
   `glitch:<install_id>` — no portal account involved.
3. The client auto-joins through the existing hosted deep-link path (`AppShell` arcade branch).
4. Heartbeats (Glitch's playtime/payout signal, every 60 s) go to **`POST /api/glitch/heartbeat`**,
   which relays them to Glitch server-to-server. A banned install gets 403 — the client leaves the
   world and shows a notice (the operator's live kick lever), while an unreachable Glitch answers
   503 and the client just keeps playing.

**Cloud-save relay (browser singleplayer):** the WebGL build's in-process singleplayer world (see
`WEBCLIENT_FEASIBILITY.md`) syncs its snapshot blob to Glitch's per-player **Cloud Save** (slot 0)
through three relayed routes, so the title token stays server-side: `GET /api/glitch/save?installId=`
(latest version + payload; 404 = none, 403 = guest — Cloud Save needs a logged-in Glitch account),
`POST /api/glitch/save` (base64 payload + `baseVersion`; the relay computes Glitch's required
SHA-256 over the DECODED bytes, enforces the 10 MB decoded cap and `BBS_WH_GLITCH_SAVES_PER_HOUR`
per install; a stale base answers 409 with `saveId`/`conflictId`) and `POST /api/glitch/save/resolve`
(`keep_server` | `use_client` — Glitch's explicit optimistic-concurrency flow; nothing is silently
overwritten, losing states stay in the slot's version history).

Operational notes:

- `/api/glitch/*` are the only CORS-enabled API routes; they echo exactly the configured Glitch
  origins (`BBS_WH_GLITCH_ALLOWED_ORIGINS`), never `*`, incl. OPTIONS preflight.
- Arcade worlds are normal fleet citizens otherwise: idle-shutdown, reaper, archive/restore and the
  `BBS_WH_MAX_ACTIVE` budget all apply (2 arcade worlds on a budget of 10). Their player cap is
  `BBS_WH_GLITCH_MAX_PLAYERS` (instance env `BBS_MAX_PLAYERS`, applied on the next container start).
- Worlds are persistent by design (returning players = retention = payout); griefing recourse is
  the admin stop/wake + install bans. In-game `/report` does NOT work for arcade guests (it needs a
  portal session) — known v0.7.8 limitation.
- Guest bookkeeping stores only Glitch's pseudonymous install id + the assigned player name
  (`glitch_guest`/`glitch_ban` tables); bans are managed on `/admin`.
- Build/publish: **the release pipeline mirrors every tagged release to glitch.fun automatically**
  (`release.yml` job `publish-glitch`): the one CI WebGL build gets `Enabled` + `PortalUrl` baked
  from the `GLITCH_PORTAL_URL` secret (dormant without Glitch's `?install_id=` param, so the same
  artifact serves `/play`), then the OFFICIAL Glitch deploy CLI uploads it — the no-Node
  `glitch-deploy-basic` shell variant from
  [Glitch-Cli-Deploy](https://github.com/Glitch-Gaming-Platform/Glitch-Cli-Deploy), pinned to a
  reviewed commit (`GLITCH_DEPLOY_TOKEN` secret; skips cleanly when unset; `--wait` until the CDN
  reports ready). Local/manual path: `scripts/publish-glitch-webgl.ps1` (same CLI via `-Deploy`
  under Git Bash, or ZIP for the Deploy Page).

## Version policy (fleet)

The fleet pins one image tag (`BBS_WH_SERVER_IMAGE`) — all instances run the same server version.
`Protocol.Version` gates incompatible clients at join (the server rejects mismatches with a clear
message). Save-schema changes must remain load-compatible with saves one release older (uploads are
validated but never migrated silently); document per release in the changelog.

## Open (tracked in the plan)

- **Impressum + Datenschutzerklärung** pages before public launch (operator-specific content).
- End-to-end playtest against a real Docker fleet (everything below the HTTP layer is unit-tested;
  the docker CLI path and Caddy routing need one real run on the VPS).
- Multi-host placement when one host stops being enough (spawner agent per node; registry → Postgres).
