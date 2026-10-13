using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using MajdataCore;
using MajdataEdit;

static class Program
{
    const BindingFlags Fields = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--long-line")) return LongLine();
        if (args.Length == 2 && args[0] == "--new-media") return NewMedia(args[1]);
        var assembly=typeof(MainWindow).Assembly;
        var parser=assembly.GetType("MajdataEdit.SimaiProcess")!;
        parser.GetMethod("ClearData",Fields)!.Invoke(null,null);
        parser.GetMethods(Fields).Single(m=>m.Name=="Serialize"&&m.GetParameters().Length==2)
            .Invoke(null,new object[]{"(120){4}1h[1:1]/3-7b[1:1]/Ch[1:1],,,,XD1[1:2],,,,E",(long)-1});
#pragma warning disable SYSLIB0050
        var main=(MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
#pragma warning restore SYSLIB0050
        var scheduleField=typeof(MainWindow).GetField("waitToBePlayed",Fields)!;
        var schedule=(IList)Activator.CreateInstance(scheduleField.FieldType)!;
        scheduleField.SetValue(main,schedule);
        var points=(IEnumerable)parser.GetField("notelist",Fields)!.GetValue(null)!;
        var first=points.Cast<object>().First();
        typeof(MainWindow).GetMethod("ScheduleSustainSoundsAfter",Fields)!.Invoke(main,new[]{first,0,(object)1d});
        var entries=schedule.Cast<object>().ToArray();
        var atTwo=entries.Single(x=>Math.Abs((double)x.GetType().GetField("time",Fields)!.GetValue(x)!-2)<.001);
        var sounds=((IEnumerable)atTwo.GetType().GetProperty("NormalSounds")!.GetValue(atTwo)!).Cast<object>().Select(x=>x.ToString()).ToArray();
        Require(sounds.Contains("Answer")&&sounds.Contains("Judge"),"Hold tail disappeared after seek");
        Require(entries.Any(x=>((IEnumerable)x.GetType().GetProperty("NormalSounds")!.GetValue(x)!).Cast<object>().Any(s=>s.ToString()=="JudgeBreakSlide")),"Break Slide tail disappeared after seek");
        Require((bool)atTwo.GetType().GetField("hasTouchHoldEnd",Fields)!.GetValue(atTwo)!,"TouchHold tail disappeared after seek");
        Require(entries.All(x=>(double)x.GetType().GetField("time",Fields)!.GetValue(x)!>=1),"Seeking replays past head sounds");
        var end=(double)typeof(MainWindow).GetMethod("GetAllPerfectStartTime",Fields)!.Invoke(main,null)!;
        Require(Math.Abs(end-6)<.001,"Noise is omitted from final-note completion time");
        using(var output=new MemoryStream()) {
            var frame=new byte[48]; var writer=new RecordingFrameWriter(output,48,2);
            using(writer) for(int i=0;i<300;i++){ Array.Fill(frame,(byte)(i%251)); writer.Enqueue(frame); }
            Require(writer.Failure==null&&writer.WrittenFrames==300,"Bounded writer lost frames");
            var raw=output.ToArray();
            Require(raw.Length==300*48&&Enumerable.Range(0,300).All(i=>raw.AsSpan(i*48,48).ToArray().All(v=>v==(byte)(i%251))),"Capture buffer reused before encoding / out of order");
        }
        if(args.Length>0) EncoderTest(args[0]);
        Console.WriteLine("VIEW EDIT FIX PASS: ordinary Hold/TouchHold and Break Slide tail after seek, no past head replay, final Noise completion, 300 bounded FIFO frames, deterministic 60/120 fps encoder.");
        return 0;
    }
    static int LongLine()
    {
        var app=new System.Windows.Application { ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown };
        var assembly=typeof(MainWindow).Assembly;
        var serialize=assembly.GetType("MajdataEdit.SimaiProcess")!.GetMethods(Fields).Single(m=>m.Name=="Serialize"&&m.GetParameters().Length==2);
        foreach(var count in new[]{1000,2000,4000}) {
            var text="(205){8}"+string.Concat(Enumerable.Repeat("1h[16:1],",count))+"E";
            var watch=Stopwatch.StartNew();
            serialize.Invoke(null,new object[]{text,(long)text.Length});
            Console.WriteLine($"LONG LINE parse {count}: {watch.Elapsed.TotalMilliseconds:F1} ms");
            var editor=new ICSharpCode.AvalonEdit.TextEditor { WordWrap=true, Text=text, FontSize=16 };
            editor.TextArea.TextView.LineTransformers.Add((ICSharpCode.AvalonEdit.Rendering.IVisualLineTransformer)Activator.CreateInstance(assembly.GetType("MajdataEdit.Editor.SimaiColorizer")!,true)!);
            using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Long line regression") { Width=650, Height=600, PositionX=-32000, PositionY=-32000, WindowStyle=unchecked((int)0x80000000) });
            source.RootVisual=editor;
            editor.ApplyTemplate();
            editor.TextArea.ApplyTemplate();
            watch.Restart();
            editor.Measure(new System.Windows.Size(650,600));
            editor.Arrange(new System.Windows.Rect(0,0,650,600));
            editor.TextArea.TextView.EnsureVisualLines();
            Require(editor.TextArea.TextView.VisualLines.Count>0,"Performance test must lay out real text lines");
            var line = editor.TextArea.TextView.VisualLines[0];
            void ColorAt(int offset, string brushName) {
                var column = line.GetVisualColumn(offset);
                var element = line.Elements.Single(x => x.VisualColumn <= column && x.VisualColumn + x.VisualLength > column);
                var expected = (System.Windows.Media.SolidColorBrush)assembly.GetType("MajdataEdit.Editor.SimaiColorizer")!.GetProperty(brushName, Fields)!.GetValue(null)!;
                Require(element.TextRunProperties.ForegroundBrush is System.Windows.Media.SolidColorBrush actual && actual.Color == expected.Color, "Long-line coloring changed for " + brushName);
            }
            ColorAt(1,"BpmBrush"); ColorAt(6,"BeatBrush"); ColorAt(11,"DurationBrush"); ColorAt(16,"DimBrush");
            Console.WriteLine($"LONG LINE layout {count}: {watch.Elapsed.TotalMilliseconds:F1} ms");
        }
        return 0;
    }
    static int NewMedia(string ffmpeg)
    {
        Environment.SetEnvironmentVariable("PATH",Path.GetDirectoryName(Path.GetFullPath(ffmpeg))+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"));
        var root=Path.GetFullPath(".codex-build/new-media-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source=Path.Combine(root,"input.mp4");
        using(var process=Process.Start(new ProcessStartInfo(ffmpeg,$"-nostdin -y -f lavfi -i color=c=blue:s=64x64:d=1 -f lavfi -i sine=frequency=440:sample_rate=48000:duration=1 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{source}\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true})!) {
            var error=process.StandardError.ReadToEnd(); process.WaitForExit(); Require(process.ExitCode==0,error);
        }
        var method=typeof(MainWindow).Assembly.GetType("MajdataEdit.MediaChartCreation")!.GetMethod("CreateAsync",Fields)!;
        var folder=Path.Combine(root,"video chart");
        ((Task)method.Invoke(null,new object[]{source,folder,true,"&title=Test\n&first=0\n"})!).GetAwaiter().GetResult();
        Require(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"pv.mp4"))),"PV changed while copying");
        Require(new FileInfo(Path.Combine(folder,"track.mp3")).Length>1000 && File.Exists(Path.Combine(folder,"maidata.txt")),"Video audio or chart missing");
        using(var probe=Process.Start(new ProcessStartInfo(ffmpeg,$"-nostdin -i \"{Path.Combine(folder,"track.mp3")}\" -f null -") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true})!) {
            var info=probe.StandardError.ReadToEnd(); probe.WaitForExit();
            Require(probe.ExitCode==0 && info.Contains("Audio: mp3, 44100 Hz"),"Video MP3 must be resampled from 48000 to 44100 Hz: "+info);
        }
        Require(!Directory.EnumerateDirectories(folder).Any(),"Temporary import directory leaked");
        var before=File.ReadAllBytes(Path.Combine(folder,"track.mp3"));
        try { ((Task)method.Invoke(null,new object[]{source,folder,true,"replace"})!).GetAwaiter().GetResult(); throw new Exception("Existing chart overwritten"); }
        catch(IOException) { }
        Require(before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"track.mp3"))),"Existing audio modified");
        var audioFolder=Path.Combine(root,"audio chart");
        ((Task)method.Invoke(null,new object[]{Path.Combine(folder,"track.mp3"),audioFolder,false,"&title=Audio\n"})!).GetAwaiter().GetResult();
        Require(before.SequenceEqual(File.ReadAllBytes(Path.Combine(audioFolder,"track.mp3"))),"MP3 import unnecessarily transcoded audio");
        var renamedVideo=Path.Combine(root,"rename video"); Directory.CreateDirectory(renamedVideo);
        var selectedVideo=Path.Combine(renamedVideo,"Original title.mp4"); File.Copy(source,selectedVideo);
        ((Task)method.Invoke(null,new object[]{selectedVideo,renamedVideo,true,"&title=Rename video\n"})!).GetAwaiter().GetResult();
        Require(!File.Exists(selectedVideo) && File.ReadAllBytes(Path.Combine(renamedVideo,"pv.mp4")).SequenceEqual(File.ReadAllBytes(source)),"Selected video must become pv.mp4 after successful conversion");
        var directVideo=Path.Combine(root,"direct video"); Directory.CreateDirectory(directVideo);
        var directPv=Path.Combine(directVideo,"pv.mp4"); File.Copy(source,directPv);
        ((Task)method.Invoke(null,new object[]{directPv,directVideo,true,"&title=Direct video\n"})!).GetAwaiter().GetResult();
        Require(File.ReadAllBytes(directPv).SequenceEqual(File.ReadAllBytes(source))&&File.Exists(Path.Combine(directVideo,"track.mp3")),"Existing selected pv.mp4 must be reused in its own folder");
        var directAudio=Path.Combine(root,"direct audio"); Directory.CreateDirectory(directAudio);
        var selectedAudio=Path.Combine(directAudio,"song.mp3"); File.WriteAllBytes(selectedAudio,before);
        ((Task)method.Invoke(null,new object[]{selectedAudio,directAudio,false,"&title=Direct audio\n"})!).GetAwaiter().GetResult();
        Require(!File.Exists(selectedAudio)&&before.SequenceEqual(File.ReadAllBytes(Path.Combine(directAudio,"track.mp3")))&&File.Exists(Path.Combine(directAudio,"maidata.txt")),"Selected MP3 must become track.mp3 beside maidata.txt without transcoding");
        Require(Directory.GetFiles(directAudio).Select(Path.GetFileName).Contains("track.mp3", StringComparer.Ordinal),
            "Audio basename must be track, with one lowercase .mp3 extension");
        var failedFolder=Path.Combine(root,"invalid chart");
        try { ((Task)method.Invoke(null,new object[]{Path.Combine(root,"missing.mp4"),failedFolder,true,"bad"})!).GetAwaiter().GetResult(); throw new Exception("Invalid video succeeded"); }
        catch(InvalidOperationException) { }
        Require(!Directory.EnumerateFileSystemEntries(failedFolder).Any(),"Failed import left partial chart/media");
        Console.WriteLine("NEW MEDIA PASS: video copied, 44100 Hz MP3 extracted, chart created, audio preserved, existing chart protected, failed conversion cleaned.");
        return 0;
    }
    static void EncoderTest(string ffmpeg)
    {
        foreach(var fps in new[]{60,120}) {
            var path=Path.Combine(AppContext.BaseDirectory,$"fps-{fps}.mp4");
            using(var process=Process.Start(new ProcessStartInfo(ffmpeg,$"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgb24 -s 16x16 -r {fps} -i pipe:0 -frames:v {fps*2} -c:v libx264 -pix_fmt yuv420p \"{path}\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true})!) {
                var writer=new RecordingFrameWriter(process.StandardInput.BaseStream,16*16*3);
                using(process.StandardInput.BaseStream) using(writer)
                    for(int i=0;i<fps*2;i++) { var frame=new byte[16*16*3]; Array.Fill(frame,(byte)(i%251)); writer.Enqueue(frame); }
                Require(writer.Failure==null&&writer.WrittenFrames==fps*2,"Encoder lost frame");
                Require(process.WaitForExit(15000)&&process.ExitCode==0,"FFmpeg stdin did not finalize");
            }
            using var decoded=Process.Start(new ProcessStartInfo(ffmpeg,$"-hide_banner -loglevel error -i \"{path}\" -f rawvideo -pix_fmt rgb24 pipe:1") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})!;
            using var bytes=new MemoryStream(); decoded.StandardOutput.BaseStream.CopyTo(bytes); decoded.WaitForExit();
            Require(decoded.ExitCode==0&&bytes.Length==(long)fps*2*16*16*3,"Decoded video frame count differs from selected rate");
            Console.WriteLine($"ENCODER PASS: {fps} fps, {fps*2} decoded frames / 2 seconds.");
        }
    }
}
