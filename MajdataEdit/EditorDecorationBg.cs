using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MajdataEdit;

// Main-window background decoration recreating the official site's maiDecorationBg with native WPF transform animations:
// radial-gradient base, slowly rotating pattern, three rings, rising fading stars, orbiting/spinning objects, and corner frames.
// RenderTransform and Opacity animations run on WPF's composition thread with GPU compositing and low overhead.
// No GIF is needed. Assets live under Themes/{circleplus|circle}/, and elements with missing images are skipped.
internal static class EditorDecorationBg
{
    private const double W = 1920;
    private const double H = 1080;
    private static readonly ConcurrentDictionary<string, BitmapSource> ImageCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Apply(Border host, string? style)
    {
        if (host == null)
            return;
        style = style?.Trim().ToLowerInvariant();
        host.Tag = style;
        if (style != "circleplus" && style != "circle")
        {
            host.Child = null;
            host.Visibility = Visibility.Collapsed;
            return;
        }

        if (host.Child is FrameworkElement existing && Equals(existing.Tag, style))
        {
            host.Visibility = Visibility.Visible;
            return;
        }

        host.Child = Build(style);
        host.Visibility = Visibility.Visible;
    }

    private static FrameworkElement Build(string style)
    {
        var intl = style == "circle";
        var canvas = new Canvas { Width = W, Height = H };

        // Base: circle-farthest-corner radial gradient with the 1101 px radius converted to a relative ratio.
        var stops = new GradientStopCollection();
        if (intl)
        {
            stops.Add(new GradientStop(FromHex("#FFB7CD"), 0.00));
            stops.Add(new GradientStop(FromHex("#FFB7CD"), 0.40));
            stops.Add(new GradientStop(FromHex("#FF4799"), 1.00));
        }
        else
        {
            stops.Add(new GradientStop(FromHex("#FFBCD7"), 0.00));
            stops.Add(new GradientStop(FromHex("#FFBCD7"), 0.27));
            stops.Add(new GradientStop(FromHex("#FC9CCC"), 0.35));
            stops.Add(new GradientStop(FromHex("#FF97EF"), 0.56));
            stops.Add(new GradientStop(FromHex("#BF9CFF"), 0.77));
            stops.Add(new GradientStop(FromHex("#55E5FD"), 0.98));
        }
        var gradient = new RadialGradientBrush(stops)
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 1101.0 / W,
            RadiusY = 1101.0 / H
        };
        gradient.Freeze();
        canvas.Children.Add(new Rectangle { Width = W, Height = H, Fill = gradient });

        // Slowly rotating white pattern approximating the site's low-opacity overlay blend.
        AddImage(canvas, style, "bg_pattern", W / 2 - 960, H / 2 - 960, 1920, opacity: 0.45,
            rotateSeconds: 500);

        // Three center rings; colorful uses the site's surge-and-pause Bézier pulse reproduced with KeySpline.
        AddCentered(canvas, style, "circle_yellow", intl ? 1026 : 1026,
            tilt: intl); // International version oscillates over 80 seconds.
        AddCentered(canvas, style, "circle_white", 788, rotateSeconds: -110);
        AddCentered(canvas, style, "circle_colorful", 953, pulse: true);

        // Rising and fading tiles and stars match the site's CSS positions, periods, and delays; international pink stars fall and flip vertically.
        AddRiser(canvas, style, "tile_green", W - 42 - 216, 167, 216, 692, 12, 0);
        AddRiser(canvas, style, "tile_purple_left", 30, 28, 192, 593, 15, 3);
        AddRiser(canvas, style, "tile_purple_right", W - 300 - 140, 0, 140, 340, 10, 1.5);
        AddRiser(canvas, style, "star_pink", 268, 562, 90, 306, 6, 4, reverse: intl, flip: intl);
        AddRiser(canvas, style, "star_pink", W - 300 - 52, 402, 52, 174, 8, 4, reverse: intl, flip: intl);
        AddRiser(canvas, style, "star_yellow", 332, 168, 64, 213, 7, 0.5);
        AddRiser(canvas, style, "star_yellow", W - 524 - 64, 618, 64, 213, 10, 5);

        // Orbital layer completes one revolution in 70 seconds while each object spins.
        var orbit = new Canvas { Width = W, Height = H };
        Rotate(orbit, 70, new Point(W / 2, H / 2));
        AddOrbiter(orbit, style, "3d_cube", 80, 130, 88, 18);
        AddOrbiter(orbit, style, "3d_cube", W - 100 - 113, 400, 113, -25);
        AddOrbiter(orbit, style, "3d_star_small", W - 506 - 34, 192, 34, 15);
        AddOrbiter(orbit, style, "3d_stars", W - 260 - 93, 700, 93, 28);
        AddOrbiter(orbit, style, "3d_glove_blue", 702, 34, 69, -20);
        AddOrbiter(orbit, style, "3d_glove_pink", 568, 34, 108, -16);
        if (!intl)
        {
            AddOrbiter(orbit, style, "3d_star_small", 540, 714, 85, -22); // The site's 3d_star is gone, so use an enlarged small star.
            AddOrbiter(orbit, style, "3d_pink", 200, 500, 120, -30);
            AddOrbiter(orbit, style, "3d_orange", W - 200 - 120, 600, 120, 30);
        }
        canvas.Children.Add(orbit);

