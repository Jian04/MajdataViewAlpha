using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Un4seen.Bass;
using MajdataEdit.AutoSaveModule;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using MajdataSessionUi;

namespace MajdataEdit;

public partial class MainWindow
{
    private ColumnDefinition? chartFilesColumn;
    private GridSplitter? chartFilesSplitter;
    private double chartFilesWidth = 220;
    private TreeView? chartFilesTree;
    private Image? chartFilePreview;
    private TextBlock? chartFilePreviewName;
    private StackPanel? chartFilePreviewPane;
    private TabControl? chartFilesPane;
    private TabItem? chartFilesTab;
    private Window? detachedChartFiles;
    private Button? chartFilesButton;
    private Button? bottomPaneButton;
    private Button? codeOverviewButton;
    private CodeOverview? codeOverview;
    private bool codeOverviewVisible;
    private Grid? waveformPane;
    private GridSplitter? waveformSplitter;
    private RowDefinition? waveformRow;
    private bool bottomPaneVisible = true;
    private bool chartFilesVisible;
    private double waveformPaneHeight = 80;
    private TabControl? editorPages;
    private TabItem? mediaPage;
    private TabItem? chartPage;
    private TextBlock? chartPageTitle;
    private bool updatingEditorPage;
    private TextBlock? totalDurationText;
    private TextBlock? playbackRateText;
    private FrameworkElement? editorMetadata;

