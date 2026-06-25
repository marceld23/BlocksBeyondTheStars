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

  -Msi additionally builds a machine-wide MSI: a classic Windows Installer wizard (Welcome → AGPL license
  → install scope → progress → finish) with clean default WiX dialogs and the game icon as the only
  branding. It installs per-user without admin by default but also offers a machine-wide scope (UAC).
  The MSI adds the WiX toolset acquisition + ~2-3 min to the pack; leave it off for routine dev builds.

  Prerequisites:
    1. The Velopack runtime must be in the client build: run ./scripts/sync-velopack-libs.ps1 ONCE,
       refresh the Unity Editor, then build with ./scripts/build-client.ps1.
    2. The Velopack CLI (vpk). Auto-installed here as a global tool if missing.

.PARAMETER Version
  SemVer for this release. Default: read from PlayerSettings.bundleVersion in ProjectSettings.asset (the
  single source of truth, which the release CI sets from the git tag; local builds keep 0.0.0-dev). Pass a
  clean value like 0.20.0 for an actual local release. Each published version MUST be higher than the last
  for updates to apply.

.PARAMETER BuildDir
  The Unity player folder to package (must contain BlocksBeyondTheStars.exe). Default: client/Build/Windows.

.PARAMETER ServeDir
  Optional server install dir; the installer + feed are copied to <ServeDir>/clients so the API serves them.

.PARAMETER Msi
  Also build a machine-wide MSI (full WiX wizard) alongside the Setup.exe. Shows the AGPL LICENSE on the
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
    [switch] $Msi      # also build a machine-wide MSI (full WiX wizard with the AGPL license + game art)
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

# Strip Unity's developer-only debug folders before packing. Their names literally end in "_DoNotShip"
# (Burst AOT debug symbols) / "_BackUpThisFolder_ButDontShipItWithYourGame" (IL2CPP backup): they are
# debugging aids that must never reach players and only bloat the installer. Safe to delete — Unity
# regenerates them on the next build. This guards every pack output (Setup.exe, MSI, Portable.zip).
foreach ($pattern in @('*_DoNotShip', '*_BackUpThisFolder_ButDontShipItWithYourGame')) {
    Get-ChildItem -Path $build -Directory -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Removing non-shippable folder: $($_.Name)" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force
    }
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

# Derive the version unless one was given. The single source of truth is PlayerSettings.bundleVersion
# (set by the release CI from the git tag at build time, e.g. "0.3.0"); read it from ProjectSettings.asset.
# A local build keeps the committed dev value (0.1.0-dev) — pass -Version for a real local release.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $projSettings = Get-Content (Join-Path $repo 'client/ProjectSettings/ProjectSettings.asset') -Raw
    if ($projSettings -match '(?m)^\s*bundleVersion:\s*(\S+)') { $Version = $Matches[1] }
    else { Write-Error 'Could not read bundleVersion from ProjectSettings.asset; pass -Version explicitly.' }
}

# Ensure the vpk CLI is on PATH (install the global tool on first use).
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing the Velopack CLI (vpk) ...' -ForegroundColor Cyan
    dotnet tool install -g vpk | Out-Null
    $env:PATH += [IO.Path]::PathSeparator + (Join-Path $HOME '.dotnet/tools')
}

New-Item -ItemType Directory -Force $out | Out-Null

