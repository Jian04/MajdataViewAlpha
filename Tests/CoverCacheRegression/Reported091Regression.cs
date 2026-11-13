using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using MajdataCore;
using MajdataEdit;
using Un4seen.Bass;

internal static class Reported091Regression
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Main = typeof(MainWindow), Process = Main.Assembly.GetType("MajdataEdit.SimaiProcess")!;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, All)!.Invoke(target, args);
    static object? Field(object target, string name) => target.GetType().GetField(name, All)!.GetValue(target);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run()
    {
        var root = Path.GetFullPath("alpha/.codex-build/091-documents");
        Directory.CreateDirectory(root);
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = CultureInfo.GetCultureInfo("zh-CN");
        VerifyPaths();
        VerifyCoverColours();
        System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "bass.dll"));
        Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
        var main = new MainWindow { editorSetting = new EditorSetting { SongDetailStyle = 0 } };
        main.Loaded -= (RoutedEventHandler)Main.GetMethod("Window_Loaded", All)!.CreateDelegate(typeof(RoutedEventHandler), main);
        Main.Assembly.GetType("MajdataEdit.WebControl")!.GetField("IsShuttingDown", All)!.SetValue(null, true);
        Main.GetField("editorAudioDevice", All)!.SetValue(main, 0);
        Main.GetField("audioOutputUnavailable", All)!.SetValue(main, true);
        Main.GetField("chartQueueStop", All)!.SetValue(main, new CancellationTokenSource());
        Call(main, "EnsureToolSidebar"); Call(main, "InitWave");
        main.Left = main.Top = -30000; main.ShowInTaskbar = false; main.ShowActivated = false; main.Show();
        var source = Path.GetFullPath("D:/Maimai/charts/megalovania/track.mp3");
        var songs = new[] { Path.Combine(root, "A"), Path.Combine(root, "B") };
        foreach (var folder in songs)
        {
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, "track.mp3"), true);
            File.WriteAllText(Path.Combine(folder, "maidata.txt"), $"&title=Song {Path.GetFileName(folder)}\n&artist=artist {Path.GetFileName(folder)}\n&first=0\n&lv_5=14\n&inote_5=(120){{4}}1,2,3,4,E\n");
        }
        File.WriteAllText((string)Call(main, "ChartSettingPath", songs[1])!, "{\"lastEditDiff\":4,\"BGM_Level\":0.8,\"Answer_Level\":0.6}");
        var audioType = Main.Assembly.GetType("MajdataEdit.EditorSongAudio")!;
        void Open(string folder)
        {
            var stream = Bass.BASS_StreamCreateFile(Path.Combine(folder, "track.mp3"), 0, 0, BASSFlag.BASS_STREAM_DECODE);
            using var prepared = (IDisposable)Activator.CreateInstance(audioType, All, null,
                new object[] { stream, new[] { new short[1000], new short[500], new short[200] }, 180d, 44100, true }, null)!;
            Call(main, "InstallChartFromFile", folder, prepared);
        }
        Open(songs[0]);
        Call(main, "MenuItem_SoundSetting_Click", main, new RoutedEventArgs());
        ((Slider)((Window)Field(main, "soundSetting")!).FindName("BGM_Slider")).Value = .25;
        var meter = (System.Windows.Threading.DispatcherTimer)typeof(SoundSetting).GetField("UpdateLevelTimer", All)!.GetValue(Field(main, "soundSetting"))!;
        var meterTicks = 0; meter.Tick += (_, _) => meterTicks++;
        var meterFrame = new System.Windows.Threading.DispatcherFrame();
        var meterWait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        meterWait.Tick += (_, _) => { meterWait.Stop(); meterFrame.Continue = false; };
        meterWait.Start(); System.Windows.Threading.Dispatcher.PushFrame(meterFrame);
        Check(meterTicks > 0 && meterTicks <= 10, "Visible volume meters must update at about 30Hz, not 1ms");
        var editor = (TextEditor)main.FindName("FumenContent");
        editor.AppendText("\n1,2,");
        var textA = editor.Text; var documentA = editor.Document;
        Check(documentA.UndoStack.CanUndo, "A owns an undo history");
        Call(main, "MenuItem_InfomationEdit_Click", main, new RoutedEventArgs());
        Check(!meter.IsEnabled, "Hidden volume page must stop its meter timer");
        Console.WriteLine($"093 METER PASS: {meterTicks} ticks/250ms; hidden page timer stopped.");
        var tools = (IDictionary)Field(main, "toolTabs")!;
        var infoEntry = tools[typeof(Infomation)]!;
        var info = (Infomation)infoEntry.GetType().GetField("Item1")!.GetValue(infoEntry)!;
        ((TextBox)info.FindName("TitleTextbox")).Text = "A edited title";
        Call(main, "Menu_ExportRender_Click", main, new RoutedEventArgs());
        var record = (Window)Field(main, "recordVideoWindow")!;
        ((TextBox)record.FindName("TitleBox")).Text = "Record A title";
        Call(main, "FlushSongTools");
        Main.GetField("guestChartPendingSave", All)!.SetValue(main, true);
        Main.GetField("guestSavePath", All)!.SetValue(main, Path.Combine(root, "guest-a.txt"));
        // This pending timer must be committed to A before B is installed.
        Open(songs[1]);
        Check(((IList)Field(main, "chartDocuments")!).Count == 2, "Opening another song creates a second page");
        Check(!(bool)Field(main, "guestChartPendingSave")! && Field(main, "guestSavePath") == null, "A guest-save state must not leak into B");
        Check((string)Process.GetField("title")!.GetValue(null)! == "Song B", "B metadata must not be replaced by A");
        Call(info, "FlushInformation");
        Check((string)Process.GetField("title")!.GetValue(null)! == "Song B", "An old information timer must not write A into B");
        Check(((TextBox)info.FindName("TitleTextbox")).Text == "Song B", "Information page rebinds to B");
        Check(((TextBox)record.FindName("TitleBox")).Text == "Song B", "Recording page rebinds to B");
        var bgmB = (Slider)((Window)Field(main, "soundSetting")!).FindName("BGM_Slider");
        var actualGain = 0f; Bass.BASS_ChannelGetAttribute((int)Field(main, "bgmStream")!, BASSAttribute.BASS_ATTRIB_VOL, ref actualGain);
        Console.WriteLine($"091 B GAIN: slider={bgmB.Value}, channel={actualGain}");
        Check(Math.Abs(bgmB.Value - .25) < .001, "B mixer must read the shared exe volume and ignore the old song-specific .8");
        editor.AppendText("\n8,7,"); var textB = editor.Text; var documentB = editor.Document;
        var documents = (IList)Field(main, "chartDocuments")!;
        var a = documents[0]!; var b = documents[1]!;
        var tabs = (TabControl)Field(main, "editorPages")!;
        tabs.SelectedItem = Field(a, "Tab");
        Check(ReferenceEquals(editor.Document, documentA) && editor.Text == textA, "A text and undo document survive page switches");
        Check(((TextBox)info.FindName("TitleTextbox")).Text == "Record A title", "A recording metadata is flushed to A and restored independently");
        Check((bool)Field(main, "guestChartPendingSave")! && (string?)Field(main, "guestSavePath") == Path.Combine(root, "guest-a.txt"),
            "A guest draft's own save state is restored");
        Main.GetField("guestChartPendingSave", All)!.SetValue(main, false);
        Check(Math.Abs(((Slider)((Window)Field(main, "soundSetting")!).FindName("BGM_Slider")).Value - .25) < .001,
            "A mixer restores A's volume rather than B's defaults");
        tabs.SelectedItem = Field(b, "Tab");
        Check(ReferenceEquals(editor.Document, documentB) && editor.Text == textB && !(bool)Field(main, "isSaved")!, "B unsaved draft survives page switches");
        var media = (object)main.FindName("MediaTimelinePanel");
        media.GetType().GetField("loadedChartDirectory", All)!.SetValue(media, songs[0]);
        media.GetType().GetField("hasPendingChanges", All)!.SetValue(media, true);
        Call(main, "SaveFumen", true);
        Check((bool)media.GetType().GetField("hasPendingChanges", All)!.GetValue(media)!, "Saving B must not commit A's pending timeline");
        Check((bool)Field(main, "isSaved")!, "A pending timeline must not dirty B");
        media.GetType().GetField("hasPendingChanges", All)!.SetValue(media, false);
        Check(File.ReadAllText(Path.Combine(songs[1], "maidata.txt")).Contains("8,7,"), "Saving B writes B");
        Check(!File.ReadAllText(Path.Combine(songs[0], "maidata.txt")).Contains("8,7,"), "B saving does not overwrite A");
        media.GetType().GetField("loadedChartDirectory", All)!.SetValue(media, songs[1]);
        File.WriteAllText(Path.Combine(songs[0], "media_timeline.json.tmp"), "{\"Version\":1,\"Clips\":[]}");
        Call(main, "CloseChartDocument", b);
        Check(documents.Count == 1 && MainWindow.maidataDir == songs[0], "Closing B removes only B and activates A");
        Call(main, "SaveFumen", true);
        Check(File.Exists(Path.Combine(songs[0], "media_timeline.json")) && !File.Exists(Path.Combine(songs[0], "media_timeline.json.tmp")),
            "A pending timeline must save even while the hidden media panel still belongs to B");
        Check(!File.Exists(Path.Combine(songs[1], "media_timeline.json")), "Saving A's timeline must not write B");
        Call(main, "CloseChartDocument", a);
        Check(documents.Count == 0 && MainWindow.maidataDir == "" && editor.IsReadOnly, "Closing the last song restores the empty read-only editor");
        Console.WriteLine("091 DOCUMENT PASS: pages, text/undo retention, pending metadata timer, save isolation, close and empty state.");
        if (info.IsLoaded) info.Close();
        Bass.BASS_Free();
    }

    static void VerifyPaths()
    {
        var expression = "1P58-6P93K5-D6[8:1]";
        Check(SlidePathParser.TryParsePath(expression, out var path), "Mixed chain must parse");
        Check(SlideSyntaxValidator.TryValidate(path, out var error), error);
        Check(path.segments.Count == 4 && path.segments[0].shape == "P" && path.segments[0].slideCode == "" &&
            path.segments[1].shape == "-" && path.segments[2].slideCode == "6P93K5" &&
            path.segments[3].end.area == 'D' && path.segments[3].end.position == 6,
            "Alpha P prefix / FES connector / SlideCode P93 / Touch ending boundaries");
        foreach (var source in new[] { "1dP93K5d", "A1A3KB4", "D1CKE6" })
        {
            Check(SlideCodeParser.TryParse(source, out _, out error), source + ": " + error);
        }
        foreach (var shape in new[] { "p", "q", "s", "z", "rp", "rq" })
        {
            var source = "1" + shape + "5P93K5[8:1]";
            Check(SlidePathParser.TryParsePath(source, out var mixed) && mixed.segments.Count == 2 &&
                mixed.segments[0].shape == shape && mixed.segments[0].slideCode == "" && mixed.segments[1].slideCode == "5P93K5",
                "Ordinary shape boundary before SlideCode: " + source);
        }
        Console.WriteLine("091 PATH PASS: " + string.Join(" | ", path.segments.Select(segment => segment.ToExpression(true))));
    }

    static void VerifyCoverColours()
    {
        var parts = Path.GetFullPath("alpha/Assets/SongDetailParts");
        using var easy = (System.Drawing.Bitmap)Main.GetMethod("LoadSongDetailPart", All)!.Invoke(null,
            new object[] { Path.Combine(parts, "UI_TST_MBase_DMY.png"), 0 })!;
        using var original = (System.Drawing.Bitmap)Main.GetMethod("LoadSongDetailPart", All)!.Invoke(null,
            new object[] { Path.Combine(parts, "UI_TST_MBase_UTG.png"), 6 })!;
        var blue = easy.GetPixel(30, 50); var pink = original.GetPixel(50, 50);
        Check(blue.B > blue.R + 40 && blue.B > blue.G && blue.A == 255, "EASY card must be blue");
        Check(pink.R > pink.G && pink.B > pink.G, "ORIGINAL card retains its pink-purple source");
        Console.WriteLine($"091 COVER COLOUR PASS: EASY={blue}, ORIGINAL={pink}.");
    }
}
