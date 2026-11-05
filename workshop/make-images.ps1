# Workshop images from in-game screenshots of the slip (workshop\shots\*.png):
#   cover.png      1024x1024  the main preview: headline, the vanilla tooltip, an arrow and the slip
#   gallery-0.png  1920x1080  before and after, when shots/vanilla-hydro.png exists: vanilla tooltip, arrow, slip
#   gallery-1.png  1920x1080  machines: status, upgrades, module effects
#   gallery-2.png  1920x1080  containers and machine slots (contents, gauges)
#   gallery-3.png  1920x1080  stolen tag and contraband stamp
#   gallery-4.png  1920x1080  the paper colours
# Usage: powershell -File make-images.ps1 [outDir]   (default: workshop\out)
# Saved as UTF-8 with BOM: Windows PowerShell reads BOM-less files as ANSI and garbles the Chinese.
param([string]$OutDir = (Join-Path $PSScriptRoot 'out'))
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PaperRecolor
{
    // Repaints a slip in another paper. from / to: paper, shade, edge, soft ink as r,g,b triples; keep: inks
    // that are the same on every paper. Each pixel is read as a blend of paper (or shade) and one other
    // colour, so the anti-aliased edges of text move with the paper instead of keeping the old tint.
    public static void Apply(Bitmap bmp, int[] from, int[] to, int[] keep)
    {
        int nf = from.Length / 3, nk = keep.Length / 3, n = nf + nk;
        int[] src = new int[n * 3], dst = new int[n * 3];
        Array.Copy(from, src, nf * 3); Array.Copy(to, dst, nf * 3);
        Array.Copy(keep, 0, src, nf * 3, nk * 3); Array.Copy(keep, 0, dst, nf * 3, nk * 3);
        Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int[] px = new int[bmp.Width * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        for (int i = 0; i < px.Length; i++)
        {
            int c = px[i], r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            double best = 1e9, bt = 0; int bb = -1, bo = -1;
            for (int a = 0; a < 2; a++)
                for (int o = 0; o < n; o++)
                {
                    if (o == a) continue;
                    double dr = src[o * 3] - src[a * 3], dg = src[o * 3 + 1] - src[a * 3 + 1], db = src[o * 3 + 2] - src[a * 3 + 2];
                    double len = dr * dr + dg * dg + db * db;
                    double t = ((r - src[a * 3]) * dr + (g - src[a * 3 + 1]) * dg + (b - src[a * 3 + 2]) * db) / len;
                    t = Math.Max(0, Math.Min(1, t));
                    double er = r - (src[a * 3] + t * dr), eg = g - (src[a * 3 + 1] + t * dg), eb = b - (src[a * 3 + 2] + t * db);
                    double e = er * er + eg * eg + eb * eb;
                    if (e < best) { best = e; bt = t; bb = a; bo = o; }
                }
            if (best > 120) continue;
            int nr = (int)Math.Round(dst[bb * 3] + bt * (dst[bo * 3] - dst[bb * 3]));
            int ng = (int)Math.Round(dst[bb * 3 + 1] + bt * (dst[bo * 3 + 1] - dst[bb * 3 + 1]));
            int nb = (int)Math.Round(dst[bb * 3 + 2] + bt * (dst[bo * 3 + 2] - dst[bb * 3 + 2]));
            px[i] = (255 << 24) | (nr << 16) | (ng << 8) | nb;
        }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
    }
}
'@

$shots = Join-Path $PSScriptRoot 'shots'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$paper = [System.Drawing.Color]::FromArgb(255, 238, 227, 198)
$soft = [System.Drawing.Color]::FromArgb(255, 185, 167, 124)
$red = [System.Drawing.Color]::FromArgb(255, 205, 72, 56)
$desk = [System.Drawing.Color]::FromArgb(255, 22, 18, 26)
# The canvas colours New-Canvas paints: a cover may switch them before it starts.
$base = $desk
$glow = [System.Drawing.Color]::FromArgb(255, 74, 58, 52)

# The papers in the mod's settings (Ticket.Papers): paper, shade, edge, soft ink, and for the dark ones
# their light ink (the light papers all use the brown ink).
$papers = [ordered]@{
    '米黄' = @(233, 221, 191, 222, 207, 170, 185, 167, 124, 122, 100, 64)
    '粉'   = @(242, 206, 201, 230, 186, 181, 198, 134, 130, 140, 62, 60)
    '蓝'   = @(204, 220, 232, 184, 203, 219, 122, 150, 176, 52, 82, 116)
    '绿'   = @(208, 228, 200, 188, 213, 179, 124, 160, 112, 54, 96, 50)
    '黄'   = @(239, 226, 166, 227, 211, 143, 196, 173, 94, 138, 109, 31)
    '炭黑' = @(40, 37, 42, 52, 48, 55, 92, 85, 94, 164, 154, 142, 232, 224, 208)
    '藏青' = @(30, 40, 58, 40, 52, 73, 80, 98, 126, 142, 162, 188, 222, 230, 240)
    '墨绿' = @(31, 47, 39, 41, 61, 51, 80, 108, 92, 146, 176, 156, 224, 236, 226)
    '深棕' = @(56, 42, 32, 70, 54, 42, 114, 92, 72, 186, 162, 130, 240, 228, 206)
}
# Ink (59, 42, 26) and black stay the same on every light paper; a dark paper turns the ink light.
$brownInk = @(59, 42, 26)
$inks = [int[]]@(59, 42, 26, 0, 0, 0)

function IsPaper($c, $pal) {
    for ($i = 0; $i -lt 9; $i += 3) {
        if ([Math]::Abs($c.R - $pal[$i]) + [Math]::Abs($c.G - $pal[$i + 1]) + [Math]::Abs($c.B - $pal[$i + 2]) -lt 30) { return $true }
    }
    return $false
}

# ------------------------------------------------------------------ category tags (1.4.0)
# The tag line in each shot (rows within the cropped slip) and its tags' colours, as the mod's tag groups
# have them (Ticket.TagPalette): the shots were taken when tags were plain soft-ink text.
$tagColors = @{
    'purple' = @(85, 58, 133, 195, 166, 255); 'blue' = @(21, 101, 192, 115, 192, 255)
    'black' = @(18, 18, 18, 208, 208, 208); 'red' = @(87, 26, 28, 255, 148, 133); 'grey' = @(85, 85, 85, 192, 192, 192)
}
# Shots from before 1.4.0 had plain "[name]" tag text to restyle; every shot now has the chips already.
$tagLines = @{}
$tagU = 2 # one pixel of the slip's pixel art, in screenshot pixels

function Near($c, [int]$r, [int]$g, [int]$b, [int]$d) { [Math]::Abs($c.R - $r) + [Math]::Abs($c.G - $g) + [Math]::Abs($c.B - $b) -lt $d }

# Lifts each "[name]" off the tag line as an ink mask without its brackets, blanks the line, and makes room
# below it for the taller chips. Returns the new bitmap and where each chip goes.
function Restyle-Tags($bmp, $spec, $pal) {
    $w = $bmp.Width; $y0 = $spec.Y0; $y1 = $spec.Y1; $h = $y1 - $y0 + 1
    $pr = $pal[0]; $pg = $pal[1]; $pb = $pal[2]
    $alpha = New-Object 'double[,]' $w, $h
    $strong = New-Object 'bool[]' $w
    for ($x = 66; $x -lt $w - 6; $x++) {
        for ($y = 0; $y -lt $h; $y++) {
            $c = $bmp.GetPixel($x, $y0 + $y)
            $d = [Math]::Abs($c.R - $pr) + [Math]::Abs($c.G - $pg) + [Math]::Abs($c.B - $pb)
            $a = [Math]::Min(1.0, $d / 340.0) # soft ink is about 360 from the paper
            if ($a -gt 0.12) { $alpha[$x, $y] = $a }
            if ($d -gt 120) { $strong[$x] = $true }
        }
    }
    # The shots are slightly blurred, so a tag's brackets run into its name: a tag is a span of inked
    # columns (gaps under 3 bridged), and its brackets the outer 4 columns each side (a tag is 33 wide
    # with two characters, 46 with three: 13 per character, 3.5 per bracket).
    $spans = @(); $x = 66
    while ($x -lt $w - 6) {
        if (-not $strong[$x]) { $x++; continue }
        $s = $x; $last = $x
        while ($x -lt $w - 6 -and ($strong[$x] -or $x - $last -lt 3)) { if ($strong[$x]) { $last = $x }; $x++ }
        $spans += , @($s, $last)
    }
    if ($spans.Count -ne $spec.Colors.Count) { throw "tag line: found $($spans.Count) tags, expected $($spec.Colors.Count)" }

    $x0 = $spans[0][0]
    $chips = @()
    for ($i = 0; $i -lt $spans.Count; $i++) {
        $ns = $spans[$i][0] + 4; $ne = $spans[$i][1] - 4
        $m = New-Object 'double[,]' ($ne - $ns + 1), $h
        for ($xx = $ns; $xx -le $ne; $xx++) { for ($y = 0; $y -lt $h; $y++) { $m[($xx - $ns), $y] = $alpha[$xx, $y] } }
        $chips += , @{ Mask = $m; W = $ne - $ns + 1; Color = $tagColors[$spec.Colors[$i]] }
    }

    # Blank the old line.
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $pr, $pg, $pb))), $x0 - 1, $y0, $w - 6 - $x0, $h)
    $g.Dispose()

    # Lay the chips out as DrawChips does: 3U padding across, 2U down, 2U apart, 2U below the line above;
    # wrapping at the slip's right padding.
    $px = 3 * $tagU; $py = 2 * $tagU; $gap = 2 * $tagU
    $right = $w - 12
    $cx = $x0; $cy = $y0 + 2 * $tagU; $ch = $h + 2 * $py
    foreach ($c in $chips) {
        $cw = $c.W + 2 * $px
        if ($cx -gt $x0 -and $cx + $cw -gt $right) { $cx = $x0; $cy += $ch + $gap }
        $c.X = $cx; $c.Y = $cy; $c.CW = $cw; $c.CH = $ch; $c.PX = $px; $c.PY = $py
        $cx += $cw + $gap
    }
    $bottom = $cy + $ch

    # Room for them: copies of the first clean row under the old line (paper across, so the icon box is
    # never stretched), as many as the chips reach past it.
    $clean = $y1 + 1
    while ($clean -lt $bmp.Height) {
        $ok = $true
        for ($x = 6; $x -lt $w - 6; $x++) { if (-not (Near $bmp.GetPixel($x, $clean) $pr $pg $pb 30)) { $ok = $false; break } }
        if ($ok) { break }
        $clean++
    }
    $add = [Math]::Max(0, $bottom + $tagU - $clean)
    if ($add -eq 0) { return @{ Bmp = $bmp; Chips = $chips } }
    $out = New-Object System.Drawing.Bitmap $w, ($bmp.Height + $add), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, $w, $clean), (New-Object System.Drawing.Rectangle 0, 0, $w, $clean), 'Pixel')
    for ($k = 0; $k -lt $add; $k++) {
        $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, ($clean + $k), $w, 1), (New-Object System.Drawing.Rectangle 0, $clean, $w, 1), 'Pixel')
    }
    $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, ($clean + $add), $w, ($bmp.Height - $clean)), (New-Object System.Drawing.Rectangle 0, $clean, $w, ($bmp.Height - $clean)), 'Pixel')
    $g.Dispose(); $bmp.Dispose()
    return @{ Bmp = $out; Chips = $chips }
}

