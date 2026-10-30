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
        legacy.MonitorItems.Single(x => x.Key == "CPU.Pump").VisibleInPanel = true;
        legacy.MonitorItems.Single(x => x.Key == "CASE.Fan").VisibleInPanel = true;
        legacy.MonitorItems.Add(new MonitorItemConfig { Key = "DASH.old-plugin", VisibleInPanel = true });
        legacy.PluginInstances.Add(new PluginInstanceConfig { Id = "legacy", Enabled = true });
        legacy.Save();
        string original = File.ReadAllText(SettingsHelper.FilePath);
        var migrated = SettingsHelper.Load(true);
        Check(File.ReadAllText(AppPaths.DataFile("settings.pre-v0.2.json")) == original, "Migration keeps an exact dedicated backup");
        Check(migrated.Skin == legacy.Skin && migrated.MonitorItems.Count == 32 && migrated.MonitorItems.Single(x => x.Key == "CPU.Pump").VisibleInPanel && migrated.MonitorItems.All(x => x.Key != "DASH.old-plugin"), "Migration preserves custom visibility and restores all built-in metrics");
        Check(!migrated.WebServerEnabled && !migrated.AutoCheckUpdate && migrated.PreferredCpuFan == "old manual port" && migrated.PluginInstances.All(x => !x.Enabled), "Legacy flags cannot reactivate retired services");
        migrated.Save();
        var again = SettingsHelper.Load(true);
        Check(JsonSerializer.Serialize(again) == JsonSerializer.Serialize(migrated) && File.ReadAllText(AppPaths.DataFile("settings.pre-v0.2.json")) == original, "Migration is idempotent and never replaces its backup");
        cfg.Save();
        foreach (var name in new[] { "LiteMonitor.src.Plugins.PluginManager", "LiteMonitor.src.WebServer.LiteWebServer", "LiteMonitor.src.SystemServices.UpdateChecker" })
            Check(typeof(Settings).Assembly.GetType(name) == null, "Retired type is absent: " + name);
    }

    private static async Task TestIteration(MainForm main, Settings cfg, UIController ui)
    {
        using (var settings = new SettingsForm(cfg, ui, main)) {
            settings.Show(main);
            await Task.Delay(60);
            var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            Check(pages.Count == 1 && pages.ContainsKey("MainPanel"), "Opening settings constructs only the first page");
            var draft = Field<Settings>(settings, "_draftCfg");
            settings.SwitchPage("System");
            var group = Descendants(pages["System"]).OfType<LiteSettingsGroup>().First(c => c.Collapsible);
            group.Collapsed = false;
            var parent = pages["System"].Parent;
            settings.SwitchPage("MainPanel"); settings.SwitchPage("System");
            Check(parent != null && ReferenceEquals(parent, pages["System"].Parent) && pages.Values.All(p => ReferenceEquals(p.Parent, parent)), "Visited pages stay attached to one content host");
            Check(!group.Collapsed, "Switching categories preserves the page's disclosure state");
            draft.PanelWidth = cfg.PanelWidth + 20;
            cfg.Skin = "MellowDark";
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            Check(Descendants(Field<Dictionary<string, SettingsPageBase>>(settings, "_pages")["System"]).OfType<LiteSettingsGroup>().First(c => c.Collapsible).Collapsed == false, "Theme reconstruction restores the expanded group");
            Check(Field<Settings>(settings, "_draftCfg").PanelWidth == cfg.PanelWidth + 20 && Field<string>(settings, "_currentKey") == "System", "Theme update retains pending values and the selected category");
            var theme = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Helpers.WindowTheme")!;
            bool backdrop = (bool)theme.GetMethod("HasBackdrop", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { settings })!;
            Check(!backdrop, "Solid settings never enable the Acrylic backdrop");
            settings.CancelButton!.PerformClick();
        }

        cfg.Skin = "MellowLight";
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        Check(ThemeManager.Current.Material == "solid", "Retained theme uses a solid material");
        cfg.GlassEffectsEnabled = false;
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        Check(typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.GlassSurface") == null,
            "Retired glass painting and its bitmap cache are absent from the assembly");
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
