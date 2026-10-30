using System.Reflection;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

namespace DesktopSmoke;

internal static partial class SmokeSuite
{
    private static void TestMigration(Settings cfg)
    {
        var legacy = cfg.DeepClone();
        legacy.Skin = "AuroraDream_Purple";
        legacy.SettingsSchemaVersion = 0;
        legacy.WebServerEnabled = true;
        legacy.PreferredCpuFan = "old manual port";
        legacy.AutoCheckUpdate = true;
        legacy.MonitorItems.Add(new MonitorItemConfig { Key = "CPU.Pump", VisibleInPanel = true });
        legacy.MonitorItems.Add(new MonitorItemConfig { Key = "CASE.Fan", VisibleInPanel = true });
        legacy.MonitorItems.Add(new MonitorItemConfig { Key = "DASH.old-plugin", VisibleInPanel = true });
        legacy.PluginInstances.Add(new PluginInstanceConfig { Id = "legacy", Enabled = true });
        legacy.Save();
        string original = File.ReadAllText(SettingsHelper.FilePath);
        var migrated = SettingsHelper.Load(true);
        Check(File.ReadAllText(AppPaths.DataFile("settings.pre-v0.2.json")) == original, "Migration keeps an exact dedicated backup");
        Check(migrated.Skin == legacy.Skin && migrated.MonitorItems.All(x => x.Key is not ("CPU.Pump" or "CASE.Fan" or "DASH.old-plugin")), "Migration preserves the chosen theme and retires unsupported metrics");
        Check(!migrated.WebServerEnabled && !migrated.AutoCheckUpdate && migrated.PreferredCpuFan == "" && migrated.PluginInstances.All(x => !x.Enabled), "Legacy flags cannot reactivate retired services");
        migrated.Save();
        var again = SettingsHelper.Load(true);
        Check(JsonSerializer.Serialize(again) == JsonSerializer.Serialize(migrated) && File.ReadAllText(AppPaths.DataFile("settings.pre-v0.2.json")) == original, "Migration is idempotent and never replaces its backup");
        cfg.Save();
        foreach (var name in new[] { "LiteMonitor.src.Plugins.PluginManager", "LiteMonitor.src.WebServer.LiteWebServer", "LiteMonitor.src.SystemServices.FpsCounter", "LiteMonitor.src.SystemServices.UpdateChecker" })
            Check(typeof(Settings).Assembly.GetType(name) == null, "Retired type is absent: " + name);
    }

