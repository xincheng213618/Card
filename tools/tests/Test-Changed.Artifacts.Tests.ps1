#requires -Version 7.0
# Exercise selected scopes, real child overlap and queue registration with fake dotnet; no game build/run.
$ErrorActionPreference = 'Stop'
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Test-Changed.ps1'))
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sandbox = Join-Path $repo '.artifacts/benchmarks/test-changed-parallel'
$sandboxOwner = Join-Path $sandbox '.mock-owner.json'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts'))
$expectedSolution = Join-Path $repo 'CardGame.sln'
$expectedCoreProject = Join-Path $repo 'tests/CardGame.Core.Tests/CardGame.Core.Tests.csproj'
$expectedWpfProject = Join-Path $repo 'tests/CardGame.Wpf.Tests/CardGame.Wpf.Tests.csproj'
$testState = @{
    scenario = 'pass'; builds = 0; runs = 0; sequence = 0
    buildTargets = [Collections.Generic.List[string]]::new()
    runAssemblies = [Collections.Generic.List[string]]::new()
}
$passed = 0
for ($ancestor = [IO.DirectoryInfo]::new($sandbox); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Mock outputs cannot pass through linked directories.' }
}
if (Test-Path -LiteralPath $sandbox) {
    if (-not (Test-Path -LiteralPath $sandboxOwner)) { throw 'The mock output directory has no matching owner.' }
    $owner = Get-Content -LiteralPath $sandboxOwner -Raw | ConvertFrom-Json
    if ($owner.repository -ne $repo -or $owner.fixture -ne $PSCommandPath) { throw 'The mock output directory belongs to another task.' }
}
[IO.Directory]::CreateDirectory($sandbox) | Out-Null
[ordered]@{ repository = $repo; fixture = $PSCommandPath } | ConvertTo-Json | Set-Content -LiteralPath $sandboxOwner -Encoding utf8

