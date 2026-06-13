<#
.SYNOPSIS
  Packages the built Windows client into a Velopack installer (Setup.exe) + auto-update feed.

.DESCRIPTION
  Runs the Velopack CLI (`vpk pack`) over the Unity player in client/Build/Windows and produces, in
  artifacts/installer:

    BlocksBeyondTheStars-win-Setup.exe        the installer players download + run
    BlocksBeyondTheStars-<ver>-full.nupkg     the release payload (the update feed)
    releases.win.json / RELEASES              the feed manifest the client reads
    BlocksBeyondTheStars-win-Portable.zip     a no-install portable copy (bonus)

  Players install the Setup.exe (per-user, no admin needed — Velopack installs to %LOCALAPPDATA%) and
  the in-app updater (ClientUpdater.cs) pulls future versions from a feed URL served by
  BlocksBeyondTheStars.Api at /updates. Use -ServeDir to copy the feed straight into a server install so
  the API serves it (its /download button hands out the Setup.exe; /updates is the feed).

  Prerequisites:
    1. The Velopack runtime must be in the client build: run ./scripts/sync-velopack-libs.ps1 ONCE,
       refresh the Unity Editor, then build with ./scripts/build-client.ps1.
    2. The Velopack CLI (vpk). Auto-installed here as a global tool if missing.

.PARAMETER Version
  SemVer for this release. Default: read from AppShell.Version (e.g. 0.20.0-dev). Pass a clean value
  like 0.20.0 for an actual release. Each published version MUST be higher than the last for updates to
  apply.

.PARAMETER BuildDir
  The Unity player folder to package (must contain BlocksBeyondTheStars.exe). Default: client/Build/Windows.

.PARAMETER ServeDir
  Optional server install dir; the installer + feed are copied to <ServeDir>/clients so the API serves them.

.EXAMPLE
  ./scripts/publish-client-installer.ps1
  ./scripts/publish-client-installer.ps1 -Version 0.21.0 -ServeDir artifacts/win-x64
#>
param(
    [string] $Version = '',
    [string] $BuildDir = 'client/Build/Windows',
    [string] $OutputDir = 'artifacts/installer',
    [string] $ServeDir = '',
    [string] $Channel = 'win'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repo $BuildDir
$out = Join-Path $repo $OutputDir

if (-not (Test-Path (Join-Path $build 'BlocksBeyondTheStars.exe'))) {
    Write-Error "Client build not found at '$build'. Build it first with ./scripts/build-client.ps1."
}

# Derive the version from AppShell.Version unless one was given.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $appShell = Get-Content (Join-Path $repo 'client/Assets/BlocksBeyondTheStars/Scripts/AppShell.cs') -Raw
    if ($appShell -match 'Version\s*=\s*"([^"]+)"') { $Version = $Matches[1] }
    else { Write-Error 'Could not read AppShell.Version; pass -Version explicitly.' }
}

# Ensure the vpk CLI is on PATH (install the global tool on first use).
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing the Velopack CLI (vpk) ...' -ForegroundColor Cyan
    dotnet tool install -g vpk | Out-Null
    $env:PATH += [IO.Path]::PathSeparator + (Join-Path $HOME '.dotnet/tools')
}

New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Packaging Blocks Beyond the Stars $Version (channel '$Channel') from $build ..." -ForegroundColor Cyan
$packArgs = @(
    'pack',
    '--packId', 'BlocksBeyondTheStars',
    '--packVersion', $Version,
    '--packTitle', 'Blocks Beyond the Stars',
    '--packAuthors', 'JuMaVe Games',
    '--packDir', $build,
    '--mainExe', 'BlocksBeyondTheStars.exe',
    '--channel', $Channel,
    '--outputDir', $out
)
# Sign here later by adding: --signParams "..."  (an unsigned Setup.exe triggers a one-time SmartScreen prompt).
vpk @packArgs

$setup = Get-ChildItem $out -Filter '*Setup.exe' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
Write-Host "Installer: $($setup.FullName)" -ForegroundColor Green
Write-Host "Feed:      $out (releases.$Channel.json + *.nupkg)" -ForegroundColor Green

# Optionally stage installer + feed into a server install dir for the API to serve.
if (-not [string]::IsNullOrWhiteSpace($ServeDir)) {
    $serve = Join-Path $repo $ServeDir
    $clients = Join-Path $serve 'clients'
    New-Item -ItemType Directory -Force $clients | Out-Null
    Copy-Item (Join-Path $out '*') $clients -Force
    Write-Host "Staged into $clients — the API serves /download (Setup.exe) and /updates (feed)." -ForegroundColor Green
}
