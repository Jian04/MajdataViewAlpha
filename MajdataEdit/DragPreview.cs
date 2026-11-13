using System.Windows;

namespace MajdataEdit;

public partial class MainWindow
{
    private void DragPreviewCheck_Click(object sender, RoutedEventArgs e)
    {
        editorSetting.DragPreview = DragPreviewCheck.IsChecked == true;
        SaveEditorSetting();
    }

    private void PrepareTimelineDrag()
    {
        if (editorSetting?.DragPreview != false)
        {
            if (isPlaying) TogglePause();
            return;
        }
        if (stopPending) return;
        if (isPlaying || pausePending || lastEditorState != EditorControlMethod.Stop)
        {
            var position = GetTimelinePosition();
            StopPlaybackForScrub();
            SetTimelinePosition(position);
            _ = PreviewAfterDragStopAsync(pendingScrubStop!, viewControlGeneration);
        }
    }

    private async Task PreviewAfterDragStopAsync(Task<bool> stop, int generation)
    {
        if (await stop && generation == viewControlGeneration) QueueNotePreview();
    }
}
