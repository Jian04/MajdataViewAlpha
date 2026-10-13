using System.Net.Sockets;
using MajdataCollaboration;

if(args.Length==1 && args[0]=="--peer-probe") {
    var folder=Directory.CreateTempSubdirectory("majdata-peer-probe-").FullName;
    var original=new byte[7_025_459]; Random.Shared.NextBytes(original);
    File.WriteAllBytes(Path.Combine(folder,"track.mp3"),original);
    using var host=new RoomServer(0,"probe-secret","&title=Peer Probe\n",true,mediaRoot:folder);
    using var guest=new RoomClient("127.0.0.1:"+host.Port,"probe-secret");
    var manifest=await guest.Manifest(); var watch=System.Diagnostics.Stopwatch.StartNew();
    if(!await guest.TryDirectPeer(manifest.Room,localTest:true)) throw new Exception("WebRTC peer negotiation failed");
    Console.WriteLine($"PEER READY: {watch.Elapsed.TotalSeconds:F2}s");
    var connected=await guest.Manifest().WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("PEER MANIFEST PASS: "+connected.Assets!.Length);
    using var received=new MemoryStream(); watch.Restart();
    await guest.StreamAsset("track.mp3",0,original.Length,manifest.Room,
        data=>{received.Write(data); if(received.Length%1048576==0) Console.WriteLine($"PEER MEDIA: {received.Length/1048576} MB at {watch.Elapsed.TotalSeconds:F2}s"); return Task.CompletedTask;},binary:true).WaitAsync(TimeSpan.FromSeconds(60));
    if(!received.ToArray().SequenceEqual(original)) throw new Exception("Peer media differs");
    if((await guest.Manifest()).Room!=manifest.Room) throw new Exception("Peer stream framing lost after transfer");
    Console.WriteLine($"PEER SPEED PASS: authenticated 6.7 MB in {watch.Elapsed.TotalSeconds:F2}s");
    return;
}

