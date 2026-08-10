using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MajdataEdit;

internal static class MediaTools
{
    public static string? FindCachedTimelineAudio(string chartDirectory)
    {
        var project = MediaTimelineProject.LoadWorking(chartDirectory);
        var clips = ResolveAudioClips(chartDirectory, project);
        if (clips.Count == 0)
            return null;
        if (TryGetPassThroughAudio(clips, out var source))
            return source;
        var cache = GetTimelineAudioCachePath(chartDirectory, clips);
        return File.Exists(cache) ? cache : null;
    }

    public static async Task<string?> BuildTimelineAudioAsync(string chartDirectory)
    {
        var project = MediaTimelineProject.LoadWorking(chartDirectory);
        var clips = ResolveAudioClips(chartDirectory, project);
        if (clips.Count == 0)
            return null;
        if (TryGetPassThroughAudio(clips, out var source))
            return source;

        var output = GetTimelineAudioCachePath(chartDirectory, clips);
        if (File.Exists(output))
            return output;

        var ffmpeg = FindFfmpeg();
        var cacheDirectory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(cacheDirectory);
        var temporary = output + ".tmp.wav";
        var inputs = string.Join(" ", clips.Select(clip => $"-i {Q(clip.SourcePath)}"));
        var filters = new List<string>();
        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            var delay = Math.Max(0L, (long)Math.Round(clip.TimelineStart * 1000d));
            filters.Add(
                $"[{index}:a:0]atrim=start={Fmt(clip.SourceOffset)}:duration={Fmt(clip.Duration)}," +
                $"asetpts=PTS-STARTPTS,aresample=44100," +
                $"aformat=sample_fmts=s16:channel_layouts=stereo,adelay={delay}:all=1[a{index}]");
        }
        var labels = string.Concat(Enumerable.Range(0, clips.Count).Select(index => $"[a{index}]"));
        filters.Add(clips.Count == 1
            ? "[a0]anull[outa]"
            : $"{labels}amix=inputs={clips.Count}:duration=longest:dropout_transition=0:normalize=0[outa]");
        var filter = string.Join(";", filters);
        try
        {
            await RunFfmpegAsync(ffmpeg,
                $"-y {inputs} -filter_complex {Q(filter)} -map \"[outa]\" " +
                $"-ar 44100 -ac 2 -c:a pcm_s16le {Q(temporary)}").ConfigureAwait(false);
            File.Move(temporary, output, true);
            return output;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static async Task ExportTimelineAudioAsync(
        string chartDirectory,
        string outputPath,
        TimelineAudioExportOptions? options = null)
    {
        options ??= new TimelineAudioExportOptions();
        var source = await BuildTimelineAudioAsync(chartDirectory).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new InvalidOperationException("时间线中没有可导出的音频。");

        var project = MediaTimelineProject.LoadWorking(chartDirectory);
        var clips = ResolveAudioClips(chartDirectory, project);
        var duration = clips.Select(clip => clip.TimelineStart + clip.Duration)
            .DefaultIfEmpty(0.05d).Max();
        var sourceFullPath = Path.GetFullPath(source);
        var outputFullPath = Path.GetFullPath(outputPath);
        var outputExtension = Path.GetExtension(outputPath).ToLowerInvariant();
        var ffmpeg = FindFfmpeg();
        var writesSource = sourceFullPath.Equals(outputFullPath, StringComparison.OrdinalIgnoreCase);
        var encodedPath = writesSource
            ? Path.Combine(Path.GetDirectoryName(outputFullPath)!,
                $".{Path.GetFileNameWithoutExtension(outputFullPath)}_{Guid.NewGuid():N}{outputExtension}")
            : outputFullPath;
        try
        {
            var sampleRate = options.Force44100Hz ? " -ar 44100" : string.Empty;
            await RunFfmpegAsync(ffmpeg,
                $"-y -i {Q(source)} -map 0:a:0 -t {Fmt(duration)}{sampleRate} " +
                $"{GetAudioCodecArgs(outputExtension)} {Q(encodedPath)}").ConfigureAwait(false);
            if (writesSource)
                File.Move(encodedPath, outputFullPath, true);
        }
        finally
        {
            if (writesSource && File.Exists(encodedPath))
                File.Delete(encodedPath);
        }
    }

    public static async Task ExportTimelineVideoAsync(
        string chartDirectory,
        string outputPath,
        TimelineVideoExportOptions? options = null)
    {
        options ??= new TimelineVideoExportOptions();
        var project = MediaTimelineProject.LoadWorking(chartDirectory);
        var videoClips = project.Clips
            .Where(clip => clip.Track == MediaTrackKind.Video)
            .Select(clip => new ResolvedVideoClip(
                project.ResolveSourcePath(chartDirectory, clip), clip))
            .Where(item => File.Exists(item.Path))
            // Video track 1 is the upper lane and must be composited over track 2.
            .OrderByDescending(item => item.Clip.TrackIndex)
            .ThenBy(item => item.Clip.TimelineStart)
            .ToList();
        if (videoClips.Count == 0)
            throw new InvalidOperationException("时间线中没有可导出的视频或图片。");

        var ffmpeg = FindFfmpeg();
        var sourceSizes = await Task.WhenAll(videoClips
            .Select(item => ProbeCanvasSizeAsync(ffmpeg, item.Path))).ConfigureAwait(false);
        var automaticSize = sourceSizes
            .OrderBy(size => (long)size.Width * size.Height)
            .ThenBy(size => size.Width)
            .Last();
        var width = options.UseHighestSourceResolution ? automaticSize.Width : options.Width;
        var height = options.UseHighestSourceResolution ? automaticSize.Height : options.Height;
        width = Math.Max(2, width / 2 * 2);
        height = Math.Max(2, height / 2 * 2);

        var audioPath = await BuildTimelineAudioAsync(chartDirectory).ConfigureAwait(false);
        var duration = Math.Max(0.05d, videoClips.Max(item => item.Clip.TimelineEnd));

        var inputs = new StringBuilder();
        for (var index = 0; index < videoClips.Count; index++)
        {
            var item = videoClips[index];
            if (item.Clip.IsStillImage || IsImageExtension(Path.GetExtension(item.Path)))
                inputs.Append(" -loop 1 -t ").Append(Fmt(item.Clip.Duration));
            inputs.Append(" -i ").Append(Q(item.Path));
        }
        var audioInputIndex = -1;
        if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
        {
            audioInputIndex = videoClips.Count;
            inputs.Append(" -i ").Append(Q(audioPath));
        }

        var hasAudio = audioInputIndex >= 0;
        int audioBitrate;
        int videoBitrate;
        if (options.SmartTargetSize)
        {
            var targetBytes = Math.Max(1d, options.TargetSizeMiB) * 1024d * 1024d * 0.96d;
            var totalBitrate = targetBytes * 8d / duration;
            audioBitrate = hasAudio
                ? (int)Math.Clamp(totalBitrate * 0.15d, 48_000d, 128_000d)
                : 0;
            videoBitrate = (int)Math.Clamp(
                totalBitrate - audioBitrate - 16_000d, 64_000d, 200_000_000d);

            const double minimumBitsPerPixel = 0.065d;
            var pixelBudget = videoBitrate /
                              (Math.Max(1, options.FrameRate) * minimumBitsPerPixel);
            if ((double)width * height > pixelBudget)
            {
                var candidates = new[] { (1920, 1080), (1280, 720), (640, 360) };
                var selected = candidates.Last();
                foreach (var candidate in candidates)
                    if (candidate.Item1 <= width && candidate.Item2 <= height &&
                        (double)candidate.Item1 * candidate.Item2 <= pixelBudget)
                    {
                        selected = candidate;
                        break;
                    }
                width = selected.Item1;
                height = selected.Item2;
            }
        }
        else
        {
            audioBitrate = hasAudio ? 192_000 : 0;
            videoBitrate = Math.Clamp(options.VideoBitrateKbps, 64, 200_000) * 1000;
        }

        var filters = new List<string>
        {
            $"color=c=black:s={width}x{height}:r={options.FrameRate}:d={Fmt(duration)}[base]"
        };
        for (var index = 0; index < videoClips.Count; index++)
        {
            var item = videoClips[index];
            var sourceOffset = item.Clip.IsStillImage ? 0d : item.Clip.SourceOffset;
            filters.Add(
                $"[{index}:v:0]trim=start={Fmt(sourceOffset)}:duration={Fmt(item.Clip.Duration)}," +
                $"setpts=PTS-STARTPTS+{Fmt(item.Clip.TimelineStart)}/TB," +
                $"format=rgba,scale={width}:{height}:force_original_aspect_ratio=decrease," +
                $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black@0[v{index}]");
        }

        var previous = "base";
        for (var index = 0; index < videoClips.Count; index++)
        {
            var clip = videoClips[index].Clip;
            var output = $"mix{index}";
            filters.Add(
                $"[{previous}][v{index}]overlay=0:0:eof_action=pass:shortest=0:" +
                $"enable='between(t,{Fmt(clip.TimelineStart)},{Fmt(clip.TimelineEnd)})'[{output}]");
            previous = output;
        }
        filters.Add($"[{previous}]fps={options.FrameRate},format=yuv420p[vout]");
        var audioMap = hasAudio ? $" -map {audioInputIndex}:a:0" : string.Empty;
        var audioCodec = hasAudio ? $" -c:a aac -b:a {audioBitrate}" : string.Empty;
        var filter = Q(string.Join(";", filters));
        var videoOptions = $"-t {Fmt(duration)} -c:v libx264 -preset medium " +
                           $"-b:v {videoBitrate} -maxrate {videoBitrate} -bufsize {videoBitrate * 2L} " +
                           "-pix_fmt yuv420p";
        var passDirectory = PrepareTempDirectory(Path.GetDirectoryName(outputPath)!);
        var passLog = Path.Combine(passDirectory, "timeline_video_pass");
        try
        {
            await RunFfmpegAsync(ffmpeg,
                $"-y{inputs} -filter_complex {filter} -map \"[vout]\" {videoOptions} " +
                $"-pass 1 -passlogfile {Q(passLog)} -an -f null NUL").ConfigureAwait(false);
            await RunFfmpegAsync(ffmpeg,
                $"-y{inputs} -filter_complex {filter} -map \"[vout]\"{audioMap} {videoOptions} " +
                $"-pass 2 -passlogfile {Q(passLog)}{audioCodec} -movflags +faststart {Q(outputPath)}")
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(passDirectory);
        }
    }

    public static async Task ConvertAudioTo44100Async(string filePath)
    {
        var ffmpeg = FindFfmpeg();
        var dir = Path.GetDirectoryName(filePath)!;
        var tmpDir = PrepareTempDirectory(dir);
        var ext = Path.GetExtension(filePath);
        var temp = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(filePath) + "_44100" + ext);

        var codec = ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "-c:a pcm_s16le"
            : "";
        try
        {
            await RunFfmpegAsync(ffmpeg, $"-y -i {Q(filePath)} -map 0:a:0 -ar 44100 {codec} {Q(temp)}");
            await ReplaceWithBackupAsync(filePath, temp);
        }
        finally
        {
            TryDeleteDirectory(tmpDir);
        }
    }

    public static async Task RemoveRangeAsync(string filePath, double start, double end)
    {
        if (end <= start || start < 0)
            throw new InvalidOperationException(MainWindow.GetLocalizedString("InvalidMediaRange"));

        var ffmpeg = FindFfmpeg();
        var dir = Path.GetDirectoryName(filePath)!;
        var tmpDir = PrepareTempDirectory(dir);
        var ext = Path.GetExtension(filePath);
        var part1 = Path.Combine(tmpDir, "part1" + ext);
        var part2 = Path.Combine(tmpDir, "part2" + ext);
        var list = Path.Combine(tmpDir, "concat.txt");
        var output = Path.Combine(tmpDir, "output" + ext);

        try
        {
            if (start <= 0.001d)
            {
                await RunFfmpegAsync(ffmpeg,
                    $"-y -ss {Fmt(end)} -i {Q(filePath)} -map 0 -c copy {Q(output)}");
            }
            else
            {
                await RunFfmpegAsync(ffmpeg, $"-y -i {Q(filePath)} -t {Fmt(start)} -map 0 -c copy {Q(part1)}");
                await RunFfmpegAsync(ffmpeg, $"-y -ss {Fmt(end)} -i {Q(filePath)} -map 0 -c copy {Q(part2)}");
                await File.WriteAllTextAsync(list,
                    "file '" + part1.Replace("\\", "/").Replace("'", "'\\''") + "'\n" +
                    "file '" + part2.Replace("\\", "/").Replace("'", "'\\''") + "'\n",
                    new UTF8Encoding(false));
                await RunFfmpegAsync(ffmpeg, $"-y -f concat -safe 0 -i {Q(list)} -map 0 -c copy {Q(output)}");
            }
            await ReplaceWithBackupAsync(filePath, output);
        }
        finally
        {
            TryDeleteDirectory(tmpDir);
        }
    }

    public static async Task PrependBlankAsync(string filePath, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0d)
            throw new InvalidOperationException(MainWindow.GetLocalizedString("NoValidBpm"));

        var ffmpeg = FindFfmpeg();
        var dir = Path.GetDirectoryName(filePath)!;
        var tmpDir = PrepareTempDirectory(dir);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var output = Path.Combine(tmpDir, "output" + ext);

        try
        {
            if (IsVideoExtension(ext))
            {
                var hasAudio = await HasAudioStreamAsync(ffmpeg, filePath);
                var delayMs = Math.Max(1L, (long)Math.Round(duration * 1000d));
                var audioFilter = hasAudio ? $" -af \"adelay={delayMs}:all=1\"" : string.Empty;
                var args = $"-y -i {Q(filePath)} " +
                           $"-vf \"tpad=start_duration={Fmt(duration)}:start_mode=add:color=black\"" +
                           audioFilter +
                           " -map 0:v:0 -map 0:a? -map_metadata 0 " +
                           GetVideoCodecArgs(ext) + " " + Q(output);
                await RunFfmpegAsync(ffmpeg, args);
            }
            else if (IsAudioExtension(ext))
            {
                var delayMs = Math.Max(1L, (long)Math.Round(duration * 1000d));
                await RunFfmpegAsync(ffmpeg,
                    $"-y -i {Q(filePath)} -af \"adelay={delayMs}:all=1\" -map 0:a:0 -map_metadata 0 " +
                    GetAudioCodecArgs(ext) + " " + Q(output));
            }
            else
            {
                throw new InvalidOperationException(MainWindow.GetLocalizedString("UnsupportedMediaType"));
            }

            await ReplaceWithBackupAsync(filePath, output);
        }
        finally
        {
            TryDeleteDirectory(tmpDir);
        }
    }

