using System.IO;
using System.IO.Compression;
using System.Reflection;
using MajdataEdit;

internal static class Package098Regression
{
    internal static void Run()
    {
        var root = Path.GetFullPath("alpha/.codex-build/098-package-" + Guid.NewGuid().ToString("N"));
        var chart = Path.Combine(root, "chart");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(Path.Combine(chart, "Fonts"));
        Directory.CreateDirectory(Path.Combine(chart, "media", ".cache"));
        Directory.CreateDirectory(Path.Combine(external, "second"));
        var firstFont = Path.Combine(external, "caption.ttf");
        var secondFont = Path.Combine(external, "second", "caption.ttf");
        File.WriteAllText(firstFont, "font one"); File.WriteAllText(secondFont, "font two");
        File.WriteAllText(Path.Combine(chart, "Fonts", "local.otf"), "local font");
        File.WriteAllText(Path.Combine(chart, "media", "clip.wav"), "referenced");
        File.WriteAllText(Path.Combine(chart, "media", ".cache", "unused.wav"), "unused cache");
        File.WriteAllText(Path.Combine(chart, "unused.ttf"), "unused font");
        File.WriteAllText(Path.Combine(external, "clip.wav"), "legacy external audio");
        var timeline = Newtonsoft.Json.JsonConvert.SerializeObject(new { Clips = new[] {
            new { SourcePath = "media/clip.wav", Volume = .7 },
            new { SourcePath = Path.Combine(external, "clip.wav"), Volume = .4 }
        }});
        File.WriteAllText(Path.Combine(chart, "media_timeline.json"), timeline);
        var notes = "&title=package\n&inote_5=(120){4}<TEXT*(\"font=missing.ttf\",2,font=\"" + firstFont + "\")>1,\n" +
            "<TEXT*(\"second\",2,0.1,0.2,40,\"" + secondFont + "\")>2,\n" +
            "&inote_6=<TEXT*(\"local\",2,font=Fonts/local.otf)>3,\n|| <TEXT*(\"ignored\",2,font=missing.ttf)>\n";
        var maidata = Path.Combine(chart, "maidata.txt");
        File.WriteAllText(maidata, notes);
        var export = typeof(MainWindow).GetMethod("ExportChartPackage", BindingFlags.Static | BindingFlags.NonPublic)!;
        var zipPath = Path.Combine(root, "chart.zip");
        export.Invoke(null, new object[] { chart, zipPath });
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var expected = new[] { "maidata.txt", "media_timeline.json", "media/clip.wav", "media/clip-2.wav", "Fonts/local.otf", "Fonts/caption.ttf", "Fonts/caption-2.ttf" };
            if (!zip.Entries.Select(entry => entry.FullName).Order().SequenceEqual(expected.Order())) throw new Exception("ZIP assets mismatch");
            using var reader = new StreamReader(zip.GetEntry("maidata.txt")!.Open());
            var packed = reader.ReadToEnd();
            if (!packed.Contains("font=\"Fonts/caption.ttf\"") || !packed.Contains(",\"Fonts/caption-2.ttf\")") ||
                !packed.Contains("font=missing.ttf\"") || !packed.Contains("font=missing.ttf)>")) throw new Exception("TEXT font references or literal/comment boundaries changed");
            using var timelineReader = new StreamReader(zip.GetEntry("media_timeline.json")!.Open());
            var packedTimeline = Newtonsoft.Json.Linq.JObject.Parse(timelineReader.ReadToEnd());
            if (packedTimeline["Clips"]![1]!["SourcePath"]!.ToString() != "media/clip-2.wav" || (double)packedTimeline["Clips"]![1]!["Volume"]! != .4)
                throw new Exception("External timeline source was not made portable");
        }
        if (File.ReadAllText(maidata) != notes || File.ReadAllText(Path.Combine(chart, "media_timeline.json")) != timeline)
            throw new Exception("Export modified source files");
        File.AppendAllText(maidata, "<TEXT*(\"missing\",2,font=absent.ttf)>1,");
        try { export.Invoke(null, new object[] { chart, Path.Combine(root, "missing.zip") }); throw new Exception("Missing font accepted"); }
        catch (TargetInvocationException error) when (error.InnerException is FileNotFoundException) { }
        Console.WriteLine("098 PACKAGE PASS: media JSON, sources, named/positional/local fonts, duplicate filenames, all difficulties, comments, missing files and unchanged originals.");
    }
}
