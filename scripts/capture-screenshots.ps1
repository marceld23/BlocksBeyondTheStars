<#
.SYNOPSIS
  Captures the marketing screenshots from the built game player, once per language (DE + EN).

.DESCRIPTION
  Runs the built Windows player with the -captureShots flag, which makes the in-game
  ScreenshotDirector self-install, drive the client through a fixed sequence (start screen →
  planet surface → cockpit HUD → three space-flight vantage points) and write a 1920x1080 PNG of
  each, then quit. The HUD language is fixed at world start, so a full DE+EN set is two runs.

  Requires a current player build (scripts/build-client.ps1) — the build bundles the local server,
  which singleplayer needs. Pass -Build to (re)build first.

  Output: <repo>/docs/screenshots/<lang>/*.png

.EXAMPLE
  ./scripts/capture-screenshots.ps1                # both languages, existing build
  ./scripts/capture-screenshots.ps1 -Lang de       # German only
  ./scripts/capture-screenshots.ps1 -Build         # build the client first, then capture both
#>
param(
    [ValidateSet("both", "de", "en")]
    [string] $Lang = "both",
    [string] $Exe,
    [string] $OutRoot,
    [long]   $Seed = 424242,
    [switch] $Build
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe)     { $Exe = Join-Path $repo "client/Build/Windows/BlocksBeyondTheStars.exe" }
if (-not $OutRoot) { $OutRoot = Join-Path $repo "docs/screenshots" }

if ($Build) {
    Write-Host "Building the client first..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build-client.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-client.ps1 failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $Exe)) {
    throw "Player build not found at '$Exe'. Run scripts/build-client.ps1 first (or pass -Build)."
}

$langs = if ($Lang -eq "both") { @("de", "en") } else { @($Lang) }

foreach ($l in $langs) {
    $out = Join-Path $OutRoot $l
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Write-Host "Capturing '$l' → $out" -ForegroundColor Cyan

    $args = @(
        "-captureShots",
        "-lang", $l,
        "-shotOut", $out,
        "-seed", $Seed,
        "-screen-width", "1920",
        "-screen-height", "1080",
        "-screen-fullscreen", "0"
    )

    $proc = Start-Process -FilePath $Exe -ArgumentList $args -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        Write-Warning "Capture run for '$l' exited with code $($proc.ExitCode) — check the player log."
    }

    $shots = @(Get-ChildItem -Path $out -Filter *.png -ErrorAction SilentlyContinue)
    Write-Host "  → $($shots.Count) PNG(s) in $out" -ForegroundColor Green
}

Write-Host "Done. Screenshots under $OutRoot" -ForegroundColor Green
