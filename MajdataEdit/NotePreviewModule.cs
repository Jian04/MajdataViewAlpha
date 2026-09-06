using System.Text;
using System.Text.RegularExpressions;
using MajdataCore;

namespace MajdataEdit;

internal static class NotePreviewModule
{
    private static readonly string[] SlideMarkers =
        { "pp", "qq", "rp", "rq", "-", "^", "<", ">", "v", "V", "P", "Q", "p", "q", "s", "z", "w" };

    public static string? ExtractNoteGroupAtCaret(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        caret = Math.Clamp(caret, 0, text.Length);

        if (IsInsideAlphaCommand(text, caret))
            return null;

        var left = caret;
        while (left > 0 && !IsGroupDelimiter(text, left - 1))
            left--;

        var right = caret;
        while (right < text.Length && !IsGroupDelimiter(text, right))
            right++;

        var raw = right <= left ? null : text.Substring(left, right - left);
        if (ContainsAlphaCommandFragment(raw))
            return null;
        return raw == null ? null : CleanNoteGroup(raw);
    }

    public static string? CleanNoteGroup(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = Regex.Replace(raw, @"\s+", "");
        s = AlphaCommandBoundary.RemoveCommands(s);
        s = Regex.Replace(s, @"\([^)]*\)", "");
        s = Regex.Replace(s, @"\{[^}]*\}", "");
        s = s.Trim();
        if (s.EndsWith("E", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - 1);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static List<string> ExpandPreview(string? group)
        => ExpandPreviewTimings(group).SelectMany(timing => timing).ToList();

    public static List<List<string>> ExpandPreviewTimings(string? group)
    {
        var cleaned = CleanNoteGroup(group ?? "");
        if (cleaned == null)
            return new List<List<string>>();
        if (ContainsIncompleteAlphaToken(cleaned))
            return new List<List<string>>();

        var result = new List<List<string>>();
        foreach (var pseudoEachPart in cleaned.Split('`', StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = ExpandSingleTiming(pseudoEachPart);
            if (expanded.Count == 0)
                return new List<List<string>>();
            result.Add(expanded);
        }
        return result;
    }

    private static List<string> ExpandSingleTiming(string cleaned)
    {
        // Same division the player uses, so the preview shows the notes that will
        // actually be judged. Same-head groups stay whole: their branches share the
        // head's appearance and are rebuilt below.
        var branches = NoteSlotParser.SplitTopLevel(cleaned);
        var expandedGroups = new List<string> { "" };
        var hasPreviewableBranch = false;
        foreach (var branch in branches)
        {
            var candidates = ExpandSingleBranch(branch);
            if (candidates.Count == 0)
                continue;
            hasPreviewableBranch = true;

            var next = new List<string>();
            foreach (var prefix in expandedGroups)
            foreach (var candidate in candidates)
                next.Add(string.IsNullOrEmpty(prefix) ? candidate : prefix + "/" + candidate);
            expandedGroups = next;
        }

        return hasPreviewableBranch
            ? expandedGroups
            : new List<string>();
    }

    private static List<string> ExpandSingleBranch(string branch)
    {
        branch = CleanNoteGroup(branch) ?? "";
        if (branch.Length == 0)
            return new List<string>();
        if (ContainsMultiLoopMarker(branch) &&
            !branch.Any(character => character is 'A' or 'B' or 'C' or 'D' or 'E'))
            return new List<string>();
        if (branch.Contains('*'))
            return ExpandSameHeadPreview(branch);

        // A branch is decided by the same parser used by SimaiProcess and
        // SyntaxCheck, in its preview mode. Half-typed slide paths are not
        // completed by guessing endpoints any more; that filled the playfield with
        // routes nobody typed.
        var candidate = EnsurePreviewDuration(branch);
        if (!NoteExpressionParser.TryParse(
                candidate, out _, out _, forPreview: true))
            return new List<string>();
        return new List<string> { candidate };
    }

    private static List<string> ExpandSameHeadPreview(string branch)
    {
        if (!SlidePathParser.TryExpandSameHead(branch, out var branches) ||
            !SlidePathParser.TryReadPosition(
                branch, 0, out var head, out _))
            return new List<string>();

        var headExpression = head.ToExpression();
        var expandedGroups = new List<string> { "" };
        for (var index = 0; index < branches.Count; index++)
        {
            var candidates = ExpandSingleBranch(branches[index]);
            if (candidates.Count == 0)
                return new List<string>();

            var next = new List<string>();
            foreach (var prefix in expandedGroups)
            foreach (var candidate in candidates)
            {
                var suffix = candidate;
                if (index > 0)
                {
                    if (!candidate.StartsWith(
                            headExpression, StringComparison.Ordinal))
                        continue;
                    suffix = candidate.Substring(headExpression.Length);
                }
                next.Add(index == 0 ? suffix : prefix + "*" + suffix);
            }
            if (next.Count == 0)
                return new List<string>();
            expandedGroups = next;
        }
        return expandedGroups;
    }

    private static bool ContainsMultiLoopMarker(string note)
        => note.Contains("<<", StringComparison.Ordinal) ||
           note.Contains(">>", StringComparison.Ordinal);

    private static string EnsurePreviewDuration(string note)
    {
        if (string.IsNullOrEmpty(note) || note.Contains('['))
            return note;

        foreach (var marker in SlideMarkers)
        {
            var idx = note.IndexOf(marker, 1, StringComparison.Ordinal);
            if (idx > 0)
                return note + "[4:1]";
        }
        return note;
    }

    public static string? BuildPreviewChartText(IEnumerable<string> notes, float bpm = 120f)
    {
        var list = notes?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
        if (list.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append('(').Append(bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(')');
        sb.Append("{1}");
        sb.Append(string.Join("/", list));
        sb.Append(",E");
        return sb.ToString();
    }

    private static bool IsGroupDelimiter(string text, int index)
    {
        var c = text[index];
        if (c is ',' or '=' or '&' or '@' or '\r' or '\n' or ';' or '\uFF0C' or '\uFF1B')
            return true;
        if (c == '<' && IsAlphaTokenStart(text, index))
            return true;
        if (c == '>')
        {
            var open = text.LastIndexOf('<', index);
            if (open >= 0 && AlphaCommandBoundary.TryGetCommand(text, open, out var close) &&
                close == index)
                return true;
        }
        return false;
    }

    private static bool IsAlphaTokenStart(string text, int index)
    {
        return AlphaCommandBoundary.TryGetCommand(text, index, out _);
    }

    private static bool IsInsideAlphaCommand(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var probe = Math.Clamp(caret, 0, text.Length);
        var searchStart = Math.Min(text.Length - 1, Math.Max(0, probe - 1));
        var open = text.LastIndexOf('<', searchStart);
        if (open < 0)
            return false;

        var hardDelimiter = LastHardDelimiterBefore(text, probe);
        if (open < hardDelimiter)
            return false;

        if (AlphaCommandBoundary.IsPotentialStart(text, open))
        {
            var close = text.IndexOf('>', open + 1);
            return close < 0 || probe <= close;
        }

        return false;
    }

    private static int LastHardDelimiterBefore(string text, int index)
    {
        var limit = Math.Min(index, text.Length);
        var last = -1;
        for (var i = 0; i < limit; i++)
            if (text[i] is ',' or '=' or '&' or '@' or '\r' or '\n' or ';' or '\uFF0C' or '\uFF1B')
                last = i;
        return last;
    }

    private static bool ContainsAlphaCommandFragment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && AlphaCommandBoundary.IsPotentialStart(text, i))
                return true;
        }
        return false;
    }

    private static bool ContainsIncompleteAlphaToken(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '<')
                continue;
            if (IsAlphaTokenStart(text, i))
                continue;
            if (AlphaCommandBoundary.IsPotentialStart(text, i))
                return true;
        }
        return false;
    }

}
