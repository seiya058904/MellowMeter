> Settings rollback (2026-10-30): current assertions use the v0.1 five-page/static-control behavior. Lazy page creation, always-parented pages, row-identity caching, automatic Apply disabling and settings Acrylic assertions were withdrawn. Previous capture and animation descriptions below document earlier candidates. This rollback was compiled and checked with the separate no-window console suite only; DesktopSmoke was not run.

# Desktop smoke harness

Build without opening windows:

```powershell
dotnet build test/DesktopSmoke/DesktopSmoke.csproj -c Release
```

Run on an interactive Windows desktop when UI execution is authorized:

```powershell
dotnet run --project test/DesktopSmoke/DesktopSmoke.csproj -c Release --no-build -- --artifacts "D:\path\to\smoke-artifacts"
```

Each run creates a fresh data subdirectory and sets `MELLOWMETER_DATA_DIR` before loading application types. It never reuses the normal profile, enables advanced sensors or changes autostart. It briefly creates and removes its own taskbar child for recovery checks. Fixtures and screenshots remain for inspection; no cleanup is performed.

The STA harness uses the real WinForms message loop, windows, settings transaction, persistence and renderer. It checks missing/non-finite values, same-value recovery and real zero progress; 32 built-in metrics and upstream default selections, the new Glass Light default and retained taskbar font; target-DPI bounds and text height; Cancel/Apply, live theme/language changes and preserved orientation; and history persistence. It also invokes lock/suspend/hidden states, sends a simulated TaskbarCreated message, and injects a pending sample to verify asynchronous shutdown drains it before saving paused history. Existing history is loaded by actual startup while recording is disabled. Reflection reaches the integration points inside this isolated test process; it does not control other applications.

Theme-linkage checks keep settings and both history windows open while switching through DarkFlat_Classic, CyberBlue_Neon, AuroraDream_Purple, SolarAmber_Warm and MellowLight via the application's normal theme action. They check the floating panel and open-form palettes, chart/grid colors, preserved settings page and unapplied width, seven-day trend range and monthly traffic period. Taskbar rendering uses the actual renderer on dark and light simulated system surfaces; its three state colors must meet 4.5:1 contrast, and cyan/purple/amber selections must produce distinct accents. These bitmap checks do not change the Windows theme or drive OS input.

Grouped settings checks cover all five pages in dark and light themes, including both metric tabs. Visible row fields and labels must stay inside their row without overlapping. Optional groups must collapse, release their body space, and expand through a focused header's Space key while retaining pending values. The history pill checkbox must preserve its draft binding and fire exactly one change per programmatic or Space toggle. Keyboard checks invoke the focused control's window procedure inside this process; they send no global OS input. The earlier Apply/Cancel checks remain in place.

Synthetic DPI renderings use pixel fonts converted from the theme's point sizes at the target DPI, and pass the measurement Graphics into the actual layout builder. This is necessary because bitmap DPI metadata alone does not change the desktop DPI of the GDI HDC used by TextRenderer. It keeps glyph size and layout scale consistent without relaxing the text-fit assertions; native forms retain their ordinary point fonts.

Native label height checks use each label's rendering mode and Graphics context. GDI `TextRenderer` metrics are used for ordinary labels, and GDI+ only for compatible-text labels; `Font.Height` alone can overestimate a correctly rendered GDI label at 96 DPI.

`results.json` records passed assertions, failures, native DPI and capture paths; capture counts are derived from the executed variants. `render_*` images exercise the actual renderer at 100%, 150% and 200%; native `main_*`, `settings_*` and `history_*` form captures use the current Windows DPI. Settings captures include `settings_{dark,light}_{main,taskbar,metrics,threshold,system}` and a Chinese `settings_light_main_zh` variant that leaves the stored language unchanged. Theme linkage adds `settings_cyan`, `settings_purple`, `settings_amber` and two `taskbar_*_system` renders per selected theme. These do not replace a live multi-monitor DPI or accessibility audit. A 120-second watchdog exits with code 124 and records the stuck stage; assertion failures return 1 and success returns 0.

The v0.2 iteration adds both glass themes to the settings and synthetic DPI matrices. It verifies migration backup/idempotence, unchanged legacy theme selection, excluded retired types, lazy page creation, clean/dirty/reverted Apply states (including monitor rows), preserved page/disclosure state, explicit material opt-out, cached chart hover lookup and spike-preserving pixel reduction without changing source points. Native Mica is verified separately by `test/PerformanceProbe --preview`: these smoke bitmaps cannot establish DWM composition.

`build.ps1 -Test` runs four suites. WebSocket and holiday/plugin suites remain in source for historical reference but are no longer release gates, because their entire production modules are excluded. Core monitoring, alias handling, cache refresh and the remaining regression checks still run.

The monitor-restoration iteration adds real group-button clicks and save/reload order checks; true follow-taskbar state through OK/Apply/Cancel without entering its tab; black/white swatch contrast in both glass themes; compact-profile migration with all 32 keys; interrupted navigation/toggles/disclosures; and zero active tracks/timer after settling or closing. The extended timeout accommodates the full metric editor geometry matrix. Physical sensor access remains explicitly disabled in the desktop fixture. Restored FPS is present; plugin, web and updater services stay excluded.

The settings performance regression cases also check that category switches keep cached pages parented, panel/taskbar mode switches reuse monitor row editors, and label-editor toggles retain those editors. Undo comparisons allow the 120 ms input debounce to settle. These cases require a real window; compiling them does not establish that they passed. They were not executed during the 2026-10-30 performance fix because desktop tests were explicitly prohibited.

The follow-up review adds immediate navigation before the dirty debounce, distinct panel/taskbar label and unit edits in cached rows, and hidden-page availability invalidation. These native cases were compiled only. Data-only signature, filtered ordering (326 cases), commit/rebase/cancel isolation and actual isolated file persistence are exercised by `test/发版审查/SettingsDraftChecks.cs` without creating controls.
