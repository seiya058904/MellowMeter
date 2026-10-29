# Interface direction

MellowMeter refines LiteMonitor's colorful, compact Windows monitor. Keep its floating strip, taskbar layout, grouped expanded panel, theme files and hardware/history foundation. The settings surface should feel like part of that small resident utility.

The visual direction is **light cartoon, calm and rounded**, with the simplicity of an iOS grouped settings list. Preserve the original theme's background, group surface and accent. Use readable body text, quieter secondary text, small familiar icons and restrained semantic colors. Avoid broad rectangular dashboard sections, oversized headings, dense two-column forms and coloring every sentence with the accent.

Settings use single-column rows inside softly rounded groups. Small section titles sit outside the group; switches and value controls align on the right. Frequent display settings lead. Low-frequency behavior, placement and customization groups expand in place without discarding their values. Keep Apply/Cancel and the existing draft transaction; changing a view or folding a group must not apply settings.

Reuse native Windows interaction: keyboard focus, Space for switches, Enter/Space for disclosure buttons, native combo menus and caption controls. No idle animation loop or additional UI framework. Theme changes update open settings, history and taskbar values together. On Windows 11 the native caption follows the app palette using [documented DWM attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).

Judge the built WinForms surfaces in light/dark modes, with English and Chinese labels, and at ordinary/high DPI. Renderer captures are useful for layout and color; they do not prove physical monitor transitions or reproduce DWM window chrome.
