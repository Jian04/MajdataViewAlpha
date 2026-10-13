using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

/// <summary>Complete options for one recording run started by the recording window and included in EditRequestjson.</summary>
public class RecordVideoOptions
{
    public int FrameRate = 60;
    public string FileName = "out.mp4";
    public int Width;
    public int Height;
    /// <summary>Whether View should reveal the output after recording.</summary>
    public bool RevealOutput = true;
    public string UtageLabel = "\u5bb4";
    public bool UtageCoop;
}

/// <summary>
/// Video recording window for frame rate, resolution, intro style, song-card style, and AP presentation.
/// Layered export is temporarily disabled because full-screen filters cannot be isolated by scene root.
/// </summary>
public partial class RecordVideoWindow : Window
{
    private readonly MainWindow main;
    private bool running;
    private bool stopRequested;
    private bool windowLoaded;
    private CancellationTokenSource? cancelSource;
    private readonly ChartListEntry[]? batchEntries;
    private readonly System.Windows.Controls.ComboBox batchMode = new();
    private readonly System.Windows.Threading.DispatcherTimer metadataSave = new() { Interval=TimeSpan.FromMilliseconds(600) };

    internal RecordVideoWindow(MainWindow main, ChartListEntry[]? batchEntries = null)
    {
        InitializeComponent();
        this.main = main;
        this.batchEntries = batchEntries;
        metadataSave.Tick += (_, _) => { metadataSave.Stop(); if (windowLoaded && batchEntries == null) SyncSettingsToMain(); };
        foreach (var input in new[]{TitleBox, ArtistBox, DesignerBox, LevelBox, BpmBox, ClockBox, UtageLabelBox})
            input.TextChanged += (_, _) => {
                if (!windowLoaded || batchEntries != null) return;
                metadataSave.Stop(); metadataSave.Start();
            };
        UtageCoopBox.Checked += (_, _) => { if (windowLoaded && batchEntries == null) SyncSettingsToMain(); };
        UtageCoopBox.Unchecked += (_, _) => { if (windowLoaded && batchEntries == null) SyncSettingsToMain(); };
        if (batchEntries != null)
        {
            Title = "批量录制";
            RefreshPreviewButton.Visibility = Visibility.Collapsed;
            var root = (System.Windows.Controls.Grid)Content;
            root.Children.OfType<System.Windows.Controls.Border>().First(x => System.Windows.Controls.Grid.GetRow(x) == 1).Visibility = Visibility.Collapsed;
            root.Children.OfType<System.Windows.Controls.Grid>().First(x => System.Windows.Controls.Grid.GetRow(x) == 0).Visibility = Visibility.Collapsed;
            var footer = (System.Windows.Controls.StackPanel)StartButton.Parent;
            footer.Orientation = System.Windows.Controls.Orientation.Vertical;
            footer.HorizontalAlignment = HorizontalAlignment.Stretch;
            batchMode.ItemsSource = new[] { "排队导出 · 同时 1 首", "并行导出 · 同时 2 首", "并行导出 · 同时 3 首" };
            batchMode.SelectedIndex = 0; MajdataSessionUi.SessionTheme.Input(batchMode);
            batchMode.Margin = new Thickness(0, 0, 0, 8); footer.Children.Insert(0, batchMode);
            StartButton.Content = "开始批量录制"; StartButton.HorizontalAlignment = HorizontalAlignment.Right;
            StatusText.Text = $"共 {batchEntries.Length} 个难度，使用相同录制参数。输出 out_难度编号.mp4（MASTER 为 out_5.mp4）。";
        }
        var (songDetailStyle, showSongDetail, showAllPerfect, introStyle) = main.GetRecordDisplayDefaults();
        SongDetailStyleBox.SelectedIndex = songDetailStyle == 1 ? 1 : 0;
        SongDetailBox.IsChecked = showSongDetail;
        AllPerfectBox.IsChecked = showAllPerfect;
        ViewIntroStyleBox.SelectedIndex = StyleToIndex(introStyle);
        ViewIntroStyleBox.SelectionChanged += (_, _) =>
        {
            if (!windowLoaded || ViewIntroStyleBox.SelectedIndex < 0) return;
            main.editorSetting!.ViewIntroStyle = IndexToStyle(ViewIntroStyleBox.SelectedIndex);
            main.SaveEditorSetting(); main.SendDisplaySettings();
        };
        UtageOptionsPanel.Visibility = main.IsOriginalDifficulty
            ? Visibility.Visible
            : Visibility.Collapsed;
        // Show the adjacent resolution fields only when Custom is selected.
        ResolutionBox.SelectionChanged += (_, _) =>
        {
            var custom = ResolutionBox.SelectedIndex == ResolutionBox.Items.Count - 1;
            CustomResolutionBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        };
        SongDetailStyleBox.SelectionChanged += (_, _) =>
        {
            if (!windowLoaded) return;
            main.ApplyRecordDisplaySettings(SongDetailStyleBox.SelectedIndex,
                SongDetailBox.IsChecked == true, AllPerfectBox.IsChecked == true,
                IndexToStyle(ViewIntroStyleBox.SelectedIndex));
            main.SendDisplaySettings();
            LoadSelectedPreview();
        };
    }

