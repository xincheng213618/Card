#requires -Version 7.0
<#
.SYNOPSIS
Copies one committed build baseline to new, isolated worker directories.
.EXAMPLE
./tools/New-GeneralBatchSnapshot.ps1 -Revision HEAD -Destination C:/Temp/Card-batch5 -WorkerDirectories @('C:/Temp/Card-worker-a', 'C:/Temp/Card-worker-b')
#>
[CmdletBinding()]
param(
    [string] $Revision = 'HEAD',
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [string[]] $WorkerDirectories,
    [switch] $IncludeReferenceData
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$startedAt = [DateTimeOffset]::Now
$resolvedCommit = & git -C $repoRoot rev-parse --verify "$Revision^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'Revision must resolve to a committed source tree.' }
$sourceCommit = $resolvedCommit.Trim()
$tracked = @(& git -C $repoRoot -c core.quotepath=false ls-tree -r --name-only $sourceCommit)
if ($LASTEXITCODE -ne 0) { throw 'Could not list the committed source tree.' }
$roots = @('src', 'tests', 'tools', 'docs')
if ($IncludeReferenceData) { $roots += 'data' }
$rootFiles = @($tracked | Where-Object { $_ -notmatch '/' })
$pathspecs = @($roots | Where-Object { $root = $_; @($tracked | Where-Object { $_.StartsWith("$root/") }).Count -gt 0 }) + $rootFiles
$expectedPaths = @($tracked | Where-Object { $_ -notmatch '/' -or ($_ -split '/')[0] -in $roots })
foreach ($required in @('CardGame.sln', 'global.json', 'tools/Test-Changed.ps1')) {
    if ($required -notin $expectedPaths) { throw "Committed baseline is missing $required." }
}

# Every destination must be new. Never overwrite or delete an earlier experiment.
$destinationPath = [IO.Path]::GetFullPath($Destination)
$workerPaths = @($WorkerDirectories | ForEach-Object { [IO.Path]::GetFullPath($_) })
if ($workerPaths.Count -eq 0) { throw 'At least one worker directory is required.' }
$targets = @($destinationPath) + $workerPaths
for ($i = 0; $i -lt $targets.Count; $i++) {
    if (Test-Path -LiteralPath $targets[$i]) { throw "Destination already exists: $($targets[$i])" }
    $prefix = $targets[$i].TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($repoRoot.Equals($targets[$i], [StringComparison]::OrdinalIgnoreCase) -or
        $repoRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A destination cannot contain the source repository.'
    }
    for ($j = 0; $j -lt $targets.Count; $j++) {
        if ($i -eq $j) { continue }
        if ($targets[$j].Equals($targets[$i], [StringComparison]::OrdinalIgnoreCase) -or
            $targets[$j].StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Destination and worker paths must be distinct and must not contain each other.'
        }
    }
}

[IO.Directory]::CreateDirectory($destinationPath) | Out-Null
$archive = Join-Path $destinationPath 'source.zip'
$baseline = Join-Path $destinationPath 'baseline'
$summaryPath = Join-Path $destinationPath 'snapshot.json'
$summary = [ordered]@{
    status = 'preparing'; startedAt = $startedAt.ToString('o'); sourceCommit = $sourceCommit
    revisionRequested = $Revision; baseline = $baseline; workers = $workerPaths
    includesReferenceData = [bool]$IncludeReferenceData; copiedFiles = 0; bytesPerCopy = 0
    omittedReferenceFiles = @($tracked | Where-Object { $_.StartsWith('data/') -and !$IncludeReferenceData }).Count
    archiveMs = $null; extractMs = $null; copyAndVerifyMs = $null; durationMs = $null
}
try {
    $phase = [Diagnostics.Stopwatch]::StartNew()
    # PNGs are already compressed; ZIP store avoids recompressing every portrait.
    & git -C $repoRoot archive --format=zip -0 --output=$archive $sourceCommit -- @pathspecs
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $summary.archiveMs = $phase.ElapsedMilliseconds
    $phase.Restart()
    Expand-Archive -LiteralPath $archive -DestinationPath $baseline
    $summary.extractMs = $phase.ElapsedMilliseconds
    $phase.Restart()
    foreach ($worker in $workerPaths) { [IO.Directory]::CreateDirectory($worker) | Out-Null }
    $manifest = [Collections.Generic.List[object]]::new()
    foreach ($relative in $expectedPaths) {
        $source = Join-Path $baseline $relative
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing archived file: $relative" }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $bytes = (Get-Item -LiteralPath $source).Length
        foreach ($worker in $workerPaths) {
            $target = Join-Path $worker $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.File]::Copy($source, $target, $false)
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) {
                throw "Worker copy hash mismatch: $target"
            }
        }
        $manifest.Add([ordered]@{ path = $relative; bytes = $bytes; sha256 = $hash })
        $summary.bytesPerCopy += $bytes
    }
    $summary.copyAndVerifyMs = $phase.ElapsedMilliseconds
    $summary.copiedFiles = $manifest.Count
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destinationPath 'manifest.json') -Encoding utf8
    $summary.status = 'verified'
} catch {
    $summary.status = 'failed'
    $summary.error = $_.Exception.Message
    throw
} finally {
    $summary.completedAt = [DateTimeOffset]::Now.ToString('o')
    $summary.durationMs = ([DateTimeOffset]::Now - $startedAt).TotalMilliseconds
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding utf8
}
[pscustomobject]$summary
