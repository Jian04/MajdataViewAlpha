using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

internal sealed class SimaiColorizer : DocumentColorizingTransformer
{
    private static readonly Regex AlphaToken = new(
        @"<[A-Za-z]+\*[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Bpm = new(@"\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex Beat = new(@"\{[^}]+\}", RegexOptions.Compiled);

    private static readonly SolidColorBrush TokenBrush = Brush("#D7A7FF");
    private static readonly SolidColorBrush BpmBrush = Brush("#55D6FF");
    private static readonly SolidColorBrush BeatBrush = Brush("#8BD49C");
    private static readonly SolidColorBrush SlideBrush = Brush("#8FD9FF");
    private static readonly SolidColorBrush TouchBrush = Brush("#65D6C2");
    private static readonly SolidColorBrush TapBrush = Brush("#FF91B8");
    private static readonly SolidColorBrush EachBrush = Brush("#FFE074");
    private static readonly SolidColorBrush BreakBrush = Brush("#FF704D");
    private static readonly SolidColorBrush MonoBrush = Brush("#A9B0BA");
    private static readonly SolidColorBrush CommentBrush = Brush("#727985");
    private static readonly SolidColorBrush SectionMarkerBrush = Brush("#39D9D0");
    private readonly List<TextSpan> markerSpans = new();
    private readonly List<TextSpan> commentSpans = new();
    private object? cachedVersion;

    protected override void ColorizeLine(DocumentLine line)
    {
        RefreshDocumentSpans();
        var text = CurrentContext.Document.GetText(line);
        var occupied = new bool[text.Length];

        ApplyMatches(line, text, AlphaToken, TokenBrush, occupied);
        ApplyMatches(line, text, Bpm, BpmBrush, occupied);
        ApplyMatches(line, text, Beat, BeatBrush, occupied);

        foreach (var slot in SplitRanges(text, ','))
            ColorizeSlot(line, text, slot.Start, slot.Length, occupied);

        ApplyForegroundSpans(line, markerSpans, SectionMarkerBrush, occupied);
        ApplyForegroundSpans(line, commentSpans, CommentBrush, occupied);
    }

    private void RefreshDocumentSpans()
    {
        var document = CurrentContext.Document;
        if (ReferenceEquals(cachedVersion, document.Version))
            return;

        cachedVersion = document.Version;
        markerSpans.Clear();
        commentSpans.Clear();

        var text = document.Text;
        for (var i = 0; i < text.Length;)
        {
            if (i + 1 < text.Length && text[i] == '|' && text[i + 1] == '*')
            {
                var end = text.IndexOf("*|", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                commentSpans.Add(new TextSpan(i, end - i));
                i = end;
                continue;
            }
            if (i + 1 < text.Length && text[i] == '|' && text[i + 1] == '|')
            {
                var end = text.IndexOf('\n', i + 2);
                end = end < 0 ? text.Length : end;
                commentSpans.Add(new TextSpan(i, end - i));
                i = end;
                continue;
            }
            if (text[i] == '&' && TryReadTintMarker(text, i, out var color, out var length))
            {
                markerSpans.Add(new TextSpan(i, length));
                i += length;
                continue;
            }
            i++;
        }
    }

    private static bool TryReadTintMarker(
        string text,
        int start,
        out string color,
        out int length)
    {
        color = "";
        length = 0;
        if (start + 5 <= text.Length &&
            string.Equals(text.Substring(start, 5), "&NULL", StringComparison.OrdinalIgnoreCase))
        {
            length = 5;
            return true;
        }
        if (start + 7 > text.Length)
            return false;

        var candidate = text.Substring(start + 1, 6);
        if (!candidate.All(Uri.IsHexDigit))
            return false;
        color = candidate;
        length = 7;
        return true;
    }

    private void ApplyForegroundSpans(
        DocumentLine line,
        IEnumerable<TextSpan> spans,
        SolidColorBrush brush,
        bool[] occupied)
    {
        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;
        foreach (var span in spans)
        {
            var start = Math.Max(lineStart, span.Start);
            var end = Math.Min(lineEnd, span.Start + span.Length);
            if (end > start)
                Apply(line, start - lineStart, end - start, brush, occupied, true);
        }
    }

    private readonly record struct TextSpan(int Start, int Length);

    private void ColorizeSlot(DocumentLine line, string text, int start, int length, bool[] occupied)
    {
        if (length <= 0)
            return;

        var slotText = text.Substring(start, length);
        var branches = SplitRanges(slotText, '/', '`');
        var isEach = branches.Count > 1;

        foreach (var branch in branches)
            ColorizeBranch(line, text, start + branch.Start, branch.Length, isEach, occupied);

        if (isEach)
        {
            for (var i = start; i < start + length; i++)
                if (text[i] is '/' or '`')
                    Apply(line, i, 1, EachBrush, occupied, true);
        }
    }

    private void ColorizeBranch(
        DocumentLine line,
        string text,
        int start,
        int length,
        bool isEach,
        bool[] occupied)
    {
        TrimRange(text, ref start, ref length);
        if (length <= 0)
            return;

        var branch = text.Substring(start, length);
        var noteStart = FindNoteStart(branch);
        if (noteStart < 0)
            return;

        start += noteStart;
        length -= noteStart;
        branch = branch.Substring(noteStart);

        var isTouch = branch[0] is 'A' or 'B' or 'C' or 'D' or 'E'
            or 'a' or 'b' or 'c' or 'd' or 'e';
        var pathIndex = FindSlidePath(branch);
        var isSlide = pathIndex >= 0;
        var breakIndexes = FindModifierIndexes(branch, 'b');
        var monoIndexes = FindModifierIndexes(branch, 'm');
        var isBreakSlide = isSlide && breakIndexes.Any(index =>
            index == branch.Length - 1 ||
            index + 1 < branch.Length && branch[index + 1] == '[');
        var isBreakNote = !isSlide && breakIndexes.Count > 0;

        var isCompactEach = !isSlide && Regex.IsMatch(branch, @"^[1-8]{2}$");
        var brush = isBreakSlide || isBreakNote
            ? BreakBrush
            : isEach || isCompactEach
                ? EachBrush
                : isSlide
                    ? SlideBrush
                    : isTouch
                        ? TouchBrush
                        : TapBrush;

        Apply(line, start, length, brush, occupied);

        // A b before the slide path modifies only the star head. The slide
        // remains blue/yellow, while the modifier itself stays break orange.
        if (isSlide && !isBreakSlide)
            foreach (var index in breakIndexes)
                Apply(line, start + index, 1, BreakBrush, occupied, true);

        // Monochrome is the final visual modifier and therefore overrides b.
        if (monoIndexes.Count > 0)
        {
            var monoHeadOnly = isSlide && monoIndexes.All(index => index < pathIndex);
            if (monoHeadOnly)
                Apply(line, start, pathIndex, MonoBrush, occupied, true);
            else
                Apply(line, start, length, MonoBrush, occupied, true);

            foreach (var index in breakIndexes)
                if (!monoHeadOnly || index < pathIndex)
                    Apply(line, start + index, 1, MonoBrush, occupied, true);
        }
    }

    private static int FindNoteStart(string text)
    {
        var angleDepth = 0;
        var roundDepth = 0;
        var braceDepth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && IsAlphaTokenStart(text, i))
            {
                angleDepth++;
                continue;
            }
            if (text[i] == '>' && angleDepth > 0)
            {
                angleDepth--;
                continue;
            }
            if (angleDepth == 0 && text[i] == '(')
            {
                roundDepth++;
                continue;
            }
            if (angleDepth == 0 && text[i] == ')' && roundDepth > 0)
            {
                roundDepth--;
                continue;
            }
            if (angleDepth == 0 && text[i] == '{')
            {
                braceDepth++;
                continue;
            }
            if (angleDepth == 0 && text[i] == '}' && braceDepth > 0)
            {
                braceDepth--;
                continue;
            }
            if (angleDepth == 0 && roundDepth == 0 && braceDepth == 0 &&
                (text[i] is >= '1' and <= '8' ||
                                    text[i] is 'A' or 'B' or 'C' or 'D' or 'E'
                                        or 'a' or 'b' or 'c' or 'd' or 'e'))
                return i;
        }
        return -1;
    }

