<#
.SYNOPSIS
  Regenerates the launcher's multi-size .ico from the game's app_icon.png so the launcher exe + window share
  the game's icon.

.DESCRIPTION
  Builds a PNG-compressed, multi-size .ico (16..256) at src/BlocksBeyondTheStars.Launcher/app_icon.ico, which
  the launcher csproj embeds via <ApplicationIcon>. Run this whenever the game icon changes, then rebuild the
  launcher (scripts/build-client.ps1 does this as part of the client build).

  Uses System.Drawing, so run it with Windows PowerShell 5.1 (it ships with that assembly):
      powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/make-launcher-icon.ps1
#>
param(
    [string] $Src = 'client/Assets/BlocksBeyondTheStars/Icon/app_icon.png',
    [string] $Out = 'src/BlocksBeyondTheStars.Launcher/app_icon.ico'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $repo $Src
$outPath = Join-Path $repo $Out

$srcImg = [System.Drawing.Image]::FromFile($srcPath)
"source: $($srcImg.Width)x$($srcImg.Height)"
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($srcImg, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $bmp.Dispose()
}
$srcImg.Dispose()

# Assemble the ICO container (little-endian) with PNG-compressed entries (supported on Vista+).
$bytes = New-Object System.Collections.Generic.List[byte]
$bytes.AddRange([BitConverter]::GetBytes([uint16]0))            # ICONDIR: reserved
$bytes.AddRange([BitConverter]::GetBytes([uint16]1))            # type = 1 (icon)
$bytes.AddRange([BitConverter]::GetBytes([uint16]$sizes.Count)) # count
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $wb = if ($s -ge 256) { 0 } else { $s }                    # 256 is encoded as 0
    $bytes.Add([byte]$wb); $bytes.Add([byte]$wb); $bytes.Add([byte]0); $bytes.Add([byte]0)
    $bytes.AddRange([BitConverter]::GetBytes([uint16]1))        # color planes
    $bytes.AddRange([BitConverter]::GetBytes([uint16]32))       # bits per pixel
    $bytes.AddRange([BitConverter]::GetBytes([uint32]$data.Length))
    $bytes.AddRange([BitConverter]::GetBytes([uint32]$offset))
    $offset += $data.Length
}
foreach ($data in $pngs) { $bytes.AddRange($data) }
[System.IO.File]::WriteAllBytes($outPath, $bytes.ToArray())
"wrote: $outPath ($([math]::Round((Get-Item $outPath).Length/1KB,1)) KB, $($sizes.Count) sizes)"
