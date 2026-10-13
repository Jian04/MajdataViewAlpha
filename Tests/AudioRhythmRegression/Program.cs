using MajdataEdit;
const int rate=22050;
float[] Clicks(int seconds, Func<double,double> bpm)
{
    var data=new float[seconds*rate];
    for(double time=.5; time < seconds-.1; time+=60/bpm(time))
        for(var i=0;i<rate*.04;i++) {
            var index=(int)(time*rate)+i;
            if(index<data.Length) data[index]+=(float)(Math.Sin(i*2*Math.PI*1700/rate)*Math.Exp(-i/(rate*.007)));
        }
    return data;
}
foreach(var bpm in new[]{90,120,180}) {
    var detected=AudioRhythmDetector.Analyze(Clicks(20,_=>bpm),rate,null,CancellationToken.None);
    var average=detected.Tempos.Where(t=>t.Start>=4&&t.End<=18).Select(t=>t.Bpm).DefaultIfEmpty(detected.Tempos.MaxBy(t=>t.End-t.Start)!.Bpm).Average();
    Console.WriteLine($"{bpm} => {average:F2}; onsets {detected.Onsets[0].Length}");
    if(Math.Abs(average-bpm)>2) throw new Exception("Fixed BPM mismatch");
    if(detected.Onsets[0].Length<20) throw new Exception("Onsets missing");
}
var variable=AudioRhythmDetector.Analyze(Clicks(40,t=>t<20?120:180),rate,null,CancellationToken.None);
if(!variable.Tempos.Any(t=>t.Start<12&&Math.Abs(t.Bpm-120)<2)||!variable.Tempos.Any(t=>t.End>28&&Math.Abs(t.Bpm-180)<2)) throw new Exception("Variable tempo missed");
Console.WriteLine("AUDIO RHYTHM PASS: 90/120/180 BPM, two-tempo song and onset extraction");
