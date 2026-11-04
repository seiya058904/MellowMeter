# Next iteration brief: flagship iOS-grade settings panel

> **Status (2026-11-04):** implemented and awaiting owner hands-on approval. Results, decisions and known misses are in [settings-ios-20261104.md](settings-ios-20261104.md). The brief below is kept as written.

Audience: the next engineering agent (or person) working in this repository.
Written after the 2026-11-04 polish round, which the owner judged **not yet flagship quality**. Treat that round as a baseline to be raised, not as a finished design.

## 1. Goal and quality bar

Make the settings panel feel like it was tuned by a senior iOS/Apple-platform engineer, on Windows/WinForms: calm, precise, fluid, restrained. The owner explicitly allows a **moderate refactor** of the settings UI (controls, page structure, layout code) in service of quality. It must not become a rewrite: keep WinForms, the five categories, existing settings semantics, profiles, 32 metrics, 12 themes, all languages, and the main floating panel / strip / taskbar rendering untouched unless a measured win requires otherwise.

Quality means, in priority order:

1. **Feel**: every press, drag, scroll, toggle and disclosure responds in the same input event, moves with physical (spring/velocity-continuous) motion, and never stutters.
2. **Look**: coherent iOS-style visual language (spacing, radii, type scale, colour roles, elevation, row types) in every theme, light and dark, every language, 100–200 % DPI.
3. **Performance and footprint**: low latency, low handle/GDI/memory cost, zero work at rest.
4. **Robustness**: keyboard, high contrast, reduced motion, accessibility names, resize, theme/language rebuild, short windows.

Do not claim a result without evidence from the protocols in section 6.

## 2. Read first

- `AGENTS.md` (repo rules: no push/publish/dependency changes without authorization; preserve profiles; separate synthetic from physical validation).
- `docs/DESIGN.md` (interface contract; it was edited this round, and you may revise it again **with evidence and in the same change**), `docs/settings-fluid-20261030.md`, `docs/settings-rollback-20261030.md` (an earlier glass/bitmap experiment was rolled back: understand why before reintroducing snapshots, blur or transparency).
- `test/DesktopSmoke/README.md` before launching native test windows.
- Code: `src/UI/SettingsForm.cs`, `src/UI/Controls/LiteUI.cs` (all custom controls, ~1.3k lines), `InterfaceMotion.cs` (+ `MotionValue`), `MotionSpring.cs`, `src/UI/Settings/*Page.cs`, `SettingsBase.cs`, `SettingsUIBuilder.cs`, `src/UI/Helpers/WindowTheme.cs`.

## 3. Environment facts (save yourself an hour)

- The machine's global `dotnet` is a .NET 7 runtime only. The working SDK is `%LOCALAPPDATA%\MellowMeter\dotnet\dotnet.exe` (8.0.x, matches `global.json`). Set `DOTNET_ROOT` to that folder before `build`/`run`; `scripts/dotnet-path.ps1` does **not** find it when a global dotnet exists.
- To launch the built app by hand: set `DOTNET_ROOT` and `DOTNET_ROOT_X64` to that folder, then start `bin\Release\net8.0-windows\MellowMeter.exe --settings`. Double-clicking without that shows a "install .NET" prompt. `--settings` uses the owner's real profile: say so before launching, and stop only processes you started.
- No Python on the machine. Use PowerShell/Bash and small `Add-Type` helpers. Note: `Add-Type` C# that uses `System.Drawing` fails; keep P/Invoke in C#, do bitmap work in PowerShell.
- Test commands (all passed at the end of the last round): `build MellowMeter.csproj -c Release`; `run --project test/DesktopSmoke/DesktopSmoke.csproj -c Release` (opens native windows; results in `test/DesktopSmoke/bin/Release/net8.0-windows/artifacts/<run>/results.json`, success is `success=true`, passed assertions are in `passed`, `captures` are PNGs); `test/发版审查`, `test/安全修复回归`, `test/Monitoring`.
- DesktopSmoke runs with `InterfaceAnimationsEnabled` mostly **off** in screenshots, so most motion is not visible in captures. Verify motion in the live window (see 6.3).
- The machine is a busy developer workstation (20 logical cores, other Node processes running). Timing noise is large: successive runs of the *same* binary differed by up to 2x. See 6.2.

