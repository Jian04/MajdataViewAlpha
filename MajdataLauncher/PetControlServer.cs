using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MajdataLauncher;

internal sealed class PetControlServer : IDisposable
{
    private readonly int port;
    private readonly Action<PetControlRequest> onRequest;
    private CancellationTokenSource? cancellation;
    private TcpListener? listener;

    public PetControlServer(int port, Action<PetControlRequest> onRequest)
    {
        this.port = port;
        this.onRequest = onRequest;
    }

    public void Start()
    {
        if (cancellation != null)
            return;

        cancellation = new CancellationTokenSource();
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _ = AcceptLoopAsync(cancellation.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener != null)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine))
            return;

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var request = ParseRequestLine(requestLine);
        if (request != null)
            onRequest(request);

        var body = Encoding.UTF8.GetBytes("{\"ok\":true}");
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
    }

    private static PetControlRequest? ParseRequestLine(string requestLine)
    {
        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
            return null;

        var uri = new Uri("http://127.0.0.1" + parts[1]);
        if (!uri.AbsolutePath.Equals("/pet", StringComparison.OrdinalIgnoreCase))
            return null;

        var query = ParseQuery(uri.Query);
        query.TryGetValue("action", out var action);
        query.TryGetValue("message", out var message);
        double? angle = null;
        if (query.TryGetValue("angle", out var angleText) &&
            double.TryParse(angleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedAngle))
            angle = parsedAngle;

        return new PetControlRequest
        {
            Action = action ?? string.Empty,
            Message = message ?? string.Empty,
            Angle = angle
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            var value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
                values[key] = value;
        }
        return values;
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        listener?.Stop();
        cancellation?.Dispose();
    }
}
