using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MajdataLauncher;

internal static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] static int Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var type = typeof(MainWindow);
        Check(window.Topmost && !window.ShowActivated, "pet must stay above normal windows without stealing focus");
        object? Field(string name) => type.GetField(name, Flags)!.GetValue(window);
        void Set(string name, object value) => type.GetField(name, Flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => type.GetMethod(name, Flags)!.Invoke(window, args);
        Check(!(bool)Field("startupDockPending")!, "startup must leave the pet visible instead of automatically docking");
        var animatorType = type.Assembly.GetType("MajdataLauncher.PetAnimator")!;
        var animator = Activator.CreateInstance(animatorType, Flags, null, new object[] { new Image(), new Image() }, null)!;
        Check((bool)animatorType.GetMethod("LoadAtlas")!.Invoke(animator, new object[] { Path.GetFullPath("MajdataLauncher/Pets/dilaxiong/spritesheet.png") })!, "real bear atlas must load");
        Set("animator", animator); Call("PlayModeGesture");
        Set("editorActive", true); Call("ApplyPetMode", true);
        var requestType = type.Assembly.GetType("MajdataLauncher.PetControlRequest")!;
        void Send(string action) {
            var request = Activator.CreateInstance(requestType)!;
            requestType.GetProperty("Action")!.SetValue(request, action);
            Call("HandlePetControlRequest", request);
        }
        var tick = animatorType.GetMethod("FrameTimer_Tick", Flags)!;
        void CompleteGesture() { for (int i = 0; i < 8; i++) tick.Invoke(animator, new object?[] { null, EventArgs.Empty }); }
        void Pump(int milliseconds) {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        var generation = animatorType.GetField("animationGeneration", Flags)!;
        var petImage=(Image)Field("PetImage")!;
        Check(petImage.RenderTransformOrigin==new Point(.5,1),"deformation must keep the pet's feet fixed");
        var petScale=(System.Windows.Media.ScaleTransform)petImage.RenderTransform;
        Call("PulsePetPress");
        void SeekPress(int milliseconds) {
            foreach(var name in new[]{"pressX","pressY"})
                ((System.Windows.Media.Animation.AnimationClock)Field(name)!).Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(milliseconds),System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
        }
        SeekPress(110);
        Check(petScale.ScaleY<.95 && petScale.ScaleX>1.025,"click must squash the pet before its rebound");
        SeekPress(450);
        Check(Math.Abs(petScale.ScaleY-1)<.001 && Math.Abs(petScale.ScaleX-1)<.001,"click deformation must finish and restore the resting scale");
        var before = generation.GetValue(animator);
        for(int i=0;i<12;i++) Call("ApplyPetMode",true);
        Check(Equals(before,generation.GetValue(animator)), "forced refreshes must not interrupt the working clip");
        Send("syntax-error"); Pump(320);
        Check(Field("displayedMode")!.ToString() == "SyntaxError", "settled error must show error status");
        Check(!Equals(before,generation.GetValue(animator)), "syntax error must interrupt the working clip immediately");
        Check(animatorType.GetField("state",Flags)!.GetValue(animator)!.ToString()=="Failed", "error expression must start without waiting for work cooldown");
        before = generation.GetValue(animator);
        Send("syntax-ok"); Send("syntax-error"); Pump(750);
        for(int i=0;i<12;i++) Call("ApplyPetMode",true);
        Check(Equals(before, generation.GetValue(animator)), "transient parser/forced events must not restart the error gesture");
        CompleteGesture();
        Check(!((DispatcherTimer)Field("expressionTimer")!).IsEnabled &&
            animatorType.GetField("state", Flags)!.GetValue(animator)!.ToString() == "Idle",
            "error must finish once without an ambient error replay timer");
        before = generation.GetValue(animator);
        Call("ResumeAmbientAnimation"); Call("ApplyPetMode",true); Call("PlayModeGesture");
        Check(Equals(before, generation.GetValue(animator)), "status refresh must not replay an unchanged error");
        Send("syntax-ok"); Pump(750);
        Check(Field("displayedMode")!.ToString() == "Editing", "a confirmed correction returns to editing");
        CompleteGesture();
        Check(((DispatcherTimer)Field("expressionTimer")!).Interval >= TimeSpan.FromSeconds(18), "editing retains cooldown even after error correction");
        before = generation.GetValue(animator);
        Send("syntax-error");
        Check(!Equals(before, generation.GetValue(animator)) && animatorType.GetField("state", Flags)!.GetValue(animator)!.ToString() == "Failed",
            "a new error after correction must bypass all ambient cooldowns");
        CompleteGesture(); Send("syntax-ok"); Pump(750);
        Send("preview"); Pump(320); Check(Field("displayedMode")!.ToString() == "Preview", "only preview action selects preview mode");
        Send("stop"); Pump(320); Check(Field("displayedMode")!.ToString() == "Editing", "stop returns to editing mode");
        CompleteGesture();
        bool Visible(System.Windows.Media.Imaging.BitmapSource image) {
            var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(image,System.Windows.Media.PixelFormats.Bgra32,null,0);
            var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
            return Enumerable.Range(0,bytes.Length/4).Any(i=>bytes[i*4+3]>16);
        }
        foreach(var pet in new[]{"dilaxiong","uni"}) {
            Check((bool)animatorType.GetMethod("LoadAtlas")!.Invoke(animator,new object[]{Path.GetFullPath($"MajdataLauncher/Pets/{pet}/spritesheet.png")})!, $"{pet} atlas load");
            var clips=(System.Windows.Media.Imaging.BitmapSource[][])animatorType.GetField("frames",Flags)!.GetValue(animator)!;
            var looks=(System.Windows.Media.Imaging.BitmapSource[])animatorType.GetField("lookFrames",Flags)!.GetValue(animator)!;
            Check(clips.Length==9 && clips.All(clip=>clip.Length>0 && clip.All(Visible)) && looks.Length==16 && looks.All(Visible),$"{pet} must never display a transparent animation or look frame");
            var animationType=type.Assembly.GetType("MajdataLauncher.PetAnimation")!;
            foreach(var animation in Enum.GetValues(animationType)) {
                animatorType.GetMethod("Play")!.Invoke(animator,new object?[]{animation,false,null,false}); CompleteGesture();
                Check(animatorType.GetField("state",Flags)!.GetValue(animator)!.ToString()=="Idle",$"{pet} {animation} must complete without getting stuck");
            }
        }
        var settingsType=type.Assembly.GetType("MajdataLauncher.Models.LauncherSettings")!;
        var settings=Activator.CreateInstance(settingsType)!;
        Check((string)settingsType.GetProperty("Pet")!.GetValue(settings)! == "dilaxiong", "default pet must be bear");
        var motionType=type.Assembly.GetType("MajdataLauncher.Models.PetMotionSettings")!;
        var config=motionType.GetMethod("Load",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{Path.GetFullPath("MajdataLauncher/Pets/dilaxiong/motion.json")})!;
        Check((double)motionType.GetProperty("BreathingIntervalSeconds")!.GetValue(config)! == 0 &&
            (double)motionType.GetProperty("BreathingDownSeconds")!.GetValue(config)! == 1.96 &&
            (double)motionType.GetProperty("BreathingUpSeconds")!.GetValue(config)! == 2.24,"bear must use the same breathing timings as Uni");
        Check((bool)motionType.GetProperty("ElasticityEnabled")!.GetValue(config)! && (bool)motionType.GetProperty("DragGravityEnabled")!.GetValue(config)!,"motion switches must default on");
        var motionRoot=Path.Combine(Path.GetTempPath(),"MajdataPetMotionTest-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(motionRoot);
        File.Copy(Path.GetFullPath("MajdataLauncher/Pets/dilaxiong/motion.json"),Path.Combine(motionRoot,"motion.json"));
        Call("LoadPetMotion",motionRoot);
        void SeekBreathing(double seconds) {
            foreach(var name in new[]{"breathingX","breathingY"})
                ((System.Windows.Media.Animation.AnimationClock)Field(name)!).Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(seconds),System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
        }
        SeekBreathing(0);
        Check(Math.Abs(petScale.ScaleY-1)<.001 && Math.Abs(petScale.ScaleX-1)<.001,"bear must begin at rest");
        SeekBreathing(1.96);
        Check(Math.Abs(petScale.ScaleY-.97)<.001 && Math.Abs(petScale.ScaleX-1.025)<.001,"bear must use Uni's downward phase");
        SeekBreathing(3.08);
        Check(Math.Abs(petScale.ScaleY-1.05)<.001 && Math.Abs(petScale.ScaleX-.988)<.001,"bear must use Uni's rebound phase");
        SeekBreathing(4.2);
        Check(Math.Abs(petScale.ScaleY-1)<.001,"bear must complete Uni's 4.2-second cycle");
        SeekBreathing(6.16);
        Check(Math.Abs(petScale.ScaleY-.97)<.001,"bear must repeat Uni's breathing cycle");
        File.WriteAllText(Path.Combine(motionRoot,"motion.json"),"{\"squashSpeed\":20,\"springSpeed\":35,\"breathingPeriodSeconds\":4.2,\"clickExpressionCooldownSeconds\":9,\"custom\":\"keep\"}");
        Call("LoadPetMotion",motionRoot);
        using (var migrated=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(motionRoot,"motion.json")))) {
            var json=migrated.RootElement;
            Check(!json.TryGetProperty("squashSpeed",out _) && !json.TryGetProperty("springSpeed",out _) && !json.TryGetProperty("breathingPeriodSeconds",out _),"migration must remove old fields from the actual config file");
            Check(Math.Abs(json.GetProperty("breathingDownSeconds").GetDouble()-1.96)<.00001 &&
                Math.Abs(json.GetProperty("breathingUpSeconds").GetDouble()-2.24)<.00001 &&
                json.GetProperty("breathingIntervalSeconds").GetDouble()==0,"migration must persist the calculated new timings");
            Check(json.GetProperty("custom").GetString()=="keep" && json.GetProperty("clickExpressionCooldownSeconds").GetDouble()==9,"migration must preserve unrelated config values");
        }
        var savedMigration=File.ReadAllText(Path.Combine(motionRoot,"motion.json"));
        Call("LoadPetMotion",motionRoot);
        Check(File.ReadAllText(Path.Combine(motionRoot,"motion.json"))==savedMigration,"new configurations must not be repeatedly rewritten");
        SeekBreathing(1.96);
        Check(Math.Abs(petScale.ScaleY-.97)<.001 && Math.Abs(petScale.ScaleX-1.025)<.001,"legacy Uni must keep its exact previous downward phase");
        SeekBreathing(3.08);
        Check(Math.Abs(petScale.ScaleY-1.05)<.001 && Math.Abs(petScale.ScaleX-.988)<.001,"legacy Uni must keep its exact previous rebound peak");
        SeekBreathing(4.2);
        Check(Math.Abs(petScale.ScaleY-1)<.001,"legacy Uni must keep its 4.2-second cycle");
        File.Copy(Path.GetFullPath("MajdataLauncher/Pets/uni/motion.json"),Path.Combine(motionRoot,"motion.json"),true);
        Call("LoadPetMotion",motionRoot);
        SeekBreathing(1.96);
        Check(Math.Abs(petScale.ScaleY-.97)<.001,"shipped Uni config must match the converted original motion");
        SeekBreathing(3.08);
        Check(Math.Abs(petScale.ScaleY-1.05)<.001,"shipped Uni config must match the original rebound");
        File.Copy(Path.GetFullPath("MajdataLauncher/Pets/dilaxiong/motion.json"),Path.Combine(motionRoot,"motion.json"),true);
        Call("LoadPetMotion",motionRoot);
        var noPress=Field("pressY");
        var downEvent=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left);
        Call("Window_MouseLeftButtonDown",window,downEvent);
        Check(ReferenceEquals(noPress,Field("pressY")),"mouse down must never start click elasticity");
        Set("petDragMoved",true);
        Call("Pet_MouseLeftButtonUp",window,downEvent);
        Check(ReferenceEquals(noPress,Field("pressY")),"drag release must never start click elasticity");
        var physics=Field("dragPhysics")!; var physicsType=physics.GetType();
        void Velocity(double value)=>physicsType.GetMethod("SetVelocity",Flags)!.Invoke(physics,new object[]{value});
        double Step(double dt)=>(double)physicsType.GetMethod("Step",Flags)!.Invoke(physics,new object[]{dt})!;
        void ResetPhysics()=>physicsType.GetMethod("Reset",Flags)!.Invoke(physics,null);
        foreach(var direction in new[]{-1d,1d}) {
            foreach(var fps in new[]{30,60,144}) {
                ResetPhysics();Velocity(direction*800);double angle=0;
                for(int i=0;i<(int)(fps*.12);i++) angle=Step(1d/fps);
                Check(angle*direction>10 && Math.Abs(angle)<=35,"amplified acceleration must visibly tilt against dragging without exceeding 35 degrees");
                for(int i=0;i<fps*2;i++) angle=Step(1d/fps);
                Check(Math.Abs(angle)<.05,"constant velocity must not sustain a false inertial force");
                Velocity(0);for(int i=0;i<(int)(fps*.15);i++) angle=Step(1d/fps);
                Check(angle*direction<-12 && Math.Abs(angle)<=35,"amplified braking must visibly swing ahead without exceeding 35 degrees");
                for(int i=0;i<fps*2;i++) angle=Step(1d/fps);
                Check(Math.Abs(angle)<.05,"pendulum must settle with damping");
            }
        }
        ResetPhysics();Velocity(-800);Set("petDragActive",true);
        Set("dragSampleTime",((System.Diagnostics.Stopwatch)Field("motionClock")!).Elapsed.TotalSeconds+1);
        for(int i=0;i<8;i++) { Pump(16); Call("MotionTick",window,EventArgs.Empty); }
        var tilt=(System.Windows.Media.RotateTransform)((FrameworkElement)Field("PetMotionHost")!).RenderTransform;
        Check(tilt.Angle<-2 && tilt.Angle>=-35,"WPF drag must lag behind leftward acceleration");
        Set("petDragActive",false); Pump(1200);
        Check(Math.Abs(tilt.Angle)<.5,"gravity must settle back upright after release");
        EventHandler dragFrame=(_,_)=>{};
        Set("petDragFrame",dragFrame); Set("petDragActive",true);
        System.Windows.Media.CompositionTarget.Rendering+=dragFrame;
        Call("StopPetDrag");
        Check(Field("petDragFrame")==null && !(bool)Field("petDragActive")!,"ending a drag must unsubscribe and clear its render callback");
        if (Directory.Exists(MajdataCore.AlphaSession.Root) && Directory.EnumerateDirectories(MajdataCore.AlphaSession.Root).Any()) {
            using var registry=new Mutex(false,"Local\\MajdataAlpha.SessionRegistry");
            registry.WaitOne();
            Task<bool> probe;
            try {
                var pointType=type.GetNestedType("DockPoint",BindingFlags.NonPublic)!;
                var point=Activator.CreateInstance(pointType)!;
                var started=System.Diagnostics.Stopwatch.StartNew();
                probe=(Task<bool>)Call("ProbePetDock",point)!;
                Check(started.ElapsedMilliseconds<100,"registry lookup must dispatch without blocking the WPF thread");
                Pump(80);
                Check(!probe.IsCompleted,"registry contention must wait in the background while UI continues");
            } finally {registry.ReleaseMutex();}
            while(!probe.IsCompleted) Pump(16);
            probe.GetAwaiter().GetResult();
            Console.WriteLine("PET DRAG PASS: registry contention left WPF responsive.");
        }
        Set("petDragMoved",false); Set("lastClickExpression",DateTime.MinValue);
        Call("Present", "test status", "existing bubble");
        var speechText=(TextBlock)Field("SpeechText")!;var bubble=(FrameworkElement)Field("SpeechBubbleHost")!;
        var originalSpeechTime=Field("lastSpeechAt");
        var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left);
        Call("Pet_MouseLeftButtonUp",window,click);
        Call("ApplyPetMode",true);
        Check(bubble.Visibility==Visibility.Visible && speechText.Text=="existing bubble" && Equals(originalSpeechTime,Field("lastSpeechAt")),"click and drag release refresh must preserve the current bubble and its expiry");
        Check(!ReferenceEquals(noPress,Field("pressY")),"a click must start elasticity only on release");
        before=generation.GetValue(animator);
        Call("Pet_MouseLeftButtonUp",window,click);
        Check(Equals(before,generation.GetValue(animator)),"click expression must have an independent cooldown");
        CompleteGesture(); Set("lastClickExpression",DateTime.UtcNow-TimeSpan.FromSeconds(7));
        Call("Pet_MouseLeftButtonUp",window,click);
        Check(!Equals(before,generation.GetValue(animator)),"click expression must become available after its cooldown");
        CompleteGesture();
        File.Delete(Path.Combine(motionRoot,"motion.json")); Directory.Delete(motionRoot);
        animatorType.GetMethod("ShowLookDirection")!.Invoke(animator, new object?[] { 45d, 1000, null });
        Check((bool)animatorType.GetProperty("IsGesturePlaying")!.GetValue(animator)!, "cursor look reserves the animation until completion");
        animatorType.GetMethod("Dispose")!.Invoke(animator, null);
        Call("Window_Closed", window, EventArgs.Empty);
        window = new MainWindow();
        window.Opacity = 0;
        Console.WriteLine("PET HWND: showing transparent test window");
        window.Show();
        Console.WriteLine("PET HWND: Show completed");
        Pump(30);
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var nativeStyle = type.GetMethod("GetWindowLong", BindingFlags.Static | BindingFlags.NonPublic)!;
        int Style() => (int)nativeStyle.Invoke(null, new object[] { handle, -20 })!;
        Check((Style() & 8) != 0, "showing the real pet HWND must establish WS_EX_TOPMOST");
        Check((Style() & 0x08000000) != 0, "native topmost must preserve WS_EX_NOACTIVATE");
        var setWindowPos = type.GetMethod("SetWindowPos", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)setWindowPos.Invoke(null, new object[] { handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013u })!,
            "test must be able to simulate loss of native topmost state");
        Check((Style() & 8) == 0, "native topmost loss must actually be reproduced");
        Call("KeepPetTopmost");
        Check((Style() & 8) != 0, "visibility handling must restore native topmost even when WPF Topmost is already true");
        window.Hide(); window.Show(); Pump(30);
        Check((Style() & 8) != 0, "restoring the pet must preserve the native topmost band");
        Check(!(bool)Field("startupDockPending")! && Field("dockedEditor") == null,
            "showing and restoring must not start automatic toolbar docking");
        Console.WriteLine("PET WINDOW PASS: real HWND is topmost/no-activate after Show and Hide/Show; startup auto-dock disabled.");
        Call("Window_Closed", window, EventArgs.Empty); window.Close();
        Console.WriteLine("PET PASS: Uni breathing for both pets, amplified acceleration/braking, frame-driven drag cleanup; both real atlases, all visible clips/directions, complete clips under state/force bursts, per-mode cooldowns and default bear.");
        return 0;
    }
}