function Assert([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$fakeDotnet = Join-Path $sandbox 'dotnet.ps1'
@'
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$arguments = $args
$state = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'state.json') -Raw | ConvertFrom-Json
if ($arguments[0] -eq 'build') {
        $target = [IO.Path]::GetFullPath($arguments[1])
        $projects = if ($target -eq $state.solution) {
            @('CardGame.Core.Tests', 'CardGame.Wpf.Tests')
        } elseif ($target -eq $state.coreProject) {
            @('CardGame.Core.Tests')
        } elseif ($target -eq $state.wpfProject) {
            @('CardGame.Wpf.Tests')
        } else {
            throw "Unexpected build target: '$target'."
        }
        $root = $arguments[[Array]::IndexOf($arguments, '--artifacts-path') + 1]
        [ordered]@{ target = $target } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "mock-$($state.token)-build.json")
        $compiled = Join-Path $root 'bin/CardGame.Core/release/CardGame.Core.dll'
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($compiled)) | Out-Null
        $compiledText = 'compiled-core' + $(if ($state.scenario -eq 'copy-corrupt') { 'x' * 4MB } else { '' })
        [IO.File]::WriteAllText($compiled, $compiledText)
        [IO.File]::SetLastWriteTimeUtc($compiled, [datetime]'2020-01-01')
        foreach ($name in $projects) {
            $bin = Join-Path $root "bin/$name/release"
            [IO.Directory]::CreateDirectory($bin) | Out-Null
            [IO.File]::WriteAllText((Join-Path $bin "$name.dll"), 'fixture')
            [IO.File]::WriteAllText((Join-Path $bin "$name.runtimeconfig.json"), '{}')
            $copy = Join-Path $bin 'CardGame.Core.dll'
            $corrupt = $state.scenario -like 'copy-*'
            [IO.File]::WriteAllText($copy, $(if ($corrupt) { 'C' + $compiledText.Substring(1) } else { $compiledText }))
            [IO.File]::SetLastWriteTimeUtc($copy, [datetime]'2020-01-01')
            [IO.File]::WriteAllText((Join-Path $bin 'ThirdParty.dll'), 'package-copy')
            $manifest = @{
                runtimeTarget = @{ name = 'fixture' }
                targets = @{ fixture = @{ 'CardGame.Core/1.0' = @{ runtime = @{ 'CardGame.Core.dll' = @{} } }; 'ThirdParty/1.0' = @{ runtime = @{ 'ThirdParty.dll' = @{} } } } }
                libraries = @{ 'CardGame.Core/1.0' = @{ type = 'project' }; 'ThirdParty/1.0' = @{ type = 'package' } }
            }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bin "$name.deps.json")
        }
        if ($state.scenario -eq 'copy-no-source') { [IO.File]::Move($compiled, $compiled + '.absent') }
        foreach ($name in @('obj', 'renders')) {
            [IO.Directory]::CreateDirectory((Join-Path $root $name)) | Out-Null
            [IO.File]::WriteAllText((Join-Path $root "$name/fixture.txt"), 'fixture')
        }
        'fixture build'
        exit $(if ($state.scenario -eq 'build-fail') { 23 } else { 0 })
}
$assembly = [IO.Path]::GetFullPath($arguments[0])
$output = [IO.Path]::GetDirectoryName($assembly)
if ([IO.File]::ReadAllText((Join-Path $output 'CardGame.Core.dll')) -ne ('compiled-core' + $(if ($state.scenario -eq 'copy-corrupt') { 'x' * 4MB } else { '' }))) { throw 'The test started with a corrupt project dependency.' }
if ([IO.File]::ReadAllText((Join-Path $output 'ThirdParty.dll')) -ne 'package-copy') { throw 'A NuGet dependency was changed.' }
$scope = if ($assembly.EndsWith('CardGame.Core.Tests.dll')) { 'core' } else { 'wpf' }
$root = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($assembly)) '../../..'))
$started = [DateTimeOffset]::UtcNow
$filters = @($arguments | Where-Object { $_ -like '--filter=*' })
if (($filters.Count -eq 0) -ne [bool]$state.runFull) { throw 'The wrapper changed the selected/full filter contract.' }
function Lock-Held {
    try {
        $lease = [IO.File]::Open((Join-Path $root '.test-changed.lock'), 'Open', 'ReadWrite', 'None')
        $lease.Dispose()
        return $false
    }
    catch [IO.IOException] { return $true }
}
$heldAtStart = Lock-Held
if ($state.bothScopes) {
    [IO.File]::WriteAllText((Join-Path $root "mock-$($state.token)-$scope-ready"), "$PID")
    $peer = if ($scope -eq 'core') { 'wpf' } else { 'core' }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath (Join-Path $root "mock-$($state.token)-$peer-ready"))) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'The two child runners did not overlap.' }
        Start-Sleep -Milliseconds 20
    }
    Start-Sleep -Milliseconds 120
}
"fixture $scope stdout 中文正在选择 → ✅"
if ($state.scenario -eq 'parent-fail') { Start-Sleep -Seconds 5 }
[Console]::Error.WriteLine("fixture $scope stderr 中文正在选择 → 🧪")
$code = if ($state.scenario -in @('test-fail', 'copy-test-fail')) { 37 }
    elseif ($state.scenario -eq 'both-fail' -or $state.scenario -eq "$scope-fail") { if ($scope -eq 'core') { 41 } else { 43 } }
    else { 0 }
[ordered]@{ assembly = $assembly; scope = $scope; pid = $PID; startedAt = $started.ToString('O'); endedAt = [DateTimeOffset]::UtcNow.ToString('O')
    lockHeldAtStart = $heldAtStart; lockHeldAtEnd = (Lock-Held); filters = $filters; arguments = $arguments; exitCode = $code } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root "mock-$($state.token)-$scope.json")
exit $code
'@ | Set-Content -LiteralPath $fakeDotnet -Encoding utf8
$previousDotnetAlias = Get-Alias dotnet -ErrorAction SilentlyContinue
Set-Alias -Name dotnet -Value $fakeDotnet -Scope Script
Assert ((Get-Command dotnet).ResolvedCommand.Source -eq $fakeDotnet) 'The fixture must never invoke the installed dotnet.'

