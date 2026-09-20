# Renders Assets\app.ico (multi-size, PNG-compressed) and Assets\app-preview.png.
# Run from the repo root:  pwsh tools\generate-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-Master {
    $s = 1024
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = 220
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(32, 32, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc(992 - 2 * $r, 32, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc(992 - 2 * $r, 992 - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(32, 992 - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(1024, 1024)), ([System.Drawing.Color]::FromArgb(255, 20, 184, 166)), ([System.Drawing.Color]::FromArgb(255, 14, 116, 144))
    $g.FillPath($bg, $path)

    $white = [System.Drawing.Brushes]::White
    $dx = -50
    $g.FillRectangle($white, 250 + $dx, 420, 140, 184)
    $cone = @(
        [System.Drawing.Point]::new(380 + $dx, 420), [System.Drawing.Point]::new(560 + $dx, 290),
        [System.Drawing.Point]::new(560 + $dx, 734), [System.Drawing.Point]::new(380 + $dx, 604))
    $g.FillPolygon($white, $cone)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 62
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($rad in 190, 320) {
        $g.DrawArc($pen, (560 + $dx - $rad + 30), (512 - $rad), (2 * $rad), (2 * $rad), -45, 90)
    }
    $g.Dispose()
    return $bmp
}

$master = New-Master
$master.Save((Join-Path $assets 'app-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) {
    $b = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.InterpolationMode = 'HighQualityBicubic'
    $gg.SmoothingMode = 'HighQuality'
    $gg.PixelOffsetMode = 'HighQuality'
    $gg.DrawImage($master, 0, 0, $sz, $sz)
    $gg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    [pscustomobject]@{ Size = $sz; Data = $ms.ToArray() }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Data.Length); $w.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $w.Write($f.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
$master.Dispose()
"wrote $(Join-Path $assets 'app.ico') ($($frames.Count) sizes)"
