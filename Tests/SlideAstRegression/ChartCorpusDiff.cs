using System.Text;
using System.Text.RegularExpressions;
using MajdataCore;

namespace MajdataEdit;

/// <summary>
/// Runs every beat of every chart on disk through the v0.4.2 parser and the
/// current one. A beat that v0.4.2 read but the current build rejects is what
/// the user sees as "读取不了".
/// </summary>
internal static class ChartCorpusDiff
{
    private static readonly Regex InoteHeader =
        new(@"^&inote_\d+\s*=", RegexOptions.Compiled);

    public static int Run(string root, int fileLimit)
    {
        var files = Directory
            .EnumerateFiles(root, "maidata.txt", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(fileLimit)
            .ToList();

        var beats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            CollectBeats(file, beats);

        var newlyRejected = new SortedDictionary<string, (string Beat, int Count)>(
            StringComparer.Ordinal);
        var countMismatch = new List<string>();
        var semanticDrift = new List<string>();
        var sameHeadBreakFix = new List<string>();
        var crashed = new List<string>();

        foreach (var beat in beats)
        {
            var baselineOk = TryBaseline(beat, out var baselineNotes);
            if (!baselineOk)
                continue;

            var currentOk = TryCurrent(
                beat, out var currentNotes, out var error, out var threw);
            if (threw)
            {
                crashed.Add($"{beat}  ==>  {error}");
                continue;
            }
            if (!currentOk)
            {
                var key = Normalize(error);
                var existing = newlyRejected.TryGetValue(key, out var value)
                    ? value
                    : (Beat: beat, Count: 0);
                newlyRejected[key] = (existing.Beat, existing.Count + 1);
                continue;
            }
            if (baselineNotes != currentNotes)
            {
                countMismatch.Add(
                    $"{beat}  notes {baselineNotes} -> {currentNotes}");
                continue;
            }

            if (TryShape(beat, baseline: true, out var oldShape) &&
                TryShape(beat, baseline: false, out var newShape) &&
                oldShape != newShape)
            {
                var entry = $"{beat}\n      v042 {oldShape}\n      now  {newShape}";
                if (IsSameHeadBreakFix(beat, oldShape, newShape))
                    sameHeadBreakFix.Add(entry);
                else
                    semanticDrift.Add(entry);
            }
        }

        var tokenGaps = AuditTokenSpans(beats, out var pathsChecked);

        var rejectedTotal = newlyRejected.Values.Sum(item => item.Count);
        Console.WriteLine($"charts scanned : {files.Count}");
        Console.WriteLine($"distinct beats : {beats.Count}");
        Console.WriteLine(
            $"slide paths token-checked : {pathsChecked}, " +
            $"spans not tiling : {tokenGaps.Count}");
        foreach (var gap in tokenGaps.Take(20))
            Console.WriteLine("  " + gap);
        Console.WriteLine($"current throws : {crashed.Count}");
        Console.WriteLine(
            $"v0.4.2 read, current rejects : {rejectedTotal} beats " +
            $"in {newlyRejected.Count} distinct messages");
        Console.WriteLine($"note count differs : {countMismatch.Count}");
        Console.WriteLine(
            $"same-head break flag restored : {sameHeadBreakFix.Count}  " +
            $"(v0.4.2 bug, fixed on purpose)");
        Console.WriteLine(
            $"per-note fields differ, unexplained : {semanticDrift.Count}");

        if (crashed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"--- CRASHES ({crashed.Count}) ---");
            foreach (var item in crashed.Take(20))
                Console.WriteLine("  " + item);
        }

        if (newlyRejected.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"--- REJECTED BY CURRENT, grouped by message " +
                $"({newlyRejected.Count}) ---");
            foreach (var entry in newlyRejected.OrderByDescending(
                         item => item.Value.Count))
                Console.WriteLine(
                    $"  {entry.Value.Count,6}x  {entry.Key}\n" +
                    $"          e.g. {entry.Value.Beat}");
        }

