using System.Globalization;
using System.IO;
using System.Windows;
using MajdataEdit.Editor;

namespace MajdataEdit;

public partial class MainWindow
{
    private void AutoOnset_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedDifficulty < 0 || string.IsNullOrWhiteSpace(maidataDir))
        {
            MessageBox.Show(
                GetLocalizedString("AutoOnsetOpenChartFirst"),
                GetLocalizedString("AutoOnset"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (isPlaying || lastEditorState != EditorControlMethod.Stop)
        {
            MessageBox.Show(
                GetLocalizedString("AutoOnsetStopPlayback"),
                GetLocalizedString("AutoOnset"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var audioPath = new[]
        {
            Path.Combine(maidataDir, "track.ogg"),
            Path.Combine(maidataDir, "track.mp3"),
            Path.Combine(maidataDir, "track.wav"),
            Path.Combine(maidataDir, "track.flac")
        }.FirstOrDefault(File.Exists);
        if (audioPath == null)
        {
            MessageBox.Show(
                GetLocalizedString("NoTrack"),
                GetLocalizedString("Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var currentLevel = selectedDifficulty < SimaiProcess.levels.Length
            ? SimaiProcess.levels[selectedDifficulty]
            : "13";
        var dialog = new AutoOnsetWindow(
            audioPath,
            SimaiProcess.first,
            currentLevel,
            SimaiProcess.title ?? string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.GenerationResult == null)
            return;

        var result = dialog.GenerationResult!;
        var formatted = BeatFormatBrush.Transform(result.Chart, 16);
        var organized = ChartOrganizer.Organize(formatted, addMeasureComments: false);
        if (string.IsNullOrWhiteSpace(organized))
        {
            MessageBox.Show(
                GetLocalizedString("AutoOnsetNoOutput"),
                GetLocalizedString("AutoOnsetFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        fumenEditor.Text = organized;
        SimaiProcess.fumens[selectedDifficulty] = organized;
        SimaiProcess.first = (float)result.First;
        OffsetTextBox.Text = result.First.ToString("0.######", CultureInfo.InvariantCulture);
        if (!string.Equals(LevelTextBox.Text, dialog.SelectedDifficulty, StringComparison.Ordinal))
            LevelTextBox.Text = dialog.SelectedDifficulty;
        SetSavedState(false);
        chartParsePending = true;
        QueueImmediateWaveRefresh();
        SyntaxCheck();

        MessageBox.Show(
            string.Format(
                CultureInfo.CurrentCulture,
                GetLocalizedString("AutoOnsetApplied"),
                result.Bpm,
                result.First,
                result.PredictedOnsets),
            GetLocalizedString("AutoOnset"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
