using MajdataCore;

namespace MajdataEdit;

// Read-only authoring view. SV integration and birth-time HS use the same source
// as View; nothing here changes the playing chart or input/judgement state.
internal sealed class NoteMonitor
{
    internal sealed record Entry(SimaiTimingPoint Timing, SimaiNote Note, string Type, bool Body, string Name)
    { public override string ToString()=>Name; }
    internal sealed record Plot(string Name, string[] Channels, double[][] Values,
        bool[]? Inactive = null, double? Spawn = null, double? Destroy = null);
    internal sealed record Result(double Appearance, double Judge, double End,
        List<(string Name,string Value)> Properties, List<Plot> Plots);
    private sealed record State(double Time, int Position, int Stream, string Type, bool Live, bool Reset,
        float X, float Y, string Color, NoteValueTransition? Curve);
    private readonly Dictionary<string, List<State>> properties = new();
    private readonly Dictionary<(string Property,int Stream,string Type,bool Live),State[]> timelines = new();
    private readonly float ringSpeed, touchSpeed, starSpeed;
    private readonly Dictionary<Entry,Result> samples = new();
    private (int Position,int Stream) sampledSlot = (-1,-1);

    internal NoteMonitor(float ringSpeed, float touchSpeed, float starSpeed)
    {
        this.ringSpeed = AlphaVisualTiming.ToViewNoteSpeed(ringSpeed);
        this.touchSpeed = touchSpeed; this.starSpeed = starSpeed;
        SvController.Load(SimaiProcess.svTable);
        properties["HS"] = SimaiProcess.hsTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",false,v.reset,v.multiplier,v.multiplier,"",v.transition)).ToList();
        properties["SPAWN"] = SimaiProcess.spawnTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",false,v.reset,v.radius,v.radius,"",null)).ToList();
        properties["DESTROY"] = SimaiProcess.destroyTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",false,v.reset,v.radius,v.radius,"",null)).ToList();
        properties["BOUNCE"] = SimaiProcess.bounceTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",false,v.reset,v.duration,v.duration,"",null)).ToList();
        properties["SPAWNMODE"] = SimaiProcess.spawnModeTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",false,v.reset,(float)v.mode,(float)v.mode,"",null)).ToList();
        properties["SIZE"] = SimaiProcess.sizeTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",v.live,v.reset,v.scaleX == 0 ? v.scale : v.scaleX,
            v.scaleY == 0 ? v.scale : v.scaleY,"",v.transition)).ToList();
        properties["ALPHA"] = SimaiProcess.alphaTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",v.live,v.reset,v.alpha,v.alpha,"",v.transition)).ToList();
        properties["ANGLE"] = SimaiProcess.angleTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType ?? "",v.live,v.reset,v.angle,v.angle,"",v.transition)).ToList();
        properties["COLOR"] = SimaiProcess.colorTable.Select(v => new State(v.time,v.sourcePosition,v.streamIndex,
            v.noteType,v.live,v.color.Equals("NULL",StringComparison.OrdinalIgnoreCase),1,1,v.color,v.transition)).ToList();
        foreach (var values in properties.Values)
            values.Sort((a,b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Position.CompareTo(b.Position));
        foreach (var property in properties)
            foreach (var group in property.Value.GroupBy(v=>(v.Stream,v.Type,v.Live)))
                timelines[(property.Key,group.Key.Stream,group.Key.Type,group.Key.Live)] = group.ToArray();
    }

    internal static List<Entry> Entries(SimaiTimingPoint timing)
    {
        var result = new List<Entry>();
        foreach (var note in timing.getNotes())
        {
            var index = result.Count + 1;
            var type = note.noteType switch { SimaiNoteType.Hold => "hold", SimaiNoteType.Touch => "touch",
                SimaiNoteType.TouchHold => "touchhold", SimaiNoteType.NoiseZone => "blockarea",
                SimaiNoteType.Slide => "star", _ => note.isForceStar ? "star" : "tap" };
            var text = note.noteContent ?? timing.notesContent;
            if (note.noteType != SimaiNoteType.Slide)
            {
                var position=type is "touch" or "touchhold" or "blockarea"
                    ? note.touchArea=='C' ? "C" : $"{note.touchArea}{note.startPosition}"
                    : $"{note.startPosition}{(note.isDZone ? "d" : "")}";
                result.Add(new(timing,note,type,false,$"{index:00} · {(type=="blockarea" ? "X" : "")}{position} · {type}"));
            }
            else if (note.isTrajectoryOnly)
                result.Add(new(timing,note,note.trajectoryCarrierType==SimaiNoteType.Tap && note.isForceStar ? "star" : note.trajectoryCarrierType.ToString().ToLowerInvariant(),true,
                    $"{index:00} · {text} · 轨迹继承"));
            else
            {
                if (!note.isSlideNoHead)
                    result.Add(new(timing,note,note.isTouchSlide && note.touchArea != 'K' ? "touch" : "star",false,
                        $"{index:00} · {note.startPosition}{(note.isDZone ? "d" : "")} · 星头"));
                result.Add(new(timing,note,"slide",true,$"{result.Count+1:00} · {text} · 轨迹"));
            }
        }
        return result;
    }

    private State? Find(string property, Entry entry, double now, bool global = true)
    {
        var note = entry.Note; var timing = entry.Timing;
        var mine = entry.Body ? note.isMineSlide : note.isMineHead;
        var isBreak = entry.Body && !note.isTrajectoryOnly ? note.isSlideBreak : note.isBreak;
        var each = timing.isEachInStream ?? timing.isEach;
        State? Lookup(string type, bool live)
        {
            if (!timelines.TryGetValue((property,timing.streamIndex,type,live),out var values)) return null;
            var low=0; var high=values.Length-1; var index=-1; var time=live ? now : timing.time;
            while(low<=high)
            {
                var middle=(low+high)/2;
                if(values[middle].Time<=time) { index=middle; low=middle+1; } else high=middle-1;
            }
            var state=index<0 ? null : values[index];
            return state?.Reset == false ? state : null;
        }
        State? Typed(bool live)
        {
            if (entry.Type == "blockarea") return Lookup(entry.Type,live);
            if (mine && property == "COLOR") return Lookup("mine",live);
            return (mine ? Lookup("mine",live) : null) ?? (isBreak ? Lookup("break",live) : null) ??
                (each ? Lookup("each",live) : null) ?? Lookup(entry.Type,live) ??
                (property == "COLOR" && entry.Type == "star" ? Lookup("tap",live) : null);
        }
        return Typed(true) ?? Typed(false) ?? (global && !(mine && property == "COLOR")
            ? Lookup("",true) ?? Lookup("",false) : null);
    }

    internal Result Sample(Entry entry)
    {
        var slot=(entry.Timing.sourcePosition,entry.Timing.streamIndex);
        if(sampledSlot!=slot) {samples.Clear(); sampledSlot=slot;}
        if(samples.TryGetValue(entry,out var cached)) return cached;
        var timing = entry.Timing; var note = entry.Note;
        var touch = entry.Type is "touch" or "touchhold" or "blockarea";
        var slide = entry.Type == "slide";
        var hs = Find("HS",entry,timing.time,!slide);
        // Slide appearance only accepts HS*slide; its path clock only accepts SV*slide.
        if (slide) hs = properties["HS"].LastOrDefault(v => v.Stream == timing.streamIndex && v.Type == "slide" &&
            v.Time <= timing.time) is { Reset:false } slideHs ? slideHs : null;
        var multiplier = hs?.Curve?.from ?? hs?.X ?? (slide ? 1f : timing.HSpeed);
        var speed = (touch ? touchSpeed : ringSpeed) * multiplier;
        var spawn = Find("SPAWN",entry,timing.time)?.X ?? 1.225f;
        var destroy = Find("DESTROY",entry,timing.time)?.X ?? 4.8f;
        var once = Find("SPAWNMODE",entry,timing.time)?.X == (float)SpawnVisualMode.Once;
        var curve = SvController.MakeCurveKey(timing.streamIndex,entry.Type);
        foreach (var target in new[] { note.isMineHead || note.isMineSlide ? "mine" : "",
                     (entry.Body ? note.isSlideBreak : note.isBreak) ? "break" : "",
                     (timing.isEachInStream ?? timing.isEach) ? "each" : "" })
            if (target.Length > 0 && SvController.HasTypedCurve(SvController.MakeCurveKey(timing.streamIndex,target)))
            { curve = SvController.MakeCurveKey(timing.streamIndex,target); break; }
        var end = entry.Body ? note.slideStartTime + note.slideTime : timing.time + Math.Max(0,note.holdTime);
        if (note.noteType == SimaiNoteType.NoiseZone) end = timing.time + note.holdTime;
        var appearance = touch ? timing.time - SvController.GetTouchWarningLead(timing.time,speed,curve)
            : slide ? timing.time - (Math.Abs(speed) < .0001 ? 0 : 3.926913 / Math.Abs(speed) * (1-Math.Clamp(starSpeed,-1,1)))
            : entry.Body ? timing.time
            : SvController.GetRingAppearanceTime(timing.time,speed,spawn,destroy,curve);
        if (hs?.Curve != null)
        {
            var distance = touch ? AlphaVisualTiming.GetTouchMotionDuration(touchSpeed)
                : slide ? (timing.time-appearance)*multiplier : (Math.Abs(destroy-spawn)+AlphaVisualTiming.SpawnScaleDistance)/ringSpeed;
            curve = SvController.CreateBirthSpeedCurve(curve,timing.time,distance,hs.Curve,end+.15,ref appearance,ignoreSv:slide);
            speed = touch ? touchSpeed : ringSpeed;
        }
        var bounce = !touch && !entry.Body ? Find("BOUNCE",entry,timing.time)?.X ?? 0 : 0;
        var bounceDirection = SvController.GetBounceDirection(timing.time,multiplier,curve);
        if (bounce > 0) appearance = SvController.GetBounceStartTime(timing.time,bounce,multiplier,curve);
        var animationAppearance=appearance;
        if(!entry.Body && bounce<=0 && hs?.Curve==null)
            appearance=SvController.MonitorFirstAppearance(timing.time,speed,spawn,destroy,curve,touch,appearance);
        var noteScroll = SvController.GetCumulativeScroll(timing.time,curve);
        const int count = 193;
        var inactive = new bool[count]; var crossing = new SpawnCrossingMemo();
        var modified = new HashSet<string>();
        var motionCurve = entry.Body ? SvController.MakeCurveKey(timing.streamIndex,"slide") : curve;
        var motionModified = hs != null || !touch && !entry.Body && (Find("SPAWN",entry,timing.time) != null || Find("DESTROY",entry,timing.time) != null) ||
            bounce > 0 || SimaiProcess.svTable.Any(v=>v.streamIndex==timing.streamIndex && !v.reset && v.time<=end &&
                (SvController.MakeCurveKey(v.streamIndex,v.noteType)==motionCurve ||
                 !entry.Body && !slide && string.IsNullOrEmpty(v.noteType)));
        var position = new double[count]; var velocity = new double[count];
        var sizeX = new double[count]; var sizeY = new double[count]; var alpha = new double[count];
        var angle = new double[count]; var r = new double[count]; var g = new double[count]; var b = new double[count];
        double Value(State? state, double now, float fallback, bool y = false) => state == null ? fallback
            : state.Curve != null ? (y ? state.Curve.EvaluateY(now-(state.Live ? state.Time : animationAppearance))
                : state.Curve.Evaluate(now-(state.Live ? state.Time : animationAppearance))) : y ? state.Y : state.X;
        for (var i=0; i<count; i++)
        {
            var now = appearance + (end-appearance)*i/(count-1);
            position[i] = entry.Body ? SvController.GetTypedOnlyProgress(note.slideStartTime,note.slideTime,now,
                    motionCurve)
                : touch ? Math.Clamp(AlphaVisualTiming.GetTouchWarningProgress(noteScroll,
                    SvController.GetCumulativeScroll(Math.Min(now,timing.time),curve),speed),0,1)
                : SvController.GetVisualRadius(noteScroll,speed,Math.Min(now,timing.time),spawn,destroy,curve);
            if (bounce > 0 && now < timing.time)
            {
                var phase = SvController.GetBounceProgress(timing.time,bounce,multiplier,bounceDirection,now,curve)*2-1;
                position[i] = destroy + (bounceDirection < 0 ? 1 : -1)*(destroy-spawn)*(1-phase*phase);
            }
            if(!touch && !entry.Body && bounce<=0)
            {
                var running = AlphaVisualTiming.GetPathPosition((float)position[i],spawn,destroy)>=-AlphaVisualTiming.Epsilon ||
                    once && SvController.HasEverCrossedSpawn(ref crossing,noteScroll,speed,now,spawn,curve,destroy);
                inactive[i]=!running;
            }
            var scale = Find("SIZE",entry,now,entry.Type is not ("slide" or "blockarea"));
            sizeX[i] = Value(scale,now,1); sizeY[i] = Value(scale,now,1,true);
            var opacity=Find("ALPHA",entry,now,entry.Type != "blockarea");
            var rotation=Find("ANGLE",entry,now,entry.Type is not ("slide" or "slidestar" or "blockarea"));
            alpha[i] = Math.Clamp(Value(opacity,now,1),0,1);
            angle[i] = Value(rotation,now,0);
            var color = Find("COLOR",entry,now,entry.Type != "blockarea");
            if(scale?.Live==true) modified.Add("SIZE"); if(opacity?.Live==true) modified.Add("ALPHA");
            if(rotation?.Live==true) modified.Add("ANGLE"); if(color?.Live==true) modified.Add("COLOR");
            if (color?.Curve != null)
            {
                var rgb = color.Curve.EvaluateColor(now-(color.Live ? color.Time : animationAppearance));
                r[i]=rgb.R; g[i]=rgb.G; b[i]=rgb.B;
            }
            else
            {
                var rgb = string.IsNullOrEmpty(color?.Color) ? 0xFFFFFF : Convert.ToInt32(color.Color,16);
                r[i]=((rgb>>16)&255)/255d; g[i]=((rgb>>8)&255)/255d; b[i]=(rgb&255)/255d;
            }
        }
        var dt = (end-appearance)/(count-1);
        for (var i=0; i<count; i++)
        {
            var left = Math.Max(0,i-1); var right = Math.Min(count-1,i+1);
            velocity[i] = dt > .0000001 ? (position[right]-position[left])/((right-left)*dt) : 0;
            if(!touch && !entry.Body && bounce<=0)
                velocity[i] = AlphaVisualTiming.GetPathDirection(spawn,destroy)*speed*
                    SvController.GetCurrentSV(appearance+(end-appearance)*i/(count-1),curve);
        }
        var plots = new List<Plot>();
        if(motionModified)
        {
            var noise = entry.Type == "blockarea";
            plots.Add(new(entry.Body ? "路径进度" : noise ? "噪域预警进度" : touch ? "Touch 收拢进度" : "位置（实际半径）",
                new[]{entry.Body || touch ? "0–1" : "radius"},new[]{position},inactive,touch || entry.Body ? null : spawn,touch || entry.Body ? null : destroy));
            plots.Add(new(noise ? "预警进度变化率（/s）" : touch ? "收拢速度（/s）" :
                entry.Body ? "路径进度变化率（/s）" : "速度（半径/s）",new[]{"v"},new[]{velocity}));
        }
        if(modified.Contains("SIZE")) plots.Add(new("大小倍率",new[]{"X","Y"},new[]{sizeX,sizeY}));
        if(modified.Contains("COLOR")) plots.Add(new("染色 RGB",new[]{"R","G","B"},new[]{r,g,b}));
        if(modified.Contains("ALPHA")) plots.Add(new("透明度倍率",new[]{"α"},new[]{alpha}));
        if(modified.Contains("ANGLE")) plots.Add(new("贴图角度（°）",new[]{"angle"},new[]{angle}));
        string Format(State? state,string fallback)
        {
            if(state==null) return fallback;
            if(state.Curve is not { } transition)
                return state.Color.Length>0 ? "#"+state.Color : state.X==state.Y ? $"{state.X:0.###}" : $"{state.X:0.###} × {state.Y:0.###}";
            string Endpoint(float x,float y,float z) => state.Color.Length>0
                ? $"#{(int)Math.Round(x*255):X2}{(int)Math.Round(y*255):X2}{(int)Math.Round(z*255):X2}"
                : x==y ? $"{x:0.###}" : $"({x:0.###}, {y:0.###})";
            return $"{Endpoint(transition.from,transition.fromY,transition.fromZ)} → {Endpoint(transition.to,transition.toY,transition.toZ)}"+
                $" · {transition.duration:0.####}s · {transition.easing}"+
                (transition.easing.StartsWith("ease") ? $"({transition.parameter:0.###})" : "");
        }
        var attributes=new List<(string,string)> {("HS",Format(hs,$"{multiplier:0.###}"))};
        if(!touch && !entry.Body)
        {
            attributes.Add(("SPAWN",$"{spawn:0.###}")); attributes.Add(("DESTROY",$"{destroy:0.###}"));
            attributes.Add(("SPAWNMODE",once ? "Once" : "Rewind"));
        }
        if(bounce>0) attributes.Add(("BOUNCE",$"{bounce:0.####}s"));
        attributes.Add(("FAKE",note.isFake || note.isTrajectoryOnly ? "True" : "False"));
        foreach(var property in new[]{"COLOR","SIZE","ANGLE","ALPHA"})
            if(!modified.Contains(property))
            {
                var global=entry.Type!="blockarea" && (property is "COLOR" or "ALPHA" ||
                    property=="SIZE" && entry.Type!="slide" || property=="ANGLE" && entry.Type is not ("slide" or "slidestar"));
                var state=Find(property,entry,timing.time,global);
                if (entry.Type == "blockarea" && state == null) continue;
                attributes.Add((property,Format(state,property=="COLOR" ? "默认" : property=="ANGLE" ? "0°" : "1")));
            }
        if(!string.IsNullOrEmpty(note.noteSkin)) attributes.Add(("图片",note.noteSkin));
        var result = new Result(appearance,timing.time,end,attributes,plots);
        if (hs?.Curve != null) SvController.ReleaseMonitorBirthCurve(curve);
        samples[entry]=result;
        return result;
    }
}
