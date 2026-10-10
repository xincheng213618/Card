param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
try {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
    $pointerPath = Join-Path $distRoot 'current-package.json'
    if (Test-Path -LiteralPath $pointerPath -PathType Leaf) {
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
    }
    else {
        $projectPath = Join-Path $repoRoot 'src\CardGame.Wpf\CardGame.Wpf.csproj'
        $executable = Join-Path $repoRoot 'src\CardGame.Wpf\bin\Debug\net8.0-windows\CardGame.Wpf.exe'
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw 'No local package or source project found.'
        }
        if (-not $VerifyOnly) {
            # A source checkout can run without publishing a distribution package.
            Push-Location -LiteralPath $repoRoot
            try {
                & dotnet build $projectPath -c Debug --nologo --verbosity quiet | Out-Host
                if ($LASTEXITCODE -ne 0) { throw 'The development build failed.' }
            }
            finally { Pop-Location }
        }
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw 'No development executable found. Run Play.cmd to build and start it.'
        }
    }
    if ($VerifyOnly) { Write-Output $executable; exit 0 }
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable)
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
