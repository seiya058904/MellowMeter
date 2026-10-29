using System.Security.Cryptography;
using System.Text;
using LiteMonitor.src.Core;

namespace LiteMonitor;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        StartupDiagnostics.Mark("entry");
        var profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant())));
        using var mutex = new Mutex(true, "Local\\MellowMeter_" + profile, out bool firstInstance);
        if (!firstInstance) return;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogCrash(e.Exception, "UI");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception, "Background");
        try
        {
            ApplicationConfiguration.Initialize();
            StartupDiagnostics.Mark("framework initialized");
            var main = new MainForm();
            if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
                main.Shown += (_, _) => main.BeginInvoke(new Action(main.OpenSettings));
            Application.Run(main);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void LogCrash(Exception? exception, string source)
    {
        if (exception == null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var path = AppPaths.DataFile("MellowMeter_Error.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{source}] {exception}\n");
            MessageBox.Show($"MellowMeter encountered an error.\n\n{exception.Message}\n\nDetails: {path}",
                "MellowMeter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
