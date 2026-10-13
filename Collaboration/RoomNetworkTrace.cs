using System.IO;

namespace MajdataCollaboration;

internal static class RoomNetworkTrace
{
    private static readonly object gate = new();
    internal static void Write(string message)
    {
        try
        {
            lock (gate)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "collaboration-network.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