## 4. State left by the last round (be critical of it)

Done (all in the working tree, **uncommitted** at the time of writing):

- `MotionValue` helper over `InterfaceMotion` for hover/press/selection channels; allocation-free clock tick.
- Nav buttons, buttons, sort buttons, combo face, switch, text-input focus border now fade instead of snapping; buttons sink 3.5 % on press; press shows immediately (synchronous paint to 70 %), releases over 220 ms.
- Switch: thumb shadow and press stretch. Disclosure groups: one 240 ms height transition with rotating chevron.
- Wheel scrolling glides with spring and retained velocity (`BufferedPanel.TryGlide`); closed combos forward the wheel to the page.
- Native vertical scrollbar suppressed on scrolling pages (`ShowScrollBar` + `WS_VSCROLL` check on paint/size) and replaced by `OverlayScrollBar`: thin rounded thumb, fades in while scrolling and out after ~0.9 s, thickens on hover, draggable, click-to-jump. A right padding equal to the native bar width is reserved while content overflows so page geometry is unchanged.
- Row/group/sidebar spacing nudged, bottom hairline, soft rounded focus rings.

Known weaknesses and open problems (your starting backlog):

1. **No page-switch transition**; navigation only cross-fades colours. There is no sliding selection indicator in the sidebar or in segmented controls.
2. **Switch is not draggable** (no velocity-based flick, no tap-vs-drag logic); no thumb overshoot.
3. **Combo boxes are still native ComboBox lists**: the dropdown popup is not themed, has no open/close motion and does not look like an iOS menu/picker. Many options (opacity, scale, width) are better as sliders, segmented controls or steppers.
4. **Monitor page reorders with up/down arrow buttons.** iOS-style drag-to-reorder with a lifted row, spring displacement of neighbours and auto-scroll is the single biggest missing interaction.
5. **No overscroll/rubber-band**, no momentum for scrollbar drag release, no wheel acceleration curve tuning, no horizontal handling.
6. **Typography is Segoe UI 9 pt everywhere**, and `new Font(...)` is allocated per control in many places (lifetime is tied to `ControlResources.CaptureFonts` disposal). There is no type scale (large title, section header, body, caption) and no use of Segoe UI Variable where available.
7. **Clock cadence**: `InterfaceMotion` uses a WinForms `Timer` (WM_TIMER, ~15.6 ms resolution, lowest message priority). Frame pacing under load is unmeasured. Evaluate a better source (e.g. a high-resolution timer posting to the UI thread, `DwmFlush`-paced loop on a worker thread that marshals once per frame, or `CompositionTarget`-like alternatives) **and prove it with frame-time histograms** before adopting it.
8. **Chrome**: native caption bar (themed via DWM attributes) and fixed-size dialog. Decide with evidence whether a unified header (extended client area, custom caption buttons, Mica-free solid surface) is worth its risk (snap layouts, hit testing, DPI, accessibility). Native chrome is acceptable if everything inside is excellent.
9. **Scrollbar implementation is a hack with risks**: it depends on `WM_NCCALCSIZE`/`WS_VSCROLL` suppression, lives as a sibling control in the page, and reserves padding by system metric. Re-check theme/language rebuild, DPI change, nested scroll surfaces, `ScrollControlIntoView` on Tab, and narrow/short windows. Consider replacing the scrolling surface with a purpose-built scroll host if you restructure pages anyway.
10. Focus ring is a bright accent outline after keyboard focus (e.g. after UI Automation Invoke); evaluate subtlety. Sort buttons show a faint 2-level lighter rectangle at some scroll positions (DrawToBitmap/PrintWindow capture of a clipped row); check whether it is real.
11. Other windows (hardware info, traffic history, theme editor, update dialog) still use the old look and native scrollbars. In scope only after the settings window is excellent, and only for shared controls.
12. Test gaps: no automated checks for glide, overlay scrollbar, press timing, sliding indicators, or page transition behaviour; motion tests run with animations on only in one block of `RestoreChecks.cs`.
13. The **100 ms P95 target is only partly met**: first visits to Taskbar/Monitor/System pages cost roughly 80–250 ms, dense mode/filter changes 60–200 ms depending on noise (see `docs/settings-fluid-*` and re-measure).

