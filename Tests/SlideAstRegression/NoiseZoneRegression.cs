using MajdataCore;
using MajdataEdit;
using System.Numerics;

internal static class NoiseZoneRegression
{
    public static void Run()
    {
        int checks=0;
        foreach(var source in new[] { "(120){4}1,2,3,4,", "(120){8}1,2,<COLOR*00BBFF>3,4,", "(120){4}1,\n|| comment\n{16}2,3,4,", "(120){4}1,2,3,4" })
        {
            SimaiProcess.ClearData(); SimaiProcess.Serialize(source);
            var expected=SimaiProcess.notelist.Select(t=>(t.sourcePosition,t.time)).ToArray();
            foreach(var point in expected)
            {
                var sampled=SimaiProcess.Serialize(source,point.sourcePosition);
                if(Math.Abs(sampled-point.time)>.00001) throw new Exception($"Caret/slot mismatch {point.sourcePosition}: {sampled} != {point.time}");
            }
        }

        void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        foreach(var args in new[]{"True,45","True,45,False","True,45,True","True,45,.3,True","Instant,45,8:1,True","2,45,True",".2,1,.2,45,True","False,.2,False"})
        {
            SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}<ROTATE*("+args+")>1,E");
            Require(SimaiProcess.effectTable.Count==1 && SimaiProcess.effectTable[0].rotatePV==args.EndsWith("True"),"ROTATE PV flag failed in actual Edit parser: "+args);
        }
        foreach(var descriptor in AlphaCommandGrammar.Commands) {
            var examples=AlphaCommandExamples.Examples(descriptor.name).Split(" / ");
            Require(examples.Length>=2, "Missing complete/special examples: "+descriptor.name);
            foreach(var example in examples)
                Require(AlphaCommandGrammar.TryValidate(example.Trim('<','>'),120,out var error), "Invalid hint example: "+example+" / "+error);
            Require(AlphaCommandExamples.Header(descriptor.name).Contains(descriptor.name+"*"), "Missing complete header: "+descriptor.name);
        }
        SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}<SV*blockarea=2><HS*blockarea=2>XD1[8:1],E");
        Require(SimaiProcess.svTable.Any(c=>c.noteType=="blockarea" && c.multiplier==2) &&
            SimaiProcess.hsTable.Any(c=>c.noteType=="blockarea" && c.multiplier==2), "Noise speed targets lost during actual Edit parsing");
        SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}<SV*noise=2><HS*noise=2><COLOR*noise=00BBFF><SIZE*noise=2><ALPHA*noise=.4>XD1[8:1],E");
        Require(SimaiProcess.svTable.Any(c=>c.noteType=="blockarea") && SimaiProcess.hsTable.Any(c=>c.noteType=="blockarea") &&
            SimaiProcess.colorTable.Any(c=>c.noteType=="blockarea"),"Legacy noise target must normalize to blockarea in Edit");
        const float touchSpeed=7.5f;
        var motion=AlphaVisualTiming.GetTouchMotionDuration(touchSpeed);
        foreach(var sv in new[]{1f,2f,-1f,-2f}) {
            SvController.Load(new List<SvPoint>{new(){time=0,multiplier=sv}});
            var lead=SvController.GetTouchWarningLead(4,touchSpeed,"blockarea");
            Require(Math.Abs(lead-motion/Math.Abs(sv))<.00001, "SV does not scale warning lead: "+sv);
            for(int n=0;n<300;n++) {
                var time=4-n*.005;
                var scroll=SvController.GetCumulativeScroll(time,"blockarea");
                var target=SvController.GetCumulativeScroll(4,"blockarea");
                var progress=AlphaVisualTiming.GetTouchWarningProgress(target,scroll,touchSpeed);
                var touchVisible=Math.Abs(AlphaVisualTiming.GetTouchVisualTiming(target,scroll,touchSpeed))<motion;
                Require((progress>=0)==touchVisible, "Noise and Touch appearance differ at SV="+sv);
            }
        }
        SvController.Load(new List<SvPoint>{new(){time=0,multiplier=1}});
        Require(SvController.GetTouchWarningLead(4,touchSpeed*2,"blockarea")<motion*.55,
            "HS times two does not shorten Touch/noise appearance");
        SvController.Load(new List<SvPoint>{new(){time=0,multiplier=-1},new(){time=2,multiplier=2},new(){time=3,multiplier=-2}});
        for(int n=0;n<900;n++) {
            double time=n*.005; var target=SvController.GetCumulativeScroll(5,"blockarea"); var scroll=SvController.GetCumulativeScroll(time,"blockarea");
            Require((AlphaVisualTiming.GetTouchWarningProgress(target,scroll,touchSpeed)>=0)==
                (Math.Abs(AlphaVisualTiming.GetTouchVisualTiming(target,scroll,touchSpeed))<motion), "Reversing SV/seek appearance disagrees with Touch");
        }
        SvController.Load(new List<SvPoint>());
        foreach (var token in new[] { "XD1", "XC", "XE8[8:1]", "XA1[#0.2]", "XB4[4:2]", "XD1[8:1]" })
        {
            Require(NoteExpressionParser.TryParse(token, out var parsed, out _), "Noise grammar rejected: " + token);
            Require(parsed.kind == NoteExpressionKind.NoiseZone, "Wrong kind: " + token);
            Require(NoteDurationTarget.TryFromSelection(token.Split('[')[0], out var slide) && !slide, "Noise duration completion failed");
        }
        foreach (var token in new[] { "X1", "XD9", "XD1h", "XD1[8:]", "XD1[8:1]garbage", "XD1-5[8:1]" })
            Require(!NoteExpressionParser.TryParse(token, out _, out _), "Bad noise token accepted: " + token);
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}<FAKE*False>XD1[8:1]/1,XE1[4:1]/XC[4:1]/XE2[4:1]/XE3[4:1]/XE4[4:1],E");
        var notes = SimaiProcess.notelist.SelectMany(point => point.getNotes()).ToList();
        Require(SimaiProcess.notelist.All(p => string.IsNullOrEmpty(p.noteParseError)), "Editor noise syntax error");
        var zones = notes.Where(n => n.noteType == SimaiNoteType.NoiseZone).ToList();
        Require(zones.Count == 6 && zones.All(n => n.isFake), "Noise contributes judge notes or was lost");
        Require(Math.Abs(zones[0].holdTime-.25) < .000001, "Noise did not use Hold BPM duration");
        Require(!SimaiProcess.notelist[0].isEach, "Noise falsely creates EACH with one tap");
        var timeline = new NoiseZoneTimeline();
        timeline.Configure(new[] { new NoiseZoneEvent(17, 2, 1, .5), new NoiseZoneEvent(17, 2.5, 1, .5), new NoiseZoneEvent(16, 2, 0, .5) });
        var warning = new float[33]; var active = new bool[33];
        timeline.Evaluate(1.5, warning, active);
        Require(warning[17] == 1 && !active[17] && !timeline.IsBlocked(17,1.5), "Warning must not disable sensor");
        timeline.Evaluate(1.999, warning, active);
        Require(warning[17] > .45 && warning[17] < .452, "Warning opacity must fade to 45%");
        foreach (var time in new[] { 2d, 2.8d, 3.2d }) Require(timeline.IsBlocked(17,time), "Overlapping noise released too early");
        Require(!timeline.IsBlocked(17,3.5), "Noise end was not exclusive");
        Require(!timeline.IsBlocked(16,2) && !timeline.TryGetVisualSpan(16,2,out _,out _), "Zero duration must be warning-only");
        timeline.Evaluate(1.8,warning,active);
        Require(warning[16]>0 && !active[16],"Zero duration lost its prewarning");
        SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}XC[1:0],XD1[384:1],E");
        var parsedZones=SimaiProcess.notelist.SelectMany(p=>p.getNotes().Where(n=>n.noteType==SimaiNoteType.NoiseZone).Select(n=>new NoiseZoneEvent(NoiseZoneTimeline.SensorIndex(n.touchArea,n.startPosition),p.time,n.holdTime,.5))).ToArray();
        Require(parsedZones.Length==2 && parsedZones[0].End==parsedZones[0].Start,"Parsed zero duration was extended");
        Require(Math.Abs(parsedZones[1].End-parsedZones[1].Start-2d/384)<.000001,"384th duration was clamped to a frame");
        var shortTimeline=new NoiseZoneTimeline(); shortTimeline.Configure(parsedZones);
        Require(shortTimeline.TryGetVisualSpan(17,.502,out _,out var shortEnd) && Math.Abs(shortEnd-(.5+2d/384))<.000001,"Short noise must render during its real duration");
        Require(!shortTimeline.TryGetVisualSpan(17,.506,out _,out _) && !shortTimeline.IsBlocked(17,.506),"Short noise outlives its real duration");
        Require(timeline.TryGetActiveSpan(17,2.8,out var mergedStart,out var mergedEnd) && mergedStart==2 && mergedEnd==3.5,"Overlaps retrigger a visual entry or retreat");
        Require(timeline.WarningShine(17,2)==0 && timeline.WarningShine(17,1)==0,"Ready flash leaks outside prewarning");
        int peaks=0; float beforeFlash=0,lastFlash=0;
        for(int n=0;n<=500;n++) {
            float next=timeline.WarningShine(17,1.5+n*.001);
            if(lastFlash>beforeFlash && lastFlash>next && lastFlash>.17f) peaks++;
            beforeFlash=lastFlash; lastFlash=next;
        }
        Require(peaks>=2 && peaks<=4,"Ready phase does not flash several times");
        var longWarning = new NoiseZoneTimeline();
        longWarning.Configure(new[] { new NoiseZoneEvent(17, 4, 1, 2) });
        Require(longWarning.WarningShine(17,2.2)>0 && longWarning.WarningShine(17,3)>0,
            "Long warning is incorrectly limited to the last half second");
        timeline.Evaluate(4, warning, active); Require(active.All(a => !a) && warning.All(a => a==0), "Noise not cleared after end");
        timeline.Evaluate(1.5, warning, active); Require(warning[17]==1 && !active[17], "Seeking did not restore warning");
        timeline.Configure(null); Require(!timeline.IsBlocked(17,2.5), "Reload retained noise");
        var centers = new[] { new Vector2(-1,0), new Vector2(1,0) };
        var cells = NoiseZoneGeometry.BuildCells(centers,2);
        Require(cells.All(cell => cell.Count >= 3), "Empty partition");
        int internalEdges=0;
        foreach(var cell in cells.Select((p,i)=>(p,i)))
        for(int v=0;v<cell.p.Count;v++)
        {
            var a=cell.p[v]; var b=cell.p[(v+1)%cell.p.Count];
            if(Math.Abs(a.X)<.00001 && Math.Abs(b.X)<.00001)
            {
                internalEdges++;
                Require(!NoiseZoneGeometry.IsUnionBoundary(cell.i,a,b,centers,new[]{true,true},2),"Internal seam remains after union");
                Require(NoiseZoneGeometry.IsUnionBoundary(cell.i,a,b,centers,new[]{cell.i==0,cell.i==1},2),"Single-zone outline missing");
            }
        }
        Require(internalEdges==2,"Fixture has no shared edge");
        var warningContours=NoiseZoneGeometry.UnionContours(cells,centers,new[]{true,true},2);
        Require(warningContours.Count==1 && warningContours[0].Points.Count>3,"Connected warnings are not one polygon");
        Require(!warningContours[0].Points.Any(p=>Math.Abs(p.X)<.00001 && Math.Abs(p.Y)<1.99),"Internal warning seam remains");
        var strengths=new[]{.4f,.8f}; var flashes=new[]{.12f,.18f};
        NoiseZoneGeometry.MergeWarningLevels(cells,centers,new[]{true,true},2,strengths,flashes);
        Require(strengths.All(s=>s==.8f) && flashes.All(s=>s==.18f),"Warning union opacity or flashing is discontinuous");
        foreach(var pending in new[]{"{","{4","{0}","{word}"}) {
            SimaiProcess.Serialize("(120){4}1,"+pending+",2,3,4,");
            Require(SimaiProcess.notelist.SelectMany(p=>p.getNotes()).Count(n=>n.noteType==SimaiNoteType.Tap)==4,"Incomplete division erased waveform notes: "+pending);
        }
        var leftCell=cells[0];
        var sharedA=leftCell.First(v=>Math.Abs(v.X)<.00001f);
        var sharedB=leftCell.Last(v=>Math.Abs(v.X)<.00001f);
        if(sharedB.Y<sharedA.Y) (sharedA,sharedB)=(sharedB,sharedA);
        double Area(List<Vector2> polygon) {
            double area=0;
            for(int n=0;n<polygon.Count;n++) { var a=polygon[n]; var b=polygon[(n+1)%polygon.Count]; area+=a.X*b.Y-b.X*a.Y; }
            return Math.Abs(area)*.5;
        }
        var small=NoiseZoneGeometry.GrowFromEdge(leftCell,sharedA,sharedB,.2f);
        var middle=NoiseZoneGeometry.GrowFromEdge(leftCell,sharedA,sharedB,.5f);
        Require(Area(small)>0 && Area(small)<Area(middle) && Area(middle)<Area(leftCell),"Growth does not expand from the shared edge");
        Require(small.All(v=>v.X>-.3f),"Growth starts from the far side instead of its neighbor");
        Require(NoiseZoneGeometry.GrowFromEdge(leftCell,sharedA,sharedB,0).Count==0,"Retreat does not finish at its boundary");
        Require(Area(NoiseZoneGeometry.GrowFromEdge(leftCell,sharedA,sharedB,1))==Area(leftCell),"Growth changes completed region shape");
        var chainCenters = new[] { new Vector2(-1,0), Vector2.Zero, new Vector2(1,0) };
        var chainCells = NoiseZoneGeometry.BuildCells(chainCenters,2);
        timeline.Configure(new[] { new NoiseZoneEvent(0,2,6,.5), new NoiseZoneEvent(1,3,3,.5), new NoiseZoneEvent(2,4,5,.5) });
        bool SharedVisible(int sensor, int neighbor, bool[] mask)
        {
            var cell=chainCells[sensor];
            for(int n=0;n<cell.Count;n++)
            {
                var a=cell[n]; var b=cell[(n+1)%cell.Count];
                var midpoint=(a+b)*.5f;
                if(Math.Abs(Vector2.DistanceSquared(midpoint,chainCenters[sensor])-Vector2.DistanceSquared(midpoint,chainCenters[neighbor]))<.00001f)
                    return NoiseZoneGeometry.IsUnionBoundary(sensor,a,b,chainCenters,mask,2);
            }
            throw new Exception("Missing fixture shared edge");
        }
        timeline.Evaluate(2.8,warning,active);
        Require(warning[1]>0 && !active[1] && SharedVisible(0,1,active),"Future warning merges into live region too early");
        timeline.Evaluate(3.2,warning,active);
        Require(active[0] && active[1] && !active[2] && !SharedVisible(0,1,active),"Later neighbor did not join the union");
        timeline.Evaluate(4.2,warning,active);
        Require(active[2] && !SharedVisible(1,2,active),"Third neighbor retains an interior seam");
        timeline.Evaluate(6.2,warning,active);
        Require(active[0] && !active[1] && active[2] && SharedVisible(0,1,active) && SharedVisible(2,1,active),"Ended bridge did not split the union");
        timeline.Evaluate(8.2,warning,active);
        Require(!active[0] && !active[1] && active[2],"Different end times remove surviving blocks");
        var demo = new[] { "output/Alpha_Noise_Showcase/maidata.txt", "../output/Alpha_Noise_Showcase/maidata.txt" }.FirstOrDefault(File.Exists);
        if (demo != null)
        {
            var chart=File.ReadAllText(demo).Split("&inote_5=")[1];
            SimaiProcess.Serialize(chart);
            Require(SimaiProcess.notelist.All(p=>string.IsNullOrEmpty(p.noteParseError)) && SimaiProcess.ValidateAlphaCommands(chart).Count==0,
                "Showcase contains syntax errors: " + string.Join("; ",SimaiProcess.notelist.Where(p=>!string.IsNullOrEmpty(p.noteParseError)).Select(p=>$"{p.time}: {p.noteParseError}")));
            Require(SimaiProcess.notelist.SelectMany(p=>p.getNotes()).Count(n=>n.noteType==SimaiNoteType.NoiseZone)==15,"Showcase noise events lost");
        }
        SimaiProcess.ClearData();
        Console.WriteLine($"NOISE CORE PASS: {checks} assertions");
    }
}
