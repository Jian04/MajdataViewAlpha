using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MajdataEdit;

internal static class Reported086Regression
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        var assembly = typeof(MainWindow).Assembly;
        var process = assembly.GetType("MajdataEdit.SimaiProcess")!;
        var main = new MainWindow();
        var setting = Activator.CreateInstance(assembly.GetType("MajdataEdit.EditorSetting")!)!;
        setting.GetType().GetField("SongDetailStyle", All)!.SetValue(setting, 0);
        typeof(MainWindow).GetField("editorSetting", All)!.SetValue(main, setting);
        var selected = typeof(MainWindow).GetField("selectedDifficulty", All)!; selected.SetValue(main, 4);
        var root = Path.GetFullPath("alpha/.codex-build/086-metadata"); Directory.CreateDirectory(root);
        MainWindow.maidataDir = Path.Combine(root, "A");
        process.GetField("title", All)!.SetValue(null, "Song A");
        var recordType = assembly.GetType("MajdataEdit.RecordVideoWindow")!;
        var record = (Window)Activator.CreateInstance(recordType, All, null, new object?[] { main, null }, null)!;
        record.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, record));
        MainWindow.maidataDir = Path.Combine(root, "B");
        process.GetField("title", All)!.SetValue(null, "Song B");
        recordType.GetMethod("SyncSettingsToMain", All)!.Invoke(record, null);
        Check((string)process.GetField("title", All)!.GetValue(null)! == "Song B", "A recording page must not overwrite another chart");
        recordType.GetMethod("ReloadChartInformation", All)!.Invoke(record, null);
        Check(((TextBox)record.FindName("TitleBox")).Text == "Song B", "Recording page must show the new chart");
        selected.SetValue(main, 3);
        ((TextBox)record.FindName("TitleBox")).Text = "Stale difficulty";
        recordType.GetMethod("SyncSettingsToMain", All)!.Invoke(record, null);
        Check((string)process.GetField("title", All)!.GetValue(null)! == "Song B", "Old difficulty metadata must not apply after switching");
        record.Close();
        Check((string)process.GetField("title", All)!.GetValue(null)! == "Song B", "Closing an old recording page must not commit it");
        Console.WriteLine("086 METADATA PASS: stale timer, chart reload, difficulty change and close preserve the active song.");
        var state = assembly.GetType("MajdataEdit.EditorControlMethod")!;
        typeof(MainWindow).GetField("lastEditorState", All)!.SetValue(main, Enum.Parse(state, "Stop"));
        typeof(MainWindow).GetField("lastNotePreviewKey", All)!.SetValue(main, "old preview");
        var watch = Stopwatch.StartNew();
        typeof(MainWindow).GetMethod("ClearStoppedNotePreview", All)!.Invoke(main, null);
        Check(watch.ElapsedMilliseconds < 150, "Clearing an old preview must not wait for HTTP on the UI thread");
        Console.WriteLine("086 DIFFICULTY PASS: clearing the previous preview returns without a blocking HTTP call.");
        MainWindow.maidataDir = "";
        typeof(MainWindow).GetMethod("EnsureToolSidebar", All)!.Invoke(main, null);
        var metadata = (FrameworkElement)typeof(MainWindow).GetField("editorMetadata", All)!.GetValue(main)!;
        Check(metadata.Visibility == Visibility.Collapsed, "Empty editor must hide difficulty/level/offset");
        Console.WriteLine("086 EMPTY UI PASS: metadata hidden until a chart is loaded.");
    }
}
