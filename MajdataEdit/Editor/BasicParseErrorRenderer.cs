using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

internal sealed class BasicParseErrorRenderer : IBackgroundRenderer
{
    private readonly TextEditor editor;
    private readonly Pen wavePen = new(new SolidColorBrush(Color.FromRgb(235, 55, 65)), 1.35);
    private readonly Brush lineFill = new SolidColorBrush(Color.FromArgb(32, 235, 55, 65));
    private readonly Dictionary<int, string> errorMessages = new();
    // Column the beat starts at, used to narrow the squiggle to the bad token.
    private readonly Dictionary<int, int> errorColumns = new();

    private static string DefaultMessage =>
        MainWindow.GetLocalizedString("ChartStatementInvalid");

    public BasicParseErrorRenderer(TextEditor editor)
    {
        this.editor = editor;
        wavePen.Freeze();
        lineFill.Freeze();
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetErrors(IEnumerable<int> zeroBasedLines)
    {
        errorMessages.Clear();
        errorColumns.Clear();
        foreach (var line in zeroBasedLines)
            if (line >= 0)
                errorMessages[line] = DefaultMessage;
        Invalidate();
    }

    public void SetErrors(IEnumerable<(int Line, string Message)> errors)
    {
        errorMessages.Clear();
        errorColumns.Clear();
        foreach (var (line, message) in errors)
            if (line >= 0)
                errorMessages[line] = string.IsNullOrWhiteSpace(message)
                    ? DefaultMessage
                    : message;
        Invalidate();
    }

    public void SetErrors(IEnumerable<(int Line, int Column, string Message)> errors)
    {
        errorMessages.Clear();
        errorColumns.Clear();
        foreach (var (line, column, message) in errors)
        {
            if (line < 0)
                continue;
            errorMessages[line] = string.IsNullOrWhiteSpace(message)
                ? DefaultMessage
                : message;
            errorColumns[line] = Math.Max(0, column);
        }
        Invalidate();
    }

    public void Clear()
    {
        if (errorMessages.Count == 0)
            return;
        errorMessages.Clear();
        errorColumns.Clear();
        Invalidate();
    }

    public string? GetMessageForLine(int zeroBasedLine) =>
        errorMessages.TryGetValue(zeroBasedLine, out var message) ? message : null;

    public bool IsErrorLine(int zeroBasedLine) => errorMessages.ContainsKey(zeroBasedLine);
    public bool HasErrors => errorMessages.Count > 0;

    private void Invalidate()
    {
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (editor.Document == null || errorMessages.Count == 0)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            var documentLine = visualLine.FirstDocumentLine;
            if (!IsErrorLine(documentLine.LineNumber - 1))
                continue;

            var lineTop = visualLine.VisualTop - textView.ScrollOffset.Y;
            drawingContext.DrawRectangle(lineFill, null,
                new System.Windows.Rect(0d, lineTop, textView.ActualWidth, visualLine.Height));

            var segment = GetTokenSegment(documentLine);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                var right = Math.Max(rect.Left + 4d, rect.Right);
                var baseline = rect.Bottom - 1.5d;
                var up = true;
                for (var x = rect.Left; x < right; x += 2d)
                {
                    var next = Math.Min(right, x + 2d);
                    drawingContext.DrawLine(
                        wavePen,
                        new System.Windows.Point(x, baseline + (up ? 0d : 1.5d)),
                        new System.Windows.Point(next, baseline + (up ? 1.5d : 0d)));
                    up = !up;
                }
            }
        }
    }

    /// <summary>
    /// The whole line is tinted, but the squiggle covers only the beat that failed:
    /// from its reported column up to the separator that ends it.
    /// </summary>
    private ErrorSegment GetTokenSegment(DocumentLine documentLine)
    {
        var lineLength = documentLine.Length;
        if (lineLength <= 0 ||
            !errorColumns.TryGetValue(documentLine.LineNumber - 1, out var column) ||
            column >= lineLength)
            return new ErrorSegment(documentLine.Offset, Math.Max(1, lineLength));

        var text = editor.Document.GetText(documentLine.Offset, lineLength);
        var end = column;
        var tracker = new MajdataCore.ChartBracketTracker();
        while (end < lineLength)
        {
            var character = text[end];
            if (tracker.IsTopLevel &&
                (character == ',' || char.IsWhiteSpace(character)))
                break;
            tracker.Advance(text, end);
            end++;
        }

        return new ErrorSegment(
            documentLine.Offset + column,
            Math.Max(1, end - column));
    }

    private readonly record struct ErrorSegment(int Offset, int Length) : ISegment
    {
        public int EndOffset => Offset + Length;
    }
}
