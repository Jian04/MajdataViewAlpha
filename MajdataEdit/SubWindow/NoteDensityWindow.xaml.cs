using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;

namespace MajdataEdit;

public enum DensityCategory
{
    TapFamily,
    SlideBody,
    Touch
}

public readonly struct NoteDensitySample
{
    public readonly double Time;
    public readonly DensityCategory Category;

    public NoteDensitySample(double time, DensityCategory category)
    {
        Time = time;
        Category = category;
    }
}

public partial class NoteDensityWindow : Window
{
    private const int BarPx = 3;
    private const int DensityCacheBins = 4096;
    private static readonly Color PinkColor = Color.FromArgb(255, 90, 140);
    private static readonly Color BlueColor = Color.FromArgb(55, 182, 255);
    private static readonly Color YellowColor = Color.FromArgb(255, 210, 60);
    private static readonly Color AudioColor = Color.FromArgb(225, 99, 210, 255);

    private readonly IReadOnlyList<float> audioIntensity;
    private readonly double length;
    private readonly double audioLength;
    private readonly int[] cachedPink = new int[DensityCacheBins];
    private readonly int[] cachedBlue = new int[DensityCacheBins];
    private readonly int[] cachedYellow = new int[DensityCacheBins];
    private int renderQueued;
    private int renderedWidth;
    private int renderedHeight;

    private static string L(string key, params object[] args)
    {
        var value = MainWindow.GetLocalizedString(key);
        return args.Length == 0 ? value : string.Format(value, args);
    }

    public NoteDensityWindow(IReadOnlyList<NoteDensitySample> samples, IReadOnlyList<float> audioIntensity,
        double length, double audioLength, string title)
    {
        InitializeComponent();
        this.audioIntensity = audioIntensity;
        this.length = length;
        this.audioLength = audioLength;
        if (!string.IsNullOrWhiteSpace(title))
            Title = L("NoteDensityWindowTitle", title);

        var pink = 0;
        var blue = 0;
        var yellow = 0;
        foreach (var sample in samples)
        {
            var cacheIndex = length > 0
                ? Math.Clamp((int)(sample.Time / length * DensityCacheBins), 0, DensityCacheBins - 1)
                : 0;
            switch (sample.Category)
            {
                case DensityCategory.TapFamily:
                    pink++;
                    cachedPink[cacheIndex]++;
                    break;
                case DensityCategory.SlideBody:
                    blue++;
                    cachedBlue[cacheIndex]++;
                    break;
                case DensityCategory.Touch:
                    yellow++;
                    cachedYellow[cacheIndex]++;
                    break;
            }
        }

        SummaryText.Text = L("NoteDensitySummary", pink + blue + yellow, FormatTime(length));
        Loaded += (_, _) => QueueRender();
        ContentRendered += (_, _) => QueueRender();
        DensityImage.SizeChanged += (_, _) => QueueRender();
        ThemeManager.ThemeChanged += QueueRender;
        Closed += (_, _) => ThemeManager.ThemeChanged -= QueueRender;
    }

