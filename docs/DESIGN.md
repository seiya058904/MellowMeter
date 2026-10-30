# Interface direction

Current contract: the 2026-10-30 fluid-settings iteration supersedes the earlier glass experiment and static rollback. Historical decisions remain in `settings-rollback-20261030.md` and `iteration-0.2.0.md`.

MellowMeter is a compact Windows monitoring utility. Preserve its WinForms foundation, floating panel, horizontal strip, taskbar layout, all 32 built-in metrics, 12 themes and existing profiles. New profiles retain DarkFlat Classic. Existing Glass selections still migrate to Mellow Light/Dark.

## Settings: operate mode

Use calm, solid, iOS-inspired grouped lists. Theme colors remain authoritative; the accent marks selection, switches and primary actions. Reuse the existing drawn navigation icons, system fonts, softly rounded groups, quiet dividers and consistent field alignment. Sort actions use drawn chevrons with distinct hover, disabled and keyboard-focus states. Monitor modes use a segmented selection treatment. Keep native caption controls, combo menus, wheel scrolling, keyboard focus and accessible control names.

The five settings categories are created on first visit and retained in one host. Revisiting a page preserves its scroll, disclosure and draft values. Monitor rows and group headers are reused by stable keys; filters and mode changes rearrange existing editors. A changed monitor context invalidates the editor cache. All cached native resources are released on close.

Apply/OK flush pending edits before merging. Cancel discards unapplied changes. Apply compares effective settings once, then batches only necessary runtime updates. It remains directly available and does no periodic dirty-state polling. Theme/language reconstruction restores the visited pages and their view states.

## Feedback and rendering

Button press feedback is immediate; release settles over 120 ms. Switches settle over 160 ms. Bindings change exactly once per action, independently of animation. Use the existing shared UI-thread clock only while tracks exist; interrupt from the current presentation state. Respect `InterfaceAnimationsEnabled`, Windows client-area animation preference and high contrast. The main appearance group exposes the motion switch.

Pages switch directly; disclosure performs one layout change. Do not animate page snapshots, container sizes, scroll offsets or native window positions. No glass, bitmap material cache, software blur or transparency polling. Window chrome and preference changes remain event-driven. Retain composited rendering: the controlled local-buffer experiment did not yield a consistent performance benefit.

History UI clocks stop while hidden/minimized and resume when shown; recording is independent. Preserve sampling, history, alert, calibration and missing-data semantics.

## Delivery and verification

Keep Portable and Lite packages with identical app features. Do not remove runtime DLLs manually, add dependencies, or enable WinForms trimming. Check native light/dark and Chinese/English settings, short windows, keyboard interaction, editing across category/mode/filter switches, Apply/Cancel, theme changes, resource lifetimes and isolated package startup. Synthetic 100/150/200-percent renderer checks do not establish physical mixed-DPI monitor transitions.

Measure the exact shipped assembly against the baseline with the same native message-loop probe. Separate first-use control creation from warm interactions, report P95 as well as median, and disclose remaining latency above the 100 ms target. See `settings-fluid-20261030.md` for results and limitations.
