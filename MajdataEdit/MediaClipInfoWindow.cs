using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed class MediaClipInfoWindow : Window
{
    private readonly MediaTimelineEditor editor;
    private readonly TextBlock name = SessionTheme.Text("请选择时间线片段", size: 16);
    private readonly TextBlock track = SessionTheme.Text("");
    private readonly TextBlock start = SessionTheme.Text("");
    private readonly TextBlock end = SessionTheme.Text("");
    private readonly TextBlock sourceLength = SessionTheme.Text("");
    private readonly ComboBox volume = new() { Width = 105, IsEditable = true,
        ItemsSource = new[] { "0%", "25%", "50%", "75%", "100%", "150%", "200%", "300%", "400%" } };
    private readonly TextBox fadeIn = new();
    private readonly TextBox fadeOut = new();
    private readonly TextBox brightness = new();
    private readonly TextBox opacity = new();
    private readonly StackPanel audioFields = new();
    private readonly StackPanel videoFields = new();
    private readonly DispatcherTimer applyDelay = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private bool refreshing;
    private bool applying;

    internal MediaClipInfoWindow(MediaTimelineEditor editor)
    {
        this.editor = editor;
        Title = "音视频信息";
        SessionTheme.Apply(this, 330, 480);
        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        name.TextWrapping = TextWrapping.Wrap;
        name.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(name);
        name.Margin = new Thickness(0, 0, 0, 14);
        AddInfo(panel, "轨道", track);
        AddInfo(panel, "时间线起点", start);
        AddInfo(panel, "时间线终点", end);
        AddInfo(panel, "源文件时长", sourceLength);
        SessionTheme.Input(volume);
        AddField(audioFields, "音量", volume);
        AddField(audioFields, "渐入", PrepareInput(fadeIn, 58), "秒");
        AddField(audioFields, "渐出", PrepareInput(fadeOut, 58), "秒");
        AddField(videoFields, "亮度", PrepareInput(brightness, 82), "%");
        AddField(videoFields, "透明度", PrepareInput(opacity, 82), "%");
        panel.Children.Add(audioFields);
        panel.Children.Add(videoFields);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        volume.SelectionChanged += (_, _) => { if (!refreshing) { applyDelay.Stop(); applyDelay.Start(); } };
        volume.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) =>
            { if (!refreshing) { applyDelay.Stop(); applyDelay.Start(); } }));
        volume.LostKeyboardFocus += (_, _) => Apply();
        foreach (var field in new[] { fadeIn, fadeOut, brightness, opacity })
        {
            field.TextChanged += (_, _) => { if (!refreshing) { applyDelay.Stop(); applyDelay.Start(); } };
            field.LostKeyboardFocus += (_, _) => Apply();
        }
        applyDelay.Tick += (_, _) => { applyDelay.Stop(); Apply(); };
        editor.SelectedClipChanged += OnSelectionChanged;
        Closed += (_, _) => { applyDelay.Stop(); editor.SelectedClipChanged -= OnSelectionChanged; };
        Refresh();
    }

    private static TextBox PrepareInput(TextBox box, double width)
    {
        SessionTheme.Input(box);
        box.Width = width;
        box.HorizontalContentAlignment = HorizontalAlignment.Left;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        return box;
    }
    private static void AddInfo(Panel parent, string label, TextBlock value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(SessionTheme.Text(label));
        Grid.SetColumn(value, 1); row.Children.Add(value); parent.Children.Add(row);
    }
    private static void AddField(Panel parent, string label, UIElement control, string? unit = null)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(SessionTheme.Text(label));
        var values = new StackPanel { Orientation = Orientation.Horizontal };
        values.Children.Add(control);
        if (unit != null) { var unitText = SessionTheme.Text(unit); unitText.Margin = new Thickness(5, 5, 0, 0); values.Children.Add(unitText); }
        Grid.SetColumn(values, 1); row.Children.Add(values); parent.Children.Add(row);
    }
    private void OnSelectionChanged(object? sender, EventArgs args) { if (!applying) Refresh(); }
    private void Refresh()
    {
        applyDelay.Stop();
        refreshing = true;
        try
        {
            var clip = editor.SelectedClip;
            if (clip == null)
            {
                name.Text = "请选择时间线片段";
                track.Text = start.Text = end.Text = sourceLength.Text = "";
                audioFields.Visibility = videoFields.Visibility = Visibility.Collapsed;
                return;
            }
            name.Text = clip.Name;
            name.ToolTip = clip.SourcePath;
            track.Text = (clip.Track == MediaTrackKind.Audio ? "音频轨" : "视频轨") + (clip.TrackIndex + 1);
            start.Text = Format(clip.TimelineStart) + " 秒";
            end.Text = Format(clip.TimelineEnd) + " 秒";
            sourceLength.Text = Format(clip.SourceDuration) + " 秒";
            volume.Text = Format(clip.Volume * 100d) + "%";
            fadeIn.Text = Format(clip.FadeIn);
            fadeOut.Text = Format(clip.FadeOut);
            brightness.Text = Format(clip.Brightness * 100d);
            opacity.Text = Format(clip.Opacity * 100d);
            audioFields.Visibility = clip.Track == MediaTrackKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            videoFields.Visibility = clip.Track == MediaTrackKind.Video ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { refreshing = false; }
    }
    private void Apply()
    {
        applyDelay.Stop();
        if (editor.SelectedClip is not { } clip) return;
        if (!TryRead(volume.Text.Trim().TrimEnd('%'), out var gain) || gain is < 0d or > 400d ||
            !TryRead(fadeIn, out var fadeStart) || fadeStart < 0d ||
            !TryRead(fadeOut, out var fadeEnd) || fadeEnd < 0d ||
            !TryRead(brightness, out var light) || light is < -100d or > 100d ||
            !TryRead(opacity, out var alpha) || alpha is < 0d or > 100d)
            return; // Partially typed values are applied when valid, without interrupting editing.
        if (Math.Abs(clip.Volume * 100d - gain) < .0001d && Math.Abs(clip.FadeIn - fadeStart) < .0001d &&
            Math.Abs(clip.FadeOut - fadeEnd) < .0001d && Math.Abs(clip.Brightness * 100d - light) < .0001d &&
            Math.Abs(clip.Opacity * 100d - alpha) < .0001d) return;
        applying = true;
        try { editor.UpdateSelectedClip(clip.Duration, gain / 100d, fadeStart, fadeEnd, light / 100d, alpha / 100d); }
        finally { applying = false; }
    }
    private static bool TryRead(TextBox box, out double value) =>
        TryRead(box.Text, out value);
    private static bool TryRead(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value);
    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}
