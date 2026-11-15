<#
.SYNOPSIS
  Takes the game's poster (#3706) with the built player and, with -Place, puts each picture where it is used.

.DESCRIPTION
  Runs the player once with -capturePoster: it waits for the main menu, frames the menu backdrop as a poster (the corvette
  before the "varied" world, full detail on the High preset — the player's saved settings are not touched), hides the UI,
  photographs the frame and stamps the block logo on it, then quits. No AI anywhere: the poster is the game's own picture.

  Written to <OutDir> (default artifacts/poster):
      poster.jpg           1920×1080, logo   — key art (README, stores, devblog)
      poster.png           1920×1080, logo   — lossless master (archive only)
      poster-nologo.png    1920×1080         — lossless master without the logo (archive only)
      launcher-splash.jpg  1120×640,  logo   — Windows + Linux launcher splash and the installer image
      bbs-poster.jpg       1600×900          — browser loading page background
      bbs-logo.png         the logo alone    — browser loading page title

  -Place copies the files the builds use:
      launcher-splash.jpg → client/Branding/launcher-splash.jpg       (compiled into both launchers at build time)
      poster.jpg          → client/Branding/poster.jpg                (key art; link it from README/stores as wanted)
      bbs-poster.jpg      → client/Assets/WebGLTemplates/BlocksBeyondTheStars/TemplateData/bbs-poster.jpg
      bbs-logo.png        → client/Assets/WebGLTemplates/BlocksBeyondTheStars/TemplateData/bbs-logo.png
  Unity creates the .meta files of the two template pictures the next time the Editor opens the project — commit them too.

  Requires a current player build (scripts/build-client.ps1) and a machine with a GPU; the window must fit 1920×1080
  (a smaller screen still works: the frame is scaled to the poster size).

.EXAMPLE
  ./scripts/capture-poster.ps1              # capture into artifacts/poster, then look at the pictures
  ./scripts/capture-poster.ps1 -Place       # capture and copy the used pictures into the repository
  ./scripts/capture-poster.ps1 -PlaceOnly   # copy an earlier capture from artifacts/poster
#>
param(
    [string] $Exe,
    [string] $OutDir,
    [switch] $Place,
    [switch] $PlaceOnly,
    [switch] $Build
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe)    { $Exe = Join-Path $repo "client/Build/Windows/BlocksBeyondTheStars.exe" }
if (-not $OutDir) { $OutDir = Join-Path $repo "artifacts/poster" }
$posterDir = Join-Path $OutDir "poster"

if (-not $PlaceOnly) {
    if ($Build) {
        & (Join-Path $PSScriptRoot "build-client.ps1")
        if ($LASTEXITCODE -ne 0) { throw "build-client.ps1 failed (exit $LASTEXITCODE)." }
    }

    if (-not (Test-Path $Exe)) { throw "Player build not found at '$Exe'. Run scripts/build-client.ps1 first (or pass -Build)." }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $runArgs = @(
        "-capturePoster",
        "-shotOut", $OutDir,
        "-lang", "en",
        "-screen-width", "1920",
        "-screen-height", "1080",
        "-screen-fullscreen", "0"
    )
    Write-Host "Capturing the poster with $Exe ..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $Exe -ArgumentList $runArgs -PassThru -Wait
    $playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\Player.log"
    if (Test-Path $playerLog) { Copy-Item $playerLog (Join-Path $OutDir "poster-run.log") -Force }
    if ($proc.ExitCode -ne 0) { Write-Warning "The player exited with code $($proc.ExitCode) — see $(Join-Path $OutDir 'poster-run.log')." }
}

$files = "poster.jpg", "poster.png", "poster-nologo.png", "launcher-splash.jpg", "bbs-poster.jpg", "bbs-logo.png"
foreach ($f in $files) {
    $p = Join-Path $posterDir $f
    if (Test-Path $p) {
        Write-Host ("  {0,-22} {1,8:N0} KB" -f $f, ((Get-Item $p).Length / 1KB)) -ForegroundColor Green
    } else {
        Write-Warning "  $f is missing in $posterDir"
    }
}

if (-not ($Place -or $PlaceOnly)) {
    Write-Host "Done. Look at the pictures in $posterDir, then run again with -PlaceOnly to put them in place." -ForegroundColor Green
    return
}

$template = Join-Path $repo "client/Assets/WebGLTemplates/BlocksBeyondTheStars/TemplateData"
$branding = Join-Path $repo "client/Branding"
New-Item -ItemType Directory -Force -Path $branding | Out-Null
$targets = [ordered]@{
    "launcher-splash.jpg" = Join-Path $branding "launcher-splash.jpg"
    "poster.jpg"          = Join-Path $branding "poster.jpg"
    "bbs-poster.jpg"      = Join-Path $template "bbs-poster.jpg"
    "bbs-logo.png"        = Join-Path $template "bbs-logo.png"
}
foreach ($entry in $targets.GetEnumerator()) {
    $src = Join-Path $posterDir $entry.Key
    if (-not (Test-Path $src)) { Write-Warning "  skipped $($entry.Key) (not captured)"; continue }
    Copy-Item $src $entry.Value -Force
    Write-Host "  $($entry.Key) → $($entry.Value)" -ForegroundColor Green
}

Write-Host "Placed. Rebuild the launchers (scripts/build-client.ps1) to compile the new splash in; open the Unity project once so it" -ForegroundColor Green
Write-Host "creates the .meta files of the two template pictures, and commit them with the pictures." -ForegroundColor Green
