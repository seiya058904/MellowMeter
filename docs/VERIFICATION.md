# Verification record

Local evidence collected on **2026-10-29**. The original default theme and taskbar font are retained. WinForms renderer captures were reviewed in dark/light modes and in the original cyan, purple and amber themes after returning checkboxes to the native implementation. The earlier interactive desktop check was stopped with Esc; the last styling changes have renderer coverage but no final interactive recheck. The [initial Windows CI run](https://github.com/seiya058904/MellowMeter/actions/runs/40875378179) passed all six suites; subsequent commit results are available in the [workflow history](https://github.com/seiya058904/MellowMeter/actions/workflows/build.yml).

## Environment and completed checks

Windows 11 Enterprise **10.0.22631**, AMD Ryzen 7 9700X (**16 logical processors**), **32 GiB** physical RAM; **3840 × 2160 at 175% / 168 DPI**. Builds used .NET SDK **8.0.427** and runtime **8.0.33**. No new hardware drivers were installed.

| Check | Recorded result |
| --- | --- |
| Release build | 0 errors; nullable/unused-code warnings remain, predominantly inherited |
| Native monitoring regression | Passed: native readings, missing adapters, counter resets, slow refresh, history boundaries and nonblocking reset |
| Desktop smoke | 246 assertions passed; 29 captures |
| Inherited release review | 28 passed |
| Inherited security regression | 25 passed |
| WebSocket protocol compatibility | 5 passed |
| Holiday fallback, mocked responses | 28 passed; live external API mode was not run |
| NuGet vulnerability audit | No known vulnerable packages reported at the time of the check |

Desktop checks cover settings Apply/Cancel, missing-value display and recovery, four metric defaults, history preservation and shutdown draining, and simulated lock/suspend/hidden/recovery transitions. These invoke application behavior; they do not physically lock or suspend Windows. See the [desktop harness](../test/DesktopSmoke/README.md) and [build script](../scripts/build.ps1), which runs all six suites.

Theme linkage was checked across DarkFlat Classic, CyberBlue Neon, AuroraDream Purple, SolarAmber Warm and Mellow Light with settings and both histories already open. Unapplied settings, the active page, seven-day trend range and monthly traffic view survived each switch. Taskbar state colors met 4.5:1 contrast on the two representative dark/light test surfaces. Actual wallpaper, transparency and custom taskbar colors may differ from those surfaces.

The renderer was exercised at synthetic **100%, 150% and 200%** scales. Local native form captures use the host's actual **175%** scale; the initial Windows CI run also passed at **96 DPI**. Physical DPI changes, mixed-DPI multi-monitor transitions, real sleep/wake, Explorer restart, and a broad hardware/driver compatibility matrix **were not tested**. Advanced sensor availability remains hardware- and permission-dependent.

## Matched local benchmark

Both applications displayed **CPU.Load, MEM.Load, NET.Up and NET.Down**, with **DarkFlat Classic**, a **1-second refresh**, horizontal desktop panel, taskbar strip and history enabled. Each visible application ran separately, with **15 seconds of warmup** after readiness and approximately **60 seconds of measurement** at 1-second intervals. The MellowMeter measurement uses the retained original theme and font; it predates the final theme-linkage fixes.

The baseline is LiteMonitor commit `3b91b5ca6d70a9056b8fdd01c9acddc985721240`, modified for this local comparison: bypass startup driver dialogs, run as the current user, tolerate an absent optional text resource, add a readiness trace, and disable automatic forced GC/working-set trimming. The [exact baseline patch](benchmark-baseline.patch) is retained. This is a **patched comparison baseline**, not an untouched upstream release. Neither measured run used forced collection or working-set trimming.

| Metric | MellowMeter | Patched LiteMonitor baseline |
| --- | ---: | ---: |
| Startup to readiness marker | 596.4 ms | 699.4 ms |
| Mean CPU, percent of the whole machine | 0.0294% | 0.0359% |
| Peak sampled CPU, percent of the whole machine | 0.4834% | 0.7563% |
| Mean working set | 68.81 MiB | 107.01 MiB |
| Mean private bytes | 18.43 MiB | 41.60 MiB |
| Mean process handles | 519.13 | 866.27 |

[Machine-readable summary](benchmark-summary.json) contains the measured values without user paths or process IDs. [Measure-Process.ps1](../scripts/Measure-Process.ps1) uses process CPU-time deltas divided by actual elapsed wall time and all 16 logical processors. Memory and handle summaries are averages of sampled values; child processes are excluded. Startup is measured from launch to detection of the marker at the end of `OnShown`, with **100 ms polling**. It does not measure the first fully populated sensor frame.

This is **one sequential run per application under ambient desktop workload**, not a controlled statistical experiment. Both CPU totals were very small, so counter granularity and background activity materially affect the comparison. Startup differences are also close to the polling resolution. These observations do not establish general performance guarantees, long-term stability, or an expected percentage improvement on other machines.

## Measurement limits

CPU load is Windows processor **busy time**, which differs from frequency-weighted utility. On systems with **more than 64 logical processors**, the current `GetSystemTimes` implementation covers only the calling thread's primary processor group, so it must not be interpreted as whole-machine utilization on those systems. See [Microsoft's GetSystemTimes documentation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes).

Network auto mode selects the busiest eligible adapter rather than summing every interface. Explicit selection does not substitute a missing adapter; unavailable samples remain missing. Traffic includes local/SMB bytes. These semantics and the benchmark's short duration should be considered when comparing other monitors.
