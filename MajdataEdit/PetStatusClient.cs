using System.Net.Http;

namespace MajdataEdit;

internal static class PetStatusClient
{
    private static string Endpoint => MajdataCore.AlphaSession.PetEndpoint;
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(350) };
    private static readonly object Gate = new();
    private static string lastState = string.Empty;
    private static Task pending = Task.CompletedTask;

    public static void Notify(string action, string message)
    {
        var state = action + "\n" + message;
        lock (Gate)
        {
            if (string.Equals(lastState, state, StringComparison.Ordinal))
                return;
            lastState = state;
            // A later syntax or playback state must never arrive before the
            // earlier one merely because two HTTP requests raced each other.
            pending = pending.ContinueWith(_ => SendAsync(action, message, state),
                TaskScheduler.Default).Unwrap();
        }
    }

    private static async Task SendAsync(string action, string message, string state)
    {
        try
        {
            var url = $"{Endpoint}?action={Uri.EscapeDataString(action)}&message={Uri.EscapeDataString(message)}";
            using var response = await Client.GetAsync(url).ConfigureAwait(false);
        }
        catch
        {
            // The pet is optional and must never affect editor input or validation.
            lock (Gate)
                if (lastState == state) lastState = string.Empty;
        }
    }
}
