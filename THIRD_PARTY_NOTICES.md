# Third-party notices

## LiteMonitor

MellowMeter is derived from [Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor), commit `3b91b5ca6d70a9056b8fdd01c9acddc985721240` (v1.3.7).

Copyright Diorser and LiteMonitor contributors. Both upstream README files at that commit declare the MIT License; the source snapshot did not contain a standalone LICENSE file. MellowMeter includes the standard MIT terms in `LICENSE`, retains original authorship/history, and adds its own contributor notice. The original app icon and legacy resources are retained from that project. Upstream screenshots are identified separately from new captures.

## NuGet dependencies

Versions and license declarations below were read from the restored package metadata. These packages are not relicensed by MellowMeter.

| Package | Version | License / source |
|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | [MPL-2.0; source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701) |
| BlackSharp.Core | 1.0.7 | [MPL-2.0; source](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0) |
| DiskInfoToolkit | 1.1.2 | [MPL-2.0; source](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea) |
| RAMSPDToolkit-NDD | 1.4.2 | [MPL-2.0; source](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce) |
| HidSharp | 2.6.4 | Apache-2.0; copyright 2010–2025 James F. Bellinger; packaged notice in [docs/licenses/HidSharp.txt](docs/licenses/HidSharp.txt) |
| Mono.Posix.NETStandard | 1.0.0 | [Mono source](https://github.com/mono/mono); [license notice](docs/licenses/Mono.txt) from the package's license URL |
| System.Text.Json, System.Text.Encodings.Web, System.IO.Pipelines | 9.0.10 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |
| System.CodeDom, System.Management | 10.0.2 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |
| System.IO.Ports, System.Threading.AccessControl and native IO.Ports runtime assets | 10.0.3 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |

MPL dependency source remains available at the exact source commits declared in the package metadata above; MellowMeter does not modify those dependency binaries. The [full MPL-2.0 text](docs/licenses/MPL-2.0.txt) is retained from LibreHardwareMonitor's corresponding source. Complete dependency resolution can be inspected with `dotnet list MellowMeter.csproj package --include-transitive`.

## Bundled .NET runtime

The Windows x64 portable ZIP includes the Microsoft .NET 8 and Windows Desktop runtimes. `scripts/package.ps1` reads their actual versions from the published runtime configuration and copies the matching runtime packs' licenses and third-party notices into `docs/licenses/` inside the ZIP. These runtime components and the Microsoft NuGet libraries retain their respective MIT and third-party terms. Runtime source is available from [dotnet/runtime](https://github.com/dotnet/runtime); Windows Desktop source is in [dotnet/winforms](https://github.com/dotnet/winforms) and [dotnet/wpf](https://github.com/dotnet/wpf).

Legacy driver installers, FPS and updater assets in the inherited source tree are not copied into the MellowMeter build by this project. No third-party driver is installed automatically. Their respective authors' terms apply if distributing or using them separately.
