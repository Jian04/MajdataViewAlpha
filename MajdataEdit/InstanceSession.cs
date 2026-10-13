using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MajdataCore;
using MajdataSessionUi;

namespace MajdataEdit;

public partial class MainWindow
{
    private string? roomIdentityColor;
    private bool identityVisible;
    private Border? identityFallbackBorder;
    private string? openedChartText;
    private string? openedChartFile;
    private void RememberChartVersion(string path)
    {
        openedChartFile = Path.GetFullPath(path);
        openedChartText = File.ReadAllText(path);
    }
    private bool ChartWasChangedElsewhere(string path)
    {
        if (!string.Equals(openedChartFile, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return false;
        return !File.Exists(path) || File.ReadAllText(path) != openedChartText;
    }
    private void NewIndependentPair_Click(object sender, RoutedEventArgs e)
    {
        try { StartIndependentPair(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "独立实例", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void InstanceList_Click(object sender, RoutedEventArgs e) =>
        ShowTool(new SessionManagerWindow(StartIndependentPair) { Owner = this }, "窗口管理");

    private static void StartIndependentPair()
    {
        AlphaSession.EnableIdentities();
        var executable = Path.Combine(AppContext.BaseDirectory, "MajdataEdit.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("找不到配套 MajdataEdit.exe", executable);
        if (FindMajdataViewExecutable() == null) throw new FileNotFoundException("找不到配套 MajdataView.exe，请先构建并组装 Alpha 发布包。");
        var session = AlphaSession.Create();
        using var process = session.StartProcess(executable, false);
        // Edit starts exactly its own View once its window has loaded.
    }
    private void ApplyInstanceIdentity()
    {
        var session = AlphaSession.Current;
        if (session == null) return;
        identityVisible = roomIdentityColor != null || AlphaSession.List().Count(s => !s.IsBackgroundWorker && AlphaSession.IsProcess(s.EditPid, s.EditStart)) > 1;
        InstanceBadge.Text = session.Label;
        InstanceBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(session.ColorHex));
        InstanceBadge.ToolTip = $"View {session.ViewPort} · Edit {session.EditPort} · Pet {session.PetPort}";
        InstanceBadge.Visibility = Visibility.Collapsed;
        // Identity belongs in native window chrome, outside the chart editor.
        var marker = " [" + session.Label + "]";
        if (identityVisible && !Title.Contains(marker)) Title += marker;
        if (!identityVisible) Title = Title.Replace(marker, "");
        var color = identityVisible ? roomIdentityColor ?? session.ColorHex : null;
        var nativeBorder = SetIdentityBorder(new System.Windows.Interop.WindowInteropHelper(this).Handle, color);
        ShowIdentityFallbackBorder(identityVisible && !nativeBorder ? color : null);
        using var view = session.GetProcess(true);
        if (view?.MainWindowHandle is { } handle && handle != IntPtr.Zero)
        {
            SetWindowText(handle, "MajdataViewAlpha v0.6.0" + (identityVisible ? marker : ""));
            SetIdentityBorder(handle, color);
        }
    }
    private void ShowIdentityFallbackBorder(string? hex)
    {
        if (Content is not Grid root) return;
        if (hex == null)
        {
            if (identityFallbackBorder != null) identityFallbackBorder.Visibility = Visibility.Collapsed;
            return;
        }
        if (identityFallbackBorder == null)
        {
            identityFallbackBorder = new Border { IsHitTestVisible = false, BorderThickness = new Thickness(2) };
            Grid.SetRowSpan(identityFallbackBorder, Math.Max(1, root.RowDefinitions.Count));
            Grid.SetColumnSpan(identityFallbackBorder, Math.Max(1, root.ColumnDefinitions.Count));
            Panel.SetZIndex(identityFallbackBorder, 1000);
            root.Children.Add(identityFallbackBorder);
        }
        identityFallbackBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        identityFallbackBorder.Visibility = Visibility.Visible;
    }
    private static bool SetIdentityBorder(IntPtr handle, string? hex)
    {
        if (handle == IntPtr.Zero) return false;
        var color = hex == null ? default : (Color)ColorConverter.ConvertFromString(hex);
        int value = hex == null ? -1 : color.R | color.G << 8 | color.B << 16;
        try { return DwmSetWindowAttribute(handle, 34, ref value, sizeof(int)) == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr handle, string title);
    private IntPtr GetPairedViewWindow()
    {
        if (AlphaSession.Current == null) return FindWindow(null, "MajdataView");
        using var process = AlphaSession.Current.GetProcess(true);
        return process?.MainWindowHandle ?? IntPtr.Zero;
    }
}
