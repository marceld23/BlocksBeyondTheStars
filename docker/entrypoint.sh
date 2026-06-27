#!/usr/bin/env bash
# Container entrypoint for the Blocks Beyond the Stars dedicated server image.
#
# It runs the server processes in one container, asymmetrically (see docs/developer/SELF_HOSTING.md §10):
#   * the GAME SERVER is the critical, foreground process — it receives the shutdown signal and saves the
#     world on its way out;
#   * the ADMIN API (dashboard + portal + client download) is a best-effort sidecar — if it dies it is
#     restarted and never takes the game server (and the players on it) down with it.
#   * the optional AI text backend (Python) is started ONLY when an ai-backend/.env is provided (or
#     BBTS_AI_* is set); it too is a best-effort, auto-restarting sidecar.
#
# tini is PID 1 (see the Dockerfile ENTRYPOINT): it reaps zombies and forwards SIGTERM to this script.
set -uo pipefail

APP_DIR=/app
CONFIG_DIR="$APP_DIR/config"
CLIENTS_DIR="$APP_DIR/clients"
SAVES_DIR="$APP_DIR/saves"
mkdir -p "$CONFIG_DIR" "$CLIENTS_DIR" "$SAVES_DIR"

log() { echo "[entrypoint] $*"; }

# The admin UI/portal must be reachable from outside the container; the application's own default is
# loopback. The Dockerfile sets BBS_ADMIN_BIND=0.0.0.0; re-export it here so it is in scope for both
# child processes regardless of how the container was started.
export BBS_ADMIN_BIND="${BBS_ADMIN_BIND:-0.0.0.0}"

# --- best-effort: fetch the latest client downloads from GitHub Releases so the portal works ---
# A Linux container cannot build the clients (Unity + Windows are needed), so the portal's one-click
# downloads are populated from the published GitHub Release instead: the Windows Setup.exe (served at
# /download) and the Linux AppImage (served at /download-linux). Non-fatal: without them the portal
# still runs and the download routes simply report "nothing published yet".

# Fetch a single release asset whose download URL matches the given suffix pattern into $CLIENTS_DIR.
# $1 = release JSON, $2 = grep pattern for the asset filename, $3 = human label.
fetch_asset() {
  local json="$1" pattern="$2" label="$3"
  local url
  url=$(printf '%s' "$json" \
        | grep -o "\"browser_download_url\":[ ]*\"[^\"]*${pattern}\"" \
        | head -n1 | sed 's/.*"\(https[^"]*\)"/\1/')
  [ -n "$url" ] || { log "no ${label} asset in the latest release"; return 1; }

  local target="$CLIENTS_DIR/$(basename "$url")"
  curl -fsSL "$url" -o "$target.partial" || return 1
  mv -f "$target.partial" "$target"
  log "${label} ready: $(basename "$target")"
}

fetch_client() {
  local repo="${BBS_CLIENT_REPO:-marceld23/BlocksBeyondTheStars}"
  local api="https://api.github.com/repos/${repo}/releases/latest"
  log "fetching latest client downloads from ${repo} ..."

  local json
  json=$(curl -fsSL "$api") || return 1

  # Each asset is independent: a missing Windows or Linux build must not skip the other.
  fetch_asset "$json" 'Setup\.exe' 'Windows installer' || true
  fetch_asset "$json" '\.AppImage' 'Linux AppImage'     || true
}

if [ "${BBS_FETCH_CLIENT:-1}" = "1" ]; then
  fetch_client || log "client download skipped (continuing without it)"
else
  log "BBS_FETCH_CLIENT=0 -> skipping client download"
fi

# --- admin API: best-effort sidecar, auto-restarted, never fatal to the container ---
run_api() {
  while true; do
    "$APP_DIR/BlocksBeyondTheStars.Api"
    log "admin API exited ($?); restarting in 5s"
    sleep 5
  done
}
run_api &
API_SUPERVISOR_PID=$!

# --- optional AI text backend (Python): started ONLY when configured ---
# "Configured" = an ai-backend/.env file is present (mounted at runtime), or BBTS_AI_BASE_URL is set in
# the environment. Set BBS_AI_BACKEND=1/0 to force it on/off regardless. Best-effort sidecar like the API.
AI_DIR="$APP_DIR/ai-backend"
AI_ENV="${BBS_AI_ENV_FILE:-$AI_DIR/.env}"
AI_SUPERVISOR_PID=""
start_ai="${BBS_AI_BACKEND:-auto}"
if [ "$start_ai" = "auto" ]; then
  if [ -s "$AI_ENV" ] || [ -n "${BBTS_AI_BASE_URL:-}" ]; then start_ai=1; else start_ai=0; fi
fi

if [ "$start_ai" = "1" ]; then
  if [ -x "$AI_DIR/.venv/bin/uvicorn" ]; then
    log "AI backend: configuration found -> starting on 127.0.0.1:8077 (set BBS_AI_LEVEL=Suggest|Auto to use it)"
    run_ai() {
      while true; do
        # CWD = ai-backend so the app's load_dotenv() picks up ai-backend/.env.
        ( cd "$AI_DIR" && exec .venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8077 )
        log "AI backend exited ($?); restarting in 5s"
        sleep 5
      done
    }
    run_ai &
    AI_SUPERVISOR_PID=$!
  else
    log "AI backend requested but not installed in this image; skipping"
  fi
else
  log "AI backend: no .env at $AI_ENV and BBTS_AI_BASE_URL unset -> not starting"
fi

# --- game server: the critical foreground process ---
"$APP_DIR/BlocksBeyondTheStars.GameServer" &
SERVER_PID=$!
log "game server started (pid $SERVER_PID); admin supervisor (pid $API_SUPERVISOR_PID)"

# `docker stop` delivers SIGTERM, but the server's clean drain+save path is wired to SIGINT
# (Console.CancelKeyPress). Translate TERM/INT into SIGINT for the server so the world is always saved.
shutdown() {
  log "shutdown requested -> SIGINT to game server (clean save)"
  kill -INT "$SERVER_PID" 2>/dev/null || true
}
trap shutdown TERM INT

# Wait for the game server to exit. A trapped signal interrupts `wait`, so loop until the process is
# really gone (it is still draining + saving in the meantime).
while :; do
  wait "$SERVER_PID"
  STATUS=$?
  kill -0 "$SERVER_PID" 2>/dev/null || break
done

log "game server stopped (exit $STATUS); stopping sidecars"
kill "$API_SUPERVISOR_PID" 2>/dev/null || true
[ -n "$AI_SUPERVISOR_PID" ] && kill "$AI_SUPERVISOR_PID" 2>/dev/null || true
exit "$STATUS"