# Draws the chips on the slip's paper as the mod does (Ticket.ChipInks): a faint wash of the tag colour and
# the name in a darker shade; on a dark paper a stronger wash and the light shade.
function Draw-Chips($bmp, $chips, $pal) {
    $pr = $pal[0]; $pg = $pal[1]; $pb = $pal[2]
    $dark = (0.299 * $pr + 0.587 * $pg + 0.114 * $pb) / 255 -lt 0.4
    foreach ($c in $chips) {
        $k = $c.Color
        $t = if ($dark) { 0.45 } else { 0.2 }
        $wash = [System.Drawing.Color]::FromArgb(255, [int]($pr + ($k[0] - $pr) * $t), [int]($pg + ($k[1] - $pg) * $t), [int]($pb + ($k[2] - $pb) * $t))
        $ink = if ($dark) { @($k[3], $k[4], $k[5]) } else { @([int]($k[0] * 0.85), [int]($k[1] * 0.85), [int]($k[2] * 0.85)) }
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush $wash), $c.X, $c.Y, $c.CW, $c.CH)
        $g.Dispose()
        $ox = $c.X + $c.PX; $oy = $c.Y + $c.PY
        for ($x = 0; $x -lt $c.W; $x++) {
            for ($y = 0; $y -lt $c.CH - 2 * $c.PY; $y++) {
                $a = $c.Mask[$x, $y]; if ($a -le 0) { continue }
                $b = $bmp.GetPixel($ox + $x, $oy + $y)
                $bmp.SetPixel($ox + $x, $oy + $y, [System.Drawing.Color]::FromArgb(255, [int]($b.R + ($ink[0] - $b.R) * $a), [int]($b.G + ($ink[1] - $b.G) * $a), [int]($b.B + ($ink[2] - $b.B) * $a)))
            }
        }
    }
}

