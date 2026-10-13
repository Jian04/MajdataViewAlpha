using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using MajdataCollaboration;
using MajdataSessionUi;

namespace MajdataEdit;

public partial class MainWindow
{
    private ChartRoomWindow? chartRoom;
    private bool installingRoomSong;
    private bool roomChartNeedsReload;
    private bool guestChartPendingSave;
    private string? guestSavePath;
    private bool SaveGuestChart()
    {
        if (!guestChartPendingSave && chartRoom?.IsGuest != true) return true;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "谱面文本 (*.txt)|*.txt", FileName = "maidata.txt", DefaultExt = ".txt", AddExtension = true };
        if (!string.IsNullOrEmpty(guestSavePath)) { dialog.InitialDirectory = Path.GetDirectoryName(guestSavePath); dialog.FileName = Path.GetFileName(guestSavePath); }
        if (dialog.ShowDialog(this) != true) return false;
        File.WriteAllText(dialog.FileName, CaptureRoomText());
        guestSavePath = dialog.FileName; guestChartPendingSave = false; SetSavedState(true);
        return true;
    }
    private Editor.RemoteCaretRenderer? remoteCarets;
    private readonly CancellationTokenSource roomHandoffStop = new();
    private void ChartRoom_Click(object sender, RoutedEventArgs e)
    {
        try { OpenChartRoom(); }
        catch (Exception ex)
        {
            WriteRoomError(ex);
            MessageBox.Show(ex.Message, "联机写谱");
        }
    }
    private ChartRoomWindow OpenChartRoom()
    {
        if (chartRoom != null) { ShowTool(chartRoom, "联机写谱"); return chartRoom; }
        var window = new ChartRoomWindow(CaptureRoomText, ApplyRoomText, BackupRoomText) { Owner = this };
        window.IsApplicationClosing = () => WebControl.IsShuttingDown;
        window.IdentityChanged = color => { roomIdentityColor = color; ApplyInstanceIdentity(); };
        window.GuestDisconnected = () => { if (WebControl.IsShuttingDown) return; guestChartPendingSave = true; if (MessageBox.Show("是否保存当前联机谱面？", "已断开联机", MessageBoxButton.YesNo) == MessageBoxResult.Yes) SaveGuestChart(); };
        window.HostRoomClosed = () =>
        {
            guestChartPendingSave = true;
            if (!WebControl.IsShuttingDown && MessageBox.Show(this,
                "房主已关闭房间，联机已断开。是否保存当前谱面？", "房间已关闭",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) SaveGuestChart();
        };
        window.BeforeConnect = () =>
        {
            if (selectedDifficulty < 0) return;
            if (!isSaved && !AskSave()) throw new OperationCanceledException("已取消连接。");
        };
        window.BeforeSongSwitch = () =>
        {
            var choice = MessageBox.Show("房主已切换歌曲。是否先保存当前谱面？", "联机切换歌曲",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return choice switch
            {
                MessageBoxResult.Yes => SaveGuestChart(),
                MessageBoxResult.No => true,
                _ => false
            };
        };
        window.MediaRoot = () => maidataDir;
        window.MediaReuseFolders = () => new[] { maidataDir }
            .Concat(ChartListStore.Read().Select(entry => entry.Folder))
            .Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        window.SelectDifficulty = SelectRoomDifficulty;
        window.InstallSongAsync = async (folder, text, cancellation) =>
        {
            var audioPath = MediaTools.FindCachedTimelineAudio(folder) ??
                Path.Combine(folder, File.Exists(Path.Combine(folder, "track.ogg")) ? "track.ogg" : "track.mp3");
            using var audio = await Task.Run(() => EditorSongAudio.Prepare(audioPath,
                editorAudioDevice, audioOutputUnavailable, cancellation), cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(folder, "maidata.txt"), text);
            installingRoomSong = true;
            try { InstallChartFromFile(folder, audio); guestSavePath = null; guestChartPendingSave = true; SetSavedState(false); }
            finally { installingRoomSong = false; }
        };
        if (remoteCarets == null)
        {
            remoteCarets = new Editor.RemoteCaretRenderer(FumenContent, () => selectedDifficulty);
            FumenContent.TextArea.TextView.BackgroundRenderers.Add(remoteCarets);
        }
        window.CapturePresence = () => new RoomPresence("", window.DisplayName, selectedDifficulty, FumenContent.CaretOffset);
        window.ShowPresence = peers => remoteCarets.Update(peers);
        EventHandler changed = async (_, _) => { if (!isLoading) await window.SyncNow(); };
        FumenContent.TextChanged += changed;
        window.Closed += (_, _) => FumenContent.TextChanged -= changed;
        window.Closed += (_, _) => { if (ReferenceEquals(chartRoom, window)) chartRoom = null; };
        // Failed initialization must not leave a cached window that cannot reopen.
        try { ShowTool(window, "联机写谱"); }
        catch { window.Close(); throw; }
        chartRoom = window;
        return window;
    }
    private static void WriteRoomError(Exception error)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "collaboration-error.log"),
                $"[{DateTime.Now:O}]{Environment.NewLine}{error}{Environment.NewLine}");
        }
        catch { /* Preserve the original room error when logging is unavailable. */ }
    }
    private string CaptureRoomText()
    {
        if (selectedDifficulty < 0) return "&title=\n&inote_5=\n";
        SimaiProcess.fumens[selectedDifficulty] = GetRawFumenText();
        if (SimaiProcess.TryReadOffset(OffsetTextBox.Text, out var offset)) SimaiProcess.first = offset;
        return SimaiProcess.ExportText();
    }
    private string BackupRoomText(string text)
    {
        var directory = Path.Combine(MajdataCore.AlphaSession.Root, "CollaborationRecovery", Process.GetCurrentProcess().Id.ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
        File.WriteAllText(path, text); return path;
    }
    private void ApplyRoomText(string text)
    {
        string previous = CaptureRoomText();
        if (text == previous) return;
        var previousFumen = GetRawFumenText();
        var previousOffset = SimaiProcess.first;
        int caret = FumenContent.CaretOffset;
        bool currentChartChanged = false;
        isLoading = true;
        try
        {
            SimaiProcess.ClearDocumentData();
            if (!SimaiProcess.ReadText(text))
            {
                SimaiProcess.ClearDocumentData(); SimaiProcess.ReadText(previous);
                throw new IOException("房间谱面无法读取，本地稿已保留。");
            }
            string next = SimaiProcess.fumens[selectedDifficulty] ?? "";
            currentChartChanged = previousFumen.Trim() != next.Trim() || previousOffset != SimaiProcess.first;
            // Synchronization edits the document only. View keeps its loaded
            // chart and Edit keeps the sound schedule until the user plays again.
            if (previousFumen.Trim() != next.Trim())
            {
                SetRawFumenText(next);
                FumenContent.CaretOffset = Math.Min(caret, FumenContent.Document.TextLength);
            }
            suppressLevelTextChange = true;
            LevelTextBox.Text = SimaiProcess.levels[selectedDifficulty] ?? "";
            OffsetTextBox.Text = SimaiProcess.first.ToString(CultureInfo.InvariantCulture);
        }
        finally { suppressLevelTextChange = false; isLoading = false; }
        // Parse the editor document without sending commands to the running View.
        if (!isPlaying || currentChartChanged) SimaiProcess.Serialize(GetRawFumenText());
        roomChartNeedsReload |= currentChartChanged;
        SetSavedState(false); chartParsePending = false;
        QueueImmediateWaveRefresh(); chartChangeTimer.Stop(); chartChangeTimer.Start();
    }
    private void SelectRoomDifficulty(int difficulty)
    {
        // Initial synchronization is not a user-requested difficulty switch;
        // do not open an unrelated Save dialog for the freshly installed song.
        var loading = isLoading;
        isLoading = true;
        suppressLevelTextChange = true;
        try
        {
            LevelSelector.SelectedIndex = difficulty;
            selectedDifficulty = difficulty;
            SetRawFumenText(SimaiProcess.fumens[difficulty] ?? "");
            LevelTextBox.Text = SimaiProcess.levels[difficulty] ?? "";
            SimaiProcess.Serialize(GetRawFumenText());
            chartParsePending = false;
            roomChartNeedsReload = true;
        }
        finally { suppressLevelTextChange = false; isLoading = loading; }
        QueueImmediateWaveRefresh();
        QueueNotePreview(chartChanged: true);
    }
    private async void StartRoomHandoff()
    {
        while (!roomHandoffStop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(RoomHandoff.PipeName(Environment.ProcessId), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(roomHandoffStop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(roomHandoffStop.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(30));
                var request = await RoomWire.Read<JoinRoomRequest>(pipe, timeout.Token);
                string result;
                try
                {
                    var room = OpenChartRoom(); room.DisplayName = request.Name;
                    await room.Join(request.Address, request.Password);
                    Activate(); result = "已连接，谱面同步状态显示在所选 Edit 的联机窗口。";
                }
                catch (Exception ex) { WriteRoomError(ex); result = "连接失败：" + ex.Message; }
                await RoomWire.Write(pipe, result, timeout.Token);
            }
            catch (OperationCanceledException) when (roomHandoffStop.IsCancellationRequested) { break; }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }
    }
}
