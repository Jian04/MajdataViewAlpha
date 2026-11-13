using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MajdataEdit;

internal sealed class ExportRangeSelector : StackPanel
{
    private readonly CheckBox enabled = new() { Content = "选择导出区间" };
    private readonly CheckBox snap = new() { Content = "吸附小节", IsChecked = true };
    private readonly TextBox start = new() { Width = 76 };
    private readonly TextBox end = new() { Width = 76 };
    private readonly RangeTimeline timeline;
    private bool updating;
    internal MediaRange? Selection => enabled.IsChecked == true ? new(timeline.Start, timeline.End) : null;
    internal event Action<double>? PreviewRequested;
    internal event Action? SelectionChanged;

    internal ExportRangeSelector()
    {
        Margin = new Thickness(0, 12, 0, 8);
        var heading = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        enabled.Margin = new Thickness(0, 0, 20, 0);
        heading.Children.Add(enabled); heading.Children.Add(snap); Children.Add(heading);
        timeline = new RangeTimeline();
        Children.Add(timeline);
        var fields = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, box) in new[] { ("起点", start), ("终点", end) })
        {
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
            fields.Children.Add(caption);
            MajdataSessionUi.SessionTheme.Input(box);
            box.Padding = new Thickness(4, 0, 4, 0);
            box.FontFamily = new FontFamily("Cascadia Mono");
            box.Margin = new Thickness(0, 0, box == end ? 0 : 8, 0);
            fields.Children.Add(box);
            box.LostKeyboardFocus += (_, _) => CommitFields();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitFields(); e.Handled = true; } };
        }
        Children.Add(fields);
        var hint = new TextBlock { Text = "拖动两端选择小节，点击时间轴在 View 中预览。单位：秒。", FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
        hint.Opacity = .7;
        Children.Add(hint);
        enabled.Checked += (_, _) => Changed(); enabled.Unchecked += (_, _) => Changed();
        timeline.Snap = () => snap.IsChecked == true;
        timeline.Changed = () => { enabled.IsChecked = true; UpdateFields(); SelectionChanged?.Invoke(); };
        timeline.Preview = time => PreviewRequested?.Invoke(time);
        Changed();
    }

    internal void Load(double duration, IReadOnlyList<double> measures, MediaRange? initialRange, bool reset)
    {
        updating = true;
        timeline.Duration = duration;
        timeline.Measures = measures;
        if (reset)
        {
            enabled.IsChecked = initialRange.HasValue;
            timeline.Start = Math.Clamp(initialRange?.Start ?? 0, 0, duration);
            timeline.End = Math.Clamp(initialRange?.End ?? duration, timeline.Start, duration);
        }
        else
        {
            timeline.Start = Math.Clamp(timeline.Start, 0, duration);
            timeline.End = Math.Clamp(timeline.End, timeline.Start, duration);
        }
        UpdateFields(); updating = false;
        Changed();
    }

    internal bool Validate(out string message)
    {
        message = "";
        if (enabled.IsChecked != true) return true;
        if (!TryFields(out var from, out var to) || from < 0 || to > timeline.Duration + .00005 || to <= from)
        {
            message = $"导出区间应满足 0 ≤ 起点 < 终点 ≤ {timeline.Duration:0.####} 秒。";
            return false;
        }
        timeline.Start = from; timeline.End = Math.Min(to, timeline.Duration);
        timeline.InvalidateVisual();
        return true;
    }

    private bool TryFields(out double from, out double to)
    {
        var validStart = double.TryParse(start.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out from);
        var validEnd = double.TryParse(end.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out to);
        return validStart && validEnd && double.IsFinite(from) && double.IsFinite(to);
    }

    private void CommitFields()
    {
        if (updating || enabled.IsChecked != true) return;
        if (!Validate(out _)) return;
        SelectionChanged?.Invoke();
        PreviewRequested?.Invoke(timeline.Start);
    }

    private void UpdateFields()
    {
        start.Text = timeline.Start.ToString("0.0000", CultureInfo.InvariantCulture);
        end.Text = timeline.End.ToString("0.0000", CultureInfo.InvariantCulture);
        timeline.InvalidateVisual();
    }

    private void Changed()
    {
        timeline.Selected = enabled.IsChecked == true;
        timeline.IsEnabled = start.IsEnabled = end.IsEnabled = snap.IsEnabled = timeline.Selected;
        timeline.Cursor = timeline.Selected ? Cursors.Hand : Cursors.Arrow;
        timeline.InvalidateVisual();
        if (!updating) SelectionChanged?.Invoke();
    }

    private sealed class RangeTimeline : FrameworkElement
    {
        internal double Duration, Start, End;
        internal bool Selected;
        internal IReadOnlyList<double> Measures = Array.Empty<double>();
        internal Func<bool> Snap = () => true;
        internal Action? Changed;
        internal Action<double>? Preview;
        private int dragging;
        private double cursor;
        private const double Inset = 10;
        internal RangeTimeline() { Height = 64; Focusable = true; ClipToBounds = true; }
        private double X(double time) => Inset + time / Math.Max(.001, Duration) * Math.Max(1, ActualWidth - 2 * Inset);
        private double TimeAt(Point position) => Math.Clamp((position.X - Inset) / Math.Max(1, ActualWidth - 2 * Inset) * Duration, 0, Duration);
        private Brush Brush(string resource) => (Brush)FindResource(resource);

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRoundedRectangle(Brush("PreviewBackground"), new Pen(Brush("MenuSeparator"), 1), new Rect(.5, .5, Math.Max(0, ActualWidth - 1), ActualHeight - 1), 5, 5);
            if (Duration <= 0) return;
            dc.PushOpacity(IsEnabled ? 1 : .4);
            const double trackY = 25;
            dc.DrawRoundedRectangle(Brush("ScrollTrack"), null, new Rect(Inset, trackY - 2, Math.Max(0, ActualWidth - 2 * Inset), 4), 2, 2);
            if (Selected)
            {
                dc.PushOpacity(.12);
                dc.DrawRoundedRectangle(Brush("HelperForeground"), null, new Rect(X(Start), trackY - 13, Math.Max(0, X(End) - X(Start)), 26), 3, 3);
                dc.Pop();
                dc.DrawRoundedRectangle(Brush("HelperForeground"), null, new Rect(X(Start), trackY - 2, Math.Max(0, X(End) - X(Start)), 4), 2, 2);
            }
            var lastLabel = double.NegativeInfinity;
            for (var i = 0; i < Measures.Count; i++)
            {
                var x = X(Measures[i]);
                dc.DrawLine(new Pen(Brush("MenuSeparator"), 1), new Point(x, trackY + 7), new Point(x, trackY + 12));
                if (x - lastLabel < 32) continue;
                var text = new FormattedText((i + 1).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface((FontFamily)FindResource("UiFontFamily"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    11, Brush("MutedForeground"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 2)), 43)); lastLabel = x;
            }
            if (Selected)
                foreach (var time in new[] { Start, End })
                {
                    var x = X(time);
                    dc.DrawRoundedRectangle(Brush("HelperForeground"), null, new Rect(x - 5, trackY - 12, 10, 24), 3, 3);
                    dc.DrawLine(new Pen(Brush("PreviewBackground"), 1), new Point(x, trackY - 4), new Point(x, trackY + 4));
                }
            dc.DrawEllipse(Brush("ButtonForeground"), new Pen(Brush("PreviewBackground"), 1), new Point(X(cursor), trackY), 3.5, 3.5);
            dc.Pop();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!IsEnabled || Duration <= 0) return;
            Focus();
            var p = e.GetPosition(this);
            dragging = Math.Abs(p.X - X(Start)) <= 12 ? 1 : Math.Abs(p.X - X(End)) <= 12 ? 2 : 0;
            if (dragging != 0) CaptureMouse();
            else { cursor = TimeAt(p); Preview?.Invoke(cursor); InvalidateVisual(); }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging == 0 || !IsMouseCaptured) return;
            var time = TimeAt(e.GetPosition(this));
            if (Snap()) time = Measures.Append(0).Append(Duration).MinBy(mark => Math.Abs(mark - time));
            if (dragging == 1) Start = Math.Min(time, End); else End = Math.Max(time, Start);
            cursor = time; Changed?.Invoke(); InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            dragging = 0; ReleaseMouseCapture(); Preview?.Invoke(cursor); e.Handled = true;
        }
    }
}
