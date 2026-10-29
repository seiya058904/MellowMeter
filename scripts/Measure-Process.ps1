#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(0, 600)][int]$WarmupSeconds = 15,
    [ValidateRange(1, 3600)][int]$SampleSeconds = 60,
    [string]$DataDirectory,
    [string]$StartupTrace
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExePath).ProviderPath
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'ExePath must be a local executable file.' }
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFile) { throw "Output already exists: $outputFile" }
$traceFile = if ($StartupTrace) { [IO.Path]::GetFullPath($StartupTrace) } else { $null }
if ($traceFile -and (Test-Path -LiteralPath $traceFile)) { throw "Startup trace already exists: $traceFile" }

$childEnvironment = @{ MELLOWMETER_DATA_DIR = $null; MELLOWMETER_STARTUP_TRACE = $null }
if ($DataDirectory) {
    $childEnvironment.MELLOWMETER_DATA_DIR = [IO.Path]::GetFullPath($DataDirectory)
}
if ($traceFile) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($traceFile)) | Out-Null
    $childEnvironment.MELLOWMETER_STARTUP_TRACE = $traceFile
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$logicalProcessors = [Environment]::ProcessorCount
$processorCountSource = 'Environment.ProcessorCount'
try {
    $machineCount = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    if ($machineCount -gt 0) {
        $logicalProcessors = [int]$machineCount
        $processorCountSource = 'Win32_ComputerSystem.NumberOfLogicalProcessors'
    }
} catch { }

$samples = [Collections.Generic.List[object]]::new()
$startedProcess = $null
$startupMilliseconds = $null
$startupState = if ($traceFile) { 'waiting_for_trace' } else { 'not_instrumented' }
$status = 'starting'
$failure = $null
$measuredSeconds = 0.0
$cpuMilliseconds = 0.0
$launchClock = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{
    schemaVersion = 1
    startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    executable = $executable
    executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    processId = $null
    dataDirectory = $childEnvironment.MELLOWMETER_DATA_DIR
    startupTrace = $traceFile
    dotnetRoot = $env:DOTNET_ROOT
    warmupSeconds = $WarmupSeconds
    requestedSampleSeconds = $SampleSeconds
    logicalProcessors = $logicalProcessors
    processorCountSource = $processorCountSource
    methodology = 'Visible normal-window launch. Startup is wall time from launch until the first main ready trace marker (100 ms polling); null without a trace. Without instrumentation, input-idle is awaited for up to 30 seconds but is not reported as startup time. Warmup begins after readiness. CPU is own-process CPU-time delta / actual wall-time delta / total logical processors * 100. Memory and handles are sampled approximately once per second. No GC, working-set trimming, child-process aggregation, or UI automation. The launched process alone is closed after recording.'
}

