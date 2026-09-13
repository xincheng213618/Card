param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
try {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
    $pointerPath = Join-Path $distRoot 'current-package.json'
    if (-not (Test-Path -LiteralPath $pointerPath -PathType Leaf)) {
        throw 'No local package found. Run: powershell -File tools\Build-GamePackage.ps1'
    }
    $package = Get-Content -LiteralPath $pointerPath -Raw | ConvertFrom-Json
    if (-not $package.Executable -or [IO.Path]::IsPathRooted($package.Executable)) {
        throw 'Invalid package path. Rebuild the local package.'
    }
    $executable = [IO.Path]::GetFullPath((Join-Path $distRoot $package.Executable))
    if (-not $executable.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($executable) -ne 'CardGame.Wpf.exe' -or
        -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'The local package is missing or invalid. Rebuild with tools\Build-GamePackage.ps1.'
    }
    if ($VerifyOnly) { Write-Output $executable; exit 0 }
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable)
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
