# Desktop smoke harness

Run only when native test windows are authorized. Each run creates a unique profile under its artifact directory and sets `MELLOWMETER_DATA_DIR` before loading app types. Physical advanced sensors and autostart are disabled. The fixture uses only its own windows and briefly creates a taskbar child for simulated recovery checks.

```powershell
. ./scripts/dotnet-path.ps1
& $DotnetExecutable run --project test/DesktopSmoke/DesktopSmoke.csproj -c Release -- --artifacts artifacts/desktop-smoke
```

The real WinForms event loop exercises:

- All 32 defaults, legacy migration, missing/nonfinite readings, zero and recovery.
- Apply/OK/Cancel, pending inputs across category/mode/filter changes, independent labels/units, filtered order and follow-taskbar state.
- Lazy category creation, stable page parents, row identity reuse, context invalidation and restoration of disclosure state after theme changes.
- Unchanged/history-only Apply without theme reload, refresh-interval updates and asynchronous source selection/generation/disposal behavior.
- Light/dark grouped settings, Chinese labels, short-window action visibility, native keyboard control, interrupted switch feedback and stopped idle animation clocks.
- Cross-window theme linkage, taskbar contrast, stopped hidden/minimized history clocks, simulated lock/suspend/taskbar recovery and asynchronous shutdown draining.
- Real renderer geometry at 100%, 150% and 200%. The expected capture set is derived from the theme/DPI matrix and checked for completeness and uniqueness.
- The settings design system with motion genuinely on (`FlagshipChecks.cs`): spring math and interruption, sliding pill, page veil, wheel glide, rubber band, overlay indicator, keyboard paging, synchronous press paint, switch drag and flick, picker menu keys and dismissal, slider stops and commits, stepper, disclosure interruption, drag-to-reorder including group blocks, edge scroll and filtered taskbar order, Tab order, minimum window size, rebuild with view state, synthetic scales, high-contrast palette and resource release.

Motion blocks set `InterfaceMotion.SystemPreferenceOverride` for this process only. Without it, a host with Windows animation effects off skips every spring and the checks would prove nothing. The machine-wide preference is never changed, and one block asserts that the real preference still turns motion off.

`results.json` contains exact assertions, captures, failure stage and native DPI. A 120-second watchdog catches hangs. Native form bitmaps are rendering checks; they do not measure DWM presentation or prove actual OS input latency, physical sleep/wake, Explorer restart or mixed-DPI monitor transitions.

`build.ps1 -Test` runs Monitoring, DesktopSmoke, the headless release-review suite and the remaining security regressions. Retired web/plugin suites are not release gates. See `docs/settings-ios-20261104.md` for the current evidence; older performance/rollback documents describe older binaries.
