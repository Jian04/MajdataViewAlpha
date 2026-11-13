using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WPFLocalizeExtension.Engine;

namespace MajdataEdit;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static System.Windows.Media.FontFamily TitleFontFamily { get; } = Editor.FontLibrary.Load("FOT-NewRodin Pro EB.otf").Family;
    public static System.Windows.Media.FontFamily UiFontFamily { get; } = CreateUiFont();

    private static System.Windows.Media.FontFamily CreateUiFont()
    {
        var chinese = Editor.FontLibrary.Load("MajdataUIRounded-Regular.otf").Family;
        var latin = Editor.FontLibrary.Load("FOT-Seurat Pro DB.otf").Family;
        var family = new System.Windows.Media.FontFamily();
        family.FamilyMaps.Add(new System.Windows.Media.FontFamilyMap
        {
            Unicode = "2E80-2FFF,3100-312F,31A0-31EF,3400-9FFF,F900-FAFF,20000-2FFFF",
            Target = chinese.BaseUri.AbsoluteUri + chinese.Source.Substring(2)
        });
        // Seurat supplies the Latin text, numbers and punctuation used on covers.
        family.FamilyMaps.Add(new System.Windows.Media.FontFamilyMap
        {
            Target = latin.BaseUri.AbsoluteUri + latin.Source.Substring(2)
        });
        return family;
    }

    public App()
    {
        MajdataSessionUi.SessionTheme.DisableWheelSelection();
        LocalizeDictionary.Instance.SetCurrentThreadCulture = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog(ex);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // File dialogs and external launchers may supply an unrelated working directory.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        MajdataCore.AlphaSession.EnsureEditorSession();
        if (MajdataCore.AlphaSession.Current is { } session)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            session.Register(false, process);
        }
        base.OnStartup(e);
        ApplyThemeEarly();
    }

    // A cold start, including antivirus scanning of self-contained DLLs and JIT, may delay Window_Loaded theme injection for many seconds.
    // Meanwhile the window is an unthemed white shell and menu text has no brush, leaving only separators visible.
    // Apply the configured theme during startup; MainWindow applies it precisely again after loading.
    private static void ApplyThemeEarly()
    {
        try
        {
            string? themeName = null;
            foreach (var dir in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var path = Path.Combine(dir, "EditorSetting.json");
                if (!File.Exists(path))
                    continue;
                themeName = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path))
                    [nameof(EditorSetting.EditorTheme)]?.ToString();
                break;
            }

            ThemeManager.ApplyApplicationResources(ThemeManager.LoadThemeByName(themeName));
        }
        catch
        {
            // Fallback: MainWindow.ReadEditorSetting will still apply the theme.
        }
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        if (e.Exception.GetType() == typeof(COMException) &&
            e.Exception.Message.IndexOf("UCEERR_RENDERTHREADFAILURE") != -1)
        {
            // Software rendering is required.
            MessageBox.Show(MajdataEdit.MainWindow.GetLocalizedString("SoftRenderError"),
                MajdataEdit.MainWindow.GetLocalizedString("Error"));
            Shutdown(114);
            return;
        }

        MessageBox.Show(
            e.Exception.Source + " At:\n" + e.Exception.Message + "\n" + e.Exception.StackTrace,
            MajdataEdit.MainWindow.GetLocalizedString("UnhandledError"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]\r\n{ex}\r\n";
            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch
        {
            // Logging must never replace the original startup error.
        }
    }
}
