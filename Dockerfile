# syntax=docker/dockerfile:1
#
# Blocks Beyond the Stars — dedicated server image (OPTIONAL, not part of the normal GitHub release).
# Runs the headless .NET game server + the admin/portal/download web UI in one container.
# See docs/developer/SELF_HOSTING.md §10 for usage. Build context = repo root (the .dockerignore keeps
# the huge Unity client/, bin/obj and .git out of the build).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Publish the three headless server projects into one folder, framework-dependent (the runtime image
# already ships the .NET + ASP.NET runtime). We publish the projects individually on purpose: the .sln
# also contains the net8.0-windows WinForms launcher, which cannot build on Linux.
RUN dotnet publish src/BlocksBeyondTheStars.GameServer/BlocksBeyondTheStars.GameServer.csproj -c Release -o /app \
 && dotnet publish src/BlocksBeyondTheStars.Api/BlocksBeyondTheStars.Api.csproj               -c Release -o /app \
 && dotnet publish src/BlocksBeyondTheStars.Tools/BlocksBeyondTheStars.Tools.csproj           -c Release -o /app \
 && cp -r data /app/data

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# tini = a proper PID 1 (reaps zombies + forwards signals to the entrypoint). curl = best-effort download
# of the client downloads from GitHub Releases so the portal's /download (Windows Setup.exe) and
# /download-linux (Linux AppImage) work. python3 + venv = the optional AI text backend (baked in, but
# only STARTED when a .env is provided — see the entrypoint).
RUN apt-get update \
 && apt-get install -y --no-install-recommends tini curl ca-certificates python3 python3-venv \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Optional AI text backend (NPC greetings / mission flavour / VEGA banter). It is installed into its own
# venv so it never clashes with anything; it is only LAUNCHED at runtime when an ai-backend/.env exists (or
# BBTS_AI_* is set). Note: this pulls LangChain/LangGraph, which makes the image noticeably larger.
# The .dockerignore keeps any local ai-backend/.env (real keys) out of the build context.
COPY ai-backend/ /app/ai-backend/
RUN python3 -m venv /app/ai-backend/.venv \
 && /app/ai-backend/.venv/bin/pip install --no-cache-dir --upgrade pip \
 && /app/ai-backend/.venv/bin/pip install --no-cache-dir \
      "fastapi>=0.110" "uvicorn>=0.29" "langchain>=0.3" "langchain-openai>=0.2" "langgraph>=0.2" "python-dotenv>=1.0"

COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Default the admin UI/portal bind to all interfaces (the app's own default is loopback, which would be
# unreachable from outside the container). ALWAYS pair a public admin port with BBS_ADMIN_PASSWORD.
ENV BBS_ADMIN_BIND=0.0.0.0

# 31415/udp native client · 31415/tcp browser WebSocket (when BBS_ENABLE_WEBSOCKET=true) · 31416/tcp admin+portal+download
EXPOSE 31415/udp 31415/tcp 31416/tcp

# saves/ = SQLite world + backups + logs + /bump bug reports (<world>/bumps) · config/ = server.json · clients/ = published Windows Setup.exe + Linux AppImage
VOLUME ["/app/saves", "/app/config", "/app/clients"]

# Health = the public admin dashboard root (cheap static HTML, never password-gated unlike /api/*).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS "http://127.0.0.1:${BBS_ADMIN_PORT:-31416}/" >/dev/null || exit 1

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
