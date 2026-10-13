using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MajdataCollaboration;

public sealed record RoomPresence(string Peer, string Name, int Difficulty, int Offset, string Color = "#3DA5FF");
public sealed record RoomRequest(string Kind, long Revision, string Text, string Peer,
    RoomPresence? Presence = null, string Asset = "", long Offset = 0, string Room = "",
    RoomTextPatch? Patch = null, bool Compact = false, long PresenceRevision = -1);
public sealed record RoomReply(string Room, long Revision, string Text, bool Conflict, int Peers,
    RoomPresence[]? Cursors = null, RoomAsset[]? Assets = null, byte[]? Content = null, int HostDifficulty = -1, string SelfColor = "#3DA5FF",
    string[]? PriorRooms = null, bool AssetStreaming = false, bool AssetBinaryStreaming = false,
    bool AssetPeerStreaming = false, bool TextPatches = false, RoomTextPatch? Patch = null,
    long PresenceRevision = 0, bool Closed = false, bool Left = false);
public sealed record SignedMessage(string Payload, string Proof);

// UTF-16 offsets match the editor. A patch is always relative to an explicit
// revision, so it can still use the existing three-way conflict handling.
public sealed record RoomTextPatch(int Start, int Remove, string Insert)
{
    public static RoomTextPatch Between(string before, string after)
    {
        int start = 0, end = before.Length, tail = after.Length;
        while (start < end && start < tail && before[start] == after[start]) start++;
        while (end > start && tail > start && before[end - 1] == after[tail - 1]) { end--; tail--; }
        return new(start, end - start, after[start..tail]);
    }
    public string Apply(string basis)
    {
        if (Insert == null || Start < 0 || Remove < 0 || Start > basis.Length || Remove > basis.Length - Start ||
            (long)basis.Length - Remove + Insert.Length > RoomWire.MaxText)
            throw new IOException("谱面增量范围无效。");
        return basis[..Start] + Insert + basis[(Start + Remove)..];
    }
}

public static class RoomWire
{
    internal const int AssetBlockSize = 64 * 1024;
    private static byte[] AssetProof(string password, string nonce, long offset, ReadOnlySpan<byte> data) {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(password));
        hmac.AppendData(Encoding.UTF8.GetBytes(nonce));
        Span<byte> position=stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(position,offset);
        hmac.AppendData(position); hmac.AppendData(data); return hmac.GetHashAndReset();
    }
    internal static async Task WriteAssetBlock(Stream stream, string password, string nonce, long offset, byte[] data, int count, CancellationToken token) {
        var frame=new byte[36+count]; BinaryPrimitives.WriteInt32BigEndian(frame,count);
        AssetProof(password,nonce,offset,data.AsSpan(0,count)).CopyTo(frame,4);
        data.AsSpan(0,count).CopyTo(frame.AsSpan(36)); await stream.WriteAsync(frame,token);
    }
    internal static async Task<byte[]> ReadAssetBlock(Stream stream, string password, string nonce, long offset, CancellationToken token) {
        var header=new byte[36]; await ReadAll(stream,header,token);
        int count=BinaryPrimitives.ReadInt32BigEndian(header);
        if(count<0 || count>AssetBlockSize) throw new IOException("素材块长度无效。");
        var data=new byte[count]; await ReadAll(stream,data,token);
        if(!CryptographicOperations.FixedTimeEquals(AssetProof(password,nonce,offset,data),header.AsSpan(4))) throw new IOException("素材块校验失败。");
        return data;
    }
    public const int MaxText = 1_000_000;
    private const int MaxFrame = 16_000_000; // JSON escaping can expand UTF-8 text.
    public static string Proof(string password, string nonce, string payload) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(nonce + "\n" + payload)));
    public static bool Verify(string password, string nonce, SignedMessage message)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Proof(password, nonce, message.Payload)), Convert.FromBase64String(message.Proof)); }
        catch (FormatException) { return false; }
    }
    public static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxFrame) throw new IOException("谱面消息过大。");
        var frame = new byte[bytes.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length);
        bytes.CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, token);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await ReadAll(stream, header, token);
        int size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size < 1 || size > MaxFrame) throw new IOException("无效消息长度。");
        var bytes = new byte[size]; await ReadAll(stream, bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("无效消息。");
    }
    private static async Task ReadAll(Stream stream, byte[] bytes, CancellationToken token)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes.AsMemory(offset), token);
            if (count == 0) throw new IOException("连接已关闭或密码不正确。");
            offset += count;
        }
    }
}

