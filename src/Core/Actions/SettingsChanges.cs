using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteMonitor.src.Core.Actions;

// Evaluated only on Apply, never from a polling timer or a paint handler.
internal sealed class SettingsChanges
{
    private static readonly HashSet<string> RuntimeProperties = new(StringComparer.Ordinal) {
        "LastAutoNetwork", "LastAutoDisk", "ScreenDevice", "Position", "MaxLimitTipShown",
        "TotalUpload", "TotalDownload", "SessionUploadBytes", "SessionDownloadBytes", "LastAutoSaveTime", "LastAlertTime"
    };
    private static readonly PropertyInfo[] Properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !RuntimeProperties.Contains(p.Name) && p.GetCustomAttribute<JsonIgnoreAttribute>() == null).ToArray();
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private bool _enabledMetricsChanged;
    internal bool Any => _names.Count != 0;
    internal bool Has(params string[] names) => names.Any(_names.Contains);
    internal bool Taskbar => _names.Any(n => n.StartsWith("Taskbar", StringComparison.Ordinal));
    internal bool Sources => _enabledMetricsChanged || Has("AdvancedSensorsEnabled", "UseWinPerCounters", "IgnoreSmbTraffic") || _names.Any(n => n.StartsWith("Preferred", StringComparison.Ordinal));
    internal bool Appearance => Has("Skin", "UIScale", "PanelWidth", "HorizontalMode", "Language");
    internal bool Layout => Has("MonitorItems", "GroupAliases", "HorizontalFollowsTaskbar", "HorizontalSingleLine", "HorizontalItemSpacing", "HorizontalInnerSpacing", "MemoryDisplayMode", "Thresholds") || Taskbar;

    internal static SettingsChanges Between(Settings live, Settings draft)
    {
        var result = new SettingsChanges();
        foreach (var property in Properties)
        {
            object? left = property.GetValue(live), right = property.GetValue(draft);
            if (property.Name == nameof(Settings.MonitorItems)) {
                left = CanonicalItems(live); right = CanonicalItems(draft);
                result._enabledMetricsChanged = !EnabledKeys(live).SequenceEqual(EnabledKeys(draft));
            } else if (property.Name == nameof(Settings.GroupAliases)) {
                left = live.GroupAliases.OrderBy(p => p.Key); right = draft.GroupAliases.OrderBy(p => p.Key);
            }
            if (Equals(left, right)) continue;
            if (JsonSerializer.Serialize(left) != JsonSerializer.Serialize(right)) result._names.Add(property.Name);
        }
        return result;
    }

    private static IEnumerable<string> EnabledKeys(Settings settings) => settings.MonitorItems
        .Where(x => x.VisibleInPanel || x.VisibleInTaskbar).Select(x => x.Key).Order(StringComparer.Ordinal);

    private static object CanonicalItems(Settings settings) => new {
        Items = settings.MonitorItems.OrderBy(x => x.Key).Select(x => new {
            x.Key, x.UserLabel, x.TaskbarLabel, x.UnitPanel, x.UnitTaskbar, x.VisibleInPanel, x.VisibleInTaskbar
        }),
        Panel = settings.MonitorItems.OrderBy(x => x.SortIndex).Select(x => x.Key),
        Taskbar = settings.MonitorItems.OrderBy(x => x.TaskbarSortIndex).ThenBy(x => x.SortIndex).Select(x => x.Key)
    };
}
