<#
.SYNOPSIS
  Publishes the dedicated server into the Unity client for Singleplayer hosting (Option A).

.DESCRIPTION
  Builds a self-contained, single-file BlocksBeyondTheStars.GameServer and places it in
  client/Assets/StreamingAssets/server/. On "Singleplayer" the client launches this
  executable as a child process bound to loopback (see LocalServerLauncher.cs and
  docs/CLIENT_COMPLETION_PLAN.md). The server reuses the client's synced data/ content
  (passed via --data) and writes saves under the user's persistent data path, so no
  content is duplicated here.

  Run scripts/sync-client-libs.ps1 first (so StreamingAssets/data exists), then this.

.EXAMPLE
  ./scripts/publish-local-server.ps1
  ./scripts/publish-local-server.ps1 -Runtime win-x64
#>
param(
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'client/Assets/StreamingAssets/server'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Publishing dedicated server ($Runtime) into the client ..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src/BlocksBeyondTheStars.GameServer') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out | Out-Null

Write-Host "Bundled local server into $out" -ForegroundColor Green
Write-Host "Singleplayer will launch it on 127.0.0.1 and reuse StreamingAssets/data." -ForegroundColor Green
