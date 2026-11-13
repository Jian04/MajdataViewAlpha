using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MajdataEdit;
using Newtonsoft.Json;

internal static class MediaAndInformationRegression
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    internal static void Run()
    {
        VerifyInformation();
        VerifyMedia();
    }
    private static void VerifyInformation()
    {
        var process=typeof(MainWindow).Assembly.GetType("MajdataEdit.SimaiProcess")!;
        var names=new[]{"title","artist","designer","wholeBpm","clockCount","other_commands"};
        var previous=names.Select(name=>process.GetField(name,All)!.GetValue(null)).ToArray();
        var oldDirectory=MainWindow.maidataDir;
        var root=Path.GetFullPath(".codex-build/song-info-rebind");
        Directory.CreateDirectory(root);
        var info=new Infomation(); var changes=0; info.InformationChanged+=()=>changes++;
        void SetSong(string prefix)
        {
            foreach(var name in names) process.GetField(name,All)!.SetValue(null,name is "wholeBpm" ? "125" : name is "clockCount" ? "4" : prefix+name);
        }
        void Load() => info.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,info));
        try
        {
            MainWindow.maidataDir=Path.Combine(root,"one"); SetSong("One"); Load();
            // A second Loaded event happens when a tool is rehosted. It must never write partial old fields.
            MainWindow.maidataDir=Path.Combine(root,"two"); SetSong("Two"); Load();
            Check((string)process.GetField("title",All)!.GetValue(null)! =="Twotitle" &&
                ((TextBox)info.FindName("ArtistTextbox")).Text=="Twoartist" && changes==0,"Rehosting must reload all metadata without committing old fields");
            ((TextBox)info.FindName("TitleTextbox")).Text="Edited two";
            Check((string)process.GetField("title",All)!.GetValue(null)! =="Edited two","Editing current song metadata still applies");
            MainWindow.maidataDir=Path.Combine(root,"three"); SetSong("Three");
            typeof(Infomation).GetMethod("FlushInformation",All)!.Invoke(info,null);
            ((TextBox)info.FindName("ArtistTextbox")).Text="Stale artist";
            Check(changes==0 && (string)process.GetField("title",All)!.GetValue(null)! =="Threetitle" &&
                (string)process.GetField("artist",All)!.GetValue(null)! =="Threeartist","Pending autosave and stale edits must not write into a different song");
            Load(); typeof(Infomation).GetMethod("ReloadChartInformation",All)!.Invoke(info,null);
            Check(changes==0 && ((TextBox)info.FindName("TitleTextbox")).Text=="Threetitle","Explicit chart switch refresh must be read-only");
            Console.WriteLine("SONG INFO PASS: repeated Loaded, chart reload, stale edits, pending autosave and current-song edits.");
        }
        finally
        {
            info.Close(); MainWindow.maidataDir=oldDirectory;
            for(var i=0;i<names.Length;i++) process.GetField(names[i],All)!.SetValue(null,previous[i]);
        }
    }
    private static void VerifyMedia()
    {
        var assembly=typeof(MainWindow).Assembly;
        var tools=assembly.GetType("MajdataEdit.MediaTools")!;
        var projectType=assembly.GetType("MajdataEdit.MediaTimelineProject")!;
        var root=Path.GetFullPath(".codex-build/media-on-demand-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var track=Path.Combine(root,"track.mp3"); File.WriteAllText(track,"No decoding is needed for an untouched timeline");
        var projectPath=Path.Combine(root,"media_timeline.json");
        void Project(string source,double volume) => File.WriteAllText(projectPath,JsonConvert.SerializeObject(new {
            Version=1,Clips=new[]{new {Track=1,TrackIndex=0,SourcePath=source,TimelineStart=0,SourceOffset=0,SourceDuration=1,Duration=1,Volume=volume}} }));
        Task<string?> Build() => (Task<string?>)tools.GetMethod("BuildTimelineAudioAsync",All)!.Invoke(null,new object[]{root})!;
        void Prune(string? active) => tools.GetMethod("PruneTimelineAudioCache",All)!.Invoke(null,new object?[]{root,active});
        Project("track.mp3",1);
        Check(Build().GetAwaiter().GetResult()==track && !Directory.Exists(Path.Combine(root,"media")),"Untouched track must bypass FFmpeg and create no media/cache folder");
        var project=projectType.GetMethod("LoadWorking",All)!.Invoke(null,new object[]{root})!;
        var clips=(IList)projectType.GetProperty("Clips")!.GetValue(project)!;
        clips[0]!.GetType().GetProperty("SourcePath")!.SetValue(clips[0],track);
        var main=(MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        typeof(MainWindow).GetField("songLength",All)!.SetValue(main,1d);
        typeof(MainWindow).GetField("waveformSourcePath",All)!.SetValue(main,track);
        ((Task)typeof(MainWindow).GetMethod("RebuildTimelineWaveformAsync",All)!.Invoke(main,new object[]{clips,1d})!).GetAwaiter().GetResult();
        Check((double)typeof(MainWindow).GetField("waveformDisplayLength",All)!.GetValue(main)! ==1d,"Untouched timeline must reuse loaded waveform without opening an audio decoder");
        var cache=Path.Combine(root,"media",".cache"); Directory.CreateDirectory(cache);
        var active=Path.Combine(cache,"timeline_audio_0000000000000000.wav");
        var stale=Path.Combine(cache,"timeline_audio_1111111111111111.wav");
        var pending=Path.Combine(cache,"timeline_audio_2222222222222222.wav.tmp.wav");
        File.WriteAllText(active,"active"); File.WriteAllText(stale,"stale"); File.WriteAllText(pending,"pending");
        Prune(active);
        Check(File.Exists(active) && !File.Exists(stale) && File.Exists(pending),"Cache cleanup must preserve active audio and in-progress renders");
        File.Delete(pending); Prune(track);
        Check(!Directory.Exists(Path.Combine(root,"media")),"Empty generated cache/media directories should be removed");
        var source=Path.GetFullPath(".codex-build/media-gap-test/a.wav");
        Project(source,.5);
        var oldPath=Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH",@"D:\Maimai\release\MajdataAlpha\App\MajdataView\MajdataView_Data\StreamingAssets;"+oldPath);
        try
        {
            var first=Build(); var second=Build();
            Check(ReferenceEquals(first,second),"Identical pending mixes must share a single FFmpeg task");
            var result=first.GetAwaiter().GetResult(); second.GetAwaiter().GetResult();
            Check(result!=track && File.Exists(result) && Directory.GetFiles(cache,"*.wav").Length==1,"Edited audio must produce one completed mix");
            Check(Build().GetAwaiter().GetResult()==result,"Unchanged edits must reuse the completed mix");
        }
        finally { Environment.SetEnvironmentVariable("PATH",oldPath); }
        Console.WriteLine("MEDIA CACHE PASS: no-edit passthrough, waveform reuse, active/temp retention, stale cleanup, concurrent-build reuse and real FFmpeg mix.");
    }
}
