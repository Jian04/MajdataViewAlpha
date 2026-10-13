using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private int[][] durations = Array.Empty<int[]>();
    private BitmapSource[] lookFrames = Array.Empty<BitmapSource>();
    private PetAnimation state;
    private int frame;
    private bool loop;
    private bool holdFinalFrame;
    private bool showingLookDirection;
    private Action? completed;
    private int animationGeneration;

    public bool HasAtlas => atlas != null;
    public bool IsGesturePlaying => frameTimer.IsEnabled && !loop;

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
            // Some atlases deliberately omit entire animations/directions.
            // Keep only visible cells, with their original frame timing.
            frames = new BitmapSource[FrameCounts.Length][];
            durations = new int[FrameCounts.Length][];
            for (int row = 0; row < FrameCounts.Length; row++)
            {
                var cells = Enumerable.Range(0, FrameCounts[row])
                    .Select(column => (Image: FreezeCrop(column, row), Column: column))
                    .Where(cell => HasVisiblePixels(cell.Image)).ToArray();
                frames[row] = cells.Select(cell => cell.Image).ToArray();
                durations[row] = cells.Select(cell => FrameDurations[row][cell.Column]).ToArray();
            }
            if (frames[(int)PetAnimation.Idle].Length == 0) return false;
            for (int row = 0; row < frames.Length; row++)
            {
                if (frames[row].Length != 0) continue;
                // Missing walking clips use the available working motion;
                // other missing clips keep the pet visible in its idle pose.
                int fallback = row is 1 or 2 && frames[(int)PetAnimation.Running].Length != 0
                    ? (int)PetAnimation.Running : (int)PetAnimation.Idle;
                frames[row] = frames[fallback]; durations[row] = durations[fallback];
            }
            lookFrames = Enumerable.Range(0, 16).Select(index => {
                var crop = FreezeCrop(index % 8, index < 8 ? 9 : 10);
                return HasVisiblePixels(crop) ? crop : frames[(int)PetAnimation.Idle][0];
            }).ToArray();
            frameTimer.Stop();
            completed = null;
            loop = false;
            holdFinalFrame = false;
            showingLookDirection = false;
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
        animationGeneration++;
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
        animationGeneration++;
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
            var generation = animationGeneration;
            var callback = completed;
            completed = null;
            callback?.Invoke();
            if (animationGeneration == generation)
                Play(PetAnimation.Idle, true);
            return;
        }

        frame++;
        var count = frames[(int)state].Length;
        if (frame >= count)
        {
            if (!loop)
            {
                if (holdFinalFrame)
                {
                    frame = count - 1;
                    ApplyFrame();
                    var callback = completed;
                    completed = null;
                    callback?.Invoke();
                    return;
                }
                var generation = animationGeneration;
                var finished = completed;
                completed = null;
                finished?.Invoke();
                if (animationGeneration == generation)
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
        if (ReferenceEquals(target.Source, source))
            return;
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
        frameTimer.Interval = TimeSpan.FromMilliseconds(durations[(int)state][frame]);
        frameTimer.Start();
    }

    private static bool HasVisiblePixels(BitmapSource image)
    {
        var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        rgba.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 16);
    }

    public void Dispose() {
        frameTimer.Stop();
    }
}
