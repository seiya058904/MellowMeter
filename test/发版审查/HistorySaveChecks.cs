using System.Reflection;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;

internal static partial class Program
{
    private const BindingFlags LoggerStatics = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly object HistoryIoLock = LoggerField("_ioLock")!;
    private static readonly object HistoryDataLock = LoggerField("_dataLock")!;
    private static readonly FieldInfo SaveTaskField = typeof(HardwareHistoryLogger).GetField("_saveTask", LoggerStatics)!;

    private static async Task HistorySaveChecks()
    {
        await Run("History: a background snapshot taken earlier cannot overwrite the save on exit", () => {
            ResetHistory();
            AddHistoryPoint(1);
            Monitor.Enter(HistoryIoLock);
            try
            {
                // The writer is now stopped at the file lock, the exit's save goes first.
                StartBackgroundSaveAndLetItReachTheLock();
                AddHistoryPoint(2);
                LoggerMethod("SaveCore").Invoke(null, new object[] { true });
            }
            finally { Monitor.Exit(HistoryIoLock); }
            Check(BackgroundWriter().Wait(TimeSpan.FromSeconds(10)), "Background save did not finish");
            Check(HistoryOnDisk().SequenceEqual(new float[] { 1, 2 }), "Older snapshot replaced newer history: " + string.Join(",", HistoryOnDisk()));
            return Task.CompletedTask;
        });
        await Run("History: the save on exit waits for a writer already started", () => {
            ResetHistory();
            AddHistoryPoint(1);
            // A writer that holds no lock yet: only waiting for it can keep the exit's save back.
            var writer = new TaskCompletionSource();
            SaveTaskField.SetValue(null, writer.Task);
            var exit = Task.Run(HardwareHistoryLogger.Save);
            Thread.Sleep(100);
            bool waited = !exit.IsCompleted;
            writer.SetResult();
            Check(waited, "Exit save did not wait for the writer");
            Check(exit.Wait(TimeSpan.FromSeconds(10)), "Exit save did not finish");
            Check(HistoryOnDisk().SequenceEqual(new float[] { 1 }), "Final history incomplete: " + string.Join(",", HistoryOnDisk()));
            return Task.CompletedTask;
        });
        await Run("History: normal saves still keep the unfinished minute, and exit finishes it", () => {
            ResetHistory();
            var settings = new Settings();
            settings.InitDefaultItems();
            foreach (var item in settings.MonitorItems.Where(x => x.Key == "MEM.Load")) item.VisibleInPanel = true;
            // The first sample of a run saves in the background, as the app does.
            HardwareHistoryLogger.RecordSnapshot(settings, key => key == "MEM.Load" ? 45f : null);
            Check(BackgroundWriter().Wait(TimeSpan.FromSeconds(10)), "Background save did not finish");
            Check(HistoryOnDisk().SequenceEqual(new float[] { 45 }), "Background save lost the pending sample");
            Check(HardwareHistoryLogger.Data.Points.Count == 0, "Background save closed the unfinished minute");
            HardwareHistoryLogger.Save();
            Check(HardwareHistoryLogger.Data.Points.Count == 1 && HistoryOnDisk().SequenceEqual(new float[] { 45 }), "Exit save did not close the minute");
            HardwareHistoryLogger.Load();
            Check(HardwareHistoryLogger.GetPoints(DateTime.Now.AddHours(-1)).Count == 1, "Saved history did not load back");
            return Task.CompletedTask;
        });
    }

    private static void StartBackgroundSaveAndLetItReachTheLock()
    {
        long before = Monitor.LockContentionCount;
        LoggerMethod("SaveAsync").Invoke(null, null);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Monitor.LockContentionCount == before)
        {
            Check(waited.Elapsed < TimeSpan.FromSeconds(10), "Background save never reached the file lock");
            Thread.Sleep(1);
        }
        Thread.Sleep(50);
    }

    private static void ResetHistory()
    {
        Check(!string.Equals(AppPaths.DataDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MellowMeter"), StringComparison.OrdinalIgnoreCase), "Test would touch the real profile");
        Directory.CreateDirectory(AppPaths.DataDirectory);
        BackgroundWriter().Wait(TimeSpan.FromSeconds(10));
        foreach (string name in new[] { "HardwareHistory.json", "HardwareHistory.json.bak" })
            File.Delete(AppPaths.DataFile(name));
        HardwareHistoryLogger.Load();
    }

    private static void AddHistoryPoint(float marker)
    {
        lock (HistoryDataLock)
        {
            HardwareHistoryLogger.Data.Points.Add(new HardwareHistoryPoint
            {
                Time = DateTime.Now,
                Values = new() { ["MEM.Load"] = new HardwareHistoryValue { Avg = marker, Min = marker, Max = marker, Count = 1 } }
            });
        }
    }

    private static float[] HistoryOnDisk() =>
        JsonSerializer.Deserialize<HardwareHistoryData>(File.ReadAllText(AppPaths.DataFile("HardwareHistory.json")))!
            .Points.Select(x => x.Values["MEM.Load"].Avg).ToArray();

    private static Task BackgroundWriter() => (Task?)LoggerField("_saveTask") ?? Task.CompletedTask;

    private static object? LoggerField(string name) =>
        (typeof(HardwareHistoryLogger).GetField(name, LoggerStatics) ?? throw new InvalidOperationException("Missing field: " + name)).GetValue(null);

    private static MethodInfo LoggerMethod(string name) =>
        typeof(HardwareHistoryLogger).GetMethod(name, LoggerStatics) ?? throw new InvalidOperationException("Missing method: " + name);
}
