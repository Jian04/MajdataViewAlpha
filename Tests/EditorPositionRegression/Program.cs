using ICSharpCode.AvalonEdit;
using MajdataEdit.Editor;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var checks = 0;
        {
            var editor = new TextEditor { FontSize = 16, Text = "{4}1,2," };
            var adapter = new FumenEditorAdapter(editor);
            editor.Select(4, 3);
            if (adapter.ZoomWithWheel(120, System.Windows.Input.ModifierKeys.None) || editor.FontSize != 16)
                throw new Exception("Ordinary wheel must not zoom the editor");
            if (!adapter.ZoomWithWheel(120, System.Windows.Input.ModifierKeys.Control) || editor.FontSize != 17 ||
                !adapter.ZoomWithWheel(-120, System.Windows.Input.ModifierKeys.Control) || editor.FontSize != 16)
                throw new Exception("Ctrl+wheel must zoom in and out");
            adapter.ZoomWithWheel(12000, System.Windows.Input.ModifierKeys.Control);
            if (editor.FontSize != 32) throw new Exception("Editor zoom upper bound");
            adapter.ZoomWithWheel(-12000, System.Windows.Input.ModifierKeys.Control);
            if (editor.FontSize != 8 || editor.SelectionStart != 4 || editor.SelectionLength != 3 ||
                editor.Text != "{4}1,2," || editor.Document.UndoStack.CanUndo)
                throw new Exception("Zoom must preserve chart text, selection and undo state");
            checks += 4;
        }
        {
            var editor = new TextEditor();
            var adapter = new FumenEditorAdapter(editor);
            adapter.Text = "{4}1,2,\n3,4,";
            var host = new System.Windows.Window { Content = editor, Width = 320, Height = 200,
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            host.Show(); host.UpdateLayout();
            var view = editor.TextArea.TextView;
            view.EnsureVisualLines();
            if (!editor.Options.AllowScrollBelowDocument || view.DocumentHeight <= 0 ||
                !adapter.MoveCaretBelowDocument(view.DocumentHeight + 5) || editor.CaretOffset != editor.Document.TextLength)
                throw new Exception($"Blank space below last line: document={view.DocumentHeight}, viewport={view.ActualHeight}, offset={view.ScrollOffset.Y}, caret={editor.CaretOffset}/{editor.Document.TextLength}");
            var original = editor.Text;
            editor.TextArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(editor.TextArea, editor.CaretOffset, 0);
            editor.SelectedText = "";
            if (editor.Text.Length != 0 || !adapter.Undo() || editor.Text != original)
                throw new Exception("Selection from below the last line must include the complete final line and support undo");
            if (adapter.MoveCaretBelowDocument(0) || editor.Text != original)
                throw new Exception("Normal document clicks must retain AvalonEdit selection behavior");
            checks += 3;
            host.Close();
        }
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        {
            var editor = new TextEditor();
            var adapter = new FumenEditorAdapter(editor);
            var lines = Enumerable.Range(0, 160).Select(i => $"{{4}}1,2,3,4, || {i}").ToArray();
            adapter.Text = string.Join(newline, lines);
            for (var line = 0; line < lines.Length; line++)
            {
                foreach (var column in new[] { 0, 4, 6, 8, 10 })
                {
                    adapter.SelectLineColumn(line, column);
                    var expected = lines.Take(line).Sum(s => s.Length + 1) + column;
                    if (adapter.CaretOffset != expected || adapter.CurrentLine != line + 1 || adapter.CurrentColumn != column)
                        throw new Exception($"Caret drift: newline={newline.Length}, line={line}, column={column}, actual={adapter.CaretOffset}, expected={expected}");
                    if (column > 0 && editor.Document.GetCharAt(editor.CaretOffset) != ',')
                        throw new Exception("Slot selection missed its closing comma");
                    if (adapter.NormalizedToRawOffset(adapter.CaretOffset) != editor.CaretOffset)
                        throw new Exception("Caret offset round trip failed");
                    checks++;
                }
            }
        }
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        {
            var editor = new TextEditor();
            var adapter = new FumenEditorAdapter(editor);
            var source = string.Join(newline, new[] { "(190){8}1,2,3,4,", "5,6,7,8," });
            adapter.Text = source;
            // Keep a previous edit on the stack: formatting must neither clear
            // that edit nor merge with it, and one undo must restore the chart.
            editor.Document.Insert(editor.Document.TextLength, "E");
            var before = editor.Text;
            var formatted = BeatFormatBrush.Transform(adapter.Text, 16);
            adapter.ReplaceAll(formatted);
            if (editor.Text == before || !adapter.Undo() || editor.Text != before)
                throw new Exception("Whole-chart format cannot be undone in one step");
            editor.Document.UndoStack.Redo();
            if (editor.Text != formatted)
                throw new Exception("Whole-chart format cannot be redone");
            adapter.Undo();
            var organized = ChartOrganizer.Organize(adapter.Text, false);
            adapter.ReplaceAll(organized);
            if (editor.Text == before || !adapter.Undo() || editor.Text != before)
                throw new Exception("Auto-organize cannot be undone in one step");
            if (!adapter.Undo() || editor.Text != source)
                throw new Exception("Formatting erased previous typing history");
            checks += 4;
        }
        Console.WriteLine($"EDITOR POSITION PASS: {checks} real AvalonEdit slot selections, LF/CRLF/CR, 160 lines each.");
    }
}