if (args.Length == 1 && args[0] == "--speed-probe") {
    var folder=Path.Combine(Path.GetTempPath(),"MajdataSpeedProbe-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var original=new byte[512*1024]; Random.Shared.NextBytes(original);
    File.WriteAllBytes(Path.Combine(folder,"track.mp3"),original);
    using var host=new RoomServer(0,"probe-secret","&title=Speed Probe\n",true,mediaRoot:folder);
    var watch=System.Diagnostics.Stopwatch.StartNew();
    using var tunnel=await RoomPublicTunnel.Open(host.Port,Console.WriteLine);
    Console.WriteLine($"ROOM READY: {watch.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine("PUBLIC PROVIDER: " + new Uri(tunnel.Address).Host.Split('.').Last());
    using var guest=new RoomClient(tunnel.Address,"probe-secret");
    var manifest=await guest.Manifest();
    watch.Restart(); using var received=new MemoryStream();
    try {
        await guest.StreamAsset("track.mp3",0,original.Length,manifest.Room,
            data=>{ received.Write(data); Console.WriteLine($"MEDIA {received.Length/1024} KB at {watch.Elapsed.TotalSeconds:F2}s"); return Task.CompletedTask; },true)
            .WaitAsync(TimeSpan.FromSeconds(35));
        if(!received.ToArray().SequenceEqual(original)) throw new Exception("Public media differs");
        Console.WriteLine($"PUBLIC SPEED PASS: authenticated 512 KB in {watch.Elapsed.TotalSeconds:F2}s");
        watch.Restart();
        if(!await guest.TryDirectPeer(manifest.Room)) throw new Exception("Public signaling did not establish a direct peer");
        Console.WriteLine($"PUBLIC PEER READY: {watch.Elapsed.TotalSeconds:F2}s");
        watch.Restart(); received.SetLength(0);
        await guest.StreamAsset("track.mp3",0,original.Length,manifest.Room,
            data=>{received.Write(data);return Task.CompletedTask;},binary:true).WaitAsync(TimeSpan.FromSeconds(20));
        if(!received.ToArray().SequenceEqual(original)) throw new Exception("Publicly negotiated peer media differs");
        Console.WriteLine($"PUBLIC PEER SPEED PASS: authenticated 512 KB in {watch.Elapsed.TotalSeconds:F2}s");
    } finally { guest.Dispose(); }
    return;
}

if (args.Length == 1 && args[0] == "--watch-probe")
{
    using var host = new RoomServer(0, "probe-secret", "&title=Latency Probe\n&inote_5=" + new string('1', 120000) + "\n", true);
    using var tunnel = await RoomPublicTunnel.Open(host.Port, Console.WriteLine);
    using var guest = new RoomClient(tunnel.Address, "probe-secret");
    var latencyInitial = await guest.Exchange(0, "", true);
    using var latencyWatcher = new RoomClient(tunnel.Address, "probe-secret", guest.PeerId);
    Console.WriteLine("Watch direct peer: " + await latencyWatcher.TryDirectPeer(latencyInitial.Room));
    using var writer = new RoomClient("127.0.0.1:" + host.Port, "probe-secret");
    await writer.Exchange(0, "", true);
    var waiting = latencyWatcher.WatchText(latencyInitial.Revision, latencyInitial.Room, latencyInitial.Text);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    await writer.SyncText(latencyInitial.Revision, latencyInitial.Text, latencyInitial.Text + "#updated\n", null);
    var update = await waiting.WaitAsync(TimeSpan.FromSeconds(20));
    clock.Stop();
    if (!update.Text.Contains("#updated")) throw new Exception("public watch did not deliver the edit");
    Console.WriteLine($"PASS: public change notification arrived in {clock.ElapsedMilliseconds} ms.");
    return;
}

if (args.Length == 1 && args[0] == "--directory-probe")
{
    var name = "Majdata-Directory-Probe-" + Guid.NewGuid().ToString("N")[..8];
    using var host = new RoomPublicLobbyHost(() => new RoomListing("Probe", name, "wss://example.trycloudflare.com/room", false, 1));
    RoomListing? found = null;
    for (int i = 0; i < 5 && found == null; i++)
    {
        await Task.Delay(800);
        var scan = await RoomLobby.Discover();
        Console.WriteLine($"Directory attempt {i + 1}: {scan.Rooms.Length} rooms, hint: {scan.Hint}");
        found = scan.Rooms.FirstOrDefault(x => x.Name == name);
    }
    if (found == null) throw new Exception("Public room directory did not return the probe listing.");
    Console.WriteLine("PASS: public directory advertisement and discovery.");
    return;
}

if (args.Length == 1 && args[0] == "--internet-probe")
{
    var probeMediaRoot = Directory.CreateTempSubdirectory("majdata-public-media-");
    var mediaBytes = new byte[7_025_459]; // 6.7 MiB, matching the reported stalled song.
    Random.Shared.NextBytes(mediaBytes);
    File.WriteAllBytes(Path.Combine(probeMediaRoot.FullName, "track.ogg"), mediaBytes);
    var videoBytes = new byte[1_100_000];
    Random.Shared.NextBytes(videoBytes);
    File.WriteAllBytes(Path.Combine(probeMediaRoot.FullName, "pv.mp4"), videoBytes);
    using var publicServer = new RoomServer(0, "probe-secret", "&title=Public Probe\n&inote_5=1,2,E\n", true, mediaRoot: probeMediaRoot.FullName);
    using var tunnel = await RoomPublicTunnel.Open(publicServer.Port, Console.WriteLine);
    Console.WriteLine("Tunnel: " + tunnel.Address);
    if (Environment.GetEnvironmentVariable("MAJDATA_PROBE_PAUSE") == "1")
        await Task.Delay(30000);
    try
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var probe = await http.GetAsync(tunnel.Address.Replace("wss://", "https://"));
        Console.WriteLine("HTTPS probe: " + (int)probe.StatusCode);
    }
    catch (Exception error) { Console.WriteLine("HTTPS probe failed: " + error.Message); }
    try
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        using var handler = new System.Net.Http.SocketsHttpHandler();
        handler.SslOptions.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
        using var invoker = new System.Net.Http.HttpMessageInvoker(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new Uri(tunnel.Address), invoker, timeout.Token);
        Console.WriteLine("TLS 1.2 WebSocket probe connected.");
    }
    catch (Exception error) { Console.WriteLine("TLS 1.2 WebSocket probe failed: " + error.Message); }
    var publicClient = new RoomClient(tunnel.Address, "probe-secret");
    var response = await publicClient.Exchange(0, "", true);
    if (!response.Text.Contains("Public Probe")) throw new Exception("public tunnel did not relay chart");
    using (var publicWatcher = new RoomClient(tunnel.Address, "probe-secret", publicClient.PeerId))
    using (var writer = new RoomClient("127.0.0.1:" + publicServer.Port, "probe-secret"))
    {
        var watching = publicWatcher.Watch(response.Revision, response.Room);
        var watchClock = System.Diagnostics.Stopwatch.StartNew();
        await writer.Exchange(response.Revision, response.Text + "#remote-update\n");
        var update = await watching.WaitAsync(TimeSpan.FromSeconds(20));
        watchClock.Stop();
        if (!update.Text.Contains("#remote-update")) throw new Exception("public watch did not deliver the changed chart");
        Console.WriteLine($"PASS: public change notification arrived in {watchClock.ElapsedMilliseconds} ms.");
    }
    var transferClock = System.Diagnostics.Stopwatch.StartNew();
    var downloaded = await RoomMediaCatalog.Download(publicClient, Path.Combine(probeMediaRoot.FullName, "download"),
        message => Console.WriteLine(message), includeVideo: false);
    if (!File.ReadAllBytes(Path.Combine(downloaded, "track.ogg")).SequenceEqual(mediaBytes))
        throw new Exception("public tunnel changed or truncated song media");
    if (File.Exists(Path.Combine(downloaded, "pv.mp4"))) throw new Exception("video blocked essential chart synchronization");
    transferClock.Stop();
    Console.WriteLine($"PASS: public tunnel synchronized 6.7 MB audio before video in {transferClock.Elapsed.TotalSeconds:0.0}s ({mediaBytes.Length / 1048576d / transferClock.Elapsed.TotalSeconds:0.00} MB/s).");
    await RoomMediaCatalog.DownloadVideos(publicClient, downloaded, message => Console.WriteLine(message));
    if (!File.ReadAllBytes(Path.Combine(downloaded, "pv.mp4")).SequenceEqual(videoBytes))
        throw new Exception("public tunnel changed or truncated background video");
    Console.WriteLine("PASS: public tunnel synchronized 1.1 MB video after audio.");
    var probeName = "Majdata-Internet-Probe-" + Guid.NewGuid().ToString("N")[..8];
    using var advertisement = new RoomPublicLobbyHost(() => new RoomListing("Probe", probeName, tunnel.Address, true, 1));
    RoomListing? found = null;
    for (int attempt = 0; attempt < 5 && found == null; attempt++)
    {
        await Task.Delay(1000);
        found = (await RoomLobby.Discover()).Rooms.FirstOrDefault(x => x.Name == probeName);
    }
    if (found == null) throw new Exception("public room directory did not discover host");
    Console.WriteLine("PASS: public tunnel relayed chart and room directory discovered host.");
    return;
}