    private string GetSelectedResolutionText() =>
        ResolutionBox.SelectedIndex == 0
            ? string.Empty
            : (ResolutionBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty;

    private static string L(string key, params object[] args)
    {
        var value = MainWindow.GetLocalizedString(key);
        return args.Length == 0 ? value : string.Format(value, args);
    }

    private void RecordVideoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (batchEntries != null) { windowLoaded = true; UtageOptionsPanel.Visibility = Visibility.Collapsed; return; }
        var info = main.GetRecordSongInfo();
        DifficultyText.Text = info.difficulty;
        TitleBox.Text = info.title;
        ArtistBox.Text = info.artist;
        DesignerBox.Text = info.designer;
        LevelBox.Text = info.level;
        BpmBox.Text = info.bpm;
        ClockBox.Text = info.clock;
        var utage = main.GetRecordUtageDefaults();
        UtageLabelBox.Text = utage.label;
        UtageCoopBox.IsChecked = utage.coop;
        windowLoaded = true;
        LoadSelectedPreview();
    }

    private void LoadSelectedPreview()
    {
        if (batchEntries != null) return;
        if (SongDetailStyleBox.SelectedIndex == 1)
        {
            var cachedPreview = main.PrepareSongDetailPreview(
                TitleBox.Text, ArtistBox.Text, DesignerBox.Text, LevelBox.Text, BpmBox.Text,
                NormalizeUtageLabel(), UtageCoopBox.IsChecked == true);
            if (!string.IsNullOrWhiteSpace(cachedPreview))
            {
                LoadCoverPreview(cachedPreview, false);
                return;
            }

            CoverPreview.Source = null;
            return;
        }

        LoadCoverPreview();
    }

