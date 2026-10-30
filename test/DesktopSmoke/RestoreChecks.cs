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
    private static async Task TestRestoreAuditAndMotion(MainForm main, Settings cfg, UIController ui)
    {
        _stage = "restored defaults and audit regressions";
        TestPerformanceDraftSafety(main, cfg, ui);
        await TestSelectiveApplyAndSources(main, cfg, ui);
        var compact = cfg.DeepClone();
        compact.SettingsSchemaVersion = 2;
        compact.AdvancedSensorsEnabled = false;
        compact.MonitorItems.RemoveAll(x => x.Key is "FPS" or "CPU.Pump" or "CASE.Fan");
        foreach (var item in compact.MonitorItems)
            item.VisibleInPanel = item.VisibleInTaskbar = item.Key is "CPU.Load" or "MEM.Load" or "NET.Up" or "NET.Down";
        compact.MonitorItems[0].UserLabel = "kept label";
        compact.Save();
        var restored = SettingsHelper.Load(true);
        Check(restored.MonitorItems.Count == 32 && restored.AdvancedSensorsEnabled, "Compact migration restores 32 metrics and full sampling");
        Check(restored.MonitorItems.Count(x => x.VisibleInPanel) == 13 && restored.MonitorItems.Count(x => x.VisibleInTaskbar) == 6, "Compact migration restores upstream panel/taskbar selections");
        Check(restored.Skin == compact.Skin && restored.HorizontalMode == compact.HorizontalMode && restored.MonitorItems[0].UserLabel == "kept label", "Migration preserves appearance, orientation and aliases");
        restored.Save();
        Check(JsonSerializer.Serialize(restored) == JsonSerializer.Serialize(SettingsHelper.Load(true)), "Restored migration is idempotent");
        var sparse = compact.DeepClone();
        sparse.MonitorItems.RemoveAll(x => x.Key is not ("CPU.Load" or "GPU.Load"));
        sparse.MonitorItems[0].SortIndex = 900;
        sparse.MonitorItems[1].SortIndex = 50;
        foreach (var item in sparse.MonitorItems) {
            item.TaskbarSortIndex = 0; // oldest single-order file
            item.VisibleInPanel = true;
            item.VisibleInTaskbar = item.Key == "GPU.Load";
        }
        sparse.Save();
        var custom = SettingsHelper.Load(true);
        Check(custom.MonitorItems.Count == 32 && custom.MonitorItems.Count(x => x.VisibleInPanel) == 2 &&
            custom.MonitorItems.Count(x => x.VisibleInTaskbar) == 1, "Sparse custom selections are not expanded by built-in restoration");
        foreach (var original in sparse.MonitorItems) {
            var actual = custom.MonitorItems.Single(x => x.Key == original.Key);
            Check(actual.SortIndex == original.SortIndex && actual.TaskbarSortIndex == original.SortIndex && actual.UserLabel == original.UserLabel,
                "Legacy custom order and label preserved: " + original.Key);
        }
        custom.Save();
        Check(JsonSerializer.Serialize(custom) == JsonSerializer.Serialize(SettingsHelper.Load(true)), "Sparse custom migration is idempotent");
        var onlyFour = compact.DeepClone();
        onlyFour.MonitorItems.RemoveAll(x => !x.VisibleInPanel);
        onlyFour.MonitorItems.Add(null!); // malformed legacy entries are filtered before completing the list
        onlyFour.Save();
        var expanded = SettingsHelper.Load(true);
        Check(expanded.MonitorItems.Count == 32 && expanded.MonitorItems.Count(x => x.VisibleInPanel) == 13 &&
            expanded.MonitorItems.Count(x => x.VisibleInTaskbar) == 6, "A physically four-entry file also restores upstream defaults");
        cfg.HorizontalFollowsTaskbar = true;
        cfg.Save();

        string[] reordered;
        using (var settings = new SettingsForm(cfg, ui, main))
        {
            settings.Show(main); settings.SwitchPage("Monitor");
            await Task.Delay(40);
            var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            var page = pages["Monitor"];
            var headers = Descendants(page).OfType<MonitorGroupHeader>().OrderBy(h => ((Control)h.Tag!).Top).ToArray();
            Check(headers.Length >= 3, "At least three groups are present for actual sorting regression");
            static LiteSortBtn Button(MonitorGroupHeader h, string text) => h.Controls.OfType<LiteSortBtn>().Single(b => b.Text == text);
            Check(!Button(headers[0], "▲").Enabled && Button(headers[0], "▼").Enabled, "First group only disables upward movement");
            Check(Button(headers[1], "▲").Enabled && Button(headers[1], "▼").Enabled, "Middle group enables both directions");
            Check(Button(headers[^1], "▲").Enabled && !Button(headers[^1], "▼").Enabled, "Last group only disables downward movement");
            var middle = (Control)headers[1].Tag!;
            int previous = middle.Parent!.Controls.GetChildIndex(middle);
            Button(headers[1], "▲").PerformClick();
            Check(middle.Parent.Controls.GetChildIndex(middle) != previous, "Clicking group up actually moves the group");
            Button(headers[1], "▼").PerformClick();
            Check(middle.Parent.Controls.GetChildIndex(middle) == previous, "Clicking group down restores the position");
            Button(headers[1], "▲").PerformClick();
            page.Save();
            reordered = Field<Settings>(settings, "_draftCfg").MonitorItems.OrderBy(x => x.SortIndex).Select(x => x.Key).ToArray();
            settings.AcceptButton!.PerformClick();
        }
        var saved = SettingsHelper.Load(true);
        Check(saved.HorizontalFollowsTaskbar, "OK from main monitor tab preserves true follow-taskbar configuration");
        Check(saved.MonitorItems.OrderBy(x => x.SortIndex).Select(x => x.Key).SequenceEqual(reordered), "Group reorder persists across save/reload");
        using (var settings = new SettingsForm(cfg, ui, main))
        {
            settings.Show(main); settings.SwitchPage("Monitor");
            var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
            Field<Settings>(settings, "_draftCfg").PanelWidth += 1;
            typeof(SettingsForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
            Check(cfg.HorizontalFollowsTaskbar && SettingsHelper.Load(true).HorizontalFollowsTaskbar, "Apply without visiting taskbar tab preserves follow state");
            Field<LiteCheck>(pages["Monitor"], "_chkLinkHorizontal").Checked = false;
            settings.CancelButton!.PerformClick();
            Check(cfg.HorizontalFollowsTaskbar && SettingsHelper.Load(true).HorizontalFollowsTaskbar, "Cancel discards a draft follow-state change");
        }

        foreach (var name in new[] { "MellowDark", "MellowLight" })
        {
            cfg.Skin = name; AppActions.ApplyThemeAndLayout(cfg, ui, main);
            using var editor = new LiteMonitor.ThemeEditor.ThemeEditorForm();
            typeof(LiteMonitor.ThemeEditor.ThemeEditorForm).GetField("_theme", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, ThemeManager.Current);
            typeof(LiteMonitor.ThemeEditor.ThemeEditorForm).GetMethod("BuildColorsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            var swatches = Descendants(editor).OfType<Button>().Where(b => Equals(b.Tag, "ThemeSwatch")).ToArray();
            Check(swatches.Length > 5 && swatches.All(b => ContrastRatio(b.ForeColor, b.BackColor) >= 4.5), name + " editor swatches all have readable initial hex values");
            foreach (var color in new[] { ColorTranslator.FromHtml("#20232B"), ColorTranslator.FromHtml("#EDF2FA"), Color.Red, Color.Gray })
                Check(ContrastRatio(UIColors.SwatchText(color), color) >= 4.5, name + " changed-color contrast " + color.Name);
        }

        _stage = "lightweight interaction feedback";
        cfg.InterfaceAnimationsEnabled = true; AppActions.ApplyThemeAndLayout(cfg, ui, main);
        var motion = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.InterfaceMotion")!;
        int Active() => (int)motion.GetProperty("ActiveCount", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using (var settings = new SettingsForm(cfg, ui, main))
        {
            settings.Show(main); await Task.Delay(300);
            settings.SwitchPage("System"); settings.SwitchPage("Monitor"); settings.SwitchPage("MainPanel");
            await Task.Delay(350);
            Check(Field<string>(settings, "_currentKey") == "MainPanel" && Active() == 0, "Rapid navigation finishes on last page and stops the animation clock");
            var toggle = Descendants(settings).OfType<LiteCheck>().First(c => c.Visible);
            int changes = 0; toggle.CheckedChanged += (_, _) => changes++;
            toggle.Checked = !toggle.Checked;
            await Task.Delay(35);
            toggle.Checked = !toggle.Checked;
            await Task.Delay(250);
            Check(changes == 2 && Active() == 0, "Interrupted switch changes binding once per action and settles without an idle timer");
            settings.SwitchPage("System"); await Task.Delay(260);
            var page = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages")["System"];
            var group = Descendants(page).OfType<LiteSettingsGroup>().First(x => x.Collapsible);
            group.Collapsed = false; await Task.Delay(60); group.Collapsed = true; await Task.Delay(260);
            Check(group.Collapsed && Active() == 0, "Disclosure reverses cleanly without a residual timer");
            cfg.InterfaceAnimationsEnabled = false; AppActions.ApplyThemeAndLayout(cfg, ui, main);
            settings.SwitchPage("Monitor");
            Check(Active() == 0, "Effects-disabled navigation completes without scheduling frames");
            settings.Close();
        }
        Check(Active() == 0 && !(bool)motion.GetProperty("ClockRunning", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, "Closing settings releases all motion tracks and stops the timer");
        cfg.Save();
    }
    private static async Task TestSelectiveApplyAndSources(MainForm main, Settings cfg, UIController ui)
    {
        using var settings = new SettingsForm(cfg, ui, main);
        settings.Show(main);
        settings.Height = UIUtils.S(360);
        Check(Descendants(settings).OfType<LiteButton>().Where(b => b.Visible).All(b => settings.ClientRectangle.Contains(settings.RectangleToClient(b.RectangleToScreen(b.ClientRectangle)))),
            "Short settings window keeps bottom actions inside its client area");
        var theme = ThemeManager.Current;
        var draft = Field<Settings>(settings, "_draftCfg");
        Invoke(settings, "ApplySettings");
        Check(ReferenceEquals(theme, ThemeManager.Current), "Unchanged Apply does not reload theme or fonts");
        draft.HistoryEnabled = !cfg.HistoryEnabled;
        Invoke(settings, "ApplySettings");
        Check(ReferenceEquals(theme, ThemeManager.Current) && SettingsHelper.Load(true).HistoryEnabled == draft.HistoryEnabled,
            "History-only Apply persists without rebuilding appearance");
        draft.HistoryEnabled = !draft.HistoryEnabled;
        Invoke(settings, "ApplySettings");
        int interval = cfg.RefreshMs;
        draft.RefreshMs = interval + 200;
        Invoke(settings, "ApplySettings");
        Check(Field<System.Windows.Forms.Timer>(ui, "_timer").Interval == ui.EffectiveRefreshMs && ReferenceEquals(theme, ThemeManager.Current),
            "Refresh interval changes immediately without a theme rebuild");
        draft.RefreshMs = interval; Invoke(settings, "ApplySettings");
        draft.PreferredNetwork = "Unavailable test adapter";
        settings.SwitchPage("System");
        var page = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages")["System"];
        for (int i = 0; i < 300 && Field<bool>(page, "_loading"); i++) await Task.Delay(10);
        Check(!Field<bool>(page, "_loading") && Field<LiteComboBox>(page, "_cbNet").Text == draft.PreferredNetwork,
            "Async source population preserves an unavailable manual selection");
        // Exercise the in-flight generation guard without enabling physical sensors.
        page.GetType().GetField("_loading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, true);
        int revision = Field<int>(page, "_sourceRevision");
        draft.AdvancedSensorsEnabled = !draft.AdvancedSensorsEnabled;
        page.OnShow();
        Check(Field<int>(page, "_sourceRevision") > revision && Field<bool>(page, "_refreshPending"),
            "Changed source context invalidates the in-flight generation");
        settings.CancelButton!.PerformClick();
        Check(cfg.PreferredNetwork != "Unavailable test adapter", "Source selection stays in the cancelled draft");
        using var closing = new SettingsForm(cfg, ui, main);
        closing.Show(main); closing.SwitchPage("System"); closing.Close();
        await Task.Delay(200);
        Check(closing.IsDisposed, "Closing during source enumeration completes without touching disposed controls");
    }

    // Native-window coverage for the console-reviewed data paths; compile only when
    // the user prohibits desktop testing. No hidden/offscreen execution is implied.
    private static void TestPerformanceDraftSafety(MainForm main, Settings cfg, UIController ui)
    {
        var original = cfg.MonitorItems.Single(x => x.Key == "CPU.Load");
        string originalName = original.UserLabel;
        string? originalUnit = original.UnitPanel;
        using var settings = new SettingsForm(cfg, ui, main);
        settings.Show(main);
        settings.SwitchPage("Monitor");
        var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
        var page = pages["Monitor"];
        var draft = Field<Settings>(settings, "_draftCfg");
        var row = Descendants(page).OfType<MonitorItemRow>().Single(x => x.Config.Key == "CPU.Load");
        Field<LiteCheck>(page, "_chkEditLabels").Checked = true;
        Check(ReferenceEquals(row, Descendants(page).OfType<MonitorItemRow>().Single(x => x.Config.Key == "CPU.Load")), "Label editor visibility reuses the same row");
        Field<LiteUnderlineInput>(row, "_inputName").Inner.Text = "pending processor";
        Field<LiteUnderlineInput>(row, "_inputUnit").Inner.Text = "panel-unit";
        settings.SwitchPage("System"); // deliberately before the dirty timer can fire
        var edited = draft.MonitorItems.Single(x => x.Key == "CPU.Load");
        Check(edited.UserLabel == "pending processor" && edited.UnitPanel == "panel-unit", "Immediate navigation flushes pending row edits");
        settings.SwitchPage("Monitor");
        Field<Button>(page, "_btnTabBar").PerformClick();
        Check(ReferenceEquals(row, Descendants(page).OfType<MonitorItemRow>().Single(x => x.Config.Key == "CPU.Load")), "Panel and taskbar views reuse the same row");
        Field<LiteUnderlineInput>(row, "_inputShort").Inner.Text = "PENDING";
        Field<LiteUnderlineInput>(row, "_inputUnit").Inner.Text = "taskbar-unit";
        settings.SwitchPage("MainPanel");
        edited = draft.MonitorItems.Single(x => x.Key == "CPU.Load");
        Check(edited.UserLabel == "pending processor" && edited.TaskbarLabel == "PENDING" && edited.UnitPanel == "panel-unit" && edited.UnitTaskbar == "taskbar-unit", "Mode switching preserves separate labels and units");
        draft.AdvancedSensorsEnabled = !draft.AdvancedSensorsEnabled;
        typeof(SettingsForm).GetMethod("SaveDraftPages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
        settings.SwitchPage("Monitor");
        Check(Field<string>(page, "_lastDataSignature") == SettingsChanger.CaptureMonitorState(draft) &&
            !ReferenceEquals(row, Descendants(page).OfType<MonitorItemRow>().Single(x => x.Config.Key == "CPU.Load")), "Hidden-page save does not acknowledge an undisplayed context; revisiting rebuilds it");
        Check(Descendants(page).OfType<MonitorItemRow>().Count() == 32, "All built-in metrics remain selectable in basic and advanced modes");
        Check(draft.MonitorItems.Single(x => x.Key == "CPU.Load").TaskbarLabel == "PENDING", "Context rebuild retains pending edits");
        settings.CancelButton!.PerformClick();
        Check(original.UserLabel == originalName && original.UnitPanel == originalUnit, "Cancel leaves live row values unchanged");
    }

}
