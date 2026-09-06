using System.Text.RegularExpressions;

namespace MajdataEdit;

/// <summary>
/// Runs whole difficulties through Serialize, current against v0.4.2.
///
/// ChartCorpusDiff feeds one beat at a time, which cannot see anything that only
/// exists for a whole chart: a note the scan invents between beats, the overlay
/// merge, or a first beat that behaves differently from the same text in the
/// middle. A note that appears in the current build and not in v0.4.2 is exactly
/// the "note nobody wrote" that shows up on key 1 at second 0.
/// </summary>
internal static class WholeChartDiff
{
    private static readonly Regex InoteHeader =
        new(@"^&inote_(\d+)\s*=", RegexOptions.Compiled);

    private sealed class Note
    {
        public double Time;
        public int Key;
        public string Type = string.Empty;
        public string Content = string.Empty;

        public override string ToString() =>
            $"t={Time:F3} {Type} key={Key} '{Content}'";
    }

    public static int Run(string root, int fileLimit)
    {
        var charts = Collect(root, fileLimit);
        var extraNotes = new List<string>();
        var missingNotes = new List<string>();
        var crashed = new List<string>();
        var compared = 0;

        foreach (var (name, text) in charts)
        {
            List<Note> current;
            List<Note> baseline;
            try
            {
                current = Current(text);
                baseline = Baseline(text);
            }
            catch (Exception e)
            {
                crashed.Add($"{name}  {e.Message}");
                continue;
            }

            compared++;
            Report(name, baseline, current, extraNotes, "EXTRA in current");
            Report(name, current, baseline, missingNotes, "MISSING from current");
        }

        Console.WriteLine($"difficulties compared : {compared}");
        Console.WriteLine($"crashed               : {crashed.Count}");
        Console.WriteLine(
            $"notes current invents : {extraNotes.Count}  <-- phantom notes");
        Console.WriteLine($"notes current loses   : {missingNotes.Count}");

        Show("CRASHED", crashed);
        Show("PHANTOM: in current, not in v0.4.2", extraNotes);
        Show("LOST: in v0.4.2, not in current", missingNotes);

        return extraNotes.Count + missingNotes.Count + crashed.Count;
    }

    private static void Show(string title, List<string> items)
    {
        if (items.Count == 0)
            return;
        Console.WriteLine();
        Console.WriteLine($"--- {title} ({items.Count}) ---");
        foreach (var item in items.Take(25))
            Console.WriteLine("  " + item);
        if (items.Count > 25)
            Console.WriteLine($"  ... and {items.Count - 25} more");
    }

    /// <summary>
    /// Notes present in <paramref name="from"/> but not in <paramref name="to"/>,
    /// matched by time, key and content so that a reordering is not reported.
    /// </summary>
    private static void Report(
        string name,
        List<Note> to,
        List<Note> from,
        List<string> sink,
        string label)
    {
        var counts = new Dictionary<string, int>();
        foreach (var note in to)
        {
            var key = note.ToString();
            counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
        }

        foreach (var note in from)
        {
            var key = note.ToString();
            if (counts.TryGetValue(key, out var value) && value > 0)
            {
                counts[key] = value - 1;
                continue;
            }
            sink.Add($"{name}  {label}: {note}");
        }
    }

    private static List<Note> Current(string text)
    {
        SimaiProcess.Serialize(text);
        return SimaiProcess.notelist
            .SelectMany(timing => timing.getNotes().Select(note => new Note
            {
                Time = timing.time,
                Key = note.startPosition,
                Type = note.noteType.ToString(),
                Content = note.noteContent ?? string.Empty
            }))
            .ToList();
    }

    private static List<Note> Baseline(string text)
    {
        Baseline042.SimaiProcess.Serialize(text);
        return Baseline042.SimaiProcess.notelist
            .SelectMany(timing => timing.getNotes().Select(note => new Note
            {
                Time = timing.time,
                Key = note.startPosition,
                Type = note.noteType.ToString(),
                Content = note.noteContent ?? string.Empty
            }))
            .ToList();
    }

    /// <summary>
    /// Each difficulty on its own, the way the editor holds one at a time. The
    /// &amp;inote_ header is dropped because the editor's text box never contains it.
    /// </summary>
    private static List<(string Name, string Text)> Collect(string root, int fileLimit)
    {
        var charts = new List<(string, string)>();
        var files = Directory
            .EnumerateFiles(root, "maidata.txt", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(fileLimit);

        foreach (var file in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                continue;
            }

            var song = Path.GetFileName(Path.GetDirectoryName(file)) ?? file;
            var difficulty = string.Empty;
            var body = new List<string>();

            void Flush()
            {
                if (difficulty.Length == 0)
                    return;
                var text = string.Join("\n", body).Trim();
                if (text.Length > 0)
                    charts.Add(($"{song}#{difficulty}", text));
                body.Clear();
            }

            foreach (var line in lines)
            {
                var header = InoteHeader.Match(line);
                if (header.Success)
                {
                    Flush();
                    difficulty = header.Groups[1].Value;
                    body.Add(line.Substring(header.Length));
                    continue;
                }
                if (line.StartsWith("&", StringComparison.Ordinal))
                {
                    Flush();
                    difficulty = string.Empty;
                    continue;
                }
                if (difficulty.Length > 0)
                    body.Add(line);
            }
            Flush();
        }

        return charts;
    }
}
