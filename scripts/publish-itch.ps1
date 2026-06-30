<#
.SYNOPSIS
  Publishes the packaged Windows installers (Setup.exe, MSI, Portable zip) to itch.io via butler.

.DESCRIPTION
  Pushes the artifacts produced by scripts/publish-client-installer.ps1 (in artifacts/installer) to the
  itch.io page jumavegames/blocks-beyond-the-stars, one artifact per channel:

    *Setup.exe     -> windows-setup     (per-user Velopack installer)
    *.msi          -> windows-msi       (machine-wide WiX installer)
    *Portable.zip  -> windows-portable  (no-install portable copy)

  Each push is stamped with --userversion so itch.io shows the release version. butler reads the itch.io
  API key from the BUTLER_API_KEY environment variable (set it locally, or as the BUTLER_API_KEY repo
  secret in CI). butler is auto-downloaded (win-x64) into artifacts/butler if it is not already on PATH.

.PARAMETER Version
  SemVer for this release (the itch userversion). Default: read from PlayerSettings.bundleVersion in
  ProjectSettings.asset (the single source of truth the release CI sets from the git tag).

.PARAMETER InstallerDir
  Folder holding the packed installers. Default: artifacts/installer (publish-client-installer.ps1's output).

.PARAMETER Target
  The itch.io target (<user>/<game>). Default: jumavegames/blocks-beyond-the-stars.

.PARAMETER PushTimeoutSec
  Hard wall-clock limit for a single `butler push`. A push that exceeds it is killed (process tree) and
  retried once; a second timeout fails the script. Guards against `butler push` hanging on an itch.io
  processing/network stall — that once ran for ~6 h until GitHub's job limit cancelled it, leaving the
  itch.io mirror half-pushed. Default: 900 (15 min) — uploads take seconds; this only bounds the wait.

.EXAMPLE
  $env:BUTLER_API_KEY = '...'; ./scripts/publish-itch.ps1 -Version 0.3.0
#>
param(
    [string] $Version = '',
    [string] $InstallerDir = 'artifacts/installer',
    [string] $Target = 'jumavegames/blocks-beyond-the-stars',
    [int]    $PushTimeoutSec = 900
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $repo $InstallerDir

if ([string]::IsNullOrWhiteSpace($env:BUTLER_API_KEY)) {
    Write-Error 'BUTLER_API_KEY is not set. Export your itch.io API key (itch.io -> Settings -> API keys) before running.'
}
if (-not (Test-Path $dir)) {
    Write-Error "Installer folder not found at '$dir'. Run ./scripts/publish-client-installer.ps1 first."
}

# Derive the version unless one was given — same single source of truth as publish-client-installer.ps1.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $projSettings = Get-Content (Join-Path $repo 'client/ProjectSettings/ProjectSettings.asset') -Raw
    if ($projSettings -match '(?m)^\s*bundleVersion:\s*(\S+)') { $Version = $Matches[1] }
    else { Write-Error 'Could not read bundleVersion from ProjectSettings.asset; pass -Version explicitly.' }
}

# Ensure butler is available; download the win-x64 build into a tool cache if it is not already on PATH.
$butler = (Get-Command butler -ErrorAction SilentlyContinue).Source
if (-not $butler) {
    $toolDir = Join-Path $repo 'artifacts/butler'
    $butler = Join-Path $toolDir 'butler.exe'
    if (-not (Test-Path $butler)) {
        Write-Host 'Downloading butler (itch.io CLI, win-x64) ...' -ForegroundColor Cyan
        New-Item -ItemType Directory -Force $toolDir | Out-Null
        $zip = Join-Path $toolDir 'butler.zip'
        Invoke-WebRequest -Uri 'https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default' -OutFile $zip
        Expand-Archive -Path $zip -DestinationPath $toolDir -Force
        Remove-Item $zip -Force
    }
}
& $butler version
if ($LASTEXITCODE -ne 0) { Write-Error 'butler is not runnable.' }

# Run one `butler push` under a wall-clock timeout. Returns $true on a clean (exit 0) push; $false on a
# non-zero exit OR a timeout (after killing the whole process tree so no orphaned butler lingers). Uses
# ProcessStartInfo.ArgumentList (PS7/.NET) so paths with spaces are quoted correctly, and inherits stdout
# so butler's progress still streams to the log.
function Invoke-ButlerPush {
    param([string] $Butler, [string] $File, [string] $Channel, [string] $Version, [int] $TimeoutSec)

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Butler
    foreach ($a in @('push', $File, $Channel, '--userversion', $Version)) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false

    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        Write-Host "butler push exceeded ${TimeoutSec}s — killing the process tree." -ForegroundColor Yellow
        try { $proc.Kill($true) } catch { }
        try { $proc.WaitForExit(10000) | Out-Null } catch { }
        return $false
    }
    return ($proc.ExitCode -eq 0)
}

# Map each packed artifact to its itch.io channel. The "windows" in each channel name tags it as a Windows
# download on the itch.io page.
$pushes = @(
    @{ Channel = 'windows-setup';    Filter = '*Setup.exe' },
    @{ Channel = 'windows-msi';      Filter = '*.msi' },
    @{ Channel = 'windows-portable'; Filter = '*Portable.zip' }
)

foreach ($p in $pushes) {
    $file = Get-ChildItem $dir -Filter $p.Filter -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $file) { Write-Error "No artifact matching '$($p.Filter)' in '$dir'." }
    $channel = "${Target}:$($p.Channel)"
    Write-Host "Pushing $($file.Name) -> $channel (v$Version)" -ForegroundColor Cyan

    # One push, with a single retry — a butler push is resumable (it diffs against the last build), so
    # retrying after a transient stall is cheap and safe.
    if (-not (Invoke-ButlerPush -Butler $butler -File $file.FullName -Channel $channel -Version $Version -TimeoutSec $PushTimeoutSec)) {
        Write-Host "Retrying push of $($file.Name) once ..." -ForegroundColor Yellow
        if (-not (Invoke-ButlerPush -Butler $butler -File $file.FullName -Channel $channel -Version $Version -TimeoutSec $PushTimeoutSec)) {
            Write-Error "butler push failed for $($file.Name) (non-zero exit or timed out twice)."
        }
    }
}

Write-Host "All installers pushed to itch.io ($Target)." -ForegroundColor Green
