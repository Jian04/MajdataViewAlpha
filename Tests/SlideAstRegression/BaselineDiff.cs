using System.Text;
using MajdataCore;

namespace MajdataEdit;

/// <summary>
/// Compares the current parser against the v0.4.2 parser note by note.
/// Anything v0.4.2 accepted must still be accepted, or it is a regression.
/// </summary>
internal static class BaselineDiff
{
    private sealed class Divergence
    {
        public string Snippet = string.Empty;
        public bool BaselineOk;
        public bool CurrentOk;
        public int BaselineNotes;
        public int CurrentNotes;
        public string CurrentError = string.Empty;
    }

    public static int Run(bool verbose)
    {
        var corpus = BuildCorpus();
        var regressions = new List<Divergence>();
        var newlyRejected = new List<Divergence>();
        var newlyAccepted = new List<Divergence>();
        var countMismatch = new List<Divergence>();

        foreach (var snippet in corpus)
        {
            var result = Compare(snippet);
            if (result == null)
                continue;
            if (result.BaselineOk && !result.CurrentOk)
                newlyRejected.Add(result);
            else if (!result.BaselineOk && result.CurrentOk)
                newlyAccepted.Add(result);
            else if (result.BaselineOk && result.BaselineNotes != result.CurrentNotes)
                countMismatch.Add(result);
        }

        regressions.AddRange(newlyRejected);
        regressions.AddRange(countMismatch);

        Console.WriteLine($"corpus: {corpus.Count} snippets");
        Console.WriteLine(
            $"v0.4.2 accepted but current rejects : {newlyRejected.Count}  <-- regressions");
        Console.WriteLine(
            $"note count differs                  : {countMismatch.Count}  <-- regressions");
        Console.WriteLine(
            $"v0.4.2 rejected but current accepts : {newlyAccepted.Count}  (intentional loosening)");

        Report("REGRESSION: v0.4.2 parsed this, current does not", newlyRejected);
        Report("REGRESSION: different note count", countMismatch);
        if (verbose)
            Report("now accepted (review each)", newlyAccepted);

        return regressions.Count;
    }

    private static void Report(string title, List<Divergence> items)
    {
        if (items.Count == 0)
            return;
        Console.WriteLine();
        Console.WriteLine($"--- {title} ({items.Count}) ---");

        // Listing the first sixty of several thousand says almost nothing: the
        // corpus is generated button by button, so the head is eight spellings of
        // one rule and the tail is never seen. Grouping by diagnostic shows every
        // rule that fired and how much of the count each one owns, which is what
        // decides whether a divergence was intended.
        foreach (var group in items
                     .GroupBy(Rule)
                     .OrderByDescending(group => group.Count()))
            Console.WriteLine(
                $"  {group.Count(),6}  {group.Key,-58} e.g. {group.First().Snippet}");
    }

    /// <summary>
    /// The diagnostic without the snippet it quotes, so that the same rule firing
    /// on eight buttons groups as one rule rather than eight.
    /// </summary>
    private static string Rule(Divergence item)
    {
        if (item.CurrentOk)
            return $"note count {item.BaselineNotes} -> {item.CurrentNotes}";
        var message = FirstLine(item.CurrentError);
        var quoted = message.LastIndexOf(": ", StringComparison.Ordinal);
        return quoted > 0 ? message.Substring(0, quoted) : message;
    }

