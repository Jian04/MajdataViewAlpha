using System;
using System.IO;
using UnityEngine;

internal static class RecordingEncoder
{
    internal static string ResolveExecutable()
    {
        string platform;
        string name;
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor: platform = "windows-x64"; name = "ffmpeg.exe"; break;
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                platform = SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "macos-arm64" : "macos-x64"; name = "ffmpeg"; break;
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor: platform = "linux-x64"; name = "ffmpeg"; break;
            default: throw new PlatformNotSupportedException("当前平台需要原生录制编码器。");
        }
        foreach (var path in new[] {
            Path.Combine(Application.streamingAssetsPath, "Encoders", platform, name),
            Path.Combine(Application.streamingAssetsPath, name) })
            if (File.Exists(path)) return path;
        throw new FileNotFoundException("找不到当前平台的 FFmpeg 编码器。", platform + "/" + name);
    }
    internal static string UseStandardInput(string arguments) =>
        arguments.Replace(@"\\.\pipe\" + MajdataCore.AlphaSession.PipeName, "pipe:0");

    internal static string BuildGifArguments(int width, int height, int frameRate, string outputName) =>
        $"-hide_banner -y -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {frameRate} -i pipe:0 " +
        $"-filter_complex \"fps={Math.Min(30, frameRate)},vflip,split[a][b];[a]palettegen=stats_mode=single[p];" +
        "[b][p]paletteuse=new=1:dither=sierra2_4a\" -loop 0 \"" + outputName + "\"";
}
