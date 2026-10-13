using System.Windows;
using System.Windows.Controls;
using System.IO;
using Un4seen.Bass;

namespace MajdataEdit;

public partial class BPMtap : Window
{
    private List<double> bpms = new();
    private DateTime lastTime = DateTime.MinValue;
    readonly Func<string?>? trackPath;
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 5, Margin = new Thickness(0,8,0,8) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
    readonly ListView tempos = new() { Height = 180 };
    AudioRhythmDetector.Result? detection;
    CancellationTokenSource? cancellation;

    public BPMtap() : this(null) { }
    public BPMtap(Func<string?>? getTrack)
    {
        InitializeComponent(); trackPath = getTrack;
        status.SetResourceReference(TextBlock.ForegroundProperty,"ButtonForeground");
        tempos.SetResourceReference(Control.ForegroundProperty,"ButtonForeground");
        tempos.SetResourceReference(Control.BackgroundProperty,"EditorBackground");
        progress.SetResourceReference(Control.ForegroundProperty,"HelperForeground");
        var view = new GridView { ColumnHeaderContainerStyle = (Style)FindResource("TempoHeaderStyle") };
        foreach(var column in new[] { ("开始", "Start", 60d), ("结束", "End", 60d), ("BPM", "Bpm", 70d), ("可信度", "Confidence", 60d) })
            view.Columns.Add(new GridViewColumn { Header=column.Item1, Width=column.Item3, DisplayMemberBinding=new System.Windows.Data.Binding(column.Item2) });
        tempos.View=view;
        var actions=new WrapPanel();
        actions.Children.Add(Action("自动检测 BPM", async () => await Detect()));
        actions.Children.Add(Action("取消", () => cancellation?.Cancel()));
        DetectionPanel.Children.Add(actions); DetectionPanel.Children.Add(progress);
        DetectionPanel.Children.Add(status); DetectionPanel.Children.Add(tempos);
        var correction=new WrapPanel();
        correction.Children.Add(Action("BPM × ½",()=>CorrectTempo(.5)));
        correction.Children.Add(Action("BPM × 2",()=>CorrectTempo(2)));
        correction.Children.Add(Action("复制 BPM 表", () => {
            if(detection!=null) Clipboard.SetText(TempoText(detection));
        }));
        DetectionPanel.Children.Add(correction);
        Closed += (_,_) => cancellation?.Cancel();
    }
    static Button Action(string text, System.Action action) {
        var button=new Button { Content=text, Padding=new Thickness(10,5,10,5), Margin=new Thickness(0,4,6,4), MinHeight=28 };
        button.SetResourceReference(Control.TemplateProperty,"DarkButton");
        button.SetResourceReference(Control.ForegroundProperty,"ButtonForeground");
        button.SetResourceReference(Control.BackgroundProperty,"ButtonsBackground");
        button.Click+=(_,_)=>action(); return button;
    }
    async Task Detect() {
        if(cancellation!=null) return;
        var path=trackPath?.Invoke();
        if(path==null||!File.Exists(path)) { MessageBox.Show("请先打开含 track.mp3 或 track.ogg 的谱面。","自动检测"); return; }
        cancellation=new CancellationTokenSource(); var cancel=cancellation.Token;
        var update=new Progress<double>(value=>progress.Value=value);
        try {
            status.Text="正在分析 BPM…"; progress.Value=0;
            var result=await Task.Run(()=> {
                var stream=Bass.BASS_StreamCreateFile(path,0,0,BASSFlag.BASS_STREAM_DECODE|BASSFlag.BASS_SAMPLE_FLOAT|BASSFlag.BASS_STREAM_PRESCAN);
                if(stream==0) throw new InvalidOperationException("音频解码失败："+Bass.BASS_ErrorGetCode());
                try {
                    var info=Bass.BASS_ChannelGetInfo(stream);
                    var length=Bass.BASS_ChannelGetLength(stream);
                    var buffer=new float[16384*info.chans]; var samples=new List<float>(); long read=0;
                    while(true) {
                        cancel.ThrowIfCancellationRequested();
                        var bytes=Bass.BASS_ChannelGetData(stream,buffer,buffer.Length*4);
                        if(bytes<=0) break;
                        for(var i=0;i<bytes/4;i+=info.chans) {
                            double sample=0; for(var c=0;c<info.chans;c++) sample+=buffer[i+c]; samples.Add((float)(sample/info.chans));
                        }
                        read+=bytes; ((IProgress<double>)update).Report(length>0?.15*Math.Min(1,read/(double)length):0);
                    }
                    return AudioRhythmDetector.Analyze(samples.ToArray(),info.freq,update,cancel);
                } finally { Bass.BASS_StreamFree(stream); }
            },cancel);
            detection=result;

            RefreshTempos();
            status.Text=$"检测完成 · {result.Tempos.Count} 个 BPM 段";
        } catch(OperationCanceledException) { status.Text="已取消检测"; }
          catch(Exception ex) { MessageBox.Show(ex.Message,"BPM 检测失败"); status.Text="检测失败"; }
        finally { cancellation?.Dispose(); cancellation=null; }
    }
    void RefreshTempos() {
        if(detection!=null) tempos.ItemsSource=detection.Tempos.Select(t=>new { Start=t.Start.ToString("0.00"), End=t.End.ToString("0.00"), Bpm=t.Bpm.ToString("0.00"), Confidence=t.Confidence.ToString("P0") });
    }
    void CorrectTempo(double factor) {
        if(detection==null) return;
        var selected=tempos.SelectedIndex;
        detection=detection with { Tempos=detection.Tempos.Select((t,i)=> selected<0||i==selected ? t with { Bpm=t.Bpm*factor } : t).ToArray() };
        RefreshTempos(); status.Text=selected<0?"已校正全部 BPM 段。":"已校正选中的 BPM 段。";
    }
    static string TempoText(AudioRhythmDetector.Result result) =>
        "开始秒\t结束秒\tBPM\t可信度\n"+string.Join("\n",result.Tempos.Select(t=>FormattableString.Invariant($"{t.Start:0.000}\t{t.End:0.000}\t{t.Bpm:0.###}\t{t.Confidence:0.000}")));
    private void Tap_Button_Click(object sender, RoutedEventArgs e)
    {
        if (lastTime != DateTime.MinValue)
        {
            var delta = (DateTime.Now - lastTime).TotalSeconds;
            bpms.Add(60d / delta);
        }

        lastTime = DateTime.Now;
        double sum = 0;
        if (bpms.Count <= 0) return;
        if (bpms.Count > 20) bpms.RemoveAt(0);
        foreach (var bpm in bpms) sum += bpm;
        var avg = sum / bpms.Count;

        Tap_Button.Content = string.Format("{0:N1}", avg);
    }

    private void Reset_Button_Click(object sender, RoutedEventArgs e)
    {
        bpms = new List<double>();
        lastTime = DateTime.MinValue;
        Tap_Button.Content = "Tap";
    }
}
