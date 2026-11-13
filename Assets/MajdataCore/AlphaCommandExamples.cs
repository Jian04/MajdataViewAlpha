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
            if(name is "SIZE" or "SIZEV") return $"<{name}*({Word("X倍率","scaleX","X倍率")}|NULL[,{Word("Y倍率","scaleY","Y倍率")}])> / "+TransitionHeader(name,language);
            if(descriptor.forms.Length==0) {
                var value=name switch {
                    "COLOR" or "COLORV" => "RRGGBB|NULL",
                    "SIZE" or "SIZEV" => scale+"|NULL",
                    "ALPHA" or "ALPHAV" => Word("不透明度","opacity","不透明度")+"|NULL",
                    "ANGLE" or "ANGLEV" => angle+"|NULL",
                    "SPAWN" or "DESTROY" => Word("半径","radius","半径")+"|NULL",
                    "SPAWNMODE" => "Rewind|Once|NULL",
                    "BOUNCE" => Word("持续时间","duration","持続時間")+"|NULL|False",
                    "FAKE" => "True|False",
                    _ => scale+"|NULL"
                };
                if (descriptor.scalar?.allowsNull == false) value = value.Replace("|NULL", "");
                return $"<{name}*{value}>"+(descriptor.scalar?.allowsTransition==true ? " / "+TransitionHeader(name,language) : "");
            }
            if(name=="ROTATE") return $"<ROTATE*(True,{angle}[,{time}][,{Word("旋转PV","rotate PV","PV回転")}])> / <ROTATE*(Instant,{angle},{time}[,{Word("旋转PV","rotate PV","PV回転")}])> / <ROTATE*(False[,{time}][,{Word("旋转PV","rotate PV","PV回転")}])>";
            if(name=="FLIP") return $"<FLIP*(True,{angle}[,{Word("仅音符","notes only","音符のみ")}])> / <FLIP*(Instant,{angle},{Word("持续时间","duration","持続時間")}[,{Word("仅音符","notes only","音符のみ")}])> / <FLIP*(False)>";
            if(name=="MOVE") return $"<MOVE*(True,dx,dy[,{time}][,{Word("移动PV","move PV","PV移動")}])> / <MOVE*(Instant,dx,dy,{time}[,{Word("移动PV","move PV","PV移動")}])> / <MOVE*(False[,{time}][,{Word("移动PV","move PV","PV移動")}])>";
            if(name=="JLINE") return $"<JLINE*(RRGGBB|RRGGBBAA|NULL[,{time}])>";
            if(name=="TEXT") return $"<TEXT*({Word("\"内容\"","\"content\"","\"内容\"")}[,{Word("持续时间","duration","持続時間")}][,x][,y][,{Word("字号","size","サイズ")}][,{Word("字体","font","フォント")}][,index][,Fade|Typewriter][,{time}][,{Word("颜色RGB","colourRGB","色RGB")}][,TopLeft|Center])>";
            if(name=="JUDGELINEEXPAND") return $"<{name}*(True|False[,{time}][,1|-1|CW|CCW])>";
            if(name=="JUDGELINE") return $"<{name}*(0|1|2|3|4|Default[,{time}])>";
            if(name=="COMBODISPLAY") return $"<{name}*({Word("模式","mode","モード")}|Default[,{time}])>";
            if(name=="INNERBRIGHTNESS" || name=="OUTERBRIGHTNESS") return $"<{name}*({Word("遮暗值","darkness","暗さ")}|Default[,{time}])>";
            if(name=="AUDIO") return $"<AUDIO*(True,{Word("相对路径","relative path","相対パス")})> / <AUDIO*(False)>";
            if(name=="PVOVERLAY") return $"<PVOVERLAY*(True,{Word("相对路径","relative path","相対パス")}[,{time}])> / <PVOVERLAY*(False[,{time}])>";
            if(descriptor.SupportsDefaultReset) return $"<{name}*(True|False|Default[,{time}])>";
            return string.Join(" / ", descriptor.forms
                .Where(form => form.kind is AlphaArgumentFormKind.StateOn or AlphaArgumentFormKind.Instant or AlphaArgumentFormKind.StateOff)
                .Select(form => {
                    var parameters = form.kind == AlphaArgumentFormKind.StateOn ? "True" : form.kind == AlphaArgumentFormKind.Instant ? "Instant" : "False";
                    for(int i=1;i<form.slots.Length;i++) {
                        var slot=form.slots[i];
                        var label=slot.kind==AlphaValueKind.Duration ? time : slot.kind==AlphaValueKind.Color ? "RRGGBB" : slot.kind==AlphaValueKind.Boolean ? Word("渐变","gradient","グラデーション") :
                            name=="SHAKE" && i==2 ? Word("频率","frequency","周波数") : name=="SHAKE" && i==3 || name is "HUE" or "KALEIDOSCOPE" ? angle : name=="ZOOM" ? scale : strength;
                        parameters+=slot.optional ? "[,"+label+"]" : ","+label;
                    }
                    return $"<{name}*({parameters})>";
                }).Distinct());
        }
        public static string TransitionHeader(string name,string language="zh")
        {
            var start=language=="en" ? "start" : language=="ja" ? "開始値" : "起始值";
            var end=language=="en" ? "end" : language=="ja" ? "終了値" : "终止值";
            if(name is "COLOR" or "COLORV") { start+="RRGGBB"; end+="RRGGBB"; }
            var time=language=="en" ? "duration" : language=="ja" ? "持続時間" : "持续时间";
            var parameter=language=="en" ? "power" : language=="ja" ? "指数" : "幂次";
            var function=language=="en" ? "function" : language=="ja" ? "関数" : "函数";
            return $"<{name}*({start},{end},{time},{function}[,{parameter}])>";
        }
    // These are executable examples, never a second list of parameter names.
    public static string Examples(string name)
    {
        string[]? explicitExamples = name switch {
            "SV" => new[] { "<SV*2>", "<SV*slide=0.5,blockarea=2>", "<SV*NULL>", "<SV*tap=NULL>" },
            "HS" => new[] { "<HS*2>", "<HS*slide=0.5,blockarea=2>" },
            "SPAWN" => new[] { "<SPAWN*1.225>", "<SPAWN*tap=-4.8,hold=NULL>" },
            "DESTROY" => new[] { "<DESTROY*4.8>", "<DESTROY*tap=6,hold=NULL>" },
            "SPAWNMODE" => new[] { "<SPAWNMODE*Rewind>", "<SPAWNMODE*tap=Once,hold=Rewind>" },
            "BOUNCE" => new[] { "<BOUNCE*8:1>", "<BOUNCE*tap=16:1,hold=NULL>" },
            "FAKE" => new[] { "<FAKE*True>", "<FAKE*tap=False,slide=True>" },
            "COLOR" or "COLORV" => new[] { $"<{name}*00BBFF>", $"<{name}*blockarea=FF6666,slidestar=00BBFF>", $"<{name}*mine=00BBFF>", $"<{name}*mine=NULL>", $"<{name}*(NULL,00BBFF,8:1,linear)>", $"<{name}*tap=(FF0000,00BBFF,4:1,easeinout,3)>" },
            "SIZE" or "SIZEV" => new[] { $"<{name}*(1.2,0.8)>", $"<{name}*blockarea=(0.8,1.2),slide=1.2>" },
            "ALPHA" or "ALPHAV" => new[] { $"<{name}*0.5>", $"<{name}*blockarea=0.7,slidestar=0.5>", $"<{name}*tapline=0.25>" },
            "ANGLE" or "ANGLEV" => new[] { $"<{name}*30>", $"<{name}*tap=45,slidestar=-30>" },
            "ROTATE" => new[] { "<ROTATE*(True,30,8:1,True)>", "<ROTATE*(True,-30)>", "<ROTATE*(Instant,90,4:1,False)>" },
            "FLIP" => new[] { "<FLIP*(Instant,45,0.5,True)>", "<FLIP*(True,0,False)>", "<FLIP*(False)>" },
            "JLINE" => new[] { "<JLINE*(00BBFF,8:1)>", "<JLINE*(NULL,0.2)>" },
            "TEXT" => new[] { "<TEXT*(\"字幕\",2:1,0.2,0.3,44,Allerta,0,Fade,0.3,00BBFF,TopLeft)>", "<TEXT*(\"居中字幕\",2,origin=Center)>", "<TEXT*(\"文件字体\",2,font=Allerta-Regular.ttf,color=FF8800)>" },
            "JUDGELINEEXPAND" => new[] { "<JUDGELINEEXPAND*(True,8:1,-1)>", "<JUDGELINEEXPAND*(False)>" },
            "JUDGELINE" => new[] { "<JUDGELINE*(3,8:1)>", "<JUDGELINE*(Default,0.2)>" },
            "COMBODISPLAY" => new[] { "<COMBODISPLAY*(Combo,8:1)>", "<COMBODISPLAY*(Default,0.2)>" },
            "OUTERBRIGHTNESS" or "INNERBRIGHTNESS" => new[] { $"<{name}*(0.5,8:1)>", $"<{name}*(Default,0.2)>" },
            "SHOWJUDGETEXT" or "SHOWJUDGEINFO" or "SHOWCOMBOINFO" => new[] { $"<{name}*(True,8:1)>", $"<{name}*(False,0.2)>", $"<{name}*(Default)>" },
            "AUDIO" => new[] { "<AUDIO*(True,sound.ogg)>", "<AUDIO*(False)>" },
            "PVOVERLAY" => new[] { "<PVOVERLAY*(True,pv.mp4,8:1)>", "<PVOVERLAY*(False,0.2)>" },
            _ => null
        };
        if (explicitExamples != null)
        {
            if(Find(name).scalar?.allowsTransition==true && name is not ("COLOR" or "COLORV"))
                explicitExamples=explicitExamples.Concat(name is "SIZE" or "SIZEV" ? new[] {
                    $"<{name}*tap=((1,1),(2,0.5),8:1,linear)>", $"<{name}*(0.5,1,4:1,sineinout)>" } : new[] {
                    $"<{name}*tap=(1,{(name is "ALPHA" or "ALPHAV" ? "0.5" : "2")},8:1,linear)>",
                    $"<{name}*(0,1,4:1,sineinout)>" }).ToArray();
            return string.Join(" / ", explicitExamples);
        }
        var descriptor = Find(name);
        var examples = descriptor.forms.Where(f => f.kind != AlphaArgumentFormKind.Plain)
            .GroupBy(f => f.kind).OrderBy(g => g.Key == AlphaArgumentFormKind.StateOn ? 0 : g.Key == AlphaArgumentFormKind.Instant ? 1 : 2)
            .Select(g => g.OrderByDescending(f => f.MaximumCount).First())
            .Select(f => $"<{name}*({string.Join(",", f.slots.Select(s => s.defaultText.Length > 0 ? s.defaultText : "0.3"))})>").ToArray();
        return string.Join(" / ", examples);
    }

    }
}
