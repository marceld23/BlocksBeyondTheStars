#!/usr/bin/env bash
# Runs the project's .NET test suites. Selectable per suite.
#
# Suites:
#   Dotnet      .NET xUnit server/shared suite (tests/BlocksBeyondTheStars.Tests) - no Unity.
#   ClientCore  Headless client<->server integration (tests/BlocksBeyondTheStars.Client.Tests).
#
# Default: Dotnet, ClientCore
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"

SUITES=""

while [ $# -gt 0 ]; do
    case "$1" in
        --suites) SUITES="$2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

if [ -z "$SUITES" ]; then
    SUITES="Dotnet,ClientCore"
fi

# Split comma-separated list.
IFS=',' read -ra SELECTED <<< "$SUITES"
# Normalize (trim whitespace).
for i in "${!SELECTED[@]}"; do
    SELECTED[$i]="$(echo "${SELECTED[$i]}" | xargs)"
done

# Expand 'All'.
if [[ " ${SELECTED[*]} " =~ " All " ]]; then
    SELECTED=("Dotnet" "ClientCore")
fi

FAILURES=()
SUMMARY=()

run_dotnet_suite() {
    local name="$1"
    local project="$2"
    echo "==> $name ($project) =="
    if dotnet test "$REPO/$project" -c Debug --nologo; then
        SUMMARY+=("passed  $name")
    else
        FAILURES+=("$name")
        SUMMARY+=("FAILED  $name")
    fi
}

for suite in "${SELECTED[@]}"; do
    case "$suite" in
        Dotnet)     run_dotnet_suite "Dotnet"     "tests/BlocksBeyondTheStars.Tests/BlocksBeyondTheStars.Tests.csproj" ;;
        ClientCore) run_dotnet_suite "ClientCore" "tests/BlocksBeyondTheStars.Client.Tests/BlocksBeyondTheStars.Client.Tests.csproj" ;;
        *)
            echo "Unknown suite: $suite (valid: Dotnet, ClientCore, All)"
            exit 1
            ;;
    esac
done

echo ""
echo "==================== Test summary ===================="
for s in "${SUMMARY[@]}"; do
    echo "  $s"
done
echo "====================================================="

if [ ${#FAILURES[@]} -gt 0 ]; then
    echo "Test run FAILED in: ${FAILURES[*]}"
    exit 1
else
    echo "All selected suites passed."
fi
