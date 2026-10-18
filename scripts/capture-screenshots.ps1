<#
.SYNOPSIS
  Captures the marketing screenshots from the built game player, once per language (DE + EN).

.DESCRIPTION
  Runs the built Windows player with the -captureShots flag, which makes the in-game
  ScreenshotDirector self-install, drive the client through a fixed sequence (start screen →
  cockpit HUD → Tab menu → space flight → planet surface) and write a 1920x1080 PNG of each, then
  quit. These are the "how it plays" shots WITH the HUD; the HUD language is fixed at world start,
  so a full DE+EN set is two runs.

  The HUD-free world gallery (planet types, creatures, villages, giants) comes from
  scripts/capture-stills.ps1 instead (#2765) — one language-neutral set in docs/screenshots/world.

  Requires a current player build (scripts/build-client.ps1) — the build bundles the local server,
  which singleplayer needs. Pass -Build to (re)build first.

  Output: <repo>/docs/screenshots/<lang>/*.png

  -Planets still takes the old per-planet HUD surface shots (surface_<key>.png, one run per type via
  -planet) when a list is given; by default none are taken.

.EXAMPLE
  ./scripts/capture-screenshots.ps1                # the main sequence, both languages
  ./scripts/capture-screenshots.ps1 -Lang de       # German only
  ./scripts/capture-screenshots.ps1 -Build         # build the client first, then capture
  ./scripts/capture-screenshots.ps1 -SkipMain -Planets lava,ocean -Lang en  # only two HUD surface shots
#>
param(
    [ValidateSet("both", "de", "en")]
    [string] $Lang = "both",
    [string] $Exe,
    [string] $OutRoot,
    [long]   $Seed = 424242,
    [string[]] $Planets = @(),
    [switch] $SkipMain,
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

# Runs the built player once with the -captureShots flag (+ any extra args) and waits for it to finish.
function Invoke-Capture {
    param([string] $OutDir, [string[]] $ExtraArgs, [string] $Label)

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $args = @(
        "-captureShots",
        "-shotOut", $OutDir,
        "-seed", $Seed,
        "-screen-width", "1920",
        "-screen-height", "1080",
        "-screen-fullscreen", "0"
    ) + $ExtraArgs

    $proc = Start-Process -FilePath $Exe -ArgumentList $args -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        Write-Warning "Capture run ($Label) exited with code $($proc.ExitCode) — check the player log."
    }
}

foreach ($l in $langs) {
    $out = Join-Path $OutRoot $l

    if (-not $SkipMain) {
        # Start the main sequence from a FRESH world every time: since #401 a quit saves the live player
        # position, so a reused save resumes wherever the last run left the player (on foot outside) — and the
        # cockpit shots / EnterSpace assume the fresh-world spawn at the helm. Only the main world is deleted;
        # the per-planet saves (MarketingShots_<type>) keep their hand-picked framings and are resume-safe.
        $mainSave = Join-Path $env:USERPROFILE "AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\singleplayer-saves\MarketingShots"
        if (Test-Path $mainSave) {
            Remove-Item $mainSave -Recurse -Force
            Write-Host "Deleted main-sequence save (fresh cockpit spawn) → $mainSave" -ForegroundColor DarkGray
        }

        Write-Host "Capturing main sequence '$l' → $out" -ForegroundColor Cyan
        Invoke-Capture -OutDir $out -ExtraArgs @("-lang", $l) -Label "main/$l"
    }

    # One run per planet type → surface_<key>.png, showing the world variety.
    foreach ($p in $Planets) {
        Write-Host "Capturing surface '$p' ('$l') → $out" -ForegroundColor Cyan
        Invoke-Capture -OutDir $out -ExtraArgs @("-lang", $l, "-planet", $p) -Label "surface:$p/$l"
    }

    $shots = @(Get-ChildItem -Path $out -Filter *.png -ErrorAction SilentlyContinue)
    Write-Host "  → $($shots.Count) PNG(s) in $out" -ForegroundColor Green
}

Write-Host "Done. Screenshots under $OutRoot" -ForegroundColor Green