    private static string FirstLine(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Split('\n')[0];

    private static Divergence? Compare(string snippet)
    {
        var baselineOk = TryBaseline(snippet, out var baselineNotes);
        var currentOk = TryCurrent(snippet, out var currentNotes, out var currentError);
        if (baselineOk == currentOk && baselineNotes == currentNotes)
            return null;

        return new Divergence
        {
            Snippet = snippet,
            BaselineOk = baselineOk,
            CurrentOk = currentOk,
            BaselineNotes = baselineNotes,
            CurrentNotes = currentNotes,
            CurrentError = currentError
        };
    }

    private static bool TryBaseline(string snippet, out int noteCount)
    {
        noteCount = 0;
        try
        {
            var timing = new Baseline042.SimaiTimingPoint(
                1d, _content: snippet, bpm: 120f);
            var notes = timing.getNotes();
            noteCount = notes.Count;
            return string.IsNullOrEmpty(timing.noteParseError);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCurrent(string snippet, out int noteCount, out string error)
    {
        noteCount = 0;
        error = string.Empty;
        try
        {
            var timing = new SimaiTimingPoint(1d, _content: snippet, bpm: 120f);
            var notes = timing.getNotes();
            noteCount = notes.Count;
            error = timing.noteParseError ?? string.Empty;
            return string.IsNullOrEmpty(error);
        }
        catch (Exception e)
        {
            error = "THREW: " + e.Message;
            return false;
        }
    }

    private static List<string> BuildCorpus()
    {
        var corpus = new List<string>();
        var buttons = new[] { "1", "2", "3", "4", "5", "6", "7", "8" };
        var shapes = new[]
        {
            "-", "^", "<", ">", "v", "p", "q", "pp", "qq", "s", "z", "w", "V"
        };
        var durations = new[]
        {
            "[8:1]", "[4:1]", "[1:1]", "[1:0]", "[16:3]", "[#2]", "[160#8:1]",
            "[2##1.5]", "[1.5##4:1]"
        };
        var noteModifiers = new[] { "", "b", "x", "h", "bh", "f", "hf", "bx" };

        foreach (var button in buttons)
        {
            foreach (var modifier in noteModifiers)
            {
                corpus.Add(button + modifier);
                corpus.Add(button + modifier + "[8:1]");
                corpus.Add(button + "d" + modifier);
            }
        }

        foreach (var start in buttons)
        foreach (var end in buttons)
        foreach (var shape in shapes)
        foreach (var duration in new[] { "[8:1]", "[1:0]" })
        {
            corpus.Add($"{start}{shape}{end}{duration}");
            corpus.Add($"{start}{shape}{end}d{duration}");
            corpus.Add($"{start}b{shape}{end}{duration}");
        }

        // V slides need a middle key, wifi needs the opposite key.
        foreach (var start in buttons)
        foreach (var middle in buttons)
        foreach (var end in buttons)
            corpus.Add($"{start}V{middle}{end}[8:1]");

        foreach (var duration in durations)
        {
            corpus.Add($"1h{duration}");
            corpus.Add($"1-5{duration}");
            corpus.Add($"C{duration}");
            corpus.Add($"Ch{duration}");
            corpus.Add($"E1h{duration}");
        }

        foreach (var area in new[] { "A", "B", "C", "D", "E" })
        foreach (var index in buttons)
        {
            corpus.Add(area + index);
            corpus.Add(area + index + "f");
            corpus.Add(area + index + "h[8:1]");
        }

        corpus.AddRange(new[]
        {
            "1", "1/2", "1/2/3", "12", "123", "1,2", "1h", "1b", "1x", "1$", "1$$",
            "1?", "1!", "1-5[8:1]?", "1-5[8:1]!", "1-5[8:1]$", "1-5[8:1]$$",
            "1-5[8:1]*-3[8:1]", "1-5[8:1]*^3[8:1]", "1-5[8:1]*p3[8:1]",
            "1-5[8:1]/2-6[8:1]", "7>2qq1d[12:1]", "6>3pp5d[12:1]",
            "7>2qq1d[12:1]/6>3pp5d[12:1]", "7^1d[8:1]", "2^8d[8:1]",
            "1-3-5[8:1]", "1-3[8:1]-5[8:1]", "1V35[8:1]", "1w5[8:1]",
            "1b-5b[8:1]", "1x-5[8:1]", "C1", "Cf", "1h[2:1]", "1h[#2]",
            "E", "", " ", "1-", "-5", "1-5", "1-5[]", "1-5[0:1]", "1-5[1:]",
            "9", "0", "1-9[8:1]", "1V15[8:1]", "1V55[8:1]"
        });

        return corpus
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct()
            .ToList();
    }
}
