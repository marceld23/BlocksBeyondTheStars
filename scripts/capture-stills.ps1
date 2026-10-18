<#
.SYNOPSIS
  Captures the HUD-free world stills (planet gallery, creatures, villages, giants) from the built game player.

.DESCRIPTION
  Each entry of the shot manifest (default scripts/shot-manifests/marketing.json) is a one-frame clip for the
  in-game ClipDirector (-captureClip): a planet type, a pose (usually "vista", the best view around the spawn),
  a still camera ("rig" above and behind the player, or "subject" framing a creature or a giant), a pinned time
  of day and weather. The run never shows the HUD, the first-person hand or the block selection frame (#2765).

  A world seed decides how a motif turns out, so every entry lists several seeds ("seeds"). This script runs the
  player once per entry and seed and collects the frames as

      <OutRoot>/<name>/<name>__<seed>.png   (+ <name>__<seed>.log, that run's Player.log)

  then builds one contact sheet per entry (<OutRoot>/_sheets/<name>.jpg, via tools/screenshots/stills.py) to pick
  from. Write the chosen seed into the entry's "pick" and run with -Publish: the picks are written as JPEGs to
  docs/screenshots/world/<name>.jpg. Stills carry no text, so there is one set for every language.

  Requires a current player build (scripts/build-client.ps1) — it bundles the local server singleplayer needs —
  and a machine with a GPU. Do not run it while a Unity build or a test run loads the machine: the client then
  lifts its spawn freeze before the chunks are in and the pose search finds nothing.

.EXAMPLE
  ./scripts/capture-stills.ps1                                   # every entry, every seed, then the contact sheets
  ./scripts/capture-stills.ps1 -Shots world_lava,creature_savanna
  ./scripts/capture-stills.ps1 -Shots world_lava -Seeds 2468     # one entry, one seed
  ./scripts/capture-stills.ps1 -Publish                          # only copy the picks into docs/screenshots/world
#>
param(
    [string]   $Exe,
    [string]   $Manifest,
    [string]   $OutRoot,
    [string[]] $Shots,             # only these entries (default: all)
    [long[]]   $Seeds,             # override the entries' seed lists
    [ValidateSet("Potato", "Low", "Medium", "High")]
    [string]   $Preset = "High",
    [switch]   $Build,
    [switch]   $Publish,           # skip capturing; write the manifest's picks to docs/screenshots/world
    [switch]   $NoSheets
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe)      { $Exe = Join-Path $repo "client/Build/Windows/BlocksBeyondTheStars.exe" }
if (-not $Manifest) { $Manifest = Join-Path $repo "scripts/shot-manifests/marketing.json" }
if (-not $OutRoot)  { $OutRoot = Join-Path $repo "marketing/stills" }
$tool = Join-Path $repo "tools/screenshots/stills.py"

if (-not (Test-Path $Manifest)) { throw "Shot manifest not found at '$Manifest'." }

if ($Publish) {
    & uv run $tool publish --manifest $Manifest --stills $OutRoot --out (Join-Path $repo "docs/screenshots/world")
    if ($LASTEXITCODE -ne 0) { throw "stills.py publish failed (exit $LASTEXITCODE)." }
    return
}

if ($Build) {
    Write-Host "Building the client first..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build-client.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-client.ps1 failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $Exe)) { throw "Player build not found at '$Exe'. Run scripts/build-client.ps1 first (or pass -Build)." }

$spec = Get-Content $Manifest -Raw | ConvertFrom-Json
$entries = $spec.clips
if ($Shots) { $entries = $entries | Where-Object { $Shots -contains $_.name } }
if (-not $entries) { throw "No matching entries in '$Manifest'." }

$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\Player.log"
$scratch = Join-Path $OutRoot "_run"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

$total = 0
foreach ($e in $entries) {
    $seedList = if ($Seeds) { $Seeds } elseif ($e.seeds) { @($e.seeds) } else { @(424242) }
    $total += $seedList.Count
}

$n = 0
foreach ($e in $entries) {
    $name = $e.name
    $dir = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $seedList = if ($Seeds) { $Seeds } elseif ($e.seeds) { @($e.seeds) } else { @(424242) }

    foreach ($seed in $seedList) {
        $n++
        Write-Host "[$n/$total] $name (seed $seed)" -ForegroundColor Cyan
        $frame = Join-Path $scratch "$name/frames/frame_00001.png"
        if (Test-Path $frame) { Remove-Item $frame -Force }

        $runArgs = @(
            "-captureClip",
            "-clipManifest", $Manifest,
            "-clipName", $name,
            "-clipOut", $scratch,
            "-lang", "en",
            "-seed", $seed,
            "-preset", $Preset,
            "-screen-width", "1920",
            "-screen-height", "1080",
            "-screen-fullscreen", "0"
        )
        $started = Get-Date
        $proc = Start-Process -FilePath $Exe -ArgumentList $runArgs -PassThru -Wait
        $secs = [int]((Get-Date) - $started).TotalSeconds
        if ($proc.ExitCode -ne 0) {
            Write-Warning "  run exited with code $($proc.ExitCode) after $secs s — see $name`__$seed.log"
        }

        if (Test-Path $playerLog) { Copy-Item $playerLog (Join-Path $dir "$name`__$seed.log") -Force }
        if (Test-Path $frame) {
            Copy-Item $frame (Join-Path $dir "$name`__$seed.png") -Force
            Write-Host "  → $name`__$seed.png ($secs s)" -ForegroundColor Green
        } else {
            Write-Warning "  no frame written ($secs s) — see $name`__$seed.log"
        }
    }
}

if (-not $NoSheets) {
    & uv run $tool sheets --stills $OutRoot
    if ($LASTEXITCODE -ne 0) { Write-Warning "stills.py sheets failed (exit $LASTEXITCODE)." }
}

Write-Host "Done. Stills under $OutRoot (contact sheets in _sheets/). Set each entry's ""pick"" to the chosen seed, then run with -Publish." -ForegroundColor Green
