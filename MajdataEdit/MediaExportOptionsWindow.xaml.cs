using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

internal sealed class TimelineAudioExportOptions
{
    public bool Force44100Hz { get; init; } = true;
}

internal sealed class TimelineVideoExportOptions
{
    public bool SmartTargetSize { get; init; } = true;
    public bool UseHighestSourceResolution { get; init; } = true;
    public int Width { get; init; }
    public int Height { get; init; }
    public int FrameRate { get; init; } = 30;
    public int VideoBitrateKbps { get; init; } = 6000;
    public double TargetSizeMiB { get; init; } = 20d;
}

public partial class MediaExportOptionsWindow : Window
{
    internal TimelineAudioExportOptions AudioOptions { get; private set; } = new();
    internal TimelineVideoExportOptions VideoOptions { get; private set; } = new();

    internal MediaExportOptionsWindow(bool videoMode)
    {
        InitializeComponent();
        VideoOptionsPanel.Visibility = videoMode ? Visibility.Visible : Visibility.Collapsed;
        AudioOptionsPanel.Visibility = videoMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConfirmAudio_Click(object sender, RoutedEventArgs e)
    {
        AudioOptions = new TimelineAudioExportOptions
        {
            Force44100Hz = Force44100Box.IsChecked == true
        };
        DialogResult = true;
    }

    private void SmartExport_Click(object sender, RoutedEventArgs e)
    {
        VideoOptions = new TimelineVideoExportOptions
        {
            SmartTargetSize = true,
            UseHighestSourceResolution = true,
            FrameRate = 30,
            TargetSizeMiB = 20d
        };
        DialogResult = true;
    }

    private void CustomExport_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPositiveInteger(GetSelectedText(FrameRateBox), 1, 240, out var frameRate) ||
            !TryReadPositiveInteger(GetSelectedText(BitrateBox), 64, 200000, out var bitrateKbps))
        {
            MessageBox.Show("帧率或码率无效。", "MajdataEdit", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var size = ResolutionBox.SelectedIndex switch
        {
            1 => (Width: 1920, Height: 1080),
            2 => (Width: 1280, Height: 720),
            3 => (Width: 640, Height: 360),
            _ => (Width: 0, Height: 0)
        };
        VideoOptions = new TimelineVideoExportOptions
        {
            SmartTargetSize = false,
            UseHighestSourceResolution = ResolutionBox.SelectedIndex <= 0,
            Width = size.Width,
            Height = size.Height,
            FrameRate = frameRate,
            VideoBitrateKbps = bitrateKbps
        };
        DialogResult = true;
    }

    private static bool TryReadPositiveInteger(string text, int minimum, int maximum, out int value)
    {
        if (int.TryParse(text?.Trim(), out value) && value >= minimum && value <= maximum)
            return true;
        value = 0;
        return false;
    }

    private static string GetSelectedText(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
}
