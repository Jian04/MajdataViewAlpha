using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using MajdataCore;
using MajdataEdit;

internal static class Monitor101Regression
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    internal static void Run()
    {
        foreach(var family in new[]{"sine","expo","circ","back","elastic","bounce"})
        {
            var input=new NoteValueTransition {easing=family+"in",duration=1};
            var output=new NoteValueTransition {easing=family+"out",duration=1};
            var both=new NoteValueTransition {easing=family+"inout",duration=1};
            foreach(var function in new[]{input,output,both})
                Check(AlphaCommandGrammar.TryValidate($"HS*(1,2,8:1,{function.easing})",120,out _),function.easing+" missing");
            for(var step=0;step<=100;step++)
            {
                var t=step/100d;
                Check(Math.Abs(input.Progress(t)+output.Progress(1-t)-1)<.00001,family+" in/out reflection");
                Check(Math.Abs(both.Progress(t)+both.Progress(1-t)-1)<.00001,family+" in/out symmetry");
            }
        }
        Check(!AlphaCommandGrammar.TryValidate("SV*(1,2,1,sigmoid)",120,out _),"Sigmoid was not removed");
        Check(AlphaCommandExamples.TransitionHeader("SV").Contains(",函数[") &&
            AlphaCommandExamples.TransitionHeader("SV","en").Contains(",function[") &&
            !AlphaCommandExamples.TransitionHeader("SV").Contains("elastic"),"Function header expanded prematurely");
        var assembly=typeof(MainWindow).Assembly;
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture=CultureInfo.GetCultureInfo("zh-CN");
        var explanation=assembly.GetTypes().Select(type=>type.GetMethod("TransitionFunctionsExplanation",All)).First(method=>method!=null)!;
        var lines=((string)explanation.Invoke(null,null)!).Split('\n');
        Check(lines.Skip(1).Select(line=>line.Split(" — ")[0]).SequenceEqual(NoteValueTransition.Functions),"Function explanation must have one ordered line per function");
        Console.WriteLine($"101 EASING PASS: {NoteValueTransition.Functions.Length} functions; complete six-family directions, reflection/symmetry, compact header, ordered explanation, sigmoid rejected.");

        // Exercise RefreshNoteMonitor itself: the former isolated window test
        // supplied notelist directly and never covered the empty waveform table.
        var main=new MainWindow {editorSetting=new EditorSetting()};
        var editor=(TextEditor)main.FindName("FumenContent");
        var adapterType=assembly.GetType("MajdataEdit.Editor.FumenEditorAdapter")!;
        var adapter=typeof(MainWindow).GetField("fumenEditor",All)!.GetValue(main)!;
        var monitorType=assembly.GetType("MajdataEdit.NoteMonitorWindow")!;
        var monitor=(Window)Activator.CreateInstance(monitorType,true)!;
        typeof(MainWindow).GetField("noteMonitor",All)!.SetValue(main,monitor);
        var parser=assembly.GetType("MajdataEdit.SimaiProcess")!;
        var refresh=typeof(MainWindow).GetMethod("RefreshNoteMonitor",All)!;
        var sections=(StackPanel)monitorType.GetField("sections",All)!.GetValue(monitor)!;
        void Load(string source,int caret)
        {
            typeof(MainWindow).GetField("isLoading",All)!.SetValue(main,true);
            editor.Text=source;
            var normalised=(string)adapterType.GetProperty("Text")!.GetValue(adapter)!;
            parser.GetMethod("Serialize",All)!.Invoke(null,new object[]{normalised,0L});
            adapterType.GetProperty("CaretOffset")!.SetValue(adapter,caret);
            typeof(MainWindow).GetField("isLoading",All)!.SetValue(main,false);
            refresh.Invoke(main,null);
        }
        const string source="(120){4}1/2-5[8:1],,\r\n3,";
        Load(source,source.IndexOf("1/",StringComparison.Ordinal));
        Check(sections.Children.Count==3 && sections.Children.OfType<Border>().Count()==3,"Caret slot must display Tap, star head and slide together");
        Check(!Descendants(monitor.Content as DependencyObject).Any(control=>control is ComboBox or Button),"Monitor still requires a selection bar");
        editor.Select(0,source.IndexOf(',')+1); refresh.Invoke(main,null);
        Check(sections.Children.OfType<Border>().Count()==3,"Selecting notes including their comma must retain their slot");
        editor.Select(0,0); adapterType.GetProperty("CaretOffset")!.SetValue(adapter,source.IndexOf(',')+1); refresh.Invoke(main,null);
        Check(sections.Children.Count==1 && sections.Children[0] is TextBlock,"Empty slot must clear the previous notes");
        adapterType.GetProperty("CaretOffset")!.SetValue(adapter,source.Replace("\r\n","\n").LastIndexOf('3')); refresh.Invoke(main,null);
        Check(sections.Children.OfType<Border>().Count()==1,"CRLF caret mapping selected the wrong slot");
        Load("(120){4}1`2/3-5[8:1],",8);
        Check(sections.Children.OfType<Border>().Count()==4,"Pseudo-each groups sharing a slot were omitted");
        Load("(120)\n@{4}1/2-5[8:1],\n{4}3,",10);
        Check(sections.Children.OfType<Border>().Count()==3,"Independent stream slot was not monitored");
        var modelType=assembly.GetType("MajdataEdit.NoteMonitor")!;
        const string snapshot="(120){4}<SV*(1,2,1,linear)><COLORV*(FF8800,00BBFF,2,linear)><HS*2>,,5hx[4:1],";
        Load(snapshot,snapshot.LastIndexOf('5'));
        var timings=(IList)parser.GetField("notelist",All)!.GetValue(null)!;
        var entry=((IList)modelType.GetMethod("Entries",All)!.Invoke(null,new[]{timings[0]})!)[0]!;
        var model=Activator.CreateInstance(modelType,All,null,new object[]{7f,7.5f,0f},null)!;
        var sample=modelType.GetMethod("Sample",All)!.Invoke(model,new[]{entry})!;
        var plots=(IList)sample.GetType().GetProperty("Plots")!.GetValue(sample)!;
        Check(plots.Cast<object>().Any(plot=>(string)plot.GetType().GetProperty("Name")!.GetValue(plot)! == "染色 RGB"),"COLORV curve missing");
        var root=(FrameworkElement)monitor.Content; root.Measure(new Size(400,1100)); root.Arrange(new Rect(0,0,400,1100)); root.UpdateLayout();
        var labels=Descendants(monitor.Content as DependencyObject).OfType<TextBlock>().Select(text=>text.Text).ToArray();
        Check(!labels.Any(text=>text.Contains("结束按自动播放") || text.Contains("仅画指令")),"Unrequested explanatory text remains");
        Check(labels.Any(text=>text.Contains("h · 0.5s")),"Hold duration/modifiers missing under heading: "+string.Join(" | ",labels));
        typeof(MainWindow).GetField("wavePaintHeight",All)!.SetValue(main,200);
        typeof(MainWindow).GetMethod("UpdateWaveMotion",All)!.Invoke(main,new[]{timings[0]});
        Check(ReferenceEquals(typeof(MainWindow).GetField("noteMonitorModel",All)!.GetValue(main),monitorType.GetField("monitor",All)!.GetValue(monitor)),"Monitor and waveform must share the same sampler");
        var motion=(IList)typeof(MainWindow).GetField("waveMotion",All)!.GetValue(main)!;
        Check(motion.Count==1,"Selected Hold must have one waveform position curve");
        var cached=motion[0]!.GetType().GetField("Item2")!.GetValue(motion[0]);
        typeof(MainWindow).GetMethod("UpdateWaveMotion",All)!.Invoke(main,new[]{timings[0]});
        Check(ReferenceEquals(cached,motion[0]!.GetType().GetField("Item2")!.GetValue(motion[0])),"Unchanged waveform redraw rebuilt numerical curves");
        var viewportType=typeof(MainWindow).GetNestedType("WaveViewport",All)!;
        var viewport=Activator.CreateInstance(viewportType,All,null,new object[]{0d,2d,360d},null)!;
        using(var bitmap=new System.Drawing.Bitmap(720,200))
        {
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(38,42,49));
                typeof(MainWindow).GetMethod("DrawWaveMotion",All)!.Invoke(main,new[]{graphics,timings[0],viewport});
            }
            bitmap.Save("alpha/.codex-build/103-wave-motion.png");
        }
        var image=new RenderTargetBitmap(400,1100,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual(); using(var drawing=background.RenderOpen()) drawing.DrawRectangle(monitor.Background,null,new Rect(0,0,400,1100));
        image.Render(background); image.Render(root);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using(var file=File.Create("alpha/.codex-build/103-monitor.png")) encoder.Save(file);
        monitor.Close();
        Console.WriteLine("101 MONITOR PASS: actual editor refresh, caret/selection/comma boundary, CRLF, pseudo-each, independent stream, ordered sections without selection controls.");
        Console.WriteLine("102 MONITOR PASS: non-V attributes, V curves, modifier duration, no explanatory footer; waveform displacement rendering and cache reuse.");

        var panel=new EditorSettingPanel();
        typeof(EditorSettingPanel).GetMethod("LoadPetChoices",All)!.Invoke(panel,null);
        var combo=(ComboBox)panel.FindName("PetComboBox");
        var ids=combo.Items.Cast<object>().Select(choice=>(string)choice.GetType().GetProperty("Id")!.GetValue(choice)!).ToArray();
        Check(ids.SequenceEqual(ids.OrderBy(id=>id,StringComparer.Ordinal)),"Pet ID order depends on the UI culture");
        Console.WriteLine("101 PET PASS: "+string.Join(", ",ids));
        typeof(EditorSettingPanel).GetField("settingsApplied",All)!.SetValue(panel,true); panel.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject? value)
    {
        if(value==null) yield break;
        yield return value;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(value,i))) yield return child;
    }
}
