# Native artwork for generated weapons. Card rank, suit and general names are runtime overlays.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Cards'
$labelFont = [Drawing.Font]::new('KaiTi', 18, [Drawing.FontStyle]::Bold)
$smallFont = [Drawing.Font]::new('KaiTi', 14)
foreach ($kind in @('RedBloodBlade', 'GeneralWeapon')) {
    foreach ($strip in @($false, $true)) {
        $width = if ($strip) { 284 } else { 186 }
        $height = if ($strip) { 50 } else { 260 }
        $bitmap = [Drawing.Bitmap]::new($width, $height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(243, 232, 206))
        $border = [Drawing.Pen]::new([Drawing.Color]::FromArgb(132, 110, 76), 2)
        $graphics.DrawRectangle($border, 2, 2, ($width - 5), ($height - 5))
        $graphics.TranslateTransform($(if ($strip) { 28 } else { 100 }), $(if ($strip) { 25 } else { 130 }))
        $graphics.RotateTransform(-35)
        $scale = if ($strip) { 0.24 } else { 0.8 }
        $graphics.ScaleTransform($scale, $scale)
        $blade = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(163, 172, 178))
        $edge = [Drawing.Pen]::new([Drawing.Color]::FromArgb(71, 76, 82), 2)
        $points = [Drawing.Point[]]@([Drawing.Point]::new(-11, 43), [Drawing.Point]::new(-14, -70), [Drawing.Point]::new(0, -102), [Drawing.Point]::new(14, -70), [Drawing.Point]::new(11, 43))
        $graphics.FillPolygon($blade, $points)
        $graphics.DrawPolygon($edge, $points)
        $graphics.FillRectangle([Drawing.Brushes]::SaddleBrown, -5, 48, 10, 42)
        $graphics.FillRectangle([Drawing.Brushes]::DarkGoldenrod, -27, 42, 54, 8)
        if ($kind -eq 'RedBloodBlade') { $graphics.FillRectangle([Drawing.Brushes]::Firebrick, -3, -64, 6, 97) }
        $graphics.ResetTransform()
        if ($kind -eq 'RedBloodBlade') {
            $graphics.DrawString('赤血刃', $(if ($strip) { $smallFont } else { $labelFont }), [Drawing.Brushes]::DarkRed, $(if ($strip) { 57 } else { 51 }), $(if ($strip) { 13 } else { 207 }))
        }
        $file = if ($strip) { "equip-$kind.png" } else { "$kind.png" }
        $bitmap.Save((Join-Path $assetRoot $file), [Drawing.Imaging.ImageFormat]::Png)
        $edge.Dispose(); $blade.Dispose(); $border.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}
$labelFont.Dispose(); $smallFont.Dispose()
