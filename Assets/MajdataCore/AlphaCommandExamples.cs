using System;
using System.Linq;
namespace MajdataCore
{
    // Pure text shared by the tooltip, chapter guide and parser regression tests.
    public static class AlphaCommandExamples
    {
        private static AlphaCommandDescriptor Find(string name) {
            AlphaCommandGrammar.TryFind(name, out var descriptor); return descriptor!;
        }
        public static string Header(string name, string language = "zh")
        {
            string Word(string zh,string en,string ja) => language == "en" ? en : language == "ja" ? ja : zh;
            var descriptor=Find(name);
            var time=Word("过渡时间","transition","遷移時間");
            var strength=Word("强度","strength","強度");
            var scale=Word("倍率","scale","倍率");
            var angle=Word("角度","degrees","角度");
            if(name is "SIZE" or "SIZEV") return $"<{name}*({Word("X倍率","scaleX","X倍率")}|NULL[,{Word("Y倍率","scaleY","Y倍率")}])>";
            if(descriptor.forms.Length==0) {
                var value=name switch {
                    "COLOR" or "COLORV" => "RRGGBB|NULL",
                    "SIZE" or "SIZEV" => scale+"|NULL",
                    "ALPHA" or "ALPHAV" => Word("不透明度","opacity","不透明度")+"|NULL",
                    "SPAWN" or "DESTROY" => Word("半径","radius","半径")+"|NULL",
                    "SPAWNMODE" => "Rewind|Once|NULL",
                    "BOUNCE" => Word("持续时间","duration","持続時間")+"|NULL|False",
                    "FAKE" => "True|False",
                    _ => scale+"|NULL"
                };
                return $"<{name}*{value}>";
            }
            if(name=="ROTATE") return $"<ROTATE*(True|False|Instant,{angle}[,{time}][,{Word("旋转PV","rotate PV","PV回転")}])>";
            if(name=="JLINE") return $"<JLINE*(RRGGBB|RRGGBBAA|NULL[,{time}])>";
            if(name=="TEXT") return $"<TEXT*({Word("\"内容\"","\"content\"","\"内容\"")}[,{Word("持续时间","duration","持続時間")}][,x][,y][,{Word("字号","size","サイズ")}][,{Word("字体","font","フォント")}][,index][,Fade|Typewriter][,{time}])>";
            if(name=="JUDGELINEEXPAND") return $"<{name}*(True|False[,{time}][,1|-1|CW|CCW])>";
            if(name=="JUDGELINE") return $"<{name}*(0|1|2|3|4|Default[,{time}])>";
            if(name=="COMBODISPLAY") return $"<{name}*({Word("模式","mode","モード")}|Default[,{time}])>";
            if(name=="INNERBRIGHTNESS" || name=="OUTERBRIGHTNESS") return $"<{name}*({Word("遮暗值","darkness","暗さ")}|Default[,{time}])>";
            if(name=="AUDIO" || name=="PVOVERLAY") return $"<{name}*(True|False,{Word("相对路径","relative path","相対パス")}{(name=="AUDIO"?"":$"[,{time}]")})>";
            if(descriptor.SupportsDefaultReset) return $"<{name}*(True|False|Default[,{time}])>";
            var form=descriptor.forms.Where(f=>f.kind==AlphaArgumentFormKind.StateOn).OrderByDescending(f=>f.MaximumCount).First();
            var states=descriptor.forms.Any(f=>f.kind==AlphaArgumentFormKind.Instant) ? "True|False|Instant" : "True|False";
            var parameters=states;
            for(int i=1;i<form.slots.Length;i++) {
                var slot=form.slots[i];
                var label=slot.kind==AlphaValueKind.Duration ? time : slot.kind==AlphaValueKind.Color ? "RRGGBB" : slot.kind==AlphaValueKind.Boolean ? Word("渐变","gradient","グラデーション") :
                    name=="MOVE" ? (i==1?"dx":"dy") : name=="SHAKE" && i==2 ? Word("频率","frequency","周波数") : name=="SHAKE" && i==3 || name is "HUE" or "ROTATE" or "KALEIDOSCOPE" ? angle : name=="ZOOM" ? scale : strength;
                parameters+=slot.optional ? "[,"+label+"]" : ","+label;
            }
            return $"<{name}*({parameters})>";
        }
    // These are executable examples, never a second list of parameter names.
    public static string Examples(string name)
    {
        string[]? explicitExamples = name switch {
            "SV" => new[] { "<SV*2>", "<SV*slide=0.5,blockarea=2>" },
            "HS" => new[] { "<HS*2>", "<HS*slide=0.5,blockarea=2>" },
            "SPAWN" => new[] { "<SPAWN*1.225>", "<SPAWN*tap=-4.8,hold=NULL>" },
            "DESTROY" => new[] { "<DESTROY*4.8>", "<DESTROY*tap=6,hold=NULL>" },
            "SPAWNMODE" => new[] { "<SPAWNMODE*Rewind>", "<SPAWNMODE*tap=Once,hold=Rewind>" },
            "BOUNCE" => new[] { "<BOUNCE*8:1>", "<BOUNCE*tap=16:1,hold=NULL>" },
            "FAKE" => new[] { "<FAKE*True>", "<FAKE*tap=False,slide=True>" },
            "COLOR" or "COLORV" => new[] { $"<{name}*00BBFF>", $"<{name}*blockarea=FF6666,slidestar=00BBFF>", $"<{name}*mine=00BBFF>", $"<{name}*mine=NULL>" },
            "SIZE" or "SIZEV" => new[] { $"<{name}*(1.2,0.8)>", $"<{name}*blockarea=(0.8,1.2),slide=1.2>" },
            "ALPHA" or "ALPHAV" => new[] { $"<{name}*0.5>", $"<{name}*blockarea=0.7,slidestar=0.5>" },
            "ROTATE" => new[] { "<ROTATE*(True,30,8:1,True)>", "<ROTATE*(True,-30)>", "<ROTATE*(Instant,90,4:1,False)>" },
            "JLINE" => new[] { "<JLINE*(00BBFF,8:1)>", "<JLINE*(NULL,0.2)>" },
            "TEXT" => new[] { "<TEXT*(\"字幕\",2:1,0.2,0.3,44,Allerta,0,Fade,0.3)>", "<TEXT*(\"逐字字幕\",,0.2,,44,Allerta,,Typewriter,1)>" },
            "JUDGELINEEXPAND" => new[] { "<JUDGELINEEXPAND*(True,8:1,-1)>", "<JUDGELINEEXPAND*(False)>" },
            "JUDGELINE" => new[] { "<JUDGELINE*(3,8:1)>", "<JUDGELINE*(Default,0.2)>" },
            "COMBODISPLAY" => new[] { "<COMBODISPLAY*(Combo,8:1)>", "<COMBODISPLAY*(Default,0.2)>" },
            "OUTERBRIGHTNESS" or "INNERBRIGHTNESS" => new[] { $"<{name}*(0.5,8:1)>", $"<{name}*(Default,0.2)>" },
            "SHOWJUDGETEXT" or "SHOWJUDGEINFO" or "SHOWCOMBOINFO" => new[] { $"<{name}*(True,8:1)>", $"<{name}*(False,0.2)>", $"<{name}*(Default)>" },
            "AUDIO" => new[] { "<AUDIO*(True,sound.ogg)>", "<AUDIO*(False)>" },
            "PVOVERLAY" => new[] { "<PVOVERLAY*(True,pv.mp4,8:1)>", "<PVOVERLAY*(False,0.2)>" },
            _ => null
        };
        if (explicitExamples != null) return string.Join(" / ", explicitExamples);
        var descriptor = Find(name);
        var examples = descriptor.forms.Where(f => f.kind != AlphaArgumentFormKind.Plain)
            .GroupBy(f => f.kind).OrderBy(g => g.Key == AlphaArgumentFormKind.StateOn ? 0 : g.Key == AlphaArgumentFormKind.Instant ? 1 : 2)
            .Select(g => g.OrderByDescending(f => f.MaximumCount).First())
            .Select(f => $"<{name}*({string.Join(",", f.slots.Select(s => s.defaultText.Length > 0 ? s.defaultText : "0.3"))})>").ToArray();
        return string.Join(" / ", examples);
    }

    }
}
