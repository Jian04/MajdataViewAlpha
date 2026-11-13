using MajdataCore;

namespace MajdataEdit;

internal static class NoteTransitionRegression
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
        void Near(double actual,double expected,string message,double tolerance=.0001d) =>
            Check(Math.Abs(actual-expected)<tolerance,$"{message}: {actual} != {expected}");
        foreach(var token in new[] { "SV*(2)","SV*tap=(1,2,8:1,linear)",
            "HS*(0,2,1,sineinout)","SIZE*((1,1),(2,0.5),8:1,easeinout,3)",
            "SIZE*(1.2,0.8)","ALPHAV*(0,1,4:1,easeout)","SPAWN*(4.8)",
            "DESTROY*(-4.8)","BOUNCE*(8:1)",
            "ANGLE*tap=45","ANGLEV*slidestar=(0,90,4:1,sineinout)",
            "COLOR*(00BBFF)","COLOR*(FF0000,00BBFF,8:1,linear)",
            "COLORV*tap=(FF0000,00BBFF,4:1,easeinout,3)","FAKE*(False)","SV*(1,2,1e-3,linear)" })
            Check(AlphaCommandGrammar.TryValidate(token,120,out var error),token+": "+error);
        foreach(var token in new[] { "SPAWN*(0,4.8,1,linear)", "DESTROY*(4.8,-4.8,1,linear)", "SV*(2,linear)","SV*(1,2,1,unknown)","SV*(1,2,1,linear,3)",
            "SV*(1,2,-1,linear)","SV*(1,2,1,easein,0)","SV*(1,2,1,sigmoid)","ANGLE*blockarea=30",
            "BOUNCE*(8:1,4:1,1,linear)","COLOR*(1,2,1,linear)","COLOR*(FF0000,NULL,1,linear)" })
            Check(!AlphaCommandGrammar.TryValidate(token,120,out _),"Should reject "+token);
        for(var button=1;button<=8;button++)
        {
            var text=$"{button}v{button}[8:1]";
            Check(SlidePathParser.TryParsePath(text,out var path) && SlideSyntaxValidator.TryValidate(path,out _),"Original v self-return: "+text);
            Check(SlideShapeResolver.TryResolve(path.segments[0],out var shape,out _,out _) && shape=="v1","Original v1 prefab");
            SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}"+text+",",0);
            Check(SimaiProcess.notelist.Single().getNotes().Count==1,"Full parser v self-return");
        }
        foreach(var token in new[]{"TEXT*(\"Caption\",2,0.2,0.3,44,Allerta,0,Fade,0.3,00BBFF)","TEXT*(\"Caption\",color=FF8800)","TEXT*(\"Caption\")"})
        {
            Check(AlphaCommandGrammar.TryValidate(token,120,out var error),"TEXT colour grammar: "+error);
            SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}<"+token+">,",0);
            var expected=token.Contains("00BBFF") ? "00BBFF" : token.Contains("FF8800") ? "FF8800" : "FFFFFF";
            Check(SimaiProcess.subtitleTable.Single().color==expected,"TEXT colour serialization");
        }
        foreach(var token in new[]{"TEXT*(\"Caption\",color=red)","TEXT*(\"Caption\",,,,,,,,,GGFFFF)"})
            Check(!AlphaCommandGrammar.TryValidate(token,120,out _),"Reject invalid TEXT colour");
        foreach(var command in AlphaCommandGrammar.Commands)
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                AlphaCommandExamples.Examples(command.name),"<([^<>]+)>"))
                Check(AlphaCommandGrammar.TryValidate(match.Groups[1].Value,120,out var error),
                    "Help example "+match.Value+": "+error);
        foreach (var file in Directory.EnumerateFiles("MajdataEdit/Langs", "Langs*.resx"))
        {
            var resource = System.Xml.Linq.XDocument.Load(file).Root!.Elements("data")
                .First(data => (string?)data.Attribute("name") == "AlphaHelpStructuredText").Element("value")!.Value;
            var described = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(resource,
                @"<([A-Za-z]+)\*").Select(match => match.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
            foreach (var command in AlphaCommandGrammar.Commands)
                Check(described.Contains(command.name), file + " missing help for " + command.name);
            foreach (var line in resource.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("E ")))
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(line,
                    @"<([A-Za-z]+\*[^<>]+)>"))
                    Check(AlphaCommandGrammar.TryValidate(match.Groups[1].Value, 120, out var error),
                        file + " authored example " + match.Value + ": " + error);
        }
        foreach (var text in new[] { "1A3B5K7[8:1]", "1A3571P0K1[4:1]", "2B4K6[8:1]" })
        {
            SimaiProcess.ClearData();
            SimaiProcess.Serialize("(120){4}" + text + ",2,", 0);
            var point = SimaiProcess.notelist.First();
            Check(point.getNotes().Count == 1 && string.IsNullOrEmpty(point.noteParseError),
                "SlideCode complete parser: " + text + ": " + point.noteParseError);
            Check(point.noteList[0].slidePath[0].shape == "SC", "SlideCode segment retained");
        }
        foreach (var reset in new[] { "<SV*NULL>", "<SV*(NULL)>" })
        {
            Check(AlphaCommandGrammar.TryValidate(reset[1..^1], 120, out _), "SV global reset grammar");
            SimaiProcess.ClearData();
            SimaiProcess.Serialize("(120){4}<SV*2>1," + reset + "2,", 0);
            Check(SimaiProcess.svTable.Count == 2 && SimaiProcess.svTable[1].reset, "SV global reset timeline");
        }
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}<COLOR*(FF0000,00BBFF,8:1,linear)>1,"+
            "<COLORV*tap=(00FF00,0000FF,4:1,sineinout)>2,",0);
        Check(SimaiProcess.colorTable.Count==2,"RGB curves must serialize for COLOR and COLORV");
        var firstColor=SimaiProcess.colorTable[0];
        Near(firstColor.transition!.duration,.25,"RGB beat duration follows the current BPM");
        var rgb=firstColor.transition.EvaluateColor(.125);
        Near(rgb.R,.5,"RGB red midpoint"); Near(rgb.G,187d/510,"RGB green midpoint"); Near(rgb.B,.5,"RGB blue midpoint");
        Check(firstColor.color=="00BBFF" && SimaiProcess.colorTable[1].live &&
            SimaiProcess.colorTable[1].noteType=="tap","RGB destination, typed target and live flag");
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(222){4}1,\n@{8}<COLOR*(000000,FFFFFF,8:1,linear)>1,",0);
        Near(SimaiProcess.colorTable.Single().transition!.duration,240d/222/8,
            "Overlay RGB duration uses its local BPM");
        foreach(var text in new[] { "(70)\n@{8},XD1[8:1]/XE7[8:1],XB6[8:1],",
            "(70)\n@*{8},XD1[8:1]/XE7[8:1],\nXB6[8:1],*@" })
        {
            SimaiProcess.ClearData(); SimaiProcess.Serialize(text,0);
            Check(SimaiProcess.timinglist.Count==0,"Independent streams must not advance the main clock");
            Check(SimaiProcess.waveTimingList.Count==3,"Every independent-stream slot needs a waveform tick");
            Check(SimaiProcess.beatDisplayTable.First().bpm==70,"Grid BPM exists without main-stream commas");
            Near(SimaiProcess.waveTimingList[1].time,240d/70/8,"Empty overlay slot remains on the waveform");
            Near(SimaiProcess.notelist[0].time,240d/70/8,"Waveform change must not move the notes");
        }
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(70)\n@{4}1,XD1[8:1]/XE7[8:1],XB6[8:1],\n{8}1,",0);
        Check(SimaiProcess.waveTimingList.Count==4 && SimaiProcess.timinglist.Count==1,
            "Main and independent-stream ticks must coexist");
        Check(SimaiProcess.beatDisplayTable.All(point=>point.bpm==70),"Overlay notation must not change the grid BPM");
        foreach (var text in new[] { "A1P9A5[8:1]", "1P5A8-B4[8:1]", "1dQ05d[8:1]" })
        {
            SimaiProcess.ClearData();
            SimaiProcess.Serialize("(120){4}"+text+",2,",0);
            var point=SimaiProcess.notelist.First();
            Check(point.getNotes().Count==1 && string.IsNullOrEmpty(point.noteParseError),
                "Alpha P/Q complete parser: "+text+": "+point.noteParseError);
            Check(point.noteList[0].slidePath.All(segment=>segment.shape!="SC"),
                "Touch sensor names must not turn P/Q into SlideCode");
        }
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1,r,2,<SIZE*bad>3,", 0);
        var recovered = SimaiProcess.notelist.Where(point => point.getNotes().Count > 0).ToArray();
        Check(recovered.Length == 3 && !string.IsNullOrEmpty(SimaiProcess.notelist[1].noteParseError),
            "Invalid note must remain diagnosed while later notes survive");
        Near(recovered[1].time, 1, "Invalid slot must retain its authored time");
        Near(recovered[2].time, 1.5, "Ignored command must not move later notes");
        Check(SimaiProcess.sizeTable.Count == 0, "Invalid commands must not change state");
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}<SV*tap=(1,3,2,linear)><HS*(1,2,1,easein)>"+
            "<SIZE*((1,1),(2,0.5),8:1,linear)><ALPHAV*(0,1,1,linear)><ANGLE*45>1,\n"+
            "@{4}<ANGLEV*tap=(0,90,1,sineinout)>2,\n{4}3,",0);
        Check(SimaiProcess.svTable.Count==1 && SimaiProcess.hsTable.Count==1,"Speed ramps must serialize");
        Check(SimaiProcess.angleTable.Count==2 && SimaiProcess.angleTable[1].streamIndex!=0,
            "Angle commands must retain overlay stream scope");
        Near(SimaiProcess.sizeTable[0].transition!.Evaluate(.125),1.5,"Pair X transition");
        Near(SimaiProcess.sizeTable[0].transition!.EvaluateY(.125),.75,"Pair Y transition");
        SvController.Load(new List<global::SvPoint> { new() { time=0,multiplier=3,
            transition=new NoteValueTransition { from=1,to=3,duration=2 } } });
        Near(SvController.GetCurrentSV(1),2,"Linear instantaneous SV");
        Near(SvController.GetCumulativeScroll(1),1.5,"Linear SV must integrate");
        Near(SvController.GetCumulativeScroll(3),7,"SV keeps its target after the ramp");
        Near(SvController.GetCumulativeScroll(-1),-1,"SV before the command stays at the starting value");
        SvController.Load(new List<global::SvPoint> { new() { time=0,multiplier=2,
            transition=new NoteValueTransition { from=-2,to=2,duration=2 } } });
        Near(SvController.GetCumulativeScroll(1),-1,"Negative-to-positive ramp integral");
        Near(SvController.GetCumulativeScroll(2),0,"Direction change must not accumulate drift");
        var points=AlphaVisualTiming.BuildScrollCurve(new[] {
            new ScrollChange(0,-2,0,2),new ScrollChange(1,0,1,2),new ScrollChange(2,2) });
        Near(AlphaVisualTiming.FindFirstTimeAtCumulativeScroll(points,0,2,-.75,false),.5,"First ramp crossing");
        Near(AlphaVisualTiming.FindLastTimeAtCumulativeScroll(points,2,-.75),1.5,"Last ramp crossing");
        foreach(var easing in new[] { "linear","easein","easeout","easeinout","sineinout" })
        {
            var ramp=new NoteValueTransition { from=1,to=3,duration=2,easing=easing,parameter=10 };
            Near(ramp.Evaluate(-1),1,easing+" start"); Near(ramp.Evaluate(3),3,easing+" end");
            Check(ramp.Evaluate(1)>1 && ramp.Evaluate(1)<3,easing+" intermediate");
        }
        SvController.Load(new List<global::SvPoint>());
        var appearance=4d;
        var key=SvController.CreateBirthSpeedCurve("",5,1,
            new NoteValueTransition { from=0,to=2,duration=1 },5.15,ref appearance);
        Near(SvController.GetCumulativeScroll(5,key)-SvController.GetCumulativeScroll(appearance,key),1,
            "HS ramp must integrate to the authored distance",.002);
        Near(SvController.GetCurrentSV(appearance,key),0,"Zero-speed HS starts stationary");
        SvController.Load(new List<global::SvPoint> { new() { time=0,multiplier=-1 } });
        appearance=4;
        key=SvController.CreateBirthSpeedCurve("",5,1,
            new NoteValueTransition { from=1,to=2,duration=1 },5.15,ref appearance);
        Check(appearance<5,"Negative SV still gives a visible HS appearance interval");
        Near(SvController.GetCumulativeScroll(5,key)-SvController.GetCumulativeScroll(appearance,key),-1,
            "HS transition integrates with negative SV",.002);
        appearance=4;
        key=SvController.CreateBirthSpeedCurve("slide",5,1,
            new NoteValueTransition { from=1,to=2,duration=1 },6,ref appearance,ignoreSv:true);
        Near(SvController.GetCumulativeScroll(5,key)-SvController.GetCumulativeScroll(appearance,key),1,
            "HS*slide appearance remains independent of SV motion",.002);
        var help=File.ReadAllText("MajdataEdit/SubWindow/AlphaSyntaxHelp.xaml.cs");
        var guide=System.Text.RegularExpressions.Regex.Match(help,"private const string SimaiGuide = @\"(.*?)\";",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        foreach(var line in guide.Split('\n').Select(line=>line.Trim()).Where(line=>line.StartsWith("E ")))
        {
            var example=line[2..];
            if(example.StartsWith('&')) continue;
            SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}"+example);
            Check(SimaiProcess.notelist.All(point=>string.IsNullOrEmpty(point.noteParseError)),
                "Simai help example does not parse: "+example);
        }
        SimaiProcess.ClearData(); SvController.Clear();
        Console.WriteLine($"PASS: {checks} note transition assertions");
    }
}
