using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

public partial class ChartLibraryWindow : Window
{
    private CancellationTokenSource? searchCts;
    private List<RhythmSearchResult> currentResults = new();

    public ChartLibraryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var root = FindChartRoot();
            StatusText.Text = root is null
                ? "未找到 charts 谱面库（请确认程序目录下有 charts 文件夹）"
                : "输入节奏型后点击搜索。";
            ProgressText.Text = root is null ? string.Empty : $"谱面库：{root}";
        };
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var root = FindChartRoot();
        if (root == null)
        {
            StatusText.Text = "未找到 charts 谱面库（请确认程序目录下有 charts 文件夹）";
            ProgressText.Text = string.Empty;
            return;
        }

        var query = QueryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "请输入节奏型。";
            ProgressText.Text = string.Empty;
            return;
        }

        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        currentResults = new List<RhythmSearchResult>();
        ResultList.ItemsSource = null;
        MatchList.ItemsSource = null;
        DetailBox.Clear();
        StatusText.Text = "搜索中...";
        ProgressText.Text = "正在读取谱面库并匹配节奏型。";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var diffs = GetSelectedDifficulties();
            var exact = ExactModeBox.IsChecked == true;
            var fuzzy = FuzzyModeBox.IsChecked == true;
            var progress = new Progress<RhythmSearchProgress>(value =>
            {
                var fileName = Path.GetFileName(Path.GetDirectoryName(value.File) ?? value.File);
                ProgressText.Text = $"搜索进度：{value.Current}/{value.Total} 谱面  {fileName}";
            });
            var results = await Task.Run(() =>
                ChartRhythmSearchEngine.Search(root, query, exact, fuzzy, diffs, token, progress).ToList(), token);
            currentResults = SortResults(results);
            ResultList.ItemsSource = currentResults;
            StatusText.Text = $"完成：{currentResults.Count} 个结果，{currentResults.Sum(r => r.Matches.Count)} 处匹配。";
            ProgressText.Text = $"耗时 {stopwatch.Elapsed.TotalSeconds:F2}s，难度：{string.Join(", ", diffs.OrderBy(x => x))}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消。";
            ProgressText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = "搜索失败。";
            ProgressText.Text = ex.Message;
        }
    }

    private List<RhythmSearchResult> SortResults(IEnumerable<RhythmSearchResult> results)
    {
        Func<RhythmSearchResult, double> key = SortComboBox.SelectedIndex switch
        {
            1 => result => result.LevelSort,
            2 => result => result.SortBpm,
            3 => result => result.SortDensity,
            4 => result => result.SortStars,
            _ => result => result.Count
        };

        return SortDescendingBox.IsChecked == true
            ? results.OrderByDescending(key).ThenBy(r => r.Title).ToList()
            : results.OrderBy(key).ThenBy(r => r.Title).ToList();
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is not RhythmSearchResult result)
        {
            MatchList.ItemsSource = null;
            return;
        }

        MatchList.ItemsSource = result.Matches;
        if (result.Matches.Count > 0)
            MatchList.SelectedIndex = 0;
    }

    private void MatchList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDetail();

    private void CopyMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (MatchList.SelectedItem is not RhythmMatch match)
            return;
        Clipboard.SetText(match.Match);
        StatusText.Text = "已复制匹配段落。";
    }

    private void CopyContextButton_Click(object sender, RoutedEventArgs e)
    {
        if (MatchList.SelectedItem is not RhythmMatch match)
            return;
        Clipboard.SetText(match.Context);
        StatusText.Text = "已复制上下文。";
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not RhythmSearchResult result)
            return;
        Clipboard.SetText(result.File);
        StatusText.Text = "已复制 maidata 路径。";
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not RhythmSearchResult result)
            return;
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.OpenFile(result.Directory);
            Close();
        }
    }

    private void RefreshDetail()
    {
        if (ResultList.SelectedItem is not RhythmSearchResult result ||
            MatchList.SelectedItem is not RhythmMatch match)
        {
            DetailBox.Clear();
            return;
        }

        var bpm = match.Bpm.HasValue ? ((int)match.Bpm.Value).ToString() : "?";
        DetailBox.Text =
            $"{result.Title} [{result.Difficulty} Lv.{result.Level}]\r\n" +
            $"BPM={bpm}   第 {match.ComboBefore}cb 起 / 共 {match.Combo}cb   ☆{match.StarCount}   " +
            $"{match.TimeSeconds:F2}s -> {match.EndSeconds:F2}s\r\n" +
            $"{result.File}\r\n\r\n" +
            match.Context;
    }

    private HashSet<string> GetSelectedDifficulties()
    {
        var result = new HashSet<string>();
        AddDiff(result, Diff2, "2");
        AddDiff(result, Diff3, "3");
        AddDiff(result, Diff4, "4");
        AddDiff(result, Diff5, "5");
        AddDiff(result, Diff6, "6");
        return result;
    }

    private static void AddDiff(ISet<string> target, CheckBox box, string diff)
    {
        if (box.IsChecked == true)
            target.Add(diff);
    }

    private static string? FindChartRoot()
    {
        // 谱面库 charts 随发布包放在程序目录下,exe 旁即是,下载者解压即用。
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "charts"),
            Path.Combine(Environment.CurrentDirectory, "charts")
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    protected override void OnClosed(EventArgs e)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        base.OnClosed(e);
    }
}