public static class TextMerge
{
    private sealed record Change(int Start, int End, string Text);
    private static Change Difference(string before, string after)
    {
        int start = 0, end = before.Length, tail = after.Length;
        while (start < end && start < tail && before[start] == after[start]) start++;
        while (end > start && tail > start && before[end - 1] == after[tail - 1]) { end--; tail--; }
        return new(start, end, after.Substring(start, tail - start));
    }
    private static List<Change> Differences(string before, string after)
    {
        var a = System.Text.RegularExpressions.Regex.Split(before, "(?<=\n)");
        var b = System.Text.RegularExpressions.Regex.Split(after, "(?<=\n)");
        if ((long)a.Length * b.Length > 1_000_000) return new() { Difference(before, after) };
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var result = new List<Change>();
        int x = 0, y = 0, offset = 0, start = 0;
        var removed = new StringBuilder(); var inserted = new StringBuilder();
        void Flush()
        {
            if (removed.Length == 0 && inserted.Length == 0) return;
            var c = Difference(removed.ToString(), inserted.ToString());
            result.Add(c with { Start = c.Start + start, End = c.End + start });
            removed.Clear(); inserted.Clear();
        }
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            { Flush(); offset += a[x++].Length; y++; start = offset; }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] > lcs[x + 1, y])) inserted.Append(b[y++]);
            else { removed.Append(a[x]); offset += a[x++].Length; }
        }
        Flush(); return result;
    }
    public static bool TryMerge(string basis, string local, string remote, out string merged)
    {
        merged = local;
        if (local == remote || remote == basis) return true;
        if (local == basis) { merged = remote; return true; }
        var a = Differences(basis, local); var b = Differences(basis, remote);
        foreach (var left in a)
            foreach (var right in b)
                if (left != right && (left.Start == right.Start || !(left.End <= right.Start || right.End <= left.Start))) return false;
        merged = basis;
        foreach (var change in a.Concat(b).Distinct().OrderByDescending(c => c.Start))
            merged = merged[..change.Start] + change.Text + merged[change.End..];
        return merged.Length <= RoomWire.MaxText;
    }
}

