#!/usr/bin/env bash
# Publishes the dedicated server into the Unity client for Singleplayer hosting on Linux.
#
# Builds a self-contained, single-file BlocksBeyondTheStars.GameServer and places it in
# client/Assets/StreamingAssets/server/. On "Singleplayer" the client launches this
# executable as a child process bound to loopback (see LocalServerLauncher.cs).
# The server reuses the client's synced data/ content.
#
# Run scripts/sync-client-libs.sh first (so StreamingAssets/data exists), then this.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
RUNTIME="${1:-linux-x64}"
OUT="$REPO/client/Assets/StreamingAssets/server"

if [ -d "$OUT" ]; then
    rm -rf "$OUT"
fi
mkdir -p "$OUT"

echo "==> Publishing dedicated server ($RUNTIME) into the client ..."
dotnet publish "$REPO/src/BlocksBeyondTheStars.GameServer" \
    -c Release -r "$RUNTIME" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT" >/dev/null

echo "Bundled local server into $OUT"
echo "Singleplayer will launch it on 127.0.0.1 and reuse StreamingAssets/data."
