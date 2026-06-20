<#
.SYNOPSIS
  Runs the .NET server test suite with code coverage and produces an HTML + text report.

.DESCRIPTION
  Executes `dotnet test` for tests/BlocksBeyondTheStars.Tests with the XPlat Code Coverage
  collector, scoped to the server assemblies via coverlet.runsettings. Results (Cobertura XML)
  land under ./TestResults, and ReportGenerator renders a browsable HTML report plus a terminal
  summary. The Unity client (client/) is not part of this solution and is not covered here.

  Requires the ReportGenerator global tool (auto-installed on first run if missing):
    dotnet tool install --global dotnet-reportgenerator-globaltool

.EXAMPLE
  ./scripts/test-coverage.ps1
  ./scripts/test-coverage.ps1 -Open      # open the HTML report when done
#>
param(
    [switch] $Open
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$results = Join-Path $repo 'TestResults'
$reportDir = Join-Path $results 'coverage-report'
$runsettings = Join-Path $repo 'coverlet.runsettings'

# Clean previous Cobertura outputs so the report only reflects this run.
if (Test-Path $results) { Remove-Item $results -Recurse -Force }

Write-Host '== Running tests with coverage ==' -ForegroundColor Cyan
dotnet test (Join-Path $repo 'BlocksBeyondTheStars.sln') `
    --collect:'XPlat Code Coverage' `
    --settings $runsettings `
    --results-directory $results

# Ensure ReportGenerator is available.
if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    Write-Host '== Installing dotnet-reportgenerator-globaltool ==' -ForegroundColor Cyan
    dotnet tool install --global dotnet-reportgenerator-globaltool
    $env:PATH = "$env:PATH;$HOME/.dotnet/tools"
}

Write-Host '== Generating report ==' -ForegroundColor Cyan
reportgenerator `
    -reports:"$results/**/coverage.cobertura.xml" `
    -targetdir:$reportDir `
    -reporttypes:'Html;TextSummary'

Get-Content (Join-Path $reportDir 'Summary.txt')

$indexHtml = Join-Path $reportDir 'index.html'
Write-Host "`nHTML report: $indexHtml" -ForegroundColor Green
if ($Open) { Start-Process $indexHtml }
