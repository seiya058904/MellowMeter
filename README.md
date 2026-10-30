<p align="center">
  <a href="https://github.com/seiya058904/MellowMeter/releases/latest">
    <img src="assets/mellowmeter-readme-cover.svg" alt="MellowMeter — Your desktop, softly in focus. A cream-and-sage illustration of a compact dark monitoring panel; artwork, not a screenshot." width="100%" />
  </a>
</p>

<h1 align="center">MellowMeter</h1>

<p align="center">
  <strong>A little quieter. A lot more aware.</strong><br />
  A small native Windows monitor for the things worth keeping an eye on — without taking over your desktop.
</p>

<p align="center">
  <a href="https://github.com/seiya058904/MellowMeter/releases/tag/v0.1.0"><img src="https://img.shields.io/badge/release-v0.1.0-4F9B6B?style=flat-square" alt="Release v0.1.0" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-536E62?style=flat-square" alt="Windows 10 and 11" />
  <img src="https://img.shields.io/badge/native-WinForms%20%2B%20.NET%208-77749A?style=flat-square" alt="Native WinForms and .NET 8" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-B78D50?style=flat-square" alt="MIT license" /></a>
</p>

<p align="center">
  <a href="https://github.com/seiya058904/MellowMeter/releases/latest"><strong>↓ Download for Windows</strong></a>
  &nbsp;·&nbsp; <a href="#preview">Screenshots</a>
  &nbsp;·&nbsp; <a href="#features">Features</a>
  &nbsp;·&nbsp; <a href="#quick-start">Get started</a>
  &nbsp;·&nbsp; <a href="#development">For developers</a>
</p>

<p align="center"><sub>Portable ZIP · Runtime included · Basic monitoring without a hardware driver</sub></p>
<p align="center"><sub>English · <a href="README.zh-CN.md">简体中文</a> · <a href="https://github.com/seiya058904/MellowMeter/issues">Issues &amp; feedback</a></sub></p>

---

