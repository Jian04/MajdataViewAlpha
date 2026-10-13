using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MajdataSessionUi;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;

namespace MajdataEdit;

public partial class MainWindow
{
    private bool checkingUpdate;
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataEditAlpha/0.6.0");
            http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25));
            var releases = new List<JObject>();
            for (var page = 1; ; page++)
            {
                using var response = await http.GetAsync($"https://api.github.com/repos/Jian04/MajdataViewAlpha/releases?per_page=100&page={page}", deadline.Token);
                response.EnsureSuccessStatusCode();
                var batch = JArray.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
                releases.AddRange(batch.OfType<JObject>());
                if (batch.Count < 100) break;
            }
            var latest = AlphaReleaseVersion.Latest(releases);
            var tag = latest.Tag;
            var version = latest.Version;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 6, 0);
            var newer = new Version(version.Major, version.Minor, Math.Max(0, version.Build)) > new Version(current.Major, current.Minor, current.Build);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(SessionTheme.Heading(newer ? $"有更新的版本：{tag}" : "当前已是最新版本！",
                $"当前版本：v{current.Major}.{current.Minor}.{current.Build} · GitHub 最新发行：{tag}"));
            var link = SessionTheme.Button("在 GitHub 查看发行版本");
            link.Click += (_, _) => MajnetPanel.OpenUrl(latest.Url); panel.Children.Add(link);
            var window = new UpdateResultPanel { Content = panel };
            if (toolTabs.TryGetValue(typeof(UpdateResultPanel), out var old)) old.Window.Close();
            ShowTool(window, "检查更新");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException or Newtonsoft.Json.JsonException)
        { MessageBox.Show(this, "检查更新失败，请稍后重试。\n" + ex.Message, "检查更新"); }
        finally { checkingUpdate = false; }
    }
}
internal sealed class UpdateResultPanel : Window { }

internal static class AlphaReleaseVersion
{
    internal static (string Tag, Version Version, string Url) Latest(IEnumerable<JObject> releases)
    {
        var versions = releases.Where(release => (bool?)release["draft"] != true)
            .Select(release => (Tag: (string?)release["tag_name"] ?? "", Url: (string?)release["html_url"] ?? ""))
            .Select(release => (release.Tag, Version: Parse(release.Tag), release.Url))
            .Where(release => release.Version != null).OrderByDescending(release => release.Version).ToArray();
        if (versions.Length == 0) throw new FormatException("GitHub 尚无可识别的公开发行版本。");
        var latest = versions[0];
        var url = latest.Url.StartsWith("https://github.com/Jian04/MajdataViewAlpha/releases/", StringComparison.OrdinalIgnoreCase)
            ? latest.Url : "https://github.com/Jian04/MajdataViewAlpha/releases";
        return (latest.Tag, latest.Version!, url);
    }
    internal static Version? Parse(string tag)
    {
        var match = Regex.Match(tag.Trim(), @"^(?:alpha[.-]?)?v?(\d+\.\d+\.\d+(?:\.\d+)?)(?:[-+].*)?$", RegexOptions.IgnoreCase);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
            ? new Version(version.Major, version.Minor, version.Build) : null;
    }
}
