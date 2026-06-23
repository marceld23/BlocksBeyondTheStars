#!/usr/bin/env bash
# Builds the Unity client as a Linux player via the editor in batch mode.
#
# It generates the launcher scene if needed and writes the player to the output folder.
# Prerequisites (sync-client-libs + sync-velopack-libs + publish-local-server) run automatically by default.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO/client"

UNITY_PATH=""
OUT_DIR="Build/Linux"
SKIP_PREREQS=false

while [ $# -gt 0 ]; do
    case "$1" in
        --unity-path) UNITY_PATH="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --skip-prereqs) SKIP_PREREQS=true; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

if [ -z "$UNITY_PATH" ]; then
    # Auto-detect Unity Hub installations.
    for candidate in /opt/Unity/Hub/Editor/*/Editor/Unity; do
        if [ -x "$candidate" ]; then
            UNITY_PATH="$candidate"
            break
        fi
    done
fi

if [ -z "$UNITY_PATH" ] || [ ! -x "$UNITY_PATH" ]; then
    echo "Unity editor not found. Pass --unity-path to your Unity 6000.4.x editor."
    echo "Looked in /opt/Unity/Hub/Editor/*/Editor/Unity"
    exit 1
fi

echo "Using Unity: $UNITY_PATH"

if [ "$SKIP_PREREQS" = false ]; then
    echo "==> Prerequisites: syncing shared libs + content ..."
    "$(dirname "$0")/sync-client-libs.sh"

    echo "==> Prerequisites: syncing Velopack libs ..."
    "$(dirname "$0")/sync-velopack-libs.sh"

    echo "==> Prerequisites: publishing the bundled server ..."
    "$(dirname "$0")/publish-local-server.sh"
fi

LOG="$PROJECT/build.log"
OUT_ABS="$PROJECT/$OUT_DIR"
mkdir -p "$OUT_ABS"

echo "==> Building Linux player (this can take a few minutes) ..."
"$UNITY_PATH" -batchmode -quit -nographics \
    -projectPath "$PROJECT" \
    -executeMethod BlocksBeyondTheStars.Client.EditorTools.BuildScript.BuildLinux \
    -buildOut "$OUT_ABS" \
    -logFile "$LOG"

# Unity can relaunch a child process, so wait for it to finish properly.
echo "==> Waiting for Unity to finish ..."
DEADLINE=$((SECONDS + 1500))  # 25 minutes
while [ $SECONDS -lt $DEADLINE ]; do
    if [ -f "$LOG" ] && grep -q 'Exiting batchmode successfully now!' "$LOG" 2>/dev/null; then
        break
    fi
    sleep 4
done
sleep 2

SUCCEEDED=false
COMPILE_FAIL=false
if [ -f "$LOG" ]; then
    if grep -q 'BlocksBeyondTheStars build:' "$LOG" && grep -q 'Succeeded' "$LOG"; then
        SUCCEEDED=true
    fi
    if grep -q 'Scripts have compiler errors' "$LOG"; then
        COMPILE_FAIL=true
    fi
fi

if [ "$COMPILE_FAIL" = true ] || [ "$SUCCEEDED" = false ]; then
    if [ "$COMPILE_FAIL" = true ]; then
        echo "Build FAILED (script compile errors). See $LOG"
    else
        echo "Build FAILED (no success marker in log). See $LOG"
    fi
    exit 1
fi

# Build the console launcher (Linux).
echo "==> Building the console launcher ..."
LAUNCHER_PROJ="$REPO/src/BlocksBeyondTheStars.Launcher.Console/BlocksBeyondTheStars.Launcher.Console.csproj"
LAUNCHER_TMP="$REPO/artifacts/launcher"
dotnet publish "$LAUNCHER_PROJ" -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$LAUNCHER_TMP" >/dev/null

cp "$LAUNCHER_TMP/BlocksBeyondTheStars.Launcher.Console" "$OUT_ABS/"

echo "Build complete -> $OUT_ABS/BlocksBeyondTheStars.x86_64"
echo "Launcher       -> $OUT_ABS/BlocksBeyondTheStars.Launcher.Console (run this for terminal output)"
