using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using MajdataCollaboration;

namespace MajdataEdit.Editor;

internal sealed class RemoteCaretRenderer : IBackgroundRenderer
{
    private readonly TextEditor editor;
    private readonly Func<int> difficulty;
    private RoomPresence[] peers = Array.Empty<RoomPresence>();
    public RemoteCaretRenderer(TextEditor editor, Func<int> difficulty) { this.editor = editor; this.difficulty = difficulty; }
    public KnownLayer Layer => KnownLayer.Selection;
    public void Update(RoomPresence[] value) { peers = value; editor.TextArea.TextView.InvalidateLayer(Layer); }
    public void Draw(TextView textView, DrawingContext context)
    {
        if (!textView.VisualLinesValid || editor.Document == null) return;
        foreach (var peer in peers.Where(p => p.Difficulty == difficulty()))
        {
            var location = editor.Document.GetLocation(Math.Clamp(peer.Offset, 0, editor.Document.TextLength));
            var visualLine = textView.GetVisualLine(location.Line);
            if (visualLine == null) continue;
            var position = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineTop) - textView.ScrollOffset;
            var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(peer.Color));
            var label = new FormattedText(peer.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 10, color, VisualTreeHelper.GetDpi(editor).PixelsPerDip);
            context.DrawLine(new Pen(color, 2), position, new Point(position.X, position.Y + textView.DefaultLineHeight));
            context.DrawText(label, new Point(position.X + 3, position.Y - label.Height));
        }
    }
}
