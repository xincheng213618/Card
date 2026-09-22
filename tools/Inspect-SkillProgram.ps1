#requires -Version 7.0
<#
.EXAMPLE
  pwsh tools/Inspect-SkillProgram.ps1
.EXAMPLE
  pwsh tools/Inspect-SkillProgram.ps1 -RulesPath .\skill.rules.json -PresentationPath .\skill.presentation.json
.EXAMPLE
  pwsh tools/Inspect-SkillProgram.ps1 -Operation draw
#>
[CmdletBinding()]
param(
    [string]$RulesPath,
    [string]$PresentationPath,
    [string]$Operation,
    [string]$ArtifactsPath,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$hasRules = -not [string]::IsNullOrWhiteSpace($RulesPath)
$hasPresentation = -not [string]::IsNullOrWhiteSpace($PresentationPath)
$hasOperation = -not [string]::IsNullOrWhiteSpace($Operation)
if ($hasOperation -and ($hasRules -or $hasPresentation)) {
    Write-Error '-Operation cannot be combined with -RulesPath or -PresentationPath.' -ErrorAction Continue
    exit 2
}
if ($hasRules -ne $hasPresentation) {
    Write-Error '-RulesPath and -PresentationPath must be supplied together.' -ErrorAction Continue
    exit 2
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'tests\CardGame.Core.Tests\CardGame.Core.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($repoRoot.ToLowerInvariant())
    $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 12).ToLowerInvariant()
    $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) "CardProgramInspect-$hash"
}
$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)

if (-not $NoBuild) {
    & dotnet build $project -c Release --nologo -v:minimal --artifacts-path $ArtifactsPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$testDll = Join-Path $ArtifactsPath 'bin\CardGame.Core.Tests\release\CardGame.Core.Tests.dll'
if (-not (Test-Path -LiteralPath $testDll -PathType Leaf)) {
    Write-Error "CardGame.Core.Tests.dll was not found under '$ArtifactsPath'. Run without -NoBuild first." -ErrorAction Continue
    exit 3
}

[string[]]$commandArgs = if ($hasRules) {
    @('--validate-skill-program', [System.IO.Path]::GetFullPath($RulesPath), [System.IO.Path]::GetFullPath($PresentationPath))
} elseif ($hasOperation) {
    @('--program-operation', $Operation)
} else {
    @('--program-capabilities')
}
& dotnet $testDll @commandArgs
exit $LASTEXITCODE
