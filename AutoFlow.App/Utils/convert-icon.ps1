$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pngPath = Join-Path $scriptDir "AppIcon.png"
$icoPath = Join-Path $scriptDir "AppIcon.ico"

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($pngPath)
$sizes = @(256, 64, 48, 32, 16)

$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$entriesOffset = 6 + 16 * $sizes.Count
$pngDataList = @()

foreach ($size in $sizes) {
    $resized = [System.Drawing.Bitmap]::new($src, $size, $size)
    $pngMs = [System.IO.MemoryStream]::new()
    $resized.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngMs.ToArray()
    $pngDataList += $pngBytes

    $w = if ($size -ge 256) { 0 } else { $size }
    $h = if ($size -ge 256) { 0 } else { $size }

    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngBytes.Length)
    $bw.Write([uint32]$entriesOffset)
    $entriesOffset += $pngBytes.Length

    $resized.Dispose()
    $pngMs.Dispose()
}

foreach ($pngBytes in $pngDataList) {
    $bw.Write($pngBytes)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$src.Dispose()

Write-Host "Created: $icoPath"
