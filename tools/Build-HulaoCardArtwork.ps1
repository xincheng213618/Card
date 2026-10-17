# Original project artwork. Rank, suit and effective card rules are runtime overlays.
[CmdletBinding()]
param([switch]$Overwrite)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'))
$cards = @(
    @{ Kind = 'AllianceFeast'; Name = '联军盛宴'; Type = '普通锦囊'; Accent = '#4C6858' },
    @{ Kind = 'WushuangHalberd'; Name = '无双方天戟'; Type = '武器'; Accent = '#685D53' },
    @{ Kind = 'PurpleGoldCrown'; Name = '束发紫金冠'; Type = '宝物'; Accent = '#725177' },
    @{ Kind = 'RedCottonRobe'; Name = '红棉百花袍'; Type = '防具'; Accent = '#96483C' },
    @{ Kind = 'LionBelt'; Name = '玲珑狮蛮带'; Type = '防具'; Accent = '#90713D' }
)
$files = @($cards | ForEach-Object {
    "$($_.Kind).png"
    if ($_.Kind -ne 'AllianceFeast') { "equip-$($_.Kind).png" }
})
foreach ($file in $files) {
    $path = Join-Path $assetRoot $file
    if ((Test-Path -LiteralPath $path) -and -not $Overwrite) {
        throw "Artwork already exists; inspect ownership before explicitly using -Overwrite: $path"
    }
}