## 5. Workstreams

You may reorder, but keep each stream shippable and measured. Suggested sequence: 5.1 → 5.2 → 5.3 → 5.4 → 5.5 → 5.6 → 5.7.

### 5.1 Design language and tokens
- Define one small token set in code (spacing scale, radii, type scale, row heights, separator insets, elevation/shadow roles, motion durations/springs, colour roles derived from the theme: surface, grouped surface, fill, separator, label, secondary label, tint). Replace magic numbers in `LiteUI.cs` and pages.
- Row anatomy like iOS grouped lists: leading label, optional footer/caption text under groups, value rows with disclosure chevron, switch rows, segmented rows, slider rows, stepper rows, inset separators that start at the label edge, 44 pt-equivalent minimum hit height, large-title page headers that can collapse on scroll.
- Typography: central font cache with explicit lifetime, a real type scale, Segoe UI Variable Text/Display when present with Segoe UI fallback, consistent text rendering (`TextRenderer` vs GDI+ policy; GDI+ only where transforms require it).
- Verify contrast (existing helpers `UIColors.EnsureContrast`) in all 12 themes; accent must remain theme-driven.

### 5.2 Motion engine
- Spec the springs in iOS terms (response/damping or stiffness/damping/mass) and expose a few named presets (press, hover, toggle, disclosure, indicator, scroll, popup). Keep analytic, timer-independent sampling and velocity continuity.
- Fix frame pacing (see 4.7). Provide an optional debug counter (off by default) that records frame intervals so cadence can be verified without guessing.
- Preserve: respect for `InterfaceAnimationsEnabled`, Windows client-area animation setting and high contrast; zero timers/allocations at rest; tracks stop when owners hide or dispose; bindings change exactly once per action.

### 5.3 Controls and interactions
- **Press/hover**: instant visual response in the same input event, spring release, no visible latency under load; keyboard parity.
- **Switch**: draggable thumb, tap-vs-drag, flick by velocity, thumb stretch, track tint interpolation, disabled/indeterminate states.
- **Segmented control** with a sliding, spring-driven selection indicator; **sidebar** with one shared sliding selection pill.
- **Popup menu/picker** to replace native combo lists where it improves quality: themed, rounded, shadowed, scale/fade from the anchor, keyboard and screen-reader friendly. Native `ComboBox` may remain behind it for accessibility, but its white native popup must not be user-visible. Replace combos with sliders/steppers/segments where the value space allows.
- **Slider and stepper** controls (opacity, scale, width, refresh interval, thresholds) with drag, keyboard, wheel, tooltips-on-drag.
- **Drag-to-reorder** for monitor items (and any order lists): lift with shadow/scale, neighbours spring aside, auto-scroll near edges, drop settle, Esc to cancel, keyboard alternative (keep Move up/down commands accessible). Preserve all existing ordering semantics, filtered-order behaviour and stable-key editor reuse (see `MonitorPage.cs` and the tests around "filtered taskbar reorder").
- **Scrolling**: momentum, rubber-band overscroll, scrollbar thumb inertia, hit-slop for the indicator, wheel with acceleration; keep native paging and zero-line preferences; keep keyboard scrolling and focus-into-view correct.
- **Disclosure**: height transition with content fade, no layout thrash; interruption-safe.
- **Page transitions**: evaluate without bitmap snapshots first (e.g. staged reveal, per-row offset/opacity where cheap, retained-page parenting). A snapshot cross-fade is allowed **only** if measurements show no flicker and no latency regression, and `docs/DESIGN.md` is amended with that evidence. Earlier glass/snapshot work was rolled back; read why.
- **Text inputs**: focus animation, caret/selection colours from theme, validation shake or tint, clear affordances.

