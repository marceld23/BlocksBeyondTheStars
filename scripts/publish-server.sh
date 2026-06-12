#!/usr/bin/env bash
# Publishes the BlocksBeyondTheStars dedicated server + admin UI as self-hosting packages
# (self-contained, single-file) for Windows x64, Linux x64 and Linux ARM64 (Pi 5).
#
# Usage:
#   ./scripts/publish-server.sh                 # all three runtimes
#   ./scripts/publish-server.sh linux-arm64     # one runtime
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS="$REPO/artifacts"
RUNTIMES=("$@")
if [ ${#RUNTIMES[@]} -eq 0 ]; then
    RUNTIMES=(win-x64 linux-x64 linux-arm64)
fi

for RID in "${RUNTIMES[@]}"; do
    echo "==> Publishing for $RID"
    OUT="$ARTIFACTS/$RID"
    rm -rf "$OUT"
    mkdir -p "$OUT"

    COMMON=(-c Release -r "$RID" -p:SelfContained=true -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT")

    dotnet publish "$REPO/src/BlocksBeyondTheStars.GameServer" "${COMMON[@]}"
    dotnet publish "$REPO/src/BlocksBeyondTheStars.Api" "${COMMON[@]}"
    dotnet publish "$REPO/src/BlocksBeyondTheStars.Tools" "${COMMON[@]}"

    cp -r "$REPO/data" "$OUT/data"
    mkdir -p "$OUT/config"

    ( cd "$OUT" && zip -rq "$ARTIFACTS/blocks-beyond-the-stars-server-$RID.zip" . )
    echo "    Package: $ARTIFACTS/blocks-beyond-the-stars-server-$RID.zip"
done

echo "Done. Packages are in $ARTIFACTS"
