using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MajdataEdit;
using Newtonsoft.Json;

internal static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread] static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var panel = new EditorSettingPanel(page: EditorSettingPanel.SettingsPage.View);
        var style = (ComboBox)panel.FindName("SongDetailStyle");
        var toggle = (CheckBox)panel.FindName("DisableAutomaticSongDetailRefresh");
        Check(Grid.GetColumn(toggle) == 1 && Grid.GetRow(toggle) == Grid.GetRow((CheckBox)panel.FindName("SmoothSlideAnime")),
            "refresh switch must align with smooth slide in the right column");
        style.SelectedIndex = 0;
        Check(toggle.Visibility == Visibility.Visible && !toggle.IsEnabled && toggle.Opacity < 1,
            "refresh switch must remain visible but disabled for the original cover");
        style.SelectedIndex = 1;
        Check(toggle.Visibility == Visibility.Visible && toggle.IsEnabled && toggle.Opacity == 1,
            "refresh switch must be enabled for official cover style");
        typeof(EditorSettingPanel).GetField("settingsApplied", Flags)!.SetValue(panel, true);
        panel.Close();
        string folder = Path.GetFullPath(".codex-build/cover-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        MainWindow.maidataDir = folder;
        var main = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        main.editorSetting = new EditorSetting { DisableAutomaticSongDetailRefresh = true };
        Check(!new EditorSetting().DisableAutomaticSongDetailRefresh, "automatic refresh must remain enabled by default");
        var dataType = typeof(MainWindow).Assembly.GetType("MajdataEdit.Majson")!;
        var process = typeof(MainWindow).Assembly.GetType("MajdataEdit.SimaiProcess")!;
        object ParseChart(string text)
        {
            process.GetField("first")!.SetValue(null, 0f);
            process.GetMethod("Serialize")!.Invoke(null, new object[] { "(126){4}" + text, 0L });
            var parsed = Activator.CreateInstance(dataType)!;
            var timings = process.GetField("notelist")!.GetValue(null)!;
            foreach (var timing in (System.Collections.IEnumerable)timings)
                timing.GetType().GetField("noteList")!.SetValue(timing,
                    timing.GetType().GetMethod("getNotes")!.Invoke(timing, null));
            dataType.GetField("timingList")!.SetValue(parsed, timings);
            foreach (var field in dataType.GetFields().Where(f => f.Name.EndsWith("Table")))
                field.SetValue(parsed, process.GetField(field.Name)!.GetValue(null));
            var meters = (System.Collections.ICollection)process.GetField("meterTable")!.GetValue(null)!;
            dataType.GetField("hasAlphaEditorSyntax")!.SetValue(parsed, meters.Count > 0);
            return parsed;
        }
        var classify = typeof(MainWindow).GetMethod("IsAlphaChart", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cleanLevel = typeof(MainWindow).GetMethod("CleanLevelForCache", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (source, expected) in new[] { ("1", "1"), ("14+", "14+"), ("15-", "15-"),
            ("14.9", "14+"), ("14A", "14"), ("未知∞", ""), ("", "") })
            Check((string)cleanLevel.Invoke(null, new object[] { source })! == expected, "level truncation: " + source);
        foreach (var source in new[] { "1,2h[8:1],3-7[4:1],", "A1/Ch[4:1]," })
            Check(!(bool)classify.Invoke(null, new[] { ParseChart(source) })!, "SD/DX syntax must keep its original badge: " + source);
        foreach (var source in new[] { "<SV*2>1,", "<COLOR*00BBFF>1,", "<JUDGELINE*(0)>1,",
            "XD1[8:1],", "1d-5d[4:1],", "A1>A4[4:1],", "1rp5[4:1],", "8P91K1[4:1],",
            "1m,", "1f,", "1~[note.png]~[B1-B4[8:3]],", "@3/4\n1," })
            Check((bool)classify.Invoke(null, new[] { ParseChart(source) })!, "Alpha syntax did not select the Alpha badge: " + source);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(@"C:\Users\sjtu_\Downloads\115012_[僕]二次元凶.zip"))
        {
            using var reader = new StreamReader(zip.Entries.Single(e => e.Name.Equals("maidata.txt", StringComparison.OrdinalIgnoreCase)).Open());
            process.GetMethod("ReadText")!.Invoke(null, new object[] { reader.ReadToEnd() });
            var fumen = ((string[])process.GetField("fumens")!.GetValue(null)!)[2];
            var parsed = ParseChart(fumen);
            File.WriteAllText(".codex-build/oct11-overlap-chart.json", JsonConvert.SerializeObject(dataType.GetField("timingList")!.GetValue(parsed)));
        }
        var data = JsonConvert.DeserializeObject("{\"songDetailStyle\":1,\"diffNum\":4,\"title\":\"Cover test\",\"artist\":\"Camellia\",\"designer\":\"Test\",\"level\":\"14+\",\"wholeBpm\":\"222\"}", dataType)!;
        void Set(string name, object value) => dataType.GetField(name)!.SetValue(data, value);
        var ensure = typeof(MainWindow).GetMethod("EnsureSongDetailCache", Flags)!;
        void Bake(bool force) => ensure.Invoke(main, new object[] { data, force });
        bool MissingError()
        {
            try { Bake(false); return false; }
            catch (TargetInvocationException exception) { return exception.InnerException is FileNotFoundException; }
        }
        Check(MissingError(), "disabled refresh with no cache must explicitly fail");
        string figure = Path.Combine(folder, "figure");
        string existing = Path.Combine(figure, "songdetail_master-oldsession.png");
        File.WriteAllText(existing, "preserve exact bytes");
        var stamp = File.GetLastWriteTimeUtc(existing);
        Bake(false);
        Check(File.ReadAllText(existing) == "preserve exact bytes" && File.GetLastWriteTimeUtc(existing) == stamp,
            "frozen cover must reuse earlier session cache without replacing it");
        Set("title", "Changed metadata");
        Bake(false);
        Check(File.ReadAllText(existing) == "preserve exact bytes", "metadata changes must not replace a frozen cache");
        Set("diffNum", 3);
        Check(MissingError(), "another difficulty must not reuse the wrong difficulty cover");
        Set("diffNum", 4);
        Bake(true);
        string rendered = Path.Combine(figure, MajdataCore.AlphaSession.FileName("songdetail_master") + ".png");
        Check(File.ReadAllBytes(rendered).Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
            "manual refresh must render a real PNG even while automatic refresh is disabled");
        using (var card = new System.Drawing.Bitmap(rendered))
            Check(card.Width == 682 && card.Height == 1176, "card text must be baked at twice the display resolution");
        using (var overlay = new System.Drawing.Bitmap(Path.ChangeExtension(rendered, null) + "_overlay.png"))
            Check(overlay.Width == 341 && overlay.Height == 588, "level artwork must retain its original logical canvas");
        File.WriteAllText(rendered, "corrupt cache");
        Bake(true);
        Check(File.ReadAllBytes(rendered)[0] == 137, "manual refresh must replace corrupt cache even with matching signature");
        string json = JsonConvert.SerializeObject(main.editorSetting);
        Check(JsonConvert.DeserializeObject<EditorSetting>(json)!.DisableAutomaticSongDetailRefresh,
            "refresh switch must persist through settings serialization");
        Set("title", "Spin Eternally");
        Set("artist", "Camellia");
        Set("designer", "Ori-DX vs LongStar");
        using (var font = new System.Drawing.Text.PrivateFontCollection())
        using (var bitmap = new System.Drawing.Bitmap(400, 52))
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            font.AddFontFile(Path.Combine(AppContext.BaseDirectory, "Resources/Fonts/GenSenRounded2TW-R.otf"));
            Check(font.Families[0].Name.Contains("源泉") && !font.Families[0].Name.Contains("Bold"),
                "Chinese designer must use the regular GenSen rounded face");
            typeof(MainWindow).GetMethod("ConfigureCardGraphics", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { graphics, 2 });
            typeof(MainWindow).GetMethod("DrawFitText", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { graphics, "测试谱师／譜面作者", font, new System.Drawing.RectangleF(0, 0, 200, 26),
                    16f, 10f, System.Drawing.Color.White, System.Drawing.StringAlignment.Near, false });
            var visible = 0;
            var antialiased = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).A;
                if (alpha > 0) visible++;
                if (alpha > 0 && alpha < 255) antialiased++;
            }
            Check(visible > 1500, "CJK designer must render inside the original logical 26px row");
            Check(antialiased > 500, "CJK text must use grayscale antialiasing instead of hard pixel edges");
            bitmap.Save(".codex-build/oct11-designer-cjk.png");
        }
        var condensedText = typeof(MainWindow).GetMethod("DrawCondensedText", BindingFlags.Static | BindingFlags.NonPublic)!;
        (int Top, int Bottom) TextBounds(string text, string fontName, float size, System.Drawing.RectangleF rect, float? baseline)
        {
            using var fonts = new System.Drawing.Text.PrivateFontCollection();
            fonts.AddFontFile(Path.Combine(AppContext.BaseDirectory, "Resources/Fonts", fontName));
            using var bitmap = new System.Drawing.Bitmap(682, 1176);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            typeof(MainWindow).GetMethod("ConfigureCardGraphics", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { graphics, 2 });
            condensedText.Invoke(null, new object?[] { graphics, text, fonts, rect, size,
                System.Drawing.Color.White, System.Drawing.StringAlignment.Center, false, baseline });
            int top = bitmap.Height, bottom = -1;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).A >= 32) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            Check(bottom >= top, "metadata text must render: " + text);
            return (top, bottom);
        }
        foreach (var row in new[] {
            (Font: "FOT-NewRodin Pro EB.otf", Size: 22f, Rect: new System.Drawing.RectangleF(8, 403, 325, 42),
                Baseline: 434f, Reference: "DREAD AREA", Other: "忌可視の鏡鯨"),
            (Font: "FOT-Seurat Pro DB.otf", Size: 17f, Rect: new System.Drawing.RectangleF(8, 450, 325, 38),
                Baseline: 469.5f, Reference: "Ashrount vs. 打打だいず", Other: "usb_nk") })
        {
            var original = TextBounds(row.Reference, row.Font, row.Size, row.Rect, null);
            var originalOther = TextBounds(row.Other, row.Font, row.Size, row.Rect, null);
            var reference = TextBounds(row.Reference, row.Font, row.Size, row.Rect, row.Baseline);
            var other = TextBounds(row.Other, row.Font, row.Size, row.Rect, row.Baseline);
            Check(Math.Abs(original.Top - reference.Top) <= 1 && Math.Abs(original.Bottom - reference.Bottom) <= 1,
                "fixed baseline must preserve Dread Area's existing position");
            Check(Math.Abs(original.Top + original.Bottom - originalOther.Top - originalOther.Bottom) > 10,
                "actual examples must reproduce the old script-dependent vertical shift");
            Check(Math.Abs(reference.Top + reference.Bottom - other.Top - other.Bottom) <= 5,
                "Latin, Japanese and mixed text must remain on the same visual row");
            var longText = TextBounds(string.Concat(Enumerable.Repeat(row.Reference, 4)), row.Font, row.Size, row.Rect, row.Baseline);
            Check(Math.Abs(longText.Top - reference.Top) <= 1 && Math.Abs(longText.Bottom - reference.Bottom) <= 1,
                "horizontal compression must not change the text baseline");
            Console.WriteLine($"COVER BASELINE PASS: {row.Reference} {reference}; {row.Other} {other}; old {originalOther}.");
        }
        File.WriteAllText(".codex-build/oct11-seek-chart.json", JsonConvert.SerializeObject(ParseChart(
            "(205){8}" + string.Concat(Enumerable.Repeat("1h[16:1],", 24)) + "1,1,1,1,")));
        Set("designer", "测试谱师／譜面作者");
        foreach (var mode in new[] { ("std", "1,"), ("dx", "A1,"), ("alpha", "<COLOR*00BBFF>1,") })
        {
            var parsed = ParseChart(mode.Item2);
            dataType.GetField("timingList")!.SetValue(data, dataType.GetField("timingList")!.GetValue(parsed));
            dataType.GetField("colorTable")!.SetValue(data, dataType.GetField("colorTable")!.GetValue(parsed));
            Bake(true);
            File.Copy(Path.ChangeExtension(rendered, null) + "_full.png", ".codex-build/oct11-cover-" + mode.Item1 + ".png", true);
        }
        Set("level", "未知∞");
        Bake(true);
        File.Copy(Path.ChangeExtension(rendered, null) + "_full.png", ".codex-build/oct11-cover-textlevel.png", true);
        Set("level", "14-");
        Bake(true);
        File.Copy(Path.ChangeExtension(rendered, null) + "_full.png", ".codex-build/oct11-cover-minus.png", true);
        foreach (var difficulty in Enumerable.Range(0, 7))
        {
            Set("diffNum", difficulty); Set("level", ""); Bake(true);
            var stem = new[] { "easy", "basic", "advanced", "expert", "master", "remaster", "utage" }[difficulty];
            // Resolve the real cache name instead of assuming its difficulty spelling.
            var cacheStem = (string)typeof(MainWindow).GetMethod("GetSongDetailCacheStem", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[]{difficulty})!;
            File.Copy(Path.Combine(figure, cacheStem + "_full.png"), ".codex-build/oct11-level-" + stem + ".png", true);
        }
        Set("diffNum", 4); Set("level", "14");
        foreach (var sample in new[] {
            (Name: "dreadarea", Title: "DREAD AREA", Artist: "Ashrount vs. 打打だいず"),
            (Name: "kikashi", Title: "忌可視の鏡鯨", Artist: "usb_nk") })
        {
            Set("title", sample.Title); Set("artist", sample.Artist); Bake(true);
            File.Copy(Path.ChangeExtension(rendered, null) + "_full.png", ".codex-build/oct11-baseline-" + sample.Name + ".png", true);
        }
        Set("title", "MEGALOVANIA (Camellia Remix)");
        Set("artist", "Toby fox & Camellia / a long artist name that must scroll");
        Set("designer", "测试谱师／譜面作者／超出文本框宽度的名字");
        Bake(true);
        var textFolder = Path.ChangeExtension(rendered, null) + "_text";
        foreach (var row in new[] { ("title", 301, 42), ("artist", 301, 38), ("designer", 200, 26) })
        {
            using var strip = new System.Drawing.Bitmap(Path.Combine(textFolder, row.Item1 + ".png"));
            Check(strip.Width > row.Item2 * 2 && strip.Height == row.Item3 * 2,
                "Long cover metadata must retain natural width in a scrolling strip: " + row.Item1);
        }
        File.Copy(Path.ChangeExtension(rendered, null) + "_full.png", ".codex-build/oct11-cover-marquee.png", true);
        main.editorSetting.DisableAutomaticSongDetailRefresh = false;
        Set("title", "Short"); Set("artist", "Artist"); Set("designer", "Designer"); Bake(false);
        Check(!Directory.EnumerateFiles(textFolder).Any(), "Short metadata must remove stale scrolling strips");
        Console.WriteLine("COVER EDIT PASS: WPF refresh switch, SD/DX/Alpha classification, fixed baselines, natural-width scrolling strips including MEGALOVANIA, stale strip cleanup, frozen cache and settings persistence.");
    }
}
