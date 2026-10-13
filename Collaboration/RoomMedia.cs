using System.Security.Cryptography;
using System.IO;

namespace MajdataCollaboration;

public sealed record RoomAsset(string Name, long Length, string Hash);
public sealed class RoomMediaCatalog
{
    private readonly string root;
    public RoomAsset[] Assets { get; }
    private static readonly HashSet<string> AllowedNames = new(StringComparer.OrdinalIgnoreCase)
        { "maidata.txt", "track.mp3", "track.ogg", "bg.png", "bg.jpg", "pv.mp4" };
    public RoomMediaCatalog(string directory)
    {
        root = Path.GetFullPath(directory);
        Assets = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(f => AllowedNames.Contains(Path.GetFileName(f)) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0)
            .Select(f => new RoomAsset(Path.GetFileName(f), new FileInfo(f).Length, Hash(f))).ToArray();
        if (Assets.Length > 10000 || Assets.Sum(a => a.Length) > 4L * 1024 * 1024 * 1024)
            throw new IOException("歌曲素材超过 4 GB 或文件数过多，请整理歌曲目录后开房。");
    }
    public byte[] Read(string name, long offset)
    {
        var asset = Assets.SingleOrDefault(a => a.Name == name) ?? throw new IOException("未知歌曲素材。");
        if (offset < 0 || offset >= asset.Length) throw new IOException("素材偏移无效。");
        using var file = File.OpenRead(SafePath(root, name)); file.Position = offset;
        // The persistent connection already avoids repeated handshakes. Keep
        // individual replies small so a rate-limited free tunnel can deliver
        // progress before the request deadline, instead of timing out at 0 MB.
        var data = new byte[(int)Math.Min(32 * 1024, asset.Length - offset)];
        var count = file.Read(data, 0, data.Length);
        return count == data.Length ? data : data.Take(count).ToArray();
    }
    public FileStream Open(string name, long offset)
    {
        var asset = Assets.SingleOrDefault(a => a.Name == name) ?? throw new IOException("未知歌曲素材。");
        if (offset < 0 || offset > asset.Length) throw new IOException("素材偏移无效。");
        var file = File.OpenRead(SafePath(root, name));
        if (file.Length != asset.Length) { file.Dispose(); throw new IOException("主机素材已改变，请重新连接。"); }
        file.Position = offset;
        return file;
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)); }
    private static string SafePath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !AllowedNames.Contains(name) ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            throw new IOException("歌曲素材路径无效。");
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("歌曲素材路径越界。");
        return path;
    }
    public static async Task<string> Download(RoomClient client, string root, Action<string> progress,
        Action<double>? percent = null, bool includeVideo = true, IEnumerable<string>? localFolders = null)
    {
        var manifest = await client.Manifest();
        if (!Guid.TryParseExact(manifest.Room, "N", out _)) throw new IOException("房间标识无效。");
        var folder = Path.Combine(root, manifest.Room);
        Directory.CreateDirectory(folder);
        var assets = manifest.Assets ?? Array.Empty<RoomAsset>();
        if (assets.Length > 10000 || assets.Any(a => a.Length < 0 || a.Length > 4L * 1024 * 1024 * 1024) || assets.Sum(a => a.Length) > 4L * 1024 * 1024 * 1024)
            throw new IOException("主机歌曲素材过大。");
        // The live room snapshot is authoritative; a disk maidata.txt can be
        // older than unsaved edits and is installed separately by the editor.
        var required = assets.Where(a => !a.Name.Equals("maidata.txt", StringComparison.OrdinalIgnoreCase) &&
            (includeVideo || !IsVideo(a))).ToArray();
        ReuseLocalAssets(folder, required, Candidates(root, localFolders), progress, client.Cancellation);
        if(manifest.AssetPeerStreaming && client.IsRemote && required.Any(a=>a.Length>=256*1024 &&
            (!File.Exists(SafePath(folder,a.Name)) || new FileInfo(SafePath(folder,a.Name)).Length!=a.Length ||
                !Hash(SafePath(folder,a.Name)).Equals(a.Hash,StringComparison.OrdinalIgnoreCase)))) {
            progress("正在建立快速直连…");
            await client.TryDirectPeer(manifest.Room);
        }
        await DownloadAssets(client, manifest.Room, folder, required, progress, percent, manifest.AssetStreaming, manifest.AssetBinaryStreaming);
        if (!File.Exists(Path.Combine(folder, "track.mp3")) && !File.Exists(Path.Combine(folder, "track.ogg")))
            throw new IOException("主机歌曲缺少 track.mp3 / track.ogg，无法同步歌曲。");
        return folder;
    }
    public static async Task DownloadVideos(RoomClient client, string folder, Action<string> progress,
        Action<double>? percent = null, IEnumerable<string>? localFolders = null)
    {
        var manifest = await client.Manifest();
        if (!Guid.TryParseExact(manifest.Room, "N", out _) ||
            !string.Equals(new DirectoryInfo(folder).Name, manifest.Room, StringComparison.Ordinal))
            throw new IOException("房主已切换歌曲，视频素材未继续下载。");
        var videos = (manifest.Assets ?? Array.Empty<RoomAsset>()).Where(IsVideo).ToArray();
        ReuseLocalAssets(folder, videos, Candidates(Path.GetDirectoryName(folder)!, localFolders), progress, client.Cancellation);
        await DownloadAssets(client, manifest.Room, folder, videos, progress, percent, manifest.AssetStreaming, manifest.AssetBinaryStreaming);
    }
    private static string[] Candidates(string root, IEnumerable<string>? localFolders)
    {
        var folders = new List<string>(localFolders ?? Array.Empty<string>());
        if (Directory.Exists(root)) folders.AddRange(Directory.EnumerateDirectories(root));
        return folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void ReuseLocalAssets(string folder, RoomAsset[] assets, string[] candidates,
        Action<string> progress, CancellationToken cancellation)
    {
        foreach (var asset in assets)
        {
            cancellation.ThrowIfCancellationRequested();
            var destination = SafePath(folder, asset.Name);
            if (Matches(destination, asset)) continue;
            foreach (var candidate in candidates)
            {
                var source = SafePath(candidate, asset.Name);
                if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || !Matches(source, asset)) continue;
                progress("正在复用本地素材：" + asset.Name);
                // Keep the original song untouched; each participant saves TXT
                // independently. Verify the copy too in case the source changed.
                var partial = destination + ".partial";
                File.Copy(source, partial, true);
                if (!Matches(partial, asset)) { File.Delete(partial); continue; }
                File.Move(partial, destination, true);
                break;
            }
        }
    }
    private static bool Matches(string path, RoomAsset asset)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == asset.Length &&
            Hash(path).Equals(asset.Hash, StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool IsVideo(RoomAsset asset) => asset.Name.Equals("pv.mp4", StringComparison.OrdinalIgnoreCase);
    private static async Task DownloadAssets(RoomClient client, string room, string folder, RoomAsset[] assets,
        Action<string> progress, Action<double>? percent, bool streaming, bool binary)
    {
        var totalBytes = Math.Max(1, assets.Sum(a => a.Length)); long downloadedBytes = 0;
        for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
        {
            var asset = assets[assetIndex];
            var destination = SafePath(folder, asset.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination) && new FileInfo(destination).Length == asset.Length &&
                Hash(destination).Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
            {
                downloadedBytes += asset.Length;
                percent?.Invoke(100d * downloadedBytes / totalBytes);
                continue;
            }
            var partial = destination + ".partial";
            using (var output = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                if (output.Length > asset.Length) output.SetLength(0);
                output.Position = output.Length;
                downloadedBytes += output.Position;
                long receivedPosition = output.Position;
                long shownTenth = -1;
                void ShowProgress(bool force = false)
                {
                    var tenth = (long)(receivedPosition * 10d / 1048576d);
                    if (!force && tenth == shownTenth) return;
                    shownTenth = tenth;
                    progress($"正在同步歌曲素材 {assetIndex + 1}/{assets.Length}：{asset.Name} " +
                        $"{(receivedPosition == asset.Length ? asset.Length / 1048576d : tenth / 10d):0.0}/{asset.Length / 1048576d:0.0} MB");
                    percent?.Invoke(100d * downloadedBytes / totalBytes);
                }
                ShowProgress(true);
                if (streaming && output.Position < asset.Length)
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            await client.StreamAsset(asset.Name, output.Position, asset.Length, room, async data =>
                            {
                                await output.WriteAsync(data);
                                downloadedBytes += data.Length;
                                receivedPosition = output.Position;
                                ShowProgress();
                            }, binary);
                            break;
                        }
                        catch (Exception error) when (!client.IsDisposed && attempt < 2 &&
                            error is IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException)
                        {
                            progress($"素材连接中断，正在重试 {attempt + 1}/2：{asset.Name} " +
                                $"{output.Position / 1048576d:0.0}/{asset.Length / 1048576d:0.0} MB");
                            await Task.Delay(500 * (attempt + 1));
                        }
                    }
                }
                while (output.Position < asset.Length)
                {
                    RoomReply reply;
                    for (var attempt = 0; ; attempt++)
                    {
                        try { reply = await client.Asset(asset.Name, output.Position); break; }
                        catch (Exception error) when (!client.IsDisposed && attempt < 2 &&
                            error is IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException)
                        {
                            progress($"素材连接中断，正在重试 {attempt + 1}/2：{asset.Name} " +
                                $"{output.Position / 1048576d:0.0}/{asset.Length / 1048576d:0.0} MB");
                            await Task.Delay(500 * (attempt + 1));
                        }
                    }
                    if (reply.Room != room) throw new IOException("房主已切换歌曲，请重新下载。");
                    var data = reply.Content ?? throw new IOException("素材块缺失。");
                    if (data.Length == 0 || output.Position + data.Length > asset.Length) throw new IOException("素材块长度无效。");
                    await output.WriteAsync(data);
                    downloadedBytes += data.Length; receivedPosition = output.Position; ShowProgress();
                }
                ShowProgress(true);
            }
            if (!Hash(partial).Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new IOException("主机素材已改变，请重新连接：" + asset.Name);
            }
            File.Move(partial, destination, true);
        }
    }
}
