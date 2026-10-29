$ErrorActionPreference = 'Stop'
$sdkDirectory = Join-Path $env:LOCALAPPDATA 'MellowMeter\dotnet'
New-Item -ItemType Directory -Force -Path $sdkDirectory | Out-Null
$installer = Join-Path $sdkDirectory 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Channel 8.0 -InstallDir $sdkDirectory -NoPath
if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
& (Join-Path $sdkDirectory 'dotnet.exe') --version