function Invoke-Case([string] $Name, [hashtable] $Options = @{}) {
    $path = Join-Path $sandbox $Name
    $buildOffset = $testState.buildTargets.Count
    $runOffset = $testState.runAssemblies.Count
    $testState.sequence++
    $token = "$PID-$($testState.sequence)"
    $bothScopes = $Options.ContainsKey('Full') -or
        ($Options.ContainsKey('CoreFilter') -and $Options.ContainsKey('WpfFilter'))
    [ordered]@{ token = $token; scenario = $testState.scenario; bothScopes = $bothScopes; runFull = $Options.ContainsKey('Full')
        solution = $expectedSolution; coreProject = $expectedCoreProject; wpfProject = $expectedWpfProject } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sandbox 'state.json') -Encoding utf8
    & $entry -ArtifactsPath $path -CleanupDatabase (Join-Path $sandbox 'queue.json') @Options | Out-Null
    $code = $LASTEXITCODE
    $summary = if (Test-Path -LiteralPath (Join-Path $path 'summary.json')) {
        Get-Content -LiteralPath (Join-Path $path 'summary.json') -Raw | ConvertFrom-Json
    }
    $buildRecord = Join-Path $path "mock-$token-build.json"
    if (Test-Path -LiteralPath $buildRecord) {
        $testState.builds++
        $testState.buildTargets.Add((Get-Content -LiteralPath $buildRecord -Raw | ConvertFrom-Json).target)
    }
    $runRecords = @(foreach ($scope in @('core', 'wpf')) {
        $runRecord = Join-Path $path "mock-$token-$scope.json"
        if (Test-Path -LiteralPath $runRecord) {
            $record = Get-Content -LiteralPath $runRecord -Raw | ConvertFrom-Json
            $testState.runs++
            $testState.runAssemblies.Add($record.assembly)
            $record
        }
    })
    return @{
        path = $path; code = $code; summary = $summary
        buildTargets = @($testState.buildTargets | Select-Object -Skip $buildOffset)
        runAssemblies = @($testState.runAssemblies | Select-Object -Skip $runOffset)
        runRecords = $runRecords; token = $token
    }
}

function Assert-Scope($Result, [string] $ExpectedTarget, [string[]] $ExpectedProjects) {
    Assert ($Result.buildTargets.Count -eq 1 -and $Result.buildTargets[0] -eq $ExpectedTarget) `
        "The selected scope must build only '$ExpectedTarget'; actual: $($Result.buildTargets -join ', ')."
    $expectedAssemblies = @($ExpectedProjects | ForEach-Object {
        Join-Path $Result.path "bin/$_/release/$_.dll"
    })
    Assert (($Result.runAssemblies -join "`n") -eq ($expectedAssemblies -join "`n")) `
        "The selected scope ran unexpected assemblies: $($Result.runAssemblies -join ', ')."
}

function Assert-Queued($Result, [string] $ExpectedStatus) {
    Assert ($Result.summary.status -eq $ExpectedStatus) "Unexpected status: $($Result.summary.status)."
    Assert ($Result.summary.cleanup.status -eq 'queued') 'Registration was not recorded.'
    $queue = Get-Content -LiteralPath (Join-Path $sandbox 'queue.json') -Raw | ConvertFrom-Json
    foreach ($name in @('bin', 'obj', 'renders')) {
        $candidate = Join-Path $Result.path $name
        Assert (Test-Path -LiteralPath $candidate) "Output '$name' was deleted automatically."
        Assert (@($queue.entries | Where-Object path -eq $candidate).Count -eq 1) "Output '$name' must be registered exactly once."
    }
    Assert (Test-Path -LiteralPath (Join-Path $Result.path 'logs/build.log')) 'Build evidence must survive.'
}

