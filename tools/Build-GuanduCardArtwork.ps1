# Original project-drawn Guandu and derived-card symbols; not official game artwork.
# Xuanjian copies the existing GeneralWeapon template; its name is a runtime overlay.
# Physical suit and rank are rendered by the game, never baked into this art.
[CmdletBinding()]
param(
    [switch]$GenerateMissing,
    [ValidateSet('Intoxication', 'StoreGrain', 'Reinforcements', 'UnitedEffort', 'ThunderChariot', 'WumiaoFlood', 'Xuanjian', 'QizhengXiangsheng')]
    [string[]]$Kinds
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'))
$cards = @(
    @{ Kind = 'Intoxication'; Name = '酗酒'; Accent = '#A45C49'; Type = '基本牌' },
    @{ Kind = 'StoreGrain'; Name = '屯粮'; Accent = '#8D7941'; Type = '普通锦囊' },
    @{ Kind = 'Reinforcements'; Name = '援军'; Accent = '#607B6C'; Type = '普通锦囊' },
    @{ Kind = 'UnitedEffort'; Name = '勠力同心'; Accent = '#536C86'; Type = '普通锦囊' },
    @{ Kind = 'ThunderChariot'; Name = '霹雳车'; Accent = '#806044'; Type = '武器' },
    @{ Kind = 'WumiaoFlood'; Name = '水淹七军'; Accent = '#496F86'; Type = '普通锦囊' },
    @{ Kind = 'QizhengXiangsheng'; Name = '奇正相生'; Accent = '#746046'; Type = '普通锦囊' },
    @{ Kind = 'Xuanjian'; Name = '玄剑'; Template = 'GeneralWeapon' }
)
if ($Kinds) { $cards = @($cards | Where-Object { $_.Kind -cin $Kinds }) }
foreach ($card in $cards) {
    $files = @("$($card.Kind).png")
    if ($card.Kind -in @('ThunderChariot', 'Xuanjian')) { $files += "equip-$($card.Kind).png" }
    foreach ($file in $files) {
        $path = Join-Path $assetRoot $file
        if ((Test-Path -LiteralPath $path) -and -not $GenerateMissing) { throw "Artwork already exists; inspect before regeneration: $path" }
    }
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
function Banner($g, [single]$x, [single]$y, [string]$color) {
    $g.DrawLine((Stroke '#7D6A4B' 3), $x, $y, $x, ($y + 77))
    Polygon $g $color @($x, $y, ($x + 34), ($y + 4), ($x + 23), ($y + 19), ($x + 32), ($y + 33), $x, ($y + 29)) $color
}
function ThunderChariot($g) {
    foreach ($x in @(-37, 31)) {
        $g.FillEllipse((Ink '#9B8157'), ($x - 16), 28, 32, 32)
        $g.DrawEllipse((Stroke '#66513B' 3), ($x - 16), 28, 32, 32)
        $g.DrawLine((Stroke '#66513B' 2), ($x - 12), 44, ($x + 12), 44)
        $g.DrawLine((Stroke '#66513B' 2), $x, 32, $x, 56)
    }
    Polygon $g '#98724C' @(-57, 21, 45, 21, 57, 34, -50, 34) '#6E523A'
    Polygon $g '#B39564' @(-19, 22, -6, -44, 7, -44, 22, 22, 8, 22, 0, -16, -8, 22) '#775C3E'
    $g.DrawLine((Stroke '#6D513A' 8), -34, -24, 33, -61)
    $g.DrawLine((Stroke '#C9B181' 2), -35, -27, 31, -64)
    $g.DrawLine((Stroke '#7B6747' 2), -34, -24, 0, 10)
    $g.DrawLine((Stroke '#7B6747' 2), 33, -61, 0, 10)
    $g.FillEllipse((Ink '#6C6D64'), 24, -70, 20, 18)
    $g.DrawEllipse((Stroke '#4F554F' 2), 24, -70, 20, 18)
}
function Flood($g) {
    foreach ($x in @(-40, -11, 18)) {
        $g.DrawLine((Stroke '#86764F' 2), $x, -45, $x, 24)
        Polygon $g '#8B7761' @($x, -45, ($x + 22), -40, ($x + 16), -26, $x, -29) '#74654F'
    }
    Polygon $g '#B59E6B' @(-45, 3, -17, 3, -6, 20, -55, 20) '#8D7B55'
    foreach ($y in @(14, 31, 48)) {
        $g.DrawBezier((Stroke '#557C8F' 4), -59, $y, -39, ($y - 18), -17, ($y + 15), 4, $y)
        $g.DrawBezier((Stroke '#557C8F' 4), 4, $y, 23, ($y - 18), 43, ($y + 15), 61, ($y - 2))
        $g.DrawBezier((Stroke '#AFC5BE' 1.5), -56, ($y + 6), -35, ($y - 10), -16, ($y + 19), 4, ($y + 6))
    }
    Polygon $g '#C5A761' @(38, -67, 23, -41, 34, -42, 20, -18, 48, -47, 36, -46, 50, -67) '#AB8F4F'
}
function EquipmentStrip($card) {
    $path = Join-Path $assetRoot "equip-$($card.Kind).png"
    if (Test-Path -LiteralPath $path) { return }
    $bitmap = [Drawing.Bitmap]::new(284, 50); $g = [Drawing.Graphics]::FromImage($bitmap)
    $font = [Drawing.Font]::new('KaiTi', 22, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $small = [Drawing.Font]::new('KaiTi', 13, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([Drawing.ColorTranslator]::FromHtml('#E7DCBB'))
        $g.DrawRectangle((Stroke '#675B3D' 2), 2, 2, 279, 45)
        $g.DrawRectangle((Stroke '#A89C6F' 1), 6, 6, 271, 37)
        $g.TranslateTransform(35, 27); $g.ScaleTransform(0.25, 0.25); ThunderChariot $g; $g.ResetTransform()
        $g.DrawString($card.Name, $font, (Ink $card.Accent), 69, 11)
        $g.DrawString($card.Type, $small, (Ink '#7B704B'), 238, 17)
        $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
        Write-Output $path
    } finally { $font.Dispose(); $small.Dispose(); $g.Dispose(); $bitmap.Dispose() }
}
try {
    foreach ($card in $cards) {
        if ($card.Template) {
            foreach ($prefix in @('', 'equip-')) {
                $path = Join-Path $assetRoot "$prefix$($card.Kind).png"
                if (Test-Path -LiteralPath $path) { continue }
                Copy-Item -LiteralPath (Join-Path $assetRoot "$prefix$($card.Template).png") -Destination $path
                Write-Output $path
            }
            continue
        }
        if ($card.Kind -eq 'ThunderChariot') { EquipmentStrip $card }
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
            $heading = if ($card.Kind -eq 'WumiaoFlood') { '武庙衍生牌' } elseif ($card.Kind -eq 'QizhengXiangsheng') { '天佐衍生牌' } else { '官渡之战' }
            Center $g $heading 13 '#90845B' 19 23
            $g.DrawEllipse((Stroke '#B2A47C' 1), 19, 54, 148, 140); $g.DrawEllipse((Stroke '#CDC397' 4), 27, 62, 132, 124)
            $g.TranslateTransform(93, 124)
            switch ($card.Kind) {
                'ThunderChariot' { ThunderChariot $g }
                'WumiaoFlood' { Flood $g }
                'QizhengXiangsheng' {
                    $g.DrawArc((Stroke '#746046' 4), -53, -49, 106, 106, 32, 146)
                    $g.DrawArc((Stroke '#496F86' 4), -53, -49, 106, 106, 212, 146)
                    Banner $g -36 -38 '#496F86'; Banner $g 17 -27 '#A27C5F'
                    Polygon $g '#746046' @(-55, 4, -44, 19, -34, 2) '#746046'
                    Polygon $g '#496F86' @(55, 4, 44, -11, 34, 6) '#496F86'
                    $g.DrawLine((Stroke '#8D7B55' 2), -38, 48, 47, 48)
                }
                'Intoxication' {
                    Polygon $g '#B78560' @(-31, -34, -5, -34, -5, -17, 12, 3, 17, 40, 7, 51, -42, 51, -48, 37, -44, 1, -31, -17) $card.Accent
                    $g.FillRectangle((Ink '#80563D'), -35, -44, 34, 11)
                    $g.DrawEllipse((Stroke '#E2C798' 2), -39, -2, 42, 36)
                    $font = [Drawing.Font]::new('KaiTi', 26, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
                    try { $g.DrawString('酗', $font, (Ink '#F0DEC0'), [Drawing.PointF]::new(-33, 0)) } finally { $font.Dispose() }
                    Polygon $g '#9A5C45' @(24, 18, 60, 12, 54, 37, 31, 41) $card.Accent
                    $g.DrawEllipse((Stroke '#C48A61' 3), 24, 8, 36, 12)
                    $g.DrawBezier((Stroke '#B36D58' 3), 30, -8, 10, -28, 52, -26, 32, -50)
                    $g.DrawBezier((Stroke '#C49573' 2), 48, -4, 28, -23, 68, -26, 47, -44)
                }
                'StoreGrain' {
                    Polygon $g '#B99B5C' @(-48, 4, -6, -24, 41, 5, 38, 50, -45, 50) $card.Accent
                    Polygon $g '#8D7941' @(-57, 6, -7, -34, 52, 7, 40, 15, -7, -14, -47, 15) $card.Accent
                    foreach ($x in @(-28, -8, 12)) { $g.DrawLine((Stroke '#8D7849' 2), $x, 15, $x, 47) }
                    foreach ($x in @(-35, -4, 28)) {
                        $g.FillEllipse((Ink '#CFB47A'), $x, 23, 26, 30); $g.DrawEllipse((Stroke '#8D7941' 2), $x, 23, 26, 30)
                        $g.DrawLine((Stroke '#A78B53' 2), ($x + 7), 22, ($x + 18), 22)
                    }
                    $g.DrawLine((Stroke '#9C8750' 3), 25, -45, 25, -8)
                    foreach ($y in @(-42, -31, -20)) {
                        Polygon $g '#BCA25F' @(25, $y, 13, ($y - 4), 16, ($y + 6), 25, ($y + 9), 35, ($y + 4), 38, ($y - 5)) '#A18A4C'
                    }
                }
                'Reinforcements' {
                    Banner $g -44 -48 '#6F8775'; Banner $g 17 -39 '#8B6D53'
                    foreach ($x in @(-23, 8, 37)) {
                        $g.FillEllipse((Ink '#6E7A6D'), ($x - 7), -5, 14, 14)
                        Polygon $g '#7D8C7A' @(($x - 8), 11, ($x + 8), 11, ($x + 13), 40, ($x - 13), 40) $card.Accent
                        $g.DrawLine((Stroke '#687667' 4), ($x - 5), 40, ($x - 10), 53)
                        $g.DrawLine((Stroke '#687667' 4), ($x + 5), 40, ($x + 9), 53)
                        $g.DrawLine((Stroke '#8A7856' 2), ($x + 17), -8, ($x + 14), 49)
                    }
                    $g.DrawLine((Stroke '#A59666' 2), -53, 57, 56, 57)
                }
                'UnitedEffort' {
                    Banner $g -44 -48 '#6A829A'; Banner $g 21 -48 '#A27C5F'
                    $g.DrawEllipse((Stroke '#546D87' 10), -49, -3, 59, 40)
                    $g.DrawEllipse((Stroke '#A17C54' 10), -6, -3, 59, 40)
                    $g.DrawArc((Stroke '#E0C998' 3), -49, -3, 59, 40, 20, 230)
                    $g.DrawArc((Stroke '#E0C998' 3), -6, -3, 59, 40, 200, 230)
                    foreach ($x in @(-23, 0, 23)) { $g.DrawLine((Stroke '#7F8A85' 3), $x, 45, $x, 55) }
                    $g.DrawLine((Stroke '#7F8A85' 3), -35, 56, 35, 56)
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
