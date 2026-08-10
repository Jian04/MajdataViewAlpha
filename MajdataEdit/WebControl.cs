using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace MajdataEdit;

internal static class WebControl
{
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

    public static string RequestPOST(string url, string data = "")
    {
        try
        {
            var webRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(data, Encoding.UTF8)
            };

            var response = SharedClient.Send(webRequest);
            using var reader = new StreamReader(response.Content.ReadAsStream());

            return reader.ReadToEnd();
        }
        catch
        {
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