    private static int FindSlidePath(string text)
    {
        var bracketDepth = 0;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '<' && IsAlphaTokenStart(text, i))
            {
                var close = text.IndexOf('>', i + 1);
                if (close < 0)
                    return -1;
                i = close;
                continue;
            }
            if (text[i] == '[')
            {
                bracketDepth++;
                continue;
            }
            if (text[i] == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }
            if (bracketDepth == 0 && text[i] is '-' or '^' or 'v' or '<' or '>' or 'V'
                    or 'p' or 'q' or 'r' or 's' or 'z' or 'w')
                return i;
        }
        return -1;
    }

    private static List<int> FindModifierIndexes(string text, char modifier)
    {
        var result = new List<int>();
        var bracketDepth = 0;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '<' && IsAlphaTokenStart(text, i))
            {
                var close = text.IndexOf('>', i + 1);
                if (close < 0)
                    break;
                i = close;
                continue;
            }
            if (text[i] == '[')
            {
                bracketDepth++;
                continue;
            }
            if (text[i] == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }
            if (bracketDepth == 0 && char.ToLowerInvariant(text[i]) == modifier)
                result.Add(i);
        }
        return result;
    }

    private static List<(int Start, int Length)> SplitRanges(string text, params char[] separators)
    {
        var result = new List<(int, int)>();
        var start = 0;
        var squareDepth = 0;
        var angleDepth = 0;
        var roundDepth = 0;
        var braceDepth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': squareDepth++; break;
                case ']': squareDepth = Math.Max(0, squareDepth - 1); break;
                case '<' when IsAlphaTokenStart(text, i): angleDepth++; break;
                case '>': angleDepth = Math.Max(0, angleDepth - 1); break;
                case '(': roundDepth++; break;
                case ')': roundDepth = Math.Max(0, roundDepth - 1); break;
                case '{': braceDepth++; break;
                case '}': braceDepth = Math.Max(0, braceDepth - 1); break;
            }

            if (squareDepth == 0 && angleDepth == 0 && roundDepth == 0 && braceDepth == 0 &&
                separators.Contains(text[i]))
            {
                result.Add((start, i - start));
                start = i + 1;
            }
        }
        result.Add((start, text.Length - start));
        return result;
    }

    private void ApplyMatches(
        DocumentLine line,
        string text,
        Regex regex,
        SolidColorBrush brush,
        bool[] occupied,
        bool overwrite = false)
    {
        foreach (Match match in regex.Matches(text))
            Apply(line, match.Index, match.Length, brush, occupied, overwrite);
    }

    private void Apply(
        DocumentLine line,
        int start,
        int length,
        SolidColorBrush brush,
        bool[] occupied,
        bool overwrite = false)
    {
        if (length <= 0 || start < 0 || start + length > occupied.Length)
            return;
        if (!overwrite)
        {
            var cursor = start;
            var end = start + length;
            while (cursor < end)
            {
                while (cursor < end && occupied[cursor])
                    cursor++;
                var partStart = cursor;
                while (cursor < end && !occupied[cursor])
                    cursor++;
                if (cursor > partStart)
                    ApplyPart(line, partStart, cursor - partStart, brush, occupied);
            }
            return;
        }

        ApplyPart(line, start, length, brush, occupied);
    }

    private void ApplyPart(
        DocumentLine line,
        int start,
        int length,
        SolidColorBrush brush,
        bool[] occupied)
    {
        Array.Fill(occupied, true, start, length);
        ChangeLinePart(
            line.Offset + start,
            line.Offset + start + length,
            element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                element.TextRunProperties.SetTypeface(new Typeface(
                    element.TextRunProperties.Typeface.FontFamily,
                    element.TextRunProperties.Typeface.Style,
                    FontWeights.SemiBold,
                    element.TextRunProperties.Typeface.Stretch));
            });
    }

    private static bool IsAlphaTokenStart(string text, int index)
    {
        if (index + 2 >= text.Length || !char.IsLetter(text[index + 1]))
            return false;
        var close = text.IndexOf('>', index + 1);
        var star = text.IndexOf('*', index + 1);
        return close > index && star > index && star < close;
    }

    private static void TrimRange(string text, ref int start, ref int length)
    {
        while (length > 0 && char.IsWhiteSpace(text[start]))
        {
            start++;
            length--;
        }
        while (length > 0 && char.IsWhiteSpace(text[start + length - 1]))
            length--;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
