using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MajdataEdit;
using MajdataSessionUi;
using MajdataCollaboration;
using Newtonsoft.Json.Linq;

internal static class Program
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread]
    static int Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = new System.Globalization.CultureInfo("zh-CN");
        var assembly = typeof(MainWindow).Assembly;
        if (args.Contains("--editor-tools-only"))
        {
            EditorToolsRegression.Run();
            return 0;
        }
        var hints = assembly.GetType("MajdataEdit.AlphaCommandHints") ?? assembly.GetType("MajdataEdit.Editor.AlphaCommandHints")!;
        var block = (TextBlock)hints.GetMethod("BuildSignatureBlock", Private)!.Invoke(null,
            new object[] { "<SHATTER*(True,强度[,过渡时间])> / <SHATTER*(False[,过渡时间])> | <SHATTER*(Instant,强度,持续时间)>", 0 })!;
        Check(block.TextAlignment == TextAlignment.Left && block.Margin.Left == 0 && block.Inlines.OfType<LineBreak>().Count() == 2,
            "hint alignment or separate syntax forms");
        Check(block.Inlines.OfType<Run>().Where(run => run.Text.Contains("过渡时间")).All(run => run.FontStyle == FontStyles.Italic),
            "every omitted transition must be italic");
        Check(block.Inlines.OfType<Run>().Single(run => run.Text == "持续时间").FontStyle != FontStyles.Italic,
            "Instant duration must remain required");
        var commands = (Array)hints.GetProperty("Commands", Private)!.GetValue(null)!;
        var shatter = commands.Cast<object>().Single(c => c.GetType().GetProperty("Name")!.GetValue(c)!.ToString() == "SHATTER");
        var panel = (StackPanel)hints.GetMethod("BuildHintPanel", Private)!.Invoke(null, new[] { shatter })!;
        var common = (TextBlock)panel.Children[0];
        var details = (StackPanel)panel.Children[1];
        Check(panel.Children.Count == 2 && common.Inlines.OfType<LineBreak>().Count() == 0 &&
            common.FontWeight == FontWeights.Bold && new TextRange(common.ContentStart, common.ContentEnd).Text.Contains("True|False|Instant") &&
            ((TextBlock)details.Children[0]).Text.Length > 0 && details.Children[1] is TextBlock special &&
            special.FontWeight == FontWeights.Normal && special.Inlines.OfType<LineBreak>().Count() >= 1 &&
            !new TextRange(special.ContentStart,special.ContentEnd).Text.Contains("强度"),
            "completion must show one bold blue common header, white explanation, then separate thin blue special forms");
        var providerType = hints.GetNestedType("AlphaOverloadProvider", Private)!;
        var provider = Activator.CreateInstance(providerType, Private, null, new object?[] { shatter, 2, true, false }, null)!;
        var header = (StackPanel)providerType.GetProperty("CurrentHeader")!.GetValue(provider)!;
        Check(((TextBlock)header.Children[0]).Inlines.OfType<Run>().Single(run => run.Text == "强度").FontWeight == FontWeights.Bold,
            "current argument must remain highlighted in the blue signature");
        string Explanation() => ((TextBlock)((StackPanel)providerType.GetProperty("CurrentContent")!.GetValue(provider)!).Children[0]).Text;
        var strength = Explanation();
        providerType.GetMethod("Update")!.Invoke(provider, new object?[] { 3, true, false });
        var transition = Explanation();
        providerType.GetMethod("Update")!.Invoke(provider, new object?[] { 3, null, true });
        var sustain = Explanation();
        Check(strength.Contains("强度") && transition.Contains("过渡时间") && sustain.Contains("持续时间") && !sustain.Contains("必填") &&
            strength != transition && transition != sustain, "white parameter explanations must track the current argument and use consistent Chinese time names");
        foreach(var command in commands.Cast<object>()) {
            var name=command.GetType().GetProperty("Name")!.GetValue(command)!.ToString()!;
            var hint=(StackPanel)hints.GetMethod("BuildHintPanel", Private)!.Invoke(null,new[]{command})!;
            var content=(StackPanel)hint.Children[1];
            Check(content.Children.Count==2 && content.Children[1] is TextBlock, "Missing examples: "+name);
            var exampleBlock=(TextBlock)content.Children[1];
            foreach(var example in new TextRange(exampleBlock.ContentStart,exampleBlock.ContentEnd).Text.Trim().Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries))
                Check(MajdataCore.AlphaCommandGrammar.TryValidate(example.Trim().Trim('<','>'),120,out _), "Invalid real WPF example: "+example);
            if(name=="JLINE") Check(new TextRange(((TextBlock)hint.Children[0]).ContentStart,((TextBlock)hint.Children[0]).ContentEnd).Text.Contains("过渡时间"),"JLINE header lost transition");
            if(name=="TEXT") Check(!((TextBlock)content.Children[0]).Text.Contains("TEXT*"), "TEXT example remains in white introduction");
            if(name is "JLINE" or "TEXT" or "SHATTER" or "SV") SaveHint(hint,name);
        }
        var tab=hints.GetMethod("TryHandleAlphaArgumentTab",Private)!;
        var input=new ICSharpCode.AvalonEdit.TextEditor();
        void CheckTabs(string initial,string[] expected) {
            input.Text=initial; input.CaretOffset=initial.IndexOf(")>",StringComparison.Ordinal);
            foreach(var value in expected) {
                Check((bool)tab.Invoke(null,new object[]{input.TextArea})!,"Tab was not handled: "+input.Text);
                Check(input.Text.Contains(value),"Wrong same-argument Tab cycle: "+input.Text+" expected "+value);
                Check(input.Text.Count(c=>c==',')==initial.Count(c=>c==','),"Tab moved to the next parameter instead of cycling");
            }
        }
        CheckTabs("<SHATTER*()>",new[]{"*(True)","*(False)","*(Instant)","*(True)"});
        CheckTabs("<SHATTER*(f)>",new[]{"*(False)","*(Instant)","*(True)"});
        CheckTabs("<SHATTER*(Fa)>",new[]{"*(False)"});
        CheckTabs("<SHATTER*(i)>",new[]{"*(Instant)"});
        CheckTabs("<JUDGELINEEXPAND*(t)>",new[]{"*(True)","*(False)"});
        CheckTabs("<JLINE*(00BBFF,)>",new[]{",8:1)",",16:1)",",2:1)"});
        CheckTabs("<JUDGELINEEXPAND*()>",new[]{"*(True)","*(False)","*(True)"});
        input.Text="<SV*blockarea=2>";input.CaretOffset=input.Text.Length-1;
        Check((bool)tab.Invoke(null,new object[]{input.TextArea})! && input.Text.StartsWith("<SV*blockarea="),"Typed Tab destroys its target key");
        var style = (Style)app.FindResource("AlphaContextMenu");
        var menu = new ContextMenu { Style = style };
        var item = new MenuItem { Header = "联机写谱", Style = (Style)app.FindResource("AlphaPopupMenuItem") };
        menu.Items.Add(item);
        menu.ApplyTemplate(); item.ApplyTemplate();
        Check(menu.Width == 208 && item.Padding == new Thickness(4, 6, 4, 6) && item.Template != null,
            "pet/editor context menu style and icon gutter");
        var help = new AlphaSyntaxHelp();
        help.GetType().GetMethod("BuildDocument", Private)!.Invoke(help, new object[] { MainWindow.GetLocalizedString("AlphaHelpStructuredText") });
        var viewer = (FlowDocumentScrollViewer)help.FindName("HelpViewer");
        Check(viewer.Document.Blocks.All(b => b.TextAlignment == TextAlignment.Left), "every help card and blue heading must align left");
        var formatted = (string[])assembly.GetType("MajdataEdit.AlphaGuideFormatter")!.GetMethod("Format", Private)!
            .Invoke(null, new object[] { MainWindow.GetLocalizedString("AlphaHelpStructuredText"), "zh" })!;
        Check(!formatted.Any(line => line.Contains("旧写法") || line.Contains("参数位置：") ||
            ((line.StartsWith("S ") || line.StartsWith("E ")) && line.Contains(" / "))), "legacy/order commentary or combined example rows");
        Check(formatted.Any(line => line == "### 继承语法 — Touch 距离") && formatted.Any(line => line == "### 继承语法 — 图片换肤"),
            "Touch distance and image replacement must be documented as inheritance");
        help.Close();

        string local = "local chart";
        var room = new ChartRoomWindow(() => local, text => local = text, _ => "") { CloseConfirmation = () => false };
        var dummy = new RoomClient("127.0.0.1:1", "");
        typeof(ChartRoomWindow).GetField("client", Private)!.SetValue(room, dummy);
        var closed = false; room.Closed += (_, _) => closed = true;
        room.Close();
        Check(!closed && room.Connected && local == "local chart", "No must keep sidebar page and connection");
        int prompts = 0;
        room.CloseConfirmation = () => { prompts++; return true; };
        Check(room.PrepareClose(), "detached close approval");
        room.Close();
        Check(closed && !room.Connected && prompts == 1, "detached page must close with one confirmation and disconnect");
        var notifications = new ChartRoomWindow(() => local, text => local = text, _ => "");
        typeof(ChartRoomWindow).GetField("client", Private)!.SetValue(notifications, new RoomClient("127.0.0.1:1", ""));
        var updateMembers = typeof(ChartRoomWindow).GetMethod("UpdateMembers", Private)!;
        updateMembers.Invoke(notifications, new object[] { new RoomReply("room", 0, local, false, 2,
            new[] { new RoomPresence("member", "成员测试", 4, 0) }) });
        updateMembers.Invoke(notifications, new object[] { new RoomReply("room", 0, local, false, 1, Array.Empty<RoomPresence>()) });
        var notificationStatus = (TextBlock)typeof(ChartRoomWindow).GetField("status", Private)!.GetValue(notifications)!;
        Check(notificationStatus.Text.Contains("成员测试 已离开房间") && notificationStatus.Text.Contains("1 人在线"),
            "member departure must appear even without TXT changes");
        typeof(ChartRoomWindow).GetMethod("MarkConnectionLost", Private)!.Invoke(notifications, null);
        Check(!notifications.Connected && notificationStatus.Text.Contains("连接已中断"), "transport failure must not continue claiming connected");
        int hostClosurePrompts = 0, genericDeparturePrompts = 0;
        notifications.HostRoomClosed = () => hostClosurePrompts++;
        notifications.GuestDisconnected = () => genericDeparturePrompts++;
        typeof(ChartRoomWindow).GetMethod("HandleRoomClosed", Private)!.Invoke(notifications,
            new object[] { new RoomReply("room", 0, "", false, 0, Closed: true) });
        Check(!notifications.Connected && notificationStatus.Text.Contains("房主已关闭房间") && local == "local chart" &&
            hostClosurePrompts == 1 && genericDeparturePrompts == 0,
            "host closure must clear connected state and retain the local draft");
        notifications.Close();

        var releases = JArray.Parse("[{\"tag_name\":\"v0.5.3\"},{\"tag_name\":\"v9.0.0\",\"draft\":true},{\"tag_name\":\"alpha.0.6.1-beta\",\"prerelease\":true},{\"tag_name\":\"unknown\"}]").OfType<JObject>();
        var latest = assembly.GetType("MajdataEdit.AlphaReleaseVersion")!.GetMethod("Latest", Private)!.Invoke(null, new object[] { releases })!;
        var version = (Version)latest.GetType().GetField("Item2")!.GetValue(latest)!;
        Check(version == new Version(0, 6, 1), "highest public version must include alpha tags and exclude drafts");

        var parser = assembly.GetType("MajdataEdit.SimaiProcess")!;
        parser.GetMethod("ClearData", Private)!.Invoke(null, null);
        parser.GetMethods(Private).Single(m => m.Name == "Serialize" && m.GetParameters().Length == 2)
            .Invoke(null, new object[] { "(120){4}1,2,3,4,E", (long)-1 });
        var notes = parser.GetField("notelist", Private)!.GetValue(null);
        parser.GetMethod("ClearDocumentData", Private)!.Invoke(null, null);
        parser.GetMethod("ReadText", Private)!.Invoke(null, new object[] { "&title=Updated\n&inote_6=(120){4}8,7,E\n" });
        Check(ReferenceEquals(notes, parser.GetField("notelist", Private)!.GetValue(null)), "other difficulty synchronization must retain the local note graph");
        if (args.Length > 0) CheckEditorApplication(assembly, parser, args[0]);
        Console.WriteLine("PAGE PASS: real WPF menu resources, all-left help, optional hint styling, close No/Yes, alpha release versions and preserved preview notes.");
        return 0;
    }
    static void SaveHint(FrameworkElement hint,string name)
    {
        var host=new Border { Child=hint, Width=510, Padding=new Thickness(16), Background=System.Windows.Media.Brushes.Black };
        host.Measure(new Size(510,double.PositiveInfinity)); host.Arrange(new Rect(host.DesiredSize)); host.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(510,(int)Math.Ceiling(host.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var folder=System.IO.Path.GetFullPath(".codex-build/HintPreview");System.IO.Directory.CreateDirectory(folder);
        using var file=System.IO.File.Create(System.IO.Path.Combine(folder,name+".png"));encoder.Save(file);
    }
    static void CheckEditorApplication(Assembly assembly, Type parser, string nativeBass)
    {
        System.Runtime.InteropServices.NativeLibrary.Load(nativeBass);
        var main = new MainWindow { editorSetting = new EditorSetting { DragPreview = true, ShowSongDetail = false } };
        var field = typeof(MainWindow).GetField("isLoading", Private)!; field.SetValue(main, true);
        parser.GetMethod("ClearData", Private)!.Invoke(null, null);
        parser.GetMethod("ReadText", Private)!.Invoke(null, new object[] {
            "&title=Playback Probe\n&inote_5=(120){4}1,2,3,4,E\n&inote_6=(120){4}7,E\n" });
        typeof(MainWindow).GetField("selectedDifficulty", Private)!.SetValue(main, 4);
        ((ComboBox)main.FindName("LevelSelector")).SelectedIndex = 4;
        typeof(MainWindow).GetMethod("SetRawFumenText", Private)!.Invoke(main, new object[] { "(120){4}1,2,3,4,E" });
        field.SetValue(main, false);
        parser.GetMethods(Private).Single(m => m.Name == "Serialize" && m.GetParameters().Length == 2)
            .Invoke(null, new object[] { "(120){4}1,2,3,4,E", (long)-1 });
        typeof(MainWindow).GetMethod("generateSoundEffectList", Private)!.Invoke(main, new object[] { 0d, false });
        var scheduleField = typeof(MainWindow).GetField("waitToBePlayed", Private)!;
        var schedule = scheduleField.GetValue(main);
        var scheduledTimes = ((System.Collections.IEnumerable)schedule!).Cast<object>()
            .Select(sound => sound.GetType().GetField("time", Private)!.GetValue(sound)).ToArray();
        Check(scheduledTimes.Length > 0, "playback probe must contain scheduled sounds");
        var endField = typeof(MainWindow).GetField("extraTime4AllPerfect", Private)!;
        var playbackEnd = endField.GetValue(main);
        var isPlaying = typeof(MainWindow).GetField("isPlaying", Private)!;
        var transport = typeof(MainWindow).GetField("lastEditorState", Private)!;
        transport.SetValue(main, Enum.Parse(transport.FieldType, "Pause")); isPlaying.SetValue(main, true);
        var timeLabel=(Label)main.FindName("NoteNowText"); timeLabel.Content=".1234";
        typeof(MainWindow).GetMethod("FumenContent_SelectionChanged",Private)!.Invoke(main,new object?[]{null,EventArgs.Empty});
        Check(timeLabel.Content.ToString()==".1234", "Paused caret selection overwrites the actual playback clock");
        transport.SetValue(main, Enum.Parse(transport.FieldType, "Start")); isPlaying.SetValue(main, true);
        var ports = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); ports.Start();
        var port = ((System.Net.IPEndPoint)ports.LocalEndpoint).Port; ports.Stop();
        var session = new MajdataCore.AlphaSession { Id = "page-regression", Slot = 1, ViewPort = port };
        typeof(MajdataCore.AlphaSession).GetField("automaticSession", Private)!.SetValue(null, session);
        using var http = new System.Net.HttpListener(); http.Prefixes.Add($"http://localhost:{port}/"); http.Start();
        using var requestStop = new CancellationTokenSource();
        int commands = 0;
        var request = Task.Run(async () => {
            try {
                while (!requestStop.IsCancellationRequested) {
                    var context = await http.GetContextAsync().WaitAsync(requestStop.Token);
                    Interlocked.Increment(ref commands);
                    var reply = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true,\"protocolVersion\":1,\"sessionId\":\"page-regression\"}");
                    context.Response.ContentLength64 = reply.Length; await context.Response.OutputStream.WriteAsync(reply); context.Response.Close();
                }
            } catch (OperationCanceledException) { }
        });
        var capture = typeof(MainWindow).GetMethod("CaptureRoomText", Private)!;
        var apply = typeof(MainWindow).GetMethod("ApplyRoomText", Private)!;
        var before = (string)capture.Invoke(main, null)!;
        apply.Invoke(main, new object[] { before.Replace("1,2,3,4,E", "8,7,6,5,E") });
        Check((bool)isPlaying.GetValue(main)! && transport.GetValue(main)!.ToString() == "Start" &&
            (bool)typeof(MainWindow).GetField("roomChartNeedsReload", Private)!.GetValue(main)!,
            "received current difficulty must preserve playback and defer the new chart until explicit play");
        Check(((string)capture.Invoke(main, null)!).Contains("8,7,6,5,E"), "received edits must update the editor document immediately");
        apply.Invoke(main, new object[] { ((string)capture.Invoke(main, null)!).Replace("&first=0", "&first=1") });
        Check(ReferenceEquals(schedule, scheduleField.GetValue(main)) && Equals(playbackEnd, endField.GetValue(main)) &&
            scheduledTimes.SequenceEqual(((System.Collections.IEnumerable)schedule!).Cast<object>()
                .Select(sound => sound.GetType().GetField("time", Private)!.GetValue(sound))),
            "remote notes and offset must preserve the running sound schedule and completion timing");
        var json = typeof(MainWindow).GetMethod("BuildSongDetailMajson", Private)!.Invoke(main, null)!;
        var timings = (System.Collections.IEnumerable)json.GetType().GetField("timingList", Private)!.GetValue(json)!;
        Check(timings.Cast<object>().Count() > 0, "received chart must generate nonempty View playback JSON");
        before = (string)capture.Invoke(main, null)!;
        var oldNotes = parser.GetField("notelist", Private)!.GetValue(null);
        isPlaying.SetValue(main, true);
        apply.Invoke(main, new object[] { before });
        Check((bool)isPlaying.GetValue(main)! && ReferenceEquals(oldNotes, parser.GetField("notelist", Private)!.GetValue(null)),
            "identical snapshots must not stop playback or clear notes");
        apply.Invoke(main, new object[] { before.Replace("7,E", "6,E") });
        Check((bool)isPlaying.GetValue(main)! && ReferenceEquals(oldNotes, parser.GetField("notelist", Private)!.GetValue(null)),
            "edits in another difficulty must not affect local playback");
        typeof(MainWindow).GetMethod("SelectRoomDifficulty", Private)!.Invoke(main, new object[] { 5 });
        Check(((ComboBox)main.FindName("LevelSelector")).SelectedIndex == 5, "initial host difficulty must not prompt Save");
        main.chartChangeTimer.Stop();
        requestStop.Cancel(); request.GetAwaiter().GetResult();
        Check(commands == 0 && (bool)isPlaying.GetValue(main)! && transport.GetValue(main)!.ToString() == "Start",
            "document synchronization must send no pause, stop, reload or other View commands");
        typeof(MajdataCore.AlphaSession).GetField("automaticSession", Private)!.SetValue(null, null);
        Console.WriteLine("PLAYBACK PASS: actual Edit updates remote TXT without any View command, preserves playback, scheduled sounds and completion timing, and prepares new playback JSON.");
    }
}
