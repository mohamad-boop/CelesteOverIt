# Dev helper: renders BepInEx/CelesteOverIt_map.txt (from Dev.SelfTest) to a PNG with a 10-unit grid.
param([string]$MapFile, [string]$OutFile, [float]$Scale = 8, [string]$Crop = "")
Add-Type -AssemblyName System.Drawing
$ci = [Globalization.CultureInfo]::InvariantCulture
$shapes = foreach ($line in Get-Content $MapFile) {
    $p = $line.Split('|')
    $pts = foreach ($xy in $p[3].Trim().Split(' ')) { $a = $xy.Split(','); ,@([float]::Parse($a[0], $ci), [float]::Parse($a[1], $ci)) }
    [pscustomobject]@{ Kind = $p[0]; Layer = $p[1]; Name = $p[2]; Pts = $pts }
}
$all = $shapes | % { $_.Pts }
$minX = ($all | % { $_[0] } | Measure-Object -Minimum).Minimum; $maxX = ($all | % { $_[0] } | Measure-Object -Maximum).Maximum
$minY = ($all | % { $_[1] } | Measure-Object -Minimum).Minimum; $maxY = ($all | % { $_[1] } | Measure-Object -Maximum).Maximum
if ($Crop) { $c = $Crop.Split(',') | % { [float]$_ }; $minX, $minY, $maxX, $maxY = $c }
"bounds x $minX..$maxX  y $minY..$maxY"
$w = [int](($maxX - $minX) * $Scale) + 80; $h = [int](($maxY - $minY) * $Scale) + 80
$bmp = New-Object Drawing.Bitmap $w, $h
$g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::White)
$font = New-Object Drawing.Font "Arial", 9
function P($x, $y) { New-Object Drawing.PointF ([float](40 + ($x - $minX) * $Scale)), ([float]($h - 40 - ($y - $minY) * $Scale)) }
$gridPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(60, 0, 0, 255))
for ($x = [math]::Floor($minX / 10) * 10; $x -le $maxX; $x += 10) {
    $g.DrawLine($gridPen, (P $x $minY), (P $x $maxY)); $g.DrawString("$x", $font, [Drawing.Brushes]::Blue, (P $x $minY))
}
for ($y = [math]::Floor($minY / 10) * 10; $y -le $maxY; $y += 10) {
    $g.DrawLine($gridPen, (P $minX $y), (P $maxX $y)); $g.DrawString("$y", $font, [Drawing.Brushes]::Blue, (P $minX $y))
}
foreach ($s in $shapes) {
    $brush = if ($s.Layer -eq 'Terrain') { [Drawing.Brushes]::SaddleBrown } else { [Drawing.Brushes]::DarkGreen }
    $pts = [Drawing.PointF[]]($s.Pts | % { P $_[0] $_[1] })
    if ($s.Kind -eq 'poly' -and $pts.Length -ge 3) { $g.FillPolygon($brush, $pts) }
    elseif ($s.Kind -eq 'rect') { $a = $pts[0]; $b = $pts[1]; $g.FillRectangle($brush, [math]::Min($a.X, $b.X), [math]::Min($a.Y, $b.Y), [math]::Abs($b.X - $a.X), [math]::Abs($b.Y - $a.Y)) }
    elseif ($pts.Length -ge 2) { $g.DrawLines([Drawing.Pens]::DarkGreen, $pts) }
}
# start position
$sp = P -44.3 -3.4; $g.FillEllipse([Drawing.Brushes]::Red, $sp.X - 4, $sp.Y - 4, 8, 8)
$bmp.Save($OutFile, [Drawing.Imaging.ImageFormat]::Png); "saved $OutFile ${w}x$h"
