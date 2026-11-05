# Broken Space Finder - logo.png ve app.ico üretir (ailenin tarzında: koyu gövde, turkuaz alan, kırmızı çizgili bozuk kısım)
# Kullanım: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'

function Round([System.Drawing.Drawing2D.GraphicsPath]$p, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
}

function Draw([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $S / 256.0

    $body = [System.Drawing.Color]::FromArgb(40, 44, 52)
    $edge = [System.Drawing.Color]::FromArgb(70, 76, 88)
    $teal = [System.Drawing.Color]::FromArgb(46, 196, 176)
    $red = [System.Drawing.Color]::FromArgb(222, 62, 66)
    $red2 = [System.Drawing.Color]::FromArgb(150, 30, 36)
    $lens = [System.Drawing.Color]::FromArgb(234, 237, 242)

    # gövde
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    Round $p (8 * $k) (8 * $k) (240 * $k) (240 * $k) (44 * $k)
    $g.FillPath((New-Object System.Drawing.SolidBrush $body), $p)
    $g.DrawPath((New-Object System.Drawing.Pen $edge, ([Math]::Max(1, 6 * $k))), $p)

    # disk dolum pastası: turkuaz dolu alan, kırmızı çizgili "yenmiş" dilim
    $cx = 112 * $k; $cy = 112 * $k; $r = 78 * $k
    $rect = New-Object System.Drawing.RectangleF ($cx - $r), ($cy - $r), (2 * $r), (2 * $r)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $teal), $rect)
    $wedge = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wedge.AddPie($rect.X, $rect.Y, $rect.Width, $rect.Height, -90, 115)
    $hb = New-Object System.Drawing.Drawing2D.HatchBrush ([System.Drawing.Drawing2D.HatchStyle]::WideUpwardDiagonal), $red2, $red
    if ($S -ge 48) { $g.FillPath($hb, $wedge) } else { $g.FillPath((New-Object System.Drawing.SolidBrush $red), $wedge) }
    # dilim ayrımı
    $sep = New-Object System.Drawing.Pen $body, ([Math]::Max(1.5, 7 * $k))
    $a1 = -90 * [Math]::PI / 180; $a2 = 25 * [Math]::PI / 180
    $g.DrawLine($sep, $cx, $cy, $cx + $r * [Math]::Cos($a1), $cy + $r * [Math]::Sin($a1))
    $g.DrawLine($sep, $cx, $cy, $cx + $r * [Math]::Cos($a2), $cy + $r * [Math]::Sin($a2))
    # merkez göbek
    $hr = 14 * $k
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $body), ($cx - $hr), ($cy - $hr), (2 * $hr), (2 * $hr))

    # büyüteç: sağ altta, dilimi inceliyor
    $mx = 160 * $k; $my = 158 * $k; $mr = 40 * $k
    $ring = New-Object System.Drawing.Pen $lens, ([Math]::Max(2, 13 * $k))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 234, 237, 242))), ($mx - $mr), ($my - $mr), (2 * $mr), (2 * $mr))
    $g.DrawEllipse($ring, ($mx - $mr), ($my - $mr), (2 * $mr), (2 * $mr))
    $handle = New-Object System.Drawing.Pen $lens, ([Math]::Max(3, 22 * $k))
    $handle.StartCap = 'Round'; $handle.EndCap = 'Round'
    $g.DrawLine($handle, ($mx + $mr * 0.74), ($my + $mr * 0.74), (222 * $k), (222 * $k))

    $g.Dispose()
    return $bmp
}

$big = Draw 256
$big.Save((Join-Path $src 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ICO: her boyut PNG olarak gömülür (Windows Vista+ destekler)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $src 'app.ico'), $out.ToArray())
Write-Host "logo.png ve app.ico hazır: $src"
