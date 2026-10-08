<#
.SYNOPSIS
  Finds the Unity editor that matches the project's pinned version (client/ProjectSettings/ProjectVersion.txt).

.DESCRIPTION
  The build and test scripts used to hardcode the Hub's default install path, which breaks the moment the
  editor lives anywhere else — on a locked-down machine the Hub cannot write to Program Files, so the editor
  is installed per user (#2389). This helper reads the project's editor version and looks, in order:

    1. $env:UNITY_EDITOR_PATH            — an explicit override (a full path to Unity.exe)
    2. C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe   — the Hub's default
    3. %USERPROFILE%\Unity\Editors\<version>\Editor\Unity.exe          — the per-user install
    4. the Hub's secondary install location (%APPDATA%\UnityHub\secondaryInstallPath.json)

  Prints the path of the first match (and sets $script:UnityPath for dot-sourcing callers). Exits with an
  error that names the expected version and every place it looked when none matches.

.EXAMPLE
  $unity = & ./scripts/resolve-unity.ps1
  ./scripts/build-client.ps1 -UnityPath (& ./scripts/resolve-unity.ps1)
#>
param(
    [string] $Repo = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$versionFile = Join-Path $Repo 'client/ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path $versionFile)) {
    Write-Error "ProjectVersion.txt not found at '$versionFile'."
}

$versionLine = Get-Content $versionFile | Where-Object { $_ -match '^\s*m_EditorVersion:\s*(\S+)' } | Select-Object -First 1
if (-not $versionLine) {
    Write-Error "Could not read m_EditorVersion from '$versionFile'."
}
$version = $Matches[1]

$candidates = @()
if ($env:UNITY_EDITOR_PATH) { $candidates += $env:UNITY_EDITOR_PATH }
$candidates += "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
$candidates += (Join-Path $env:USERPROFILE "Unity\Editors\$version\Editor\Unity.exe")

$secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
if (Test-Path $secondary) {
    try {
        $root = (Get-Content $secondary -Raw | ConvertFrom-Json)
        if ($root -is [string] -and $root.Trim().Length -gt 0) {
            $candidates += (Join-Path $root.Trim() "$version\Editor\Unity.exe")
        }
    }
    catch { }
}

foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) {
        $script:UnityPath = $c
        Write-Output $c
        return
    }
}

Write-Error ("Unity $version not found. Looked in:`n  " + ($candidates -join "`n  ") +
    "`nInstall it via Unity Hub (or run the editor installer with /S /D=<folder>) or set UNITY_EDITOR_PATH.")
