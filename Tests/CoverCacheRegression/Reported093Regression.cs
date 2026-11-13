using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media;
using MajdataEdit;

internal static class Reported093Regression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    internal static void Run()
    {
        var folder = Path.GetFullPath("alpha/.codex-build/093-chart"); Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        using (var image = new System.Drawing.Bitmap(32, 32))
        {
            using var paint = System.Drawing.Graphics.FromImage(image); paint.Clear(System.Drawing.Color.Red);
            image.Save(Path.Combine(folder, "bg.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
            image.Save(Path.Combine(folder, "images", "head.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllText(Path.Combine(folder, "track.mp3"), "fixture");
        File.WriteAllText(Path.Combine(folder, "pv.mp4"), "fixture");
        File.WriteAllText(Path.Combine(folder, "unused.png"), "unused");
        Directory.CreateDirectory(Path.Combine(folder, "media", ".cache"));
        File.WriteAllText(Path.Combine(folder, "fx.mp4"), "overlay");
        File.WriteAllText(Path.Combine(folder, "media", "hit.wav"), "audio command");
        File.WriteAllText(Path.Combine(folder, "media", "clip.wav"), "timeline source");
        File.WriteAllText(Path.Combine(folder, "media", ".cache", "timeline_audio_unused.wav"), "cache");
        var timelineText = Newtonsoft.Json.JsonConvert.SerializeObject(new { Marker = "keep", Clips = new[] {
            new { Track = 1, SourcePath = Path.Combine(folder, "media", "clip.wav"), Duration = 1 }
        }});
        File.WriteAllText(Path.Combine(folder, "media_timeline.json"), timelineText);
        const string notes = "(120){4}1x~[1-5[1:1]]/2$x~[2-6[1:1]]/3h[2:1]~[3-7[1:1]]/4h[4:1]~[4-8[1:1]],1~[images/head.png]-5[1:1],1x~[images/head.png],1$x~[1-5[1:1]],";
        File.WriteAllText(Path.Combine(folder, "maidata.txt"), "&title=093\n&artist=test\n&first=0\n&lv_5=14\n&inote_5=" + notes + "\n|| ~[missing.png]\n<TEXT*(\"~[not-an-asset.png]\")><PVOVERLAY*(True,fx.mp4)><AUDIO*(True,media/hit.wav)>\n");
        var export = typeof(MainWindow).GetMethod("ExportChartPackage", All)!;
        var output = Path.Combine(folder, "chart.zip"); export.Invoke(null, new object[] { folder, output });
        using (var zip = ZipFile.OpenRead(output))
        {
            if (!zip.Entries.Select(entry => entry.FullName).Order().SequenceEqual(new[] { "bg.jpg", "images/head.png", "maidata.txt", "pv.mp4", "track.mp3", "fx.mp4", "media/hit.wav", "media/clip.wav", "media_timeline.json" }.Order()))
                throw new Exception("ZIP includes unused files or omits required files");
            using var reader = new StreamReader(zip.GetEntry("media_timeline.json")!.Open());
            var packed = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
            if (packed["Marker"]!.ToString() != "keep" || packed["Clips"]![0]!["SourcePath"]!.ToString() != "media/clip.wav" ||
                File.ReadAllText(Path.Combine(folder, "media_timeline.json")) != timelineText)
                throw new Exception("ZIP must preserve timeline values and use portable paths without modifying the source");
        }
        Console.WriteLine("093 ZIP PASS: exactly listed assets plus referenced image; comments, TEXT literals and unused files excluded.");
        File.Delete(Path.Combine(folder, "pv.mp4"));
        File.Delete(Path.Combine(folder, "media_timeline.json"));
        var process = typeof(MainWindow).Assembly.GetType("MajdataEdit.SimaiProcess")!;
        process.GetMethod("ReadData", All)!.Invoke(null, new object[] { Path.Combine(folder, "maidata.txt") });
        process.GetMethod("Serialize", All)!.Invoke(null, new object[] { notes, 0L });
        MainWindow.maidataDir = folder;
        var main = new MainWindow { editorSetting = new EditorSetting { SongDetailStyle = 0 } };
        typeof(MainWindow).GetField("selectedDifficulty", All)!.SetValue(main, 4);
        var majson = typeof(MainWindow).GetMethod("BuildSongDetailMajson", All)!.Invoke(main, null)!;
        var timings = ((System.Collections.IEnumerable)process.GetField("notelist", All)!.GetValue(null)!).Cast<object>().ToArray();
        var carriers = ((System.Collections.IEnumerable)timings[0].GetType().GetField("noteList")!.GetValue(timings[0])!).Cast<object>().ToArray();
        if ((double)carriers[2].GetType().GetField("holdTime")!.GetValue(carriers[2])! != 1 ||
            (double)carriers[3].GetType().GetField("holdTime")!.GetValue(carriers[3])! != .5)
            throw new Exception("Borrowed Hold lost its own duration");
        File.WriteAllText(Path.Combine(folder, "majdata.json"), Newtonsoft.Json.JsonConvert.SerializeObject(majson));
        Console.WriteLine("093 PARSE PASS: native Hold duration retained; carrier/skin fixtures generated.");
    }
}
