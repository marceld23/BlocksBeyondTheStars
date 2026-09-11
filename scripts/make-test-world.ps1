<#
.SYNOPSIS
  Creates a ready-to-play singleplayer world that STARTS on a chosen planet type (e.g. the rainbow planet).

.DESCRIPTION
  A playtest helper: the game's own create-world panel never lets you pick the planet you spawn on, but the
  server has always understood --start-planet. This script runs the client's bundled dedicated server ONCE
  with exactly the arguments the in-game launcher uses (LocalServerLauncher.cs) plus --start-planet <type>,
  lets it create the save, and stops it gracefully (the same stdin stop the client uses). The new world then
  shows up in the Singleplayer world picker like any other — pick it, type your name, and you land on that
  planet. Nothing in the game changes; the save is an ordinary save.

  The world is created with the SAME bundled server and data the client plays with, so the client has to
  know the planet type: the school club planets (rainbow_sea, flower_fields, scrapyard, gamer_hills) need a
  build with terrain generation 5. The client is looked up in this order unless -Client is given:
    1. client/Build/Windows (a local build from scripts/build-client.ps1)
    2. %LOCALAPPDATA%\BlocksBeyondTheStarsDev\current (a dev installer)
    3. %LOCALAPPDATA%\BlocksBeyondTheStars\current (the installed release)
  Saves go where that client reads them: next to the exe when a portable_data_dir.txt marker exists, else
  %USERPROFILE%\AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars\singleplayer-saves.

  The world name defaults to the planet's display name in -Locale (default: German, e.g. "Regenbogenplanet").
  The seed derives from the world name unless -Seed is given, so the same name always gives the same world.

.PARAMETER Planet
  One or more planet type keys from data/planets.json (see -List). Default: rainbow_sea.
.PARAMETER World
  World name (folder + picker entry). Only with a single -Planet; defaults to the planet's display name.
.PARAMETER Client
  Folder holding BlocksBeyondTheStars.exe. Default: see DESCRIPTION.
.PARAMETER SavesRoot
  Override the singleplayer-saves folder (e.g. to inspect a save without touching the real ones).
.PARAMETER Seed
  World seed; 0 = derive from the world name (the game's default).
.PARAMETER Locale
  Locale for the default world names (a file in StreamingAssets/data/locales). Default: de.
.PARAMETER Port
  Loopback port for the throwaway server run; only matters if something else is listening there.
.PARAMETER Peaceful
  Apply the "Peaceful" preset of the create-world panel (frequent creatures, no enemies, no bandits,
  light hazards, no death penalty). Default: the "Standard" preset.
.PARAMETER Sandbox
  Creative game mode (free crafting, no oxygen/hunger, peaceful) — handy for placing the new blocks.
.PARAMETER Force
  Replace an existing world of the same name (deletes its folder first).
.PARAMETER List
  Print the planet types the chosen client knows, with display names, and exit.
.PARAMETER ShowServerLog
  Echo the server's log lines while the world is being created.
.PARAMETER Launch
  Start the SAME client afterwards, so the world is opened by a build that knows the planet. Opening a
  save with an older client is not harmless: its server does not know the type, adopts another planet as
  the start and SAVES that — the world is then permanently a different planet (recreate it with -Force).

.EXAMPLE
  ./scripts/make-test-world.ps1
  # → world "Regenbogenplanet" starting on rainbow_sea

.EXAMPLE
  ./scripts/make-test-world.ps1 -Planet rainbow_sea,flower_fields,scrapyard,gamer_hills,glacier -Peaceful
  # → the four school club planets plus an ice world (Leni), all peaceful

.EXAMPLE
  ./scripts/make-test-world.ps1 -Planet flower_fields -World "Damians Welt" -Sandbox -Force

.EXAMPLE
  ./scripts/make-test-world.ps1 -List
#>
[CmdletBinding()]
param(
    [string[]] $Planet = @('rainbow_sea'),
    [string] $World = '',
    [string] $Client = '',
    [string] $SavesRoot = '',
    [long] $Seed = 0,
    [string] $Locale = 'de',
    [int] $Port = 31577,
    [switch] $Peaceful,
    [switch] $Sandbox,
    [switch] $Force,
    [switch] $List,
    [switch] $ShowServerLog,
    [switch] $Launch
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------- client, server, data, saves
function Resolve-ClientDir {
    if ($Client) {
        $dir = (Resolve-Path $Client).Path
        if (-not (Test-Path (Join-Path $dir 'BlocksBeyondTheStars.exe'))) {
            throw "No BlocksBeyondTheStars.exe in '$dir'."
        }
        return $dir
    }

    $candidates = @(
        (Join-Path $repo 'client/Build/Windows'),
        (Join-Path $env:LOCALAPPDATA 'BlocksBeyondTheStarsDev/current'),
        (Join-Path $env:LOCALAPPDATA 'BlocksBeyondTheStars/current')
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'BlocksBeyondTheStars.exe')) {
            return (Resolve-Path $c).Path
        }
    }

    throw "No client found. Build one (scripts/build-client.ps1), install the game, or pass -Client <folder>."
}