public sealed class RoomDocument
{
    private readonly object gate = new();
    private readonly SortedDictionary<long, string> history = new();
    private readonly Dictionary<string, DateTime> peers = new();
    private readonly Dictionary<string, RoomPresence> cursors = new();
    private readonly Dictionary<string, string> colors = new();
    private long presenceRevision;
    private bool closed;
    private readonly HashSet<string> closePending = new();
    private readonly HashSet<string> departedPeers = new();
    private TaskCompletionSource<bool> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly string[] palette = { "#3DA5FF", "#F57391", "#36C9A0", "#F3AA43", "#C779EF", "#65CEDB", "#A7C957", "#B39DDB", "#FF8040", "#00B894", "#E84393", "#A29BFE", "#FDCB6E", "#81ECEC", "#D63031", "#74B9FF" };
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly string[] priorRooms;
    public string Id => id;
    public string[] PriorRooms => priorRooms;
    public int PeerCount { get { lock (gate) return peers.Count; } }
    public Task WaitForChange(long knownRevision, string knownRoom, long knownPresenceRevision = -1)
    {
        lock (gate)
            return closed || knownRoom != id || knownRevision != revision ||
                knownPresenceRevision >= 0 && knownPresenceRevision != presenceRevision ? Task.CompletedTask : changed.Task;
    }
    public void CloseRoom()
    {
        lock (gate) { closed = true; closePending.UnionWith(peers.Keys); NotifyChange(); }
    }
    public bool CloseAcknowledged { get { lock (gate) return closePending.Count == 0; } }
    public void PruneExpiredPeers()
    {
        lock (gate)
            foreach (var peer in peers.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(15)).Select(p => p.Key).ToArray())
                RemovePeer(peer);
    }
    private void RemovePeer(string peer)
    {
        if (!peers.Remove(peer)) return;
        cursors.Remove(peer); colors.Remove(peer); closePending.Remove(peer);
        presenceRevision++; NotifyChange();
    }
    public void NotifyChange()
    {
        lock (gate)
        {
            var waiting = changed;
            changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.TrySetResult(true);
        }
    }
    private long revision;
    private string text;
    public RoomDocument(string initial, string[]? priorRooms = null)
    {
        if (initial.Length > RoomWire.MaxText) throw new IOException("谱面超过 100 万字符限制。");
        this.priorRooms = priorRooms ?? Array.Empty<string>();
        text = initial; history[0] = initial;
    }
    public RoomReply Apply(RoomRequest request)
    {
        lock (gate)
        {
            if (request.Text == null || request.Text.Length > RoomWire.MaxText || string.IsNullOrEmpty(request.Peer) || request.Peer.Length > 80)
                throw new IOException("无效谱面请求。");
            if (closed)
            {
                if (request.Kind == "closedack") closePending.Remove(request.Peer);
                return new(id, revision, "", false, 0, Closed: true, PresenceRevision: presenceRevision);
            }
            PruneExpiredPeers();
            if (request.Kind == "leave")
            {
                departedPeers.Add(request.Peer);
                RemovePeer(request.Peer);
                return new(id, revision, "", false, peers.Count, TextPatches: true, PresenceRevision: presenceRevision, Left: true);
            }
            // A watch/read/edit already in flight must not resurrect a departed
            // member. Only an explicit join starts a new presence session.
            if (request.Kind == "join") departedPeers.Remove(request.Peer);
            else if (departedPeers.Contains(request.Peer))
                return new(id, revision, "", false, peers.Count, PresenceRevision: presenceRevision, Left: true);
            if (peers.Count >= 16 && !peers.ContainsKey(request.Peer)) throw new IOException("房间已满（16 人）。");
            var presenceChanged = !peers.ContainsKey(request.Peer);
            peers[request.Peer] = DateTime.UtcNow;
            foreach (var old in colors.Keys.Where(p => !peers.ContainsKey(p)).ToArray()) colors.Remove(old);
            if (!colors.ContainsKey(request.Peer)) colors[request.Peer] = palette.First(c => !colors.Values.Contains(c));
            if (request.Presence is { } presence)
            {
                var next = presence with { Peer = request.Peer, Name = (presence.Name ?? "用户").Substring(0, Math.Min(40, (presence.Name ?? "用户").Length)), Offset = Math.Clamp(presence.Offset, 0, RoomWire.MaxText) };
                presenceChanged |= !cursors.TryGetValue(request.Peer, out var old) || old != next;
                cursors[request.Peer] = next;
            }
            if (presenceChanged) { presenceRevision++; NotifyChange(); }
            bool conflict = false;
            if (request.Kind == "edit" && (string.IsNullOrEmpty(request.Room) || request.Room == id))
            {
                if (!history.TryGetValue(request.Revision, out var basis) || !TextMerge.TryMerge(basis,
                    request.Patch?.Apply(basis) ?? request.Text, text, out var merged)) conflict = true;
                else if (merged != text)
                {
                    text = merged; history[++revision] = text;
                    while (history.Count > 128) history.Remove(history.Keys.First());
                    NotifyChange();
                }
            }
            else if (request.Kind is not ("edit" or "read" or "watch" or "join")) throw new IOException("不支持的房间协议。");
            RoomTextPatch? patch = null;
            if (request.Compact && request.Room == id && history.TryGetValue(request.Revision, out var replyBasis))
            {
                var delta = RoomTextPatch.Between(replyBasis, text);
                if (delta.Insert.Length + 64 < text.Length) patch = delta;
            }
            return new(id, revision, patch == null ? text : "", conflict, peers.Count, cursors.Where(p => p.Key != request.Peer)
                .Select(p => p.Value with { Color = colors[p.Key] }).ToArray(), SelfColor: colors[request.Peer], PriorRooms: priorRooms,
                TextPatches: true, Patch: patch, PresenceRevision: presenceRevision);
        }
    }
}

