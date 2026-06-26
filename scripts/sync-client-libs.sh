#!/usr/bin/env bash
# Builds the shared netstandard2.1 libraries and copies them (plus their dependencies and
# the data/ content) into the Unity client so it can reference the exact same game code as
# the server.
#
# Run this after changing BlocksBeyondTheStars.Shared / WorldGeneration / Networking / Client.Core,
# then refresh the Unity Editor. DLLs land in client/Assets/Plugins; content lands in
# client/Assets/StreamingAssets/data.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS="$REPO/client/Assets/Plugins"
STREAMING="$REPO/client/Assets/StreamingAssets/data"

mkdir -p "$PLUGINS"
mkdir -p "$STREAMING"

PROJECTS=(
    "src/BlocksBeyondTheStars.Shared"
    "src/BlocksBeyondTheStars.WorldGeneration"
    "src/BlocksBeyondTheStars.Networking"
    "src/BlocksBeyondTheStars.Client.Core"
)

TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

for p in "${PROJECTS[@]}"; do
    echo "==> Publishing $p ..."
    name="$(basename "$p")"
    out="$TEMP/$name"
    dotnet publish "$REPO/$p" -c Release -o "$out" >/dev/null
    find "$out" -name '*.dll' -exec cp -f {} "$PLUGINS/" \;
done

# Copy data-driven content into StreamingAssets.
cp -r "$REPO/data/"* "$STREAMING/"

# Copy embedded-browser web content (wiki + minigames) into StreamingAssets root.
WEB="$REPO/web"
if [ -d "$WEB" ]; then
    cp -r "$WEB/"* "$REPO/client/Assets/StreamingAssets/"
fi

echo "Synced libraries to $PLUGINS"
echo "Synced content to $STREAMING"
echo "Synced web content (wiki + minigames) to $REPO/client/Assets/StreamingAssets"
echo "Note: if Unity reports a duplicate of a System.* assembly it already ships, delete that DLL from Plugins."
