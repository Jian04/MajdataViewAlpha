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
    private static readonly SolidColorBrush DarkBorrowBrush = Brush("#E8935A");
    private static readonly SolidColorBrush LightBpmBrush = Brush("#16825D");
    private static readonly SolidColorBrush LightBeatBrush = Brush("#9A6700");
    private static readonly SolidColorBrush LightDurationBrush = Brush("#0969DA");
    private static readonly SolidColorBrush LightDimBrush = Brush("#6E7781");
    private static readonly SolidColorBrush LightMarkerBrush = Brush("#007A78");
    private static readonly SolidColorBrush LightCommentBrush = Brush("#6A737D");
    private static readonly SolidColorBrush LightAlphaBrush = Brush("#A626A4");
    private static readonly SolidColorBrush LightBorrowBrush = Brush("#BC4C00");

    private static bool IsLightTheme => ThemeManager.CurrentIsLight;
    private static SolidColorBrush BpmBrush => IsLightTheme ? LightBpmBrush : DarkBpmBrush;
    private static SolidColorBrush BeatBrush => IsLightTheme ? LightBeatBrush : DarkBeatBrush;
    private static SolidColorBrush DurationBrush => IsLightTheme ? LightDurationBrush : DarkDurationBrush;
    private static SolidColorBrush DimBrush => IsLightTheme ? LightDimBrush : DarkDimBrush;
    private static SolidColorBrush MarkerBrush => IsLightTheme ? LightMarkerBrush : DarkMarkerBrush;
    private static SolidColorBrush CommentBrush => IsLightTheme ? LightCommentBrush : DarkCommentBrush;
    private static SolidColorBrush AlphaBrush => IsLightTheme ? LightAlphaBrush : DarkAlphaBrush;
    private static SolidColorBrush BorrowBrush => IsLightTheme ? LightBorrowBrush : DarkBorrowBrush;

    private readonly List<TextSpan> markerSpans = new();
    private readonly List<TextSpan> commentSpans = new();
    private readonly List<TextSpan> alphaSpans = new();
    private readonly List<TextSpan> borrowSpans = new();
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
        ApplyForegroundSpans(line, borrowSpans, BorrowBrush);
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
        borrowSpans.Clear();

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

            // Only a token the command grammar recognizes is painted as a command,
            // so a colored "<...>" means the editor understood it. A half-typed or
            // misspelled one stays plain and leaves the squiggle as the only signal.
            if (text[i] == '<' &&
                MajdataCore.AlphaCommandBoundary.TryGetToken(text, i, out var command) &&
                command.isKnown)
            {
                alphaSpans.Add(new TextSpan(command.start, command.length));
                i = command.start + command.length;
                continue;
            }

            // All inheritance suffixes share one colour. The carrier remains a
            // normal note token; only the inherited value starts at '~'.
            if (text[i] == '~' &&
                MajdataCore.SlidePathParser.TryReadTrajectoryBorrow(
                    text, i, out _, out var afterBorrow) &&
                text.IndexOf('\n', i, afterBorrow - i) < 0)
            {
                borrowSpans.Add(new TextSpan(i, afterBorrow - i));
                i = afterBorrow;
                continue;
            }

            if (text[i] == '~')
            {
                var inheritedPosition = new MajdataCore.SlidePositionData
                {
                    area = 'E',
                    position = 1
                };
                if (MajdataCore.SlidePathParser.TryReadRadiusOverride(
                        text, i, inheritedPosition, out var afterInheritance) ==
                    MajdataCore.RadiusOverrideResult.Applied &&
                    text.IndexOf('\n', i, afterInheritance - i) < 0)
                {
                    borrowSpans.Add(new TextSpan(i, afterInheritance - i));
                    i = afterInheritance;
                    continue;
                }
            }

            if (MajdataCore.EditorDirectiveScanner.TryRead(text, i, out var directive) &&
                directive.length > 0)
            {
                if (directive.kind == MajdataCore.EditorDirectiveKind.Overlay &&
                    directive.length >= 4 &&
                    directive.start + 1 < text.Length &&
                    text[directive.start + 1] == '*')
                {
                    markerSpans.Add(new TextSpan(directive.start, 2));
                    markerSpans.Add(new TextSpan(
                        directive.start + directive.length - 2, 2));
                    // The scanner returns the whole overlay block as one directive.
                    // Only skip its opening marker so commands and inheritance
                    // expressions inside the block still receive syntax colours.
                    i = directive.start + 2;
                    continue;
                }
                else
                {
                    markerSpans.Add(new TextSpan(directive.start, directive.length));
                }
                i = directive.start + directive.length;
                continue;
            }

            i++;
        }
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
