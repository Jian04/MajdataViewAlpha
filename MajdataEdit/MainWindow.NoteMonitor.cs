using System.Windows;

namespace MajdataEdit;

public partial class MainWindow
{
    private NoteMonitorWindow? noteMonitor;
    private object? monitoredTimeline;
    private int monitoredCaret = -1;
    private (float Ring,float Touch,float Star) monitoredSpeeds;
    private NoteMonitor? noteMonitorModel;
    private object? noteMonitorModelSource;
    private (float Ring,float Touch,float Star) noteMonitorModelSpeeds;
    private bool noteMonitorRefreshQueued;
    private void QueueNoteMonitorRefresh()
    {
        if(noteMonitor==null || noteMonitorRefreshQueued) return;
        noteMonitorRefreshQueued=true;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            noteMonitorRefreshQueued=false;
            RefreshNoteMonitor();
        }),System.Windows.Threading.DispatcherPriority.Background);
    }
    private NoteMonitor GetNoteMonitorModel()
    {
        var speeds=(editorSetting.playSpeed,editorSetting.touchSpeed,editorSetting.starSpeed);
        if(noteMonitorModel==null || !ReferenceEquals(noteMonitorModelSource,SimaiProcess.svTable) || noteMonitorModelSpeeds!=speeds)
        {
            noteMonitorModel=new NoteMonitor(speeds.playSpeed,speeds.touchSpeed,speeds.starSpeed);
            noteMonitorModelSource=SimaiProcess.svTable; noteMonitorModelSpeeds=speeds;
        }
        return noteMonitorModel;
    }
    private void NoteMonitor_MenuItem_Click(object sender,RoutedEventArgs e)
    {
        if (!FocusTool<NoteMonitorWindow>())
        {
            noteMonitor=new NoteMonitorWindow();
            monitoredTimeline=null;
            noteMonitor.Closed += (_,_) => {noteMonitor=null; DrawWave();};
            ShowTool(noteMonitor,"音符监控");
        }
        RefreshNoteMonitor();
        DrawWave();
    }
    private void RefreshNoteMonitor()
    {
        if (noteMonitor == null || editorSetting == null || chartParsePending || isLoading) return;
        var speeds=(editorSetting.playSpeed,editorSetting.touchSpeed,editorSetting.starSpeed);
        var caret=fumenEditor.HasSelection ? fumenEditor.Selection.Start : (int)GetRawFumenPosition();
        if (ReferenceEquals(monitoredTimeline,SimaiProcess.notelist) && monitoredCaret==caret && monitoredSpeeds==speeds) return;
        monitoredTimeline=SimaiProcess.notelist; monitoredCaret=caret; monitoredSpeeds=speeds;
        var source=GetRawFumenText();
        noteMonitor.ChartFolder=maidataDir; noteMonitor.PreviewSettings=editorSetting;
        var bounds=GetTimingSlotBounds(source,caret);
        var timings=SimaiProcess.notelist.Where(point=>point.sourcePosition==bounds.End).ToArray();
        noteMonitor.UpdateSlot(timings,GetNoteMonitorModel());
    }
}