function Assert-Parallel($Result) {
    Assert ($Result.runRecords.Count -eq 2) 'Both real child runners must finish.'
    Assert ($Result.runRecords[0].pid -ne $Result.runRecords[1].pid -and
        @($Result.runRecords | Where-Object pid -eq $PID).Count -eq 0) 'Each scope needs its own child process.'
    $latestStart = ($Result.runRecords.startedAt | ForEach-Object { [DateTimeOffset]$_ } | Sort-Object -Descending | Select-Object -First 1)
    $earliestEnd = ($Result.runRecords.endedAt | ForEach-Object { [DateTimeOffset]$_ } | Sort-Object | Select-Object -First 1)
    Assert ($latestStart -lt $earliestEnd) "The native child execution intervals must actually overlap: latestStart=$($latestStart.ToString('O')), earliestEnd=$($earliestEnd.ToString('O'))."
    foreach ($record in $Result.runRecords) {
        Assert ($record.lockHeldAtStart -and $record.lockHeldAtEnd) 'The artifact lease must stay held through both child lifetimes.'
        Assert ($null -eq (Get-Process -Id $record.pid -ErrorAction SilentlyContinue)) 'A child outlived summary/cleanup registration.'
        $step = $Result.summary.results | Where-Object name -like "$($record.scope) *"
        Assert ($step.processId -eq $record.pid -and $step.exitCode -eq $record.exitCode) 'Summary lost the exact child PID or exit code.'
        $log = Get-Content -LiteralPath $step.log -Raw
        Assert ($log.Contains("fixture $($record.scope) stdout 中文正在选择 → ✅") -and
            $log.Contains("fixture $($record.scope) stderr 中文正在选择 → 🧪")) 'Both UTF-8 streams must preserve complete Chinese, arrow and emoji text in their own scope log.'
        Assert (-not $log.Contains([char]0xFFFD)) 'UTF-8 native output acquired replacement characters.'
        $peer = if ($record.scope -eq 'core') { 'wpf' } else { 'core' }
        Assert (-not $log.Contains("fixture $peer stdout")) 'Scope logs mixed child output.'
    }
    $lease = [IO.File]::Open((Join-Path $Result.path '.test-changed.lock'), 'Open', 'ReadWrite', 'None')
    $lease.Dispose()
    Write-Host "Mock child overlap: $([IO.Path]::GetFileName($Result.path)), two PIDs, both streams, lease held until both completed."
}

