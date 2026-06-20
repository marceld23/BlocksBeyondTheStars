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
    'src/BlocksBeyondTheStars.Shared',
    'src/BlocksBeyondTheStars.WorldGeneration',
    'src/BlocksBeyondTheStars.Networking',
    'src/BlocksBeyondTheStars.Client.Core'   # Unity-free client logic (NetworkClient, ClientWorld) — see docs/developer/CLIENT_TESTING.md
)

# Publish (not just build) each library so its NuGet dependencies (MessagePack, LiteNetLib,
# System.Text.Json, ...) are gathered, then copy every produced DLL into Plugins.
$temp = Join-Path $repo 'artifacts/client-libs'
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }

foreach ($p in $projects) {
    Write-Host "Publishing $p ..." -ForegroundColor Cyan
    $name = Split-Path $p -Leaf
    $out = Join-Path $temp $name
    dotnet publish (Join-Path $repo $p) -c Release -o $out | Out-Null
    Get-ChildItem $out -Filter *.dll | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $plugins $_.Name) -Force
    }
}

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

# Copy the data-driven content so the client can load definitions + locales at runtime.
Copy-Item (Join-Path $repo 'data/*') $streaming -Recurse -Force

# Copy the embedded-browser web content (in-game wiki + arcade minigames) into StreamingAssets. Source of
# truth is web/; StreamingAssets is generated (gitignored), like data/. See docs/developer/MINIGAMES_AND_WIKI.md.
$streamingRoot = Join-Path $repo 'client/Assets/StreamingAssets'
$web = Join-Path $repo 'web'
if (Test-Path $web) {
    Copy-Item (Join-Path $web '*') $streamingRoot -Recurse -Force
}

Write-Host "Synced libraries to $plugins" -ForegroundColor Green
Write-Host "Synced content to $streaming" -ForegroundColor Green
Write-Host "Synced web content (wiki + minigames) to $streamingRoot" -ForegroundColor Green
Write-Host "Note: if Unity reports a duplicate of a System.* assembly it already ships, delete that DLL from Plugins." -ForegroundColor Yellow
