using System.Windows;

namespace MajdataEdit;

public partial class MainWindow
{
    private void DragPreviewCheck_Click(object sender, RoutedEventArgs e)
    {
        editorSetting.DragPreview = DragPreviewCheck.IsChecked == true;
        SaveEditorSetting();
        if (!editorSetting.DragPreview)
        {
            PrepareTimelineDrag();
            ClearStoppedNotePreview();
        }
    }

    private void PrepareTimelineDrag()
    {
        if (editorSetting?.DragPreview != false)
        {
            if (isPlaying) TogglePause();
            return;
        }
        if (stopPending) return;
        if (isPlaying || pausePending || lastEditorState == EditorControlMethod.Pause)
        {
            var position = GetTimelinePosition();
            ToggleStop();
            SetTimelinePosition(position);
        }
    }
}
