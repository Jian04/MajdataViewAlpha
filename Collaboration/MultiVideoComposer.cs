using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MajdataCollaboration;

public sealed record VideoTile(string File, int X, int Y, int Width, int Height, string AudioFile = "");

public static class MultiVideoComposer
{
    public static async Task Compose(string ffmpeg, IReadOnlyList<VideoTile> tiles, int width, int height,
        int fps, string destination, CancellationToken token)
    {
        if (tiles.Count == 0 || !File.Exists(ffmpeg)) throw new IOException("找不到录制画面或 ffmpeg。");
        double duration = 0;
        var audioInputs = new List<int>();
        foreach (var tile in tiles)
        {
            if (!File.Exists(tile.File)) throw new IOException("录制未生成视频：" + tile.File);
            var probe = Start(ffmpeg, new[] { "-hide_banner", "-i", tile.File });
            using (probe)
            {
                var log = probe.StandardError.ReadToEndAsync();
                var output = probe.StandardOutput.ReadToEndAsync();
                await Wait(probe, token);
                var probeText = await log;
                if (Regex.IsMatch(probeText, @"Stream #\d+:\d+.*Audio:")) audioInputs.Add(tiles.ToList().IndexOf(tile));
                var match = Regex.Match(probeText, @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
                await output;
                if (!match.Success) throw new IOException("无法读取视频时长：" + tile.File);
                duration = Math.Max(duration, int.Parse(match.Groups[1].Value) * 3600 +
                    int.Parse(match.Groups[2].Value) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            }
        }
        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var tile in tiles) { arguments.Add("-i"); arguments.Add(tile.File); }
        var audioMap = new Dictionary<int, int>();
        for (int i = 0, next = tiles.Count; i < tiles.Count; i++)
            if (!string.IsNullOrEmpty(tiles[i].AudioFile))
            {
                if (!File.Exists(tiles[i].AudioFile)) throw new IOException("录制音轨缺失。");
                arguments.Add("-i"); arguments.Add(tiles[i].AudioFile); audioMap[i] = next++;
                if (!audioInputs.Contains(i)) audioInputs.Add(i);
            }
        var filter = new StringBuilder($"color=c=black:s={width}x{height}:r={fps}:d={duration.ToString(CultureInfo.InvariantCulture)}[base];");
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            filter.Append($"[{i}:v]setpts=PTS-STARTPTS,scale={t.Width}:{t.Height}:force_original_aspect_ratio=decrease,pad={t.Width}:{t.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1[v{i}];");
            filter.Append($"[{(i == 0 ? "base" : "o" + (i - 1))}][v{i}]overlay={t.X}:{t.Y}:eof_action=pass:repeatlast=0[o{i}];");
        }
        if (audioInputs.Count > 0)
        {
            foreach (var index in audioInputs) filter.Append($"[{audioMap.GetValueOrDefault(index, index)}:a]aresample=48000,asetpts=PTS-STARTPTS[a{index}];");
            foreach (var index in audioInputs) filter.Append($"[a{index}]");
            // Only the first participant includes music; other tracks contain hits.
            filter.Append($"amix=inputs={audioInputs.Count}:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.95:level=0:latency=1[mix];");
        }
        filter.Length--;
        arguments.AddRange(new[] { "-filter_complex", filter.ToString(), "-map", "[o" + (tiles.Count - 1) + "]",
            "-c:v", "libx264", "-crf", "18", "-preset", "fast", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "320k", "-t", duration.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", destination });
        if (audioInputs.Count > 0) arguments.InsertRange(arguments.Count - 1, new[] { "-map", "[mix]" });
        using var process = Start(ffmpeg, arguments);
        var errors = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        await Wait(process, token);
        var diagnostic = await errors; await stdout;
        if (process.ExitCode != 0) throw new IOException("合成失败：" + diagnostic.Substring(Math.Max(0, diagnostic.Length - 1500)));
    }
    private static Process Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("无法启动 ffmpeg。");
    }
    private static async Task Wait(Process process, CancellationToken token)
    {
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
    }
}
