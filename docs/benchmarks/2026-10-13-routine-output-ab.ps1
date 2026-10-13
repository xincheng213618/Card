#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('before', 'after')][string] $Variant)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRepo = 'C:\Users\17917\Desktop\Card'
$taskRoot = 'C:\Users\17917\AppData\Local\Temp\CardGame-Startup-01a13324'
$taskEvidence = Join-Path $taskRepo 'docs\benchmarks'
$taskPrefix = Join-Path $taskEvidence '2026-10-13-routine-output-'
$taskAssembly = Join-Path $taskRoot 'bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.dll'
$taskChanged = @('StableHandResponseFixture.cs', 'NativeWeaponFeedbackChecks.cs', 'Program.cs') |
    ForEach-Object { Join-Path $taskRepo "tests\CardGame.Wpf.Tests\$_" }
$taskFilters = @(
    'skill target selection adds portrait outlines and confirms a real Zhefu choice',
    'response context keeps the current responder and native chain stage visible',
    'card flights follow a continuous native hand lift',
    'battle feedback readability protects the measured phase HUD',
    'battle feedback shows native weapon activations and exact followup costs'
)
function Manifest([string[]] $Paths) {
    $result = [ordered]@{}
    foreach ($path in ($Paths | Sort-Object -Unique)) { $result[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    return $result
}
function Dependencies {
    $paths = foreach ($project in @('CardGame.Core', 'CardGame.Content.Standard', 'CardGame.Wpf')) {
        Join-Path $taskRoot "obj\$project\release\ref\$project.dll"
        Join-Path $taskRoot "bin\$project\release\$project.dll"
        Join-Path $taskRoot "bin\CardGame.Wpf.Tests\release\$project.dll"
    }
    return Manifest @($paths)
}
function SourceControls {
    $paths = @(& rg --files --hidden --no-ignore (Join-Path $taskRepo 'tests\CardGame.Wpf.Tests') (Join-Path $taskRepo 'tests\Shared') -g '*.cs' -g '*.csproj' -g '*.json' -g '!**/bin/**' -g '!**/obj/**')
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate test sources.' }
    $paths += Join-Path $taskRepo 'global.json'
    return Manifest @($paths | ForEach-Object { [IO.Path]::GetFullPath($_) } | Where-Object { $_ -notin $taskChanged })
}
function Same($Expected, $Actual, [string] $Label) {
    if ($Expected.Count -ne $Actual.Count) { throw "$Label input set changed." }
    foreach ($path in $Expected.Keys) {
        if (-not $Actual.Contains($path) -or $Expected[$path] -cne $Actual[$path]) { throw "$Label changed: $path" }
    }
}
Set-Location -LiteralPath $taskRepo
$owner = Get-Content -LiteralPath (Join-Path $taskRoot '.test-changed-owner.json') -Raw | ConvertFrom-Json
if ($owner.repository -ne $taskRepo -or $owner.artifactsPath -ne $taskRoot) { throw 'Output ownership mismatch.' }
$lock = $null
$steps = [Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::Now
$status = 'failed'
try {
    $lock = [IO.File]::Open((Join-Path $taskRoot '.test-changed.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $deps = Dependencies; $controls = SourceControls; $changed = Manifest $taskChanged
    if ($Variant -eq 'before') {
        if (Test-Path -LiteralPath ($taskPrefix + 'seal.json')) { throw 'The controlled baseline is already sealed; refusing replacement.' }
        [ordered]@{ Dependencies = $deps; SourceControls = $controls; BeforeSources = $changed; Filters = $taskFilters } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($taskPrefix + 'seal.json') -Encoding utf8
    } else {
        $seal = Get-Content -LiteralPath ($taskPrefix + 'seal.json') -Raw | ConvertFrom-Json -AsHashtable
        Same $seal.Dependencies $deps 'Frozen production dependency'
        Same $seal.SourceControls $controls 'Unrelated test source'
    }
    foreach ($path in $taskChanged) { Copy-Item -LiteralPath $path -Destination ($taskPrefix + $Variant + '-' + [IO.Path]::GetFileName($path) + '.txt') }
    $began = [DateTimeOffset]::Now
    & dotnet build (Join-Path $taskRepo 'tests\CardGame.Wpf.Tests\CardGame.Wpf.Tests.csproj') -c Release --no-dependencies --no-restore --artifacts-path $taskRoot 2>&1 |
        Tee-Object -LiteralPath ($taskPrefix + $Variant + '-build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Controlled test-only build failed.' }
    $steps.Add(@{Name='Test-only build';DurationMs=([DateTimeOffset]::Now-$began).TotalMilliseconds})
    Same $deps (Dependencies) 'Frozen production dependency'
    Same $controls (SourceControls) 'Unrelated test source'
    Same $changed (Manifest $taskChanged) 'Reviewed variant source'
    $assemblyHash = (Get-FileHash -LiteralPath $taskAssembly -Algorithm SHA256).Hash
    foreach ($sample in 1..2) {
        $output = Join-Path $taskRoot "renders\routine-output-$Variant$sample"
        if (Test-Path -LiteralPath $output) { throw "Sample output already exists: $output" }
        [IO.Directory]::CreateDirectory($output) | Out-Null
        [ordered]@{Thread='01a13324-1a1e-7821-956c-1648665cc309';Root=$taskRoot;Variant=$Variant;Sample=$sample;AbsentBeforeRun=$true} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output '.owner.json') -Encoding utf8
        $began = [DateTimeOffset]::Now
        & dotnet $taskAssembly $output --verbose @($taskFilters | ForEach-Object { "--filter=$_" }) 2>&1 |
            Tee-Object -LiteralPath ($taskPrefix + "$Variant$sample-wpf.log") | Out-Host
        $code = $LASTEXITCODE
        $steps.Add(@{Name="$Variant$sample";DurationMs=([DateTimeOffset]::Now-$began).TotalMilliseconds;ExitCode=$code;Output=$output})
        if ($code -ne 0) { throw "Controlled sample $Variant$sample failed." }
        Same $deps (Dependencies) 'Frozen production dependency'
        Same $controls (SourceControls) 'Unrelated test source'
        Same $changed (Manifest $taskChanged) 'Reviewed variant source'
        if ((Get-FileHash -LiteralPath $taskAssembly -Algorithm SHA256).Hash -cne $assemblyHash) { throw 'Test assembly changed between samples.' }
    }
    $status = 'passed'
} finally {
    if ($null -ne $lock) { $lock.Dispose() }
    [ordered]@{Variant=$Variant;Status=$status;StartedAt=$started.ToString('o');Steps=$steps;Dependencies=$deps;SourceControls=$controls;ReviewedSources=$changed;AssemblyHash=$assemblyHash;
        Scope='Five existing name-filtered WPF checks, two independent processes per variant, no full runner';
        Limits='BB then AA order, small samples; test-only changes, frozen compiled production dependencies, no physical foreground acceptance'} |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath ($taskPrefix + $Variant + '-summary.json') -Encoding utf8
}
