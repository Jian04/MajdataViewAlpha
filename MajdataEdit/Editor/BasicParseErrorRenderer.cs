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
        foreach (var line in zeroBasedLines)
            if (line >= 0)
                errorMessages[line] = "谱面语句无法解析";
        Invalidate();
    }

    public void SetErrors(IEnumerable<(int Line, string Message)> errors)
    {
        errorMessages.Clear();
        foreach (var (line, message) in errors)
            if (line >= 0)
                errorMessages[line] = string.IsNullOrWhiteSpace(message)
                    ? "谱面语句无法解析"
                    : message;
        Invalidate();
    }

    public void Clear()
    {
        if (errorMessages.Count == 0)
            return;
        errorMessages.Clear();
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

            var segment = new ErrorSegment(documentLine.Offset, Math.Max(1, documentLine.Length));
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

    private readonly record struct ErrorSegment(int Offset, int Length) : ISegment
    {
        public int EndOffset => Offset + Length;
    }
}
