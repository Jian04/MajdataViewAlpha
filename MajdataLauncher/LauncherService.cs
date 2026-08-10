using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using MajdataLauncher.Models;

namespace MajdataLauncher;

internal sealed class LauncherService
{
    private readonly string baseDirectory = AppContext.BaseDirectory;
    public LauncherSettings Settings { get; }

    private static string L(string key, params object[] args) => LauncherLocalization.Text(key, args);

    public LauncherService()
    {
        Settings = LoadSettings();
    }

    public async Task LaunchAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var viewPath = ResolveExecutable(Settings.ViewPath, ViewCandidates());
        var editPath = ResolveExecutable(Settings.EditPath, EditCandidates());
        if (editPath == null)
            throw new FileNotFoundException(L("MissingEdit"));

        var waitForView = false;
        var viewRunning = IsAnyProcessRunning("MajdataView", "MajdataViewAlpha");
        if (viewRunning)
        {
            progress.Report(L("ViewAlreadyRunning"));
        }
        else if (viewPath != null)
        {
            progress.Report(L("StartingView"));
            Start(viewPath);
            waitForView = true;
        }
        else
        {
            throw new FileNotFoundException(L("MissingView"));
        }

        if (!IsUsableEditorRunning())
        {
            progress.Report(L("StartingEdit"));
            Start(editPath);
        }

        if (waitForView)
        {
            progress.Report(L("WaitingForView"));
            try
            {
                await WaitForPortAsync(Settings.ViewReadyPort,
                    TimeSpan.FromSeconds(Math.Clamp(Settings.ViewReadyTimeoutSeconds, 3, 60)), cancellationToken);
            }
            catch (TimeoutException)
            {
                progress.Report(L("ViewTimeout"));
            }
        }

        progress.Report(L("ReadyAll"));
    }

    private LauncherSettings LoadSettings()
    {
        var path = Path.Combine(baseDirectory, "launcher.json");
        if (!File.Exists(path))
        {
            var defaults = new LauncherSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions()));
            return defaults;
        }
        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions()) ?? new();
            // Port 8014 belongs to Edit's visual-note bridge. Migrate early launcher
            // configurations so the pet can never consume View click requests.
            if (settings.PetControlPort == 8014)
            {
                settings.PetControlPort = 8015;
                File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions()));
            }
            return settings;
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    private string? ResolveExecutable(string configured, IEnumerable<string> candidates)
    {
        var candidatePaths = candidates.Select(Path.GetFullPath).ToList();
        var packagedCandidate = candidatePaths.FirstOrDefault();
        if (packagedCandidate != null && File.Exists(packagedCandidate))
            return packagedCandidate;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            var path = Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }
        return candidatePaths.Skip(1).FirstOrDefault(File.Exists);
    }

    private IEnumerable<string> ViewCandidates()
    {
        // Release layout: Launcher is at the root, with View and Edit under App\.
        yield return Path.Combine(baseDirectory, "App", "MajdataView", "MajdataView.exe");
        yield return Path.Combine(baseDirectory, "MajdataView.exe");
        yield return Path.Combine(baseDirectory, "MajdataViewAlpha.exe");
        yield return Path.Combine(baseDirectory, "..", "MajdataView.exe");
        yield return Path.Combine(baseDirectory, "..", "MajdataViewAlpha.exe");
        yield return Path.Combine(baseDirectory, "..", "..", "..", "..", "MajdataView.exe");
    }

    private IEnumerable<string> EditCandidates()
    {
        yield return Path.Combine(baseDirectory, "App", "MajdataEdit", "MajdataEdit.exe");
        yield return Path.Combine(baseDirectory, "MajdataEdit.exe");
        yield return Path.Combine(baseDirectory, "MajdataEdit", "MajdataEdit.exe");
        yield return Path.Combine(baseDirectory, "..", "MajdataEdit.exe");
        yield return Path.Combine(baseDirectory, "..", "MajdataEdit", "MajdataEdit.exe");
        yield return Path.Combine(baseDirectory, "..", "..", "..", "..", "MajdataEdit", "bin", "Debug", "net6.0-windows", "MajdataEdit.exe");
    }

    private static void Start(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static bool IsAnyProcessRunning(params string[] names) =>
        names.Any(name => Process.GetProcessesByName(name).Length > 0);

    private static bool IsUsableEditorRunning()
    {
        foreach (var name in new[] { "MajdataEdit", "MajdataEditAlpha" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (!process.HasExited &&
                    (process.MainWindowHandle != IntPtr.Zero ||
                     DateTime.Now - process.StartTime < TimeSpan.FromSeconds(15)))
                    return true;
            }
            catch
            {
                // A process that disappears while being inspected is not a usable editor.
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    private static async Task WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(150, cancellationToken);
            }
        }
        throw new TimeoutException(L("ViewPortTimeout", port, timeout.TotalSeconds));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
