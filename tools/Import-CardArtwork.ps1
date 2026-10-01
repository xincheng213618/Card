# Import authenticated artwork without painting over it or baking in runtime suit/rank.
[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$Ffmpeg = 'ffmpeg',
    [string[]]$Kinds,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$catalogPath = Join-Path $repoRoot 'docs\content\card-art-catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repoRoot '.artifacts\card-artwork\source' }
$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
[IO.Directory]::CreateDirectory($SourceDirectory) | Out-Null
$entries = @($catalog.cards) + @($catalog.components)
if ($Kinds) {
    foreach ($kind in $Kinds) {
        if (@($catalog.cards | Where-Object { $_.kind -ceq $kind }).Count -ne 1) { throw "Unknown or duplicate card kind: $kind" }
    }
    $entries = @($catalog.cards | Where-Object { $_.kind -cin $Kinds })
}

function Get-VerifiedSource([string]$Name, [string]$Url, [string]$Hash) {
    if ($Name -notmatch '^[A-Za-z0-9_.-]+$') { throw "Invalid source file name: $Name" }
    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('sanguosha.com', 'www.sanguosha.com', 'web.sanguosha.com')) {
        throw "Unexpected official artwork host: $Url"
    }
    $path = Join-Path $SourceDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $uri -OutFile $path }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) {
        throw "Source hash mismatch: $path. Review the source version before importing."
    }
    return $path
}

Add-Type -AssemblyName PresentationCore, WindowsBase
function Read-ArtworkBitmap([string]$Path) {
    $image = [Windows.Media.Imaging.BitmapImage]::new()
    $image.BeginInit()
    $image.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $image.UriSource = [Uri]::new($Path)
    $image.EndInit()
    $image.Freeze()
    return $image
}

$atlasEntries = @($entries | Where-Object { -not $_.sourceKind })
$atlas = $null
$bitmap = $null
if ($atlasEntries.Count -gt 0) {
    $atlasPath = Get-VerifiedSource 'cards.atlas' $catalog.source.atlasUrl $catalog.source.atlasSha256
    $imagePath = Get-VerifiedSource 'cards.webp' $catalog.source.imageUrl $catalog.source.imageSha256
    $atlas = Get-Content -LiteralPath $atlasPath -Raw | ConvertFrom-Json
    $decodedPath = Join-Path $SourceDirectory 'cards-decoded.png'
    & $Ffmpeg -hide_banner -loglevel error -y -i $imagePath -frames:v 1 $decodedPath
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg could not decode the source atlas.' }
    $bitmap = Read-ArtworkBitmap $decodedPath
}

