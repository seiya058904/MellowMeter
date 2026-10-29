using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI;

namespace DesktopSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? requested = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--artifacts" && i + 1 < args.Length) requested = args[++i];
            else { Console.Error.WriteLine("Usage: DesktopSmoke [--artifacts DIRECTORY]"); return 2; }
        }
        string root = Path.GetFullPath(requested ?? Path.Combine(AppContext.BaseDirectory, "artifacts"));
        string run = Path.Combine(root, "desktop_smoke_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(run);
        string data = Path.Combine(run, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("MELLOWMETER_DATA_DIR", data);
        Console.WriteLine("Artifacts: " + run);
        return SmokeSuite.Run(run);
    }
}

internal static class SmokeSuite
{
    private static readonly List<string> Passed = new();
    private static readonly List<string> Captures = new();
    private static string _artifacts = "";
    private static string _stage = "initialization";
    private static int _exitCode = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Keep the app's static path initialization behind Main's environment setup.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string artifacts)
    {
        _artifacts = artifacts;
        using var watchdog = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL: 60-second timeout during " + _stage);
            File.WriteAllText(Path.Combine(_artifacts, "timeout.txt"), _stage);
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1), StartPosition = FormStartPosition.Manual };
        using var heartbeat = new System.Windows.Forms.Timer { Interval = 1000 };
        heartbeat.Tick += (_, _) => Console.WriteLine($"HEARTBEAT {DateTime.UtcNow:O}: {_stage}");
        heartbeat.Start();
        host.Shown += async (_, _) =>
        {
            MainForm? main = null;
            try
            {
                _stage = "defaults and formatting";
                var cfg = Settings.Load();
                Check(Path.GetFullPath(SettingsHelper.FilePath).StartsWith(Path.GetFullPath(Path.Combine(_artifacts, "data")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Settings writes are isolated");
                Check(cfg.MonitorItems.Where(x => x.VisibleInPanel).Select(x => x.Key).Order().SequenceEqual(new[] { "CPU.Load", "MEM.Load", "NET.Down", "NET.Up" }.Order()), "Fresh panel has exactly CPU, memory, upload and download");
                Check(cfg.MonitorItems.Where(x => x.VisibleInTaskbar).Select(x => x.Key).Order().SequenceEqual(new[] { "CPU.Load", "MEM.Load", "NET.Down", "NET.Up" }.Order()), "Fresh taskbar has exactly CPU, memory, upload and download");
                Check(!cfg.AdvancedSensorsEnabled && !cfg.WebServerEnabled && !cfg.AutoStart && cfg.HistoryEnabled, "Fresh profile uses basic sensors and local history");
                Check(cfg.Skin == "DarkFlat_Classic" && cfg.TaskbarFontFamily == "Microsoft YaHei UI",
                    "Fresh profile preserves the original panel theme and taskbar typography");
                cfg.Language = "en";
                LanguageManager.Load(cfg.Language);
                TestMetricRecovery();
                _stage = "target-DPI renderer";
                TestLayouts(cfg);

                // Startup remains off; taskbar recovery is exercised explicitly below.
                cfg.ShowTaskbar = false;
                cfg.HideTrayIcon = true;
                cfg.TopMost = false;
                cfg.AutoHide = false;
                cfg.ClickThrough = false;
                cfg.HideMainForm = false;
                cfg.HistoryEnabled = false;
                cfg.Skin = "MellowLight";
                cfg.Save();
                SeedHistory();
                _stage = "main window construction";
                main = new MainForm();
                Console.WriteLine($"MainForm ctor finished: Visible={main.Visible}, Opacity={main.Opacity}, Bounds={main.Bounds}, HandleCreated={main.IsHandleCreated}");
                Check(TrafficLogger.Data.History.TryGetValue(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), out var oldTraffic) && oldTraffic.Download == 4194304,
                    "Startup loads existing traffic history while recording is disabled");
                Check(HardwareHistoryLogger.GetPoints(DateTime.Now.AddHours(-1)).Any(x => x.Values.TryGetValue("MEM.Load", out var value) && value.Avg == 45),
                    "Startup loads existing sensor history while recording is disabled");
                main.Shown += (_, _) => Console.WriteLine($"MainForm Shown: Visible={main.Visible}, Opacity={main.Opacity}, Bounds={main.Bounds}, HandleCreated={main.IsHandleCreated}");
                main.Show();
                main.Location = new Point(40, 40);
                var ui = Field<UIController>(main, "_ui");
                await Task.Delay(1400);
                Check(main.Visible && main.ClientSize.Width > 0 && main.ClientSize.Height > 0, "Main window is shown and laid out");
                Capture(main, "main_light");

                _stage = "settings cancel";
                int width = cfg.PanelWidth;
                bool orientation = cfg.HorizontalMode;
                using (var settings = new SettingsForm(cfg, ui, main))
                {
                    settings.Show(main);
                    settings.SwitchPage("Monitor");
                    await Task.Delay(180);
                    Check(cfg.HorizontalMode == orientation && ui.IsLayoutHorizontal == orientation,
                        "Opening settings preserves the profile orientation and rendered layout");
                    var draft = Field<Settings>(settings, "_draftCfg");
                    draft.PanelWidth = width + 20;
                    draft.HistoryEnabled = true;
                    draft.HorizontalMode = !orientation;
                    Check(cfg.PanelWidth == width && !cfg.HistoryEnabled, "Editing the settings draft does not mutate live configuration");
                    Check(cfg.HorizontalMode == orientation, "Draft orientation changes do not rotate the live panel");
                    Check(settings.CancelButton is IButtonControl, "Settings provides a Cancel action");
                    settings.CancelButton!.PerformClick();
                }
                Check(cfg.PanelWidth == width && !cfg.HistoryEnabled && SettingsHelper.Load().PanelWidth == width, "Cancel discards draft without changing persisted configuration");
                Check(cfg.HorizontalMode == orientation && SettingsHelper.Load().HorizontalMode == orientation && ui.IsLayoutHorizontal == orientation,
                    "Cancel preserves both persisted orientation and the live layout");

                _stage = "settings apply and history persistence";
                using (var settings = new SettingsForm(cfg, ui, main))
                {
                    settings.Show(main);
                    settings.SwitchPage("System");
                    await Task.Delay(250);
                    Capture(settings, "settings_light_system");
                    var draft = Field<Settings>(settings, "_draftCfg");
                    draft.PanelWidth = width + 20;
                    draft.HistoryEnabled = true;
                    Invoke(settings, "ApplySettings");
                    var persisted = SettingsHelper.Load();
                    Check(cfg.PanelWidth == width + 20 && persisted.PanelWidth == width + 20 && persisted.HistoryEnabled, "Explicit Apply commits width and history preference");
                    Check(cfg.HorizontalMode == orientation && persisted.HorizontalMode == orientation, "Applying other settings preserves orientation");
                    draft.Skin = "MellowDark";
                    draft.Language = "zh";
                    Invoke(settings, "ApplySettings");
                    Check(settings.BackColor == LiteMonitor.src.UI.Controls.UIColors.MainBg &&
                        Field<string>(settings, "_currentKey") == "System" && settings.Text.Contains("设置"),
                        "Apply updates the open settings palette and language while preserving its page");
                    draft.Skin = "MellowLight";
                    draft.Language = "en";
                    Invoke(settings, "ApplySettings");
                    draft.PanelWidth = width + 40;
                    settings.CancelButton!.PerformClick();
                }
                Check(cfg.PanelWidth == width + 20 && SettingsHelper.Load().PanelWidth == width + 20, "Cancel after Apply preserves the last applied state");
                using (var settings = new SettingsForm(cfg, ui, main))
                {
                    Field<Settings>(settings, "_draftCfg").HistoryEnabled = false;
                    Invoke(settings, "ApplySettings");
                }
                Check(!SettingsHelper.Load().HistoryEnabled, "History can be turned off and persisted");
                AssertSeedHistorySurvives();

                _stage = "dark theme and history forms";
                cfg.Skin = "DarkFlat_Classic";
                ui.ApplyTheme(cfg.Skin);
                await Task.Delay(180);
                Capture(main, "main_dark");
                using (var settings = new SettingsForm(cfg, ui, main))
                {
                    settings.Show(main);
                    settings.SwitchPage("System");
                    await Task.Delay(250);
                    Capture(settings, "settings_dark_system");
                    settings.SwitchPage("Monitor");
                    await Task.Delay(120);
                    Capture(settings, "settings_dark_metrics");
                    settings.Close();
                }
                using (var history = new HardwareTrendForm(cfg))
                {
                    history.Show(main);
                    await Task.Delay(150);
                    Capture(history, "history_dark_load");
                    history.Close();
                }
                using (var traffic = new TrafficHistoryForm(cfg))
                {
                    traffic.Show(main);
                    await Task.Delay(150);
                    Capture(traffic, "history_dark_traffic");
                    traffic.Close();
                }
                Check(Captures.Count == 16, "All nine DPI renderings and seven native form captures were saved");
                _stage = "power and taskbar recovery";
                await TestLifecycle(main, cfg, ui);
                _stage = "paused-history shutdown";
                ui.SetPowerState(suspended: true);
                while (Field<bool>(ui, "_busy")) await Task.Delay(20);
                cfg.HistoryEnabled = false;
                var beforeClose = TrafficLogger.GetTodayStats();
                var pendingSample = Task.Run(async () => { await Task.Delay(300); TrafficLogger.AddTraffic(123, 456); });
                typeof(UIController).GetField("_samplingTask", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui, pendingSample);
                int nativeDpi = main.DeviceDpi;
                main.Close();
                Check(!main.IsDisposed, "Close waits asynchronously for the in-flight sample");
                await pendingSample;
                for (int i = 0; i < 100 && !main.IsDisposed; i++) await Task.Delay(20);
                Check(main.IsDisposed, "Window closes after the in-flight sample drains");
                var flushed = JsonSerializer.Deserialize<TrafficData>(File.ReadAllText(AppPaths.DataFile("TrafficHistory.json")))!;
                var today = flushed.History[DateTime.Today.ToString("yyyy-MM-dd")];
                Check(today.Upload == beforeClose.up + 123 && today.Download == beforeClose.down + 456,
                    "Paused history retains the final pending traffic sample on shutdown");
                AssertSeedHistorySurvives();
                _exitCode = 0;
                WriteReport(null, nativeDpi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL during " + _stage + ": " + ex);
                WriteReport(ex.ToString(), main?.DeviceDpi);
            }
            finally
            {
                if (main != null && !main.IsDisposed) { main.Close(); await Task.Delay(100); }
                main?.Dispose();
                host.Close();
            }
        };
        try { Application.Run(host); }
        catch (Exception ex) { WriteReport(ex.ToString(), null); Console.Error.WriteLine(ex); }
        return _exitCode;
    }

    private static void TestMetricRecovery()
    {
        var metric = new MetricItem { Key = "CPU.Load", BoundConfig = new MonitorItemConfig { Key = "CPU.Load" } };
        foreach (bool horizontal in new[] { false, true })
        {
            metric.Value = metric.DisplayValue = 27;
            string valid = metric.GetFormattedText(horizontal);
            Check(valid != "—" && valid.Contains("27"), $"{horizontal}: valid CPU reading is formatted");
            foreach (float? absent in new float?[] { null, float.NaN, float.PositiveInfinity })
            {
                metric.Value = absent;
                Check(metric.GetFormattedText(horizontal) == "—" && metric.CachedPercent == 0 && metric.CachedUnitText == "", $"{horizontal}: unavailable reading is neutral ({absent?.ToString() ?? "missing"})");
                metric.Value = metric.DisplayValue = 27;
                Check(metric.GetFormattedText(horizontal) == valid, $"{horizontal}: identical reading recovers after unavailability");
            }
            metric.Value = metric.DisplayValue = 0;
            string zero = metric.GetFormattedText(horizontal);
            Check(zero != "—" && zero.Contains('0') && metric.CachedPercent == 0,
                $"{horizontal}: a real zero remains numeric with zero progress");
        }
    }

    private static async Task TestLifecycle(MainForm main, Settings cfg, UIController ui)
    {
        var timer = Field<System.Windows.Forms.Timer>(ui, "_timer");
        timer.Stop();
        while (Field<bool>(ui, "_busy")) await Task.Delay(20);
        var monitor = Field<LiteMonitor.src.SystemServices.HardwareMonitor>(ui, "_mon");
        int updates = 0;
        monitor.OnValuesUpdated += () => Interlocked.Increment(ref updates);
        async Task TickAndDrain()
        {
            Invoke(ui, "Tick");
            for (int i = 0; i < 100 && Field<bool>(ui, "_busy"); i++) await Task.Delay(20);
            Check(!Field<bool>(ui, "_busy"), "Sampling cycle completes");
        }
        cfg.HistoryEnabled = false;
        ui.SetPowerState(locked: true);
        await TickAndDrain();
        Check(updates == 0 && ui.RenderingPaused && ui.EffectiveRefreshMs == 10000, "Locked without history skips sampling and pauses drawing");
        ui.SetPowerState(locked: false, suspended: true);
        await TickAndDrain();
        Check(updates == 0, "Suspended state skips sampling");
        ui.SetPowerState(suspended: false);
        await TickAndDrain();
        Check(updates == 1 && !ui.RenderingPaused, "Resume restarts sampling");
        cfg.HistoryEnabled = true;
        ui.SetPowerState(locked: true);
        await TickAndDrain();
        Check(updates == 2 && timer.Interval == 10000, "Locked with history retains a slow sampling cadence");
        ui.SetPowerState(locked: false);
        main.Hide();
        cfg.HistoryEnabled = false;
        await TickAndDrain();
        Check(updates == 2, "Hidden with history off skips sampling");
        cfg.HistoryEnabled = true;
        await TickAndDrain();
        Check(updates == 3 && timer.Interval == 10000, "Hidden with history on continues slow sampling");
        main.Show();
        cfg.HistoryEnabled = false;
        cfg.ShowTaskbar = true;
        main.ToggleTaskbar(true);
        await Task.Delay(150);
        var first = Field<TaskbarForm>(main, "_taskbar");
        Check(first.IsHandleCreated && !first.IsDisposed, "Taskbar strip creates its native window");
        int messageId = Field<int>(main, "_wmTaskbarCreated");
        Check(messageId != 0, "TaskbarCreated recovery message is registered");
        object[] message = { Message.Create(main.Handle, messageId, IntPtr.Zero, IntPtr.Zero) };
        typeof(MainForm).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, message);
        await Task.Delay(3300);
        var recovered = Field<TaskbarForm>(main, "_taskbar");
        Check(first.IsDisposed && !ReferenceEquals(first, recovered) && recovered.IsHandleCreated,
            "Simulated TaskbarCreated message replaces the old strip");
        cfg.ShowTaskbar = false;
        main.ToggleTaskbar(false);
        Check(recovered.IsDisposed, "Taskbar toggle releases the recovered window");
    }

    private static void TestLayouts(Settings cfg)
    {
        // Give the illustrative memory reading a known capacity before native sampling starts.
        Settings.DetectedRamTotalGB = 32;
        foreach (string skin in new[] { "MellowLight", "MellowDark", "DarkFlat_Classic" })
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            var previous = ThemeManager.Current;
            var theme = ThemeManager.Load(skin);
            Check(theme.Name == (skin == "MellowLight" ? "Mellow Light" : skin == "MellowDark" ? "Mellow Dark" : "MinimalDarkFlat v2.0"), skin + " parses without fallback");
            if (previous != theme) previous.DisposeFonts();
            theme.Scale(scale, 1);
            // A bitmap's DPI metadata does not change the native GDI HDC DPI used by
            // TextRenderer. Pixel fonts make target-DPI glyph sizes explicit, independent
            // of the host desktop scale. Native application forms retain point fonts.
            NormalizeSyntheticFonts(theme, 96 * scale);
            theme.Layout.Width = (int)Math.Round(cfg.PanelWidth * scale);
            var groups = cfg.MonitorItems.Where(x => x.VisibleInPanel).GroupBy(x => x.UIGroup)
                .Select(group => new GroupLayoutInfo(group.Key, group.Select(item => new MetricItem {
                    Key = item.Key, BoundConfig = item, Label = LanguageManager.T("Items." + item.Key),
                    Value = item.Key.StartsWith("NET.") ? 65536 : 37,
                    DisplayValue = item.Key.StartsWith("NET.") ? 65536 : 37
                }).ToList()) { Label = LanguageManager.T("Groups." + group.Key) }).ToList();
            using var measureBitmap = new Bitmap(1, 1);
            measureBitmap.SetResolution(96 * scale, 96 * scale);
            using var measureGraphics = Graphics.FromImage(measureBitmap);
            int height = new UILayout(theme).Build(groups, graphics: measureGraphics);
            using var bitmap = new Bitmap(theme.Layout.Width, height);
            bitmap.SetResolution(96 * scale, 96 * scale);
            using var graphics = Graphics.FromImage(bitmap);
            var canvas = new Rectangle(Point.Empty, bitmap.Size);
            foreach (var group in groups)
            {
                Check(canvas.Contains(group.Bounds), $"{skin} {scale:P0}: group stays inside canvas");
                foreach (var item in group.Items)
                {
                    Check(group.Bounds.Contains(item.Bounds) && item.LabelRect.Width > 0 && item.ValueRect.Width > 0, $"{skin} {scale:P0}: {item.Key} has usable bounds");
                    int textHeight = TextRenderer.MeasureText(graphics, item.GetFormattedText(false), theme.FontValue, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
                    Check(textHeight <= item.ValueRect.Height, $"{skin} {scale:P0}: {item.Key} value fits vertically at target DPI");
                }
            }
            UIRenderer.Render(graphics, groups, theme);
            Save(bitmap, $"render_{skin}_{(int)(scale * 100)}pct");
        }
    }

    private static void NormalizeSyntheticFonts(Theme theme, float targetDpi)
    {
        Font Pixels(Font pointFont)
        {
            var pixelFont = new Font(pointFont.FontFamily, pointFont.SizeInPoints * targetDpi / 72f,
                pointFont.Style, GraphicsUnit.Pixel);
            if (!pointFont.IsSystemFont) pointFont.Dispose();
            return pixelFont;
        }
        theme.FontTitle = Pixels(theme.FontTitle);
        theme.FontGroup = Pixels(theme.FontGroup);
        theme.FontItem = Pixels(theme.FontItem);
        theme.FontValue = Pixels(theme.FontValue);
    }

    private static void SeedHistory()
    {
        string yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
        var traffic = new TrafficData { History = new() { [yesterday] = new DailyRecord { Upload = 1048576, Download = 4194304 } } };
        var history = new HardwareHistoryData { Points = Enumerable.Range(1, 5).Select(i => new HardwareHistoryPoint {
            Time = DateTime.Now.AddMinutes(-30 + i * 3), Values = new() {
                ["CPU.Load"] = new HardwareHistoryValue { Avg = 20 + i * 5, Min = 18, Max = 55, Count = 6 },
                ["MEM.Load"] = new HardwareHistoryValue { Avg = 44 + i, Min = 43, Max = 52, Count = 6 }
            }
        }).ToList() };
        File.WriteAllText(AppPaths.DataFile("TrafficHistory.json"), JsonSerializer.Serialize(traffic, JsonOptions));
        File.WriteAllText(AppPaths.DataFile("HardwareHistory.json"), JsonSerializer.Serialize(history, JsonOptions));
    }

    private static void AssertSeedHistorySurvives()
    {
        var traffic = JsonSerializer.Deserialize<TrafficData>(File.ReadAllText(AppPaths.DataFile("TrafficHistory.json")))!;
        var history = JsonSerializer.Deserialize<HardwareHistoryData>(File.ReadAllText(AppPaths.DataFile("HardwareHistory.json")))!;
        Check(traffic.History[DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")].Download == 4194304, "History toggle retains previous traffic records on disk");
        Check(history.Points.Count >= 5 && history.Points.Any(x => x.Values.TryGetValue("MEM.Load", out var value) && value.Avg == 45), "History toggle retains previous sensor records on disk");
    }

    private static T Field<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException("Missing test integration field: " + name));

    private static void Invoke(object instance, string name) =>
        (instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing test integration method: " + name)).Invoke(instance, null);

    private static void Capture(Form form, string name)
    {
        Check(form.Visible && form.Width > 0 && form.Height > 0, name + " is a shown form");
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        Save(bitmap, name);
    }

    private static void Save(Bitmap bitmap, string name)
    {
        string path = Path.Combine(_artifacts, name + ".png");
        bitmap.Save(path, ImageFormat.Png);
        Captures.Add(path);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Passed.Add(description);
        Console.WriteLine("PASS: " + description);
    }

    private static void WriteReport(string? error, int? nativeDpi)
    {
        File.WriteAllText(Path.Combine(_artifacts, "results.json"), JsonSerializer.Serialize(new {
            success = error == null && _exitCode == 0, stage = _stage, error, nativeDpi,
            passed = Passed, captures = Captures, dataDirectory = Path.Combine(_artifacts, "data"),
            note = "Native form captures use the host OS DPI. render_* images exercise the actual application renderer at 96, 144 and 192 DPI; they do not emulate native multi-monitor DPI transitions."
        }, JsonOptions));
    }
}
