<#
.SYNOPSIS
  Packages the built Windows client into a Velopack installer (Setup.exe) + auto-update feed.

.DESCRIPTION
  Runs the Velopack CLI (`vpk pack`) over the Unity player in client/Build/Windows and produces, in
  artifacts/installer:

    BlocksBeyondTheStars-win-Setup.exe        the installer players download + run
    BlocksBeyondTheStars-<ver>-full.nupkg     the release payload (the update feed)
    releases.win.json / RELEASES              the feed manifest the client reads
    BlocksBeyondTheStars-win-Portable.zip     a no-install portable copy (bonus)
    BlocksBeyondTheStars-win.msi              a full WiX wizard installer (only with -Msi)

  Players install the Setup.exe (per-user, no admin needed — Velopack installs to %LOCALAPPDATA%) and
  the in-app updater (ClientUpdater.cs) pulls future versions from a feed URL served by
  BlocksBeyondTheStars.Api at /updates. Use -ServeDir to copy the feed straight into a server install so
  the API serves it (its /download button hands out the Setup.exe; /updates is the feed).

  -Msi additionally builds a machine-wide MSI: a classic Windows Installer wizard (Welcome → MIT license
  → install scope → progress → finish) with the game icon and game-art banner/dialog bitmaps rendered from
  the launcher. It installs per-user without admin by default but also offers a machine-wide scope (UAC).
  The MSI adds the WiX toolset acquisition + ~2-3 min to the pack; leave it off for routine dev builds.

  Prerequisites:
    1. The Velopack runtime must be in the client build: run ./scripts/sync-velopack-libs.ps1 ONCE,
       refresh the Unity Editor, then build with ./scripts/build-client.ps1.
    2. The Velopack CLI (vpk). Auto-installed here as a global tool if missing.

.PARAMETER Version
  SemVer for this release. Default: read from AppShell.Version (e.g. 0.20.0-dev). Pass a clean value
  like 0.20.0 for an actual release. Each published version MUST be higher than the last for updates to
  apply.

.PARAMETER BuildDir
  The Unity player folder to package (must contain BlocksBeyondTheStars.exe). Default: client/Build/Windows.

.PARAMETER ServeDir
  Optional server install dir; the installer + feed are copied to <ServeDir>/clients so the API serves them.

.PARAMETER Msi
  Also build a machine-wide MSI (full WiX wizard) alongside the Setup.exe. Shows the MIT LICENSE on the
  license page and uses game-art banner/dialog bitmaps + the game icon.

.EXAMPLE
  ./scripts/publish-client-installer.ps1
  ./scripts/publish-client-installer.ps1 -Version 0.21.0 -ServeDir artifacts/win-x64
  ./scripts/publish-client-installer.ps1 -Version 0.21.0 -Msi
