using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataEdit;

internal static class Monitor103Regression
{
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static void Check(bool value,string message) {if(!value) throw new Exception(message);}
    internal static void Run()
    {
        var assembly=typeof(MainWindow).Assembly;
        var parser=assembly.GetType("MajdataEdit.SimaiProcess")!;
        var modelType=assembly.GetType("MajdataEdit.NoteMonitor")!;
        var previewType=assembly.GetType("MajdataEdit.NoteMonitorPreview")!;
        var preview=Activator.CreateInstance(previewType,true)!;
        IList Parse(string source)
        {
            parser.GetMethod("ClearData",All)!.Invoke(null,null);
            parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{source,0L});
            return (IList)parser.GetField("notelist",All)!.GetValue(null)!;
        }
        foreach(var source in new[]{"1m","2hm[4:1]","B1m","Chm[4:1]","3-5m[4:1]"})
        {
            var point=Parse("(120){4}"+source+",")[0]!;
            var entries=(IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{point})!;
            foreach(var entry in entries)
            {
                var image=(ImageSource)previewType.GetMethod("Create",All)!.Invoke(preview,new[]{entry,"",new EditorSetting()})!;
                var visual=new DrawingVisual(); using(var drawing=visual.RenderOpen()) drawing.DrawImage(image,new Rect(0,0,96,64));
                var bitmap=new RenderTargetBitmap(96,64,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
                var pixels=new byte[96*64*4]; bitmap.CopyPixels(pixels,96*4,0);
                Check(Enumerable.Range(0,96*64).Any(i=>pixels[i*4+3]>30),"Empty mine preview: "+source);
                var note=entry!.GetType().GetProperty("Note")!.GetValue(entry)!;
                var body=(bool)entry.GetType().GetProperty("Body")!.GetValue(entry)!;
                var mine=(bool)note.GetType().GetField(body ? "isMineSlide" : "isMineHead")!.GetValue(note)!;
                if(mine) Check(Enumerable.Range(0,96*64).All(i=>pixels[i*4]==pixels[i*4+1] && pixels[i*4+1]==pixels[i*4+2]),"Mine must use grayscale base skin: "+source);
            }
        }
        var imageCache=(IDictionary)previewType.GetField("images",All)!.GetValue(preview)!;
        var firstImages=imageCache.Values.Cast<object>().Select(item=>item.GetType().GetField("Item3")!.GetValue(item)).ToArray();
        var last=Parse("(120){4}3-5m[4:1],")[0]!;
        foreach(var entry in (IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{last})!)
            previewType.GetMethod("Create",All)!.Invoke(preview,new[]{entry,"",new EditorSetting()});
        Check(imageCache.Values.Cast<object>().Select(item=>item.GetType().GetField("Item3")!.GetValue(item)).SequenceEqual(firstImages),"Unchanged skins were decoded again");
        foreach(var source in new[]{"(120){8}1`2,","(120)\n@{16}1,\n{4}2,","(120)\n@*{32}1,\n2,\n*@\n{4}3,"})
        {
            var points=Parse(source);
            foreach(var point in points)
            {
                var type=point!.GetType(); var stream=(int)type.GetField("streamIndex")!.GetValue(point)!;
                var expected=stream==0 ? source.Contains('`') ? 8 : 4 : source.Contains("@*") ? 32 : 16;
                Check((int)type.GetField("subdivision")!.GetValue(point)! == expected,"Wrong stream subdivision");
                Check(!Newtonsoft.Json.JsonConvert.SerializeObject(point).Contains("subdivision"),"Monitor metadata changed preview JSON");
            }
        }
        var timing=Parse("(120){4}<SV*(1,2,1,linear)><SIZEV*(1,2,1,easeinout)>,,1,")[0]!;
        var model=Activator.CreateInstance(modelType,All,null,new object[]{7f,7.5f,0f},null)!;
        var selected=((IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{timing})!)[0]!;
        var sample=modelType.GetMethod("Sample",All)!; var clock=Stopwatch.StartNew();
        var result=sample.Invoke(model,new[]{selected}); var cold=clock.Elapsed.TotalMilliseconds;
        clock.Restart(); for(var i=0;i<1000;i++) Check(ReferenceEquals(result,sample.Invoke(model,new[]{selected})),"Duplicate curve sampling");
        Console.WriteLine($"103 MONITOR PASS: grayscale base skins, decoded-image reuse, stream subdivision, unchanged JSON; sample {cold:0.###}ms, 1000 cached reads {clock.Elapsed.TotalMilliseconds:0.###}ms.");
    }
}
