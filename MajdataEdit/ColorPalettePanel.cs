using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed class ColorPalettePanel : Window
{
    private readonly Border preview = new() { Height = 78, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox hex = new() { MaxLength = 7, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
    private readonly Button picker = new() { Width = 30, Height = 30, Padding = new Thickness(6), Background = Brushes.Transparent, ToolTip = "屏幕取色" };
    private readonly TextBlock status = SessionTheme.Text("");
    private readonly Slider[] channels = new Slider[3];
    private readonly TextBlock[] values = new TextBlock[3];
    private bool updating;
    private Color selected;
    private Color beforePicking;
    private System.Drawing.Bitmap? screenPixel;
    private System.Drawing.Graphics? screenGraphics;

    internal ColorPalettePanel()
    {
        Title = "调色盘";
        SessionTheme.Apply(this, 360, 520);
        var body = new StackPanel { Margin = new Thickness(12) };
        preview.SetResourceReference(Border.BorderBrushProperty, "MenuSeparator"); preview.BorderThickness = new Thickness(1);
        body.Children.Add(preview);
        var hexRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition());
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = SessionTheme.Text("HEX"); label.VerticalAlignment = VerticalAlignment.Center;
        hexRow.Children.Add(label);
        SessionTheme.Input(hex);
        hex.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(hex, 1); hexRow.Children.Add(hex);
        picker.Template = (ControlTemplate)FindResource("SidebarButton");
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M10,2 L14,6 M9,3 L13,7 M10,4 L3,11 L2,14 L5,13 L12,6"), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ButtonForeground");
        picker.Content = icon;
        Grid.SetColumn(picker, 2); hexRow.Children.Add(picker);
        body.Children.Add(hexRow);
        picker.Click += (_, _) =>
        {
            if (Mouse.Capture(picker))
            {
                beforePicking = selected;
                screenPixel = new System.Drawing.Bitmap(1, 1);
                screenGraphics = System.Drawing.Graphics.FromImage(screenPixel);
                picker.Focus();
                picker.Cursor = Cursors.Cross;
                status.Text = "移动预览，点击固定，Esc 取消";
            }
            else status.Text = "无法捕获鼠标，请重试。";
        };
        picker.LostMouseCapture += (_, _) =>
        {
            FinishPicking(false);
        };
        picker.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !picker.IsMouseCaptured) return;
            FinishPicking(false);
            e.Handled = true;
        };
        picker.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (screenPixel == null) return;
            e.Handled = true;
            PreviewScreenColor();
            FinishPicking(true);
        };
        picker.PreviewMouseMove += (_, _) => { if (screenPixel != null) PreviewScreenColor(); };
        Closed += (_, _) => FinishPicking(false);
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            var text = hex.Text.Trim().TrimStart('#');
            if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                SetColor(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), preserveInput: true);
            }
            else status.Text = "输入 6 位 HEX 色值";
        };
        for (var index = 0; index < channels.Length; index++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            var name = SessionTheme.Text("RGB"[index].ToString()); name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var slider = SessionTheme.PlaybackSlider();
            slider.Maximum = 255; slider.TickFrequency = 1; slider.IsSnapToTickEnabled = true;
            slider.VerticalAlignment = VerticalAlignment.Center; slider.Margin = new Thickness(0, 0, 8, 0);
            channels[index] = slider;
            Grid.SetColumn(slider, 1); row.Children.Add(slider);
            values[index] = SessionTheme.Text("0"); values[index].TextAlignment = TextAlignment.Right;
            Grid.SetColumn(values[index], 2); row.Children.Add(values[index]);
            slider.ValueChanged += (_, _) => {
                if (!updating) SetColor(Color.FromRgb((byte)channels[0].Value, (byte)channels[1].Value, (byte)channels[2].Value)); };
            body.Children.Add(row);
        }
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeground");
        status.Margin = new Thickness(0, 4, 0, 0); body.Children.Add(status);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(BackgroundProperty, "PanelBackground"); Content = scroll;
        SetColor(Color.FromRgb(253, 121, 185));
    }

    private void SetColor(Color color, bool preserveInput = false)
    {
        updating = true;
        selected = color;
        preview.Background = new SolidColorBrush(color);
        if (!preserveInput) hex.Text = Hex(color);
        var rgb = new[] { color.R, color.G, color.B };
        for (var index = 0; index < channels.Length; index++)
        { channels[index].Value = rgb[index]; values[index].Text = rgb[index].ToString(CultureInfo.InvariantCulture); }
        status.Text = "";
        updating = false;
    }

    private void PreviewScreenColor()
    {
        try
        {
            if (!GetCursorPos(out var point)) throw new System.ComponentModel.Win32Exception();
            screenGraphics!.CopyFromScreen(point.X, point.Y, 0, 0, screenPixel!.Size);
            var color = screenPixel.GetPixel(0, 0);
            SetColor(Color.FromRgb(color.R, color.G, color.B));
            status.Text = "移动预览，点击固定，Esc 取消";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or ExternalException)
        {
            FinishPicking(false);
            status.Text = "取色失败：" + error.Message;
        }
    }

    private void FinishPicking(bool accept)
    {
        if (screenPixel == null) return;
        screenGraphics!.Dispose(); screenGraphics = null;
        screenPixel.Dispose(); screenPixel = null;
        picker.ReleaseMouseCapture(); picker.Cursor = null;
        if (!accept) SetColor(beforePicking);
        else status.Text = "已取色 " + Hex(selected);
    }
    private static string Hex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out System.Drawing.Point point);
}
