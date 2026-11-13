using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MajdataEdit.Editor;

internal sealed class WrappedLineSpacing : VisualLineElementGenerator
{
    private const double ContinuationRatio = .85;

    internal static FontFamily CreateFont(FontFamily source)
    {
        var font = new FontFamily
        {
            LineSpacing = source.LineSpacing * 1.2 * ContinuationRatio,
            Baseline = source.Baseline
        };
        font.FamilyMaps.Add(new FontFamilyMap
        {
            Target = source.BaseUri == null ? source.Source : new Uri(source.BaseUri, source.Source).AbsoluteUri
        });
        return font;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.LastDocumentLine;
        return startOffset <= line.EndOffset ? line.EndOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var view = CurrentContext.TextView;
        // Use the same source line boundary height in both modes. Only continuation rows stay compact.
        var spacer = new Border { Width = 0, Height = view.DefaultLineHeight / ContinuationRatio, IsHitTestVisible = false };
        TextBlock.SetBaselineOffset(spacer, view.DefaultBaseline);
        return new LineEndSpace(spacer);
    }

    private sealed class LineEndSpace : InlineObjectElement
    {
        internal LineEndSpace(Border spacer) : base(0, spacer) { }

        public override bool HandlesLineBorders => true;

        public override int GetNextCaretPosition(int column, LogicalDirection direction, CaretPositioningMode mode)
        {
            var reachesEnd = direction == LogicalDirection.Forward ? column < VisualColumn : column > VisualColumn;
            return reachesEnd ? VisualColumn : -1;
        }
    }
}
