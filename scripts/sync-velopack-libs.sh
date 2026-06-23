#!/usr/bin/env bash
# Copies the Velopack runtime assemblies into the Unity client so the client can self-update.
#
# Unlike the shared game libraries (handled by sync-client-libs.sh), Velopack is a client-only
# dependency. This restores the pinned Velopack package via a throwaway project, then copies
# every produced DLL into client/Assets/Plugins — but ONLY if a file of that name is not already
# there, so it never clobbers the shared libs or the System.* facades Unity already ships.
#
# Run this once before the first client build that includes updates, and again only when bumping
# the Velopack version. After running, refresh the Unity Editor so it imports the new DLLs.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS="$REPO/client/Assets/Plugins"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

VERSION="${1:-1.2.0}"

mkdir -p "$PLUGINS"

echo "==> Restoring Velopack $VERSION ..."
dotnet new classlib -n VeloVendor -o "$TEMP" --framework netstandard2.0 >/dev/null
dotnet add "$TEMP/VeloVendor.csproj" package Velopack --version "$VERSION" >/dev/null
dotnet publish "$TEMP/VeloVendor.csproj" -c Release -o "$TEMP/out" >/dev/null

ADDED=()
SKIPPED=()
for dll in "$TEMP/out"/*.dll; do
    name="$(basename "$dll")"
    [ "$name" = "VeloVendor.dll" ] && continue
    dest="$PLUGINS/$name"
    if [ -f "$dest" ]; then
        SKIPPED+=("$name")
    else
        cp "$dll" "$dest"
        ADDED+=("$name")
    fi
done

IFS=', '
echo "Added to Plugins:   ${ADDED[*]:-(none)}"
echo "Already present:    ${SKIPPED[*]:-(none)}"
unset IFS
echo "Now refresh the Unity Editor so it imports the new DLLs."
