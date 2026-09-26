# Unpack original atlas sprites; never paint over artwork or bake in suit/rank.
[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$Ffmpeg = 'ffmpeg'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$catalogPath = Join-Path $repoRoot 'docs\content\card-art-catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repoRoot '.artifacts\card-artwork\source' }
$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
[IO.Directory]::CreateDirectory($SourceDirectory) | Out-Null
$atlasPath = Join-Path $SourceDirectory 'cards.atlas'
$imagePath = Join-Path $SourceDirectory 'cards.webp'
foreach ($inputFile in @(
    @{ Path = $atlasPath; Url = $catalog.source.atlasUrl; Hash = $catalog.source.atlasSha256 },
    @{ Path = $imagePath; Url = $catalog.source.imageUrl; Hash = $catalog.source.imageSha256 }
)) {
    if (-not (Test-Path -LiteralPath $inputFile.Path)) {
        Invoke-WebRequest -Uri $inputFile.Url -OutFile $inputFile.Path
    }
    if ((Get-FileHash -LiteralPath $inputFile.Path -Algorithm SHA256).Hash -ne $inputFile.Hash) {
        throw "Source hash mismatch: $($inputFile.Path). Review the atlas version before importing."
    }
}
$atlas = Get-Content -LiteralPath $atlasPath -Raw | ConvertFrom-Json
$decodedPath = Join-Path $SourceDirectory 'cards-decoded.png'
& $Ffmpeg -hide_banner -loglevel error -y -i $imagePath -frames:v 1 $decodedPath
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg could not decode the source atlas.' }
Add-Type -AssemblyName PresentationCore, WindowsBase
$bitmap = [Windows.Media.Imaging.BitmapImage]::new()
$bitmap.BeginInit()
$bitmap.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$bitmap.UriSource = [Uri]::new($decodedPath)
$bitmap.EndInit()
$bitmap.Freeze()
$outputDir = Join-Path $repoRoot 'src\CardGame.Wpf\Assets\Cards'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
# Validate every mapping before writing any sprite.
$entries = @($catalog.cards) + @($catalog.components)
foreach ($entry in $entries) {
    if ($entry.file -notmatch '^[A-Za-z0-9_-]+\.png$' -or ($entry.kind -and $entry.file -cne "$($entry.kind).png")) {
        throw "Invalid card file name: $($entry.file)"
    }
    $sprite = $atlas.frames.PSObject.Properties[$entry.frame].Value
    if (-not $sprite -or $sprite.rotated -or $sprite.frame.idx -ne 0 -or
        $sprite.frame.w -ne $sprite.sourceSize.w -or $sprite.frame.h -ne $sprite.sourceSize.h -or
        $sprite.spriteSourceSize.x -ne 0 -or $sprite.spriteSourceSize.y -ne 0 -or
        $sprite.frame.w -le 0 -or $sprite.frame.h -le 0 -or $sprite.frame.x -lt 0 -or $sprite.frame.y -lt 0 -or
        ($sprite.frame.x + $sprite.frame.w) -gt $bitmap.PixelWidth -or ($sprite.frame.y + $sprite.frame.h) -gt $bitmap.PixelHeight -or
        ($entry.kind -and ($sprite.frame.w -ne 186 -or $sprite.frame.h -ne 260))) {
        throw "Missing, trimmed, rotated or invalid card sprite: $($entry.frame)"
    }
}
foreach ($entry in $entries) {
    $frame = $atlas.frames.PSObject.Properties[$entry.frame].Value.frame
    $rect = [Windows.Int32Rect]::new($frame.x, $frame.y, $frame.w, $frame.h)
    $crop = [Windows.Media.Imaging.CroppedBitmap]::new($bitmap, $rect)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($crop))
    $path = Join-Path $outputDir $entry.file
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $entry | Add-Member -NotePropertyName rectangle -NotePropertyValue @($frame.x, $frame.y, $frame.w, $frame.h) -Force
    $entry | Add-Member -NotePropertyName sha256 -NotePropertyValue (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -Force
}
$catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $catalogPath -Encoding utf8
Write-Output "Imported $($catalog.cards.Count) original card faces and $($catalog.components.Count) components to $outputDir"
