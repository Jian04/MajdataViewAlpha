using System.IO;
using System.Windows.Media;
using MajdataCore;

namespace MajdataEdit.Editor;

internal static class FontLibrary
{
    internal sealed record Choice(string FileName, string DisplayName, FontFamily Family);
    private static readonly Dictionary<string, (long Stamp, long Length, Choice Choice)> cache = new(StringComparer.OrdinalIgnoreCase);
    internal static string DirectoryPath => FontFiles.DirectoryForApplication(AppContext.BaseDirectory);

    internal static Choice Load(string fileName)
    {
        var path = FontFiles.Resolve(fileName, DirectoryPath);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("找不到字体文件：" + path, path);
        if (cache.TryGetValue(path, out var saved) && saved.Stamp == info.LastWriteTimeUtc.Ticks && saved.Length == info.Length)
            return saved.Choice;
        var fontPath = FontFiles.CreateLoadCopy(path);
        var family = System.Windows.Media.Fonts.GetFontFamilies(
            new Uri(Path.GetDirectoryName(fontPath) + Path.DirectorySeparatorChar), "./" + Path.GetFileName(fontPath)).FirstOrDefault()
            ?? throw new FileFormatException("字体文件中没有可用的字体族：" + path);
        var choice = new Choice(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), family);
        cache[path] = (info.LastWriteTimeUtc.Ticks, info.Length, choice);
        return choice;
    }

    internal static IReadOnlyList<Choice> ReadChoices(out List<string> errors)
    {
        Directory.CreateDirectory(DirectoryPath);
        errors = new List<string>();
        var choices = new List<Choice>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath).Where(FontFiles.IsFontFile)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try { choices.Add(Load(path)); }
            catch (Exception error) { errors.Add(Path.GetFileName(path) + ": " + error.Message); }
        }
        return choices;
    }

    internal static string Import(string source, string? replaceFile = null)
    {
        _ = Load(source); // Validate before touching the library entry.
        Directory.CreateDirectory(DirectoryPath);
        var name = replaceFile ?? Path.GetFileName(source);
        var destination = Path.Combine(DirectoryPath, name);
        if (!Path.GetFullPath(source).Equals(destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, true);
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
        cache.Remove(destination);
        return name;
    }

    internal static void Delete(string fileName)
    {
        var path = Path.Combine(DirectoryPath, Path.GetFileName(fileName));
        File.Delete(path);
        cache.Remove(path);
    }
}
