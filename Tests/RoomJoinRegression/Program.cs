using System.IO;
using System.Windows;
using System.Windows.Threading;
using MajdataCollaboration;
using MajdataEdit;
using MajdataSessionUi;
using Un4seen.Bass;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(args[0]); result = 0; }
            catch (Exception error) { Console.WriteLine(error); }
            finally { Bass.BASS_Free(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
        }));
        Dispatcher.Run();
        return result;
    }
    private static async Task Run(string audioSource)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "join-fixture");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("MAJDATA_SESSION_TEST_ROOT", Path.Combine(root, "sessions"));
        File.Copy(audioSource, Path.Combine(root, "track.mp3"), true);
        string shared = "&title=Join Audio Probe\n&wholebpm=120\n&lv_5=12\n&inote_5=(120){4}1,2,3,4,E\n";
        File.WriteAllText(Path.Combine(root, "maidata.txt"), shared);
        if (!Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero)) throw new Exception("BASS init failed");
        using var server = new RoomServer(0, "join-test", shared, true, root) { HostDifficulty = 4 };
        using var tunnel = await RoomPublicTunnel.Open(server.Port, Console.WriteLine);
        var local = "local draft";
        var window = new ChartRoomWindow(() => local, text => local = text, _ => "") { DisplayName = "Join Probe" };
        bool installed = false; int difficulty = -1;
        window.SelectDifficulty = value => difficulty = value;
        window.InstallSongAsync = async (folder, text, cancellation) =>
        {
            using var prepared = await Task.Run(() => EditorSongAudio.Prepare(Path.Combine(folder, "track.mp3"), 0, true, cancellation));
            if (!window.Dispatcher.CheckAccess()) throw new Exception("Installation lost the WPF dispatcher");
            if (prepared.Length <= 0 || prepared.Waveforms.Any(level => level.Length <= 1)) throw new Exception("Invalid installed audio");
            if (text != shared) throw new Exception("Chart differs from host");
            installed = true;
        };
        var heartbeat = 0;
        var previousStatus = "";
        var status = (System.Windows.Controls.TextBlock)typeof(ChartRoomWindow).GetField("status",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { heartbeat++; if (status.Text != previousStatus) { previousStatus = status.Text; Console.WriteLine(previousStatus); } }; timer.Start();
        try { await window.Join(tunnel.Address, "join-test").WaitAsync(TimeSpan.FromSeconds(120)); }
        catch { window.Disconnect(); window.Close(); timer.Stop(); throw; }
        if (!window.Connected || !installed || difficulty != 4 || local != shared || heartbeat < 5)
            throw new Exception("Incomplete join or blocked UI");
        window.Close(); timer.Stop();
        Console.WriteLine($"JOIN PASS: public transfer, real BASS decoding, installed difficulty, WPF responsive ({heartbeat} ticks)");

        // A validly transferred, invalid audio file must fail before applying text.
        var invalidRoot = Path.Combine(root, "invalid"); Directory.CreateDirectory(invalidRoot);
        File.WriteAllText(Path.Combine(invalidRoot, "track.mp3"), "Not audio");
        server.SwitchSong(shared, invalidRoot, 4);
        local = "local draft";
        var bad = new ChartRoomWindow(() => local, text => local = text, _ => "") { DisplayName = "Bad Audio Probe" };
        bad.InstallSongAsync = async (folder, _, token) =>
        { using var prepared = await Task.Run(() => EditorSongAudio.Prepare(Path.Combine(folder, "track.mp3"), 0, true, token)); };
        try { await bad.Join(tunnel.Address, "join-test"); throw new Exception("Invalid audio joined"); }
        catch (IOException error) when (error.Message.Contains("BASS_ERROR_FILEFORM"))
        {
            if (bad.Connected || local != "local draft") throw new Exception("Failed join replaced the local chart");
            Console.WriteLine("JOIN PASS: invalid audio preserves local chart, exits joining state, records exact BASS error");
        }
        bad.Close();

        // Terminate only the helper owned by this test room, then verify that
        // the host replaces its public address and still serves the same chart.
        var host = new ChartRoomWindow(() => shared, _ => { }, _ => "")
        { DisplayName = "Restart Probe", MediaRoot = () => root };
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        ((System.Windows.Controls.TextBox)typeof(ChartRoomWindow).GetField("roomName", fields)!.GetValue(host)!).Text = "Restart Probe";
        await ((Task)typeof(ChartRoomWindow).GetMethod("Host", fields)!.Invoke(host, null)!).WaitAsync(TimeSpan.FromSeconds(90));
        var tunnelField = typeof(ChartRoomWindow).GetField("internetTunnel", fields)!;
        var old = (RoomPublicTunnel)tunnelField.GetValue(host)!;
        var helper = (System.Diagnostics.Process)typeof(RoomPublicTunnel).GetField("process", fields)!.GetValue(old)!;
        helper.Kill(entireProcessTree: true);
        await helper.WaitForExitAsync();
        using var restartDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        RoomPublicTunnel? restored;
        while (ReferenceEquals(restored = (RoomPublicTunnel?)tunnelField.GetValue(host), old) || restored?.IsRunning != true)
            await Task.Delay(250, restartDeadline.Token);
        using var reconnected = new RoomClient(restored.Address, "");
        if ((await reconnected.Exchange(0, "", true)).Text != shared) throw new Exception("Restored public room lost the chart");
        host.Close();
        Console.WriteLine("JOIN PASS: own tunnel helper failure automatically restores a usable public address and preserves host chart");
    }
}
