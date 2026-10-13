using System.IO;

namespace MajdataEdit;

internal static class MediaChartCreation
{
    internal static async Task CreateAsync(string source, string folder, bool video, string chartText)
    {
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var copyAudio = !video && extension is ".mp3" or ".ogg";
        var track = Path.Combine(folder, "track.mp3");
        var chart = Path.Combine(folder, "maidata.txt");
        var pv = Path.Combine(folder, "pv.mp4");
        var existingPv = video && Path.GetFullPath(source).Equals(Path.GetFullPath(pv), StringComparison.OrdinalIgnoreCase);
        // Never replace an existing chart or its media when creating a new one.
        foreach (var name in new[] { "maidata.txt", "track.mp3", "track.ogg" }.Concat(video && !existingPv ? new[] { "pv.mp4" } : Array.Empty<string>()))
            if (File.Exists(Path.Combine(folder, name))) throw new IOException("所选位置已存在 " + name + "，请选择另一个谱面文件夹。");
        var temporary = Path.Combine(folder, ".new-chart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var committed = new List<string>();
        try
        {
            var audio = Path.Combine(temporary, Path.GetFileName(track));
            if (copyAudio) await CopyAsync(source, audio);
            else await MediaTools.ExtractMp3Async(source, audio);
            var copiedVideo = Path.Combine(temporary, "pv.mp4");
            if (video && !existingPv) await CopyAsync(source, copiedVideo);
            await File.WriteAllTextAsync(Path.Combine(temporary, "maidata.txt"), chartText);
            foreach (var pair in new[] { (audio, track), (copiedVideo, pv), (Path.Combine(temporary, "maidata.txt"), chart) })
            {
                if (!File.Exists(pair.Item1)) continue;
                File.Move(pair.Item1, pair.Item2); committed.Add(pair.Item2);
            }
            // Rename selected media in its own folder only after creation succeeds.
            if ((copyAudio || video && !existingPv) &&
                Path.GetDirectoryName(Path.GetFullPath(source))!.Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                File.Delete(source);
        }
        catch
        {
            foreach (var file in committed) File.Delete(file);
            throw;
        }
        finally { Directory.Delete(temporary, true); }
    }
    private static async Task CopyAsync(string source, string target)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output);
    }
}
