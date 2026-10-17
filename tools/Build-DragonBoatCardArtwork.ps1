# Original project-drawn Dragon Boat symbols, not official game artwork.
# Suit, rank and effective rules remain runtime overlays.
[CmdletBinding()]
param([switch]$GenerateMissing)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'))
$cards = @(
    @{ Kind = 'Zong'; Name = '粽'; Accent = '#4C7555'; Type = '基本牌' },
    @{ Kind = 'RealgarWine'; Name = '雄黄酒'; Accent = '#A47537'; Type = '基本牌' },
    @{ Kind = 'SameBoat'; Name = '同舟共济'; Accent = '#4F7380'; Type = '普通锦囊' },
    @{ Kind = 'StriveUpstream'; Name = '力争上游'; Accent = '#64784D'; Type = '普通锦囊' },
    @{ Kind = 'AgainstCurrent'; Name = '逆水行舟'; Accent = '#94604C'; Type = '普通锦囊' }
)
foreach ($card in $cards) {
    $path = Join-Path $assetRoot "$($card.Kind).png"
    if ((Test-Path -LiteralPath $path) -and -not $GenerateMissing) { throw "Artwork already exists; inspect before regeneration: $path" }
}
$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) { $b = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex)); $resources.Add($b); return $b }
function Stroke([string]$hex, [single]$width = 2) {
    $p = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $p.LineJoin = [Drawing.Drawing2D.LineJoin]::Round; $resources.Add($p); return $p
}
function Polygon($g, [string]$fill, [single[]]$xy, [string]$edge = '#6D5A40') {
    $points = [Drawing.PointF[]]@(for ($i = 0; $i -lt $xy.Length; $i += 2) { [Drawing.PointF]::new($xy[$i], $xy[$i + 1]) })
    $g.FillPolygon((Ink $fill), $points); $g.DrawPolygon((Stroke $edge 1.5), $points)
}
function Center($g, [string]$text, [single]$size, [string]$color, [single]$y, [single]$height) {
    $font = [Drawing.Font]::new('KaiTi', $size, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Alignment = [Drawing.StringAlignment]::Center; $format.LineAlignment = [Drawing.StringAlignment]::Center
        $g.DrawString($text, $font, (Ink $color), [Drawing.RectangleF]::new(10, $y, 166, $height), $format)
    } finally { $format.Dispose(); $font.Dispose() }
}
function Waves($g, [string]$color, [single]$y) {
    foreach ($x in @(-58, -20, 18)) { $g.DrawBezier((Stroke $color 2), $x, $y, ($x + 12), ($y - 7), ($x + 24), ($y + 7), ($x + 38), $y) }
}
function Boat($g, [string]$color) {
    Polygon $g '#AD8551' @(-58, 10, -32, 32, 31, 32, 56, 5, 33, 13, -35, 13) $color
    $g.DrawLine((Stroke '#D7BD80' 3), -30, 22, 30, 22)
    Polygon $g $color @(34, 13, 48, -6, 64, -13, 57, -1, 50, 0, 46, 16) $color
    $g.FillEllipse((Ink '#E6CF8F'), 52, -8, 3, 3)
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
                [Drawing.ColorTranslator]::FromHtml('#F1E8CF'), [Drawing.ColorTranslator]::FromHtml('#D2C7A1'), 90)
            try { $g.FillRectangle($paper, 0, 0, 186, 260) } finally { $paper.Dispose() }
            $g.DrawRectangle((Stroke '#675B3D' 2), 2, 2, 181, 255); $g.DrawRectangle((Stroke '#A89C6F' 1), 7, 7, 171, 245)
            Center $g '龙舟争渡' 13 '#90845B' 19 23
            $g.DrawEllipse((Stroke '#B2A47C' 1), 19, 54, 148, 140); $g.DrawEllipse((Stroke '#CDC397' 4), 27, 62, 132, 124)
            $g.TranslateTransform(93, 124)
            switch ($card.Kind) {
                'Zong' {
                    Polygon $g '#6A8A58' @(-47, 28, -10, -49, 43, 32, -9, 55) $card.Accent
                    Polygon $g '#91A16B' @(-10, -49, 7, 21, 43, 32) $card.Accent
                    Polygon $g '#507649' @(-47, 28, 7, 21, -9, 55) $card.Accent
                    $g.DrawLine((Stroke '#D3C69A' 4), -29, -7, 28, 13)
                    $g.DrawLine((Stroke '#D3C69A' 4), -35, 31, 13, -14)
                    $g.DrawLine((Stroke '#E5D9AD' 2), -35, 31, 12, -14)
                    $g.DrawBezier((Stroke '#D3C69A' 3), 3, 18, 29, -12, 39, -1, 9, 18)
                    $g.DrawBezier((Stroke '#D3C69A' 3), 3, 18, -20, 0, -28, 15, 3, 22)
                }
                'RealgarWine' {
                    Polygon $g '#BB975D' @(-28, -43, 4, -43, 4, -23, 21, -5, 25, 40, 13, 52, -35, 52, -45, 39, -40, -5, -28, -23) $card.Accent
                    $g.FillRectangle((Ink '#8F6C42'), -32, -51, 40, 11); $g.DrawRectangle((Stroke '#6B5638' 2), -32, -51, 40, 11)
                    $g.DrawEllipse((Stroke '#E0C991' 2), -31, -3, 43, 35)
                    $labelFont = [Drawing.Font]::new('KaiTi', 25, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
                    try { $g.DrawString('雄', $labelFont, (Ink '#E8D9AE'), [Drawing.PointF]::new(-23, -2)) } finally { $labelFont.Dispose() }
                    Polygon $g '#A38750' @(24, 19, 60, 19, 54, 45, 31, 45) $card.Accent
                    $g.FillEllipse((Ink '#D2AD60'), 24, 14, 36, 11); $g.DrawEllipse((Stroke $card.Accent 2), 24, 14, 36, 11)
                    $g.DrawLine((Stroke '#698256' 3), 31, -48, 47, 8)
                    foreach ($y in @(-41, -27, -13)) { Polygon $g '#83935E' @(38, $y, 23, ($y - 5), 30, ($y + 6), 41, ($y + 5), 52, ($y - 3), 49, ($y + 8)) '#698256' }
                }
                'SameBoat' {
                    Boat $g $card.Accent
                    foreach ($x in @(-23, 13)) {
                        $g.FillEllipse((Ink '#6D776A'), ($x - 7), -34, 14, 14)
                        $g.DrawLine((Stroke '#6D776A' 8), $x, -17, ($x + 2), 10)
                        $g.DrawLine((Stroke '#A27D47' 4), ($x - 10), -7, ($x + 17), 39)
                        Polygon $g '#A27D47' @(($x + 10), 29, ($x + 25), 44, ($x + 22), 50, ($x + 11), 42) '#886C42'
                    }
                    Waves $g '#829791' 49
                }
                'StriveUpstream' {
                    Waves $g '#91A38B' -31; Waves $g '#91A38B' -6; Waves $g '#91A38B' 44
                    foreach ($x in @(-31, 0, 31)) {
                        $g.DrawLine((Stroke $card.Accent 5), $x, 26, $x, -28)
                        Polygon $g $card.Accent @(($x - 9), -23, $x, -42, ($x + 9), -23) $card.Accent
                        Polygon $g '#AC8B50' @(($x - 5), 11, ($x + 5), 11, ($x + 8), 34, ($x - 8), 34) '#7C7846'
                    }
                }
                'AgainstCurrent' {
                    $state = $g.Save(); $g.ScaleTransform(-1, 1); Boat $g $card.Accent; $g.Restore($state)
                    foreach ($y in @(-42, -17, 50)) {
                        $g.DrawLine((Stroke '#82989B' 3), -54, $y, 48, $y)
                        Polygon $g '#82989B' @(48, ($y - 6), 60, $y, 48, ($y + 6)) '#82989B'
                    }
                    $g.DrawLine((Stroke '#8B7152' 4), -24, -28, -49, 21)
                    Polygon $g '#AC8B59' @(-48, 11, -38, 16, -47, 36, -57, 31) '#8B7152'
                }
            }
            $g.ResetTransform(); $g.DrawLine((Stroke '#AA9C6D' 1), 24, 204, 162, 204)
            Center $g $card.Name 24 $card.Accent 207 29; Center $g $card.Type 13 '#7B704B' 236 18
            $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
            Write-Output $path
        } finally { $g.Dispose(); $bitmap.Dispose() }
    }
} finally { foreach ($resource in $resources) { $resource.Dispose() } }
