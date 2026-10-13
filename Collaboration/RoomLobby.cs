using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MajdataCollaboration;

public sealed record RoomListing(string Host, string Name, string Address, bool HasPassword, int Players);
public sealed record LobbyScan(RoomListing[] Rooms, string Hint);

internal sealed record RoomDirectoryMessage(string Type, string RequestId, RoomListing? Listing = null);

public static class RoomLobby
{
    private static readonly Uri PublicDirectory = new("wss://router.metapage.io/majdata-alpha-room-lobby-v1");

    public static Task<LobbyScan> Discover() => Task.Run(DiscoverCore);
    private static async Task<LobbyScan> DiscoverCore()
    {
        var online = await DiscoverPublic();
        return new LobbyScan(online.Rooms.DistinctBy(room => room.Address)
            .OrderBy(room => room.Host).ThenBy(room => room.Name).ToArray(), online.Hint);
    }

    private static async Task<LobbyScan> DiscoverPublic()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var socket = await RoomWebSocketConnector.Connect(PublicDirectory, timeout.Token);
            string requestId = Guid.NewGuid().ToString("N");
            await Send(socket, new RoomDirectoryMessage("discover", requestId), timeout.Token);
            var rooms = new List<RoomListing>();
            using var replies = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                while (!replies.IsCancellationRequested)
                {
                    var message = await Read(socket, replies.Token);
                    if (message?.Type != "listing" || message.RequestId != requestId || message.Listing is not { } room) continue;
                    if (room.Address is null || room.Name is null || room.Host is null ||
                        !ValidPublicAddress(room.Address) || room.Name.Length is 0 or > 80 || room.Host.Length is 0 or > 80) continue;
                    rooms.Add(room);
                }
            }
            catch (OperationCanceledException) when (replies.IsCancellationRequested) { }
            return new LobbyScan(rooms.ToArray(), "");
        }
        catch (OperationCanceledException) { return new LobbyScan(Array.Empty<RoomListing>(), "公网房间目录连接超时，可通过房主分享的地址加入。"); }
        catch (Exception error) when (error is IOException or WebSocketException or JsonException)
        {
            return new LobbyScan(Array.Empty<RoomListing>(), "公网房间目录暂不可用，可通过房主分享的地址加入。");
        }
    }

    internal static bool ValidPublicAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == "wss" &&
        (uri.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".localhost.run", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".lhr.life", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath == "/room" && uri.Query == "" && uri.UserInfo == "";

    internal static async Task Send(ClientWebSocket socket, RoomDirectoryMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token);
    }
    internal static async Task<RoomDirectoryMessage?> Read(ClientWebSocket socket, CancellationToken token)
    {
        var bytes = new byte[4096];
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(bytes.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("房间目录连接已关闭。");
            if (message.Length + result.Count > 16 * 1024) throw new IOException("房间目录消息过长。");
            message.Write(bytes, 0, result.Count);
        } while (!result.EndOfMessage);
        if (result.MessageType != WebSocketMessageType.Text) return null;
        try { return JsonSerializer.Deserialize<RoomDirectoryMessage>(message.GetBuffer().AsSpan(0, (int)message.Length)); }
        catch (JsonException) { return null; }
    }

    internal static Uri DirectoryAddress => PublicDirectory;

}

public sealed class RoomPublicLobbyHost : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Func<RoomListing> listing;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => ready.Task;
    public RoomPublicLobbyHost(Func<RoomListing> listing)
    { this.listing = listing; _ = Task.Run(Serve); }
    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                ClientWebSocket connected;
                using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    connecting.CancelAfter(TimeSpan.FromSeconds(15));
                    connected = await RoomWebSocketConnector.Connect(RoomLobby.DirectoryAddress, connecting.Token);
                }
                using var socket = connected;
                ready.TrySetResult(true);
                while (!stop.IsCancellationRequested)
                {
                    var message = await RoomLobby.Read(socket, stop.Token);
                    if (message?.Type != "discover" || message.RequestId is null || message.RequestId.Length is 0 or > 64) continue;
                    await RoomLobby.Send(socket, new RoomDirectoryMessage("listing", message.RequestId, listing()), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or WebSocketException) { }
            try { await Task.Delay(1500, stop.Token); } catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() => stop.Cancel();
}
