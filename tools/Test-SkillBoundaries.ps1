#requires -Version 7.0
<#
.SYNOPSIS
Checks retired skill contracts and the shared Pindian boundary without building.
#>
[CmdletBinding()]
param(
    [string] $Repository = (Join-Path $PSScriptRoot '..'),
    [switch] $Details
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Repository)
$violations = [Collections.Generic.List[object]]::new()
$checks = @(
    @{ Name = 'retired passive contract'; Pattern = '\bIPassiveSkill\b'; Scope = 'all' },
    @{ Name = 'retired active contract'; Pattern = '\bIActiveSkill\b'; Scope = 'all' },
    @{ Name = 'retired active registry entry'; Pattern = '\bSkillRegistry\s*\.\s*GetActive\s*\('; Scope = 'all' },
    @{ Name = 'retired response alias'; Pattern = '\bRespondCommand\b'; Scope = 'all' },
    @{ Name = 'retired phase module chain'; Pattern = '\b(?:IPhaseSkillModule|PhaseSkillFrame|PhaseSkillModuleRegistry|IPindianResultModule|PindianCardClaimPlan)\b'; Scope = 'all' },
    @{ Name = 'retired engine mutation entry'; Pattern = 'public\s+(?:void|bool|CommandResult)\s+(?:Start|Advance|AdvanceOneStep|Human\w+)\s*\('; Scope = 'engine' },
    @{ Name = 'retired preferences import'; Pattern = '\bImportLegacyPreferences\s*\('; Scope = 'all' },
    @{ Name = 'historical engine constructor'; Pattern = '\bCreateForReplay\s*\(|\bValidateRulesVersion\s*\('; Scope = 'core' },
    @{ Name = 'historical engine version branch'; Pattern = '\b_rulesVersion\b|\brulesVersion\s*(?:[<>]=?|==|!=)\s*\d|\brulesVersion\s+is\s+[<>]'; Scope = 'engine' },
    @{ Name = 'character-specific turn state'; Pattern = '\bTianyi(?:Won|Lost)ThisTurn\b'; Scope = 'core' },
    @{ Name = 'character-specific Pindian execution'; Pattern = '(?i)\b(?:Tianyi|Quhu|Lieren)\b|(?:Begin|Complete|Resolve|Pending)(?:Tianyi|Quhu|Lieren)|\b(?:天义|驱虎|烈刃)\b'; Scope = 'pindian' }
)

foreach ($directory in @('src', 'tests')) {
    $path = Join-Path $root $directory
    foreach ($file in Get-ChildItem -LiteralPath $path -Filter '*.cs' -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        if ($relative -match '/(?:bin|obj|\.artifacts)/') { continue }
        $lineNumber = 0
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            $lineNumber++
            # Comments may explain what was retired; they do not bind a contract.
            if ($line.TrimStart().StartsWith('//')) { continue }
            foreach ($check in $checks) {
                if ($check.Scope -eq 'core' -and !$relative.StartsWith('src/CardGame.Core/')) { continue }
                if ($check.Scope -eq 'engine' -and $file.Name -notlike 'GameEngine*.cs') { continue }
                if ($check.Scope -eq 'pindian' -and $relative -ne 'src/CardGame.Core/GameEngine.Pindian.cs') { continue }
                if ($line -match $check.Pattern) {
                    $violations.Add([pscustomobject]@{
                        Boundary = $check.Name; File = $relative; Line = $lineNumber
                    })
                }
            }
        }
    }
}
if ($violations.Count -gt 0) {
    if ($Details) {
        $violations | Format-Table -AutoSize | Out-String | Write-Output
    } else {
        $violations | Group-Object Boundary | Select-Object Name, Count |
            Format-Table -AutoSize | Out-String | Write-Output
    }
    throw "$($violations.Count) retired skill boundary references remain."
}
Write-Output 'Skill boundary checks passed. This is a source boundary check, not gameplay validation.'
