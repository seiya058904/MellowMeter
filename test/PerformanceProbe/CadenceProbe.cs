using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

// Frame cadence of the live settings window. It records each animation tick on the UI thread
// (start and end, including the forced paint) with the debug counter in FrameClock, under the same
// scripted interactions for each clock source and composition style. No physical input is used:
// wheel messages are posted to the page, the rest goes through the controls' action paths.
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    // Time spent inside one input message, including its synchronous first paint.
    private static async Task InputLatency(SettingsForm settings, Dictionary<string, SettingsPageBase> pages, ScrollSurface surface, Dictionary<string, List<double>> samples)
    {
        void Time(string action, Action run)
        {
            long start = Stopwatch.GetTimestamp();
            run();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (!samples.TryGetValue(action, out var list)) samples[action] = list = new List<double>();
            list.Add(ms);
        }
        async Task Idle() { for (int i = 0; i < 300 && InterfaceMotion.ActiveCount > 0; i++) await Task.Delay(10); }
        IntPtr Xy(int x, int y) => new((y << 16) | (x & 0xFFFF));
        const int Down = 0x0201, Move = 0x0200;
        settings.SwitchPage("MainPanel");
        await Idle();
        var button = (LiteButton)settings.AcceptButton!;
        var check = Children(pages["MainPanel"]).OfType<LiteCheck>().First(c => c.Visible);
        var slider = Children(pages["MainPanel"]).OfType<LiteSlider>().First(s => s.Visible);
        var picker = Children(pages["MainPanel"]).OfType<LiteComboBox>().First(p => p.Visible);
        var segments = Children(pages["MainPanel"]).OfType<LiteSelector>().First(s => s.Visible);
        var nav = Get<LiteSelector>(settings, "_nav");
        for (int i = 0; i < 12; i++)
        {
            Time("button press", () => SendMessage(button.Handle, Down, new IntPtr(1), Xy(3, 3)));
            button.Capture = false;
            await Idle();
            int y = check.Height / 2, x = check.Checked ? check.Width - 10 : 10, dir = check.Checked ? -1 : 1;
            Time("switch press", () => SendMessage(check.Handle, Down, new IntPtr(1), Xy(x, y)));
            await Task.Delay(30);
            Time("switch drag step", () => SendMessage(check.Handle, Move, new IntPtr(1), Xy(x + dir * 8, y)));
            await Task.Delay(30);
            check.Capture = false;
            await Idle();
            slider.BeginProbeDrag(0.2f);
            await Task.Delay(30);
            Time("slider drag step", () => slider.MoveProbeDrag(0.2f + (i % 5) * 0.1f));
            slider.EndProbeDrag();
            await Idle();
            Time("segment select", () => segments.PerformSelect((segments.SelectedIndex + 1) % segments.Count));
            await Idle();
            Time("menu open", () => picker.OpenMenu());
            await Idle();
            Time("menu key", () => LitePopupMenu.Current?.ProcessKey(Keys.Down));
            await Idle();
            LitePopupMenu.Current?.ProcessKey(Keys.Escape);
            await Idle();
        }
        settings.SwitchPage("Monitor");
        await Idle();
        var viewport = surface.RectangleToScreen(surface.ClientRectangle);
        var rows = Children(pages["Monitor"]).OfType<MonitorItemRow>().Where(r => r.Visible && viewport.Contains(r.RectangleToScreen(r.ClientRectangle))).ToArray();
        for (int i = 0; i < 8; i++)
        {
            var row = rows[i % rows.Length];
            var host = (StackPanel)row.Parent!;
            int rowY = row.PointToScreen(new Point(0, row.Height / 2)).Y;
            ReorderSession? session = null;
            Time("reorder lift", () => session = new ReorderSession(host, row, surface, rowY, (_, _) => { }, () => { }, followPointer: false));
            await Idle();
            Time("reorder move", () => session!.MoveTo(rowY + row.Height));
            await Task.Delay(20);
            Time("reorder idle move", () => session!.MoveTo(rowY + row.Height + 2));
            session!.Cancel();
            await Idle();
        }
    }

    private static int CadenceProbe(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var cfg = Settings.Load();
        cfg.AdvancedSensorsEnabled = false; cfg.AutoStart = false; cfg.ShowTaskbar = false;
        cfg.HideTrayIcon = true; cfg.TopMost = false; cfg.HistoryEnabled = false;
        cfg.Language = "en"; cfg.Skin = "DarkFlat_Classic"; cfg.InterfaceAnimationsEnabled = true; cfg.Save();
        using var main = new MainForm();
        int exit = 1;
        var clockType = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.FrameClock")!;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        var trace = (System.Collections.IList)clockType.GetField("Trace", Static)!.GetValue(null)!;
        var modeType = clockType.GetNestedType("Mode", BindingFlags.NonPublic)!;
        void SetMode(string mode) => clockType.GetProperty("Source", Static)!.SetValue(null, Enum.Parse(modeType, mode));
        clockType.GetProperty("TraceEnabled", Static)!.SetValue(null, true);
        // Windows animation effects may be off on the host; this probe measures motion anyway.
        typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.InterfaceMotion")!.GetProperty("SystemPreferenceOverride", Static)!.SetValue(null, true);
        var results = new List<object>();
        var latency = new Dictionary<string, List<double>>();
        main.Shown += async (_, _) =>
        {
            try
            {
                var ui = Get<UIController>(main, "_ui");
                await Task.Delay(1000);
                foreach (var pass in new[] { 0, 1, 2 })
                foreach (var clock in new[] { "Composition", "Timer" })
                foreach (var composited in new[] { false, true })
                {
                    SetMode(clock);
                    using var settings = new SettingsForm(cfg, ui, main);
                    settings.Show(main);
                    await Task.Delay(300);
                    if (composited) SetStyle(settings.Handle, -20, GetStyle(settings.Handle, -20) | 0x02000000);
                    foreach (var key in new[] { "Taskbar", "Monitor", "Threshold", "System", "Monitor" }) settings.SwitchPage(key);
                    await Task.Delay(600);
                    var pages = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages");
                    var surface = (ScrollSurface)pages["Monitor"].GetType().GetProperty("Surface", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(pages["Monitor"])!;
                    async Task Scenario(string name, Func<Task> run)
                    {
                        trace.Clear();
                        double cpu0 = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                        var wall = Stopwatch.StartNew();
                        await run();
                        await Task.Delay(700);
                        wall.Stop();
                        double cpu = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds - cpu0;
                        var frames = new List<(long Start, long End)>();
                        foreach (var entry in trace) { var t = ((long Start, long End))entry!; frames.Add(t); }
                        double f = Stopwatch.Frequency / 1000d;
                        var intervals = new List<double>();
                        for (int i = 1; i < frames.Count; i++)
                        {
                            double ms = (frames[i].Start - frames[i - 1].Start) / f;
                            if (ms < 100) intervals.Add(ms); // a larger gap is the pause between gestures
                        }
                        var costs = frames.Select(x => (x.End - x.Start) / f).ToList();
                        intervals.Sort(); costs.Sort();
                        double P(List<double> s, double q) => s.Count == 0 ? 0 : s[Math.Min(s.Count - 1, (int)Math.Ceiling(s.Count * q) - 1)];
                        results.Add(new
                        {
                            pass, clock, composited, scenario = name, frames = frames.Count,
                            intervalMedianMs = P(intervals, .5), intervalP95Ms = P(intervals, .95), intervalMaxMs = intervals.Count > 0 ? intervals[^1] : 0,
                            shareAtMost20Ms = intervals.Count == 0 ? 0 : intervals.Count(x => x <= 20) / (double)intervals.Count,
                            costMedianMs = P(costs, .5), costP95Ms = P(costs, .95),
                            cpuMsPerSecond = cpu / wall.Elapsed.TotalSeconds
                        });
                    }
                    if (!composited && clock == "Composition") await InputLatency(settings, pages, surface, latency);
                    await Scenario("wheel-glide", async () =>
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            int delta = i < 8 ? -120 : 120;
                            PostMessage(surface.Handle, 0x020A, new IntPtr(delta << 16), IntPtr.Zero);
                            await Task.Delay(110);
                        }
                    });
                    surface.GetType().GetMethod("ScrollTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, new object[] { 0f });
                    await Scenario("switch-toggle", async () =>
                    {
                        var toggles = Children(pages["Monitor"]).OfType<LiteCheck>().Where(c => c.Visible && c.Text == "").Take(4).ToArray();
                        for (int i = 0; i < 8; i++) { foreach (var t in toggles) t.Checked = !t.Checked; await Task.Delay(220); }
                    });
                    settings.SwitchPage("Taskbar"); await Task.Delay(500);
                    await Scenario("disclosure", async () =>
                    {
                        var groups = Children(pages["Taskbar"]).OfType<LiteSettingsGroup>().Where(g => g.Collapsible && g.Visible).Take(2).ToArray();
                        for (int i = 0; i < 6; i++) { foreach (var g in groups) g.Collapsed = !g.Collapsed; await Task.Delay(480); }
                    });
                    await Scenario("page-switch", async () =>
                    {
                        foreach (var key in new[] { "Monitor", "Threshold", "System", "MainPanel", "Taskbar", "Monitor", "Threshold", "System" })
                        { settings.SwitchPage(key); await Task.Delay(380); }
                    });
                    settings.Close();
                    await Task.Delay(100);
                }
                var inputSummary = latency.Select(pair => {
                    var sorted = pair.Value.Order().ToArray();
                    return new { action = pair.Key, count = sorted.Length, medianMs = sorted[sorted.Length / 2], p95Ms = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * .95) - 1)], maxMs = sorted[^1] };
                }).ToArray();
                File.WriteAllText(Path.Combine(root, "cadence.json"), JsonSerializer.Serialize(new
                {
                    input = inputSummary,
                    assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Settings).Assembly.Location))),
                    dpi = main.DeviceDpi, results,
                    note = "UI-thread tick intervals and tick cost including forced paint; not photon timing. Gaps over 100 ms are excluded as pauses between gestures. Synthetic input."
                }, new JsonSerializerOptions { WriteIndented = true }));
                exit = 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
            finally { main.Close(); }
        };
        Application.Run(main);
        return exit;
    }
}
