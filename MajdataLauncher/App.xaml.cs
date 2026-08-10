using System.IO;
using System.Text;
using System.Windows;

namespace MajdataLauncher;

public partial class App : Application
{
    public App()
    {
        // The desktop pet previously disappeared silently, likely from an unhandled exception terminating the process.
        // Log every exception and handle dispatcher exceptions so the pet stays alive and launcher-error.log can diagnose the cause.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog(ex);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            args.Handled = true;
        };
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "launcher-error.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]\r\n{ex}\r\n", Encoding.UTF8);
        }
        catch
        {
            // Logging failures must not affect operation.
        }
    }
}
