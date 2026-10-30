param([switch]$Test)
. "$PSScriptRoot\dotnet-path.ps1"
Push-Location (Split-Path $PSScriptRoot)
try {
    & $DotnetExecutable build MellowMeter.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Test) {
        foreach ($project in @('test/Monitoring/Monitoring.csproj', 'test/DesktopSmoke/DesktopSmoke.csproj', 'test/发版审查/发版审查.csproj', 'test/安全修复回归/安全修复回归.csproj')) {
            & $DotnetExecutable run --project $project -c Release
            if ($LASTEXITCODE -ne 0) { throw "Regression failed: $project" }
        }
    }
} finally { Pop-Location }