# The screenshots carry a few pixels of game scene around the slip; keep only the slip itself
# (its outline included), found as the rows and columns that are mostly paper. <paper> names the paper
# the screenshot was taken on; <as>, when given, repaints it in that paper.
function Load-Slip([string]$name, [string]$on = '米黄', [string]$as = '') {
    $pal = $papers[$on]
    $src = New-Object System.Drawing.Bitmap (Join-Path $shots "$name.png")
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
    $crop = $src.Clone((New-Object System.Drawing.Rectangle $l, $t, ($r - $l + 1), ($b - $t + 1)), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $src.Dispose()
    # Shots taken before 1.4.0 have the tags as plain "[机器]" text: lifted off and drawn again as chips.
    $chips = $null
    if ($tagLines.Contains($name)) {
        $re = Restyle-Tags $crop $tagLines[$name] $pal
        $crop = $re.Bmp
        $chips = $re.Chips
    }
    if ($as -and $papers[$as].Count -gt 12) {
        [PaperRecolor]::Apply($crop, [int[]]($pal + $brownInk), [int[]]$papers[$as], [int[]]@(0, 0, 0))
    }
    elseif ($as) { [PaperRecolor]::Apply($crop, [int[]]$pal, [int[]]$papers[$as], $inks) }
    if ($chips) { Draw-Chips $crop $chips $(if ($as) { $papers[$as] } else { $pal }) }
    # Up 3x with nearest neighbour, so scaling down a little later (or tilting) only softens edges.
    $big = New-Object System.Drawing.Bitmap ($crop.Width * 3), ($crop.Height * 3)
    $bg = [System.Drawing.Graphics]::FromImage($big)
    $bg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $bg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $bg.DrawImage($crop, 0, 0, $big.Width, $big.Height)
    $bg.Dispose(); $crop.Dispose()
    return $big
}

# A vanilla tooltip screenshot (shots\vanilla-*.png), up 3x like Load-Slip. The screenshots keep a few
# pixels of game scene around the tooltip, so it is cut to its red-brown frame: the outermost rows and
# columns that are mostly frame colour.
function Load-Vanilla([string]$name) {
    $src = New-Object System.Drawing.Bitmap (Join-Path $shots "vanilla-$name.png")
    $w = $src.Width; $h = $src.Height
    $cols = New-Object 'int[]' $w; $rows = New-Object 'int[]' $h
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $src.GetPixel($x, $y)
            if ([Math]::Abs($c.R - 155) + [Math]::Abs($c.G - 74) + [Math]::Abs($c.B - 61) -lt 30) { $cols[$x]++; $rows[$y]++ }
        }
    }
    $l = 0; while ($cols[$l] -lt $h * 0.4) { $l++ }
    $r = $w - 1; while ($cols[$r] -lt $h * 0.4) { $r-- }
    $t = 0; while ($rows[$t] -lt $w * 0.4) { $t++ }
    $b = $h - 1; while ($rows[$b] -lt $w * 0.4) { $b-- }
    $crop = $src.Clone((New-Object System.Drawing.Rectangle $l, $t, ($r - $l + 1), ($b - $t + 1)), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $src.Dispose()
    $big = New-Object System.Drawing.Bitmap ($crop.Width * 3), ($crop.Height * 3)
    $bg = [System.Drawing.Graphics]::FromImage($big)
    $bg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $bg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $bg.DrawImage($crop, 0, 0, $big.Width, $big.Height)
    $bg.Dispose(); $crop.Dispose()
    return $big
}

