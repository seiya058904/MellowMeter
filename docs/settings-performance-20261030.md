# Settings responsiveness correction — 2026-10-30

## Scope and evidence

The user reported that settings navigation was unusably slow in the previous candidate. This correction compares navigation with local upstream commit `3b91b5ca6d70a9056b8fdd01c9acddc985721240`. Upstream switches native pages directly; it does not rasterize and cross-fade the entire settings hierarchy. The removed paths are plausible contributors established by code inspection, not a measured ranking of wall-clock costs.

The user's desktop remains untouched: no application/window test, desktop capture, input injection, driver installation or hardware benchmark was run. No commit, push or release was made. Existing candidate folders remain available.

## Changes

- Remove whole-page `DrawToBitmap` snapshots and two-bitmap slide/fade composition. Keep visited settings pages attached to their original parent; switch visibility and z-order directly. Do not restore upstream's synchronous Refresh/DoEvents calls.
- Preserve cached scroll and disclosure state naturally. Theme/language reconstruction still captures and restores view state. Suppress dirty tracking during reconstruction.
- Track actual input changes instead of every control Click/TextChanged. Deduplicate subscriptions and coalesce canonical draft comparison over 120 ms; enable Apply immediately. Save the departing/current page during editing and all visited pages when applying. Cancel retains the draft transaction.
- Do not invalidate the monitor page after its own Save. Reuse row editors across panel/taskbar/filter changes; toggle label editors in place. Dispose detached cached rows when their data changes or the page closes.
- Replace animated container-height changes with a single layout update and a lightweight chevron animation. Restore native wheel/trackpad scrolling instead of repeatedly moving child windows on animation frames.
- Coalesce rapid hardware-source page revisits within five seconds and avoid refilling unchanged string selectors. This does not poll on a timer.
- Retain the glass palette/material policy, rounded groups, button press/release, switch and navigation selection feedback, reduced-motion support and idle-stopped animation scheduler.

The original 220 ms whole-page fade, animated body expansion and wheel easing are intentionally removed in response to the user's performance report. This changes that part of the earlier animation plan. It does not change monitoring collection, the 32-item catalog, the three display layouts or configuration migration.

## Verification actually performed

- Release build of application plus DesktopSmoke project: 0 warnings, 0 errors.
- Isolated no-window console regression: 23 passed, 0 failed, including built-in defaults/migration, sensor-selection logic, native-resource disposal and interrupted spring math. No real sensor-driver verification.
- Added native-window regressions for page-parent stability and monitor-row reuse across navigation, monitor sub-tabs and label-editor visibility. Compiled only; not run.
- Both Portable/Lite packages built. ZIP CRC, SHA-256 manifest, packaged/unpacked executable and DLL consistency, equal application DLL between editions, and bundled/runtime-required distinction verified without launching either edition.
- `git diff --check` passed. Prior unrelated/uncommitted work was preserved.
- No numerical responsiveness/resource-usage claim: latency, scrolling quality, DWM composition and real-window behavior remain user acceptance checks.

## Local candidate

`artifacts/settings-performance-20261030/` contains ready-to-run folders, both ZIPs, `SHA256SUMS.txt`, `verification.json`, and build/regression/package logs.

Portable application: `MellowMeter-0.2.0-win-x64/MellowMeter.exe`. Lite provides the same features but requires .NET 8 Desktop Runtime x64.

When convenient, exit the older MellowMeter from its tray menu before launching this candidate; otherwise single-instance handling may redirect to the older process. Do not copy just the EXE out of its folder. Both editions use the existing profile.

1. Repeatedly switch all five settings categories, especially returning to Monitor; scroll continuously and expand/collapse optional groups.
2. In Monitor, alternate panel/taskbar modes, toggle label editing and visible-only filtering, change a label/unit, and move items/groups. Verify edits survive switching; check Apply, Cancel and reopening separately.
3. Check button/switch feedback and Glass Light/Dark. Turning interface animations off should remain fully usable. Report the exact category/action if a pause persists; no current runtime smoothness guarantee is inferred from compilation.