# Optional machine-wide MSI (full WiX wizard). We deliberately do NOT pass custom banner/dialog bitmaps:
# WiX overlays its own dark heading/welcome text on those, so full-bleed game art is unreadable, and
# Velopack 1.2.0 also feeds --msiBanner/--msiLogo into the opposite UI slots (stretched/squished). Instead
# we keep WiX's clean default dialogs and use the game icon as the only branding (--icon below).
$msiArgs = @()
if ($Msi) {
    # MSI ProductVersion must be purely numeric (x.x.x, each part <= 65535) — strip any pre-release suffix
    # like "-dev" so a dev build still produces a valid MSI.
    $msiVersion = ($Version -split '-')[0]

    # The AGPL license shown on the wizard's License page. Velopack requires the file to end in .txt/.md/.rtf,
    # but the repo LICENSE has no extension — copy it to a .txt next to the other build artifacts (.txt keeps
    # the AGPL text verbatim; .md would reflow it).
    $licenseSrc = Join-Path $repo 'LICENSE'
    if (-not (Test-Path $licenseSrc)) { Write-Error "LICENSE not found at '$licenseSrc' (needed for the MSI license page)." }
    $licensePath = Join-Path $repo 'artifacts/LICENSE.txt'
    Copy-Item $licenseSrc $licensePath -Force

    $msiArgs = @(
        '--msi', 'true',
        '--msiVersion', $msiVersion,
        '--instLicense', $licensePath
    )
    Write-Host "MSI will be built (version $msiVersion, AGPL license page, clean WiX dialogs, game icon only)." -ForegroundColor Cyan
}

# Attribution: copy the project LICENSE + the third-party NOTICES into the player folder so every pack
# output (Setup.exe, the portable zip AND the MSI — they all come from this one `vpk pack --packDir $build`)
# ships them next to the executable. The CEF/UWB engine notices are already placed under
# BlocksBeyondTheStars_Data/UWB by the UWB package; these two add the top-level project + full third-party
# list (THIRD-PARTY-NOTICES.txt is the name the in-game Credits screen points players at).
$licenseRoot = Join-Path $repo 'LICENSE'
$noticesRoot = Join-Path $repo 'NOTICES.md'
if (-not (Test-Path $licenseRoot)) { Write-Error "LICENSE not found at '$licenseRoot' (needed for the build attribution)." }
if (-not (Test-Path $noticesRoot)) { Write-Error "NOTICES.md not found at '$noticesRoot' (needed for the build attribution)." }
Copy-Item $licenseRoot (Join-Path $build 'LICENSE.txt') -Force
Copy-Item $noticesRoot (Join-Path $build 'THIRD-PARTY-NOTICES.txt') -Force
Write-Host 'Copied LICENSE.txt + THIRD-PARTY-NOTICES.txt into the player folder for the installer.' -ForegroundColor DarkGray

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
    if ($msiFile) {
        Write-Host "MSI:       $($msiFile.FullName)" -ForegroundColor Green

        # vpk embeds the game icon as the 'appicon' Icon-table row (used for the Start-menu shortcut) but does
        # NOT set ARPPRODUCTICON, so the entry in Apps & Features would show a generic icon. Point it at that
        # existing icon so the game icon shows there too. (Done post-pack; safe on the still-unsigned MSI.)
        try {
            $msiInstaller = New-Object -ComObject WindowsInstaller.Installer
            $msiDb = $msiInstaller.OpenDatabase($msiFile.FullName, 1)  # 1 = msiOpenDatabaseModeTransact
            $del = $msiDb.OpenView("DELETE FROM ``Property`` WHERE ``Property``='ARPPRODUCTICON'")
            $del.Execute(); $del.Close()
            $ins = $msiDb.OpenView("INSERT INTO ``Property`` (``Property``,``Value``) VALUES ('ARPPRODUCTICON','appicon')")
            $ins.Execute(); $ins.Close()
            $msiDb.Commit()
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($msiDb)
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($msiInstaller)
            Write-Host '           (set ARPPRODUCTICON -> game icon for Apps & Features)' -ForegroundColor DarkGray
        }
        catch {
            Write-Warning "Could not set ARPPRODUCTICON on the MSI: $_"
        }
    }
}

# Optionally stage installer + feed into a server install dir for the API to serve.
if (-not [string]::IsNullOrWhiteSpace($ServeDir)) {
    $serve = Join-Path $repo $ServeDir
    $clients = Join-Path $serve 'clients'
    New-Item -ItemType Directory -Force $clients | Out-Null
    Copy-Item (Join-Path $out '*') $clients -Force
    Write-Host "Staged into $clients — the API serves /download (Setup.exe) and /updates (feed)." -ForegroundColor Green
}
