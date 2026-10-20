# Project-native geometric emblems, not official character illustrations.
[CmdletBinding()]
param([switch]$Overwrite, [string[]]$Keys)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Generals'))
$beasts = @(
    @{ Key = 'hundun'; Name = '混沌'; Accent = '#AA844C' },
    @{ Key = 'qiongqi'; Name = '穷奇'; Accent = '#A65F43' },
    @{ Key = 'taowu'; Name = '梼杌'; Accent = '#578C97' },
    @{ Key = 'taotie'; Name = '饕餮'; Accent = '#608C73' },
    @{ Key = 'zhuyin'; Name = '烛阴'; Accent = '#AE4D45' },
    @{ Key = 'xiangliu'; Name = '相柳'; Accent = '#69A388' },
    @{ Key = 'zhuyan'; Name = '朱厌'; Accent = '#B4765E' },
    @{ Key = 'bifang'; Name = '毕方'; Accent = '#D19242' },
    @{ Key = 'yingzhao'; Name = '英招'; Accent = '#6A9BAA' }
)
if ($Keys.Count -gt 0) {
    foreach ($key in $Keys) { if ($beasts.Key -notcontains $key) { throw "Unknown beast key: $key" } }
    $beasts = @($beasts | Where-Object Key -In $Keys)
}
foreach ($beast in $beasts) {
    $path = Join-Path $assetRoot "project-shenwu-$($beast.Key).png"
    if ((Test-Path -LiteralPath $path) -and -not $Overwrite) { throw "Artwork exists; inspect before -Overwrite: $path" }
}
$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) {
    $result = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    $resources.Add($result)
    return $result
}
function Stroke([string]$hex, [single]$width = 3) {
    $result = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $result.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $resources.Add($result)
    return $result
}
function Polygon($graphics, [string]$fill, [single[]]$xy) {
    $points = [Drawing.PointF[]]@(for ($index = 0; $index -lt $xy.Length; $index += 2) {
        [Drawing.PointF]::new($xy[$index], $xy[$index + 1])
    })
    $graphics.FillPolygon((Ink $fill), $points)
    $graphics.DrawPolygon((Stroke '#D0BA86' 2), $points)
}
function Label($graphics, [string]$text, [single]$size, [single]$top, [string]$color) {
    $font = [Drawing.Font]::new('KaiTi', $size, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Alignment = [Drawing.StringAlignment]::Center
        $graphics.DrawString($text, $font, (Ink $color), [Drawing.RectangleF]::new(0, $top, 300, 75), $format)
    }
    finally { $font.Dispose(); $format.Dispose() }
}
try {
    foreach ($beast in $beasts) {
        $bitmap = [Drawing.Bitmap]::new(300, 400)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
                [Drawing.Rectangle]::new(0, 0, 300, 400), [Drawing.ColorTranslator]::FromHtml('#2F3029'),
                [Drawing.ColorTranslator]::FromHtml('#101A1B'), 90)
            try { $graphics.FillRectangle($gradient, 0, 0, 300, 400) } finally { $gradient.Dispose() }
            $graphics.DrawRectangle((Stroke '#9F885D' 3), 10, 10, 279, 379)
            $graphics.DrawRectangle((Stroke '#655D48' 1), 17, 17, 265, 365)
            $graphics.DrawEllipse((Stroke '#685F45' 2), 31, 54, 238, 238)
            $graphics.DrawEllipse((Stroke '#413F32' 1), 42, 65, 216, 216)
            $accent = $beast.Accent
            switch ($beast.Key) {
                'xiangliu' {
                    foreach ($x in @(75, 100, 125, 150, 175, 200, 225)) {
                        $graphics.DrawBezier((Stroke $accent 11), $x, 100, ($x - 65), 150, ($x + 50), 210, 150, 243)
                        Polygon $graphics $accent @($x, 82, ($x - 12), 103, $x, 116, ($x + 12), 103)
                    }
                    foreach ($x in @(112, 188)) { $graphics.DrawBezier((Stroke $accent 9), $x, 65, 230, 130, 62, 180, 150, 243) }
                }
                'zhuyan' {
                    Polygon $graphics $accent @(68, 145, 85, 95, 125, 105, 150, 88, 175, 105, 215, 95, 232, 145, 214, 217, 150, 251, 86, 217)
                    Polygon $graphics '#E5D9BD' @(105, 143, 150, 117, 195, 143, 187, 207, 150, 232, 113, 207)
                    $graphics.DrawLine((Stroke '#6F3E30' 6), 116, 162, 137, 169)
                    $graphics.DrawLine((Stroke '#6F3E30' 6), 184, 162, 163, 169)
                    $graphics.DrawArc((Stroke '#6F3E30' 5), 127, 178, 46, 29, 0, 180)
                }
                'bifang' {
                    Polygon $graphics $accent @(150, 162, 52, 83, 72, 161, 122, 189, 150, 171, 178, 189, 228, 161, 248, 83)
                    Polygon $graphics '#D8B868' @(150, 86, 127, 118, 145, 166, 136, 210, 150, 224, 164, 210, 155, 166, 173, 118)
                    Polygon $graphics '#B64F37' @(140, 106, 159, 107, 186, 122, 154, 126)
                    $graphics.DrawLine((Stroke '#D8B868' 6), 150, 219, 150, 256)
                }
                'yingzhao' {
                    Polygon $graphics $accent @(146, 160, 58, 86, 63, 149, 113, 191, 150, 176, 187, 191, 237, 149, 242, 86)
                    Polygon $graphics '#A8C1B9' @(125, 115, 133, 85, 150, 71, 169, 92, 176, 125, 166, 147, 185, 203, 168, 239, 131, 239, 116, 202, 135, 147)
                    $graphics.DrawLine((Stroke '#263D46' 4), 137, 111, 146, 114)
                    $graphics.DrawLine((Stroke '#263D46' 4), 164, 111, 155, 114)
                    $graphics.DrawArc((Stroke '#6A9BAA' 3), 137, 116, 27, 15, 0, 180)
                }
                'hundun' {
                    foreach ($offset in @(-36, 0, 36)) {
                        Polygon $graphics $accent @(130, (134 + $offset), 61, (86 + $offset), 31, (103 + $offset), 103, (163 + $offset))
                        Polygon $graphics $accent @(170, (134 + $offset), 239, (86 + $offset), 269, (103 + $offset), 197, (163 + $offset))
                    }
                    $graphics.FillEllipse((Ink '#756B4F'), 99, 95, 102, 144)
                    $graphics.DrawEllipse((Stroke '#D0BA86' 3), 99, 95, 102, 144)
                    $graphics.DrawArc((Stroke '#D2B273' 4), 119, 124, 62, 87, -50, 275)
                    $graphics.DrawArc((Stroke '#A88D60' 3), 133, 145, 34, 47, 45, 275)
                }
                'qiongqi' {
                    Polygon $graphics $accent @(103, 173, 36, 91, 48, 168, 78, 220, 115, 216)
                    Polygon $graphics $accent @(197, 173, 264, 91, 252, 168, 222, 220, 185, 216)
                    Polygon $graphics '#7A5140' @(91, 139, 94, 77, 128, 115, 172, 115, 206, 77, 209, 139, 193, 215, 150, 246, 107, 215)
                    Polygon $graphics '#C99C5E' @(105, 149, 138, 164, 114, 174)
                    Polygon $graphics '#C99C5E' @(195, 149, 162, 164, 186, 174)
                    Polygon $graphics '#AD7950' @(133, 186, 150, 177, 167, 186, 150, 202)
                    $graphics.DrawLines((Stroke '#DFBF7F' 4), [Drawing.Point[]]@([Drawing.Point]::new(122, 211), [Drawing.Point]::new(150, 222), [Drawing.Point]::new(178, 211)))
                }
                'taowu' {
                    Polygon $graphics '#385761' @(86, 133, 62, 80, 115, 115, 185, 115, 238, 80, 214, 133, 204, 226, 150, 252, 96, 226)
                    Polygon $graphics $accent @(101, 148, 150, 132, 199, 148, 191, 215, 150, 238, 109, 215)
                    $graphics.FillEllipse((Ink '#121D20'), 112, 159, 24, 12)
                    $graphics.FillEllipse((Ink '#121D20'), 164, 159, 24, 12)
                    Polygon $graphics '#DBCCA8' @(104, 194, 93, 223, 121, 214)
                    Polygon $graphics '#DBCCA8' @(196, 194, 207, 223, 179, 214)
                    $graphics.DrawArc((Stroke '#D0BA86' 3), 129, 187, 42, 24, 0, 180)
                }
                'taotie' {
                    Polygon $graphics '#476A56' @(73, 126, 49, 86, 107, 103, 150, 89, 193, 103, 251, 86, 227, 126, 219, 228, 150, 253, 81, 228)
                    Polygon $graphics $accent @(91, 135, 134, 143, 127, 183, 94, 177)
                    Polygon $graphics $accent @(209, 135, 166, 143, 173, 183, 206, 177)
                    $graphics.FillEllipse((Ink '#D6C38E'), 106, 150, 19, 19)
                    $graphics.FillEllipse((Ink '#D6C38E'), 175, 150, 19, 19)
                    Polygon $graphics '#283F35' @(137, 169, 150, 153, 163, 169, 166, 201, 134, 201)
                    $graphics.DrawRectangle((Stroke '#D0BA86' 4), 108, 212, 84, 14)
                    foreach ($x in @(123, 141, 159, 177)) { $graphics.DrawLine((Stroke '#D0BA86' 2), $x, 212, $x, 226) }
                }
                'zhuyin' {
                    $graphics.DrawBezier((Stroke '#5E3534' 34), 160, 256, 35, 221, 251, 173, 148, 123)
                    $graphics.DrawBezier((Stroke $accent 22), 160, 256, 35, 221, 251, 173, 148, 123)
                    Polygon $graphics '#87433D' @(113, 117, 122, 91, 136, 114, 159, 111, 176, 93, 183, 121, 164, 151, 129, 153)
                    $graphics.FillEllipse((Ink '#EBC780'), 135, 123, 24, 12)
                    $graphics.DrawLine((Stroke '#18211E' 3), 147, 124, 147, 134)
                    Polygon $graphics '#D6A45D' @(146, 87, 132, 68, 143, 40, 160, 65)
                    Polygon $graphics '#EFDB9F' @(146, 81, 141, 68, 146, 56, 153, 69)
                }
            }
            Label $graphics $beast.Name 44 292 '#E6D6B0'
            Label $graphics '本地兽符' 17 353 '#A79F89'
            $path = Join-Path $assetRoot "project-shenwu-$($beast.Key).png"
            $mode = if ($Overwrite) { [IO.FileMode]::Create } else { [IO.FileMode]::CreateNew }
            $stream = [IO.File]::Open($path, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
            Write-Output $path
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { foreach ($resource in $resources) { $resource.Dispose() } }