// Optional integration probe: target only an Edit opened on a disposable test chart.
if (args.Length == 2 && args[0] == "--editor-pid")
{
    var initialChart = string.Join(Environment.NewLine, new[] { "&title=Collaboration Integration", "&artist=", "&first=0", "&des=", "", "&lv_5=14", "&inote_5=(190){4}1,2,3,4,E" }) + Environment.NewLine;
    using var probe = new RoomServer(0, "local-probe-password", initialChart, true);
    var endpoint = "127.0.0.1:" + probe.Port;
    Console.WriteLine(await RoomHandoff.Join(int.Parse(args[1]), endpoint, "local-probe-password"));
    var peer = new RoomClient(endpoint, "local-probe-password");
    var initialState = await peer.Exchange(0, "", true);
    await peer.Exchange(initialState.Revision, initialState.Text.Replace("1,2,3,4", "1b,2,3,4"));
    Console.WriteLine("READY: remote break note sent; waiting up to 120 seconds for LOCAL_SYNC_PROBE in the disposable chart.");
    var deadline = DateTime.UtcNow.AddSeconds(120);
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = await peer.Exchange(0, "", true);
        if (snapshot.Text.Contains("LOCAL_SYNC_PROBE"))
        {
            Console.WriteLine("PASS: real Edit received remote chart and sent local edit through the pet handoff pipe and room TCP protocol.");
            Console.WriteLine(snapshot.Text); return;
        }
        await Task.Delay(500);
    }
    Console.WriteLine("FAIL: no local edit arrived before integration timeout."); Environment.ExitCode = 1;
    return;
}

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Check(TextMerge.TryMerge("123456", "1a3456", "1234b6", out var merged) && merged == "1a34b6", "disjoint replacements");
Check(!TextMerge.TryMerge("123", "1a23", "1b23", out _), "same-position insertion must conflict");
Check(!TextMerge.TryMerge("123456", "16", "12b456", out _), "deletion covering peer edit");
Check(TextMerge.TryMerge("甲乙丙丁", "甲乙X丙丁", "甲乙丙丁Y", out merged) && merged == "甲乙X丙丁Y", "unicode insertions");
Check(TextMerge.TryMerge("abc", "abc", "", out merged) && merged == "", "whole-document deletion");
Check(TextMerge.TryMerge("abc", "aXc", "aXc", out merged) && merged == "aXc", "identical changes coalesce");