MellowMeter builds on **[LiteMonitor](https://github.com/Diorser/LiteMonitor)**, retaining its compact floating monitor, taskbar layout, colorful readings, and familiar themes. It adds a calmer default setup, rounded settings, and coordinated light/dark styling — without trying to become a full dashboard.

**Local v0.2 iteration:** Glass Light / Glass Dark bring the theme count to **14**, with an optional Windows 11 Mica sidebar, lazy settings pages, faster history interaction and a runtime-free Lite package. Existing profiles keep their theme. See the [iteration report](docs/iteration-0.2.0.md) for actual desktop captures, measurements and compatibility limits. The public download above remains v0.1.0 until a new release is authorized.

<a id="features"></a>

## 🌿 A small space for the essentials

<table>
<tr>
<td width="50%" valign="top">
<strong>📊 Four readings. One glance.</strong><br /><br />
CPU busy time, memory load, upload speed, and download speed appear by default. Everything important stays visible without a maze of widgets.
</td>
<td width="50%" valign="top">
<strong>🪟 Right where you need it.</strong><br /><br />
Keep a floating desktop panel or use the taskbar strip. Expand the floating strip when you want to see a little more.
</td>
</tr>
<tr>
<td valign="top">
<strong>🎨 Your colors, your pace.</strong><br /><br />
Soft rounded settings, pill switches, collapsible groups, and coordinated themes sit alongside the original DarkFlat Classic look.
</td>
<td valign="top">
<strong>🗂️ History that stays yours.</strong><br /><br />
Review local monitoring history and export hardware trends as CSV. Advanced hardware sensors are optional, not a requirement for basic use.
</td>
</tr>
</table>

<a id="preview"></a>

## 🖼️ A closer look

<p align="center">
  <img src="docs/screenshots/panel-dark.png" alt="MellowMeter's compact dark floating panel with its default CPU, memory, upload, and download readings" width="82%" />
</p>
<p align="center"><sub>01 / The floating panel — the four default readings, without extra clutter</sub></p>

<table>
<tr>
<td width="50%" align="center" valign="top">
  <img src="docs/screenshots/settings-dark.png" alt="MellowMeter rounded settings in a dark theme" width="100%" /><br />
  <sub>02 / After dark</sub>
</td>
<td width="50%" align="center" valign="top">
  <img src="docs/screenshots/settings-light.png" alt="MellowMeter rounded settings in a light theme" width="100%" /><br />
  <sub>03 / In the daylight</sub>
</td>
</tr>
</table>

<details>
<summary><strong>✨ More looks — expanded panel, themes &amp; history</strong></summary>

<p align="center">
  <img src="docs/screenshots/expanded-dark.png" alt="Expanded dark monitor panel" width="82%" />
</p>
<p align="center"><sub>Expanded floating panel</sub></p>

<table>
<tr>
<td width="50%" valign="top" align="center">
  <img src="docs/screenshots/settings-purple.png" alt="Settings in AuroraDream Purple" width="100%" /><br />
  <sub>AuroraDream Purple</sub>
</td>
<td width="50%" valign="top" align="center">
  <img src="docs/screenshots/history-dark.png" alt="Local monitoring history in the dark theme" width="100%" /><br />
  <sub>Local history</sub>
</td>
</tr>
</table>

Browse the [screenshot collection](docs/screenshots/) for more theme and taskbar captures.

</details>

<sub><strong>About these images:</strong> They come from the actual WinForms renderer. Some use seeded example readings and <code>DrawToBitmap</code>; they are not physical-desktop screenshots or performance measurements. The illustrated cover above is concept art. See the <a href="docs/VERIFICATION.md">verification record</a> for the exact capture conditions.</sub>

<a id="quick-start"></a>

## 📦 Download. Extract. Relax.

| Download | Details |
| :--- | :--- |
| **[MellowMeter v0.1.0 · Windows x64 portable ZIP](https://github.com/seiya058904/MellowMeter/releases/download/v0.1.0/MellowMeter-0.1.0-win-x64.zip)** | **70.6 MiB**, includes the .NET 8 runtime. No separate SDK required. |
| [SHA256SUMS.txt](https://github.com/seiya058904/MellowMeter/releases/download/v0.1.0/SHA256SUMS.txt) | Compare the archive's SHA-256 hash with the published checksum. |
| [Release notes](https://github.com/seiya058904/MellowMeter/releases/tag/v0.1.0) | Changes, downloads, and release verification. |

**Three steps to start:**

1. Download the portable ZIP and **extract the entire folder** to a location of your choice.
2. Open the folder and run **`MellowMeter.exe`**.
3. **Right-click** the panel or taskbar strip for display options, settings, and history. **Double-click** the floating strip to expand it.

The application targets **Windows 10/11 x64**; the recorded physical host test was on **Windows 11 x64**. Basic CPU, memory, and network monitoring needs neither administrator access nor an additional hardware driver. The EXE is currently **unsigned**, so Windows may display its normal reputation warning. Keep the extracted package together when running the app.

<a id="how-it-works"></a>

## 🎛️ Make it feel like your desktop

**The familiar layout, with gentler controls.** MellowMeter preserves the original floating/taskbar views, colorful values, circular progress indicators, and DarkFlat Classic theme. Rounded groups, disclosure sections, and pill switches make it easier to adjust the essentials. Theme changes apply across open settings and history windows.

| What | How it behaves |
| :--- | :--- |
| **Default metrics** | CPU busy time, memory load, network upload, and network download. |
| **Advanced sensors** | Optional GPU, temperature, disk, clock, power, and fan readings. Enable in **Settings → System**, apply, and restart. Hardware, permissions, and compatible drivers determine availability. |
| **Unavailable readings** | Shown as **—** rather than guessed or silently replaced. The app does not install hardware drivers. |
| **Network rates** | Automatic selection uses the busiest eligible adapter, not a sum across all interfaces; local and SMB traffic may be included. |
| **History** | Stored on your PC, with CSV export for hardware trends. Monitoring data is not uploaded. |

<details>
<summary><strong>What is deliberately outside the normal v0.1.0 experience?</strong></summary>

Plugins, FPS tools, web serving, automatic updating, and speed tests are not part of this release's normal interface or startup path. CPU reports processor <em>busy time</em>, which is not the same as frequency-weighted CPU utility. For hosts with more than 64 logical processors, the collector has a [documented processor-group limitation](docs/VERIFICATION.md#measurement-limits).

</details>

<a id="development"></a>

## 🛠️ Build it yourself

**C# · Windows Forms · .NET 8 · PowerShell**

Development requires Windows 10/11 x64 and the .NET 8 SDK. Close the running application before rebuilding.

```powershell
# Optional: set up a user-local .NET SDK
.\scripts\setup.ps1

# Build and run
.\scripts\run.ps1

# Open settings directly
.\scripts\run.ps1 -NoBuild -Settings

# Build and run regression checks
.\scripts\build.ps1 -Test

# Build the portable ZIP and its SHA-256 file (PowerShell 7.4+)
.\scripts\package.ps1

# Or build both Portable and Lite; Lite requires .NET 8 Desktop Runtime x64
.\scripts\package.ps1 -Mode Both -OutputDirectory artifacts/release-both
```

<details>
<summary><strong>Build outputs, app data &amp; implementation notes</strong></summary>

Packaging writes to `artifacts/release` and refuses to overwrite an existing directory. Use `-OutputDirectory artifacts/another-release` for a later build. Runtime licenses and third-party notices are included in the package. Settings and history are stored under `%LOCALAPPDATA%/MellowMeter`.

Further reading: [design direction](docs/DESIGN.md) · [verification and limitations](docs/VERIFICATION.md) · [third-party notices](THIRD_PARTY_NOTICES.md).

</details>

## 🧪 Tested — with the edges clearly marked

The published v0.1.0 Windows 11 x64 check passed **371 desktop assertions** and produced **37 renderer captures**, with all **six CI suites** passing at that release. The v0.2 source uses four suites after retiring only tests for removed services; current local results and real desktop captures are recorded in the [v0.2 report](docs/iteration-0.2.0.md). No v0.2 remote CI or public release is claimed by local verification.

| Covered | Not established by these checks |
| :--- | :--- |
| Windows 11 host checks, render/interaction regressions, and portable package smoke test | Universal Windows 10 and hardware compatibility |
| Synthetic renderer scaling at 100%, 150%, and 200%; host captures at 175% | Physical DPI switching, mixed-DPI monitors, and HDR-like display differences |
| Simulated lock/suspend/hidden-state transitions | Physical sleep/wake, Explorer restart, and varied sensor/driver environments |
| One brief comparison with a modified LiteMonitor baseline | General performance gains or long-term stability across machines |

Read the [full verification record](docs/VERIFICATION.md) for methods, limits, and the benchmark context.

## 🌱 Made from a good idea

MellowMeter is an **independent derivative** of [Diorser's LiteMonitor](https://github.com/Diorser/LiteMonitor), based on upstream commit [`3b91b5ca`](https://github.com/Diorser/LiteMonitor/commit/3b91b5ca6d70a9056b8fdd01c9acddc985721240). Original project history, namespaces, themes, and relevant resources are preserved. It is **not an official LiteMonitor release**.

Licensed under **[MIT](LICENSE)**. Original attribution and bundled dependency terms are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

<p align="center">
  <strong>Keep an eye on what matters. Let the rest stay mellow.</strong><br />
  <a href="https://github.com/seiya058904/MellowMeter/releases/latest">Download MellowMeter</a>
  &nbsp;·&nbsp; <a href="#mellowmeter">Back to top ↑</a>
</p>
