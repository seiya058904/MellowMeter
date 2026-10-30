using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteMonitor.src.Core;

namespace LiteMonitor
{
    public static class SettingsHelper
    {
        // Cache path
        private static readonly string _cachedPath = AppPaths.DataFile("settings.json");
        private static readonly string _backupPath = AppPaths.DataFile("settings.json.bak");
        private static readonly string _tempPath = AppPaths.DataFile("settings.json.tmp");
        private static readonly object _ioLock = new object();
        public static string FilePath => _cachedPath;

        // Global block save lock
        public static bool GlobalBlockSave { get; set; } = false;

        public static Settings Load(bool forceReload = false)
        {
            // Note: The singleton instance management is kept in Settings.Load() facade
            // or handled by the caller. This method strictly loads from disk/creates default.
            
            Settings s;
            string? loadedSource = null;
            lock (_ioLock)
            {
                var primary = TryLoadFile(FilePath);
                var backup = primary == null ? TryLoadFile(_backupPath) : null;
                s = primary ?? backup ?? new Settings();
                loadedSource = primary != null ? FilePath : backup != null ? _backupPath : null;
            }

            s.MonitorItems?.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.Key) ||
                ((item.Key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase) &&
                  item.Key is not ("DASH.HOST" or "DASH.Time" or "DASH.Uptime" or "DASH.IP"))));

            // Legacy files had one order only. Initialize the second order in place
            // before appending missing entries; never rebuild and reset custom positions.
            if (s.MonitorItems is { Count: > 0 } && s.MonitorItems.Where(x => x != null).All(x => x.TaskbarSortIndex == 0))
                foreach (var item in s.MonitorItems)
                    if (item != null) item.TaskbarSortIndex = item.SortIndex;

            if (s.SettingsSchemaVersion < 2)
            {
                // A dedicated, never-overwritten backup survives the normal rotating .bak file.
                string migrationBackup = AppPaths.DataFile("settings.pre-v0.2.json");
                if (loadedSource != null && !File.Exists(migrationBackup))
                    File.Copy(loadedSource, migrationBackup, false);
                s.SettingsSchemaVersion = 2;
            }
            if (s.SettingsSchemaVersion < 3)
            {
                string backup = AppPaths.DataFile("settings.pre-monitor-restore.json");
                if (loadedSource != null && !File.Exists(backup)) File.Copy(loadedSource, backup, false);
                var compact = new HashSet<string> { "CPU.Load", "MEM.Load", "NET.Up", "NET.Down" };
                var defaults = new Settings();
                defaults.InitDefaultItems();
                bool compactPanel = s.MonitorItems != null && compact.SetEquals(s.MonitorItems.Where(x => x != null && x.VisibleInPanel).Select(x => x.Key));
                bool compactTaskbar = s.MonitorItems != null && compact.SetEquals(s.MonitorItems.Where(x => x != null && x.VisibleInTaskbar).Select(x => x.Key));
                if (s.MonitorItems != null)
                    foreach (var item in s.MonitorItems)
                    {
                        if (item == null) continue;
                        var original = defaults.MonitorItems.FirstOrDefault(x => x.Key == item.Key);
                        if (original == null) continue;
                        if (compactPanel) item.VisibleInPanel = original.VisibleInPanel;
                        if (compactTaskbar) item.VisibleInTaskbar = original.VisibleInTaskbar;
                    }
                // Complete sparse legacy lists before the generic repair. Only the exact
                // compact selection opts in to the upstream visible defaults.
                if (s.MonitorItems is { Count: > 0 }) s.CheckAndAppendMissingItems(compactPanel, compactTaskbar);
                s.AdvancedSensorsEnabled = true;
                s.SettingsSchemaVersion = 3;
            }
            s.Skin = ThemeManager.ResolveThemeName(s.Skin);
            s.AutoCheckUpdate = false;
            s.WebServerEnabled = false;
            if (s.MainFormDoubleClickAction == 5) s.MainFormDoubleClickAction = 0;
            if (s.TaskbarDoubleClickAction == 5) s.TaskbarDoubleClickAction = 0;
            s.PluginInstances?.ForEach(plugin => plugin.Enabled = false);

            if (s.GroupAliases == null) s.GroupAliases = new Dictionary<string, string>();

            // 1. Check if new install
            if (s.MonitorItems == null || s.MonitorItems.Count == 0)
            {
                var initialItems = s.InitDefaultItems();
                // Ensure TaskbarSortIndex has initial value
                foreach (var item in initialItems)
                {
                    if (item.TaskbarSortIndex == 0)
                        item.TaskbarSortIndex = item.SortIndex;
                }
            }
            else
            {
                // 2. Version check
                bool isLegacyConfig = s.MonitorItems.All(x => x.TaskbarSortIndex == 0);

                if (isLegacyConfig)
                {
                    s.RebuildAndMigrateSettings();
                }
                else
                {
                    s.CheckAndAppendMissingItems();
                }
            }

            // [Fix] Removed redundant SyncToLanguage call. 
            // Caller (AppActions.ApplyAllSettings) is responsible for syncing language state.
            // s.SyncToLanguage();
            
            // [Migration] 兼容老用户：如果未设置 PresetStyle (-1)，根据旧的 FontSize 推断  
            // 未来1.2.9几个版本后，将TaskbarPresetStyle 默认配置改成1即可
            if (s.TaskbarPresetStyle == -1)
            {
                // 9.0pt => 小字模式(0), 其他 => 大字模式(1)
                s.TaskbarPresetStyle = (Math.Abs(s.TaskbarFontSize - Settings.DEFAULT_TB_SIZE_REGULAR) < 0.1f) ? 0 : 1;
            }

            s.InternAllStrings();
            
            return s;
        }

        public static void Save(this Settings settings)
        {
            if (GlobalBlockSave) return;

            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                lock (_ioLock)
                {
                    AtomicWrite(json);
                }
            }
            catch
            {
                CleanupTempFile();
            }
        }

        public static void DeleteStoredSettings()
        {
            lock (_ioLock)
            {
                DeleteFileIfExists(FilePath);
                DeleteFileIfExists(_backupPath);
                DeleteFileIfExists(_tempPath);
            }
        }

        private static Settings? TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private static void AtomicWrite(string json)
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_tempPath, json);

            if (File.Exists(FilePath))
            {
                try { File.Copy(FilePath, _backupPath, true); }
                catch { }
            }

            File.Move(_tempPath, FilePath, true);
        }

        private static void CleanupTempFile()
        {
            try
            {
                if (File.Exists(_tempPath)) File.Delete(_tempPath);
            }
            catch { }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static List<MonitorItemConfig> InitDefaultItems(this Settings settings)
        {
            settings.MonitorItems = new List<MonitorItemConfig>
            {
                // [1xx] Dashboard Items
                // [Taskbar] DASH items should appear at the END by default (SortIndex > 800)
                new MonitorItemConfig { Key = "DASH.HOST", SortIndex = 101, TaskbarSortIndex = 1100, VisibleInPanel = false, TaskbarLabel = " " },
                new MonitorItemConfig { Key = "DASH.Time", SortIndex = 102, TaskbarSortIndex = 1200, VisibleInPanel = false, TaskbarLabel = " " },
                new MonitorItemConfig { Key = "DASH.Uptime", SortIndex = 103, TaskbarSortIndex = 1300, VisibleInPanel = true, TaskbarLabel = " " },
                new MonitorItemConfig { Key = "DASH.IP",   SortIndex = 104, TaskbarSortIndex = 1400, VisibleInPanel = false, TaskbarLabel = " " },
               
                // [2xx] CPU
                new MonitorItemConfig { Key = "CPU.Load",  SortIndex = 201, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "CPU.Temp",  SortIndex = 202, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "CPU.Clock", SortIndex = 203, VisibleInPanel = false },
                new MonitorItemConfig { Key = "CPU.Power", SortIndex = 204, VisibleInPanel = false },
                // [New] CPU Voltage
                new MonitorItemConfig { Key = "CPU.Voltage", SortIndex = 205, VisibleInPanel = false },
                new MonitorItemConfig { Key = "CPU.Fan",   SortIndex = 206, VisibleInPanel = false },
                new MonitorItemConfig { Key = "CPU.Pump",  SortIndex = 207, VisibleInPanel = false },

                // [3xx] GPU
                new MonitorItemConfig { Key = "GPU.Load",  SortIndex = 301, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "GPU.Temp",  SortIndex = 302, VisibleInPanel = true },
                new MonitorItemConfig { Key = "GPU.Clock", SortIndex = 303, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.Power", SortIndex = 304, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.Fan",   SortIndex = 305, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.VRAM",  SortIndex = 306, VisibleInPanel = true },

                // [4xx] HOST (MEM, FPS, MOBO, DISK Temp, CASE Fan)
                new MonitorItemConfig { Key = "MEM.Load",  SortIndex = 401, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "FPS",       SortIndex = 402, VisibleInPanel = false },
                new MonitorItemConfig { Key = "MOBO.Temp", SortIndex = 403, VisibleInPanel = false },
                new MonitorItemConfig { Key = "DISK.Temp", SortIndex = 404, VisibleInPanel = false },
                new MonitorItemConfig { Key = "CASE.Fan",  SortIndex = 405, VisibleInPanel = false },
                
                // [5xx] BATTERY (New Group)
                new MonitorItemConfig { Key = "BAT.Percent", SortIndex = 501, VisibleInPanel = false, VisibleInTaskbar = false },
                new MonitorItemConfig { Key = "BAT.Power",   SortIndex = 502, VisibleInPanel = false },
                new MonitorItemConfig { Key = "BAT.Voltage", SortIndex = 503, VisibleInPanel = false },
                new MonitorItemConfig { Key = "BAT.Current", SortIndex = 504, VisibleInPanel = false },

                // [6xx] DISK IO
                new MonitorItemConfig { Key = "DISK.Read", SortIndex = 601, VisibleInPanel = true },
                new MonitorItemConfig { Key = "DISK.Write",SortIndex = 602, VisibleInPanel = true },

                // [7xx] NET
                new MonitorItemConfig { Key = "NET.Up",    SortIndex = 701, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "NET.Down",  SortIndex = 702, VisibleInPanel = true, VisibleInTaskbar = true },

                // [8xx] DATA
                new MonitorItemConfig { Key = "DATA.DayUp",  SortIndex = 801, VisibleInPanel = true },
                new MonitorItemConfig { Key = "DATA.DayDown",SortIndex = 802, VisibleInPanel = true },
            };
            return settings.MonitorItems;
        }
        // [Sync] 同步到语言设置
        // 作用：将配置中的组别名和监控项标签同步到语言管理器
        // 注意：这会清除所有当前的语言覆盖
        public static void SyncToLanguage(this Settings settings)
        {
            LanguageManager.ClearOverrides();
            if (settings.GroupAliases != null)
            {
                foreach (var kv in settings.GroupAliases)
                    LanguageManager.SetOverride(UIUtils.Intern("Groups." + kv.Key), kv.Value);
            }
            if (settings.MonitorItems != null)
            {
                foreach (var item in settings.MonitorItems)
                {
                    if (!string.IsNullOrEmpty(item.UserLabel))
                        LanguageManager.SetOverride(UIUtils.Intern("Items." + item.Key), item.UserLabel);
                    if (!string.IsNullOrEmpty(item.TaskbarLabel))
                        LanguageManager.SetOverride(UIUtils.Intern("Short." + item.Key), item.TaskbarLabel);
                }
            }
        }

        // Cache for standard keys to avoid repeated allocations
        private static readonly Lazy<HashSet<string>> _standardKeys = new Lazy<HashSet<string>>(() => 
        {
            var s = new Settings();
            s.InitDefaultItems();
            return new HashSet<string>(s.MonitorItems.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        });

        public static HashSet<string> GetStandardKeys() => _standardKeys.Value;

        public static void InternAllStrings(this Settings settings)
        {
            if (settings.MonitorItems != null)
            {
                // [Cleanup] Remove Orphaned Items
                // Optimization: Use StandardKeys whitelist to avoid heavy allocations
                var whitelist = GetStandardKeys();
                var keysToRemove = new List<MonitorItemConfig>();

                foreach (var item in settings.MonitorItems)
                {
                    if (item == null) continue;
                    
                    item.Key = UIUtils.Intern(item.Key);
                    
                    // 1. Is it a standard item?
                    if (whitelist.Contains(item.Key)) continue;

                    // 2. Is it a valid Plugin item? 
                    // [Safety] 只要是 DASH. 开头，一律保留，防止误删用户自定义的简单插件
                    if (item.Key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // 保留所有 DASH.*
                    }
                    keysToRemove.Add(item);
                }
                
                foreach (var orphan in keysToRemove)
                {
                    settings.MonitorItems.Remove(orphan);
                }
            }

            settings.PreferredDisk = UIUtils.Intern(settings.PreferredDisk);
            settings.LastAutoDisk = UIUtils.Intern(settings.LastAutoDisk);
            settings.PreferredNetwork = UIUtils.Intern(settings.PreferredNetwork);
            settings.LastAutoNetwork = UIUtils.Intern(settings.LastAutoNetwork);
            settings.PreferredGpu = UIUtils.Intern(settings.PreferredGpu);
            
            settings.PreferredCpuFan = UIUtils.Intern(settings.PreferredCpuFan);
            settings.PreferredCpuPump = UIUtils.Intern(settings.PreferredCpuPump);
            settings.PreferredCaseFan = UIUtils.Intern(settings.PreferredCaseFan);
            settings.PreferredMoboTemp = UIUtils.Intern(settings.PreferredMoboTemp);
            
            settings.TaskbarFontFamily = UIUtils.Intern(settings.TaskbarFontFamily);
        }

        public static void RebuildAndMigrateSettings(this Settings settings)
        {
            // 1. 获取最新版本的标准清单 (确保包含新版本增加的监控项)
            var temp = new Settings();
            temp.InitDefaultItems();
            var migratedList = temp.MonitorItems;

            // 2. 遍历新标准清单，回填用户的个性化设置
            // 同时构建标准键集合，用于后续过滤
            var standardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var newItem in migratedList)
            {
                standardKeys.Add(newItem.Key);

                var oldItem = settings.MonitorItems.FirstOrDefault(x => x.Key.Equals(newItem.Key, StringComparison.OrdinalIgnoreCase));
                if (oldItem != null)
                {
                    newItem.SortIndex = oldItem.SortIndex;
                    newItem.TaskbarSortIndex = oldItem.TaskbarSortIndex == 0 ? oldItem.SortIndex : oldItem.TaskbarSortIndex;
                    newItem.VisibleInPanel = oldItem.VisibleInPanel;
                    newItem.VisibleInTaskbar = oldItem.VisibleInTaskbar;
                    newItem.UserLabel = oldItem.UserLabel;
                    newItem.TaskbarLabel = oldItem.TaskbarLabel;
                    newItem.UnitPanel = oldItem.UnitPanel;
                    newItem.UnitTaskbar = oldItem.UnitTaskbar;
                }
                // 数据修复：老版本没有 TaskbarSortIndex，默认初始化
                if (newItem.TaskbarSortIndex == 0) newItem.TaskbarSortIndex = newItem.SortIndex;
            }

            // 3. [Fix] 找回插件项：追加 DASH 开头且不在标准列表中的配置
            // 必须过滤掉 DASH.HOST/DASH.Time 等标准项，否则会重复！
            migratedList.AddRange(settings.MonitorItems
                .Where(x => x.Key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase) && !standardKeys.Contains(x.Key))
                .Select(x => { 
                    if (x.TaskbarSortIndex == 0) x.TaskbarSortIndex = x.SortIndex; 
                    return x; 
                }));

            settings.MonitorItems = migratedList;
        }

        public static void CheckAndAppendMissingItems(this Settings settings, bool panelDefaults = false, bool taskbarDefaults = false)
        {
            var defaults = new Settings();
            defaults.InitDefaultItems();
            var keys = new HashSet<string>(settings.MonitorItems.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            int panelOrder = settings.MonitorItems.Count == 0 ? 0 : settings.MonitorItems.Max(x => x.SortIndex);
            int taskbarOrder = settings.MonitorItems.Count == 0 ? 0 : settings.MonitorItems.Max(x => x.TaskbarSortIndex);
            foreach (var item in defaults.MonitorItems)
            {
                if (!keys.Add(item.Key)) continue;
                // Preserve every existing position and selection. New capabilities are
                // available in settings without silently expanding a custom monitor.
                item.VisibleInPanel &= panelDefaults;
                item.VisibleInTaskbar &= taskbarDefaults;
                item.SortIndex = ++panelOrder;
                item.TaskbarSortIndex = ++taskbarOrder;
                settings.MonitorItems.Add(item);
            }
        }

        public static Settings.TBStyle GetStyle(this Settings settings)
        {
            // 如果开启了自定义布局，完全使用自定义参数
            if (settings.TaskbarCustomLayout) return new Settings.TBStyle {
                Font = settings.TaskbarFontFamily, Size = settings.TaskbarFontSize, Bold = settings.TaskbarFontBold,
                Gap = settings.TaskbarItemSpacing, Inner = settings.TaskbarInnerSpacing, VOff = settings.TaskbarVerticalPadding
            };

            // 如果未开启自定义布局，使用标准布局参数
            bool isSmall = settings.TaskbarPresetStyle == 0; // 0 = Regular/Small, 1 = Bold/Big

            return new Settings.TBStyle {
                Font = Settings.DEFAULT_TB_FONT, 
                Size = isSmall ? Settings.DEFAULT_TB_SIZE_REGULAR : Settings.DEFAULT_TB_SIZE_BOLD,
                Bold = !isSmall,
                Gap = Settings.DEFAULT_TB_GAP, 
                Inner = isSmall ? Settings.DEFAULT_TB_INNER_REGULAR : Settings.DEFAULT_TB_INNER_BOLD, 
                VOff = Settings.DEFAULT_TB_VOFF 
            };
        }

        public static bool IsAnyEnabled(this Settings settings, string keyPrefix)
        {
            return settings.MonitorItems.Any(x => x.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) && (x.VisibleInPanel || x.VisibleInTaskbar));
        }

        public static void UpdateMaxRecord(this Settings settings, string key, float val)
        {
            bool changed = false;
            if (val <= 0 || float.IsNaN(val) || float.IsInfinity(val)) return;
            
            if (key.Contains("Clock") && val > 10000) return; 
            if (key.Contains("Power") && val > 1000) return;
            if ((key.Contains("Fan") || key.Contains("Pump")) && val > 10000) return;

            if (key == "CPU.Power" && val > settings.RecordedMaxCpuPower) { settings.RecordedMaxCpuPower = val; changed = true; }
            else if (key == "CPU.Clock" && val > settings.RecordedMaxCpuClock) { settings.RecordedMaxCpuClock = val; changed = true; }
            else if (key == "GPU.Power" && val > settings.RecordedMaxGpuPower) { settings.RecordedMaxGpuPower = val; changed = true; }
            else if (key == "GPU.Clock" && val > settings.RecordedMaxGpuClock) { settings.RecordedMaxGpuClock = val; changed = true; }
            
            else if (key == "CPU.Fan" && val > settings.RecordedMaxCpuFan) { settings.RecordedMaxCpuFan = val; changed = true; }
            else if (key == "CPU.Pump" && val > settings.RecordedMaxCpuPump) { settings.RecordedMaxCpuPump = val; changed = true; }
            else if (key == "GPU.Fan" && val > settings.RecordedMaxGpuFan) { settings.RecordedMaxGpuFan = val; changed = true; }
            else if (key == "CASE.Fan" && val > settings.RecordedMaxChassisFan) { settings.RecordedMaxChassisFan = val; changed = true; }
            
            if (changed && (DateTime.Now - settings.LastAutoSaveTime).TotalSeconds > 30)
            {
                settings.Save();
                settings.LastAutoSaveTime = DateTime.Now;
            }
        }
    }
}
