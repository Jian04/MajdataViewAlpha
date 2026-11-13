using System.Text.RegularExpressions;
using System.Xml.Linq;
using MajdataCore;

internal static class Wiki095Audit
{
    internal static void Run()
    {
        var source = XDocument.Load("MajdataEdit/Langs/Langs.zh-CN.resx").Root!.Elements("data")
            .Single(entry => (string?)entry.Attribute("name") == "AlphaHelpStructuredText").Element("value")!.Value;
        var lines = MajdataEdit.AlphaGuideFormatter.Format(source);
        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>(); var examples = 0;
        foreach (var line in lines)
        {
            if (!line.StartsWith("S ") && !line.StartsWith("E ")) continue;
            foreach (Match match in Regex.Matches(line, "<([A-Za-z]+)\\*")) described.Add(match.Groups[1].Value);
            if (!line.StartsWith("E ")) continue;
            var code = line[2..];
            for (var at = 0; at < code.Length; at++)
            {
                if (code[at] != '<' || !AlphaCommandBoundary.TryGetToken(code, at, out var token)) continue;
                var text = code.Substring(at + 1, token.length - 2); examples++;
                if (!AlphaCommandGrammar.TryValidate(text, 120, out var error)) errors.Add(text + " => " + error);
                at += token.length - 1;
            }
        }
        var missing = AlphaCommandGrammar.Commands.Where(command => !described.Contains(command.name)).Select(command => command.name).ToArray();
        Console.WriteLine($"095 WIKI AUDIT: {examples} command examples; missing commands: {string.Join(",", missing)}");
        var simaiSource = Regex.Match(File.ReadAllText("MajdataEdit/SubWindow/AlphaSyntaxHelp.xaml.cs"),
            "private const string SimaiGuide = @\"(.*?)\";", RegexOptions.Singleline).Groups[1].Value.Replace("\"\"", "\"");
        var noteExamples = 0;
        foreach (var line in lines.Concat(MajdataEdit.AlphaGuideFormatter.Format(simaiSource)))
        {
            if (!line.StartsWith("E ")) continue;
            var example = line[2..];
            if (!Regex.IsMatch(example, @"^[0-9ABCDEFX(]") || example.Contains('<') && AlphaCommandBoundary.TryGetToken(example, example.IndexOf('<'), out _)) continue;
            MajdataEdit.SimaiProcess.ClearData();
            MajdataEdit.SimaiProcess.Serialize("(120){4}" + example + (example.EndsWith(',') ? "" : ","));
            var invalid = MajdataEdit.SimaiProcess.notelist.Where(point => !string.IsNullOrEmpty(point.noteParseError)).ToArray();
            noteExamples++;
            foreach (var point in invalid) errors.Add(example + " => " + point.noteParseError);
        }
        Console.WriteLine($"095 WIKI NOTE AUDIT: {noteExamples} note examples");
        foreach (var error in errors) Console.WriteLine("EXAMPLE AUDIT: " + error);
        if (missing.Length > 0 || errors.Count > 0) Environment.ExitCode = 1;
    }
}
