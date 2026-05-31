<#
.SYNOPSIS
  Builds the Unity client as a Windows player (.exe) via the editor in batch mode (M28).

.DESCRIPTION
  Runs the bundled BuildScript.BuildWindows editor method headless. It generates the launcher
  scene if needed and writes the player to the output folder. For a self-contained singleplayer
  build, sync the shared content and bundle the server first:

      ./scripts/sync-client-libs.ps1
      ./scripts/publish-local-server.ps1
      ./scripts/build-client.ps1

  Everything under client/Assets/StreamingAssets (data + the published server) is included in
  the build automatically.

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
    [string] $Out = "Build/Windows"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'client'

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

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit $LASTEXITCODE). See $log"
}

Write-Host "Build complete → $(Join-Path $project $Out)\Spacecraft.exe" -ForegroundColor Green
