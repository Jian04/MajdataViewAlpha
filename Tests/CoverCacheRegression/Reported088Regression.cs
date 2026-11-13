using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Reported088Regression
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run(bool fontsOnly)
    {
        var artifactRoot = Path.GetFullPath("alpha/.codex-build");
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = CultureInfo.GetCultureInfo("zh-CN");
        if (fontsOnly) { RenderFonts(); return; }
        VerifyParsing();
        VerifyMixedPaths();
        VerifyDensity();
        var main = new MainWindow { editorSetting = new EditorSetting() };
        var type = typeof(MainWindow);
        type.GetMethod("EnsureToolSidebar", All)!.Invoke(main, null);
        var content = (FrameworkElement)main.Content; main.Content = null;
        var host = new Window { Content = content, Width = 1000, Height = 700, Left = -30000, Top = -30000,
            ShowInTaskbar = false, ShowActivated = false, UseLayoutRounding = true };
        host.Show(); host.UpdateLayout();
        var metadata = (FrameworkElement)type.GetField("editorMetadata", All)!.GetValue(main)!;
        metadata.Visibility = Visibility.Visible;
        var selector = (ComboBox)main.FindName("LevelSelector"); selector.SelectedIndex = 4;
        host.UpdateLayout();
        var naturalHeight = metadata.ActualHeight;
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false }); host.UpdateLayout();
        host.Width = 620;
        ((ColumnDefinition)type.GetField("toolSidebarColumn", All)!.GetValue(main)!).Width = new GridLength(350);
        host.UpdateLayout();
        Check(metadata.ActualHeight == naturalHeight && metadata is StackPanel { Orientation: Orientation.Horizontal }, "Metadata must clip instead of wrapping");
        Console.WriteLine($"088 METADATA PASS: one row at {metadata.ActualWidth:F1}px; difficulty width={((FrameworkElement)selector.Parent).ActualWidth:F1}px.");
        host.Width = 1000; type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false }); host.UpdateLayout();
        VerifyTabs(main, host);
        var clock = (Label)main.FindName("TimeLabel"); clock.Content = "0:29";
        var icon = (Button)main.FindName("PlayAndPauseButton"); host.UpdateLayout();
        double PixelCenter(FrameworkElement element)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
                Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var top = bitmap.PixelHeight; var bottom = -1;
            for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
                if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 100) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            return element.TranslatePoint(new Point(0, (top + bottom) / 2d), host).Y;
        }
        var clockCenter = PixelCenter(clock); var iconCenter = PixelCenter(icon);
        Check(Math.Abs(clockCenter - iconCenter) <= 1.5, "Clock glyphs must be vertically centered with the transport icons");
        Console.WriteLine($"088 CLOCK CENTERS: text={clockCenter:F2}, icon={iconCenter:F2}.");
        var ui = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        ui.Render(host);
        var uiEncoder = new PngBitmapEncoder(); uiEncoder.Frames.Add(BitmapFrame.Create(ui));
        using (var output = File.Create(Path.Combine(artifactRoot, "089-ui.png"))) uiEncoder.Save(output);
        foreach (var names in new[] { new[] { "DifficultyLabel", "LevelTextBox" }, new[] { "OffsetLabel", "OffsetTextBox" } })
        {
            var label = (FrameworkElement)main.FindName(names[0]);
            var input = (TextBox)main.FindName(names[1]); input.Text = "14.0000"; host.UpdateLayout();
            var labelCenter = PixelCenter(label); var inputCenter = PixelCenter(input);
            Console.WriteLine($"089 FIELD CENTERS: {names[0]}={labelCenter:F2}, {names[1]}={inputCenter:F2}.");
            Check(Math.Abs(labelCenter - inputCenter) <= 1.5, "Metadata label/input glyph alignment: " + names[0]);
        }
        var settings = new MajdataEdit.EditorSettingPanel(page: MajdataEdit.EditorSettingPanel.SettingsPage.View);
        var checkbox = (CheckBox)settings.FindName("ShowJudgeText");
        Check(checkbox.Template == Application.Current.FindResource("DarkCheck") && checkbox.BorderThickness.Left == 1,
            "Display checkboxes must inherit the complete themed style");
        Check(checkbox.Margin.Left + 1 == 5 && checkbox.Margin.Top >= 7, "Display options must align their checkbox border with the heading and have wider vertical spacing");
        Check(!((UIElement)settings.FindName("EditorBackgroundStyle")).IsEnabled, "Editor background choice must be disabled");
        typeof(MajdataEdit.EditorSettingPanel).GetField("settingsApplied", All)!.SetValue(settings, true); settings.Close();
        var pet = (Button)type.GetField("dockedPetButton", All)!.GetValue(main)!; pet.ApplyTemplate();
        pet.Tag = false; var surface = (Border)pet.Template.FindName("surface", pet); var before = surface.Background.ToString();
        pet.Tag = true; Check(surface.Background.ToString() != before, "Floating pet must highlight its toolbar button");
        pet.Tag = false; Check(surface.Background.ToString() == before, "Docked pet must clear the persistent highlight");
        Console.WriteLine("088 SETTINGS/PET PASS: themed checkbox border, alignment, spacing, disabled background choice, floating/docked highlight.");
        host.Content = null; main.Content = content; host.Close();
    }

    private static void VerifyParsing()
    {
        var process = typeof(MainWindow).Assembly.GetType("MajdataEdit.SimaiProcess")!;
        var main = new MainWindow { editorSetting = new EditorSetting { SongDetailStyle = 0 } };
        var original = MainWindow.maidataDir; MainWindow.maidataDir = Path.GetFullPath("alpha/.codex-build/088-parse");
        var caseIndex = 0;
        foreach (var text in new[] { "(120){4}1,xxx,2,", "(120){4}1,1-5,2,", "(120){4}1,9,2,", "(120){4}1,3/9,2,", "(120){4}1,<SIZE*oops>3,2,", "(120){4}1,<UNKNOWN*1>3,2,", "(120){4}1>5K4[8:1],2,", "(120){4}1P93K5-7[8:1],2," })
        {
            process.GetMethod("Serialize", All)!.Invoke(null, new object[] { text, 0L });
            var data = typeof(MainWindow).GetMethod("BuildSongDetailMajson", All)!.Invoke(main, null)!;
            var json = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(data));
            var notes = json["timingList"]!.SelectMany(timing => timing["noteList"]!).ToArray();
            Check(notes.Any(note => (int)note["startPosition"]! == 2), "Malformed slot suppressed later notes: " + text);
            if (text.Contains("3/9")) Check(notes.Length == 2, "An invalid slot must not retain partial notes");
            File.WriteAllText(MainWindow.maidataDir + "-" + caseIndex++ + ".json", json.ToString());
            Console.WriteLine("088 PARSE: " + text + " => " + notes.Length + " notes; later key 2 retained.");
        }
        MainWindow.maidataDir = original;
    }

    private static void VerifyMixedPaths()
    {
        Check(MajdataCore.SlidePathParser.TryParsePath("1^5K4[8:1]", out var invalid) &&
            !MajdataCore.SlideSyntaxValidator.TryValidate(invalid, out _), "Opposite-key ^ must retain its original restriction");
        foreach (var expression in new[] { "1>5K4[8:1]", "1^4K5[8:1]", "1P93K5-7[8:1]", "1P93K5[8:1]", "1^4-7[8:1]", "1P9A5[8:1]" })
        {
            Check(MajdataCore.SlidePathParser.TryParsePath(expression, out var path), "Mixed path parse: " + expression);
            Check(MajdataCore.SlideSyntaxValidator.TryValidate(path, out var error), expression + ": " + error);
            if (expression.Contains("^4K")) Check(path.segments.Count == 2 && path.segments[0].shape == "^" && path.segments[1].slideCode == "4K5", "FES + SlideCode segments");
            if (expression.Contains("K5-")) Check(path.segments.Count == 2 && path.segments[0].slideCode == "1P93K5" && path.segments[1].endPosition == 7, "SlideCode + normal segments");
            Check(path.segments[^1].duration == "[8:1]", "Mixed path duration must be retained");
            Console.WriteLine("089 PATH PASS: " + expression + " => " + string.Join(" / ", path.segments.Select(s => s.ToExpression(true))));
        }
    }

    private static void VerifyDensity()
    {
        var density = new NoteDensityWindow(new[] { new NoteDensitySample(1, DensityCategory.TapFamily), new NoteDensitySample(2, DensityCategory.SlideBody) }, new[] { .1f, .8f, .3f }, 20, 20, "test");
        var content = (FrameworkElement)density.Content; density.Content = null;
        var host = new Window { Content = content, Width = 800, Height = 340, Left = -30000, Top = -30000, ShowInTaskbar = false };
        host.Show(); host.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        var image = (Image)density.FindName("DensityImage");
        Check(image.ActualHeight > 100 && image.Source?.Height > 100, $"Hosted density plot is missing: {image.ActualHeight}/{image.Source?.Height}");
        Console.WriteLine($"089 DENSITY PASS: hosted image={image.ActualWidth:F1}x{image.ActualHeight:F1}, source={image.Source!.Width}x{image.Source.Height}.");
        host.Content = null; density.Content = content; host.Close(); density.Close();
    }

    private static void VerifyTabs(MainWindow main, Window host)
    {
        var type = typeof(MainWindow);
        var tabs = (TabControl)type.GetField("toolSidebar", All)!.GetValue(main)!;
        for (var index = 0; index < 8; index++)
        {
            tabs.Items.Add(type.GetMethod("CreateSidebarTab", All)!.Invoke(main, new object?[] {
                index == 0 ? "语法帮助" : index == 1 ? "View 设置" : "语法帮助与录制设置 " + index,
                new Grid(), (Action)(() => { }), null }));
            if (index != 1) continue;
            host.UpdateLayout();
            foreach (var tab in tabs.Items.Cast<TabItem>())
            {
                var caption = ((Panel)tab.Header).Children.OfType<TextBlock>().Single();
                var full = new FormattedText(caption.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch),
                    caption.FontSize, Brushes.White, VisualTreeHelper.GetDpi(caption).PixelsPerDip).Width;
                Check(caption.ActualWidth + 1.5 >= full, "A short tab strip must retain full captions when there is spare room");
            }
        }
        host.UpdateLayout();
        var widths = tabs.Items.Cast<TabItem>().Select(tab => tab.ActualWidth).ToArray();
        Check(widths.All(width => width >= 28 && width < 100), "Tabs must shrink before they scroll: " + string.Join(',', widths));
        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            var panel = (Panel)tab.Header; var close = panel.Children.OfType<Button>().Single();
            Check(close.ActualWidth == 20 && close.TranslatePoint(new Point(), tab).X >= 0, "Tab close button became inaccessible");
        }
        Console.WriteLine("088 TABS PASS: eight captions shrink to " + string.Join(',', widths.Select(width => width.ToString("F1"))) + "; close buttons retained.");
    }

    private static void RenderFonts()
    {
        FontFamily FileFont(string path) => Fonts.GetFontFamilies(new Uri(Path.GetDirectoryName(Path.GetFullPath(path)) + "/"),
            "./" + Path.GetFileName(path)).First();
        var fonts = new[] { ("A · 当前 / 思源黑体", (FontFamily)Application.Current.FindResource("UiFontFamily")),
            ("B · MiSans Regular", FileFont("alpha/.codex-build/MiSans-Regular.ttf")),
            ("C · 源泉圆体 Regular", FileFont("alpha/Fonts/GenSenRounded2TW-R.otf")) };
        var root = new StackPanel { Width = 1040, Background = (Brush)Application.Current.FindResource("WindowBackground") };
        foreach (var (name, family) in fonts)
        {
            var body = new StackPanel { Margin = new Thickness(20, 10, 20, 14) };
            body.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, family);
            body.Children.Add(new TextBlock { Text = name, FontSize = 16, Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "文件　编辑　设置　工具　联机　谱面列表　检查更新　Majnet", FontSize = 13, Margin = new Thickness(0, 0, 0, 14) });
            body.Children.Add(new TextBlock { Text = "MASTER　│　难度 14+　│　偏移 -0.5500　　0:29.1234 / 3:12.0000　1.00×", FontSize = 14, Margin = new Thickness(0, 0, 0, 12) });
            var checks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var caption in new[] { "点击生成音符", "拖动预览", "文本跟随", "自动换行", "暂停判定区" })
                checks.Children.Add(new CheckBox { Content = caption, IsChecked = true, FontSize = 13, Margin = new Thickness(0, 0, 20, 0) });
            body.Children.Add(checks);
            body.Children.Add(new TextBlock { Text = "语法错误 0　│　静态无理 0　　动态无理 0　　显示选项　显示判定文字　关闭自动刷新封面", FontSize = 13 });
            root.Children.Add(body);
            root.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.FindResource("MenuSeparator") });
            Console.WriteLine("FONT PREVIEW " + name + " => " + family.Source);
        }
        var window = new Window { Content = root, Width = 1040, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false, UseLayoutRounding = true };
        window.Show(); window.UpdateLayout();
        var image = new RenderTargetBitmap(1040, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.GetFullPath("alpha/.codex-build/088-font-comparison.png"))) encoder.Save(file);
        window.Close();
    }
}