    private void QueueRender()
    {
        if (Interlocked.Exchange(ref renderQueued, 1) != 0)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            Interlocked.Exchange(ref renderQueued, 0);
            Render();
        }));
    }

    private void Render()
    {
        var width = Math.Max(4, (int)Math.Round(DensityImage.ActualWidth));
        var height = Math.Max(4, (int)Math.Round(DensityImage.ActualHeight));
        if (width == renderedWidth && height == renderedHeight)
            return;

        try
        {
            DensityImage.Source = RenderBitmap(width, height);
            renderedWidth = width;
            renderedHeight = height;
        }
        catch (Exception ex)
        {
            SummaryText.Text = L("NoteDensityRenderFailed", ex.Message);
        }
    }

    private BitmapSource RenderBitmap(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.Clear(ThemeColor(ThemeManager.CurrentTheme.waveBackground, Color.FromArgb(26, 27, 32)));

            const int marginLeft = 7;
            const int marginRight = 7;
            const int marginTop = 8;
            const int marginBottom = 19;
            var plotWidth = width - marginLeft - marginRight;
            var plotHeight = height - marginTop - marginBottom;
            var baseY = marginTop + plotHeight;
            if (plotWidth > 1 && plotHeight > 1 && length > 0)
            {
                DrawTimeAxis(graphics, marginLeft, marginTop, baseY, plotWidth);
                DrawNoteBars(graphics, marginLeft, baseY, plotWidth, plotHeight);
                DrawAudioCurve(graphics, marginLeft, marginTop, plotWidth, plotHeight);
                using var basePen = new Pen(Color.FromArgb(120, 120, 140));
                graphics.DrawLine(basePen, marginLeft, baseY, marginLeft + plotWidth, baseY);
            }
        }

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var source = BitmapSource.Create(width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, bytes, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void DrawNoteBars(Graphics graphics, int x0, int baseY, int plotWidth, int plotHeight)
    {
        var binCount = Math.Max(1, plotWidth / BarPx);
        var pink = new int[binCount];
        var blue = new int[binCount];
        var yellow = new int[binCount];
        for (var sourceIndex = 0; sourceIndex < DensityCacheBins; sourceIndex++)
        {
            var index = Math.Min(binCount - 1, sourceIndex * binCount / DensityCacheBins);
            pink[index] += cachedPink[sourceIndex];
            blue[index] += cachedBlue[sourceIndex];
            yellow[index] += cachedYellow[sourceIndex];
        }

        var maxSum = 1;
        for (var i = 0; i < binCount; i++)
            maxSum = Math.Max(maxSum, pink[i] + blue[i] + yellow[i]);

        var barWidth = plotWidth / (float)binCount;
        using var pinkBrush = new SolidBrush(PinkColor);
        using var blueBrush = new SolidBrush(BlueColor);
        using var yellowBrush = new SolidBrush(YellowColor);
        for (var i = 0; i < binCount; i++)
        {
            var x = x0 + i * barWidth;
            var y = (float)baseY;
            DrawBarPart(graphics, pinkBrush, x, ref y, barWidth + 1f, pink[i] / (float)maxSum * plotHeight);
            DrawBarPart(graphics, blueBrush, x, ref y, barWidth + 1f, blue[i] / (float)maxSum * plotHeight);
            DrawBarPart(graphics, yellowBrush, x, ref y, barWidth + 1f, yellow[i] / (float)maxSum * plotHeight);
        }
    }

    private static void DrawBarPart(Graphics graphics, Brush brush, float x, ref float y, float width, float height)
    {
        if (height <= 0f)
            return;
        y -= height;
        graphics.FillRectangle(brush, x, y, width, height);
    }

    private void DrawAudioCurve(Graphics graphics, int x0, int y0, int plotWidth, int plotHeight)
    {
        if (audioIntensity.Count < 2)
            return;

        using var path = new GraphicsPath();
        var previous = PointF.Empty;
        var curveWidth = Math.Clamp((int)Math.Round(audioLength / length * plotWidth), 2, plotWidth);
        for (var x = 0; x < curveWidth; x++)
        {
            var sourcePosition = x / (double)Math.Max(1, curveWidth - 1) * (audioIntensity.Count - 1);
            var left = (int)sourcePosition;
            var right = Math.Min(left + 1, audioIntensity.Count - 1);
            var mix = (float)(sourcePosition - left);
            var level = audioIntensity[left] + (audioIntensity[right] - audioIntensity[left]) * mix;
            var point = new PointF(x0 + x, y0 + (1f - Math.Clamp(level, 0f, 1f)) * plotHeight);
            if (x == 0)
                previous = point;
            else
            {
                path.AddLine(previous, point);
                previous = point;
            }
        }

        graphics.SmoothingMode = SmoothingMode.None;
        using var linePen = new Pen(AudioColor, 1f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawPath(linePen, path);
    }

    private void DrawTimeAxis(Graphics graphics, int x0, int topY, int baseY, int plotWidth)
    {
        double[] candidates = { 5, 10, 15, 20, 30, 60, 120, 300 };
        var interval = candidates[^1];
        foreach (var candidate in candidates)
        {
            if (length / candidate <= 14)
            {
                interval = candidate;
                break;
            }
        }

        using var gridPen = new Pen(Color.FromArgb(45, 120, 120, 140));
        using var font = new Font("Segoe UI", 7f);
        using var labelBrush = new SolidBrush(Color.FromArgb(150, 160, 180));
        for (var time = 0d; time <= length + 0.001; time += interval)
        {
            var x = x0 + (float)(time / length * plotWidth);
            graphics.DrawLine(gridPen, x, topY, x, baseY);
            graphics.DrawString(FormatTime(time), font, labelBrush, x + 1f, baseY + 2f);
        }
    }

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    private static Color ThemeColor(string value, Color fallback)
    {
        try
        {
            var parsed = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            return Color.FromArgb(parsed.A, parsed.R, parsed.G, parsed.B);
        }
        catch
        {
            return fallback;
        }
    }
}
