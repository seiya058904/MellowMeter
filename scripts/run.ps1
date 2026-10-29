param([switch]$Settings, [switch]$NoBuild)
. "$PSScriptRoot\dotnet-path.ps1"
$projectDirectory = Split-Path $PSScriptRoot
if (-not $NoBuild) {
    & "$PSScriptRoot\build.ps1"
}
$application = Join-Path $projectDirectory 'bin\Release\net8.0-windows\MellowMeter.exe'
if (-not (Test-Path -LiteralPath $application)) { throw 'Build the application first.' }
$options = @{ FilePath = $application; WorkingDirectory = Split-Path $application; WindowStyle = 'Normal' }
if ($Settings) { $options.ArgumentList = '--settings' }
Start-Process @options
