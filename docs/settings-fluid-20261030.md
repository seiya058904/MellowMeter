# Settings refinement — 2026-10-30

Implementation: `5b7f456`, based on `f1a04924088ee45e2ae32ad4c022a39cd7d58c17`. The release documentation commit adds records and screenshots without changing the application. The two release editions contain the same tested application assembly.

## What changed

- Settings pages are created on demand and stay attached after first visit. Scroll, disclosure and draft state survive switching. Metric rows and editors are reused by stable keys; changed context invalidates their cache.
- Apply compares effective configuration once, batches runtime refreshes and skips an unchanged commit. Labels, units and sorting no longer reopen hardware. Source/visibility changes still refresh collection configuration.
- Asynchronous device enumeration checks request versions, preserves manual selections and ignores disposed windows.
- Solid grouped surfaces, line navigation icons, segmented monitor modes and drawn sort controls. Immediate button press, 120 ms release and 160 ms switch feedback use the shared clock; no page/scroll animation or snapshot animation.
- Removed dead glass cache and transparency polling. System preferences remain event-driven. Hidden/minimized history windows stop their display timers while collection/history semantics remain unchanged.

## Local gates and visual review

`scripts/build.ps1 -Test`: build **0 warnings / 0 errors**; Monitoring passed; DesktopSmoke **667 assertions / 37 captures**, passed; inherited core **31 passed / 0 failed**; inherited security **4 passed / 0 failed**. Final local gate log: `artifacts/iteration-fluid/final-gates-r2.log`. Desktop report: `test/DesktopSmoke/bin/Release/net8.0-windows/artifacts/desktop_smoke_20261030_131208_ff11e619/results.json`.

The interrupted baseline (579 assertions, 37 captures) was not counted as a pass. Its fixed renderer expectation now derives from three themes × three scales = nine unique images and verifies completeness. Regression coverage includes lazy pages and retained parents, draft Apply/Cancel, independent labels/units/order, filtering, availability changes on hidden pages, language/theme reconstruction, async return/close races, interrupted motion, idle clock shutdown and hidden history timers. A short-window layout check keeps all bottom actions within the client area.

A concentrated visual review checked native light/Chinese and dark/English settings and the final renderer captures. Current screenshots are in [screenshots/fluid](screenshots/fluid/). Native input verified category/mode switching, label editors, long-list wheel scrolling and Escape in the isolated Portable candidate. The Lite input helper later returned `foreground window did not report a process id`; Lite click-through is not claimed. Final package startup and exact-assembly message-loop Apply/persistence were checked separately. No daily profile, existing instance, driver installation or system display settings were changed.

Native host DPI was **168 (175%)**. The 100/150/200% checks exercise the real renderer at synthetic target DPI. They do **not** establish physical mixed-DPI monitor transitions. Physical sleep/wake, Explorer restart, broad hardware/driver compatibility and long-duration soak testing remain outside this run.

## Performance

[Structured results](settings-fluid-performance-20261030.json) include all three rounds, allocation deltas, process/GDI/USER samples, composition controls and package sizes. The baseline and final probes ran on the same Windows host, DPI and isolated configuration. One warmup round is excluded. First-use category/editor construction is separated from repeated actions. The final timed binary is the shipped binary. Basic sampling continues; physical advanced sensors are disabled.

Timing is **action to native message-loop idle, including pending paint**, not physical input-to-photon latency. Durations are not animation lengths. Allocation deltas include other process activity. Short CPU windows and non-forced-GC memory samples are diagnostic, not power or leak guarantees. A build-overlap diagnostic run was not selected for this comparison.

| Operation | Baseline median / P95 ms | Final median / P95 ms | Mean allocation KiB: before → after |
| --- | ---: | ---: | ---: |
| Open settings | 608.04 / 670.95 | 275.09 / 325.49 | 1558.1 → 479.0 |
| First taskbar page | 96.98 / 125.82 | 238.88 / 253.79 | 120.1 → 611.7 |
| First monitor page | 457.51 / 524.34 | 359.39 / 428.82 | 1531.0 → 1578.9 |
| First threshold page | 192.49 / 194.08 | 182.13 / 194.60 | 555.9 → 569.1 |
| First system page | 158.63 / 163.68 | 407.08 / 413.56 | 288.2 → 875.3 |
| Revisit main | 42.53 / 62.53 | 18.84 / 24.68 | 144.2 → 38.2 |
| Revisit taskbar | 18.25 / 35.82 | 13.00 / 19.39 | 86.2 → 34.7 |
| Revisit monitor | 54.60 / 79.22 | 29.25 / 53.32 | 187.4 → 83.4 |
| Revisit thresholds | 98.66 / 129.50 | 28.47 / 48.50 | 292.6 → 174.0 |
| Revisit system | 54.65 / 100.27 | 27.28 / 38.20 | 217.3 → 86.0 |
| First taskbar mode | 645.56 / 647.34 | 235.00 / 240.83 | 1278.9 → 317.6 |
| First filter | 131.28 / 138.17 | 51.00 / 57.30 | 364.6 → 85.4 |
| First panel mode | 416.18 / 419.40 | 199.77 / 208.96 | 1340.0 → 204.9 |
| First label editors | 927.50 / 1068.28 | 240.42 / 241.61 | 1475.6 → 128.1 |
| Warm taskbar mode | 307.12 / 588.47 | 135.50 / 202.00 | 804.0 → 188.1 |
| Warm filter | 496.07 / 602.90 | 166.70 / 219.62 | 803.9 → 161.1 |
| Warm panel mode | 629.81 / 765.89 | 216.74 / 250.90 | 1449.7 → 236.7 |
| Warm label editors | 734.56 / 860.03 | 74.84 / 97.77 | 1436.4 → 64.3 |
| Unchanged Apply | 35.35 / 60.64 | 0.42 / 3.89 | 2477.1 → 112.1 |
| Width Apply | 37.22 / 70.23 | 15.75 / 25.13 | 2291.4 → 717.4 |