        // Corner frames attach directly to the rectangular editor window.
        if (intl)
        {
            AddImage(canvas, style, "corner_top_left", 0, 0, 853);
            AddImage(canvas, style, "corner_top_right", W - 316, 0, 316);
            AddImageBottom(canvas, style, "corner_bottom_left", 0, 231);
            AddImageBottom(canvas, style, "corner_bottom_right", W - 683, 683);
        }
        else
        {
            AddImage(canvas, style, "corner_top_left", 0, 0, 853);
            AddImage(canvas, style, "corner_top_right", W - 568, 0, 568);
            AddImageBottom(canvas, style, "corner_bottom_left", 0, 280);
            AddImageBottom(canvas, style, "corner_bottom_right", W - 683, 683);
        }

        return new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.UniformToFill,
            Tag = style
        };
    }

    // ---------- Element construction ----------

    private static Image? AddImage(Canvas canvas, string style, string name,
        double left, double top, double width, double opacity = 1.0, double rotateSeconds = 0)
    {
        var image = CreateImage(style, name, width);
        if (image == null)
            return null;
        image.Opacity = opacity;
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        if (rotateSeconds != 0)
            Rotate(image, rotateSeconds, null);
        canvas.Children.Add(image);
        return image;
    }

    private static void AddImageBottom(Canvas canvas, string style, string name, double left, double width)
    {
        var image = CreateImage(style, name, width);
        if (image == null)
            return;
        Canvas.SetLeft(image, left);
        Canvas.SetBottom(image, 0);
        canvas.Children.Add(image);
    }

    private static void AddCentered(Canvas canvas, string style, string name, double width,
        double rotateSeconds = 0, bool pulse = false, bool tilt = false)
    {
        var image = CreateImage(style, name, width);
        if (image == null)
            return;
        // Scale height to the asset aspect ratio; center once SizeChanged reveals the loaded height.
        image.Loaded += (_, _) =>
        {
            Canvas.SetLeft(image, W / 2 - image.ActualWidth / 2);
            Canvas.SetTop(image, H / 2 - image.ActualHeight / 2);
        };
        if (pulse)
            RotatePulse(image);
        else if (tilt)
            RotateTilt(image);
        else if (rotateSeconds != 0)
            Rotate(image, rotateSeconds, null);
        canvas.Children.Add(image);
    }

    private static void AddRiser(Canvas canvas, string style, string name,
        double left, double top, double width, double height,
        double periodSeconds, double delaySeconds, bool reverse = false, bool flip = false)
    {
        var image = CreateImage(style, name, width);
        if (image == null)
            return;
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        image.Opacity = 0;

        var travel = height * 1.5;
        var translate = new TranslateTransform();
        var transform = new TransformGroup();
        if (flip)
            transform.Children.Add(new ScaleTransform(1, -1, width / 2, height / 2));
        transform.Children.Add(translate);
        image.RenderTransform = transform;

        var duration = TimeSpan.FromSeconds(periodSeconds);
        var begin = TimeSpan.FromSeconds(delaySeconds);
        var yAnim = new DoubleAnimation(reverse ? -travel : travel, reverse ? travel : -travel, duration)
        {
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = begin
        };
        translate.BeginAnimation(TranslateTransform.YProperty, yAnim);

        // Match the site's riseAndFade with 10% fade-in and fade-out intervals.
        var opacityAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = begin
        };
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.9)));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        image.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        canvas.Children.Add(image);
    }

    private static void AddOrbiter(Canvas orbit, string style, string name,
        double left, double top, double width, double rotateSeconds)
    {
        var image = CreateImage(style, name, width);
        if (image == null)
            return;
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        Rotate(image, rotateSeconds, null);
        orbit.Children.Add(image);
    }

    private static Image? CreateImage(string style, string name, double width)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Themes", style, name + ".png");
        if (!File.Exists(path))
            return null;
        try
        {
            var bitmap = ImageCache.GetOrAdd(path, LoadBitmap);
            return new Image { Source = bitmap, Width = width };
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ---------- Animation ----------

    // Uniform rotation; negative seconds mean counterclockwise, matching the site's rotateReverse.
    private static void Rotate(FrameworkElement element, double seconds, Point? center)
    {
        var rotate = new RotateTransform();
        if (center.HasValue)
        {
            rotate.CenterX = center.Value.X;
            rotate.CenterY = center.Value.Y;
            element.RenderTransform = rotate;
        }
        else
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = rotate;
        }

        var animation = new DoubleAnimation(0, seconds > 0 ? 360 : -360,
            TimeSpan.FromSeconds(Math.Abs(seconds)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    // Colorful ring: 100-second period with a cubic-bezier(.01,.99,.28,.99) surge-and-pause pulse reversal.
    private static void RotatePulse(FrameworkElement element)
    {
        var rotate = new RotateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = rotate;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(100),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(-360,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(100)),
            new KeySpline(0.01, 0.99, 0.28, 0.99)));
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    // International yellow ring: oscillates between 0 and 90 degrees over 80 seconds, matching the site's linear alternate tilt.
    private static void RotateTilt(FrameworkElement element)
    {
        var rotate = new RotateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = rotate;

        var animation = new DoubleAnimation(0, 90, TimeSpan.FromSeconds(80))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private static Color FromHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);
}
