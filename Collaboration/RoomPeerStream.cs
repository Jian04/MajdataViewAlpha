using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using SIPSorcery.Net;

namespace MajdataCollaboration;

// SDP travels inside the existing authenticated room connection. The reliable,
// ordered data channel carries the same nonce/HMAC protocol, never a file URL.
internal sealed class RoomPeerStream : Stream
{
    private readonly RTCPeerConnection connection;
    private readonly RTCDataChannel channel;
    private readonly Channel<byte[]> incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128) {
        SingleReader=true, SingleWriter=true, FullMode=BoundedChannelFullMode.Wait
    });
    private readonly TaskCompletionSource<bool> opened=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte[] pending=Array.Empty<byte>();
    private int position;
    private bool disposed;
    private Exception? failure;
    internal Task Opened => opened.Task;
    internal RoomPeerStream(RTCPeerConnection connection, RTCDataChannel channel) {
        this.connection=connection; this.channel=channel;
        channel.onopen+=()=>opened.TrySetResult(true);
        channel.onclose+=()=>Fail(new IOException("直连已关闭。"));
        channel.onerror+=_=>Fail(new IOException("直连传输失败。"));
        channel.onmessage+=(_,type,bytes)=> {
            if(type!=DataChannelPayloadProtocols.WebRTC_Binary || bytes.Length>16384 ||
                !incoming.Writer.TryWrite(bytes.ToArray())) Fail(new IOException("直连数据缓冲无效。"));
        };
        connection.onconnectionstatechange+=state=> {
            if(state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                Fail(new IOException("直连已断开。"));
        };
        if(channel.IsOpened) opened.TrySetResult(true);
    }
    private void Fail(Exception error) {
        failure = error;
        opened.TrySetException(error); incoming.Writer.TryComplete(error);
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token=default) {
        if(bytes.IsEmpty) return 0;
        if (failure != null) throw new IOException("直连已中断。", failure);
        try {
            while(position==pending.Length) {
                if(!await incoming.Reader.WaitToReadAsync(token)) return 0;
                pending=await incoming.Reader.ReadAsync(token); position=0;
            }
        } catch(ChannelClosedException error) { throw new IOException("直连已中断。",error); }
        int count=Math.Min(bytes.Length,pending.Length-position);
        pending.AsMemory(position,count).CopyTo(bytes); position+=count; return count;
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token=default) {
        await opened.Task.WaitAsync(token);
        while(!bytes.IsEmpty) {
            token.ThrowIfCancellationRequested();
            while(channel.bufferedAmount>256*1024 && !disposed && failure == null) await Task.Delay(2,token);
            if(disposed || failure != null || !channel.IsOpened) throw new IOException("直连已关闭。", failure);
            int count=Math.Min(16384,bytes.Length); channel.send(bytes[..count].ToArray()); bytes=bytes[count..];
        }
    }
    protected override void Dispose(bool disposing) {
        if(disposing && !disposed) {
            disposed=true; incoming.Writer.TryComplete(); opened.TrySetCanceled(); connection.Close("room closed");
        }
        base.Dispose(disposing);
    }
    public override bool CanRead=>!disposed;
    public override bool CanWrite=>!disposed;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] b,int offset,int count)=>ReadAsync(b.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] b,int offset,int count)=>WriteAsync(b.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
    public override void Flush(){}
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long length)=>throw new NotSupportedException();

    internal static RTCPeerConnection CreateConnection() => new(new RTCConfiguration {
        iceServers=new List<RTCIceServer>{new(){urls="stun:stun.cloudflare.com:3478"},new(){urls="stun:stun.sipsorcery.com"}}
    });
    internal static async Task Gather(RTCPeerConnection pc,CancellationToken token) {
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(RTCIceGatheringState state) { if(state==RTCIceGatheringState.complete) ready.TrySetResult(true); }
        pc.onicegatheringstatechange+=Changed;
        try {
            Changed(pc.iceGatheringState);
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(2),token); } catch(TimeoutException){}
        } finally { pc.onicegatheringstatechange-=Changed; }
    }
    internal static async Task<RoomPeerStream> Connect(RoomClient signaling,string room,CancellationToken token) {
        var pc=CreateConnection(); RoomPeerStream? stream=null;
        try {
            var channel=await pc.createDataChannel("majdata-room"); stream=new RoomPeerStream(pc,channel);
            await Gather(pc,token);
            var offer=pc.createOffer(); await pc.setLocalDescription(offer);
            var reply=await signaling.PeerOffer(offer.sdp,room).WaitAsync(token);
            if(reply.Room!=room || reply.Text.Length>32768 || pc.setRemoteDescription(new RTCSessionDescriptionInit {
                type=RTCSdpType.answer,sdp=reply.Text})!=SetDescriptionResultEnum.OK) throw new IOException("直连协商失败。");
            await stream.Opened.WaitAsync(TimeSpan.FromSeconds(5),token); return stream;
        } catch { stream?.Dispose(); pc.Close("negotiation failed"); throw; }
    }
}

internal sealed class RoomPeerHost : IDisposable
{
    private readonly int roomPort;
    private readonly CancellationTokenSource stop=new();
    private readonly SemaphoreSlim slots=new(8,8);
    internal RoomPeerHost(int port)=>roomPort=port;
    internal async Task<string> Answer(string offer,CancellationToken token) {
        if(offer.Length>32768 || !offer.StartsWith("v=0") || !offer.Contains("m=application")) throw new IOException("直连协商格式无效。");
        if(!slots.Wait(0)) throw new IOException("直连协商繁忙。");
        RTCPeerConnection? pc=null;
        var ready=new TaskCompletionSource<RoomPeerStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            pc=RoomPeerStream.CreateConnection();
            pc.ondatachannel+=dc=> {
                if(dc.label!="majdata-room") { dc.close(); return; }
                var candidate=new RoomPeerStream(pc,dc);
                if(!ready.TrySetResult(candidate)) candidate.Dispose();
            };
            if(pc.setRemoteDescription(new RTCSessionDescriptionInit {type=RTCSdpType.offer,sdp=offer})!=SetDescriptionResultEnum.OK)
                throw new IOException("直连协商格式无效。");
            await RoomPeerStream.Gather(pc,token);
            var answer=pc.createAnswer(); await pc.setLocalDescription(answer);
            _=Bridge(pc,ready.Task); return answer.sdp;
        } catch(Exception error) { pc?.Close("negotiation failed"); slots.Release(); throw new IOException("直连协商失败。",error); }
    }
    private async Task Bridge(RTCPeerConnection pc,Task<RoomPeerStream> ready) {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(15));
        try {
            using var stream=await ready.WaitAsync(lifetime.Token);
            await stream.Opened.WaitAsync(lifetime.Token);
            using var client=new TcpClient {NoDelay=true};
            await client.ConnectAsync(IPAddress.Loopback,roomPort,lifetime.Token);
            lifetime.CancelAfter(Timeout.InfiniteTimeSpan);
            var upstream=client.GetStream();
            var inbound=stream.CopyToAsync(upstream,65536,lifetime.Token);
            var outbound=upstream.CopyToAsync(stream,65536,lifetime.Token);
            await Task.WhenAny(inbound,outbound); lifetime.Cancel();
            try { await Task.WhenAll(inbound,outbound); } catch(OperationCanceledException){}
        } catch(Exception) { }
        finally { pc.Close("room transport ended"); slots.Release(); }
    }
    public void Dispose()=>stop.Cancel();
}
