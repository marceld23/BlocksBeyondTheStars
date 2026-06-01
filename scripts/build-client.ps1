<#
.SYNOPSIS
  Builds the Unity client as a Windows player (.exe) via the editor in batch mode (M28).

.DESCRIPTION
  Runs the bundled BuildScript.BuildWindows editor method headless. It generates the launcher
  scene if needed and writes the player to the output folder. The two prerequisites — syncing the
  shared libs/content and publishing the bundled server — now run automatically, so a
  self-contained singleplayer .exe is a single command:

      ./scripts/build-client.ps1

  Use -SkipPrereqs to rebuild without re-syncing/re-publishing. Everything under
  client/Assets/StreamingAssets (data + the published server) is included in the build.

.PARAMETER UnityPath
  Path to Unity.exe. Defaults to the Unity Hub install of the project's editor version.

.PARAMETER Out
  Output folder for the build (relative to the client project). Default: Build/Windows.

.EXAMPLE
  ./scripts/build-client.ps1
  ./scripts/build-client.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe"
#>
param(
    [string] $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe",
    [string] $Out = "Build/Windows",
    [switch] $SkipPrereqs   # skip the sync-libs + publish-server steps (e.g. when re-building only)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'client'

# Self-contained singleplayer build needs the synced shared libs/content + the bundled server.
if (-not $SkipPrereqs) {
    Write-Host "Prerequisites: syncing shared libs/content + publishing the bundled server..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'sync-client-libs.ps1')
    & (Join-Path $PSScriptRoot 'publish-local-server.ps1')
}

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity editor not found at '$UnityPath'. Pass -UnityPath to your Unity 6000.4.x Unity.exe."
}

$log = Join-Path $project 'build.log'
Write-Host "Building Windows player (this can take a few minutes)..." -ForegroundColor Cyan

& $UnityPath -batchmode -quit -nographics `
    -projectPath $project `
    -executeMethod Spacecraft.Client.EditorTools.BuildScript.BuildWindows `
    -spacecraftOut $Out `
    -logFile $log
$unityExit = $LASTEXITCODE

# Don't trust the exit code alone: a script-compile failure can still exit 0 and skip the build.
# Treat it as success only if the editor logged the BuildScript success marker and no compile errors.
$succeeded   = (Test-Path $log) -and (Select-String -Path $log -Pattern 'Spacecraft build: Succeeded' -Quiet)
$compileFail = (Test-Path $log) -and (Select-String -Path $log -Pattern 'Scripts have compiler errors' -Quiet)

if ($unityExit -ne 0 -or $compileFail -or -not $succeeded) {
    $why = if ($compileFail) { 'script compile errors' } elseif (-not $succeeded) { 'no success marker in log' } else { "unity exit $unityExit" }
    Write-Error "Build FAILED ($why). See $log"
}

Write-Host "Build complete → $(Join-Path $project $Out)\Spacecraft.exe" -ForegroundColor Green
