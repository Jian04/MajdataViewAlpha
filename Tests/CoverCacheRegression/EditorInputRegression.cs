using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MajdataEdit;

internal static class EditorInputRegression
{
    private sealed record CompletionSegment(int Offset,int Length) : ISegment
    {
        public int EndOffset => Offset+Length;
    }
    private const BindingFlags All = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }

    internal static void VerifySectionAndBadges()
    {
        var assembly = typeof(MainWindow).Assembly;
        var process = assembly.GetType("MajdataEdit.SimaiProcess")!;
        var chartType = assembly.GetType("MajdataEdit.Majson")!;
        var serialize = process.GetMethod("Serialize", All)!;
        var validate = process.GetMethod("ValidateAlphaCommands", All)!;
        var isAlpha = typeof(MainWindow).GetMethod("IsAlphaChart", All)!;
        var isDx = typeof(MainWindow).GetMethod("IsDxChart", All)!;
        foreach (var (notes, dx) in new[] { ("1?-5[8:1],2!-6[8:1],\n||nihao\n3$,4$$,", false), ("B2h[8:1],", true) })
        {
            serialize.Invoke(null, new object[] { "(120){4}" + notes, 0L });
            var timings = (IList)process.GetField("notelist", All)!.GetValue(null)!;
            foreach (var timing in timings)
            {
                var type = timing!.GetType();
                var parsed = (IList)type.GetMethod("getNotes", All)!.Invoke(timing, null)!;
                Check(parsed.Count > 0, "Expected ordinary notes: " + notes);
                type.GetField("noteList", All)!.SetValue(timing, parsed);
            }
            var chart = Activator.CreateInstance(chartType)!;
            chartType.GetField("timingList", All)!.SetValue(chart, timings);
            Check(!(bool)isAlpha.Invoke(null, new[] { chart })! && (bool)isDx.Invoke(null, new[] { chart })! == dx,
                "Incorrect badge: " + notes);
        }
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var source = string.Join(newline, "(120){4}", "@ff0000 ||nihao", "<SHAKE*(Instant,1,10,,0.2)><ALPHA*0.8><SV*2>1,",
                "@NULL || reset", "<COLOR*00BBFF><HS*1>2,");
            Check(!((IEnumerable)validate.Invoke(null, new object[] { source })!).Cast<object>().Any(),
                "Section tint with comment must not mark the next commands as invalid");
            serialize.Invoke(null, new object[] { source, 0L });
            var effects = (IList)process.GetField("effectTable", All)!.GetValue(null)!;
            Check(effects.Count == 1 && !(bool)effects[0]!.GetType().GetField("hasDirection", All)!.GetValue(effects[0])!,
                "SHAKE Instant with a blank optional direction must play without a fixed direction");
        }
        Console.WriteLine("108 EDIT PASS: SD/DX badges, inline section comments, adjacent Alpha commands, blank SHAKE direction.");
    }

    internal static void Run()
    {
        var assembly=typeof(MainWindow).Assembly;
        var editor=new TextEditor { FontSize=16 };
        var hints=assembly.GetType("MajdataEdit.Editor.AlphaCommandHints")!;
        var curveText = hints.GetMethod("GetCurveText", All)!;
        var curvePreview = hints.GetMethod("BuildCurvePreview", All)!;
        var curveDocument = new TextDocument("<SV*tap=(1,-1,8:1,linear),hold=(1,2,1,easein)>");
        var firstTuple = (string)curveText.Invoke(null, new object[] { curveDocument, 4, 23 })!;
        Check(firstTuple == "(1,-1,8:1,linear)", "Curve hint must retain the active target tuple");
        var preview = (System.Windows.Controls.StackPanel)curvePreview.Invoke(null, new object[] { "SV", firstTuple })!;
        Check(preview != null && preview.Children.Count == 4, "SV hint needs speed and displacement plots");
        var distance = ((System.Windows.Controls.Canvas)preview.Children[2]).Children.OfType<System.Windows.Shapes.Polyline>().Single();
        Check(Math.Abs(distance.Points[0].Y - distance.Points[^1].Y) < .001 && distance.Points[48].Y < distance.Points[0].Y,
            "A linear 1 to -1 speed curve must advance then return to zero displacement");
        Check(curvePreview.Invoke(null, new object[] { "SIZE", "((1,1),(2,.5),8:1,easeout)" }) != null, "SIZE pair curve preview");
        Check(curvePreview.Invoke(null, new object[] { "COLOR", "(FF0000,00BBFF,8:1,sineinout)" }) != null, "RGB curve preview");
        Check(curvePreview.Invoke(null, new object[] { "SV", "(1,2,8:1,lin)" }) == null, "Incomplete function must not draw a different curve");
        Console.WriteLine("CURVE 083 PASS: active tuple, RGB/XY, signed speed integral, partial function");
        hints.GetMethod("Attach",All)!.Invoke(null,new object[]{editor});
        var colorizer=(DocumentColorizingTransformer)Activator.CreateInstance(
            assembly.GetType("MajdataEdit.Editor.SimaiColorizer")!,All,null,new object[]{editor},null)!;
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        var host=new Window { Content=editor,Width=400,Height=250,Left=-10000,Top=-10000,
            ShowActivated=false,ShowInTaskbar=false };
        host.Show(); host.UpdateLayout();
        try
        {
            editor.Text = "<SV*tap=(1,2,8:1,linear)>1,";
            editor.CaretOffset = editor.Text.IndexOf("linear", StringComparison.Ordinal) + 3;
            hints.GetMethod("ShowOrUpdateInsight", All)!.Invoke(null, new object[] { editor.TextArea });
            var hint = (Window)hints.GetField("insightWindow", All)!.GetValue(null)!;
            var curveWindow = (Window)hints.GetField("curveWindow", All)!.GetValue(null)!;
            var distanceWindow = (Window)hints.GetField("distanceWindow", All)!.GetValue(null)!;
            hint.UpdateLayout(); curveWindow.UpdateLayout(); distanceWindow.UpdateLayout();
            var inner = (FrameworkElement)hint.Content;
            Console.WriteLine($"086 hint inset: {inner.TranslatePoint(new Point(), hint)}; Padding={hint.Padding}");
            hints.GetMethod("PositionCurveWindows", All)!.Invoke(null, new object[] { hint });
            Check(curveWindow.Owner == hint && distanceWindow.Owner == hint &&
                curveWindow.Left >= hint.Left + hint.ActualWidth && distanceWindow.Top > curveWindow.Top,
                "Function and displacement windows must attach to the right of the original hint");
            Check(!curveWindow.IsHitTestVisible, "Speed preview must be read-only");
            var provider = hints.GetField("insightProvider", All)!.GetValue(null)!;
            var content = (System.Windows.Controls.StackPanel)provider.GetType().GetProperty("CurrentContent")!.GetValue(provider)!;
            Check(!content.Children.OfType<System.Windows.Controls.Canvas>().Any(), "Original hint content must not contain graphs");
            hint.Close();
            Check(hints.GetField("curveWindow", All)!.GetValue(null) == null, "Attached plots must close with the hint");
            Console.WriteLine("CURVE WINDOWS 085 PASS: original hint, two attached windows, close lifetime");
            editor.CaretOffset = editor.Text.IndexOf('>');
            hints.GetMethod("ShowOrUpdateInsight", All)!.Invoke(null, new object[] { editor.TextArea });
            Check(hints.GetField("curveWindow", All)!.GetValue(null) != null && hints.GetField("distanceWindow", All)!.GetValue(null) != null,
                "Completing the closing parenthesis must not hide valid function graphs");
            ((Window)hints.GetField("insightWindow", All)!.GetValue(null)!).Close();
            Console.WriteLine("CURVE 086 PASS: complete tuple with caret after its closing parenthesis retains both plots");
            editor.Text = "<RIPPLE*(True,0.7)>1,";
            editor.CaretOffset = editor.Text.IndexOf('>');
            hints.GetMethod("ShowOrUpdateInsight", All)!.Invoke(null, new object[] { editor.TextArea });
            var filterWindow = (Window)hints.GetField("curveWindow", All)!.GetValue(null)!;
            var filterContent = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)filterWindow.Content).Child;
            Check(filterContent.Children.OfType<System.Windows.Controls.Image>().Single().Source != null,
                "Filter hint must show the packaged shader preview");
            ((Window)hints.GetField("insightWindow", All)!.GetValue(null)!).Close();
            Check(!filterWindow.IsVisible && hints.GetField("curveWindow", All)!.GetValue(null) == null,
                "Filter preview must close with the hint");
            var filterImage = (System.Windows.Controls.Image)hints.GetMethod("BuildFilterPreview", All)!
                .Invoke(null, new object[] { "RIPPLE" })!;
            var playbackHost = new Window { Content = filterImage, Owner = host, ShowActivated = false, ShowInTaskbar = false };
            var firstFrame = filterImage.Source;
            var wait = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) => { timer.Stop(); wait.Continue = false; };
            playbackHost.Show();
            timer.Start();
            Dispatcher.PushFrame(wait);
            Check(filterImage.Source != firstFrame, "Filter preview must advance while the hint is visible");
            playbackHost.Close();
            Console.WriteLine("FILTER PREVIEW PASS: actual packaged frames, animated, attached window lifetime.");
            editor.Text="";
            editor.TextArea.PerformTextInput("<"); editor.TextArea.PerformTextInput(">");
            Check(editor.Text=="<>","Typing <> must not accept the default SV candidate: "+editor.Text);
            var commands=(IEnumerable)hints.GetProperty("Commands",All)!.GetValue(null)!;
            var command=commands.Cast<object>().First(value=>(string)value.GetType().GetProperty("Name")!.GetValue(value)! == "SV");
            var completion=(ICompletionData)Activator.CreateInstance(hints.GetNestedType("AlphaCompletionData",All)!,
                All,null,new[]{command},null)!;
            foreach(var sample in new[]{("<>",1,0,"<SV*>"),("<S>",1,1,"<SV*>"),
                ("<*>",1,0,"<SV*>"),("<SV*2>",1,2,"<SV*2>")})
            {
                editor.Text=sample.Item1; editor.CaretOffset=sample.Item2+sample.Item3;
                completion.Complete(editor.TextArea,new CompletionSegment(sample.Item2,sample.Item3),EventArgs.Empty);
                Check(editor.Text==sample.Item4,"Completion must reuse its existing suffix: "+editor.Text);
            }
            var source="@*{4}1,1,1,1,1,1,\n||help\n{4}1,1,1,1,\n*";
            editor.Text=source; editor.CaretOffset=editor.Document.TextLength;
            editor.TextArea.TextView.EnsureVisualLines();
            var oldLine=editor.TextArea.TextView.VisualLines.First();
            editor.TextArea.PerformTextInput("@");
            host.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            Check(!ReferenceEquals(oldLine,editor.TextArea.TextView.VisualLines.First()),
                "Typing closing @ must invalidate the earlier opening line");
            var markers=(IEnumerable)colorizer.GetType().GetField("markerSpans",All)!.GetValue(colorizer)!;
            var spans=markers.Cast<object>().Select(span=>(
                (int)span.GetType().GetProperty("Start")!.GetValue(span)!,
                (int)span.GetType().GetProperty("Length")!.GetValue(span)!)).ToArray();
            Check(spans.Contains((0,2)) && spans.Contains((source.Length-1,2)),
                "Both @* and *@ must be highlighted after completing the last @");
            VerifyDurationCompletion(editor, hints, host);
            Console.WriteLine("EDITOR INPUT PASS: literal <>, suffix-aware completion, live multiline-stream marker highlighting");
            VerifyWrappedLineSpacing(editor, host);
        }
        finally { host.Close(); }
        VerifyWaveGrid(assembly);
    }

    private static void VerifyWrappedLineSpacing(TextEditor editor, Window host)
    {
        var source = string.Concat(Enumerable.Repeat("1,", 80)) + "\n2,";
        editor.Text = source;
        editor.Width = 180;
        editor.ScrollToHome();
        var view = editor.TextArea.TextView;
        var type = typeof(MainWindow).Assembly.GetType("MajdataEdit.Editor.WrappedLineSpacing")!;
        var createFont = type.GetMethod("CreateFont", All)!;
        var baseFont = new System.Windows.Media.FontFamily("Consolas");
        view.ElementGenerators.Add((VisualLineElementGenerator)Activator.CreateInstance(type, nonPublic: true)!);
        editor.FontFamily = (System.Windows.Media.FontFamily)createFont.Invoke(null, new object[] { baseFont })!;
        editor.WordWrap = false;
        host.UpdateLayout();
        var normal = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(2)).Height;
        editor.WordWrap = true;
        host.UpdateLayout();
        var spaced = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
        Check(spaced.TextLines.Count > 1, "Spacing probe did not wrap its first source line");
        var compact = spaced.TextLines[0].Height;
        var last = spaced.TextLines[^1].Height;
        var shortLine = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(2)).Height;
        Console.WriteLine($"110 WRAP metrics: normal={normal:F3}, compact={compact:F3}, source end={last:F3}, short={shortLine:F3}");
        Check(spaced.TextLines.Take(spaced.TextLines.Count - 1).All(line => Math.Abs(line.Height - normal * .75) < 1),
            "Continuation rows must use 0.75x line spacing");
        Check(Math.Abs(last - normal) < 1 && Math.Abs(shortLine - normal) < 1,
            "Source line endings must retain the normal line spacing");
        Check(editor.Text == source &&
            spaced.GetNextCaretPosition(spaced.VisualLength - 1, System.Windows.Documents.LogicalDirection.Forward,
                CaretPositioningMode.Normal, false) == -1,
            "The spacer must not change the source or add a caret step");
        editor.Text = "(120){4}1,2,\n|| 这里是短行注释\n\n3h[8:1],\n";
        editor.Width = 380;
        editor.FontSize = 15;
        var mismatches = new List<string>();
        foreach (var family in new[] { "Cascadia Mono, JetBrains Mono, Cascadia Code, Consolas", "Consolas", "Microsoft YaHei UI", "Global Monospace, Consolas" })
        {
            editor.FontFamily = (System.Windows.Media.FontFamily)createFont.Invoke(null,
                new object[] { new System.Windows.Media.FontFamily(family) })!;
            (double Height, double Baseline, double Top)[] Measure(bool wrap)
            {
                editor.WordWrap = wrap;
                host.UpdateLayout();
                return editor.Document.Lines.Select(line =>
                {
                    var visualLine = view.GetOrConstructVisualLine(line);
                    var textLine = visualLine.TextLines.Single();
                    return (textLine.Height, textLine.Baseline, visualLine.VisualTop);
                }).ToArray();
            }
            var off = Measure(false);
            var defaultHeight = view.DefaultLineHeight;
            var on = Measure(true);
            if (defaultHeight != view.DefaultLineHeight || !off.SequenceEqual(on) || !on.SequenceEqual(Measure(false)))
                mismatches.Add($"{family}: {string.Join(", ", off)} / {string.Join(", ", on)}");
        }
        Check(mismatches.Count == 0, "Unwrapped rows shifted after toggling wrap:\n" + string.Join("\n", mismatches));
        Console.WriteLine("110 WRAP PASS: identical unwrapped row positions in four fonts; 0.75x continuations; document and caret unchanged.");
    }

    private static void VerifyDurationCompletion(TextEditor editor, Type hints, Window host)
    {
        CompletionWindow? Window() => (CompletionWindow?)hints.GetField("completionWindow", All)!.GetValue(null);
        void Pump() => editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void KeyPress(Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host), 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.TextArea.RaiseEvent(args);
            Check(args.Handled, "completion key must be handled: " + key);
        }
        foreach (var (note, text, expected) in new[] { ("1h", "[8", "1h[8:1]"),
            ("1-5", "[16", "1-5[16:1]"), ("XD1", "[1:", "XD1[1:1]"),
            ("Ch", "[1:1", "Ch[1:1]") })
        {
            Window()?.Close(); editor.Text = note; editor.CaretOffset = note.Length; Pump();
            foreach (var ch in text) { editor.TextArea.PerformTextInput(ch.ToString()); Pump(); }
            Check(editor.Text == note + text, "typing a duration must not insert a candidate: " + editor.Text);
            Check(Window()?.CompletionList.SelectedItem?.Text == expected[note.Length..], "partial duration must stay matched: " + editor.Text);
            KeyPress(Key.Tab);
            Check(editor.Text == expected, "Tab must replace the typed duration prefix: " + editor.Text);
        }
        Window()?.Close(); editor.Text = "1h[8]"; editor.CaretOffset = 4; Pump();
        Check(Window() != null, "duration hints must reopen inside brackets");
        Window()!.CompletionList.RequestInsertion(EventArgs.Empty);
        Check(editor.Text == "1h[8:1]", "click completion must reuse the closing bracket: " + editor.Text);
        foreach (var source in new[] { "1h", "<" })
        {
            Window()?.Close(); editor.Text = source; editor.CaretOffset = source.Length; Pump();
            var list = Window()!.CompletionList.ListBox;
            Check(list.Items.Count > 1, "expected completion candidates: " + source);
            if (source == "1h")
            {
                var durations = list.Items.Cast<ICompletionData>().Select(data => data.Text).ToArray();
                Check(durations.SequenceEqual(new[] { "[8:1]", "[4:1]", "[2:1]", "[16:1]", "[32:1]", "[16:3]", "[1:1]", "[1:0]" }),
                    "duration candidates must retain the original order, with 32:1 directly above 16:3");
            }
            list.SelectedIndex = list.Items.Count - 1; KeyPress(Key.Down);
            Check(list.SelectedIndex == 0, "Down must wrap from last to first: " + source);
            KeyPress(Key.Up);
            Check(list.SelectedIndex == list.Items.Count - 1, "Up must wrap from first to last: " + source);
        }
        Window()?.Close(); editor.Text = "1h"; editor.CaretOffset = 2; Pump();
        editor.TextArea.PerformTextInput(","); Pump();
        Check(editor.Text == "1h," && Window() == null, "comma must close the popup without adding a duration");
        Console.WriteLine("COMPLETION 071 PASS: duration order, typed prefixes, Tab/click, closing brackets and command/duration list wrapping");
    }

    private static void VerifyWaveGrid(Assembly assembly)
    {
        var process=assembly.GetType("MajdataEdit.SimaiProcess")!;
        var serialize=process.GetMethod("Serialize",All)!;
        var grid=typeof(MainWindow).GetMethod("BuildWaveBeatLines",All)!;
        foreach(var chart in new[]{"(70)\n@{8},XD1[8:1]/XE7[8:1],XB6[8:1],",
            "(70)\n@*{8},XD1[8:1]/XE7[8:1],\nXB6[8:1],*@",
            "(70)\n@{4}1,XD1[8:1]/XE7[8:1],XB6[8:1],\n{8}1,"})
        {
            process.GetMethod("ClearData",All)!.Invoke(null,null);
            serialize.Invoke(null,new object[]{chart,0L});
            var args=new object[]{8d,null!,null!}; grid.Invoke(null,args);
            var strong=(List<double>)args[1]; var weak=(List<double>)args[2];
            Check(strong.Count==3 && Math.Abs(strong[1]-240d/70)<.000001,
                "Waveform measure lines must use BPM 70 even without a main stream");
            Check(Math.Abs(weak[0]-60d/70)<.000001,"Waveform beat lines must retain the main BPM");
        }
        process.GetMethod("ClearData",All)!.Invoke(null,null);
        Console.WriteLine("WAVE GRID PASS: BPM 70, single/multiline overlays, empty slots, main-stream coexistence");
    }
}
