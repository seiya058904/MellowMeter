# Performance and desktop capture probe

This Windows-only harness uses isolated data and the application's real forms. Run it only on an interactive desktop where showing test windows is appropriate. It does not control another app's input. `--preview` briefly shows owned bright/dark/patterned background windows, app windows and the app's taskbar strip, captures desktop pixels, then closes them. It never changes Windows transparency or wallpaper itself.

```powershell
. ./scripts/dotnet-path.ps1
& $DotnetExecutable publish test/PerformanceProbe/PerformanceProbe.csproj -c Release -o artifacts/probe
& ./artifacts/probe/PerformanceProbe.exe "$PWD/artifacts/probe-results" DarkFlat_Classic
& ./artifacts/probe/PerformanceProbe.exe "$PWD/artifacts/preview-results" --preview
```

To compare a saved old build or the exact packaged assembly, supply `-p:ProbeDirectory=<absolute published folder>` when publishing into a new output folder. The reference uses that build and its existing JSON dependencies, not updated packages. Add `--baseline` to `--preview` when testing v0.1's classic/light themes.

The microbenchmark records 30 settings openings after one warmup, three 1,000-move batches after one warmup, three batches of 50 chart paints and GDI/USER counts. The chart contains 10,000 generated points. `DrawToBitmap` measures chart-renderer cost; it does not measure DWM. GDI counts can rise between natural GC cycles, so first/last counts alone do not prove a leak. No forced GC is used.

Use `scripts/Measure-Process.ps1` separately for resident sampling. It captures startup, process CPU, private/working-set bytes, handles and GDI/USER objects, plus GPU-engine and raw DWM performance-counter samples. A null/unavailable direct DWM CPU reading must never be interpreted as zero cost. DWM/GPU samples include ambient desktop activity. Results and captures belong in ignored `artifacts` folders, not the user's profile.
