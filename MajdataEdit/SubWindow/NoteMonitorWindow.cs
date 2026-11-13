using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed class NoteMonitorWindow : Window
{
    private readonly StackPanel sections = new();
    private readonly NoteMonitorPreview previewRenderer = new();
    private SimaiTimingPoint[] slots = Array.Empty<SimaiTimingPoint>();
    private NoteMonitor? monitor;
    internal string ChartFolder="";
    internal EditorSetting PreviewSettings=new();

    internal NoteMonitorWindow()
    {
        Title="音符监控"; SessionTheme.Apply(this,400,720);
        sections.Children.Add(SessionTheme.Text("将光标放到一个已完成的槽位。",true));
        Content=new ScrollViewer { Content=sections,Margin=new Thickness(12),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    }
    internal void UpdateSlot(IReadOnlyList<SimaiTimingPoint> timings, NoteMonitor model)
    {
        if (slots.SequenceEqual(timings) && ReferenceEquals(monitor,model)) return;
        slots=timings.ToArray(); monitor=model;
        sections.Children.Clear();
        foreach(var entry in timings.SelectMany(NoteMonitor.Entries))
        {
            var result=monitor!.Sample(entry);
            var content=new StackPanel();
            var header=new Grid(); header.ColumnDefinitions.Add(new(){Width=new GridLength(104)}); header.ColumnDefinitions.Add(new());
            FrameworkElement preview;
            try { preview=new Image {Source=previewRenderer.Create(entry,ChartFolder,PreviewSettings),Width=96,Height=64,Stretch=Stretch.Uniform,
                RenderTransform=new TranslateTransform(-6,-4)}; }
            catch(Exception error) when(error is System.IO.IOException or NotSupportedException or ArgumentException)
            { preview=SessionTheme.Text("图片读取失败："+error.Message,true,10); }
            header.Children.Add(preview);
            var label=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            var title=SessionTheme.Text($"{sections.Children.Count+1:00} · {entry.Name[(entry.Name.IndexOf('·')+1)..].Trim()}");
            title.FontWeight=FontWeights.SemiBold; label.Children.Add(title);
            label.Children.Add(SessionTheme.Text($"音符流 {entry.Timing.streamIndex} · BPM {entry.Timing.currentBpm:0.###} · {entry.Timing.subdivision} 分音",size:12));
            label.Children.Add(SessionTheme.Text(NoteMonitorPreview.Traits(entry),true,12));
            Grid.SetColumn(label,1); header.Children.Add(label); content.Children.Add(header);
            content.Children.Add(SessionTheme.Text($"出现 {result.Appearance:0.0000}s  ·  击打 {result.Judge:0.0000}s\n结束 {result.End:0.0000}s"));
            var attributes=new Grid {Margin=new Thickness(0,10,0,0)};
            attributes.ColumnDefinitions.Add(new(){Width=new GridLength(108)}); attributes.ColumnDefinitions.Add(new());
            foreach(var property in result.Properties)
            {
                var row=attributes.RowDefinitions.Count; attributes.RowDefinitions.Add(new(){Height=GridLength.Auto});
                var name=SessionTheme.Text(property.Name,size:12); var value=SessionTheme.Text(property.Value,size:12);
                name.Margin=value.Margin=new Thickness(0,3,0,3); Grid.SetRow(name,row); Grid.SetRow(value,row); Grid.SetColumn(value,1);
                attributes.Children.Add(name); attributes.Children.Add(value);
            }
            content.Children.Add(attributes);
            foreach(var plot in result.Plots)
            {
                var caption=SessionTheme.Text(plot.Name); caption.Margin=new Thickness(0,12,0,5); content.Children.Add(caption);
                content.Children.Add(new MonitorPlot(plot,result) {Height=206,Margin=new Thickness(0,0,0,8)});
            }
            var section=new Border {Child=content,BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(0,0,0,12),Margin=new Thickness(0,0,0,16)};
            section.SetResourceReference(Border.BorderBrushProperty,"MenuSeparator"); sections.Children.Add(section);
        }
        if(sections.Children.Count==0) sections.Children.Add(SessionTheme.Text(timings.FirstOrDefault()?.noteParseError ?? "当前槽位没有音符。",true));
    }

    private sealed class MonitorPlot : FrameworkElement
    {
        private readonly NoteMonitor.Plot plot;
        private readonly NoteMonitor.Result timing;
        internal MonitorPlot(NoteMonitor.Plot plot, NoteMonitor.Result timing)
        { this.plot=plot; this.timing=timing; }
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            var foreground=SessionTheme.Brush("ButtonForeground","#E4E8EF");
            var grid=SessionTheme.Brush("MenuSeparator","#39404A");
            var accent=SessionTheme.Brush("AccentBrush","#64B5FF");
            var xAxis=new SolidColorBrush(ThemeManager.CurrentIsLight ? Color.FromRgb(36,93,163) : Color.FromRgb(102,183,255));
            var yAxis=foreground;
            var spawnBrush=Brushes.MediumSeaGreen; var destroyBrush=Brushes.MediumPurple;
            Brush[] colors=plot.Channels.Length==3 ? new Brush[]{Brushes.Coral,Brushes.MediumSeaGreen,Brushes.CornflowerBlue}
                : new[]{accent,Brushes.Goldenrod};
            const double left=52,top=52,bottom=152;
            var right=Math.Max(left+20,ActualWidth-14);
            context.DrawRoundedRectangle(SessionTheme.Brush("EditorBackground","#1B1D21"),null,new Rect(0,0,ActualWidth,ActualHeight),5,5);
            var min=plot.Values.Min(v=>v.Min()); var max=plot.Values.Max(v=>v.Max());
            if(plot.Spawn.HasValue) {min=Math.Min(min,plot.Spawn.Value);max=Math.Max(max,plot.Spawn.Value);}
            if(plot.Destroy.HasValue) {min=Math.Min(min,plot.Destroy.Value);max=Math.Max(max,plot.Destroy.Value);}
            if (max-min<Math.Max(.0001,Math.Abs(max)*.001)) { min-=.5; max+=.5; }
            var padding=(max-min)*.08; min-=padding; max+=padding;
            double X(double time)=>left+(right-left)*(time-timing.Appearance)/Math.Max(.000001,timing.End-timing.Appearance);
            double Y(double value)=>bottom-(value-min)/(max-min)*(bottom-top);
            void Text(string value,double x,double y,Brush brush,bool alignRight=false)
            {
                var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                    new Typeface("Consolas"),11,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(text,new Point(alignRight ? x-text.Width : x,y));
            }
            Text(max.ToString("0.###"),left-7,top-6,yAxis,true); Text(min.ToString("0.###"),left-7,bottom-6,yAxis,true);
            for(var row=0;row<3;row++)
            {
                var y=top+(bottom-top)*row/2; context.DrawLine(new Pen(grid,.6),new Point(left,y),new Point(right,y));
            }
            context.DrawLine(new Pen(xAxis,1.2),new Point(left,bottom),new Point(right,bottom));
            context.DrawLine(new Pen(yAxis,1.2),new Point(left,top),new Point(left,bottom));
            foreach(var boundary in new[]{(plot.Spawn,"SPAWN",spawnBrush),(plot.Destroy,"DESTROY",destroyBrush)})
                if(boundary.Item1.HasValue)
                {
                    var y=Y(boundary.Item1.Value); context.DrawLine(new Pen(boundary.Item3,.9){DashStyle=DashStyles.Dash},new Point(left,y),new Point(right,y));
                    Text(boundary.Item2+" "+boundary.Item1.Value.ToString("0.###"),boundary.Item2=="SPAWN" ? left : right,27,boundary.Item3,boundary.Item2=="DESTROY");
                }
            for(var channel=0;channel<plot.Values.Length;channel++)
            {
                var geometry=new StreamGeometry();
                using(var path=geometry.Open())
                {
                    var values=plot.Values[channel]; path.BeginFigure(new Point(left,Y(values[0])),false,false);
                    for(var i=1;i<values.Length;i++) path.LineTo(new Point(left+(right-left)*i/(values.Length-1),Y(values[i])),true,false);
                }
                geometry.Freeze();
                if(plot.Inactive==null) context.DrawGeometry(null,new Pen(colors[channel],1.5),geometry);
                else
                {
                    var values=plot.Values[channel];
                    for(var i=1;i<values.Length;i++)
                        context.DrawLine(new Pen(plot.Inactive[i] ? foreground : colors[channel],plot.Inactive[i] ? .8 : 1.5),
                            new Point(left+(right-left)*(i-1)/(values.Length-1),Y(values[i-1])),
                            new Point(left+(right-left)*i/(values.Length-1),Y(values[i])));
                }
                Text(plot.Channels[channel],left+channel*62,8,colors[channel]);
            }
            void Marker(double time,string label,bool dash)
            {
                var pen=new Pen(foreground,.8) { DashStyle=dash ? DashStyles.Dash : DashStyles.Solid };
                var x=X(time); context.DrawLine(pen,new Point(x,top),new Point(x,bottom));
                if(label!="hit" || x>left+28 && x<right-28)
                    Text(label,label=="end" ? right : x+3,top-14,foreground,label=="end");
            }
            Marker(timing.Appearance,"in",false); Marker(timing.Judge,"hit",true); Marker(timing.End,"end",false);
            Text(timing.Appearance.ToString("0.0000"),left,163,xAxis);
            Text(timing.End.ToString("0.0000"),right,163,xAxis,true);
            Text("t (s)",right,184,xAxis,true);
        }
    }
}