        if (countMismatch.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"--- NOTE COUNT DIFFERS ({countMismatch.Count}) ---");
            foreach (var item in countMismatch.Take(40))
                Console.WriteLine("  " + item);
        }

        if (semanticDrift.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"--- PER-NOTE FIELDS DIFFER, UNEXPLAINED ({semanticDrift.Count}) ---");
            foreach (var item in semanticDrift.Take(40))
                Console.WriteLine("  " + item);
        }

        if (sameHeadBreakFix.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"--- SAME-HEAD BREAK FLAG RESTORED ({sameHeadBreakFix.Count}) ---");
            foreach (var item in sameHeadBreakFix.Take(3))
                Console.WriteLine("  " + item);
            if (sameHeadBreakFix.Count > 3)
                Console.WriteLine(
                    $"  ... and {sameHeadBreakFix.Count - 3} more, all the same shape");
        }

        return rejectedTotal + countMismatch.Count + crashed.Count +
               semanticDrift.Count;
    }

    /// <summary>
    /// Whether the whole divergence is v0.4.2 having dropped the break flag from a
    /// same-head slide branch.
    ///
    /// In '5bxpp5b[4:1]*qq5b[4:1]' both branches are written with their own 'b',
    /// and v0.4.2 honoured it on the first branch only, so the second branch was
    /// silently not a break. Correcting that changes what the corpus parses to,
    /// which is a divergence the tool must report rather than hide, but it is a
    /// fix and not a regression.
    ///
    /// The three conditions are what keep this from excusing anything else: the
    /// beat has to be a same-head group at all, the only field allowed to move is
    /// isSlideBreak, and it may only turn on. A flag being lost, or any other
    /// field moving, still fails.
    /// </summary>
    private static bool IsSameHeadBreakFix(
        string beat, string oldShape, string newShape)
    {
        if (!beat.Contains('*'))
            return false;

        var oldNotes = oldShape.Split(" ;; ");
        var newNotes = newShape.Split(" ;; ");
        if (oldNotes.Length != newNotes.Length)
            return false;

        var turnedOn = false;
        for (var i = 0; i < oldNotes.Length; i++)
        {
            if (oldNotes[i] == newNotes[i])
                continue;
            if (!oldNotes[i].Contains("|s=False|") ||
                oldNotes[i].Replace("|s=False|", "|s=True|") != newNotes[i])
                return false;
            turnedOn = true;
        }

        return turnedOn;
    }

    private static string Normalize(string error)
    {
        var line = (error ?? string.Empty).Split('\n')[0];
        var colon = line.LastIndexOf('：');
        return colon > 0 ? line.Substring(0, colon) : line;
    }

    private static void CollectBeats(string file, HashSet<string> beats)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file, Encoding.UTF8);
        }
        catch
        {
            return;
        }

        var inside = false;
        var body = new StringBuilder();
        foreach (var line in lines)
        {
            if (InoteHeader.IsMatch(line))
            {
                inside = true;
                body.Append(line.Substring(line.IndexOf('=') + 1));
                body.Append('\n');
                continue;
            }
            if (!inside)
                continue;
            if (line.StartsWith("&"))
            {
                inside = false;
                continue;
            }
            body.Append(line);
            body.Append('\n');
        }

        foreach (var raw in body.ToString().Split(','))
        {
            var beat = StripNonNotes(raw);
            if (beat.Length > 0)
                beats.Add(beat);
        }
    }

    /// <summary>
    /// Drops comments, whitespace and the chart-level "(bpm)" / "{beat}" /
    /// "&lt;command&gt;" prefixes so that only the note text of the beat is left.
    /// </summary>
    private static string StripNonNotes(string raw)
    {
        var text = Regex.Replace(raw, @"\|\|[^\n]*", string.Empty);
        text = Regex.Replace(text, @"\s+", string.Empty);
        text = Regex.Replace(text, @"^(\([^)]*\)|\{[^}]*\}|<[^>]*>)+", string.Empty);
        return text;
    }

    /// <summary>
    /// Every slide path in the corpus must be tiled exactly by the spans the
    /// parser reports: no gap, no overlap, nothing past the end. A gap would mean
    /// the editor has characters it cannot attribute to anything it parsed.
    /// </summary>
    private static List<string> AuditTokenSpans(
        IEnumerable<string> beats, out int pathsChecked)
    {
        var failures = new List<string>();
        var checkedPaths = 0;
        foreach (var beat in beats)
        {
            List<SimaiNote> notes;
            try
            {
                notes = new SimaiTimingPoint(1d, _content: beat, bpm: 120f)
                    .getNotes();
            }
            catch
            {
                continue;
            }

            foreach (var note in notes)
            {
                if (string.IsNullOrEmpty(note.pathExpression))
                    continue;

                var expression = note.pathExpression;
                var tokens = new ChartTokenList();
                if (!SlidePathParser.TryParsePath(expression, out _, tokens))
                    continue;

                checkedPaths++;
                var previousEnd = 0;
                var ordered = tokens.tokens.OrderBy(token => token.start);
                foreach (var token in ordered)
                {
                    if (token.start != previousEnd)
                    {
                        failures.Add(
                            $"{expression}: {(token.start < previousEnd ? "overlap" : "gap")}" +
                            $" at {previousEnd}");
                        break;
                    }
                    previousEnd = token.End;
                }
                if (previousEnd != expression.Length)
                    failures.Add(
                        $"{expression}: covered {previousEnd} of {expression.Length}");
            }
        }

        pathsChecked = checkedPaths;
        return failures;
    }

    private static bool TryBaseline(string beat, out int noteCount)
    {
        noteCount = 0;
        try
        {
            var timing = new Baseline042.SimaiTimingPoint(
                1d, _content: beat, bpm: 120f);
            var notes = timing.getNotes();
            noteCount = notes.Count;
            return string.IsNullOrEmpty(timing.noteParseError);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The per-note fields that decide what the player draws and judges. Note
    /// count alone hides drift such as a break flag moving between head and body.
    /// </summary>
    private static string Shape(string beat, bool baseline)
    {
        var parts = new List<string>();
        if (baseline)
        {
            var timing = new Baseline042.SimaiTimingPoint(
                1d, _content: beat, bpm: 120f);
            foreach (var note in timing.getNotes())
                parts.Add(
                    $"{note.noteType}|{note.startPosition}|{note.noteContent}|" +
                    $"b={note.isBreak}|s={note.isSlideBreak}|e={note.isEx}|" +
                    $"h={note.isHanabi}|f={note.isForceStar}|" +
                    $"t={note.isFakeRotate}|no={note.isSlideNoHead}");
        }
        else
        {
            var timing = new SimaiTimingPoint(1d, _content: beat, bpm: 120f);
            foreach (var note in timing.getNotes())
                parts.Add(
                    $"{note.noteType}|{note.startPosition}|{note.noteContent}|" +
                    $"b={note.isBreak}|s={note.isSlideBreak}|e={note.isEx}|" +
                    $"h={note.isHanabi}|f={note.isForceStar}|" +
                    $"t={note.isFakeRotate}|no={note.isSlideNoHead}");
        }
        return string.Join(" ;; ", parts);
    }

    private static bool TryShape(string beat, bool baseline, out string shape)
    {
        try
        {
            shape = Shape(beat, baseline);
            return true;
        }
        catch
        {
            shape = string.Empty;
            return false;
        }
    }

    private static bool TryCurrent(
        string beat,
        out int noteCount,
        out string error,
        out bool threw)
    {
        noteCount = 0;
        error = string.Empty;
        threw = false;
        try
        {
            var timing = new SimaiTimingPoint(1d, _content: beat, bpm: 120f);
            var notes = timing.getNotes();
            noteCount = notes.Count;
            error = timing.noteParseError ?? string.Empty;
            return string.IsNullOrEmpty(error);
        }
        catch (Exception e)
        {
            threw = true;
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }
}
