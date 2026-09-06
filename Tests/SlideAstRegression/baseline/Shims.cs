namespace Baseline042;

// Stubs for the WPF symbols the v0.4.2 parser touches. Parsing does not depend
// on their behaviour, only on them existing.
internal static class MainWindow
{
    public static string GetLocalizedString(string key) => key;
}

internal static class ThemeManager
{
    public const string DefaultTheme = "default";
}
