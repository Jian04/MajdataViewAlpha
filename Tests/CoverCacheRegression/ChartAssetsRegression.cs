using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using MajdataCore;
using MajdataEdit;
using Newtonsoft.Json;
using Un4seen.Bass;

internal static class ChartAssetsRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly Assembly = typeof(MainWindow).Assembly;
    private static readonly Type Files = Assembly.GetType("MajdataEdit.ChartAssetFiles")!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Wait(Task task)
    {
        while (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    internal static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = Environment.CurrentDirectory;
        var root = Path.GetFullPath("alpha/.codex-build/063-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PATH", Path.Combine(workspace, "alpha/Assets/StreamingAssets") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        foreach (var name in new[] { "cover.mp4", "cover.gif", "cover.apng", "cover.png", "bg.apng", "bg.jpg", "pv.mp4", "mv.mp4" })
            File.WriteAllText(Path.Combine(root, name), name);
        Check(Path.GetFileName(ChartCover.Find(root)) == "cover.mp4", "Video cover priority");
        File.Delete(Path.Combine(root, "cover.mp4"));
        Check(Path.GetFileName(ChartCover.Find(root)) == "cover.gif", "GIF cover priority");
        File.Delete(Path.Combine(root, "cover.gif"));
        Check(Path.GetFileName(ChartCover.Find(root)) == "cover.apng", "Cover priority");
        Check(Path.GetFileName(ChartCover.FindBackground(root)) == "pv.mp4", "PV priority");
        Check(Path.GetFileName(ChartCover.FindBackground(root, false)) == "bg.apng", "BG image priority");
        File.Delete(Path.Combine(root, "cover.apng")); File.Delete(Path.Combine(root, "cover.png"));
        Check(Path.GetFileName(ChartCover.Find(root)) == "bg.apng", "APNG background fallback");

        var source = Path.Combine(root, "picture.jpg"); var target = Path.Combine(root, "cover.jpg");
        var conflict = Path.Combine(root, "cover.png");
        File.WriteAllText(source, "new image"); File.WriteAllText(conflict, "old image");
        var dataPath = Path.Combine(root, "maidata.txt");
        var chart = "&title=picture.jpg\n&inote_5=<PVOVERLAY*(True,\"picture.jpg\",8:1)>1~[picture.jpg],\n" +
            "<TEXT*(\"~[picture.jpg]\")>\n|| ~[picture.jpg]\n&inote_6=1~[picture.jpg]~[1-5[8:1]],";
        File.WriteAllText(dataPath, chart);
        var timeline = Path.Combine(root, "media_timeline.json");
        File.WriteAllText(timeline, JsonConvert.SerializeObject(new { Clips = new[] { new {
            Track = 0, SourcePath = "picture.jpg", Name = "picture.jpg", Duration = 12, SourceDuration = 12 } } }));
        var updates = (Dictionary<string, string>)Files.GetMethod("PrepareReferenceUpdates", All)!.Invoke(null,
            new object[] { root, new[] { source }, target })!;
        Check(updates[dataPath].Contains("True,\"cover.jpg\"") && updates[dataPath].Contains("1~[cover.jpg]~[1-5[8:1]]") &&
            updates[dataPath].Contains("<TEXT*(\"~[picture.jpg]\")>") && updates[dataPath].Contains("|| ~[picture.jpg]") &&
            updates[dataPath].Contains("&title=picture.jpg"), "Only actual references should be renamed");
        Task Replace() => (Task)Files.GetMethod("ReplaceAsync", All)!.Invoke(null,
            new object[] { source, target, new[] { conflict }, true, new[] { source } })!;
        // Lock the second metadata file: asset and first metadata moves must roll back together.
        using (var locked = new FileStream(timeline, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { Wait(Replace()); } catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(source) == "new image" && File.ReadAllText(conflict) == "old image" &&
                File.ReadAllText(dataPath) == chart && !File.Exists(target), "Locked file must leave every original intact");
        }
        Wait(Replace());
        Check(File.ReadAllText(target) == "new image" && !File.Exists(source) && !File.Exists(conflict) &&
            File.ReadAllText(dataPath) == updates[dataPath] && File.ReadAllText(timeline).Contains("cover.jpg") &&
            !Directory.GetFiles(root, ".asset-*").Any(), "Commit must include all references and remove temporary files");
        var background = Path.Combine(root, "bg.jpg");
        File.AppendAllText(dataPath, "\n&inote_7=1~[bg.jpg],");
        var savedChart = File.ReadAllText(dataPath);
        File.WriteAllText(background, "background retained");
        Wait((Task)Files.GetMethod("ReplaceAsync", All)!.Invoke(null,
            new object[] { background, target, new[] { target }, false, Array.Empty<string>() })!);
        Check(File.ReadAllText(background) == "background retained" && File.ReadAllText(target) == "background retained" &&
            File.ReadAllText(dataPath) == savedChart && !Directory.GetFiles(root, ".asset-*").Any(),
            "Copying a background to cover must preserve the source and its existing references");
        Directory.SetCurrentDirectory(workspace);
        Console.WriteLine("ASSET PASS: priority, rename, reference boundaries, locked-file rollback, commit and cleanup.");

        var tools = Assembly.GetType("MajdataEdit.MediaTools")!;
        var audioRoot = Path.Combine(root, "audio"); Directory.CreateDirectory(audioRoot);
        var audio = Path.Combine(audioRoot, "custom.mp3"); File.WriteAllText(audio, "passthrough needs no decoder");
        File.WriteAllText(Path.Combine(audioRoot, "media_timeline.json"), JsonConvert.SerializeObject(new { Clips = new[] { new {
            Track = 1, TrackIndex = 0, SourcePath = "custom.mp3", TimelineStart = 0, SourceOffset = 0,
            SourceDuration = 12, Duration = 12, Volume = 1 } } }));
        var build = (Task<string?>)tools.GetMethod("BuildTimelineAudioAsync", All)!.Invoke(null, new object[] { audioRoot })!;
        Wait(build);
        Check(build.Result == audio && !Directory.Exists(Path.Combine(audioRoot, "media")), "Untouched custom audio must not produce a WAV cache");

        var video = Path.Combine(root, "actual.mp4");
        using (var encoder = Process.Start(new ProcessStartInfo(Path.Combine(workspace, "alpha/Assets/StreamingAssets/ffmpeg.exe"),
            $"-nostdin -v error -y -f lavfi -i color=c=black:s=32x32:r=30 -t 12.345 -an \"{video}\"")
            { CreateNoWindow = true, UseShellExecute = false }))
        { encoder!.WaitForExit(); Check(encoder.ExitCode == 0, "Video fixture"); }
        var poster = (byte[])tools.GetMethod("ReadVideoPoster", All)!.Invoke(null, new object[] { video })!;
        using (var stream = new MemoryStream(poster))
        {
            var image = System.Windows.Media.Imaging.BitmapFrame.Create(stream);
            Check(image.PixelWidth == 32 && image.PixelHeight == 32, "MP4 cover poster must decode as a real image");
        }
        var probe = (Task<double>)tools.GetMethod("ProbeVideoDurationAsync", All)!.Invoke(null, new object[] { video })!;
        Wait(probe); Check(Math.Abs(probe.Result - 12.345) < .05, "Real container duration must not become 10 seconds");
        var panel = new MediaTimelineEditor();
        var directoryA = Path.Combine(root, "one"); var directoryB = Path.Combine(root, "two");
        Directory.CreateDirectory(directoryA); Directory.CreateDirectory(directoryB);
        File.Copy(video, Path.Combine(directoryA, "pv.mp4")); File.Copy(video, Path.Combine(directoryB, "mv.mp4"));
        var first = panel.ConfigureAsync(directoryA, 20, Array.Empty<double>(), Array.Empty<double>(), .5, null, null);
        var second = panel.ConfigureAsync(directoryB, 20, Array.Empty<double>(), Array.Empty<double>(), .5, null, null);
        Wait(Task.WhenAll(first, second));
        var configured = (string)typeof(MediaTimelineEditor).GetField("loadedChartDirectory", All)!.GetValue(panel)!;
        var project = typeof(MediaTimelineEditor).GetField("project", All)!.GetValue(panel)!;
        var clips = (IList)project.GetType().GetProperty("Clips")!.GetValue(project)!;
        Check(configured == directoryB && clips.Count == 1 &&
            (string)clips[0]!.GetType().GetProperty("SourcePath")!.GetValue(clips[0])! == "mv.mp4" &&
            (double)clips[0]!.GetType().GetProperty("Duration")!.GetValue(clips[0])! > 12,
            "Overlapping opens must finish on the requested chart with its full MV");
        File.WriteAllText(Path.Combine(root, "bad.mp4"), "invalid mp4");
        var invalid = (Task<double>)tools.GetMethod("ProbeVideoDurationAsync", All)!.Invoke(null, new object[] { Path.Combine(root, "bad.mp4") })!;
        var rejected = false;
        try { Wait(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Unreadable media must report an error instead of returning a made-up duration");
        Console.WriteLine($"MEDIA PASS: untouched audio bypass; video {probe.Result:F3}s; concurrent open, MV fallback and invalid-media error.");

        var main = new MainWindow();
        var majson = Activator.CreateInstance(Assembly.GetType("MajdataEdit.Majson")!)!;
        var effectiveMedia = (IList)majson.GetType().GetField("mediaTable", All)!.GetValue(majson)!;
        effectiveMedia.Add(Activator.CreateInstance(effectiveMedia.GetType().GenericTypeArguments[0]));
        var isAlpha = typeof(MainWindow).GetMethod("IsAlphaChart", All)!;
        Check(!(bool)isAlpha.Invoke(null, new[] { majson })!, "Default media must not turn a standard chart into Alpha");
        majson.GetType().GetField("hasAlphaEditorSyntax", All)!.SetValue(majson, true);
        Check((bool)isAlpha.Invoke(null, new[] { majson })!, "Explicit Alpha media syntax must retain its badge");
        var coverRoot = Path.Combine(root, "cover-bg"); Directory.CreateDirectory(coverRoot);
        var cover = Path.Combine(coverRoot, "cover.png"); File.WriteAllText(cover, "cover bytes");
        var ensureBg = typeof(MainWindow).GetMethod("EnsureCoverBackground", All)!;
        ensureBg.Invoke(null, new object[] { cover, coverRoot });
        Check(File.ReadAllText(Path.Combine(coverRoot, "bg.png")) == "cover bytes", "Missing BG must be copied from the static cover");
        File.WriteAllText(cover, "changed cover"); ensureBg.Invoke(null, new object[] { cover, coverRoot });
        Check(File.ReadAllText(Path.Combine(coverRoot, "bg.png")) == "cover bytes", "An existing background must be retained");
        Console.WriteLine("085 BADGE / BG PASS: default media, explicit Alpha, missing BG and retained BG.");
        var playing = typeof(MainWindow).GetField("isPlaying", All)!;
        var start = typeof(MainWindow).GetMethod("StartSELoop", All)!;
        start.Invoke(main, null);
        var worker = (Thread)typeof(MainWindow).GetField("soundEffectThread", All)!.GetValue(main)!;
        try
        {
            for (var index = 0; index < 100; index++)
            { playing.SetValue(main, false); playing.SetValue(main, true); start.Invoke(main, null); }
            Check(ReferenceEquals(worker, typeof(MainWindow).GetField("soundEffectThread", All)!.GetValue(main)),
                "Pause/resume must reuse one scheduler thread");
        }
        finally
        {
            playing.SetValue(main, false);
            typeof(MainWindow).GetField("soundEffectShutdown", All)!.SetValue(main, true);
            ((AutoResetEvent)typeof(MainWindow).GetField("soundEffectWake", All)!.GetValue(main)!).Set();
            Check(worker.Join(2000), "Scheduler must stop on shutdown");
        }
        Console.WriteLine("AUDIO SCHEDULER PASS: 100 pause/resume starts retain one worker and stop cleanly.");
        var settings = Activator.CreateInstance(Assembly.GetType("MajdataEdit.EditorSetting")!)!;
        settings.GetType().GetField("DragPreview", All)!.SetValue(settings, false);
        typeof(MainWindow).GetField("editorSetting", All)!.SetValue(main, settings);
        typeof(MainWindow).GetField("lastEditorState", All)!.SetValue(main, Enum.Parse(Assembly.GetType("MajdataEdit.EditorControlMethod")!, "Stop"));
        typeof(MainWindow).GetMethod("QueueNotePreview", All)!.Invoke(main, new object[] { false });
        var timer = (System.Timers.Timer)typeof(MainWindow).GetField("notePreviewTimer", All)!.GetValue(main)!;
        Check(timer.Enabled, "Disabling drag preview must preserve stopped static preview");
        typeof(MainWindow).GetMethod("CancelNotePreview", All)!.Invoke(main, null);
        var predecessor = new TaskCompletionSource<bool>();
        Check(Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero), "No-sound decoder device");
        typeof(MainWindow).GetField("editorAudioDevice", All)!.SetValue(main, 0);
        var validate = typeof(MainWindow).GetMethod("ValidateChartAssetAsync", All)!;
        var audioRole = Enum.Parse(Assembly.GetType("MajdataEdit.ChartAssetRole")!, "Audio");
        var actualAudio = @"C:\Users\sjtu_\Downloads\⚡朋友的酒⚡\⚡朋友的酒⚡\track.mp3";
        Wait((Task)validate.Invoke(main, new object[] { actualAudio, audioRole })!);
        var invalidAudio = false;
        try { Wait((Task)validate.Invoke(main, new object[] { audio, audioRole })!); }
        catch (IOException) { invalidAudio = true; }
        Check(invalidAudio && File.ReadAllText(target) == "background retained", "Invalid incoming audio must be rejected before replacing files");
        Console.WriteLine("ASSET DECODE PASS: valid MP3 accepted; invalid audio rejected before replacement.");
        var previewAudio = Bass.BASS_StreamCreateFile(@"C:\Users\sjtu_\Downloads\⚡朋友的酒⚡\⚡朋友的酒⚡\track.mp3",
            0, 0, BASSFlag.BASS_STREAM_DECODE);
        Check(previewAudio != 0, "Preview decoder");
        typeof(MainWindow).GetField("bgmStream", All)!.SetValue(main, previewAudio);
        typeof(MainWindow).GetField("pendingPlaySend", All)!.SetValue(main, predecessor.Task);
        typeof(MainWindow).GetField("songLength", All)!.SetValue(main, 60d);
        typeof(MainWindow).GetField("flowTimelineCursor", All)!.SetValue(main, (double?)19.5);
        var queueFinished = new TaskCompletionSource<bool>();
        typeof(MainWindow).GetField("chartQueueCompletion", All)!.SetValue(main, queueFinished);
        typeof(MainWindow).GetField("multiAudioMuted", All)!.SetValue(main, true);
        playing.SetValue(main, true);
        typeof(MainWindow).GetMethod("PrepareTimelineDrag", All)!.Invoke(main, null);
        Check(!(bool)playing.GetValue(main)! && (bool)typeof(MainWindow).GetField("stopPending", All)!.GetValue(main)! &&
            Math.Abs((double)typeof(MainWindow).GetMethod("GetTimelinePosition", All)!.Invoke(main, null)! - 19.5) < .0001,
            "Drag with the option off must stop immediately and preserve the cursor");
        Check(queueFinished.Task.IsCompleted && !queueFinished.Task.Result &&
            !(bool)typeof(MainWindow).GetField("multiAudioMuted", All)!.GetValue(main)!,
            "Drag termination must preserve normal Stop's queue completion and group-volume restore");
        predecessor.SetResult(false);
        Wait((Task)typeof(MainWindow).GetField("pendingScrubStop", All)!.GetValue(main)!);
        Bass.BASS_StreamFree(previewAudio); Bass.BASS_Free();
        Console.WriteLine("DRAG OPTION PASS: stopped static preview stays enabled; dragging requests Stop and preserves its position.");
    }
}