### 5.4 Structure and window
- Permitted refactors: introduce a small settings design-system layer (rows, groups, headers) that all five pages use; consolidate duplicated layout code in pages; replace deep `TableLayoutPanel`/nested panel stacks with lighter custom layout where handle/GDI counts or layout time justify it; consider owner-drawn rows for long lists (Monitor page has dozens of rows × several child controls).
- Not permitted without strong evidence: changing Settings serialization, monitor semantics, collection/history behaviour, packaging layout, dependencies, trimming.
- Window: resizable with sensible minimum size and correct layout at every size, safe short-window behaviour (bottom actions always reachable), DPI-change handling, Mica-free solid surface. Decide on chrome per 4.8.
- Keep lazy page creation and view-state restoration (scroll, disclosure, drafts) unless a replacement is demonstrably better and the tests are updated with it.

### 5.5 Performance and resource budgets
Targets (state measured results against these; disclose misses):
- Press/hover/toggle visual response in the same input event; no input-handling work above 8 ms on the UI thread.
- Animation: on an otherwise idle machine, at least 95 % of frame intervals ≤ 20 ms and no more than one consecutive missed frame; degrade gracefully under load (spring sampling by wall clock, never by frame count).
- Warm interactions (page switch, mode, filter, labels, Apply): P95 ≤ 100 ms. First visit to any page: median ≤ 120 ms, P95 ≤ 150 ms. Open settings: median ≤ 120 ms.
- Resources with settings open and idle: zero timers running, zero allocations per second, CPU ≈ 0. After close and a forced collection in the probe, handles/GDI/USER return to within a small, documented margin of the pre-open baseline. Set explicit GDI/USER/handle budgets from the current numbers in `docs/settings-fluid-performance-20261030.json` and beat them.
- Levers to investigate (measure each; keep only what wins): cached fonts/pens/brushes/paths and cached rounded-rect geometry; fewer child windows per row (owner-draw or lightweight controls instead of Panel+Label+Control triples); `SuspendLayout`/batching around page build; avoiding repeated `PerformLayout` during disclosure/scroll; cheaper invalidation (invalidate rectangles, not controls, during animation); reducing full-window composited repaints (`WS_EX_COMPOSITED` experiments previously showed no consistent win, but the cost profile changes with animation frames, so re-test deliberately); optional one-shot idle prewarm of remaining pages (trade: more memory while open and a change to the "pages created on first visit" contract/tests; only with numbers); main-app timers while settings is open (`UIController`, taskbar, history windows) and hidden/minimized behaviour.
- The rest of the app: record idle CPU/memory/handles of the running app with `scripts/Measure-Process.ps1` (long sampling) before and after; do not optimise what you have not measured; do not alter sampling, history, alert, calibration or missing-data semantics.

### 5.6 Accessibility, localisation, robustness
- Keyboard: full traversal, visible but tasteful focus, Space/Enter semantics, arrow keys in segmented/sliders/lists, Esc to cancel drags and popups.
- Accessible names/roles/values for every custom control (including the overlay scrollbar, sliders, steppers, popups, drag handles); UI Automation `Invoke`/`Toggle`/`RangeValue` where natural.
- High contrast mode falls back to system colours and no motion; reduced motion honoured; no information conveyed by colour only.
- Languages: de, ru, fr, es, ja, ko, zh, zh-TW, en. Check text expansion and truncation in every page. RTL layouts must not break.
- DPI 100/125/150/175/200 %, per-monitor changes at runtime (synthetic checks do not prove physical mixed-DPI transitions: say so).
- Theme/language rebuild keeps view state; no leaks of fonts, tracks, timers, overlay controls.

