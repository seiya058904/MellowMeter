using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;

internal static partial class Program
{
    private static async Task WarningRegressionChecks()
    {
        await Run("Retired Glass themes resolve without resetting other preferences", () => {
            Check(!string.Equals(AppPaths.DataDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MellowMeter"), StringComparison.OrdinalIgnoreCase), "Refusing real profile");
            Directory.CreateDirectory(AppPaths.DataDirectory);
            Check(new Settings().Skin == "DarkFlat_Classic", "New profile uses a removed theme");
            var available = ThemeManager.GetAvailableThemes().ToArray();
            Check(!available.Any(x => x.StartsWith("Glass", StringComparison.OrdinalIgnoreCase)) && available.Contains("MellowLight") && available.Contains("MellowDark"), "Theme menu exposes removed files or misses replacements");
            foreach (var (oldName, replacement, title) in new[] { ("GlassLight", "MellowLight", "Mellow Light"), ("gLaSsDaRk", "MellowDark", "Mellow Dark"), ("AuroraDream_Purple", "AuroraDream_Purple", "") })
            {
                var before = new Settings { SettingsSchemaVersion = 3, Skin = oldName, HorizontalMode = true, Position = new(234, 345) };
                before.InitDefaultItems();
                before.MonitorItems[0].UserLabel = "keep this label";
                File.WriteAllText(SettingsHelper.FilePath, JsonSerializer.Serialize(before));
                var after = SettingsHelper.Load(true);
                Check(after.Skin == replacement && after.Position == before.Position && after.HorizontalMode && after.MonitorItems[0].UserLabel == "keep this label", "Theme replacement resets user preferences");
                if (title.Length > 0) {
                    var theme = ThemeManager.Load(oldName);
                    try { Check(theme.Name == title, "Direct theme loading does not resolve retired selection"); }
                    finally { theme.DisposeFonts(); }
                }
            }
            return Task.CompletedTask;
        });
        await Run("32 built-ins and original defaults", () => {
            var settings = new Settings();
            settings.InitDefaultItems();
            Check(settings.MonitorItems.Count == 32 && settings.MonitorItems.Select(x => x.Key).Distinct().Count() == 32, "Built-in set incomplete");
            Check(settings.MonitorItems.Count(x => x.VisibleInPanel) == 13 && settings.MonitorItems.Count(x => x.VisibleInTaskbar) == 6 && !settings.HorizontalMode, "Default selection/layout changed");
            Check(MetricUtils.GetValueStr("CPU.Load", 100) == "100" && MetricUtils.GetValueStr("CPU.Temp", 99.5f) == "99.5", "Upstream adaptive formatting regressed");
            return Task.CompletedTask;
        });
        await Run("Sparse migration, custom order and immutable backup", () => {
            string path = AppPaths.DataDirectory;
            Check(!string.Equals(path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MellowMeter"), StringComparison.OrdinalIgnoreCase), "Refusing real profile");
            Directory.CreateDirectory(path);
            string backupPath = AppPaths.DataFile("settings.pre-monitor-restore.json");
            string? previousBackup = File.Exists(backupPath) ? File.ReadAllText(backupPath) : null;
            var legacy = new Settings { SettingsSchemaVersion = 2, HorizontalMode = true, Skin = "AuroraDream_Purple", Position = new(222, 333) };
            legacy.MonitorItems = new() {
                new() { Key = "CPU.Load", SortIndex = 900, VisibleInPanel = true, UserLabel = "my cpu", UnitPanel = "%" },
                new() { Key = "GPU.Load", SortIndex = 50, VisibleInPanel = true, VisibleInTaskbar = true },
                null!
            };
            string raw = JsonSerializer.Serialize(legacy);
            File.WriteAllText(SettingsHelper.FilePath, raw);
            var restored = SettingsHelper.Load(true);
            Check(restored.SettingsSchemaVersion == 3 && restored.AdvancedSensorsEnabled && restored.MonitorItems.Count == 32, "Migration failed");
            Check(restored.MonitorItems.Count(x => x.VisibleInPanel) == 2 && restored.MonitorItems.Count(x => x.VisibleInTaskbar) == 1, "Custom selection expanded");
            foreach (var original in legacy.MonitorItems.Where(x => x != null)) {
                var actual = restored.MonitorItems.Single(x => x.Key == original.Key);
                Check(actual.SortIndex == original.SortIndex && actual.TaskbarSortIndex == original.SortIndex && actual.UserLabel == original.UserLabel && actual.UnitPanel == original.UnitPanel, "Custom order/label/unit changed");
            }
            Check(restored.Position == legacy.Position && restored.Skin == legacy.Skin && restored.HorizontalMode, "Appearance reset");
            string backup = File.ReadAllText(AppPaths.DataFile("settings.pre-monitor-restore.json"));
            // A new isolated run creates the backup; reruns must preserve the original bytes.
            Check(backup == (previousBackup ?? raw), "Backup is not the original input bytes");
            SettingsHelper.GlobalBlockSave = false;
            try { restored.Save(); } finally { SettingsHelper.GlobalBlockSave = true; }
            Check(JsonSerializer.Serialize(restored) == JsonSerializer.Serialize(SettingsHelper.Load(true)), "Migration not idempotent");
            Check(File.ReadAllText(AppPaths.DataFile("settings.pre-monitor-restore.json")) == backup, "Backup overwritten");

            legacy.MonitorItems = new[] { "CPU.Load", "MEM.Load", "NET.Up", "NET.Down" }
                .Select((key, index) => new MonitorItemConfig { Key = key, SortIndex = 10 + index, TaskbarSortIndex = 20 + index, VisibleInPanel = true, VisibleInTaskbar = true }).ToList();
            File.WriteAllText(SettingsHelper.FilePath, JsonSerializer.Serialize(legacy));
            restored = SettingsHelper.Load(true);
            Check(restored.MonitorItems.Count == 32 && restored.MonitorItems.Count(x => x.VisibleInPanel) == 13 && restored.MonitorItems.Count(x => x.VisibleInTaskbar) == 6, "Physical four-entry migration incomplete");
            Check(File.ReadAllText(AppPaths.DataFile("settings.pre-monitor-restore.json")) == backup, "Later migration replaced original backup");
            return Task.CompletedTask;
        });
        await Run("Theme font replacement releases old resources", () => {
            var theme = new Theme();
            try {
                theme.BuildFonts();
                for (int i = 0; i < 30; i++) {
                    var old = theme.FontItem;
                    theme.Font.Item = 9 + i % 5;
                    theme.BuildFonts();
                    Check(theme.FontItem.GetHeight(96) > 0, "Replacement font invalid");
                    bool disposed = false;
                    try { old.GetHeight(96); } catch (ArgumentException) { disposed = true; } catch (ObjectDisposedException) { disposed = true; }
                    Check(disposed, "Old custom font leaked");
                }
                theme.Font.Item = double.NaN;
                theme.BuildFonts();
                Check(float.IsFinite(theme.FontItem.Size) && theme.FontItem.Size > 0, "Invalid font size reached GDI");
                theme.DisposeFonts(); theme.DisposeFonts();
                Check(theme.FontItem.IsSystemFont && theme.FontItem.GetHeight(96) > 0, "Disposed theme retained a dead font");
            } finally { theme.DisposeFonts(); }
            return Task.CompletedTask;
        });
        await Run("Disposed network manager does not remain event-rooted", () => {
            var reference = DisposedNetworkManager();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!reference.IsAlive, "Network change event still owns disposed manager");
            return Task.CompletedTask;
        });
        await Run("Interrupted spring preserves state and settles exactly", () => {
            var type = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Controls.MotionSpring")!;
            var sample = type.GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object Spring(float from, float to, float velocity, bool bounce) => Activator.CreateInstance(type, from, to, velocity, 0.18d, bounce)!;
            (float value, float velocity) At(object spring, double t) => ((float, float))sample.Invoke(spring, new object[] { t })!;
            var first = Spring(0, 1, 0, false);
            var middle = At(first, .045);
            var reversed = Spring(middle.value, 0, middle.velocity, true);
            var start = At(reversed, 0);
            Check(Math.Abs(start.value - middle.value) < .0001 && Math.Abs(start.velocity - middle.velocity) < .0001, "Retarget lost state/velocity");
            Check(At(reversed, .18) == (0f, 0f), "Spring did not stop exactly at target");
            for (int i = 0; i <= 180; i++) {
                var frame = At(reversed, i / 1000d);
                Check(float.IsFinite(frame.value) && float.IsFinite(frame.velocity), "Nonfinite animation value");
            }
            return Task.CompletedTask;
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DisposedNetworkManager()
    {
        var manager = new NetworkManager();
        manager.Dispose(); manager.Dispose();
        return new WeakReference(manager);
    }
}