public sealed class RoomServer : IDisposable
{
    private readonly RoomPeerHost peerHost;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim slots = new(32);
    private readonly string password;
    private readonly object songGate = new();
    private bool disposed;
    private RoomDocument document;
    private RoomMediaCatalog? media;
    public int HostDifficulty { get; set; } = -1;
    public int PeerCount { get { lock (songGate) { document.PruneExpiredPeers(); return document.PeerCount; } } }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public RoomServer(int port, string password, string initial, bool loopbackOnly = false, string? mediaRoot = null)
    {
        this.password = password; document = new(initial);
        if (!string.IsNullOrEmpty(mediaRoot)) media = new RoomMediaCatalog(mediaRoot);
        listener = new(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(); peerHost=new RoomPeerHost(Port); _ = Task.Run(Accept);
    }
    public void SwitchSong(string initial, string mediaRoot, int difficulty)
    {
        var nextMedia = new RoomMediaCatalog(mediaRoot);
        lock (songGate)
        {
            document.NotifyChange();
            var prior = document.PriorRooms.Append(document.Id).ToArray();
            document = new RoomDocument(initial, prior);
            media = nextMedia;
            HostDifficulty = difficulty;
        }
    }
    private async Task Accept()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                client.NoDelay = true;
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                _ = Handle(client);
            }
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
    }
    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var stream = client.GetStream(); var token = timeout.Token;
                while (!token.IsCancellationRequested)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    await RoomWire.Write(stream, nonce, token);
                    var message = await RoomWire.Read<SignedMessage>(stream, token);
                    if (!RoomWire.Verify(password, nonce, message)) return;
                    var request = JsonSerializer.Deserialize<RoomRequest>(message.Payload) ?? throw new IOException();
                    RoomReply reply;
                    RoomMediaCatalog? requestedMedia;
                    RoomDocument watchedDocument;
                    lock (songGate)
                    {
                        watchedDocument = document;
                        reply = watchedDocument.Apply(request.Kind is "manifest" or "asset" or "assetstream" or "assetstream2" or "watch" or "peeroffer" ? request with { Kind = "read" } : request) with { HostDifficulty = HostDifficulty };
                        if (request.Kind == "manifest") reply = reply with { Assets = media?.Assets ?? Array.Empty<RoomAsset>(), AssetStreaming = true, AssetBinaryStreaming = true, AssetPeerStreaming = true };
                        requestedMedia = media;
                    }
                    if(!reply.Closed && request.Kind=="peeroffer") {
                        if(request.Room!=reply.Room) throw new IOException("房主已切换歌曲。");
                        reply=reply with { Text=await peerHost.Answer(request.Text,token), Cursors=null };
                    }
                    if (!reply.Closed && request.Kind is ("assetstream" or "assetstream2"))
                    {
                        if (request.Room != reply.Room) throw new IOException("房主已切换歌曲。");
                        using var file = requestedMedia?.Open(request.Asset, request.Offset) ?? throw new IOException("主机没有歌曲素材。");
                        bool binary=request.Kind=="assetstream2";
                        var buffer = new byte[binary ? RoomWire.AssetBlockSize : 32 * 1024];
                        while (true)
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(60));
                            lock (songGate)
                                if (document.Id != reply.Room) throw new IOException("房主已切换歌曲。");
                            var offset = file.Position;
                            var count = await file.ReadAsync(buffer, token);
                            if(binary) {
                                await RoomWire.WriteAssetBlock(stream,password,nonce,offset,buffer,count,token);
                                if(count==0) break;
                                continue;
                            }
                            var block = reply with { Text = "", Cursors = null, Content = buffer.AsSpan(0, count).ToArray() };
                            var payload = JsonSerializer.Serialize(block);
                            await RoomWire.Write(stream, new SignedMessage(payload, RoomWire.Proof(password,
                                nonce + ":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture), payload)), token);
                            if (count == 0) break;
                        }
                        continue;
                    }
                    if (!reply.Closed && request.Kind == "watch" && reply.Room == request.Room && reply.Revision == request.Revision &&
                        (request.PresenceRevision < 0 || reply.PresenceRevision == request.PresenceRevision))
                    {
                        try { await watchedDocument.WaitForChange(request.Revision, request.Room, request.PresenceRevision).WaitAsync(TimeSpan.FromSeconds(12), token); }
                        catch (TimeoutException) { }
                        lock (songGate)
                            reply = document.Apply(request with { Kind = "read" }) with { HostDifficulty = HostDifficulty };
                        if (reply.Room == request.Room && reply.Revision == request.Revision)
                            reply = reply with { Text = "" };
                    }
                    if (!reply.Closed && request.Kind == "asset")
                        reply = reply with { Text = "", Cursors = null,
                            Content = requestedMedia?.Read(request.Asset, request.Offset) ?? throw new IOException("主机没有歌曲素材。") };
                    var result = JsonSerializer.Serialize(reply);
                    await RoomWire.Write(stream, new SignedMessage(result, RoomWire.Proof(password, nonce, result)), token);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or JsonException or ArgumentException) { }
            finally { slots.Release(); }
        }
    }
    public async Task CloseRoomAsync(string? hostPeer = null)
    {
        RoomDocument closing;
        lock (songGate)
        {
            closing = document;
            if (hostPeer != null) closing.Apply(new RoomRequest("leave", 0, "", hostPeer));
            closing.CloseRoom();
        }
        // Keep the tunnel and peer bridge alive until watchers receive the signed
        // closure. A dead guest must never block the host's own disconnect.
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!closing.CloseAcknowledged && !disposed && DateTime.UtcNow < until) await Task.Delay(25).ConfigureAwait(false);
        Dispose();
    }
    public void Dispose() { if (disposed) return; disposed = true; document.CloseRoom(); stop.Cancel(); listener.Stop(); peerHost.Dispose(); }
}

