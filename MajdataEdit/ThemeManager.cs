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
    public string windowBackground { get; set; } = "#FF1E2025";
    public string buttonForeground { get; set; } = "#FFFFFFFF";
    public string helperForeground { get; set; } = "#FF569CD6";
    public string buttonsBackground { get; set; } = "#FF2E313A";
    public string editorBackground { get; set; } = "#FF1A1B20";
    public string editorForeground { get; set; } = "#FFE2E6EC";
    public string editorSelection { get; set; } = "#664E9AF1";
    public string waveBackground { get; set; } = "#64000000";
    public string waveAccent { get; set; } = "#FFE5484D";
    public string scrollTrack { get; set; } = "#FF1B1D22";
    public string scrollThumb { get; set; } = "#FF50545E";
    public string scrollThumbHover { get; set; } = "#FF676C78";
    public string menuSeparator { get; set; } = "#FF454A55";

    public override string ToString() => displayName;
}

internal static class ThemeManager
{
    public const string DefaultTheme = "dark";
    public static EditorTheme CurrentTheme { get; private set; } = DefaultDarkTheme();
    public static event Action? ThemeChanged;

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
        SetBrush("ButtonForeground", theme.buttonForeground);
        SetBrush("HelperForeground", theme.helperForeground);
        SetBrush("ButtonsBackground", theme.buttonsBackground);
        SetBrush("EditorBackground", theme.editorBackground);
        SetBrush("ScrollTrack", theme.scrollTrack);
        SetBrush("ScrollThumb", theme.scrollThumb);
        SetBrush("ScrollThumbHover", theme.scrollThumbHover);
        SetBrush("MenuSeparator", theme.menuSeparator);
        ThemeChanged?.Invoke();
    }

    public static void ApplyEditor(TextEditor editor, EditorTheme theme)
    {
        var background = BrushFrom(theme.editorBackground);
        editor.Background = background;
        editor.TextArea.Background = background;
        editor.Foreground = BrushFrom(theme.editorForeground);
        editor.TextArea.SelectionBrush = BrushFrom(theme.editorSelection);
        // Keep the scrollbar corner consistent with the editor surface.
        editor.Resources[SystemColors.ControlBrushKey] = background;
    }

    private static void SetBrush(string key, string color)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        if (Application.Current.Resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = parsed;
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

    private static EditorTheme DefaultDarkTheme() => new();
}
