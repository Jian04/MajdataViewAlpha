using System.IO;
using System.IO.Pipes;

namespace MajdataCollaboration;

public sealed record GroupTarget(string Id, int Pid, long Start);
public sealed record GroupSize(int Width, int Height);
public sealed record GroupCommand
{
    public string Action { get; init; } = "status";
    public string Batch { get; init; } = Guid.NewGuid().ToString("N");
    public string Session { get; init; } = "";
    public long ProcessStart { get; init; }
    public long StartUtc { get; init; }
    public double Seconds { get; init; } = -1;
    public int Fps { get; init; } = 60;
    public double IntroDuration { get; init; } = 5;
    public int Width { get; init; } = 1080;
    public int Height { get; init; } = 1080;
    public int X { get; init; }
    public int Y { get; init; }
    public string FileName { get; init; } = "";
    public string ChartDirectory { get; init; } = "";
    public int Difficulty { get; init; } = 4;
    public bool IncludeMusic { get; init; } = true;
    public int PetPid { get; init; }
    public long PetStart { get; init; }
    public Dictionary<string, GroupSize>? Sizes { get; init; }
}
public sealed record GroupReply(bool Ok, string Message, string Title = "", bool Playing = false,
    bool Recording = false, bool Ready = false, double Position = 0, string Output = "",
    int X = 0, int Y = 0, int Width = 720, int Height = 720,
    string ChartDirectory = "", string Ffmpeg = "", string AudioOutput = "", double IntroDuration = 5, bool Ended = false,
    double Duration = 0);

public static class GroupControl
{
    public static string PipeName(int pid) => "MajdataAlpha.GroupControl." + pid;
    public static async Task<GroupReply> Send(GroupTarget target, GroupCommand command)
    {
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(command.Action == "dock-probe" ? .5 : command.Action.StartsWith("prepare-") || command.Action is "commit-record" or "batch-open" ? 180 : 12));
            using var pipe = new NamedPipeClientStream(".", PipeName(target.Pid), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2500, stop.Token);
            await RoomWire.Write(pipe, command with { Session = target.Id, ProcessStart = target.Start }, stop.Token);
            return await RoomWire.Read<GroupReply>(pipe, stop.Token);
        }
        catch (Exception e) { return new(false, "无法控制该 Edit：" + e.Message); }
    }
    public static void Validate(GroupCommand command)
    {
        if (command.Action is not ("queue-play" or "batch-open" or "batch-close" or "dock" or "dock-probe" or "dock-ready" or "dock-startup" or "dock-complete" or "status" or "stop" or "abort" or "pause" or "seek" or "layout" or "check-play" or "check-record" or "prepare-play" or "prepare-record" or "commit-play" or "commit-record"))
            throw new ArgumentException("不支持的控制指令。");
        if (!Guid.TryParseExact(command.Batch, "N", out _)) throw new ArgumentException("批次标识无效。");
        if (command.IntroDuration is not (0 or 5)) throw new ArgumentException("开场时长需为 0 或 5 秒。");
        var minimumSeconds = command.Action is "seek" or "check-play" or "prepare-play" or "commit-play" ? -5d : -1d;
        if (!double.IsFinite(command.Seconds) || command.Seconds < minimumSeconds || command.Seconds > 86400)
            throw new ArgumentException("时间超出允许范围。");
        if (command.Action is "check-record" or "prepare-record" or "commit-record")
        {
            if (command.Fps is not (60 or 120) || command.Width < 256 || command.Height < 256 || command.Width > 3840 || command.Height > 3840 || command.Width % 2 != 0 || command.Height % 2 != 0)
                throw new ArgumentException("录制分辨率需为 256–3840 的偶数，帧率为 60 或 120。");
            if (command.FileName != Path.GetFileName(command.FileName) || command.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !command.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("录制文件名无效。");
        }
        if (command.Action == "layout" && (command.Width < 200 || command.Height < 200 || command.Width > 4096 || command.Height > 4096 || Math.Abs((long)command.X) > 32768 || Math.Abs((long)command.Y) > 32768))
            throw new ArgumentException("窗口位置或尺寸超出范围。");
    }
}

public static class GroupBatch
{
    // Every participant must prepare successfully before any receives a start command.
    public static async Task<Dictionary<string, GroupReply>> Start(GroupTarget[] targets, GroupCommand command,
        Func<GroupTarget, GroupCommand, Task<GroupReply>>? send = null, CancellationToken token = default)
    {
        send ??= GroupControl.Send;
        var kind = command.Action;
        if (targets.Length == 0 || targets.Length > 16 || targets.Select(t => t.Id).Distinct().Count() != targets.Length)
            throw new ArgumentException("请选择 1–16 个不同的实例。");
        if (kind is not ("play" or "record")) throw new ArgumentException("Unsupported batch action.");
        Dictionary<string, bool>? includeMusic = null;
        async Task<Dictionary<string, GroupReply>> Round(string action, long start = 0)
        {
            var replies = await Task.WhenAll(targets.Select(async target =>
            {
                var size = command.Sizes?.GetValueOrDefault(target.Id);
                return new KeyValuePair<string, GroupReply>(target.Id,
                    await send(target, command with { Action = action, StartUtc = start,
                        IncludeMusic = includeMusic?.GetValueOrDefault(target.Id) ?? target.Id == targets[0].Id,
                        Width = size?.Width ?? command.Width, Height = size?.Height ?? command.Height,
                        FileName = kind == "record" ? "multi-" + command.Batch + "-" + target.Id + ".mp4" : "" }));
            }));
            return replies.ToDictionary(p => p.Key, p => p.Value);
        }
        var check = await Round("check-" + kind);
        if (check.Values.Any(r => !r.Ok) || token.IsCancellationRequested) return check;
        var heardFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        includeMusic = targets.ToDictionary(target => target.Id, target =>
        {
            var folder = check[target.Id].ChartDirectory;
            return heardFolders.Add(string.IsNullOrWhiteSpace(folder) ? target.Id : Path.GetFullPath(folder));
        });
        command = command with { IntroDuration = check[targets[0].Id].IntroDuration };
        var prepared = await Round("prepare-" + kind);
        if (prepared.Values.Any(r => !r.Ok) || token.IsCancellationRequested) { await Round("abort"); return prepared; }
        var committed = await Round("commit-" + kind, DateTime.UtcNow.AddSeconds(2).Ticks);
        if (committed.Values.Any(r => !r.Ok) || token.IsCancellationRequested) await Round("abort");
        return committed;
    }
}
