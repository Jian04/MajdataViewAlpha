using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace MajdataEdit;

internal static class WebControl
{
    // 不设 Timeout:沿用 HttpClient 默认(100s),与上游 440 一致。
    // View 收到请求后,httpListen 要等主线程 Update 处理完才回包,所以响应时间 = View 处理耗时;
    // 录制/OP 在 View 端同步实例化整张谱面(LoadJsonImmediate),首次冷加载可达十几秒。
    // 曾有人把超时砍到 2s,导致录制冷加载即误报"端口断开"(而 View 其实已开始录制,用户重试
    // 又会清空/重载谱面打断视频管道 → Pipe is broken)。注意:View 未启动是 connection
    // refused,会立即失败,不靠超时检测,所以 2s 超时纯属误伤、不该加。
    public static string RequestPOST(string url, string data = "")
    {
        try
        {
            using var client = new HttpClient();

            var webRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(data, Encoding.UTF8)
            };

            var response = client.Send(webRequest);
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
        
        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", $"{executingAssembly.GetName().Name!} / {executingAssembly.GetName().Version!.ToString(3)}");
        var response = httpClient.Send(request);
        using var reader = new StreamReader(response.Content.ReadAsStream());

        return reader.ReadToEnd();
    }
}