try {
    $launchClock.Restart()
    $startOptions = @{
        FilePath = $executable
        WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
        WindowStyle = 'Normal' # Intentional: comparable runs need the visible application UI.
        PassThru = $true
    }
    if ($childEnvironment.Count -gt 0) { $startOptions.Environment = $childEnvironment }
    $startedProcess = Start-Process @startOptions
    $result.processId = $startedProcess.Id

    if ($traceFile) {
        while ($launchClock.Elapsed.TotalSeconds -lt 30) {
            if ($startedProcess.HasExited) { throw "Process exited during startup (code $($startedProcess.ExitCode))." }
            if (Test-Path -LiteralPath $traceFile) {
                $traceText = Get-Content -LiteralPath $traceFile -Encoding UTF8 -Raw -ErrorAction SilentlyContinue
                if ($traceText -match '(?m)^\s*[\d.]+ ms main ready\s*$') {
                    $startupMilliseconds = $launchClock.Elapsed.TotalMilliseconds
                    $startupState = 'main_ready'
                    break
                }
            }
            Start-Sleep -Milliseconds 100
        }
        if ($null -eq $startupMilliseconds) {
            $startupState = 'timeout'
            throw 'Startup did not reach the main ready trace marker within 30 seconds.'
        }
    } else {
        $remaining = [Math]::Max(1, 30000 - [int]$launchClock.Elapsed.TotalMilliseconds)
        if (-not $startedProcess.WaitForInputIdle($remaining)) {
            $startupState = 'timeout'
            throw 'Application did not become input-idle within the 30-second startup deadline.'
        }
    }

    $warmupClock = [Diagnostics.Stopwatch]::StartNew()
    while ($warmupClock.Elapsed.TotalSeconds -lt $WarmupSeconds) {
        if ($startedProcess.HasExited) { throw 'Process exited during warmup.' }
        Start-Sleep -Milliseconds 200
    }

    $startedProcess.Refresh()
    $previousCpu = $startedProcess.TotalProcessorTime.TotalMilliseconds
    $sampleClock = [Diagnostics.Stopwatch]::StartNew()
    $previousSeconds = 0.0
    $status = 'sampling'
    while ($sampleClock.Elapsed.TotalSeconds -lt $SampleSeconds) {
        $remainingMs = [Math]::Ceiling(($SampleSeconds - $sampleClock.Elapsed.TotalSeconds) * 1000)
        Start-Sleep -Milliseconds ([int][Math]::Max(1, [Math]::Min(1000, $remainingMs)))
        if ($startedProcess.HasExited) { throw 'Process exited during measurement.' }
        $startedProcess.Refresh()
        $currentCpu = $startedProcess.TotalProcessorTime.TotalMilliseconds
        $currentSeconds = $sampleClock.Elapsed.TotalSeconds
        $elapsedSeconds = $currentSeconds - $previousSeconds
        $cpuDelta = [Math]::Max(0, $currentCpu - $previousCpu)
        $samples.Add([pscustomobject]@{
            elapsedSeconds = $currentSeconds
            intervalSeconds = $elapsedSeconds
            cpuMilliseconds = $cpuDelta
            cpuPercentOfMachine = $cpuDelta / ($elapsedSeconds * 1000 * $logicalProcessors) * 100
            workingSetBytes = $startedProcess.WorkingSet64
            privateBytes = $startedProcess.PrivateMemorySize64
            handleCount = $startedProcess.HandleCount
        })
        $cpuMilliseconds += $cpuDelta
        $measuredSeconds = $currentSeconds
        $previousCpu = $currentCpu
        $previousSeconds = $currentSeconds
    }
    $status = 'complete'
} catch {
    $status = 'failed'
    $failure = $_.Exception.Message
} finally {
    $result.status = $status
    $result.error = $failure
    $result.startupState = $startupState
    $result.startupMilliseconds = $startupMilliseconds
    $result.measuredSeconds = $measuredSeconds
    $result.summary = if ($samples.Count -gt 0) {
        [ordered]@{
            meanCpuPercentOfMachine = $cpuMilliseconds / ($measuredSeconds * 1000 * $logicalProcessors) * 100
            peakCpuPercentOfMachine = ($samples | Measure-Object cpuPercentOfMachine -Maximum).Maximum
            workingSetBytes = $samples | Measure-Object workingSetBytes -Average -Minimum -Maximum | Select-Object Average, Minimum, Maximum
            privateBytes = $samples | Measure-Object privateBytes -Average -Minimum -Maximum | Select-Object Average, Minimum, Maximum
            handleCount = $samples | Measure-Object handleCount -Average -Minimum -Maximum | Select-Object Average, Minimum, Maximum
        }
    } else { $null }
    $result.samples = $samples.ToArray()
    $result.finishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFile -Encoding utf8NoBOM
    } finally {
        if ($null -ne $startedProcess) {
            try {
                if (-not $startedProcess.HasExited) {
                    try { [void]$startedProcess.CloseMainWindow() } catch { }
                    try { [void]$startedProcess.WaitForExit(2000) } catch { }
                    if (-not $startedProcess.HasExited) {
                        Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
                    }
                }
            } finally { $startedProcess.Dispose() }
        }
    }
}
if ($failure) { throw "Benchmark failed: $failure. Diagnostic result: $outputFile" }
Write-Output $outputFile
