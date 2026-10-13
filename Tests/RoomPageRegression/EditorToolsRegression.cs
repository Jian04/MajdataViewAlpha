using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using MajdataEdit;

internal static class EditorToolsRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        System.Runtime.InteropServices.NativeLibrary.Load(System.IO.Path.GetFullPath(".codex-build/ViewBuild/App/MajdataEdit/bass.dll"));
        var main = new MainWindow { editorSetting = new EditorSetting() };
        typeof(MainWindow).GetField("isLoading", Private)!.SetValue(main, true);
        var editor = (TextEditor)main.FindName("FumenContent");
        var mergeTools = (MenuItem)main.FindName("MergeNoteStreamsToolsMenuItem");
        var mergeContext = (MenuItem)main.FindName("MergeNoteStreamsMenuItem");
        var tools = ItemsControl.ItemsControlFromItemContainer(mergeTools);
        void CheckMenu(bool enabled)
        {
            typeof(MainWindow).GetMethod("ToolsMenu_Opened", Private)!.Invoke(main,
                new object[] { tools, new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, tools) });
            typeof(MainWindow).GetMethod("FumenContextMenu_Opened", Private)!.Invoke(main,
                new object?[] { editor.ContextMenu, new RoutedEventArgs() });
            if (mergeTools.IsEnabled != enabled || mergeContext.IsEnabled != enabled)
                throw new Exception("Tools and context merge enablement disagree with caret/selection");
        }
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            editor.Text = "(120){4}," + newline + "@{8}1,2," + newline + "{4}3,";
            editor.Select(0, 0);
            CheckMenu(false);
            editor.Select(editor.Text.IndexOf("1,", StringComparison.Ordinal), 0);
            CheckMenu(true);
            var streamStart = editor.Text.IndexOf('@');
            var streamLength = "@{8}1,2,".Length;
            editor.Select(streamStart, streamLength);
            CheckMenu(true);
            typeof(MainWindow).GetMethod("MergeNoteStreams_MenuItem_Click", Private)!.Invoke(main,
                new object?[] { mergeTools, new RoutedEventArgs() });
            if (editor.Text != "(120){4}," + newline + "{8}1/3,2,")
                throw new Exception("Merge corrupted CRLF positions or the preceding main stream: " + editor.Text);
            editor.Undo();
            if (!editor.Text.Contains("@{8}1,2,")) throw new Exception("Merge did not support undo");
            editor.Select(streamStart, editor.Text.Length - streamStart);
            CheckMenu(false);
        }
        var getTime = typeof(MainWindow).GetMethod("GetCaretTimingTime", Private)!;
        foreach (var width in new[] { 680d, 1200d })
        {
            main.Width = width;
            editor.Text = "{4},,,,Cf/B1/B2/B3/B4/B5/B6/B7/B8,,,,,,,,,,,,,,";
            editor.CaretOffset = editor.Text.Length - 1;
            var before = (double)getTime.Invoke(main, null)!;
            editor.CaretOffset = editor.Text.Length;
            var after = (double)getTime.Invoke(main, null)!;
            if (after <= before) throw new Exception("EOF comma did not advance the real editor marker");
        }
        Console.WriteLine("EDITOR TOOLS PASS: actual WPF menus, LF/CRLF merge and undo, disabled outside streams, EOF marker at both window widths.");
    }
}
