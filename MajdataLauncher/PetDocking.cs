using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MajdataLauncher.Models;
using MajdataCollaboration;
using MajdataCore;

namespace MajdataLauncher;

public partial class MainWindow
{
    private readonly CancellationTokenSource dockStop = new();
    private AlphaSession? dockedEditor;
    private EventHandler? petDragFrame;
    private bool docking;
    private bool probingDock;
    private bool petDragActive;
    // Automatic startup docking is temporarily disabled; manual toolbar docking remains available.
    private bool startupDockPending;
    private bool startupAppearancePlaying;
    private TaskCompletionSource<bool>? startupJumpCompletion;
    private int startupAppearanceGeneration;
    private async void PlayStartupAppearance()
    {
        var generation = ++startupAppearanceGeneration;
        startupJumpCompletion?.TrySetResult(true);
        startupAppearancePlaying = true;
        try { await PlayStartupJump(generation); }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == startupAppearanceGeneration)
            {
                startupAppearancePlaying = false;
                ApplyPetMode(true);
            }
        }
    }
    private async Task PlayStartupJump(int generation)
    {
        if (animator?.HasAtlas != true) return;
        var finished = new TaskCompletionSource<bool>();
        startupJumpCompletion = finished;
        animator.Play(PetAnimation.Jumping, false, () => finished.TrySetResult(true));
        await Task.WhenAny(finished.Task, Task.Delay(5000, dockStop.Token));
        if (ReferenceEquals(startupJumpCompletion, finished)) startupJumpCompletion = null;
        dockStop.Token.ThrowIfCancellationRequested();
        if (generation == startupAppearanceGeneration && !docking && dockedEditor == null)
            animator.Play(PetAnimation.Idle, true);
    }
    private DateTime petTransitionUntil;
    private async void StartDockListener()
    {
        Closed += (_, _) => dockStop.Cancel();
        while (!dockStop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PetDock.Pipe(Environment.ProcessId), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(dockStop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(dockStop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = await RoomWire.Read<PetDockRequest>(pipe, timeout.Token);
                using var self = Process.GetCurrentProcess();
                bool valid = request.ProcessStart == self.StartTime.ToUniversalTime().Ticks;
                if (valid)
                {
                    while (docking) await Task.Delay(20, timeout.Token);
                    startupDockPending = false;
                    dockedEditor = null; Show();
                    if (double.IsFinite(request.X) && double.IsFinite(request.Y) && Math.Abs(request.X) < 100000 && Math.Abs(request.Y) < 100000)
                    {
                        var dpi = VisualTreeHelper.GetDpi(this);
                        Left = request.X / dpi.DpiScaleX - Width / 2; Top = request.Y / dpi.DpiScaleY - Height / 2;
                    }
                    animator?.Play(PetAnimation.Idle, true);
                    AnimatePet(false);
                    if (Math.Abs(request.X) < 100000) ContinueUndockDrag();
                    else PlayStartupAppearance();
                }
                await RoomWire.Write(pipe, valid, timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.WriteLine(error); }
        }
    }
    private async Task TryDockPet()
    {
        if (docking || !GetCursorPos(out var point)) return;
        docking = true;
        try
        {
        using var self = Process.GetCurrentProcess();
        foreach (var session in AlphaSession.List())
        {
            if (!AlphaSession.IsProcess(session.EditPid, session.EditStart)) continue;
            var reply = await GroupControl.Send(new(session.Id, session.EditPid, session.EditStart), new GroupCommand {
                Action = "dock", X = point.X, Y = point.Y, PetPid = self.Id, PetStart = self.StartTime.ToUniversalTime().Ticks });
            if (!reply.Ok) continue;
            await CompleteDock(session, reply);
            return;
        }
        }
        catch (OperationCanceledException) { }
        finally { docking = false; if (dockedEditor == null) ApplyPetMode(true); }
    }
    private void AnimatePet(bool entering)
    {
        petTransitionUntil = DateTime.UtcNow.AddMilliseconds(entering ? 880 : 550);
        var visual = (FrameworkElement)Content;
        visual.Effect = null;
        var scale = visual.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        var fromScale = scale.ScaleX;
        var fromOpacity = visual.Opacity;
        visual.RenderTransform = scale;
        visual.RenderTransformOrigin = new Point(0.5, 0.5);
        var duration = TimeSpan.FromMilliseconds(entering ? 840 : 520);
        var animation = new DoubleAnimation(fromScale, entering ? 0.1 : 1, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        visual.BeginAnimation(OpacityProperty,
            new DoubleAnimation(fromOpacity, entering ? 0 : 1, duration));
    }
    // WPF's Mouse state belongs to this window's input queue. During a drag
    // handed off from Edit, follow the physical button until its first release.
    private void ContinueUndockDrag() => BeginPetDrag(true);

    private void StopPetDrag()
    {
        if (petDragFrame != null) CompositionTarget.Rendering -= petDragFrame;
        petDragFrame = null;
        petDragActive = false;
    }

    private void BeginPetDrag(bool fromIcon)
    {
        StopPetDrag();
        petDragStartScreen = null;
        petDragMoved = fromIcon;
        if (fromIcon) PlayStartupAppearance();
        if ((GetAsyncKeyState(1) & 0x8000) == 0 || !GetCursorPos(out var start))
        { return; }
        startupDockPending = false; petDragActive = true;
        petDragStartScreen = new Point(start.X, start.Y);
        petDragMoved = fromIcon;
        BeginPetTilt(start.X / VisualTreeHelper.GetDpi(this).DpiScaleX);
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var bounds)) { StopPetDrag(); return; }
        var grab = new Point(start.X - bounds.Left, start.Y - bounds.Top);
        var leftDockArea = !fromIcon;
        bool moved = false;
        var lastFrame = TimeSpan.MinValue;
        petDragFrame = async (_, frame) =>
        {
            if (frame is RenderingEventArgs rendering)
            {
                if (rendering.RenderingTime == lastFrame) return;
                lastFrame = rendering.RenderingTime;
            }
            if ((GetAsyncKeyState(1) & 0x8000) == 0)
            {
                StopPetDrag();
                SetDockPreview(false);
                // A quick first drag must not put the pet straight back into
                // the enlarged toolbar hitbox it has just left.
                if (moved && leftDockArea) await TryDockPet();
                ApplyPetMode(true);
                return;
            }
            if (!GetCursorPos(out var point)) return;
            moved |= Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) > 12;
            petDragMoved = moved || fromIcon;
            var dpi = VisualTreeHelper.GetDpi(this);
            SamplePetDrag(point.X / dpi.DpiScaleX);
            MotionTick(this, frame);
            // Update both screen coordinates in one native move. Setting Left and
            // Top separately makes a transparent WPF window move twice per frame.
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(point.X - grab.X),
                (int)Math.Round(point.Y - grab.Y), 0, 0, 0x0015);
            if (probingDock || DateTime.UtcNow < petTransitionUntil) return;
            probingDock = true;
            try
            {
                bool nearby = await ProbePetDock(point);
                if (!nearby) leftDockArea = true;
                if (petDragActive && !docking) SetDockPreview(nearby && leftDockArea);
                await Task.Delay(100, dockStop.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.WriteLine(error); }
            finally { probingDock = false; }
        };
        CompositionTarget.Rendering += petDragFrame;
    }

    private Task<bool> ProbePetDock(DockPoint point) => Task.Run(async () =>
    {
        // Reading the instance registry can wait on its mutex or disk. Keep that
        // work off the render thread so docking hints cannot stop cursor tracking.
        using var self = Process.GetCurrentProcess();
        foreach (var session in AlphaSession.List())
        {
            if (!AlphaSession.IsProcess(session.EditPid, session.EditStart)) continue;
            var reply = await GroupControl.Send(new(session.Id, session.EditPid, session.EditStart),
                new GroupCommand { Action = "dock-probe", X = point.X, Y = point.Y,
                    PetPid = self.Id, PetStart = self.StartTime.ToUniversalTime().Ticks });
            if (reply.Ok) return true;
        }
        return false;
    }, dockStop.Token);
    private void CheckDockOwner()
    {
        if (dockedEditor != null && !AlphaSession.IsProcess(dockedEditor.EditPid, dockedEditor.EditStart))
        {
            dockedEditor = null;
            editorActive = syntaxErrors = previewActive = false;
            displayedMode = (PetMode)(-1);
            Show(); AnimatePet(false); ApplyPetMode(true);
        }
        sessionTimer.Interval = startupDockPending ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(2);
        if (startupDockPending && !docking && !petDragActive && !launching)
            _ = DockAfterStartup();
    }
    private void SetDockPreview(bool nearby)
    {
        var visual = (FrameworkElement)Content;
        visual.BeginAnimation(OpacityProperty, null);
        visual.Opacity = nearby ? .55 : 1;
        visual.Effect = nearby ? new System.Windows.Media.Effects.BlurEffect { Radius = 2 } : null;
    }
    private async Task<bool> CompleteDock(AlphaSession session, GroupReply reply)
    {
        dockedEditor = session;
        var completed = false;
        try
        {
            SetDockPreview(false);
            ResetPetTilt();
            startupJumpCompletion?.TrySetResult(true);
            // The expression and flight share a start; never wait for the
            // atlas callback before moving toward the toolbar.
            animator?.Play(PetAnimation.Jumping, false);
            var dpi = VisualTreeHelper.GetDpi(this);
            var duration = TimeSpan.FromMilliseconds(840);
            BeginAnimation(LeftProperty, new DoubleAnimation(Left, reply.X / dpi.DpiScaleX - Width / 2, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            BeginAnimation(TopProperty, new DoubleAnimation(Top, reply.Y / dpi.DpiScaleY - Height / 2, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            AnimatePet(true);
            await Task.Delay(duration, dockStop.Token);
            Hide(); BeginAnimation(LeftProperty, null); BeginAnimation(TopProperty, null);
            using var self = Process.GetCurrentProcess();
            var complete = new GroupCommand { Action = "dock-complete", PetPid = self.Id,
                PetStart = self.StartTime.ToUniversalTime().Ticks };
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var result = await GroupControl.Send(new(session.Id, session.EditPid, session.EditStart), complete);
                if (result.Ok) return completed = true;
                await Task.Delay(150, dockStop.Token);
            }
            return false;
        }
        finally
        {
            if (!completed && !dockStop.IsCancellationRequested)
            {
                // The Edit hand-off may fail after Hide (for example, if Edit
                // exits mid-flight). Keep the pet visible for the next retry.
                dockedEditor = null;
                Show();
                AnimatePet(false);
                ApplyPetMode(true);
            }
        }
    }
    private async Task DockAfterStartup()
    {
        if (docking || !startupDockPending) return;
        docking = true;
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (var session in AlphaSession.List())
            {
                if (!AlphaSession.IsProcess(session.EditPid, session.EditStart)) continue;
                var reply = await GroupControl.Send(new(session.Id, session.EditPid, session.EditStart),
                    new GroupCommand { Action = "dock-ready", PetPid = self.Id, PetStart = self.StartTime.ToUniversalTime().Ticks });
                if (!reply.Ok) continue;
                reply = await GroupControl.Send(new(session.Id, session.EditPid, session.EditStart),
                    new GroupCommand { Action = "dock-startup", PetPid = self.Id, PetStart = self.StartTime.ToUniversalTime().Ticks });
                if (reply.Ok)
                {
                    if (await CompleteDock(session, reply))
                        startupDockPending = false;
                }
                break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Debug.WriteLine(error); }
        finally { docking = false; if (dockedEditor == null) ApplyPetMode(true); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DockPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct DockRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out DockPoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out DockRect rectangle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
