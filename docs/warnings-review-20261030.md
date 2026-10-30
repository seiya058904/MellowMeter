# Compiler warning review and local acceptance build

The 163-warning baseline was reviewed individually. A full Release rebuild now
reports **0 warnings and 0 errors**. Nullable analysis remains enabled; no `NoWarn`,
`#pragma warning disable`, dependency upgrade or framework change was added.
The per-diagnostic baseline and disposition are retained in the delivery's
`evidence/warning-dispositions.csv`.

## Actual defects corrected

- **Theme font ownership:** repeated `BuildFonts()` calls leaked previous fonts.
  Replacement now constructs a complete set, releases partial allocations on
  failure, disposes the old set, and provides valid system-font fallbacks.
  Invalid numeric font sizes are bounded before reaching GDI. Disposal is
  repeatable and leaves no disposed font exposed as a usable fallback.
- **Theme editor lifetime:** switching themes and closing the editor now release
  its owned theme fonts and installed-font collection; the application's live
  theme is not disposed by the editor.
- **Network event lifetime:** the anonymous static network-change subscription
  kept manager instances alive. The manager now unsubscribes on disposal and
  disposes a performance manager only when it owns that instance.
- **Display disappearance:** taskbar placement/handle discovery tolerate a missing
  primary screen instead of dereferencing it during topology changes.
- **Screen color picker:** capture and overlay use the same screen, including
  non-primary and negative-coordinate displays; pixel coordinates are checked,
  the bitmap is disposed, and Escape/right-click cancels. This remains an explicit
  user action; it was not launched during this review.
- **Deferred settings/hardware context:** callbacks guard unbound contexts and
  snapshot the optional hardware-monitor reference before background enumeration.
  Empty lookup/selection values and optional input units are modeled accurately.
- **Minor regressions:** renamed `PageTransition.CaptureSnapshot` to avoid hiding
  WinForms' mouse-capture property; the component dialog now displays MellowMeter
  branding. Removed an unused duplicate drag flag while retaining real drag state.

The remaining warning resolutions express existing contracts: UI initialization
uses compiler-verified `MemberNotNull` postconditions; optional fields/results and
event senders are nullable; the text setter accepts null and normalizes it; lazy
InfoService initialization is thread-safe. These are not 163 separate runtime bugs.

## Verification performed without windows

| Check | Result |
| --- | --- |
| Full main application Release rebuild | 0 warnings, 0 errors |
| DesktopSmoke project compile only | 0 warnings, 0 errors; executable not run |
| Console core regression | 23 passed, 0 failed |
| Native sampler console regression | Passed |
| Pinned upstream initializer comparison | All 32 definitions match; default panel 13/taskbar 6 |
| Group rules and FanMapper comparison | Match ignoring whitespace |
| Monitor renderer separation | No settings glass painter in vertical/horizontal/taskbar renderers |
| Portable/Lite publish and archive verification | Passed; see delivery hashes and manifest |
| Diff whitespace check | Passed |

New console regressions exercise sparse and four-entry migration, old single-order
profiles, custom label/unit/position preservation, exact original backup bytes and
repeat-load stability; repeated font replacement/disposal; network-manager garbage
collection after unsubscription; spring interruption/velocity/settling. Existing
cases cover synthetic GPU selection/mapping, CPU frequency fallbacks and memory
formatting. Native CPU/memory/NIC checks do not open hardware drivers.

The console suite creates no forms. Configuration writes use isolated directories
under `.local/warnings-review/`; the normal user profile was not used. The security
suite's control-construction cases, DesktopSmoke and PerformanceProbe were not run.
No application window, recorder, driver installer or FPS helper was started.
Active Acrylic, final UI interactions, physical multi-monitor/DPI changes, actual
fan/FPS hardware and packaged application startup remain manual acceptance items.
Passing console checks does not establish complete GUI or hardware regression
coverage.

## Delivery

Latest folder: `artifacts/warnings-reviewed-20261030/`.

- `MellowMeter-0.2.0-win-x64/MellowMeter.exe`: ready-to-start self-contained Portable.
- `MellowMeter-0.2.0-win-x64-lite/MellowMeter.exe`: identical application features,
  requires .NET 8 Desktop Runtime x64 installed on the target computer.
- ZIPs, `SHA256SUMS.txt`, `verification.json` and `evidence/` accompany both folders.

Portable bundles the runtime; Lite omits it. Both use the same application code,
monitoring features and default configuration directory `%LOCALAPPDATA%/MellowMeter`.
The name Portable means no runtime installation is needed; settings do not travel
inside the application folder by default. Keep the whole extracted folder together.

Suggested acceptance: launch Portable after exiting any older MellowMeter instance;
inspect the full metric list and the three monitor layouts; reorder groups and save/
reopen; test follow-taskbar with OK/Apply/Cancel; test light/dark themes, readable
swatches, rapid navigation/scroll/press feedback and disabled animation; finally
exit/reopen and confirm saved settings. A dash for unsupported sensors is expected.
With Windows transparency disabled, the solid material fallback is expected.

No commit, push, release, deployment or cleanup was performed. Earlier packages and
evidence remain preserved. This build supersedes the prior quality-review package.
