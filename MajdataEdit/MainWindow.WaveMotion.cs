using System.Drawing;
using System.Drawing.Drawing2D;

namespace MajdataEdit;

public partial class MainWindow
{
    private SimaiTimingPoint? waveMotionTiming;
    private object? waveMotionSource;
    private (float Ring,float Touch,float Star) waveMotionSpeeds;
    private readonly List<(NoteMonitor.Entry Entry,NoteMonitor.Result Result,NoteMonitor.Plot Position)> waveMotion = new();
    private static readonly Font waveMotionFont = new("Consolas",9f,System.Drawing.FontStyle.Regular,GraphicsUnit.Pixel);

    private void UpdateWaveMotion(SimaiTimingPoint timing)
    {
        var speeds=(Ring:editorSetting.playSpeed,Touch:editorSetting.touchSpeed,Star:editorSetting.starSpeed);
        var changed=!ReferenceEquals(waveMotionSource,SimaiProcess.svTable) || waveMotionSpeeds!=speeds;
        if(ReferenceEquals(waveMotionTiming,timing) && !changed) return;
        waveMotionTiming=timing; waveMotionSource=SimaiProcess.svTable; waveMotionSpeeds=speeds;
        waveMotion.Clear();
        var model=GetNoteMonitorModel();
        foreach(var entry in NoteMonitor.Entries(timing))
        {
            if (entry.Type == "blockarea" || entry.Body && !entry.Note.isTrajectoryOnly) continue;
            var result=model.Sample(entry);
            var position=result.Plots.FirstOrDefault(plot=>plot.Channels[0] is "radius" or "0–1");
            if(position!=null) waveMotion.Add((entry,result,position));
        }
    }

    private void DrawWaveMotion(Graphics graphics,SimaiTimingPoint timing,WaveViewport viewport)
    {
        UpdateWaveMotion(timing);
        if(waveMotion.Count==0) return;
        var min=waveMotion.Min(item=>item.Position.Values[0].Min());
        var max=waveMotion.Max(item=>item.Position.Values[0].Max());
        foreach(var item in waveMotion)
        {
            if(item.Position.Spawn.HasValue) min=Math.Min(min,item.Position.Spawn.Value);
            if(item.Position.Destroy.HasValue) max=Math.Max(max,item.Position.Destroy.Value);
        }
        if(max-min<.0001) {min-=.5; max+=.5;}
        var top=12f; var bottom=Math.Max(top+1,wavePaintHeight-14f);
        float Y(double value)=>(float)(bottom-(value-min)/(max-min)*(bottom-top));
        var saved=graphics.Save();
        graphics.SetClip(new RectangleF(0,0,viewport.DurationToPixels(viewport.Duration),wavePaintHeight),CombineMode.Intersect);
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var light=ThemeManager.CurrentIsLight;
        Color[] colors={light ? Color.FromArgb(32,133,81) : WaveSpawnMarkerColor,
            light ? Color.FromArgb(111,72,179) : Color.FromArgb(204,172,255),
            light ? Color.FromArgb(171,101,13) : Color.FromArgb(255,209,112)};
        for(var index=0;index<waveMotion.Count;index++)
        {
            var item=waveMotion[index]; var result=item.Result; var plot=item.Position;
            if(result.End<viewport.StartTime || result.Appearance>viewport.EndTime) continue;
            var values=plot.Values[0]; var points=new PointF[values.Length];
            for(var sample=0;sample<values.Length;sample++)
            {
                var time=result.Appearance+(result.End-result.Appearance)*sample/(values.Length-1);
                points[sample]=new(viewport.TimeToX(time),Y(values[sample]));
            }
            var color=colors[index%colors.Length];
            using var outline=new Pen(light ? Color.FromArgb(215,245,247,250) : Color.FromArgb(215,21,24,29),3.6f);
            using var line=new Pen(color,1.6f);
            graphics.DrawLines(outline,points);
            if(plot.Inactive==null) graphics.DrawLines(line,points);
            else
            {
                using var hidden=new Pen(Color.FromArgb(110,color),1.1f) {DashStyle=DashStyle.Dash};
                for(var sample=1;sample<points.Length;sample++)
                    graphics.DrawLine(plot.Inactive[sample] ? hidden : line,points[sample-1],points[sample]);
            }
            var startX=viewport.TimeToX(result.Appearance);
            if(!viewport.Contains(result.Appearance)) continue;
            using var marker=new Pen(Color.FromArgb(110,color),.8f) {DashStyle=DashStyle.Dot};
            graphics.DrawLine(marker,startX,top,startX,bottom);
            using var text=new SolidBrush(color);
            graphics.DrawString($"{max:0.###}",waveMotionFont,text,startX+3,top);
            graphics.DrawString($"{min:0.###}",waveMotionFont,text,startX+3,bottom-12);
            var position=item.Entry.Note.isDZone ? item.Entry.Note.startPosition-.5f : item.Entry.Note.startPosition;
            DrawScrollSpawnGlyph(graphics,item.Entry.Note,startX,WaveNoteY(position,wavePaintHeight));
            graphics.FillEllipse(text,points[0].X-2.5f,points[0].Y-2.5f,5,5);
        }
        graphics.Restore(saved);
    }
}
