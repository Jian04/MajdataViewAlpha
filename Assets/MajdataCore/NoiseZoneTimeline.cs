using System;
using System.Collections.Generic;

namespace MajdataCore
{
    // Environment events, deliberately separate from note counts and judge queues.
    public readonly struct NoiseZoneEvent
    {
        public readonly int Sensor;
        public readonly double Start, End, WarningStart;
        public NoiseZoneEvent(int sensor, double start, double duration, double lead)
        {
            Sensor = sensor;
            Start = start;
            End = start + Math.Max(0, duration);
            WarningStart = start - Math.Max(0.001d, lead);
        }
    }
    public sealed class NoiseZoneTimeline
    {
        private readonly List<NoiseZoneEvent> events = new();
        private readonly List<NoiseZoneEvent>[] spans = new List<NoiseZoneEvent>[33];
        public void Configure(IEnumerable<NoiseZoneEvent> source)
        {
            events.Clear();
            if (source != null) events.AddRange(source);
            events.Sort((a, b) => a.WarningStart.CompareTo(b.WarningStart));
            for (int i=0;i<spans.Length;i++) {
                spans[i] ??= new List<NoiseZoneEvent>(); spans[i].Clear();
                var ordered=new List<NoiseZoneEvent>();
                foreach(var item in events) if(item.Sensor==i && item.End>item.Start) ordered.Add(item);
                ordered.Sort((a,b)=>a.Start.CompareTo(b.Start));
                foreach(var item in ordered) {
                    if(spans[i].Count==0 || spans[i][spans[i].Count-1].End<item.Start) spans[i].Add(item);
                    else {
                        var previous=spans[i][spans[i].Count-1];
                        spans[i][spans[i].Count-1]=new NoiseZoneEvent(i,previous.Start,Math.Max(previous.End,item.End)-previous.Start,previous.Start-previous.WarningStart);
                    }
                }
            }
        }
        public bool IsBlocked(int sensor, double time)
        {
            foreach (var zone in events)
            {
                if (zone.WarningStart > time) break;
                if (zone.Sensor == sensor && time >= zone.Start && time < zone.End) return true;
            }
            return false;
        }
        public void Evaluate(double time, float[] warnings, bool[] active)
        {
            Array.Clear(warnings, 0, warnings.Length);
            Array.Clear(active, 0, active.Length);
            foreach (var zone in events)
            {
                if (zone.WarningStart > time) break;
                if (zone.Sensor < 0 || zone.Sensor >= active.Length || time >= zone.End) continue;
                if (time >= zone.Start) active[zone.Sensor] = true;
                else
                {
                    var progress = (time - zone.WarningStart) / (zone.Start - zone.WarningStart);
                    warnings[zone.Sensor] = Math.Max(warnings[zone.Sensor], (float)(1d - 0.55d * progress));
                }
            }
        }
        public bool TryGetActiveSpan(int sensor,double time,out double start,out double end)
        {
            start=end=0;
            if(sensor<0 || sensor>=spans.Length || spans[sensor]==null) return false;
            var ranges=spans[sensor]; int low=0,high=ranges.Count-1,index=-1;
            while(low<=high) { int middle=(low+high)/2; if(ranges[middle].Start<=time) { index=middle; low=middle+1; } else high=middle-1; }
            if(index>=0 && time<ranges[index].End) { start=ranges[index].Start; end=ranges[index].End; return true; }
            return false;
        }
        // Visual and input lifetimes are identical. Zero duration is warning-only.
        public bool TryGetVisualSpan(int sensor,double time,out double start,out double end)
            => TryGetActiveSpan(sensor,time,out start,out end);
        // Flash throughout the speed/SV-dependent warning window. The chart
        // clock (rather than wall time) keeps pauses and backward seeks stable.
        public float WarningShine(int sensor,double time)
        {
            if(IsBlocked(sensor,time)) return 0;
            foreach(var zone in events) {
                if(zone.WarningStart>time) break;
                if(zone.Sensor==sensor && time>=zone.WarningStart && time<zone.Start)
                    return (float)(.16*(1+.5*Math.Sin((time-zone.WarningStart)*37.9)));
            }
            return 0;
        }
        public static int SensorIndex(char area, int position) => char.ToUpperInvariant(area) switch
        {
            'A' when position >= 1 && position <= 8 => position - 1,
            'B' when position >= 1 && position <= 8 => position + 7,
            'C' => 16,
            'D' when position >= 1 && position <= 8 => position + 16,
            'E' when position >= 1 && position <= 8 => position + 24,
            _ => -1
        };
    }
}
