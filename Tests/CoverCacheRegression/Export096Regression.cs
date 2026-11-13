using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Export096Regression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static void Run()
    {
        var main = new MainWindow { editorSetting = new EditorSetting() };
        var recordMenu = (MenuItem)main.FindName("Menu_ExportRender");
        if (((MenuItem)recordMenu.Parent).Header.ToString() != "导出") throw new Exception("Video export is outside Export menu");
        if (recordMenu.Header.ToString() != "视频…" || ((MenuItem)recordMenu.Parent).Items.OfType<MenuItem>().Any(item => item.Header.ToString()!.StartsWith("导出")))
            throw new Exception("Export menu options repeat the parent label");
        var assembly = typeof(MainWindow).Assembly;
        var gifWindow = (Window)Activator.CreateInstance(typeof(GifExportWindow), All, null, new object[] { main }, null)!;
        var gifResolution = (ComboBox)gifWindow.FindName("ResolutionBox");
        if (((ComboBoxItem)gifResolution.SelectedItem).Content.ToString() != "320 × 320" ||
            !gifResolution.Items.OfType<ComboBoxItem>().Skip(1).Take(4).Select(item => item.Content.ToString()).SequenceEqual(new[] { "240 × 240", "320 × 320", "480 × 480", "512 × 512" }))
            throw new Exception("GIF small-resolution presets or default changed");
        var selectorType = assembly.GetType("MajdataEdit.ExportRangeSelector")!;
        var rangeType = assembly.GetType("MajdataEdit.MediaRange")!;
        var selector = (FrameworkElement)Activator.CreateInstance(selectorType, true)!;
        var load = selectorType.GetMethod("Load", All)!;
        var marked = Activator.CreateInstance(rangeType, new object[] { 2d, 6d });
        load.Invoke(selector, new object?[] { 12d, new double[] { 0, 2, 4, 6, 8, 10, 12 }, marked, true });
        var selected = selectorType.GetProperty("Selection", All)!;
        if ((double)rangeType.GetProperty("Start")!.GetValue(selected.GetValue(selector))! != 2) throw new Exception("Markers did not initialize selection");
        var start = (TextBox)selectorType.GetField("start", All)!.GetValue(selector)!;
        var end = (TextBox)selectorType.GetField("end", All)!.GetValue(selector)!;
        var enabled = (CheckBox)selectorType.GetField("enabled", All)!.GetValue(selector)!;
        var snap = (CheckBox)selectorType.GetField("snap", All)!.GetValue(selector)!;
        var timeline = (FrameworkElement)selectorType.GetField("timeline", All)!.GetValue(selector)!;
        var previews = 0;
        selectorType.GetEvent("PreviewRequested", All)!.GetAddMethod(true)!.Invoke(selector, new object[] { (Action<double>)(_ => previews++) });
        enabled.IsChecked = false;
        selectorType.GetMethod("CommitFields", All)!.Invoke(selector, null);
        if (timeline.IsEnabled || start.IsEnabled || end.IsEnabled || snap.IsEnabled) throw new Exception("Range controls remain active when unchecked");
        timeline.GetType().GetMethod("OnMouseLeftButtonDown", All)!.Invoke(timeline, new object[] {
            new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) });
        if (previews != 0 || selected.GetValue(selector) != null) throw new Exception("Unchecked timeline changed preview or enabled selection");
        enabled.IsChecked = true;
        if (!timeline.IsEnabled || !start.IsEnabled || !end.IsEnabled || !snap.IsEnabled) throw new Exception("Range controls did not reactivate");
        start.Text = "3.1250"; end.Text = "7.7500";
        var arguments = new object?[] { null };
        if (!(bool)selectorType.GetMethod("Validate", All)!.Invoke(selector, arguments)!) throw new Exception(arguments[0]?.ToString());
        load.Invoke(selector, new object?[] { 12d, new double[] { 0, 2, 4, 6, 8, 10, 12 }, marked, false });
        if (start.Text != "3.1250" || end.Text != "7.7500") throw new Exception("An update overwrote the edited range");
        start.Text = "8"; end.Text = "7";
        if ((bool)selectorType.GetMethod("Validate", All)!.Invoke(selector, arguments)!) throw new Exception("Reversed range accepted");
        load.Invoke(selector, new object?[] { 12d, new double[] { 0, 2, 4, 6, 8, 10, 12 }, marked, true });
        var host = new Window { Content = selector, Width = 380, Height = 230, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        host.Show(); host.UpdateLayout();
        if (snap.TranslatePoint(new Point(), selector).X <= enabled.TranslatePoint(new Point(), selector).X || snap.TranslatePoint(new Point(), selector).X > 200)
            throw new Exception("Range and snap options are not grouped on the left");
        var dpi = VisualTreeHelper.GetDpi(selector);
        var image = new RenderTargetBitmap((int)(selector.ActualWidth * dpi.DpiScaleX), (int)(selector.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        image.Render(selector);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create("alpha/.codex-build/097-export-range.png")) png.Save(file);
        host.Close();
        var path = Path.GetFullPath("alpha/.codex-build/096-receipt.gif");
        File.WriteAllBytes(path, new byte[] { 71, 73, 70 });
        if (File.Exists(path + ".done")) File.Delete(path + ".done");
        var wait = (Task<bool>)assembly.GetType("MajdataEdit.RecordVideoWindow")!.GetMethod("WaitForOutput", All)!
            .Invoke(null, new object?[] { path, TimeSpan.FromSeconds(10), CancellationToken.None, null })!;
        // Keep the UI dispatcher active while exercising the actual completion poll.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var frame = new System.Windows.Threading.DispatcherFrame();
        timer.Tick += (_, _) => { timer.Stop(); if (wait.IsCompleted) throw new Exception("An unfinished output was reported complete"); File.WriteAllText(path + ".done", "complete"); };
        timer.Start();
        wait.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        if (!wait.Result || File.Exists(path + ".done")) throw new Exception("Completion receipt was not consumed");
        File.Delete(path);
        VerifyReopenedRanges(main);
        Console.WriteLine("096 EXPORT PASS: menu nesting, marker initialization, range edits, invalid ranges, actual WPF timeline and encoder completion receipt.");
    }

    private static void VerifyReopenedRanges(MainWindow main)
    {
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        MainWindow.maidataDir = Directory.CreateDirectory("alpha/.codex-build/109-export-chart").FullName;
        var type = typeof(MainWindow);
        var process = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetField("first", All)!.SetValue(null, 0f);
        var setText = type.GetMethod("SetRawFumenText", All)!;
        var selectorField = typeof(RecordVideoWindow).GetField("rangeSelector", All)!;
        var selectorType = selectorField.FieldType;
        var selection = selectorType.GetProperty("Selection", All)!;
        foreach (var (menu, field) in new[] { ("Menu_ExportRender_Click", "recordVideoWindow"), ("Menu_ExportGif_Click", "gifExportWindow") })
        {
            void Open() => type.GetMethod(menu, All)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
            void Set(string notes) => setText.Invoke(main, new object[] { notes });
            Set("(120){4}1,\n@start\n2,3,\n@end\n4,\nE");
            Open();
            var window = (Window)type.GetField(field, All)!.GetValue(main)!;
            var selector = selectorField.GetValue(window)!;
            void Expect(double start, double end)
            {
                var range = selection.GetValue(selector) ?? throw new Exception("No range after opening " + menu);
                var rangeType = range.GetType();
                if ((double)rangeType.GetProperty("Start")!.GetValue(range)! != start ||
                    (double)rangeType.GetProperty("End")!.GetValue(range)! != end)
                    throw new Exception("Incorrect range after opening " + menu + ": " + range);
            }
            Expect(.5, 1.5);
            Set("(120){4}1,2,\n@start\n3,,,,\n@end\nE");
            Open();
            Expect(1, 3);
            Set("(120){4}1,2,3,4,\nE");
            Open();
            if (selection.GetValue(selector) != null) throw new Exception("Removed markers retained an old range");
            window.Close();
        }
        Console.WriteLine("109 EXPORT PASS: new and reused Video/GIF tabs follow updated or deleted chart markers.");
    }
}
