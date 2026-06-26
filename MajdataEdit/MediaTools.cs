using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

internal static class MediaTools
{
    public static async Task ConvertAudioTo44100Async(string filePath)
    {
        var ffmpeg = FindFfmpeg();
        var dir = Path.GetDirectoryName(filePath)!;
        var tmpDir = PrepareTempDirectory(dir);
        var ext = Path.GetExtension(filePath);
        var temp = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(filePath) + "_44100" + ext);

        var codec = ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "-c:a pcm_s16le"
            : "";
        await RunFfmpegAsync(ffmpeg, $"-y -i {Q(filePath)} -map 0:a:0 -ar 44100 {codec} {Q(temp)}");

        BackupOriginal(filePath);
        File.Copy(temp, filePath, true);
        TryDeleteDirectory(tmpDir);
    }

    public static async Task RemoveRangeAsync(string filePath, double start, double end)
    {
        if (end <= start || start < 0)
            throw new InvalidOperationException("时间范围不合法。");

        var ffmpeg = FindFfmpeg();
        var dir = Path.GetDirectoryName(filePath)!;
        var tmpDir = PrepareTempDirectory(dir);
        var ext = Path.GetExtension(filePath);
        var part1 = Path.Combine(tmpDir, "part1" + ext);
        var part2 = Path.Combine(tmpDir, "part2" + ext);
        var list = Path.Combine(tmpDir, "concat.txt");
        var output = Path.Combine(tmpDir, "output" + ext);

        await RunFfmpegAsync(ffmpeg, $"-y -i {Q(filePath)} -t {Fmt(start)} -map 0 -c copy {Q(part1)}");
        await RunFfmpegAsync(ffmpeg, $"-y -ss {Fmt(end)} -i {Q(filePath)} -map 0 -c copy {Q(part2)}");
        await File.WriteAllTextAsync(list,
            "file '" + part1.Replace("\\", "/").Replace("'", "'\\''") + "'\n" +
            "file '" + part2.Replace("\\", "/").Replace("'", "'\\''") + "'\n",
            Encoding.UTF8);
        await RunFfmpegAsync(ffmpeg, $"-y -f concat -safe 0 -i {Q(list)} -map 0 -c copy {Q(output)}");

        BackupOriginal(filePath);
        File.Copy(output, filePath, true);
        TryDeleteDirectory(tmpDir);
    }

    private static string FindFfmpeg()
    {
        var candidates = new List<string>();
        candidates.Add(AppContext.BaseDirectory);
        candidates.Add(Environment.CurrentDirectory);
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var dir in candidates)
        {
            var path = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException("找不到 ffmpeg.exe。请把 ffmpeg.exe 放到程序目录或 PATH。");
    }

    private static async Task RunFfmpegAsync(string ffmpeg, string args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Length > 0 ? stderr : "ffmpeg 执行失败。");
    }

    private static void BackupOriginal(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var backupDir = Path.Combine(dir, "backup");
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backup = Path.Combine(backupDir,
            Path.GetFileNameWithoutExtension(filePath) + "_" + stamp + Path.GetExtension(filePath));
        File.Copy(filePath, backup, false);
    }

    private static string PrepareTempDirectory(string dir)
    {
        var tmp = Path.Combine(dir, ".majdata_tmp");
        if (Directory.Exists(tmp))
            Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
        }
    }

    private static string Q(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal readonly record struct MediaRange(double Start, double End);

internal sealed class MediaRangeDialog : Window
{
    private readonly TextBox startBox = new() { Text = "0", Width = 110, Margin = new Thickness(6) };
    private readonly TextBox endBox = new() { Text = "1", Width = 110, Margin = new Thickness(6) };
    private MediaRange? result;

    private MediaRangeDialog()
    {
        Title = "剪掉时间段";
        Width = 280;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        grid.Children.Add(new Label { Content = "开始秒", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(startBox, 1);
        grid.Children.Add(startBox);
        var endLabel = new Label { Content = "结束秒", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(endLabel, 1);
        grid.Children.Add(endLabel);
        Grid.SetRow(endBox, 1);
        Grid.SetColumn(endBox, 1);
        grid.Children.Add(endBox);

        var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(6) };
        ok.Click += (_, _) => Confirm();
        Grid.SetRow(ok, 2);
        Grid.SetColumnSpan(ok, 2);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(ok);
        Content = grid;
    }

    public static MediaRange? ShowDialog(Window owner)
    {
        var dialog = new MediaRangeDialog { Owner = owner };
        dialog.ShowDialog();
        return dialog.result;
    }

    private void Confirm()
    {
        if (!double.TryParse(startBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(endBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var end) ||
            end <= start)
        {
            MessageBox.Show("请输入合法的开始秒和结束秒。");
            return;
        }
        result = new MediaRange(start, end);
        Close();
    }
}
