using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MajdataEdit;

internal static class MediaWait092Regression
{
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var main = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var field = typeof(MainWindow).GetField("timelineAudioBuildTask", flags)!;
        var method = typeof(MainWindow).GetMethod("EnsureTimelineAudioReadyAsync", flags)!;
        var pending = new TaskCompletionSource<string?>();
        field.SetValue(main, pending.Task);
        var watch = Stopwatch.StartNew();
        var wait = (Task<bool>)method.Invoke(main, null)!;
        if (wait.IsCompleted || watch.ElapsedMilliseconds > 100)
            throw new Exception("Pending audio preparation blocked the dispatcher");
        field.SetValue(main, Task.FromResult<string?>(null));
        pending.SetResult("old-song.wav");
        if (wait.GetAwaiter().GetResult()) throw new Exception("An old song mix was accepted after switching songs");
        field.SetValue(main, Task.FromException<string?>(new IOException("test mix failure")));
        wait = (Task<bool>)method.Invoke(main, null)!;
        try { wait.GetAwaiter().GetResult(); throw new Exception("Mix failure was silently ignored"); }
        catch (IOException error) when (error.Message == "test mix failure") { }
        Console.WriteLine("MEDIA WAIT PASS: pending mix yields immediately, changed song is rejected, failed mix reaches the playback error handler.");
    }
}
