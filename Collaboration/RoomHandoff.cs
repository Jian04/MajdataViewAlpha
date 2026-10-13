using System.IO.Pipes;

namespace MajdataCollaboration;

public sealed record JoinRoomRequest(string Address, string Password, string Name = "Guest");
public static class RoomHandoff
{
    public static string PipeName(int pid) => "MajdataAlpha.JoinRoom." + pid;
    public static async Task<string> Join(int pid, string address, string password, string name = "Guest")
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var pipe = new NamedPipeClientStream(".", PipeName(pid), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, stop.Token);
        await RoomWire.Write(pipe, new JoinRoomRequest(address, password, name), stop.Token);
        return await RoomWire.Read<string>(pipe, stop.Token);
    }
}
