using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using MajdataCore;

namespace MajdataEdit.Editor;

internal static partial class AlphaCommandHints
{
    private static Window? curveWindow, distanceWindow;
    private static string curveWindowKey = "";
    private static bool positioningCurves;

    private static void CloseCurveWindows()
    {
        curveWindow?.Close(); distanceWindow?.Close();
        curveWindow = distanceWindow = null; curveWindowKey = "";
    }

    private static void UpdateCurveWindows(Window hint, string name, string text)
    {
        var filter = Describe(name)?.kind == AlphaCommandKind.Effect;
        var key = filter ? name : name + ":" + text;
        if (curveWindowKey == key && curveWindow?.Owner == hint) return;
        CloseCurveWindows(); curveWindowKey = key;
        Window Create(FrameworkElement plot, string caption)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = caption, Foreground = HintAccent, FontSize = 11, Margin = new Thickness(0, 0, 0, 5) });
            content.Children.Add(plot);
            var window = new Window { Owner = hint, Topmost = true, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowActivated = false, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight,
                IsHitTestVisible = false, Background = HintBackground,
                Content = new Border { Child = content, Background = HintBackground, BorderBrush = HintBorder,
                    BorderThickness = new Thickness(1), Padding = new Thickness(10) } };
            window.Show(); return window;
        }
        if (filter)
        {
            curveWindow = Create(BuildFilterPreview(name), name + "  ·  " +
                Localized("示例预览", "Effect preview", "効果プレビュー"));
            PositionCurveWindows(hint);
            return;
        }
        if (BuildCurvePreview(name, text) is not StackPanel preview) return;
        var plots = preview.Children.OfType<Canvas>().ToArray();
        var title = ((TextBlock)preview.Children[0]).Text;
        foreach (var plot in plots) preview.Children.Remove(plot);
        curveWindow = Create(plots[0], title);
        if (plots.Length > 1) distanceWindow = Create(plots[1], Localized("位移 · t/T", "Displacement · t/T", "変位 · t/T"));
        PositionCurveWindows(hint);
    }

    private static FrameworkElement BuildFilterPreview(string name)
    {
        var uri = new Uri($"pack://application:,,,/MajdataEdit;component/Image/FilterPreviews/{name}.jpg");
        var atlas = new BitmapImage(uri);
        atlas.Freeze();
        const int columns = 4, rows = 3;
        var size = atlas.PixelWidth / columns;
        var frames = new CroppedBitmap[columns * rows];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = new CroppedBitmap(atlas, new Int32Rect(i % columns * size, i / columns * size, size, size));
            frames[i].Freeze();
        }
        var image = new Image { Source = frames[0], Width = 216, Height = 216 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        var timer = new DispatcherTimer(DispatcherPriority.Background, image.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(name == "SHATTER" ? 45 : 90) };
        var frame = 0;
        timer.Tick += (_, _) =>
        {
            frame = (frame + 1) % (frames.Length + 3);
            var next = frames[Math.Min(frame, frames.Length - 1)];
            if (!ReferenceEquals(next, image.Source)) image.Source = next;
        };
        image.Loaded += (_, _) => timer.Start();
        image.Unloaded += (_, _) => timer.Stop();
        return image;
    }

    private static void PositionCurveWindows(Window hint)
    {
        if (curveWindow == null || positioningCurves) return;
        positioningCurves = true;
        try
        {
            var point = hint.PointToScreen(new Point(hint.ActualWidth + 8, 0));
            var matrix = PresentationSource.FromVisual(hint)!.CompositionTarget.TransformFromDevice;
            point = matrix.Transform(point);
            var monitor = MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(hint).Handle, 2);
            var info = new CurveMonitorInfo { Size = Marshal.SizeOf<CurveMonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var topLeft = matrix.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = matrix.Transform(new Point(info.Work.Right, info.Work.Bottom));
                var groupHeight = curveWindow.ActualHeight + (distanceWindow?.ActualHeight + 6 ?? 0);
                point.X = Math.Max(topLeft.X, point.X);
                point.Y = Math.Max(topLeft.Y, Math.Min(point.Y, bottomRight.Y - groupHeight));
            }
            curveWindow.Left = point.X; curveWindow.Top = point.Y;
            if (distanceWindow != null)
            {
                distanceWindow.Left = point.X; distanceWindow.Top = point.Y + curveWindow.ActualHeight + 6;
            }
        }
        finally { positioningCurves = false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CurveRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CurveMonitorInfo { public int Size; public CurveRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref CurveMonitorInfo info);

    private static string GetCurveText(TextDocument document, int start, int caret)
    {
        // Select the current target's tuple, including values already after the caret.
        var depth = 0;
        var tuple = -1;
        for (var i = start; i < caret; i++)
        {
            var ch = document.GetCharAt(i);
            if (ch == '(' && depth++ == 0) tuple = i;
            else if (ch == ')') depth--;
            else if (ch == ',' && depth == 0) tuple = -1;
        }
        if (tuple < 0) return "";
        depth = 0;
        var end = tuple;
        for (; end < document.TextLength; end++)
        {
            var ch = document.GetCharAt(end);
            if (ch is '>' or '\r' or '\n') break;
            if (ch == '(') depth++;
            if (ch == ')' && --depth == 0) { end++; break; }
        }
        return document.GetText(tuple, end - tuple) + (depth == 1 ? ")" : "");
    }

    private static FrameworkElement? BuildCurvePreview(string name, string text)
    {
        var spec = Describe(name)?.scalar;
        if (spec?.allowsTransition != true || text.Length < 2 ||
            !AlphaCommandGrammar.TrySplitValues(text[1..^1], out var args) || args.Count < 4) return null;
        // Normalised time avoids assuming a BPM while a slot is still being edited.
        var duration = args[2];
        if (!AlphaCommandGrammar.TryParseDuration(duration, 120, out var seconds) || seconds <= 0) return null;
        args[2] = "1";
        if (!NoteValueTransition.TryParse("(" + string.Join(",", args) + ")", 120, spec, out var curve)) return null;
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"{curve.easing} · T = {duration}", Foreground = HintAccent, FontSize = 11 });
        var channels = curve.fromUntinted ? 1 : spec.kind == AlphaValueKind.Color ? 3 : spec.kind == AlphaValueKind.Scale ? 2 : 1;
        var values = new double[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            values[channel] = new double[97];
            for (var i = 0; i < 97; i++)
                values[channel][i] = curve.fromUntinted ? Math.Clamp(curve.Progress(i / 96d), 0f, 1f) :
                    channel == 0 ? curve.Evaluate(i / 96d) :
                    channel == 1 ? curve.EvaluateY(i / 96d) : curve.EvaluateColor(i / 96d).B;
        }
        panel.Children.Add(CurvePlot(values, curve.fromUntinted ? "Tint" : channels == 3 ? "RGB" : channels == 2 ? "X / Y" : "value"));
        if (name is "SV" or "HS")
        {
            var distance = new double[97];
            for (var i = 1; i < 97; i++) distance[i] = distance[i - 1] + (values[0][i - 1] + values[0][i]) / 192d;
            panel.Children.Add(CurvePlot(new[] { distance }, "s / (v₀T)"));
            panel.Children.Add(new TextBlock { Text = Localized("相对位移；横轴为 t/T。负值表示反向。", "Relative displacement; time is t/T. Negative values reverse direction.", "相対変位。横軸はt/T。負の値は逆方向。"),
                Foreground = HintOptional, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        }
        return panel;
    }

    private static FrameworkElement CurvePlot(double[][] values, string label)
    {
        const double width = 320, height = 100, left = 42, top = 18, bottom = 83;
        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 3, 0, 0), IsHitTestVisible = false };
        var min = Math.Min(0, values.Min(row => row.Min()));
        var max = Math.Max(0, values.Max(row => row.Max()));
        if (max - min < .000001) max = min + 1;
        double Y(double value) => bottom - (value - min) / (max - min) * (bottom - top);
        void Text(string value, double x, double y)
        {
            var text = new TextBlock { Text = value, FontFamily = HintMonoFont, FontSize = 10, Foreground = HintSecondary };
            Canvas.SetLeft(text, x); Canvas.SetTop(text, y); canvas.Children.Add(text);
        }
        Text(label, left, 0); Text("0", left, bottom + 2); Text("t/T = 1", width - 55, bottom + 2);
        Text(max.ToString("0.##", CultureInfo.InvariantCulture), 0, top - 5);
        Text(min.ToString("0.##", CultureInfo.InvariantCulture), 0, bottom - 7);
        foreach (var y in new[] { top, Y(0), bottom })
            canvas.Children.Add(new Line { X1 = left, X2 = width - 8, Y1 = y, Y2 = y, Stroke = HintBorder, StrokeThickness = 1 });
        Brush[] colors = values.Length == 3 ? new Brush[] { Brushes.Coral, Brushes.MediumSeaGreen, Brushes.CornflowerBlue }
            : new[] { HintAccent, Brushes.Goldenrod };
        for (var c = 0; c < values.Length; c++)
        {
            var line = new Polyline { Stroke = colors[c], StrokeThickness = 1.6 };
            for (var i = 0; i < values[c].Length; i++)
                line.Points.Add(new Point(left + (width - left - 8) * i / (values[c].Length - 1), Y(values[c][i])));
            canvas.Children.Add(line);
        }
        return canvas;
    }
}
