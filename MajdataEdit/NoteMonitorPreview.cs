using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MajdataCore;

namespace MajdataEdit;

internal sealed class NoteMonitorPreview
{
    private readonly Dictionary<(string Path,bool Mine),(long Stamp,long Length,BitmapSource Image)> images = new();
    private string chartFolder="";
    internal ImageSource Create(NoteMonitor.Entry entry,string folder,EditorSetting settings)
    {
        if(!chartFolder.Equals(folder,StringComparison.OrdinalIgnoreCase)) { images.Clear(); chartFolder=folder; }
        var note=entry.Note;
        var skin=entry.Type switch {"tap"=>settings.TapSkin,"hold"=>settings.HoldSkin,
            "star"=>settings.StarSkin,"slide"=>settings.SlideSkin,_=>settings.Skin};
        var root=SkinPaths.Folder(SkinPaths.Roots(AppContext.BaseDirectory),skin,Path.Combine(AppContext.BaseDirectory,"Skin",skin));
        var mine=entry.Body && !note.isTrajectoryOnly ? note.isMineSlide : note.isMineHead;
        var suffix=mine ? ""
            : (entry.Body && !note.isTrajectoryOnly ? note.isSlideBreak : note.isBreak) ? "_break"
            : (entry.Timing.isEachInStream ?? entry.Timing.isEach) ? "_each" : entry.Type=="star" && settings.PinkStar ? "_pink" : "";
        BitmapSource Load(string path)
        {
            path=Path.GetFullPath(path); var info=new FileInfo(path);
            if(images.TryGetValue((path,mine),out var cached) && cached.Stamp==info.LastWriteTimeUtc.Ticks && cached.Length==info.Length)
                return cached.Image;
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
            image.UriSource=new Uri(path); image.EndInit(); image.Freeze();
            BitmapSource result=image;
            if(mine)
            {
                var bgra=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
                var stride=bgra.PixelWidth*4; var pixels=new byte[stride*bgra.PixelHeight]; bgra.CopyPixels(pixels,stride,0);
                for(var i=0;i<pixels.Length;i+=4)
                    pixels[i]=pixels[i+1]=pixels[i+2]=(byte)Math.Round(pixels[i+2]*.299+pixels[i+1]*.587+pixels[i]*.114);
                result=BitmapSource.Create(bgra.PixelWidth,bgra.PixelHeight,bgra.DpiX,bgra.DpiY,PixelFormats.Bgra32,null,pixels,stride);
                result.Freeze();
            }
            images[(path,mine)]=(info.LastWriteTimeUtc.Ticks,info.Length,result); return result;
        }
        var drawing=new DrawingGroup();
        using(var context=drawing.Open())
        {
            context.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,96,64));
            if(entry.Type=="blockarea")
            {
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(160,240,55,75)),new Pen(Brushes.IndianRed,2),new Rect(27,12,42,40),4,4);
            }
            else if(!string.IsNullOrEmpty(note.noteSkin) && (entry.Type!="slide" || note.isTrajectoryOnly))
                context.DrawImage(Load(Path.Combine(folder,note.noteSkin)),new Rect(24,8,48,48));
            else if(entry.Type=="slide")
            {
                var image=Load(Path.Combine(root,"slide"+suffix+".png"));
                for(var x=8;x<86;x+=14) context.DrawImage(image,new Rect(x,22,22,20));
            }
            else if(entry.Type is "touch" or "touchhold")
            {
                var petals=new DrawingGroup();
                using(var leaves=petals.Open())
                {
                    for(var petal=0;petal<4;petal++)
                    {
                        var touchHold=entry.Type=="touchhold";
                        var file=touchHold ? "touchhold_"+(suffix=="_break" ? "break_" : "")+petal+".png" : "touch"+suffix+".png";
                        var image=Load(Path.Combine(root,file));
                        var angle=(touchHold ? 45 : 0)+petal*90;
                        // Same leaf layout as View, slightly closed for the monitor thumbnail.
                        var radians=angle*Math.PI/180;
                        leaves.PushTransform(new TranslateTransform(.526*Math.Sin(radians),-.526*Math.Cos(radians)));
                        leaves.PushTransform(new RotateTransform(petal*90-(touchHold ? 135 : 180)));
                        leaves.DrawImage(image,new Rect(-image.PixelWidth/200d,-image.PixelHeight/200d,image.PixelWidth/100d,image.PixelHeight/100d));
                        leaves.Pop(); leaves.Pop();
                    }
                    var pointSuffix=entry.Type=="touchhold" && suffix!="_break" ? "" : suffix;
                    var point=Load(Path.Combine(root,"touch_point"+pointSuffix+".png"));
                    leaves.DrawImage(point,new Rect(-point.PixelWidth/200d,-point.PixelHeight/200d,point.PixelWidth/100d,point.PixelHeight/100d));
                }
                var bounds=petals.Bounds; var scale=Math.Min(56/bounds.Width,56/bounds.Height);
                context.PushTransform(new TranslateTransform(48-(bounds.Left+bounds.Width/2)*scale,32-(bounds.Top+bounds.Height/2)*scale));
                context.PushTransform(new ScaleTransform(scale,scale));
                context.DrawDrawing(petals); context.Pop(); context.Pop();
            }
            else
            {
                var file=entry.Type+suffix+".png";
                var rect=entry.Type=="hold" ? new Rect(34,4,28,56) : new Rect(23,7,50,50);
                context.DrawImage(Load(Path.Combine(root,file)),rect);
                if(note.isEx) context.DrawImage(Load(Path.Combine(root,entry.Type+"_ex.png")),rect);
            }
        }
        var source=new DrawingImage(drawing); source.Freeze(); return source;
    }
    internal static string Traits(NoteMonitor.Entry entry)
    {
        var note=entry.Note; var traits=new List<string>();
        if(entry.Type is "hold" or "touchhold") traits.Add($"h · {note.holdTime:0.####}s");
        if(entry.Body && !note.isTrajectoryOnly ? note.isSlideBreak : note.isBreak) traits.Add("b");
        if(note.isHanabi) traits.Add("f"); if(note.isEx) traits.Add("x");
        if(entry.Body ? note.isMineSlide : note.isMineHead) traits.Add("mine");
        if(entry.Timing.isEachInStream ?? entry.Timing.isEach) traits.Add("each");
        return traits.Count==0 ? "普通" : string.Join(" · ",traits);
    }
}