const string initial = "&title=Test\n&inote_5=1,2,3,4,E\n&inote_6=5,6,7,8,E\n";
const string secret = "test-password";
using var server = new RoomServer(0, secret, initial, loopbackOnly: true);
var address = "127.0.0.1:" + server.Port;
var a = new RoomClient(address, secret); var b = new RoomClient(address, secret);
using (var gateway = new RoomWebSocketGateway(server.Port))
using (var websocket = new System.Net.WebSockets.ClientWebSocket())
{
    await websocket.ConnectAsync(new Uri("ws://127.0.0.1:" + gateway.Port + "/room"), CancellationToken.None);
    using var relay = new RoomWebSocketStream(websocket);
    var nonce = await RoomWire.Read<string>(relay, CancellationToken.None);
    var payload = System.Text.Json.JsonSerializer.Serialize(new RoomRequest("read", 0, "", "websocket-probe"));
    await RoomWire.Write(relay, new SignedMessage(payload, RoomWire.Proof(secret, nonce, payload)), CancellationToken.None);
    var signed = await RoomWire.Read<SignedMessage>(relay, CancellationToken.None);
    Check(RoomWire.Verify(secret, nonce, signed) &&
          System.Text.Json.JsonSerializer.Deserialize<RoomReply>(signed.Payload)?.Text == initial,
          "WebSocket gateway preserves room authentication and chart data");
}
Check(RoomLobby.ValidPublicAddress("wss://example.trycloudflare.com/room") &&
      RoomLobby.ValidPublicAddress("wss://example.localhost.run/room") &&
      RoomLobby.ValidPublicAddress("wss://example.lhr.life/room") &&
      !RoomLobby.ValidPublicAddress("wss://example.com/room") &&
      !RoomLobby.ValidPublicAddress("wss://example.trycloudflare.com/other"), "public room address validation");
server.HostDifficulty = 5;
var firstA = await a.Exchange(0, "", true); var firstB = await b.Exchange(0, "", true);
Check(firstA.HostDifficulty == 5 && firstB.HostDifficulty == 5, "host difficulty included on join");
server.HostDifficulty = 4;
Check((await a.Exchange(0, "", true, new RoomPresence("", "Guest", 1, 0))).HostDifficulty == 4, "guest presence cannot override host difficulty");
Check(firstA.Text == initial && firstA.Room == firstB.Room, "initial room synchronization");
using var watcher = new RoomClient(address, secret, b.PeerId);
var liveUpdate = watcher.Watch(firstB.Revision, firstB.Room);
var edits = await Task.WhenAll(a.Exchange(0, initial.Replace("1,2", "1,2h[4:1]")), b.Exchange(0, initial.Replace("7,8", "7,8b")));
var watched = await liveUpdate.WaitAsync(TimeSpan.FromSeconds(3));
Check(watched.Revision > firstB.Revision && watched.Text != initial, "watch must deliver a changed chart without a poll interval");
Check(edits.All(e => !e.Conflict), "concurrent different difficulties");
var current = await a.Exchange(0, "", true);
Check(current.Text.Contains("2h[4:1]") && current.Text.Contains("8b"), "both users' edits survived");
long basis = current.Revision;
var accepted = await a.Exchange(basis, current.Text.Replace("&title=Test", "&title=Alice"));
var conflict = await b.Exchange(basis, current.Text.Replace("&title=Test", "&title=Bob"));
Check(conflict.Conflict && conflict.Text == accepted.Text, "overlapping edit must not overwrite server");
var noChange = await b.Exchange(conflict.Revision, conflict.Text);
Check(noChange.Revision == conflict.Revision, "read without spurious revision");
try { await new RoomClient(address, "wrong-password").Exchange(0, "", true); throw new Exception("wrong password accepted"); }
catch (IOException) { }
Check((await a.Exchange(0, "", true)).Text == accepted.Text, "auth failure changed room");
var reconnect = new RoomClient(address, secret);
Check((await reconnect.Exchange(0, "", true)).Text == accepted.Text, "reconnect snapshot");

var history = new RoomDocument("0");
for (int i = 1; i <= 130; i++) Check(!history.Apply(new("edit", i - 1, i.ToString(), "peer")).Conflict, "sequential revision");
Check(history.Apply(new("edit", 0, "old", "peer")).Conflict, "expired history must not overwrite");
try { history.Apply(new("edit", 130, new string('x', RoomWire.MaxText + 1), "peer")); throw new Exception("oversized chart accepted"); }
catch (IOException) { }

