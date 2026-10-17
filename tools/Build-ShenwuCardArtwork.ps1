# Original project geometry; rank, suit and effective rules are runtime overlays.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'))
$cards = @(
    @{ Kind = 'SowBeans'; Name = '撒豆成兵'; Type = '普通锦囊'; Accent = '#647A49'; Equipment = $false },
    @{ Kind = 'TransplantWood'; Name = '移花接木'; Type = '普通锦囊'; Accent = '#8F6558'; Equipment = $false },
    @{ Kind = 'NationalStyleRobe'; Name = '国风玉袍'; Type = '防具'; Accent = '#557C72'; Equipment = $true },
    @{ Kind = 'CrimsonSoulZither'; Name = '赤焰镇魂琴'; Type = '武器'; Accent = '#994B3C'; Equipment = $true },
    @{ Kind = 'QimenEightTrigrams'; Name = '奇门八卦'; Type = '防具'; Accent = '#656B76'; Equipment = $true },
    @{ Kind = 'DustlessGoldenSpear'; Name = '绝尘金戈'; Type = '坐骑'; Accent = '#9B7B36'; Equipment = $true },
    @{ Kind = 'ShuraInfernoHalberd'; Name = '修罗炼狱戟'; Type = '武器'; Accent = '#9A4439'; Equipment = $true },
    @{ Kind = 'DelusionCrown'; Name = '虚妄之冕'; Type = '宝物'; Accent = '#73557F'; Equipment = $true }
)
foreach ($card in $cards) {
    $files = @("$($card.Kind).png")
    if ($card.Equipment) { $files += "equip-$($card.Kind).png" }
    foreach ($file in $files) {
        $path = Join-Path $assetRoot $file
        if (Test-Path -LiteralPath $path) { throw "Artwork already exists; generation will not overwrite it: $path" }
    }
}
$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) {
    $result = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    $resources.Add($result)
    return $result
}
function Stroke([string]$hex, [single]$width = 2) {
    $result = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $result.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $resources.Add($result)
    return $result
}
function Font([single]$size, [Drawing.FontStyle]$style = [Drawing.FontStyle]::Regular) {
    $result = [Drawing.Font]::new('KaiTi', $size, $style, [Drawing.GraphicsUnit]::Pixel)
    $resources.Add($result)
    return $result
}
function Polygon($g, [string]$fill, [single[]]$xy, [string]$edge = '#5F4D37') {
    $points = [Drawing.PointF[]]@(for ($index = 0; $index -lt $xy.Length; $index += 2) {
        [Drawing.PointF]::new($xy[$index], $xy[$index + 1])
    })
    $g.FillPolygon((Ink $fill), $points)
    $g.DrawPolygon((Stroke $edge 1.5), $points)
}
function CenterText($g, [string]$text, $font, [string]$color, [single]$x, [single]$y, [single]$width, [single]$height) {
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Alignment = [Drawing.StringAlignment]::Center
        $format.LineAlignment = [Drawing.StringAlignment]::Center
        $g.DrawString($text, $font, (Ink $color), [Drawing.RectangleF]::new($x, $y, $width, $height), $format)
    }
    finally { $format.Dispose() }
}
function DrawBeans($g) {
    Polygon $g '#8D7650' @(-49, -49, -31, -61, -11, -49, -6, -14, -25, -6, -48, -21)
    $g.DrawLine((Stroke '#C3A468' 4), -46, -43, -12, -45)
    foreach ($bean in @(@(-5, -39), @(12, -28), @(29, -14), @(40, 3), @(17, -2))) {
        $g.FillEllipse((Ink '#9A974D'), $bean[0], $bean[1], 7, 10)
        $g.DrawEllipse((Stroke '#6F723B' 1), $bean[0], $bean[1], 7, 10)
    }
    foreach ($x in @(-43, 0, 43)) {
        $g.FillEllipse((Ink '#B1A16F'), ($x - 8), 16, 16, 16)
        $g.DrawEllipse((Stroke '#5E6045' 2), ($x - 8), 16, 16, 16)
        Polygon $g '#6B7B54' @(($x - 9), 35, ($x + 9), 35, ($x + 13), 58, ($x - 13), 58)
        $g.DrawLine((Stroke '#6D6147' 3), ($x - 6), 57, ($x - 8), 72)
        $g.DrawLine((Stroke '#6D6147' 3), ($x + 6), 57, ($x + 8), 72)
        $g.DrawLine((Stroke '#6D6147' 2), ($x + 17), 18, ($x + 17), 67)
        Polygon $g '#A8AF9B' @(($x + 17), 8, ($x + 13), 21, ($x + 21), 21)
    }
}
function DrawTransplant($g) {
    $g.DrawBezier((Stroke '#6D6541' 7), -44, 69, -56, 24, -36, -2, -27, -41)
    $g.DrawBezier((Stroke '#6D6541' 7), 44, 69, 56, 24, 36, -2, 27, -41)
    $g.DrawLine((Stroke '#786247' 4), -42, 12, -62, -12)
    $g.DrawLine((Stroke '#786247' 4), 42, 12, 62, -12)
    Polygon $g '#81935E' @(-48, 31, -72, 23, -69, 7, -52, 14)
    Polygon $g '#81935E' @(48, 31, 72, 23, 69, 7, 52, 14)
    Polygon $g '#7C8A5B' @(29, -35, 11, -45, 13, -63, 31, -54)
    Polygon $g '#8E9B64' @(30, -42, 49, -61, 56, -44, 40, -34)
    foreach ($point in @(@(-28, -58), @(-42, -46), @(-36, -30), @(-18, -30), @(-11, -46))) {
        $g.FillEllipse((Ink '#B56C64'), ($point[0] - 9), ($point[1] - 9), 18, 18)
    }
    $g.FillEllipse((Ink '#DCC486'), -35, -51, 16, 16)
    $g.DrawArc((Stroke '#9F7E4C' 3), -28, -11, 58, 48, 205, 150)
    Polygon $g '#9F7E4C' @(27, 12, 38, 16, 29, 25) '#9F7E4C'
    $g.DrawArc((Stroke '#7F9164' 3), -28, 6, 58, 46, 25, 150)
    Polygon $g '#7F9164' @(-26, 34, -38, 29, -29, 20) '#7F9164'
}
function DrawJadeRobe($g) {
    Polygon $g '#72968A' @(-17, -62, -38, -50, -67, -19, -46, 4, -29, -13, -33, 63, 33, 63, 29, -13, 46, 4, 67, -19, 38, -50, 17, -62, 0, -42)
    Polygon $g '#B2C9AF' @(-15, -58, -26, -49, -18, 10, -27, 58, 6, 58, 14, 9, 1, -35)
    Polygon $g '#DFD4A8' @(-17, -62, 0, -42, 17, -62, 10, -31, -4, -17, -11, -34)
    $g.FillRectangle((Ink '#D6C895'), -29, 6, 58, 8)
    $g.FillEllipse((Ink '#668A73'), -8, 3, 16, 15)
    $g.DrawEllipse((Stroke '#E6DAB0' 2), -8, 3, 16, 15)
    foreach ($y in @(28, 43)) {
        $g.DrawBezier((Stroke '#E2DCB5' 2), -20, $y, -7, ($y - 12), 6, ($y + 12), 22, $y)
    }
    $g.DrawLine((Stroke '#E2DCB5' 2), -29, 58, 29, 58)
}
function DrawZither($g) {
    $g.RotateTransform(18)
    Polygon $g '#99493B' @(-22, -74, 17, -74, 26, -58, 19, -14, 24, 58, 15, 74, -19, 74, -26, 57, -19, -14, -25, -58)
    $g.DrawLine((Stroke '#633A2D' 4), -18, -57, 19, -57)
    $g.DrawLine((Stroke '#633A2D' 4), -18, 54, 18, 54)
    foreach ($x in @(-14, -9, -4, 1, 6, 11, 16)) {
        $g.DrawLine((Stroke '#E3CD93' 1), $x, -57, $x, 54)
    }
    foreach ($y in @(-38, -16, 6, 28)) { $g.FillEllipse((Ink '#E2C17D'), 13, $y, 4, 4) }
    Polygon $g '#D18A44' @(-40, 49, -59, 20, -51, -6, -46, 9, -35, -23, -29, 9) '#9D5B3A'
    Polygon $g '#D18A44' @(39, 25, 59, -5, 51, -32, 46, -15, 34, -46, 29, -9) '#9D5B3A'
}
function DrawTrigrams($g) {
    $vertices = [Collections.Generic.List[single]]::new()
    for ($index = 0; $index -lt 8; $index++) {
        $angle = [Math]::PI * ($index / 4.0 + 0.125)
        $vertices.Add([single]([Math]::Cos($angle) * 73))
        $vertices.Add([single]([Math]::Sin($angle) * 73))
    }
    Polygon $g '#A8986C' $vertices.ToArray()
    $g.FillEllipse((Ink '#D6CEAC'), -60, -60, 120, 120)
    foreach ($index in 0..7) {
        $state = $g.Save()
        try {
            $g.RotateTransform([single]($index * 45))
            foreach ($row in 0..2) {
                $y = -57 + $row * 6
                if (($index -band (1 -shl $row)) -eq 0) { $g.DrawLine((Stroke '#555A55' 3), -13, $y, 13, $y) }
                else { $g.DrawLine((Stroke '#555A55' 3), -13, $y, -4, $y); $g.DrawLine((Stroke '#555A55' 3), 4, $y, 13, $y) }
            }
        }
        finally { $g.Restore($state) }
    }
    $g.FillEllipse((Ink '#EEE2BC'), -29, -29, 58, 58)
    $g.FillPie((Ink '#4D5553'), -29, -29, 58, 58, -90, 180)
    $g.FillEllipse((Ink '#EEE2BC'), -14.5, -29, 29, 29)
    $g.FillEllipse((Ink '#4D5553'), -14.5, 0, 29, 29)
    $g.FillEllipse((Ink '#4D5553'), -4, -19, 8, 8)
    $g.FillEllipse((Ink '#EEE2BC'), -4, 11, 8, 8)
    $g.DrawEllipse((Stroke '#555A55' 2), -29, -29, 58, 58)
}
function DrawGoldenSpear($g) {
    $g.FillEllipse((Ink '#A58B52'), -49, -8, 88, 43)
    $g.DrawEllipse((Stroke '#6F5939' 2), -49, -8, 88, 43)
    Polygon $g '#A58B52' @(13, 8, 29, -38, 45, -49, 50, -32, 60, -23, 53, -12, 39, -16, 35, 24)
    Polygon $g '#736344' @(25, -31, 28, -47, 38, -52, 37, -37, 28, -8)
    $g.FillEllipse((Ink '#3F402E'), 42, -29, 4, 4)
    foreach ($leg in @(@(-34, 25, -43, 63), @(-21, 27, -12, 65), @(19, 24, 11, 63), @(29, 23, 42, 57))) {
        $g.DrawLine((Stroke '#866B40' 7), $leg[0], $leg[1], $leg[2], $leg[3])
        $g.DrawLine((Stroke '#524B36' 4), ($leg[2] - 5), $leg[3], ($leg[2] + 5), $leg[3])
    }
    $g.DrawBezier((Stroke '#736344' 6), -44, 3, -72, -17, -66, 26, -71, 37)
    $g.DrawLine((Stroke '#A68A44' 5), -63, -54, 44, -54)
    Polygon $g '#D1B76D' @(43, -62, 72, -54, 43, -46) '#8A713E'
    $g.DrawLine((Stroke '#E5D49A' 1), -59, -56, 44, -56)
}
function DrawInfernoHalberd($g) {
    $g.RotateTransform(19)
    $g.DrawLine((Stroke '#6B493C' 7), 0, -63, 0, 77)
    $g.DrawLine((Stroke '#B99B5C' 2), -1, -48, -1, 74)
    Polygon $g '#8B4940' @(0, -83, -10, -55, -5, -21, 5, -21, 10, -55)
    Polygon $g '#AF6B4C' @(-7, -58, -39, -68, -47, -29, -36, -43, -11, -31)
    Polygon $g '#AF6B4C' @(7, -58, 39, -68, 47, -29, 36, -43, 11, -31)
    $g.DrawLine((Stroke '#D3BA7A' 2), -38, -59, -39, -43)
    $g.DrawLine((Stroke '#D3BA7A' 2), 38, -59, 39, -43)
    $g.FillRectangle((Ink '#C6A165'), -13, -28, 26, 7)
    Polygon $g '#B54D35' @(-8, 11, -37, -11, -31, 20, -43, 43, -23, 35, -8, 56)
    Polygon $g '#CC7540' @(8, -4, 33, -19, 27, 10, 42, 26, 23, 23, 8, 44)
    $g.FillEllipse((Ink '#763F34'), -7, -30, 14, 14)
    $g.DrawEllipse((Stroke '#D9B46F' 2), -7, -30, 14, 14)
}
function DrawDelusionCrown($g) {
    Polygon $g '#786282' @(-53, 26, -57, -23, -32, -4, -25, -54, 0, -25, 25, -54, 32, -4, 57, -23, 53, 26)
    Polygon $g '#AE97A7' @(-47, 20, -49, -9, -29, 8, -20, -35, 0, -13, 20, -35, 29, 8, 49, -9, 47, 20)
    $g.FillEllipse((Ink '#5E4D6C'), -55, 17, 110, 24)
    $g.DrawEllipse((Stroke '#BFA26D' 3), -55, 17, 110, 24)
    Polygon $g '#D9C38A' @(-17, -6, 0, -18, 17, -6, 0, 7)
    $g.FillEllipse((Ink '#5E4D6C'), -5, -11, 10, 13)
    foreach ($x in @(-34, -16, 16, 34)) {
        $g.DrawLine((Stroke '#BDA46F' 2), $x, 35, $x, 57)
        $g.FillEllipse((Ink '#CBB77E'), ($x - 3), 54, 6, 9)
    }
    $g.DrawArc((Stroke '#AD9BA4' 2), -37, -75, 74, 32, 180, 180)
    $g.DrawArc((Stroke '#AD9BA4' 2), -24, -66, 48, 20, 180, 180)
}
try {
    foreach ($card in $cards) {
        $formats = if ($card.Equipment) { @($false, $true) } else { @($false) }
        foreach ($strip in $formats) {
            $width = if ($strip) { 284 } else { 186 }
            $height = if ($strip) { 50 } else { 260 }
            $bitmap = [Drawing.Bitmap]::new($width, $height)
            $g = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
                $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0, 0, $width, $height),
                    [Drawing.Color]::FromArgb(246, 237, 214), [Drawing.Color]::FromArgb(221, 201, 162), 90)
                try { $g.FillRectangle($gradient, 0, 0, $width, $height) } finally { $gradient.Dispose() }
                $g.DrawRectangle((Stroke '#68533C' 2), 2, 2, ($width - 5), ($height - 5))
                $g.DrawRectangle((Stroke '#B59A65' 1), 6, 6, ($width - 13), ($height - 13))
                if ($strip) { $g.TranslateTransform(33, 25); $g.ScaleTransform(0.23, 0.23) }
                else {
                    $g.DrawEllipse((Stroke '#CEB98E' 1), 17, 53, 152, 143)
                    $g.DrawArc((Stroke '#D8C69F' 5), 25, 61, 136, 127, 210, 205)
                    CenterText $g '神武再世' (Font 12) '#92754D' 45 18 96 22
                    $g.TranslateTransform(93, 130)
                    $g.ScaleTransform(0.96, 0.96)
                }
                switch ($card.Kind) {
                    'SowBeans' { DrawBeans $g }
                    'TransplantWood' { DrawTransplant $g }
                    'NationalStyleRobe' { DrawJadeRobe $g }
                    'CrimsonSoulZither' { DrawZither $g }
                    'QimenEightTrigrams' { DrawTrigrams $g }
                    'DustlessGoldenSpear' { DrawGoldenSpear $g }
                    'ShuraInfernoHalberd' { DrawInfernoHalberd $g }
                    'DelusionCrown' { DrawDelusionCrown $g }
                }
                $g.ResetTransform()
                if ($strip) {
                    CenterText $g $card.Name (Font 21 ([Drawing.FontStyle]::Bold)) $card.Accent 61 3 177 42
                    CenterText $g $card.Type (Font 12) '#796242' 239 3 36 42
                }
                else {
                    $g.DrawLine((Stroke '#B59660' 1), 26, 204, 160, 204)
                    CenterText $g $card.Name (Font 23 ([Drawing.FontStyle]::Bold)) $card.Accent 12 207 162 29
                    CenterText $g $card.Type (Font 13) '#796242' 22 236 142 17
                }
                $file = if ($strip) { "equip-$($card.Kind).png" } else { "$($card.Kind).png" }
                $path = Join-Path $assetRoot $file
                $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
                Write-Output $path
            }
            finally { $g.Dispose(); $bitmap.Dispose() }
        }
    }
}
finally { foreach ($resource in $resources) { $resource.Dispose() } }
