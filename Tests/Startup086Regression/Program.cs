using System.Diagnostics;
using System.Runtime.InteropServices;
using MajdataCore;
using MajdataLauncher;

var root = AppContext.BaseDirectory;
var trace = Path.Combine(root, "App", "MajdataEdit", "startup-trace.log");
var offset = File.Exists(trace) ? File.ReadAllText(trace).Length : 0;
var before = AlphaSession.List().Select(session => session.Id).ToHashSet();
Environment.SetEnvironmentVariable("MAJDATA_STARTUP_TRACE", "1");
var launcher = new LauncherService();
await launcher.LaunchAsync(new Progress<string>(), CancellationToken.None, "1100;700");
var session = AlphaSession.List().Single(item => !before.Contains(item.Id));
try
{
    var timer = Stopwatch.StartNew(); var tail = ""; var observedHiddenView = false; var observedHiddenEdit = false;
    var visibleSamples = new List<string>();
    while (timer.Elapsed.TotalSeconds < 45)
    {
        if (File.Exists(trace))
        {
            using var input = new FileStream(trace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(input);
            tail = reader.ReadToEnd()[offset..];
        }
        if (System.Text.RegularExpressions.Regex.Matches(tail, @"Entrance animation:").Count < 2)
        {
            foreach (var isView in new[] { true, false })
            {
                using var current = session.GetProcess(isView);
                var handle = current == null ? IntPtr.Zero : Native.FindWindow(current.Id);
                if (handle == IntPtr.Zero) continue;
                if (Native.IsPresented(handle)) visibleSamples.Add($"{(isView ? "View" : "Edit")}@{timer.ElapsedMilliseconds}ms");
                else if (isView) observedHiddenView = true; else observedHiddenEdit = true;
            }
        }
        if (tail.Contains("Entrance: complete, windows restored")) break;
        Thread.Sleep(10);
    }
    var phases = new[] { "Entrance: prepared", "Entrance: Edit from pet", "Entrance: View from Edit", "Entrance: complete, windows restored" };
    var previous = -1;
    foreach (var phase in phases)
    {
        var at = tail.IndexOf(phase, StringComparison.Ordinal);
        if (at <= previous) throw new Exception("Missing startup phase: " + phase + "\n" + tail);
        previous = at;
    }
    var animations = System.Text.RegularExpressions.Regex.Matches(tail, @"Entrance animation: (\d+) frames, max gap ([\d.]+)ms");
    if (animations.Count != 2 || animations.Cast<System.Text.RegularExpressions.Match>().Any(match => int.Parse(match.Groups[1].Value) < 10))
        throw new Exception("Startup did not render both animations: " + tail);
    if (!observedHiddenView || !observedHiddenEdit || visibleSamples.Count > 2)
        throw new Exception($"Native visibility during entrance: hidden Edit={observedHiddenEdit}, View={observedHiddenView}; visible samples={visibleSamples.Count}: {string.Join(',', visibleSamples.Take(25))}\n{tail}");
    using var edit = session.GetProcess(false); using var view = session.GetProcess(true);
    if (edit == null || view == null) throw new Exception("Paired process exited during startup");
    if (!Native.IsPresented(Native.FindWindow(view.Id)) || !Native.IsPresented(Native.FindWindow(edit.Id)))
        throw new Exception("A paired window remained hidden after animation");
    if (File.Exists(Path.Combine(session.DirectoryPath, "startup-frame.png"))) throw new Exception("Startup frame was not cleaned up");
    Console.WriteLine("087 STARTUP PASS: both native windows hidden during entrance; paired windows restored; temporary frame removed.");
    Console.WriteLine(tail);
    if (args.Contains("--play-errors-088"))
    {
        var folder = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        Directory.CreateDirectory(Path.Combine(folder, "088-parse"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("X-Majdata-Session", session.Id);
        foreach (var fixture in Directory.EnumerateFiles(folder, "088-parse-*.json").Order())
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { control = 0, jsonPath = fixture,
                chartDirectory = Path.Combine(folder, "088-parse"), startAt = DateTime.Now.Ticks,
                noteSpeed = 7.5f, touchSpeed = 7.5f, starSpeed = 0f, audioSpeed = 1f,
                showSongDetail = false, showAllPerfect = false, skin = "dx" });
            using var response = await client.PostAsync("http://localhost:" + session.ViewPort + "/",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            using var result = System.Text.Json.JsonDocument.Parse(body);
            if (!response.IsSuccessStatusCode || !result.RootElement.GetProperty("ok").GetBoolean() ||
                result.RootElement.GetProperty("droppedBeats").GetArrayLength() != 0)
                throw new Exception("Malformed-slot playback failed: " + Path.GetFileName(fixture) + " " + body);
            Console.WriteLine("088 NATIVE PLAY PASS: " + Path.GetFileName(fixture) + " accepted; no runtime-dropped beats.");
            using var stop = await client.PostAsync("http://localhost:" + session.ViewPort + "/",
                new StringContent("{\"control\":1}", System.Text.Encoding.UTF8, "application/json"));
            stop.EnsureSuccessStatusCode();
        }
    }
}
finally
{
    foreach (var view in new[] { false, true })
    {
        using var process = session.GetProcess(view);
        if (process == null) continue;
        process.CloseMainWindow();
        if (!process.WaitForExit(6000)) { Console.WriteLine("Test-owned process required termination: " + process.Id); process.Kill(); }
    }
}
internal static class Native
{
    internal static IntPtr FindWindow(int pid)
    {
        var found = IntPtr.Zero; var largest = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != pid || GetWindowTextLength(window) == 0 || !GetClientRect(window, out var bounds)) return true;
            var area = (bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top);
            if (area > largest) { largest = area; found = window; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    internal static bool IsPresented(IntPtr window)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)));
        return IsWindowVisible(window) && cloaked == 0;
    }
}
