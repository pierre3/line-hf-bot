#requires -Version 7
<#
.SYNOPSIS
    Generate LINE rich menu images (mode switcher) with System.Drawing.
    Produces three 2500x843 PNGs, one per active mode (chat / image / video),
    so an alias-based rich menu switch can highlight the current mode.

.NOTES
    Windows only (System.Drawing / GDI+). Output: ./out/richmenu-<mode>.png
    LINE constraints: width 2500, height 843 (compact) or 1686, <= 1 MB, PNG/JPEG.
#>
[CmdletBinding()]
param(
    # 'en' is the default for the published image; 'ja' is the switchable Japanese variant.
    [ValidateSet('en', 'ja')]
    [string]$Locale = 'en',
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$W = 2500; $H = 843
$OutDir = Join-Path $OutDir $Locale
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Palette
$bg          = [System.Drawing.Color]::FromArgb(250, 250, 252)
$divider     = [System.Drawing.Color]::FromArgb(232, 232, 238)
$inactiveFg  = [System.Drawing.Color]::FromArgb(150, 152, 162)
$white       = [System.Drawing.Color]::White

# Per-mode accent colors
$accents = @(
    [System.Drawing.Color]::FromArgb(38, 166, 154),   # chat  - teal
    [System.Drawing.Color]::FromArgb(150, 63, 196),   # image - purple
    [System.Drawing.Color]::FromArgb(240, 124, 40)    # video - orange
)
# Short, universally understood mode labels. English is the default for distribution.
$labelSets = @{
    en = @('Chat', 'Image', 'Video')
    ja = @('チャット', '画像生成', '動画生成')
}
$labels = $labelSets[$Locale]

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-ChatIcon($g, [single]$cx, [single]$cy, [single]$s, $color) {
    $pen = New-Object System.Drawing.Pen($color, [single]($s * 0.09))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $bw = $s * 1.15; $bh = $s * 0.82
    $bx = $cx - $bw / 2; $by = $cy - $bh / 2 - $s * 0.06
    $path = New-RoundedPath $bx $by $bw $bh ([single]($s * 0.22))
    $g.DrawPath($pen, $path)
    # tail
    $tail = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($cx - $s * 0.12), [single]($by + $bh - $s * 0.02))),
        (New-Object System.Drawing.PointF([single]($cx - $s * 0.30), [single]($by + $bh + $s * 0.28))),
        (New-Object System.Drawing.PointF([single]($cx + $s * 0.06), [single]($by + $bh - $s * 0.02)))
    ))
    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.FillPath($brush, $tail)
    # three dots
    $dotR = $s * 0.075
    foreach ($dx in @(-0.28, 0, 0.28)) {
        $g.FillEllipse($brush, [single]($cx + $dx * $s - $dotR), [single]($cy - $s * 0.06 - $dotR), [single]($dotR * 2), [single]($dotR * 2))
    }
    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $tail.Dispose()
}

function Draw-ImageIcon($g, [single]$cx, [single]$cy, [single]$s, $color) {
    $pen = New-Object System.Drawing.Pen($color, [single]($s * 0.09))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $fw = $s * 1.15; $fh = $s * 0.92
    $fx = $cx - $fw / 2; $fy = $cy - $fh / 2
    $frame = New-RoundedPath $fx $fy $fw $fh ([single]($s * 0.16))
    $g.DrawPath($pen, $frame)
    $brush = New-Object System.Drawing.SolidBrush($color)
    # sun
    $sr = $s * 0.14
    $g.FillEllipse($brush, [single]($fx + $fw * 0.24 - $sr), [single]($fy + $fh * 0.28 - $sr), [single]($sr * 2), [single]($sr * 2))
    # mountain
    $mt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mt.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($fx + $fw * 0.14), [single]($fy + $fh * 0.80))),
        (New-Object System.Drawing.PointF([single]($fx + $fw * 0.46), [single]($fy + $fh * 0.42))),
        (New-Object System.Drawing.PointF([single]($fx + $fw * 0.66), [single]($fy + $fh * 0.62))),
        (New-Object System.Drawing.PointF([single]($fx + $fw * 0.86), [single]($fy + $fh * 0.80)))
    ))
    $g.FillPath($brush, $mt)
    $pen.Dispose(); $brush.Dispose(); $frame.Dispose(); $mt.Dispose()
}

