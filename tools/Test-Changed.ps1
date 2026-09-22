#requires -Version 7.0
<#
.SYNOPSIS
Builds an isolated test scope once, then runs the built test executables directly.

.EXAMPLE
./tools/Test-Changed.ps1 -CoreFilter @('draw-phase', 'lifecycle programs')

.EXAMPLE
./tools/Test-Changed.ps1 -WpfFilter 'public reveal' -ArtifactsPath D:/temp/card-checks

.EXAMPLE
./tools/Test-Changed.ps1 -Full
#>
[CmdletBinding()]
param(
    [string[]] $CoreFilter,
    [string[]] $WpfFilter,
    [switch] $Full,
    [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$coreProject = Join-Path $repoRoot 'tests/CardGame.Core.Tests/CardGame.Core.Tests.csproj'
$wpfProject = Join-Path $repoRoot 'tests/CardGame.Wpf.Tests/CardGame.Wpf.Tests.csproj'
$solution = Join-Path $repoRoot 'CardGame.sln'

if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $normalizedRoot = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar).ToUpperInvariant()
    $bytes = [Text.Encoding]::UTF8.GetBytes($normalizedRoot)
    $hashBytes = [Security.Cryptography.SHA256]::HashData($bytes)
    $hash = [Convert]::ToHexString($hashBytes).Substring(0, 12).ToLowerInvariant()
    $ArtifactsPath = Join-Path ([IO.Path]::GetTempPath()) "CardGame-TestChanged-$hash"
}
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
[IO.Directory]::CreateDirectory($ArtifactsPath) | Out-Null
$summaryPath = Join-Path $ArtifactsPath 'summary.json'
$logRoot = Join-Path $ArtifactsPath 'logs'
[IO.Directory]::CreateDirectory($logRoot) | Out-Null

$started = [DateTimeOffset]::Now
$steps = [Collections.Generic.List[object]]::new()
$status = 'config-error'
$message = $null
$exitCode = 1
$lock = $null
$ownsLock = $false

function Invoke-LoggedStep {
    param([string] $Name, [string] $LogName, [scriptblock] $Action)
    $stepStarted = [DateTimeOffset]::Now
    $logPath = Join-Path $logRoot $LogName
    Write-Host "==> $Name"
    $global:LASTEXITCODE = 0
    try {
        & $Action 2>&1 | Tee-Object -LiteralPath $logPath | Out-Host
        $code = if ($null -eq $global:LASTEXITCODE) { 0 } else { $global:LASTEXITCODE }
    }
    catch {
        $_ | Out-String | Tee-Object -LiteralPath $logPath -Append | Out-Host
        $code = 1
    }
    $ended = [DateTimeOffset]::Now
    $steps.Add([ordered]@{
        name = $Name; status = $(if ($code -eq 0) { 'passed' } else { 'failed' })
        exitCode = $code; durationMs = [int64]($ended - $stepStarted).TotalMilliseconds
        log = $logPath
    })
    return $code
}

function Find-TestAssembly {
    param([string] $AssemblyName)
    $projectName = [IO.Path]::GetFileNameWithoutExtension($AssemblyName)
    $projectOutput = Join-Path $ArtifactsPath "bin/$projectName/release"
    $assemblyPath = Join-Path $projectOutput $AssemblyName
    $runtimeName = [IO.Path]::ChangeExtension($AssemblyName, '.runtimeconfig.json')
    if (-not (Test-Path -LiteralPath $assemblyPath) -or
        -not (Test-Path -LiteralPath (Join-Path $projectOutput $runtimeName))) {
        throw "The selected project has no runnable Release output at '$assemblyPath'."
    }
    return $assemblyPath
}

function Invoke-TestAssembly {
    param([string] $Assembly, [string] $Scope, [string[]] $Filters, [switch] $RunFull)
    if ($RunFull) {
        return Invoke-LoggedStep "$Scope full checks" "$($Scope.ToLowerInvariant())-full.log" { & dotnet $Assembly }
    }
    $failed = 0
    for ($index = 0; $index -lt $Filters.Count; $index++) {
        $filter = $Filters[$index]
        $safeName = "$($Scope.ToLowerInvariant())-filter-$($index + 1).log"
        $code = Invoke-LoggedStep "$Scope filter: $filter" $safeName { & dotnet $Assembly "--filter=$filter" }
        if ($code -ne 0) { $failed = 1 }
    }
    return $failed
}

try {
    try {
        $lockPath = Join-Path $ArtifactsPath '.test-changed.lock'
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $ownsLock = $true
    }
    catch [IO.IOException] {
        $status = 'lock-busy'
        $message = "Another Test-Changed run owns '$ArtifactsPath'."
        throw
    }

    $CoreFilter = @($CoreFilter | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $WpfFilter = @($WpfFilter | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($Full -and ($CoreFilter.Count -gt 0 -or $WpfFilter.Count -gt 0)) {
        throw '-Full cannot be combined with -CoreFilter or -WpfFilter.'
    }
    if (-not $Full -and $CoreFilter.Count -eq 0 -and $WpfFilter.Count -eq 0) {
        throw 'Specify -Full or at least one explicit -CoreFilter/-WpfFilter.'
    }

    $usesWpf = $Full -or $WpfFilter.Count -gt 0
    $status = 'run-failed'
    $buildTarget = if ($usesWpf) { $solution } else { $coreProject }
    $buildCode = Invoke-LoggedStep 'Build selected scope' 'build.log' {
        & dotnet build $buildTarget -c Release --artifacts-path $ArtifactsPath
    }
    if ($buildCode -ne 0) {
        $status = 'build-failed'
        $message = 'The selected scope did not build; checks were not started.'
    }
    else {
        $testFailed = 0
        if ($Full -or $CoreFilter.Count -gt 0) {
            $coreAssembly = Find-TestAssembly 'CardGame.Core.Tests.dll'
            if ((Invoke-TestAssembly $coreAssembly 'Core' $CoreFilter -RunFull:$Full) -ne 0) { $testFailed = 1 }
        }
        if ($Full -or $WpfFilter.Count -gt 0) {
            $wpfAssembly = Find-TestAssembly 'CardGame.Wpf.Tests.dll'
            if ((Invoke-TestAssembly $wpfAssembly 'WPF' $WpfFilter -RunFull:$Full) -ne 0) { $testFailed = 1 }
        }
        if ($testFailed -eq 0) { $status = 'passed'; $exitCode = 0 }
        else { $status = 'test-failed'; $message = 'One or more selected checks failed.' }
    }
}
catch {
    if ($null -eq $message) { $message = $_.Exception.Message }
    Write-Host "ERROR: $message" -ForegroundColor Red
}
finally {
    $ended = [DateTimeOffset]::Now
    $summary = [ordered]@{
        status = $status
        mode = $(if ($Full) { 'full' } else { 'targeted' })
        repository = $repoRoot
        artifactsPath = $ArtifactsPath
        coreFilters = @($CoreFilter)
        wpfFilters = @($WpfFilter)
        startedAt = $started.ToString('O')
        durationMs = [int64]($ended - $started).TotalMilliseconds
        message = $message
        results = @($steps)
    }
    if ($ownsLock) {
        $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    }
    if ($null -ne $lock) { $lock.Dispose() }
    if ($ownsLock) { Write-Host "Summary: $summaryPath" }
    Write-Host "Status: $status"
}
exit $exitCode
