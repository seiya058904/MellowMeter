using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;

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

internal static partial class SmokeSuite
{
    private static readonly List<string> Passed = new();
    private static readonly List<string> Captures = new();
    private static readonly string[] RenderThemes = { "MellowLight", "MellowDark", "DarkFlat_Classic" };
    private static readonly float[] RenderScales = { 1f, 1.5f, 2f };
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
            Console.Error.WriteLine("FAIL: 120-second timeout during " + _stage);
            File.WriteAllText(Path.Combine(_artifacts, "timeout.txt"), _stage);
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(120), Timeout.InfiniteTimeSpan);

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
                Check(cfg.MonitorItems.Where(x => x.VisibleInPanel).Select(x => x.Key).Order().SequenceEqual(new[] { "DASH.Uptime", "CPU.Load", "CPU.Temp", "GPU.Load", "GPU.Temp", "GPU.VRAM", "MEM.Load", "DISK.Read", "DISK.Write", "NET.Up", "NET.Down", "DATA.DayUp", "DATA.DayDown" }.Order()), "Fresh panel matches upstream defaults");
                Check(cfg.MonitorItems.Where(x => x.VisibleInTaskbar).Select(x => x.Key).Order().SequenceEqual(new[] { "CPU.Load", "CPU.Temp", "GPU.Load", "MEM.Load", "NET.Down", "NET.Up" }.Order()), "Fresh taskbar matches upstream defaults");
                Check(cfg.AdvancedSensorsEnabled && !cfg.WebServerEnabled && !cfg.AutoStart && cfg.HistoryEnabled, "Fresh profile enables full sensors and local history");
                Check(cfg.Skin == "DarkFlat_Classic" && cfg.TaskbarFontFamily == "Microsoft YaHei UI",
                    "Fresh profile uses DarkFlat Classic and preserves taskbar typography");
                cfg.Language = "en";
                LanguageManager.Load(cfg.Language);
                TestMigration(cfg);
                Check(!cfg.HorizontalMode && cfg.MonitorItems.Count == 32, "Fresh vertical profile exposes all 32 upstream metrics");
                cfg.AdvancedSensorsEnabled = false; // This fixture never opens physical sensors.
                cfg.InterfaceAnimationsEnabled = false; // Deterministic geometry; motion is tested separately.
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
                _stage = "theme linkage across open windows";
                await TestThemeLinkage(main, cfg, ui);
                _stage = "grouped settings layout and keyboard controls";
                await TestSettingsPages(main, cfg, ui);
                string[] requiredNativeCaptures = { "main_light", "settings_light_system", "main_dark",
                    "settings_dark_system", "settings_dark_metrics", "history_dark_load", "history_dark_traffic",
                    "settings_cyan", "settings_purple", "settings_amber",
                    "settings_dark_main", "settings_dark_taskbar", "settings_dark_threshold",
                    "settings_light_main", "settings_light_taskbar", "settings_light_metrics", "settings_light_threshold",
                    "settings_light_main_zh" };
                var capturedNames = Captures.Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet();
                Check(RenderThemes.SelectMany(theme => RenderScales.Select(scale => $"render_{theme}_{(int)(scale * 100)}pct")).All(capturedNames.Contains) &&
                    Captures.Count(path => Path.GetFileName(path).StartsWith("render_")) == RenderThemes.Length * RenderScales.Length &&
                    requiredNativeCaptures.All(capturedNames.Contains) && capturedNames.Count == Captures.Count &&
                    Captures.All(File.Exists), "All DPI, native-form and theme-linkage captures were saved without duplicates");
                foreach (var window in new Form[] { new HardwareTrendForm(cfg), new TrafficHistoryForm(cfg) }) {
                    using (window) {
                        window.Show(main); await Task.Delay(30);
                        window.Hide();
                        Check(!Field<System.Windows.Forms.Timer>(window, "_timer").Enabled, window.GetType().Name + ": hiding stops its UI clock");
                        window.Show(main);
                        Check(Field<System.Windows.Forms.Timer>(window, "_timer").Enabled, window.GetType().Name + ": showing resumes its UI clock");
                        window.Close();
                    }
                }
                _stage = "iteration regressions";
                await TestIteration(main, cfg, ui);
                await TestRestoreAuditAndMotion(main, cfg, ui);
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

    private static async Task TestThemeLinkage(MainForm main, Settings cfg, UIController ui)
    {
        using var settings = new SettingsForm(cfg, ui, main);
        using var history = new HardwareTrendForm(cfg);
        using var traffic = new TrafficHistoryForm(cfg);
        settings.Show(main);
        settings.SwitchPage("System");
        history.Show(main);
        traffic.Show(main);
        await Task.Delay(120);

        foreach (Form window in new Form[] { history, traffic }) {
            window.WindowState = FormWindowState.Minimized;
            await Task.Delay(20);
            Check(!Field<System.Windows.Forms.Timer>(window, "_timer").Enabled, window.GetType().Name + ": minimizing stops UI refresh");
            window.WindowState = FormWindowState.Normal;
            await Task.Delay(20);
            Check(Field<System.Windows.Forms.Timer>(window, "_timer").Enabled, window.GetType().Name + ": restoring resumes UI refresh");
        }

        int liveWidth = cfg.PanelWidth;
        int pendingWidth = liveWidth + 40;
        Field<Settings>(settings, "_draftCfg").PanelWidth = pendingWidth;
        var ranges = Field<System.Collections.IDictionary>(history, "_rangeButtons");
        foreach (System.Collections.DictionaryEntry range in ranges)
            if (range.Key.ToString() == "Day7") ((Button)range.Value!).PerformClick();
        Field<Dictionary<HistoryViewMode, Button>>(traffic, "_viewButtons")[HistoryViewMode.Monthly].PerformClick();
        Check(Field<object>(history, "_range").ToString() == "Day7" &&
            Field<HistoryViewMode>(traffic, "_currentMode") == HistoryViewMode.Monthly,
            "Theme linkage fixture has nondefault history selections");

        var variants = new[] {
            (Skin: "DarkFlat_Classic", Slug: "classic", CaptureSettings: false),
            (Skin: "CyberBlue_Neon", Slug: "cyan", CaptureSettings: true),
            (Skin: "AuroraDream_Purple", Slug: "purple", CaptureSettings: true),
            (Skin: "SolarAmber_Warm", Slug: "amber", CaptureSettings: true),
            (Skin: "MellowLight", Slug: "light", CaptureSettings: false)
        };
        int capturesBefore = Captures.Count;
        var taskbarAccents = new Dictionary<string, Color>();
        foreach (var variant in variants)
        {
            cfg.Skin = variant.Skin;
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            await Task.Delay(150);
            string expectedThemeName = variant.Skin == "DarkFlat_Classic" ? "MinimalDarkFlat v2.0" :
                variant.Skin == "MellowLight" ? "Mellow Light" : variant.Skin;
            Check(ThemeManager.Current.Name == expectedThemeName, variant.Skin + ": selected theme loads without fallback");
            Color background = ThemeManager.ParseColor(ThemeManager.Current.Color.Background);
            Color card = ThemeManager.ParseColor(ThemeManager.Current.Color.GroupBackground);
            Color border = UIColors.Border;
            Color text = ThemeManager.ParseColor(ThemeManager.Current.Color.TextPrimary);
            Check(main.BackColor == background && settings.BackColor == background &&
                history.BackColor == background && traffic.BackColor == background,
                variant.Skin + ": open float, settings and both histories share the selected background");
            Check(Field<string>(settings, "_currentKey") == "System" &&
                Field<Settings>(settings, "_draftCfg").PanelWidth == pendingWidth && cfg.PanelWidth == liveWidth,
                variant.Skin + ": refresh preserves the active settings page and unapplied width");
            Check(Field<object>(history, "_range").ToString() == "Day7" &&
                Field<HistoryViewMode>(traffic, "_currentMode") == HistoryViewMode.Monthly,
                variant.Skin + ": history range and traffic period survive refresh");
            var chart = Field<TrendChartControl>(history, "_chart");
            var gridColor = (Color)typeof(TrendChartControl).GetProperty("_gridColor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
            Check(chart.BackColor == card && chart.ForeColor == text && gridColor == border,
                variant.Skin + ": the existing chart adopts background, text and grid colors");
            foreach (var historyGrid in new[] { Field<DataGridView>(history, "_grid"), Field<DataGridView>(traffic, "_grid") })
                Check(historyGrid.BackgroundColor == card && historyGrid.GridColor == border &&
                    historyGrid.DefaultCellStyle.ForeColor == text,
                    variant.Skin + ": history grid uses the selected palette");

            var stateColor = typeof(TaskbarRenderer).GetMethod("GetStateColor", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (bool lightSystem in new[] { false, true })
            {
                Color surface = lightSystem ? Color.FromArgb(210, 210, 210) : Color.FromArgb(40, 40, 40);
                for (int state = 0; state <= 2; state++)
                {
                    Color color = (Color)stateColor.Invoke(null, new object[] { state, lightSystem })!;
                    Check(ContrastRatio(color, surface) >= 4.5,
                        variant.Skin + $": taskbar state {state} has readable contrast on {(lightSystem ? "light" : "dark")} system chrome");
                    if (state == 0 && !lightSystem) taskbarAccents[variant.Slug] = color;
                }
                using var bitmap = new Bitmap(320, 72);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(surface);
                    var metric = new MetricItem { Key = "CPU.Load", Label = "CPU", ShortLabel = "CPU",
                        Value = 27, DisplayValue = 27, BoundConfig = new MonitorItemConfig { Key = "CPU.Load" } };
                    TaskbarRenderer.Render(graphics, new List<Column> {
                        new Column { Top = metric, Bounds = new Rectangle(12, 12, 296, 48) }
                    }, lightSystem);
                }
                Save(bitmap, $"taskbar_{variant.Slug}_{(lightSystem ? "light" : "dark")}_system");
            }
            if (variant.CaptureSettings)
            {
                settings.BringToFront();
                Capture(settings, "settings_" + variant.Slug);
            }
        }
        Check(new[] { "cyan", "purple", "amber" }.Select(key => taskbarAccents[key].ToArgb()).Distinct().Count() == 3,
            "Cyan, purple and amber selections produce distinct taskbar accents");
        Check(Captures.Count == capturesBefore + variants.Length * 2 + variants.Count(variant => variant.CaptureSettings),
            "Each selected theme has both taskbar renders and each requested settings capture");
        settings.CancelButton!.PerformClick();
        Check(cfg.PanelWidth == liveWidth, "Closing themed settings does not commit the preserved draft");
        history.Close();
        traffic.Close();
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte component)
            {
                double value = component / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static async Task TestSettingsPages(MainForm main, Settings cfg, UIController ui)
    {
        var pages = new[] { (Key: "MainPanel", Slug: "main"), (Key: "Taskbar", Slug: "taskbar"),
            (Key: "Monitor", Slug: "metrics"), (Key: "Threshold", Slug: "threshold"), (Key: "System", Slug: "system") };
        int collapsedGroupsChecked = 0;
        foreach (var appearance in new[] { (Skin: "DarkFlat_Classic", Slug: "dark"), (Skin: "MellowLight", Slug: "light") })
        {
            cfg.Skin = appearance.Skin;
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            using var settings = new SettingsForm(cfg, ui, main);
            settings.Show(main);
            foreach (var page in pages)
            {
                settings.SwitchPage(page.Key);
                await Task.Delay(120);
                Control content = Field<Control>(settings, "_pnlContent");
                Control activePage = content.Controls.Cast<Control>().Single(control => control.Visible);
                string label = appearance.Slug + " " + page.Slug;
                string captureName = $"settings_{appearance.Slug}_{page.Slug}";
                // Three of these views already have captures from the retained Apply/Cancel tests.
                if (!Captures.Any(path => Path.GetFileNameWithoutExtension(path) == captureName)) Capture(settings, captureName);
                CheckSettingsRowGeometry(activePage, label);
                if (page.Key == "Monitor")
                {
                    var editLabels = Field<LiteCheck>(activePage, "_chkEditLabels");
                    Check(!editLabels.Checked, label + ": label and unit editing stays out of the default monitor list");
                    editLabels.Checked = true;
                    CheckSettingsRowGeometry(activePage, label + " with label editors");
                    var cpuRow = Descendants(activePage).OfType<MonitorItemRow>().Single(row => row.Config.Key == "CPU.Load");
                    var nameInput = Field<LiteUnderlineInput>(cpuRow, "_inputName");
                    string previousLabel = nameInput.Inner.Text;
                    string liveLabel = cfg.MonitorItems.Single(item => item.Key == "CPU.Load").UserLabel;
                    nameInput.Inner.Text = "Processor preview";
                    editLabels.Checked = false;
                    Check(Field<List<MonitorItemConfig>>(activePage, "_workingList").Single(item => item.Key == "CPU.Load").UserLabel == "Processor preview" &&
                        cfg.MonitorItems.Single(item => item.Key == "CPU.Load").UserLabel == liveLabel,
                        label + ": hiding label editors preserves the pending label without changing live settings");
                    editLabels.Checked = true;
                    cpuRow = Descendants(activePage).OfType<MonitorItemRow>().Single(row => row.Config.Key == "CPU.Load");
                    Field<LiteUnderlineInput>(cpuRow, "_inputName").Inner.Text = previousLabel;
                    editLabels.Checked = false;
                    Field<Button>(activePage, "_btnTabBar").PerformClick();
                    await Task.Delay(30);
                    CheckSettingsRowGeometry(activePage, label + " taskbar tab");
                    editLabels.Checked = true;
                    CheckSettingsRowGeometry(activePage, label + " taskbar label editors");
                    editLabels.Checked = false;
                    Field<Button>(activePage, "_btnTabMain").PerformClick();
                }

                var groups = Descendants(activePage).OfType<LiteSettingsGroup>()
                    .Where(group => group.Visible && group.Collapsible).ToList();
                foreach (var group in groups)
                {
                    await TestCollapsedGroup(settings, group, label);
                    collapsedGroupsChecked++;
                }
                if (groups.Count > 0)
                {
                    var originalStates = groups.Select(group => group.Collapsed).ToArray();
                    foreach (var group in groups) group.Collapsed = false;
                    activePage.PerformLayout();
                    await Task.Delay(30);
                    CheckSettingsRowGeometry(activePage, label + " with optional groups expanded");
                    for (int i = 0; i < groups.Count; i++) groups[i].Collapsed = originalStates[i];
                }
                if (page.Key == "System") TestBoundHistoryToggle(settings, activePage, label);
            }
            settings.CancelButton!.PerformClick();
        }
        Check(collapsedGroupsChecked > 0, "Optional settings groups were exercised instead of silently skipping collapse tests");

        string language = LanguageManager.CurrentLang;
        string liveLanguage = cfg.Language;
        string persistedLanguage = SettingsHelper.Load().Language;
        try
        {
            LanguageManager.Load("zh");
            using var variantChinese = new SettingsForm(cfg, ui, main);
            variantChinese.Show(main);
            variantChinese.SwitchPage("MainPanel");
            await Task.Delay(120);
            Check(variantChinese.Text.Contains("设置"), "Chinese settings variant uses translated labels");
            Capture(variantChinese, "settings_light_main_zh");
            variantChinese.CancelButton!.PerformClick();
        }
        finally { LanguageManager.Load(language); cfg.SyncToLanguage(); }
        Check(cfg.Language == liveLanguage && SettingsHelper.Load().Language == persistedLanguage,
            "Chinese capture leaves live and persisted language preferences unchanged");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CheckSettingsRowGeometry(Control page, string label)
    {
        var rows = Descendants(page).Where(control => control.Visible &&
            control is LiteSettingsItem or LiteActionRow or LiteThresholdRow or MonitorItemRow or MonitorGroupHeader).ToList();
        if (page.GetType().Name == "MonitorPage")
        {
            rows.Add(Field<Panel>(page, "_tabPanel"));
            rows.Add(Field<Panel>(page, "_headerPanel"));
        }
        Check(rows.Count > 0, label + ": populated settings rows are available for geometry checks");
        foreach (Control row in rows)
        {
            row.PerformLayout();
            var fields = row.Controls.Cast<Control>().Where(control => control.Visible && control.Width > 0 && control.Height > 0).ToList();
            foreach (Control field in fields)
            {
                if (!row.ClientRectangle.Contains(field.Bounds))
                    throw new InvalidOperationException($"{label}: {row.GetType().Name} field {field.GetType().Name} '{field.Text}' bounds {field.Bounds} escape row {row.ClientRectangle}.");
                if (field is Label text && !string.IsNullOrWhiteSpace(text.Text))
                {
                    // Native Label uses GDI by default. Font.Height is a GDI+
                    // metric and can be larger at 96 DPI despite complete text.
                    using var graphics = text.CreateGraphics();
                    int lineHeight = text.UseCompatibleTextRendering
                        ? (int)Math.Ceiling(graphics.MeasureString(text.Text, text.Font).Height)
                        : TextRenderer.MeasureText(graphics, text.Text, text.Font, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
                    if (text.Height + 1 < lineHeight)
                        throw new InvalidOperationException($"{label}: label '{text.Text}' height {text.Height} cannot fit its rendered line height {lineHeight}.");
                }
            }
            for (int i = 0; i < fields.Count; i++)
            for (int j = i + 1; j < fields.Count; j++)
                if (fields[i].Bounds.IntersectsWith(fields[j].Bounds))
                    throw new InvalidOperationException($"{label}: '{fields[i].Text}' ({fields[i].Bounds}) overlaps '{fields[j].Text}' ({fields[j].Bounds}) in {row.GetType().Name}.");
        }
        Check(true, label + $": {rows.Count} visible rows keep labels and fields separate and inside their bounds");
    }

    private static async Task TestCollapsedGroup(SettingsForm settings, LiteSettingsGroup group, string label)
    {
        bool originalCollapsed = group.Collapsed;
        group.Collapsed = false;
        settings.PerformLayout();
        await Task.Delay(20);
        var header = Field<Button>(group, "_titleButton");
        Check(header.TabStop && header.CanSelect, label + ": optional group header is keyboard reachable");
        Func<string> readPendingValue;
        var numeric = Descendants(group).OfType<TextBox>().FirstOrDefault(text => text.Enabled && !text.ReadOnly && int.TryParse(text.Text, out _));
        // Exercise a harmless preference. Visibility guardrails intentionally open
        // a dialog, and advanced sensors expose hardware-specific controls.
        var excluded = new[] { "Menu.HideMainForm", "Menu.HideTrayIcon", "Menu.TaskbarShow", "Menu.AdvancedSensors" }
            .Select(LanguageManager.T).ToHashSet();
        var toggle = Descendants(group).OfType<CheckBox>().FirstOrDefault(check => check.Enabled &&
            !excluded.Contains(check.AccessibleName ?? check.Text));
        if (numeric != null)
        {
            numeric.Text = (int.Parse(numeric.Text) + 1).ToString();
            readPendingValue = () => numeric.Text;
        }
        else if (toggle != null)
        {
            toggle.Checked = !toggle.Checked;
            readPendingValue = () => toggle.Checked.ToString();
        }
        else
        {
            var input = Descendants(group).OfType<TextBox>().FirstOrDefault(text => text.Enabled && !text.ReadOnly);
            readPendingValue = input != null ? () => input.Text : () => JsonSerializer.Serialize(Field<Settings>(settings, "_draftCfg"));
        }
        string pending = readPendingValue();
        string draft = JsonSerializer.Serialize(Field<Settings>(settings, "_draftCfg"));
        int expandedHeight = group.Height;
        header.PerformClick();
        settings.PerformLayout();
        await Task.Delay(20);
        Check(group.Collapsed && group.Height < expandedHeight, label + ": optional group collapses and releases its body space");
        PressSpace(header);
        settings.PerformLayout();
        await Task.Delay(20);
        Check(!group.Collapsed, label + ": Space expands the focused group header");
        Check(readPendingValue() == pending && JsonSerializer.Serialize(Field<Settings>(settings, "_draftCfg")) == draft,
            label + ": collapse and keyboard expansion preserve pending field and draft values");
        group.Collapsed = originalCollapsed;
    }

    private static void TestBoundHistoryToggle(SettingsForm settings, Control page, string label)
    {
        string historyLabel = LanguageManager.T("Menu.HistoryEnabled");
        var row = Descendants(page).OfType<LiteSettingsItem>().Single(item => item.Label.Text == historyLabel);
        var toggle = Descendants(row).OfType<CheckBox>().Single();
        var draft = Field<Settings>(settings, "_draftCfg");
        bool original = toggle.Checked;
        int changes = 0;
        EventHandler changed = (_, _) => changes++;
        toggle.CheckedChanged += changed;
        try
        {
            toggle.Checked = !original;
            Check(changes == 1 && draft.HistoryEnabled == !original, label + ": pill checkbox preserves CheckedChanged draft binding");
            PressSpace(toggle);
            Check(changes == 2 && toggle.Checked == original && draft.HistoryEnabled == original,
                label + ": Space toggles the focused pill checkbox exactly once and updates its draft");
        }
        finally { toggle.CheckedChanged -= changed; toggle.Checked = original; }
    }

    private static void PressSpace(Control target)
    {
        for (Control? parent = target.Parent; parent != null; parent = parent.Parent)
            if (parent is ScrollableControl scroll && scroll.AutoScroll) scroll.ScrollControlIntoView(target);
        target.FindForm()?.Activate();
        Check(target.Focus(), target.GetType().Name + ": test keyboard target receives focus");
        var wndProc = typeof(Control).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] down = { Message.Create(target.Handle, 0x0100, (IntPtr)Keys.Space, (IntPtr)0x00390001) };
        object[] up = { Message.Create(target.Handle, 0x0101, (IntPtr)Keys.Space, new IntPtr(unchecked((int)0xC0390001))) };
        wndProc.Invoke(target, down);
        wndProc.Invoke(target, up);
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
        foreach (string skin in RenderThemes)
        foreach (float scale in RenderScales)
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
