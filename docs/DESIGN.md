# Interface direction

Current contract: the 2026-11-04 settings design system (`settings-ios-20261104.md` holds the evidence) supersedes the 2026-10-30 fluid-settings iteration, the earlier glass experiment and the static rollback. Historical decisions remain in `settings-fluid-20261030.md`, `settings-rollback-20261030.md` and `iteration-0.2.0.md`.

MellowMeter is a compact Windows monitoring utility. Preserve its WinForms foundation, floating panel, horizontal strip, taskbar layout, all 32 built-in metrics, 12 themes and existing profiles. New profiles retain DarkFlat Classic. Existing Glass selections still migrate to Mellow Light/Dark.

## Settings: design language

Calm, solid, iOS-grouped lists. `src/UI/Controls/DesignSystem.cs` is the single source of the tokens below; pages compose them through `SettingsUIBuilder` and do not hard-code geometry.

- **Colour roles** derive from the active theme (`UIColors`): page, card, fill, separator, label, secondary and tertiary label, tint. The accent marks selection, switches and primary actions. High contrast maps every role to system colours and adds outlines.
- **Type scale** (`UiFonts`): large title, title, headline, body, footnote, caption. Segoe UI Variable where present, Segoe UI otherwise. CJK languages use the Text cut for titles, because GDI font linking sends the Display semibold face to a serif fallback. Fonts are shared pixel fonts per scale with process lifetime; controls never dispose them.
- **Rows**: 44 logical pixels minimum, leading title drawn by the row, one trailing control, inset separators, explanatory text as a footer under the card. Choice rows use the control that fits the value space: segmented for two exclusive options, a slider for ordered stops, a stepper for free numbers, a picker menu for long lists.
- **Shapes**: continuous-corner rounding (`Ink.Round`), soft keyboard rings shown only with keyboard cues.
- **Page chrome**: a large title that scrolls away and collapses into a compact bar. The Monitor page pins its title because the mode control beneath it must stay reachable.

The window is resizable with a minimum size, and the action bar is always reachable. Native caption and frame are kept: the caption is already colour-matched through DWM, and a custom frame would put snap layouts, hit testing and accessibility at risk for no visible gain.

## Interaction

- **Press**: the pressed frame is painted inside the input event, then a spring releases it.
- **Switch**: tap toggles; the thumb can be dragged or flicked, stretches while held, and bindings change once per action.
- **Selector** (`LiteSelector`): one window drawing the sidebar or a segmented control with one spring-driven pill. Items are exposed as accessible children.
- **Picker** (`LiteComboBox`, `LitePopupMenu`): replaces the native combo and its unthemed list. Themed floating menu that scales and fades from its anchor, never takes activation, supports keyboard, type-ahead, wheel and press-drag-release.
- **Slider and stepper**: stops are exactly the values the setting accepts; a profile value between stops is shown and left untouched until the user acts. A drag commits once on release.
- **Reorder** (`ReorderSession`): drag handle lifts the row into a floating sprite, neighbours spring aside, the list auto-scrolls near its edges, Esc cancels. A drop performs the same child-order change as the Move up / Move down commands, which remain the keyboard path, so saving and filtered-order behaviour are unchanged.
- **Scrolling** (`ScrollSurface`): one content window is moved, so a frame costs one window move however many rows exist. Wheel notches glide and accelerate, edges rubber-band, the overlay indicator fades at rest, thickens under the pointer, drags with momentum on release and has a generous hit area. Keyboard paging and focus-into-view work; the user's lines-per-notch and page mode are respected.
- **Disclosure**: one spring on the body height with a rotating chevron, rows revealed by clipping and a dissolving veil, then normal layout at rest.

## Motion and rendering

Springs are specified in iOS terms (`SpringSpec`: response, damping) and sampled analytically by wall clock, so retargeting keeps position and velocity and a slow frame never changes the motion's timing. Frames are paced to display composition: a worker blocks in `DwmFlush` and posts one tick at a time to the UI thread, which drops rather than queues under load. No thread wakes, no timer runs and nothing allocates at rest; the worker exits after a short idle period.

`WS_EX_COMPOSITED` is gone from the settings window. It was retained in the last iteration because it showed no consistent benefit for static interactions, but with animation frames it costs roughly a factor of six per scroll frame. Page swaps are atomic through `WM_SETREDRAW`, and controls paint opaque local surfaces, so no flicker is expected from removing it.

Page transitions: the swap happens synchronously and the new page is interactive at once. A page-coloured top-level veil dissolves over it while the content settles a few pixels. No page snapshot, blur or transparency polling is used; the veil is one layered-window opacity change per frame. Layered child windows are unavailable because the manifest does not declare Windows 8 compatibility, and changing that would affect the whole application.

Motion respects `InterfaceAnimationsEnabled`, the Windows client-area animation preference and high contrast. **When Windows has animation effects off, all of this motion is off and Appearance says so.** `MELLOWMETER_FORCE_MOTION=1` enables motion for one process without changing the machine preference, for tests and hands-on comparison.

Settings structure: five categories created on first visit and retained in one host. Revisiting preserves scroll, disclosure and drafts. Monitor rows and headers are reused by stable keys; a changed monitor context invalidates the cache. Theme, language and DPI changes rebuild the shell and restore view state without transitions. Apply/OK flush pending edits before merging; Cancel discards them; Apply compares effective settings once and batches only necessary runtime updates. All cached native resources are released on close.

History UI clocks stop while hidden/minimized and resume when shown; recording is independent. Preserve sampling, history, alert, calibration and missing-data semantics.

## Delivery and verification

Keep Portable and Lite packages with identical app features. Do not remove runtime DLLs manually, add dependencies, or enable WinForms trimming. Run `scripts/build.ps1 -Test`; DesktopSmoke forces motion on for its motion blocks because a host with animation effects off would otherwise skip every spring. Synthetic 100/150/200-percent checks do not establish physical mixed-DPI monitor transitions.

Measure with interleaved runs of the same probe against a worktree baseline, compare medians and P95 per operation, record machine load, and separate first-use construction from warm interactions. Timing is action to message-loop idle, not photon latency. `PerformanceProbe --cadence` records frame intervals and input-handling cost; `--qa` captures the visual matrix.