    private static async Task TestIteration(MainForm main, Settings cfg, UIController ui)
    {
        using (var settings = new SettingsForm(cfg, ui, main)) {
            settings.Show(main);
            await Task.Delay(60);
            var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            Check(pages.Count == 1, "Settings only constructs the initial page");
            var apply = Field<LiteButton>(settings, "_applyButton");
            Check(!apply.Enabled, "Apply is disabled for an unchanged draft");
            cfg.Skin = "GlassLight";
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            Check(!Field<LiteButton>(settings, "_applyButton").Enabled, "External theme changes do not create an unsaved edit");
            pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            apply = Field<LiteButton>(settings, "_applyButton");
            settings.SwitchPage("System");
            await Task.Delay(150);
            Check(pages.Count == 2, "Only visited settings pages are cached");
            var history = Descendants(pages["System"]).OfType<LiteCheck>().Single(c => c.AccessibleName == LanguageManager.T("Menu.HistoryEnabled"));
            history.Checked = !history.Checked;
            await Task.Delay(40);
            Check(apply.Enabled, "Changing a bound control enables Apply");
            history.Checked = !history.Checked;
            await Task.Delay(40);
            Check(!apply.Enabled, "Reverting a bound control disables Apply again");
            settings.SwitchPage("Monitor");
            await Task.Delay(40);
            Check(!apply.Enabled, "Visiting the monitor editor does not mark normalized ordering as an edit");
            var row = Descendants(pages["Monitor"]).OfType<MonitorItemRow>().First();
            var rowToggle = Descendants(row).OfType<LiteCheck>().First();
            rowToggle.Checked = !rowToggle.Checked;
            await Task.Delay(40);
            Check(apply.Enabled, "Unapplied monitor-row edits enable Apply");
            rowToggle.Checked = !rowToggle.Checked;
            await Task.Delay(40);
            Check(!apply.Enabled, "Reverting a monitor-row edit clears the dirty state");
            Field<Button>(pages["Monitor"], "_btnTabBar").PerformClick();
            Field<LiteCheck>(pages["Monitor"], "_chkEditLabels").Checked = true;
            cfg.Skin = "GlassDark";
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            Check(Field<bool>(pages["Monitor"], "_isTaskbarTab") && Field<LiteCheck>(pages["Monitor"], "_chkEditLabels").Checked, "Theme reconstruction retains monitor tab and label editor state");
            settings.SwitchPage("System");
            var group = Descendants(pages["System"]).OfType<LiteSettingsGroup>().First(c => c.Collapsible);
            group.Collapsed = false;
            settings.SwitchPage("MainPanel"); settings.SwitchPage("System");
            Check(!group.Collapsed, "Returning to a page retains expanded groups");
            var draft = Field<Settings>(settings, "_draftCfg");
            draft.PanelWidth += 20;
            cfg.Skin = "GlassDark";
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            Check(Field<Settings>(settings, "_draftCfg").PanelWidth == cfg.PanelWidth + 20 && Field<string>(settings, "_currentKey") == "System", "Theme reconstruction preserves pending edits and active page");
            settings.CancelButton!.PerformClick();
        }

        cfg.Skin = "GlassLight";
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        Check(ThemeManager.Current.Material == "glass" && ThemeManager.Current.Color.Accent == "#1766CE", "Glass material and accent parse independently of metric colors");
        cfg.GlassEffectsEnabled = false;
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        var effects = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.GlassSurface")!;
        Check(!(bool)effects.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!, "Explicitly disabling effects selects the solid fallback");
        cfg.GlassEffectsEnabled = true;
        AppActions.ApplyThemeAndLayout(cfg, ui, main);

        using var chart = new TrendChartControl { Size = new Size(520, 300) };
        var start = DateTime.Today;
        var points = Enumerable.Range(0, 10000).Select(i => new TrendChartPoint { Time = start.AddSeconds(i), Avg = i == 5010 ? 100 : 20, Min = 20, Max = i == 5010 ? 100 : 20, Count = 1 }).ToList();
        chart.SetSeries(new[] { new TrendChartSeries { Key = "CPU.Load", Label = "CPU", Points = points, Color = Color.Green } });
        using var bitmap = new Bitmap(520, 300);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        var geometry = Field<Dictionary<string, PointF[]>>(chart, "_geometry")["CPU.Load"];
        Check(geometry.Length < points.Count / 2 && geometry.Min(p => p.Y) < geometry.Max(p => p.Y) - 50, "Long-history geometry is reduced while retaining the narrow spike");
        var index = Field<List<DateTime>>(chart, "_times");
        var mouse = typeof(TrendChartControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 100; i++) mouse.Invoke(chart, new object[] { new MouseEventArgs(MouseButtons.None, 0, 70 + i * 3, 150, 0) });
        Check(ReferenceEquals(index, Field<List<DateTime>>(chart, "_times")) && points.Count == 10000, "Hover reuses the time index and leaves full export data intact");
        chart.RefreshTheme(new[] { new TrendChartSeries { Key = "CPU.Load", Label = "Processor", Color = Color.Blue } });
        Check(Field<List<TrendChartSeries>>(chart, "_series")[0].Color == Color.Blue && ReferenceEquals(index, Field<List<DateTime>>(chart, "_times")), "Theme refresh updates copied series styles without rebuilding the data index");
        chart.Size = new Size(700, 300);
        using var wider = new Bitmap(700, 300);
        chart.DrawToBitmap(wider, chart.ClientRectangle);
        Check(!ReferenceEquals(geometry, Field<Dictionary<string, PointF[]>>(chart, "_geometry")["CPU.Load"]), "Resizing invalidates chart coordinates");
    }
}
