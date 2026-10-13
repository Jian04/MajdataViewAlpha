using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MajdataCore;

namespace MajdataSessionUi;

public sealed class SessionManagerWindow : Window
{
    private readonly StackPanel rows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    public SessionManagerWindow(Action create)
    {
        Title = "Edit + View 实例"; MinWidth = 660; MinHeight = 380;
        SessionTheme.Apply(this, 800, 540);
        var panel = new DockPanel { Margin = new Thickness(24) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        var button = SessionTheme.Button("＋ 新建独立 Edit + View", true);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => Run(create);
        top.Children.Add(button);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel; timer.Tick += (_, _) => Refresh(); Closed += (_, _) => timer.Stop();
        Loaded += (_, _) => { Refresh(); timer.Start(); };
    }
    private void Refresh()
    {
        // Keep controls stable while the user is clicking a row.
        if (IsMouseOver && rows.Children.Count > 0) return;
        rows.Children.Clear();
        try
        {
            foreach (var s in AlphaSession.List())
            {
                var content = new StackPanel();
                var swatch = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.ColorHex));
                var label = SessionTheme.Text($"{s.Label}   ·   {(s.IsAlive ? "运行中" : "启动中")}", size: 16);
                label.Foreground = swatch; label.FontWeight = FontWeights.SemiBold; content.Children.Add(label);
                var ports = SessionTheme.Text("Edit + View", true);
                ports.ToolTip = $"View {s.ViewPort} · Edit {s.EditPort} · Pet {s.PetPort}";
                ports.Margin = new Thickness(0, 5, 0, 14); content.Children.Add(ports);
                var line = new WrapPanel();
                AddButton(line, "定位 Edit", () => Focus(s, false));
                AddButton(line, "定位 View", () => Focus(s, true));
                AddButton(line, "关闭组合…", () =>
                {
                    using var editor = s.GetProcess(false);
                    if (editor != null) editor.CloseMainWindow(); // Edit owns the save/cancel/close-View prompts.
                    else { using var view = s.GetProcess(true); view?.CloseMainWindow(); }
                });
                content.Children.Add(line);
                rows.Children.Add(SessionTheme.Card(content));
            }
            if (rows.Children.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(0, 18, 0, 18) };
                empty.Children.Add(SessionTheme.Text("还没有独立实例", size: 17));
                var hint = SessionTheme.Text("点击上方按钮创建第一组。默认窗口继续使用原有启动方式。", true);
                hint.Margin = new Thickness(0, 8, 0, 0); empty.Children.Add(hint);
                rows.Children.Add(SessionTheme.Card(empty));
            }
        }
        catch (Exception e) { rows.Children.Add(new TextBlock { Text = e.Message }); }
    }
    private static void AddButton(Panel line, string caption, Action action)
    {
        var b = SessionTheme.Button(caption);
        b.Click += (_, _) => Run(action); line.Children.Add(b);
    }
    private static void Run(Action action)
    {
        try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "独立实例", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static void Focus(AlphaSession s, bool view)
    {
        using var process = s.GetProcess(view);
        if (process == null || process.MainWindowHandle == IntPtr.Zero) throw new InvalidOperationException("这个窗口尚未就绪或已关闭。");
        ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
}