Warm category switches satisfy the 100 ms P95 target. Dense mode/filter work remains above it; first visits also carry deferred construction cost. The 100 ms objective is **partially met**, not an across-the-board claim. First-page cost must be considered together with the faster initial opening; lazy creation shifts work to the page actually used. Improvements for original dense hotspots are reported directly by the table rather than implying universal smoothness.

The same-assembly composition experiment removed `WS_EX_COMPOSITED` from the root and then all descendant controls. Local buffering did not consistently improve latency (including slower repeated category switches), so production retains composited rendering. No claim of validated flicker-free replacement is made.

## Resources

| Build / state | Private MiB | Handles | GDI | USER | CPU % of machine, 5 s |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline / settings-closed | 23.71 | 527 | 33 | 39 | 0.020 |
| Baseline / closed-round--1 | 56.02 | 583 | 98 | 60 | — |
| Baseline / settings-open-idle | 57.00 | 585 | 119 | 152 | 0.137 |
| Baseline / closed-round-0 | 67.38 | 591 | 98 | 61 | — |
| Baseline / closed-round-1 | 76.14 | 592 | 130 | 61 | — |
| Baseline / after-interaction-idle | 85.04 | 593 | 148 | 578 | 0.117 |
| Baseline / closed-round-2 | 85.92 | 593 | 118 | 61 | — |
| Baseline / after-20-open-close | 76.10 | 605 | 300 | 66 | 0.000 |
| Final / settings-closed | 22.59 | 530 | 33 | 38 | 0.000 |
| Final / closed-round--1 | 51.55 | 590 | 202 | 61 | — |
| Final / settings-open-idle | 56.05 | 590 | 208 | 124 | 0.078 |
| Final / closed-round-0 | 60.92 | 589 | 202 | 62 | — |
| Final / closed-round-1 | 60.81 | 593 | 338 | 62 | — |
| Final / after-interaction-idle | 82.93 | 599 | 371 | 829 | 0.000 |
| Final / closed-round-2 | 83.83 | 599 | 212 | 63 | — |
| Final / after-20-open-close | 82.77 | 595 | 278 | 60 | 0.098 |

Closing releases the cached controls and USER handles. Private bytes and GDI counts fluctuate with collection/framework caches; the 20-open/close sample is not sufficient to certify absence of long-term growth. Idle animation shutdown is asserted separately. Retained visited pages/editors trade some open-window memory/handles for much lower allocation and repeat-action cost. Do not equate a retained working set with a permanent leak or claim that memory decreased in every state.

## Package verification

Both archives passed CRC validation, per-entry SHA-256 comparison against their published folders, required resource/license checks, 12-theme checks and exclusion checks. The manifest excludes retired plugins/themes, updater, driver/FPS installers, debug symbols, private keys and user profiles. No runtime DLL was manually removed, no dependency was changed and `PublishTrimmed=false` remains explicit in packaging.

| Edition | ZIP MiB | Expanded MiB | Files | App/config KiB | Resources KiB | Dependencies MiB | Runtime MiB | Notices KiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Lite | 2.73 | 6.53 | 46 | 804.9 | 95.2 | 5.55 | 0.00 | 103.5 |
| Portable | 70.56 | 164.34 | 504 | 838.6 | 95.2 | 5.55 | 157.71 | 175.1 |

Runtime dominates Portable size; Lite offers the same features without bundled .NET. This iteration removes dead code but does **not** claim a smaller ZIP: safety checks and feedback add a few KiB compared with the prior local candidate. All runtime/dependency files required by the publish graph remain.

Application SHA-256: `cc3879e821e51ad42c73ff02f3e053e9561b49c2b42c97ff008b3b1e60ec3d09`.

Archive SHA-256:

- `MellowMeter-0.2.0-win-x64-lite.zip`: `a943ab1b053f4c2a5569eaa0761e9f12f0c70c025e6192d6c9bb86966d8484bc`
- `MellowMeter-0.2.0-win-x64.zip`: `7b70b9fdd88de2ebfbf92a5f55e3bf359643b68ca4255b365cd40459376f538d`

Local deliverable: `artifacts/release-v0.2.0-5b7f456/`. Portable startup used its bundled `coreclr.dll` with an intentionally unavailable external DOTNET_ROOT; Lite used the user-local .NET 8 Desktop Runtime. Isolated profiles and diagnostics remain under `artifacts/iteration-fluid/`. Real process startup does not by itself prove hardware compatibility; Apply/save, pending-sample shutdown and history persistence are covered by the exact-source/assembly regressions and probe.

[Release](https://github.com/seiya058904/MellowMeter/releases/tag/v0.2.0) · [CI](https://github.com/seiya058904/MellowMeter/actions/workflows/build.yml). CI status and public downloads should be checked at their current revision; historical reports are not substitutes.

## Local workspace audit

The workspace is dominated by retained candidate packages and repeated bundled runtimes, not application source. `artifacts/iteration-fluid/WORKSPACE-AUDIT.md` lists exact paths, sizes and recovery costs. Reparse points are excluded from the size walk. Historical candidates, profiles and backups were preserved; no cleanup or branch deletion was performed.