function Draw-VideoIcon($g, [single]$cx, [single]$cy, [single]$s, $color) {
    $pen = New-Object System.Drawing.Pen($color, [single]($s * 0.09))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $fw = $s * 1.2; $fh = $s * 0.9
    $fx = $cx - $fw / 2; $fy = $cy - $fh / 2
    $frame = New-RoundedPath $fx $fy $fw $fh ([single]($s * 0.16))
    $g.DrawPath($pen, $frame)
    # play triangle
    $brush = New-Object System.Drawing.SolidBrush($color)
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($cx - $s * 0.16), [single]($cy - $s * 0.24))),
        (New-Object System.Drawing.PointF([single]($cx - $s * 0.16), [single]($cy + $s * 0.24))),
        (New-Object System.Drawing.PointF([single]($cx + $s * 0.26), [single]($cy)))
    ))
    $g.FillPath($brush, $tri)
    $pen.Dispose(); $brush.Dispose(); $frame.Dispose(); $tri.Dispose()
}

function Build-Menu([int]$activeIndex) {
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $bmp.SetResolution(72, 72)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear($bg)

    $cellW = $W / 3.0
    $fontFamily = 'Yu Gothic UI'
    try { $null = New-Object System.Drawing.FontFamily($fontFamily) } catch { $fontFamily = 'Meiryo' }
    $font = New-Object System.Drawing.Font($fontFamily, 66, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

    for ($i = 0; $i -lt 3; $i++) {
        $cx = [single]($cellW * $i + $cellW / 2)
        $active = ($i -eq $activeIndex)
        $accent = $accents[$i]

        if ($active) {
            # tinted cell background
            $tint = [System.Drawing.Color]::FromArgb(26, $accent.R, $accent.G, $accent.B)
            $tb = New-Object System.Drawing.SolidBrush($tint)
            $g.FillRectangle($tb, [single]($cellW * $i), 0, [single]$cellW, [single]$H)
            $tb.Dispose()
            # top indicator bar
            $bar = New-Object System.Drawing.SolidBrush($accent)
            $g.FillRectangle($bar, [single]($cellW * $i), 0, [single]$cellW, 18)
            $bar.Dispose()
        }

        $fg = if ($active) { $accent } else { $inactiveFg }
        $iconCx = $cx
        $iconCy = [single]($H * 0.38)
        $iconS = 205.0
        switch ($i) {
            0 { Draw-ChatIcon  $g $iconCx $iconCy $iconS $fg }
            1 { Draw-ImageIcon $g $iconCx $iconCy $iconS $fg }
            2 { Draw-VideoIcon $g $iconCx $iconCy $iconS $fg }
        }

        $tb = New-Object System.Drawing.SolidBrush($fg)
        $rect = New-Object System.Drawing.RectangleF([single]($cellW * $i), [single]($H * 0.66), [single]$cellW, [single]($H * 0.26))
        $g.DrawString($labels[$i], $font, $tb, $rect, $sf)
        $tb.Dispose()
    }

    # dividers
    $dp = New-Object System.Drawing.Pen($divider, 3)
    $g.DrawLine($dp, [single]$cellW, [single]($H * 0.14), [single]$cellW, [single]($H * 0.86))
    $g.DrawLine($dp, [single]($cellW * 2), [single]($H * 0.14), [single]($cellW * 2), [single]($H * 0.86))
    $dp.Dispose()

    $font.Dispose(); $sf.Dispose(); $g.Dispose()
    return $bmp
}

$modes = @('chat', 'image', 'video')
for ($i = 0; $i -lt 3; $i++) {
    $bmp = Build-Menu $i
    $path = Join-Path $OutDir ("richmenu-{0}.png" -f $modes[$i])
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $kb = [math]::Round((Get-Item $path).Length / 1KB, 1)
    Write-Host ("Wrote {0}  ({1} KB)" -f $path, $kb)
}
Write-Host 'Done. 3 rich menu images generated (chat/image/video active variants).'
