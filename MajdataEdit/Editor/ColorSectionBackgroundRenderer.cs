using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

internal sealed class ColorSectionBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextEditor editor;
    private readonly Dictionary<string, SolidColorBrush> brushes =
        new(StringComparer.OrdinalIgnoreCase);

    public ColorSectionBackgroundRenderer(TextEditor editor)
    {
        this.editor = editor;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = editor.Document;
        if (document == null || textView.VisualLines.Count == 0)
            return;

        var sections = ParseSections(document.Text).ToList();
        if (sections.Count == 0)
            return;

        foreach (var line in textView.VisualLines)
        {
            var documentLine = line.FirstDocumentLine;
            var lineStart = documentLine.Offset;
            var lineEnd = documentLine.EndOffset;
            var color = FindLineColor(sections, lineStart, lineEnd);
            if (string.IsNullOrEmpty(color))
                continue;

            var top = line.VisualTop - textView.ScrollOffset.Y;
            var height = Math.Max(line.Height, documentLine.TotalLength > 0 ? line.Height : textView.DefaultLineHeight);
            drawingContext.DrawRectangle(
                GetBrush(color),
                null,
                new System.Windows.Rect(0, top, textView.ActualWidth, height));
        }
    }

    private static string FindLineColor(List<ColorSection> sections, int lineStart, int lineEnd)
    {
        foreach (var section in sections)
        {
            var start = section.Start;
            var end = section.Start + section.Length;
            if (start <= lineEnd && end >= lineStart)
                return section.Color;
        }
        return "";
    }

    private static IEnumerable<ColorSection> ParseSections(string text)
    {
        var activeColor = "";
        var sectionStart = 0;
        for (var i = 0; i < text.Length;)
        {
            if (!TryReadTint(text, i, out var color, out var length))
            {
                i++;
                continue;
            }

            if (!string.IsNullOrEmpty(activeColor) && i > sectionStart)
                yield return new ColorSection(sectionStart, i - sectionStart, activeColor);

            activeColor = color;
            sectionStart = string.IsNullOrEmpty(color) ? i + length : i;
            i += length;
        }

        if (!string.IsNullOrEmpty(activeColor) && sectionStart < text.Length)
            yield return new ColorSection(sectionStart, text.Length - sectionStart, activeColor);
    }

    private SolidColorBrush GetBrush(string hex)
    {
        if (brushes.TryGetValue(hex, out var brush))
            return brush;

        var color = (Color)ColorConverter.ConvertFromString("#" + hex);
        color.A = 34;
        brush = new SolidColorBrush(color);
        brush.Freeze();
        brushes[hex] = brush;
        return brush;
    }

    // An empty color ends the current section; the grammar lives in MajdataCore so
    // the tint and the syntax colors agree on where a marker starts and stops.
    private static bool TryReadTint(
        string text,
        int start,
        out string color,
        out int length)
    {
        color = "";
        length = 0;
        if (!MajdataCore.EditorDirectiveScanner.TryRead(text, start, out var directive))
            return false;

        switch (directive.kind)
        {
            case MajdataCore.EditorDirectiveKind.SectionReset:
                length = directive.length;
                return true;
            case MajdataCore.EditorDirectiveKind.SectionColor:
                color = directive.color;
                length = directive.length;
                return true;
            default:
                return false;
        }
    }

    private readonly record struct ColorSection(int Start, int Length, string Color);
}
