using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using MajdataSessionUi;
using Newtonsoft.Json;

namespace MajdataEdit;

internal sealed partial class ChartListPanel : Window
{
    private readonly MainWindow main;
    private readonly StackPanel rows = new();
    private readonly TextBlock status = SessionTheme.Text("", true);
    private readonly ComboBox mode = new() { ItemsSource = new[] { "顺序循环", "随机循环", "单曲循环" }, SelectedIndex = 0, Width = 96 };
    internal int PlaybackMode => mode.SelectedIndex;
    private readonly Button playbackButton = SessionTheme.Button("▶");
    private readonly Slider playbackProgress = SessionTheme.PlaybackSlider();
    private readonly TextBlock playbackTime = SessionTheme.Text("00:00 / 00:00", true);
    private readonly System.Windows.Threading.DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private Task? pendingPause;
    private bool draggingProgress;
    private bool progressRefreshing;
    private bool importing;
    internal ChartListPanel(MainWindow main)
    {
        this.main = main; Title = "谱面列表"; SessionTheme.Apply(this, 400, 600);
        var root = new DockPanel { Margin = new Thickness(8) };
        var toolbar = new StackPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var top = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; toolbar.Children.Add(top);
        void AddButton(string title, Action action)
        {
            var b = SessionTheme.Button(title); b.Margin = new Thickness(0, 0, 6, 6);
            b.Click += (_, _) => { try { action(); } catch (Exception e) { SetStatus(e.Message); } }; top.Children.Add(b);
        }
        AddButton("加入当前谱面", () => { main.AddCurrentToChartList(); Refresh(); });
        AddButton("导入文件夹", Import);
        AddButton("清空列表", () => { main.StopChartList(); ChartListStore.Update(x => x.Clear()); Refresh(); });
        AddButton("批量录制", main.ShowBatchRecording);
        AddButton("刷新", Refresh);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
        toolbar.Children.Add(transport);
        Button TransportButton(string glyph, Action action)
        {
            var button = SessionTheme.Button(glyph);
            button.Width = button.Height = 34;
            button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.TemplateProperty, "DarkButton");
            button.Click += (_, _) => action();
            transport.Children.Add(button);
            return button;
        }
        Button StepButton(bool previous, Action action)
        {
            var button = SessionTheme.Button("");
            button.Width = button.Height = 34;
            button.Padding = new Thickness(0);
            button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.TemplateProperty, "DarkButton");
            button.Content = new ShapePath
            {
                Data = Geometry.Parse(previous
                    ? "M 2,2 L 4,2 L 4,18 L 2,18 Z M 18,2 L 5,10 L 18,18 Z"
                    : "M 16,2 L 18,2 L 18,18 L 16,18 Z M 2,2 L 15,10 L 2,18 Z"),
                Stretch = Stretch.Uniform
            };
            ((ShapePath)button.Content).Width = 11;
            ((ShapePath)button.Content).Height = 11;
            ((ShapePath)button.Content).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ButtonForeground");
            button.Click += (_, _) => action();
            transport.Children.Add(button);
            return button;
        }
        StepButton(true, () => main.StepChartList(-1));
        TransportButton("◼", main.StopChartList);
        playbackButton.Width = playbackButton.Height = 34;
        playbackButton.Padding = new Thickness(0);
        playbackButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        playbackButton.VerticalContentAlignment = VerticalAlignment.Center;
        playbackButton.Background = Brushes.Transparent;
        playbackButton.SetResourceReference(Control.TemplateProperty, "DarkButton");
        playbackButton.Click += (_, _) => main.ToggleChartListPlayback(mode.SelectedIndex);
        transport.Children.Add(playbackButton);
        StepButton(false, () => main.StepChartList(1));
        SessionTheme.Input(mode); mode.Margin = new Thickness(8, 0, 0, 0);
        mode.VerticalAlignment = VerticalAlignment.Center; transport.Children.Add(mode);
        var progressRow = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playbackProgress.SetResourceReference(Control.ForegroundProperty, "HelperForeground");
        playbackProgress.PreviewMouseLeftButtonDown += (_, _) =>
        {
            draggingProgress = true;
            pendingPause = main.PauseChartListForSeek();
        };
        playbackProgress.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            try
            {
                if (pendingPause != null) await pendingPause;
                await main.SeekChartList(playbackProgress.Value);
            }
            catch (Exception error) { SetStatus(error.Message); }
            finally { pendingPause = null; draggingProgress = false; }
        };
        progressRow.Children.Add(playbackProgress);
        playbackTime.Margin = new Thickness(8, 2, 0, 0);
        Grid.SetColumn(playbackTime, 1); progressRow.Children.Add(playbackTime);
        toolbar.Children.Add(progressRow);
        progressTimer.Tick += async (_, _) => await UpdateProgress();
        Loaded += (_, _) => progressTimer.Start();
        Closed += (_, _) => progressTimer.Stop();
        status.MaxHeight = 100; status.TextTrimming = TextTrimming.CharacterEllipsis; status.Margin = new Thickness(0, 5, 0, 6); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        InitializeDragSurface(root);
        Content = root; Refresh(); UpdateTransport();
    }
    internal void SetStatus(string value) => status.Text = value;
    internal void UpdateTransport() => playbackButton.Content = main.ChartListIsPlaying && !main.ChartListIsPaused ? "  ▌▌ " : "▶";
    private async Task UpdateProgress()
    {
        if (progressRefreshing) return;
        progressRefreshing = true;
        try
        {
            var reply = await main.ChartListProgress();
            if (reply is not { Ok: true }) return;
            playbackProgress.Maximum = Math.Max(1d, reply.Duration);
            if (!draggingProgress) playbackProgress.Value = Math.Clamp(reply.Position, 0d, playbackProgress.Maximum);
            static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"mm\:ss");
            playbackTime.Text = Clock(playbackProgress.Value) + " / " + Clock(reply.Duration);
        }
        finally { progressRefreshing = false; }
    }
    internal void Refresh()
    {
        rows.Children.Clear();
        foreach (var entry in ChartListStore.Read())
        {
            var card = new StackPanel();
            var details = new StackPanel(); details.Children.Add(SessionTheme.Text(entry.Title));
            details.Children.Add(SessionTheme.Text(new[] { "EASY", "BASIC", "ADVANCED", "EXPERT", "MASTER", "Re:MASTER", "UTAGE" }[Math.Clamp(entry.Difficulty, 0, 6)] + "  " + entry.Level, true));
            details.Children.Add(SessionTheme.Text("曲师：" + entry.Artist + "\n谱师：" + entry.Designer, true));
            var open = new Border { Child = details, Padding = new Thickness(8), CornerRadius = new CornerRadius(4),
                Background = SessionTheme.Brush("ButtonsBackground", "#343A43"), ToolTip = entry.Folder,
                Cursor = System.Windows.Input.Cursors.Hand };
            card.Children.Add(open);
            var remove = SessionTheme.Button("移出列表"); remove.Margin = new Thickness(0, 6, 0, 0); remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Click += (_, _) => { ChartListStore.Update(list => list.RemoveAll(x => ChartListStore.Same(x, entry))); Refresh(); };
            card.Children.Add(remove);
            var frame = SessionTheme.Card(card);
            frame.Tag = entry;
            AttachReorderHandle(frame, remove, async () =>
            {
                try { await main.OpenChartListEntry(entry); }
                catch (Exception error) { SetStatus(error.Message); }
            });
            rows.Children.Add(frame);
        }
    }
    private async void Import()
    {
        if (importing) return;
        // Native folder chooser; importing only records paths, never moves chart/media files.
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        string? folder = null;
        try
        {
            dynamic picked = shell.BrowseForFolder(new System.Windows.Interop.WindowInteropHelper(main).Handle.ToInt32(), "选择包含谱面的文件夹（包括子文件夹）", 0x41);
            if (picked != null) folder = picked.Self.Path;
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        importing = true; SetStatus("正在扫描…");
        try
        {
            var result = await Task.Run(() =>
            {
                var found = new List<ChartListEntry>(); var errors = new List<string>();
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(folder, "*", options).Where(p => Path.GetFileName(p).Equals("maidata.txt", StringComparison.OrdinalIgnoreCase)))
                    try { found.AddRange(ChartListStore.InspectAll(Path.GetDirectoryName(file)!)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add(e.Message); }
                ChartListStore.Add(found); return (found.Count, errors);
            });
            Refresh(); SetStatus($"已导入 {result.Count} 个谱面" + (result.errors.Count == 0 ? "" : $"，跳过 {result.errors.Count} 个缺少素材或谱面的目录。"));
        }
        catch (Exception e) { SetStatus("导入失败：" + e.Message); }
        finally { importing = false; }
    }
}
