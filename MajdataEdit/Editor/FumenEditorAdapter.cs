using ICSharpCode.AvalonEdit;
using System.Windows.Input;

namespace MajdataEdit.Editor;

internal readonly record struct EditorSelection(int Start, int Length);

internal sealed class FumenEditorAdapter
{
    private readonly TextEditor editor;

    public FumenEditorAdapter(TextEditor editor)
    {
        this.editor = editor;
        editor.Options.AllowScrollBelowDocument = true;
        // AvalonEdit normally maps a click below the document to the last line's
        // mouse column. Correct the drag anchor after its selection handler runs.
        editor.TextArea.AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None &&
                Mouse.Captured == editor.TextArea)
                MoveCaretBelowDocument(e.GetPosition(editor.TextArea.TextView).Y);
        }), true);
    }

    internal bool MoveCaretBelowDocument(double viewY)
    {
        var view = editor.TextArea.TextView;
        if (viewY < 0 || viewY >= view.ActualHeight || viewY + view.ScrollOffset.Y < view.DocumentHeight)
            return false;
        editor.TextArea.ClearSelection();
        editor.CaretOffset = editor.Document.TextLength;
        return true;
    }

    internal bool ZoomWithWheel(int delta, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0 || delta == 0)
            return false;
        editor.FontSize = Math.Clamp(editor.FontSize + delta / 120d, 8d, 32d);
        return true;
    }

    public string Text
    {
        get => editor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        set => editor.Text = value ?? string.Empty;
    }

    public int CaretOffset
    {
        get => RawToNormalizedOffset(editor.CaretOffset);
        set => editor.CaretOffset = NormalizedToRawOffset(value);
    }

    public int CurrentLine => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;
    public int CurrentColumn
    {
        get
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            return editor.CaretOffset - line.Offset;
        }
    }
    public string SelectedText => editor.SelectedText;
    public bool HasSelection => editor.SelectionLength > 0;
    public EditorSelection Selection
    {
        get
        {
            var start = RawToNormalizedOffset(editor.SelectionStart);
            var end = RawToNormalizedOffset(editor.SelectionStart + editor.SelectionLength);
            return new EditorSelection(start, end - start);
        }
    }

    public void Focus() => editor.Focus();

    public bool Undo()
    {
        if (!editor.Document.UndoStack.CanUndo)
            return false;

        editor.Document.UndoStack.Undo();
        return true;
    }

    public void ReplaceSelection(string text)
    {
        var start = editor.SelectionStart;
        editor.Document.Replace(start, editor.SelectionLength, text ?? string.Empty);
        editor.Select(start, text?.Length ?? 0);
    }

    public void ReplaceAll(string text)
    {
        editor.Document.Replace(0, editor.Document.TextLength, text ?? string.Empty);
        editor.CaretOffset = 0;
        editor.ScrollToHome();
    }

    public void Select(int start, int length)
    {
        var normalizedLength = Text.Length;
        start = Math.Clamp(start, 0, normalizedLength);
        length = Math.Clamp(length, 0, normalizedLength - start);
        var rawStart = NormalizedToRawOffset(start);
        var rawEnd = NormalizedToRawOffset(start + length);
        editor.Select(rawStart, rawEnd - rawStart);
        editor.CaretOffset = rawEnd;
        var caretLine = editor.Document.GetLineByOffset(rawEnd);
        var caretColumn = rawEnd - caretLine.Offset + 1;
        editor.ScrollTo(caretLine.LineNumber, caretColumn);
        editor.TextArea.Caret.BringCaretToView();
    }

    public void SelectLineColumn(int line, int column)
    {
        if (editor.Document.LineCount == 0)
            return;

        line = Math.Clamp(line + 1, 1, editor.Document.LineCount);
        var documentLine = editor.Document.GetLineByNumber(line);
        var offset = documentLine.Offset + Math.Clamp(column, 0, documentLine.Length);
        // DocumentLine.Offset includes CRLF; Select accepts normalized LF offsets.
        // Converting only once prevents a growing drift on later chart lines.
        Select(RawToNormalizedOffset(offset), 0);
    }

    public int FindNext(string text)
    {
        if (string.IsNullOrEmpty(text))
            return -1;

        var source = Text;
        var start = Math.Clamp(CaretOffset, 0, source.Length);
        var found = source.IndexOf(text, start, StringComparison.CurrentCultureIgnoreCase);
        if (found < 0 && start > 0)
            found = source.IndexOf(text, 0, start, StringComparison.CurrentCultureIgnoreCase);
        return found;
    }

    internal int RawToNormalizedOffset(int rawOffset)
    {
        var raw = editor.Text;
        rawOffset = Math.Clamp(rawOffset, 0, raw.Length);
        var normalized = 0;
        for (var i = 0; i < rawOffset; i++)
        {
            if (raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                continue;
            normalized++;
        }
        return normalized;
    }

    internal int NormalizedToRawOffset(int normalizedOffset)
    {
        var raw = editor.Text;
        normalizedOffset = Math.Clamp(normalizedOffset, 0, Text.Length);
        var normalized = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (normalized == normalizedOffset)
                return i;
            if (raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                continue;
            normalized++;
        }
        return raw.Length;
    }
}
