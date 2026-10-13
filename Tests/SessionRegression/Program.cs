using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MajdataCore;

if (args.Contains("--allocate"))
{
    var s = AlphaSession.Create(); Console.WriteLine(s.Id); return;
}
if (args.Contains("--alpha-session"))
{
    var s = AlphaSession.Current;
    bool view = args.Contains("--view");
    s.Register(view, Process.GetCurrentProcess());
    var endpoint = new Uri(view ? AlphaSession.ViewEndpoint : AlphaSession.EditEndpoint);
    var server = new TcpListener(IPAddress.Loopback, endpoint.Port);
    server.Start();
    Console.WriteLine("READY " + AlphaSession.FileName("out.wav") + " " + AlphaSession.PipeName);
    var accepting = Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                using var client = await server.AcceptTcpClientAsync();
                using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                await writer.WriteLineAsync(s.Id + (view ? ":View" : ":Edit"));
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    });
    Console.ReadLine(); server.Stop(); await accepting; return;
}
var root = Path.Combine(Path.GetTempPath(), "AlphaSessionTests-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("MAJDATA_SESSION_TEST_ROOT", root);
var children = new List<Process>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Process Child(params string[] options)
{
    var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        psi.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (var option in options) psi.ArgumentList.Add(option);
    var p = Process.Start(psi)!; children.Add(p); return p;
}
try
{
    if (args.Contains("--muri-state-only"))
    {
        using var self = Process.GetCurrentProcess();
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, id); Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "session.txt"), new[] { "1", "18013", "18014", "18015", "0", "0", self.Id.ToString(), self.StartTime.ToUniversalTime().Ticks.ToString(), DateTime.UtcNow.Ticks.ToString(), "1" });
        var session = AlphaSession.Read(id);
        var detail = "2\t4\t" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("1-5 · LateGood\n判定异常"));
        session.PublishViewState(false, 1, 1, new[] { detail });
        Check(session.ReadDynamicMuri() == 1 && session.ReadDynamicMuriDetails().SequenceEqual(new[] { detail }), "muri details lost source or Unicode text");
        session.PublishViewState(false, 2);
        Check(session.ReadDynamicMuri() == 0 && session.ReadDynamicMuriDetails().Length == 0, "replay must clear stale details");
        var state = Path.Combine(directory, "view-state.txt");
        var values = File.ReadAllLines(state); values[2] = "0"; File.WriteAllLines(state, values);
        Check(session.ReadDynamicMuriDetails().Length == 0 && session.ReadDynamicMuri() == 0, "stale heartbeat exposed diagnostics");
        Console.WriteLine("PASS: dynamic diagnostics positions, Unicode, replay reset, stale heartbeat.");
        return;
    }
    if (args.Contains("--manifest-race-only"))
    {
        var racedSession = AlphaSession.Create();
        var manifestReaderTask = Task.Run(() =>
        {
            for (var n = 0; n < 500; n++)
                Check(AlphaSession.Read(racedSession.Id).Id == racedSession.Id, "concurrent manifest read failed");
        });
        using (var currentProcess = Process.GetCurrentProcess())
            for (var n = 0; n < 500; n++) racedSession.Register(false, currentProcess);
        await manifestReaderTask;
        Console.WriteLine("PASS: 500 concurrent session manifest reads and replacements.");
        return;
    }
    Check(AlphaSession.FromArguments(Array.Empty<string>()) == null, "legacy arguments");
    Check(AlphaSession.ViewEndpoint == "http://localhost:8013/", "legacy endpoint");
    Check(AlphaSession.FileName("out.wav") == "out.wav", "legacy filenames");
    try { AlphaSession.FromArguments(new[] { "--alpha-session", "../../other" }); throw new Exception("accepted bad id"); }
    catch (ArgumentException) { }
    var blocker = new TcpListener(IPAddress.Loopback, 18014); blocker.Start();
    var blocked = AlphaSession.Create(); blocker.Stop();
    Check(blocked.Slot != 1, "must skip an occupied Edit port even when View port is free");
    var allocators = Enumerable.Range(0, 6).Select(_ => Child("--allocate")).ToArray();
    var ids = await Task.WhenAll(allocators.Select(async p =>
    {
        var id = await p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await p.WaitForExitAsync(); Check(p.ExitCode == 0, await p.StandardError.ReadToEndAsync()); return id!;
    }));
    var sessions = ids.Select(AlphaSession.Read).ToArray();
    Check(sessions.SelectMany(s => new[] { s.ViewPort, s.EditPort, s.PetPort }).Distinct().Count() == 18, "concurrent allocations collided");
    var raced = sessions[^1];
    var manifestReader = Task.Run(() =>
    {
        for (var n = 0; n < 300; n++) Check(AlphaSession.Read(raced.Id).Id == raced.Id, "concurrent manifest read failed");
    });
    using (var currentProcess = Process.GetCurrentProcess())
        for (var n = 0; n < 300; n++) raced.Register(false, currentProcess);
    await manifestReader;
    var pairs = sessions.Take(2).ToArray();
    var probes = new List<Process>();
    var resources = new List<string>();
    foreach (var s in pairs)
    foreach (bool view in new[] { false, true })
    {
        var p = Child("--alpha-session", s.Id, view ? "--view" : "--edit"); probes.Add(p);
        var ready = await p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Check(ready?.StartsWith("READY ") == true, "child failed: " + ready);
        resources.Add(ready!);
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, view ? s.ViewPort : s.EditPort);
        using var reader = new StreamReader(client.GetStream());
        Check(await reader.ReadLineAsync() == s.Id + (view ? ":View" : ":Edit"), "cross-wired transport");
        using var registered = s.GetProcess(view);
        Check(registered?.Id == p.Id, "process registration mismatch");
        Check(!AlphaSession.IsProcess(p.Id, 1), "PID reuse must not target an unrelated process");
    }
    Check(resources[0] == resources[1] && resources[2] == resources[3] && resources[0] != resources[2], "pair file/pipe isolation failed");
    foreach (var p in probes) { await p.StandardInput.WriteLineAsync("stop"); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Check(p.ExitCode == 0, "child shutdown failed"); }
    Check(pairs.All(s => !AlphaSession.Read(s.Id).IsAlive), "stale process considered alive");
    Console.WriteLine("PASS: legacy compatibility; invalid IDs; occupied ports; six-process allocation; two Edit/View pairs; isolated file/pipe names; PID/start-time validation; closed process detection.");
    Console.WriteLine("Test manifests: " + root);
}
finally
{
    foreach (var p in children)
    {
        try { if (!p.HasExited) { p.StandardInput.WriteLine("stop"); if (!p.WaitForExit(2000)) p.Kill(); } } catch { }
        p.Dispose();
    }
}
