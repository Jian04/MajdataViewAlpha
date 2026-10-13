using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MajdataCollaboration;

internal static class RoomWebSocketConnector
{
    internal static async Task<ClientWebSocket> Connect(Uri address, CancellationToken token)
    {
        // Some system proxies handle HTTPS but reject WebSocket upgrades. Try
        // the configured route first, then a direct TLS route on connection failure.
        for (int route = 0; ; route++)
        {
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            if (route == 1) socket.Options.Proxy = null;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(TimeSpan.FromSeconds(6));
            try { await socket.ConnectAsync(address, attempt.Token); return socket; }
            catch (Exception error) when (route == 0 && !token.IsCancellationRequested &&
                error is WebSocketException or IOException or OperationCanceledException)
            { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
    }
}

// The WebSocket is only a byte transport. RoomWire's nonce/HMAC exchange and
// bounded frames remain identical for LAN and public connections.
internal sealed class RoomWebSocketStream : Stream
{
    private readonly WebSocket socket;
    internal RoomWebSocketStream(WebSocket socket) => this.socket = socket;
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return 0;
            if (result.MessageType != WebSocketMessageType.Binary) throw new IOException("房间传输格式无效。");
            // Empty binary messages/continuation frames are not stream EOF.
            if (result.Count != 0) return result.Count;
        }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            var count = Math.Min(256 * 1024, buffer.Length);
            await socket.SendAsync(buffer.Slice(0, count), WebSocketMessageType.Binary, true, cancellationToken);
            buffer = buffer.Slice(count);
        }
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) socket.Dispose(); base.Dispose(disposing); }
}

// A loopback-only WebSocket bridge avoids firewall rules and HTTP.sys URL ACLs.
public sealed class RoomWebSocketGateway : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly int roomPort;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public RoomWebSocketGateway(int roomPort)
    {
        this.roomPort = roomPort;
        listener.Start();
        _ = Task.Run(Accept);
    }
    private async Task Accept()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var peer = await listener.AcceptTcpClientAsync(stop.Token);
                _ = Handle(peer);
            }
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
    }
    private async Task Handle(TcpClient peer)
    {
        using (peer)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                peer.NoDelay = true;
                var net = peer.GetStream();
                var header = new byte[8192]; int length = 0;
                while (length < header.Length)
                {
                    int read = await net.ReadAsync(header.AsMemory(length, 1), timeout.Token);
                    if (read == 0) return;
                    length++;
                    if (length >= 4 && header[length - 4] == 13 && header[length - 3] == 10 &&
                        header[length - 2] == 13 && header[length - 1] == 10) break;
                }
                if (length == header.Length) return;
                var request = Encoding.ASCII.GetString(header, 0, length);
                var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2 || lines[0] != "GET /room HTTP/1.1" ||
                    !lines.Any(x => x.StartsWith("Upgrade:", StringComparison.OrdinalIgnoreCase) && x.Contains("websocket", StringComparison.OrdinalIgnoreCase)) ||
                    !lines.Any(x => x.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) && x.Contains("upgrade", StringComparison.OrdinalIgnoreCase))) return;
                var key = lines.FirstOrDefault(x => x.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim();
                if (key == null || Convert.FromBase64String(key).Length != 16) return;
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await net.WriteAsync(response, timeout.Token);
                using var websocket = WebSocket.CreateFromStream(net, true, null, TimeSpan.FromSeconds(20));
                using var room = new TcpClient();
                room.NoDelay = true;
                await room.ConnectAsync(IPAddress.Loopback, roomPort, timeout.Token);
                var upstream = room.GetStream();
                // Only the handshake is time-limited. A writing session may stay open for hours.
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                var towardRoom = PumpWebSocket(websocket, upstream, timeout.Token);
                var towardPeer = PumpTcp(upstream, websocket, timeout.Token);
                await Task.WhenAny(towardRoom, towardPeer);
                timeout.Cancel();
                try { await Task.WhenAll(towardRoom, towardPeer); } catch (OperationCanceledException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or WebSocketException or OperationCanceledException or FormatException or ArgumentException) { }
        }
    }
    private static async Task PumpWebSocket(WebSocket from, Stream to, CancellationToken token)
    {
        var bytes = new byte[256 * 1024];
        while (!token.IsCancellationRequested)
        {
            var result = await from.ReceiveAsync(bytes.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Binary) throw new IOException("房间传输格式无效。");
            await to.WriteAsync(bytes.AsMemory(0, result.Count), token);
        }
    }
    private static async Task PumpTcp(Stream from, WebSocket to, CancellationToken token)
    {
        var bytes = new byte[256 * 1024];
        while (!token.IsCancellationRequested)
        {
            var count = await from.ReadAsync(bytes.AsMemory(), token);
            if (count == 0) break;
            await to.SendAsync(bytes.AsMemory(0, count), WebSocketMessageType.Binary, true, token);
        }
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); }
}

