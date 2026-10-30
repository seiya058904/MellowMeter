using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

internal static partial class Program
{
    private static int MotionPreview(string root, bool interactive)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var cfg = Settings.Load();
        cfg.Language = "zh"; cfg.Skin = "MellowLight"; cfg.TopMost = false;
        cfg.ShowTaskbar = false; cfg.HideTrayIcon = true; cfg.HistoryEnabled = false;
        cfg.AdvancedSensorsEnabled = false; cfg.InterfaceAnimationsEnabled = true; cfg.Save();
        using var main = new MainForm();
        int exit = 0;
        main.Shown += async (_, _) =>
        {
            Process? video = null;
            try
            {
                var ui = Get<UIController>(main, "_ui");
                using var settings = new SettingsForm(cfg, ui, main);
                settings.Show(main); settings.Activate();
                if (interactive)
                {
                    File.WriteAllText(Path.Combine(root, "interactive.json"), JsonSerializer.Serialize(new { settings.Text, settings.Bounds, settings.DeviceDpi, processId = Environment.ProcessId }));
                    var closed = new TaskCompletionSource();
                    settings.FormClosed += (_, _) => closed.TrySetResult();
                    await closed.Task;
                    return;
                }
                await Task.Delay(700);
                Snapshot(settings, Path.Combine(root, "settings-glass-light.png"));
                var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-y", "-f", "gdigrab", "-framerate", "30", "-i", "title=" + settings.Text, "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "22", "-pix_fmt", "yuv420p", Path.Combine(root, "settings-motion.mp4") }) start.ArgumentList.Add(arg);
                video = Process.Start(start);
                var videoLog = video!.StandardError.ReadToEndAsync();
                await Task.Delay(800);
                var results = new List<object>();
                var motion = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.InterfaceMotion")!;
                int Active() => (int)motion.GetProperty("ActiveCount", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                foreach (bool enabled in new[] { false, true })
                {
                    cfg.InterfaceAnimationsEnabled = enabled; AppActions.ApplyThemeAndLayout(cfg, ui, main);
                    using var process = Process.GetCurrentProcess();
                    var intervals = new List<double>();
                    long previous = Stopwatch.GetTimestamp();
                    using var pulse = new System.Windows.Forms.Timer { Interval = 15 };
                    pulse.Tick += (_, _) => { long now = Stopwatch.GetTimestamp(); intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; };
                    pulse.Start();
                    var watch = Stopwatch.StartNew();
                    TimeSpan cpu = process.TotalProcessorTime;
                    var transitions = new List<double>();
                    foreach (string page in new[] { "Taskbar", "Monitor", "System", "MainPanel", "Monitor" })
                    {
                        var switchWatch = Stopwatch.StartNew(); settings.SwitchPage(page); switchWatch.Stop();
                        transitions.Add(switchWatch.Elapsed.TotalMilliseconds);
                        await Task.Delay(450);
                        var pages = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages");
                        var toggle = Children(pages[page]).OfType<LiteCheck>().FirstOrDefault(x => x.Visible && x.Enabled);
                        if (toggle != null) { toggle.Checked = !toggle.Checked; await Task.Delay(240); toggle.Checked = !toggle.Checked; }
                        if (page == "Monitor")
                        {
                            var panel = Get<Panel>(pages[page], "_container");
                            var wheel = panel.GetType().GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance)!;
                            for (int i = 0; i < 5; i++) { wheel.Invoke(panel, new object[] { new HandledMouseEventArgs(MouseButtons.None, 0, 100, 100, -120) }); await Task.Delay(70); }
                            await Task.Delay(240);
                            for (int i = 0; i < 5; i++) { wheel.Invoke(panel, new object[] { new HandledMouseEventArgs(MouseButtons.None, 0, 100, 100, 120) }); await Task.Delay(70); }
                        }
                        if (page == "System")
                        {
                            var group = Children(pages[page]).OfType<LiteSettingsGroup>().First(x => x.Collapsible);
                            group.Collapsed = false; await Task.Delay(240); group.Collapsed = true;
                        }
                    }
                    await Task.Delay(350); pulse.Stop(); process.Refresh();
                    intervals.Sort();
                    double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                    long memory = process.PrivateMemorySize64;
                    TimeSpan idleCpu = process.TotalProcessorTime;
                    int activeAtRest = Active();
                    await Task.Delay(2000); process.Refresh();
                    results.Add(new { enabled, elapsedMs = watch.Elapsed.TotalMilliseconds - 2000, cpuMs, memory,
                        switchCallMs = transitions, messageLoopP95Ms = intervals[(int)((intervals.Count - 1) * .95)],
                        messageLoopMaxMs = intervals.Max(), activeAtRest, activeAfterIdle = Active(), idleCpuMs = (process.TotalProcessorTime - idleCpu).TotalMilliseconds,
                        gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1) });
                }
                settings.SwitchPage("MainPanel"); await Task.Delay(350);
                cfg.Skin = "MellowDark"; AppActions.ApplyThemeAndLayout(cfg, ui, main);
                await Task.Delay(700); Snapshot(settings, Path.Combine(root, "settings-glass-dark.png"));
                settings.SwitchPage("Monitor"); await Task.Delay(500); Snapshot(settings, Path.Combine(root, "settings-metrics.png"));
                cfg.GlassEffectsEnabled = false; AppActions.ApplyThemeAndLayout(cfg, ui, main);
                await Task.Delay(500); Snapshot(settings, Path.Combine(root, "settings-effects-off.png"));
                File.WriteAllText(Path.Combine(root, "motion-probe.json"), JsonSerializer.Serialize(new { results, nativeDpi = settings.DeviceDpi,
                    method = "Visible native WinForms, same basic sampler and restored metric selection; UI-loop pulse latency is not GPU frame presentation time. A single off/on paired run, not a universal performance claim." }, new JsonSerializerOptions { WriteIndented = true }));
                await video.StandardInput.WriteLineAsync("q");
                await video.WaitForExitAsync();
                File.WriteAllText(Path.Combine(root, "recording.log"), await videoLog);
                if (video.ExitCode != 0) throw new InvalidOperationException("Recording failed: " + video.ExitCode);
                settings.Close();
            }
            catch (Exception ex) { exit = 1; File.WriteAllText(Path.Combine(root, "motion-error.txt"), ex.ToString()); }
            finally { if (video is { HasExited: false }) { await video.StandardInput.WriteLineAsync("q"); await video.WaitForExitAsync(); } video?.Dispose(); main.Close(); }
        };
        Application.Run(main);
        return exit;
    }
    private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static IEnumerable<Control> Children(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var item in Children(child)) yield return item; }
    }
}