$resources = [Collections.Generic.List[IDisposable]]::new()
function Ink([string]$hex) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    $resources.Add($brush)
    return $brush
}
function Stroke([string]$hex, [single]$width = 2) {
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($hex), $width)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $resources.Add($pen)
    return $pen
}
function Font([single]$size, [Drawing.FontStyle]$style = [Drawing.FontStyle]::Regular) {
    $font = [Drawing.Font]::new('KaiTi', $size, $style, [Drawing.GraphicsUnit]::Pixel)
    $resources.Add($font)
    return $font
}
function Points([single[]]$xy) {
    for ($i = 0; $i -lt $xy.Length; $i += 2) { [Drawing.PointF]::new($xy[$i], $xy[$i + 1]) }
}
function Polygon($g, [string]$fill, [single[]]$xy, [string]$edge = '#513F2D') {
    $vertices = [Drawing.PointF[]]@(Points $xy)
    $g.FillPolygon((Ink $fill), $vertices)
    $g.DrawPolygon((Stroke $edge 1.5), $vertices)
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

function DrawFeast($g) {
    # Three standards and shared cups, drawn from simple original geometry.
    foreach ($flag in @(@(-44, '#4C6858'), @(0, '#984B3B'), @(44, '#596779'))) {
        $x = [single]$flag[0]
        $g.DrawLine((Stroke '#6C5437' 3), $x, -69, $x, 22)
        Polygon $g $flag[1] @($x, -67, ($x + 28), -62, ($x + 20), -42, $x, -47)
        $g.DrawLine((Stroke '#D4B374' 1), ($x + 6), -59, ($x + 19), -55)
    }
    $g.FillEllipse((Ink '#9D723E'), -66, 8, 132, 36)
    $g.DrawEllipse((Stroke '#59402B' 2), -66, 8, 132, 36)
    $g.FillEllipse((Ink '#D7B67B'), -61, 9, 122, 26)
    $g.DrawLine((Stroke '#775536' 6), -44, 38, -49, 61)
    $g.DrawLine((Stroke '#775536' 6), 44, 38, 49, 61)
    foreach ($x in @(-41, 0, 41)) {
        $g.FillEllipse((Ink '#E5D1A3'), ($x - 12), 11, 24, 11)
        $g.DrawArc((Stroke '#6F5132' 2), ($x - 12), 7, 24, 23, 0, 180)
        $g.FillEllipse((Ink '#8B4432'), ($x - 9), 11, 18, 6)
        $g.DrawLine((Stroke '#8A6437' 2), $x, 24, $x, 31)
    }
    Polygon $g '#B99456' @(-12, -12, -14, -34, -8, -48, 8, -48, 14, -34, 12, -12)
    $g.DrawEllipse((Stroke '#6C4C2D' 3), 9, -35, 16, 19)
    $g.FillEllipse((Ink '#B99456'), -13, -18, 26, 11)
    $g.DrawLine((Stroke '#EFE0AF' 2), -6, -35, -7, -23)
}
function DrawHalberd($g) {
    $g.RotateTransform(21)
    $g.DrawLine((Stroke '#5E4230' 7), 0, -61, 0, 76)
    $g.DrawLine((Stroke '#A87F4B' 2), -1, -50, -1, 73)
    Polygon $g '#ACB6B5' @(0, -86, -9, -62, -5, -30, 5, -30, 9, -62)
    Polygon $g '#919EA0' @(-7, -60, -34, -52, -37, -25, -26, -36, -7, -34)
    Polygon $g '#BBC2BB' @(7, -60, 34, -52, 37, -25, 26, -36, 7, -34)
    $g.DrawLine((Stroke '#E5E8D4' 1.5), -30, -49, -31, -33)
    $g.DrawLine((Stroke '#E5E8D4' 1.5), 30, -49, 31, -33)
    $g.FillRectangle((Ink '#B48B44'), -11, -32, 22, 8)
    $g.FillRectangle((Ink '#B48B44'), -7, 47, 14, 6)
    Polygon $g '#9B4031' @(-7, -23, -28, -8, -23, 15, -10, 3, -5, -16)
    Polygon $g '#B45640' @(7, -23, 24, -7, 18, 24, 8, 6, 3, -15)
    $g.FillEllipse((Ink '#D4B574'), -6, -28, 12, 12)
}
function DrawCrown($g) {
    $g.DrawBezier((Stroke '#77473B' 4), -12, -30, -44, -48, -17, -70, -50, -84)
    $g.DrawBezier((Stroke '#77473B' 4), 12, -30, 44, -48, 17, -70, 50, -84)
    foreach ($side in @(-1, 1)) {
        for ($i = 0; $i -lt 6; $i++) {
            $x = [single]($side * (24 + $i * 3))
            $y = [single](-50 - $i * 5)
            $g.DrawLine((Stroke '#C49B59' 1.5), $x, $y, ($x + $side * 9), ($y - 3))
        }
    }
    Polygon $g '#BD954B' @(-50, 28, -47, -17, -27, -5, -20, -44, 0, -24, 20, -44, 27, -5, 47, -17, 50, 28)
    Polygon $g '#D3B16A' @(-44, 23, -41, -6, -23, 6, -16, -28, 0, -11, 16, -28, 23, 6, 41, -6, 44, 23)
    $g.FillEllipse((Ink '#725177'), -51, 19, 102, 22)
    $g.DrawEllipse((Stroke '#5C402D' 2), -51, 19, 102, 22)
    $g.DrawArc((Stroke '#C8A65C' 4), -49, 13, 98, 22, 0, 180)
    $g.FillEllipse((Ink '#89608F'), -12, -9, 24, 29)
    $g.DrawEllipse((Stroke '#E1C57E' 3), -12, -9, 24, 29)
    $g.FillEllipse((Ink '#E2C78A'), -4, -3, 6, 7)
}
function DrawRobe($g) {
    Polygon $g '#9A4438' @(-18, -58, -35, -48, -62, -20, -42, 2, -28, -13, -32, 58, 32, 58, 28, -13, 42, 2, 62, -20, 35, -48, 18, -58, 0, -41)
    Polygon $g '#B65B46' @(-16, -54, -27, -45, -19, 12, -25, 54, 5, 54, 13, 12, 2, -35)
    Polygon $g '#D4B16D' @(-18, -58, 0, -42, 18, -58, 11, -35, -3, -16, -10, -35)
    $g.DrawLine((Stroke '#632E2C' 2), -27, -14, -19, 43)
    $g.DrawLine((Stroke '#632E2C' 2), 27, -14, 19, 43)
    $g.FillRectangle((Ink '#D1A95F'), -29, 6, 58, 9)
    $g.FillRectangle((Ink '#704C30'), -7, 5, 14, 12)
    foreach ($point in @(@(-12, 30), @(13, 40), @(-42, -24), @(42, -24))) {
        $x = [single]$point[0]; $y = [single]$point[1]
        foreach ($offset in @(@(-4, 0), @(4, 0), @(0, -4), @(0, 4))) {
            $g.DrawEllipse((Stroke '#D5AC6B' 1), ($x + $offset[0] - 3), ($y + $offset[1] - 3), 6, 6)
        }
        $g.FillEllipse((Ink '#E3C484'), ($x - 2), ($y - 2), 4, 4)
    }
    $g.DrawLine((Stroke '#D2AE69' 3), -31, 55, 31, 55)
}
function DrawBelt($g) {
    Polygon $g '#6A4D35' @(-68, -17, -31, -23, 31, -23, 68, -17, 68, 21, 31, 27, -31, 27, -68, 21)
    $g.DrawLine((Stroke '#C1A16A' 3), -65, -10, 65, -10)
    $g.DrawLine((Stroke '#C1A16A' 3), -65, 14, 65, 14)
    foreach ($x in @(-57, -45, 45, 57)) { $g.FillEllipse((Ink '#CFB47C'), ($x - 2), 0, 4, 4) }
    $mane = [Collections.Generic.List[single]]::new()
    for ($i = 0; $i -lt 24; $i++) {
        $angle = [Math]::PI * 2 * $i / 24
        $radius = if ($i % 2 -eq 0) { 41 } else { 34 }
        $mane.Add([single]([Math]::Cos($angle) * $radius))
        $mane.Add([single]([Math]::Sin($angle) * $radius - 1))
    }
    Polygon $g '#B58A43' $mane.ToArray()
    $g.FillEllipse((Ink '#DCBF7F'), -28, -28, 56, 54)
    $g.FillEllipse((Ink '#AC7E3D'), -30, -30, 14, 18)
    $g.FillEllipse((Ink '#AC7E3D'), 16, -30, 14, 18)
    $g.DrawArc((Stroke '#61452D' 3), -22, -19, 19, 17, 195, 135)
    $g.DrawArc((Stroke '#61452D' 3), 3, -19, 19, 17, 210, 135)
    $g.FillEllipse((Ink '#463B2B'), -15, -9, 6, 7)
    $g.FillEllipse((Ink '#463B2B'), 9, -9, 6, 7)
    Polygon $g '#775039' @(-6, 1, 6, 1, 0, 9)
    $g.DrawArc((Stroke '#68462F' 2), -14, 3, 14, 14, 0, 180)
    $g.DrawArc((Stroke '#68462F' 2), 0, 3, 14, 14, 0, 180)
    $g.DrawArc((Stroke '#AA7A3A' 2), -11, -29, 22, 15, 0, 180)
}

try {
    foreach ($card in $cards) {
        $formats = if ($card.Kind -eq 'AllianceFeast') { @($false) } else { @($false, $true) }
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
                if ($strip) {
                    $g.TranslateTransform(33, 25)
                    $g.ScaleTransform(0.23, 0.23)
                }
                else {
                    $g.DrawEllipse((Stroke '#CEB98E' 1), 17, 53, 152, 143)
                    $g.DrawArc((Stroke '#D8C69F' 5), 25, 61, 136, 127, 210, 205)
                    CenterText $g '虎牢关' (Font 12) '#92754D' 58 18 80 22
                    $g.TranslateTransform(93, 130)
                    $g.ScaleTransform(0.96, 0.96)
                }
                switch ($card.Kind) {
                    'AllianceFeast' { DrawFeast $g }
                    'WushuangHalberd' { DrawHalberd $g }
                    'PurpleGoldCrown' { DrawCrown $g }
                    'RedCottonRobe' { DrawRobe $g }
                    'LionBelt' { DrawBelt $g }
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
                $mode = if ($Overwrite) { [IO.FileMode]::Create } else { [IO.FileMode]::CreateNew }
                $stream = [IO.File]::Open($path, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
                Write-Output $path
            }
            finally { $g.Dispose(); $bitmap.Dispose() }
        }
    }
}
finally { foreach ($resource in $resources) { $resource.Dispose() } }
