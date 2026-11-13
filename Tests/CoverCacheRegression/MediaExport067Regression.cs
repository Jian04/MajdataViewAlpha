using System.Diagnostics;
using System.IO;
using System.Reflection;
using MajdataEdit;
using Newtonsoft.Json;

internal static class MediaExport067Regression
{
    internal static void Run()
    {
        var root = Path.GetFullPath(".codex-build/067-export"); Directory.CreateDirectory(root);
        var ffmpeg = Path.GetFullPath("alpha/Assets/StreamingAssets/ffmpeg.exe");
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(ffmpeg) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        void Ffmpeg(params string[] args)
        {
            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = root };
            info.ArgumentList.Add("-y");
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var errors = process.StandardError.ReadToEndAsync(); process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception(errors.GetAwaiter().GetResult());
        }
        Ffmpeg("-f", "lavfi", "-i", "aevalsrc=0.1|0.1:s=44100:d=5", "tone.wav");
        Ffmpeg("-f", "lavfi", "-i", "color=red:s=64x64:r=30:d=1", "-f", "lavfi", "-i", "color=blue:s=64x64:r=30:d=1", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0", "source.mp4");
        File.WriteAllText(Path.Combine(root, "media_timeline.json"), JsonConvert.SerializeObject(new { Clips = new object[] {
            new { Track = 1, TrackIndex = 0, SourcePath = "tone.wav", TimelineStart = .5, SourceDuration = 5, Duration = 2, Volume = 1 },
            new { Track = 1, TrackIndex = 1, SourcePath = "tone.wav", TimelineStart = 1.5, SourceDuration = 5, Duration = 2, Volume = .5 },
            new { Track = 0, TrackIndex = 0, SourcePath = "source.mp4", TimelineStart = 1, SourceOffset = 1, SourceDuration = 2, Duration = 1, Opacity = 1 },
            new { Track = 0, TrackIndex = 1, SourcePath = "source.mp4", TimelineStart = 0, SourceOffset = 0, SourceDuration = 2, Duration = 1.5, Opacity = 1 }
        }}));
        var assembly = typeof(MainWindow).Assembly;
        var tools = assembly.GetType("MajdataEdit.MediaTools")!;
        void Export(string method, string file, object? options) => ((Task)tools.GetMethod(method)!.Invoke(null,
            new[] { root, Path.Combine(root, file), options })!).GetAwaiter().GetResult();
        Export("ExportTimelineAudioAsync", "mix.wav", null);
        var videoOptionsType = assembly.GetType("MajdataEdit.TimelineVideoExportOptions")!;
        var options = Activator.CreateInstance(videoOptionsType)!;
        videoOptionsType.GetProperty("SmartTargetSize")!.SetValue(options, false);
        videoOptionsType.GetProperty("FrameRate")!.SetValue(options, 60);
        Export("ExportTimelineVideoAsync", "mix.mp4", options);
        Ffmpeg("-i", "mix.wav", "-af", "pan=mono|c0=c0", "-f", "f32le", "samples.raw");
        var samples = File.ReadAllBytes(Path.Combine(root, "samples.raw"));
        if (samples.Length != (int)(3.5 * 44100 * 4)) throw new Exception("Audio export duration is not 3.5 seconds");
        foreach (var (time, expected) in new[] { (.25, 0f), (.75, .1f), (1.75, .15f), (3.0, .05f) })
            if (Math.Abs(BitConverter.ToSingle(samples, (int)(time * 44100) * 4) - expected) > .002)
                throw new Exception("Audio gap/track sum mismatch at " + time);
        Ffmpeg("-i", "mix.mp4", "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", "frames.raw");
        var frames = File.ReadAllBytes(Path.Combine(root, "frames.raw"));
        var stride = 64 * 64 * 3;
        if (frames.Length != 120 * stride) throw new Exception("Video duration/frame rate mismatch: expected 120 frames at 60 FPS");
        foreach (var (time, blue) in new[] { (.5, false), (1.25, true), (1.75, true) })
        {
            var offset = (int)(time * 60) * stride + stride / 2;
            if (blue ? frames[offset + 2] < 220 || frames[offset] > 30 : frames[offset] < 220 || frames[offset + 2] > 30)
                throw new Exception("Video trim/layer/gap mismatch at " + time);
        }
        File.WriteAllText(Path.Combine(root, "media_timeline.json"), JsonConvert.SerializeObject(new { Clips = new object[] {
            new { Track = 1, TrackIndex = 0, SourcePath = "tone.wav", TimelineStart = 0, SourceDuration = 5, Duration = 5, Volume = 1 }
        }}));
        var passThrough = ((Task<string?>)tools.GetMethod("BuildTimelineAudioAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, new object[] { root })!).GetAwaiter().GetResult();
        if (passThrough != Path.Combine(root, "tone.wav")) throw new Exception("Unedited audio must reuse the source");
        tools.GetMethod("PruneTimelineAudioCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { root, passThrough });
        if (Directory.Exists(Path.Combine(root, "media", ".cache"))) throw new Exception("Obsolete timeline mix cache was retained");
        Console.WriteLine("MEDIA CACHE PASS: unedited source is passed through; obsolete mix removed without deleting the source.");
        Console.WriteLine("MEDIA EXPORT PASS: 3.5s stereo mix with leading gaps and additive tracks; video source trim, track 1 over 2, no stale final frame, 120 frames at 60 FPS.");
    }
}
