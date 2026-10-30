using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;
using LibreHardwareMonitor.Hardware;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        // 测试只使用内存配置与模拟硬件，不打开驱动或保存用户设置。
        typeof(Settings).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, new Settings());
        SettingsHelper.GlobalBlockSave = true;
        await Run("自动模式优先保留主显卡风扇", () => GpuTest("", checkFan: true));
        await Run("失效显卡首选项按自动模式刷新补充传感器", () => GpuTest("不存在的显卡"));
        await Run("同名显卡旧配置按自动模式刷新补充传感器", () => GpuTest("同名显卡", duplicate: true));
        await Run("有效显卡首选项保持严格单卡采集", () => GpuTest("/gpu/primary", explicitSelection: true));
        await Run("关闭 GPU 监控时不预热显卡", GpuDisabledTest);
        await Run("首次更新后才出现的传感器可以建图", GpuDelayedTest);
        foreach (var (name, nominal, percent, registry, expected) in new (string, float?, float?, float, float?)[]
        {
            ("标称频率结合睿频百分比", 3000, 150, 2500, 4500),
            ("标称频率结合降频百分比", 3000, 50, 2500, 1500),
            ("缺少标称计数器时用注册表基准", null, 160, 2500, 4000),
            ("缺少百分比时回退标称值", 3000, null, 2500, 3000),
            ("计数器均缺失时交给硬件传感器", null, null, 2500, null),
            ("拒绝非数值性能百分比", 3000, float.NaN, 2500, null),
            ("拒绝无限性能百分比", 3000, float.PositiveInfinity, 2500, null),
            ("拒绝负性能百分比", 3000, -1, 2500, null),
            ("拒绝零性能百分比", 3000, 0, 2500, null),
            ("拒绝换算后过高频率", 3000, 1000, 2500, null),
            ("拒绝无效基准频率", null, 100, float.NaN, null)
        })
        {
            await Run("CPU 频率：" + name, () =>
            {
                var method = typeof(PerformanceCounterManager).GetMethod("ResolveCpuFrequency", BindingFlags.Static | BindingFlags.NonPublic)!;
                float? actual = (float?)method.Invoke(null, new object?[] { nominal, percent, registry });
                Check(actual == expected, "频率回退计算不符合预期");
                return Task.CompletedTask;
            });
        }
        await Run("内存和显存的数值与自定义单位一致", MemoryTest);

        Console.WriteLine($"发版审查：通过 {_passed} 项，失败 {_failed} 项。");
        return _failed == 0 ? 0 : 1;
    }

    private static Task GpuTest(string preference, bool checkFan = false, bool explicitSelection = false, bool duplicate = false)
    {
        var primary = Hardware(HardwareType.GpuNvidia, duplicate ? "同名显卡" : "主显卡", "primary");
        var secondary = Hardware(HardwareType.GpuIntel, duplicate ? "同名显卡" : "Intel UHD", "secondary");
        Proxy(primary).Values["Sensors"] = new[] { Sensor(primary, "GPU Core", SensorType.Load), Sensor(primary, "Fan", SensorType.Fan) };
        Proxy(secondary).Values["Sensors"] = new[] { Sensor(secondary, "GPU Core", SensorType.Temperature), Sensor(secondary, "Fan", SensorType.Fan) };
        var computer = Computer(primary, secondary);
        var cfg = new Settings { PreferredGpu = preference };
        cfg.MonitorItems.Clear();
        cfg.MonitorItems.Add(new MonitorItemConfig { Key = "GPU.Load", VisibleInPanel = true });
        var map = new SensorMap();
        map.Rebuild(computer, cfg);
        if (checkFan)
        {
            Check(map.TryGetSensor("GPU.Fan", out var fan) && ReferenceEquals(fan!.Hardware, primary), "低优先级显卡覆盖了主显卡风扇");
        }
        else
        {
            var monitor = (HardwareMonitor)RuntimeHelpers.GetUninitializedObject(typeof(HardwareMonitor));
            typeof(HardwareMonitor).GetField("_cfg", Private)!.SetValue(monitor, cfg);
            typeof(HardwareMonitor).GetField("_sensorMap", Private)!.SetValue(monitor, map);
            bool refresh = (bool)typeof(HardwareMonitor).GetMethod("ShouldUpdateGpuHardware", Private)!.Invoke(monitor, new object[] { secondary })!;
            Check(refresh == !explicitSelection, "显卡刷新规则与建图选择规则不一致");
            Check(map.TryGetSensor("GPU.Temp", out _) == !explicitSelection, "显卡映射范围不符合预期");
        }
        return Task.CompletedTask;
    }

    private static Task GpuDisabledTest()
    {
        var gpu = Hardware(HardwareType.GpuNvidia, "主显卡", "primary");
        var cfg = new Settings();
        cfg.MonitorItems.Clear();
        new SensorMap().Rebuild(Computer(gpu), cfg);
        Check(Proxy(gpu).Updates == 0, "禁用 GPU 后仍然预热");
        return Task.CompletedTask;
    }

    private static Task GpuDelayedTest()
    {
        var gpu = Hardware(HardwareType.GpuNvidia, "主显卡", "primary");
        Proxy(gpu).OnUpdate = () => Proxy(gpu).Values["Sensors"] = new[] { Sensor(gpu, "GPU Core", SensorType.Load) };
        var cfg = new Settings();
        cfg.MonitorItems.Clear();
        cfg.MonitorItems.Add(new MonitorItemConfig { Key = "GPU.Load", VisibleInPanel = true });
        var map = new SensorMap();
        map.Rebuild(Computer(gpu), cfg);
        Check(Proxy(gpu).Updates == 1 && map.TryGetSensor("GPU.Load", out _), "首次更新产生的传感器没有映射");
        return Task.CompletedTask;
    }

    private static Task MemoryTest()
    {
        var cfg = Settings.Load();
        cfg.MemoryDisplayMode = 1;
        Settings.DetectedRamTotalGB = 32;
        Settings.DetectedGpuVramTotalGB = 8;
        foreach (string key in new[] { "MEM.Load", "GPU.VRAM" })
        {
            string expectedCapacity = key == "MEM.Load" ? "16.0" : "4.00";
            Check(MetricUtils.GetValueStr(key, 50, false, "%") == "50.0", "百分号单位未保留百分比数值");
            Check(MetricUtils.GetValueStr(key, 50, false, "{u}") == expectedCapacity, "默认容量转换错误");
            var item = new MetricItem { Key = key, Value = 50, DisplayValue = 50, BoundConfig = new MonitorItemConfig { Key = key, UnitPanel = "%", UnitTaskbar = "{u}" } };
            Check(item.GetFormattedText(false) == "50.0%", "面板格式化不一致");
            Check(item.GetFormattedText(true) == expectedCapacity + "GB", "任务栏格式化不一致");
        }
        return Task.CompletedTask;
    }

    private static IHardware Hardware(HardwareType type, string name, string id)
    {
        var hardware = DispatchProxy.Create<IHardware, StubProxy>();
        Proxy(hardware).Values = new() { ["HardwareType"] = type, ["Name"] = name, ["Identifier"] = new Identifier("gpu", id), ["Sensors"] = Array.Empty<ISensor>(), ["SubHardware"] = Array.Empty<IHardware>() };
        return hardware;
    }

    private static ISensor Sensor(IHardware hardware, string name, SensorType type)
    {
        var sensor = DispatchProxy.Create<ISensor, StubProxy>();
        Proxy(sensor).Values = new() { ["Hardware"] = hardware, ["Name"] = name, ["SensorType"] = type, ["Value"] = 50f };
        return sensor;
    }

    private static Computer Computer(params IHardware[] hardware)
    {
        var computer = new Computer();
        var field = typeof(Computer).GetField("_groups", Private)!;
        var groupType = field.FieldType.GenericTypeArguments[0];
        object group = DispatchProxy.Create(groupType, typeof(StubProxy));
        Proxy(group).Values["Hardware"] = hardware;
        ((IList)field.GetValue(computer)!).Add(group);
        return computer;
    }

    private static StubProxy Proxy(object value) => (StubProxy)value;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Run(string name, Func<Task> test)
    {
        try { await test(); _passed++; Console.WriteLine("通过：" + name); }
        catch (Exception ex) { _failed++; Console.WriteLine($"失败：{name}：{ex.GetBaseException().Message}"); }
    }
}

// 用动态代理模拟硬件接口，只向真实业务代码提供测试所需的传感器。
public class StubProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    public Action? OnUpdate;
    public int Updates;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "Update") { Updates++; OnUpdate?.Invoke(); return null; }
        if (method.Name.StartsWith("get_")) return Values.GetValueOrDefault(method.Name[4..]);
        return null;
    }
}