function Resolve-DataRoot([string] $clientDir) {
    # Mirrors AppPaths.Root: the portable marker next to the exe redirects everything, else Unity's
    # persistentDataPath (%USERPROFILE%\AppData\LocalLow\<company>\<product>).
    $marker = Join-Path $clientDir 'portable_data_dir.txt'
    if (Test-Path $marker) {
        $target = (Get-Content $marker -Raw).Trim().TrimStart([char]0xFEFF).Trim().Trim('"')
        if ($target) {
            if (-not [IO.Path]::IsPathRooted($target)) {
                $target = Join-Path $clientDir $target
            }
            return [IO.Path]::GetFullPath($target)
        }
    }

    return Join-Path $env:USERPROFILE 'AppData\LocalLow\JuMaVe Games\Blocks Beyond the Stars'
}

$clientDir = Resolve-ClientDir
$streaming = Join-Path $clientDir 'BlocksBeyondTheStars_Data/StreamingAssets'
$serverExe = Join-Path $streaming 'server/BlocksBeyondTheStars.GameServer.exe'
$dataDir = Join-Path $streaming 'data'
if (-not (Test-Path $serverExe)) {
    throw "The client in '$clientDir' has no bundled server ($serverExe). Run scripts/publish-local-server.ps1 before building."
}
if (-not (Test-Path (Join-Path $dataDir 'planets.json'))) {
    throw "The client in '$clientDir' has no content folder ($dataDir)."
}

$dataRoot = Resolve-DataRoot $clientDir
$savesDir = if ($SavesRoot) { [IO.Path]::GetFullPath($SavesRoot) } else { Join-Path $dataRoot 'singleplayer-saves' }
$userContentDir = Join-Path $dataRoot 'usercontent'

# ---------------------------------------------------------------- planet catalog + display names
$planets = Get-Content (Join-Path $dataDir 'planets.json') -Raw | ConvertFrom-Json
$byKey = @{}
foreach ($p in $planets) { $byKey[$p.key] = $p }

$names = @{}
$localeFile = Join-Path $dataDir "locales/$Locale.json"
if (Test-Path $localeFile) {
    $loc = Get-Content $localeFile -Raw | ConvertFrom-Json -AsHashtable
    foreach ($p in $planets) {
        if ($p.nameKey -and $loc.ContainsKey($p.nameKey)) { $names[$p.key] = $loc[$p.nameKey] }
    }
}
else {
    Write-Warning "No locale '$Locale' in $dataDir/locales — world names fall back to the planet keys."
}

if ($List) {
    Write-Host "Planet types known to $clientDir" -ForegroundColor Cyan
    $planets |
        Where-Object { $_.key -notin @('orbital_station', 'ship_interior') } |
        ForEach-Object {
            [pscustomobject]@{
                Key        = $_.key
                Name       = if ($names.ContainsKey($_.key)) { $names[$_.key] } else { '' }
                Generation = if ($null -ne $_.minTerrainGeneration) { [int]$_.minTerrainGeneration } else { 0 }
                Exotic     = [bool]$_.exotic
            }
        } | Format-Table -AutoSize
    return
}

if ($World -and $Planet.Count -ne 1) {
    throw "-World names one world; pass a single -Planet with it."
}

foreach ($key in $Planet) {
    if (-not $byKey.ContainsKey($key)) {
        throw "Planet type '$key' is unknown to the client in '$clientDir' (a school club planet needs a build with generation 5). See -List."
    }
}

# ---------------------------------------------------------------- server arguments (= LocalServerLauncher + the create panel)
function Get-ServerArgs([string] $worldName, [string] $planetKey) {
    $a = [System.Collections.Generic.List[string]]::new()
    $a.AddRange([string[]]@(
        '--port', "$Port", '--name', 'Singleplayer', '--world', $worldName, '--max-players', '1',
        '--saves', $savesDir, '--data', $dataDir, '--usercontent', $userContentDir, '--stdin-stop', 'true',
        # LocalServerLauncher's fixed singleplayer args
        '--free-flight', 'true', '--space-combat', 'PvE', '--space-npcs', 'Normal', '--voice', 'true',
        '--guarantee-start-cube', 'true', '--admin-cheats', 'true', '--no-config', 'true'
    ))
    if ($Seed -ne 0) { $a.AddRange([string[]]@('--seed', "$Seed")) }
    if ($Sandbox) { $a.AddRange([string[]]@('--game-mode', 'Creative')) }
    if ($Peaceful) {
        # WorldCreationOptions.Peaceful(): frequent creatures, no planet enemies / space NPCs / UFOs / bandits,
        # light hazards, no death penalty, space combat off.
        $a.AddRange([string[]]@('--creatures', 'Frequent', '--planet-enemies', 'Off', '--space-npcs', 'Off',
                '--bandits', 'Off', '--hazards', 'Light', '--death-penalty', 'None'))
    }
    else {
        # WorldCreationOptions.Standard(): the panel's SpaceCombat toggle is on → fightable space enemies.
        $a.AddRange([string[]]@('--space-combat', 'PvE', '--ship-weapons', 'NpcsOnly'))
    }
    $a.AddRange([string[]]@('--start-planet', $planetKey))
    return $a
}

