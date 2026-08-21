#!/usr/bin/env bash
# Builds the shared netstandard2.1 libraries and copies them (plus their dependencies and
# the data/ content) into the Unity client so it can reference the exact same game code as
# the server.
#
# Run this after changing BlocksBeyondTheStars.Shared / WorldGeneration / Networking / Client.Core,
# then refresh the Unity Editor. DLLs land in client/Assets/Plugins; content lands in
# client/Assets/StreamingAssets/data; the music library (client/Music) lands in
# client/Assets/StreamingAssets/music.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS="$REPO/client/Assets/Plugins"
STREAMING="$REPO/client/Assets/StreamingAssets/data"
MUSIC="$REPO/client/Assets/StreamingAssets/music"

mkdir -p "$PLUGINS"
mkdir -p "$STREAMING"
mkdir -p "$MUSIC"

# "project[:framework]" — the last two are multi-target; Unity consumes their netstandard2.1
# library flavor (the in-browser singleplayer server + managed persistence). Keep in sync with
# scripts/sync-client-libs.ps1.
PROJECTS=(
    "src/BlocksBeyondTheStars.Shared"
    "src/BlocksBeyondTheStars.WorldGeneration"
    "src/BlocksBeyondTheStars.Networking"
    "src/BlocksBeyondTheStars.Client.Core"
    "src/BlocksBeyondTheStars.Persistence:netstandard2.1"
    "src/BlocksBeyondTheStars.GameServer:netstandard2.1"
)

TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

for entry in "${PROJECTS[@]}"; do
    p="${entry%%:*}"
    fw="${entry#*:}"
    fwargs=()
    if [ "$fw" != "$entry" ]; then
        fwargs=(-f "$fw")
    fi
    echo "==> Publishing $p ${fwargs[*]:-} ..."
    name="$(basename "$p")"
    out="$TEMP/$name"
    dotnet publish "$REPO/$p" -c Release "${fwargs[@]}" -o "$out" >/dev/null
    find "$out" -name '*.dll' -exec cp -f {} "$PLUGINS/" \;
done

# Copy data-driven content into StreamingAssets. This also carries the in-game wiki
# (data/wiki/articles.json) and arcade catalogue (data/minigames/catalog.json), both read by the native UI.
cp -r "$REPO/data/"* "$STREAMING/"

# Copy the background-music library (client/Music/*.mp3, tracked) into StreamingAssets/music. It lives
# OUTSIDE Assets/ on purpose: as a Resources asset Unity baked all 40 tracks (164 MB) into the WebGL
# player data file that every browser visitor downloads before the first frame (#1167). As raw
# StreamingAssets files ClientMusic streams each track on demand instead. Keep it out of
# StreamingAssets/data — that folder's manifest is prefetched eagerly by the browser client.
cp -f "$REPO/client/Music/"*.mp3 "$MUSIC/"

echo "Synced libraries to $PLUGINS"
echo "Synced content to $STREAMING"
echo "Synced music to $MUSIC"
echo "Note: if Unity reports a duplicate of a System.* assembly it already ships, delete that DLL from Plugins."
