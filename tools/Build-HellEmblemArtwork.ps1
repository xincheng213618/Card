# Project-native typographic seals. These are not official character portraits.
[CmdletBinding()]
param([switch]$GenerateMissing)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\CardGame.Wpf\Assets\Generals'))
$seals = @(
    @('qinguang','秦广王','秦','#9E794B'), @('chujiang','楚江王','楚','#557D96'),
    @('songdi','宋帝王','宋','#747884'), @('wuguan','五官王','官','#6F8877'),
    @('yanluo','阎罗王','阎','#9B5E50'), @('biancheng','卞城王','卞','#6A6D9B'),
    @('taishan','泰山王','泰','#8D7964'), @('dushi','都市王','都','#A35740'),
    @('pingdeng','平等王','平','#877397'), @('zhuanlun','转轮王','轮','#8E834E'),
    @('mengpo','孟婆','孟','#648D8C'), @('dizang','地藏王','藏','#C09D55')
)
foreach ($seal in $seals) {
    $path = Join-Path $assetRoot "project-hell-$($seal[0]).png"
    if ((Test-Path -LiteralPath $path) -and -not $GenerateMissing) { throw "Artwork already exists; inspect it before regenerating: $path" }
}

$cardPath = [IO.Path]::GetFullPath((Join-Path $assetRoot '..\Cards\ReturnSoul.png'))
if ((Test-Path -LiteralPath $cardPath) -and -not $GenerateMissing) { throw "Card artwork already exists; inspect before regenerating: $cardPath" }
if (-not (Test-Path -LiteralPath $cardPath)) {
$cardBitmap = [Drawing.Bitmap]::new(186,260)
$cardGraphics = [Drawing.Graphics]::FromImage($cardBitmap)
$cardResources = [Collections.Generic.List[IDisposable]]::new()
try {
    $cardGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $paper = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#DED4BB')); $cardResources.Add($paper)
    $dark = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#354C50')); $cardResources.Add($dark)
    $border = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#5E7370'),3); $cardResources.Add($border)
    $cardGraphics.FillRectangle($paper,0,0,186,260)
    $cardGraphics.DrawRectangle($border,5,5,175,249)
    $cardGraphics.DrawEllipse($border,35,69,116,116)
    $cardGraphics.DrawEllipse($border,47,81,92,92)
    $format = [Drawing.StringFormat]::new(); $format.Alignment = [Drawing.StringAlignment]::Center; $cardResources.Add($format)
    foreach ($line in @(@('回魂',33,16),@('魂',62,84),@('普通锦囊',18,195),@('OL 2020 · 本地制图',10,230))) {
        $font = [Drawing.Font]::new('KaiTi',[single]$line[1],[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel); $cardResources.Add($font)
        $cardGraphics.DrawString($line[0],$font,$dark,[Drawing.RectangleF]::new(0,[single]$line[2],186,90),$format)
    }
    $stream = [IO.File]::Open($cardPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $cardBitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
    Write-Output $cardPath
}
finally { foreach ($resource in $cardResources) { $resource.Dispose() }; $cardGraphics.Dispose(); $cardBitmap.Dispose() }
}
foreach ($seal in $seals) {
    if ($GenerateMissing -and (Test-Path -LiteralPath (Join-Path $assetRoot "project-hell-$($seal[0]).png"))) { continue }
    $bitmap = [Drawing.Bitmap]::new(300,400)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $resources = [Collections.Generic.List[IDisposable]]::new()
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $background = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,300,400),
            [Drawing.ColorTranslator]::FromHtml('#263035'),[Drawing.ColorTranslator]::FromHtml('#101417'),90)
        $resources.Add($background)
        $graphics.FillRectangle($background,0,0,300,400)
        $accent = [Drawing.ColorTranslator]::FromHtml($seal[3])
        $pen = [Drawing.Pen]::new($accent,3); $resources.Add($pen)
        $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#DFD1AE')); $resources.Add($ink)
        $muted = [Drawing.SolidBrush]::new($accent); $resources.Add($muted)
        $graphics.DrawRectangle($pen,11,11,278,378)
        $graphics.DrawRectangle($pen,21,21,258,358)
        $graphics.DrawEllipse($pen,39,67,222,222)
        $graphics.DrawEllipse($pen,50,78,200,200)
        $points = [Drawing.Point[]]@([Drawing.Point]::new(65,92),[Drawing.Point]::new(81,46),
            [Drawing.Point]::new(120,68),[Drawing.Point]::new(150,32),[Drawing.Point]::new(180,68),
            [Drawing.Point]::new(219,46),[Drawing.Point]::new(235,92))
        $graphics.FillPolygon($muted,$points)
        $format = [Drawing.StringFormat]::new(); $format.Alignment = [Drawing.StringAlignment]::Center; $resources.Add($format)
        foreach ($line in @(@($seal[2],112,108,$ink),@($seal[1],31,296,$ink),@('OL 2020 · 本地殿印',16,350,$muted))) {
            $font = [Drawing.Font]::new('KaiTi',[single]$line[1],[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
            $resources.Add($font)
            $graphics.DrawString([string]$line[0],$font,$line[3],[Drawing.RectangleF]::new(0,[single]$line[2],300,155),$format)
        }
        $path = Join-Path $assetRoot "project-hell-$($seal[0]).png"
        $stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png) } finally { $stream.Dispose() }
        Write-Output $path
    }
    finally { foreach ($resource in $resources) { $resource.Dispose() }; $graphics.Dispose(); $bitmap.Dispose() }
}