public sealed class RoomPublicTunnel : IDisposable
{
    private const string Version = "2026.9.1";
    private const string ExpectedDigest = "2837888cc0f5d58f15b6dc478376de90b4d3ba5241c7947455d1e0a0df429712";
    private static readonly Uri Download = new($"https://github.com/cloudflare/cloudflared/releases/download/{Version}/cloudflared-windows-amd64.exe");
    private readonly RoomWebSocketGateway gateway;
    private readonly Process process;
    public string Address { get; }
    public bool IsRunning
    {
        get { try { return !process.HasExited; } catch (InvalidOperationException) { return false; } }
    }
    private RoomPublicTunnel(RoomWebSocketGateway gateway, Process process, string address)
    { this.gateway = gateway; this.process = process; Address = address; }
    public static Task<RoomPublicTunnel> Open(int roomPort, Action<string>? progress = null)
        => Task.Run(() => OpenCore(roomPort, progress));
    private static async Task<RoomPublicTunnel> OpenCore(int roomPort, Action<string>? progress)
    {
        progress?.Invoke("正在准备公网连接…");
        var provider = Environment.GetEnvironmentVariable("MAJDATA_TUNNEL_PROVIDER");
        if (provider == "ssh") return await OpenSsh(roomPort);
        if (provider == "cloudflare") return await OpenCloudflare(roomPort);
        using var race=new CancellationTokenSource();
        var pending = new List<Task<RoomPublicTunnel>> { OpenCloudflare(roomPort,race.Token), OpenSsh(roomPort,race.Token) };
        var errors = new List<Exception>();
        while (pending.Count > 0) {
            var ready = await Task.WhenAny(pending); pending.Remove(ready);
            try {
                var selected = await ready;
                race.Cancel();
                // The losing attempt owns its helper. Dispose it as soon as it
                // finishes, and observe failure even after the winner returns.
                foreach (var other in pending) _ = DisposeUnused(other);
                return selected;
            } catch (Exception error) { errors.Add(error); }
        }
        throw new IOException("公网线路均未能建立，请检查网络连接。", new AggregateException(errors));
    }
    private static async Task DisposeUnused(Task<RoomPublicTunnel> attempt) {
        try { (await attempt).Dispose(); } catch { }
    }
    private static async Task<RoomPublicTunnel> OpenSsh(int roomPort, CancellationToken cancellation = default)
    {
        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OpenSSH", "ssh.exe");
        if (!File.Exists(ssh)) throw new FileNotFoundException("系统未提供 OpenSSH 客户端。", ssh);
        var gateway = new RoomWebSocketGateway(roomPort);
        Process? process = null;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MajdataAlpha", "Network");
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo(ssh)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-T", "-o", "BatchMode=yes", "-o", "ExitOnForwardFailure=yes",
                "-o", "StrictHostKeyChecking=accept-new", "-o", "UserKnownHostsFile=" + Path.Combine(folder, "known_hosts"),
                "-o", "ServerAliveInterval=20", "-o", "ServerAliveCountMax=3",
                "-R", $"80:127.0.0.1:{gateway.Port}", "nokey@localhost.run" }) start.ArgumentList.Add(argument);
            process = Process.Start(start) ?? throw new IOException("无法启动系统 SSH 组件。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var address = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Drain(StreamReader reader)
            {
                try
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!line.Contains("tunneled", StringComparison.OrdinalIgnoreCase)) continue;
                        var match = Regex.Match(line, @"https://([a-z0-9-]+\.(?:localhost\.run|lhr\.life))(?:/|\b)", RegexOptions.IgnoreCase);
                        if (match.Success) address.TrySetResult("wss://" + match.Groups[1].Value + "/room");
                    }
                }
                catch (IOException) { }
            }
            _ = Drain(process.StandardOutput);
            _ = Drain(process.StandardError);
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(address.Task, exited).WaitAsync(timeout.Token) != address.Task)
                throw new IOException("免费 SSH 隧道未能建立。");
            var tunnel = new RoomPublicTunnel(gateway, process, await address.Task);
            await Probe(tunnel.Address,cancellation);
            return tunnel;
        }
        catch
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.Dispose(); gateway.Dispose(); throw;
        }
    }
    private static async Task<RoomPublicTunnel> OpenCloudflare(int roomPort, CancellationToken cancellation = default)
    {
        var binary = await GetBinary(cancellation);
        var gateway = new RoomWebSocketGateway(roomPort);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(binary)
            {
                Arguments = $"tunnel --no-autoupdate --url http://127.0.0.1:{gateway.Port}",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            process = Process.Start(start) ?? throw new IOException("无法启动公网连接组件。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            while (!timeout.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync().WaitAsync(timeout.Token);
                if (line == null) break;
                var match = Regex.Match(line, @"https://([a-z0-9-]+\.trycloudflare\.com)(?:/|\b)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var tunnel = new RoomPublicTunnel(gateway, process, "wss://" + match.Groups[1].Value + "/room");
                    _ = process.StandardError.ReadToEndAsync();
                    _ = process.StandardOutput.ReadToEndAsync();
                    await Probe(tunnel.Address,cancellation);
                    return tunnel;
                }
            }
            throw new IOException("公网隧道未能建立，请检查网络连接。");
        }
        catch
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.Dispose(); gateway.Dispose(); throw;
        }
    }
    private static async Task Probe(string address, CancellationToken cancellation = default)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        total.CancelAfter(TimeSpan.FromSeconds(18));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var socket = await RoomWebSocketConnector.Connect(new Uri(address), total.Token);
                using var stream = new RoomWebSocketStream(socket);
                var challenge = await RoomWire.Read<string>(stream, total.Token);
                if (challenge.Length is < 1 or > 100) throw new IOException("公网房间响应无效。");
                return;
            }
            catch (Exception error) when ((error is WebSocketException or IOException) && attempt < 3)
            { await Task.Delay(1500, total.Token); }
        }
        throw new IOException("公网地址未能接受连接。");
    }
    private static async Task<string> GetBinary(CancellationToken cancellation)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MajdataAlpha", "Network");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, $"cloudflared-{Version}.exe");
        if (File.Exists(file) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(ExpectedDigest, StringComparison.OrdinalIgnoreCase)) return file;
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".download";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        try
        {
            using var response = await client.GetAsync(Download, HttpCompletionOption.ResponseHeadersRead,cancellation);
            response.EnsureSuccessStatusCode();
            await using (var target = File.Create(temporary)) await response.Content.CopyToAsync(target,cancellation);
            var actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temporary)));
            if (!actual.Equals(ExpectedDigest, StringComparison.OrdinalIgnoreCase)) throw new IOException("公网连接组件校验失败。");
            File.Move(temporary, file, true);
            return file;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose()
    {
        gateway.Dispose();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.Dispose();
    }
}
