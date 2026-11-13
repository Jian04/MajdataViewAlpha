using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Settings071Regression
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = new System.Globalization.CultureInfo("zh-CN");
        var uiFont = (FontFamily)Application.Current.FindResource("UiFontFamily");
        var textDrawing = new DrawingGroup();
        using (var context = textDrawing.Open())
            context.DrawText(new FormattedText("MASTER 偏移，。", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),
                FlowDirection.LeftToRight, new Typeface(uiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 13, Brushes.White, 1), new Point());
        IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var run in Glyphs(child)) yield return run;
        }
        var families = Glyphs(textDrawing).SelectMany(run => run.GlyphRun.GlyphTypeface.FamilyNames.Values).Distinct().ToArray();
        Check(families.Any(name => name.Contains("Seurat")) && families.Any(name => name.Contains("Majdata UI Rounded")), "UI must use cover Seurat and the existing Chinese GenSen subset: " + string.Join(", ", families));
        Check(Glyphs(textDrawing).Where(run => run.GlyphRun.Characters.Contains('。')).All(run =>
            run.GlyphRun.GlyphTypeface.FamilyNames.Values.Any(name => name.Contains("Seurat"))), "Chinese punctuation must use the cover Seurat font");
        Console.WriteLine("UI FONT 089 PASS: actual glyph families=" + string.Join(", ", families));
        using (var context = textDrawing.Open())
            context.DrawText(new FormattedText("MASTER", System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                FlowDirection.LeftToRight, new Typeface((FontFamily)Application.Current.FindResource("TitleFontFamily"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 14, Brushes.White, 1), new Point());
        Check(Glyphs(textDrawing).All(run => run.GlyphRun.GlyphTypeface.FamilyNames.Values.Any(name => name.Contains("NewRodin"))),
            "Difficulty text must use the cover NewRodin font");
        var editor = new ICSharpCode.AvalonEdit.TextEditor { FontSize = 13 };
        var type = typeof(MainWindow).Assembly.GetType("MajdataEdit.Editor.FumenEditorAdapter")!;
        var adapter = Activator.CreateInstance(type, All, null, new object[] { editor }, null)!;
        var zoom = type.GetMethod("ZoomWithWheel", All)!;
        Check(!(bool)zoom.Invoke(adapter, new object[] { 120, ModifierKeys.Control, "Shift+Wheel" })!, "replaced Ctrl gesture must stop zooming");
        Check((bool)zoom.Invoke(adapter, new object[] { 120, ModifierKeys.Shift, "Shift+Wheel" })! && editor.FontSize == 14, "custom Shift+wheel must zoom");
        zoom.Invoke(adapter, new object[] { -120, ModifierKeys.Control, new EditorSetting().EditorZoomWheel });
        Check(editor.FontSize == 13, "default Ctrl+wheel must remain compatible");

        var raw = "(120){16}" + string.Concat(Enumerable.Repeat("1,2,3,4,", 20)) + "\r\n5,6,7,8,";
        editor.Text = raw; editor.Document.UndoStack.ClearAll();
        var changes = 0; editor.TextChanged += (_, _) => changes++;
        editor.WordWrap = true;
        var wrapWindow = new Window { Content = editor, Width = 240, Height = 150, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false };
        wrapWindow.Show(); wrapWindow.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
        Check(editor.TextArea.TextView.VisualLines.Any(line => line.TextLines.Count > 1), "Word wrap must visually split a long authored line");
        Check((bool)zoom.Invoke(adapter, new object[] { 120, ModifierKeys.Control, "Ctrl+Wheel" })! && editor.FontSize == 14,
            "Wrapped editor must retain configured font zoom");
        Check(!(bool)zoom.Invoke(adapter, new object[] { 120, ModifierKeys.None, "Ctrl+Wheel" })!, "Ordinary scrolling must remain available while wrapped");
        editor.WordWrap = false; wrapWindow.UpdateLayout();
        Check(editor.Text == raw && editor.Document.LineCount == 2 && !editor.CanUndo && changes == 0,
            "Toggling visual wrap must preserve CRLF, logical lines, text events, and undo history");
        Check((bool)zoom.Invoke(adapter, new object[] { -120, ModifierKeys.Control, "Ctrl+Wheel" })! && editor.FontSize == 13,
            "Disabling wrap must preserve configured font zoom");
        wrapWindow.Content = null; wrapWindow.Close();
        var savedWrap = Newtonsoft.Json.JsonConvert.DeserializeObject<EditorSetting>(Newtonsoft.Json.JsonConvert.SerializeObject(new EditorSetting { EditorWordWrap = true }))!;
        Check(savedWrap.EditorWordWrap && !new EditorSetting().EditorWordWrap, "Wrap preference must persist and remain off for existing configurations");
        Console.WriteLine("WORD WRAP PASS: visual lines only, unchanged CRLF/text/undo, persistent default-off option, configured zoom retained.");

        foreach (var paused in new[] { " ▌▌", "  ▌▌ " })
        {
            var button = new Button { Content = paused, Template = (ControlTemplate)Application.Current.FindResource("EditorTransportButton") };
            button.ApplyTemplate();
            var icon = (System.Windows.Shapes.Path)button.Template.FindName("icon", button);
            Check(PathGeometry.CreateFromGeometry(icon.Data).Figures.Count == 2 && icon.Fill == null,
                "Both main and clip pause states must use the same two-stroke vector icon");
        }
        Console.WriteLine("TRANSPORT ICON PASS: both pause states use the shared outline icon.");

        foreach (var orientation in new[] { Orientation.Horizontal, Orientation.Vertical })
        {
            var bar = new ScrollBar { Orientation = orientation, Minimum = 0, Maximum = 100, ViewportSize = 20,
                Width = orientation == Orientation.Horizontal ? 250 : 12, Height = orientation == Orientation.Horizontal ? 12 : 250 };
            var window = new Window { Content = bar, Width = 300, Height = 300, Left = -10000, Top = -10000,
                ShowInTaskbar = false, ShowActivated = false };
            window.Show(); window.UpdateLayout();
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            Check(track.Opacity == 0, orientation + " scrollbar must hide at rest");
            Check(bar.Background is SolidColorBrush background && background.Color.A == 0,
                "the whole scrollbar background must stay transparent, not only its thumb");
            var before = bar.RenderSize;
            var hover = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", All)!.GetValue(null)!;
            bar.SetValue(hover, true);
            Check(track.Opacity == 1 && track.IsEnabled, "hover must reveal an operable scrollbar");
            bar.SetValue(hover, false);
            Check(track.Opacity == 0 && bar.RenderSize == before, "hiding must not resize content");
            window.Close();
        }
        Console.WriteLine("SETTINGS 071 PASS: hover scrollbars retain layout; configurable wheel gestures keep Ctrl defaults.");
        var scroll = new ScrollViewer { Width = 240, Height = 140, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Width = 800, Height = 500 } };
        var scrollWindow = new Window { Content = scroll, Width = 300, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false };
        scrollWindow.Show(); scrollWindow.UpdateLayout();
        var presenter = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
        Check(presenter.ActualWidth == scroll.ActualWidth && presenter.ActualHeight == scroll.ActualHeight,
            "Transparent scrollbars must overlay content without reserving an opaque strip or corner");
        scroll.ScrollToHorizontalOffset(100); scroll.ScrollToVerticalOffset(80); scroll.UpdateLayout();
        Check(scroll.HorizontalOffset == 100 && scroll.VerticalOffset == 80, "Overlay scrollbars must preserve both scroll directions");
        scrollWindow.Close();

        var settings = new EditorSettingPanel(page: EditorSettingPanel.SettingsPage.View);
        typeof(EditorSettingPanel).GetMethod("LoadSkinChoices", All)!.Invoke(settings, new object[] { "dx" });
        var content = (FrameworkElement)settings.Content;
        settings.Content = null;
        var host = new Window { Content = content, Width = 420, Height = 1400, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false };
        host.Show(); host.UpdateLayout();
        var samples = (DrawingImage)typeof(EditorSettingPanel).GetMethod("SlideSamples", All)!.Invoke(settings, new object[] { "dx", true, "slide.png" })!;
        var drawings = ((DrawingGroup)samples.Drawing).Children;
        Check(drawings.Count >= 12 && drawings[2].Bounds.X - drawings[1].Bounds.X <= drawings[1].Bounds.Width * .65,
            "Slide arrows must form a closely spaced trail");
        var skinStar = ((DrawingGroup)drawings[^1]).Children.OfType<ImageDrawing>().Single();
        var order = (Border)typeof(EditorSettingPanel).GetField("starOrderHost", All)!.GetValue(settings)!;
        var selection = (TextBlock)typeof(EditorSettingPanel).GetField("starOrderSelection", All)!.GetValue(settings)!;
        foreach (var mode in new[] { 1, 0 })
        {
            typeof(EditorSettingPanel).GetField("starDisplayMode", All)!.SetValue(settings, mode);
            typeof(EditorSettingPanel).GetMethod("UpdateStarOrderPreview", All)!.Invoke(settings, new object[] { 0 });
            var stage = (Canvas)((Viewbox)order.Child).Child;
            var movingHead = ((Canvas)stage.Children[0]).Children.OfType<Image>().Last();
            Check(movingHead.Width == Math.Max(skinStar.Rect.Width, skinStar.Rect.Height) && movingHead.Height == movingHead.Width,
                "Slide and display-order previews must use the same unscaled star size");
            Check(Panel.GetZIndex(stage.Children[0]) == mode && Panel.GetZIndex(stage.Children[1]) == 1 - mode,
                "blue/red preview ordering must preserve stored mode semantics");
            Check(selection.Text == (mode == 1 ? "新版（先出在上）" : "旧版（后出在上）"), "star order labels");
        }
        var list = (StackPanel)settings.FindName("SkinGroupList");
        var labels = list.Children.OfType<Grid>().Select(row => ((StackPanel)row.Children[0]).Children.OfType<TextBlock>().First().Text).ToArray();
        Check(labels.SequenceEqual(new[] { "Tap", "Hold", "Star", "Star 颜色", "Slide", "Slide 出现样式" }),
            "Visual settings must keep Star, colour, Slide, order together without an extra category");
        typeof(MainWindow).GetMethod("NormalizeToolControls", All)!.Invoke(null, new object[] { content });
        foreach (var row in list.Children.OfType<Grid>())
        {
            var field = (Border)row.Children[1];
            var arrows = ((Grid)field.Child).Children.OfType<Button>().ToArray();
            Check(Grid.GetColumn(field) == 1 && arrows.Length == 2 && arrows.All(button => button.Margin == new Thickness(0) && button.BorderThickness == new Thickness(0)),
                "Label/value must stay left of one joined picker; sidebar normalization must not separate its arrows");
        }
        var colorImage = (Image)typeof(EditorSettingPanel).GetField("starColorPreview", All)!.GetValue(settings)!;
        var rows = (System.Collections.IDictionary)typeof(EditorSettingPanel).GetField("skinGroupRows", All)!.GetValue(settings)!;
        Image Preview(string key) => (Image)rows[key]!.GetType().GetProperty("Preview")!.GetValue(rows[key])!;
        typeof(EditorSettingPanel).GetMethod("SetSkinGroup", All)!.Invoke(settings, new object[] { "hold", "dx", "dx" });
        var horizontalHold = (DrawingGroup)((DrawingGroup)((DrawingImage)Preview("hold").Source).Drawing).Children[^1];
        Check(((RotateTransform)horizontalHold.Transform).Angle == 90 && horizontalHold.Bounds.Width > horizontalHold.Bounds.Height,
            "Hold selection must be horizontal without changing its visible size");
        var standardInput = (TextBox)settings.FindName("InnerViewerCover");
        var joinedPicker = (Border)((Grid)list.Children[0]).Children[1];
        host.UpdateLayout();
        Check(Math.Abs(standardInput.TranslatePoint(new Point(), content).X - joinedPicker.TranslatePoint(new Point(), content).X) < .01 && standardInput.Height == 30,
            "Normal fields and visual selectors must use the same label column and field alignment");
        ImageSource StarSource(Image image) => ((DrawingGroup)((DrawingImage)image.Source).Drawing).Children.OfType<DrawingGroup>().Last().Children.OfType<ImageDrawing>().Single().ImageSource;
        typeof(EditorSettingPanel).GetMethod("SetSkinGroup", All)!.Invoke(settings, new object[] { "star", "sd", "dx" });
        typeof(EditorSettingPanel).GetMethod("StarColorArrow_Click", All)!.Invoke(settings, new object[] { new Button { Content = "›" }, new RoutedEventArgs() });
        var slideHost = (Border)rows["slide"]!.GetType().GetProperty("Host")!.GetValue(rows["slide"])!;
        var slideCanvas = (Canvas)((Viewbox)slideHost.Child).Child;
        var slideHead = ((Canvas)slideCanvas.Children[0]).Children.OfType<Image>().Last();
        Check(ReferenceEquals(StarSource(colorImage), StarSource(Preview("star"))) && ReferenceEquals(StarSource(colorImage), slideHead.Source),
            "Star skin and colour changes must update the Star, colour, and Slide previews together");
        var staticStar = (DrawingGroup)((DrawingImage)Preview("star").Source).Drawing;
        Check(staticStar.Children.OfType<DrawingGroup>().All(group => !group.Transform.HasAnimatedProperties), "Star selector must remain static");
        Check(((TransformGroup)slideHead.RenderTransform).Children.Any(t => t.HasAnimatedProperties),
            "Slide skin preview must animate the guide star using the order preview's lane");
        var currentStage = (Canvas)((Viewbox)order.Child).Child;
        var currentHead = ((Canvas)currentStage.Children[0]).Children.OfType<Image>().Last();
        Check(ReferenceEquals(currentHead.Source, StarSource(colorImage)), "Display-order preview must use the chosen Star skin and colour");
        var oldStage = order.Child;
        typeof(EditorSettingPanel).GetMethod("SkinGroupArrow_Click", All)!.Invoke(settings,
            new object[] { new Button { Content = "›", DataContext = "slide", Tag = 1 }, new RoutedEventArgs() });
        Check(!ReferenceEquals(oldStage, order.Child), "Slide selection must refresh the display-order trail");
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(420, 1400, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = System.IO.File.Create(".codex-build/081-view-settings.png")) encoder.Save(file);
        host.Close(); settings.Content = content;
        typeof(EditorSettingPanel).GetField("settingsApplied", All)!.SetValue(settings, true);
        settings.Close();
        Console.WriteLine("SETTINGS 077 PASS: joined left-label pickers, ordered settings, linked Star/colour/Slide previews, equal head scale, static Star and both overlap modes.");
        VerifyPaletteThemes();
    }

    private static void VerifyPaletteThemes()
    {
        var assembly = typeof(MainWindow).Assembly;
        var paletteType = assembly.GetType("MajdataEdit.ColorPalettePanel")!;
        var palette = (Window)Activator.CreateInstance(paletteType, true)!;
        palette.Left = palette.Top = -10000; palette.WindowStartupLocation = WindowStartupLocation.Manual;
        palette.ShowInTaskbar = false; palette.Show(); palette.UpdateLayout();
        var hex = (TextBox)paletteType.GetField("hex", All)!.GetValue(palette)!;
        var copy = (Button)paletteType.GetField("copy", All)!.GetValue(palette)!;
        var channels = (Slider[])paletteType.GetField("channels", All)!.GetValue(palette)!;
        var preview = (Border)paletteType.GetField("preview", All)!.GetValue(palette)!;
        var star = Descendants(palette).OfType<Button>().Single(button => button.ToolTip?.ToString() == "00A2FF");
        star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(hex.Text == "00A2FF", "Star preset must select its bundled skin colour");
        hex.Text = "#ab12ef";
        Check(copy.IsEnabled && channels[0].Value == 171 && channels[1].Value == 18 && channels[2].Value == 239,
            "HEX input must accept # and lowercase and update RGB channels");
        hex.Text = "12"; Check(!copy.IsEnabled, "Incomplete HEX must not copy a previous colour");
        hex.Text = "FFFFFF"; channels[1].Value = 128;
        Check(hex.Text == "FF80FF", "RGB edits must produce uppercase six-digit HEX");
        star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var manager = assembly.GetType("MajdataEdit.ThemeManager")!;
        var load = manager.GetMethod("LoadThemeByName", All)!;
        var apply = manager.GetMethod("ApplyApplicationResources", All)!;
        foreach (var name in new[] { "dark", "light", "dark" })
        {
            var theme = load.Invoke(null, new object[] { name })!;
            apply.Invoke(null, new[] { theme }); palette.UpdateLayout();
            string Value(string field) => (string)theme.GetType().GetProperty(field)!.GetValue(theme)!;
            Check(hex.Background.ToString() == Value("buttonsBackground") && copy.Foreground.ToString() == Value("buttonForeground"),
                "Existing palette controls must track theme changes without rebuilding");
            var body = (StackPanel)((ScrollViewer)palette.Content).Content;
            var check = new CheckBox { IsChecked = true }; body.Children.Add(check); palette.UpdateLayout();
            var mark = (System.Windows.Shapes.Path)check.Template.FindName("optionMark", check);
            Check(mark.Fill.ToString() == Value("buttonForeground"), "Checked marks must remain visible in both themes before hovering");
            body.Children.Remove(check); palette.UpdateLayout();
            foreach (var field in new[] { "previewBackground", "sidebarHeaderBackground", "hoverBackground", "mutedForeground" })
            {
                Check(Value(field).Length > 0, "Built-in theme must define every UI surface role: " + field);
                var key = char.ToUpperInvariant(field[0]) + field[1..];
                Check(Application.Current.Resources[key].ToString() == Value(field), "Theme role was not applied: " + field);
            }
            Check(hex.Text == "00A2FF" && preview.Background.ToString() == "#FF00A2FF", "UI themes must preserve picked note colours");
            var content = (FrameworkElement)palette.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(".codex-build/075-palette-" + name + ".png"); png.Save(file);
        }
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject("{\"name\":\"legacy\",\"windowBackground\":\"#FF18202A\"}",
            assembly.GetType("MajdataEdit.EditorTheme")!)!;
        apply.Invoke(null, new[] { legacy });
        Check(Application.Current.Resources["PanelBackground"].ToString() == "#FF18202A", "Existing theme files must retain their original surface default");
        apply.Invoke(null, new[] { load.Invoke(null, new object[] { "dark" })! });
        palette.Close();
        Console.WriteLine("PALETTE/THEME 075 PASS: note presets, HEX/RGB validation, copy gating, live dark/light switching and legacy themes; clipboard untouched.");
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
