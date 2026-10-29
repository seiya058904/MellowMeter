# Third-party notices

## LiteMonitor

MellowMeter is derived from [Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor), commit `3b91b5ca6d70a9056b8fdd01c9acddc985721240` (v1.3.7).

Copyright Diorser and LiteMonitor contributors. Both upstream README files at that commit declare the MIT License; the source snapshot did not contain a standalone LICENSE file. MellowMeter includes the standard MIT terms in `LICENSE`, retains original authorship/history, and adds its own contributor notice. The original app icon and legacy resources are retained from that project. Upstream screenshots are identified separately from new captures.

## NuGet dependencies

Versions and license declarations below were read from the restored package metadata. These packages are not relicensed by MellowMeter.

| Package | Version | License / source |
|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | [MPL-2.0; source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| BlackSharp.Core | 1.0.7 | [MPL-2.0; source](https://github.com/Blacktempel/BlackSharp) |
| DiskInfoToolkit | 1.1.2 | [MPL-2.0; source](https://github.com/Blacktempel/DiskInfoToolkit) |
| RAMSPDToolkit-NDD | 1.4.2 | [MPL-2.0; source](https://github.com/Blacktempel/RAMSPDToolkit) |
| HidSharp | 2.6.4 | Apache-2.0; copyright 2010–2025 James F. Bellinger; packaged notice in [docs/licenses/HidSharp.txt](docs/licenses/HidSharp.txt) |
| Mono.Posix.NETStandard | 1.0.0 | [Mono source and license notices](https://github.com/mono/mono) |
| System.Text.Json, System.Text.Encodings.Web, System.IO.Pipelines | 9.0.10 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |
| System.CodeDom, System.Management | 10.0.2 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |
| System.IO.Ports, System.Threading.AccessControl and native IO.Ports runtime assets | 10.0.3 | [MIT; .NET runtime source](https://github.com/dotnet/runtime) |

MPL dependency source remains available at the links above; MellowMeter does not modify those dependency binaries. Complete dependency resolution can be inspected with `dotnet list MellowMeter.csproj package --include-transitive`.

Legacy driver installers, FPS and updater assets in the inherited source tree are not copied into the MellowMeter build by this project. No third-party driver is installed automatically. Their respective authors' terms apply if distributing or using them separately.
