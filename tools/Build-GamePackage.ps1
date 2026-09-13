param([ValidatePattern('^8\.0\.\d+$')][string]$RuntimeVersion = '8.0.31')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$distRoot = Join-Path $repoRoot 'dist'
$releaseRoot = Join-Path $distRoot "releases\$buildId"
$packageRoot = Join-Path $releaseRoot 'CardGame'
$artifactsRoot = Join-Path $repoRoot ".artifacts\package-$buildId"
$verificationRoot = Join-Path $artifactsRoot 'Package verification'
New-Item -ItemType Directory -Path $packageRoot, $verificationRoot -Force | Out-Null
Push-Location $repoRoot
try {
    & dotnet publish '.\src\CardGame.Wpf\CardGame.Wpf.csproj' -c Release -r win-x64 --self-contained true `
        --artifacts-path $artifactsRoot -o $packageRoot "-p:RuntimeFrameworkVersion=$RuntimeVersion" `
        -p:PublishSingleFile=false -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE. Previous package is unchanged." }
    foreach ($name in @('CardGame.Wpf.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $name) -PathType Leaf)) { throw "Missing package file: $name" }
    }
    # Compare published runtime DLLs with the resolved NuGet runtime packs before shipping.
    $assets = Get-Content -LiteralPath (Join-Path $artifactsRoot 'obj\CardGame.Wpf\project.assets.json') -Raw | ConvertFrom-Json
    $checkedRuntimeFiles = @{}
    foreach ($pack in @('microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.netcore.app.runtime.win-x64')) {
        $packRoot = $null
        foreach ($cacheRoot in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $cacheRoot "$pack\$RuntimeVersion\runtimes\win-x64"
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packRoot = $candidate; break }
        }
        if (-not $packRoot) { throw "Cannot locate restored runtime pack: $pack $RuntimeVersion" }
        foreach ($runtimePart in @('lib\net8.0', 'native')) {
            $runtimeRoot = Join-Path $packRoot $runtimePart
            foreach ($runtimeFile in (Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File -Filter '*.dll')) {
                $relative = $runtimeFile.FullName.Substring($runtimeRoot.Length + 1)
                # WindowsDesktop replaces a few Core facade assemblies, including WindowsBase.
                if ($checkedRuntimeFiles.ContainsKey($relative)) { continue }
                $checkedRuntimeFiles[$relative] = $true
                $publishedFile = Join-Path $packageRoot $relative
                # Only inspect assets selected for publish.
                if (-not (Test-Path -LiteralPath $publishedFile -PathType Leaf)) { continue }
                if ((Get-FileHash -LiteralPath $runtimeFile.FullName).Hash -ne (Get-FileHash -LiteralPath $publishedFile).Hash) {
                    Write-Warning "Re-copying runtime asset that differs from its restored pack: $relative"
                    [IO.File]::WriteAllBytes($publishedFile, [IO.File]::ReadAllBytes($runtimeFile.FullName))
                    if ((Get-FileHash -LiteralPath $runtimeFile.FullName).Hash -ne (Get-FileHash -LiteralPath $publishedFile).Hash) {
                        throw "Runtime integrity check failed: $relative. Previous package is unchanged."
                    }
                }
            }
        }
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\LOCAL_PLAY.md') -Destination (Join-Path $packageRoot 'README.md')

    $fileManifest = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName.Substring($packageRoot.Length + 1); Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $fileManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'package-files.json') -Encoding UTF8
    $zipPath = Join-Path $releaseRoot 'CardGame-win-x64.zip'
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    # Verify the extracted ZIP in a path with spaces, not just the publish directory.
    Expand-Archive -LiteralPath $zipPath -DestinationPath $verificationRoot
    foreach ($entry in $fileManifest) {
        $extractedFile = Join-Path (Join-Path $verificationRoot 'CardGame') $entry.Path
        if (-not (Test-Path -LiteralPath $extractedFile) -or (Get-FileHash -LiteralPath $extractedFile).Hash -ne $entry.Sha256) {
            throw "ZIP file verification failed: $($entry.Path). Previous package is unchanged."
        }
    }
    $verifiedExe = Join-Path $verificationRoot 'CardGame\CardGame.Wpf.exe'
    $reportPath = Join-Path $verificationRoot 'package-check.json'
    $process = Start-Process -FilePath $verifiedExe -ArgumentList @('--verify-package', ('"' + $reportPath + '"')) `
        -WorkingDirectory $verificationRoot -WindowStyle Hidden -PassThru
    $processHandle = $process.Handle
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Package verification exceeded 60 seconds. Previous package is unchanged.'
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) { throw "Package verification failed. See $reportPath" }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.Success -or $report.Runtime -ne ".NET $RuntimeVersion" -or $report.Architecture -ne 'X64') {
        throw "Package runtime or resource verification failed. See $reportPath"
    }
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$hash  CardGame-win-x64.zip" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $releaseRoot 'verification.json')
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($reportPath, '.png')) -Destination (Join-Path $releaseRoot 'preview.png')

    $pointerPath = Join-Path $distRoot 'current-package.json'
    $temporaryPointer = Join-Path $distRoot "current-$buildId.tmp"
    [ordered]@{
        Build = $buildId
        Executable = "releases/$buildId/CardGame/CardGame.Wpf.exe"
        Archive = "releases/$buildId/CardGame-win-x64.zip"
        Sha256 = $hash
        RuntimeVersion = $RuntimeVersion
    } | ConvertTo-Json | Set-Content -LiteralPath $temporaryPointer -Encoding UTF8
    if (Test-Path -LiteralPath $pointerPath) { [IO.File]::Replace($temporaryPointer, $pointerPath, (Join-Path $distRoot "previous-$buildId.json")) }
    else { [IO.File]::Move($temporaryPointer, $pointerPath) }
    Write-Output "Package verified: $zipPath"
    Write-Output "Launch from: $(Join-Path $repoRoot 'Play.cmd')"
}
finally { Pop-Location }