public sealed class RoomClient : IDisposable
{
    private readonly string host, password, peer;
    public string PeerId => peer;
    private readonly int port;
    private readonly Uri? websocketAddress;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private Stream? persistentStream;
    private readonly CancellationTokenSource stop = new();
    private bool disposed;
    internal bool IsDisposed => disposed;
    internal bool IsRemote => websocketAddress != null;
    internal CancellationToken Cancellation => stop.Token;
    internal RoomClient CreateTransferClient() => new(websocketAddress?.AbsoluteUri ?? host + ":" + port, password, peer);
    private string? peerAttemptedRoom;
    internal bool IsDirectPeer => persistentStream is RoomPeerStream;
    internal Task<RoomReply> PeerOffer(string offer,string room)=>Request(new RoomRequest("peeroffer",0,offer,peer,Room:room));
    internal async Task<bool> TryDirectPeer(string room,bool localTest=false) {
        if(IsDirectPeer) return true;
        if((!IsRemote && !localTest) || peerAttemptedRoom==room) return false;
        peerAttemptedRoom=room;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        RoomPeerStream? direct=null;
        try {
            using var signaling=CreateTransferClient();
            direct=await RoomPeerStream.Connect(signaling,room,deadline.Token);
            // A data channel opening is not proof that its bridge is serving
            // the room. Authenticate a complete request before replacing relay IO.
            var verified = await RequestOnStream(direct, new RoomRequest("read", 0, "", peer, Room: room), deadline.Token);
            if (verified.Room != room) throw new IOException("直连房间已切换。");
            await requestGate.WaitAsync(deadline.Token);
            try {
                if(disposed) throw new IOException("房间连接已断开。");
                persistentStream?.Dispose(); persistentStream=direct; direct=null;
            } finally { requestGate.Release(); }
            RoomNetworkTrace.Write("直连已通过房间认证。");
            return true;
        } catch(Exception error) {
            RoomNetworkTrace.Write("直连未建立，回退公网中继：" + error.GetType().Name + " " + error.Message);
            direct?.Dispose(); return false;
        }
    }
    public string? Room { get; private set; }
    private bool supportsTextPatches;
    private bool hasLeft;
    public long PresenceRevision { get; private set; } = -1;
    public RoomClient(string address, string password, string? peerId = null)
    {
        peer = peerId ?? Guid.NewGuid().ToString("N");
        if (RoomLobby.ValidPublicAddress(address) && Uri.TryCreate(address, UriKind.Absolute, out var remote))
        {
            websocketAddress = remote;
            host = remote.Host; port = 443;
        }
        else
        {
            if (!Uri.TryCreate("tcp://" + address, UriKind.Absolute, out var uri) || uri.Port < 1 || uri.Port > 65535 || uri.AbsolutePath != "/" || uri.UserInfo != "")
                throw new ArgumentException("服务器地址应为 IP:端口或房主提供的公网房间地址。");
            host = uri.Host; port = uri.Port;
        }
        this.password = password;
    }
    public async Task<RoomReply> Exchange(long revision, string text, bool readOnly = false, RoomPresence? presence = null)
    {
        var reply = await Request(new RoomRequest(hasLeft ? "join" : readOnly ? "read" : "edit", revision, text, peer, presence, Room: Room ?? ""));
        if (!reply.Left && !reply.Closed) hasLeft = false;
        return reply;
    }
    public Task<RoomReply> Watch(long revision, string room)
        => Request(new RoomRequest("watch", revision, "", peer, Room: room, PresenceRevision: PresenceRevision));
    public async Task<RoomReply> Leave()
    {
        hasLeft = true;
        var request = new RoomRequest("leave", 0, "", peer, Room: Room ?? "");
        if (requestGate.Wait(0))
        {
            try
            {
                if (persistentStream != null)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var directReply = await RequestOnStream(persistentStream, request, deadline.Token).ConfigureAwait(false);
                    Room = null;
                    return directReply;
                }
            }
            catch (Exception error) when (error is IOException or SocketException or WebSocketException or OperationCanceledException)
            { persistentStream?.Dispose(); persistentStream = null; }
            finally { requestGate.Release(); }
        }
        // A stalled synchronization must not keep Leave behind its request gate.
        using var control = CreateTransferClient();
        var reply = await control.Request(request).ConfigureAwait(false);
        Room = null;
        return reply;
    }
    public async Task<RoomReply> SyncText(long revision, string basis, string text, RoomPresence? presence)
    {
        if (!supportsTextPatches) return await Exchange(revision, text, text == basis, presence);
        var reply = await Request(new RoomRequest(text == basis ? "read" : "edit", revision, "", peer,
            presence, Room: Room ?? "", Patch: text == basis ? null : RoomTextPatch.Between(basis, text), Compact: true));
        return reply.Closed || reply.Patch == null ? reply : reply with { Text = reply.Patch.Apply(basis), Patch = null };
    }
    public async Task<RoomReply> WatchText(long revision, string room, string basis)
    {
        var reply = await Request(new RoomRequest("watch", revision, "", peer, Room: room, Compact: true, PresenceRevision: PresenceRevision));
        if (reply.Closed || reply.Left) return reply;
        if (reply.Patch != null) return reply with { Text = reply.Patch.Apply(basis), Patch = null };
        return reply.Revision == revision && reply.Room == room && reply.Text.Length == 0
            ? reply with { Text = basis } : reply;
    }
    public Task<RoomReply> Manifest() => Request(new RoomRequest("manifest", 0, "", peer));
    public Task<RoomReply> Asset(string name, long offset) => Request(new RoomRequest("asset", 0, "", peer, Asset: name, Offset: offset));
    // One authenticated request, then server-pushed blocks. A remote round trip
    // per 32 KB makes song transfers dominated by latency rather than bandwidth.
    public Task StreamAsset(string name, long offset, long length, string room, Func<byte[], Task> receive, bool binary = false)
        => Task.Run(() => StreamAssetCore(name,offset,length,room,receive,binary));
    private async Task StreamAssetCore(string name, long offset, long length, string room, Func<byte[], Task> receive, bool binary)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await requestGate.WaitAsync(timeout.Token);
        Stream? stream = null;
        try
        {
            if (disposed) throw new IOException("房间连接已断开。");
            stream = persistentStream ??= await OpenStream(timeout.Token);
            var nonce = await RoomWire.Read<string>(stream, timeout.Token);
            if (nonce.Length > 100) throw new IOException("服务器挑战无效。");
            var payload = JsonSerializer.Serialize(new RoomRequest(binary ? "assetstream2" : "assetstream", 0, "", peer,
                Asset: name, Offset: offset, Room: room));
            await RoomWire.Write(stream, new SignedMessage(payload, RoomWire.Proof(password, nonce, payload)), timeout.Token);
            while (true)
            {
                // Timeout measures a stalled block, not the duration of a large PV.
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                byte[] data;
                if(binary) data=await RoomWire.ReadAssetBlock(stream,password,nonce,offset,timeout.Token);
                else {
                var signed = await RoomWire.Read<SignedMessage>(stream, timeout.Token);
                if (!RoomWire.Verify(password, nonce + ":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture), signed))
                    throw new IOException("素材块校验失败。");
                var reply = JsonSerializer.Deserialize<RoomReply>(signed.Payload) ?? throw new IOException("素材块无效。");
                if (reply.Room != room) throw new IOException("房主已切换歌曲。");
                data = reply.Content ?? throw new IOException("素材块缺失。");
                }
                if (data.Length > (binary ? RoomWire.AssetBlockSize : 32 * 1024) || offset + data.Length > length) throw new IOException("素材块长度无效。");
                if (data.Length == 0)
                {
                    if (offset != length) throw new IOException("素材传输未完成。");
                    return;
                }
                await receive(data);
                offset += data.Length;
            }
        }
        catch { persistentStream = null; stream?.Dispose(); throw; }
        finally
        {
            if (websocketAddress == null && stream is not RoomPeerStream) { persistentStream = null; stream?.Dispose(); }
            requestGate.Release();
        }
    }
    private Task<RoomReply> Request(RoomRequest request) => Task.Run(() => RequestCore(request));
    private async Task<RoomReply> RequestCore(RoomRequest request)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(websocketAddress == null ? 8 : request.Kind == "asset" ? 45 : 25));
        await requestGate.WaitAsync(timeout.Token);
        try
        {
            if (disposed) throw new IOException("房间连接已断开。");
            // Reuse the tunneled WebSocket for the manifest and all asset blocks.
            // This removes a slow TLS/tunnel handshake from every media chunk.
            // Read-only requests can safely reconnect when an idle tunnel or
            // completed peer transfer left a dead stream. Never replay an edit.
            bool canRetry = request.Kind is "read" or "manifest" or "asset" or "watch" or "join";
            for (int attempt = 0; ; attempt++)
            {
                bool reused = persistentStream != null;
                var stream = persistentStream ??= await OpenStream(timeout.Token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                if (reused && canRetry && request.Kind != "watch")
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                RoomReply reply;
                try { reply = await RequestOnStream(stream, request, requestTimeout.Token); }
                catch (Exception error) when (canRetry && attempt == 0 && !timeout.IsCancellationRequested &&
                    error is IOException or WebSocketException or SocketException or OperationCanceledException)
                {
                    persistentStream = null; stream.Dispose();
                    RoomNetworkTrace.Write("读取连接失效，重新连接：" + request.Kind + " / " + error.GetType().Name);
                    timeout.CancelAfter(TimeSpan.FromSeconds(websocketAddress == null ? 8 : 25));
                    continue;
                }
                catch { persistentStream = null; stream.Dispose(); throw; }
                finally
                {
                    if (websocketAddress == null && stream is not RoomPeerStream)
                    { persistentStream = null; stream.Dispose(); }
                }
                if (Room != null && Room != reply.Room && reply.PriorRooms?.Contains(Room) != true)
                    throw new IOException("房间已重建，请重新连接。");
                Room = reply.Room; supportsTextPatches = reply.TextPatches; PresenceRevision = reply.PresenceRevision; return reply;
            }
        }
        finally { requestGate.Release(); }
    }
    private async Task<RoomReply> RequestOnStream(Stream stream, RoomRequest request, CancellationToken token)
    {
        var nonce = await RoomWire.Read<string>(stream, token);
        if (nonce.Length > 100) throw new IOException("服务器挑战无效。");
        var payload = JsonSerializer.Serialize(request);
        await RoomWire.Write(stream, new SignedMessage(payload, RoomWire.Proof(password, nonce, payload)), token);
        var signed = await RoomWire.Read<SignedMessage>(stream, token);
        if (!RoomWire.Verify(password, nonce, signed)) throw new IOException("服务器身份校验失败。");
        var reply = JsonSerializer.Deserialize<RoomReply>(signed.Payload) ?? throw new IOException("无效房间回复。");
        if (reply.Text == null || reply.Text.Length > RoomWire.MaxText) throw new IOException("无效谱面大小。");
        if (reply.Closed && request.Kind != "closedack")
        {
            // Receipt of the signed closure, rather than creation of its reply,
            // acknowledges delivery. The host may now safely close its tunnel.
            using var acknowledgment = CancellationTokenSource.CreateLinkedTokenSource(token);
            acknowledgment.CancelAfter(TimeSpan.FromSeconds(1));
            try { await RequestOnStream(stream, new RoomRequest("closedack", 0, "", peer, Room: reply.Room), acknowledgment.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or SocketException or WebSocketException or OperationCanceledException) { }
        }
        return reply;
    }
    public void Dispose() { disposed = true; stop.Cancel(); persistentStream?.Dispose(); persistentStream = null; }
    private async Task<Stream> OpenStream(CancellationToken token)
    {
        if (websocketAddress != null)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var socket = await RoomWebSocketConnector.Connect(websocketAddress, token);
                    return new RoomWebSocketStream(socket);
                }
                catch (WebSocketException) when (attempt < 3)
                {
                    await Task.Delay(1500, token);
                }
            }
            throw new IOException("公网房间暂时不可达。");
        }
        var client = new TcpClient();
        try
        {
            client.NoDelay = true;
            await client.ConnectAsync(host, port).WaitAsync(token);
            return new OwnedTcpStream(client);
        }
        catch { client.Dispose(); throw; }
    }
    private sealed class OwnedTcpStream : Stream
    {
        private readonly TcpClient client;
        private NetworkStream Inner => client.GetStream();
        internal OwnedTcpStream(TcpClient client) => this.client = client;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => Inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Inner.ReadAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => Inner.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) client.Dispose(); base.Dispose(disposing); }
    }
}