#>
param(
    [string] $Version = '',
    [string] $BuildDir = 'client/Build/Windows',
    [string] $OutputDir = 'artifacts/installer',
    [string] $ServeDir = '',
    [string] $Channel = 'win',
    [switch] $Msi      # also build a machine-wide MSI (full WiX wizard with the MIT license + game art)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repo $BuildDir
$out = Join-Path $repo $OutputDir

if (-not (Test-Path (Join-Path $build 'BlocksBeyondTheStars.exe'))) {
    Write-Error "Client build not found at '$build'. Build it first with ./scripts/build-client.ps1."
}

# The launcher (loading splash) is the executable users launch; it must be present so it can be the mainExe.
if (-not (Test-Path (Join-Path $build 'BlocksBeyondTheStars.Launcher.exe'))) {
    Write-Error "Launcher not found at '$build'. Re-run ./scripts/build-client.ps1 (it now builds the launcher)."
}

# The installer icon (.ico). Regenerate from the game PNG via ./scripts/make-launcher-icon.ps1 if missing.
$iconPath = Join-Path $repo 'src/BlocksBeyondTheStars.Launcher/app_icon.ico'
if (-not (Test-Path $iconPath)) {
    Write-Error "Installer icon not found at '$iconPath'. Run ./scripts/make-launcher-icon.ps1 to generate it."
}

# Render the installer splash from the launcher itself, so the install window matches the loading splash the
# player sees at startup (same procedural space art). We render WITHOUT the launcher's indeterminate bar —
# Velopack overlays its own live progress bar, which we tint to the game's cyan (#7DDEEC) below.
$splashImage = Join-Path $repo 'artifacts/installer-splash.png'
$splashColor = '#7DDEEC'
New-Item -ItemType Directory -Force (Split-Path -Parent $splashImage) | Out-Null
if (Test-Path $splashImage) { Remove-Item $splashImage -Force }
Write-Host 'Rendering installer splash from the launcher (game look)...' -ForegroundColor Cyan
# The launcher is a WinExe (GUI subsystem), so it does not block the shell — Start-Process -Wait ensures the
# PNG is fully written before we pack it.
$render = Start-Process -FilePath (Join-Path $build 'BlocksBeyondTheStars.Launcher.exe') `
    -ArgumentList @('--render-install-splash', $splashImage, '560', '320') -Wait -PassThru -NoNewWindow
if ($render.ExitCode -ne 0 -or -not (Test-Path $splashImage)) {
    Write-Error "Failed to render the installer splash via the launcher at '$build'."
}

# Derive the version from AppShell.Version unless one was given.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $appShell = Get-Content (Join-Path $repo 'client/Assets/BlocksBeyondTheStars/Scripts/AppShell.cs') -Raw
    if ($appShell -match 'Version\s*=\s*"([^"]+)"') { $Version = $Matches[1] }
    else { Write-Error 'Could not read AppShell.Version; pass -Version explicitly.' }
}

# Ensure the vpk CLI is on PATH (install the global tool on first use).
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing the Velopack CLI (vpk) ...' -ForegroundColor Cyan
    dotnet tool install -g vpk | Out-Null
    $env:PATH += [IO.Path]::PathSeparator + (Join-Path $HOME '.dotnet/tools')
}

New-Item -ItemType Directory -Force $out | Out-Null

# Optional machine-wide MSI (full WiX wizard). Build its extra args + render its two game-art bitmaps.
$msiArgs = @()
if ($Msi) {
    # MSI ProductVersion must be purely numeric (x.x.x, each part <= 65535) — strip any pre-release suffix
    # like "-dev" so a dev build still produces a valid MSI.
    $msiVersion = ($Version -split '-')[0]

    # The MIT license shown on the wizard's License page. Velopack requires the file to end in .txt/.md/.rtf,
    # but the repo LICENSE has no extension — copy it to a .txt next to the other build artifacts (.txt keeps
    # the MIT text verbatim; .md would reflow it).
    $licenseSrc = Join-Path $repo 'LICENSE'
    if (-not (Test-Path $licenseSrc)) { Write-Error "LICENSE not found at '$licenseSrc' (needed for the MSI license page)." }
    $licensePath = Join-Path $repo 'artifacts/LICENSE.txt'
    Copy-Item $licenseSrc $licensePath -Force

    # Game-art wizard bitmaps, rendered from the launcher at WiX's fixed resolutions (banner 493x58,
    # dialog/logo 493x312). Light text areas + dark space art where WiX leaves room (see MsiArt.cs).
    $msiBanner = Join-Path $repo 'artifacts/msi-banner.bmp'
    $msiLogo = Join-Path $repo 'artifacts/msi-logo.bmp'
    $launcher = Join-Path $build 'BlocksBeyondTheStars.Launcher.exe'
    Write-Host 'Rendering MSI wizard bitmaps from the launcher (game look)...' -ForegroundColor Cyan
    foreach ($spec in @(@{ mode = '--render-msi-banner'; path = $msiBanner }, @{ mode = '--render-msi-logo'; path = $msiLogo })) {
        if (Test-Path $spec.path) { Remove-Item $spec.path -Force }
        $r = Start-Process -FilePath $launcher -ArgumentList @($spec.mode, $spec.path) -Wait -PassThru -NoNewWindow
        if ($r.ExitCode -ne 0 -or -not (Test-Path $spec.path)) { Write-Error "Failed to render MSI bitmap ($($spec.mode))." }
    }

    $msiArgs = @(
        '--msi', 'true',
        '--msiVersion', $msiVersion,
        '--instLicense', $licensePath,
        '--msiBanner', $msiBanner,
        '--msiLogo', $msiLogo
    )
    Write-Host "MSI will be built (version $msiVersion, MIT license page, game-art wizard)." -ForegroundColor Cyan
}

Write-Host "Packaging Blocks Beyond the Stars $Version (channel '$Channel') from $build ..." -ForegroundColor Cyan
$packArgs = @(
    'pack',
    '--packId', 'BlocksBeyondTheStars',
    '--packVersion', $Version,
    '--packTitle', 'Blocks Beyond the Stars',
    '--packAuthors', 'JuMaVe Games',
    '--packDir', $build,
    # The launcher is the launched executable (shortcut target + Velopack hook host); it shows the loading
    # splash, then starts BlocksBeyondTheStars.exe. The launcher calls VelopackApp.Run() first, so install/
    # update/uninstall hooks are handled correctly.
    '--mainExe', 'BlocksBeyondTheStars.Launcher.exe',
    # Give Setup.exe / Update.exe the game icon too (the installed shortcut already inherits it from the
    # launcher's embedded <ApplicationIcon>; vpk does NOT derive the installer icon from mainExe).
    '--icon', $iconPath,
    # Game-styled install window: our rendered splash art + a cyan progress bar (matches the launcher).
    '--splashImage', $splashImage,
    '--splashProgressColor', $splashColor,
    '--channel', $Channel,
    '--outputDir', $out
)
$packArgs += $msiArgs
# Sign here later by adding: --signParams "..."  (an unsigned Setup.exe triggers a one-time SmartScreen prompt).
vpk @packArgs

$setup = Get-ChildItem $out -Filter '*Setup.exe' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
Write-Host "Installer: $($setup.FullName)" -ForegroundColor Green
Write-Host "Feed:      $out (releases.$Channel.json + *.nupkg)" -ForegroundColor Green
if ($Msi) {
    $msiFile = Get-ChildItem $out -Filter '*.msi' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($msiFile) { Write-Host "MSI:       $($msiFile.FullName)" -ForegroundColor Green }
}

# Optionally stage installer + feed into a server install dir for the API to serve.
if (-not [string]::IsNullOrWhiteSpace($ServeDir)) {
    $serve = Join-Path $repo $ServeDir
    $clients = Join-Path $serve 'clients'
    New-Item -ItemType Directory -Force $clients | Out-Null
    Copy-Item (Join-Path $out '*') $clients -Force
    Write-Host "Staged into $clients — the API serves /download (Setup.exe) and /updates (feed)." -ForegroundColor Green
}
