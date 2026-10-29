$ErrorActionPreference = 'Stop'
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
$DotnetExecutable = if ($sdk) { $sdk.Source } else { Join-Path $env:LOCALAPPDATA 'MellowMeter\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $DotnetExecutable)) {
    throw 'Install the .NET 8 SDK, or run .\scripts\setup.ps1, then try again.'
}
$env:DOTNET_ROOT = Split-Path $DotnetExecutable
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
