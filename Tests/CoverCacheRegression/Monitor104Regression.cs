using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataCore;
using MajdataEdit;

internal static class Monitor104Regression
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static void Check(bool value,string message) {if(!value) throw new Exception(message);}
    internal static void Run()
    {
        var assembly=typeof(MainWindow).Assembly;
        var parser=assembly.GetType("MajdataEdit.SimaiProcess")!;
        var modelType=assembly.GetType("MajdataEdit.NoteMonitor")!;
        var previewType=assembly.GetType("MajdataEdit.NoteMonitorPreview")!;
        var preview=Activator.CreateInstance(previewType,true)!;
        object Parse(string source)
        {
            parser.GetMethod("ClearData",All)!.Invoke(null,null);
            parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{"(120){4}"+source+",",0L});
            return ((IList)parser.GetField("notelist",All)!.GetValue(null)!)[0]!;
        }
        var sheet=new DrawingVisual();
        using(var drawing=sheet.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(38,42,49)),null,new Rect(0,0,384,128));
            var index=0;
            foreach(var source in new[]{"B1","B1/B2","B1b","B1m","Ch[4:1]","Chb[4:1]","Chm[4:1]"})
            {
                var timing=Parse(source);
                var entry=((IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{timing})!)[0]!;
                var image=(DrawingImage)previewType.GetMethod("Create",All)!.Invoke(preview,new[]{entry,"",new EditorSetting()})!;
                // Each View leaf must occupy its own quadrant, rather than four images sharing the center.
                var framed=((DrawingGroup)image.Drawing).Children.OfType<DrawingGroup>().Single();
                var leaves=(DrawingGroup)((DrawingGroup)framed.Children[0]).Children[0];
                var centers=leaves.Children.Take(4).Select(leaf=>new Point(leaf.Bounds.Left+leaf.Bounds.Width/2,leaf.Bounds.Top+leaf.Bounds.Height/2)).ToArray();
                Check(centers.Length==4 && centers.All(p=>Math.Sqrt(p.X*p.X+p.Y*p.Y)>.4),"Touch leaves still overlap at their center: "+source);
                Check(centers.Select(p=>(Math.Round(p.X,3),Math.Round(p.Y,3))).Distinct().Count()==4,"Touch leaves were not separated: "+source);
                drawing.DrawImage(image,new Rect(index%4*96,index/4*64,96,64)); index++;
            }
        }
        var bitmap=new RenderTargetBitmap(384,128,96,96,PixelFormats.Pbgra32); bitmap.Render(sheet);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create("alpha/.codex-build/104-touch-previews.png")) encoder.Save(file);

        var main=new MainWindow {editorSetting=new EditorSetting()};
        var selected=Parse("<SV*2>1");
        typeof(MainWindow).GetField("wavePaintHeight",All)!.SetValue(main,200);
        typeof(MainWindow).GetField("ghostCusorPositionTime",All)!.SetValue(main,0f);
        var viewportType=typeof(MainWindow).GetNestedType("WaveViewport",All)!;
        var viewport=Activator.CreateInstance(viewportType,All,null,new object[]{-1d,2d,300d},null)!;
        var draw=typeof(MainWindow).GetMethod("DrawScrollSpawnMarker",All)!;
        using(var wave=new System.Drawing.Bitmap(900,200))
        using(var graphics=System.Drawing.Graphics.FromImage(wave))
        {
            int Colored()=>Enumerable.Range(0,wave.Width).Sum(x=>Enumerable.Range(0,wave.Height).Count(y=>wave.GetPixel(x,y).A!=0));
            draw.Invoke(main,new[]{graphics,selected,viewport});
            Check(Colored()==0,"Closed monitor drew a spawn marker or displacement curve");
            var monitor=(Window)Activator.CreateInstance(assembly.GetType("MajdataEdit.NoteMonitorWindow")!,true)!;
            typeof(MainWindow).GetField("noteMonitor",All)!.SetValue(main,monitor);
            draw.Invoke(main,new[]{graphics,selected,viewport});
            Check(Colored()>50,"Open monitor did not draw its marker and displacement curve");
            typeof(MainWindow).GetField("noteMonitor",All)!.SetValue(main,null); monitor.Close(); graphics.Clear(System.Drawing.Color.Transparent);
            draw.Invoke(main,new[]{graphics,selected,viewport}); Check(Colored()==0,"Closing monitor left its annotations visible");
        }

        var install=Path.GetFullPath("alpha/.codex-build/104-layout-probe");
        var fonts=Path.Combine(install,"App","MajdataEdit","Fonts");
        foreach(var app in new[]{"MajdataEdit","MajdataView"})
            Check(FontFiles.DirectoryForApplication(Path.Combine(install,"App",app))==fonts,app+" still loads root Fonts");
        Check(FontFiles.DirectoryForApplication(install)==Path.Combine(install,"Fonts"),"Source/development font layout changed");
        Check(FontFiles.Resolve("Fonts/probe.ttf",fonts)==Path.Combine(fonts,"probe.ttf"),"Relative Fonts syntax changed");
        Check(FontFiles.Resolve(Path.Combine(AlphaSession.InstallRoot,"Fonts","probe.ttf"),fonts)==Path.Combine(fonts,"probe.ttf"),"Saved legacy absolute font reference broke");
        Check(AlphaSession.Root==Path.Combine(AlphaSession.EditorDataRoot,"Cache","Sessions"),"Session directory is outside Edit");
        var loadCopy=FontFiles.CreateLoadCopy(Path.Combine(AppContext.BaseDirectory,"Fonts","Allerta-Regular.ttf"));
        Check(loadCopy.StartsWith(Path.Combine(AlphaSession.EditorDataRoot,"Cache","Fonts")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Font renderer copy is outside Edit");
        Console.WriteLine("104 PASS: separated Touch/TouchHold petals; monitor-only spawn marker and motion curve; shared Edit font/cache paths and saved path compatibility. Contact sheet rendered.");
    }
}
