using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;
using MajdataCore;
using Newtonsoft.Json;

internal static class Wiki096Examples
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    internal static void Run()
    {
        var root = Path.GetFullPath("alpha");
        var folder = Path.Combine(root, ".codex-build/wiki-examples");
        Directory.CreateDirectory(folder);
        var black = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(black));
        using (var stream = File.Create(Path.Combine(folder, "bg.png"))) png.Save(stream);
        File.Copy(Path.Combine(root, "Assets/StreamingAssets/Skin/dx/tap.png"), Path.Combine(folder, "note.png"), true);
        var examples = new List<(string Id, string Label, string Document, string Heading, string Code)>();
        void Add(string id, string label, string document, string heading, string code) => examples.Add((id, label, document, heading, code));
        foreach (var command in AlphaCommandGrammar.Commands)
        {
            Add(command.name.ToLowerInvariant(), command.name, "Alpha", command.name, CommandDemo(command.name));
        }
        Add("transition", "数值渐变", "Alpha", "通用数值渐变", "<HS*(0.4,2,2,linear)>{8}1,2,3,4,5,6,7,8,1,2,3,4,");
        Add("comments", "注释", "Alpha", "单行与块注释", "{4}|| Comment\n1,2,|* block *|3,4,");
        Add("meter", "拍号", "Alpha", "波形拍号", "@3/4\n{4}1,2,3,1,2,3,\n@4/4\n4,5,6,7,");
        Add("streams", "可重叠音符流", "Alpha", "可重叠音符流", "@{8}5,6,7,8,5,6,7,8,\n{4}1,2,3,4,");
        Add("editor-color", "编辑区背景色", "Alpha", "编辑区背景色", "@334466\n{4}1,2,3,4,\n@NULL\n5,6,7,8,");
        Add("touchhold", "非 C 区 TouchHold", "Alpha", "非 C 区 TouchHold", "{4}A1h[2:1]/B3h[2:1],,E5h[2:1]/D7h[2:1],,,");
        Add("touch-break", "Break Touch / TouchHold", "Alpha", "Break Touch / TouchHold", "{4}B1b,B3b,Cbh[2:1],,,");
        Add("headless", "无头 Slide", "Alpha", "无头 Slide 修饰", "{4}1?-5[2:1],3!-7[2:1],5?-1[2:1],7!-3[2:1],,,");
        Add("firework", "烟花", "Alpha", "烟花修饰", "{4}1f,B3f,Cf,5f,");
        Add("star-tap", "星形 Tap", "Alpha", "星形 TAP", "{4}1$,3$$,5$b,7$x,");
        Add("touch-slide", "TouchSlide", "Alpha", "Touch Slide", "{4}A1>A4[2:1],,B5-B1[2:1],,E7-C-E3[2:1],,,");
        Add("pq", "大 P / Q", "Alpha", "大 P / Q", "{4}1P58[2:1],,A1Q9A5[2:1],,1dP95d[2:1],,,");
        Add("slidecode", "SlideCode", "Alpha", "SlideCode", "{4}1A3B5K7[2:1],,8P91K1[2:1],,1dP93K5d[2:1],,,");
        Add("mine-d", "Mine 与 D 区", "Alpha", "Mine、D 区", "{4}1m/3,2d,3dm,4dh[2:1],5-1m[2:1],,,");
        Add("rprq", "rp / rq", "Alpha", "rp / rq", "{4}1rp5[2:1],,5rq1[2:1],,,");
        Add("inherit-path", "轨迹继承", "Alpha", "继承语法 — 星星轨迹", "{4}1~[1-5[2:1]],,3h[2:1]~[3<7[2:1]],,5$$~[5-1[2:1]],,,");
        Add("inherit-distance", "Touch 距离", "Alpha", "继承语法 — Touch 距离", "{4}A1~2,B3~1.2,C~0.5,E5~1.8,");
        Add("inherit-image", "图片换肤", "Alpha", "继承语法 — 图片换肤", "{4}1~[note.png],3~[note.png]~[3<7[2:1]],,5~[note.png]-1[2:1],,,");
        Add("blockarea", "噪域", "Alpha", "X 噪域", "{8}XB1[2:1],XC[2:1],XB2[4:1],,XB3[8:1],,,XD1[1:0],,,,");
        Add("file", "谱面结构", "Simai", "文件与谱面结构", "{4}1,2,3,4,5,6,7,8,");
        Add("bpm", "BPM 与分音", "Simai", "时间槽、BPM 与分音", "{4}1,2,3,4,(180){8}5,6,7,8,1,2,3,4,");
        Add("duration", "持续时间", "Simai", "持续时间的统一写法", "{4}1h[4:1],3h[2:1],5h[1:1],7h[8:1],,,");
        Add("tap", "Tap / Break / EX", "Simai", "Tap、Break 与 EX", "{4}1,2b,3x,4bx,5/7,6/8,");
        Add("hold", "Hold", "Simai", "Hold", "{4}1h,3hx[4:1],5hb[2:1],7h[1:1],,,");
        Add("slide", "Slide 基本形状", "Simai", "Slide 基本形状", "{4}1-5[2:1],,3<7[2:1],,5v1[2:1],,7w3[2:1],,,");
        Add("slide-time", "Slide # / ##", "Simai", "Slide 组合与时间", "{4}1-5[0.5##1],,3<7[180#2:1],,5-1[0.2##180#2:1],,,");
        Add("slide-start", "Slide 启动与星星", "Simai", "进阶 Slide", "{4}1?-5[2:1],,3!-7[2:1],,5$-1[2:1],,,");
        Add("touch", "Touch / TouchHold", "Simai", "Touch 与 TouchHold", "{4}A1,B3,Ch[2:1],B5/B7,,,C,");
        Add("pseudo-each", "同时音符与顺序", "Simai", "同时音符与顺序", "{4}1/3,2`4,B1`D2,5/7,");
        Add("end", "结束与注释", "Simai", "结束、注释与排错", "{4}1,2,|| Notes end\n3,4,E");
        Add("d-zone", "D 区键位", "Alpha", "d：D 区键位", "{4}1d,4dh[8:1],1d-5[8:1],1-5d[8:1],1d-5d[8:1],,,");
        Add("touch-slide-multi", "TouchSlide 多圈螺旋", "Alpha", "TouchSlide 多圈螺旋", "{4}A1<<E5[8:1],,A1<<<E5[8:1],,A1>>>E5[8:1],,E5>E5[8:1],,,");
        Add("record-range", "录制范围", "Alpha", "录制范围", "{4},,,,\n@start\n1,2,3,4,\n@end\n5,6,7,8,");
        Add("combine-streams", "分流表演音符与正常谱面", "Alpha", "分流表演音符与正常谱面", "{8}\n@*{4}<FAKE*True><COLOR*00BBFF><SIZE*1.2>\n1,2,3,4,\n*@\n5,6,7,8,5,6,7,8,");
        Add("combine-display", "同拍显示控制、字幕与滤镜", "Alpha", "同拍显示控制、字幕与滤镜", "(160){16}\n<COLOR*star=FF66CC><SIZE*star=1.15>1-5[8:1],2,3,4,\n<TEXT*(\"间奏\",2,index=1)><FADE*(True,0.65,8:1)>,,\n<FADE*(False,8:1)><COLOR*star=NULL><SIZE*star=NULL>,E");
        Add("combine-noise", "相邻噪域先后出现、分块结束", "Alpha", "相邻噪域先后出现、分块结束", "{8}<COLOR*blockarea=FF6666>XA1[2:1],XD1[4:1],XA8[8:1],,,,,,");

        var type = typeof(MainWindow);
        var parser = type.Assembly.GetType("MajdataEdit.SimaiProcess")!;
        var main = new MainWindow { editorSetting = new EditorSetting { SongDetailStyle = 0 } };
        type.GetField("selectedDifficulty", All)!.SetValue(main, 4);
        var manifest = new List<object>();
        var errors = new List<string>();
        foreach (var example in examples)
        {
            var source = "(120)" + example.Code;
            for (var at = 0; at < source.Length; at++)
            {
                if (source[at] != '<' || !AlphaCommandBoundary.TryGetToken(source, at, out var command)) continue;
                var token = source.Substring(at + 1, command.length - 2);
                if (!AlphaCommandGrammar.TryValidate(token, 120, out var error)) errors.Add(example.Id + ": " + error);
                at += command.length - 1;
            }
        }
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
        foreach (var example in examples)
        {
            var source = "(120)" + example.Code;
            var file = Path.Combine(folder, "maidata.txt");
            File.WriteAllText(file, "&title=" + example.Label + "\n&first=1\n&lv_5=14\n&inote_5=" + source);
            parser.GetMethod("ReadData", All)!.Invoke(null, new object[] { file });
            parser.GetMethod("Serialize", All)!.Invoke(null, new object[] { source, 0L });
            MainWindow.maidataDir = folder;
            var packet = type.GetMethod("BuildSongDetailMajson", All)!.Invoke(main, null)!;
            var json = JsonConvert.SerializeObject(packet);
            var invalid = Newtonsoft.Json.Linq.JObject.Parse(json)["timingList"]!
                .Where(point => !string.IsNullOrEmpty((string?)point["noteParseError"])).ToArray();
            if (invalid.Length > 0) throw new Exception(example.Id + ": " + string.Join(";", invalid.Select(point => point["noteParseError"])));
            var chart = Path.Combine(folder, example.Id + ".json"); File.WriteAllText(chart, json);
            manifest.Add(new { id = example.Id, label = example.Label, document = example.Document, heading = example.Heading,
                code = source, chart, start = 0, duration = 6, output = Path.Combine(root, "wiki/gifs", example.Id + ".gif") });
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
        Console.WriteLine($"096 WIKI: generated {examples.Count} parser-validated example packets.");
    }

    private static string CommandDemo(string name)
    {
        var taps = "1,3,5,7,";
        var stationary = "{4}1h[1:3]/5hb[1:3]/B3h[1:3]/B7hb[1:3],,,";
        var scene = name switch
        {
            "SV" or "HS" => $"<{name}*0.4>{{4}}{taps}<{name}*2>{taps}<{name}*1>{taps}",
            "SPAWN" => $"{{4}}{taps}<SPAWN*4.8>{taps}<SPAWN*NULL>{taps}",
            "DESTROY" => $"<FAKE*True>{{4}}{taps}<DESTROY*0>{taps}<DESTROY*-4.8>{taps}",
            "SPAWNMODE" => "<FAKE*True><SPAWN*3.5><BOUNCE*4:1><SPAWNMODE*Rewind>{4}1,3,5,7,<SPAWNMODE*Once>1,3,5,7,",
            "BOUNCE" => $"{{4}}{taps}<BOUNCE*4:1>{taps}<BOUNCE*NULL>{taps}",
            "FAKE" => "<SV*0.5>{4}1,3,5,7,<FAKE*True><DESTROY*-4.8>1,3,5,7,1,3,5,7,",
            "COLOR" => $"{{4}}{taps}<COLOR*00BBFF>{taps}<COLOR*FF66CC>{taps}",
            "COLORV" => stationary + "<COLORV*00BBFF>,,<COLORV*FF66CC>,,<COLORV*NULL>,,,,",
            "SIZE" => $"{{4}}{taps}<SIZE*1.6>{taps}<SIZE*(0.65,1.2)>{taps}",
            "SIZEV" => stationary + "<SIZEV*1.6>,,<SIZEV*(0.65,1.2)>,,<SIZEV*NULL>,,,,",
            "ALPHA" => $"{{4}}{taps}<ALPHA*0.2>{taps}<ALPHA*1>{taps}",
            "ALPHAV" => stationary + "<ALPHAV*0.2>,,<ALPHAV*1>,,,,",
            "ANGLE" => $"{{4}}{taps}<ANGLE*100>{taps}<ANGLE*0>{taps}",
            "ANGLEV" => stationary + "<ANGLEV*(0,120,1,linear)>,,,<ANGLEV*0>,,,,",
            "JLINE" => stationary + "<JLINE*(FF6688,0.4)>,,<JLINE*(00BBFF,0.4)>,,<JLINE*(NULL,0.4)>,,,",
            "JUDGELINE" => stationary + "<JUDGELINE*(0,0.4)>,,<JUDGELINE*(3,0.4)>,,<JUDGELINE*(2,0.4)>,,,",
            "JUDGELINEEXPAND" => stationary + "<JUDGELINEEXPAND*(True,0.8,1)>,,,<JUDGELINEEXPAND*(False)>,,,,",
            "TEXT" => "{4}<TEXT*(\"Top left\",1.5,x=0.08,y=0.18,size=40,color=00BBFF)>1,2,3,4,<TEXT*(\"Center\nTEXT\",2,origin=Center,color=FF88CC)>5,6,7,8,",
            "SHOWJUDGETEXT" or "SHOWJUDGEINFO" or "SHOWCOMBOINFO" => $"<{name}*(True)>{{8}}{taps}{taps}<{name}*(False)>{taps}{taps}<{name}*(True)>{taps}{taps}",
            "COMBODISPLAY" => $"<COMBODISPLAY*(Combo)>{{8}}{taps}{taps}<COMBODISPLAY*(DXACC)>{taps}{taps}<COMBODISPLAY*(Combo)>{taps}{taps}",
            "INNERBRIGHTNESS" or "OUTERBRIGHTNESS" => stationary + $"<{name}*(0.85,0.5)>,,,<{name}*(0,0.5)>,,,,",
            "AUDIO" => "{4}<TEXT*(\"AUDIO: ON\",2,origin=Center)><AUDIO*(True,sound.wav)>1,3,5,7,<TEXT*(\"AUDIO: OFF\",2,origin=Center)><AUDIO*(False)>1,3,5,7,",
            "PVOVERLAY" => stationary + "<PVOVERLAY*(True,overlay.mp4,0.5)>,,,<PVOVERLAY*(False,0.5)>,,,,",
            "MOVE" => stationary + "<MOVE*(True,0.25,0,0.6,False)>,,<MOVE*(True,0,0.2,0.6,False)>,,<MOVE*(False,0.6)>,,,",
            "ROTATE" => stationary + "<ROTATE*(True,65,0.7)>,,,<ROTATE*(False,0.7)>,,,,",
            "SHAKE" => stationary + "<SHAKE*(Instant,0.5,10,90,1.5)>,,,,,,,",
            "ZOOM" => stationary + "<ZOOM*(True,1.5,0.6)>,,,<ZOOM*(False,0.6)>,,,,",
            "TINT" => stationary + "<TINT*(True,FF6688,0.65,0.6)>,,,<TINT*(False,0.6)>,,,,",
            "HUE" or "KALEIDOSCOPE" => stationary + $"<{name}*(True,120,1.2)>,,,<{name}*(False,0.6)>,,,,",
            "VIGNETTE" => stationary + "<VIGNETTE*(True,0.7,0.5)>,,,<VIGNETTE*(False,0.5)>,,,,",
            _ => stationary + $"<{name}*(0.3,1.2,0.5,0.75)>,,,,,,,"
        };
        return name is "TEXT" or "AUDIO" ? scene : $"<TEXT*(\"{name}\",6,x=0.06,y=0.03,size=24,color=FFFFFF)>" + scene;
    }
}
