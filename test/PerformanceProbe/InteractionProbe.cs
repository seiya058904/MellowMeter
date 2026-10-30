using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetStyle(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetStyle(nint handle, int index, nint style);
    private sealed record Timing(string Operation, int Round, double Milliseconds, long AllocatedBytes);
    private static int InteractionProbe(string root, bool localBuffer)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var cfg = Settings.Load();
        cfg.AdvancedSensorsEnabled = false; cfg.AutoStart = false; cfg.ShowTaskbar = false;
        cfg.HideTrayIcon = true; cfg.TopMost = false; cfg.HistoryEnabled = false;
        cfg.Language = "en"; cfg.Skin = "DarkFlat_Classic"; cfg.InterfaceAnimationsEnabled = true; cfg.Save();
        using var main = new MainForm();
        var times = new List<Timing>();
        var resources = new List<object>();
        using var process = Process.GetCurrentProcess();
        int exit = 1;
        main.Shown += async (_, _) => {
            try {
                var ui = Get<UIController>(main, "_ui");
                await Task.Delay(1000);
                async Task Idle() {
                    var ready = new TaskCompletionSource();
                    EventHandler? handler = null;
                    handler = (_, _) => { Application.Idle -= handler; ready.SetResult(); };
                    Application.Idle += handler;
                    main.BeginInvoke(new Action(() => { }));
                    await ready.Task;
                }
                SettingsForm? activeSettings = null;
                async Task Measure(string operation, int round, Action action) {
                    long allocated = GC.GetTotalAllocatedBytes(false);
                    var clock = Stopwatch.StartNew();
                    action();
                    if (localBuffer && activeSettings is { IsDisposed: false })
                        foreach (var control in Children(activeSettings).Prepend(activeSettings))
                            if (control.IsHandleCreated) {
                                var style = GetStyle(control.Handle, -20);
                                if ((style & 0x02000000) != 0) SetStyle(control.Handle, -20, style & ~0x02000000);
                            }
                    await Idle(); clock.Stop();
                    times.Add(new(operation, round, clock.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(false) - allocated));
                }
                async Task Sample(string state) {
                    process.Refresh(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
                    await Task.Delay(5000); process.Refresh();
                    resources.Add(new { state, cpuPercentMachine = (process.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                        privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64, handles = process.HandleCount,
                        gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1) });
                }
                await Sample("settings-closed");
                for (int round = -1; round < 3; round++) {
                    SettingsForm? settings = null;
                    await Measure("open", round, () => {
                        settings = activeSettings = new SettingsForm(cfg, ui, main);
                        if (localBuffer) SetStyle(settings.Handle, -20, GetStyle(settings.Handle, -20) & ~0x02000000);
                        settings.Show(main);
                    });
                    using (settings!) {
                        if (round == 0) await Sample("settings-open-idle");
                        foreach (var key in new[] { "Taskbar", "Monitor", "Threshold", "System" })
                            await Measure("first-" + key, round, () => settings!.SwitchPage(key));
                        // Enumeration is deliberately kept out of warm interaction timings.
                        await Task.Delay(300);
                        for (int repeat = 0; repeat < 10; repeat++)
                            foreach (var key in new[] { "MainPanel", "Taskbar", "Monitor", "Threshold", "System" })
                                await Measure("switch-" + key, round, () => settings!.SwitchPage(key));
                        settings!.SwitchPage("Monitor"); await Idle();
                        var page = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages")["Monitor"];
                        for (int repeat = 0; repeat < 10; repeat++) {
                            await Measure(repeat == 0 ? "first-tab-taskbar" : "tab-taskbar", round, () => Get<Button>(page, "_btnTabBar").PerformClick());
                            await Measure(repeat == 0 ? "first-filter" : "filter", round, () => { var c = Get<LiteCheck>(page, "_chkOnlyVisible"); c.Checked = !c.Checked; });
                            await Measure(repeat == 0 ? "first-tab-panel" : "tab-panel", round, () => Get<Button>(page, "_btnTabMain").PerformClick());
                            await Measure(repeat == 0 ? "first-labels" : "labels", round, () => { var c = Get<LiteCheck>(page, "_chkEditLabels"); c.Checked = !c.Checked; });
                        }
                        var apply = typeof(SettingsForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        for (int repeat = 0; repeat < 5; repeat++) {
                            await Measure("apply-unchanged", round, () => apply.Invoke(settings, null));
                            await Measure("apply-width", round, () => { Get<Settings>(settings, "_draftCfg").PanelWidth = repeat % 2 == 0 ? 260 : 240; apply.Invoke(settings, null); });
                        }
                        await Task.Delay(250);
                        if (round == 2) await Sample("after-interaction-idle");
                        settings.Close();
                    }
                    await Idle();
                    process.Refresh();
                    resources.Add(new { state = "closed-round-" + round, privateBytes = process.PrivateMemorySize64,
                        handles = process.HandleCount, gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1) });
                }
                for (int i = 0; i < 20; i++) {
                    using var settings = new SettingsForm(cfg, ui, main); settings.Show(main); await Idle(); settings.Close();
                }
                await Sample("after-20-open-close");
                var summary = times.Where(t => t.Round >= 0).GroupBy(t => t.Operation).Select(g => {
                    var sorted = g.Select(t => t.Milliseconds).Order().ToArray();
                    return new { operation = g.Key, count = sorted.Length, medianMs = sorted[sorted.Length / 2], p95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], meanAllocatedBytes = g.Average(t => t.AllocatedBytes) };
                });
                File.WriteAllText(Path.Combine(root, "interaction.json"), JsonSerializer.Serialize(new {
                    assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Settings).Assembly.Location))),
                    dpi = main.DeviceDpi, localBuffer, summary, times, resources,
                    note = "Native message-loop action-to-idle latency including pending paint; not OS input-to-photon timing. Round -1 is excluded warmup. Real basic sampling remains enabled, physical advanced sensors disabled. Allocations include background process activity."
                }, new JsonSerializerOptions { WriteIndented = true }));
                exit = 0;
            } catch (Exception ex) { File.WriteAllText(Path.Combine(root, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
            finally { main.Close(); }
        };
        Application.Run(main);
        return exit;
    }
}
