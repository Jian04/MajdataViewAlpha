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
    public string windowBackground { get; set; } = "#FF1F1F1F";
    public string buttonForeground { get; set; } = "#FFFFFFFF";
    public string helperForeground { get; set; } = "#FF569CD6";
    public string buttonsBackground { get; set; } = "#99303030";
    public string editorBackground { get; set; } = "#FF424852";
    public string editorForeground { get; set; } = "#FFEEEEEE";
    public string editorSelection { get; set; } = "#664C7D9D";
    public string waveBackground { get; set; } = "#CC303030";
    public string waveAccent { get; set; } = "#FFFF4F5E";
}

internal static class ThemeManager
{
    public const string DefaultTheme = "dark";

    public static string ThemeDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Themes");

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
        SetBrush("WindowBackground", theme.windowBackground);
        SetBrush("ButtonForeground", theme.buttonForeground);
        SetBrush("HelperForeground", theme.helperForeground);
        SetBrush("ButtonsBackground", theme.buttonsBackground);
    }

    public static void ApplyEditor(TextEditor editor, EditorTheme theme)
    {
        var background = BrushFrom(theme.editorBackground);
        editor.Background = background;
        editor.TextArea.Background = background;
        editor.Foreground = BrushFrom(theme.editorForeground);
    }

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] = BrushFrom(color);
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
