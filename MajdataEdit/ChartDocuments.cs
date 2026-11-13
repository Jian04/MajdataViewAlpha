using System.IO;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;

namespace MajdataEdit;

public partial class MainWindow
{
    private sealed class ChartDocument
    {
        internal string Directory = "";
        internal TabItem Tab = null!;
        internal TextBlock Caption = null!;
        internal SimaiProcess.DocumentData Data = null!;
        internal TextDocument Text = null!;
        internal int Difficulty, Caret, SelectionStart, SelectionLength;
        internal double Scroll, Time;
        internal string Offset = "", DiskText = "";
        internal bool Saved;
        internal bool GuestPendingSave;
        internal string? GuestSavePath;
        internal string AudioPath = "";
        internal DateTime AudioModified;
        internal short[][] Waveforms = null!;
        internal double AudioLength;
    }

    private readonly List<ChartDocument> chartDocuments = new();
    private ChartDocument? activeChartDocument;
    private bool switchingChartDocument;

    private void FlushSongTools()
    {
        if (toolTabs.TryGetValue(typeof(Infomation), out var info))
            ((Infomation)info.Window).FlushPendingInformation();
        if (toolTabs.TryGetValue(typeof(MediaClipInfoWindow), out var clip))
            ((MediaClipInfoWindow)clip.Window).FlushPendingChanges();
        recordVideoWindow?.FlushPendingInformation();
        gifExportWindow?.FlushPendingInformation();
    }

    private void CaptureChartDocument()
    {
        var document = activeChartDocument;
        if (document == null || selectedDifficulty < 0) return;
        SimaiProcess.fumens[selectedDifficulty] = GetRawFumenText();
        document.Data = SimaiProcess.CaptureDocument();
        document.Text = FumenContent.Document;
        document.Difficulty = selectedDifficulty;
        document.Offset = OffsetTextBox.Text;
        document.Saved = isSaved;
        document.GuestPendingSave = guestChartPendingSave;
        document.GuestSavePath = guestSavePath;
        document.DiskText = openedChartText ?? "";
        document.Caret = FumenContent.CaretOffset;
        document.SelectionStart = FumenContent.SelectionStart;
        document.SelectionLength = FumenContent.SelectionLength;
        document.Scroll = FumenContent.VerticalOffset;
        document.Time = GetTimelinePosition();
        UpdateChartDocumentCaption(document);
    }

    private void UpdateChartDocumentCaption(ChartDocument document)
    {
        document.Caption.Text = document.Data.Title;
        ((Panel)document.Tab.Header).Children.OfType<Button>().Single().Tag = !document.Saved;
        document.Tab.ToolTip = document.Directory;
    }

