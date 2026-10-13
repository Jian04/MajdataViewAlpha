using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MajdataSessionUi;
using Newtonsoft.Json.Linq;

namespace MajdataEdit;

internal sealed class MajnetPanel : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private const string Api = "https://majdata.net/api3/api/maichart/";
    private readonly TextBox query = new(), destination = new();
    private readonly TextBlock status = SessionTheme.Text("", true, size: 12);
    private readonly ComboBox sort = new();
    private readonly CheckBox excludePv = new() { Content = "不下载 PV", Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel results = new();
    private readonly Dictionary<string, JObject> selected = new();
    private readonly List<CheckBox> checks = new();
    private CancellationTokenSource? searchStop, downloadStop;
    private readonly ScrollViewer list = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Action<string, bool> openChart;
    private readonly Dictionary<string, string> downloaded = new();
    private readonly Dictionary<string, (Button Open, Button Preview)> localActions = new();
    private readonly Button download = SessionTheme.Button("下载已选（0）");
    private readonly ProgressBar downloadProgress = new() { Minimum = 0, Maximum = 100, Height = 6, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 6) };
    private int page;
    private bool downloading, closed, loading, hasMore = true;
    private string activeQuery = "", activeSort = "";

    public MajnetPanel(Action<string, bool> openChart)
    {
        this.openChart = openChart;
        Title = "Majnet"; SessionTheme.Apply(this, 400, 600);
        var root = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        SessionTheme.Input(query); query.ToolTip = "与 Majnet 网站相同的搜索语法，例如 tag:Original";
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { page = 0; await Search(); } };
        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sort.ItemsSource = new[] { "默认排序", "最新上传", "最多播放" }; sort.SelectedIndex = 0; SessionTheme.Input(sort);
        searchRow.Children.Add(query);
        var search = SessionTheme.Button("搜索"); search.Margin = new Thickness(8, 0, 0, 0);
        search.Click += async (_, _) => { page = 0; await Search(); }; Grid.SetColumn(search, 1); searchRow.Children.Add(search);
        top.Children.Add(searchRow);
        var selectionRow = new WrapPanel();
        sort.Width = 100; sort.Margin = new Thickness(0, 0, 6, 0); selectionRow.Children.Add(sort);
        var all = SessionTheme.Button("全选"); all.Click += (_, _) => { foreach (var check in checks) check.IsChecked = true; };
        var clear = SessionTheme.Button("清空选择"); clear.Margin = new Thickness(6, 0, 0, 0);
        clear.Click += (_, _) => { selected.Clear(); foreach (var check in checks) check.IsChecked = false; UpdateSelection(); };
        selectionRow.Children.Add(all); selectionRow.Children.Add(clear); top.Children.Add(selectionRow);
        var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        destination.Text = Path.Combine(ChartListStore.InstallRoot, "Downloads");
        SessionTheme.Input(destination); destination.ToolTip = "下载目录";
        destination.Margin = new Thickness(0, 0, 0, 6); bottom.Children.Add(destination);
        bottom.Children.Add(downloadProgress);
        excludePv.SetResourceReference(Control.TemplateProperty, "DarkCheck");
        bottom.Children.Add(excludePv);
        var actions = new WrapPanel();
        download.Click += async (_, _) => await Download(selected.Values.ToArray());
        var cancel = SessionTheme.Button("取消下载"); cancel.Margin = new Thickness(6, 0, 0, 0);
        cancel.Click += (_, _) => downloadStop?.Cancel(); actions.Children.Add(download); actions.Children.Add(cancel); bottom.Children.Add(actions);
        var addAll = SessionTheme.Button("全部加入列表");
        addAll.Click += (_, _) =>
        {
            var added = 0;
            foreach (var id in selected.Keys.ToArray())
                if (downloaded.TryGetValue(id, out var folder) && File.Exists(Path.Combine(folder, "maidata.txt")))
                {
                    try { ChartListStore.Add(ChartListStore.InspectAll(folder)); added++; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; return; }
                }
            status.Text = $"已加入 {added} 个谱面" + (added < selected.Count ? $" · {selected.Count - added} 个尚未下载" : "");
        };
        actions.Children.Add(addAll);
        status.Margin = new Thickness(0, 6, 0, 0); status.MaxHeight = 48;
        bottom.Children.Add(new ScrollViewer { Content = status, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 48 });
        list.Content = results; list.Margin = new Thickness(0, 6, 0, 0);
        list.ScrollChanged += async (_, _) =>
        {
            if (!loading && hasMore && !closed && list.ViewportHeight > 0 && list.VerticalOffset >= list.ScrollableHeight - 100)
                await Search(true);
        };
        root.Children.Add(list);
        foreach (var control in selectionRow.Children.OfType<FrameworkElement>().Concat(actions.Children.OfType<FrameworkElement>()))
            control.Margin = new Thickness(0, 0, 6, 6);
        sort.SelectionChanged += async (_, _) => await Search();
        Content = root;
        Loaded += async (_, _) => await Search();
        Closed += (_, _) => { closed = true; searchStop?.Cancel(); downloadStop?.Cancel(); };
    }
    private void UpdateSelection() => download.Content = $"下载已选（{selected.Count}）";
    private async Task Search(bool append = false)
    {
        if (append && (loading || !hasMore)) return;
        searchStop?.Cancel();
        var stop = searchStop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        loading = true;
        if (!append)
        {
            page = 0; hasMore = true; activeQuery = query.Text.Trim();
            activeSort = new[] { "", "timep", "playp" }[sort.SelectedIndex];
            results.Children.Clear(); checks.Clear(); localActions.Clear(); list.ScrollToTop();
        }
        status.Text = append ? "正在加载更多…" : "正在搜索…";
        try
        {
            var json = await Http.GetStringAsync(Api + $"list?sort={activeSort}&page={page}&search={Uri.EscapeDataString(activeQuery)}", stop.Token);
            var charts = JArray.Parse(json).OfType<JObject>().ToArray();
            if (stop.IsCancellationRequested || closed) return;
            var previousCount = checks.Count;
            foreach (var chart in charts)
            {
                var id = (string?)chart["id"];
                if (!Guid.TryParse(id, out _)) continue;
                if (localActions.ContainsKey(id)) continue;
                var card = new StackPanel();
                var check = new CheckBox { Tag = id, Content = SessionTheme.Text((string?)chart["title"] ?? id), IsChecked = selected.ContainsKey(id), Margin = new Thickness(0, 0, 0, 5) };
                check.SetResourceReference(Control.TemplateProperty, "DarkCheck");
                check.Checked += (_, _) => { selected[id] = chart; UpdateSelection(); };
                check.Unchecked += (_, _) => { selected.Remove(id); UpdateSelection(); };
                checks.Add(check); card.Children.Add(check);
                var levels = chart["levels"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)) ?? Array.Empty<string>();
                card.Children.Add(SessionTheme.Text($"{chart["artist"]}\n谱师：{chart["designer"]}\n难度：{string.Join(" / ", levels)}", true));
                var tags = new[] { chart["tags"], chart["publicTags"] }.OfType<JArray>().SelectMany(x => x.Values<string>()).Distinct();
                card.Children.Add(SessionTheme.Text(string.Join(" · ", tags), true));
                var row = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
                var browse = SessionTheme.Button("查看 / 试玩"); browse.Click += (_, _) => OpenUrl("https://majdata.net/song?id=" + Uri.EscapeDataString(id));
                var get = SessionTheme.Button("下载"); get.Margin = new Thickness(6, 0, 0, 0);
                get.Click += async (_, _) => await Download(new[] { chart }); row.Children.Add(browse); row.Children.Add(get);
                var open = SessionTheme.Button("打开谱面");
                var preview = SessionTheme.Button("加入谱面列表");
                open.Click += (_, _) => OpenDownloaded(id, false);
                preview.Click += (_, _) => OpenDownloaded(id, true);
                var localPath = Path.Combine(Path.GetFullPath(destination.Text.Trim()), ChartFolderName(chart, id));
                if (File.Exists(Path.Combine(localPath, "maidata.txt"))) downloaded[id] = localPath;
                open.IsEnabled = preview.IsEnabled = downloaded.ContainsKey(id);
                localActions[id] = (open, preview);
                row.Children.Add(open); row.Children.Add(preview);
                foreach (var button in row.Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 6, 6);
                card.Children.Add(row); results.Children.Add(SessionTheme.Card(card));
            }
            hasMore = charts.Length > 0 && checks.Count > previousCount; page++;
            status.Text = $"已加载 {checks.Count} 个谱面" + (hasMore ? "" : " · 已到底");
        }
        catch (OperationCanceledException) { if (ReferenceEquals(stop, searchStop) && !closed) status.Text = "搜索已取消或超时，可重试。"; }
        catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException or IOException or ArgumentException)
        { if (ReferenceEquals(stop, searchStop) && !closed) status.Text = "搜索失败：" + ex.Message; }
        finally { stop.Dispose(); if (ReferenceEquals(searchStop, stop)) { searchStop = null; loading = false; } }
    }
    private static string ChartFolderName(JObject chart, string id)
    {
        var name = string.Concat(((string?)chart["title"] ?? "Chart").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length > 70) name = name[..70];
        return name + "_" + id;
    }
    private void OpenDownloaded(string id, bool preview)
    {
        try
        {
            if (downloaded.TryGetValue(id, out var folder) && File.Exists(Path.Combine(folder, "maidata.txt"))) openChart(folder, preview);
            else status.Text = "本地谱面已移动或删除，请重新下载。";
        }
        catch (Exception e) { status.Text = e.Message; }
    }
    private async Task Download(JObject[] charts)
    {
        if (downloading || charts.Length == 0) return;
        downloading = true; download.IsEnabled = false;
        downloadProgress.Visibility = Visibility.Visible; downloadProgress.Value = 0;
        using var stop = downloadStop = new CancellationTokenSource();
        int completed = 0; var failures = new List<string>();
        string root = "";
        try
        {
            root = Path.GetFullPath(destination.Text.Trim()); Directory.CreateDirectory(root);
            var skipPv = excludePv.IsChecked == true;
            foreach (var chart in charts)
            {
                stop.Token.ThrowIfCancellationRequested();
                var id = (string?)chart["id"] ?? "";
                if (!Guid.TryParse(id, out _)) continue;
                var name = string.Concat(((string?)chart["title"] ?? "Chart").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
                if (name.Length > 70) name = name[..70];
                var final = Path.Combine(root, ChartFolderName(chart, id));
                if (Directory.Exists(final))
                {
                    if (File.Exists(Path.Combine(final, "maidata.txt")) &&
                        (File.Exists(Path.Combine(final, "track.mp3")) || File.Exists(Path.Combine(final, "track.ogg"))))
                    {
                        downloaded[id] = final; completed++;
                        if (localActions.TryGetValue(id, out var existingButtons))
                            existingButtons.Open.IsEnabled = existingButtons.Preview.IsEnabled = true;
                    }
                    else failures.Add(name + "：已有目录不完整，请移动该目录后重试");
                    continue;
                }
                var temp = Path.Combine(root, ".majnet-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                try
                {
                    var fileIndex = 0;
                    var files = new[] { ("chart", "maidata.txt", false), ("track", "track.mp3", false), ("image?fullImage=true", "bg.jpg", true), ("video", "pv.mp4", true) }
                        .Where(file => !skipPv || file.Item1 != "video").ToArray();
                    foreach (var file in files)
                    {
                        status.Text = $"下载 {completed + failures.Count + 1}/{charts.Length} · {name} · {file.Item2}";
                        using var response = await Http.GetAsync(Api + id + "/" + file.Item1, HttpCompletionOption.ResponseHeadersRead, stop.Token);
                        if (file.Item3 && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) { fileIndex++; continue; }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentType?.MediaType == "text/html") throw new IOException("服务器返回了网页，请稍后重试");
                        await using var stream = await response.Content.ReadAsStreamAsync(stop.Token);
                        await using var output = File.Create(Path.Combine(temp, file.Item2));
                        var total = response.Content.Headers.ContentLength;
                        downloadProgress.IsIndeterminate = !total.HasValue || total.Value <= 0;
                        var buffer = new byte[65536]; long received = 0; var lastUpdate = Stopwatch.StartNew();
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(), stop.Token)) != 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), stop.Token); received += read;
                            if (lastUpdate.ElapsedMilliseconds < 100) continue;
                            lastUpdate.Restart();
                            status.Text = $"下载 {completed + failures.Count + 1}/{charts.Length} · {name} · {file.Item2}\n{received / 1048576d:F1} MB" + (total.HasValue ? $" / {total.Value / 1048576d:F1} MB" : "");
                            if (total > 0) downloadProgress.Value = 100d * (completed + failures.Count + (fileIndex + Math.Clamp((double)received / total.Value, 0, 1)) / files.Length) / charts.Length;
                        }
                        fileIndex++;
                    }
                    Directory.Move(temp, final); completed++;
                    downloaded[id] = final;
                    if (localActions.TryGetValue(id, out var buttons)) buttons.Open.IsEnabled = buttons.Preview.IsEnabled = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException) { failures.Add(name + "：" + ex.Message); }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { failures.Add(name + "：下载超时，请重试"); }
                finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            }
            status.Text = $"已下载 {completed}/{charts.Length} 个谱面" + (failures.Count > 0 ? "\n" + string.Join("\n", failures) : "");
            downloadProgress.IsIndeterminate = false; downloadProgress.Value = 100;
            if (completed > 0 && !closed) OpenUrl(root);
        }
        catch (OperationCanceledException) { if (!closed) status.Text = $"已取消，保留 {completed} 个完整下载。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (!closed) status.Text = "下载失败：" + ex.Message; }
        finally
        {
            downloading = false; downloadStop = null; download.IsEnabled = true;
            downloadProgress.IsIndeterminate = false;
            foreach (var check in checks) check.IsChecked = selected.ContainsKey((string)check.Tag);
            UpdateSelection();
        }
    }
    internal static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
