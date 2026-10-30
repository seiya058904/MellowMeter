<div align="center">

# MellowMeter

**安静、紧凑的 Windows 桌面与任务栏监控工具。**

[![Release](https://img.shields.io/badge/release-v0.2.0-36a66a?style=flat-square)](https://github.com/seiya058904/MellowMeter/releases/tag/v0.2.0)
[![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-7957c8?style=flat-square)](#下载)
[![License](https://img.shields.io/badge/license-MIT-36a66a?style=flat-square)](LICENSE)

[下载](#下载) · [预览](#界面预览) · [使用](#快速开始) · [开发](#开发)

[English](README.md) · 简体中文

</div>

MellowMeter 是基于 [Diorser 的 LiteMonitor](https://github.com/Diorser/LiteMonitor) 独立衍生的项目。它保留原有 WinForms 监控程序、悬浮面板与任务栏布局，并提供按需创建并复用页面的设置面板、圆角设置界面和联动主题。

## 一览

<table>
<tr>
<td width="50%"><strong>查看常用指标</strong><br>保留 32 项内置监控能力；默认竖向面板选择 13 项，任务栏选择 6 项。</td>
<td width="50%"><strong>保留原有风格</strong><br>保留悬浮与任务栏布局、DarkFlat Classic 主题、彩色数值和圆形进度指示。</td>
</tr>
<tr>
<td><strong>按需调整</strong><br>实色圆角分组、统一控件和可关闭的轻动效；复用编辑器，并减少应用设置时的重复刷新。</td>
<td><strong>数据由你掌控</strong><br>基础监控使用 Windows API。新配置默认启用高级传感器，可在设置中关闭；历史记录保存在本地，硬件趋势可导出 CSV。</td>
</tr>
</table>

## 界面预览

<div align="center">

<img src="docs/screenshots/fluid/panel-dark.png" alt="MellowMeter 桌面面板" width="300" />

*竖向悬浮面板；缺失的高级传感器读数显示为横线。*

![MellowMeter 深色设置界面](docs/screenshots/fluid/settings-dark.png)

*设置界面跟随当前主题。更多渲染器截图见 [`docs/screenshots`](docs/screenshots/)。*

![MellowMeter 浅色设置界面](docs/screenshots/fluid/settings-light.png)

</div>

以上图片来自真实 WinForms 渲染器。部分图片使用预置示例读数并通过 `DrawToBitmap` 捕获；它们不是物理桌面截图，也不是性能测试结果。

## 快速开始

1. 下载 ZIP 并解压完整文件夹。
2. 打开解压后的文件夹，运行 **MellowMeter.exe**。
3. 右键面板或任务栏条可打开显示控制、历史记录和设置；双击悬浮条可展开面板。

便携 ZIP 已包含 .NET 8 运行时。基础监控无需安装 SDK、管理员权限或额外驱动。运行时请保留解压出的完整文件夹。当前 Windows 程序尚未签名，Windows 可能会显示常见的下载安全提示。

## 下载

| 文件 | 说明 |
| --- | --- |
| [MellowMeter 0.2.0 · Windows x64](https://github.com/seiya058904/MellowMeter/releases/download/v0.2.0/MellowMeter-0.2.0-win-x64.zip) | 约 70.6 MiB，含 .NET 8 运行时的便携 ZIP；运行前请解压完整文件夹。 |
| [MellowMeter 0.2.0 Lite · Windows x64](https://github.com/seiya058904/MellowMeter/releases/download/v0.2.0/MellowMeter-0.2.0-win-x64-lite.zip) | 约 2.73 MiB，功能相同，需要安装 .NET 8 Desktop Runtime x64。 |
| [SHA256SUMS.txt](https://github.com/seiya058904/MellowMeter/releases/download/v0.2.0/SHA256SUMS.txt) | 用于校验发布压缩包的 SHA-256 校验值。 |

发布说明和文件见 [v0.2.0 Release 页面](https://github.com/seiya058904/MellowMeter/releases/tag/v0.2.0)。

<details>
<summary>高级监控与数据</summary>

- 新配置默认启用高级传感器，可在 **设置 → 硬件与系统** 中关闭；更改后应用并重启。是否可用取决于硬件、权限及兼容驱动。驱动及 FPS 组件仅在你主动点击对应安装按钮时安装。
- 缺失读数显示为 **—**。基础 CPU、内存和网络监控无需硬件驱动。
- 历史记录和 CSV 导出保存在本机，不会上传监控数据。
- CPU 显示处理器忙碌时间。网络自动模式选择当前流量最大的合格网卡；速率包含本地及 SMB 流量。
- 插件、Web 服务、自动更新器和测速已停用；FPS 监控需要手动安装可选组件。

</details>

<a id="开发"></a>
<details>
<summary>开发</summary>

MellowMeter 使用 C#、.NET 8 和 Windows Forms。开发需要 Windows 10/11 x64 与 .NET 8 SDK。重新构建前请先关闭正在运行的程序。

```powershell
# 可选：安装到当前用户目录的 .NET SDK
.\scripts\setup.ps1

# 构建并运行
.\scripts\run.ps1

# 直接打开设置
.\scripts\run.ps1 -NoBuild -Settings

# 构建并运行回归检查
.\scripts\build.ps1 -Test

# 打包便携 ZIP 和 SHA-256 校验文件（PowerShell 7.4+）
.\scripts\package.ps1
```

发布文件写入 `artifacts/release`，脚本不会覆盖已有目录。再次打包可指定 `-OutputDirectory artifacts/another-release`。运行时许可和第三方声明会自动包含在发布包内。

设置和历史记录保存在 `%LOCALAPPDATA%/MellowMeter`。详见[设计说明](docs/DESIGN.md)、[验证记录与限制](docs/VERIFICATION.md)以及[第三方声明](THIRD_PARTY_NOTICES.md)。

</details>

## 验证与限制

当前版本在 Windows 11 x64 上通过四套回归，并使用独立配置检查发布包的实际点击、切换和滚动。性能比较包含三轮测量，区分首次构建和重复交互；密集监控项操作仍有超过 100 ms 的情况，不保证所有机器均有相同表现。

渲染器覆盖模拟的 100%、150% 和 200% 缩放，本机为 175%。物理 DPI 切换、混合 DPI 多显示器、真实睡眠/唤醒、Explorer 重启以及广泛硬件/驱动兼容性仍未验证。具体计数、包校验、性能数据和限制见[本轮报告](docs/settings-fluid-20261030.md)。

## 上游项目与许可

项目基于 LiteMonitor 提交 [`3b91b5ca6d70a9056b8fdd01c9acddc985721240`](https://github.com/Diorser/LiteMonitor/commit/3b91b5ca6d70a9056b8fdd01c9acddc985721240)。原项目历史、源码命名空间、主题和相关资源均予以保留。MellowMeter 是独立衍生项目，并非 LiteMonitor 官方版本。LiteMonitor 声明使用 MIT 许可；归属与依赖许可请见 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
