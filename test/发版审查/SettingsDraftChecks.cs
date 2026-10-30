using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;

internal static partial class Program
{
    private static async Task SettingsDraftChecks()
    {
        static List<MonitorItemConfig> Copy(List<MonitorItemConfig> items) =>
            JsonSerializer.Deserialize<List<MonitorItemConfig>>(JsonSerializer.Serialize(items))!;
        static string[] Keys(IEnumerable<MonitorItemConfig> items) => items.Select(x => x.Key).ToArray();
        static Settings NewSettings() { var s = new Settings(); s.InitDefaultItems(); return s; }

        await Run("Apply comparison ignores runtime values and equivalent orders", () => {
            var type = typeof(Settings).Assembly.GetType("LiteMonitor.src.Core.Actions.SettingsChanges")!;
            object Changes(Settings a, Settings b) => type.GetMethod("Between", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { a, b })!;
            bool Flag(object change, string name) => (bool)type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(change)!;
            var live = NewSettings(); var draft = live.DeepClone();
            draft.Position = new System.Drawing.Point(100, 200); draft.SessionUploadBytes = 123;
            draft.MonitorItems[0].DynamicLabel = "runtime label";
            Check(!Flag(Changes(live, draft), "Any"), "Runtime state makes Apply dirty");
            foreach (var item in draft.MonitorItems) { item.SortIndex += 100; item.TaskbarSortIndex += 100; }
            Check(!Flag(Changes(live, draft), "Any"), "Equivalent normalized ordering makes Apply dirty");
            draft.PanelWidth += 10;
            Check(Flag(Changes(live, draft), "Appearance") && !Flag(Changes(live, draft), "Sources"), "Width rebuilds hardware sources");
            draft = live.DeepClone(); draft.HistoryEnabled = !live.HistoryEnabled;
            Check(Flag(Changes(live, draft), "Any") && !Flag(Changes(live, draft), "Appearance"), "History toggle reloads appearance");
            draft = live.DeepClone(); draft.PreferredNetwork = "test adapter";
            Check(Flag(Changes(live, draft), "Sources") && !Flag(Changes(live, draft), "Appearance"), "Source selection misses its refresh");
            draft = live.DeepClone(); draft.MonitorItems[0].TaskbarLabel = "custom";
            Check(Flag(Changes(live, draft), "Layout") && !Flag(Changes(live, draft), "Sources"), "Monitor labels must rebuild layout without reopening hardware");
            var cpu = draft.MonitorItems.Single(x => x.Key == "CPU.Load");
            cpu.VisibleInPanel = false;
            Check(!Flag(Changes(live, draft), "Sources"), "A metric still shown in taskbar must not reopen hardware");
            cpu.VisibleInTaskbar = false;
            Check(Flag(Changes(live, draft), "Sources"), "Disabling a sampled metric must refresh hardware");
            return Task.CompletedTask;
        });
        await Run("Cached monitor save preserves edits without rebuilding unchanged context", () => {
            var draft = NewSettings();
            string state = SettingsChanger.CaptureMonitorState(draft);
            var rows = Copy(draft.MonitorItems);
            var cpu = rows.Single(x => x.Key == "CPU.Load");
            cpu.UserLabel = "my : cpu|name"; cpu.TaskbarLabel = "chip";
            cpu.UnitPanel = "percent"; cpu.UnitTaskbar = "";
            cpu.VisibleInPanel = false; cpu.VisibleInTaskbar = true;
            rows.Reverse();
            for (int i = 0; i < rows.Count; i++) rows[i].TaskbarSortIndex = i;
            state = SettingsChanger.SaveMonitorDraft(draft, rows, state);
            Check(state == SettingsChanger.CaptureMonitorState(draft), "Own save invalidates a valid cached page");
            Check(JsonSerializer.Serialize(rows) == JsonSerializer.Serialize(draft.MonitorItems), "Pending row edits were lost");
            Check(!ReferenceEquals(cpu, draft.MonitorItems.Single(x => x.Key == cpu.Key)), "Draft shares editor data");
            return Task.CompletedTask;
        });
        await Run("Saving a hidden monitor cannot acknowledge changed sensor context", () => {
            var draft = NewSettings();
            string displayed = SettingsChanger.CaptureMonitorState(draft);
            var rows = Copy(draft.MonitorItems);
            draft.AdvancedSensorsEnabled = !draft.AdvancedSensorsEnabled;
            string saved = SettingsChanger.SaveMonitorDraft(draft, rows, displayed);
            Check(saved != SettingsChanger.CaptureMonitorState(draft), "Hidden save masks required monitor refresh");
            return Task.CompletedTask;
        });
        await Run("Monitor cache detects order, aliases and unambiguous label/unit changes", () => {
            var draft = NewSettings();
            string state = SettingsChanger.CaptureMonitorState(draft);
            draft.MonitorItems[0].SortIndex += 500;
            Check(state != SettingsChanger.CaptureMonitorState(draft), "Order change missed");
            state = SettingsChanger.CaptureMonitorState(draft);
            draft.GroupAliases["CPU"] = "Processor";
            Check(state != SettingsChanger.CaptureMonitorState(draft), "Alias change missed");
            var item = draft.MonitorItems[0];
            item.UserLabel = "a:b"; item.TaskbarLabel = "c";
            state = SettingsChanger.CaptureMonitorState(draft);
            item.UserLabel = "a"; item.TaskbarLabel = "b:c";
            Check(state != SettingsChanger.CaptureMonitorState(draft), "Delimiter collision masks label changes");
            item.UnitPanel = null; state = SettingsChanger.CaptureMonitorState(draft);
            item.UnitPanel = "null";
            Check(state != SettingsChanger.CaptureMonitorState(draft), "Literal unit collides with auto unit");
            return Task.CompletedTask;
        });
        await Run("Filtered unchanged taskbar order respects tied-index display fallback", () => {
            var a = new MonitorItemConfig { Key = "CPU.Load", SortIndex = 1, TaskbarSortIndex = 0 };
            var h = new MonitorItemConfig { Key = "MEM.Load", SortIndex = 2, TaskbarSortIndex = 0 };
            var b = new MonitorItemConfig { Key = "GPU.Load", SortIndex = 3, TaskbarSortIndex = 0 };
            var rows = new List<MonitorItemConfig> { b, h, a };
            SettingsChanger.UpdateTaskbarOrder(rows, new() { a, b }, true);
            Check(Keys(rows.OrderBy(x => x.TaskbarSortIndex)).SequenceEqual(new[] { a.Key, h.Key, b.Key }), "Unchanged filtered view moves hidden entries");
            return Task.CompletedTask;
        });
        await Run("Filtered taskbar reorder preserves every item and stays idempotent", () => {
            // Exercise all subsets and permutations of five entries, including zero/all visible.
            static IEnumerable<List<MonitorItemConfig>> Permutations(List<MonitorItemConfig> list)
            {
                if (list.Count == 0) { yield return new(); yield break; }
                foreach (var item in list)
                    foreach (var tail in Permutations(list.Where(x => x != item).ToList()))
                        yield return new[] { item }.Concat(tail).ToList();
            }
            for (int mask = 0; mask < 32; mask++)
            {
                var rows = Enumerable.Range(0, 5).Select(i => new MonitorItemConfig { Key = "CPU.Test" + i, TaskbarSortIndex = i }).ToList();
                var selected = rows.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                var hidden = rows.Except(selected).ToList();
                foreach (var visible in Permutations(selected))
                {
                    for (int i = 0; i < rows.Count; i++) rows[i].TaskbarSortIndex = i;
                    SettingsChanger.UpdateTaskbarOrder(rows, visible, true);
                    var ordered = rows.OrderBy(x => x.TaskbarSortIndex).ToList();
                    Check(ordered.Where(selected.Contains).SequenceEqual(visible), "Visible order changed");
                    Check(ordered.Where(hidden.Contains).SequenceEqual(hidden), "Hidden relative order changed");
                    Check(ordered.Select(x => x.TaskbarSortIndex).SequenceEqual(Enumerable.Range(0, 5)), "Index missing/duplicated");
                    SettingsChanger.UpdateTaskbarOrder(rows, visible, true);
                    Check(rows.OrderBy(x => x.TaskbarSortIndex).SequenceEqual(ordered), "Second save moves items");
                }
            }
            return Task.CompletedTask;
        });
        await Run("Draft switching, repeated Apply and Cancel preserve saved labels and both orders", () => {
            var live = NewSettings();
            string original = JsonSerializer.Serialize(live);
            var draft = live.DeepClone();
            var rows = Copy(draft.MonitorItems);
            rows.Reverse();
            for (int i = 0; i < rows.Count; i++) { rows[i].SortIndex = i; rows[i].TaskbarSortIndex = rows.Count - 1 - i; }
            var cpu = rows.Single(x => x.Key == "CPU.Load");
            cpu.UserLabel = "processor"; cpu.TaskbarLabel = "P";
            cpu.UnitPanel = "percent"; cpu.UnitTaskbar = "";
            draft.HorizontalFollowsTaskbar = true;
            draft.GroupAliases["CPU"] = "My CPU";
            SettingsChanger.SaveMonitorDraft(draft, rows, SettingsChanger.CaptureMonitorState(draft));
            // Model of switching to an immediately bound page then returning to the editor.
            draft.PanelWidth += 17;
            Check(JsonSerializer.Serialize(live) == original, "Unapplied draft leaks to live settings");
            SettingsChanger.Merge(live, draft);
            SettingsChanger.RebaseDraftMonitorItems(live, draft);
            string committed = JsonSerializer.Serialize(live);
            SettingsChanger.SaveMonitorDraft(draft, rows, SettingsChanger.CaptureMonitorState(draft));
            SettingsChanger.Merge(live, draft);
            Check(JsonSerializer.Serialize(live) == committed, "Second Apply changes saved data");
            SettingsHelper.GlobalBlockSave = false;
            try { live.Save(); } finally { SettingsHelper.GlobalBlockSave = true; }
            var loaded = SettingsHelper.Load(true);
            Check(Keys(loaded.MonitorItems.OrderBy(x => x.SortIndex)).SequenceEqual(Keys(live.MonitorItems.OrderBy(x => x.SortIndex))), "Panel order lost on reopen");
            Check(Keys(loaded.MonitorItems.OrderBy(x => x.TaskbarSortIndex)).SequenceEqual(Keys(live.MonitorItems.OrderBy(x => x.TaskbarSortIndex))), "Taskbar order lost on reopen");
            var savedCpu = loaded.MonitorItems.Single(x => x.Key == "CPU.Load");
            Check(savedCpu.UserLabel == "processor" && savedCpu.TaskbarLabel == "P" && savedCpu.UnitPanel == "percent" && savedCpu.UnitTaskbar == "", "Labels/units lost on reopen");
            Check(loaded.GroupAliases["CPU"] == "My CPU" && loaded.HorizontalFollowsTaskbar && loaded.PanelWidth == live.PanelWidth, "Other-page changes lost");
            rows[0].UserLabel = "cancelled";
            draft.GroupAliases["CPU"] = "cancelled";
            draft.PanelWidth += 10;
            Check(JsonSerializer.Serialize(live) == committed, "Cancelled edit leaks through shared references");
            Check(SettingsHelper.Load(true).GroupAliases["CPU"] == "My CPU", "Cancel changed persisted settings");
            return Task.CompletedTask;
        });
    }
}
