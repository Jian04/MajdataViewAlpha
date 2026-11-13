using System.IO;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Newtonsoft.Json;

namespace MajdataEdit;

internal sealed class EditorTheme
{
    public string name { get; set; } = "dark";
    public string displayName { get; set; } = "Dark";
    public string windowBackground { get; set; } = "#FF23262C";
    public string panelBackground { get; set; } = "";
    public string buttonForeground { get; set; } = "#FFE2E5EB";
    public string helperForeground { get; set; } = "#FF8DBDEB";
    public string buttonsBackground { get; set; } = "#FF30353D";
    public string editorBackground { get; set; } = "#FF272B32";
    public string editorForeground { get; set; } = "#FFE2E5EB";
    public string editorSelection { get; set; } = "#6634475D";
    public string waveBackground { get; set; } = "#FF262A31";
    public string waveAccent { get; set; } = "#FF838E9F";
    public string scrollTrack { get; set; } = "#FF2A2E35";
    public string scrollThumb { get; set; } = "#FF59616E";
    public string scrollThumbHover { get; set; } = "#FF788494";
    public string menuSeparator { get; set; } = "#FF3D444E";
    // Optional roles preserve existing theme files; built-in themes define all of them.
    public string previewBackground { get; set; } = "";
    public string sidebarHeaderBackground { get; set; } = "";
    public string hoverBackground { get; set; } = "";
    public string mutedForeground { get; set; } = "";

    public override string ToString() => WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture.TwoLetterISOLanguageName == "zh"
        ? name.ToLowerInvariant() switch { "dark" => "深色", "light" => "浅色", _ => displayName }
        : displayName;
}

internal static class ThemeManager
{
    public const string DefaultTheme = "dark";
    public static EditorTheme CurrentTheme { get; private set; } = DefaultDarkTheme();
    public static event Action? ThemeChanged;
    public static Color WaveLabelColor => CurrentIsLight
        ? (Color)ColorConverter.ConvertFromString(CurrentTheme.buttonForeground) : Colors.White;

    public static string ThemeDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Themes");

    /// <summary>
    /// Themes are files anyone can add to the folder above, so "light" is the name
    /// of one theme rather than a kind of theme. What the syntax colours, the
    /// waveform pens and View's standby screen actually need to know is whether
    /// they are drawing on a light surface, and the theme's own editor background
    /// answers that for every theme instead of only for the two shipped ones.
    /// </summary>
    public static bool IsLight(EditorTheme? theme)
    {
        if (theme == null)
            return false;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(theme.editorBackground);
            var luminance =
                (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
            return luminance > 0.5d;
        }
        catch
        {
            return false;
        }
    }

    public static bool CurrentIsLight => IsLight(CurrentTheme);

    public static IReadOnlyList<EditorTheme> LoadThemes()
    {
        EnsureDefaultThemeFile();
        var themes = Directory.GetFiles(ThemeDirectory, "*.json")
            .Select(LoadTheme)
            .Where(theme => theme != null)
            .Cast<EditorTheme>()
            .OrderBy(theme => theme.displayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (themes.Count == 0)
            themes.Add(DefaultDarkTheme());
        return themes;
    }

    public static EditorTheme LoadThemeByName(string? name)
    {
        var themes = LoadThemes();
        return themes.FirstOrDefault(theme =>
                   string.Equals(theme.name, name, StringComparison.OrdinalIgnoreCase)) ??
               themes.FirstOrDefault(theme => theme.name == DefaultTheme) ??
               themes.FirstOrDefault() ??
               DefaultDarkTheme();
    }

    public static void ApplyApplicationResources(EditorTheme theme)
    {
        CurrentTheme = theme;
        SetBrush("WindowBackground", theme.windowBackground);
        // Existing user themes predate the separate panel surface.
        SetBrush("PanelBackground", theme.panelBackground.Length == 0 ? theme.windowBackground : theme.panelBackground);
        var panel = (Color)ColorConverter.ConvertFromString(theme.panelBackground.Length == 0 ? theme.windowBackground : theme.panelBackground);
        var controls = (Color)ColorConverter.ConvertFromString(theme.buttonsBackground);
        SetBrush("PreviewBackground", theme.previewBackground.Length > 0 ? theme.previewBackground : Color.FromRgb((byte)((panel.R + controls.R) / 2),
            (byte)((panel.G + controls.G) / 2), (byte)((panel.B + controls.B) / 2)).ToString());
        var headerShift = IsLight(theme) ? -8 : 10;
        SetBrush("SidebarHeaderBackground", theme.sidebarHeaderBackground.Length > 0 ? theme.sidebarHeaderBackground : Color.FromRgb((byte)Math.Clamp(panel.R + headerShift, 0, 255),
            (byte)Math.Clamp(panel.G + headerShift, 0, 255), (byte)Math.Clamp(panel.B + headerShift, 0, 255)).ToString());
        SetBrush("ButtonForeground", theme.buttonForeground);
        SetBrush("WaveLabelForeground", WaveLabelColor.ToString());
        SetBrush("HelperForeground", theme.helperForeground);
        SetBrush("ButtonsBackground", theme.buttonsBackground);
        SetBrush("EditorBackground", theme.editorBackground);
        SetBrush("WaveBackground", theme.waveBackground);
        SetBrush("WaveAccent", theme.waveAccent);
        SetBrush("ScrollTrack", theme.scrollTrack);
        SetBrush("ScrollThumb", theme.scrollThumb);
        SetBrush("ScrollThumbHover", theme.scrollThumbHover);
        SetBrush("MenuSeparator", theme.menuSeparator);
        SetBrush("AccentBrush", theme.helperForeground);
        SetBrush("SelectionBackground", theme.editorSelection);
        var light = IsLight(theme);
        SetBrush("HoverBackground", theme.hoverBackground.Length > 0 ? theme.hoverBackground : light ? "#FFE3E8EE" : "#FF343941");
        SetBrush("MutedForeground", theme.mutedForeground.Length > 0 ? theme.mutedForeground : light ? "#FF697180" : "#FF9BA3AF");
        ThemeChanged?.Invoke();
    }

    public static void ApplyEditor(TextEditor editor, EditorTheme theme)
    {
        var background = BrushFrom(theme.editorBackground);
        editor.Background = background;
        editor.TextArea.Background = background;
        editor.Foreground = BrushFrom(theme.editorForeground);
        Editor.GlyphSelectionRenderer.Apply(editor, BrushFrom(theme.editorSelection));
        // Keep the scrollbar corner consistent with the editor surface.
        editor.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;
    }

    private static void SetBrush(string key, string color)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        if (Application.Current.Resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = parsed;
            existing.Opacity = 1;
            return;
        }

        // Keep application brushes mutable. Some menu templates retain the resolved
        // brush instance, so replacing a frozen resource does not repaint them live.
        Application.Current.Resources[key] = new SolidColorBrush(parsed);
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static EditorTheme? LoadTheme(string path)
    {
        try
        {
            return JsonConvert.DeserializeObject<EditorTheme>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDefaultThemeFile()
    {
        Directory.CreateDirectory(ThemeDirectory);
        var path = Path.Combine(ThemeDirectory, "dark.json");
        if (File.Exists(path))
            return;

        File.WriteAllText(path, JsonConvert.SerializeObject(DefaultDarkTheme(), Formatting.Indented));
    }

    private static EditorTheme DefaultDarkTheme() => new() { panelBackground = "#FF2A2E35",
        previewBackground = "#FF30353D", sidebarHeaderBackground = "#FF31363E",
        hoverBackground = "#FF3A414B", mutedForeground = "#FFA0A9B7" };
}