function Get-WorldFolder([string] $worldName) {
    # SaveGamePaths.Sanitize: invalid file-name characters become '_'.
    $safe = $worldName
    foreach ($c in [IO.Path]::GetInvalidFileNameChars()) { $safe = $safe.Replace($c, '_') }
    return Join-Path $savesDir $safe
}

# ---------------------------------------------------------------- one throwaway server run per world
function New-TestWorld([string] $worldName, [string] $planetKey) {
    $folder = Get-WorldFolder $worldName
    if (Test-Path (Join-Path $folder 'world.db')) {
        if (-not $Force) {
            Write-Host "  skip   '$worldName' already exists ($folder) — pass -Force to replace it." -ForegroundColor Yellow
            return $false
        }
        Remove-Item $folder -Recurse -Force
    }
    New-Item -ItemType Directory -Force $savesDir | Out-Null
    New-Item -ItemType Directory -Force $userContentDir | Out-Null

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $serverExe
    foreach ($arg in (Get-ServerArgs $worldName $planetKey)) { $psi.ArgumentList.Add($arg) }
    $psi.WorkingDirectory = Split-Path -Parent $serverExe
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    Write-Host "  create '$worldName' on $planetKey ..." -ForegroundColor Cyan
    $proc = [System.Diagnostics.Process]::Start($psi)
    $proc.BeginErrorReadLine()   # drain stderr so a chatty server can never block on a full pipe

    $tail = [System.Collections.Generic.Queue[string]]::new()
    $started = $false
    $deadline = (Get-Date).AddSeconds(180)
    while (-not $started) {
        $read = $proc.StandardOutput.ReadLineAsync()
        while (-not $read.Wait(250)) {
            if ((Get-Date) -gt $deadline) { break }
        }
        if (-not $read.IsCompleted) { break }              # timed out
        $line = $read.Result
        if ($null -eq $line) { break }                     # stdout closed: the server exited
        if ($tail.Count -ge 30) { [void]$tail.Dequeue() }
        $tail.Enqueue($line)
        if ($ShowServerLog) { Write-Host "         | $line" -ForegroundColor DarkGray }
        # GameServer.Start's last line: "Server '…' started on port N, world '…' (seed S, planet P)."
        if ($line -match "started on port \d+, world '.*' \(seed (-?\d+), planet ([^)]+)\)") {
            $started = $true
            $seedUsed = $Matches[1]; $planetUsed = $Matches[2]
        }
    }

    if (-not $started) {
        try { $proc.Kill($true) } catch { }
        Write-Host "  FAILED '$worldName': the server never reported the world as started. Last log lines:" -ForegroundColor Red
        foreach ($l in $tail) { Write-Host "         | $l" -ForegroundColor DarkGray }
        return $false
    }

    # Graceful stop = what the client does on quit: "stop" on stdin, then close it. The server drains, saves
    # and exits; keep reading stdout until it does so the pipe never fills.
    try { $proc.StandardInput.WriteLine('stop'); $proc.StandardInput.Close() } catch { }
    while ($null -ne ($line = $proc.StandardOutput.ReadLine())) {
        if ($ShowServerLog) { Write-Host "         | $line" -ForegroundColor DarkGray }
    }
    if (-not $proc.WaitForExit(60000)) {
        Write-Warning "  '$worldName': the server did not exit within 60 s — killing it."
        try { $proc.Kill($true) } catch { }
    }

    if (-not (Test-Path (Join-Path $folder 'world.db'))) {
        Write-Host "  FAILED '$worldName': no world.db in $folder after the run." -ForegroundColor Red
        return $false
    }

    Write-Host "  ok     '$worldName' → $folder (planet $planetUsed, seed $seedUsed)" -ForegroundColor Green
    return $true
}

Write-Host "Client : $clientDir"
Write-Host "Saves  : $savesDir"
$preset = if ($Peaceful) { 'Peaceful' } else { 'Standard' }
if ($Sandbox) { $preset += ' + Creative (sandbox)' }
Write-Host "Preset : $preset"

$made = 0
foreach ($key in $Planet) {
    $name = if ($World) { $World } elseif ($names.ContainsKey($key)) { $names[$key] } else { $key }
    if (New-TestWorld $name $key) { $made++ }
}

Write-Host ""
if ($made -gt 0) {
    Write-Host "$made world(s) ready. Start the game → Singleplayer → pick the world from the list; you spawn on that planet." -ForegroundColor Green
    Write-Host "Open them ONLY with this client ($clientDir) or a newer one: an older client's server does not know" -ForegroundColor Yellow
    Write-Host "the planet, picks another start planet and saves that — the world would keep the wrong planet for good." -ForegroundColor Yellow
}
else {
    Write-Host "No world was created." -ForegroundColor Yellow
}

if ($Launch) {
    $game = Join-Path $clientDir 'BlocksBeyondTheStars.exe'
    Write-Host "Starting $game ..." -ForegroundColor Cyan
    Start-Process -FilePath $game -WorkingDirectory $clientDir | Out-Null
}
