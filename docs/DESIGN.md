# Interface direction

MellowMeter refines LiteMonitor's colorful, compact Windows monitor. Keep its floating strip, taskbar layout, grouped expanded panel, theme files and hardware/history foundation. The settings surface should feel like part of that small resident utility.

The visual direction is **calm iOS-inspired glass**, with the simplicity of a grouped settings list. Glass Light uses mist-white surfaces and graphite text; Glass Dark uses charcoal and soft white. Blue is the action accent, with green/orange/red reserved for metric status. Preserve the twelve existing palettes as selectable alternatives. New profiles use Glass Light; migration never changes an existing theme.

Settings use single-column rows inside softly rounded groups. Small section titles sit outside the group; switches and value controls align on the right. Frequent display settings lead. Low-frequency behavior, placement and customization groups expand in place without discarding their values. Keep Apply/Cancel and the existing draft transaction; changing a view or folding a group must not apply settings.

Reuse native Windows interaction: keyboard focus, Space for switches, Enter/Space for disclosure buttons, native combo menus and caption controls. Use system fonts and locally drawn line icons. No idle animation loop, bundled font, additional UI framework, desktop capture loop or software blur. Theme changes update open settings, history and taskbar values together. Pages are created on first visit and preserve their draft, scroll and disclosure states. Apply is disabled when the effective draft equals the saved state, including after a reverted edit.

## Material contract

Theme JSON accepts optional `material: "glass"` and `color.accent` fields. Missing material means `solid`, so older themes retain their behavior. The app's Glass effects switch changes material, never geometry or data contrast. Windows transparency disabled, high contrast, battery saver or an explicit app opt-out disables effects; system changes never overwrite the user's switch.

The settings sidebar uses `DWMSBT_MAINWINDOW` on Windows 11 build 22621+. Only the sidebar extends the DWM frame, with alpha-preserving GDI+ text. Opaque data controls remain outside that composition area. Mica is a wallpaper-derived Windows material; it is not live blur of another window. The floating strip and Explorer taskbar strip retain their layered-window contract, using cached gradients/highlight edges instead of native Mica/Acrylic. Older Windows versions use the same drawn material, or a solid surface when effects are disabled. No transient surface currently requires an Acrylic backdrop. These choices preserve click-through, opacity and taskbar integration; they do not reproduce Apple's full optical effects.

Material bitmaps are bounded by count and memory, and invalidated by theme/policy changes; size and DPI are part of their pixel dimensions. Data regions prioritize stable contrast. History uses solid chart/table surfaces, quiet grids and pill range controls. Hidden/minimized windows should not keep refreshing their UI, while recording continues independently.

References: [Apple materials](https://developer.apple.com/design/human-interface-guidelines/materials), [Microsoft system backdrop types and minimum Windows build](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type). Packaging keeps `PublishTrimmed=false` because of [WinForms trimming limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities).

Judge the built WinForms surfaces in light/dark modes, with English and Chinese labels, and at ordinary/high DPI. Renderer captures are useful for layout and color; they do not prove physical monitor transitions or reproduce DWM window chrome.
