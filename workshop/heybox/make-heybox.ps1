# Images for the Heybox (小黑盒) article, from the same in-game screenshots as the Workshop images:
#   cover.png       1920x1080  feed cover; title and slips kept in the middle so a crop to 4:3 or 1:1 still works
#   01-all.png      1080 wide  six slips in a grid, the "here is what it looks like" shot
#   02-container.png           storage + machine area (contents grid, bars)
#   03-liquid.png              water + wine
#   04-special.png             contraband stamp + the pink not-owned slip
#   05-compare.png             vanilla vs mod, built only when shots\vanilla.png and shots\after.png exist
# Inline images are 1080 wide for phones and carry no text: the article says it.
# Usage: powershell -File make-heybox.ps1   (writes next to this script, in out\)
# Saved as UTF-8 with BOM: Windows PowerShell reads BOM-less files as ANSI and garbles the Chinese.
Add-Type -AssemblyName System.Drawing

$here = $PSScriptRoot
$shots = Join-Path (Split-Path $here) 'shots'
$ownShots = Join-Path $here 'shots'
$OutDir = Join-Path $here 'out'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$paper = [System.Drawing.Color]::FromArgb(255, 238, 227, 198)
$soft = [System.Drawing.Color]::FromArgb(255, 185, 167, 124)
$red = [System.Drawing.Color]::FromArgb(255, 205, 72, 56)
$desk = [System.Drawing.Color]::FromArgb(255, 22, 18, 26)

# Paper, edge and shade of the cream slip and of the pink one.
$cream = @(@(233, 221, 191), @(185, 167, 124), @(222, 207, 170))
$pink = @(@(242, 206, 201), @(198, 134, 130), @(230, 186, 181))

function IsPaper($c, $pal) {
    foreach ($p in $pal) {
        if ([Math]::Abs($c.R - $p[0]) + [Math]::Abs($c.G - $p[1]) + [Math]::Abs($c.B - $p[2]) -lt 30) { return $true }
    }
    return $false
}

# Crops the slip out of a screenshot (rows and columns that are mostly paper) and scales it up 3x
# with nearest neighbour, so later scaling or tilting only softens edges.
function Load-Slip($src, $pal) {
    $w = $src.Width; $h = $src.Height
    $cols = New-Object 'int[]' $w; $rows = New-Object 'int[]' $h
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            if (IsPaper $src.GetPixel($x, $y) $pal) { $cols[$x]++; $rows[$y]++ }
        }
    }
    $l = 0; while ($cols[$l] -lt $h * 0.4) { $l++ }
    $r = $w - 1; while ($cols[$r] -lt $h * 0.4) { $r-- }
    $t = 0; while ($rows[$t] -lt $w * 0.4) { $t++ }
    $b = $h - 1; while ($rows[$b] -lt $w * 0.4) { $b-- }
    $crop = $src.Clone((New-Object System.Drawing.Rectangle $l, $t, ($r - $l + 1), ($b - $t + 1)), $src.PixelFormat)
    $big = New-Object System.Drawing.Bitmap ($crop.Width * 3), ($crop.Height * 3)
    $bg = [System.Drawing.Graphics]::FromImage($big)
    $bg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $bg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $bg.DrawImage($crop, 0, 0, $big.Width, $big.Height)
    $bg.Dispose(); $crop.Dispose()
    return $big
}

function Load-Shot([string]$path, $pal) {
    $src = New-Object System.Drawing.Bitmap $path
    $big = Load-Slip $src $pal
    $src.Dispose()
    return $big
}

function New-Canvas([int]$w, [int]$h, [float]$spotX, [float]$spotY, [float]$spotW, [float]$spotH) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear($desk)
    $spot = New-Object System.Drawing.Drawing2D.GraphicsPath
    $spot.AddEllipse($spotX - $spotW / 2, $spotY - $spotH / 2, $spotW, $spotH)
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $spot
    $pg.CenterColor = [System.Drawing.Color]::FromArgb(255, 74, 58, 52)
    $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 22, 18, 26))
    $g.FillPath($pg, $spot)
    return @($bmp, $g)
}