    private void InstallChartFromFile(string path, EditorSongAudio? preparedAudio)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var document = chartDocuments.FirstOrDefault(item => string.Equals(item.Directory, path, StringComparison.OrdinalIgnoreCase));
        if (document == activeChartDocument && document != null && !installingRoomSong) return;
        if (switchingChartDocument) return;
        if (recordVideoWindow?.IsRecording == true || gifExportWindow?.IsRecording == true || activeChartExport != null)
        {
            MessageBox.Show(this, "请先停止录制或批量导出，再切换谱面。", "切换谱面");
            return;
        }
        var dataPath = Path.Combine(path, "maidata.txt");
        var trackPath = MediaTools.FindCachedTimelineAudio(path) ??
            Path.Combine(path, File.Exists(Path.Combine(path, "track.ogg")) ? "track.ogg" : "track.mp3");
        if (!File.Exists(dataPath) || !File.Exists(trackPath))
        {
            MessageBox.Show(this, GetLocalizedString(!File.Exists(dataPath) ? "NoMaidata_txt" : "NoTrack"), GetLocalizedString("Error"));
            return;
        }
        var reuseWave = document != null && document.AudioPath.Equals(trackPath, StringComparison.OrdinalIgnoreCase) &&
            document.AudioModified == File.GetLastWriteTimeUtc(trackPath);
        using var audio = preparedAudio ?? EditorSongAudio.Prepare(trackPath, editorAudioDevice, audioOutputUnavailable,
            cachedWaveforms: reuseWave ? document!.Waveforms : null, cachedLength: reuseWave ? document!.AudioLength : 0);
        switchingChartDocument = true;
        var reopenSound = toolTabs.ContainsKey(typeof(SoundSetting));
        var selectedTool = toolSidebar?.SelectedItem;
        try
        {
            if (!installingRoomSong && chartRoom?.IsHost != true)
            {
                chartRoom?.Close();
                if (chartRoom != null) return;
            }
            FlushSongTools();
            CaptureChartDocument();
            SaveSetting();
            if (!installingRoomSong)
            {
                guestChartPendingSave = document?.GuestPendingSave ?? false;
                guestSavePath = document?.GuestSavePath;
            }
            roomChartNeedsReload = false;
            StopChartList();
            if (isPlaying || lastEditorState != EditorControlMethod.Stop || flowPreviewActive) ToggleStop();
            soundSetting?.Close();
            MediaTimelinePanel_CloseRequested(MediaTimelinePanel, EventArgs.Empty);
            // Each song owns its AvalonEdit document, including undo history.
            isLoading = true;
            FumenContent.Document = new TextDocument();
            WebControl.ResetChartDiagnostics();
            muriSource = playedMuriSource = null;
            wasMuriPlaying = false;
            dynamicDiagnostics.Clear(); DynamicMuriCount.Content = 0;
            ClearBasicParseErrors();
            LoadChartFromFile(path, audio, installingRoomSong ? null : document);
            if (!string.Equals(maidataDir, path, StringComparison.OrdinalIgnoreCase) || selectedDifficulty < 0) return;
            if (document == null)
            {
                document = new ChartDocument { Directory = path };
                if (chartPage?.Tag is not ChartDocument) document.Tab = chartPage!;
                else
                {
                    document.Tab = CreateSidebarTab("", new Grid(), () => CloseChartDocument(document), null);
                    editorPages!.Items.Insert(chartDocuments.Count, document.Tab);
                }
                document.Tab.Tag = document;
                SidebarTabPanel.SetWidthMultiplier(document.Tab, 1.5);
                document.Caption = ((Panel)document.Tab.Header).Children.OfType<TextBlock>().Single();
                chartDocuments.Add(document);
            }
            activeChartDocument = document;
            chartPage = document.Tab; chartPageTitle = document.Caption;
            document.AudioPath = trackPath; document.AudioModified = File.GetLastWriteTimeUtc(trackPath);
            document.Waveforms = audio.Waveforms; document.AudioLength = audio.Length;
            CaptureChartDocument();
            updatingEditorPage = true;
            editorPages!.SelectedItem = document.Tab;
            updatingEditorPage = false;
            UpdateWaveformPane(false);
            QueueNotePreview(chartChanged: true);
            if (reopenSound)
            {
                MenuItem_SoundSetting_Click(this, new RoutedEventArgs());
                if (selectedTool != null && toolSidebar!.Items.Contains(selectedTool)) toolSidebar.SelectedItem = selectedTool;
            }
        }
        finally { isLoading = false; switchingChartDocument = false; }
    }

    private void RestoreChartDocumentEditor(ChartDocument document)
    {
        FumenContent.Document = document.Text;
        FumenContent.Select(Math.Min(document.SelectionStart, document.Text.TextLength),
            Math.Min(document.SelectionLength, document.Text.TextLength - Math.Min(document.SelectionStart, document.Text.TextLength)));
        FumenContent.CaretOffset = Math.Min(document.Caret, document.Text.TextLength);
        FumenContent.ScrollToVerticalOffset(document.Scroll);
        openedChartFile = Path.Combine(document.Directory, "maidata.txt");
        openedChartText = document.DiskText;
        SetTimelinePosition(document.Time);
    }

    private void CloseChartDocument(ChartDocument document)
    {
        if (switchingChartDocument) return;
        if (recordVideoWindow?.IsRecording == true || gifExportWindow?.IsRecording == true || activeChartExport != null)
        {
            MessageBox.Show(this, "请先停止录制或批量导出，再关闭谱面。", "关闭谱面");
            return;
        }
        var previouslyActive = activeChartDocument;
        if (document != activeChartDocument) InstallChartFromFile(document.Directory, null);
        if (document != activeChartDocument) return;
        FlushSongTools();
        if (!isSaved && !AskSave()) return;
        var index = chartDocuments.IndexOf(document);
        chartDocuments.Remove(document);
        activeChartDocument = null;
        updatingEditorPage = true;
        editorPages!.Items.Remove(document.Tab);
        updatingEditorPage = false;
        if (chartDocuments.Count > 0)
        {
            var next = previouslyActive != document && previouslyActive != null && chartDocuments.Contains(previouslyActive)
                ? previouslyActive : chartDocuments[Math.Min(index, chartDocuments.Count - 1)];
            InstallChartFromFile(next.Directory, null);
            return;
        }
        // Keep the empty placeholder used by the existing no-song layout.
        chartPage = CreateSidebarTab("", new Grid(), CloseChartPage, null);
        chartPageTitle = ((Panel)chartPage.Header).Children.OfType<TextBlock>().Single();
        chartPage.Visibility = Visibility.Collapsed;
        editorPages.Items.Add(chartPage);
        isSaved = true;
        CloseChartPage();
    }

    private bool ConfirmClosingChartDocuments()
    {
        FlushSongTools();
        CaptureChartDocument();
        foreach (var document in chartDocuments.ToArray())
        {
            if (document.Saved) continue;
            if (document != activeChartDocument) InstallChartFromFile(document.Directory, null);
            if (document != activeChartDocument || !AskSave()) return false;
        }
        return chartDocuments.Count > 0 || isSaved || AskSave();
    }
}
