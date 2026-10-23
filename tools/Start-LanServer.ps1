#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int] $Port = 5088,
    [string] $ListenAddress = '0.0.0.0'
)
$ErrorActionPreference = 'Stop'
$listenIp = $null
if (-not [Net.IPAddress]::TryParse($ListenAddress, [ref]$listenIp)) { throw 'ListenAddress must be an IP address.' }
$serverProject = Join-Path $PSScriptRoot '../src/CardGame.Server/CardGame.Server.csproj'
$hostPart = if ($listenIp.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetworkV6) { "[$ListenAddress]" } else { $ListenAddress }
Write-Host "LAN server: http://${hostPart}:$Port"
Write-Host 'Clients should enter this computer''s LAN IP address. Ctrl+C stops the server.'
& dotnet run --project $serverProject --no-launch-profile -- --urls "http://${hostPart}:$Port"
exit $LASTEXITCODE
