using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit;

internal static class NotePreviewModule
{
    private static readonly string[] SlideMarkers =
        { "pp", "qq", "rp", "rq", "-", "^", "<", ">", "v", "V", "p", "q", "s", "z", "w" };

    public static string? ExtractNoteGroupAtCaret(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        caret = Math.Clamp(caret, 0, text.Length);

        var left = caret;
        while (left > 0 && !IsGroupDelimiter(text, left - 1))
            left--;

        var right = caret;
        while (right < text.Length && !IsGroupDelimiter(text, right))
            right++;

        return right <= left ? null : CleanNoteGroup(text.Substring(left, right - left));
    }

    public static string? CleanNoteGroup(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = Regex.Replace(raw, @"\s+", "");
        s = Regex.Replace(s, @"\([^)]*\)", "");
        s = Regex.Replace(s, @"\{[^}]*\}", "");
        s = s.Trim();
        if (s.EndsWith("E", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - 1);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static List<string> ExpandPreview(string? group)
    {
        var cleaned = CleanNoteGroup(group ?? "");
        if (cleaned == null)
            return new List<string>();
        if (ContainsIncompleteAlphaToken(cleaned))
            return new List<string>();

        var branches = cleaned.Split('/');
        var expandedGroups = new List<string> { "" };
        foreach (var branch in branches)
        {
            var candidates = ExpandSingleBranch(branch);
            if (candidates.Count == 0)
                return new List<string>();

            var next = new List<string>();
            foreach (var prefix in expandedGroups)
            foreach (var candidate in candidates)
                next.Add(string.IsNullOrEmpty(prefix) ? candidate : prefix + "/" + candidate);
            expandedGroups = next;
        }

        return expandedGroups;
    }

    private static List<string> ExpandSingleBranch(string branch)
    {
        branch = CleanNoteGroup(branch) ?? "";
        if (branch.Length == 0)
            return new List<string>();

        if (!TryGetIncompleteSlide(branch, out var head, out var marker))
        {
            if (IsDanglingVSlide(branch))
                return new List<string>();
            return new List<string> { EnsurePreviewDuration(branch) };
        }

        var result = new List<string>();
        for (var end = 1; end <= 8; end++)
        {
            if (IsValidPreviewSlideTarget(head[0] - '0', marker, end, out var suffix))
                result.Add(EnsurePreviewDuration(head + marker + suffix));
        }
        return result;
    }

    private static bool TryGetIncompleteSlide(string note, out string head, out string marker)
    {
        head = "";
        marker = "";
        if (note.Length < 2 || note[0] < '1' || note[0] > '8')
            return false;

        foreach (var m in SlideMarkers)
        {
            var idx = note.IndexOf(m, 1, StringComparison.Ordinal);
            if (idx <= 0)
                continue;

            var rest = note.Substring(idx + m.Length);
            if (m == "V")
            {
                if (rest.Length == 0)
                    return false;
                if (rest.Length == 1 && rest[0] >= '1' && rest[0] <= '8')
                {
                    head = note.Substring(0, idx);
                    marker = m + rest[0];
                    return true;
                }
                return false;
            }

            if (rest.Length > 0 && rest[0] >= '1' && rest[0] <= '8')
                return false;

            head = note.Substring(0, idx);
            marker = m;
            return true;
        }
        return false;
    }

    private static bool IsDanglingVSlide(string note)
    {
        var idx = note.IndexOf('V', 1);
        if (idx < 0)
            return false;
        var rest = note.Substring(idx + 1);
        return rest.Length == 0 || rest.All(c => c is 'b' or 'x' or 'm');
    }

    private static bool IsValidPreviewSlideTarget(int start, string marker, int end, out string suffix)
    {
        suffix = end.ToString();
        if (start < 1 || start > 8 || end < 1 || end > 8 || start == end)
            return false;

        var opposite = Opposite(start);
        if (marker.Length == 2 && marker[0] == 'V')
        {
            var middle = marker[1] - '0';
            if (middle < 1 || middle > 8 || middle == start || middle == opposite || end == middle)
                return false;

            var clockwiseToMiddle = ClockwiseDistance(start, middle);
            var clockwiseToEnd = ClockwiseDistance(start, end);
            return clockwiseToMiddle switch
            {
                2 => clockwiseToEnd is >= 4 and <= 7,
                6 => clockwiseToEnd is >= 1 and <= 4,
                _ => false
            };
        }

        return marker switch
        {
            "-" => !IsAdjacent(start, end),
            "s" or "z" or "w" => end == opposite,
            "^" or "v" => end != opposite,
            "V" => false,
            _ => true
        };
    }

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
        if (c is ',' or '=' or '&' or '\r' or '\n' or ';' or '\uFF0C' or '\uFF1B')
            return true;
        if (c == '<' && IsAlphaTokenStart(text, index))
            return true;
        if (c == '>')
        {
            var open = text.LastIndexOf('<', index);
            if (open >= 0)
            {
                var lastHardDelimiter = Math.Max(
                    Math.Max(text.LastIndexOf(',', index), text.LastIndexOf('\n', index)),
                    text.LastIndexOf('&', index));
                var star = text.IndexOf('*', open, index - open);
                if (open > lastHardDelimiter && star > open && IsAlphaTokenStart(text, open))
                    return true;
            }
        }
        return false;
    }

    private static bool IsAlphaTokenStart(string text, int index)
    {
        if (index + 2 >= text.Length || !char.IsLetter(text[index + 1]))
            return false;
        var close = text.IndexOf('>', index + 1);
        var star = text.IndexOf('*', index + 1);
        return close > index && star > index && star < close;
    }

    private static bool ContainsIncompleteAlphaToken(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '<')
                continue;
            if (IsAlphaTokenStart(text, i))
                continue;
            if (i + 1 < text.Length && char.IsLetter(text[i + 1]))
                return true;
        }
        return false;
    }

    private static int Opposite(int position) => (position + 3) % 8 + 1;

    private static int ClockwiseDistance(int start, int end) => (end - start + 8) % 8;

    private static bool IsAdjacent(int start, int end)
    {
        var clockwise = start == 8 ? 1 : start + 1;
        var counterClockwise = start == 1 ? 8 : start - 1;
        return end == clockwise || end == counterClockwise;
    }
}
