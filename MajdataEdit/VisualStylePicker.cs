using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

// A single layout for visual appearance choices, matching the note-skin cards.
public sealed class VisualStylePicker : Grid
{
    private readonly Border preview = new() { Height = 100, ClipToBounds = true, CornerRadius = new CornerRadius(4) };
    private readonly TextBlock caption = new() { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private int selectedIndex;
    private string kind = "Background";
    private int transitionDirection;
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
        Margin = new Thickness(0, 0, 0, 7);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.SetResourceReference(Border.BackgroundProperty, "ButtonsBackground");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "HelperForeground");
        SetColumn(preview, 1); Children.Add(preview);
        SetRow(caption, 1); SetColumnSpan(caption, 3); Children.Add(caption);
        AddArrow("‹", -1, 0); AddArrow("›", 1, 2);
        Loaded += (_, _) => Refresh();
    }
    private void AddArrow(string glyph, int delta, int column)
    {
        var button = new Button { Content = glyph, FontSize = 18, Padding = new Thickness(0), Margin = new Thickness(3) };
        button.SetResourceReference(Control.TemplateProperty, "DarkButton");
        button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        button.SetResourceReference(Control.BackgroundProperty, "ButtonsBackground");
        button.Click += (_, _) => { var count = kind == "JudgeLine" ? MajdataCore.JudgeLineStyles.Available.Length : Themes.Length;
            transitionDirection = delta;
            SelectedIndex = (selectedIndex + delta + count) % count;
            transitionDirection = 0; };
        SetColumn(button, column); Children.Add(button);
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
    private static BitmapSource ReadImage(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
    }
}
