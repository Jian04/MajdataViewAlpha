using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

// Split the extra inter-line spacing equally above and below the glyph box.
internal sealed class GlyphSelectionRenderer : IBackgroundRenderer
{
    private readonly TextEditor editor;
    private Brush brush = Brushes.Transparent;
    public KnownLayer Layer => KnownLayer.Selection;

    private GlyphSelectionRenderer(TextEditor editor)
    {
        this.editor = editor;
        editor.TextArea.SelectionChanged += (_, _) =>
            editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public static void Apply(TextEditor editor, Brush brush)
    {
        var renderers = editor.TextArea.TextView.BackgroundRenderers;
        var renderer = renderers.OfType<GlyphSelectionRenderer>().FirstOrDefault();
        if (renderer == null)
        {
            renderer = new GlyphSelectionRenderer(editor);
            renderers.Add(renderer);
        }
        renderer.brush = brush;
        editor.TextArea.SelectionBrush = Brushes.Transparent;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || editor.TextArea.Selection.IsEmpty) return;
        var height = textView.DefaultLineHeight / 1.2d;
        foreach (var segment in editor.TextArea.Selection.Segments)
        foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            drawingContext.DrawRectangle(brush, null,
                new Rect(rectangle.X, rectangle.Y - height * .1d, rectangle.Width, height * 1.2d));
    }
}
