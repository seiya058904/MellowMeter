using System.Diagnostics;

namespace LiteMonitor.src.Core;

internal static class StartupDiagnostics
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string? TracePath = Environment.GetEnvironmentVariable("MELLOWMETER_STARTUP_TRACE");
    public static void Mark(string stage)
    {
        if (string.IsNullOrEmpty(TracePath)) return;
        try { File.AppendAllText(TracePath, FormattableString.Invariant($"{Clock.Elapsed.TotalMilliseconds:F1} ms {stage}\n")); }
        catch { }
    }
}
