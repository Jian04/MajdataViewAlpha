using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

internal sealed class SimaiColorizer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush DarkBpmBrush = Brush("#86C88F");
    private static readonly SolidColorBrush DarkBeatBrush = Brush("#D9B84E");
    private static readonly SolidColorBrush DarkDurationBrush = Brush("#6F9FD8");
    private static readonly SolidColorBrush DarkDimBrush = Brush("#8A929E");
    private static readonly SolidColorBrush DarkMarkerBrush = Brush("#4EC9B0");
    private static readonly SolidColorBrush DarkCommentBrush = Brush("#7A828E");
    private static readonly SolidColorBrush DarkAlphaBrush = Brush("#C586C0");
    private static readonly SolidColorBrush LightBpmBrush = Brush("#16825D");
    private static readonly SolidColorBrush LightBeatBrush = Brush("#9A6700");
    private static readonly SolidColorBrush LightDurationBrush = Brush("#0969DA");
    private static readonly SolidColorBrush LightDimBrush = Brush("#6E7781");
    private static readonly SolidColorBrush LightMarkerBrush = Brush("#007A78");
    private static readonly SolidColorBrush LightCommentBrush = Brush("#6A737D");
    private static readonly SolidColorBrush LightAlphaBrush = Brush("#A626A4");

    private static bool IsLightTheme =>
        string.Equals(ThemeManager.CurrentTheme.name, "light", StringComparison.OrdinalIgnoreCase);
    private static SolidColorBrush BpmBrush => IsLightTheme ? LightBpmBrush : DarkBpmBrush;
    private static SolidColorBrush BeatBrush => IsLightTheme ? LightBeatBrush : DarkBeatBrush;
    private static SolidColorBrush DurationBrush => IsLightTheme ? LightDurationBrush : DarkDurationBrush;
    private static SolidColorBrush DimBrush => IsLightTheme ? LightDimBrush : DarkDimBrush;
    private static SolidColorBrush MarkerBrush => IsLightTheme ? LightMarkerBrush : DarkMarkerBrush;
    private static SolidColorBrush CommentBrush => IsLightTheme ? LightCommentBrush : DarkCommentBrush;
    private static SolidColorBrush AlphaBrush => IsLightTheme ? LightAlphaBrush : DarkAlphaBrush;

    private readonly List<TextSpan> markerSpans = new();
    private readonly List<TextSpan> commentSpans = new();
    private readonly List<TextSpan> alphaSpans = new();
    private object? cachedVersion;

    protected override void ColorizeLine(DocumentLine line)
    {
        RefreshDocumentSpans();

        var text = CurrentContext.Document.GetText(line);
        var state = 0;
        var spanStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var nextState = state;
            SolidColorBrush? oneCharBrush = null;

            if (ch == '(')
                nextState = 1;
            else if (ch == ')')
                nextState = 0;
            else if (ch == '{')
                nextState = 2;
            else if (ch == '}')
                nextState = 0;
            else if (ch == '[')
                nextState = 3;
            else if (ch == ']')
                nextState = 0;
            else if (state == 0 && ch == ',')
                oneCharBrush = DimBrush;

            var currentBrush = BrushForState(state);
            var newBrush = BrushForState(nextState);

            if (oneCharBrush != null)
            {
                ApplyStateSpan(line, spanStart, i, currentBrush);
                Apply(line, i, 1, oneCharBrush);
                spanStart = i + 1;
                state = nextState;
                continue;
            }

            if (nextState != state)
            {
                ApplyStateSpan(line, spanStart, i, currentBrush);
                spanStart = i;
                state = nextState;

                if (ch is ')' or '}' or ']')
                {
                    if (currentBrush != null)
                        Apply(line, i, 1, currentBrush);
                    spanStart = i + 1;
                }
                else if (newBrush != null)
                {
                    Apply(line, i, 1, newBrush);
                    spanStart = i + 1;
                }
            }
        }

        ApplyStateSpan(line, spanStart, text.Length, BrushForState(state));
        ApplyForegroundSpans(line, alphaSpans, AlphaBrush);
        ApplyForegroundSpans(line, markerSpans, MarkerBrush);
        ApplyForegroundSpans(line, commentSpans, CommentBrush);
    }

    private void RefreshDocumentSpans()
    {
        var document = CurrentContext.Document;
        if (ReferenceEquals(cachedVersion, document.Version))
            return;

        cachedVersion = document.Version;
        markerSpans.Clear();
        commentSpans.Clear();
        alphaSpans.Clear();

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

            if (text[i] == '<' && AlphaCommandBoundary.IsPotentialStart(text, i))
            {
                var end = FindAlphaTokenEnd(text, i);
                alphaSpans.Add(new TextSpan(i, end - i));
                i = end;
                continue;
            }

            if (text[i] is '@' or '&' && TryReadEditorMarker(text, i, out var length))
            {
                markerSpans.Add(new TextSpan(i, length));
                i += length;
                continue;
            }

            i++;
        }
    }

    private static int FindAlphaTokenEnd(string text, int start)
    {
        var end = start + 2;
        while (end < text.Length)
        {
            var ch = text[end];
            if (ch == '>')
                return end + 1;
            if (ch is '\r' or '\n' or ';' or '\uFF1B')
                return end;
            end++;
        }
        return end;
    }

    private static bool TryReadEditorMarker(string text, int start, out int length)
    {
        length = 0;
        if (start < 0 || start >= text.Length || text[start] is not ('@' or '&'))
            return false;

        if (text[start] == '@')
        {
            if (start + 2 < text.Length && text[start + 1] == '{')
            {
                var close = text.IndexOf('}', start + 2);
                if (close > start + 2 &&
                    text.Substring(start + 2, close - start - 2).All(char.IsDigit))
                {
                    length = close - start + 1;
                    return true;
                }
            }

            foreach (var marker in new[] { "start", "end" })
            {
                if (start + marker.Length + 1 <= text.Length &&
                    string.Equals(text.Substring(start + 1, marker.Length), marker,
                        StringComparison.OrdinalIgnoreCase))
                {
                    length = marker.Length + 1;
                    return true;
                }
            }
        }

        if (start + 3 < text.Length && char.IsDigit(text[start + 1]))
        {
            var slash = text.IndexOf('/', start + 1);
            if (slash > start + 1 && slash + 1 < text.Length && char.IsDigit(text[slash + 1]))
            {
                var end = slash + 1;
                while (end < text.Length && char.IsDigit(text[end]))
                    end++;
                length = end - start;
                return true;
            }
        }

        if (start + 5 <= text.Length &&
            string.Equals(text.Substring(start + 1, 4), "NULL", StringComparison.OrdinalIgnoreCase))
        {
            length = 5;
            return true;
        }

        if (start + 7 > text.Length)
            return false;

        var candidate = text.Substring(start + 1, 6);
        if (!candidate.All(Uri.IsHexDigit))
            return false;

        length = 7;
        return true;
    }

    private static SolidColorBrush? BrushForState(int state) => state switch
    {
        1 => BpmBrush,
        2 => BeatBrush,
        3 => DurationBrush,
        _ => null
    };

    private void ApplyStateSpan(DocumentLine line, int start, int end, SolidColorBrush? brush)
    {
        if (brush != null && end > start)
            Apply(line, start, end - start, brush);
    }

    private void ApplyForegroundSpans(DocumentLine line, IEnumerable<TextSpan> spans, SolidColorBrush brush)
    {
        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;
        foreach (var span in spans)
        {
            var start = Math.Max(lineStart, span.Start);
            var end = Math.Min(lineEnd, span.Start + span.Length);
            if (end > start)
                Apply(line, start - lineStart, end - start, brush);
        }
    }

    private void Apply(DocumentLine line, int start, int length, SolidColorBrush brush)
    {
        if (length <= 0)
            return;

        ChangeLinePart(
            line.Offset + start,
            line.Offset + start + length,
            element => element.TextRunProperties.SetForegroundBrush(brush));
    }

    private readonly record struct TextSpan(int Start, int Length);

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
