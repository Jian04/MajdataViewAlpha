using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

public partial class AutoOnsetWindow : Window
{
    private readonly string audioPath;
    private readonly string title;
    private CancellationTokenSource? generationCancellation;
    private bool closeAfterCancellation;

    internal AutoOnsetResult? GenerationResult { get; private set; }
    internal string SelectedDifficulty { get; private set; }

    public AutoOnsetWindow(
        string audioPath,
        double currentFirst,
        string currentDifficulty,
        string title)
    {
        InitializeComponent();
        this.audioPath = audioPath;
        this.title = title;
        FirstBox.Text = currentFirst.ToString("0.######", CultureInfo.InvariantCulture);
        DifficultyBox.Text = string.IsNullOrWhiteSpace(currentDifficulty) ? "13" : currentDifficulty;
        SelectedDifficulty = DifficultyBox.Text;
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadOptionalPositive(BpmBox.Text, out var bpm))
        {
            ShowValidationError("AutoOnsetBpmInvalid", BpmBox);
            return;
        }
        if (!TryReadOptionalNumber(FirstBox.Text, out var first))
        {
            ShowValidationError("AutoOnsetFirstInvalid", FirstBox);
            return;
        }

        var level = DifficultyBox.Text.Trim();
        if (!TryParseLevel(level, out _))
        {
            ShowValidationError("AutoOnsetDifficultyInvalid", DifficultyBox);
            return;
        }
        if (ThresholdPresetBox.SelectedItem is not ComboBoxItem thresholdItem ||
            !double.TryParse(thresholdItem.Tag?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var threshold))
        {
            MessageBox.Show(
                MainWindow.GetLocalizedString("AutoOnsetThresholdInvalid"),
                MainWindow.GetLocalizedString("Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (MessageBox.Show(
                MainWindow.GetLocalizedString("AutoOnsetReplaceConfirm"),
                MainWindow.GetLocalizedString("Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        generationCancellation = new CancellationTokenSource();
        var completed = false;
        try
        {
            var result = await AutoOnsetRunner.GenerateAsync(
                new AutoOnsetRequest(audioPath, level, bpm, first, threshold, title),
                message => Dispatcher.InvokeAsync(() => StatusText.Text = message),
                generationCancellation.Token);
            GenerationResult = result;
            SelectedDifficulty = level;
            StatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                MainWindow.GetLocalizedString("AutoOnsetComplete"),
                result.Bpm,
                result.First,
                result.PredictedOnsets);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = MainWindow.GetLocalizedString("AutoOnsetCancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                MainWindow.GetLocalizedString("AutoOnsetFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = MainWindow.GetLocalizedString("AutoOnsetFailed");
        }
        finally
        {
            generationCancellation?.Dispose();
            generationCancellation = null;
            SetBusy(false);
        }
        if (completed)
            DialogResult = true;
        else if (closeAfterCancellation)
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (generationCancellation != null)
        {
            generationCancellation.Cancel();
            CancelButton.IsEnabled = false;
            StatusText.Text = MainWindow.GetLocalizedString("AutoOnsetCancelling");
            return;
        }
        Close();
    }

    private void AutoOnsetWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (generationCancellation == null)
            return;
        e.Cancel = true;
        closeAfterCancellation = true;
        generationCancellation.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = MainWindow.GetLocalizedString("AutoOnsetCancelling");
    }

    private void SetBusy(bool busy)
    {
        GenerateButton.IsEnabled = !busy;
        BpmBox.IsEnabled = !busy;
        FirstBox.IsEnabled = !busy;
        DifficultyBox.IsEnabled = !busy;
        ThresholdPresetBox.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = true;
    }

    private static bool TryReadOptionalPositive(string text, out double? value)
    {
        if (!TryReadOptionalNumber(text, out value))
            return false;
        return !value.HasValue || value.Value > 0;
    }

    private static bool TryReadOptionalNumber(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
            return double.IsFinite(parsed);
        }
        return false;
    }

    private static bool TryParseLevel(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var normalized = text.Trim();
        var hasPlus = normalized.EndsWith("+", StringComparison.Ordinal);
        if (hasPlus)
            normalized = normalized[..^1];
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return false;
        if (hasPlus)
            value += 0.75;
        return value is >= 1 and <= 15;
    }

    private static void ShowValidationError(string key, Control control)
    {
        MessageBox.Show(
            MainWindow.GetLocalizedString(key),
            MainWindow.GetLocalizedString("Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        control.Focus();
    }
}
