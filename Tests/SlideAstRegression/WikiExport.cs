using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

namespace MajdataEdit;

internal static class WikiExport
{
    private sealed record Example(string id, string label, string document, string heading, string code);
    internal static void Run()
    {
        Directory.CreateDirectory("wiki");
        var alpha = XDocument.Load("MajdataEdit/Langs/Langs.zh-CN.resx").Root!.Elements("data")
            .First(data => (string?)data.Attribute("name") == "AlphaHelpStructuredText").Element("value")!.Value;
        var simai = Regex.Match(File.ReadAllText("MajdataEdit/SubWindow/AlphaSyntaxHelp.xaml.cs"),
            "private const string SimaiGuide = @\"(.*?)\";", RegexOptions.Singleline).Groups[1].Value.Replace("\"\"", "\"");
        if (simai.Length == 0) throw new InvalidDataException("Simai guide source was not found.");
        var examples = JsonSerializer.Deserialize<Example[]>(File.ReadAllText(".codex-build/wiki-examples/manifest.json"))!
            .Where(example => example.id is not ("comments" or "end" or "audio" or "record-range")).ToArray();
        WriteGuide("wiki/Simai.md", simai, examples.Where(example => example.document == "Simai").ToArray());
        WriteGuide("wiki/Alpha.md", alpha, examples.Where(example => example.document == "Alpha").ToArray());
        var gallery = new StringBuilder("# 语法 GIF 示例\n\n黑色背景、默认判定线与判定区；画面由 View 实际渲染。GIF 不包含声音。\n\n注释、导出标记和附加音频使用文字示例说明，不附 GIF。\n\n");
        foreach (var example in examples)
            gallery.AppendLine($"## {example.label}\n\n```simai\n{example.code}\n```\n\n![{example.label}](gifs/{example.id}.gif)\n");
        File.WriteAllText("wiki/GIF-Examples.md", gallery.ToString(), new UTF8Encoding(false));
        File.Copy("SLIDECODE.md", "wiki/SlideCode.md", true);
        File.WriteAllText("wiki/Home.md", """
            # MajdataViewAlpha v0.6.1 写谱文档

            - [Simai 语法](Simai.md)：谱面结构、BPM、分音、音符、Slide 启动与持续时间。
            - [Alpha 语法](Alpha.md)：独立音符流、拍号、继承、大 P/Q、噪域、显示、滤镜与媒体。
            - [SlideCode](SlideCode.md)：MajdataPlay 的 A/B/C 节点、P/Q 绕圈、K 终点和判定区。
            - [GIF 示例](GIF-Examples.md)：每条显示命令、滤镜与补充音符语法的实际画面。

            普通、D 区与 TouchSlide 的大 P/Q 是 Alpha 扩展，例如 `A1P9A5[8:1]`、`1P5A8-B4[8:1]`。
            SlideCode 以 K 终点区分，例如 `1A3B5K7[8:1]`；其中 A/B 是几何节点。

            文档与 Edit 内中文语法帮助使用同一份内容。重新导出：

            ```powershell
            dotnet run --project Tests/SlideAstRegression/SlideAstRegression.csproj -- --export-wiki
            ```
            """ + "\n", new UTF8Encoding(false));
        Console.WriteLine("Wiki exported: wiki/Home.md, Simai.md, Alpha.md, SlideCode.md");
    }

    private static void WriteGuide(string path, string source, Example[] examples)
    {
        var result = new StringBuilder();
        var inCode = false;
        foreach (var raw in AlphaGuideFormatter.Format(source))
        {
            var line = raw.TrimEnd();
            var isCode = line.StartsWith("S ") || line.StartsWith("E ");
            if (isCode != inCode)
            {
                result.AppendLine(isCode ? "\n```text" : "```\n");
                inCode = isCode;
            }
            if (isCode) { result.AppendLine(line[2..]); continue; }
            if (line.StartsWith("! ")) line = "> " + line[2..];
            line = Regex.Replace(line, @"(?<!`)<[^<>\r\n]+>(?!`)", match => "`" + match.Value + "`");
            result.AppendLine(line);
            if (line.Length > 0) result.AppendLine();
        }
        if (inCode) result.AppendLine("```");
        var markdown = result.ToString().Replace("\r\n", "\n").Trim();
        markdown = Regex.Replace(markdown, @"(?ms)^### ([^\n]+)\n(.*?)(?=^#{1,3} |\z)", match =>
        {
            var title = match.Groups[1].Value;
            var images = examples.Where(example => title.StartsWith(example.heading, StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(title, @"\b" + Regex.Escape(example.label) + @"\b", RegexOptions.IgnoreCase));
            return match.Value.TrimEnd() + "\n\n" + string.Concat(images.Select(example => $"![{example.label}](gifs/{example.id}.gif)\n\n"));
        });
        File.WriteAllText(path, Regex.Replace(markdown, "\n{3,}", "\n\n") + "\n", new UTF8Encoding(false));
    }
}
