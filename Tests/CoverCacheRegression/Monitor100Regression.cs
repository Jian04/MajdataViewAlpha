using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataCore;
using MajdataEdit;

internal static class Monitor100Regression
{
    private const BindingFlags All = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static object Get(object value,string name)=>value.GetType().GetProperty(name,All)!.GetValue(value)!;
    private static void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
    private static void Near(double a,double b,string label,double error=.0001)=>Check(Math.Abs(a-b)<error,$"{label}: {a} != {b}");
    internal static void Run()
    {
        foreach(var name in NoteValueTransition.Functions)
        {
            Check(AlphaCommandGrammar.TryValidate($"SV*(1,2,8:1,{name})",120,out var error),error);
            var curve=new NoteValueTransition {from=1,to=2,duration=1,easing=name,parameter=2};
            Near(curve.Evaluate(0),1,name+" start"); Near(curve.Evaluate(1),2,name+" end");
            var previous=1f;
            for(var i=0;i<=100;i++) { var value=curve.Evaluate(i/100d); Check(float.IsFinite(value),name+" finite");
                if(!name.StartsWith("back") && !name.StartsWith("elastic") && !name.StartsWith("bounce")) Check(value>=previous-.00001,name+" monotonic"); previous=value; }
        }
        foreach(var name in new[]{"smoothstep","smootherstep","sineinout","expoinout"})
            Check(!AlphaCommandGrammar.TryValidate($"HS*(1,2,1,{name},3)",120,out _),"Fixed easing must reject fifth parameter");
        var assembly=typeof(MainWindow).Assembly;
        var parser=assembly.GetType("MajdataEdit.SimaiProcess")!;
        foreach(var text in new[]{"1~[1P93K5[8:1]]","1P93K5[8:1]","1~[1P03K5[8:1]]","1~[1P95[8:1]]"})
        {
            parser.GetMethod("ClearData",All)!.Invoke(null,null);
            parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{"(120){4}"+text+",",0L});
            var parsed=parser.GetField("notelist",All)!.GetValue(null)!;
            Check(((IList)parsed).Count==1,"Example did not produce its note: "+text);
            var errors=(IList)typeof(MainWindow).GetMethod("ValidateTimingsForView",All)!.Invoke(null,new[]{parsed})!;
            Check(errors.Count==0,"Inherited path failed validation: "+text+" "+string.Join(" / ",errors.Cast<object>()));
        }
        Console.WriteLine("100 PATH PASS: inherited/ordinary SlideCode P9/P0 and Alpha P9 all accepted.");
        var modelType=assembly.GetType("MajdataEdit.NoteMonitor")!;
        object ParseSlot(string text)
        {
            parser.GetMethod("ClearData",All)!.Invoke(null,null);
            parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{text,0L});
            return ((IList)parser.GetField("notelist",All)!.GetValue(null)!)[0]!;
        }
        var ordinary=ParseSlot("(120){4}1/2-5[8:1],");
        var ordinaryEntries=(IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{ordinary})!;
        Check(ordinaryEntries.Count==3,"1/2-5 must list Tap, head and path separately");
        var ordinaryModel=Activator.CreateInstance(modelType,All,null,new object[]{7f,7.5f,0f},null)!;
        var ordinaryResult=modelType.GetMethod("Sample",All)!.Invoke(ordinaryModel,new[]{ordinaryEntries[0]})!;
        Check(((IList)Get(ordinaryResult,"Plots")).Count==0,"Unmodified attributes must have no curves");
        var hiddenCounts=new List<int>();
        foreach(var mode in new[]{"Rewind","Once"})
        {
            var testSlot=ParseSlot("(120){4}<SPAWN*2><DESTROY*5><SPAWNMODE*"+mode+"><SV*1>,,<SV*-1>,<SV*1>,1,");
            var testEntries=(IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{testSlot})!;
            var testModel=Activator.CreateInstance(modelType,All,null,new object[]{7f,7.5f,0f},null)!;
            var testResult=modelType.GetMethod("Sample",All)!.Invoke(testModel,new[]{testEntries[0]})!;
            var testPlots=(IList)Get(testResult,"Plots");
            var mask=(bool[])Get(testPlots[0]!,"Inactive"); hiddenCounts.Add(mask.Count(value=>value));
        }
        Check(hiddenCounts[0]>hiddenCounts[1],"Rewind must grey out the recrossing section; Once must retain it");
        Console.WriteLine($"100 BOUNDARY PASS: 3 components, unchanged curves hidden, Rewind/Once grey samples {string.Join('/',hiddenCounts)}.");
        parser.GetMethod("ClearData",All)!.Invoke(null,null);
        parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{
            "(120){4}<SV*2><HS*tap=2><SPAWN*2><DESTROY*5><SIZE*tap=(1,2,1,linear)>"+
            "<COLOR*tap=(FF0000,0000FF,1,linear)><ALPHA*tap=(1,0,1,linear)>1/B1/3h[4:1]/4-6[4:1]/XD2[8:1],",0L});
        var points=(IList)parser.GetField("notelist",All)!.GetValue(null)!; var slot=points[0]!;
        var model=Activator.CreateInstance(modelType,All,null,new object[]{7f,7.5f,0f},null)!;
        var entries=(IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{slot})!;
        Check(entries.Count==6,"Tap, Touch, Hold, star head, slide path and noise must be individually selectable");
        var tap=modelType.GetMethod("Sample",All)!.Invoke(model,new[]{entries[0]})!;
        var plots=(IList)Get(tap,"Plots"); var position=(double[][])Get(plots[0]!,"Values");
        var velocity=(double[][])Get(plots[1]!,"Values");
        Check(!plots.Cast<object>().Any(plot=>Get(plot,"Name").ToString() is "大小倍率" or "染色 RGB" or "透明度倍率"),
            "Non-V properties must be attributes, including their birth-time functions");
        var attributes=(IList)Get(tap,"Properties");
        Check(attributes.Cast<object>().Any(row=>(string)row.GetType().GetField("Item1")!.GetValue(row)! == "COLOR"),"Static color is missing from attributes");
        Near(position[0][0],2-AlphaVisualTiming.SpawnScaleDistance,"Ring birth radius");
        Near(position[0][^1],5,"DESTROY radius");
        Near(velocity[0][90],AlphaVisualTiming.ToViewNoteSpeed(7)*4,"Effective SV × HS speed",.001);
        Near((double)Get(tap,"Appearance"),-(3+AlphaVisualTiming.SpawnScaleDistance)/(AlphaVisualTiming.ToViewNoteSpeed(7)*4),"Birth time");
        for(var i=0;i<entries.Count;i++)
        {
            var result=modelType.GetMethod("Sample",All)!.Invoke(model,new[]{entries[i]})!;
            foreach(var plot in (IList)Get(result,"Plots"))
                Check(((double[][])Get(plot!,"Values")).All(row=>row.All(double.IsFinite)),"Monitor contains invalid values");
        }
        Check(ReferenceEquals(points,parser.GetField("notelist",All)!.GetValue(null)),"Monitoring must not reparse or replace the chart");
        var windowType=assembly.GetType("MajdataEdit.NoteMonitorWindow")!;
        var window=(Window)Activator.CreateInstance(windowType,true)!;
        var slotArray=Array.CreateInstance(slot.GetType(),1); slotArray.SetValue(slot,0);
        windowType.GetMethod("UpdateSlot",All)!.Invoke(window,new object[]{slotArray,model});
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(400,760)); root.Arrange(new Rect(0,0,400,760)); root.UpdateLayout();
        var image=new RenderTargetBitmap(400,760,96,96,PixelFormats.Pbgra32); image.Render(root);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using(var file=File.Create("alpha/.codex-build/100-note-monitor.png")) encoder.Save(file);
        window.Close();
        var combo=new ComboBox {ItemsSource=new[]{"240×240","320×320","512×512"},SelectedIndex=1};
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120) {RoutedEvent=UIElement.PreviewMouseWheelEvent};
        combo.RaiseEvent(wheel); Check(wheel.Handled && combo.SelectedIndex==1,"Closed resolution picker changed on wheel");
        Console.WriteLine($"100 MONITOR PASS: {NoteValueTransition.Functions.Length} easing functions, typed speed/radius/birth time, 6 note components, finite property graphs, no chart mutation, wheel selection blocked; WPF render saved.");
    }
}