    private static string FindFfmpeg()
    {
        var candidates = new List<string>();
        candidates.Add(AppContext.BaseDirectory);
        candidates.Add(Environment.CurrentDirectory);
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var dir in candidates)
        {
            var path = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException(MainWindow.GetLocalizedString("FfmpegNotFound"));
    }

    private static async Task RunFfmpegAsync(string ffmpeg, string args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Length > 0 || stdout.Length > 0
                ? stderr + stdout
                : MainWindow.GetLocalizedString("FfmpegFailed"));
    }

    private static async Task<bool> HasAudioStreamAsync(string ffmpeg, string filePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, $"-hide_banner -i {Q(filePath)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdoutTask + await stderrTask;
        return output.Contains(" Audio:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoExtension(string extension) =>
        extension is ".mp4" or ".mov" or ".mkv" or ".webm";

    private static bool IsImageExtension(string extension) =>
        extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";

    private static bool IsAudioExtension(string extension) =>
        extension is ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" or ".aac";

    private static string GetAudioCodecArgs(string extension) => extension switch
    {
        ".wav" => "-c:a pcm_s16le",
        ".mp3" => "-c:a libmp3lame -q:a 2",
        ".ogg" => "-c:a libvorbis -q:a 6",
        ".flac" => "-c:a flac",
        ".m4a" or ".aac" => "-c:a aac -b:a 256k",
        _ => throw new InvalidOperationException(MainWindow.GetLocalizedString("UnsupportedMediaType"))
    };

    private static string GetVideoCodecArgs(string extension) => extension switch
    {
        ".webm" => "-c:v libvpx-vp9 -crf 24 -b:v 0 -c:a libopus -b:a 192k",
        ".mp4" or ".mov" => "-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart",
        ".mkv" => "-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k",
        _ => throw new InvalidOperationException(MainWindow.GetLocalizedString("UnsupportedMediaType"))
    };

    private static async Task<(int Width, int Height)> ProbeCanvasSizeAsync(
        string ffmpeg,
        string mediaPath)
    {
        if (IsImageExtension(Path.GetExtension(mediaPath)))
        {
            using var image = System.Drawing.Image.FromFile(mediaPath);
            return (image.Width, image.Height);
        }

        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(ffprobe))
            return await ProbeVideoSizeWithFfmpegAsync(ffmpeg, mediaPath).ConfigureAwait(false);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(
                ffprobe,
                $"-v error -select_streams v:0 -show_entries stream=width,height " +
                $"-of csv=s=x:p=0 {Q(mediaPath)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var output = (await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var values = output.Split('x');
        return values.Length == 2 && int.TryParse(values[0], out var width) &&
               int.TryParse(values[1], out var height) && width > 0 && height > 0
            ? (width, height)
            : (1920, 1080);
    }

    private static async Task<(int Width, int Height)> ProbeVideoSizeWithFfmpegAsync(
        string ffmpeg,
        string mediaPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, $"-hide_banner -i {Q(mediaPath)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false) + await errorTask.ConfigureAwait(false);
        var match = System.Text.RegularExpressions.Regex.Match(
            output,
            @"Video:.*?\s(\d{2,5})x(\d{2,5})(?:\s|,)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var width) &&
               int.TryParse(match.Groups[2].Value, out var height)
            ? (width, height)
            : (1920, 1080);
    }

    private static async Task ReplaceWithBackupAsync(string filePath, string replacementPath)
    {
        Exception? lastError = null;
        var backupCreated = false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if (!backupCreated)
                {
                    BackupOriginal(filePath);
                    backupCreated = true;
                }

                var attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);

                File.Move(replacementPath, filePath, true);
                return;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                lastError = error;
                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException(
            string.Format(CultureInfo.CurrentCulture,
                MainWindow.GetLocalizedString("MediaAccessDenied"), filePath),
            lastError);
    }

    private static void BackupOriginal(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var backupDir = Path.Combine(dir, "backup");
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backup = Path.Combine(backupDir,
            Path.GetFileNameWithoutExtension(filePath) + "_" + stamp + Path.GetExtension(filePath));
        File.Copy(filePath, backup, false);
    }

    private static string PrepareTempDirectory(string dir)
    {
        var tmp = Path.Combine(dir, ".majdata_tmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
        }
    }

    private static string Q(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static List<TimelineAudioClip> ResolveAudioClips(
        string chartDirectory,
        MediaTimelineProject project)
    {
        return project.Clips
            .Where(clip => clip.Track == MediaTrackKind.Audio)
            .Select(clip => new TimelineAudioClip(
                project.ResolveSourcePath(chartDirectory, clip),
                clip.TrackIndex,
                clip.TimelineStart,
                clip.SourceOffset,
                clip.Duration,
                clip.SourceDuration))
            .Where(clip => File.Exists(clip.SourcePath))
            .OrderBy(clip => clip.TrackIndex)
            .ThenBy(clip => clip.TimelineStart)
            .ToList();
    }

    private static bool TryGetPassThroughAudio(
        IReadOnlyList<TimelineAudioClip> clips,
        out string source)
    {
        source = string.Empty;
        if (clips.Count != 1)
            return false;
        var clip = clips[0];
        if (clip.TrackIndex != 0 || clip.TimelineStart > 0.0001d ||
            clip.SourceOffset > 0.0001d || clip.Duration + 0.02d < clip.SourceDuration)
            return false;
        var fileName = Path.GetFileName(clip.SourcePath);
        if (!fileName.Equals("track.mp3", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("track.ogg", StringComparison.OrdinalIgnoreCase))
            return false;
        source = clip.SourcePath;
        return true;
    }

    private static string GetTimelineAudioCachePath(
        string chartDirectory,
        IReadOnlyList<TimelineAudioClip> clips)
    {
        var signature = new StringBuilder();
        foreach (var clip in clips)
        {
            var info = new FileInfo(clip.SourcePath);
            signature.Append(clip.SourcePath).Append('|')
                .Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|')
                .Append(clip.TrackIndex).Append('|')
                .Append(clip.TimelineStart.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(clip.SourceOffset.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(clip.Duration.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString())))
            .Substring(0, 16).ToLowerInvariant();
        return Path.Combine(chartDirectory, "media", ".cache", $"timeline_audio_{hash}.wav");
    }

    private sealed record TimelineAudioClip(
        string SourcePath,
        int TrackIndex,
        double TimelineStart,
        double SourceOffset,
        double Duration,
        double SourceDuration);

    private sealed record ResolvedVideoClip(string Path, MediaTimelineClip Clip);
}

internal readonly record struct MediaRange(double Start, double End);