# A fat arrow pointing right, centred on (cx, cy).
function Draw-Arrow($g, [float]$cx, [float]$cy, [float]$len, $color) {
    $t = $len * 0.22; $head = $len * 0.45; $hw = $len * 0.42
    $x0 = $cx - $len / 2; $x1 = $cx + $len / 2
    $pts = @(
        (New-Object System.Drawing.PointF $x0, ($cy - $t / 2)),
        (New-Object System.Drawing.PointF ($x1 - $head), ($cy - $t / 2)),
        (New-Object System.Drawing.PointF ($x1 - $head), ($cy - $hw)),
        (New-Object System.Drawing.PointF $x1, $cy),
        (New-Object System.Drawing.PointF ($x1 - $head), ($cy + $hw)),
        (New-Object System.Drawing.PointF ($x1 - $head), ($cy + $t / 2)),
        (New-Object System.Drawing.PointF $x0, ($cy + $t / 2))
    )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $color), [System.Drawing.PointF[]]$pts)
}

# A small caption centred over a picture whose top edge is at y.
function Draw-Caption($g, [string]$text, [float]$cx, [float]$y, $color) {
    $f = Font 30 $true
    $s = $g.MeasureString($text, $f)
    $g.DrawString($text, $f, (Brush $color), $cx - $s.Width / 2, $y - $s.Height - 10)
}

