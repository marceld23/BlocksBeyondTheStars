<#
.SYNOPSIS
  Builds the shared netstandard2.1 libraries and copies them (plus their dependencies and
  the data/ content) into the Unity client so it can reference the exact same game code as
  the server.

.DESCRIPTION
  Run this after changing BlocksBeyondTheStars.Shared / WorldGeneration / Networking, then refresh
  the Unity Editor. DLLs land in client/Assets/Plugins; content lands in
  client/Assets/StreamingAssets/data.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$plugins = Join-Path $repo 'client/Assets/Plugins'
$streaming = Join-Path $repo 'client/Assets/StreamingAssets/data'

New-Item -ItemType Directory -Force $plugins | Out-Null
New-Item -ItemType Directory -Force $streaming | Out-Null

$projects = @(
    @{ Path = 'src/BlocksBeyondTheStars.Shared' },
    @{ Path = 'src/BlocksBeyondTheStars.WorldGeneration' },
    @{ Path = 'src/BlocksBeyondTheStars.Networking' },
    @{ Path = 'src/BlocksBeyondTheStars.Client.Core' },  # Unity-free client logic (NetworkClient, ClientWorld) — see docs/developer/CLIENT_TESTING.md
    # The authoritative simulation + managed persistence for the in-browser singleplayer (WebGL runs
    # the REAL server in-process over the LoopbackTransport). Both are multi-target; the Unity client
    # consumes their netstandard2.1 library flavor — the net10 exe/SQLite side never enters Plugins.
    @{ Path = 'src/BlocksBeyondTheStars.Persistence'; Framework = 'netstandard2.1' },
    @{ Path = 'src/BlocksBeyondTheStars.GameServer';  Framework = 'netstandard2.1' }
)

# Publish (not just build) each library so its NuGet dependencies (MessagePack, LiteNetLib,
# System.Text.Json, ...) are gathered, then copy every produced DLL into Plugins.
$temp = Join-Path $repo 'artifacts/client-libs'
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }

foreach ($p in $projects) {
    Write-Host "Publishing $($p.Path) ..." -ForegroundColor Cyan
    $name = Split-Path $p.Path -Leaf
    $out = Join-Path $temp $name
    $fw = if ($p.Framework) { @('-f', $p.Framework) } else { @() }
    dotnet publish (Join-Path $repo $p.Path) -c Release @fw -o $out | Out-Null
    Get-ChildItem $out -Filter *.dll | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $plugins $_.Name) -Force
    }
}

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

# Copy the data-driven content so the client can load definitions + locales at runtime. This also carries
# the in-game wiki (data/wiki/articles.json) and arcade catalogue (data/minigames/catalog.json), both of
# which the native UI reads from StreamingAssets. See docs/developer/MINIGAMES_AND_WIKI.md.
Copy-Item (Join-Path $repo 'data/*') $streaming -Recurse -Force

Write-Host "Synced libraries to $plugins" -ForegroundColor Green
Write-Host "Synced content to $streaming" -ForegroundColor Green
Write-Host "Note: if Unity reports a duplicate of a System.* assembly it already ships, delete that DLL from Plugins." -ForegroundColor Yellow
