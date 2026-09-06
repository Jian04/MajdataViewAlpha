using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;
using System.Linq;

namespace MajdataLauncher;

internal sealed class PetAnimator : IDisposable
{
    private static readonly int[] FrameCounts = { 6, 8, 8, 4, 5, 8, 6, 6, 6 };
    private static readonly int[][] FrameDurations =
    {
        new[] { 3200, 110, 110, 140, 140, 3200 },
        new[] { 120, 120, 120, 120, 120, 120, 120, 220 },
        new[] { 120, 120, 120, 120, 120, 120, 120, 220 },
        new[] { 140, 140, 140, 280 },
        new[] { 140, 140, 140, 140, 280 },
        new[] { 160, 160, 180, 4000, 180, 180, 220, 400 },
        new[] { 150, 150, 150, 150, 150, 260 },
        new[] { 120, 120, 120, 120, 120, 220 },
        new[] { 2600, 170, 170, 170, 170, 2600 }
    };

    private readonly Image previous;
    private readonly Image target;
    private readonly DispatcherTimer frameTimer = new();
    private BitmapSource? atlas;
    private BitmapSource[][] frames = Array.Empty<BitmapSource[]>();
    private BitmapSource[] lookFrames = Array.Empty<BitmapSource>();
    private PetAnimation state;
    private int frame;
    private bool loop;
    private bool holdFinalFrame;
    private bool showingLookDirection;
    private Action? completed;

    public bool HasAtlas => atlas != null;

    public PetAnimator(Image previous, Image target)
    {
        this.previous = previous;
        this.target = target;
        frameTimer.Tick += FrameTimer_Tick;
    }

    public bool LoadAtlas(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.EndInit();
            image.Freeze();
            if (image.PixelWidth != 1536 || image.PixelHeight != 2288)
                return false;
            atlas = image;
            frames = FrameCounts.Select((count, row) => Enumerable.Range(0, count)
                .Select(column => FreezeCrop(column, row)).ToArray()).ToArray();
            lookFrames = Enumerable.Range(0, 16)
                .Select(index => FreezeCrop(index % 8, index < 8 ? 9 : 10)).ToArray();
            Play(PetAnimation.Idle, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Play(PetAnimation animation, bool shouldLoop = false, Action? onCompleted = null,
        bool holdOnLastFrame = false)
    {
        if (atlas != null && !showingLookDirection && animation == PetAnimation.Failed &&
            state == animation && frameTimer.IsEnabled)
            return;

        if (atlas != null && !showingLookDirection && state == animation &&
            holdFinalFrame && holdOnLastFrame)
        {
            completed = onCompleted;
            return;
        }
        if (atlas != null && !showingLookDirection && state == animation && loop && shouldLoop && frameTimer.IsEnabled)
        {
            completed = onCompleted;
            return;
        }

        showingLookDirection = false;
        state = animation;
        frame = 0;
        loop = shouldLoop;
        holdFinalFrame = holdOnLastFrame;
        completed = onCompleted;
        if (atlas == null)
            return;
        ApplyFrame();
        ScheduleFrame();
    }

    public void ShowLookDirection(double degrees, int holdMilliseconds = 900, Action? onCompleted = null)
    {
        if (atlas == null)
            return;

        showingLookDirection = true;
        loop = false;
        completed = onCompleted;
        ApplyLookFrame(degrees);
        frameTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(holdMilliseconds, 120, 5000));
        frameTimer.Start();
    }

    private void FrameTimer_Tick(object? sender, EventArgs e)
    {
        frameTimer.Stop();
        if (showingLookDirection)
        {
            showingLookDirection = false;
            completed?.Invoke();
            completed = null;
            Play(PetAnimation.Idle, true);
            return;
        }

        frame++;
        var count = FrameCounts[(int)state];
        if (frame >= count)
        {
            if (!loop)
            {
                if (holdFinalFrame)
                {
                    frame = count - 1;
                    ApplyFrame();
                    completed?.Invoke();
                    completed = null;
                    return;
                }
                completed?.Invoke();
                completed = null;
                Play(PetAnimation.Idle, true);
                return;
            }
            frame = 0;
        }
        ApplyFrame();
        ScheduleFrame();
    }

    private void ApplyFrame()
    {
        if (atlas == null)
            return;
        ApplySource(frames[(int)state][frame]);
    }

    private void ApplyLookFrame(double degrees)
    {
        if (atlas == null)
            return;

        var normalized = ((degrees % 360) + 360) % 360;
        var index = (int)Math.Round(normalized / 22.5, MidpointRounding.AwayFromZero) % 16;
        ApplySource(lookFrames[index]);
    }

    private void ApplySource(BitmapSource source)
    {
        previous.BeginAnimation(UIElement.OpacityProperty, null);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Source = source;
        target.Opacity = 1d;
        previous.Source = null;
        previous.Visibility = Visibility.Collapsed;
        previous.Opacity = 1d;
    }

    private BitmapSource FreezeCrop(int column, int row)
    {
        var crop = new CroppedBitmap(atlas!,
            new System.Windows.Int32Rect(column * 192, row * 208, 192, 208));
        crop.Freeze();
        return crop;
    }

    private void ScheduleFrame()
    {
        frameTimer.Interval = TimeSpan.FromMilliseconds(FrameDurations[(int)state][frame]);
        frameTimer.Start();
    }

    public void Dispose() => frameTimer.Stop();
}
