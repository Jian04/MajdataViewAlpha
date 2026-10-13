using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
namespace MajdataEdit;

internal sealed record ChartListEntry(string Folder, string Title, int Difficulty,
    string Level = "", string Artist = "", string Designer = "");

internal static class ChartListStore
{
    private static readonly string FilePath =
#if CHART_LIST_TESTS
        Environment.GetEnvironmentVariable("CHART_LIST_TEST_FILE")!;
#else
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MajdataAlpha", "ChartList.json");
#endif
    internal static string InstallRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            return folder.Name.Equals("MajdataEdit", StringComparison.OrdinalIgnoreCase) && folder.Parent?.Name.Equals("App", StringComparison.OrdinalIgnoreCase) == true
                ? folder.Parent.Parent!.FullName : folder.FullName;
        }
    }
    internal static List<ChartListEntry> Read()
    {
        var entries = File.Exists(FilePath)
            ? JsonConvert.DeserializeObject<List<ChartListEntry>>(File.ReadAllText(FilePath)) ?? new() : new List<ChartListEntry>();
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!File.Exists(Path.Combine(entries[i].Folder, "maidata.txt")))
            {
                entries.RemoveAt(i);
                continue;
            }
            try { entries[i] = Inspect(entries[i].Folder, entries[i].Difficulty); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Retain unavailable entries for removal or relocation. */ }
        }
        return entries;
    }
    internal static ChartListEntry Inspect(string folder, int? difficulty = null)
    {
        var entries = InspectAll(folder);
        if (difficulty.HasValue && !entries.Any(x => x.Difficulty == difficulty))
            throw new IOException("此难度已没有谱面：" + folder);
        return entries.FirstOrDefault(x => x.Difficulty == difficulty) ??
               entries.FirstOrDefault(x => x.Difficulty == 4) ?? entries.Last();
    }
    internal static IReadOnlyList<ChartListEntry> InspectAll(string folder)
    {
        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var text = File.ReadAllText(Path.Combine(folder, "maidata.txt"));
        if (!File.Exists(Path.Combine(folder, "track.mp3")) && !File.Exists(Path.Combine(folder, "track.ogg")))
            throw new IOException("缺少 track.mp3 / track.ogg：" + folder);
        var title = Regex.Match(text, @"(?m)^&title=(.*)$").Groups[1].Value.Trim();
        var populated = Regex.Matches(text, @"(?ms)^&inote_([1-7])=(.*?)(?=^&|\z)")
            .Cast<Match>().Where(m => !string.IsNullOrWhiteSpace(m.Groups[2].Value))
            .Select(m => int.Parse(m.Groups[1].Value) - 1).ToArray();
        if (populated.Length == 0) throw new IOException("没有可播放的难度：" + folder);
        string Field(string key) => Regex.Match(text, @"(?m)^&" + key + @"=(.*)$").Groups[1].Value.Trim();
        return populated.Distinct().OrderBy(x => x).Select(d => new ChartListEntry(folder,
            title.Length == 0 ? Path.GetFileName(folder) : title, d, Field("lv_" + (d + 1)), Field("artist"),
            string.IsNullOrEmpty(Field("des_" + (d + 1))) ? Field("des") : Field("des_" + (d + 1)))).ToArray();
    }
    internal static bool Same(ChartListEntry a, ChartListEntry b) => a.Difficulty == b.Difficulty &&
        string.Equals(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
    internal static void Update(Action<List<ChartListEntry>> change)
    {
        using var mutex = new Mutex(false, "Local\\MajdataAlpha.ChartList");
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try
        {
            var entries = Read(); change(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonConvert.SerializeObject(entries, Formatting.Indented));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        finally { mutex.ReleaseMutex(); }
    }
    internal static void Add(IEnumerable<ChartListEntry> entries) => Update(list =>
    {
        foreach (var entry in entries)
        {
            var index = list.FindIndex(x => Same(x, entry));
            if (index < 0) list.Add(entry); else list[index] = entry;
        }
    });
}
