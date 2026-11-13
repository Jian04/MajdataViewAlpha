using System.IO;
using System.Reflection;

internal static class Startup084Regression
{
    internal static void Run()
    {
        const string root = "D:/Maimai/release/MajdataAlpha";
        var trace = Path.Combine(root, "App/MajdataEdit/startup-trace.log");
        var before = File.Exists(trace) ? File.ReadAllText(trace).Length : 0;
        var assembly = Assembly.LoadFrom(Path.GetFullPath("alpha/MajdataLauncher/bin/Release/net6.0-windows/MajdataLauncher.dll"));
        var type = assembly.GetType("MajdataLauncher.LauncherService")!;
        var launcher = Activator.CreateInstance(type, true)!;
        var settings = type.GetProperty("Settings")!.GetValue(launcher)!;
        settings.GetType().GetProperty("EditPath")!.SetValue(settings, Path.Combine(root, "App/MajdataEdit/MajdataEdit.exe"));
        settings.GetType().GetProperty("ViewPath")!.SetValue(settings, Path.Combine(root, "App/MajdataView/MajdataView.exe"));
        var oldTrace = Environment.GetEnvironmentVariable("MAJDATA_STARTUP_TRACE");
        try
        {
            Environment.SetEnvironmentVariable("MAJDATA_STARTUP_TRACE", "1");
            type.GetMethod("LaunchIndependent", new[] { typeof(string) })!.Invoke(launcher, new object[] { "1100;700" });
        }
        finally { Environment.SetEnvironmentVariable("MAJDATA_STARTUP_TRACE", oldTrace); }
        var deadline = DateTime.UtcNow.AddSeconds(45);
        string tail = "";
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(trace)) tail = File.ReadAllText(trace)[before..];
            if (tail.Contains("Entrance: complete, windows restored")) break;
            Thread.Sleep(100);
        }
        var markers = new[] { "Entrance: prepared", "Entrance: Edit from pet", "Entrance: View from Edit", "Entrance: complete, windows restored" };
        var last = -1;
        foreach (var marker in markers)
        {
            var index = tail.IndexOf(marker, StringComparison.Ordinal);
            if (index <= last) throw new Exception("Startup sequence missing/out of order: " + marker + "\n" + tail);
            last = index;
        }
        Console.WriteLine("STARTUP 084 PASS: Launcher launch path → Edit appearance → View entrance → restored windows.");
        Console.WriteLine(tail);
    }
}
