using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

internal static class VideoSourceCompatibility
{
    private static readonly Dictionary<string, Task<string>> pending = new(StringComparer.OrdinalIgnoreCase);

    internal static Task<string> Prepare(string source)
    {
        lock (pending)
        {
            if (pending.TryGetValue(source, out var running)) return running;
            var gif = Path.GetExtension(source).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            if (!gif && !IsDash(source)) return Task.FromResult(source);
            var encoder = RecordingEncoder.ResolveExecutable();
            var task = Task.Run(() => ConvertSource(source, encoder, gif));
            pending[source] = task;
            task.ContinueWith(_ => { lock (pending) pending.Remove(source); });
            return task;
        }
    }

    private static bool IsDash(string source)
    {
        using (var stream = File.OpenRead(source))
        {
            var header = new byte[Math.Min(256L, stream.Length)];
            stream.Read(header, 0, header.Length);
            if (header.Length < 16 || Encoding.ASCII.GetString(header, 4, 4) != "ftyp") return false;
            var size = header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3];
            var dash = Encoding.ASCII.GetString(header, 8, 4) == "dash";
            for (var i = 16; i + 4 <= Math.Min(size, header.Length); i += 4)
                dash |= Encoding.ASCII.GetString(header, i, 4) == "dash";
            return dash;
        }
    }

    private static string ConvertSource(string source, string encoder, bool gif)
    {
        // Media Foundation rejects DASH containers even with supported H.264.
        // Repackage the stream without re-encoding; ordinary MP4s never reach here.
        var info = new FileInfo(source);
        using var hash = SHA256.Create();
        var key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
            info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks))).Replace("-", "");
        var cache = Path.Combine(MajdataCore.AlphaSession.EditorDataRoot, "Cache", "Video");
        Directory.CreateDirectory(cache);
        var extension = gif ? ".apng" : ".mp4";
        var output = Path.Combine(cache, key + extension);
        if (File.Exists(output)) return output;
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp" + extension;
        try
        {
            var conversion = gif ? "-c:v apng -plays 0 -f apng" : "-c:v copy -brand mp42 -movflags +faststart";
            var start = new ProcessStartInfo(encoder,
                $"-hide_banner -loglevel error -nostdin -y -i \"{source}\" -map 0:v:0 -an {conversion} \"{temporary}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using var process = Process.Start(start);
            var error = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException((gif ? "GIF 封面转换失败：" : "PV 容器转换失败：") + error);
            try { File.Move(temporary, output); }
            catch (IOException) when (File.Exists(output)) { /* Another View completed the same remux. */ }
            return output;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
