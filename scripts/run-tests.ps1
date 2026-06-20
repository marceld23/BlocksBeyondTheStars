<#
.SYNOPSIS
  Runs the project's test suites, selectable per suite — so you can choose whether the (slow,
  Unity-Editor-bound) client tests run alongside the fast .NET ones.

.DESCRIPTION
  Four suites:
    Dotnet      .NET xUnit server/shared suite (tests/BlocksBeyondTheStars.Tests) — no Unity.
    ClientCore  Headless client<->server integration (tests/BlocksBeyondTheStars.Client.Tests):
                the REAL NetworkClient driven against the REAL in-process GameServer over a
                LoopbackLink — no Unity, no sockets.
    UnityEdit   Unity EditMode tests (client/Assets/Tests/EditMode) via the Unity Editor in batch mode.
    UnityPlay   Unity PlayMode tests (client/Assets/Tests/PlayMode): a real NetworkClient against the
                bundled server exe over loopback UDP. Publishes the server + syncs libs first.

  The default runs only the fast .NET suites (Dotnet + ClientCore); the Unity suites are opt-in.
  See docs/developer/CLIENT_TESTING.md for the full picture.

.PARAMETER Suites
  Which suites to run: any of Dotnet, ClientCore, UnityEdit, UnityPlay, or All. Default: Dotnet, ClientCore.

.PARAMETER UnityPath
  Path to Unity.exe (only needed for the Unity suites). Defaults to the project's editor version.

.PARAMETER Coverage
  Run the .NET suites under coverage (delegates to scripts/test-coverage.ps1; ignores the Unity suites).

.EXAMPLE
  ./scripts/run-tests.ps1                          # fast .NET suites only (Dotnet + ClientCore)
  ./scripts/run-tests.ps1 -Suites All              # everything, including the Unity Editor suites
  ./scripts/run-tests.ps1 -Suites ClientCore       # just the headless client<->server tests
  ./scripts/run-tests.ps1 -Suites Dotnet,UnityEdit # server suite + Unity EditMode
  ./scripts/run-tests.ps1 -Coverage                # .NET suites with a coverage report
#>
param(
    [ValidateSet('Dotnet', 'ClientCore', 'UnityEdit', 'UnityPlay', 'All')]
    [string[]] $Suites = @('Dotnet', 'ClientCore'),
    [string]   $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe",
    [switch]   $Coverage
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$client = Join-Path $repo 'client'
$results = Join-Path $repo 'TestResults'
New-Item -ItemType Directory -Force $results | Out-Null

# Expand 'All' into the concrete suites.
if ($Suites -contains 'All') {
    $Suites = @('Dotnet', 'ClientCore', 'UnityEdit', 'UnityPlay')
}

$failures = @()
$summary = @()

# Coverage path delegates the .NET suites to the existing coverage script (one combined run).
if ($Coverage) {
    Write-Host '== .NET suites with coverage ==' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'test-coverage.ps1')
    if ($LASTEXITCODE -ne 0) { $failures += 'Coverage' }
    $Suites = $Suites | Where-Object { $_ -notin @('Dotnet', 'ClientCore') }
}

function Invoke-DotnetSuite([string] $name, [string] $projectRelPath) {
    Write-Host "== $name ($projectRelPath) ==" -ForegroundColor Cyan
    & dotnet test (Join-Path $repo $projectRelPath) -c Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        $script:failures += $name
        $script:summary += "FAILED  $name"
    }
    else {
        $script:summary += "passed  $name"
    }
}

# --- Unity batch test runner ---------------------------------------------------------------
# Runs a Unity test platform headless and parses the NUnit XML it writes. Unity's own exit code is
# non-zero on test failure, but (like the build script) Unity can relaunch a child process, so we also
# parse the result file to report pass/fail counts and to confirm the run actually produced results.
function Invoke-UnitySuite([string] $name, [string] $platform) {
    if (-not (Test-Path $UnityPath)) {
        Write-Error "Unity editor not found at '$UnityPath'. Pass -UnityPath to your Unity 6000.4.x Unity.exe."
    }

    $resultFile = Join-Path $results ("unity-" + $platform.ToLower() + ".xml")
    $log = Join-Path $results ("unity-" + $platform.ToLower() + ".log")
    if (Test-Path $resultFile) { Remove-Item $resultFile -Force }

    Write-Host "== $name (Unity $platform) ==" -ForegroundColor Cyan
    & $UnityPath -batchmode -nographics -projectPath $client `
        -runTests -testPlatform $platform `
        -testResults $resultFile -logFile $log
    $unityExit = $LASTEXITCODE

    # Wait until Unity has fully exited and the result file exists (Unity may relaunch a child).
    $deadline = (Get-Date).AddMinutes(20)
    do {
        Start-Sleep -Seconds 3
        $running = [bool](Get-Process Unity -ErrorAction SilentlyContinue)
    } while (((Get-Date) -lt $deadline) -and $running)

    if (-not (Test-Path $resultFile)) {
        Write-Warning "$name produced no result file ($resultFile). See $log"
        $script:failures += $name
        $script:summary += "FAILED  $name (no results — see $log)"
        return
    }

    [xml] $xml = Get-Content $resultFile
    $run = $xml.'test-run'
    $total = [int] $run.total
    $passed = [int] $run.passed
    $failed = [int] $run.failed
    $skipped = [int] $run.skipped
    $line = "$name`: total=$total passed=$passed failed=$failed skipped=$skipped"
    if ($failed -gt 0 -or ($total -eq 0 -and $unityExit -ne 0)) {
        $script:failures += $name
        $script:summary += "FAILED  $line"
    }
    else {
        $script:summary += "passed  $line"
    }
}

# Unity suites need the synced libs/content; PlayMode additionally needs the bundled server exe.
$unityWanted = ($Suites -contains 'UnityEdit') -or ($Suites -contains 'UnityPlay')
if ($unityWanted) {
    Write-Host '== Prerequisite: syncing shared libs + content ==' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'sync-client-libs.ps1')
}
if ($Suites -contains 'UnityPlay') {
    Write-Host '== Prerequisite: publishing the bundled server (PlayMode needs it) ==' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'publish-local-server.ps1')
}

foreach ($suite in $Suites) {
    switch ($suite) {
        'Dotnet' { Invoke-DotnetSuite 'Dotnet' 'tests/BlocksBeyondTheStars.Tests' }
        'ClientCore' { Invoke-DotnetSuite 'ClientCore' 'tests/BlocksBeyondTheStars.Client.Tests' }
        'UnityEdit' { Invoke-UnitySuite 'UnityEdit' 'EditMode' }
        'UnityPlay' { Invoke-UnitySuite 'UnityPlay' 'PlayMode' }
    }
}

Write-Host "`n==================== Test summary ====================" -ForegroundColor Cyan
$summary | ForEach-Object { Write-Host "  $_" }
Write-Host "=====================================================" -ForegroundColor Cyan

if ($failures.Count -gt 0) {
    Write-Error ("Test run FAILED in: " + ($failures -join ', '))
}
else {
    Write-Host "All selected suites passed." -ForegroundColor Green
}