    private void LoadCoverPreview(string? preferredPath = null, bool allowCoverFallback = true)
    {
        var png = Path.Combine(MainWindow.maidataDir, "bg.png");
        var jpg = Path.Combine(MainWindow.maidataDir, "bg.jpg");
        var path = !string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath)
            ? preferredPath
            : allowCoverFallback
                ? File.Exists(png) ? png : File.Exists(jpg) ? jpg : null
                : null;
        if (path == null)
            return;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            CoverPreview.Source = image;
            RenderOptions.SetBitmapScalingMode(CoverPreview, BitmapScalingMode.HighQuality);
        }
        catch (Exception exception)
        {
            CoverPreview.Source = null;
            if (!allowCoverFallback)
                main.ReportSongDetailError(exception);
            else
                MessageBox.Show("封面预览加载失败：\n" + exception.Message);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (running)
            return;

        if (batchEntries == null && string.IsNullOrEmpty(MainWindow.maidataDir))
        {
            StatusText.Text = L("RecordOpenChartFirst");
            return;
        }

        var frameRate = FrameRateBox.SelectedIndex == 1 ? 120 : 60;
        var resolutionText = GetSelectedResolutionText();
        if (ResolutionBox.SelectedIndex == ResolutionBox.Items.Count - 1)
            resolutionText = CustomResolutionBox.Text;
        if (!TryParseResolution(resolutionText, out var width, out var height))
        {
            StatusText.Text = L("RecordInvalidResolution");
            return;
        }
        if (!IsRecordingLoadSafe(width, height, frameRate) &&
            MessageBox.Show(
                L("RecordResolutionHighWarning", width, height, frameRate),
                MainWindow.GetLocalizedString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (batchEntries != null)
        {
            var existing = batchEntries.Count(x => File.Exists(Path.Combine(x.Folder, "outputs", $"out_{x.Difficulty + 1}.mp4")));
            if (existing > 0 && MessageBox.Show($"其中 {existing} 个难度已有导出视频，成功导出后将替换对应的 out_难度编号.mp4，是否继续？", "批量录制", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            main.ApplyRecordDisplaySettings(SongDetailStyleBox.SelectedIndex, SongDetailBox.IsChecked == true,
                AllPerfectBox.IsChecked == true, IndexToStyle(ViewIntroStyleBox.SelectedIndex));
            running = true; StartButton.IsEnabled = false; batchMode.IsEnabled = false;
            CancelButton.Content = L("Cancel"); CancelButton.Visibility = Visibility.Visible;
            using var stop = cancelSource = new CancellationTokenSource();
            try
            {
                await main.ExportChartList(batchEntries, new RecordVideoOptions { FrameRate = frameRate, Width = width, Height = height },
                    batchMode.SelectedIndex + 1, message => StatusText.Text = message, stop.Token);
            }
            catch (OperationCanceledException) { StatusText.Text = "批量录制已停止，已完成的视频已保留。"; }
            catch (Exception error) { StatusText.Text = "批量录制失败：" + error.Message; }
            finally { running = false; StartButton.IsEnabled = true; batchMode.IsEnabled = true; CancelButton.Visibility = Visibility.Collapsed; cancelSource = null; }
            return;
        }
        SyncSettingsToMain();

        var runs = new List<RecordVideoOptions>
        {
            new()
            {
                FrameRate = frameRate,
                Width = width,
                Height = height,
                FileName = "out.mp4",
                UtageLabel = NormalizeUtageLabel(),
                UtageCoop = UtageCoopBox.IsChecked == true
            }
        };

        running = true;
        stopRequested = false;
        StartButton.IsEnabled = false;
        CancelButton.Content = MainWindow.GetLocalizedString("Cancel");
        CancelButton.Visibility = Visibility.Visible;
        cancelSource = new CancellationTokenSource();
        // Per-run timeout: virtual-clock recording may be faster or slower than real time, so double chart length plus presentation/finalization headroom.
        var timeout = TimeSpan.FromSeconds(Math.Max(120, main.GetChartLengthForRecord() * 2 + 90));

        try
        {
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var prefix = runs.Count > 1
                    ? $"{i + 1}/{runs.Count} {run.FileName}"
                    : run.FileName;
                // StartRecordRun renders the audio track synchronously. Let Dispatcher.Yield render the status first
                // before entering the blocking section, or the window appears frozen.
                StatusText.Text = L("RecordRenderingAudio", prefix);
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.Background);
                main.StartRecordRun(run);
                StatusText.Text = L("RecordPreparing", prefix);
                var outputPath = Path.Combine(MainWindow.maidataDir, run.FileName);
                var ok = await WaitForOutput(outputPath, timeout, cancelSource.Token, (elapsed, size) =>
                    StatusText.Text = size < 0
                        ? L("RecordPreparingElapsed", prefix, elapsed.TotalSeconds)
                        : L("RecordProgress", prefix, elapsed.TotalSeconds, size / 1048576.0));
                main.FinishRecordRun();
                if (!ok)
                {
                    RequestViewStop();
                    StatusText.Text = cancelSource.IsCancellationRequested
                        ? L("RecordCancelled")
                        : L("RecordTimeout", run.FileName);
                    return;
                }
            }

            StatusText.Text = L("RecordDone");
        }
        finally
        {
            running = false;
            StartButton.IsEnabled = true;
            CancelButton.Content = L("Close");
            CancelButton.Visibility = Visibility.Collapsed;
            main.pendingRecordOptions = null;
            cancelSource?.Dispose();
            cancelSource = null;
        }
    }

    private static bool TryParseResolution(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        text = text.Trim();
        if (text.Length == 0)
            return true;

        var parts = text.Replace(" ", string.Empty)
            .Split(new[] { '×', 'x', 'X', '*' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], out width) &&
               int.TryParse(parts[1], out height) &&
               width > 0 && height > 0 && width % 2 == 0 && height % 2 == 0;
    }

    private static bool IsRecordingLoadSafe(int width, int height, int frameRate)
    {
        // Zero means "keep View's current size"; View performs the same check after
        // resolving that actual size.
        if (width == 0 || height == 0)
            return true;
        var maxLongSide = frameRate >= 120 ? 1920 : 2560;
        var maxShortSide = frameRate >= 120 ? 1080 : 1440;
        return Math.Max(width, height) <= maxLongSide &&
               Math.Min(width, height) <= maxShortSide;
    }

    /// <summary>A run, including ffmpeg finalization, is complete when its output exists, is nonempty, and stays the same size for five seconds.</summary>
    private static async Task<bool> WaitForOutput(string path, TimeSpan timeout, CancellationToken token,
        Action<TimeSpan, long>? progress = null)
    {
        var startedAt = DateTime.Now;
        long lastSize = -1;
        var stableSince = DateTime.MinValue;

        while (DateTime.Now - startedAt < timeout)
        {
            if (token.IsCancellationRequested)
                return false;
            await Task.Delay(1000);

            var info = new FileInfo(path);
            progress?.Invoke(DateTime.Now - startedAt, info.Exists ? info.Length : -1);
            if (!info.Exists || info.Length <= 0)
                continue;

            if (info.Length == lastSize)
            {
                if (stableSince == DateTime.MinValue)
                    stableSince = DateTime.Now;
                else if (DateTime.Now - stableSince > TimeSpan.FromSeconds(5))
                    return true;
            }
            else
            {
                lastSize = info.Length;
                stableSince = DateTime.MinValue;
            }
        }

        return false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (running)
        {
            cancelSource?.Cancel();
            RequestViewStop();
            StatusText.Text = L("Cancelling");
        }
        else
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (running)
        {
            // Do not close during recording; cancel the active encoder first.
            cancelSource?.Cancel();
            RequestViewStop();
            e.Cancel = true;
            StatusText.Text = L("CancellingBeforeClose");
            return;
        }
        metadataSave.Stop();
        if (batchEntries == null && windowLoaded) SyncSettingsToMain();
        base.OnClosing(e);
    }

    private void RequestViewStop()
    {
        if (batchEntries != null) return;
        if (stopRequested)
            return;
        stopRequested = true;
        main.CancelRecordRun();
    }

    private void SyncSettingsToMain()
    {
        main.ApplyRecordSongInfo(
            TitleBox.Text,
            ArtistBox.Text,
            DesignerBox.Text,
            LevelBox.Text,
            BpmBox.Text,
            ClockBox.Text);
        main.ApplyRecordUtageInfo(NormalizeUtageLabel(), UtageCoopBox.IsChecked == true);
        main.ApplyRecordDisplaySettings(
            SongDetailStyleBox.SelectedIndex,
            SongDetailBox.IsChecked == true,
            AllPerfectBox.IsChecked == true,
            IndexToStyle(ViewIntroStyleBox.SelectedIndex));
    }

    private string NormalizeUtageLabel() =>
        string.IsNullOrWhiteSpace(UtageLabelBox.Text) ? "\u5bb4" : UtageLabelBox.Text.Trim();

    private void RefreshPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        main.ApplyRecordUtageInfo(NormalizeUtageLabel(), UtageCoopBox.IsChecked == true);
        if (SongDetailStyleBox.SelectedIndex != 1)
        {
            LoadSelectedPreview();
            return;
        }

        var path = main.PrepareSongDetailPreview(
            TitleBox.Text,
            ArtistBox.Text,
            DesignerBox.Text,
            LevelBox.Text,
            BpmBox.Text,
            NormalizeUtageLabel(),
            UtageCoopBox.IsChecked == true,
            forceRefresh: true);
        if (!string.IsNullOrWhiteSpace(path))
            LoadCoverPreview(path, false);
    }

    private static int StyleToIndex(string? style) => style?.ToLowerInvariant() switch
    {
        "circleplus" => 1,
        "circle" => 2,
        "chunithm-xversex" => 3,
        "chunithm-mate" => 4,
        "magical" => 5,
        _ => 0
    };

    private static string IndexToStyle(int index) => index switch
    {
        1 => "circleplus",
        2 => "circle",
        3 => "chunithm-xversex",
        4 => "chunithm-mate",
        5 => "magical",
        _ => "default"
    };
}