try {
    $result = Invoke-Case 'routine'
    Assert-Queued $result 'passed'
    Assert ($result.code -eq 0 -and $result.summary.mode -eq 'routine' -and $testState.runs -eq 1) 'Routine must run the filtered Core smoke scope only.'
    Assert-Scope $result $expectedCoreProject @('CardGame.Core.Tests')
    $routineScope = Import-PowerShellDataFile -LiteralPath (Join-Path $repo 'tools/VerificationScopes.psd1')
    Assert (($result.summary.coreFilters -join "`n") -eq ($routineScope.Core -join "`n") -and
        $result.summary.wpfFilters.Count -eq 0) 'Routine must preserve the configured smoke filters without starting WPF.'
    $passed++

    foreach ($scenario in @('copy-corrupt', 'copy-test-fail', 'copy-no-source')) {
        $testState.scenario = $scenario
        $result = Invoke-Case $scenario @{ CoreFilter = @('probe'); WpfFilter = @('probe') }
        if ($scenario -eq 'copy-no-source') {
            Assert-Queued $result 'run-failed'
            Assert ($result.code -ne 0 -and $result.runRecords.Count -eq 0) 'Missing compiled output must stop before any tests; never use an unverified copy.'
        }
        else {
            Assert-Queued $result $(if ($scenario -eq 'copy-corrupt') { 'passed' } else { 'test-failed' })
            Assert ($result.summary.dependencyRepairs.Count -eq 2) 'Both same-size, same-mtime project copies must be repaired before testing.'
            if ($scenario -eq 'copy-corrupt') { Write-Host "Two 4 MiB DLL copies verified and repaired in $($result.summary.dependencyVerificationMs) ms." }
            Assert-Parallel $result
            foreach ($repair in $result.summary.dependencyRepairs) {
                Assert ($repair.previousSha256 -ne $repair.sha256 -and (Get-FileHash -LiteralPath $repair.source).Hash -eq $repair.sha256 -and (Get-FileHash -LiteralPath $repair.copy).Hash -eq $repair.sha256) 'Repair evidence must match the unchanged compiled source and repaired copy.'
            }
            if ($scenario -eq 'copy-test-fail') { Assert (@($result.runRecords | Where-Object exitCode -ne 37).Count -eq 0) 'Repair must retain genuine test failures and exact exit codes.' }
        }
        $passed++
    }
    $testState.scenario = 'pass'

    $result = Invoke-Case 'only-wpf' @{ WpfFilter = @('probe') }
    Assert-Queued $result 'passed'
    Assert ($result.code -eq 0) 'The WPF-only scope must pass.'
    Assert-Scope $result $expectedWpfProject @('CardGame.Wpf.Tests')
    $passed++

    $result = Invoke-Case 'only-core' @{ CoreFilter = @('probe') }
    Assert-Queued $result 'passed'
    Assert ($result.code -eq 0) 'The Core-only scope must pass.'
    Assert ($result.summary.dependencyRepairs.Count -eq 0 -and [IO.File]::GetLastWriteTimeUtc((Join-Path $result.path 'bin/CardGame.Core.Tests/release/CardGame.Core.dll')) -eq [datetime]'2020-01-01') 'An identical dependency must not be rewritten.'
    Assert-Scope $result $expectedCoreProject @('CardGame.Core.Tests')
    $passed++

    $result = Invoke-Case 'mixed' @{ CoreFilter = @('probe'); WpfFilter = @('probe') }
    Assert-Queued $result 'passed'
    Assert ($result.code -eq 0) 'The mixed scope must pass.'
    Assert-Scope $result $expectedSolution @('CardGame.Core.Tests', 'CardGame.Wpf.Tests')
    Assert-Parallel $result
    $passed++

    foreach ($scenario in @('core-fail', 'wpf-fail', 'both-fail')) {
        $testState.scenario = $scenario
        $result = Invoke-Case $scenario @{ CoreFilter = @('probe'); WpfFilter = @('probe') }
        Assert-Queued $result 'test-failed'
        Assert ($result.code -ne 0 -and $result.summary.results.Count -eq 3) 'Child failure must retain both scope results after one build.'
        Assert-Parallel $result
        foreach ($record in $result.runRecords) {
            $shouldFail = $scenario -eq 'both-fail' -or $scenario -eq "$($record.scope)-fail"
            Assert (($record.exitCode -ne 0) -eq $shouldFail) 'One child failure changed the other child result.'
        }
        $passed++
    }
    $testState.scenario = 'pass'
    $result = Invoke-Case 'mock-full' @{ Full = $true }
    Assert-Queued $result 'passed'
    Assert ($result.code -eq 0 -and $result.summary.mode -eq 'full' -and $result.summary.coreFilters.Count -eq 0 -and $result.summary.wpfFilters.Count -eq 0) 'Explicit Full must retain its original unfiltered contract in the fake runners only.'
    Assert-Scope $result $expectedSolution @('CardGame.Core.Tests', 'CardGame.Wpf.Tests')
    Assert-Parallel $result
    $passed++

    $testState.scenario = 'parent-fail'
    function Write-Host {
        [CmdletBinding(PositionalBinding = $false)]
        param([Parameter(Position = 0, ValueFromRemainingArguments)] [object[]] $Object, [ConsoleColor] $ForegroundColor)
        if ($Object[0].StartsWith('fixture core stdout')) { throw 'Injected parent output failure with two live children.' }
        Microsoft.PowerShell.Utility\Write-Host @PSBoundParameters
    }
    try { $result = Invoke-Case 'parent-fail' @{ CoreFilter = @('probe'); WpfFilter = @('probe') } }
    finally { Remove-Item -LiteralPath Function:Write-Host }
    Assert-Queued $result 'run-failed'
    Assert ($result.code -ne 0 -and $result.summary.message.Contains('Injected parent output failure')) 'The parent infrastructure failure must remain a failure.'
    foreach ($scope in @('core', 'wpf')) {
        $childPid = [int](Get-Content -LiteralPath (Join-Path $result.path "mock-$($result.token)-$scope-ready") -Raw)
        Assert ($null -eq (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) 'Parent failure released the artifact lease with a live child.'
    }
    $lease = [IO.File]::Open((Join-Path $result.path '.test-changed.lock'), 'Open', 'ReadWrite', 'None')
    $lease.Dispose()
    $passed++
    $testState.scenario = 'pass'
    if ($IsWindows) {
        $result = Invoke-Case 'partial-start' @{ CoreFilter = @('probe'); WpfFilter = @('x' * 40000) }
        Assert-Queued $result 'test-failed'
        $coreStep = $result.summary.results | Where-Object name -like 'Core *'
        $wpfStep = $result.summary.results | Where-Object name -like 'WPF *'
        Assert ($result.code -ne 0 -and $result.summary.results.Count -eq 3 -and $null -ne $coreStep.processId -and
            $null -eq $wpfStep.processId -and $wpfStep.exitCode -eq 1) 'A partial native start failure must retain both scope failures after one build.'
        Assert ($null -eq (Get-Process -Id $coreStep.processId -ErrorAction SilentlyContinue)) 'The successfully started peer outlived partial launch failure.'
        Assert ((Get-Content -LiteralPath $wpfStep.log -Raw).Length -gt 0) 'Native launch failure diagnostics were lost.'
        $passed++
    }

    foreach ($scenario in @('build-fail', 'test-fail')) {
        $testState.scenario = $scenario
        $result = Invoke-Case $scenario @{ CoreFilter = @('probe') }
        Assert-Queued $result ($scenario + 'ed')
        Assert ($result.code -ne 0) 'A failed verification must remain failed after cleanup.'
        $passed++
    }
    $testState.scenario = 'pass'
    $result = Invoke-Case 'keep' @{ CoreFilter = @('probe'); KeepArtifacts = $true }
    Assert ($result.code -eq 0 -and $result.summary.cleanup.status -eq 'queued') 'The compatibility switch must retain and register outputs.'
    Assert (Test-Path -LiteralPath (Join-Path $result.path 'bin')) 'Kept binaries are missing.'
    $result = Invoke-Case 'keep' @{ CoreFilter = @('probe') }
    Assert-Queued $result 'passed'
    $passed++

    $foreign = Join-Path $sandbox 'foreign'
    [IO.Directory]::CreateDirectory($foreign) | Out-Null
    [IO.File]::WriteAllText((Join-Path $foreign 'user.txt'), 'do not change')
    $before = $testState.builds
    $result = Invoke-Case 'foreign' @{ CoreFilter = @('probe') }
    Assert ($result.code -ne 0 -and $testState.builds -eq $before) 'Unowned directories must be refused before building.'
    Assert (@(Get-ChildItem -LiteralPath $foreign -Force).Count -eq 1) 'A foreign directory was modified.'
    $passed++

    $result = Invoke-Case 'locked' @{ CoreFilter = @('probe'); KeepArtifacts = $true }
    $oldSummary = Get-Content -LiteralPath (Join-Path $result.path 'summary.json') -Raw
    $lease = [IO.File]::Open((Join-Path $result.path '.test-changed.lock'), 'Open', 'ReadWrite', 'None')
    try {
        $before = $testState.builds
        $result = Invoke-Case 'locked' @{ CoreFilter = @('probe') }
        Assert ($result.code -ne 0 -and $testState.builds -eq $before) 'A busy output must not build.'
        Assert ((Get-Content -LiteralPath (Join-Path $result.path 'summary.json') -Raw) -eq $oldSummary) 'Busy owner evidence was overwritten.'
        Assert (Test-Path -LiteralPath (Join-Path $result.path 'bin')) 'Busy owner outputs were removed.'
    }
    finally { $lease.Dispose() }
    $result = Invoke-Case 'locked' @{ CoreFilter = @('probe') }
    Assert-Queued $result 'passed'
    $passed++

    $result = Invoke-Case 'invalid' @{ CoreFilter = @('probe'); KeepArtifacts = $true }
    $before = $testState.builds
    $result = Invoke-Case 'invalid' @{ CoreFilter = @('') }
    Assert ($result.code -ne 0 -and $testState.builds -eq $before -and $result.summary.cleanup.status -eq 'not-needed') 'Invalid filters must not trigger build or cleanup.'
    Assert (Test-Path -LiteralPath (Join-Path $result.path 'bin')) 'Invalid filters removed prior outputs.'
    $result = Invoke-Case 'invalid' @{ CoreFilter = @('probe') }
    Assert-Queued $result 'passed'
    $passed++

    $before = $testState.builds
    & $entry -ArtifactsPath ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))) -CoreFilter 'probe' | Out-Null
    Assert ($LASTEXITCODE -ne 0 -and $testState.builds -eq $before) 'A source root must be refused before writing.'
    $passed++

    if ($IsWindows) {
        $result = Invoke-Case 'busy-file' @{ CoreFilter = @('probe'); KeepArtifacts = $true }
        $busyPath = Join-Path $result.path 'bin/in-use.txt'
        $busy = [IO.File]::Open($busyPath, 'Create', 'ReadWrite', 'None')
        try {
            $result = Invoke-Case 'busy-file' @{ CoreFilter = @('probe') }
            Assert ($result.code -eq 0 -and $result.summary.cleanup.status -eq 'queued') 'Registration must work without attempting to delete an occupied output.'
        }
        finally { $busy.Dispose() }
        $result = Invoke-Case 'busy-file' @{ CoreFilter = @('probe') }
        Assert-Queued $result 'passed'
        $passed++
    }
    $brokenDatabase = Join-Path $sandbox 'broken-queue.json'
    [IO.File]::WriteAllText($brokenDatabase, 'invalid JSON')
    $failurePath = Join-Path $sandbox 'registration-failure'
    & $entry -ArtifactsPath $failurePath -CleanupDatabase $brokenDatabase -CoreFilter 'probe' | Out-Null
    $failureCode = $LASTEXITCODE
    $failureSummary = Get-Content -LiteralPath (Join-Path $failurePath 'summary.json') -Raw | ConvertFrom-Json
    Assert ($failureCode -ne 0 -and $failureSummary.status -eq 'cleanup-registration-failed') 'Failed registration must return a failure even when checks pass.'
    Assert (Test-Path -LiteralPath (Join-Path $failurePath 'bin')) 'Registration failure deleted outputs.'
    Assert ((Get-Content -LiteralPath $brokenDatabase -Raw) -eq 'invalid JSON') 'Invalid queue contents were overwritten.'
    $passed++
    Write-Host "$passed artifact registration and parallel runner checks passed. No game binaries were built or game checks run."
}
finally {
    if ($null -eq $previousDotnetAlias) { Remove-Item -LiteralPath Alias:dotnet -ErrorAction SilentlyContinue }
    else { Set-Alias -Name dotnet -Value $previousDotnetAlias.Definition -Scope Script }
    if (Test-Path -LiteralPath $sandbox) {
        $resolved = (Resolve-Path -LiteralPath $sandbox).Path
        $owner = Get-Content -LiteralPath $sandboxOwner -Raw | ConvertFrom-Json
        Assert ($resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            $resolved -eq [IO.Path]::GetFullPath($sandbox) -and $owner.repository -eq $repo -and $owner.fixture -eq $PSCommandPath) 'Cleanup target escaped its dedicated owned output.'
        for ($ancestor = [IO.DirectoryInfo]::new($resolved); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
            Assert (-not ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Cleanup must reject linked output paths.'
        }
        try { Remove-Item -LiteralPath $resolved -Recurse -Force }
        catch {
            & python -B (Join-Path $repo 'tools/cleanup_temp.py') --add $resolved --root $artifactRoot --reason "Parallel runner mock cleanup retained: $($_.Exception.Message)"
            if ($LASTEXITCODE -ne 0) { throw 'Could not register retained mock fixtures.' }
            throw
        }
        Assert (-not (Test-Path -LiteralPath $resolved)) 'Task-owned mock outputs remain after cleanup.'
        Write-Host 'Task-owned parallel mock outputs removed.'
    }
}
exit 0