# A slip centred on (cx, cy), <scale> times its in-game size, tilted by <angle>, with a soft shadow.
function Draw-Slip($g, $img, [float]$cx, [float]$cy, [float]$scale, [float]$angle) {
    $w = $img.Width / 3 * $scale; $h = $img.Height / 3 * $scale
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy)
    $g.RotateTransform($angle)
    foreach ($i in 1..3) {
        $sh = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60 - $i * 15, 0, 0, 0))
        $g.FillRectangle($sh, -$w / 2 + 4 + $i * 4, -$h / 2 + 8 + $i * 5, $w, $h)
    }
    $g.DrawImage($img, -$w / 2, -$h / 2, $w, $h)
    $g.Restore($state)
}

function Font([float]$px, [bool]$bold) {
    $style = if ($bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    return New-Object System.Drawing.Font('Microsoft YaHei', $px, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

function Brush($c) { return New-Object System.Drawing.SolidBrush $c }

function Save($bmp, $g, [string]$name) {
    $g.Dispose()
    $path = Join-Path $OutDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $size = "{0}x{1}" -f $bmp.Width, $bmp.Height
    $bmp.Dispose()
    "{0}  {1}  {2:N0} KB" -f $name, $size, ((Get-Item $path).Length / 1KB)
}

$slip = @{}
foreach ($n in 'storage', 'machine-area', 'water', 'contraband', 'purifier', 'wine') {
    $slip[$n] = Load-Shot (Join-Path $shots "$n.png") $cream
}
# The pink slip: a real pink screenshot if one was dropped in shots\, else the recolour preview.
$pinkShot = Join-Path $ownShots 'not-owned.png'
if (Test-Path $pinkShot) {
    $slip['not-owned'] = Load-Shot $pinkShot $pink
} else {
    $cmp = New-Object System.Drawing.Bitmap (Join-Path (Split-Path $here) 'out\not-owned-colors.png')
    # not-owned-colors.png: four panels of equal width, 24 px apart, pink second, below a 44 px label row.
    $pw = ($cmp.Width - 24) / 4 - 24
    $panel = $cmp.Clone((New-Object System.Drawing.Rectangle (24 + $pw + 24), 44, $pw, ($cmp.Height - 44)), $cmp.PixelFormat)
    $slip['not-owned'] = Load-Slip $panel $pink
    $panel.Dispose(); $cmp.Dispose()
}

# Slips upright in one row on a 1080-wide canvas, scaled to fill it, tops aligned.
function Row([string]$file, [string[]]$names) {
    $gap = 48; $side = 60; $raw = 0; $tall = 0
    foreach ($n in $names) { $raw += $slip[$n].Width / 3 }
    $scale = [Math]::Min(2.2, (1080 - 2 * $side - $gap * ($names.Count - 1)) / $raw)
    foreach ($n in $names) { $tall = [Math]::Max($tall, $slip[$n].Height / 3 * $scale) }
    $h = [int]($tall + 2 * 70)
    $c = New-Canvas 1080 $h 540 ($h / 2) 1400 ($h * 1.3)
    $bmp = $c[0]; $g = $c[1]
    $x = (1080 - $raw * $scale - $gap * ($names.Count - 1)) / 2
    foreach ($n in $names) {
        $w = $slip[$n].Width / 3 * $scale; $sh = $slip[$n].Height / 3 * $scale
        Draw-Slip $g $slip[$n] ($x + $w / 2) (70 + $sh / 2) $scale 0
        $x += $w + $gap
    }
    Save $bmp $g $file
}

# ------------------------------------------------------------------ cover
$c = New-Canvas 1920 1080 960 760 1900 1100
$bmp = $c[0]; $g = $c[1]
Draw-Slip $g $slip['wine'] 600 770 1.35 -10
Draw-Slip $g $slip['storage'] 1320 790 1.35 9
Draw-Slip $g $slip['contraband'] 960 740 1.6 -2
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$title = Font 128 $true
$g.DrawString('物品详情美化', $title, (Brush ([System.Drawing.Color]::FromArgb(170, 0, 0, 0))), (New-Object System.Drawing.RectangleF 4, 46, 1920, 170), $fmt)
$g.DrawString('物品详情美化', $title, (Brush $paper), (New-Object System.Drawing.RectangleF 0, 40, 1920, 170), $fmt)
$sub = Font 36 $true
$ribbon = 'Probably Stolen 模组 · 估价单风格'
$rs = $g.MeasureString($ribbon, $sub)
$rx = (1920 - $rs.Width - 52) / 2
$g.FillRectangle((Brush $red), $rx, 200, $rs.Width + 52, 60)
$g.DrawString($ribbon, $sub, (Brush $paper), $rx + 26, 207)
Save $bmp $g 'cover.png'

# ------------------------------------------------------------------ all six
$grid = @(@('contraband', 'storage', 'water'), @('wine', 'purifier', 'machine-area'))
$gap = 36; $side = 48
# Slips differ in width; one scale for both rows, set by the wider row.
$scale = 9
foreach ($r in $grid) {
    $raw = 0; foreach ($n in $r) { $raw += $slip[$n].Width / 3 }
    $scale = [Math]::Min($scale, (1080 - 2 * $side - 2 * $gap) / $raw)
}
$rowH = @()
foreach ($r in $grid) { $t = 0; foreach ($n in $r) { $t = [Math]::Max($t, $slip[$n].Height / 3 * $scale) }; $rowH += $t }
$h = [int]($rowH[0] + $rowH[1] + $gap + 2 * 60)
$c = New-Canvas 1080 $h 540 ($h / 2) 1500 ($h * 1.2)
$bmp = $c[0]; $g = $c[1]
$y = 60
for ($ri = 0; $ri -lt 2; $ri++) {
    $raw = 0; foreach ($n in $grid[$ri]) { $raw += $slip[$n].Width / 3 }
    $x = (1080 - $raw * $scale - 2 * $gap) / 2
    foreach ($n in $grid[$ri]) {
        $w = $slip[$n].Width / 3 * $scale; $sh = $slip[$n].Height / 3 * $scale
        Draw-Slip $g $slip[$n] ($x + $w / 2) ($y + $sh / 2) $scale 0
        $x += $w + $gap
    }
    $y += $rowH[$ri] + $gap
}
Save $bmp $g '01-all.png'

Row '02-container.png' @('storage', 'machine-area')
Row '03-liquid.png' @('water', 'wine')
Row '04-special.png' @('contraband', 'not-owned')

# ------------------------------------------------------------------ before / after
# Two full screenshots, not crops: shots\vanilla.png (mod off) and shots\after.png (mod on), same item.
$vanilla = Join-Path $ownShots 'vanilla.png'
$after = Join-Path $ownShots 'after.png'
if ((Test-Path $vanilla) -and (Test-Path $after)) {
    $a = [System.Drawing.Image]::FromFile($vanilla); $b = [System.Drawing.Image]::FromFile($after)
    $gap = 40; $side = 50; $label = 90
    $scale = [Math]::Min(3.0, (1080 - 2 * $side - $gap) / ($a.Width + $b.Width))
    $tall = [Math]::Max($a.Height, $b.Height) * $scale
    $h = [int]($tall + $label + 2 * 50)
    $c = New-Canvas 1080 $h 540 ($h / 2) 1400 ($h * 1.3)
    $bmp = $c[0]; $g = $c[1]
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $x = (1080 - ($a.Width + $b.Width) * $scale - $gap) / 2
    $f = Font 40 $true
    foreach ($pair in @(@($a, '原版', $soft), @($b, '装上之后', $paper))) {
        $img = $pair[0]; $w = $img.Width * $scale
        $g.DrawString($pair[1], $f, (Brush $pair[2]), $x, 50)
        $g.DrawImage($img, $x, 50 + $label, $w, $img.Height * $scale)
        $x += $w + $gap
    }
    $a.Dispose(); $b.Dispose()
    Save $bmp $g '05-compare.png'
} else {
    '05-compare.png skipped: put shots\vanilla.png and shots\after.png next to this script first'
}

foreach ($k in @($slip.Keys)) { $slip[$k].Dispose() }