    private Grid CreateEditorPanes(Grid editor)
    {
        // MusicWave used to cover this last row through RowSpan. Once moved out,
        // leaving its star height exposes a dark strip above the waveform.
        editor.RowDefinitions[2].Height = new GridLength(0);
        editor.RowDefinitions[0].Height = GridLength.Auto;
        var host = new Grid { ClipToBounds = true };
        host.SetResourceReference(BackgroundProperty, "WindowBackground");
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        waveformRow = new RowDefinition { Height = new GridLength(0) };
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        host.RowDefinitions.Add(waveformRow);
        // Lay out the decoration against the full center pane. Raising the wave
        // covers its lower part instead of shrinking its Viewbox first.
        host.SetResourceReference(BackgroundProperty, "WindowBackground");
        editor.Background = Brushes.Transparent;
        editor.Children.Remove(DecorationBgHost);
        Grid.SetRow(DecorationBgHost, 0); Grid.SetRowSpan(DecorationBgHost, 4);
        Grid.SetColumn(DecorationBgHost, 0); Grid.SetColumnSpan(DecorationBgHost, 1);
        host.Children.Add(DecorationBgHost);
        host.Children.Add(editor);
        Panel.SetZIndex(Cover, 10);
        Cover.Margin = new Thickness(0);
        Cover.StrokeThickness = 0;
        FumenContent.IsReadOnly = true;
        var header = CreateEditorHeader(editor);
        Grid.SetColumnSpan(header, 3); editor.Children.Add(header);

        waveformPane = new Grid { Visibility = Visibility.Collapsed };
        waveformPane.SetResourceReference(BackgroundProperty, "WindowBackground");
        var waveControls = editor.Children.OfType<FrameworkElement>().Where(child =>
            child == MusicWave || child == MusicWaveCursor).ToArray();
        foreach (var child in waveControls)
        {
            editor.Children.Remove(child);
            Grid.SetRow(child, 0); Grid.SetRowSpan(child, 1); Grid.SetColumnSpan(child, 1);
            Grid.SetColumn(child, 0);
            waveformPane.Children.Add(child);
        }
        MusicWave.Height = double.NaN;
        MusicWave.VerticalAlignment = VerticalAlignment.Stretch;
        MusicWave.SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(MusicWave, BitmapScalingMode.NearestNeighbor);
        MusicWaveCursor.Height = double.NaN;
        MusicWaveCursor.VerticalAlignment = VerticalAlignment.Stretch;
        foreach (var child in editor.Children.OfType<FrameworkElement>())
        {
            var margin = child.Margin;
            if (margin.Bottom >= 80)
                child.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom - 80);
        }
        var footer = CreateEditorFooter(editor);
        Grid.SetRow(footer, 1); host.Children.Add(footer);
        FumenContent.Margin = new Thickness(0);
        Grid.SetColumn(FumenContent, 0); Grid.SetColumnSpan(FumenContent, 3);
        codeOverview = new CodeOverview(FumenContent, () => SetCodeOverviewVisible(false)) { Visibility = Visibility.Collapsed };
        Grid.SetRow(codeOverview, 1); Grid.SetColumnSpan(codeOverview, 3);
        Panel.SetZIndex(codeOverview, 2); editor.Children.Add(codeOverview);
        MediaTimelinePanel.IsVisibleChanged += (_, _) => UpdateCodeOverview();
        Closed += (_, _) => codeOverview.Detach();
        var lanes = new Canvas { IsHitTestVisible = false, ClipToBounds = true,
            Visibility = Visibility.Collapsed, Opacity = 1 };
        var labels = Enumerable.Range(1, 8).Select(index => new TextBlock
        {
            Text = index.ToString(), TextAlignment = TextAlignment.Right, Width = 18
        }).ToArray();
        var laneLines = labels.Select(_ => new Line { Stroke = new SolidColorBrush(Color.FromRgb(145, 150, 158)),
            StrokeThickness = 1, Opacity = 0.3, IsHitTestVisible = false }).ToArray();
        foreach (var line in laneLines) lanes.Children.Add(line);
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            label.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(FumenContent.FontFamily)) { Source = FumenContent });
            label.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(FumenContent.FontSize)) { Source = FumenContent });
            label.SetResourceReference(TextBlock.ForegroundProperty, "WaveLabelForeground");
            label.Background = Brushes.Transparent;
            lanes.Children.Add(label);
            Canvas.SetLeft(label, 2);
            var position = index + 1;
            label.SizeChanged += (_, _) => Canvas.SetTop(label, WaveNoteY(position, (float)MusicWave.ActualHeight) - label.ActualHeight / 2);
        }
        Panel.SetZIndex(lanes, 1);
        waveformPane.Children.Add(lanes);
        void UpdateLanes()
        {
            var showLanes = WaveLanesVisible;
            lanes.Visibility = showLanes ? Visibility.Visible : Visibility.Collapsed;
            for (var index = 0; index < labels.Length; index++)
            {
                Canvas.SetTop(labels[index], WaveNoteY(index + 1, (float)MusicWave.ActualHeight) - labels[index].ActualHeight / 2);
                laneLines[index].X2 = MusicWave.ActualWidth;
                laneLines[index].Y1 = laneLines[index].Y2 = WaveNoteY(index + 1, (float)MusicWave.ActualHeight);
            }
        }
        MusicWave.SizeChanged += (_, _) => UpdateLanes();
        waveformPane.IsVisibleChanged += (_, _) => UpdateLanes();
        Grid.SetRow(waveformPane, 3); host.Children.Add(waveformPane);
        waveformSplitter = new GridSplitter { Height = 6,
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, Visibility = Visibility.Collapsed,
            FocusVisualStyle = null, Cursor = Cursors.SizeNS };
        // The footer is between the editor and waveform. Resize the waveform
        // explicitly instead of letting GridSplitter resize the footer's Auto row.
        double dragHeight = 0, dragY = 0;
        waveformSplitter.PreviewMouseLeftButtonDown += (_, e) =>
        {
            StopPaneSizeAnimation(waveformPane!, waveformRow, RowDefinition.HeightProperty);
            dragHeight = waveformRow.ActualHeight; dragY = e.GetPosition(host).Y;
            waveformSplitter.CaptureMouse(); e.Handled = true;
        };
        waveformSplitter.PreviewMouseMove += (_, e) =>
        {
            if (!waveformSplitter.IsMouseCaptured) return;
            waveformPaneHeight = Math.Clamp(dragHeight + dragY - e.GetPosition(host).Y, 64, waveformRow.MaxHeight);
            waveformRow.Height = new GridLength(waveformPaneHeight); e.Handled = true;
        };
        waveformSplitter.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!waveformSplitter.IsMouseCaptured) return;
            waveformSplitter.ReleaseMouseCapture(); e.Handled = true;
        };
        Grid.SetRow(waveformSplitter, 1); waveformSplitter.VerticalAlignment = VerticalAlignment.Top;
        waveformSplitter.Background = Brushes.Transparent;
        var waveDivider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        waveDivider.SetResourceReference(Border.BackgroundProperty, "MenuSeparator");
        Grid.SetRow(waveDivider, 1); host.Children.Add(waveDivider);
        Panel.SetZIndex(waveformSplitter, 5); host.Children.Add(waveformSplitter);
        // Fill the upper center pane; keep the waveform and its controls below.
        editor.Children.Remove(MediaTimelinePanel);
        Grid.SetRow(MediaTimelinePanel, 0); Grid.SetRowSpan(MediaTimelinePanel, 1);
        Grid.SetColumn(MediaTimelinePanel, 0); Grid.SetColumnSpan(MediaTimelinePanel, 1);
        MediaTimelinePanel.Margin = new Thickness(0, 60, 0, 0);
        MediaTimelinePanel.ClipToBounds = true;
        host.Children.Add(MediaTimelinePanel);
        void UpdateWaveformLimit() => waveformRow.MaxHeight = Math.Max(64, host.ActualHeight - 240 - footer.ActualHeight);
        host.SizeChanged += (_, _) => UpdateWaveformLimit();
        footer.SizeChanged += (_, _) => UpdateWaveformLimit();
        return host;
    }

    private FrameworkElement CreateEditorHeader(Grid editor)
    {
        var header = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        header.SetResourceReference(BackgroundProperty, "WindowBackground");
        editorPages = CreateSidebarTabs();
        editorPages.Margin = new Thickness(0); editorPages.Height = 30; editorPages.Visibility = Visibility.Visible;
        chartPage = CreateSidebarTab("", new Grid(), CloseChartPage, null);
        chartPageTitle = ((Panel)chartPage.Header).Children.OfType<TextBlock>().First();
        chartPage.Visibility = Visibility.Collapsed;
        editorPages.Items.Add(chartPage); chartPage.IsSelected = true;
        header.Children.Add(editorPages);
        var metadata = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true,
            Margin = new Thickness(10, 1, 10, 1), VerticalAlignment = VerticalAlignment.Center };
        TextOptions.SetTextFormattingMode(metadata, TextFormattingMode.Display);
        editorMetadata = metadata;
        void Move(FrameworkElement control, double width = double.NaN)
        {
            editor.Children.Remove(control); control.Margin = new Thickness(0, 0, 8, 0);
            control.Width = width; control.Height = 26; control.VerticalAlignment = VerticalAlignment.Center;
            if (control is Control field)
            {
                field.SetResourceReference(FontFamilyProperty, "UiFontFamily"); field.FontSize = 12;
                field.VerticalContentAlignment = VerticalAlignment.Center; field.FocusVisualStyle = null;
                if (field is Label) { field.Padding = new Thickness(0); field.SetResourceReference(ForegroundProperty, "MutedForeground"); }
                else
                {
                    field.Padding = new Thickness(8, 0, field is ComboBox ? 24 : 8, 0);
                    field.BorderThickness = new Thickness(0);
                    field.SetResourceReference(BorderBrushProperty, "MenuSeparator");
                    field.Background = Brushes.Transparent;
                }
                if (field is TextBox input)
                {
                    input.TextAlignment = TextAlignment.Left; input.TextWrapping = TextWrapping.NoWrap;
                    input.HorizontalContentAlignment = HorizontalAlignment.Stretch; input.Padding = new Thickness(2, 0, 2, 0);
                    var style = new Style(typeof(TextBox), (Style)FindResource(typeof(TextBox)));
                    var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
                    hover.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("HoverBackground")));
                    var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
                    focus.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ButtonsBackground")));
                    style.Triggers.Add(hover); style.Triggers.Add(focus);
                    // Local values take precedence over style triggers.
                    input.ClearValue(BackgroundProperty);
                    style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent)); input.Style = style;
                }
            }
            metadata.Children.Add(control);
        }
        Move(LevelSelector);
        metadata.Children.Remove(LevelSelector);
        editor.Children.Remove(UnsavedDifficultyFrame);
        var difficulty = new Grid { Height = 26 };
        LevelSelector.Margin = UnsavedDifficultyFrame.Margin = new Thickness(0);
        UnsavedDifficultyFrame.Height = 26;
        UnsavedDifficultyFrame.BorderThickness = new Thickness(0);
        UnsavedDifficultyFrame.Background = Brushes.Transparent;
        LevelSelector.VerticalContentAlignment = VerticalAlignment.Center;
        LevelSelector.Template = (ControlTemplate)FindResource("DifficultySelector");
        LevelSelector.SetResourceReference(FontFamilyProperty, "TitleFontFamily");
        LevelSelector.FontSize = 14;
        LevelSelector.FontWeight = FontWeights.Bold;
        LevelSelector.FontStyle = FontStyles.Normal;
        LevelSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
        var difficultyWidth = LevelSelector.Items.OfType<ComboBoxItem>().Max(item => new FormattedText(
            item.Content.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(LevelSelector.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            LevelSelector.FontSize, Brushes.White, VisualTreeHelper.GetDpi(LevelSelector).PixelsPerDip).Width) + 28;
        difficulty.Width = UnsavedDifficultyFrame.Width = Math.Ceiling(difficultyWidth);
        var difficultyColors = new[] { "#62AFE7", "#78B85E", "#D3A23E", "#DD6F77", "#B48CE2", "#C8A4D8", "#CD8BCB" };
        void UpdateDifficultyColor()
        {
            var index = LevelSelector.SelectedIndex;
            LevelSelector.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                difficultyColors[Math.Clamp(index, 0, difficultyColors.Length - 1)]));
            // NewRodin has a two-em line box; remove that extra leading in this compact row.
            LevelSelector.Tag = new TextBlock { Text = (LevelSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                FontFamily = LevelSelector.FontFamily, FontSize = LevelSelector.FontSize, FontWeight = FontWeights.Bold,
                LineHeight = LevelSelector.FontSize, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = LevelSelector.Foreground };
        }
        LevelSelector.SelectionChanged += (_, _) => UpdateDifficultyColor();
        UpdateDifficultyColor();
        difficulty.Children.Add(LevelSelector); difficulty.Children.Add(UnsavedDifficultyFrame); metadata.Children.Add(difficulty);
        void AddMetadataSeparator()
        {
            var line = new Border { Width = 1, Height = 14, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            line.SetResourceReference(Border.BackgroundProperty, "MenuSeparator"); metadata.Children.Add(line);
        }
        AddMetadataSeparator();
        double InputWidth(string sample) => Math.Ceiling(new FormattedText(sample, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(App.UiFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, Brushes.White,
            VisualTreeHelper.GetDpi(LevelSelector).PixelsPerDip).WidthIncludingTrailingWhitespace + 10);
        Move(DifficultyLabel); Move(LevelTextBox, InputWidth("14+-？"));
        AddMetadataSeparator(); Move(OffsetLabel); Move(OffsetTextBox, InputWidth("-0.0000"));
        DifficultyLabel.Margin = OffsetLabel.Margin = new Thickness(0, 0, 3, 0);
        LevelTextBox.Margin = OffsetTextBox.Margin = new Thickness(0);
        header.Children.Add(metadata);
        editorPages.SelectionChanged += (_, e) =>
        {
            if (updatingEditorPage || e.Source != editorPages) return;
            if (editorPages.SelectedItem is TabItem { Tag: ChartDocument document } && document != activeChartDocument)
            {
                InstallChartFromFile(document.Directory, null);
                if (activeChartDocument != document)
                {
                    updatingEditorPage = true; editorPages.SelectedItem = activeChartDocument?.Tab; updatingEditorPage = false;
                }
            }
            if (editorPages.SelectedItem != mediaPage && MediaTimelinePanel.Visibility == Visibility.Visible)
                MediaTimelinePanel_CloseRequested(MediaTimelinePanel, EventArgs.Empty);
        };
        MediaTimelinePanel.IsVisibleChanged += (_, _) =>
        {
            if (updatingEditorPage) return;
            updatingEditorPage = true;
            if (MediaTimelinePanel.Visibility == Visibility.Visible)
            {
                if (mediaPage == null) mediaPage = CreateSidebarTab("音视频剪辑", new Grid(),
                    () => MediaTimelinePanel_CloseRequested(MediaTimelinePanel, EventArgs.Empty), null);
                if (!editorPages.Items.Contains(mediaPage)) editorPages.Items.Add(mediaPage);
                editorPages.SelectedItem = mediaPage;
            }
            else
            {
                if (mediaPage != null) editorPages.Items.Remove(mediaPage);
                editorPages.SelectedItem = activeChartDocument?.Tab ?? chartPage;
            }
            updatingEditorPage = false;
        };
        return header;
    }

    private async void CloseChartPage()
    {
        if (activeChartDocument != null) { CloseChartDocument(activeChartDocument); return; }
        if (!HasLoadedChartDocument() || !isSaved && !AskSave()) return;
        var closingDirectory = maidataDir;
        try { await StopChartListAsync(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "关闭谱面", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (activeChartDocument != null || maidataDir != closingDirectory) return;
        ToggleStop();
        if (toolTabs.TryGetValue(typeof(Infomation), out var information)) information.Window.Close();
        recordVideoWindow?.Close();
        gifExportWindow?.Close();
        soundSetting?.Close();
        if (MediaTimelinePanel.Visibility == Visibility.Visible)
            MediaTimelinePanel_CloseRequested(MediaTimelinePanel, EventArgs.Empty);
        chartRoom?.Close();
        AutoSaveManager.Of().SetAutoSaveEnable(false);
        Interlocked.Increment(ref timelineAudioBuildGeneration);
        Interlocked.Increment(ref timelineWaveBuildGeneration);
        isLoading = true;
        try
        {
            selectedDifficulty = -1; maidataDir = "";
            guestChartPendingSave = false; guestSavePath = null;
            SetRawFumenText(""); SimaiProcess.ClearData();
            LevelSelector.SelectedIndex = -1; LevelTextBox.Text = OffsetTextBox.Text = "";
            if (bgmStream != -1024) { Bass.BASS_StreamFree(bgmStream); bgmStream = -1024; }
            songLength = 0; loadedTrackPath = timelineAudioSourcePath = null;
            timelineAudioBuildTask = null;
            lock (timelineWaveCacheLock) timelineWaveCache.Clear();
            Array.Clear(waveRaws); waveformBars = Array.Empty<short>(); waveformBarStep = waveformReferenceWidth = 0;
            WaveBitmap = null; MusicWave.Source = null;
            Cover.Visibility = Visibility.Visible;
            MenuEdit.IsEnabled = Menu_ExportNoAlpha.IsEnabled = VolumnSetting.IsEnabled = false;
            MenuMuriCheck.IsEnabled = Menu_ExportRender.IsEnabled = SyntaxCheckButton.IsEnabled = false;
            SetSavedState(true);
            if (editorSetting != null) { editorSetting.LastChartPath = ""; SaveEditorSetting(); }
        }
        finally { isLoading = false; }
        UpdateWaveformPane(false); RefreshChartFiles();
    }

    private FrameworkElement CreateEditorFooter(Grid editor)
    {
        var footer = new StackPanel { Name = "EditorFooter", ClipToBounds = true };
        footer.SetResourceReference(BackgroundProperty, "WindowBackground");
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 2, 10, 0), RenderTransform = new TranslateTransform(0, 4) };
        foreach (var box in new[] { VisualChartEditorCheck, DragPreviewCheck, FollowPlayCheck, WordWrapCheck, PausedJudgeAreasCheck })
        {
            editor.Children.Remove(box); box.Width = double.NaN; box.Height = 20;
            box.FontSize = 12; box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(0, 0, 16, 0); box.VerticalAlignment = VerticalAlignment.Center;
            options.Children.Add(box);
        }
        footer.Children.Add(options);
        const double transportHeight = 38;
        const double transportButtonSize = 34;
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Height = transportHeight, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { StopButton, PlayAndPauseButton, TimelineStopButton, TimelinePlayAndPauseButton,
            WaveViewZoomOut, WaveViewZoomIn, MediaTimelineButton })
        {
            editor.Children.Remove(button); button.Width = button.Height = transportButtonSize; button.Margin = new Thickness(0);
            button.Padding = new Thickness(0); button.VerticalAlignment = VerticalAlignment.Center; button.FocusVisualStyle = null;
        }
        void AddPlaybackPair(Button main, Button timeline)
        {
            main.Template = timeline.Template = (ControlTemplate)FindResource("EditorTransportButton");
            var style = new Style(typeof(Button));
            var editing = new DataTrigger { Binding = new Binding(nameof(Visibility)) { Source = timeline }, Value = Visibility.Visible };
            editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); style.Triggers.Add(editing); main.Style = style;
            var pair = new Grid { Margin = new Thickness(0, 0, 2, 0) };
            pair.Children.Add(main); pair.Children.Add(timeline); transport.Children.Add(pair);
        }
        AddPlaybackPair(StopButton, TimelineStopButton); AddPlaybackPair(PlayAndPauseButton, TimelinePlayAndPauseButton);
        var divider = new Border { Width = 1, Height = 14, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        divider.SetResourceReference(Border.BackgroundProperty, "MenuSeparator"); transport.Children.Add(divider);
        void AddIcon(Button button, string geometry, string tooltip)
        {
            button.Template = SidebarToggleButton.Template;
            button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
            var icon = new Path { Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round };
            icon.SetResourceReference(Shape.StrokeProperty, "ButtonForeground");
            button.Content = icon; button.ToolTip = tooltip;
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip); transport.Children.Add(button);
        }
        AddIcon(WaveViewZoomOut, "M5,12 L19,12", "缩小波形图");
        AddIcon(WaveViewZoomIn, "M5,12 L19,12 M12,5 L12,19", "放大波形图");
        AddIcon(MediaTimelineButton, "M7,6 A2,2 0 1 1 3,6 A2,2 0 1 1 7,6 M7,18 A2,2 0 1 1 3,18 A2,2 0 1 1 7,18 M6.5,7.5 L20,20 M6.5,16.5 L20,4", "剪辑");
        var diagnosticsDivider = new Border { Width = 1, Height = 14, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        diagnosticsDivider.SetResourceReference(Border.BackgroundProperty, "MenuSeparator"); transport.Children.Add(diagnosticsDivider);
        editor.Children.Remove(DiagnosticsSummary);
        DiagnosticsSummary.Margin = new Thickness(0); DiagnosticsSummary.Height = transportHeight;
        DiagnosticsSummary.Orientation = Orientation.Horizontal; DiagnosticsSummary.VerticalAlignment = VerticalAlignment.Center;
        var diagnosticIcons = new[] { "M8,1 L15,14 L1,14 Z M8,5 L8,9 M8,11 L8,12", "M2,3 L14,3 L14,13 L2,13 Z M6,3 L6,13 M10,3 L10,13", "M1,8 L4,8 L6,3 L10,13 L12,8 L15,8" };
        var diagnosticNames = new[] { "语法错误", "静态无理", "动态无理" };
        var diagnosticIndex = 0;
        foreach (var row in DiagnosticsSummary.Children.OfType<StackPanel>())
        {
            row.Height = transportHeight; row.Margin = new Thickness(0, 0, diagnosticIndex == 2 ? 0 : 12, 0); row.VerticalAlignment = VerticalAlignment.Center;
            var title = row.Children.OfType<Label>().First();
            var count = row.Children.OfType<Label>().Last();
            row.Children.Remove(count);
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Height = 18,
                VerticalAlignment = VerticalAlignment.Center };
            var icon = new Path { Data = Geometry.Parse(diagnosticIcons[diagnosticIndex]),
                Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 1.3,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            icon.SetResourceReference(Shape.StrokeProperty, "ButtonForeground");
            caption.Children.Add(icon);
            caption.Children.Add(new TextBlock { Text = diagnosticNames[diagnosticIndex++],
                VerticalAlignment = VerticalAlignment.Center });
            var number = new TextBlock { VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0), RenderTransform = new TranslateTransform(0, 1) };
            number.SetBinding(TextBlock.TextProperty, new Binding(nameof(ContentControl.Content)) { Source = count });
            caption.Children.Add(number);
            title.Content = caption;
            foreach (var label in row.Children.OfType<Label>())
            {
                label.Width = double.NaN; label.MinWidth = 0; label.Height = 18;
                label.SetResourceReference(FontFamilyProperty, "UiFontFamily"); label.FontSize = FollowPlayCheck.FontSize;
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Padding = new Thickness(0);
                label.VerticalContentAlignment = VerticalAlignment.Center;
                label.SetResourceReference(ForegroundProperty, "MutedForeground");
            }
        }
        transport.Children.Add(DiagnosticsSummary);
        const double timeFontSize = 16;
        var time = new StackPanel { Orientation = Orientation.Horizontal, Height = transportHeight, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        foreach (var label in new[] { TimeLabel, NoteNowText })
        {
            editor.Children.Remove(label); label.SetResourceReference(FontFamilyProperty, "UiFontFamily"); label.FontSize = timeFontSize;
            label.Width = double.NaN; label.Height = transportButtonSize; label.Margin = label.Padding = new Thickness(0);
            label.VerticalAlignment = VerticalAlignment.Center; label.VerticalContentAlignment = VerticalAlignment.Center;
            time.Children.Add(label);
        }
        totalDurationText = new TextBlock { Text = "/ 0:00.0000", FontFamily = TimeLabel.FontFamily, FontSize = timeFontSize, Margin = new Thickness(5, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        playbackRateText = new TextBlock { Text = "1.00×", FontFamily = TimeLabel.FontFamily, FontSize = timeFontSize, VerticalAlignment = VerticalAlignment.Center };
        totalDurationText.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeground");
        playbackRateText.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
        playbackRateText.FontWeight = FontWeights.SemiBold;
        time.Children.Add(totalDurationText); time.Children.Add(playbackRateText);
        var clockDivider = new Border { Width = 1, Height = 14, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        clockDivider.SetResourceReference(Border.BackgroundProperty, "MenuSeparator");
        transport.Children.Add(clockDivider); transport.Children.Add(time);
        transport.MinWidth = 660;
        footer.Children.Add(transport);
        return footer;
    }

    private void AddPaneButtons(Grid layout)
    {
        chartFilesButton = CreatePaneButton("显示／隐藏谱面文件", "M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M9,3 L9,21");
        bottomPaneButton = CreatePaneButton("显示／隐藏波形图", "M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M3,15 L21,15");
        codeOverviewButton = CreatePaneButton("显示／隐藏代码概览", "M4,4 L11,4 M4,8 L15,8 M4,12 L10,12 M4,16 L14,16 M19,3 L22,3 L22,21 L19,21 Z");
        codeOverviewButton.Click += (_, _) => SetCodeOverviewVisible(!codeOverviewVisible);
        chartFilesButton.Click += (_, _) =>
        {
            if (detachedChartFiles != null) { detachedChartFiles.Activate(); return; }
            var visible = !chartFilesVisible || sidebarExpanded;
            if (sidebarExpanded) { sidebarExpanded = false; SetSidebarVisible(true); }
            if (!chartFilesPane!.Items.Contains(chartFilesTab)) chartFilesPane.Items.Add(chartFilesTab);
            chartFilesPane.SelectedItem = chartFilesTab;
            SetChartFilesVisible(visible);
        };
        bottomPaneButton.Click += (_, _) =>
        {
            bottomPaneVisible = !bottomPaneVisible;
            UpdateWaveformPane();
        };
        foreach (var button in new[] { chartFilesButton, bottomPaneButton, codeOverviewButton })
        {
            Grid.SetColumnSpan(button, 4); Panel.SetZIndex(button, 52); layout.Children.Add(button);
        }
        UpdateWaveformPane(animate: false);
        UpdatePaneToolbar();
    }

    private void SetChartFilesVisible(bool visible, bool animate = true)
    {
        chartFilesVisible = visible;
        chartFilesButton!.Tag = visible;
        var currentWidth = chartFilesColumn!.ActualWidth;
        StopPaneSizeAnimation(chartFilesPane!, chartFilesColumn, ColumnDefinition.WidthProperty);
        chartFilesColumn.MinWidth = 0;
        chartFilesColumn.Width = new GridLength(visible ? chartFilesWidth : 0);
        chartFilesSplitter!.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            chartFilesPane!.Visibility = Visibility.Visible;
            RefreshChartFiles();
        }
        if (animate)
            AnimatePaneSize(chartFilesPane!, chartFilesColumn, ColumnDefinition.WidthProperty,
                currentWidth, visible ? chartFilesWidth : 0,
                () => { chartFilesPane!.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    chartFilesColumn.MinWidth = visible ? 140 : 0; });
        else chartFilesPane!.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWaveformPane(bool animate = true)
    {
        var loaded = HasLoadedChartDocument();
        UpdateCodeOverview();
        if (editorMetadata != null) editorMetadata.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        var visible = loaded && bottomPaneVisible;
        FumenContent.IsReadOnly = !loaded;
        DecorationBgHost.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        if (chartPage != null)
        {
            chartPage.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            if (loaded && activeChartDocument != null) UpdateChartDocumentCaption(activeChartDocument);
            else chartPageTitle!.Text = loaded ? SimaiProcess.title : "";
        }
        bottomPaneButton!.IsEnabled = loaded;
        bottomPaneButton.Tag = visible;
        var currentHeight = waveformRow!.ActualHeight;
        StopPaneSizeAnimation(waveformPane!, waveformRow, RowDefinition.HeightProperty);
        waveformRow.MinHeight = 0;
        waveformRow.Height = new GridLength(visible ? waveformPaneHeight : loaded ? 0 : 80);
        if (visible) waveformPane!.Visibility = waveformSplitter!.Visibility = Visibility.Visible;
        void Complete()
        {
            waveformRow.MinHeight = visible ? 64 : 0;
            waveformPane!.Visibility = waveformSplitter!.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (animate)
            AnimatePaneSize(waveformPane!, waveformRow, RowDefinition.HeightProperty,
                currentHeight, visible ? Math.Min(waveformPaneHeight, waveformRow.MaxHeight) : loaded ? 0 : 80, Complete);
        else Complete();
    }

    private Button CreatePaneButton(string tooltip, string geometry)
    {
        var button = new Button { Width = 30, Height = 24, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(6, 4, 6, 4), FocusVisualStyle = null,
            Template = SidebarToggleButton.Template, ToolTip = tooltip };
        button.SetResourceReference(ForegroundProperty, "ButtonForeground");
        var icon = new Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        icon.SetResourceReference(Shape.StrokeProperty, "ButtonForeground");
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(icon);
        button.Content = new Viewbox { Width = 16, Height = 16, Child = canvas };
        return button;
    }

    private void UpdatePaneToolbar()
    {
        var rightVisible = toolSidebar?.Visibility == Visibility.Visible;
        SidebarExpandButton.Visibility = rightVisible ? Visibility.Visible : Visibility.Collapsed;
        var offset = rightVisible ? 60 : 30;
        if (bottomPaneButton != null) bottomPaneButton.Margin = new Thickness(0, 0, offset, 0);
        offset += 30;
        if (chartFilesButton != null) chartFilesButton.Margin = new Thickness(0, 0, offset, 0);
        offset += 30;
        if (codeOverviewButton != null) codeOverviewButton.Margin = new Thickness(0, 0, offset, 0);
        offset += 30;
        if (dockedPetButton != null) dockedPetButton.Margin = new Thickness(0, 0, offset, 0);
        MainMenu.Padding = new Thickness(0, 0, offset + 30, 0);
    }

    private void SetCodeOverviewVisible(bool visible)
    {
        codeOverviewVisible = visible;
        UpdateCodeOverview();
    }

    private void UpdateCodeOverview()
    {
        var loaded = HasLoadedChartDocument();
        var visible = loaded && codeOverviewVisible && MediaTimelinePanel.Visibility != Visibility.Visible;
        if (codeOverview != null) codeOverview.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FumenContent.Margin = new Thickness(0);
        if (codeOverviewButton != null) { codeOverviewButton.Tag = visible; codeOverviewButton.IsEnabled = loaded; }
    }

    private TabControl CreateChartFilesPane()
    {
        var dock = new DockPanel();
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        chartFilePreview = new Image { MaxHeight = 130, Stretch = Stretch.Uniform, Margin = new Thickness(10, 8, 10, 4) };
        chartFilePreviewName = SessionTheme.Text("", size: 12);
        chartFilePreviewName.TextTrimming = TextTrimming.CharacterEllipsis;
        chartFilePreviewName.Margin = new Thickness(10, 0, 10, 4);
        chartFilePreviewPane = new StackPanel { Visibility = Visibility.Collapsed };
        chartFilePreviewPane.Children.Add(chartFilePreview);
        chartFilePreviewPane.Children.Add(chartFilePreviewName);
        footer.Children.Add(chartFilePreviewPane);
        var refresh = SessionTheme.Button("刷新素材");
        refresh.Margin = new Thickness(10, 8, 10, 10);
        refresh.Click += async (_, _) => await RefreshChartAssets();
        footer.Children.Add(refresh);
        chartFilesTree = new TreeView { BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(4, 0, 4, 4), FocusVisualStyle = null };
        chartFilesTree.SetResourceReference(ForegroundProperty, "ButtonForeground");
        chartFilesTree.Resources[SystemColors.ControlBrushKey] = FindResource("WindowBackground");
        chartFilesTree.ItemContainerStyle = CreateChartTreeStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(chartFilesTree, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(chartFilesTree, true);
        chartFilesTree.SelectedItemChanged += (_, _) => PreviewChartFile();
        chartFilesTree.MouseDoubleClick += (_, e) =>
        {
            if (chartFilesTree.SelectedItem is not TreeViewItem { Tag: string path } || !File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            e.Handled = true;
        };
        dock.Children.Add(chartFilesTree);
        chartFilesPane = CreateSidebarTabs();
        chartFilesTab = CreateSidebarTab("谱面文件", dock, () =>
        {
            chartFilesPane.Items.Remove(chartFilesTab);
            if (chartFilesPane.Items.Count == 0) SetChartFilesVisible(false);
        }, DetachChartFiles);
        return chartFilesPane;
    }

    private void DetachChartFiles(TabItem tab, Point screenPoint)
    {
        var content = (UIElement)tab.Content;
        tab.Content = null;
        chartFilesPane!.Items.Remove(tab);
        detachedChartFiles = CreateDetachedSidebarWindow("谱面文件", content, 320, 560, 240);
        PositionDetachedSidebarWindow(detachedChartFiles, screenPoint);
        detachedChartFiles.Closed += (_, _) =>
        {
            detachedChartFiles!.Content = null;
            detachedChartFiles = null;
            tab.Content = content;
        };
        if (chartFilesPane.Items.Count == 0) SetChartFilesVisible(false, animate: false);
        ShowDetachedSidebarWindow(detachedChartFiles);
    }

    private static Style CreateChartTreeStyle()
    {
        var style = new Style(typeof(TreeViewItem));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TreeViewItem'>
 <StackPanel>
  <Border Name='row' Background='Transparent' CornerRadius='3'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <ToggleButton Name='expander' IsChecked='{Binding IsExpanded,RelativeSource={RelativeSource TemplatedParent}}' Focusable='False' Width='18' Height='28'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'><Path Name='arrow' Data='M0,0 L4,4 L0,8' Stroke='{DynamicResource ButtonForeground}' StrokeThickness='1.2' HorizontalAlignment='Center' VerticalAlignment='Center' RenderTransformOrigin='.5,.5'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='arrow' Property='RenderTransform'><Setter.Value><RotateTransform Angle='90'/></Setter.Value></Setter></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center' Margin='0,0,6,0'/>
   </Grid>
  </Border>
  <ItemsPresenter Name='ItemsHost' Margin='16,0,0,0'/>
 </StackPanel>
 <ControlTemplate.Triggers>
  <Trigger Property='IsExpanded' Value='False'><Setter TargetName='ItemsHost' Property='Visibility' Value='Collapsed'/></Trigger>
  <Trigger Property='HasItems' Value='False'><Setter TargetName='expander' Property='Visibility' Value='Hidden'/></Trigger>
  <Trigger SourceName='row' Property='IsMouseOver' Value='True'><Setter TargetName='row' Property='Background' Value='{DynamicResource MenuSeparator}'/></Trigger>
  <Trigger Property='IsSelected' Value='True'><Setter TargetName='row' Property='Background' Value='{DynamicResource EditorBackground}'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>")));
        return style;
    }

    private void PreviewChartFile()
    {
        chartFilePreviewPane!.Visibility = Visibility.Collapsed;
        chartFilePreview!.Source = null;
        if (chartFilesTree!.SelectedItem is not TreeViewItem { Tag: string path }) return;
        chartFilePreviewName!.Text = System.IO.Path.GetFileName(path);
        chartFilePreviewName.ToolTip = path;
        chartFilePreviewPane.Visibility = Visibility.Visible;
        if (!IsChartImage(path)) return;
        try
        {
            chartFilePreview.Source = ReadChartThumbnail(path, 360);
            chartFilePreviewName!.Text = System.IO.Path.GetFileName(path);
            chartFilePreviewPane.Visibility = Visibility.Visible;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { chartFilePreviewName!.Text = error.Message; chartFilePreviewPane.Visibility = Visibility.Visible; }
    }

    private static bool IsChartImage(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".apng" or ".gif" or ".jpg" or ".jpeg" or ".bmp";

    internal static BitmapImage ReadChartThumbnail(string path, int width)
    {
        using Stream stream = System.IO.Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            ? new MemoryStream(MediaTools.ReadVideoPoster(path)) : File.OpenRead(path);
        var image = new BitmapImage(); image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }

    private void RefreshChartFiles()
    {
        if (chartFilesTree == null) return;
        chartFilesTree.Items.Clear();
        chartFilePreviewPane!.Visibility = Visibility.Collapsed;
        chartFilePreview!.Source = null;
        if (!chartFilesVisible && detachedChartFiles == null) return;
        if (string.IsNullOrEmpty(maidataDir) || !Directory.Exists(maidataDir)) return;
        try
        {
            var root = CreateChartFileItem(maidataDir, true);
            chartFilesTree.Items.Add(root); root.IsExpanded = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, error.Message, "谱面文件", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private TreeViewItem CreateChartFileItem(string path, bool directory)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Height = 28 };
        var icon = new Path { Width = 15, Height = 15, Stretch = Stretch.Uniform, StrokeThickness = 1.15,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
            Data = Geometry.Parse(directory ? "M1,3 L6,3 L8,5 L15,5 L15,13 L1,13 Z" :
                IsChartImage(path) ? "M1,1 L15,1 L15,15 L1,15 Z M2,12 L6,7 L10,11 L13,8 L15,10 M10,4 L11,4" :
                System.IO.Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".ogg" or ".wav" or ".mp4"
                    ? "M3,1 L12,1 L15,4 L15,15 L3,15 Z M7,5 L12,8 L7,11 Z" :
                    "M3,1 L11,1 L15,5 L15,15 L3,15 Z M11,1 L11,5 L15,5 M6,8 L12,8 M6,11 L12,11") };
        icon.Stroke = directory ? new SolidColorBrush(Color.FromRgb(217, 177, 100)) : SessionTheme.Brush("ButtonForeground", "#D4D4D4");
        if (!directory) icon.SetResourceReference(Shape.StrokeProperty, "ButtonForeground");
        header.Children.Add(icon);
        var name = SessionTheme.Text(System.IO.Path.GetFileName(path));
        name.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(name);
        var item = new TreeViewItem { Header = header, Tag = path, ToolTip = path,
            Style = chartFilesTree!.ItemContainerStyle };
        if (!directory)
        {
            var menu = new ContextMenu();
            menu.SetResourceReference(StyleProperty, "AlphaContextMenu");
            void AddAssetAction(string label, ChartAssetRole role)
            {
                var action = new MenuItem { Header = label };
                action.Click += async (_, _) => await AssignChartAssetAsync(path, role);
                menu.Items.Add(action);
            }
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".apng" or ".gif" or ".jpg" or ".jpeg" or ".bmp")
            { AddAssetAction("设为封面", ChartAssetRole.Cover); if (extension is not (".gif" or ".bmp")) AddAssetAction("设为背景", ChartAssetRole.Background); }
            if (extension == ".mp4") { AddAssetAction("设为封面", ChartAssetRole.Cover); AddAssetAction("设为背景", ChartAssetRole.Background); }
            if (extension is ".mp3" or ".ogg") AddAssetAction("设为音频", ChartAssetRole.Audio);
            if (menu.Items.Count > 0) item.ContextMenu = menu;
            item.PreviewMouseRightButtonDown += (_, e) => { item.IsSelected = true; e.Handled = true; };
            return item;
        }
        item.Items.Add(new TreeViewItem());
        item.Expanded += (_, e) =>
        {
            if (e.OriginalSource != item || item.Items.Count != 1 || item.Items[0] is not TreeViewItem { Tag: null }) return;
            item.Items.Clear();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path).OrderBy(entry => !Directory.Exists(entry)).ThenBy(System.IO.Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
                    item.Items.Add(CreateChartFileItem(entry, Directory.Exists(entry)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { item.Items.Add(new TreeViewItem { Header = error.Message, IsEnabled = false }); }
        };
        return item;
    }

    private async Task RefreshChartAssets()
    {
        RefreshChartFiles();
        if (!HasLoadedChartDocument()) return;
        var position = GetTimelinePosition();
        CancelNotePreview();
        StopPlaybackForScrub();
        var generation = viewControlGeneration;
        if (pendingScrubStop != null && !await pendingScrubStop) return;
        if (generation != viewControlGeneration) return;
        var request = new EditRequestjson { control = EditorControlMethod.RefreshAssets };
        var response = await Task.Run(() => WebControl.RequestPOST(MajdataCore.AlphaSession.ViewEndpoint,
            Newtonsoft.Json.JsonConvert.SerializeObject(request)));
        if (response == "ERROR") { ReportViewUnreachable(); return; }
        if (generation != viewControlGeneration) return;
        SetTimelinePosition(position);
        InvalidateSongDetailCache();
        QueueNotePreview(chartChanged: true);
    }

    private static float WaveNoteY(float position, float height) => 8f + position * (height - 25f) / 8f;
    private bool WaveLanesVisible => bottomPaneVisible && waveformPane?.Visibility == Visibility.Visible && MusicWave.ActualHeight >= 145;

    private bool CanEditWaveNotes => WaveLanesVisible && editorSetting?.EnableVisualChartEditor == true &&
        !isLoading && lastEditorState == EditorControlMethod.Stop && HasLoadedChartDocument();

    private bool TryInsertWaveNote(Point point)
    {
        if (!CanEditWaveNotes) return false;
        var source = GetRawFumenText();
        if (chartParsePending) SimaiProcess.Serialize(source, GetRawFumenPosition());
        var viewport = CreateWaveViewport(GetTimelinePosition(), (int)Math.Round(MusicWave.ActualWidth));
        var at = viewport.StartTime + point.X / MusicWave.ActualWidth * viewport.Duration;
        var slotEnd = GetTimingSlotBounds(source, (int)GetRawFumenPosition()).End;
        var stream = SimaiProcess.waveTimingList.FirstOrDefault(item => item.sourcePosition == slotEnd)?.streamIndex ?? 0;
        var timing = SimaiProcess.waveTimingList.Where(item => viewport.Contains(item.time))
            .OrderBy(item => Math.Abs(item.time - at))
            .ThenBy(item => item.streamIndex == stream ? 0 : 1).FirstOrDefault();
        if (timing == null) return false;
        var position = Math.Clamp((int)Math.Round((point.Y - 8d) * 8d / (MusicWave.ActualHeight - 25d)), 1, 8);
        followingTimelineCaret = true;
        try { fumenEditor.Select(timing.sourcePosition, 0); }
        finally { followingTimelineCaret = false; }
        InsertVisualNote(new VisualEditMessage { note = position.ToString(), action = "note" }, atExistingSlot: true);
        return true;
    }
}
