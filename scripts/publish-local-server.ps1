<#
.SYNOPSIS
  Publishes the dedicated server into the Unity client for Singleplayer hosting (Option A).

.DESCRIPTION
  Builds a self-contained, single-file BlocksBeyondTheStars.GameServer and places it in
  client/Assets/StreamingAssets/server/. On "Singleplayer" the client launches this
  executable as a child process bound to loopback (see LocalServerLauncher.cs and
  docs/developer/CLIENT_COMPLETION.md). The server reuses the client's synced data/ content
  (passed via --data) and writes saves under the user's persistent data path, so no
  content is duplicated here.

  Run scripts/sync-client-libs.ps1 first (so StreamingAssets/data exists), then this.

.EXAMPLE
  ./scripts/publish-local-server.ps1
  ./scripts/publish-local-server.ps1 -Runtime win-x64
  ./scripts/publish-local-server.ps1 -Runtime win-x64 -Version 0.8.3
#>
param(
    [string] $Runtime = 'win-x64',
    # Baked into the server assembly so its reports (e.g. a server crash) carry the release version instead
    # of the .NET default 1.0.0 (release.yml passes the resolved tag). Defaults to a dev marker locally.
    [string] $Version = '0.0.0-dev'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'client/Assets/StreamingAssets/server'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Publishing dedicated server ($Runtime, v$Version) into the client ..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src/BlocksBeyondTheStars.GameServer') `
    -c Release -f net10.0 -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:InformationalVersion=$Version `
    -o $out | Out-Null

Write-Host "Bundled local server into $out" -ForegroundColor Green
Write-Host "Singleplayer will launch it on 127.0.0.1 and reuse StreamingAssets/data." -ForegroundColor Green
