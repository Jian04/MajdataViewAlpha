using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WPFLocalizeExtension.Engine;

namespace MajdataEdit;

/// <summary>
///     App.xaml 的交互逻辑
/// </summary>
public partial class App : Application
{
    public App()
    {
        LocalizeDictionary.Instance.SetCurrentThreadCulture = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog(ex);
        };
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        if (e.Exception.GetType() == typeof(COMException) &&
            e.Exception.Message.IndexOf("UCEERR_RENDERTHREADFAILURE") != -1)
        {
            // 需要开启软件渲染
            MessageBox.Show(MajdataEdit.MainWindow.GetLocalizedString("SoftRenderError"),
                MajdataEdit.MainWindow.GetLocalizedString("Error"));
            Shutdown(114);
            return;
        }

        MessageBox.Show(e.Exception.Source + " At:\n" + e.Exception.Message + "\n" + e.Exception.StackTrace, "发生错误",
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
