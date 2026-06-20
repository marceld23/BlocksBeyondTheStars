<#
.SYNOPSIS
  Sends a sample player-feedback report to the website API, exactly in the shape the Unity client uses,
  so you can verify the live Wix/Velo endpoint (and the API key) end to end.

.DESCRIPTION
  Resolves the API key in this order: -ApiKey arg → $env:WIX_BUGREPORT_API_KEY → the local git-ignored
  BugReportBuildSecrets.Generated.cs (so it "just works" after a local build setup, without putting the key
  in this script). Posts the payload and prints the HTTP status + response body.

  Run a NEGATIVE test with -BadKey to confirm the endpoint rejects a wrong key (expects 403).

.EXAMPLE
  ./scripts/test-feedback-endpoint.ps1
  ./scripts/test-feedback-endpoint.ps1 -BadKey
  ./scripts/test-feedback-endpoint.ps1 -ApiKey "..." -Url "https://www.blocksbeyondthestars.com/_functions/bugreport"
#>
param(
    [string]$Url = "https://www.blocksbeyondthestars.com/_functions/bugreport",
    [string]$ApiKey = "",
    [switch]$BadKey,
    [switch]$NoScreenshot
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# --- Resolve the API key --------------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ApiKey)) { $ApiKey = $env:WIX_BUGREPORT_API_KEY }
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $gen = Join-Path $repo 'client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.Generated.cs'
    if (Test-Path $gen) {
        $m = [regex]::Match((Get-Content $gen -Raw), 'key\s*=\s*"([^"]+)"')
        if ($m.Success) { $ApiKey = $m.Groups[1].Value }
    }
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "No API key. Pass -ApiKey, set WIX_BUGREPORT_API_KEY, or create BugReportBuildSecrets.Generated.cs."
}
if ($BadKey) { $ApiKey = "definitely-wrong-key" }

# --- Build the payload (same field shape as FeedbackReport) ---------------------------------------------
$report = [ordered]@{
    title           = "Testreport (curl/PS)"
    description     = "Das ist ein Test-Bugreport vom test-feedback-endpoint.ps1 Skript."
    email           = ""
    gameVersion     = "0.1.0-alpha"
    buildNumber     = "test-build-guid"
    playerId        = "anonymous-test-player"
    playerName      = "TestPilot"
    sessionId       = "test-session-123"
    platform        = "WindowsPlayer"
    clientTimestamp = (Get-Date).ToUniversalTime().ToString("o")
    reportJson      = [ordered]@{
        location       = "Sol - Terra"
        station        = ""
        worldSeed      = 424242
        health         = 80; oxygen = 95; energy = 60; hunger = 40
        sessionSeconds = 1837
        language       = "de"
    }
}
if (-not $NoScreenshot) {
    # A 1x1 JPG so the media-upload path is exercised too.
    $report.screenshot = [ordered]@{
        fileName = "feedback_test.jpg"
        mimeType = "image/jpeg"
        base64   = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAA//EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AfwD/2Q=="
    }
}
$json = $report | ConvertTo-Json -Depth 6

Write-Host "POST $Url" -ForegroundColor Cyan
Write-Host ("key:  " + $ApiKey.Substring(0, [Math]::Min(6, $ApiKey.Length)) + "… (" + $ApiKey.Length + " chars)") -ForegroundColor DarkGray

try {
    $resp = Invoke-WebRequest -Uri $Url -Method Post -Body $json `
        -ContentType "application/json" `
        -Headers @{ "x-bugreport-key" = $ApiKey } `
        -SkipHttpErrorCheck
    Write-Host ("HTTP " + [int]$resp.StatusCode) -ForegroundColor ($(if ([int]$resp.StatusCode -lt 300) { "Green" } else { "Yellow" }))
    Write-Host $resp.Content
}
catch {
    Write-Host "Request failed (endpoint offline / DNS / TLS): $($_.Exception.Message)" -ForegroundColor Red
}
