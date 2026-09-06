using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace MajdataEdit;

internal static class WebControl
{
    private const int ProtocolVersion = 1;
    public static string? LastError { get; private set; }

    private sealed class ViewResponse
    {
        public bool ok;
        public int protocolVersion;
        public string? error;
        public DroppedBeat[]? droppedBeats;
    }

    /// <summary>
    /// A beat View accepted as text but could not turn into a note. Validation
    /// runs on the chart text and View builds the note, so a legal beat can still
    /// go missing; these carry the position back so it can be marked in the editor
    /// the same way a syntax error is.
    /// </summary>
    internal sealed class DroppedBeat
    {
        public int line;
        public int column;
        public double time;
        public string? content;
        public string? reason;
    }

    /// <summary>
    /// Beats the last request reported as unbuildable. Empty after a request that
    /// built everything, so a stale report never keeps marking a fixed beat.
    /// </summary>
    public static IReadOnlyList<DroppedBeat> LastDroppedBeats { get; private set; } =
        Array.Empty<DroppedBeat>();

    // Do not set Timeout; keep HttpClient's 100-second default to match upstream 440.
    // After View receives a request, httpListen waits for the main-thread Update to finish, so response time equals View processing time.
    // Recording and OP synchronously instantiate the entire chart in View via LoadJsonImmediate; an initial cold load can take many seconds.
    // A previous two-second timeout falsely reported a disconnected port during cold recording loads even though View had started recording.
    // Retrying then cleared or reloaded the chart and interrupted the video pipe. An absent View causes an immediate connection-refused error,
    // so timeout detection is unnecessary in that case and a two-second timeout only creates false failures.
    //
    // Disable the system proxy. Proxy tools such as Clash or v2rayN can intercept requests to 127.0.0.1 unless local addresses are excluded.
    // When the proxy cannot reach the target port, it may return an HTTP 200 error page instead of closing the connection.
    // The response then neither throws nor equals the "ERROR" sentinel, so Edit incorrectly continues local playback or recording
    // while View receives nothing. Symptoms include Edit producing audio while View is idle, a recording reporting completion
    // without a video, and no disconnected-port prompt. View's LocalHttp also sets HttpClientHandler.UseProxy=false;
    // this side must do the same.
    private static readonly HttpClient SharedClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        return new HttpClient(handler);
    }

    /// <summary>
    /// Set while the editor is closing so in-flight timers stop dialing View.
    /// </summary>
    public static bool IsShuttingDown;

    public static string RequestPOST(string url, string data = "")
    {
        LastError = null;
        if (IsShuttingDown)
            return "ERROR";
        try
        {
            using var webRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(data, Encoding.UTF8)
            };

            using var response = SharedClient.Send(webRequest);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var body = reader.ReadToEnd();
            var result = JsonConvert.DeserializeObject<ViewResponse>(body);
            LastDroppedBeats = result?.droppedBeats ?? Array.Empty<DroppedBeat>();
            if (!response.IsSuccessStatusCode)
            {
                LastError = result?.error ??
                            $"View returned HTTP {(int)response.StatusCode}.";
                return "ERROR";
            }
            if (result is
            {
                ok: true,
                protocolVersion: ProtocolVersion
            })
                return body;
            LastError = result?.protocolVersion != ProtocolVersion
                ? $"View protocol {result?.protocolVersion ?? 0} does not match Edit protocol {ProtocolVersion}."
                : result?.error ?? "View rejected the request.";
            return "ERROR";
        }
        catch (System.Exception exception)
        {
            LastError = exception.Message;
            return "ERROR";
        }
    }

    public static string RequestGETAsync(string url)
    {
        var executingAssembly = Assembly.GetExecutingAssembly();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", $"{executingAssembly.GetName().Name!} / {executingAssembly.GetName().Version!.ToString(3)}");
        var response = SharedClient.Send(request);
        using var reader = new StreamReader(response.Content.ReadAsStream());

        return reader.ReadToEnd();
    }
}