using var malformed = new TcpClient(); await malformed.ConnectAsync("127.0.0.1", server.Port);
var stream = malformed.GetStream(); await RoomWire.Read<string>(stream, CancellationToken.None);
await stream.WriteAsync(new byte[] { 127, 255, 255, 255 });
Check((await a.Exchange(0, "", true)).Text == accepted.Text, "malformed frame broke server");
int oldPort = server.Port; server.Dispose();
using var replacement = new RoomServer(oldPort, secret, "replacement", loopbackOnly: true);
try { await a.Exchange(0, "", true); throw new Exception("new room silently accepted"); }
catch (IOException e) { Check(e.Message.Contains("重建"), "wrong replacement error: " + e.Message); }
Console.WriteLine("PASS: disjoint/overlap/Unicode/deletion merge; real TCP concurrent clients; authentication; reconnect; bounded history; frame/text limits; room identity.");
Check(TextMerge.TryMerge("a\nb\nc\n", "A\nb\nC\n", "a\nB\nc\n", out var islandMerge) && islandMerge == "A\nB\nC\n", "separate edits surrounding a peer edit must merge");
var presenceDoc = new RoomDocument("text");
presenceDoc.Apply(new("read", 0, "", "alice", new("", "01", 5, 3)));
var peerView = presenceDoc.Apply(new("read", 0, "", "bob", new("", "02", 5, 1)));
Check(peerView.SelfColor != peerView.Cursors![0].Color, "room participants receive distinct server-assigned colors");
Check(presenceDoc.Apply(new("read", 0, "", "bob")).SelfColor == peerView.SelfColor, "room color remains stable");
Check(peerView.Cursors is { Length: 1 } && peerView.Cursors[0].Peer == "alice" && peerView.Cursors[0].Offset == 3, "peer caret synchronization");
var mediaRoot = Path.Combine(Path.GetTempPath(), "majdata-room-media-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(mediaRoot);
File.WriteAllBytes(Path.Combine(mediaRoot, "track.mp3"), Enumerable.Range(0, 700000).Select(i => (byte)(i % 251)).ToArray());
File.WriteAllText(Path.Combine(mediaRoot, "private.exe"), "must not transfer");
File.WriteAllText(Path.Combine(mediaRoot, "preview.mp4"), "only pv.mp4 is shared");
File.WriteAllText(Path.Combine(mediaRoot, "out.mp4"), "exports are private");
Directory.CreateDirectory(Path.Combine(mediaRoot, "extra"));
File.WriteAllText(Path.Combine(mediaRoot, "extra", "bg.png"), "nested files are private");
Directory.CreateDirectory(Path.Combine(mediaRoot, ".majdata"));
File.WriteAllText(Path.Combine(mediaRoot, ".majdata", "out.wav"), "generated audio must not transfer");
using var mediaServer = new RoomServer(0, secret, initial, true, mediaRoot);
var mediaClient = new RoomClient("127.0.0.1:" + mediaServer.Port, secret);
var mediaManifest = await mediaClient.Manifest();
// All socket work must finish while the editor's UI thread is busy. A context
// that never dispatches posted callbacks reproduces that condition without WPF.
var uiContext=new BusyUiContext();
var previousContext=SynchronizationContext.Current;
RoomServer isolatedServer; RoomWebSocketGateway isolatedGateway; Task<RoomReply> isolatedReply;
try {
    SynchronizationContext.SetSynchronizationContext(uiContext);
    isolatedServer=new RoomServer(0,secret,initial,true);
    isolatedGateway=new RoomWebSocketGateway(isolatedServer.Port);
    isolatedReply=new RoomClient("127.0.0.1:"+isolatedServer.Port,secret).Manifest();
} finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
using(isolatedServer) using(isolatedGateway) {
    Check((await isolatedReply.WaitAsync(TimeSpan.FromSeconds(3))).Text==initial && uiContext.Posts==0,
        "room server and client never require the busy editor UI to process socket IO");
    using var socket=new System.Net.WebSockets.ClientWebSocket();
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await socket.ConnectAsync(new Uri("ws://127.0.0.1:"+isolatedGateway.Port+"/room"),timeout.Token);
    using var relay=new RoomWebSocketStream(socket);
    var nonce=await RoomWire.Read<string>(relay,timeout.Token);
    var request=System.Text.Json.JsonSerializer.Serialize(new RoomRequest("manifest",0,"","ui-probe"));
    await RoomWire.Write(relay,new SignedMessage(request,RoomWire.Proof(secret,nonce,request)),timeout.Token);
    var response=await RoomWire.Read<SignedMessage>(relay,timeout.Token);
    Check(RoomWire.Verify(secret,nonce,response) && uiContext.Posts==0,"WebSocket bridge responds without UI dispatch");
}
Check(mediaManifest.Assets is { Length: 1 } && mediaManifest.Assets[0].Name == "track.mp3", "only song media is exposed");
Check(mediaManifest.AssetStreaming, "new hosts advertise continuous asset transfer; absent capability preserves old chunk protocol");
Check(mediaManifest.AssetBinaryStreaming,"new hosts advertise binary media without changing legacy support");
using(var frame=new MemoryStream()) {
    var data=new byte[]{0,1,2,250};
    await RoomWire.WriteAssetBlock(frame,secret,"nonce",100003,data,data.Length,CancellationToken.None);
    frame.Position=0;
    Check((await RoomWire.ReadAssetBlock(frame,secret,"nonce",100003,CancellationToken.None)).SequenceEqual(data),"binary block bytes and unaligned offset");
    frame.Position=0;
    try { await RoomWire.ReadAssetBlock(frame,secret,"nonce",100004,CancellationToken.None); throw new Exception("Wrong offset accepted"); }
    catch(IOException) { }
    frame.Position=36; frame.WriteByte(9); frame.Position=0;
    try { await RoomWire.ReadAssetBlock(frame,secret,"nonce",100003,CancellationToken.None); throw new Exception("Corrupt block accepted"); }
    catch(IOException) { }
}
var firstBlock = await mediaClient.Asset("track.mp3", 0);
Check(firstBlock.Content is { Length: <= 32768 } && firstBlock.Text == "", "rate-limited media blocks stay small and omit duplicate chart text");
var progress = new List<double>();
var progressMessages = new List<string>();
var download = await RoomMediaCatalog.Download(mediaClient, Path.Combine(mediaRoot, "received"), progressMessages.Add, progress.Add);
Check(progress.Count >= 1 && progress[^1] == 100 && progress.SequenceEqual(progress.OrderBy(v => v)), "media progress is monotonic and completes");
Check(progressMessages.Count <= 9 && progressMessages.Last().EndsWith("0.7/0.7 MB"), "progress text updates by 0.1 MB with exact rounded completion");
Check(File.ReadAllBytes(Path.Combine(download, "track.mp3")).SequenceEqual(File.ReadAllBytes(Path.Combine(mediaRoot, "track.mp3"))), "chunked song transfer and checksum");
Check((await mediaClient.Exchange(0, "", true)).Text == initial, "stream EOF does not desynchronize the next room request");
// Simulate the transport dying after the last verified media file. Reading the
// final snapshot must reconnect instead of aborting the completed song transfer.
var transportField = typeof(RoomClient).GetField("persistentStream", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
transportField.SetValue(mediaClient, new MemoryStream());
Check((await mediaClient.Exchange(0, "", true)).Text == initial,
    "final snapshot reconnects when the completed transfer left an EOF stream");
transportField.SetValue(mediaClient, new MemoryStream());
Check((await mediaClient.Manifest()).Room == mediaManifest.Room,
    "manifest also recovers a stale transport without changing the room identity");
var resumeRoot = Path.Combine(mediaRoot, "resume");
var resumeFolder = Path.Combine(resumeRoot, mediaManifest.Room);
Directory.CreateDirectory(resumeFolder);
File.WriteAllBytes(Path.Combine(resumeFolder, "track.mp3.partial"), File.ReadAllBytes(Path.Combine(mediaRoot, "track.mp3"))[..100003]);
await RoomMediaCatalog.Download(mediaClient, resumeRoot, _ => {});
Check(File.ReadAllBytes(Path.Combine(resumeFolder, "track.mp3")).SequenceEqual(File.ReadAllBytes(Path.Combine(mediaRoot, "track.mp3"))), "stream resumes from an unaligned partial offset and verifies the completed file");
using (var cancelledClient = new RoomClient("127.0.0.1:" + mediaServer.Port, secret))
{
    var cancelled = false;
    var blocks = 0;
    try
    {
        await cancelledClient.StreamAsset("track.mp3", 0, 700000, mediaManifest.Room, data =>
        {
            if (++blocks == 2) cancelledClient.Dispose();
            return Task.CompletedTask;
        });
    }
    catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { cancelled = true; }
    Check(cancelled && blocks == 2, "disconnect cancels continuous media transfer immediately");
}
var chosen = Path.Combine(mediaRoot, "chosen-folder");
Directory.CreateDirectory(chosen);
File.WriteAllText(Path.Combine(chosen, "maidata.txt"), "existing chart");
var firstCache = await RoomMediaCatalog.Download(mediaClient, chosen, _ => {});
var secondCache = await RoomMediaCatalog.Download(mediaClient, chosen, _ => {});
Check(firstCache == secondCache && firstCache != chosen, "rejoining reuses a resumable room cache without requiring an empty folder");
Check(File.ReadAllText(Path.Combine(chosen, "maidata.txt")) == "existing chart", "joining leaves existing local charts untouched");
var oldRoom = mediaClient.Room;
var switched = "&title=Next Song\n&inote_5=8,7,6,5,E\n";
mediaServer.SwitchSong(switched, mediaRoot, 4);
var afterSwitch = await mediaClient.Exchange(mediaManifest.Revision, "stale edits from old song");
Check(afterSwitch.Room != oldRoom && afterSwitch.Text == switched && afterSwitch.HostDifficulty == 4,
    "host song switch informs existing client without copying stale edits");
using var openRoom = new RoomServer(0, "", initial, true);
Check((await new RoomClient("127.0.0.1:" + openRoom.Port, "").Exchange(0, "", true)).Text == initial,
    "password-free room is joinable");
Check(!RoomLobby.ValidPublicAddress("127.0.0.1:" + openRoom.Port), "local addresses are not public rooms");
Console.WriteLine("PASS: multi-hunk live merge, remote caret, authenticated chunked song transfer.");
Console.WriteLine("PASS: busy UI independence and authenticated binary media.");

// A one-character edit in a large chart must stay a small wire message, retain
// the acknowledged basis, and merge a second user's concurrent edit correctly.
var largeText = "&title=Patch Probe\n&inote_5=" + new string('1', 120000) + "\n";
using (var patchServer = new RoomServer(0, secret, largeText, true))
using (var left = new RoomClient("127.0.0.1:" + patchServer.Port, secret))
using (var right = new RoomClient("127.0.0.1:" + patchServer.Port, secret))
{
    var patchBasis = await left.Exchange(0, "", true);
    await right.Exchange(0, "", true);
    var leftText = largeText.Insert(35, "2");
    var rightText = largeText.Insert(largeText.Length - 2, "3");
    Check(System.Text.Json.JsonSerializer.Serialize(RoomTextPatch.Between(largeText, leftText)).Length < 100,
        "one-character patch is compact, including UTF-16 offset");
    var one = await left.SyncText(patchBasis.Revision, largeText, leftText, null);
    var two = await right.SyncText(patchBasis.Revision, largeText, rightText, null);
    Check(one.Text == leftText && !two.Conflict && two.Text.Contains("2") && two.Text.Contains("3"),
        "compact changes use the existing concurrent merge");
    var final = await left.SyncText(one.Revision, one.Text, one.Text, null);
    Check(final.Text == two.Text && final.Revision == two.Revision, "unchanged polls reconstruct a compact server response");
    var waiting = left.WatchText(final.Revision, final.Room, final.Text);
    var updated = await right.SyncText(two.Revision, two.Text, two.Text.Insert(100, "😀"), null);
    Check((await waiting.WaitAsync(TimeSpan.FromSeconds(3))).Text == updated.Text, "watched UTF-16 patch applies to its captured revision");
    var late = await left.SyncText(final.Revision, final.Text, final.Text.Insert(500, "4"), null);
    Check(!late.Conflict && late.Text.Contains("😀") && late.Text.Contains("4"), "in-flight local edits are sent against their real acknowledged basis");
}
try { new RoomTextPatch(-1, 0, "x").Apply("abc"); throw new Exception("Bad patch accepted"); } catch (IOException) { }
Console.WriteLine("PASS: compact text synchronization, watched changes, in-flight edits and Unicode.");

var reuseSource = Path.Combine(mediaRoot, "reuse-source"); Directory.CreateDirectory(reuseSource);
File.WriteAllBytes(Path.Combine(reuseSource, "track.mp3"), new byte[] { 1, 2, 3, 4 });
File.WriteAllBytes(Path.Combine(reuseSource, "bg.png"), new byte[] { 5, 6, 7 });
File.WriteAllBytes(Path.Combine(reuseSource, "pv.mp4"), new byte[] { 8, 9 });
using (var reuseServer = new RoomServer(0, secret, initial, true, reuseSource))
using (var reuseClient = new RoomClient("127.0.0.1:" + reuseServer.Port, secret))
{
    var local = Path.Combine(mediaRoot, "local-copy"); Directory.CreateDirectory(local);
    foreach (var file in Directory.EnumerateFiles(reuseSource)) File.Copy(file, Path.Combine(local, Path.GetFileName(file)));
    var stale = Path.Combine(mediaRoot, "stale-copy"); Directory.CreateDirectory(stale);
    File.WriteAllBytes(Path.Combine(stale, "track.mp3"), new byte[] { 9, 9, 9, 9 });
    // Deliberately remove host assets: any attempted media transfer now fails.
    foreach (var file in Directory.EnumerateFiles(reuseSource)) File.Delete(file);
    var messages = new List<string>();
    var reused = await RoomMediaCatalog.Download(reuseClient, Path.Combine(mediaRoot, "reuse-cache"), messages.Add,
        includeVideo:false, localFolders:new[] { stale, local });
    await RoomMediaCatalog.DownloadVideos(reuseClient, reused, messages.Add, localFolders:new[] { local });
    Check(messages.Count(m => m.Contains("复用本地素材")) == 3 &&
        File.ReadAllBytes(Path.Combine(reused, "track.mp3")).SequenceEqual(new byte[] { 1, 2, 3, 4 }) &&
        File.Exists(Path.Combine(reused, "pv.mp4")), "audio, cover and PV reuse checks hashes and never downloads a stale match");
}
Console.WriteLine("PASS: verified local audio/cover/PV reuse without any media download.");

using (var lifecycleServer = new RoomServer(0, secret, initial, true))
using (var lifecycleHost = new RoomClient("127.0.0.1:" + lifecycleServer.Port, secret))
using (var lifecycleGuest = new RoomClient("127.0.0.1:" + lifecycleServer.Port, secret))
{
    var hostSnapshot = await lifecycleHost.Exchange(0, "", true, new("", "房主", 4, 0));
    var guestSnapshot = await lifecycleGuest.Exchange(0, "", true, new("", "成员", 4, 3));
    hostSnapshot = await lifecycleHost.Exchange(0, "", true);
    var hostWaiting = lifecycleHost.WatchText(hostSnapshot.Revision, hostSnapshot.Room, hostSnapshot.Text);
    var departedWaiting = lifecycleGuest.WatchText(guestSnapshot.Revision, guestSnapshot.Room, guestSnapshot.Text);
    await Task.Delay(100);
    await lifecycleGuest.Leave();
    var departure = await hostWaiting.WaitAsync(TimeSpan.FromSeconds(3));
    Check(departure.Peers == 1 && departure.Cursors!.Length == 0 && departure.Revision == hostSnapshot.Revision,
        "member departure must wake watch and remove member/cursor without editing TXT");
    var departedReply = await departedWaiting.WaitAsync(TimeSpan.FromSeconds(3));
    Check(departedReply.Left, "in-flight guest watch must not resurrect a departed member");
    Check((await lifecycleHost.Exchange(0, "", true)).Peers == 1,
        "member count must stay one after the departed guest's pending watch completes");
    guestSnapshot = await lifecycleGuest.Exchange(0, "", true, new("", "成员", 4, 3));
    var guestWaiting = lifecycleGuest.WatchText(guestSnapshot.Revision, guestSnapshot.Room, guestSnapshot.Text);
    var closure = lifecycleServer.CloseRoomAsync(lifecycleHost.PeerId);
    var closed = await guestWaiting.WaitAsync(TimeSpan.FromSeconds(3));
    Check(closed.Closed && closed.Revision == guestSnapshot.Revision && closed.Peers == 0,
        "host closure must reach the member as an authenticated closed reply before transport disposal");
    await closure.WaitAsync(TimeSpan.FromSeconds(3));
}
Console.WriteLine("PASS: member leave and host closure notify idle peers without any chart edit.");

var departureDocument = new RoomDocument(initial);
departureDocument.Apply(new RoomRequest("read", 0, "", "guest"));
departureDocument.Apply(new RoomRequest("leave", 0, "", "guest"));
Check(departureDocument.Apply(new RoomRequest("edit", 0, "late edit", "guest")).Left && departureDocument.PeerCount == 0,
    "late edits must not re-register a departed guest or modify the document");
var acknowledgedDocument = new RoomDocument(initial);
acknowledgedDocument.Apply(new RoomRequest("read", 0, "", "guest"));
acknowledgedDocument.CloseRoom();
Check(acknowledgedDocument.Apply(new RoomRequest("read", 0, "", "guest")).Closed && !acknowledgedDocument.CloseAcknowledged,
    "generating a closed reply must not be mistaken for successful delivery");
acknowledgedDocument.Apply(new RoomRequest("closedack", 0, "", "guest"));
Check(acknowledgedDocument.CloseAcknowledged, "only the client's receipt acknowledgment completes closure");
Console.WriteLine("PASS: departed peer tombstones and explicit closure receipt acknowledgments.");

sealed class BusyUiContext : SynchronizationContext {
    public int Posts;
    public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref Posts);
}
