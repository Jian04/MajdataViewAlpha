using System;
using System.Globalization;

internal static class ViewLocalization
{
    private static string language = "en-US";

    internal static void SetLanguage(string value)
    {
        language = string.IsNullOrWhiteSpace(value) ? "en-US" : value;
    }

    internal static string Text(string key, params object[] args)
    {
        var format = IsChinese()
            ? Chinese(key)
            : IsJapanese()
                ? Japanese(key)
                : English(key);
        return args == null || args.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args);
    }

    private static bool IsChinese() =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static bool IsJapanese() =>
        language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    private static string English(string key) => key switch
    {
        "RecordingNoChart" => "Recording could not start because the chart directory is unavailable.\n",
        "RecordingEvenResolution" => "Recording requires even dimensions. Current resolution: {0}x{1}\n",
        "RecordingResolutionHighWarning" => "Warning: {0}x{1} at {2} fps is above the recommended capture load and may cause severe lag or a display-driver reset.\nRecording will continue.\n",
        "FfmpegStartFailed" => "FFmpeg could not start.\n{0}\n",
        "FfmpegPipeExit" => "FFmpeg exited before connecting to the video pipe.\nExit code: {0}\n",
        "NamedPipeFailed" => "The FFmpeg video pipe could not connect.\n",
        "PvPrewarmFailed" => "The PV could not decode its first frame before recording started.\nRecording was cancelled to prevent a frozen video.\n",
        "MediaPrewarmFailed" => "A timeline media file could not be prepared.\n{0}\nRecording was cancelled.\n",
        "RecordingResize" => "The View window changed from {0}x{1} to {2}x{3}; recording stopped.\nDo not move or resize View while recording.\n",
        "RecordingFailedAt" => "Recording failed at {0:0.000}s.\n{1}: {2}\n",
        "RecordingAborted" => "Recording stopped before FFmpeg finished.\n",
        "RecordingSuccess" => "Recording complete: {0}\nExit code: {1}",
        "FfmpegExited" => "FFmpeg exited.\nExit code: {0}",
        _ => key
    };

    private static string Chinese(string key) => key switch
    {
        "RecordingNoChart" => "无法开始录制：谱面目录不可用。\n",
        "RecordingEvenResolution" => "录制分辨率的宽和高必须是偶数。当前分辨率：{0}x{1}\n",
        "RecordingResolutionHighWarning" => "警告：{0}x{1}、{2} fps 超出建议采集负载，可能严重卡顿或触发显卡驱动重置。\n录制仍将继续。\n",
        "FfmpegStartFailed" => "FFmpeg 启动失败。\n{0}\n",
        "FfmpegPipeExit" => "FFmpeg 在连接视频管道前已退出。\n退出码：{0}\n",
        "NamedPipeFailed" => "无法连接 FFmpeg 视频管道。\n",
        "PvPrewarmFailed" => "录制开始前无法解码 PV 首帧。\n为避免导出视频画面冻结，已取消录制。\n",
        "MediaPrewarmFailed" => "时间轴媒体文件预载失败。\n{0}\n录制已取消。\n",
        "RecordingResize" => "View 窗口从 {0}x{1} 变为 {2}x{3}，录制已停止。\n录制时请勿移动或缩放 View 窗口。\n",
        "RecordingFailedAt" => "录制在 {0:0.000} 秒时失败。\n{1}：{2}\n",
        "RecordingAborted" => "录制在 FFmpeg 完成前停止。\n",
        "RecordingSuccess" => "录制完成：{0}\n退出码：{1}",
        "FfmpegExited" => "FFmpeg 已退出。\n退出码：{0}",
        _ => English(key)
    };

    private static string Japanese(string key) => key switch
    {
        "RecordingNoChart" => "録画を開始できません：譜面フォルダーを利用できません。\n",
        "RecordingEvenResolution" => "録画解像度の幅と高さは偶数である必要があります。現在の解像度：{0}x{1}\n",
        "RecordingResolutionHighWarning" => "警告：{0}x{1}、{2} fps は推奨録画負荷を超えており、深刻な遅延やディスプレイドライバーのリセットが発生する可能性があります。\n録画は続行します。\n",
        "FfmpegStartFailed" => "FFmpeg を起動できませんでした。\n{0}\n",
        "FfmpegPipeExit" => "FFmpeg は映像パイプへ接続する前に終了しました。\n終了コード：{0}\n",
        "NamedPipeFailed" => "FFmpeg の映像パイプへ接続できませんでした。\n",
        "PvPrewarmFailed" => "録画開始前に PV の最初のフレームをデコードできませんでした。\n映像の停止を防ぐため、録画を中止しました。\n",
        "MediaPrewarmFailed" => "タイムラインのメディアを準備できませんでした。\n{0}\n録画を中止しました。\n",
        "RecordingResize" => "View ウィンドウが {0}x{1} から {2}x{3} に変更されたため、録画を停止しました。\n録画中は View を移動またはリサイズしないでください。\n",
        "RecordingFailedAt" => "録画は {0:0.000} 秒で失敗しました。\n{1}：{2}\n",
        "RecordingAborted" => "FFmpeg の完了前に録画が停止しました。\n",
        "RecordingSuccess" => "録画完了：{0}\n終了コード：{1}",
        "FfmpegExited" => "FFmpeg が終了しました。\n終了コード：{0}",
        _ => English(key)
    };
}