$prepared = @{}
# Validate every selected mapping and transformed source before writing delivered assets.
foreach ($entry in $entries) {
    if ($entry.file -notmatch '^[A-Za-z0-9_-]+\.png$' -or ($entry.kind -and $entry.file -cne "$($entry.kind).png")) {
        throw "Invalid card file name: $($entry.file)"
    }
    if (-not $entry.sourceKind) {
        $sprite = $atlas.frames.PSObject.Properties[$entry.frame].Value
        if (-not $sprite -or $sprite.rotated -or $sprite.frame.idx -ne 0 -or
            $sprite.frame.w -ne $sprite.sourceSize.w -or $sprite.frame.h -ne $sprite.sourceSize.h -or
            $sprite.spriteSourceSize.x -ne 0 -or $sprite.spriteSourceSize.y -ne 0 -or
            $sprite.frame.w -le 0 -or $sprite.frame.h -le 0 -or $sprite.frame.x -lt 0 -or $sprite.frame.y -lt 0 -or
            ($sprite.frame.x + $sprite.frame.w) -gt $bitmap.PixelWidth -or ($sprite.frame.y + $sprite.frame.h) -gt $bitmap.PixelHeight -or
            ($entry.kind -and ($sprite.frame.w -ne 186 -or $sprite.frame.h -ne 260))) {
            throw "Missing, trimmed, rotated or invalid card sprite: $($entry.frame)"
        }
        continue
    }
    if ($entry.sourceKind -ceq 'projectGenerated') {
        $generatedPath = Join-Path $repoRoot "src\CardGame.Wpf\Assets\Cards\$($entry.file)"
        if ($entry.source.generator -notmatch '^tools/[A-Za-z0-9_-]+\.ps1$' -or
            -not (Test-Path -LiteralPath (Join-Path $repoRoot $entry.source.generator))) {
            throw "Missing recorded project artwork generator: $($entry.file)"
        }
        if (-not (Test-Path -LiteralPath $generatedPath)) {
            throw "Missing existing project artwork: $($entry.file). Review and run $($entry.source.generator) separately."
        }
        if ((Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Existing project artwork hash changed: $($entry.file). Review its generator output before importing."
        }
        $generated = Read-ArtworkBitmap $generatedPath
        if ($entry.source.originalSize.Count -ne 2 -or
            $generated.PixelWidth -ne $entry.source.originalSize[0] -or $generated.PixelHeight -ne $entry.source.originalSize[1] -or
            ($entry.kind -and ($generated.PixelWidth -ne 186 -or $generated.PixelHeight -ne 260))) {
            throw "Invalid existing project artwork dimensions: $($entry.file)"
        }
        continue
    }
    if ($entry.sourceKind -cnotin @('officialCardFace', 'officialIllustration') -or -not $entry.kind -or
        $entry.source.rankSuitBakedIn -ne $false -or $entry.source.originalSize.Count -ne 2 -or
        $entry.transform.outputSize.Count -ne 2 -or $entry.transform.outputSize[0] -ne 186 -or
        $entry.transform.outputSize[1] -ne 260) { throw "Invalid official source mapping: $($entry.file)" }
    $extension = [IO.Path]::GetExtension(([Uri]$entry.source.imageUrl).AbsolutePath)
    if ($extension -notin @('.png', '.jpg', '.jpeg')) { throw "Unsupported official image format: $extension" }
    $sourcePath = Get-VerifiedSource "$($entry.kind)-original$extension" $entry.source.imageUrl $entry.source.imageSha256
    $original = Read-ArtworkBitmap $sourcePath
    if ($original.PixelWidth -ne $entry.source.originalSize[0] -or $original.PixelHeight -ne $entry.source.originalSize[1]) {
        throw "Official source dimensions changed: $($entry.kind)"
    }
    $transform = $entry.transform
    $scaled = $transform.scaledSize
    if ($scaled.Count -ne 2 -or $scaled[0] -le 0 -or $scaled[1] -le 0 -or
        ($scaled[0] * $original.PixelHeight) -ne ($scaled[1] * $original.PixelWidth)) {
        throw "Artwork scaling would change its aspect ratio: $($entry.kind)"
    }
    if ($entry.sourceKind -ceq 'officialCardFace') {
        if ($transform.mode -cne 'scale' -or $transform.filter -cne 'neighbor' -or $transform.scale -ne 2 -or
            $scaled[0] -ne $original.PixelWidth * 2 -or $scaled[1] -ne $original.PixelHeight * 2 -or
            $scaled[0] -ne 186 -or $scaled[1] -ne 260 -or $transform.padding.Count -ne 4 -or
            @($transform.padding | Where-Object { $_ -ne 0 }).Count -ne 0) { throw "Invalid complete-face transform: $($entry.kind)" }
        $filter = "format=rgba,scale=$($scaled[0]):$($scaled[1]):flags=neighbor"
    } else {
        $padding = $transform.padding
        if ($transform.mode -cne 'contain' -or $transform.filter -cne 'lanczos' -or $transform.background -cne 'transparent' -or
            $padding.Count -ne 4 -or $padding[0] -ne $padding[2] -or $padding[1] -ne $padding[3] -or
            @($padding | Where-Object { $_ -lt 0 }).Count -ne 0 -or
            ($scaled[0] + $padding[0] + $padding[2]) -ne 186 -or ($scaled[1] + $padding[1] + $padding[3]) -ne 260) {
            throw "Invalid uncropped illustration transform: $($entry.kind)"
        }
        $filter = "scale=$($scaled[0]):$($scaled[1]):flags=lanczos,format=rgba,pad=186:260:$($padding[0]):$($padding[1]):color=0x00000000"
    }
    $preparedPath = Join-Path $SourceDirectory "$($entry.kind)-prepared.png"
    & $Ffmpeg -hide_banner -loglevel error -y -i $sourcePath -vf $filter -frames:v 1 $preparedPath
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg could not adapt official artwork: $($entry.kind)" }
    $delivered = Read-ArtworkBitmap $preparedPath
    if ($delivered.PixelWidth -ne 186 -or $delivered.PixelHeight -ne 260) { throw "Invalid adapted face dimensions: $($entry.kind)" }
    $prepared[$entry.file] = $preparedPath
}

if ($ValidateOnly) {
    Write-Output "Validated $($entries.Count) authenticated artwork inputs and transforms; delivered assets and catalog unchanged."
    return
}
$outputDir = Join-Path $repoRoot 'src\CardGame.Wpf\Assets\Cards'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
foreach ($entry in $entries) {
    $path = Join-Path $outputDir $entry.file
    if ($entry.sourceKind -ceq 'projectGenerated') { continue }
    if ($entry.sourceKind) {
        Copy-Item -LiteralPath $prepared[$entry.file] -Destination $path
    } else {
        $frame = $atlas.frames.PSObject.Properties[$entry.frame].Value.frame
        $rect = [Windows.Int32Rect]::new($frame.x, $frame.y, $frame.w, $frame.h)
        $crop = [Windows.Media.Imaging.CroppedBitmap]::new($bitmap, $rect)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($crop))
        $stream = [IO.File]::Create($path)
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        $entry | Add-Member -NotePropertyName rectangle -NotePropertyValue @($frame.x, $frame.y, $frame.w, $frame.h) -Force
    }
    $entry | Add-Member -NotePropertyName sha256 -NotePropertyValue (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -Force
}
$catalog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $catalogPath -Encoding utf8
Write-Output "Imported $($entries.Count) authenticated card artwork entries to $outputDir"
