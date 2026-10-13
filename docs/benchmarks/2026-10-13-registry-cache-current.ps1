#requires -Version 7.0
# Read-only workspace comparison: fresh Build versus actual immutable registry reuse.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRepo = 'C:\Users\17917\Desktop\Card'
$taskRoot = 'C:\Users\17917\AppData\Local\Temp\CardGame-Startup-01a13324'
$taskPrefix = Join-Path $taskRepo 'docs\benchmarks\2026-10-13-registry-cache-'
$taskSource = Join-Path $taskRepo 'tests\CardGame.Wpf.Tests\StableHandResponseFixture.cs'
$taskHarness = Join-Path $taskRoot 'amount-cache-harness\RegistryCacheHarness.csproj'
$taskBench = Join-Path $taskRoot 'bin\RegistryCacheHarness\release\RegistryCacheHarness.dll'
$taskSeal = Get-Content -LiteralPath (Join-Path $taskRepo 'docs\benchmarks\2026-10-13-routine-output-seal.json') -Raw | ConvertFrom-Json -AsHashtable
$taskSourceHash = (Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
$taskLock = $null
$taskSucceeded = $false
function Assert-Inputs {
    if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -cne $taskSourceHash) { throw 'Fixture changed during measurement.' }
    foreach ($path in $taskSeal.Dependencies.Keys) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $taskSeal.Dependencies[$path]) { throw "Frozen dependency changed: $path" }
    }
}
try {
    $owner = Get-Content -LiteralPath (Join-Path $taskRoot '.test-changed-owner.json') -Raw | ConvertFrom-Json
    if ($owner.repository -ne $taskRepo -or $owner.artifactsPath -ne $taskRoot) { throw 'Output ownership mismatch.' }
    $taskLock = [IO.File]::Open((Join-Path $taskRoot '.test-changed.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    Assert-Inputs
    & dotnet build (Join-Path $taskRepo 'tests\CardGame.Wpf.Tests\CardGame.Wpf.Tests.csproj') -c Release --no-dependencies --no-restore --artifacts-path $taskRoot 2>&1 |
        Tee-Object -LiteralPath ($taskPrefix + 'current-tests-build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Test-only build failed.' }
    & dotnet build $taskHarness -c Release --artifacts-path $taskRoot "-p:CardBuildRoot=$taskRoot" 2>&1 |
        Tee-Object -LiteralPath ($taskPrefix + 'current-harness-build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Registry harness build failed.' }
    Assert-Inputs
    foreach ($sample in 1..2) {
        & dotnet $taskBench 2>&1 | Tee-Object -LiteralPath ($taskPrefix + "current$sample.json") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Registry acquisition measurement failed.' }
        $result = Get-Content -LiteralPath ($taskPrefix + "current$sample.json") -Raw | ConvertFrom-Json
        if ($result.Samples.Count -ne 12) { throw 'Expected six alternating pairs.' }
        Assert-Inputs
    }
    $taskSucceeded = $true
} finally {
    if ($null -ne $taskLock) { $taskLock.Dispose() }
    [ordered]@{RecordedAt=[DateTimeOffset]::Now.ToString('o');Succeeded=$taskSucceeded;SourceNeverWritten=$true;
        SourceHash=$taskSourceHash;CurrentSourceHash=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash;
        FrozenDependencies=$taskSeal.Dependencies;
        Scope='Actual fresh response registry acquisition versus current warm reuse, alternating pairs in two fresh processes';
        Supersedes='Earlier alias and incorrect general-count attempts produced no valid performance results'} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($taskPrefix + 'current-summary.json') -Encoding utf8
}