function New-Canvas([int]$w, [int]$h, [float]$spotX, [float]$spotY, [float]$spotW, [float]$spotH) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear($script:base)
    # A warm spotlight where the slips lie.
    $spot = New-Object System.Drawing.Drawing2D.GraphicsPath
    $spot.AddEllipse($spotX - $spotW / 2, $spotY - $spotH / 2, $spotW, $spotH)
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $spot
    $pg.CenterColor = $script:glow
    $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $script:base.R, $script:base.G, $script:base.B))
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
    $bmp.Dispose()
    "{0}  {1:N0} KB" -f $path, ((Get-Item $path).Length / 1KB)
}

$slip = @{}
foreach ($n in 'contraband', 'hydro', 'aquaculture', 'storage', 'furnace', 'stolen') { $slip[$n] = Load-Slip $n }
# The same short slip on four of the other papers, for the colour settings: the light ones repainted from
# the cream shot, the dark ones shot in game (repainting muddied their tag chips).
foreach ($p in '粉', '蓝') { $slip["contraband-$p"] = Load-Slip 'contraband' '米黄' $p }
foreach ($p in '炭黑', '藏青') { $slip["contraband-$p"] = Load-Slip "contraband-$p" $p }

# The game's own tooltip of the same items (taken with the mod switched off), where there is one.
$vanilla = @{}
foreach ($n in 'contraband', 'hydro') { if (Test-Path (Join-Path $shots "vanilla-$n.png")) { $vanilla[$n] = Load-Vanilla $n } }

# ------------------------------------------------------------------ cover
# The same item before and after, so even the small Workshop thumbnail says what the mod changes.
$c = New-Canvas 1024 1024 512 680 1184 900
$bmp = $c[0]; $g = $c[1]
$vs = 1.12; $ss = 1.12  # same scale: the slip must not look bigger than it is
$vw = $vanilla['contraband'].Width / 3 * $vs; $vh = $vanilla['contraband'].Height / 3 * $vs
$sw = $slip['contraband'].Width / 3 * $ss; $sh = $slip['contraband'].Height / 3 * $ss
$arrow = 64; $gapX = 26
$x0 = (1024 - ($vw + $sw + $arrow + 2 * $gapX)) / 2
$midY = 620
Draw-Slip $g $vanilla['contraband'] ($x0 + $vw / 2) $midY $vs 0
Draw-Arrow $g ($x0 + $vw + $gapX + $arrow / 2) $midY $arrow $red
Draw-Slip $g $slip['contraband'] ($x0 + $vw + 2 * $gapX + $arrow + $sw / 2) $midY $ss 0
Draw-Caption $g '原版' ($x0 + $vw / 2) ($midY - $vh / 2) $soft
Draw-Caption $g '装上后' ($x0 + $vw + 2 * $gapX + $arrow + $sw / 2) ($midY - $sh / 2) $paper

