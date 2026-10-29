#requires -Version 7.4
[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/release')

. "$PSScriptRoot/dotnet-path.ps1"
$repoRoot = Split-Path $PSScriptRoot
$projectPath = Join-Path $repoRoot 'MellowMeter.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[\w.-]+)?$') { throw 'Project version is not suitable for a release filename.' }
$output = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
if (Test-Path -LiteralPath $output) {
    throw "Output already exists: $output. Choose a new -OutputDirectory; existing files are never removed."
}
$bundleName = "MellowMeter-$version-win-x64"
$bundle = Join-Path $output $bundleName
[IO.Directory]::CreateDirectory($bundle) | Out-Null

Push-Location $repoRoot
try {
    & $DotnetExecutable publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $bundle --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/PORTABLE.txt') -Destination (Join-Path $bundle 'START-HERE.txt')
    $licenseDirectory = Join-Path $bundle 'docs/licenses'
    [IO.Directory]::CreateDirectory($licenseDirectory) | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs/licenses') -File | Copy-Item -Destination $licenseDirectory

    # Resolve legal files from the actual runtime packs used by this publish.
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $bundle 'MellowMeter.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'obj/project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageRoots = $assets.packageFolders.PSObject.Properties.Name
    $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    if ($frameworks.Count -ne 2) { throw 'Expected bundled .NET and Windows Desktop runtimes.' }
    foreach ($framework in $frameworks) {
        $packRelativePath = "$($framework.name.ToLowerInvariant()).runtime.win-x64/$($framework.version)"
        $pack = $packageRoots | ForEach-Object { Join-Path $_ $packRelativePath } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (-not $pack) { throw "Runtime package was not found: $packRelativePath" }
        $license = Get-ChildItem -LiteralPath $pack -File | Where-Object Name -Match '^LICENSE(?:\.TXT)?$' | Select-Object -First 1
        if (-not $license) { throw "Runtime license was not found: $packRelativePath" }
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenseDirectory "$($framework.name)-LICENSE.txt")
        $notice = Join-Path $pack 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $notice) {
            Copy-Item -LiteralPath $notice -Destination (Join-Path $licenseDirectory "$($framework.name)-THIRD-PARTY-NOTICES.txt")
        }
    }

    foreach ($required in @('MellowMeter.exe', 'MellowMeter.dll', 'coreclr.dll', 'hostfxr.dll', 'System.Windows.Forms.dll',
        'LICENSE', 'THIRD_PARTY_NOTICES.md', 'START-HERE.txt', 'docs/licenses/HidSharp.txt', 'docs/licenses/MPL-2.0.txt',
        'docs/licenses/Mono.txt', 'docs/licenses/Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt',
        'resources/themes/DarkFlat_Classic.json', 'resources/lang/en.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $bundle $required) -PathType Leaf)) { throw "Release file is missing: $required" }
    }
    $unexpected = Get-ChildItem -LiteralPath $bundle -Recurse -File |
        Where-Object { $_.Extension -in @('.pdb', '.pfx', '.p12') -or $_.Name -match '^(settings\.json|HardwareHistory\.json|TrafficHistory\.json|\.env)|Updater|PawnIO|WinRing|PresentMon' }
    if ($unexpected) { throw "Unexpected release files: $($unexpected.Name -join ', ')" }

    $archive = Join-Path $output "$bundleName.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($bundle, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha256  $bundleName.zip" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8NoBOM
    Write-Output "Portable archive: $archive"
    Write-Output "SHA256: $sha256"
} finally { Pop-Location }
