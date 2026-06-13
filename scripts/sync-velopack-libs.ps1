<#
.SYNOPSIS
  Copies the Velopack runtime assemblies into the Unity client so the client can self-update.

.DESCRIPTION
  The client's in-app updater (ClientUpdater.cs) references the Velopack runtime. Unlike the shared
  game libraries (handled by sync-client-libs.ps1), Velopack is a client-only dependency, so it lives
  in its own sync step. This restores the pinned Velopack package via a throwaway project, then copies
  every produced DLL into client/Assets/Plugins — but ONLY if a file of that name is not already there,
  so it never clobbers the shared libs or the System.* facades Unity already ships / sync-client-libs
  placed (System.Buffers/Memory/Numerics.Vectors/Runtime.CompilerServices.Unsafe).

  Run this once before the first client build that includes updates, and again only when bumping the
  Velopack version. After running, refresh the Unity Editor so it imports the new DLLs (.meta files are
  generated on import). If Unity reports a duplicate of an assembly it already provides, delete that one
  DLL from Plugins (same rule as sync-client-libs.ps1).

.PARAMETER Version
  Velopack package version to vendor. Keep this in lockstep with the vpk CLI version used by
  publish-client-installer.ps1.
#>
param(
    [string] $Version = '1.2.0'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$plugins = Join-Path $repo 'client/Assets/Plugins'
$temp = Join-Path $repo 'artifacts/velopack-libs'

New-Item -ItemType Directory -Force $plugins | Out-Null
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
New-Item -ItemType Directory -Force $temp | Out-Null

Write-Host "Restoring Velopack $Version ..." -ForegroundColor Cyan
& dotnet new classlib -f netstandard2.0 -n VeloVendor -o $temp | Out-Null
& dotnet add (Join-Path $temp 'VeloVendor.csproj') package Velopack --version $Version | Out-Null
& dotnet publish (Join-Path $temp 'VeloVendor.csproj') -c Release -o (Join-Path $temp 'out') | Out-Null

# Copy each produced DLL into Plugins unless it (a) is the throwaway project assembly or
# (b) already exists in Plugins (Unity ships some, sync-client-libs placed others).
$added = @()
$skipped = @()
Get-ChildItem (Join-Path $temp 'out') -Filter *.dll | ForEach-Object {
    if ($_.Name -eq 'VeloVendor.dll') { return }
    $dest = Join-Path $plugins $_.Name
    if (Test-Path $dest) {
        $skipped += $_.Name
    }
    else {
        Copy-Item $_.FullName $dest -Force
        $added += $_.Name
    }
}

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Added to Plugins:   $([string]::Join(', ', $added))" -ForegroundColor Green
Write-Host "Already present:    $([string]::Join(', ', $skipped))" -ForegroundColor DarkGray
Write-Host "Now refresh the Unity Editor so it imports the new DLLs." -ForegroundColor Yellow
