using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MajdataCollaboration;
using MajdataCore;

namespace MajdataSessionUi;

public sealed class MultiControlWindow : Window
{
    private sealed class Row
    {
        public AlphaSession Session = null!;
        public CheckBox Select = new() { IsChecked = true };
        public TextBlock State = SessionTheme.Text("正在获取状态…", true);
        public GroupReply? Last;
        public WindowLayout Layout = new(0, 0, 640, 640);
        public bool Positioned;
        public GroupTarget Target => new(Session.Id, Session.EditPid, Session.EditStart);
    }
    public sealed record WindowLayout(int X, int Y, int Width, int Height);
    private readonly Dictionary<string, Row> rows = new();
    private readonly StackPanel rowPanel = new();
    private readonly Grid body = new();
    public void UseSidebarLayout()
    {
        var root = (DockPanel)body.Parent;
        var heading = root.Children[0]; var toolbar = root.Children[1];
        var groups = (Border)body.Children[0];
        var groupScroll = (ScrollViewer)groups.Child;
        var groupContent = groupScroll.Content; groupScroll.Content = null; groups.Child = (UIElement)groupContent;
        groups.Margin = new Thickness(0, 0, 0, 10);
        var workspace = (ScrollViewer)body.Children[1];
        var controls = (UIElement)workspace.Content; workspace.Content = null;
        root.Children.Clear(); body.Children.Clear();
        var page = new StackPanel();
        page.Children.Add(toolbar); page.Children.Add(groups);
        page.Children.Add(controls); page.Children.Add(status);
        root.Margin = new Thickness(10);
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) });
    }
    private readonly TextBlock status = SessionTheme.Text("", true);
    private readonly Canvas canvas = new() { Height = 180, ClipToBounds = true };
    private readonly ComboBox fps = new() { ItemsSource = new[] { "60 FPS", "120 FPS" }, SelectedIndex = 0 };
    private readonly TextBox width = new() { Text = "1440" }, height = new() { Text = "720" };
    private readonly TextBox left = new(), top = new(), boxWidth = new(), boxHeight = new();
    private readonly TextBlock selection = SessionTheme.Text("点击布局中的色块选中窗口", true);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Slider playbackProgress = SessionTheme.PlaybackSlider();
    private readonly TextBlock playbackTime = SessionTheme.Text("00:00 / 00:00", true);
    private readonly Button unifiedPlay = SessionTheme.Button("▶", true);
    private bool draggingProgress, unifiedPaused;
    private Task? pendingProgressPause;
    private double pausedPosition = -6;
    private readonly List<Control> mutationControls = new();
    private bool busy, refreshing, closed;
    private Row? active, dragged;
    private Point dragStart;
    private WindowLayout? dragLayout;
    private double scale = 1;
    private CancellationTokenSource? batchStop;
    private readonly int desktopX = GetSystemMetrics(76), desktopY = GetSystemMetrics(77), desktopW = GetSystemMetrics(78), desktopH = GetSystemMetrics(79);
    public MultiControlWindow()
    {
        Title = "多窗口控制台"; MinWidth = 760; MinHeight = 560;
        SessionTheme.Apply(this, 860, Math.Min(760, SystemParameters.WorkArea.Height - 30));
        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = SessionTheme.Heading("多窗口控制台", "统一播放、安排布局与合成录制");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var transport = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        unifiedPlay.Width = unifiedPlay.Height = 34;
        unifiedPlay.Padding = new Thickness(0);
        unifiedPlay.HorizontalContentAlignment = HorizontalAlignment.Center;
        unifiedPlay.VerticalContentAlignment = VerticalAlignment.Center;
        unifiedPlay.Background = Brushes.Transparent;
        unifiedPlay.SetResourceReference(Control.TemplateProperty, "DarkButton");
        SetPlayIcon(false);
        unifiedPlay.Margin = new Thickness(0, 0, 4, 6);
        unifiedPlay.Click += async (_, _) => { try { await ToggleUnified(); } catch (Exception e) { status.Text = e.Message; } };
        toolbar.Children.Add(unifiedPlay); mutationControls.Add(unifiedPlay);
        var stopButton = SessionTheme.Button("◼");
        stopButton.Width = stopButton.Height = 34;
        stopButton.Padding = new Thickness(0);
        stopButton.Background = Brushes.Transparent;
        stopButton.SetResourceReference(Control.TemplateProperty, "DarkButton");
        stopButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        stopButton.VerticalContentAlignment = VerticalAlignment.Center;
        stopButton.ToolTip = "终止";
        stopButton.Margin = new Thickness(0, 0, 4, 6);
        stopButton.Click += async (_, _) => { try { await StopAll(); } catch (Exception e) { status.Text = e.Message; } };
        toolbar.Children.Insert(0, stopButton);
        unifiedPlay.ToolTip = "统一播放 / 暂停";
        transport.Children.Add(toolbar);
        var progressRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playbackProgress.SetResourceReference(Control.ForegroundProperty, "HelperForeground");
        playbackProgress.PreviewMouseLeftButtonDown += (_, _) =>
        {
            draggingProgress = true;
            if (rows.Values.Any(row => row.Select.IsChecked == true && row.Last?.Playing == true))
                pendingProgressPause = PauseUnified();
        };
        playbackProgress.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            try
            {
                if (pendingProgressPause != null) await pendingProgressPause;
                await SeekUnified(playbackProgress.Value);
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { pendingProgressPause = null; draggingProgress = false; }
        };
        progressRow.Children.Add(playbackProgress);
        playbackTime.Margin = new Thickness(10, 2, 0, 0);
        Grid.SetColumn(playbackTime, 1); progressRow.Children.Add(playbackTime);
        transport.Children.Add(progressRow);
        DockPanel.SetDock(transport, Dock.Top); root.Children.Add(transport);
        status.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var content = new StackPanel();
        var groups = new StackPanel(); groups.Children.Add(SessionTheme.Text("参与的窗口组", size: 16));
        var select = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        AddButton(select, "全选", () => { foreach (var r in rows.Values) r.Select.IsChecked = true; DrawLayout(); return Task.CompletedTask; });
        AddButton(select, "刷新", Refresh);
        groups.Children.Add(select); groups.Children.Add(rowPanel);
        var groupCard = SessionTheme.Card(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        groupCard.Margin = new Thickness(0, 0, 16, 0); body.Children.Add(groupCard);
        var recording = new StackPanel();
        var recordHeading = new WrapPanel();
        var recordTitle = SessionTheme.Text("录制设置", size: 16); recordTitle.Margin = new Thickness(0, 6, 20, 0);
        recordHeading.Children.Add(recordTitle);
        AddButton(recordHeading, "● 开始统一录制", () => StartBatch("record", 0), true);
        AddButton(recordHeading, "依次导出", ExportSequentially);
        recording.Children.Add(recordHeading);
        recording.Children.Add(Fields(("帧率", fps), ("宽度 / px", width), ("高度 / px", height)));
        mutationControls.AddRange(new Control[] { fps, width, height });
        var recordCard = SessionTheme.Card(recording); recordCard.Margin = new Thickness(0);
        var layout = new StackPanel(); layout.Children.Add(SessionTheme.Text("录制合成画布", size: 16));
        var hint = SessionTheme.Text("拖动摆放；点击画面设置它的宽高和位置。黑色区域就是最终视频范围。", true);
        hint.Margin = new Thickness(0, 8, 0, 12); layout.Children.Add(hint);
        canvas.Background = SessionTheme.Brush("WindowBackground", "#20242B"); layout.Children.Add(canvas);
        canvas.SizeChanged += (_, _) => DrawLayout();
        width.TextChanged += (_, _) => DrawLayout(); height.TextChanged += (_, _) => DrawLayout();
        selection.Margin = new Thickness(0, 10, 0, 10); layout.Children.Add(selection);
        var numbers = Fields(("X / px", left), ("Y / px", top), ("宽度 / px", boxWidth), ("高度 / px", boxHeight));
        mutationControls.AddRange(new Control[] { left, top, boxWidth, boxHeight });
        layout.Children.Add(numbers);
        var layoutButtons = new WrapPanel();
        AddButton(layoutButtons, "更新色块", UpdateBox);
        AddButton(layoutButtons, "网格排列", () => { Tile(false); return Task.CompletedTask; });
        AddButton(layoutButtons, "横向排列", () => { Tile(true); return Task.CompletedTask; });
        AddButton(layoutButtons, "应用布局", ApplyLayout, true);
        AddButton(layoutButtons, "保存布局", SaveLayout);
        layout.Children.Add(layoutButtons); content.Children.Add(SessionTheme.Card(layout));
        content.Children.Add(recordCard);
        var workspace = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(workspace, 1); body.Children.Add(workspace); root.Children.Add(body); Content = root;
        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { await Refresh(); timer.Start(); };
        Closed += (_, _) => { closed = true; timer.Stop(); batchStop?.Cancel(); };
    }
    private static Grid Fields(params (string Label, Control Input)[] fields)
    {
        var grid = new Grid();
        var columns = Math.Min(2, fields.Length);
        for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < (fields.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < fields.Length; i++)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, i % columns == columns - 1 ? 0 : 8, 0) };
            SessionTheme.Field(field, fields[i].Label, fields[i].Input);
            Grid.SetColumn(field, i % columns); Grid.SetRow(field, i / columns); grid.Children.Add(field);
        }
        return grid;
    }
    private void AddButton(Panel panel, string title, Func<Task> action, bool primary = false, bool stop = false)
    {
        var button = SessionTheme.Button(title, primary); button.Margin = new Thickness(0, 0, 8, 6);
        button.Click += async (_, _) => { try { await action(); } catch (Exception e) { status.Text = e.Message; } };
        panel.Children.Add(button); if (!stop) mutationControls.Add(button);
    }
    private Row[] Selected() => rows.Values.Where(r => r.Select.IsChecked == true).ToArray();
    private void SetBusy(bool value)
    {
        busy = value;
        foreach (var control in mutationControls) control.IsEnabled = !value;
        foreach (var row in rows.Values) row.Select.IsEnabled = !value;
        canvas.IsEnabled = !value;
    }
    private async Task Refresh()
    {
        if (refreshing || closed || busy) return;
        refreshing = true;
        try
        {
            var sessions = AlphaSession.List().Where(s => AlphaSession.IsProcess(s.EditPid, s.EditStart)).ToArray();
            foreach (var id in rows.Keys.Except(sessions.Select(s => s.Id)).ToArray()) rows.Remove(id);
            bool changed = rowPanel.Children.Count != rows.Count;
            foreach (var s in sessions)
            {
                if (rows.TryGetValue(s.Id, out var existing)) { existing.Session = s; continue; }
                var row = new Row { Session = s, Layout = new(rows.Count * 512, 0, 512, 512), Positioned = true }; rows.Add(s.Id, row); changed = true;
                LoadLayout(row);
                row.Select.Content = s.Label;
                row.Select.Foreground = (Brush)new BrushConverter().ConvertFromString(s.ColorHex)!;
                row.Select.FontSize = 15; row.Select.FontWeight = FontWeights.SemiBold;
                row.Select.Checked += (_, _) => DrawLayout(); row.Select.Unchecked += (_, _) => DrawLayout();
            }
            if (changed || rowPanel.Children.Count == 0)
            {
                rowPanel.Children.Clear();
                foreach (var row in rows.Values)
                {
                    var block = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
                    block.Children.Add(row.Select); row.State.Margin = new Thickness(0, 3, 0, 0); row.State.FontSize = 12;
                    block.Children.Add(row.State); rowPanel.Children.Add(block);
                }
                if (rows.Count == 0) rowPanel.Children.Add(SessionTheme.Text("还没有窗口组。请打开新版 Edit，或在联机菜单中新建 Edit + View。", true));
            }
            await Task.WhenAll(rows.Values.ToArray().Select(async row =>
            {
                var reply = await GroupControl.Send(row.Target, new()); if (closed || busy) return;
                ShowReply(row, reply);
            }));
            UpdateProgress();
            if (dragged == null) DrawLayout();
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { refreshing = false; }
    }
    private void ShowReply(Row row, GroupReply reply)
    {
        row.Last = reply;
        string state = !reply.Ok ? "连接异常" : reply.Recording ? "录制中" : reply.Playing ? "播放中" : reply.Ready ? "就绪" : "等待配套 View / 谱面";
        row.State.Text = $"{reply.Title} · {state}" + (reply.Ok ? "" : "\n" + reply.Message);
        row.State.ToolTip = reply.Output;
    }
    private static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"mm\:ss");
    private void UpdateProgress()
    {
        var activeRows = Selected().Where(row => row.Last is { Ok: true }).ToArray();
        var duration = activeRows.Select(row => row.Last!.Duration).DefaultIfEmpty(0d).Max();
        playbackProgress.Maximum = Math.Max(1d, duration);
        if (!draggingProgress)
            playbackProgress.Value = Math.Clamp(activeRows.Select(row => row.Last!.Position).DefaultIfEmpty(0d).Max(), 0d, playbackProgress.Maximum);
        playbackTime.Text = Clock(playbackProgress.Value) + " / " + Clock(duration);
        if (!busy && !draggingProgress && activeRows.Length > 0)
            SetPlayIcon(activeRows.Any(row => row.Last!.Playing) && !unifiedPaused);
    }
    private async Task ToggleUnified()
    {
        if (rows.Values.Any(row => row.Select.IsChecked == true && row.Last?.Playing == true) && !unifiedPaused)
            await PauseUnified();
        else await StartBatch("play", pausedPosition < -5d ? 0d : pausedPosition);
    }
    private async Task PauseUnified()
    {
        if (busy) return;
        var selected = Selected(); if (selected.Length == 0) return;
        SetBusy(true);
        try
        {
            var pauseAt = DateTime.UtcNow.AddMilliseconds(500).Ticks;
            var replies = await Task.WhenAll(selected.Select(row => GroupControl.Send(row.Target,
                new GroupCommand { Action = "pause", StartUtc = pauseAt })));
            for (var i = 0; i < selected.Length; i++) ShowReply(selected[i], replies[i]);
            if (replies.Any(reply => !reply.Ok)) throw new IOException("部分窗口未能同步暂停。");
            pausedPosition = replies[0].Position;
            var aligned = await Task.WhenAll(selected.Select(row => GroupControl.Send(row.Target,
                new GroupCommand { Action = "seek", Seconds = Math.Max(-5d, pausedPosition) })));
            for (var i = 0; i < selected.Length; i++) ShowReply(selected[i], aligned[i]);
            if (aligned.Any(reply => !reply.Ok)) throw new IOException("部分窗口未能对齐暂停位置。");
            unifiedPaused = true;
            SetPlayIcon(false);
            UpdateProgress();
        }
        finally { SetBusy(false); }
    }
    private async Task SeekUnified(double position)
    {
        if (busy) return;
        pausedPosition = position;
        await Broadcast("seek", position);
        unifiedPaused = true;
        SetPlayIcon(false);
        UpdateProgress();
    }
    private void SetPlayIcon(bool playing)
    {
        // Same transport glyphs and button template as Edit's chart list.
        unifiedPlay.Content = playing ? "  ▌▌ " : "▶";
    }
    private async Task StartBatch(string action, double position)
    {
        if (busy) return;
        var selected = Selected(); if (selected.Length == 0) throw new InvalidOperationException("请至少勾选一组。");
        int w = 1080, h = 1080;
        if (action == "record" && (!int.TryParse(width.Text, out w) || !int.TryParse(height.Text, out h))) throw new ArgumentException("请输入录制宽高。");
        if (action == "record") ValidateCanvas();
        if (action == "record") await ApplyLayout();
        var layouts = selected.ToDictionary(r => r.Session.Id, r => r.Layout);
        var command = new GroupCommand { Action = action, Seconds = position, Width = w, Height = h, Fps = fps.SelectedIndex == 1 ? 120 : 60,
            Sizes = selected.ToDictionary(r => r.Session.Id, r => new GroupSize(r.Layout.Width, r.Layout.Height)) };
        SetBusy(true); batchStop = new(); status.Text = action == "play" ? "正在等待全部 View 加载，完成后统一开始…" : "正在为全部组生成录制音频，完成后同批开始…";
        try
        {
            var replies = await GroupBatch.Start(selected.Select(r => r.Target).ToArray(), command, token: batchStop.Token);
            foreach (var row in selected) if (replies.TryGetValue(row.Session.Id, out var reply)) ShowReply(row, reply);
            if (action == "play" && replies.Values.All(reply => reply.Ok))
            { unifiedPaused = false; pausedPosition = -6; SetPlayIcon(true); }
            if (action == "record" && replies.Values.All(r => r.Ok) && !batchStop.IsCancellationRequested)
            {
                await WaitForVideos(selected, batchStop.Token);
                status.Text = "录制完成，正在按画布合成…";
                var folders = selected.Select(r => r.Last!.ChartDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var temporary = Path.Combine(AlphaSession.Root, "RecordBatches", command.Batch, "out_multi.mp4");
                await MultiVideoComposer.Compose(selected[0].Last!.Ffmpeg,
                    selected.Select(r => { var b = layouts[r.Session.Id]; return new VideoTile(r.Last!.Output, b.X, b.Y, b.Width, b.Height, r.Last.AudioOutput); }).ToArray(),
                    w, h, command.Fps, temporary, batchStop.Token);
                foreach (var folder in folders)
                {
                    var outputFolder = Path.Combine(folder, "outputs");
                    Directory.CreateDirectory(outputFolder);
                    File.Copy(temporary, Path.Combine(outputFolder, "out_multi.mp4"), true);
                }
                status.Text = "录制完成。";
                RevealVideo(Path.Combine(folders[0], "outputs", "out_multi.mp4"));
                TryDeleteIntermediate(temporary);
                foreach (var row in selected)
                {
                    TryDeleteIntermediate(row.Last!.Output);
                    TryDeleteIntermediate(row.Last.Output + ".done");
                    TryDeleteIntermediate(row.Last.Output + ".ready"); TryDeleteIntermediate(row.Last.Output + ".start");
                    TryDeleteIntermediate(row.Last.AudioOutput);
                }
                return;
            }
            status.Text = batchStop.IsCancellationRequested ? "批次已取消。" : replies.Values.All(r => r.Ok) ? "指令已发送，请查看各组状态。" : "有组未能完成本批次，已取消本批次准备或启动；详情见各组提示。";
        }
        catch
        {
            await StopBatch(selected);
            throw;
        }
        finally
        {
            if (action == "record")
            {
                await StopBatch(selected);
                await CleanupBatch(command.Batch, selected);
            }
            batchStop.Dispose(); batchStop = null; SetBusy(false);
        }
    }
    private async Task Broadcast(string action, double position = -1)
    {
        var selected = Selected(); if (selected.Length == 0) throw new InvalidOperationException("请至少勾选一组。");
        if (busy && action != "stop") return;
        bool ownsBusy = !busy;
        if (ownsBusy) SetBusy(true);
        try
        {
            await Task.WhenAll(selected.Select(async row => ShowReply(row, await GroupControl.Send(row.Target, new() { Action = action, Seconds = position }))));
            status.Text = "已向所选组发送指令；各组结果见列表。";
        }
        finally { if (ownsBusy) SetBusy(false); }
    }
    private async Task StopAll() { batchStop?.Cancel(); await Broadcast("stop"); pausedPosition = -6; unifiedPaused = false; SetPlayIcon(false); }
    private static Task StopBatch(Row[] selected) => Task.WhenAll(selected.Select(async row =>
    {
        try { var state = await GroupControl.Send(row.Target, new()); if (state.Recording) await GroupControl.Send(row.Target, new() { Action = "stop" }); }
        catch { /* Preserve the original recording error when a peer has exited. */ }
    }));
    private static void TryDeleteIntermediate(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static async Task CleanupBatch(string batch, Row[] selected)
    {
        if (!Guid.TryParseExact(batch, "N", out _)) return;
        var root = Path.GetFullPath(Path.Combine(AlphaSession.Root, "RecordBatches")) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(root, batch));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        // Encoders may still be closing their output after a cancellation.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                foreach (var row in selected)
                    if (!string.IsNullOrEmpty(row.Last?.AudioOutput)) TryDeleteIntermediate(row.Last!.AudioOutput);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await Task.Delay(250);
        }
    }
    private void ValidateCanvas()
    {
        if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h) ||
            w < 256 || h < 256 || w > 16384 || h > 16384 || w % 2 != 0 || h % 2 != 0)
            throw new ArgumentException("画布宽高需为 256–16384 的偶数。");
        foreach (var row in Selected())
        {
            var b = row.Layout;
            if (b.X < 0 || b.Y < 0 || b.Width < 256 || b.Height < 256 || b.Width > 3840 || b.Height > 3840 ||
                b.Width % 2 != 0 || b.Height % 2 != 0 || b.X + b.Width > w || b.Y + b.Height > h)
                throw new ArgumentException($"窗口 {row.Session.Label} 需在画布内，宽高为 256–3840 的偶数。");
        }
    }
    private async Task WaitForVideos(Row[] selected, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddHours(6);
        var lastHealthy = selected.ToDictionary(r => r.Session.Id, _ => DateTime.UtcNow);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(1000, token);
            foreach (var row in selected)
            {
                var reply = await GroupControl.Send(row.Target, new());
                if (!reply.Ok || !reply.Ready)
                {
                    if (DateTime.UtcNow - lastHealthy[row.Session.Id] > TimeSpan.FromSeconds(45))
                        throw new IOException($"窗口 {row.Session.Label} 失去响应：{reply.Message}");
                    // A busy encoder or a transient heartbeat read must not cancel
                    // the entire batch and discard its pending composite.
                    continue;
                }
                lastHealthy[row.Session.Id] = DateTime.UtcNow;
                ShowReply(row, reply);
            }
            if (selected.All(r => !r.Last!.Recording))
            {
                foreach (var row in selected)
                    if (string.IsNullOrEmpty(row.Last!.Output) || !File.Exists(row.Last.Output))
                        throw new IOException($"窗口 {row.Session.Label} 没有生成录制视频。");
                return;
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException("录制超时，请检查 View。");
            status.Text = "正在录制，完成后自动合成…";
        }
    }
    private async Task ExportSequentially()
    {
        if (busy) return;
        ValidateCanvas(); var selected = Selected();
        if (selected.Length == 0) throw new InvalidOperationException("请选择窗口。");
        SetBusy(true); batchStop = new();
        try
        {
            foreach (var row in selected)
            {
                batchStop.Token.ThrowIfCancellationRequested();
                status.Text = "正在导出 " + row.Session.Label;
                var command = new GroupCommand { Action = "record", Seconds = 0,
                    Width = row.Layout.Width, Height = row.Layout.Height, Fps = fps.SelectedIndex == 1 ? 120 : 60 };
                try
                {
                    var result = await GroupBatch.Start(new[] { row.Target }, command, token: batchStop.Token);
                    ShowReply(row, result[row.Session.Id]);
                    if (!result[row.Session.Id].Ok) throw new IOException(result[row.Session.Id].Message);
                    await WaitForVideos(new[] { row }, batchStop.Token);
                    var outputFolder = Path.Combine(row.Last!.ChartDirectory, "outputs");
                    Directory.CreateDirectory(outputFolder);
                    File.Copy(row.Last.Output, Path.Combine(outputFolder, "out_" + row.Session.Label + ".mp4"), true);
                }
                finally { await StopBatch(new[] { row }); await CleanupBatch(command.Batch, new[] { row }); }
            }
            status.Text = "全部窗口已依次导出。";
            RevealVideo(Path.Combine(selected[0].Last!.ChartDirectory, "outputs", "out_" + selected[0].Session.Label + ".mp4"));
        }
        catch
        {
            await StopBatch(selected);
            throw;
        }
        finally { batchStop.Dispose(); batchStop = null; SetBusy(false); }
    }
    private void DrawLayout()
    {
        if (dragged != null) return;
        canvas.Children.Clear();
        var outputW = int.TryParse(width.Text, out var cw) && cw > 0 ? cw : 1440;
        var outputH = int.TryParse(height.Text, out var ch) && ch > 0 ? ch : 720;
        scale = Math.Max(100, canvas.ActualWidth) / outputW;
        canvas.Height = outputH * scale;
        foreach (var row in Selected())
        {
            var tile = new Border { Width = Math.Max(45, row.Layout.Width * scale), Height = Math.Max(38, row.Layout.Height * scale),
                Background = (Brush)new BrushConverter().ConvertFromString(row.Session.ColorHex)!, Opacity = 0.8,
                BorderBrush = row == active ? Brushes.White : Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Cursor = Cursors.SizeAll,
                Child = new TextBlock { Text = row.Session.Label, Foreground = Brushes.Black, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap } };
            Canvas.SetLeft(tile, row.Layout.X * scale); Canvas.SetTop(tile, row.Layout.Y * scale);
            tile.MouseLeftButtonDown += (_, e) => { active = row; dragged = row; dragStart = e.GetPosition(canvas); dragLayout = row.Layout; tile.CaptureMouse(); ReadBox(); e.Handled = true; };
            tile.MouseMove += (_, e) =>
            {
                if (dragged != row || dragLayout == null || e.LeftButton != MouseButtonState.Pressed) return;
                var point = e.GetPosition(canvas);
                row.Layout = dragLayout with { X = Math.Clamp(dragLayout.X + (int)((point.X - dragStart.X) / scale), 0, Math.Max(0, outputW - dragLayout.Width)),
                    Y = Math.Clamp(dragLayout.Y + (int)((point.Y - dragStart.Y) / scale), 0, Math.Max(0, outputH - dragLayout.Height)) };
                row.Positioned = true; Canvas.SetLeft(tile, row.Layout.X * scale); Canvas.SetTop(tile, row.Layout.Y * scale); ReadBox();
            };
            tile.MouseLeftButtonUp += (_, _) => { dragged = null; tile.ReleaseMouseCapture(); DrawLayout(); };
            tile.LostMouseCapture += (_, _) => { dragged = null; };
            canvas.Children.Add(tile);
        }
    }
    private void RevealVideo(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
        catch { status.Text += " 请在歌曲目录查看成片。"; }
    }
    private void ReadBox()
    {
        if (active == null) return; var b = active.Layout; selection.Text = "当前窗口：" + active.Session.Label;
        left.Text = b.X.ToString(); top.Text = b.Y.ToString(); boxWidth.Text = b.Width.ToString(); boxHeight.Text = b.Height.ToString();
    }
    private Task UpdateBox()
    {
        if (active == null) throw new InvalidOperationException("请先点击一个色块。");
        var value = new WindowLayout(int.Parse(left.Text), int.Parse(top.Text), int.Parse(boxWidth.Text), int.Parse(boxHeight.Text));
        GroupControl.Validate(new() { Action = "layout", X = value.X, Y = value.Y, Width = value.Width, Height = value.Height });
        active.Layout = value; active.Positioned = true; DrawLayout(); return Task.CompletedTask;
    }
    private void Tile(bool horizontal)
    {
        var selected = Selected(); if (selected.Length == 0) return;
        int columns = horizontal ? selected.Length : (int)Math.Ceiling(Math.Sqrt(selected.Length));
        int lines = (int)Math.Ceiling((double)selected.Length / columns);
        var area = SystemParameters.WorkArea;
        var side = Math.Max(256, Math.Min(640, (int)Math.Min((area.Width - columns * 32) / columns, (area.Height - lines * 64) / lines))) / 2 * 2;
        for (int i = 0; i < selected.Length; i++)
        {
            selected[i].Layout = new(i % columns * side, i / columns * side, side, side); selected[i].Positioned = true;
        }
        width.Text = (side * columns).ToString(); height.Text = (side * lines).ToString();
        DrawLayout(); ReadBox();
    }
    private async Task ApplyLayout()
    {
        if (busy) return;
        var selected = Selected();
        if (selected.Length == 0) throw new InvalidOperationException("请至少勾选一组。");
        SetBusy(true);
        try
        {
            await Task.WhenAll(selected.Select(async row => ShowReply(row, await GroupControl.Send(row.Target, new() {
                Action = "layout",
                X = row.Layout.X + selected.Select(r => r.Layout.X).Distinct().Count(x => x < row.Layout.X) * 32,
                Y = row.Layout.Y + selected.Select(r => r.Layout.Y).Distinct().Count(y => y < row.Layout.Y) * 64,
                Width = row.Layout.Width, Height = row.Layout.Height }))));
            status.Text = "布局请求已发送。正在录制的组会拒绝移动或缩放。";
        }
        finally { SetBusy(false); }
    }
    private static string LayoutPath => Path.Combine(AlphaSession.Root, "window-layout.json");
    private Task SaveLayout()
    {
        Directory.CreateDirectory(AlphaSession.Root);
        File.WriteAllText(LayoutPath, JsonSerializer.Serialize(rows.Values.ToDictionary(r => r.Session.Slot, r => r.Layout)));
        status.Text = "布局已保存，下次按组号恢复。"; return Task.CompletedTask;
    }
    private static void LoadLayout(Row row)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<int, WindowLayout>>(File.ReadAllText(LayoutPath));
            if (saved?.TryGetValue(row.Session.Slot, out var layout) == true) { row.Layout = layout; row.Positioned = true; }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
