using System.Collections.Concurrent;
using MajdataCollaboration;

int assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
var targets = Enumerable.Range(1, 3).Select(i => new GroupTarget(Guid.NewGuid().ToString("N"), i, i)).ToArray();
foreach (string kind in new[] { "play", "record" })
foreach (string failure in new[] { "", "check", "prepare", "commit", "cancel" })
{
    using var cancel = new CancellationTokenSource();
    var calls = new ConcurrentQueue<(string Id, GroupCommand Command)>();
    int prepared = 0;
    async Task<GroupReply> Send(GroupTarget target, GroupCommand command)
    {
        GroupControl.Validate(command);
        calls.Enqueue((target.Id, command));
        if (command.Action.StartsWith("prepare-"))
        {
            await Task.Delay(target.Pid * 8);
            Interlocked.Increment(ref prepared);
            if (failure == "cancel") cancel.Cancel();
        }
        if (command.Action.StartsWith("commit-")) Check(prepared == 3, "Commit before every participant prepared");
        return new(!(target.Pid == 2 && command.Action == failure + "-" + kind), "test",
            IntroDuration: target.Pid == 1 ? 0 : 5,
            ChartDirectory: Path.Combine(Path.GetTempPath(), target.Pid == 3 ? "song-b" : "song-a"));
    }
    var reply = await GroupBatch.Start(targets, new() { Action = kind }, Send, cancel.Token);
    var history = calls.ToArray();
    Check(history.Where(c => c.Command.Action.StartsWith("prepare-") || c.Command.Action.StartsWith("commit-")).All(c => c.Command.IntroDuration == 0), "All peers must use the first participant's intro setting");
    var commits = history.Where(c => c.Command.Action.StartsWith("commit-")).ToArray();
    Check(commits.Length == (failure is "" or "commit" ? 3 : 0), "Failure/cancellation barrier broken");
    Check(history.Count(c => c.Command.Action == "abort") == (failure is "prepare" or "commit" or "cancel" ? 3 : 0), "Rollback must cover every participant");
    if (commits.Length > 0)
    {
        Check(commits.Select(c => c.Command.StartUtc).Distinct().Count() == 1, "Start timestamps differ");
        Check(commits[0].Command.StartUtc > DateTime.UtcNow.Ticks, "Start must be scheduled ahead");
        if (kind == "record") Check(commits.Select(c => c.Command.FileName).Distinct().Count() == 3, "Recording outputs collide");
        Check(commits.Count(c => c.Command.IncludeMusic) == 2 &&
            commits.First(c => c.Id == targets[0].Id).Command.IncludeMusic &&
            !commits.First(c => c.Id == targets[1].Id).Command.IncludeMusic &&
            commits.First(c => c.Id == targets[2].Id).Command.IncludeMusic,
            "Each distinct song must play once, including in preview");
    }
    Check(reply.Count == 3, "Missing participant status");
}
foreach (var command in new GroupCommand[] {
    new() { Action = "seek", Seconds = double.NaN }, new() { Action = "seek", Seconds = double.PositiveInfinity },
    new() { Action = "seek", Seconds = -5.001 },
    new() { Action = "prepare-play", IntroDuration = double.NaN }, new() { Action = "prepare-play", IntroDuration = 3 },
    new() { Action = "layout", X = int.MinValue }, new() { Action = "layout", Width = 199 },
    new() { Action = "prepare-record", FileName = "../outside.mp4" },
    new() { Action = "prepare-record", FileName = "valid.mp4", Width = 1081 },
    new() { Action = "prepare-record", FileName = "valid.mp4", Fps = 30 }, new() { Action = "unknown" }
})
{
    bool rejected = false;
    try { GroupControl.Validate(command); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid command accepted");
}
GroupControl.Validate(new GroupCommand { Action = "seek", Seconds = -5 });
GroupControl.Validate(new GroupCommand { Action = "prepare-play", Seconds = -2.5 });
Console.WriteLine($"PASS: {assertions} assertions; preparation barrier, rollback, cancellation, shared start, distinct outputs, input bounds.");
if (args.Length == 2 && args[0] == "--ffmpeg")
{
    var ffmpeg = Path.GetFullPath(args[1]);
    var folder = Path.Combine(Environment.CurrentDirectory, ".codex-build", "composite-regression");
    Directory.CreateDirectory(folder);
    async Task<byte[]> Run(params string[] values)
    {
        var start = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in values) start.ArgumentList.Add(value);
        using var process = System.Diagnostics.Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var memory = new MemoryStream(); var output = process.StandardOutput.BaseStream.CopyToAsync(memory);
        await Task.WhenAll(process.WaitForExitAsync(), output); var error = await errors;
        Check(process.ExitCode == 0, error); return memory.ToArray();
    }
    var red = Path.Combine(folder, "red.mp4"); var blue = Path.Combine(folder, "blue.mp4"); var output = Path.Combine(folder, "out_multi.mp4");
    await Run("-y", "-f", "lavfi", "-i", "color=red:s=320x240:r=60:d=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", red);
    await Run("-y", "-f", "lavfi", "-i", "color=blue:s=240x320:r=60:d=2", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", blue);
    var music = Path.Combine(folder, "music.wav");
    await Run("-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", music);
    await MultiVideoComposer.Compose(ffmpeg, new[] { new VideoTile(red, 0, 0, 320, 240, music), new VideoTile(blue, 320, 0, 240, 320) }, 560, 320, 60, output, CancellationToken.None);
    var pixels = await Run("-v", "error", "-i", output, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1");
    Check(pixels.Length == 560 * 320 * 3, "composite resolution");
    int leftPixel = (120 * 560 + 160) * 3, rightPixel = (120 * 560 + 440) * 3, gapPixel = (120 * 560 + 319) * 3;
    Check(pixels[leftPixel] > 180 && pixels[leftPixel + 2] < 60, "left tile must be red");
    Check(pixels[rightPixel + 2] > 180 && pixels[rightPixel] < 60, "right tile must be blue");
    Check(pixels[gapPixel] > 180, "horizontal tiles must have no artificial gap");
    var pcm = await Run("-v", "error", "-i", output, "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1");
    double Amplitude(int frequency, int start = 4800)
    {
        double real = 0, imaginary = 0; const int length = 24000;
        Check(pcm.Length >= (start + length) * 4, "mixed audio duration");
        for (int i = 0; i < length; i++)
        {
            var sample = BitConverter.ToSingle(pcm, (start + i) * 4);
            var angle = 2 * Math.PI * frequency * i / 48000;
            real += sample * Math.Cos(angle); imaginary += sample * Math.Sin(angle);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / length;
    }
    Check(Amplitude(440) > 0.03 && Amplitude(880) > 0.03, "both participants must be audible in the composite");
    Check(Amplitude(440, 62400) > 0.03 && Amplitude(880, 62400) > 0.03, "music must survive the first video's early end");
    Console.WriteLine("PASS: real ffmpeg composite, independent aspect ratios, output dimensions and adjacent pixel positions.");
}
