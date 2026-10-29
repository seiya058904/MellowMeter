using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(BasicSystemSampler.CalculateCpuLoad(250, 600, 400) == 75f, "Kernel time includes idle time.");
Require(BasicSystemSampler.CalculateCpuLoad(100, 100, 0) == 0f, "A valid idle sample is zero.");
Require(BasicSystemSampler.CalculateCpuLoad(0, 0, 0) == null, "No elapsed CPU time is unavailable.");
Require(BasicSystemSampler.CalculateCpuLoad(101, 100, 0) == null, "Inconsistent CPU counters are unavailable.");
Require(BasicSystemSampler.TryNetworkDelta(1500, 1000, 1, out long delta) && delta == 500, "Network byte deltas.");
Require(BasicSystemSampler.TryNetworkDelta(1000, 1000, 10, out delta) && delta == 0, "A valid idle NIC is zero.");
Require(!BasicSystemSampler.TryNetworkDelta(100, 1000, 1, out delta), "Counter resets must not produce negative rates.");
Require(!BasicSystemSampler.TryNetworkDelta(1500, 1000, 0, out delta), "Startup requires a baseline.");
Require(BasicSystemSampler.TryNetworkDelta(1500, 1000, 60, out delta) && delta == 500,
    "The supported 60-second UI refresh interval must produce a valid sample.");
Require(!BasicSystemSampler.TryNetworkDelta(1500, 1000, 121, out delta), "Long suspension gaps must not produce a rate.");
Require(!BasicSystemSampler.TryNetworkDelta(1500, 1000, double.NaN, out delta), "Invalid elapsed time is rejected.");

var settings = new Settings { PreferredNetwork = "MellowMeter regression nonexistent adapter", HistoryEnabled = false };
var sampler = new BasicSystemSampler(settings);
Require(sampler.Get("CPU.Load") == null && sampler.Get("NET.Up") == null, "No fabricated startup values.");
sampler.Update();
Require(sampler.Get("CPU.Load") == null, "First CPU sample only establishes the baseline.");
Require(sampler.Get("MEM.Load") is >= 0 and <= 100 && Settings.DetectedRamTotalGB > 0, "Native memory sample.");
Thread.Sleep(250);
sampler.Update();
Require(sampler.Get("CPU.Load") is >= 0 and <= 100, "Native CPU sample after baseline.");
Require(sampler.Get("NET.Up") == null && sampler.Get("NET.Down") == null, "An unavailable preferred NIC must not fall back silently.");
Require(TrafficLogger.Calls == 0, "Disabled history must not record traffic.");
sampler.RecordTraffic(123, 456);
Require(TrafficLogger.Calls == 0 && settings.SessionUploadBytes == 123 && settings.SessionDownloadBytes == 456,
    "Disabled persistent history preserves only in-memory session totals.");
settings.HistoryEnabled = true;
sampler.RecordTraffic(10, 20);
Require(TrafficLogger.Calls == 0, "Re-enabling history cannot attribute a previously disabled interval.");
sampler.Update();
sampler.RecordTraffic(10, 20);
Require(TrafficLogger.Calls == 1, "Enabled history records valid byte deltas.");
sampler.RecordTraffic(-1, 20);
Require(TrafficLogger.Calls == 1, "Invalid deltas never enter history.");
sampler.Reset();
Require(sampler.Get("CPU.Load") == null && sampler.Get("MEM.Load") == null, "Reset invalidates samples.");
sampler.Update();
Require(sampler.Get("CPU.Load") == null, "Resume needs a new baseline.");
var sampleLock = typeof(BasicSystemSampler).GetField("_sync",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sampler)!;
lock (sampleLock)
{
    Require(Task.Run(sampler.Reset).Wait(TimeSpan.FromSeconds(2)),
        "A power/session reset must not block on an ongoing native sample.");
}
Require(sampler.Get("CPU.Load") == null && sampler.Get("MEM.Load") == null,
    "A reset invalidates the published snapshot immediately.");
Console.WriteLine("PASS: CPU and network delta edge cases, native sampling, missing adapter, reset, and history gating.");

namespace LiteMonitor.src.Core
{
    // The sampler's small host contract, without loading UI or sensor libraries.
    public sealed class Settings
    {
        public string PreferredNetwork { get; set; } = "";
        public bool HistoryEnabled { get; set; }
        public bool WebServerEnabled { get; set; }
        public static float DetectedRamTotalGB;
        public long SessionUploadBytes, SessionDownloadBytes;
        public bool IsAnyEnabled(string key) => true;
    }

    public static class TrafficLogger
    {
        public static int Calls;
        public static void AddTraffic(long up, long down) => Calls++;
    }
}
