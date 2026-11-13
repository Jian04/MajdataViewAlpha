using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Ui090Regression
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static void Run()
    {
        var output = Path.GetFullPath("alpha/.codex-build");
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = CultureInfo.GetCultureInfo("zh-CN");
        var main = new MainWindow { editorSetting = new EditorSetting() };
        var type = typeof(MainWindow);
        type.GetMethod("EnsureToolSidebar", All)!.Invoke(main, null);
        var root = (FrameworkElement)main.Content; main.Content = null;
        var host = new Window { Content = root, Width = 1200, Height = 800, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false, UseLayoutRounding = true };
        host.Show(); host.UpdateLayout();
        ((FrameworkElement)type.GetField("editorMetadata", All)!.GetValue(main)!).Visibility = Visibility.Visible;
        var master = (ComboBox)main.FindName("LevelSelector"); master.SelectedIndex = 4;
        ((TextBox)main.FindName("LevelTextBox")).Text = "14+";
        ((TextBox)main.FindName("OffsetTextBox")).Text = "-4.4700";
        ((Label)main.FindName("TimeLabel")).Content = "0:29";
        var tabs = (TabControl)type.GetField("toolSidebar", All)!.GetValue(main)!;
        var tab = (TabItem)type.GetMethod("CreateSidebarTab", All)!.Invoke(main, new object?[] { "语法帮助", new Grid(), (Action)(() => { }), null })!;
        tabs.Items.Add(tab); tab.IsSelected = true;
        type.GetMethod("SetSidebarVisible", All)!.Invoke(main, new object[] { true, false });
        ((ICSharpCode.AvalonEdit.TextEditor)main.FindName("FumenContent")).Text = "谱面文件通常放在歌曲文件夹内。横向笔画：编辑器设置，语法错误，静态无理。\n朋友的酒 14.6 0123456789";
        var chart = (TabItem)type.GetField("chartPage", All)!.GetValue(main)!;
        var chartTitle = (TextBlock)type.GetField("chartPageTitle", All)!.GetValue(main)!;
        chart.Visibility = Visibility.Visible; chartTitle.Text = "MEGALOVANIA (Camellia Remix) " + new string('W', 100);
        host.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(root);
        var frame = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(root.RenderSize));
        frame.Render(visual);
        var fontFrame = new PngBitmapEncoder(); fontFrame.Frames.Add(BitmapFrame.Create(frame));
        using (var fontFile = File.Create(Path.Combine(output, "093-font-current.png"))) fontFrame.Save(fontFile);
        if (Environment.GetCommandLineArgs().Contains("--font-095"))
        {
            Font095Sampling.Run();
            foreach (var element in new FrameworkElement[] { (Menu)main.FindName("MainMenu"),
                (MenuItem)((Menu)main.FindName("MainMenu")).Items[0],
                (Label)main.FindName("ErrCount_Label"),
                ((Panel)tab.Header).Children.OfType<TextBlock>().Single() })
            {
                var family = (FontFamily)element.GetValue(System.Windows.Documents.TextElement.FontFamilyProperty);
                Console.WriteLine($"095 FONT {element.GetType().Name}/{element.Name}: family={family.Source}, maps={family.FamilyMaps.Count}, size={element.GetValue(System.Windows.Documents.TextElement.FontSizeProperty)}, format={TextOptions.GetTextFormattingMode(element)}, render={TextOptions.GetTextRenderingMode(element)}, hint={TextOptions.GetTextHintingMode(element)}, dpi={dpi.DpiScaleY}, pos={element.TranslatePoint(new Point(), root)}");
            }
            host.Content = null; main.Content = root; host.Close();
            return;
        }
        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4]; frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        double InkCenter(FrameworkElement element)
        {
            var at = element.TranslatePoint(new Point(), root);
            var first = frame.PixelHeight; var last = -1;
            for (var y = Math.Max(0, (int)Math.Floor(at.Y * dpi.DpiScaleY)); y < Math.Min(frame.PixelHeight, (int)Math.Ceiling((at.Y + element.ActualHeight) * dpi.DpiScaleY)); y++)
                for (var x = Math.Max(0, (int)Math.Floor(at.X * dpi.DpiScaleX)); x < Math.Min(frame.PixelWidth, (int)Math.Ceiling((at.X + element.ActualWidth) * dpi.DpiScaleX)); x++)
                {
                    var offset = (y * frame.PixelWidth + x) * 4;
                    if (pixels[offset] + pixels[offset + 1] + pixels[offset + 2] <= 350) continue;
                    first = Math.Min(first, y); last = Math.Max(last, y);
                }
            if (last < 0) throw new Exception("No visible ink: " + element.GetType().Name);
            return (first + last + 1) / 2d / dpi.DpiScaleY;
        }
        var play = (Button)main.FindName("PlayAndPauseButton");
        var items = new Dictionary<string, FrameworkElement>
        {
            ["master"] = (TextBlock)master.Tag,
            ["level label"] = (Label)main.FindName("DifficultyLabel"),
            ["level input"] = (TextBox)main.FindName("LevelTextBox"),
            ["play"] = play,
            ["zoom"] = (Button)main.FindName("WaveViewZoomIn"),
            ["syntax label"] = (Label)main.FindName("ErrCount_Label"),
            ["syntax count"] = (Label)main.FindName("ErrCount"),
            ["clock"] = (Label)main.FindName("TimeLabel"),
            ["tab title"] = ((Panel)tab.Header).Children.OfType<TextBlock>().Single(),
            ["tab close"] = ((Panel)tab.Header).Children.OfType<Button>().Single()
        };
        foreach (var item in items) Console.WriteLine($"090 INK {item.Key}: center={InkCenter(item.Value):F3}, top={item.Value.TranslatePoint(new Point(), root).Y:F3}, height={item.Value.ActualHeight:F3}");
        foreach (var names in new[] { new[] { "master", "level label", "level input" }, new[] { "play", "zoom", "syntax label", "syntax count", "clock" }, new[] { "tab title", "tab close" } })
        {
            var centers = names.Select(name => InkCenter(items[name]) - (name.StartsWith("syntax") ? 1 : 0)).ToArray();
            if ((centers.Max() - centers.Min()) * dpi.DpiScaleY > 1.05) throw new Exception("Visible ink is not centered: " + string.Join(",", names));
        }
        foreach (var block in new[] { (TextBlock)master.Tag, items["tab title"] as TextBlock })
        {
            var metrics = new FormattedText(block!.Text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.White, dpi.PixelsPerDip);
            Console.WriteLine($"090 METRICS {block.Text}: height={metrics.Height}, baseline={metrics.Baseline}, ink={metrics.BuildGeometry(new Point()).Bounds}");
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var file = File.Create(Path.Combine(output, "090-ui.png"))) encoder.Save(file);
        Console.WriteLine($"090 CHART TAB: width={chart.ActualWidth:F2}, title width={chartTitle.ActualWidth:F2}, trimming={chartTitle.TextTrimming}");
        if (chart.ActualWidth > 110 || chart.ActualWidth < 65 || chartTitle.TextTrimming != TextTrimming.CharacterEllipsis) throw new Exception("Long chart title was not capped");
        foreach (var (name, value) in new[] { ("LevelTextBox", "14+-？"), ("OffsetTextBox", "-4.4700") })
        {
            var input = (TextBox)main.FindName(name); input.Text = value; host.UpdateLayout();
            var first = input.GetRectFromCharacterIndex(0); var last = input.GetRectFromCharacterIndex(value.Length - 1, true);
            if (first.Left < 0 || last.Right > input.ActualWidth - 1) throw new Exception("Metadata text clipped: " + name);
        }
        var chartClose = ((Panel)chart.Header).Children.OfType<Button>().Single(); chartClose.Tag = true; host.UpdateLayout();
        if (((UIElement)chartClose.Template.FindName("dirty", chartClose)).Visibility != Visibility.Visible ||
            ((UIElement)chartClose.Template.FindName("cross", chartClose)).Visibility != Visibility.Collapsed)
            throw new Exception("Dirty chart must use the small dot at the close position");
        chartClose.Tag = false;
        foreach (var count in new[] { 3, 12, 30 })
        {
            while (tabs.Items.Count < count)
                tabs.Items.Add(type.GetMethod("CreateSidebarTab", All)!.Invoke(main, new object?[] { new string('W', tabs.Items.Count * 4), new Grid(), (Action)(() => { }), null }));
            host.UpdateLayout();
            var widths = tabs.Items.Cast<TabItem>().Select(item => item.ActualWidth).ToArray();
            if (widths.Max() - widths.Min() > .01 || widths.Min() < 28 || widths.Max() > 110)
                throw new Exception("Sidebar tabs must shrink equally before overflowing");
        }
        Console.WriteLine("092 METADATA/TABS PASS: 14+-？ and -4.4700 fit, dirty dot, equal-width tabs at 3/12/30 pages.");
        var transport = (Panel)((FrameworkElement)main.FindName("DiagnosticsSummary")).Parent;
        var follow = (CheckBox)main.FindName("FollowPlayCheck");
        var syntax = (Label)main.FindName("ErrCount_Label");
        if (transport.ActualHeight != 38 || play.ActualWidth != 34 || play.ActualHeight != 34 ||
            ((Label)main.FindName("TimeLabel")).FontSize != 16 ||
            !syntax.FontFamily.Equals(follow.FontFamily) || syntax.FontSize != follow.FontSize)
            throw new Exception("Footer targets, clock size or diagnostics typography differ from the requested layout");
        var controls = (Panel)transport.Parent;
        Console.WriteLine("090 DIVIDERS: " + string.Join(",", transport.Children.OfType<Border>().Concat(controls.Children.OfType<Border>()).Select(line => $"{line.ActualHeight:F2}@{line.TranslatePoint(new Point(), root).Y + line.ActualHeight / 2:F2}")));
        var dividers = transport.Children.OfType<Border>().ToArray();
        if (dividers.Length != 3 || dividers.Any(line => Math.Abs(line.ActualHeight - 14) > .01) ||
            dividers.Select(line => line.TranslatePoint(new Point(), root).Y + line.ActualHeight / 2).Distinct().Count() != 1)
            throw new Exception("Footer divider heights/centers differ");
        var rowCenter = transport.TranslatePoint(new Point(), root).Y + transport.ActualHeight / 2;
        foreach (var name in new[] { "play", "zoom", "syntax label", "syntax count", "clock" })
            if (Math.Abs(InkCenter(items[name]) - (name.StartsWith("syntax") ? 1 : 0) - rowCenter) * dpi.DpiScaleY > 1.1)
                throw new Exception("Toolbar item must be centered in the whole row: " + name);
        Console.WriteLine($"091 FOOTER CENTER PASS: row={rowCenter:F3}; labels, clock and icons centered in the row.");
        var themeType = type.Assembly.GetType("MajdataEdit.ThemeManager")!;
        var icons = ((Panel)main.FindName("DiagnosticsSummary")).Children.OfType<Panel>()
            .Select(row => ((Panel)((Label)row.Children[0]).Content).Children.OfType<System.Windows.Shapes.Path>().Single()).ToArray();
        var oldStroke = icons[0].Stroke.ToString();
        var lightTheme = themeType.GetMethod("LoadThemeByName", All)!.Invoke(null, new object?[] { "light" });
        themeType.GetMethod("ApplyApplicationResources", All)!.Invoke(null, new[] { lightTheme });
        if (icons.Any(icon => icon.Stroke.ToString() == oldStroke || icon.Stroke.ToString() != main.FindResource("ButtonForeground").ToString()))
            throw new Exception("Diagnostics icons must follow light theme resources");
        var darkTheme = themeType.GetMethod("LoadThemeByName", All)!.Invoke(null, new object?[] { "dark" });
        themeType.GetMethod("ApplyApplicationResources", All)!.Invoke(null, new[] { darkTheme });
        if (TextOptions.GetTextHintingMode(root) != TextHintingMode.Fixed || TextOptions.GetTextRenderingMode(root) != TextRenderingMode.ClearType)
            throw new Exception("UI font rendering must not switch to animated text quality");
        foreach (var page in new[] { EditorSettingPanel.SettingsPage.Edit, EditorSettingPanel.SettingsPage.View })
        {
            var settings = new EditorSettingPanel(page: page);
            if (((UIElement)settings.FindName("PetComboBox")).Visibility != (page == EditorSettingPanel.SettingsPage.Edit ? Visibility.Visible : Visibility.Collapsed))
                throw new Exception("Pet selector belongs in Edit settings");
            typeof(EditorSettingPanel).GetField("settingsApplied", All)!.SetValue(settings, true); settings.Close();
        }
        Console.WriteLine("091 THEME/FONT/SETTINGS PASS: light icons update, static ClearType quality, pet selector in Edit.");
        host.Content = null; main.Content = root; host.Close();
        VerifyDensityZoom();
    }

    private static void VerifyDensityZoom()
    {
        var density = new NoteDensityWindow(new[] { new NoteDensitySample(60, DensityCategory.TapFamily) }, new[] { .1f, .8f }, 180, 180, "zoom");
        var content = (FrameworkElement)density.Content; density.Content = null;
        var host = new Window { Content = content, Width = 550, Height = 330, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        void Pump()
        {
            host.UpdateLayout(); var frame = new System.Windows.Threading.DispatcherFrame();
            host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        host.Show(); Pump();
        var image = (Image)density.FindName("DensityImage"); var scroller = (ScrollViewer)density.FindName("DensityScroller");
        scroller.ScrollToHorizontalOffset(200); Pump();
        var width = image.Width; var at = (scroller.HorizontalOffset + 100) / width;
        typeof(NoteDensityWindow).GetMethod("ZoomPlot", All)!.Invoke(density, new object[] { 1.2, 100d }); Pump();
        if (image.Width <= width || Math.Abs((scroller.HorizontalOffset + 100) / image.Width - at) > .002)
            throw new Exception("Density zoom lost the time under the pointer");
        typeof(NoteDensityWindow).GetMethod("ZoomPlot", All)!.Invoke(density, new object[] { 1e-5, 100d }); Pump();
        if (Math.Abs(image.Width - scroller.ViewportWidth) > 1) throw new Exception("Density zoom minimum must fill the viewport");
        typeof(NoteDensityWindow).GetMethod("ZoomPlot", All)!.Invoke(density, new object[] { 1e5, 100d }); Pump();
        if (image.Width != 16000) throw new Exception("Density zoom exceeded its existing width limit");
        Console.WriteLine("090 DENSITY ZOOM PASS: pointer time retained, min/max widths, rendered image present.");
        host.Content = null; density.Content = content; host.Close(); density.Close();
    }
}