### 5.7 Tests, docs, evidence
- Extend DesktopSmoke/ReleaseReview-style tests for everything you add: motion with animations enabled (tracks start/stop, interruption, final values, no timers at rest), overlay scrollbar (appears, drags, fades, geometry with padding reserve), press timing (synchronous first paint), switch drag, reorder correctness (including filtered orders), popup keyboard behaviour, short-window actions, rebuild on theme/language change, resource release on close.
- Keep tests lean (repo rule: local gates under ~5 minutes); do not add retired web/plugin suites.
- Update `docs/DESIGN.md`, `docs/settings-*` records, screenshots (`docs/screenshots`) and the README only to the extent the shipped behaviour changed. Do not leave stale claims.

## 6. Verification protocols

### 6.1 Gates before any claim of "done"
Build 0 warnings / 0 errors; `test/Monitoring`, `test/DesktopSmoke` (`success=true`), `test/发版审查`, `test/安全修复回归` all passing; final diff inspected; `git status` reviewed so unrelated changes are not swept in.

### 6.2 Performance A/B (mandatory for any performance claim)
- Baseline = a `git worktree` of the parent commit in the scratchpad (never stash; remove the worktree when finished). Run `test/PerformanceProbe` with `-- <out> --interaction` (it references the project directly when `ProbeDirectory` is not set).
- **Interleave** runs (A, B, A, B, A, B), at least three each, and compare medians and P95 per operation. A single pair of runs on this machine is not evidence; last round's first two runs showed a spurious "regression" that disappeared on the third.
- Record machine load alongside (total CPU sample and top processes). Prefer quieter periods; report if you could not get one.
- Allocation deltas and process GDI/USER/handle samples come from the same probe; report them next to latency.
- Timing means action-to-message-loop-idle, not photon latency. Say so.

### 6.3 Live-window verification without hijacking the user's input
- Launch the real app (see section 3), find the window by process, drive it with UI Automation (`InvokePattern`, `FindAll` by name) and `PostMessage` (e.g. `WM_MOUSEWHEEL` to `WindowFromPoint`) instead of moving the physical cursor; capture with `PrintWindow(hwnd, dc, 2)` after setting DPI awareness. Take sequences (during/after animation) to judge motion; consider logging frame intervals through the optional debug counter for cadence evidence.
- Judge feel yourself where you can, but **ask the owner to hands-on test** at milestones and act on specific feedback (which control, which gesture, what felt wrong). The owner has said the quality was not high enough: expect critical feedback and iterate.

### 6.4 Visual QA matrix
Themes: at least MellowLight, MellowDark, DarkFlat_Classic and three accent-heavy themes; languages: zh, en, plus the longest-string language per page; DPI: 100/150/200 % synthetic and the native DPI; pages: all five, both Monitor tabs, collapsed/expanded groups, empty/filtered lists, disabled controls, error states. Review images at full size; zoom into edges (rounded corners, shadows, separators, anti-aliasing, text clipping).

## 7. Constraints and etiquette

- Follow `AGENTS.md` and the owner's global rules: smallest safe change that meets the goal, but this brief authorises a moderate settings-UI refactor; no new dependencies; no lockfile/version changes; no pushes, merges, releases or publishing; commits only when asked; never touch the user's profile except to launch the app they asked to see.
- Keep Portable and Lite editions feature-identical; do not remove runtime DLLs, enable trimming, or change third-party notices.
- Do not use destructive cleanup commands. Remove only temporary things you created, individually.
- Report outcomes faithfully: separate what was measured from what was reasoned, synthetic from physical, and tests passed from tests not run. If something is below the bar, say so and say what you would do next.

## 8. Definition of done for the iteration

- All workstreams in 5.1–5.4 delivered to a visibly higher standard (owner hands-on approval at each milestone), with drag-to-reorder, draggable switches, sliding indicators, themed popup/picker, sliders/steppers where appropriate, refined scrolling, a consistent token-based look, and an honest decision recorded about page transitions and window chrome.
- Performance budgets in 5.5 met or the misses explicitly documented with causes.
- Tests and documentation updated; evidence (A/B tables, frame cadence data, resource samples, screenshots) stored under `docs/` or ignored `artifacts/` as appropriate; the working tree contains only intended changes.
