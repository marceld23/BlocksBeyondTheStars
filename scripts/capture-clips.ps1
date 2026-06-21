<#
.SYNOPSIS
  Captures short in-game video clips (with audio) from the built game player, then muxes them to MP4.

.DESCRIPTION
  Reads a JSON clip manifest (marketing/clips/clips.json by default) and, for each clip, runs the built
  Windows player with -captureClip + -clipName. The in-game ClipDirector self-installs, drives the client
  into that clip's scene and renders a deterministic offline clip: one PNG per frame (1920x1080) plus a
  frame-synced audio.wav, then quits. One world start per run (the proven path). Afterwards this script
  muxes each clip's PNG sequence + WAV into an H.264 MP4 with FFmpeg; the lossless PNG sequence + WAV are
  kept alongside the MP4 as an editing intermediate.

  Each clip picks its own scene (space / surface / cockpit), HUD on/off, length, fps and camera move in the
  manifest. The HUD language is fixed at world start, so a full DE+EN set is two passes (-Lang both).

  Capture must run HEADED (a real audio device) — there is no -batchmode here, or the audio is silent.

  Requires a current player build (scripts/build-client.ps1) — the build bundles the local server, which
  singleplayer needs. Pass -Build to (re)build first. Requires FFmpeg on PATH (or -FfmpegPath).

  Output: <repo>/marketing/clips/<lang>/<clip>.mp4 (+ <clip>/frames/*.png + <clip>/audio.wav)

.EXAMPLE
  ./scripts/capture-clips.ps1                       # all clips, German + English
  ./scripts/capture-clips.ps1 -Lang en             # English only
  ./scripts/capture-clips.ps1 -Build               # build the client first, then capture
  ./scripts/capture-clips.ps1 -Clips space_pan     # only the named clip(s)
#>
param(
    [ValidateSet("both", "de", "en")]
    [string] $Lang = "both",
    [string] $Exe,
    [string] $OutRoot,
    [string] $Manifest,
    [string[]] $Clips,            # optional: only these clip names (default: all in the manifest)
    [long]   $Seed = 424242,
    [switch] $Build,
    [string] $FfmpegPath = "ffmpeg"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe)      { $Exe = Join-Path $repo "client/Build/Windows/BlocksBeyondTheStars.exe" }
if (-not $OutRoot)  { $OutRoot = Join-Path $repo "marketing/clips" }
if (-not $Manifest) { $Manifest = Join-Path $repo "marketing/clips/clips.json" }

if ($Build) {
    Write-Host "Building the client first..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build-client.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-client.ps1 failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $Exe))      { throw "Player build not found at '$Exe'. Run scripts/build-client.ps1 first (or pass -Build)." }
if (-not (Test-Path $Manifest)) { throw "Clip manifest not found at '$Manifest'." }

# Parse the manifest so we know which clips to capture (and each clip's fps for the mux).
$spec = Get-Content $Manifest -Raw | ConvertFrom-Json
$clipList = $spec.clips
if ($Clips) { $clipList = $clipList | Where-Object { $Clips -contains $_.name } }
if (-not $clipList) { throw "No matching clips in '$Manifest'." }

# FFmpeg is required for the mux step; capture still runs without it, but warn loudly.
$haveFfmpeg = $null -ne (Get-Command $FfmpegPath -ErrorAction SilentlyContinue)
if (-not $haveFfmpeg) {
    Write-Warning "FFmpeg not found ('$FfmpegPath'). PNG frames + WAV will be written, but no MP4 is muxed. Install FFmpeg or pass -FfmpegPath."
}

$langs = if ($Lang -eq "both") { @("de", "en") } else { @($Lang) }

foreach ($l in $langs) {
    $out = Join-Path $OutRoot $l
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    foreach ($clip in $clipList) {
        $name = $clip.name
        $fps = if ($clip.fps) { [int]$clip.fps } else { 30 }

        Write-Host "Capturing clip '$name' ('$l') → $out" -ForegroundColor Cyan
        $args = @(
            "-captureClip",
            "-clipManifest", $Manifest,
            "-clipName", $name,
            "-clipOut", $out,
            "-lang", $l,
            "-seed", $Seed,
            "-screen-width", "1920",
            "-screen-height", "1080",
            "-screen-fullscreen", "0"
        )

        $proc = Start-Process -FilePath $Exe -ArgumentList $args -PassThru -Wait
        if ($proc.ExitCode -ne 0) {
            Write-Warning "Capture run '$name' ('$l') exited with code $($proc.ExitCode) — check the player log."
        }

        $clipDir = Join-Path $out $name
        $framesGlob = Join-Path $clipDir "frames/frame_%05d.png"
        $wav = Join-Path $clipDir "audio.wav"
        $mp4 = Join-Path $out "$name.mp4"

        $frameCount = @(Get-ChildItem -Path (Join-Path $clipDir "frames") -Filter *.png -ErrorAction SilentlyContinue).Count
        Write-Host "  → $frameCount frame(s) captured" -ForegroundColor Green

        if ($haveFfmpeg -and $frameCount -gt 0) {
            Write-Host "  Muxing → $mp4" -ForegroundColor Cyan
            $ffArgs = @("-y", "-framerate", $fps, "-i", $framesGlob)
            if (Test-Path $wav) { $ffArgs += @("-i", $wav) }
            $ffArgs += @("-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "16")
            if (Test-Path $wav) { $ffArgs += @("-c:a", "aac", "-b:a", "192k", "-shortest") }
            $ffArgs += $mp4

            & $FfmpegPath @ffArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "FFmpeg mux failed (exit $LASTEXITCODE) for '$name' ('$l')."
            } else {
                Write-Host "  → wrote $mp4" -ForegroundColor Green
            }
        }
    }
}

Write-Host "Done. Clips under $OutRoot" -ForegroundColor Green
