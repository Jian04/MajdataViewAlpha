using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

// Visual choices share one label/value column and one joined preview control.
public sealed class VisualStylePicker : Grid
{
    private readonly Border preview = new() { Height = 100, ClipToBounds = true, CornerRadius = new CornerRadius(4) };
    private readonly TextBlock caption = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 8, 0) };
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(VisualStylePicker),
        new PropertyMetadata("", (sender, _) => ((VisualStylePicker)sender).UpdateTitle()));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap };
    private int selectedIndex;
    private string kind = "Background";
    private int transitionDirection;
    private readonly Dictionary<string, (DateTime Modified, BitmapSource Image)> images = new();
    private static readonly string[] Themes = { "default", "circleplus", "circle", "chunithm-xversex", "chunithm-mate", "magical" };
    private static readonly string[] Names = { "OriginalStyle", "CiRCLE PLUS", "CiRCLE", "X-VERSE-X", "Mate", "MAGiCAL" };
    public event SelectionChangedEventHandler? SelectionChanged;
    public string Kind { get => kind; set { kind = value; Refresh(); } }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            var next = Math.Clamp(value, 0, kind == "JudgeLine" ? MajdataCore.JudgeLineStyles.Available.Length - 1 : Themes.Length - 1);
            if (next == selectedIndex) { Refresh(); return; }
            var old = selectedIndex; selectedIndex = next; Refresh();
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, new[] { old }, new[] { next }));
        }
    }
    public VisualStylePicker()
    {
        Children.Add(CreateRow(title, caption, CreatePicker(preview, AddArrow("‹", -1), AddArrow("›", 1))));
        Loaded += (_, _) => Refresh();
    }
    private void UpdateTitle() => title.Text = Title;

    internal static Grid CreateRow(TextBlock title, TextBlock value, FrameworkElement picker)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = "AppearancePicker" };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
        title.TextWrapping = value.TextWrapping = TextWrapping.Wrap;
        title.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
        value.FontSize = 12; value.Margin = new Thickness(0, 4, 0, 0);
        value.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeground");
        labels.Children.Add(title); labels.Children.Add(value); row.Children.Add(labels);
        SetColumn(picker, 1); row.Children.Add(picker);
        return row;
    }

    internal static Border CreatePicker(FrameworkElement preview, Button previous, Button next)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        foreach (var arrow in new[] { previous, next })
        {
            arrow.Background = Brushes.Transparent; arrow.BorderThickness = new Thickness(0);
            arrow.Padding = arrow.Margin = new Thickness(0); arrow.FontSize = 18;
            arrow.FocusVisualStyle = null;
            arrow.SetResourceReference(Control.TemplateProperty, "AppearanceArrow");
            arrow.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        }
        SetColumn(preview, 1); SetColumn(next, 2);
        grid.Children.Add(previous); grid.Children.Add(preview); grid.Children.Add(next);
        var field = new Border { Child = grid, CornerRadius = new CornerRadius(4), ClipToBounds = true,
            BorderThickness = new Thickness(0) };
        field.Background = Brushes.Transparent;
        field.SetResourceReference(Border.BorderBrushProperty, "MenuSeparator");
        return field;
    }

    private Button AddArrow(string glyph, int delta)
    {
        var button = new Button { Content = glyph };
        button.Click += (_, _) => { var count = kind == "JudgeLine" ? MajdataCore.JudgeLineStyles.Available.Length : Themes.Length;
            transitionDirection = delta;
            SelectedIndex = (selectedIndex + delta + count) % count;
            transitionDirection = 0; };
        return button;
    }
    private void Refresh()
    {
        preview.Height = kind == "JudgeLine" ? 140 : 190;
        if (kind == "JudgeLine")
        {
            var id = MajdataCore.JudgeLineStyles.Available[selectedIndex];
            caption.Text = id == 0 ? "0 · " + MainWindow.GetLocalizedString("JudgeLineOff") : id + " · " + MainWindow.GetLocalizedString("JudgeLineStyle" + id);
            var layers = new Grid { Margin = new Thickness(4) };
            void Add(string file)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Themes", "judgelines", file);
                if (File.Exists(path)) layers.Children.Add(new Image { Source = ReadImage(path), Stretch = Stretch.Uniform });
            }
            if (id == 2) Add("2-overlay.png");
            if (id != 0) Add(id == 3 ? "3.png" : "1.png");
            CarouselTransition.Replace(preview, layers, transitionDirection);
        }
        else
        {
            caption.Text = selectedIndex == 0 ? MainWindow.GetLocalizedString(Names[0]) : Names[selectedIndex];
            var host = new Border { ClipToBounds = true, Width = 640, Height = 360 };
            if (selectedIndex == 0)
                host.Background = kind == "ViewBackground"
                    ? new LinearGradientBrush(Color.FromRgb(247, 112, 206), Color.FromRgb(117, 81, 216), 45)
                    : (Brush)FindResource("WindowBackground");
            else EditorDecorationBg.Apply(host, Themes[selectedIndex]);
            CarouselTransition.Replace(preview, new Viewbox { Child = host, Stretch = Stretch.Uniform }, transitionDirection);
        }
    }
    private BitmapSource ReadImage(string path)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        if (images.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Image;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        images[path] = (modified, image); return image;
    }
}
