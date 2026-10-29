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

The STA harness uses the real WinForms message loop, windows, settings transaction, persistence and renderer. It checks missing/non-finite values, same-value recovery and real zero progress; four default metrics and the original default theme/font; target-DPI bounds and text height; Cancel/Apply, live theme/language changes and preserved orientation; and history persistence. It also invokes lock/suspend/hidden states, sends a simulated TaskbarCreated message, and injects a pending sample to verify asynchronous shutdown drains it before saving paused history. Existing history is loaded by actual startup while recording is disabled. Reflection reaches the integration points inside this isolated test process; it does not control other applications.

Synthetic DPI renderings use pixel fonts converted from the theme's point sizes at the target DPI, and pass the measurement Graphics into the actual layout builder. This is necessary because bitmap DPI metadata alone does not change the desktop DPI of the GDI HDC used by TextRenderer. It keeps glyph size and layout scale consistent without relaxing the text-fit assertions; native forms retain their ordinary point fonts.

`results.json` records passed assertions, failures, native DPI and capture paths. `render_*` images exercise the actual renderer at 100%, 150% and 200%; native `main_*`, `settings_*` and `history_*` form captures use the current Windows DPI. These do not replace a live multi-monitor DPI or accessibility audit. A 60-second watchdog exits with code 124 and records the stuck stage; assertion failures return 1 and success returns 0.
