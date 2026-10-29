using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiteMonitor.src.SystemServices;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MellowMeter";

    private static string Command
    {
        get
        {
            // An explicit host also supports a locally installed SDK without changing PATH.
            string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
            string host = Path.Combine(root, "dotnet.exe");
            string assembly = Path.Combine(AppContext.BaseDirectory, "MellowMeter.dll");
            if (File.Exists(host) && File.Exists(assembly)) return $"\"{host}\" \"{assembly}\"";
            return $"\"{Environment.ProcessPath}\"";
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not change startup: {ex.Message}", "MellowMeter",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }
}