$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$title = Font 112 $true
$g.DrawString('物品详情美化', $title, (Brush ([System.Drawing.Color]::FromArgb(170, 0, 0, 0))), (New-Object System.Drawing.RectangleF 4, 32, 1024, 150), $fmt)
$g.DrawString('物品详情美化', $title, (Brush $paper), (New-Object System.Drawing.RectangleF 0, 26, 1024, 150), $fmt)
$sub = Font 34 $true
$ribbon = '把物品提示框改成估价单 · Better Item Tooltips'
$rs = $g.MeasureString($ribbon, $sub)
$rx = (1024 - $rs.Width - 48) / 2
$g.FillRectangle((Brush $red), $rx, 182, $rs.Width + 48, 56)
$g.DrawString($ribbon, $sub, (Brush $paper), $rx + 24, 188)
Save $bmp $g 'cover.png'

# ------------------------------------------------------------------ other cover styles, to choose from
# The headline and the ribbon under it, as on cover.png, in the given colours.
function Draw-Headline($g, $titleColor, $ribbonColor, $ribbonText, [float]$top = 26) {
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $title = Font 112 $true
    $g.DrawString('物品详情美化', $title, (Brush ([System.Drawing.Color]::FromArgb(150, 0, 0, 0))), (New-Object System.Drawing.RectangleF 4, ($top + 6), 1024, 150), $fmt)
    $g.DrawString('物品详情美化', $title, (Brush $titleColor), (New-Object System.Drawing.RectangleF 0, $top, 1024, 150), $fmt)
    $sub = Font 34 $true
    $text = '把物品提示框改成估价单 · Better Item Tooltips'
    $rs = $g.MeasureString($text, $sub)
    $rx = (1024 - $rs.Width - 48) / 2
    $g.FillRectangle((Brush $ribbonColor), $rx, $top + 156, $rs.Width + 48, 56)
    $g.DrawString($text, $sub, (Brush $ribbonText), $rx + 24, $top + 162)
}

# An image like Draw-Slip but see-through (<alpha> 0..1), for the old tooltip lying underneath.
function Draw-Faded($g, $img, [float]$cx, [float]$cy, [float]$scale, [float]$angle, [float]$alpha) {
    $w = $img.Width / 3 * $scale; $h = $img.Height / 3 * $scale
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix33 = $alpha
    $ia = New-Object System.Drawing.Imaging.ImageAttributes
    $ia.SetColorMatrix($cm)
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy)
    $g.RotateTransform($angle)
    $g.FillRectangle((Brush ([System.Drawing.Color]::FromArgb([int](70 * $alpha), 0, 0, 0))), -$w / 2 + 10, -$h / 2 + 14, $w, $h)
    $dest = New-Object System.Drawing.Rectangle ([int](-$w / 2)), ([int](-$h / 2)), ([int]$w), ([int]$h)
    $g.DrawImage($img, $dest, 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
    $g.Restore($state)
}

# A label joined by a thin line to a dot on the slip: from the label's right end, or with <above>
# from under its middle.
function Draw-Callout($g, [string]$text, [float]$px, [float]$py, [float]$lx, [float]$ly, [bool]$above) {
    $f = Font 32 $true
    $s = $g.MeasureString($text, $f)
    $pen = New-Object System.Drawing.Pen $paper, 3
    if ($above) { $g.DrawLine($pen, $lx + $s.Width / 2, $ly + $s.Height + 4, $px, $py) }
    else { $g.DrawLine($pen, $lx + $s.Width + 8, $ly + $s.Height / 2, $px, $py) }
    $g.FillEllipse((Brush $red), $px - 9, $py - 9, 18, 18)
    $g.FillEllipse((Brush $paper), $px - 4, $py - 4, 8, 8)
    $g.DrawString($text, $f, (Brush $paper), $lx, $ly)
}


