# Project-native geometric emblems for OL 2016, not official character illustrations.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Generals'))
$forms = @(
    @{ Key = 'initial'; Name = '年兽 · 初始'; Accent = '#9D915A' },
    @{ Key = 'wise'; Name = '年兽 · 睿智'; Accent = '#668CA6' },
    @{ Key = 'alert'; Name = '年兽 · 警觉'; Accent = '#719B77' },
    @{ Key = 'capricious'; Name = '年兽 · 任性'; Accent = '#AD7EAE' },
    @{ Key = 'furious'; Name = '年兽 · 暴怒'; Accent = '#B96449' }
)
foreach ($form in $forms) {
    $path = Join-Path $assetRoot "project-nian-$($form.Key).png"
    if (Test-Path -LiteralPath $path) { throw "Artwork already exists; refusing to overwrite: $path" }
}
$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    $resources.Add($brush)
    return $brush
}
function Stroke([string]$hex, [single]$width = 3) {
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $resources.Add($pen)
    return $pen
}
function Polygon($graphics, [string]$color, [single[]]$xy) {
    $points = [Drawing.PointF[]]@(for ($i = 0; $i -lt $xy.Length; $i += 2) {
        [Drawing.PointF]::new($xy[$i], $xy[$i + 1])
    })
    $graphics.FillPolygon((Ink $color), $points)
    $graphics.DrawPolygon((Stroke '#C5AE7E' 2), $points)
}
function Label($graphics, [string]$text, [single]$size, [single]$top, [string]$color) {
    $font = [Drawing.Font]::new('KaiTi', $size, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Alignment = [Drawing.StringAlignment]::Center
        $graphics.DrawString($text, $font, (Ink $color), [Drawing.RectangleF]::new(0, $top, 300, 60), $format)
    }
    finally { $font.Dispose(); $format.Dispose() }
}
try {
    foreach ($form in $forms) {
        $bitmap = [Drawing.Bitmap]::new(300, 400)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
                [Drawing.Rectangle]::new(0, 0, 300, 400), [Drawing.ColorTranslator]::FromHtml('#30302C'),
                [Drawing.ColorTranslator]::FromHtml('#131F22'), 90)
            try { $graphics.FillRectangle($gradient, 0, 0, 300, 400) } finally { $gradient.Dispose() }
            $graphics.DrawRectangle((Stroke '#A48C62' 3), 10, 10, 279, 379)
            $graphics.DrawRectangle((Stroke '#615E4C' 1), 17, 17, 265, 365)
            $graphics.DrawEllipse((Stroke '#70694F' 2), 31, 47, 238, 238)
            $graphics.DrawEllipse((Stroke '#45493D' 1), 42, 58, 216, 216)
            # A common horned mask keeps all five images recognizably one creature.
            Polygon $graphics $form.Accent @(95, 131, 68, 72, 105, 91, 121, 119, 179, 119, 195, 91, 232, 72, 205, 131, 215, 195, 191, 239, 150, 260, 109, 239, 85, 195)
            Polygon $graphics '#343E37' @(97, 144, 120, 128, 150, 139, 180, 128, 203, 144, 192, 217, 150, 244, 108, 217)
            $graphics.FillEllipse((Ink '#D3BB78'), 109, 159, 26, 15)
            $graphics.FillEllipse((Ink '#D3BB78'), 165, 159, 26, 15)
            $graphics.DrawLine((Stroke '#15272A' 4), 122, 160, 122, 171)
            $graphics.DrawLine((Stroke '#15272A' 4), 178, 160, 178, 171)
            Polygon $graphics '#BA9C62' @(139, 189, 150, 180, 161, 189, 150, 203)
            $graphics.DrawArc((Stroke '#CBB98B' 3), 127, 204, 46, 23, 0, 180)
            Polygon $graphics '#E0D2AC' @(112, 210, 122, 212, 117, 230)
            Polygon $graphics '#E0D2AC' @(188, 210, 178, 212, 183, 230)
            switch ($form.Key) {
                'initial' {
                    Polygon $graphics '#D2BF7F' @(150, 56, 157, 75, 178, 76, 162, 89, 168, 109, 150, 97, 132, 109, 138, 89, 122, 76, 143, 75)
                }
                'wise' {
                    Polygon $graphics '#91AFBE' @(118, 66, 145, 71, 150, 80, 155, 71, 182, 66, 182, 102, 155, 105, 150, 113, 145, 105, 118, 102)
                    $graphics.DrawLine((Stroke '#314B58' 2), 150, 82, 150, 103)
                    foreach ($y in @(80, 89, 98)) {
                        $graphics.DrawLine((Stroke '#314B58' 2), 124, $y, 141, ($y + 2))
                        $graphics.DrawLine((Stroke '#314B58' 2), 159, ($y + 2), 176, $y)
                    }
                }
                'alert' {
                    Polygon $graphics '#A7CBA4' @(114, 87, 130, 72, 150, 66, 170, 72, 186, 87, 170, 102, 150, 108, 130, 102)
                    $graphics.FillEllipse((Ink '#2A4B40'), 140, 74, 20, 26)
                    $graphics.DrawLine((Stroke '#E2DAB0' 3), 150, 78, 150, 94)
                }
                'capricious' {
                    $graphics.FillEllipse((Ink '#D0A4CF'), 123, 65, 54, 45)
                    $graphics.FillEllipse((Ink '#343E37'), 142, 59, 41, 43)
                    foreach ($point in @(@(111, 68), @(188, 94), @(114, 111))) {
                        $graphics.FillEllipse((Ink '#DDCE9C'), $point[0], $point[1], 5, 5)
                    }
                    $graphics.DrawArc((Stroke '#C599C7' 4), 62, 199, 52, 46, 80, 230)
                    $graphics.DrawArc((Stroke '#C599C7' 4), 186, 199, 52, 46, -130, 230)
                }
                'furious' {
                    Polygon $graphics '#D57C4D' @(150, 49, 166, 80, 181, 69, 176, 105, 150, 121, 124, 105, 119, 69, 134, 80)
                    Polygon $graphics '#E5BD75' @(150, 76, 162, 99, 150, 111, 138, 99)
                    $graphics.DrawLine((Stroke '#D78A55' 6), 103, 151, 136, 160)
                    $graphics.DrawLine((Stroke '#D78A55' 6), 197, 151, 164, 160)
                    Polygon $graphics '#C36C4C' @(89, 197, 62, 177, 72, 216, 104, 235)
                    Polygon $graphics '#C36C4C' @(211, 197, 238, 177, 228, 216, 196, 235)
                }
            }
            Label $graphics $form.Name 34 292 '#E7D7B3'
            Label $graphics 'OL 2016 · 本地兽符' 16 351 '#AEA58E'
            $path = Join-Path $assetRoot "project-nian-$($form.Key).png"
            $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
            Write-Output $path
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { foreach ($resource in $resources) { $resource.Dispose() } }
