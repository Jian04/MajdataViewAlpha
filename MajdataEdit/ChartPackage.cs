using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MajdataCore;
using Microsoft.Win32;
using System.Windows;

namespace MajdataEdit;

public partial class MainWindow
{
    private async void ExportChartPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLoadedChartDocument()) return;
        if (!isSaved && !AskSave()) return;
        var folder = maidataDir;
        var picker = new SaveFileDialog { Filter = "谱面压缩包|*.zip", FileName = new DirectoryInfo(folder).Name + ".zip" };
        if (picker.ShowDialog(this) != true) return;
        var output = picker.FileName;
        try { await Task.Run(() => ExportChartPackage(folder, output)); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "导出压缩包", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    internal static void ExportChartPackage(string folder, string output)
    {
        folder = Path.GetFullPath(folder);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(folder, "maidata.txt") };
        var packagedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var portablePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string AddPortableFile(string path, string directory)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到引用素材：" + path, path);
            if (path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(path);
                return Path.GetRelativePath(folder, path).Replace('\\', '/');
            }
            if (portablePaths.TryGetValue(path, out var saved)) return saved;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var relative = directory + "/" + name + extension;
            for (var suffix = 2; packagedFiles.ContainsKey(relative) || File.Exists(Path.Combine(folder, relative)); suffix++)
                relative = directory + "/" + name + "-" + suffix + extension;
            packagedFiles.Add(relative, path);
            portablePaths.Add(path, relative);
            return relative;
        }
        string AddAsset(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(folder, relative));
            if (!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("引用素材不在谱面文件夹内：" + relative);
            if (!File.Exists(path)) throw new FileNotFoundException("找不到引用素材：" + relative, path);
            files.Add(path);
            return path;
        }
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (((stem is "cover" or "bg") && (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".apng" or ".gif" or ".mp4")) ||
                ((stem is "pv" or "mv") && extension == ".mp4") ||
                (stem == "track" && (extension is ".mp3" or ".ogg"))) files.Add(path);
        }
        var timeline = Path.Combine(folder, MediaTimelineProject.FileName);
        string? packagedTimeline = null;
        if (File.Exists(timeline))
        {
            var project = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(timeline));
            if (project.GetValue("Clips", StringComparison.OrdinalIgnoreCase) is not Newtonsoft.Json.Linq.JArray clips)
                throw new InvalidDataException("剪辑信息无效：" + timeline);
            files.Add(timeline);
            foreach (var value in clips)
            {
                if (value is not Newtonsoft.Json.Linq.JObject clip) throw new InvalidDataException("剪辑片段无效：" + timeline);
                var source = clip.Properties().FirstOrDefault(property => property.Name.Equals("SourcePath", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("剪辑片段缺少 SourcePath：" + timeline);
                source.Value = AddPortableFile(Path.GetFullPath(Path.Combine(folder, source.Value.ToString())), "media");
            }
            packagedTimeline = project.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        var text = File.ReadAllText(Path.Combine(folder, "maidata.txt"));
        var fontEdits = new List<(int Start, int Length, string Replacement)>();
        foreach (Match section in Regex.Matches(text, @"(?ms)^&inote_\d+\s*=(.*?)(?=^&\w+\s*=|\z)"))
        {
            var notes = section.Groups[1].Value;
            for (var index = 0; index < notes.Length; index++)
            {
                if (notes.AsSpan(index).StartsWith("||"))
                { var end = notes.IndexOf('\n', index); index = end < 0 ? notes.Length : end; continue; }
                if (notes.AsSpan(index).StartsWith("|*"))
                { var end = notes.IndexOf("*|", index + 2, StringComparison.Ordinal); index = end < 0 ? notes.Length : end + 1; continue; }
                if (notes[index] == '<' && AlphaCommandBoundary.TryGetToken(notes, index, out var command))
                {
                    var token = notes.Substring(index + 1, command.length - 2);
                    if (SimaiProcess.TryParseMediaChange(token, 0, 120, out var media) &&
                        media.enabled && !string.IsNullOrEmpty(media.path)) AddAsset(media.path);
                    if (SimaiProcess.TryParseSubtitleChange(token, 0, 120, out var caption) && FontFiles.IsFontFile(caption.font))
                    {
                        var fontPath = AddPortableFile(FontFiles.Resolve(caption.font, Editor.FontLibrary.DirectoryPath, folder), "Fonts");
                        var body = token.Substring(5).Trim();
                        AlphaCommandGrammar.TrySplitValues(body.Substring(1, body.Length - 2), out var values);
                        var namedFont = values.FindIndex(1, value => value.Split('=')[0].Trim().Equals("font", StringComparison.OrdinalIgnoreCase));
                        if (namedFont >= 0) values[namedFont] = "font=\"" + fontPath + "\"";
                        else values[5] = "\"" + fontPath + "\"";
                        fontEdits.Add((section.Groups[1].Index + index, command.length, "<TEXT*(" + string.Join(",", values) + ")>"));
                    }
                    index += command.length - 1; continue;
                }
                if (!notes.AsSpan(index).StartsWith("~[")) continue;
                var close = notes.IndexOf(']', index + 2);
                if (close < 0 || !SlidePathParser.IsSkinPathUsable(notes.Substring(index + 2, close - index - 2), out var relative)) continue;
                AddAsset(relative); index = close;
            }
        }
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var packagedChart = new System.Text.StringBuilder(text);
        foreach (var edit in fontEdits.OrderByDescending(edit => edit.Start))
            packagedChart.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var path in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    if (path == timeline && packagedTimeline != null)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(MediaTimelineProject.FileName).Open());
                        writer.Write(packagedTimeline);
                    }
                    else if (path.Equals(Path.Combine(folder, "maidata.txt"), StringComparison.OrdinalIgnoreCase) && fontEdits.Count > 0)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry("maidata.txt").Open());
                        writer.Write(packagedChart.ToString());
                    }
                    else zip.CreateEntryFromFile(path, Path.GetRelativePath(folder, path).Replace('\\', '/'), CompressionLevel.Optimal);
                foreach (var asset in packagedFiles.OrderBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase))
                    zip.CreateEntryFromFile(asset.Value, asset.Key, CompressionLevel.Optimal);
            }
            File.Move(temporary, output, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
