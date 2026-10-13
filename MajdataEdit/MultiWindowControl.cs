using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using MajdataCollaboration;
using MajdataCore;
using MajdataSessionUi;
using Un4seen.Bass;

namespace MajdataEdit;

public partial class MainWindow
{
    private MultiControlWindow? multiControlWindow;
    private string? multiBatch, multiFingerprint, multiPreparedKind;
    private int multiGeneration;
    private float multiPosition;
    private bool multiIncludeMusic = true;
    private bool multiAudioMuted;
    private float MultiMediaVolume => multiBatch != null && !multiIncludeMusic &&
        multiGeneration == viewControlGeneration ? 0f : editorSetting?.Default_BGM_Level ?? 1f;
    private bool multiPreparing, multiRecording, multiSawRecording;
    private string multiOutput = "", multiMessage = "";
    private double multiIntroDuration;
    private DateTime multiRecordStarted;
    private long multiRecordGeneration;
    private MultiRect? multiRestoreRectangle;
    private RecordVideoOptions? multiRecordOptions;
    private readonly System.Windows.Threading.DispatcherTimer multiStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private void MultiControl_Click(object sender, RoutedEventArgs e)
    {
        AlphaSession.EnableIdentities();
        ApplyInstanceIdentity();
        if (multiControlWindow != null) { ShowTool(multiControlWindow, "多窗口录制"); return; }
        multiControlWindow = new MultiControlWindow();
        multiControlWindow.Closed += (_, _) => multiControlWindow = null;
        ShowTool(multiControlWindow, "多窗口录制");
    }
    private string MultiFingerprint() => maidataDir + "\n" + selectedDifficulty + "\n" + CaptureRoomText();
    private Task<bool> SendMultiOrdered(Func<bool> send)
    {
        var task = Task.Run(send);
        pendingPlaySend = task;
        return task;
    }
    private GroupReply MultiStatus(bool ok = true, string? message = null)
    {
        long recordGeneration = -1;
        var state = WebControl.ViewSession?.ReadViewRecording(out recordGeneration);
        if (multiRecording && File.Exists(multiOutput + ".done"))
        {
            multiSawRecording = true;
            state = false;
        }
        if (multiRecording)
        {
            multiSawRecording |= state == true || recordGeneration > multiRecordGeneration;
            if (multiSawRecording && state == false)
            {
                multiRecording = false; FinishRecordRun(); pendingRecordOptions = null;
                multiMessage = state == false && File.Exists(multiOutput) ? "录制结束" : "View 失去响应，请检查输出";
                if (state == false) _ = RestoreMultiWindow(false);
            }
        }
        var rectangle = new MultiRect();
        using var view = WebControl.ViewSession?.GetProcess(true);
        if (view?.MainWindowHandle is { } handle && handle != IntPtr.Zero) GetWindowRect(handle, out rectangle);
        return new(ok, message ?? (multiPreparing ? "准备中" : multiRecording ? "录制中" : multiMessage),
            SimaiProcess.title ?? "未打开谱面", isPlaying, multiRecording || state == true,
            selectedDifficulty >= 0 && state.HasValue && view != null,
            selectedDifficulty >= 0 ? GetTimelinePosition() : 0, multiOutput,
            rectangle.Left, rectangle.Top, Math.Max(200, rectangle.Right - rectangle.Left), Math.Max(200, rectangle.Bottom - rectangle.Top),
            maidataDir, ResolveGroupFfmpeg(),
            string.IsNullOrEmpty(maidataDir) ? "" : AlphaSession.ChartWorkFile(maidataDir, "out.wav"), editorSetting?.ShowSongDetail == true ? 5 : 0, queueNaturalEnd,
            selectedDifficulty >= 0 ? songLength : 0);
    }
    private static string ResolveGroupFfmpeg()
    {
        try { return MediaTools.FindFfmpeg(); }
        catch (FileNotFoundException) { return ""; }
    }
    private async void StartGroupControl()
    {
        multiStatusTimer.Tick += (_, _) => { ApplyInstanceIdentity(); if (multiRecording) MultiStatus(); };
        multiStatusTimer.Start();
        Closed += (_, _) => { multiStatusTimer.Stop(); multiControlWindow?.Close(); };
        while (!roomHandoffStop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(GroupControl.PipeName(Environment.ProcessId), PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(roomHandoffStop.Token);
                _ = HandleGroupPipe(pipe); pipe = null;
            }
            catch (OperationCanceledException) when (roomHandoffStop.IsCancellationRequested) { break; }
            catch (Exception ex) { Debug.WriteLine(ex); await Task.Delay(500); }
            finally { pipe?.Dispose(); }
        }
    }
    private async Task HandleGroupPipe(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(roomHandoffStop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                var command = await RoomWire.Read<GroupCommand>(pipe, timeout.Token);
                GroupReply result;
                try
                {
                    GroupControl.Validate(command);
                    using var process = Process.GetCurrentProcess();
                    if (AlphaSession.Current == null || command.Session != AlphaSession.Current.Id || command.ProcessStart != process.StartTime.ToUniversalTime().Ticks)
                        throw new InvalidOperationException("目标实例已变化，请刷新列表。");
                    result = await HandleGroupCommand(command);
                }
                catch (Exception e) { result = MultiStatus(false, e.Message); }
                await RoomWire.Write(pipe, result, timeout.Token);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
    }
    private async Task<GroupReply> HandleGroupCommand(GroupCommand command)
    {
        if (command.Action == "queue-play")
        {
            if (!IsBatchWorker && chartQueueStop == null)
                throw new InvalidOperationException("当前没有正在运行的谱面队列。");
            queueNaturalEnd = false;
            if (command.Seconds == 0) SetTimelinePosition(GetTimelineMinimum());
            if (!isPlaying) TogglePlay();
            return MultiStatus(isPlaying, isPlaying ? "正在播放" : "未能开始播放，请检查音频设备、View 和谱面语法。");
        }
        if (command.Action is "batch-open" or "batch-close")
        {
            if (!IsBatchWorker && (command.Action == "batch-close" || chartQueueStop == null))
                throw new InvalidOperationException("当前没有正在运行的谱面队列。");
            if (command.Action == "batch-close")
            {
                CancelRecordRun(); batchWorkerClosing = true;
                Dispatcher.BeginInvoke(new Action(Close));
                return new(true, "批量实例已停止");
            }
            if (!editorUiReady) return new(false, "Edit 正在启动");
            if (command.Difficulty < 0 || command.Difficulty > 6) throw new ArgumentException("无效难度。");
            // A next/previous request can arrive while the old View run is still
            // loading. Drain it and wait for the Stop acknowledgement before
            // opening the next song, or that late run can redraw the old chart.
            ToggleStop();
            if (pendingPlaySend != null) await pendingPlaySend;
            if (!await Task.Run(sendRequestStop)) throw new IOException("View 未确认停止旧谱面。");
            if (!await OpenChartListEntry(ChartListStore.Inspect(command.ChartDirectory, command.Difficulty), queued: true))
                throw new IOException("无法加载批量谱面。");
            CheckAndStartView();
            return MultiStatus();
        }
        if (command.Action is "dock" or "dock-probe" or "dock-ready" or "dock-startup" or "dock-complete") return DockPet(command);
        if (command.Action == "status") return MultiStatus();
        if (command.Action is "stop" or "abort")
        {
            if (command.Action == "abort" && (command.Batch != multiBatch || multiGeneration != viewControlGeneration)) return MultiStatus(true, "批次已失效");
            multiPreparedKind = null; multiBatch = null; multiMessage = "已停止";
            ToggleStop(); multiRecording = false; pendingRecordOptions = null;
            _ = RestoreMultiWindow(false);
            return MultiStatus();
        }
        var status = MultiStatus();
        if (multiPreparing) throw new InvalidOperationException("本组正在准备，请等待或停止。");
        if (status.Recording && command.Action != "commit-record") throw new InvalidOperationException("本组正在录制，请先停止录制再播放、暂停或调整窗口。");
        if (!status.Ready) throw new InvalidOperationException("请打开谱面，并启动当前 Alpha 构建的配对 View（需支持控制台状态）。");
        if (command.Action.StartsWith("check-"))
        {
            if (command.Action == "check-play" && !RestoreAudioOutput()) throw new InvalidOperationException("音频输出设备不可用。");
            if (editorSetting?.SyntaxCheckLevel == 2 && latestParseErrors.Count > 0) throw new InvalidOperationException("谱面存在语法错误，请先在 Edit 中修复。");
            if (command.Action == "check-record" && File.Exists(Path.Combine(maidataDir, command.FileName))) throw new IOException("录制文件已存在，请重新发起批次。");
            return MultiStatus(true, "检查通过");
        }
        if (command.Action == "pause")
        {
            multiPreparedKind = null;
            if (command.StartUtc > DateTime.UtcNow.Ticks)
                await Task.Delay(TimeSpan.FromTicks(command.StartUtc - DateTime.UtcNow.Ticks));
            if (isPlaying) TogglePause();
            return MultiStatus(true, "已暂停");
        }
        if (command.Action == "seek")
        {
            if (command.Seconds < GetTimelineMinimum()) throw new ArgumentException("跳转时间早于开场起点。");
            ToggleStop(); SetTimelinePosition(command.Seconds); multiPreparedKind = null;
            return MultiStatus(true, "已跳转");
        }
        if (command.Action == "layout")
        {
            using var view = AlphaSession.Current!.GetProcess(true);
            if (view == null || view.MainWindowHandle == IntPtr.Zero ||
                !GetWindowRect(view.MainWindowHandle, out var outer) || !GetClientRect(view.MainWindowHandle, out var client) ||
                !MoveWindow(view.MainWindowHandle, command.X, command.Y,
                    command.Width + outer.Right - outer.Left - client.Right,
                    command.Height + outer.Bottom - outer.Top - client.Bottom, true))
                throw new InvalidOperationException("View 窗口尚未就绪。");
            return MultiStatus(true, "布局已应用");
        }
        if (command.Action.StartsWith("prepare-"))
        {
            multiPreparing = true; multiBatch = command.Batch; multiPreparedKind = null;
            var readOnly = FumenContent.IsReadOnly;
            FumenContent.IsReadOnly = true;
            try
            {
                multiIntroDuration = command.IntroDuration;
                multiIncludeMusic = command.IncludeMusic;
                // Every participant receives the same explicit timeline position.
                // Reading each editor's local caret here made one-click unified
                // playback start several seconds apart when their carets differed.
                multiPosition = (float)Math.Clamp(command.Action == "prepare-record" ? 0 : command.Seconds == 0 ? -multiIntroDuration : command.Seconds, -multiIntroDuration, songLength);
                ToggleStop(); multiGeneration = viewControlGeneration;
                var oldPlay = pendingPlaySend; var oldPreview = pendingNotePreviewSend;
                if (oldPlay != null) await oldPlay;
                if (oldPreview != null) await oldPreview;
                if (multiGeneration != viewControlGeneration) throw new InvalidOperationException("准备已取消。");
                if (!await Task.Run(sendRequestStop)) throw new IOException("View 未能停止旧谱面。");
                EnsureTimelineAudioReady(); SetTimelinePosition(multiPosition); SaveFumen();
                SimaiProcess.Serialize(GetRawFumenText());
                if (!ValidateMediaFiles()) throw new IOException("请补齐本地媒体素材。");
                multiFingerprint = MultiFingerprint();
                generateSoundEffectList(multiPosition, command.Action == "prepare-record");
                if (command.Action == "prepare-record")
                {
                    using (var view = AlphaSession.Current?.GetProcess(true))
                        if (view != null && GetWindowRect(view.MainWindowHandle, out var rectangle)) multiRestoreRectangle = rectangle;
                    multiRecordOptions = new() { FrameRate = command.Fps, Width = command.Width, Height = command.Height, FileName = command.FileName, RevealOutput = false };
                    // All participants use the same frame-zero and intro duration.
                    await Task.Run(() => renderSoundEffect(multiIntroDuration, command.IncludeMusic));
                    pendingRecordOptions = multiRecordOptions;
                    multiOutput = Path.Combine(AlphaSession.Root, "RecordBatches", command.Batch, multiRecordOptions.FileName);
                    multiRecordStarted = DateTime.UtcNow; multiSawRecording = false; multiRecording = true;
                    AlphaSession.Current!.ReadViewRecording(out multiRecordGeneration);
                    var method = command.Fps == 120 ? PlayMethod.Record120 : PlayMethod.Record60;
                    if (!await SendMultiOrdered(() => sendRequestRun(DateTime.Now.AddSeconds(multiIntroDuration), method, 0, controlGeneration: multiGeneration))) throw new IOException("View 未接受录制准备。");
                    var readyUntil = DateTime.UtcNow.AddSeconds(120);
                    while (!File.Exists(multiOutput + ".ready"))
                    {
                        if (multiGeneration != viewControlGeneration || DateTime.UtcNow > readyUntil || AlphaSession.Current.ReadViewRecording() == false) throw new IOException("View 录制准备未完成。");
                        await Task.Delay(50);
                    }
                }
                else if (!await SendMultiOrdered(() => sendRequestRun(DateTime.Now, PlayMethod.Normal, multiPosition, controlGeneration: multiGeneration)))
                    throw new IOException("View 加载谱面失败。");
                if (multiGeneration != viewControlGeneration || multiFingerprint != MultiFingerprint()) throw new InvalidOperationException("准备期间谱面或播放状态已改变，请重新发起。");
                multiPreparedKind = command.Action.Substring("prepare-".Length);
                return MultiStatus(true, "已准备，等待其他组");
            }
            finally { multiPreparing = false; FumenContent.IsReadOnly = readOnly; }
        }
        if (command.Action.StartsWith("commit-"))
        {
            string kind = command.Action.Substring("commit-".Length);
            if (command.Batch != multiBatch || kind != multiPreparedKind || multiGeneration != viewControlGeneration || multiFingerprint != MultiFingerprint())
                throw new InvalidOperationException("准备状态已失效，请重新发起。");
            multiPreparedKind = null;
            var start = new DateTime(command.StartUtc, DateTimeKind.Utc);
            if (start < DateTime.UtcNow.AddMilliseconds(100) || start > DateTime.UtcNow.AddSeconds(10)) throw new InvalidOperationException("统一开始时间已过期，请重试。");
            isPlaying = true; Op_Button.IsEnabled = false;
            if (kind == "play")
            {
                if (!await SendMultiOrdered(() => sendRequestContinue(start.ToLocalTime(), multiPosition, multiGeneration, showIntroOverride: multiIntroDuration > 0))) throw new IOException("View 未接受统一开始时间。");
                _ = StartMultiAudioAt(start, multiGeneration, multiPosition);
                multiMessage = "统一播放";
            }
            else
            {
                File.WriteAllText(multiOutput + ".start.tmp", command.StartUtc.ToString(CultureInfo.InvariantCulture));
                File.Move(multiOutput + ".start.tmp", multiOutput + ".start", true);
                multiMessage = "录制中";
                _ = RestoreMultiWindow(true);
            }
            return MultiStatus(true, kind == "play" ? "统一播放已安排" : "录制已开始");
        }
        throw new ArgumentException("不支持的控制指令。");
    }
    private async Task StartMultiAudioAt(DateTime start, int generation, float position)
    {
        await Task.Delay(Math.Max(0, (int)(start - DateTime.UtcNow).TotalMilliseconds));
        if (generation != viewControlGeneration || !isPlaying) return;
        var elapsed = Math.Max(0d, (DateTime.UtcNow - start).TotalSeconds * GetPlaybackSpeed());
        multiAudioMuted = !multiIncludeMusic;
        var bgmVolume = multiIncludeMusic ? editorSetting?.Default_BGM_Level ?? 1f : 0f;
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, bgmVolume);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, bgmVolume);
        if (position < 0 && position + elapsed < 0)
        {
            flowPreviewActive = true; flowPreviewAwaitingView = false;
            flowPreviewStartTime = position; flowPreviewStartedAt = start.ToLocalTime();
            visualEffectRefreshTimer.Start();
            // If Edit's UI thread wakes late, skip the elapsed intro audio. View
            // already follows the shared UTC anchor and must not wait for us.
            Bass.BASS_ChannelSetPosition(trackStartStream, elapsed);
            Bass.BASS_ChannelPlay(trackStartStream, false);
            var songStart = start.AddSeconds(-position / GetPlaybackSpeed());
            await Task.Delay(Math.Max(0, (int)(songStart - DateTime.UtcNow).TotalMilliseconds));
            if (generation != viewControlGeneration || !isPlaying) return;
            flowPreviewActive = false; flowTimelineCursor = null;
        }
        var songPosition = Math.Clamp(position +
            (DateTime.UtcNow - start).TotalSeconds * GetPlaybackSpeed(), 0d, songLength);
        flowPreviewActive = false; flowTimelineCursor = null;
        Bass.BASS_ChannelSetPosition(bgmStream, songPosition);
        playStartTime = songPosition;
        SimaiProcess.ClearNoteListPlayedState(); StartSELoop();
        waveStopMonitorTimer.Start(); visualEffectRefreshTimer.Start();
        PlayAndPauseButton.Content = " ▌▌"; TimelinePlayAndPauseButton.Content = " ▌▌";
        Bass.BASS_ChannelPlay(bgmStream, false);
    }
    private async Task RestoreMultiWindow(bool positionOnly)
    {
        if (multiRestoreRectangle is not { } rectangle) return;
        int generation = viewControlGeneration;
        if (!positionOnly)
        {
            for (int wait = 0; wait < 50; wait++)
            {
                if (generation != viewControlGeneration || multiRecording) return;
                if (AlphaSession.Current?.ReadViewRecording() == false) break;
                await Task.Delay(200);
                if (wait == 49) return;
            }
        }
        // Unity applies SetResolution on a later frame and may re-centre its
        // native window. Preserve each participant's own pre-record placement.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(400);
            if (generation != viewControlGeneration || multiRecording != positionOnly) return;
            using var view = AlphaSession.Current?.GetProcess(true);
            if (view == null || view.MainWindowHandle == IntPtr.Zero) return;
            SetWindowPos(view.MainWindowHandle, IntPtr.Zero, rectangle.Left, rectangle.Top,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                0x0004 | 0x0010 | (positionOnly ? 0x0001u : 0u));
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MultiRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out MultiRect rectangle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out MultiRect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
