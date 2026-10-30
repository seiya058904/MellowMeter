# Monitor parity and settings quality review

The subsequent [compiler warning review](warnings-review-20261030.md) supersedes this build: the current main rebuild has zero warnings; this document preserves the earlier review evidence.

Reviewed against LiteMonitor commit `3b91b5ca6d70a9056b8fdd01c9acddc985721240`.
This is a local follow-up to the monitor restoration. No application, desktop test,
recording, driver installer or FPS helper was launched during this review. The user
is working on the desktop and has paused interactive verification.

## Findings corrected

| Finding | Correction |
| --- | --- |
| Percent/temperature formatting had an extra forced decimal rule | Use upstream adaptive formatting again, including values at/above 100 |
| Filling missing definitions shifted existing sort indexes; oldest files rebuilt their ordering | Preserve existing entries and indexes; initialize the legacy taskbar order in place; append new definitions |
| Sparse custom files gained visible default metrics | Add missing capabilities hidden unless the exact four-item preset is being restored, independently for panel/taskbar |
| A physically four-entry legacy file needed more than flag updates | Complete it to 32 definitions and apply the upstream 13/6 selections |
| Device selectors stopped refreshing once they had more than two entries | Repopulate from each completed enumeration, retaining unavailable manual selections |
| Both transition images faded simultaneously over a solid background | Keep the outgoing image opaque under the incoming fade to prevent mid-transition dimming |
| Button release was ordinary cubic easing; repeated events restarted the same target | Analytic damped spring with velocity continuity; unchanged destinations do not restart |
| Navigation press and disclosure chevrons changed abruptly | Add press/release feedback and synchronized chevron rotation; keep hit bounds stable |
| Wheel animation ignored the system's zero-line preference | Respect zero-line/page scrolling; continue interrupted scrolling with retained velocity and clamp to the live extent |
| An animation requested during disposal could still invoke callbacks | Reject disposed/disposing owners and stop existing tracks without touching dying controls |

The existing three audited fixes were reviewed again: group movement and boundary
states both resolve the header's `Tag`; follow-taskbar state binds when context is
set and saves the draft; swatch initialization and color changes use the same
black/white contrast selection.

## Upstream parity

A read-only source comparison checked all initializer fields, including order,
visibility and taskbar labels. All 32 entries match. `MonitorItemConfig` group
definitions and `FanMapper` also match after whitespace normalization.

| Family | Built-in keys | Collection route reviewed |
| --- | --- | --- |
| Dashboard (4) | DASH.HOST, DASH.Time, DASH.Uptime, DASH.IP | UIController -> InfoService; native network IP injection |
| CPU (7) | CPU.Load, CPU.Temp, CPU.Clock, CPU.Power, CPU.Voltage, CPU.Fan, CPU.Pump | BasicSystemSampler for load; HardwareValueProvider, SensorMatcher/ComponentProcessor and FanMapper for sensors |
| GPU (6) | GPU.Load, GPU.Temp, GPU.Clock, GPU.Power, GPU.Fan, GPU.VRAM | Selected GPU sensor map, component processing and VRAM capacity/percentage handling |
| Host (5) | MEM.Load, FPS, MOBO.Temp, DISK.Temp, CASE.Fan | Native memory; explicit-component FPS; motherboard/storage/fan sensor map |
| Battery (4) | BAT.Percent, BAT.Power, BAT.Voltage, BAT.Current | BatteryService and selected hardware sensors |
| Disk (2) | DISK.Read, DISK.Write | DiskManager and mapped storage sensors |
| Network (2) | NET.Up, NET.Down | BasicSystemSampler and preferred adapter |
| Traffic (2) | DATA.DayUp, DATA.DayDown | TrafficLogger snapshots |

Default panel selection: DASH.Uptime; CPU.Load/Temp; GPU.Load/Temp/VRAM; MEM.Load;
DISK.Read/Write; NET.Up/Down; DATA.DayUp/Down (13). Default taskbar selection:
CPU.Load/Temp, GPU.Load, MEM.Load, NET.Up/Down (6). New profiles are vertical.
All 32 entries remain available in the monitor selector and menu.

The vertical renderer retains upstream groups, cards, progress bars and double
columns; the horizontal renderer retains column composition and display rules;
the taskbar retains its independent order, single/double rows and follow behavior.
Settings glass painting is absent from all three monitor renderers.

These are **functional/source comparisons, not byte-for-byte or pixel identity**.
Intentional differences remain: MellowMeter branding/themes, DPI-aware font
measurement and gaps, long-text clipping, missing/nonfinite/stale-value handling,
valid zero-length bars, native basic counters, demand-driven hardware activation,
pause/history preferences, and user-triggered component installation. Native basic
sampling also retains the derivative's traffic behavior rather than the upstream
performance-counter/SMB subtraction path. Automatic updates, plugins, web monitor
and speed tests remain excluded. Physical sensor availability is not established
by finding a source route.

## iOS implementation references

- [FluidInterfaceKit Drag.swift, pinned source](https://github.com/FluidGroup/FluidInterfaceKit/blob/ca52622410198ea2922de935943aef1e3bba4080/Sources/FluidGesture/Drag.swift): studied its transfer of interaction velocity into spring settling.
- [Telegram iOS layout transitions](https://github.com/TelegramMessenger/Telegram-iOS/blob/master/submodules/Display/Source/ContainedViewLayoutTransition.swift): reviewed no-op target checks and spring timing to understand interruption and layout stability.
- [Microsoft system backdrop material](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type): the settings shell uses documented Windows 11 Desktop Acrylic where supported.

The C# spring is an independent implementation of the damped oscillator equation;
no Swift implementation was copied and no dependency was added. Button press is
90 ms; release 180 ms with restrained overshoot; switches/selection/disclosure
200 ms; page crossfade/shift 220 ms. Scrolling uses a non-bouncing spring. Existing
single-clock scheduling, reduced-animation policy, draft bindings, cached pages
and scroll/expanded-state preservation remain in place. At completion, each track
is removed and the clock stops when no tracks remain. This is a source guarantee;
the updated runtime's idle cost has not been measured.

Native Acrylic is confined to the eligible settings navigation surface; the rest
uses readable themed surfaces/highlights. This does not reproduce Apple's optical
refraction. The earlier recording exercised solid fallback because Windows
transparency was off; that preference was not changed. Active Acrylic still needs
visual verification, including light/dark text composition.

## Evidence and limits

- Static comparison: 32 definitions, 13/6 default visibility, group identity, fan
  mapper, vertical default and absence of settings glass in monitor renderers.
- Main application and DesktopSmoke project compiled: zero errors. The main
  project still reports 163 warnings. No test executable was run in this review.
- Added regression cases for sparse custom profiles, old single-order files and
  physically four-entry profiles. They are compiled, **not executed**.
- Portable and Lite local packages: `artifacts/monitor-quality-20261030-r2/`.
  `SHA256SUMS.txt`, `verification.json` and `source-parity-review.json` record
  package integrity, identical application assembly and source comparison.
- No extracted-package startup, interaction/screenshot/recording, high-DPI visual
  pass, active-Acrylic pass, new performance measurement or hardware validation.
  Previous 996-assertion desktop evidence belongs to the earlier build only.
- No commit, push, tag or publication. Previous packages/evidence are preserved.
