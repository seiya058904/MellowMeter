using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("MELLOWMETER_DATA_DIR", Path.Combine(root, "data"));
        if (args.Contains("--motion") || args.Contains("--interactive")) return MotionPreview(root, args.Contains("--interactive"));
        if (args.Contains("--preview")) return Preview(root, args.Contains("--baseline"));
        return Run(root, args.Length > 1 ? args[1] : "DarkFlat_Classic");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run(string root, string theme)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var cfg = Settings.Load();
        cfg.Skin = theme; cfg.ShowTaskbar = false; cfg.HideTrayIcon = true; cfg.TopMost = false; cfg.HistoryEnabled = false; cfg.Save();
        using var main = new MainForm();
        main.Show(); Application.DoEvents();
        var ui = (UIController)typeof(MainForm).GetField("_ui", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        ui.SetPowerState(suspended: true);
        using var process = Process.GetCurrentProcess();
        var opens = new List<double>();
        var handles = new List<object>();
        for (int i = 0; i < 31; i++) {
            var clock = Stopwatch.StartNew();
            using (var settings = new SettingsForm(cfg, ui, main)) {
                settings.Show(main); Application.DoEvents();
                clock.Stop();
                if (i > 0) opens.Add(clock.Elapsed.TotalMilliseconds);
                settings.Close();
            }
            Application.DoEvents();
            handles.Add(new { i, gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1) });
        }
        using var chart = new TrendChartControl { Size = new Size(800, 400) };
        var points = Enumerable.Range(0, 10000).Select(i => new TrendChartPoint { Time = DateTime.Today.AddSeconds(i), Avg = i % 100, Min = i % 100, Max = i % 100, Count = 1 }).ToList();
        chart.SetSeries(new[] { new TrendChartSeries { Key = "CPU.Load", Label = "CPU", Points = points } });
        using var bitmap = new Bitmap(800, 400);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        var mouse = typeof(TrendChartControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var moves = new List<double>();
        for (int run = 0; run < 4; run++) {
            var clock = Stopwatch.StartNew();
            for (int i=0; i<1000; i++) mouse.Invoke(chart, new object[] { new MouseEventArgs(MouseButtons.None, 0, 70+i%700, 180, 0) });
            clock.Stop();
            if (run > 0) moves.Add(clock.Elapsed.TotalMilliseconds);
        }
        var paints = new List<double>();
        for (int run=0; run<3; run++) {
            var clock = Stopwatch.StartNew();
            for (int i=0; i<50; i++) chart.DrawToBitmap(bitmap, chart.ClientRectangle);
            paints.Add(clock.Elapsed.TotalMilliseconds / 50);
        }
        File.WriteAllText(Path.Combine(root,"probe.json"), JsonSerializer.Serialize(new { theme, settingsOpenMilliseconds = opens, hover1000Milliseconds = moves, chartPaintMilliseconds = paints, handles,
            note = "Same in-process UI harness, 30 settings opens after one warmup; 10000-point chart. DrawToBitmap measures renderer cost, not DWM composition." }, new JsonSerializerOptions { WriteIndented = true }));
        main.Close(); Application.DoEvents();
        return 0;
    }
}
