# MellowMeter project rules

MellowMeter is a Windows x64 .NET 8 WinForms monitor derived from LiteMonitor. `MellowMeter.csproj`, `src/`, `resources/` and `test/` define the application and checks. Preserve local settings and history, existing monitoring semantics and third-party notices; hardware readings and physical taskbar behavior require real environment evidence.

Use `global.json` and the existing `scripts/dotnet-path.ps1` tool resolution. For relevant changes, `./scripts/build.ps1 -Test` builds and runs the existing regression suites; consult `test/DesktopSmoke/README.md` before opening native test windows. Package with `scripts/package.ps1` only when required, using a fresh output directory. CI is `.github/workflows/build.yml`. Documentation-only work needs proportionate checks.

Distinguish synthetic or offscreen checks from physical UI, performance and hardware validation. Preserve user profiles and unrelated changes. Publishing, pushes, merges, dependency updates, releases and deletion require explicit authorization; never commit secrets, local profiles or generated artifacts.
