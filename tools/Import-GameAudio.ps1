param(
    [string]$CacheDir = (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\Default\Cache\Cache_Data'),
    [switch]$RefreshMapping,
    [switch]$RetryFailures
)

$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'import_game_audio.py'), '--sync-cache', '--cache-dir', $CacheDir)
if ($RefreshMapping) { $arguments += '--refresh-mapping' }
if ($RetryFailures) { $arguments += '--retry-failures' }
& python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
