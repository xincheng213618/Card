# Original project-drawn Wenhe seals; no official game artwork is reproduced.
# Rank, suit and effective rules remain runtime overlays.
[CmdletBinding()]
param([switch]$GenerateMissing)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'))
$cards = @(
    @{ Kind = 'StarShift'; Name = '斗转星移'; Seal = '星'; Accent = '#59637D'; Type = '普通锦囊' },
    @{ Kind = 'BeamSwap'; Name = '偷梁换柱'; Seal = '柱'; Accent = '#916A4B'; Type = '普通锦囊' },
    @{ Kind = 'HandRedistribution'; Name = '李代桃僵'; Seal = '易'; Accent = '#7A6B83'; Type = '普通锦囊' },
    @{ Kind = 'WenheChaos'; Name = '文和乱武'; Seal = '乱'; Accent = '#98584C'; Type = '普通锦囊' },
    @{ Kind = 'Poison'; Name = '毒'; Seal = '毒'; Accent = '#557A5D'; Type = '基本牌' }
)
foreach ($card in $cards) {
    $path = Join-Path $assetRoot "$($card.Kind).png"
    if ((Test-Path -LiteralPath $path) -and -not $GenerateMissing) { throw "Artwork already exists; inspect before regeneration: $path" }
}
$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex)); $resources.Add($brush); return $brush
}
function Stroke([string]$hex, [single]$width = 2) {
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round; $resources.Add($pen); return $pen
}
function Font([single]$size, [Drawing.FontStyle]$style = [Drawing.FontStyle]::Regular) {
    $font = [Drawing.Font]::new('KaiTi', $size, $style, [Drawing.GraphicsUnit]::Pixel); $resources.Add($font); return $font
}
function Polygon($g, [string]$fill, [single[]]$xy, [string]$edge = '#6D5A40') {
    $points = [Drawing.PointF[]]@(for ($i = 0; $i -lt $xy.Length; $i += 2) { [Drawing.PointF]::new($xy[$i], $xy[$i + 1]) })
    $g.FillPolygon((Ink $fill), $points); $g.DrawPolygon((Stroke $edge 1.5), $points)
}
function Center($g, [string]$text, [single]$size, [string]$color, [single]$y, [single]$height) {
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Alignment = [Drawing.StringAlignment]::Center; $format.LineAlignment = [Drawing.StringAlignment]::Center
        $g.DrawString($text, (Font $size ([Drawing.FontStyle]::Bold)), (Ink $color), [Drawing.RectangleF]::new(10, $y, 166, $height), $format)
    } finally { $format.Dispose() }
}
function SwapArrows($g, [string]$color) {
    $g.DrawArc((Stroke $color 3), -54, -47, 108, 94, 202, 117)
    Polygon $g $color @(43, -38, 58, -32, 46, -22) $color
    $g.DrawArc((Stroke $color 3), -54, -47, 108, 94, 22, 117)
    Polygon $g $color @(-43, 38, -58, 32, -46, 22) $color
}
try {
    foreach ($card in $cards) {
        $path = Join-Path $assetRoot "$($card.Kind).png"
        if (Test-Path -LiteralPath $path) { continue }
        $bitmap = [Drawing.Bitmap]::new(186, 260); $g = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
            $paper = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0, 0, 186, 260),
                [Drawing.ColorTranslator]::FromHtml('#F1E5CD'), [Drawing.ColorTranslator]::FromHtml('#D9C39F'), 90)
            try { $g.FillRectangle($paper, 0, 0, 186, 260) } finally { $paper.Dispose() }
            $g.DrawRectangle((Stroke '#67533D' 2), 2, 2, 181, 255)
            $g.DrawRectangle((Stroke '#B2996F' 1), 7, 7, 171, 245)
            Center $g '文和乱武' 13 '#9B805B' 19 23
            $g.DrawEllipse((Stroke '#BDA581' 1), 19, 54, 148, 140)
            $g.DrawEllipse((Stroke '#D4BF98' 4), 27, 62, 132, 124)
            $g.TranslateTransform(93, 124)
            switch ($card.Kind) {
                'StarShift' {
                    $g.DrawArc((Stroke $card.Accent 3), -52, -45, 104, 90, 22, 282)
                    $stars = @(@(-45, 7), @(-29, -27), @(2, -46), @(36, -23), @(41, 16), @(7, 43), @(-16, 23))
                    for ($i = 0; $i -lt $stars.Count - 1; $i++) { $g.DrawLine((Stroke '#9D976E' 1.5), $stars[$i][0], $stars[$i][1], $stars[$i + 1][0], $stars[$i + 1][1]) }
                    foreach ($s in $stars) {
                        $x = $s[0]; $y = $s[1]
                        Polygon $g '#F6EAC0' @($x, ($y - 7), ($x + 2), ($y - 2), ($x + 7), $y, ($x + 2), ($y + 2), $x, ($y + 7), ($x - 2), ($y + 2), ($x - 7), $y, ($x - 2), ($y - 2)) $card.Accent
                    }
                    $g.FillEllipse((Ink $card.Accent), -12, -12, 24, 24)
                    $g.DrawEllipse((Stroke '#EAD7AB' 2), -8, -8, 16, 16)
                }
                'BeamSwap' {
                    foreach ($x in @(-30, 30)) {
                        $g.FillRectangle((Ink '#A88B60'), ($x - 10), -32, 20, 77)
                        $g.DrawRectangle((Stroke $card.Accent 2), ($x - 10), -32, 20, 77)
                        $g.DrawLine((Stroke '#D1B681' 2), ($x - 4), -26, ($x - 4), 39)
                        $g.FillRectangle((Ink '#7B6142'), ($x - 15), 43, 30, 8)
                    }
                    Polygon $g '#92704E' @(-56, -48, 56, -48, 60, -34, -60, -34)
                    SwapArrows $g $card.Accent
                }
                'HandRedistribution' {
                    $state = $g.Save(); $g.RotateTransform(-18)
                    $g.FillRectangle((Ink '#D1AA83'), -43, -34, 37, 66); $g.DrawRectangle((Stroke '#956A4D' 2), -43, -34, 37, 66)
                    $g.DrawEllipse((Stroke '#9D6B59' 2), -35, -18, 21, 24); $g.Restore($state)
                    $state = $g.Save(); $g.RotateTransform(18)
                    $g.FillRectangle((Ink '#B4B997'), 6, -34, 37, 66); $g.DrawRectangle((Stroke '#65775B' 2), 6, -34, 37, 66)
                    $g.DrawLine((Stroke '#65775B' 3), 14, -15, 35, 10); $g.DrawLine((Stroke '#65775B' 3), 35, -15, 14, 10); $g.Restore($state)
                    SwapArrows $g $card.Accent
                }
                'WenheChaos' {
                    foreach ($angle in @(-35, 35)) {
                        $state = $g.Save(); $g.RotateTransform($angle)
                        Polygon $g '#ABB0A8' @(-6, -60, 0, -73, 6, -60, 5, 24, -5, 24) '#646A65'
                        $g.DrawLine((Stroke '#E0D7BA' 1.5), 0, -59, 0, 21)
                        $g.DrawLine((Stroke '#9B7849' 6), -17, 24, 17, 24)
                        $g.DrawLine((Stroke '#684D3E' 8), 0, 29, 0, 53)
                        $g.FillEllipse((Ink '#A28A5B'), -6, 51, 12, 12); $g.Restore($state)
                    }
                    $g.DrawArc((Stroke $card.Accent 3), -58, -49, 116, 105, 30, 145)
                    $g.DrawArc((Stroke $card.Accent 3), -58, -49, 116, 105, 210, 145)
                }
                'Poison' {
                    Polygon $g '#A0B08A' @(-15, -38, 15, -38, 15, -22, 34, 3, 39, 41, 27, 54, -27, 54, -39, 41, -34, 3, -15, -22) '#55705B'
                    Polygon $g '#68866B' @(-31, 13, 31, 13, 34, 39, 24, 48, -24, 48, -34, 39) '#55705B'
                    $g.FillRectangle((Ink '#8D7250'), -19, -48, 38, 12)
                    $g.DrawRectangle((Stroke '#5F6047' 2), -19, -48, 38, 12)
                    $g.DrawBezier((Stroke '#879579' 2), -1, -54, -23, -74, 30, -73, 8, -93)
                    $g.DrawEllipse((Stroke '#D7D5AD' 2), -13, 17, 26, 24)
                    $g.DrawLine((Stroke '#D7D5AD' 2), -8, 22, 8, 35); $g.DrawLine((Stroke '#D7D5AD' 2), 8, 22, -8, 35)
                }
            }
            $g.ResetTransform(); $g.DrawLine((Stroke '#B19569' 1), 24, 204, 162, 204)
            Center $g $card.Name 24 $card.Accent 207 29
            Center $g $card.Type 13 '#7F684B' 236 18
            $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
            Write-Output $path
        } finally { $g.Dispose(); $bitmap.Dispose() }
    }
} finally { foreach ($resource in $resources) { $resource.Dispose() } }
