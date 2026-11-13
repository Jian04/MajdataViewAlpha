using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Font095Sampling
{
    internal static void Run()
    {
        var root = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(35, 39, 46)) };
        foreach (var family in new[] { App.UiFontFamily })
        foreach (var mode in new[] { TextFormattingMode.Display, TextFormattingMode.Ideal })
        foreach (var size in new[] { 11d, 12d, 13d })
        foreach (var rendering in new[] { TextRenderingMode.Grayscale, TextRenderingMode.ClearType })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 30 };
            row.Children.Add(new TextBlock { Text = $"{mode} {size} {rendering}", Width = 175, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brushes.Gray });
            foreach (var offset in new[] { 0d, 1d / 3 })
            {
                var text = new TextBlock { Text = "三三三，编辑器设置，语法错误。", FontFamily = family, FontSize = size, Foreground = Brushes.White, Width = 280, RenderTransform = new TranslateTransform(0, offset) };
                TextOptions.SetTextFormattingMode(text, mode);
                TextOptions.SetTextRenderingMode(text, rendering);
                TextOptions.SetTextHintingMode(text, TextHintingMode.Fixed);
                RenderOptions.SetClearTypeHint(text, ClearTypeHint.Enabled);
                row.Children.Add(text);
            }
            RenderOptions.SetClearTypeHint(row, ClearTypeHint.Enabled);
            row.CacheMode = new BitmapCache { EnableClearType = true, SnapsToDevicePixels = true };
            root.Children.Add(row);
        }
        root.Measure(new Size(800, 600)); root.Arrange(new Rect(0, 0, 800, 600)); root.UpdateLayout();
        var image = new RenderTargetBitmap(1200, 900, 144, 144, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create("alpha/.codex-build/095-font-sampling.png"); encoder.Save(file);
        Console.WriteLine("095 font sampling comparison saved: same rounded font, Display/Ideal, Grayscale/ClearType, 11/12/13 DIP, integer/half physical pixel offsets.");
    }
}
