using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows;
using System.Diagnostics;
using System.IO;
using MajdataLauncher.Models;

namespace MajdataLauncher;

public partial class MainWindow
{
    private AnimationClock? pressX;
    private AnimationClock? pressY;
    private PetMotionSettings petMotion = new();
    private string? petMotionPath;
    private DateTime lastClickExpression = DateTime.MinValue;
    private readonly DispatcherTimer motionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch motionClock = Stopwatch.StartNew();
    private double lastMotionTime, dragSampleTime, dragSampleX;
    private readonly PetDragPhysics dragPhysics = new();
    private bool petDragMoved;
    private Point? petDragStartScreen;
    private AnimationClock? breathingX;
    private AnimationClock? breathingY;

    private void LoadPetMotion(string petRoot)
    {
        petMotionPath = Path.Combine(petRoot, "motion.json");
        petMotion = PetMotionSettings.Load(petMotionPath);
        lastClickExpression = DateTime.MinValue;
        ApplyPetMotionSettings();
        motionTimer.Tick -= IdleMotionTick;
        motionTimer.Tick += IdleMotionTick;
        lastMotionTime = motionClock.Elapsed.TotalSeconds;
        motionTimer.Start();
    }

    private void ApplyPetMotionSettings()
    {
        pressX?.Controller?.Remove(); pressY?.Controller?.Remove();
        var scale = (ScaleTransform)PetImage.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (petMotion.ElasticityEnabled)
        {
            breathingX = BreathingClock(1.025, .988);
            breathingY = BreathingClock(.97, 1.05);
            scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, breathingX);
            scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, breathingY);
        }
        if (!petMotion.DragGravityEnabled) ResetPetTilt();
    }

    private AnimationClock BreathingClock(double squashed, double sprung)
    {
        var interval = petMotion.BreathingIntervalSeconds;
        var down = petMotion.BreathingDownSeconds;
        var up = petMotion.BreathingUpSeconds;
        var duration = interval + down + up;
        // The upward phase includes the rebound back to rest. Splitting it
        // evenly preserves the exact waveform of converted old configurations.
        var peak = interval + down + up / 2;
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(duration),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (interval > 0)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(interval))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(squashed, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(interval + down)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(sprung, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(peak)), new SineEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(duration)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return (AnimationClock)animation.CreateClock(true);
    }

    private void BeginPetTilt(double x)
    {
        dragSampleX=x; dragSampleTime=motionClock.Elapsed.TotalSeconds;
        dragPhysics.SetVelocity(0);
    }

    private void SamplePetDrag(double x)
    {
        var now=motionClock.Elapsed.TotalSeconds;
        var dt=Math.Max(.005,now-dragSampleTime);
        dragPhysics.SetVelocity(petMotion.DragGravityEnabled ? (x-dragSampleX)/dt : 0);
        dragSampleX=x; dragSampleTime=now;
    }

    private void IdleMotionTick(object? sender, EventArgs e)
    {
        if (!petDragActive) MotionTick(sender, e);
    }

    private void MotionTick(object? sender, EventArgs e)
    {
        var now=motionClock.Elapsed.TotalSeconds;
        var dt=Math.Clamp(now-lastMotionTime,.001,.05); lastMotionTime=now;
        if (!petDragActive || docking || now-dragSampleTime>.06) dragPhysics.SetVelocity(0);
        if (!petMotion.DragGravityEnabled) dragPhysics.Reset();
        ((RotateTransform)PetMotionHost.RenderTransform).Angle=dragPhysics.Step(dt);
    }

    private void ResetPetTilt()
    {
        dragPhysics.Reset();
        ((RotateTransform)PetMotionHost.RenderTransform).Angle=0;
    }

    private void PulsePetPress()
    {
        if (!petMotion.ElasticityEnabled || PetImage.RenderTransform is not ScaleTransform scale)
            return;
        pressX?.Controller?.Remove();
        pressY?.Controller?.Remove();
        pressX = PressClock(.055, -.015);
        pressY = PressClock(-.13, .035);
        // Compose additive deltas with the breathing clock; never replace it.
        scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, pressX, HandoffBehavior.Compose);
        scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, pressY, HandoffBehavior.Compose);
    }

    private AnimationClock PressClock(double pressed, double rebound)
    {
        // Click feedback is independent of the breathing phase speeds.
        var down = TimeSpan.FromMilliseconds(90);
        var up = TimeSpan.FromMilliseconds(120);
        var end = down + up + TimeSpan.FromMilliseconds(140);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            IsAdditive = true,
            FillBehavior = FillBehavior.Stop,
            Duration = end
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(System.TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(pressed, KeyTime.FromTimeSpan(down), new SineEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(rebound, KeyTime.FromTimeSpan(down+up), new SineEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(end), new SineEase { EasingMode = EasingMode.EaseInOut }));
        var clock = (AnimationClock)animation.CreateClock(true);
        clock.Completed += (_, _) => clock.Controller?.Remove();
        return clock;
    }
}