# cover-wide: 16:9 for the Workshop, where the browse grid shows it about 230 px wide. Only a big headline
# and the short contraband pair, both at the same large scale, so it still reads that small.
function Cover-Wide([string]$file, [string]$head) {
    $c = New-Canvas 1920 1080 960 640 2200 1300
    $bmp = $c[0]; $g = $c[1]
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $hf = Font 120 $true
    $g.DrawString($head, $hf, (Brush ([System.Drawing.Color]::FromArgb(150, 0, 0, 0))), (New-Object System.Drawing.RectangleF 5, 37, 1920, 170), $fmt)
    $g.DrawString($head, $hf, (Brush $paper), (New-Object System.Drawing.RectangleF 0, 30, 1920, 170), $fmt)
    # As large as the width allows (both at the same scale), and in the middle of the room under the headline.
    $arrow = 120; $gapX = 44
    $raw = ($vanilla['contraband'].Width + $slip['contraband'].Width) / 3
    $rawTall = [Math]::Max($vanilla['contraband'].Height, $slip['contraband'].Height) / 3
    $ws = [Math]::Min(2.2, [Math]::Min((1920 - 160 - $arrow - 2 * $gapX) / $raw, (1080 - 250) / $rawTall))
    $vw = $vanilla['contraband'].Width / 3 * $ws
    $sw = $slip['contraband'].Width / 3 * $ws; $sh = $slip['contraband'].Height / 3 * $ws
    $x0 = (1920 - ($vw + $sw + $arrow + 2 * $gapX)) / 2
    $midY = (210 + 1080) / 2
    Draw-Slip $g $vanilla['contraband'] ($x0 + $vw / 2) $midY $ws 0
    Draw-Arrow $g ($x0 + $vw + $gapX + $arrow / 2) $midY $arrow $red
    Draw-Slip $g $slip['contraband'] ($x0 + $vw + 2 * $gapX + $arrow + $sw / 2) $midY $ws 0
    Save $bmp $g $file
}
Cover-Wide 'cover-wide.png' '原版 → 物品详情美化'
Cover-Wide 'cover-wide-short.png' '原版 → 美化'

# ------------------------------------------------------------------ gallery
# Headline and a few lines on the left, the slips upright on the right, tops aligned.
function Gallery([string]$file, [string]$head, [string[]]$lines, [string[]]$names, [bool]$swatches = $false) {
    $c = New-Canvas 1920 1080 1260 560 1500 1100
    $bmp = $c[0]; $g = $c[1]

    # The text column keeps the left 600 px; the slips share the rest, never above 1.55x and never
    # taller than the picture.
    $gap = 34; $raw = 0; $rawTall = 0
    foreach ($n in $names) { $raw += $slip[$n].Width / 3; $rawTall = [Math]::Max($rawTall, $slip[$n].Height / 3) }
    $scale = [Math]::Min(1.55, (1920 - 70 - 600 - $gap * ($names.Count - 1)) / $raw)
    $scale = [Math]::Min($scale, (1080 - 90) / $rawTall)
    $total = $raw * $scale + $gap * ($names.Count - 1); $tall = $rawTall * $scale
    $x = 1920 - 70 - $total
    $top = [Math]::Max(40, (1080 - $tall) / 2)
    foreach ($n in $names) {
        $w = $slip[$n].Width / 3 * $scale; $h = $slip[$n].Height / 3 * $scale
        Draw-Slip $g $slip[$n] ($x + $w / 2) ($top + $h / 2) $scale 0
        $x += $w + $gap
    }

    $left = 80
    $g.DrawString($head, (Font 76 $true), (Brush $paper), $left, 300)
    $g.FillRectangle((Brush $red), $left + 4, 410, 120, 8)
    $y = 460
    $f = Font 32 $false
    foreach ($l in $lines) {
        $g.FillRectangle((Brush $soft), $left + 6, $y + 19, 8, 8)
        $g.DrawString($l, $f, (Brush $paper), $left + 28, $y)
        $y += 64
    }
    if ($swatches) {
        # The settings' papers as small sheets, each with its edge and name: the light ones, then the dark
        # ones on a row of their own with a line of their ink across.
        $y += 12; $sf = Font 22 $false
        foreach ($dark in $false, $true) {
            $x = $left + 28
            foreach ($k in $papers.Keys) {
                $p = $papers[$k]
                if (($p.Count -gt 12) -ne $dark) { continue }
                $g.FillRectangle((Brush ([System.Drawing.Color]::FromArgb(255, $p[6], $p[7], $p[8]))), $x, $y, 76, 56)
                $g.FillRectangle((Brush ([System.Drawing.Color]::FromArgb(255, $p[0], $p[1], $p[2]))), $x + 3, $y + 3, 70, 50)
                $ink = if ($dark) { $p[12..14] } else { $brownInk }
                $g.FillRectangle((Brush ([System.Drawing.Color]::FromArgb(255, $ink[0], $ink[1], $ink[2]))), $x + 14, $y + 22, 48, 4)
                $g.FillRectangle((Brush ([System.Drawing.Color]::FromArgb(255, $p[9], $p[10], $p[11]))), $x + 14, $y + 32, 32, 4)
                $ks = $g.MeasureString($k, $sf)
                $g.DrawString($k, $sf, (Brush $soft), $x + (76 - $ks.Width) / 2, $y + 62)
                $x += 96
            }
            $y += 104
        }
    }
    $g.DrawString('物品详情美化 | Better Item Tooltips', (Font 24 $false), (Brush $soft), $left + 4, 980)
    Save $bmp $g $file
}

