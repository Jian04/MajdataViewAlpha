using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using MajdataEdit;
using Un4seen.Bass;

internal static class Reported061Regression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static void Run(bool renderFriendAudio = false)
    {
        var folder = @"C:\Users\sjtu_\Downloads\⚡朋友的酒⚡\⚡朋友的酒⚡";
        var assembly = typeof(MainWindow).Assembly;
        VerifyClipTiming(assembly);
        VerifySectionTints(assembly);
        var process = assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetMethod("ClearData", All)!.Invoke(null, null);
        process.GetMethod("ReadData", All)!.Invoke(null, new object[] { Path.Combine(folder, "maidata.txt") });
        var text = ((string[])process.GetField("fumens", All)!.GetValue(null)!)[4];
        foreach (var colored in new[] { false, true })
        {
            var editor = new TextEditor { WordWrap = false, FontSize = 14, Text = text };
            if (colored)
                editor.TextArea.TextView.LineTransformers.Add((ICSharpCode.AvalonEdit.Rendering.IVisualLineTransformer)
                    Activator.CreateInstance(assembly.GetType("MajdataEdit.Editor.SimaiColorizer")!, All, null, new object[] { editor }, null)!);
            var host = new Window { Content = editor, Width = 560, Height = 400, ShowActivated = false,
                ShowInTaskbar = false, Left = -30000, Top = -30000 };
            var watch = Stopwatch.StartNew();
            host.Show(); editor.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            Console.WriteLine($"Text layout colored={colored}: {watch.ElapsedMilliseconds} ms");
            host.Close();
        }
        var bassPath = Path.Combine(AppContext.BaseDirectory, "bass.dll");
        System.Runtime.InteropServices.NativeLibrary.Load(bassPath);
        Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
        try
        {
            var watch = Stopwatch.StartNew();
            var audio = assembly.GetType("MajdataEdit.EditorSongAudio")!.GetMethod("ReadWaveform", All)!
                .Invoke(null, new object[] { Path.Combine(folder, "track.mp3"), CancellationToken.None });
            Console.WriteLine($"Audio waveform: {watch.ElapsedMilliseconds} ms");
            VerifyMainWindow(folder, process, renderFriendAudio);
        }
        finally { Bass.BASS_Free(); }
    }

    private static void VerifyMainWindow(string sourceFolder, Type process, bool renderFriendAudio)
    {
        var folder = Path.GetFullPath(".codex-build/061-editor-open-fixture");
        Directory.CreateDirectory(folder);
        foreach (var source in Directory.EnumerateFiles(sourceFolder))
        {
            if (Path.GetFileName(source).Equals("majdata.json", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(source, Path.Combine(folder, Path.GetFileName(source)), true);
        }
        var main = new MainWindow();
        var type = typeof(MainWindow);
        // This fixture measures rendering without a View process.
        type.GetField("chartQueueStop", All)!.SetValue(main, new CancellationTokenSource());
        var settings = Activator.CreateInstance(type.Assembly.GetType("MajdataEdit.EditorSetting")!)!;
        settings.GetType().GetField("Language", All)!.SetValue(settings, "zh-CN");
        settings.GetType().GetField("DisableAutomaticSongDetailRefresh", All)!.SetValue(settings, true);
        type.GetField("editorSetting", All)!.SetValue(main, settings);
        type.GetField("editorAudioDevice", All)!.SetValue(main, 0);
        type.GetField("audioOutputUnavailable", All)!.SetValue(main, true);
        type.GetMethod("EnsureToolSidebar", All)!.Invoke(main, null);
        type.GetMethod("InitWave", All)!.Invoke(main, null);
        VerifyEmptyPane(main, type);
        var watch = Stopwatch.StartNew();
        var audioType = type.Assembly.GetType("MajdataEdit.EditorSongAudio")!;
        var samples = new[] { new short[1000], new short[500], new short[200] };
        var stream = Bass.BASS_StreamCreateFile(Path.Combine(folder, "track.mp3"), 0, 0, BASSFlag.BASS_STREAM_DECODE);
        var prepared = Activator.CreateInstance(audioType, All, null, new object[] { stream, samples, 180d, 44100, true }, null)!;
        type.GetMethod("InstallChartFromFile", All)!.Invoke(main, new[] { (object)folder, prepared });
        Console.WriteLine($"Full chart install (no cover bake): {watch.ElapsedMilliseconds} ms");
        watch.Restart();
        var majson = type.GetMethod("BuildSongDetailMajson", All)!.Invoke(main, null)!;
        Console.WriteLine($"Playback chart build: {watch.ElapsedMilliseconds} ms");
        majson.GetType().GetField("filePath")!.SetValue(majson, Path.Combine(folder, "maidata.txt"));
        File.WriteAllText(Path.Combine(folder, "092-friend.json"), Newtonsoft.Json.JsonConvert.SerializeObject(majson));
        watch.Restart();
        type.GetMethod("ValidateMajsonForView", All)!.Invoke(null, new[] { majson });
        Console.WriteLine($"Playback validation: {watch.ElapsedMilliseconds} ms");
        watch.Restart(); type.GetMethod("generateSoundEffectList", All)!.Invoke(main, new object[] { 0d, false });
        Console.WriteLine($"Playback SE schedule: {watch.ElapsedMilliseconds} ms");
        if (renderFriendAudio)
        {
            var currentDirectory = Environment.CurrentDirectory;
            var savedMix = Path.GetFullPath("alpha/.codex-build/092-friend-audio.wav");
            var ffmpegPath = Path.GetFullPath("alpha/Assets/StreamingAssets");
            Environment.SetEnvironmentVariable("PATH", ffmpegPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            try
            {
                var sfx = Path.Combine(folder, "SFX"); Directory.CreateDirectory(sfx);
                foreach (var sample in Directory.EnumerateFiles(Path.GetFullPath("alpha/MajdataEdit/SFX"), "*.wav"))
                    File.Copy(sample, Path.Combine(sfx, Path.GetFileName(sample)), true);
                Directory.CreateDirectory(Path.Combine(folder, "MajdataView_Data", "StreamingAssets"));
                Environment.CurrentDirectory = folder;
                var allocated = GC.GetTotalAllocatedBytes(true); watch.Restart();
                type.GetMethod("renderSoundEffect", All)!.Invoke(main, new object?[] { 0d, true, null });
                Console.WriteLine($"FRIEND RECORD AUDIO: {watch.ElapsedMilliseconds}ms, allocated={GC.GetTotalAllocatedBytes(true) - allocated} bytes");
                File.Copy(MajdataCore.AlphaSession.ChartWorkFile(folder, "out.wav"), savedMix, true);
            }
            finally { Environment.CurrentDirectory = currentDirectory; }
        }
        main.Measure(new Size(800, 589)); main.Arrange(new Rect(0, 0, 800, 589)); main.UpdateLayout();
        watch.Restart(); type.GetMethod("DrawWaveCore", All)!.Invoke(main, null);
        Console.WriteLine($"First waveform draw: {watch.ElapsedMilliseconds} ms");
        var previousNotes = process.GetField("notelist", All)!.GetValue(null);
        watch.Restart(); type.GetMethod("GetCaretTimingTime", All)!.Invoke(main, null);
        Console.WriteLine($"Caret timing lookup: {watch.ElapsedMilliseconds} ms");
        if (!ReferenceEquals(previousNotes, process.GetField("notelist", All)!.GetValue(null)))
            throw new Exception("Moving the caret must reuse parsed note timelines");
        watch.Restart();
        for (var index = 0; index < 30; index++)
        {
            type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)index);
            type.GetMethod("DrawWaveCore", All)!.Invoke(main, null);
        }
        Console.WriteLine($"Cached drag draw average: {watch.Elapsed.TotalMilliseconds / 30:F2} ms");
        var files = (Button)type.GetField("chartFilesButton", All)!.GetValue(main)!;
        var tree = (TreeView)type.GetField("chartFilesTree", All)!.GetValue(main)!;
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (tree.Items.Count != 1 || !((TreeViewItem)tree.Items[0]).HasItems) throw new Exception("File pane must show chart files");
        Console.WriteLine("PANE PASS: chart tree, waveform hide/show.");
        VerifyPaneLayout(main, tree, type);
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { "(120){4}1,2,", 0L });
        process.GetField("clockCount", All)!.SetValue(null, "4");
        type.GetMethod("generateSoundEffectList", All)!.Invoke(main, new object[] { 0d, false });
        var schedule = (System.Collections.IEnumerable)type.GetField("waitToBePlayed", All)!.GetValue(main)!;
        var clocks = schedule.Cast<object>().Count(entry =>
            ((System.Collections.IEnumerable)entry.GetType().GetProperty("NormalSounds", All)!.GetValue(entry)!)
                .Cast<object>().Any(sound => sound.ToString() == "Clock"));
        if (clocks != 4) throw new Exception("Normal playback from zero must include all authored clock counts");
        Console.WriteLine("CLOCK PASS: normal start includes 4 count-in sounds.");
        VerifyRecordedAudio(main, process);
        type.GetField("isSaved", All)!.SetValue(main, true);
        main.Close();
    }

    private static void VerifyEmptyPane(MainWindow main, Type type)
    {
        var content = (FrameworkElement)main.Content; main.Content = null;
        var host = new Window { Content = content, Width = 800, Height = 589, WindowStyle = WindowStyle.None,
            ShowActivated = false, ShowInTaskbar = false, Left = -30000, Top = -30000 };
        host.Show(); host.UpdateLayout();
        var pane = (Grid)type.GetField("waveformPane", All)!.GetValue(main)!;
        var row = (RowDefinition)type.GetField("waveformRow", All)!.GetValue(main)!;
        var bottom = (Button)type.GetField("bottomPaneButton", All)!.GetValue(main)!;
        if (pane.Visibility != Visibility.Collapsed || row.ActualHeight != 80 || bottom.IsEnabled ||
            ((FrameworkElement)type.GetField("waveformSplitter", All)!.GetValue(main)!).IsVisible)
            throw new Exception("Before selecting a song, waveform stays hidden and reserves 80 pixels");
        type.GetMethod("UpdateWaveformPane", All)!.Invoke(main, new object[] { false });
        if (pane.Visibility != Visibility.Collapsed) throw new Exception("Updating panes without a chart must not show the waveform");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 589, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.GetFullPath(".codex-build/066-empty-editor.png"))) encoder.Save(output);
        host.Content = null; main.Content = content; host.Close();
        Console.WriteLine("EMPTY EDITOR PASS: hidden waveform reserves 80 pixels until a song is loaded.");
    }

    private static void WaitForCollapse(FrameworkElement pane)
    {
        var watch = Stopwatch.StartNew();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => { if (pane.Visibility == Visibility.Collapsed || watch.Elapsed.TotalSeconds > 2) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (pane.Visibility != Visibility.Collapsed) throw new Exception("Pane close animation did not complete");
    }

    private static void VerifyPaneLayout(MainWindow main, TreeView tree, Type type)
    {
        var workingDirectory = Environment.CurrentDirectory;
        var content = (FrameworkElement)main.Content;
        main.Content = null;
        var host = new Window { Content = content, Width = 950, Height = 760, WindowStyle = WindowStyle.None,
            ShowActivated = false, ShowInTaskbar = false, Left = -30000, Top = -30000 };
        host.Show(); host.UpdateLayout();
        PumpAnimations(host);
        var pane = (Grid)type.GetField("waveformPane", All)!.GetValue(main)!;
        var pet = (Button)type.GetField("dockedPetButton", All)!.GetValue(main)!;
        if (pet.Visibility != Visibility.Visible || pet.Opacity <= 0) throw new Exception("Pet docking target must be visible before docking");
        type.GetField("editorUiReady", All)!.SetValue(main, true);
        type.GetField("startupViewAligned", All)!.SetValue(main, true);
        var commandType = type.Assembly.GetType("MajdataCollaboration.GroupCommand")!;
        var probe = Activator.CreateInstance(commandType)!;
        var center = pet.PointToScreen(new Point(pet.ActualWidth / 2, pet.ActualHeight / 2));
        using var self = Process.GetCurrentProcess();
        foreach (var (name, value) in new (string, object)[] { ("Action", "dock-probe"), ("X", (int)center.X), ("Y", (int)center.Y),
            ("PetPid", self.Id), ("PetStart", self.StartTime.ToUniversalTime().Ticks) })
            commandType.GetProperty(name)!.SetValue(probe, value);
        var reply = type.GetMethod("DockPet", All)!.Invoke(main, new[] { probe })!;
        if (!(bool)reply.GetType().GetProperty("Ok")!.GetValue(reply)!) throw new Exception("Pet must dock at the visible toolbar icon, not its old coordinates");
        var row = (RowDefinition)type.GetField("waveformRow", All)!.GetValue(main)!;
        var image = (Image)main.FindName("MusicWave");
        var decoration = (FrameworkElement)main.FindName("DecorationBgHost");
        var decorationType = type.Assembly.GetType("MajdataEdit.EditorDecorationBg")!;
        decorationType.GetMethod("Apply", All)!.Invoke(null, new object[] { decoration, "magical" });
        var editor = (TextEditor)main.FindName("FumenContent");
        var play = (Button)main.FindName("PlayAndPauseButton");
        if (editor.Margin.Bottom != play.Margin.Bottom || editor.Margin.Right != 0 || editor.Margin.Left != 0)
            throw new Exception("Editor bottom must align with playback buttons, with no left or right background gutter");
        var leftSplitter = (GridSplitter)type.GetField("chartFilesSplitter", All)!.GetValue(main)!;
        if (leftSplitter.ResizeBehavior != GridResizeBehavior.CurrentAndNext)
            throw new Exception("File pane must resize against the editor column");
        var diagnostics = (StackPanel)main.FindName("DiagnosticsSummary");
        var menu = editor.ContextMenu;
        menu.Measure(new Size(208, 2000)); menu.Arrange(new Rect(0, 0, 208, menu.DesiredSize.Height)); menu.UpdateLayout();
        var format = menu.Items.OfType<MenuItem>().Single(item => item.Items.OfType<MenuItem>().Any(child => Equals(child.Tag, "m")));
        var arrow = (System.Windows.Shapes.Path)format.Template.FindName("SubmenuArrow", format);
        var arrowGap = menu.ActualWidth - arrow.TranslatePoint(new Point(arrow.ActualWidth, 0), menu).X;
        if (arrow.Visibility != Visibility.Visible || arrowGap > 12)
            throw new Exception($"Context menu must not reserve an empty scrollbar beside its arrow: gap={arrowGap}");
        Console.WriteLine($"CONTEXT MENU PASS: arrow right inset={arrowGap:F1}px.");
        var splitter = (GridSplitter)type.GetField("waveformSplitter", All)!.GetValue(main)!;
        var zoom = (Button)main.FindName("WaveViewZoomIn");
        var clip = (Button)main.FindName("MediaTimelineButton");
        var zoomOut = (Button)main.FindName("WaveViewZoomOut");
        var footer = ((Grid)pane.Parent).Children.OfType<StackPanel>().Single(child => child.Name == "EditorFooter");
        if (zoomOut.TranslatePoint(new Point(), footer).X >= zoom.TranslatePoint(new Point(), footer).X ||
            zoom.TranslatePoint(new Point(), footer).X >= clip.TranslatePoint(new Point(), footer).X)
            throw new Exception("Shared toolbar must keep minus, plus, then scissors in one row");
        var clock = (Label)main.FindName("TimeLabel"); var fraction = (Label)main.FindName("NoteNowText");
        if (clock.Parent != fraction.Parent || clock.FontFamily.Source != fraction.FontFamily.Source || clock.FontSize != fraction.FontSize)
            throw new Exception("Clock and four decimal digits must share one baseline and font");
        foreach (var name in new[] { "LevelSelector", "LevelTextBox", "OffsetTextBox", "DifficultyLabel", "OffsetLabel" })
        {
            var field = (Control)main.FindName(name);
            var expectedFont = ((System.Windows.Media.FontFamily)Application.Current.FindResource(name == "LevelSelector" ? "TitleFontFamily" : "UiFontFamily")).Source;
            if (field.Height != 26 || field.FontSize != (name == "LevelSelector" ? 14 : 12) || field.FontFamily.Source != expectedFont)
                throw new Exception("Metadata font or height differs: " + name);
        }
        // This fixture owns a decode-only stream; wrap it in the same native Tempo processor used for preview.
        System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "bass_fx.dll"));
        var streamField = type.GetField("bgmStream", All)!;
        var originalStream = (int)streamField.GetValue(main)!;
        var probeSource = Bass.BASS_StreamCreateFile(Path.Combine((string)type.GetField("maidataDir", All)!.GetValue(main)!, "track.mp3"), 0, 0,
            BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_SAMPLE_FLOAT);
        if (probeSource == 0) throw new Exception("Tempo source failed: " + Bass.BASS_ErrorGetCode());
        var tempoStream = Un4seen.Bass.AddOn.Fx.BassFx.BASS_FX_TempoCreate(probeSource, BASSFlag.BASS_STREAM_DECODE);
        if (tempoStream == 0) throw new Exception("Tempo probe failed: " + Bass.BASS_ErrorGetCode());
        streamField.SetValue(main, tempoStream);
        var originalRate = (float)type.GetMethod("GetPlaybackSpeed", All)!.Invoke(main, null)!;
        try
        {
            type.GetMethod("SetPlaybackSpeed", All)!.Invoke(main, new object[] { 1.25f });
            type.GetMethod("UpdateTimeDisplay", All)!.Invoke(main, null);
            var rate = (TextBlock)type.GetField("playbackRateText", All)!.GetValue(main)!;
            var total = (TextBlock)type.GetField("totalDurationText", All)!.GetValue(main)!;
            if (rate.Text != "1.25×" || !System.Text.RegularExpressions.Regex.IsMatch(fraction.Content.ToString()!, @"^\.\d{4}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(total.Text, @"^/ \d+:\d{2}\.\d{4}$"))
                throw new Exception($"Transport display: rate={rate.Text}, fraction={fraction.Content}, total={total.Text}, previousRate={originalRate}");
        }
        finally
        {
            type.GetMethod("SetPlaybackSpeed", All)!.Invoke(main, new object[] { originalRate });
            streamField.SetValue(main, originalStream); Bass.BASS_StreamFree(tempoStream); Bass.BASS_StreamFree(probeSource);
            type.GetMethod("UpdateTimeDisplay", All)!.Invoke(main, null);
        }
        Console.WriteLine("TRANSPORT 080 PASS: live 1.25x rate, full four-decimal clock, total duration and shared metadata typography.");
        var follow = (CheckBox)main.FindName("FollowPlayCheck");
        if (LogicalTreeHelper.GetParent(diagnostics) != LogicalTreeHelper.GetParent(clip) || diagnostics.Orientation != Orientation.Horizontal ||
            diagnostics.TranslatePoint(new Point(), footer).X <= clip.TranslatePoint(new Point(), footer).X)
            throw new Exception("Diagnostics must follow scissors in the transport row");
        var options = (Panel)LogicalTreeHelper.GetParent(follow);
        if (options.Children.OfType<CheckBox>().Count() != 5 || main.FindName("WordWrapCheck") is not CheckBox || main.FindName("PausedJudgeAreasCheck") is not CheckBox)
            throw new Exception("Main editor must expose all five quick toggles");
        var pages = (TabControl)type.GetField("editorPages", All)!.GetValue(main)!;
        if (((TextBlock)type.GetField("chartPageTitle", All)!.GetValue(main)!).Text != (string)type.Assembly.GetType("MajdataEdit.SimaiProcess")!.GetField("title", All)!.GetValue(null)! || Grid.GetColumn(editor) != 0 || Grid.GetColumnSpan(editor) != 3)
            throw new Exception("Chart page must fill the editor below its tab and horizontal metadata");
        var root = (TreeViewItem)tree.Items[0]; root.IsExpanded = true;
        var picture = root.Items.OfType<TreeViewItem>().First(item => ((string?)item.Tag)?.EndsWith("bg.jpg") == true);
        var sidebar = (TabControl)type.GetField("toolSidebar", All)!.GetValue(main)!;
        var help = new AlphaSyntaxHelp();
        var helpContent = (FrameworkElement)help.Content; help.Content = null;
        typeof(AlphaSyntaxHelp).GetMethod("UseSidebarLayout", All)!.Invoke(help, new object[] { helpContent });
        sidebar.Items.Add(type.GetMethod("CreateSidebarTab", All)!.Invoke(main,
            new object[] { "语法帮助", helpContent, (Action)(() => { }), (Action<TabItem, Point>)((_, _) => { }) }));
        sidebar.SelectedIndex = 0;
        var fileName = ((StackPanel)picture.Header).Children.OfType<TextBlock>().Single();
        if (tree.FontFamily.Source != sidebar.FontFamily.Source || tree.FontSize != sidebar.FontSize ||
            fileName.FontFamily.Source != sidebar.FontFamily.Source || fileName.FontSize != sidebar.FontSize)
            throw new Exception($"Sidebar fonts differ: tree={tree.FontFamily}/{tree.FontSize}, file={fileName.FontFamily}/{fileName.FontSize}, right={sidebar.FontFamily}/{sidebar.FontSize}");
        picture.IsSelected = true;
        type.GetMethod("PreviewChartFile", All)!.Invoke(main, null);
        if (((Image)type.GetField("chartFilePreview", All)!.GetValue(main)!).Source == null)
            throw new Exception("Selecting a chart image must show a decoded preview");
        foreach (var height in new[] {80d, 200d})
        {
            row.Height = new GridLength(height);
            host.UpdateLayout();
            if (Math.Abs(decoration.ActualHeight - ((FrameworkElement)pane.Parent).ActualHeight) > .001)
                throw new Exception("Raising the waveform must cover the full-height Magical background without rescaling it");
            var lanes = pane.Children.OfType<Canvas>().Single();
            if (lanes.Children.OfType<System.Windows.Shapes.Line>().Count() != 8 ||
                lanes.Children.OfType<System.Windows.Shapes.Line>().Any(line => line.X2 != image.ActualWidth || line.Opacity > .4))
                throw new Exception("Expanded lane numbers must have eight faint full-width horizontal grid lines");
            if ((lanes.Visibility == Visibility.Visible) != (image.ActualHeight >= 145)) throw new Exception($"Lane numbers must appear only when each row has enough height: image={image.ActualHeight}, lanes={lanes.Visibility}");
            if (lanes.Visibility == Visibility.Visible && lanes.Children.OfType<TextBlock>().Any(label =>
                label.Foreground.ToString() != main.FindResource("WaveLabelForeground").ToString() || label.FontFamily != editor.FontFamily || label.FontSize != editor.FontSize ||
                label.Background != System.Windows.Media.Brushes.Transparent))
                throw new Exception("Lane numbers must use the editor line-number font and theme foreground without a background");
            if (image.ActualWidth != pane.ActualWidth || image.TranslatePoint(new Point(), pane).X != 0 || lanes.Opacity != 1)
                throw new Exception("White lane labels must overlay the original full-width waveform");
            var editorParent = (Grid)editor.Parent;
            if (Math.Abs(pane.TranslatePoint(new Point(), content).Y - editor.TranslatePoint(new Point(0, editor.ActualHeight), content).Y - footer.ActualHeight) > .001 ||
                editorParent.RowDefinitions[2].ActualHeight != 0)
                throw new Exception("The shared toolbar must sit between the editor and the unsplit waveform without a reserved strip");
            if (diagnostics.Visibility != Visibility.Visible) throw new Exception("Expanded waveform must not hide footer diagnostics");
            if (pane.TranslatePoint(new Point(), content).Y != footer.TranslatePoint(new Point(0, footer.ActualHeight), content).Y)
                throw new Exception("The waveform must meet the toolbar without a separator band");
            var handlePoint = splitter.TranslatePoint(new Point(splitter.ActualWidth / 2, splitter.ActualHeight / 2), content);
            var hit = content.InputHitTest(handlePoint) as DependencyObject;
            while (hit != null && hit != splitter) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            if (hit == null)
                throw new Exception("Removing the separator band must retain the resize hit target");
            var visual = (System.Windows.Media.Visual)content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(950, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.GetFullPath($".codex-build/066-editor-panes-{height:0}.png"));
            encoder.Save(output);
        }
        splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false));
        var clipEditor = (FrameworkElement)main.FindName("MediaTimelinePanel");
        clipEditor.Visibility = Visibility.Visible;
        var clipButtons = new[] { (Button)main.FindName("TimelineStopButton"), (Button)main.FindName("TimelinePlayAndPauseButton") };
        foreach (var button in clipButtons) button.Visibility = Visibility.Visible;
        var clipHost = (Grid)clipEditor.Parent;
        foreach (var waveHeight in new[] { 80d, 200d })
        foreach (var showRight in new[] { true, false })
        {
            row.Height = new GridLength(waveHeight);
            type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { showRight, false });
            host.UpdateLayout();
            if (clipEditor.TranslatePoint(new Point(), clipHost) != new Point(0, 60) ||
                Math.Abs(clipEditor.ActualWidth - clipHost.ActualWidth) > .001 ||
                Math.Abs(clipEditor.ActualHeight + 60 - clipHost.RowDefinitions[0].ActualHeight) > .001)
                throw new Exception("Clip editor must fill only the upper center pane");
            var wavePoint = image.TranslatePoint(new Point(image.ActualWidth / 2, image.ActualHeight / 2), content);
            var hit = content.InputHitTest(wavePoint) as DependencyObject;
            while (hit != null && hit != pane) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            if (hit == null || Math.Abs(clipEditor.TranslatePoint(new Point(0, clipEditor.ActualHeight), clipHost).Y + footer.ActualHeight -
                pane.TranslatePoint(new Point(), clipHost).Y) > .001)
                throw new Exception("Clip editor and shared toolbar must leave the waveform visible and hittable");
            foreach (var button in clipButtons)
                if (!button.IsVisible || button.TranslatePoint(new Point(), content).Y < footer.TranslatePoint(new Point(), content).Y)
                    throw new Exception("Clip playback controls must remain in the shared toolbar");
            if (play.IsVisible || ((Button)main.FindName("StopButton")).IsVisible)
                throw new Exception("Clip mode must not draw duplicate playback controls");
        }
        var clipBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(950, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        clipBitmap.Render(content);
        var clipEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        clipEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(clipBitmap));
        using (var output = File.Create(Path.GetFullPath(".codex-build/068-clip-waveform.png"))) clipEncoder.Save(output);
        clipEditor.Visibility = Visibility.Collapsed;
        foreach (var button in clipButtons) button.Visibility = Visibility.Collapsed;
        Console.WriteLine("CLIP LAYOUT PASS: upper pane filled; 80/200px waveform remains visible and hittable with right pane open/closed.");
        var bottom = (Button)type.GetField("bottomPaneButton", All)!.GetValue(main)!;
        var bottomPath = (System.Windows.Shapes.Path)((Canvas)((Viewbox)bottom.Content).Child).Children[0];
        var bottomGeometry = bottomPath.Data;
        var bottomBackground = (Border)bottom.Template.FindName("surface", bottom);
        var selectedBackground = bottomBackground.Background.ToString();
        bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout();
        if (pane.Visibility != Visibility.Visible || !ReferenceEquals(bottomPath.Data, bottomGeometry) || bottomBackground.Background.ToString() == selectedBackground)
            throw new Exception($"Pane selection: visibility={pane.Visibility}, tag={bottom.Tag}, before={selectedBackground}, after={bottomBackground.Background}, sameIcon={ReferenceEquals(bottomPath.Data, bottomGeometry)}");
        WaitForCollapse(pane); host.UpdateLayout();
        if (diagnostics.Visibility != Visibility.Visible) throw new Exception("Hiding the waveform must restore diagnostics");
        bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout();
        PumpAnimations(host);
        if (diagnostics.Visibility != Visibility.Visible) throw new Exception("Footer diagnostics must stay visible when restoring the tall waveform");
        var files = (Button)type.GetField("chartFilesButton", All)!.GetValue(main)!;
        var leftPane = (FrameworkElement)type.GetField("chartFilesPane", All)!.GetValue(main)!;
        var filePath = (System.Windows.Shapes.Path)((Canvas)((Viewbox)files.Content).Child).Children[0];
        var fileGeometry = filePath.Data;
        var fileBackground = (Border)files.Template.FindName("surface", files);
        var fileSelectedBackground = fileBackground.Background.ToString();
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitForCollapse(leftPane);
        if (!ReferenceEquals(filePath.Data, fileGeometry) || fileBackground.Background.ToString() == fileSelectedBackground)
            throw new Exception("File pane selection must change only its background");
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout();
        PumpAnimations(host);
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, true }); host.UpdateLayout();
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { false, true });
        if (sidebar.Visibility != Visibility.Visible) throw new Exception("Right pane must animate before collapsing");
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, true });
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        if (sidebar.Visibility != Visibility.Visible) throw new Exception("Reopening must cancel an in-flight close");
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { false, true }); WaitForCollapse(sidebar);
        var animationView = editor.TextArea.TextView;
        var timedRenderers = animationView.BackgroundRenderers.Select(renderer => new TimedRenderer(renderer)).ToArray();
        animationView.BackgroundRenderers.Clear();
        foreach (var renderer in timedRenderers) animationView.BackgroundRenderers.Add(renderer);
        var timedColors = animationView.LineTransformers.Select(transform => new TimedTransformer(transform)).ToArray();
        animationView.LineTransformers.Clear();
        foreach (var transform in timedColors) animationView.LineTransformers.Add(transform);
        foreach (var (name, toggle, definition, size) in new (string, Action, object, string)[]
        {
            ("right", () => type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, true }),
                type.GetField("toolSidebarColumn", All)!.GetValue(main)!, "ActualWidth"),
            ("left", () => files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                type.GetField("chartFilesColumn", All)!.GetValue(main)!, "ActualWidth"),
            ("bottom", () => bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), row, "ActualHeight")
        })
        {
            var widths = new List<double>();
            toggle();
            PumpAnimations(host, () => widths.Add((double)definition.GetType().GetProperty(size)!.GetValue(definition)!));
            Console.WriteLine($"PANE DRAW COST {name}: {string.Join(',', timedRenderers.Select(renderer => $"{renderer.Name}={renderer.Elapsed:F1}ms/{renderer.Calls}"))}; color={timedColors.Sum(item => item.Elapsed):F1}ms/{timedColors.Sum(item => item.Calls)}.");
            var steps = widths.Select(value => Math.Round(value, 1)).Distinct().Count();
            if (steps < 3) throw new Exception($"{name} must change size progressively instead of snapping: {string.Join(',', widths)}");
            Console.WriteLine($"PANE TRANSITION PASS {name}: {steps} distinct sizes.");
        }
        // Restore the layout used by the dense-chart drag checks.
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpAnimations(host);
        clipEditor.Visibility = Visibility.Visible;
        bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitForCollapse(pane); host.UpdateLayout();
        if (Math.Abs(clipEditor.ActualHeight + 60 + footer.ActualHeight - clipHost.ActualHeight) > .001)
            throw new Exception("Clip editor must fill the center above the shared toolbar when the waveform is hidden");
        bottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpAnimations(host);
        type.GetField("sidebarExpanded", All)!.SetValue(main, true);
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false }); host.UpdateLayout();
        if (clipHost.IsVisible) throw new Exception("Full-width right pane must hide the center host");
        type.GetField("sidebarExpanded", All)!.SetValue(main, false);
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false }); host.UpdateLayout();
        if (!clipHost.IsVisible || !pane.IsVisible) throw new Exception("Restoring the right pane must restore clip editor and waveform");
        clipEditor.Visibility = Visibility.Collapsed;
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { false, false });
        var settings = type.GetField("editorSetting", All)!.GetValue(main)!;
        settings.GetType().GetField("DragPreview", All)!.SetValue(settings, false);
        ((CheckBox)main.FindName("FollowPlayCheck")).IsChecked = true;
        type.GetField("lastEditorState", All)!.SetValue(main, Enum.Parse(type.Assembly.GetType("MajdataEdit.EditorControlMethod")!, "Stop"));
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        Environment.CurrentDirectory = workingDirectory;
        var process = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        var parsed = process.GetField("notelist", All)!.GetValue(null);
        var dragTimes = new List<double>();
        var inputTimes = new List<double>();
        var renderTimes = new List<double>();
        var layoutTimes = new List<double>();
        var textView = ((ICSharpCode.AvalonEdit.TextEditor)main.FindName("FumenContent")).TextArea.TextView;
        var transforms = textView.LineTransformers.Select(transform => new TimedTransformer(transform)).ToArray();
        textView.LineTransformers.Clear();
        foreach (var transform in transforms) textView.LineTransformers.Add(transform);
        for (var index = 0; index < 60; index++)
        {
            type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)(35d + index * .15));
            var dragWatch = Stopwatch.StartNew();
            type.GetMethod("ScrollWave", All)!.Invoke(main, new object[] { 1d, true });
            inputTimes.Add(dragWatch.Elapsed.TotalMilliseconds);
            var layoutWatch = Stopwatch.StartNew();
            host.UpdateLayout(); textView.EnsureVisualLines();
            layoutTimes.Add(layoutWatch.Elapsed.TotalMilliseconds);
            var renderWatch = Stopwatch.StartNew();
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            renderTimes.Add(renderWatch.Elapsed.TotalMilliseconds);
            dragTimes.Add(dragWatch.Elapsed.TotalMilliseconds);
        }
        if (!ReferenceEquals(parsed, process.GetField("notelist", All)!.GetValue(null))) throw new Exception("Following the dragged timeline must not reparse the chart");
        dragTimes.Sort();
        Console.WriteLine($"FOLLOW DRAG PASS: mean={dragTimes.Average():F2}ms, p95={dragTimes[57]:F2}ms, no full-chart reparse.");
        Console.WriteLine($"FOLLOW DRAG STAGES: input={inputTimes.Average():F2}ms, queued render={renderTimes.Average():F2}ms.");
        Console.WriteLine($"FOLLOW DRAG TEXT: layout={layoutTimes.Average():F2}ms, colorize total={transforms.Sum(item => item.Elapsed):F2}ms, calls={transforms.Sum(item => item.Calls)}.");
        var ghostField = type.GetField("ghostCusorPositionTime", All)!;
        var ghost = ghostField.GetValue(main);
        foreach (var marker in new[] { true, false })
        {
            if (!marker) ghostField.SetValue(main, -999999f);
            var drawWatch = Stopwatch.StartNew();
            for (var index = 0; index < 20; index++) type.GetMethod("DrawWaveCore", All)!.Invoke(main, null);
            Console.WriteLine($"DENSE WAVE DRAW marker={marker}: {drawWatch.Elapsed.TotalMilliseconds / 20:F2}ms");
        }
        ghostField.SetValue(main, ghost);
        VerifyWaveNoteEditing(main, host, row);
        VerifyFileTabs(main, host, tree, sidebar);
        if (Environment.GetCommandLineArgs().Any(arg => arg is "--ui-072" or "--ui-073" or "--ui-074" or "--ui-075" or "--ui-077")) VerifyUi072(main, host, content, type);
        if (Environment.GetCommandLineArgs().Contains("--wave-087")) VerifyFixedWave(main, host, type);
        host.Content = null; main.Content = content; host.Close();
        Console.WriteLine("PANE LAYOUT PASS: left controls, readable lane threshold, image preview and permanent pet target.");
    }

    private static void VerifyFixedWave(MainWindow main, Window host, Type type)
    {
        var process = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { "(120){4},,,,", 0L });
        var image = (Image)main.FindName("MusicWave");
        var row = (RowDefinition)type.GetField("waveformRow", All)!.GetValue(main)!;
        row.Height = new GridLength(180); host.UpdateLayout();
        var samples = Enumerable.Range(0, 3).Select(_ => Enumerable.Repeat((short)22000, 100000).ToArray()).ToArray();
        type.GetMethod("InstallSongWaveforms", All)!.Invoke(main, new object[] { samples, 180d });
        type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)20d);
        var draw = type.GetMethod("DrawWaveCore", All)!;
        draw.Invoke(main, null);
        var bars = type.GetField("waveformBars", All)!.GetValue(main);
        var viewport = type.GetMethod("CreateWaveViewport", All)!;
        double Scale()
        {
            var value = viewport.Invoke(main, new object[] { 20d, (int)image.ActualWidth })!;
            return (double)value.GetType().GetProperty("PixelsPerSecond")!.GetValue(value)!;
        }
        var scale = Scale();
        var backing = type.GetField("WaveBitmap", All)!.GetValue(main);
        var widths = new HashSet<int>();
        var painted = new HashSet<int>();
        var frameCosts = new List<string>();
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { false, false }); host.UpdateLayout();
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, true });
        PumpAnimations(host, () =>
        {
            widths.Add((int)image.ActualWidth);
            var cost = Stopwatch.StartNew();
            draw.Invoke(main, null);
            painted.Add((int)image.Source.Width);
            frameCosts.Add($"{image.ActualWidth:F0}/{cost.Elapsed.TotalMilliseconds:F1}ms");
            if (!ReferenceEquals(bars, type.GetField("waveformBars", All)!.GetValue(main)) || Math.Abs(Scale() - scale) > .001)
                throw new Exception("Sidebar changed the audio samples or pixels/second");
        });
        if (painted.Count < 3) throw new Exception("Wave image froze during the sidebar animation: " + string.Join(',', frameCosts.Take(25)));
        foreach (var height in new[] { 120, 230, 170 })
        {
            row.Height = new GridLength(height); host.UpdateLayout(); draw.Invoke(main, null);
            var source = (System.Windows.Media.DrawingImage)image.Source;
            var backingSource = ((System.Windows.Media.ImageDrawing)((System.Windows.Media.DrawingGroup)source.Drawing).Children[0]).ImageSource;
            if (Math.Abs(source.Height - image.ActualHeight) > .01 || !ReferenceEquals(backing, backingSource))
                throw new Exception("Vertical resize replaced the backing surface or displayed an empty/wrong-size bitmap");
        }
        var nativeDpi = System.Windows.Media.VisualTreeHelper.GetDpi(image);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(image.ActualWidth * nativeDpi.DpiScaleX),
            (int)Math.Round(image.ActualHeight * nativeDpi.DpiScaleY), 96 * nativeDpi.DpiScaleX, 96 * nativeDpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(image);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.GetFullPath("alpha/.codex-build/087-wave.png"))) encoder.Save(output);
        var pixels = new byte[bitmap.PixelWidth * 4];
        bitmap.CopyPixels(new Int32Rect(0, bitmap.PixelHeight / 2 - 10, bitmap.PixelWidth, 1), pixels, pixels.Length, 0);
        var background = ((System.Windows.Media.SolidColorBrush)main.FindResource("WaveBackground")).Color;
        var runs = new List<int>(); var run = 0;
        for (var x = 0; x < bitmap.PixelWidth; x++)
        {
            var offset = x * 4;
            if (pixels[offset] != background.B || pixels[offset + 1] != background.G || pixels[offset + 2] != background.R) run++;
            else if (run > 0) { runs.Add(run); run = 0; }
        }
        if (runs.Count < 20 || runs.Count(length => length <= 2) < runs.Count * .9)
            throw new Exception("Actual waveform pixels became a filled silhouette: " + string.Join(',', runs));
        byte[] ReadWaveRow()
        {
            var frame = new System.Windows.Media.Imaging.RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight,
                96 * nativeDpi.DpiScaleX, 96 * nativeDpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
            frame.Render(image);
            var result = new byte[frame.PixelWidth * 4];
            frame.CopyPixels(new Int32Rect(0, frame.PixelHeight / 2 - 10, frame.PixelWidth, 1), result, result.Length, 0);
            return result;
        }
        var baseline = ReadWaveRow();
        foreach (var offset in new[] { .15, .3, .5, .75, 1d })
        {
            type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)(20d + offset / scale / nativeDpi.DpiScaleX));
            draw.Invoke(main, null); var moved = ReadWaveRow();
            double Difference(int shift)
            {
                var different = 0; var count = 0;
                for (var x = 5; x < bitmap.PixelWidth - 6; x++)
                {
                    if (Math.Abs(x - bitmap.PixelWidth / 2) < 6) continue;
                    count++;
                    if (!moved.AsSpan(x * 4, 3).SequenceEqual(baseline.AsSpan((x + shift) * 4, 3))) different++;
                }
                return (double)different / count;
            }
            if (Math.Min(Difference(0), Difference(1)) > .05 || offset == 1d && Difference(1) > .05)
                throw new Exception($"Wave columns changed spacing while panning {offset:F2}px: same={Difference(0):P1}, shifted={Difference(1):P1}");
        }
        type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)20d);
        Console.WriteLine("088 WAVE PAN PASS: five subpixel positions preserve the entire bar pattern as a single pixel translation.");
        var originalDpi = System.Windows.Media.VisualTreeHelper.GetDpi(host);
        foreach (var dpi in new[] { 1.25, 1.5, 2d })
        {
            System.Windows.Media.VisualTreeHelper.SetRootDpi(host, new System.Windows.DpiScale(dpi, dpi));
            host.InvalidateMeasure(); host.InvalidateArrange(); image.InvalidateMeasure();
            host.UpdateLayout();
            Console.WriteLine($"089 DPI FIXTURE: requested={dpi}, actual={System.Windows.Media.VisualTreeHelper.GetDpi(image).DpiScaleX}, width={image.ActualWidth}, physical width={image.ActualWidth * dpi}.");
            type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)20d); draw.Invoke(main, null);
            byte[] RowAtDpi(bool raw = false)
            {
                var width = (int)Math.Round(image.ActualWidth * dpi);
                var height = (int)Math.Round(image.ActualHeight * dpi);
                if (raw)
                {
                    var backingImage = (System.Windows.Media.Imaging.WriteableBitmap)type.GetField("WaveBitmap", All)!.GetValue(main)!;
                    var bytes = new byte[width * 4];
                    backingImage.CopyPixels(new Int32Rect(0, height / 2 - (int)(10 * dpi), width, 1), bytes, bytes.Length, 0);
                    return bytes;
                }
                var frame = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(image.ActualWidth * dpi),
                    (int)Math.Round(image.ActualHeight * dpi), 96 * dpi, 96 * dpi, System.Windows.Media.PixelFormats.Pbgra32);
                var visual = new System.Windows.Media.DrawingVisual();
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(visual, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                System.Windows.Media.VisualTreeHelper.SetRootDpi(visual, new System.Windows.DpiScale(dpi, dpi));
                using (var context = visual.RenderOpen()) context.DrawImage(image.Source, new Rect(0, 0, image.ActualWidth, image.ActualHeight));
                frame.Render(visual);
                var rowPixels = new byte[frame.PixelWidth * 4];
                frame.CopyPixels(new Int32Rect(0, frame.PixelHeight / 2 - (int)(10 * dpi), frame.PixelWidth, 1), rowPixels, rowPixels.Length, 0);
                return rowPixels;
            }
            var originalRow = RowAtDpi();
            var originalRaw = RowAtDpi(true);
            foreach (var delta in new[] { .2, .5, 1d, 2d })
            {
                type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)(20 + delta / scale / dpi));
                draw.Invoke(main, null); var movedRow = RowAtDpi(); var movedRaw = RowAtDpi(true);
                double DifferenceAt(int shift, bool raw = false)
                {
                    var mismatch = 0; var total = 0; var columns = movedRow.Length / 4;
                    for (var x = 8; x < columns - 10; x++)
                    {
                        if (Math.Abs(x - columns / 2) < 10 * dpi) continue;
                        total++;
                        if (!(raw ? movedRaw : movedRow).AsSpan(x * 4, 3).SequenceEqual((raw ? originalRaw : originalRow).AsSpan((x + shift) * 4, 3))) mismatch++;
                    }
                    return (double)mismatch / total;
                }
                var best = Enumerable.Range(0, 4).Min(shift => DifferenceAt(shift));
                var bestRaw = Enumerable.Range(0, 4).Min(shift => DifferenceAt(shift, true));
                Console.WriteLine($"089 DPI TRANSLATION: dpi={dpi}, delta={delta}, raw={bestRaw:P2}, render={best:P2}, time={type.GetMethod("GetTimelinePosition", All)!.Invoke(main, null)}, pps={Scale()}.");
                if (best > .05 || bestRaw > .05) throw new Exception($"Wave pattern flickered at {dpi:P0}, delta={delta}: raw={bestRaw:P1}, rendered={best:P1} changed pixels");
            }
            Console.WriteLine($"089 DPI PAN PASS: {dpi:P0}, four fractional translations retain the full waveform pixel pattern.");
        }
        System.Windows.Media.VisualTreeHelper.SetRootDpi(host, originalDpi);
        host.UpdateLayout();
        type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)20d); draw.Invoke(main, null);
        var summary = (StackPanel)main.FindName("DiagnosticsSummary");
        var transport = (Panel)summary.Parent;
        var indexOfSummary = transport.Children.IndexOf(summary);
        var before = (Border)transport.Children[indexOfSummary - 1]; var after = (Border)transport.Children[indexOfSummary + 1];
        var leftGap = summary.TranslatePoint(new Point(), host).X - before.TranslatePoint(new Point(before.ActualWidth, 0), host).X;
        var rightGap = after.TranslatePoint(new Point(), host).X - summary.TranslatePoint(new Point(summary.ActualWidth, 0), host).X;
        if (Math.Abs(leftGap - rightGap) > 1) throw new Exception($"Diagnostics separator gaps differ: {leftGap}/{rightGap}");
        Console.WriteLine($"087 WAVE PASS: samples retained; scale={scale:F2}px/s; {painted.Count} painted widths during sidebar transition; reusable surface for 3 heights; {runs.Count} separated bar runs; diagnostics gaps={leftGap:F1}/{rightGap:F1}px.");
        VerifyWaveLeadIn(main, type);
    }

    private static void VerifyWaveLeadIn(MainWindow main, Type type)
    {
        var cursor = type.GetField("flowTimelineCursor", All)!;
        var previousCursor = cursor.GetValue(main);
        var previousCover = main.editorSetting.ShowSongDetail;
        var accent = ((System.Windows.Media.SolidColorBrush)main.FindResource("WaveAccent")).Color;
        var draw = type.GetMethod("DrawWaveCore", All)!;
        foreach (var (cover, time) in new[] { (true, -5d), (true, -1d), (true, .55d), (false, .55d) })
        {
            main.editorSetting.ShowSongDetail = cover;
            cursor.SetValue(main, (double?)time);
            draw.Invoke(main, null);
            var surface = (System.Windows.Media.Imaging.WriteableBitmap)type.GetField("WaveBitmap", All)!.GetValue(main)!;
            var width = (int)type.GetField("wavePixelWidth", All)!.GetValue(main)!;
            var height = (int)type.GetField("wavePixelHeight", All)!.GetValue(main)!;
            var scale = (double)type.GetField("waveRasterScaleX", All)!.GetValue(main)!;
            var viewport = type.GetMethod("CreateWaveViewport", All)!.Invoke(main, new object[] { time, width / scale })!;
            var zero = (float)viewport.GetType().GetMethod("TimeToX")!.Invoke(viewport, new object[] { 0d })! * scale;
            var last = Math.Min(width - 2, (int)Math.Floor(zero) - 2);
            var pixels = new byte[(last + 1) * 4];
            surface.CopyPixels(new Int32Rect(0, height / 2, last + 1, 1), pixels, pixels.Length, 0);
            for (var x = 3; x <= last; x++)
            {
                if (Math.Abs(x - width / 2) < 5) continue; // red playhead
                if (pixels[x * 4] != accent.B || pixels[x * 4 + 1] != accent.G || pixels[x * 4 + 2] != accent.R)
                    throw new Exception($"Wave lead-in broke at pixel {x}, time={time}, cover={cover}");
            }
        }
        main.editorSetting.ShowSongDetail = previousCover;
        cursor.SetValue(main, previousCursor);
        Console.WriteLine("111 WAVE LEAD-IN PASS: a continuous centre line across recording load, transition and cover off.");
    }

    private static void VerifyUi072(MainWindow main, Window host, FrameworkElement content, Type type)
    {
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = new System.Globalization.CultureInfo("zh-CN");
        var editor = (TextEditor)main.FindName("FumenContent");
        var themeType = type.Assembly.GetType("MajdataEdit.ThemeManager")!;
        var round073 = Environment.GetCommandLineArgs().Contains("--ui-073");
        var round074 = Environment.GetCommandLineArgs().Contains("--ui-074");
        var round075 = Environment.GetCommandLineArgs().Contains("--ui-075");
        var round077 = Environment.GetCommandLineArgs().Contains("--ui-077");
        if (round073 || round074 || round075 || round077)
        {
            var decoration = (FrameworkElement)main.FindName("DecorationBgHost");
            var decorationType = type.Assembly.GetType("MajdataEdit.EditorDecorationBg")!;
            decorationType.GetMethod("Apply", All)!.Invoke(null, new object[] { decoration, "default" });
        }
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        var window = new EditorSettingPanel(page: EditorSettingPanel.SettingsPage.View);
        type.GetMethod("ShowTool", All)!.Invoke(main, new object[] { window, "View 设置" });
        type.GetMethod("SetChartFilesVisible", All)!.Invoke(main, new object[] { true, false });
        host.Width = 1200; host.Height = 860; host.UpdateLayout(); PumpAnimations(host);
        var column = (ColumnDefinition)type.GetField("chartFilesColumn", All)!.GetValue(main)!;
        var grip = (GridSplitter)type.GetField("chartFilesSplitter", All)!.GetValue(main)!;
        column.Width = new GridLength(260); host.UpdateLayout();
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(40, 0, false)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        type.GetMethod("SetChartFilesVisible", All)!.Invoke(main, new object[] { false, false });
        type.GetMethod("SetChartFilesVisible", All)!.Invoke(main, new object[] { true, false });
        host.UpdateLayout();
        if (Math.Abs(column.ActualWidth - 260) > 1) throw new Exception("File pane must remember dragged width");
        foreach (var name in new[] { "dark", "light" })
        {
            var theme = themeType.GetMethod("LoadThemeByName", All)!.Invoke(null, new object[] { name })!;
            type.GetMethod("PreviewEditorTheme", All)!.Invoke(main, new[] { theme });
            if ((round074 || round075 || round077) && name == "dark" && main.Background.ToString() == editor.Background.ToString())
                throw new Exception("Editor text surface must remain distinct from the main background");
            host.UpdateLayout(); PumpAnimations(host);
            if (theme.ToString() != (name == "dark" ? "深色" : "浅色"))
                throw new Exception("Chinese built-in theme labels must be localized");
            foreach (var bar in Descendants(editor).OfType<System.Windows.Controls.Primitives.ScrollBar>())
                if (bar.Background is System.Windows.Media.SolidColorBrush brush && brush.Color.A != 0)
                    throw new Exception("Actual AvalonEdit scrollbar still paints an opaque background");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.GetFullPath(".codex-build/" + (round077 ? "080" : round075 ? "075" : round074 ? "074" : round073 ? "073" : "072") + "-editor-" + name + ".png"));
            encoder.Save(stream);
        }
        typeof(EditorSettingPanel).GetField("settingsApplied", All)!.SetValue(window, true); window.Close();
        Console.WriteLine("UI 072 PASS: editor alignment, real AvalonEdit scrollbar transparency, resizable file pane, dark/light rendering and Chinese labels.");
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }

    private static void VerifyFileTabs(MainWindow main, Window host, TreeView tree, TabControl right)
    {
        var type = typeof(MainWindow);
        var left = (TabControl)type.GetField("chartFilesPane", All)!.GetValue(main)!;
        var files = (Button)type.GetField("chartFilesButton", All)!.GetValue(main)!;
        var tab = (TabItem)type.GetField("chartFilesTab", All)!.GetValue(main)!;
        var activeLine = (Border)tab.Template.FindName("activeLine", tab);
        if (!activeLine.IsVisible || Math.Abs(activeLine.ActualWidth - tab.ActualWidth) > .001)
            throw new Exception("The file page needs the same full-width active underline as right sidebar pages");
        if (System.Windows.Markup.XamlWriter.Save(left.Template) != System.Windows.Markup.XamlWriter.Save(right.Template) ||
            left.FontFamily.Source != right.FontFamily.Source)
            throw new Exception("Left and right pages must use the same sidebar appearance");
        ((Panel)tab.Header).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCollapse(left);
        if (left.Items.Count != 0 || files.Tag is not false) throw new Exception("Closing the file tab must release its sidebar");
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpAnimations(host);
        if (left.SelectedItem != tab || tree.Items.Count != 1) throw new Exception("Toolbar must reopen the file page and tree");
        var content = tab.Content;
        type.GetMethod("DetachChartFiles", All)!.Invoke(main, new object[] { tab, host.PointToScreen(new Point(40, 80)) });
        var floating = (Window)type.GetField("detachedChartFiles", All)!.GetValue(main)!;
        floating.Hide();
        if (floating.Content != content || floating.Owner != host || tab.Content != null || left.Items.Count != 0 || left.IsVisible)
            throw new Exception("Dragging the file tab must move its existing tree into a single owned window");
        var chartFolder = MainWindow.maidataDir;
        var otherFolder = Path.GetFullPath(".codex-build/070-floating-chart");
        Directory.CreateDirectory(otherFolder);
        MainWindow.maidataDir = otherFolder;
        type.GetMethod("RefreshChartFiles", All)!.Invoke(main, null);
        if (tree.Items.Count != 1 || ((TreeViewItem)tree.Items[0]).Tag as string != otherFolder)
            throw new Exception("Floating file tree must follow the newly selected chart");
        MainWindow.maidataDir = chartFolder;
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (left.Items.Count != 0 || type.GetField("detachedChartFiles", All)!.GetValue(main) != floating)
            throw new Exception("Clicking files while floating must reuse that window");
        floating.Close();
        if (tab.Content != content || type.GetField("detachedChartFiles", All)!.GetValue(main) != null)
            throw new Exception("Closing a detached page must release its window and retain reusable content");
        files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpAnimations(host);
        if (left.SelectedItem != tab || tab.Content != content) throw new Exception("A closed floating page must reopen in the sidebar");
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false }); host.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(950, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render((System.Windows.Media.Visual)host.Content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.GetFullPath(".codex-build/070-sidebar-tabs.png"))) encoder.Save(output);
        Console.WriteLine("FILE TAB PASS: close/reopen, detach, current-chart refresh, single window and reopen after closing.");
    }

    private static void VerifyWaveNoteEditing(MainWindow main, Window host, RowDefinition row)
    {
        var type = typeof(MainWindow);
        var editor = (TextEditor)main.FindName("FumenContent");
        var image = (Image)main.FindName("MusicWave");
        var settings = type.GetField("editorSetting", All)!.GetValue(main)!;
        var enabled = settings.GetType().GetField("EnableVisualChartEditor", All)!;
        var process = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetField("first", All)!.SetValue(null, 0f);
        type.GetField("lastEditorState", All)!.SetValue(main, Enum.Parse(type.Assembly.GetType("MajdataEdit.EditorControlMethod")!, "Stop"));
        type.GetField("flowTimelineCursor", All)!.SetValue(main, (double?)1d);
        row.Height = new GridLength(200); host.UpdateLayout();
        const string source = "(120){8},,,,\n{4},,,,\n";
        type.GetMethod("SetRawFumenText", All)!.Invoke(main, new object[] { source });
        editor.Document.UndoStack.ClearAll();
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { source, 0L });
        type.GetField("chartParsePending", All)!.SetValue(main, false);
        var point = new Point(image.ActualWidth * 3.5 / 8d + 1, 8 + 3 * (image.ActualHeight - 25) / 8 + 1);
        bool Insert() => (bool)type.GetMethod("TryInsertWaveNote", All)!.Invoke(main, new object[] { point })!;
        enabled.SetValue(settings, false);
        if (Insert() || editor.Text != source) throw new Exception("Disabled visual editing must not write waveform notes");
        enabled.SetValue(settings, true);
        if (!Insert() || editor.Text != "(120){8},,3,,\n{4},,,,\n")
            throw new Exception("Wave click must snap to the nearest authored division and lane: " + editor.Text);
        if (!Insert() || !editor.Text.Contains("3h[8:1]")) throw new Exception("Repeated waveform clicks must use the View note cycle");
        var undo = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
        type.GetMethod("MusicWave_PreviewMouseRightButtonDown", All)!.Invoke(main, new object[] { image, undo });
        if (!undo.Handled || editor.Text != "(120){8},,3,,\n{4},,,,\n") throw new Exception("Waveform right-click must undo the preceding generated note");
        const string prefixed = "(120){8}<COLOR*FFFFFF>,,,,\n{4},,,,\n";
        type.GetMethod("SetRawFumenText", All)!.Invoke(main, new object[] { prefixed });
        type.GetField("chartParsePending", All)!.SetValue(main, true);
        point.X = image.ActualWidth * 3 / 8d;
        if (!Insert() || !Insert() || editor.Text != "(120){8}<COLOR*FFFFFF>3h[8:1],,,,\n{4},,,,\n")
            throw new Exception("First-slot editing must preserve BPM/division/Alpha prefixes and note cycling: " + editor.Text);
        const string overlay = "(120)\n@{8},,,,\n{4},,,,\n";
        type.GetMethod("SetRawFumenText", All)!.Invoke(main, new object[] { overlay });
        editor.CaretOffset = overlay.IndexOf("@{8}", StringComparison.Ordinal) + 4;
        type.GetField("chartParsePending", All)!.SetValue(main, true);
        point.X = image.ActualWidth * 3.5 / 8d;
        if (!Insert() || editor.Text != "(120)\n@{8},,3,,\n{4},,,,\n")
            throw new Exception("Shared grid times must edit the caret's current stream without moving the main stream: " + editor.Text);
        const string block = "(120)\n@*{8},,,,*@\n|| comment\n{4},,,,\n";
        type.GetMethod("SetRawFumenText", All)!.Invoke(main, new object[] { block });
        editor.CaretOffset = block.IndexOf("{4}", StringComparison.Ordinal) + 3;
        type.GetField("chartParsePending", All)!.SetValue(main, true);
        point.X = image.ActualWidth * 3 / 8d;
        if (!Insert() || editor.Text != "(120)\n@*{8},,,,*@\n|| comment\n{4}3,,,,\n")
            throw new Exception("Writing the main stream must preserve preceding stream markers and comments: " + editor.Text);
        var before = editor.Text;
        row.Height = new GridLength(80); host.UpdateLayout();
        if (Insert() || editor.Text != before) throw new Exception("Compact waveform must retain its original drag-only behavior");
        Console.WriteLine("WAVE EDIT PASS: snapped division/lane, note cycling, right-click undo, control prefixes, overlays and toggle/height gates.");
    }

    private static void VerifySectionTints(Assembly assembly)
    {
        var editor = new TextEditor { FontSize = 14, Text = "@FF0000\n1,2,3,4,\n@NULL\n5,6," };
        var renderer = (ICSharpCode.AvalonEdit.Rendering.IBackgroundRenderer)Activator.CreateInstance(
            assembly.GetType("MajdataEdit.Editor.ColorSectionBackgroundRenderer")!, All, null, new object[] { editor }, null)!;
        var host = new Window { Content = editor, Width = 400, Height = 200, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        byte[] Pixel()
        {
            host.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen()) renderer.Draw(editor.TextArea.TextView, context);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(400, 200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(100, 3, 1, 1), pixel, 4, 0);
            return pixel;
        }
        var red = Pixel();
        editor.Document.Replace(1, 6, "00FF00");
        var green = Pixel();
        editor.Document.Replace(0, 7, "@NULL");
        var clear = Pixel();
        editor.Document.UndoStack.Undo();
        var undo = Pixel();
        host.Width = 350;
        var resized = Pixel(); host.Close();
        if (red[2] == 0 || green[1] == 0 || green[2] != 0 || clear[3] != 0 || !undo.SequenceEqual(green) || !resized.SequenceEqual(green))
            throw new Exception("Section tint must update after edit/undo and remain correct across resize");
        Console.WriteLine("SECTION TINT PASS: edit, reset, undo and resize retain the correct color.");
    }

    private static void PumpAnimations(Window host, Action? sample = null)
    {
        var directory = Environment.CurrentDirectory;
        var watch = Stopwatch.StartNew();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var lastRender = TimeSpan.MinValue;
        EventHandler rendered = (_, args) =>
        {
            var time = ((System.Windows.Media.RenderingEventArgs)args).RenderingTime;
            if (time != lastRender) { lastRender = time; sample?.Invoke(); }
        };
        System.Windows.Media.CompositionTarget.Rendering += rendered;
        timer.Tick += (_, _) =>
        {
            if (watch.ElapsedMilliseconds > 250) frame.Continue = false;
        };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        System.Windows.Media.CompositionTarget.Rendering -= rendered;
        Environment.CurrentDirectory = directory;
    }

    private sealed class TimedTransformer(ICSharpCode.AvalonEdit.Rendering.IVisualLineTransformer inner)
        : ICSharpCode.AvalonEdit.Rendering.IVisualLineTransformer
    {
        public double Elapsed;
        public int Calls;
        public void Transform(ICSharpCode.AvalonEdit.Rendering.ITextRunConstructionContext context,
            IList<ICSharpCode.AvalonEdit.Rendering.VisualLineElement> elements)
        {
            var watch = Stopwatch.StartNew(); inner.Transform(context, elements);
            Elapsed += watch.Elapsed.TotalMilliseconds; Calls++;
        }
    }

    private sealed class TimedRenderer(ICSharpCode.AvalonEdit.Rendering.IBackgroundRenderer inner)
        : ICSharpCode.AvalonEdit.Rendering.IBackgroundRenderer
    {
        public string Name => inner.GetType().Name;
        public double Elapsed;
        public int Calls;
        public ICSharpCode.AvalonEdit.Rendering.KnownLayer Layer => inner.Layer;
        public void Draw(ICSharpCode.AvalonEdit.Rendering.TextView view, System.Windows.Media.DrawingContext context)
        {
            var watch = Stopwatch.StartNew(); inner.Draw(view, context);
            Elapsed += watch.Elapsed.TotalMilliseconds; Calls++;
        }
    }

    private static void VerifyRecordedAudio(MainWindow main, Type process)
    {
        var folder = Path.GetFullPath(".codex-build/061-record-range");
        var before = Environment.CurrentDirectory;
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var effects = Path.Combine(folder, "SFX"); Directory.CreateDirectory(effects);
        foreach (var effect in Directory.EnumerateFiles(Path.GetFullPath("alpha/MajdataEdit/SFX"), "*.wav"))
            File.Copy(effect, Path.Combine(effects, Path.GetFileName(effect)), true);
        Directory.CreateDirectory(Path.Combine(folder, "MajdataView_Data", "StreamingAssets"));
        Environment.SetEnvironmentVariable("PATH", Path.GetFullPath("alpha/Assets/StreamingAssets") + Path.PathSeparator + oldPath);
        MainWindow.maidataDir = folder;
        typeof(MainWindow).GetField("songLength", All)!.SetValue(main, 6d);
        process.GetField("first", All)!.SetValue(null, 0f);
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { "(120){4}1,2,3,4,5,6,7,8,1,2,3,4,", 0L });
        typeof(MainWindow).GetMethod("generateSoundEffectList", All)!.Invoke(main, new object[] { 0d, false });
        var render = typeof(MainWindow).GetMethod("renderSoundEffect", All)!;
        var rangeType = typeof(MainWindow).Assembly.GetType("MajdataEdit.MediaRange")!;
        try
        {
            Environment.CurrentDirectory = folder;
            render.Invoke(main, new object?[] { 0d, true, null });
            var output = (string)typeof(MainWindow).Assembly.GetType("MajdataCore.AlphaSession")!.GetMethod("ChartWorkFile")!.Invoke(null, new object[] { folder, "out.wav" })!;
            var full = File.ReadAllBytes(output);
            render.Invoke(main, new object?[] { 0d, true, Activator.CreateInstance(rangeType, 2d, 4d) });
            var cut = File.ReadAllBytes(output);
            var frequency = BitConverter.ToInt32(cut,24);
            var bytesPerSecond = frequency * 4;
            if (cut.Length != 44 + 2 * bytesPerSecond || BitConverter.ToInt32(cut,40) != 2 * bytesPerSecond ||
                !cut.AsSpan(44).SequenceEqual(full.AsSpan(44 + 2 * bytesPerSecond, 2 * bytesPerSecond)))
                throw new Exception("Marked recording must mix exactly the corresponding BGM + SFX samples");
            Console.WriteLine("RECORD AUDIO PASS: 2-4s matches the full stereo mix sample-for-sample, correct WAV duration/header.");
            File.WriteAllText(Path.Combine(folder, "media_timeline.json"), "{\"Version\":1,\"Clips\":[{\"Track\":1,\"TrackIndex\":0,\"SourcePath\":\"track.mp3\",\"TimelineStart\":0,\"SourceOffset\":0,\"SourceDuration\":6,\"Duration\":6,\"Volume\":1}]}");
            process.GetMethod("Serialize", All)!.Invoke(null, new object[] { "(120){4}<AUDIO*(True,track.mp3)>1,2,", 0L });
            var events = (System.Collections.IEnumerable)typeof(MainWindow).GetMethod("GetEffectiveMediaTable", All)!.Invoke(main, null)!;
            if (!events.Cast<object>().Any(item => (string)item.GetType().GetField("kind")!.GetValue(item)! == "audio" &&
                !(bool)item.GetType().GetField("timelineClip")!.GetValue(item)!)) throw new Exception("Timeline must not discard AUDIO commands");
            File.Delete(Path.Combine(folder, "media_timeline.json"));
            Console.WriteLine("AUDIO EVENTS PASS: AUDIO retained alongside the default audio timeline.");
        }
        finally { Environment.CurrentDirectory = before; Environment.SetEnvironmentVariable("PATH", oldPath); }
    }

    private static void VerifyClipTiming(Assembly assembly)
    {
        var timeline = new MediaTimelineEditor();
        var clipType = assembly.GetType("MajdataEdit.MediaTimelineClip")!;
        var clip = Activator.CreateInstance(clipType)!;
        void Set(string name, object value) => clipType.GetProperty(name)!.SetValue(clip, value);
        Set("Track", Enum.Parse(assembly.GetType("MajdataEdit.MediaTrackKind")!, "Video"));
        Set("Name", "Timing regression"); Set("TimelineStart", 1d); Set("Duration", 8d);
        Set("SourceOffset", 2d); Set("SourceDuration", 20d);
        var timelineType = typeof(MediaTimelineEditor);
        var folder = Path.GetFullPath(".codex-build/061-clip-timing"); Directory.CreateDirectory(folder);
        timelineType.GetField("chartDirectory", All)!.SetValue(timeline, folder);
        var project = timelineType.GetField("project", All)!.GetValue(timeline)!;
        ((System.Collections.IList)project.GetType().GetProperty("Clips")!.GetValue(project)!).Add(clip);
        timelineType.GetField("selectedClip", All)!.SetValue(timeline, clip);
        var infoType = assembly.GetType("MajdataEdit.MediaClipInfoWindow")!;
        var info = (Window)Activator.CreateInstance(infoType, All, null, new object[] { timeline }, null)!;
        var content = (FrameworkElement)info.Content;
        content.Measure(new Size(330, 600)); content.Arrange(new Rect(0, 0, 330, 600)); content.UpdateLayout();
        var sourceLength = (TextBlock)infoType.GetField("sourceLength", All)!.GetValue(info)!;
        var endInput = (TextBox)infoType.GetField("end", All)!.GetValue(info)!;
        if (sourceLength.TranslatePoint(new Point(), content).Y <= endInput.TranslatePoint(new Point(0, endInput.ActualHeight), content).Y)
            throw new Exception("Source length must be below timeline inputs and appearance controls");
        ((TextBox)infoType.GetField("start", All)!.GetValue(info)!).Text = "2.123";
        ((TextBox)infoType.GetField("end", All)!.GetValue(info)!).Text = "5.678";
        infoType.GetMethod("Apply", All)!.Invoke(info, null);
        if (Math.Abs((double)clipType.GetProperty("TimelineStart")!.GetValue(clip)! - 2.123d) > .000001d ||
            Math.Abs((double)clipType.GetProperty("TimelineEnd")!.GetValue(clip)! - 5.678d) > .000001d ||
            (double)clipType.GetProperty("SourceOffset")!.GetValue(clip)! != 2d)
            throw new Exception("Millisecond timeline editing must preserve the source in-point");
        info.Close();
        Console.WriteLine("CLIP TIMING PASS: 2.123–5.678 seconds, source in-point preserved.");
    }
}
