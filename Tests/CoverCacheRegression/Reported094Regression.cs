using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Reported094Regression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    internal static void Run()
    {
        var type = typeof(MainWindow);
        var source = "(120)\n@{8}5,6,\n{4}<TEXT*(\"一\n二\")>1x~[head.png]-5[8:1],1m,3h[8:1],4,\n@3/4\n@start\n";
        var merger = type.Assembly.GetType("MajdataEdit.Editor.NoteStreamMerger")!;
        var arguments = new object?[] { source, null, null };
        if (!(bool)merger.GetMethod("TryFlattenAll", All)!.Invoke(null, arguments)!) throw new Exception(arguments[2]?.ToString());
        var flattened = (string)arguments[1]!;
        var cleaned = (string)type.GetMethod("StripAlphaOnlySyntax", All)!.Invoke(null, new object[] { flattened })!;
        if (cleaned.Contains("TEXT") || cleaned.Contains("二") || cleaned.Contains('@') ||
            !cleaned.Contains("head.png") || !cleaned.Contains("1m") || !cleaned.Contains("3h[8:1]") ||
            !cleaned.Contains('5') || !cleaned.Contains('6'))
            throw new Exception("No-effect export changed notes or retained display/editor directives: " + cleaned);
        var arc = (string)type.GetMethod("StripAlphaOnlySyntax", All)!.Invoke(null, new object[] { "{4}1<5[8:1]," })!;
        if (arc != "{4}1<5[8:1],") throw new Exception("No-effect export changed an ordinary arc");
        Console.WriteLine("094 EXPORT PASS: merged stream notes and inherited/mine/Hold syntax retained; multiline TEXT and editor directives removed, arc untouched.");

        var panel = new SidebarTabPanel { ViewportWidth = 800 };
        var tool = new TabItem { Header = "Edit 设置" };
        var song = new TabItem { Header = "很长很长的歌曲名字" };
        SidebarTabPanel.SetWidthMultiplier(song, 1.5);
        panel.Children.Add(tool); panel.Children.Add(song);
        panel.Measure(new Size(800, 30)); panel.Arrange(new Rect(0, 0, 800, 30));
        if (Math.Abs(song.ActualWidth / tool.ActualWidth - 1.5) > .001) throw new Exception("Song tabs must be 50% wider");
        Console.WriteLine($"094 TABS PASS: tool={tool.ActualWidth:F2}, song={song.ActualWidth:F2} DIP.");

        var folder = Path.GetFullPath("alpha/.codex-build/094-chart"); Directory.CreateDirectory(folder);
        var notes = Environment.GetCommandLineArgs().Contains("--hold-095")
            ? "(120){4}1hx[2:1]~[1<5[1:1]],1h[2:1]~[1V35[384:1]],1h[2:1]~[1v1[384:1]],1h[2:1]~[B1<B1[1:1]],1h[2:1]~[B1<<B1[1:1]],"
            : "(120){4}1hx[2:1]~[1<5[1:1]],";
        File.WriteAllText(Path.Combine(folder, "maidata.txt"), "&title=094\n&artist=test\n&first=0\n&lv_5=14\n&inote_5=" + notes);
        var process = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetMethod("ReadData", All)!.Invoke(null, new object[] { Path.Combine(folder, "maidata.txt") });
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { notes, 0L });
        MainWindow.maidataDir = folder;
        var main = new MainWindow { editorSetting = new EditorSetting { SongDetailStyle = 0 } };
        type.GetField("selectedDifficulty", All)!.SetValue(main, 4);
        var packet = type.GetMethod("BuildSongDetailMajson", All)!.Invoke(main, null)!;
        File.WriteAllText(Path.Combine(folder, "majdata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(packet));
        if (!Environment.GetCommandLineArgs().Contains("--hold-095")) DrawFontComparison();
    }

    private static void DrawFontComparison()
    {
        var oldPath = Path.GetFullPath("alpha/Fonts/MajdataUIRounded-Regular.otf");
        var old = Fonts.GetFontFamilies(new Uri(Path.GetDirectoryName(oldPath) + "/"), "./" + Path.GetFileName(oldPath)).Single();
        var visual = new DrawingVisual(); TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 39, 46)), null, new Rect(0, 0, 720, 140));
            var rows = new[] { (old, "之前：源泉圆体 Regular"), (App.UiFontFamily, "本次：MiSans Regular") };
            for (var index = 0; index < rows.Length; index++)
            {
                var font = new Typeface(rows[index].Item1, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var sample = new FormattedText(rows[index].Item2 + "\n谱面文件、编辑器设置、语法错误、静态无理。", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),
                    FlowDirection.LeftToRight, font, 13, Brushes.White, 1.5);
                draw.DrawText(sample, new Point(16, 12 + index * 62));
            }
        }
        var bitmap = new RenderTargetBitmap(1080, 210, 144, 144, PixelFormats.Pbgra32); bitmap.Render(visual);
        var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create("alpha/.codex-build/094-font-comparison.png"); image.Save(output);
        Console.WriteLine("094 FONT PASS: actual WPF rounded/MiSans render saved.");
    }
}