# Before and after: headline and lines on the left, the vanilla tooltip, an arrow and the slip on the right.
function Show-BeforeAfter([string]$file, [string]$head, [string[]]$lines, [string]$name) {
    $c = New-Canvas 1920 1080 1260 560 1500 1100
    $bmp = $c[0]; $g = $c[1]
    $v = $vanilla[$name]; $s = $slip[$name]
    $arrow = 80; $gapX = 36
    $rawW = $v.Width / 3 + $s.Width / 3; $rawH = [Math]::Max($v.Height, $s.Height) / 3
    $scale = [Math]::Min(1.6, [Math]::Min((1920 - 70 - 640 - $arrow - 2 * $gapX) / $rawW, (1080 - 150) / $rawH))
    $vw = $v.Width / 3 * $scale; $vh = $v.Height / 3 * $scale
    $sw = $s.Width / 3 * $scale; $sh = $s.Height / 3 * $scale
    $x = 1920 - 70 - ($vw + $sw + $arrow + 2 * $gapX)
    $midY = 1080 / 2 + 30
    Draw-Slip $g $v ($x + $vw / 2) $midY $scale 0
    Draw-Arrow $g ($x + $vw + $gapX + $arrow / 2) $midY $arrow $red
    Draw-Slip $g $s ($x + $vw + 2 * $gapX + $arrow + $sw / 2) $midY $scale 0
    Draw-Caption $g '原版' ($x + $vw / 2) ($midY - $vh / 2) $soft
    Draw-Caption $g '装上后' ($x + $vw + 2 * $gapX + $arrow + $sw / 2) ($midY - $sh / 2) $paper

    $left = 80
    $g.DrawString($head, (Font 76 $true), (Brush $paper), $left, 300)
    $g.FillRectangle((Brush $red), $left + 4, 410, 120, 8)
    $y = 460
    $f = Font 32 $false
    foreach ($l in $lines) {
        $g.FillRectangle((Brush $soft), $left + 6, $y + 19, 8, 8)
        $g.DrawString($l, $f, (Brush $paper), $left + 28, $y)
        $y += 64
    }
    $g.DrawString('物品详情美化 | Better Item Tooltips', (Font 24 $false), (Brush $soft), $left + 4, 980)
    Save $bmp $g $file
}

if ($vanilla.Contains('hydro')) {
    Show-BeforeAfter 'gallery-0.png' '原版 → 估价单' @(
        '售价放大，放在最上面',
        '状态、能耗、模组效果分组列出',
        '名称和数值两列对齐'
    ) 'hydro'
}

Gallery 'gallery-1.png' '机器' @(
    '名称和数值两列对齐，一眼找到',
    '机器状态放在最前面',
    '升级显示为格子，模组效果用色块区分'
) @('hydro', 'aquaculture')

Gallery 'gallery-2.png' '容器 · 槽位' @(
    '里面装的东西画成图标',
    '同样的合在一起，标出数量',
    '电池和液体显示为量条'
) @('storage', 'furnace')

Gallery 'gallery-3.png' '赃物 · 违禁品' @(
    '赃物在名字下面贴红标签，写着热度',
    '违禁品盖一个红章',
    '售价放大，放在最上面'
) @('stolen', 'contraband')

Gallery 'gallery-4.png' '纸色' @(
    '九种纸色：五种浅色，四种深色',
    '自己的和未拥有的物品分开设置',
    '未拥有的默认用粉色'
) @('contraband-粉', 'contraband-蓝', 'contraband-炭黑', 'contraband-藏青') $true

foreach ($k in @($slip.Keys)) { $slip[$k].Dispose() }
foreach ($k in @($vanilla.Keys)) { $vanilla[$k].Dispose() }
