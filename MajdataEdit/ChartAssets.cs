using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MajdataCore;
using Microsoft.Win32;
using Newtonsoft.Json;
using Un4seen.Bass;

namespace MajdataEdit;

internal enum ChartAssetRole { Cover, Background, Audio }

public partial class MainWindow
{
    internal async Task SelectChartAssetAsync(ChartAssetRole role)
    {
        var dialog = new OpenFileDialog
        {
            InitialDirectory = maidataDir,
            Filter = role == ChartAssetRole.Audio ? "音频|*.mp3;*.ogg" :
                role == ChartAssetRole.Cover ? "封面|*.mp4;*.gif;*.apng;*.png;*.jpg;*.jpeg;*.bmp" :
                "背景|*.mp4;*.apng;*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog(this) == true) await AssignChartAssetAsync(dialog.FileName, role);
    }

    private bool assigningChartAsset;
    internal async Task AssignChartAssetAsync(string source, ChartAssetRole role, bool confirmReplace = true)
    {
        if (assigningChartAsset || !HasLoadedChartDocument()) return;
        var directory = Path.GetFullPath(maidataDir);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var name = role == ChartAssetRole.Cover ? "cover" : role == ChartAssetRole.Audio ? "track" :
            extension == ".mp4" ? "pv" : "bg";
        var target = Path.Combine(directory, name + extension);
        var names = role switch
        {
            ChartAssetRole.Cover => new[] { "cover.mp4", "cover.gif", "cover.apng", "cover.png", "cover.jpg", "cover.jpeg", "cover.bmp" },
            ChartAssetRole.Audio => new[] { "track.mp3", "track.ogg" },
            _ => new[] { "pv.mp4", "mv.mp4", "bg.mp4", "bg.apng", "bg.png", "bg.jpg", "bg.jpeg" }
        };
        var conflicts = Directory.GetFiles(directory).Where(file => names.Contains(Path.GetFileName(file),
            StringComparer.OrdinalIgnoreCase) && !file.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (confirmReplace && conflicts.Length > 0 && MessageBox.Show(this,
            "将替换以下文件：\n" + string.Join("\n", conflicts.Select(Path.GetFileName)), "替换素材",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase) && conflicts.Length == 0)
        {
            if (role == ChartAssetRole.Cover) EnsureCoverBackground(target, directory);
            return;
        }

        assigningChartAsset = true;
        var position = GetTimelinePosition();
        var reloadAudio = role == ChartAssetRole.Audio ||
            string.Equals(source, GetCurrentTrackPath(), StringComparison.OrdinalIgnoreCase);
        try
        {
            await ValidateChartAssetAsync(source, role);
            if (!directory.Equals(Path.GetFullPath(maidataDir), StringComparison.OrdinalIgnoreCase)) return;
            CancelNotePreview();
            StopPlaybackForScrub();
            var generation = viewControlGeneration;
            if (pendingScrubStop != null && !await pendingScrubStop) return;
            if (generation != viewControlGeneration || !directory.Equals(Path.GetFullPath(maidataDir), StringComparison.OrdinalIgnoreCase)) return;
            // RefreshAssets is processed after the replacement scene is ready,
            // so the previous VideoPlayer and note textures no longer own files.
            var response = await Task.Run(() => WebControl.RequestPOST(AlphaSession.ViewEndpoint,
                Newtonsoft.Json.JsonConvert.SerializeObject(new EditRequestjson { control = EditorControlMethod.RefreshAssets })));
            if (response == "ERROR") { ReportViewUnreachable(); return; }
            if (generation != viewControlGeneration || !directory.Equals(Path.GetFullPath(maidataDir), StringComparison.OrdinalIgnoreCase)) return;
            if (reloadAudio)
            {
                Interlocked.Increment(ref timelineAudioBuildGeneration);
                timelineAudioBuildTask = null;
                Bass.BASS_StreamFree(bgmStream); bgmStream = -1024;
            }
            var localSource = Path.GetFullPath(source).StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            // A background reused as a cover still supplies the chart's background.
            var moveSource = localSource && !(role == ChartAssetRole.Cover &&
                new[] { "bg", "pv", "mv" }.Contains(Path.GetFileNameWithoutExtension(source), StringComparer.OrdinalIgnoreCase));
            var renames = conflicts.Concat(moveSource ? new[] { source } : Array.Empty<string>())
                .Where(path => !path.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
            await ChartAssetFiles.ReplaceAsync(source, target, conflicts, moveSource, renames);
            if (role == ChartAssetRole.Cover) EnsureCoverBackground(target, directory);
            if (!directory.Equals(Path.GetFullPath(maidataDir), StringComparison.OrdinalIgnoreCase)) return;
            foreach (var previous in renames)
            {
                for (var index = 0; index < SimaiProcess.fumens.Length; index++)
                    SimaiProcess.fumens[index] = ChartAssetFiles.RewriteReferences(SimaiProcess.fumens[index] ?? "", directory, previous, target);
                var text = GetRawFumenText();
                var updated = ChartAssetFiles.RewriteReferences(text, directory, previous, target);
                if (updated != text)
                {
                    var caret = GetRawFumenPosition();
                    fumenEditor.ReplaceAll(updated);
                    fumenEditor.CaretOffset = (int)Math.Min(caret, updated.Length);
                }
                MediaTimelinePanel.RenameAsset(directory, previous, target);
            }
            MediaTimelinePanel.InvalidateSourceMetadata();
            if (reloadAudio)
            {
                timelineAudioSourcePath = MediaTools.FindCachedTimelineAudio(directory);
                loadedTrackPath = null;
                ReloadCurrentTrack(position, editorSetting?.Default_BGM_Level ?? 1f, 0f);
                QueueTimelineAudioRefresh();
            }
            SetTimelinePosition(position);
            InvalidateSongDetailCache();
            RefreshChartFiles();
            if (toolTabs.TryGetValue(typeof(Infomation), out var info)) ((Infomation)info.Window).ReloadChartAssets();
            QueueNotePreview(chartChanged: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            NotSupportedException or FormatException or JsonException)
        {
            MessageBox.Show(this, error.Message, "素材替换失败", MessageBoxButton.OK, MessageBoxImage.Error);
            if (reloadAudio && bgmStream <= 0) ReloadCurrentTrack(position, editorSetting?.Default_BGM_Level ?? 1f, 0f);
        }
        finally { assigningChartAsset = false; }
    }

    private async Task ValidateChartAssetAsync(string path, ChartAssetRole role)
    {
        if (role != ChartAssetRole.Audio)
        {
            if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                await MediaTools.ProbeVideoDurationAsync(path);
            else await Task.Run(() => ReadChartThumbnail(path, 64));
            return;
        }
        await Task.Run(() =>
        {
            if (!Bass.BASS_SetDevice(editorAudioDevice)) throw new IOException("无法访问音频解码设备。");
            var decoder = Bass.BASS_StreamCreateFile(path, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
            if (decoder == 0) throw new IOException("音频解码失败：" + path + "（" + Bass.BASS_ErrorGetCode() + "）");
            try
            {
                var samples = new short[2048];
                if (Bass.BASS_ChannelGetData(decoder, samples, samples.Length * sizeof(short)) <= 0)
                    throw new IOException("音频没有可读取的采样：" + path);
            }
            finally { Bass.BASS_StreamFree(decoder); }
        });
    }

    private static void EnsureCoverBackground(string cover, string directory)
    {
        if (ChartCover.FindBackground(directory, includeVideo: false) != null) return;
        var extension = Path.GetExtension(cover).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg")
        {
            File.Copy(cover, Path.Combine(directory, "bg" + extension));
            return;
        }
        var image = ReadChartThumbnail(cover, 0);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, "bg.png"));
        encoder.Save(output);
    }
}

internal static class ChartAssetFiles
{
    internal static async Task ReplaceAsync(string source, string target, IEnumerable<string> conflicts, bool moveSource,
        string[] renamedSources)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".asset-" + Guid.NewGuid().ToString("N"));
        var backups = new List<(string Original, string Backup)>();
        var staged = new List<(string Temporary, string Target)> { (temporary, target) };
        var installed = new List<string>();
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, true))
                await input.CopyToAsync(output);
            // Read metadata after the potentially long media copy. From here to
            // commit there is no await, so UI autosaves cannot overwrite or be
            // overwritten by an older metadata snapshot.
            var textUpdates = PrepareReferenceUpdates(Path.GetDirectoryName(target)!, renamedSources, target);
            foreach (var update in textUpdates)
            {
                var path = temporary + "-text-" + staged.Count;
                staged.Add((path, update.Key));
                File.WriteAllText(path, update.Value);
            }
            foreach (var original in conflicts.Concat(moveSource ? new[] { source } : Array.Empty<string>())
                .Concat(textUpdates.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var backup = temporary + "-" + backups.Count;
                File.Move(original, backup);
                backups.Add((original, backup));
            }
            foreach (var item in staged) { File.Move(item.Temporary, item.Target); installed.Add(item.Target); }
        }
        catch
        {
            foreach (var path in installed) File.Delete(path);
            foreach (var backup in backups.AsEnumerable().Reverse()) File.Move(backup.Backup, backup.Original);
            throw;
        }
        finally { foreach (var item in staged) if (File.Exists(item.Temporary)) File.Delete(item.Temporary); }
        foreach (var backup in backups) File.Delete(backup.Backup);
    }

    internal static Dictionary<string, string> PrepareReferenceUpdates(string directory, string[] sources, string target)
    {
        var updates = new Dictionary<string, string>();
        if (sources.Length == 0) return updates;
        var dataPath = Path.Combine(directory, "maidata.txt");
        var data = File.ReadAllText(dataPath);
        var rewritten = Regex.Replace(data, @"(?m)^&inote_[1-7]=[\s\S]*?(?=^&|\z)", match =>
        {
            var separator = match.Value.IndexOf('=') + 1;
            var text = match.Value.Substring(separator);
            foreach (var source in sources) text = RewriteReferences(text, directory, source, target);
            return match.Value.Substring(0, separator) + text;
        });
        if (rewritten != data) updates.Add(dataPath, rewritten);
        foreach (var name in new[] { MediaTimelineProject.FileName, MediaTimelineProject.TemporaryFileName })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            var project = JsonConvert.DeserializeObject<MediaTimelineProject>(File.ReadAllText(path))
                ?? throw new InvalidDataException("剪辑文件内容为空：" + path);
            var changed = false;
            foreach (var source in sources) changed |= RenameClips(project.Clips, directory, source, target);
            if (changed) updates.Add(path, JsonConvert.SerializeObject(project, Formatting.Indented));
        }
        return updates;
    }

    internal static bool RenameClips(IEnumerable<MediaTimelineClip> clips, string directory, string source, string target)
    {
        var changed = false;
        foreach (var clip in clips)
        {
            if (!Path.GetFullPath(Path.Combine(directory, clip.SourcePath)).Equals(source, StringComparison.OrdinalIgnoreCase)) continue;
            clip.SourcePath = MediaTimelineProject.StoreSourcePath(directory, target);
            clip.Name = Path.GetFileName(target); changed = true;
        }
        return changed;
    }

    internal static string RewriteReferences(string text, string directory, string source, string target)
    {
        string RewritePath(string value)
        {
            var trimmed = value.Trim().Trim('"');
            if (!Path.GetExtension(trimmed).Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase)) return value;
            if (!Path.GetFullPath(Path.Combine(directory, trimmed.Replace('/', Path.DirectorySeparatorChar)))
                    .Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) return value;
            return value.Replace(trimmed, Path.GetRelativePath(directory, target).Replace('\\', '/'));
        }
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '|' && index + 1 < text.Length && text[index + 1] == '|')
            {
                var end = text.IndexOf('\n', index);
                if (end < 0) { result.Append(text, index, text.Length - index); break; }
                result.Append(text, index, end - index + 1); index = end; continue;
            }
            if (text[index] == '<' && AlphaCommandBoundary.TryGetToken(text, index, out var token))
            {
                var command = text.Substring(index, token.length);
                if (token.name.Equals("AUDIO", StringComparison.OrdinalIgnoreCase) || token.name.Equals("PVOVERLAY", StringComparison.OrdinalIgnoreCase))
                    command = Regex.Replace(command, @"(?<prefix>\(\s*true\s*,\s*)(?<path>""[^""]+""|[^,)]+)",
                        match => match.Groups["prefix"].Value + RewritePath(match.Groups["path"].Value), RegexOptions.IgnoreCase);
                result.Append(command); index += token.length - 1; continue;
            }
            if (text[index] == '~' && index + 1 < text.Length && text[index + 1] == '[')
            {
                var end = text.IndexOf(']', index + 2);
                if (end >= 0 && !text.AsSpan(index + 2, end - index - 2).Contains('['))
                {
                    result.Append("~[").Append(RewritePath(text.Substring(index + 2, end - index - 2))).Append(']');
                    index = end; continue;
                }
            }
            result.Append(text[index]);
        }
        return result.ToString();
    }
}
