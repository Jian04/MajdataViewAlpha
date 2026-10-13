using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using MajdataCollaboration;
using MajdataCore;
using Newtonsoft.Json;

namespace MajdataEdit;

public partial class MainWindow
{
    private ChartListPanel? chartListPanel;
    private CancellationTokenSource? chartQueueStop;
    private TaskCompletionSource<bool>? chartQueueStopped;
    private TaskCompletionSource<bool>? chartQueueCompletion;
    private static bool IsBatchWorker => Environment.GetCommandLineArgs().Contains("--batch-worker");
    private bool batchWorkerClosing;
    private CancellationTokenSource? activeChartExport;
    private bool closeAfterChartExport;
    private void ChartList_Click(object sender, RoutedEventArgs e) => ShowChartList();
    private void ShowChartList()
    {
        if (chartListPanel == null)
        {
            chartListPanel = new ChartListPanel(this);
            chartListPanel.Closed += (_, _) => chartListPanel = null;
        }
        chartListPanel.Refresh(); ShowTool(chartListPanel);
    }
    internal void AddCurrentToChartList()
    {
        if (string.IsNullOrEmpty(maidataDir)) throw new InvalidOperationException("请先打开谱面。");
        if (!isSaved && !AskSave()) return;
        AddFolderToChartList(maidataDir, selectedDifficulty);
    }
    private void AddFolderToChartList(string folder, int? difficulty = null)
    {
        ChartListStore.Add(difficulty.HasValue ? new[] { ChartListStore.Inspect(folder, difficulty) } : ChartListStore.InspectAll(folder));
        ShowChartList(); chartListPanel!.SetStatus("已加入谱面列表。");
    }
    internal async Task<bool> OpenChartListEntry(ChartListEntry entry, bool queued = false)
    {
        if (!queued) await StopChartListAsync();
        if (!queued && !isSaved && !AskSave()) return false;
        entry = ChartListStore.Inspect(entry.Folder, entry.Difficulty);
        if (!queued) ToggleStop();
        initFromFile(entry.Folder);
        if (!string.Equals(maidataDir, entry.Folder, StringComparison.OrdinalIgnoreCase)) return false;
        LevelSelector.SelectedIndex = entry.Difficulty;
        SetTimelinePosition(0); FumenContent.CaretOffset = 0;
        return true;
    }
    private GroupTarget? queueTarget;
    private bool queuePaused;
    private bool closeAfterQueue;
    private bool queueNaturalEnd;
    private int queueStep;
    internal void StepChartList(int step)
    {
        if (chartQueueStop != null) Interlocked.Exchange(ref queueStep, step < 0 ? -1 : 1);
    }
    internal void StopChartList() => chartQueueStop?.Cancel();
    private async Task StopChartListAsync()
    {
        var stopped = chartQueueStopped;
        StopChartList();
        if (stopped != null && !await stopped.Task)
            throw new IOException("播放队列未能完全退出，请检查 View 连接后重试。");
    }
    internal bool ChartListIsPlaying => chartQueueStop != null;
    internal bool ChartListIsPaused => queuePaused;
    internal async Task<GroupReply?> ChartListProgress() => queueTarget == null ? null :
        await GroupControl.Send(queueTarget, new());
    internal async Task SeekChartList(double seconds)
    {
        if (queueTarget == null || chartQueueStop == null) return;
        var reply = await GroupControl.Send(queueTarget, new() { Action = "seek", Seconds = seconds });
        if (!reply.Ok) { chartListPanel?.SetStatus(reply.Message); return; }
        queuePaused = true;
        chartListPanel?.UpdateTransport();
    }
    internal async void PauseChartList() => await PauseChartListForSeek();
    internal async Task PauseChartListForSeek()
    {
        if (queueTarget == null || chartQueueStop == null) return;
        var reply = await GroupControl.Send(queueTarget, new() { Action = "pause" });
        if (reply.Ok) queuePaused = true;
        chartListPanel?.SetStatus(reply.Ok ? "队列已暂停，点击播放继续。" : reply.Message);
        chartListPanel?.UpdateTransport();
    }
    internal void ToggleChartListPlayback(int mode)
    {
        if (ChartListIsPlaying && !ChartListIsPaused) PauseChartList();
        else PlayChartList(mode);
    }
    internal async void PlayChartList(int mode)
    {
        if (chartQueueStop != null)
        {
            if (queuePaused && queueTarget != null)
            {
                var reply = await GroupControl.Send(queueTarget, new() { Action = "queue-play" });
                if (reply.Ok) queuePaused = false;
                chartListPanel?.SetStatus(reply.Ok ? "继续播放。" : reply.Message);
                chartListPanel?.UpdateTransport();
            }
            return;
        }
        var queue = ChartListStore.Read();
        if (queue.Count == 0) { chartListPanel?.SetStatus("谱面列表为空。"); return; }
        if (mode == 1)
            for (var i = queue.Count - 1; i > 0; i--) { var j = Random.Shared.Next(i + 1); (queue[i], queue[j]) = (queue[j], queue[i]); }
        using var stop = new CancellationTokenSource(); chartQueueStop = stop;
        var stopped = chartQueueStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        queueStep = 0;
        chartListPanel?.UpdateTransport();
        AlphaSession? workerSession = null;
        Process? workerProcess = null;
        try
        {
            if (FindMajdataViewExecutable() == null) throw new FileNotFoundException("找不到配套 MajdataView.exe，请在完整发布目录中运行。");
            CheckAndStartView();
            if (AlphaSession.Current == null) throw new IOException("当前 Edit 没有可用的 View 会话。");
            ToggleStop();
            if (pendingPlaySend != null) await pendingPlaySend;
            if (pendingNotePreviewSend != null) await pendingNotePreviewSend;
            // Supersede any ordered Stop queued by the ordinary transport.
            viewControlGeneration++;
            if (!await Task.Run(sendRequestStop)) throw new IOException("View 未确认停止当前谱面。");
            stop.Token.ThrowIfCancellationRequested();
            workerSession = AlphaSession.Create();
            File.WriteAllText(Path.Combine(workerSession.DirectoryPath, "EditorSetting.json"), JsonConvert.SerializeObject(editorSetting));
            workerProcess = workerSession.StartProcess(Path.Combine(AppContext.BaseDirectory, "MajdataEdit.exe"), false, true, AlphaSession.Current.Id);
            queueTarget = new(workerSession.Id, workerProcess.Id, workerProcess.StartTime.ToUniversalTime().Ticks);
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                stop.Token.ThrowIfCancellationRequested();
                chartListPanel?.SetStatus($"准备 {i + 1}/{queue.Count} · {queue[i].Title}");
                GroupReply reply = new(false, "等待播放器初始化");
                var deadline = DateTime.UtcNow.AddSeconds(120);
                do
                {
                    if (workerProcess.HasExited || DateTime.UtcNow > deadline) throw new IOException("播放器启动失败：" + reply.Message);
                    await Task.Delay(300, stop.Token);
                    reply = await GroupControl.Send(queueTarget, new() { Action = "batch-open", ChartDirectory = queue[i].Folder, Difficulty = queue[i].Difficulty }).WaitAsync(stop.Token);
                } while (!reply.Ok && Volatile.Read(ref queueStep) == 0);
                while (reply.Ok && !reply.Ready && Volatile.Read(ref queueStep) == 0)
                {
                    if (DateTime.UtcNow > deadline) throw new IOException("配对 View 未就绪：" + reply.Message);
                    await Task.Delay(300, stop.Token);
                    reply = await GroupControl.Send(queueTarget, new()).WaitAsync(stop.Token);
                }
                var requestedStep = Interlocked.Exchange(ref queueStep, 0);
                if (requestedStep != 0)
                {
                    i = (i + requestedStep + queue.Count) % queue.Count;
                    continue;
                }
                queuePaused = false;
                reply = await GroupControl.Send(queueTarget, new() { Action = "queue-play", Seconds = 0 }).WaitAsync(stop.Token);
                if (!reply.Ok) throw new IOException(reply.Message);
                chartListPanel?.UpdateTransport();
                chartListPanel?.SetStatus($"播放 {i + 1}/{queue.Count} · {queue[i].Title} · {queue[i].Level}");
                do
                {
                    await Task.Delay(400, stop.Token);
                    if (Volatile.Read(ref queueStep) != 0) break;
                    reply = await GroupControl.Send(queueTarget, new()).WaitAsync(stop.Token);
                    if (!reply.Ok) throw new IOException(reply.Message);
                    if (!reply.Playing && !reply.Ended && !queuePaused) throw new IOException("播放已中断：" + reply.Message);
                } while (!reply.Ended);
                requestedStep = Interlocked.Exchange(ref queueStep, 0);
                if (requestedStep != 0)
                    i = (i + requestedStep + queue.Count) % queue.Count;
                else if ((chartListPanel?.PlaybackMode ?? mode) != 2)
                {
                    i++;
                    if (i == queue.Count)
                    {
                        i = 0;
                        if ((chartListPanel?.PlaybackMode ?? mode) == 1)
                            for (var n = queue.Count - 1; n > 0; n--)
                            { var j = Random.Shared.Next(n + 1); (queue[n], queue[j]) = (queue[j], queue[n]); }
                    }
                }
                queuePaused = false;
            }
        }
        catch (OperationCanceledException) { chartListPanel?.SetStatus("播放队列已停止。"); }
        catch (Exception e) { chartListPanel?.SetStatus("队列停止：" + e.Message); }
        finally
        {
            var cleanupSucceeded = false;
            try
            {
                if (queueTarget != null)
                    await GroupControl.Send(queueTarget, new() { Action = "batch-close" });
                if (workerProcess != null && !workerProcess.HasExited)
                {
                    try { await workerProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (TimeoutException)
                    {
                        workerProcess.Kill();
                        await workerProcess.WaitForExitAsync();
                    }
                }
                if (!await Task.Run(sendRequestStop)) throw new IOException("View 未确认结束队列。");
                cleanupSucceeded = true;
            }
            catch (Exception e) { chartListPanel?.SetStatus("结束队列失败：" + e.Message); }
            finally
            {
                workerProcess?.Dispose();
                chartQueueStop = null; queueTarget = null; queuePaused = false;
                try
                {
                    SendDisplaySettings();
                    QueueNotePreview(chartChanged: true);
                }
                finally
                {
                    chartQueueStopped = null;
                    stopped.TrySetResult(cleanupSucceeded);
                }
                chartListPanel?.UpdateTransport();
                if (closeAfterQueue) Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }
    internal void ShowBatchRecording()
    {
        if (!isSaved && !AskSave()) return;
        var entries = ChartListStore.Read().ToArray();
        if (entries.Length == 0) { chartListPanel?.SetStatus("请先加入谱面。"); return; }
        ShowTool(new BatchRecordVideoWindow(this, entries), "批量录制");
    }

    internal async Task ExportChartList(ChartListEntry[] entries, RecordVideoOptions options, int parallel,
        Action<string> progress, CancellationToken token)
    {
        if (activeChartExport != null) throw new InvalidOperationException("批量录制正在进行。");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        activeChartExport = stop;
        try { await ExportChartListCore(entries, options, parallel, progress, stop.Token); }
        finally
        {
            activeChartExport = null;
            if (closeAfterChartExport) Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private async Task ExportChartListCore(ChartListEntry[] entries, RecordVideoOptions options, int parallel,
        Action<string> progress, CancellationToken token)
    {
        if (FindMajdataViewExecutable() == null) throw new FileNotFoundException("请在完整的 Alpha 发布目录中运行批量录制。");
        var settings = JsonConvert.SerializeObject(editorSetting);
        var intro = editorSetting?.ShowSongDetail == true ? 5 : 0;
        using var slots = new SemaphoreSlim(parallel);
        var completed = 0; var failures = new List<string>();
        string? firstOutputFolder = null;
        await Task.WhenAll(entries.Select(async entry =>
        {
            await slots.WaitAsync(token);
            AlphaSession? session = null; GroupTarget? target = null;
            var batch = Guid.NewGuid().ToString("N");
            var work = Path.Combine(AlphaSession.Root, "RecordBatches", batch);
            try
            {
                token.ThrowIfCancellationRequested(); ChartListStore.Inspect(entry.Folder, entry.Difficulty);
                progress($"已完成 {completed}/{entries.Length} · 正在导出 {entry.Title}");
                session = AlphaSession.Create();
                File.WriteAllText(Path.Combine(session.DirectoryPath, "EditorSetting.json"), settings);
                using var process = session.StartProcess(Path.Combine(AppContext.BaseDirectory, "MajdataEdit.exe"), false, true);
                target = new(session.Id, process.Id, process.StartTime.ToUniversalTime().Ticks);
                GroupReply reply = new(false, "等待 Edit 启动");
                var readyBy = DateTime.UtcNow.AddSeconds(120);
                while (!reply.Ok)
                {
                    token.ThrowIfCancellationRequested();
                    if (process.HasExited || DateTime.UtcNow > readyBy) throw new IOException("Edit 启动失败：" + reply.Message);
                    await Task.Delay(500, token);
                    reply = await GroupControl.Send(target, new() { Action = "batch-open", ChartDirectory = entry.Folder, Difficulty = entry.Difficulty });
                }
                while (!reply.Ready)
                {
                    token.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > readyBy) throw new IOException("View 启动超时。");
                    await Task.Delay(500, token); reply = await GroupControl.Send(target, new());
                }
                var command = new GroupCommand { Batch = batch, FileName = "out.mp4", Fps = options.FrameRate,
                    Width = options.Width == 0 ? 720 : options.Width, Height = options.Height == 0 ? 720 : options.Height,
                    IntroDuration = intro, IncludeMusic = true };
                reply = await GroupControl.Send(target, command with { Action = "prepare-record" }).WaitAsync(token);
                if (!reply.Ok) throw new IOException(reply.Message);
                token.ThrowIfCancellationRequested();
                reply = await GroupControl.Send(target, command with { Action = "commit-record", StartUtc = DateTime.UtcNow.AddSeconds(2).Ticks });
                if (!reply.Ok) throw new IOException(reply.Message);
                var output = Path.Combine(work, "out.mp4");
                var finishBy = DateTime.UtcNow.AddHours(2);
                while (!File.Exists(output + ".done"))
                {
                    await Task.Delay(1000, token);
                    reply = await GroupControl.Send(target, new());
                    if (!reply.Ok || !reply.Recording || DateTime.UtcNow > finishBy)
                    {
                        if (File.Exists(output + ".done")) break;
                        throw new IOException("录制中断：" + reply.Message);
                    }
                }
                if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("没有生成有效的视频。");
                var outputFolder = Path.Combine(entry.Folder, "outputs");
                Directory.CreateDirectory(outputFolder);
                var final = Path.Combine(outputFolder, $"out_{entry.Difficulty + 1}.mp4");
                var pending = final + "." + batch + ".tmp";
                try { File.Copy(output, pending); File.Move(pending, final, true); }
                finally { if (File.Exists(pending)) File.Delete(pending); }
                completed++;
                firstOutputFolder ??= outputFolder;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { failures.Add(entry.Title + "：" + e.Message); }
            finally
            {
                try
                {
                if (target != null) await GroupControl.Send(target, new() { Action = "batch-close" });
                if (session != null)
                {
                    await Task.Delay(1000);
                    // Only processes registered to this newly-created worker are eligible for cleanup.
                    var owned = AlphaSession.Read(session.Id);
                    foreach (var view in new[] { true, false })
                        using (var process = owned.GetProcess(view))
                            if (process != null) { try { process.Kill(); await process.WaitForExitAsync(); } catch (InvalidOperationException) { } }
                }
                if (Directory.Exists(work)) { try { Directory.Delete(work, true); } catch (IOException) { } }
                }
                catch (Exception cleanupError) { failures.Add(entry.Title + "：清理录制进程失败，" + cleanupError.Message); }
                finally { slots.Release(); }
            }
        }));
        progress($"导出完成 {completed}/{entries.Length}" + (failures.Count == 0 ? "" : "\n" + string.Join("\n", failures)));
        if (firstOutputFolder != null) MajnetPanel.OpenUrl(firstOutputFolder);
    }
}
