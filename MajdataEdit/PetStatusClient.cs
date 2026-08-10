using System.Net.Http;

namespace MajdataEdit;

internal static class PetStatusClient
{
    private const string Endpoint = "http://127.0.0.1:8015/pet";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMilliseconds(350) };
    private static string lastState = string.Empty;

    public static void Notify(string action, string message)
    {
        var state = action + "\n" + message;
        if (string.Equals(lastState, state, StringComparison.Ordinal))
            return;
        lastState = state;
        _ = SendAsync(action, message);
    }

    private static async Task SendAsync(string action, string message)
    {
        try
        {
            var url = $"{Endpoint}?action={Uri.EscapeDataString(action)}&message={Uri.EscapeDataString(message)}";
            using var response = await Client.GetAsync(url).ConfigureAwait(false);
        }
        catch
        {
            // The pet is optional and must never affect editor input or validation.
        }
    }
}
