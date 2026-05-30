<#
.SYNOPSIS
  Publishes the Spacecraft dedicated server + admin UI as self-hosting packages.

.DESCRIPTION
  Produces self-contained, single-file builds (no .NET install needed on the host) for
  Windows x64, Linux x64 and Linux ARM64 (Raspberry Pi 5), bundles the data/ content and
  a default config, then zips each package into artifacts/.

.EXAMPLE
  ./scripts/publish-server.ps1
  ./scripts/publish-server.ps1 -Runtimes win-x64
#>
param(
    [string[]] $Runtimes = @('win-x64', 'linux-x64', 'linux-arm64'),
    [bool] $SelfContained = $true
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing for $rid" -ForegroundColor Cyan
    $out = Join-Path $artifacts $rid
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null

    $common = @(
        '-c', 'Release', '-r', $rid,
        "-p:SelfContained=$($SelfContained.ToString().ToLower())",
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-o', $out
    )

    dotnet publish (Join-Path $repo 'src/Spacecraft.GameServer') @common
    dotnet publish (Join-Path $repo 'src/Spacecraft.Api') @common
    dotnet publish (Join-Path $repo 'src/Spacecraft.Tools') @common

    # Bundle content and a default config.
    Copy-Item (Join-Path $repo 'data') (Join-Path $out 'data') -Recurse -Force
    $configDir = Join-Path $out 'config'
    New-Item -ItemType Directory -Force $configDir | Out-Null
    if (-not (Test-Path (Join-Path $configDir 'server.json'))) {
        # The server writes a default config on first run; leave the dir present.
        Set-Content -Path (Join-Path $configDir 'README.txt') -Value 'server.json is generated on first launch; edit it or use the admin UI.'
    }

    $zip = Join-Path $artifacts "spacecraft-server-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host "    Package: $zip" -ForegroundColor Green
}

Write-Host "Done. Packages are in $artifacts" -ForegroundColor Green
