# Performance and desktop capture probe

Current measurement mode:

```powershell
. ./scripts/dotnet-path.ps1
& $DotnetExecutable publish test/PerformanceProbe/PerformanceProbe.csproj -c Release -p:ProbeDirectory="<absolute win-x64 Lite publish folder>" -o artifacts/probe
& ./artifacts/probe/PerformanceProbe.exe "$PWD/artifacts/probe-results" --interaction
```

The referenced folder must be a Windows x64 publish (`-r win-x64`), with its complete dependencies. The probe uses those assemblies without upgrading packages. `interaction.json` records the actual app assembly hash, DPI, per-action samples, median/P95, managed allocation deltas and process/GDI/USER resource samples. Its isolated profile disables physical advanced sensors, autostart, taskbar and tray; basic sampling continues.

One round warms JIT/framework paths; three measured rounds each open a new settings window. First category visits, first mode/filter/label interactions, warm repeats and changed/unchanged Apply are separate. Action-to-idle includes pending native paint work; it is **not** physical input-to-photon latency. Allocation deltas include background process activity. CPU samples are brief diagnostics, not a long-run power benchmark. No forced GC is used. `--local-buffer` removes composited styles from this probe's settings controls only for the controlled comparison, without changing production settings.

`--cadence` runs scripted wheel, switch, disclosure and page-switch interactions for each clock source and composition style, three passes each, and writes `cadence.json`: UI-thread frame intervals, tick cost including the forced paint, CPU, and the time spent inside single input messages. Motion is forced on for this process. It does not measure photons.

`--qa` captures the visual matrix (themes, languages, pages, expanded groups, menu, slider bubble, mid-drag, mid-transition, focus, disabled, invalid) from the real window on the isolated profile. `--qa-only scale|contrast` narrows it, and `--qa-scale 1.0|1.5|2.0` renders at a synthetic scale on the host monitor, which checks layout and glyph sizes but not physical DPI transitions. It puts a topmost window on screen while it runs.

For A/B runs, set `MELLOWMETER_FORCE_MOTION=1` to measure with motion on even when Windows animation effects are off. A baseline older than this switch needs the same one-line change in its own worktree.

`--interactive` leaves an isolated settings window open for native input verification and closes the host when settings close. Existing `--motion` and `--preview` modes are diagnostic/historical tooling; their old glass-oriented artifact filenames are not evidence of active glass support. Do not use the off/on motion sequence as a controlled speedup comparison because its first pass creates pages and the second reuses them.

The original mode (artifact directory plus optional theme name) measures 30 settings openings, chart hover and bitmap-renderer cost. `DrawToBitmap` never measures DWM. `scripts/Measure-Process.ps1` offers longer resident sampling; missing DWM/GPU readings must not be interpreted as zero cost. All outputs belong in ignored artifact directories, not the user's profile.
