using System.Diagnostics;
using System.IO.Pipes;

namespace MajdataCollaboration;

public sealed record PetDockRequest(long ProcessStart, double X, double Y);
public static class PetDock
{
    public static string Pipe(int pid) => "MajdataAlpha.PetDock." + pid;
    public static async Task<bool> Restore(int pid, long start, double x, double y)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", Pipe(pid), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await RoomWire.Write(pipe, new PetDockRequest(start, x, y), timeout.Token);
            return await RoomWire.Read<bool>(pipe, timeout.Token);
        }
        catch { return false; }
    }
}
