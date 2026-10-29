using System;
using System.IO;

namespace LiteMonitor.src.Core;

public static class AppPaths
{
    // A separate profile makes this derivative safe to run alongside LiteMonitor.
    // The override is useful for repeatable tests and portable deployments.
    public static string DataDirectory { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("MELLOWMETER_DATA_DIR") is { Length: > 0 } path
            ? path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MellowMeter"));

    public static string DataFile(string name) => Path.Combine(DataDirectory, name);
}
