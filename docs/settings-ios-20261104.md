# Settings design system — 2026-11-04

Record of the iteration briefed in [iteration-next-settings-ios.md](iteration-next-settings-ios.md). The contract it produced is in [DESIGN.md](DESIGN.md). Structured numbers are in [settings-ios-performance-20261104.json](settings-ios-performance-20261104.json); raw runs and the full capture matrix are in the ignored `artifacts/ios-iteration/`.

**Status: awaiting owner hands-on approval.** Everything below is measured or synthetic evidence. The brief's definition of done requires hands-on approval, and nobody has yet used this build by hand.

## Read this first: Windows animation effects are off on the development machine

`SPI_GETCLIENTAREAANIMATION` returns 0 on this machine. The app has always honoured that preference, so **none of the previous iteration's motion ever ran here, in the owner's window or in the test suite**. The motion block of DesktopSmoke passed without exercising a single spring; it now forces motion on for its own process.

The preference is still honoured and Appearance now says when it is the reason motion is off. To judge motion, turn on Settings > Accessibility > Visual effects > Animation effects, or start one process with `MELLOWMETER_FORCE_MOTION=1`, which changes nothing machine-wide:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\MellowMeter\dotnet"; $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:MELLOWMETER_FORCE_MOTION = '1'
$env:MELLOWMETER_DATA_DIR = "$env:TEMP\mellowmeter-handson"   # keeps your real profile out of it
& .\bin\Release\net8.0-windows\MellowMeter.exe --settings
```

## What changed

- **Design system** (`DesignSystem.cs`): colour roles, type scale with Segoe UI Variable, row and shape tokens, continuous corners, allocation-free painting helpers, a light `StackPanel` replacing `TableLayoutPanel` and dock chains.
- **Motion** (`MotionSpring.cs`, `InterfaceMotion.cs`): springs specified by response and damping, analytic wall-clock sampling, composition-paced clock, optional frame trace.
- **Scrolling** (`ScrollSurface.cs`): one moved content window, wheel glide with acceleration, rubber band, overlay indicator with hit slop and thumb momentum. This removes the native-scrollbar suppression hack.
- **Controls**: draggable and flickable switch; `LiteSelector` (sidebar and segmented, one sliding pill); `LiteComboBox` plus `LitePopupMenu` replacing native combos; `LiteSlider` with value bubble; stepper; validation shake and tint; clear button; themed caret.
- **Drag-to-reorder** (`ReorderSession.cs`) for rows and whole groups, including filtered taskbar lists. It commits through the same child-order change as Move up / Move down, so saving logic is untouched: `MonitorItemRow.SyncToConfig` is byte-identical to the previous commit.
- **Window**: resizable with a minimum size, large collapsing page titles, section footers, atomic page swaps, keyboard order matching visual order, DPI and system-preference rebuilds.
- **Translations**: filled keys that showed raw English or key names outside en/zh (`MoveUp`, `MoveDown`, `CustomizeLabels`, `Sources*`, `VisibilitySafety`, `InterfaceAnimations`) and corrected German `Apply`/`Cancel`.

## Decisions, with evidence

| Decision | Choice | Evidence |
| --- | --- | --- |
| Frame clock (backlog 4.7) | `DwmFlush` worker posting one tick at a time | Wheel-glide P95 interval 6.8 ms against 31.1 ms for the WinForms timer, same build and composition |
| `WS_EX_COMPOSITED` | Removed from settings | Wheel-glide frame cost 1.7 ms median against 9.5 ms with it |
| Page transition | Synchronous swap, then a page-coloured veil dissolves; no snapshot | Veil is one opacity change per frame; warm switch P95 with motion on stays at 13–17 ms |
| Window chrome (backlog 4.8) | Keep native caption, colour-matched through DWM | A custom frame risks snap layouts, hit testing and accessibility, and the caption is already visually unified |
| Layered child windows | Not used | Need a Windows 8 compatibility manifest entry, which would affect the whole app |
| Idle page prewarm | Not added | First visits already meet budget, so the extra memory buys nothing |

## Performance

Three interleaved run pairs on the final assembly (SHA-256 `D197E55D…76EC7`), each with three measured rounds after an excluded warmup. Baseline is the previous iteration: commit `612ccaa` plus its uncommitted patch. Machine: 20 logical CPUs, 144 DPI, 165 Hz, 4–7 % total CPU before each run. **Timing is action to message-loop idle, not photon latency.** This table uses the host's real preference, so both builds run without motion; the motion-forced table is in the JSON and agrees.

| Operation | Baseline median / P95 ms | Final median / P95 ms | Budget |
| ---: | ---: | ---: | ---: |
| Open settings | 88.1 / 109.0 | **36.1** / 67.6 | median ≤ 120 — met |
| First Taskbar page | 86.6 / 115.8 | **23.2** / 33.8 | ≤ 120 / ≤ 150 — met |
| First Monitor page | 118.4 / 193.3 | **84.5** / 97.1 | ≤ 120 / ≤ 150 — met |
| First Threshold page | 54.1 / 148.7 | **30.2** / 36.7 | ≤ 120 / ≤ 150 — met |
| First System page | 120.4 / 168.9 | **31.5** / 41.2 | ≤ 120 / ≤ 150 — met |
| Revisit pages (slowest) | 15.4 / 21.6 | 12.3 / 29.7 | P95 ≤ 100 — met |
| Taskbar tab, warm | 52.8 / 72.9 | **33.9** / 45.9 | P95 ≤ 100 — met |
| Filter, warm | 71.0 / 93.8 | **40.9** / 54.7 | P95 ≤ 100 — met |
| Panel tab, warm | 73.8 / 115.7 | **51.1** / 61.5 | P95 ≤ 100 — met |
| Label editors, warm | 23.8 / 35.0 | **13.1** / 17.3 | P95 ≤ 100 — met |
| First Taskbar tab | 59.7 / 96.1 | 86.8 / 97.2 | **slower than baseline** |
| Width Apply | 17.4 / 30.1 | 11.5 / 31.3 | P95 ≤ 100 — met |

### Frame cadence

UI-thread tick intervals and tick cost including the forced paint; synthetic input; median of three passes.

| Scenario | Clock, composition | Interval median / P95 ms | ≤ 20 ms | Frame cost median / P95 ms |
| --- | --- | ---: | ---: | ---: |
| Wheel glide | **DwmFlush, none (shipped)** | 6.1 / 6.8 | ~100 % | 1.7 / 3.0 |
| Wheel glide | Timer, composited (previous) | 15.6 / 31.7 | 87 % | 10.5 / 14.6 |
| Disclosure | **DwmFlush, none (shipped)** | 6.1 / 12.1 | ~100 % | 2.7 / 7.9 |
| Disclosure | Timer, composited (previous) | 15.7 / 26.8 | 95 % | 11.7 / 25.3 |
| Page switch | **DwmFlush, none (shipped)** | 6.1 / 8.7 | ~100 % | 0.9 / 2.2 |
| Page switch | Timer, composited (previous) | 15.8 / 30.5 | 93 % | 2.0 / 20.5 |

The clock follows the 165 Hz display. The longest shipped-configuration gaps were 20 ms for wheel, 22 ms for page switch and 34 ms for disclosure.

### Input handling

Time inside one input message, including its synchronous first paint. Budget 8 ms.

| Action | Median / P95 / max ms | |
| --- | ---: | --- |
| Button press | 3.7 / 6.1 / 7.0 | met |
| Switch press | 2.3 / 4.1 / 4.8 | met |
| Switch drag step | 0.2 / 0.4 / 0.8 | met |
| Slider drag step | 1.0 / 2.5 / 2.6 | met |
| Segment select | 0.4 / 0.9 / 1.1 | met |
| Menu key | 2.8 / 3.9 / 4.3 | met |
| Reorder move | 0.2 / 1.0 / 1.4 | met |
| Reorder lift | 4.3 / 7.7 / 13.5 | P95 met; one outlier over |
| **Menu open** | **11.1 / 15.1 / 15.8** | **missed** |

### Resources (median of three runs)

| State | Private MiB | Handles | GDI | USER |
| --- | ---: | ---: | ---: | ---: |
| Settings closed: baseline → final | 21.7 → 21.7 | 481 → 482 | 33 → 33 | 35 → 35 |
| Open, idle: baseline → final | 52.3 → **47.1** | 518 → **507** | 208 → **186** | 103 → **76** |
| All pages visited: baseline → final | 55.8 → **49.4** | 519 → **506** | 361 → **337** | 807 → **651** |
| After closing: baseline → final | 55.8 → **49.5** | 519 → **506** | 202 → **182** | 39 → 40 |

At rest with settings open, no track, timer or clock runs. Managed allocation in the final build is about 8–13 KiB/s in every state, including with settings closed, so that is the monitor's own sampling and settings adds nothing measurable. Six open/close cycles in DesktopSmoke return USER 51 → 51 and GDI 116 → 84. Resident 60-second sampling without settings open shows no real change: 0.180 % → 0.171 % of machine CPU and 108.5 → 107.4 MB working set. That sample was taken just before the last colour-role and focus-ring changes, which do not touch this path.

## Known misses and limits

- **Menu open** costs 11 ms median against the 8 ms budget, because it creates a top-level window and renders the first frame. The press feedback itself is painted well inside budget. Reusing one hidden window would fix this.
- **First Taskbar tab** is about 27 ms slower than baseline at the median. It happens once per window and stays under 100 ms; warm tab switches are faster than baseline.
- **Warm page switch P95** without motion rose from 11–22 ms to 16–30 ms, because painting is no longer batched by composition. Medians are unchanged and it stays far below budget.
- **Lifted row padding**: the row's title sits flush against the rounded left edge of the drag sprite, because the sprite is cut to the row's own bounds. Cosmetic, and only visible during a drag.
- **Text selection colour** stays the system colour. The native edit control offers no per-window override; the caret does take the theme tint.
- **Disclosure content fade** uses a veil over the rows rather than per-row opacity. That needs layered child windows, which are ruled out above.
- **Slider accessibility** exposes value and role, not the UIA RangeValue pattern: .NET 8 keeps that hook internal for custom controls.
- **Screen-reader reading order** in object navigation follows window z-order, which is bottom-to-top in stacks, as it was before. Keyboard Tab order is correct and tested.
- **Monitor page** pins its title rather than collapsing it, so the mode control stays reachable.
- **Other windows** (hardware info, traffic history, theme editor, update dialog) keep their old look, as the brief allowed. They share `LiteButton` and were checked for regressions only.

## Not verified

- **Hands-on feel by a person.** Motion was judged from frame data and mid-animation captures. Nobody has used the gestures with a real mouse: switch drag, slider, reorder and menu were driven through their message or action paths.
- Physical mixed-DPI transitions and a runtime `WM_DPICHANGED`; only synthetic 100 / 150 / 200 % scales were run, on a 150 % host.
- A real high-contrast theme; the palette was exercised with a process-local override against the host's normal system colours.
- RTL layouts, since no shipped language uses them, and a real Narrator or NVDA session.
- Packaged Portable and Lite builds; no package was built in this iteration.

## Checks on the final tree

Performance figures above were measured on commit `f14e4a3`. The commit after it only changes how the Monitor toolbar and threshold rows wrap when a small screen narrows the window below its minimum; CI's 1024-pixel screen at synthetic 200 % exposed that overlap, and the row geometry check now reproduces it on any host. It was not re-measured.

On `f14e4a3`: Release build 0 warnings and 0 errors; `test/Monitoring` passed; `test/DesktopSmoke` 991 assertions in three consecutive runs (667 before this iteration); `test/发版审查` 31 passed; `test/安全修复回归` 4 passed. With the wrap fix: DesktopSmoke 1017 assertions. The app started with `--settings` on an isolated profile. Coverage added: `test/DesktopSmoke/FlagshipChecks.cs` runs with motion genuinely on, including contrast of thirteen text-and-surface pairs in all 12 themes, which found secondary text at 4.28:1 in input fields; that is now fixed. An external client read the live window through MSAA and UI Automation from another process and confirmed roles, names, values and patterns, and that invoking a sidebar item switches the page. `PerformanceProbe --qa` captured the visual matrix across eight themes and six languages; a selection is in [screenshots/ios](screenshots/ios/).
