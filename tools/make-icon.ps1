# Generates Assets/Shotto.ico (16-256 px) and Assets/Shotto.png (512 px) from the aperture logo.
# Run: powershell -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Assets'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$blue = [System.Drawing.Color]::FromArgb(255, 132, 186, 222)

# Logo proportions, relative to the outer radius.
$holeApothem = 0.53   # Hexagonal opening in the middle.
$discRadius = 0.39    # Solid disc inside the opening.
$gapWidth = 0.075     # Cuts between the six blades.
$rotation = -20       # Degrees.

# Draws the logo at $size px. It's rendered 8x larger and scaled down for smooth edges; cut-outs are
# transparent. Small sizes get wider cuts so the blades stay distinguishable.
function New-Logo([int]$size) {
    $k = 8
    $M = $size * $k
    $big = New-Object System.Drawing.Bitmap $M, $M, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'
    $c = $M / 2.0
    $R = $M * 0.48
    $gap = [Math]::Max($gapWidth * $R, 1.1 * $k)

    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), [single]($c - $R), [single]($c - $R), [single](2 * $R), [single](2 * $R))

    # Cut the opening and the gaps out to transparency.
    $g.CompositingMode = 'SourceCopy'
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $h = $holeApothem * $R
    $hr = $h / [Math]::Cos([Math]::PI / 6)
    $hex = [System.Drawing.PointF[]]@(for ($i = 0; $i -lt 6; $i++) {
        $t = ($rotation + $i * 60 + 30) * [Math]::PI / 180
        [System.Drawing.PointF]::new([single]($c + $hr * [Math]::Cos($t)), [single]($c + $hr * [Math]::Sin($t)))
    })
    $g.FillPolygon($clear, $hex)
    # Each gap runs along the inside of one side of the hexagon, out past the rim.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Transparent), ([single]$gap)
    for ($i = 0; $i -lt 6; $i++) {
        $t = ($rotation + $i * 60) * [Math]::PI / 180
        $o = $h - $gap / 2
        $px = $c + $o * [Math]::Cos($t); $py = $c + $o * [Math]::Sin($t)
        $dx = -[Math]::Sin($t); $dy = [Math]::Cos($t)
        $g.DrawLine($pen, [single]$px, [single]$py, [single]($px + $dx * 2 * $R), [single]($py + $dy * 2 * $R))
    }

    $g.CompositingMode = 'SourceOver'
    $d = $discRadius * $R
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), [single]($c - $d), [single]($c - $d), [single](2 * $d), [single](2 * $d))
    $g.Dispose()

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBilinear'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingMode = 'SourceCopy'
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose(); $big.Dispose()
    $bmp
}

# Classic 32-bit DIB icon frame (header, bottom-up BGRA rows, empty AND mask). Small frames use this
# because some Windows code paths can't decode PNG frames; the 256 px frame stays PNG to keep the file small.
function Get-IconFrame($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $s = $bmp.Width
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $w = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($s * $s * 4 + $maskStride * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $w.Write((New-Object byte[] ($maskStride * $s)))
    , $ms.ToArray()
}

$pngs = foreach ($s in $sizes) {
    $b = New-Logo $s
    $frame = Get-IconFrame $b
    $b.Dispose()
    , $frame # Leading comma keeps the byte array from being unrolled into the list.
}

$out = Join-Path $assets 'Shotto.ico'
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
"Wrote $out"

$logo = New-Logo 512
$logo.Save((Join-Path $assets 'Shotto.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()
"Wrote $(Join-Path $assets 'Shotto.png')"
