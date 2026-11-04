# LiteMonitor 设置模块开发规范

> **重要**：在修改设置模块的任何代码之前，必须阅读并遵守本规范。

---

## 1. 架构总览

设置模块采用 **Draft-Commit (草稿-提交)** 架构，以确保线程安全和数据一致性。

- **实时设置 (`Settings.Instance`)**：全局单例，供正在运行的程序读取。**严禁从 UI 代码中直接修改此对象。**
- **草稿设置 (`_draftCfg`)**：当设置窗口打开时，会创建一个深拷贝副本。UI 的所有操作（绑定、修改）都必须只针对这个草稿对象。
- **提交阶段**：当用户点击“保存”按钮时，通过 `SettingsChanger.Merge` 方法将草稿合并回实时设置中。

**架构核心原则**：
*   **隔离**：UI 编辑状态与程序运行状态完全隔离。
*   **原子性**：所有修改要么全部生效，要么（点击取消时）全部丢弃。

---

## 2. 新增配置项流程

### 第一步：在 `Settings.cs` 中定义
在 `Settings` 类中添加属性。使用标准的 C# 属性定义。
```csharp
public bool ShowSeconds { get; set; } = false; // 设置默认值
```

### 第二步：在 UI 中绑定 (例如 `TaskbarPage.cs`)
直接将控件绑定到 **Config** 对象（这里的 Config 是草稿副本）。
**不要**在 `SettingsChanger` 中创建专门的 Set 方法。

```csharp
// ✅ 正确 (直接绑定到 Draft)
group.AddToggle(this, "Menu.ShowSeconds", 
    () => Config.ShowSeconds, 
    v => Config.ShowSeconds = v);

// ❌ 错误 (不要为简单属性创建辅助方法)
// SettingsChanger.SetShowSeconds(Config, v); 
```

### 第三步：处理运行时数据 (可选)
如果你新增的属性是一个**运行时统计数据**（例如 `MaxCpuTemp`），由后台线程自动更新，那么：
1.  打开 `src/Core/Logic/SettingsChanger.cs`。
2.  将该属性名添加到 `Merge` 方法内部的 `runtimeProps` 黑名单集合中。
3.  **原因**：这能防止旧的草稿值覆盖后台线程刚刚记录的新值（防止数据回滚）。

---

## 3. 修改逻辑规范

### ✅ 推荐做法 (DOs)
*   **信任 `SettingsChanger.Merge`**：利用反射机制自动处理大部分属性的合并。
*   **使用 `SettingsChanger.UpdateMonitorList`**：对于复杂的列表操作（如监控项 MonitorItems），必须使用现有的辅助方法，以保留动态属性。
*   **检查 `AppActions`**：如果你的设置修改后需要立即生效（例如重启定时器、重载主题），请将逻辑添加到 `AppActions.ApplyAllSettings` 中。

### ❌ 禁止做法 (DON'Ts)
*   **严禁**在具体的设置子页面（如 `PluginPage`）中调用 `Settings.Save()`。保存操作必须是原子性的，只能由主设置窗口触发。
*   **严禁**从 UI 代码中直接修改 `Settings.Instance`。必须始终使用 `SettingsPageBase` 提供的 `Config` 属性。
*   **严禁**在 `SettingsChanger` 中添加副作用调用（如 `SyncToLanguage`）。该类应保持纯逻辑性。

---

## 4. 最佳实践

### 空值安全
访问 `Config` 时（特别是在事件处理程序或 Lambda 中），始终检查是否为 null，因为页面初始化时 Config 可能尚未注入。
```csharp
// 推荐写法
v => { if(Config != null) Config.SomeProp = v; }
```

### UI 组件
使用 `LiteSettingsGroup` 和 `SettingsUIBuilder` 的辅助方法来创建控件，不要手动实例化 WinForms 控件。这能保证样式和行为的一致性。间距、圆角、字体和颜色都来自 `Controls/DesignSystem.cs`，不要写魔法数字。

按取值空间选控件：开关用 `AddToggle`，两个互斥选项用 `AddSegmented`，有序的固定档位用 `AddSlider`，可自由输入的数字用 `AddInt`/`AddDouble`（带步进器），长列表用 `AddCombo`/`AddComboIndex`（主题化菜单）。说明文字用 `AddHint`，会显示为分组下方的脚注。

页面用 `CreateSurface(标题)` 创建滚动表面，用 `AddGroupToPage` 添加分组。不要使用 `AutoScroll`：滚动只移动一个内容窗口，这是滚动帧成本低的原因。自绘控件的 `OnPaint` 必须用 `Ink` 的共享画笔并铺满自己的不透明背景，设置窗口已不再使用 `WS_EX_COMPOSITED`。

动效一律走 `InterfaceMotion.Spring` / `MotionValue`，弹簧预设在 `SpringSpec`。本机 Windows“动画效果”关闭时所有动效都不运行；验证动效时设置环境变量 `MELLOWMETER_FORCE_MOTION=1`，它只影响当前进程。

### 国际化
*   不要硬编码字符串。请使用 `LanguageManager.T("Key")`。
*   新增 Key 后，确保 `resources/lang/` 下全部九种语言都有对应的翻译。缺失时界面会显示 Key 本身。

---

## 5. 常见问题排查

*   **问：点击保存后，我的设置被重置了。**
    *   答：你是否误将该属性添加到了 `SettingsChanger.cs` 的 `runtimeProps` 黑名单中？或者 `Merge` 逻辑有误？

*   **问：设置值变了，但界面没有反应。**
    *   答：你可能忘记在 `AppActions.ApplyAllSettings` 中添加应用逻辑。配置值变了，但程序需要被通知去*应用*这个变化。

*   **问：打开设置时程序崩溃。**
    *   答：检查构造函数中是否访问了 `Config`。构造函数执行时 `Config` 还是 null；它是在稍后的 `SetContext` 中注入的。请将逻辑移至 `OnShow` 或使用 Lambda 延迟获取。
