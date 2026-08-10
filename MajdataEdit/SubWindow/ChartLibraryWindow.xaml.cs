using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

public partial class ChartLibraryWindow : Window
{
    private readonly string initialQuery;
    private CancellationTokenSource? searchCts;
    private List<RhythmSearchResult> currentResults = new();

    private static string L(string key, params object[] args)
    {
        var value = MainWindow.GetLocalizedString(key);
        return args.Length == 0 ? value : string.Format(value, args);
    }

    public ChartLibraryWindow(string? initialQuery = null)
    {
        this.initialQuery = initialQuery?.Trim() ?? string.Empty;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var root = FindChartRoot();
            StatusText.Text = root is null
                ? L("ChartLibraryMissing")
                : L("ChartLibraryPrompt");
            ProgressText.Text = root is null ? string.Empty : L("ChartLibraryPath", root);
            if (root != null && !string.IsNullOrWhiteSpace(this.initialQuery))
            {
                QueryBox.Text = this.initialQuery;
                SearchButton_Click(this, new RoutedEventArgs());
            }
        };
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var root = FindChartRoot();
        if (root == null)
        {
            StatusText.Text = L("ChartLibraryMissing");
            ProgressText.Text = string.Empty;
            return;
        }

        var query = QueryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = L("ChartLibraryEnterPattern");
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
        StatusText.Text = L("ChartLibrarySearching");
        ProgressText.Text = L("ChartLibraryReading");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var diffs = GetSelectedDifficulties();
            var exact = ExactModeBox.IsChecked == true;
            var fuzzy = FuzzyModeBox.IsChecked == true;
            var progress = new Progress<RhythmSearchProgress>(value =>
            {
                var fileName = Path.GetFileName(Path.GetDirectoryName(value.File) ?? value.File);
                ProgressText.Text = L("ChartLibraryProgress", value.Current, value.Total, fileName);
            });
            var results = await Task.Run(() =>
                ChartRhythmSearchEngine.Search(root, query, exact, fuzzy, diffs, token, progress).ToList(), token);
            currentResults = SortResults(results);
            ResultList.ItemsSource = currentResults;
            StatusText.Text = L("ChartLibraryComplete", currentResults.Count,
                currentResults.Sum(r => r.Matches.Count));
            ProgressText.Text = L("ChartLibraryElapsed", stopwatch.Elapsed.TotalSeconds,
                string.Join(", ", diffs.OrderBy(x => x)));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = L("ChartLibraryCancelled");
            ProgressText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = L("ChartLibraryFailed");
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

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not RhythmSearchResult result)
            return;
        Clipboard.SetText(result.File);
        StatusText.Text = L("ChartLibraryCopiedPath");
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
            L("ChartLibraryPosition", bpm, match.ComboBefore, match.Combo, match.StarCount,
                match.TimeSeconds, match.EndSeconds) + "\r\n" +
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
        // Release packages place the chart-library charts beside the executable so they work immediately after extraction.
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
