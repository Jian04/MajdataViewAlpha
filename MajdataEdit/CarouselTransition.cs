using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

internal static class CarouselTransition
{
    public static void Replace(Border host, UIElement next, int direction)
    {
        if (host.Child == null || direction == 0 || !host.IsLoaded) { host.Child = next; return; }
        host.UpdateLayout();
        var width = Math.Max(1, host.ActualWidth); var height = Math.Max(1, host.ActualHeight);
        // Freeze the outgoing frame: detaching a live preview restarts its Loaded/animation cycle.
        var dpi = VisualTreeHelper.GetDpi(host);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(host); bitmap.Freeze();
        var old = new Image { Source = bitmap, Stretch = Stretch.Fill };
        host.Child = null;
        var stage = new Grid { ClipToBounds = true, Tag = next };
        stage.Children.Add(old); stage.Children.Add(next); host.Child = stage;
        var distance = width * Math.Sign(direction);
        var duration = TimeSpan.FromMilliseconds(180);
        var outgoing = new TranslateTransform(); old.RenderTransform = outgoing;
        var incoming = new TranslateTransform(); next.RenderTransform = incoming;
        outgoing.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -distance, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var move = new DoubleAnimation(distance, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        move.Completed += (_, _) =>
        {
            if (!ReferenceEquals(host.Child, stage)) return;
            stage.Children.Remove(old); next.RenderTransform = Transform.Identity;
            // Keep the incoming visual attached: reparenting restarts its animation.
        };
        incoming.BeginAnimation(TranslateTransform.XProperty, move);
    }

    public static void Image(Image current, ImageSource? oldSource, int direction)
    {
        // The Image remains the setting panel's owned control after the slide.
        Border? host = current.Parent as Border;
        if (host == null && current.Parent is Grid stage && stage.Parent is Border border) host = border;
        if (host == null) return;
        if (current.Parent is Panel panel) panel.Children.Remove(current);
        else host.Child = null;
        host.Child = new Image { Source = oldSource, Width = current.Width, MaxWidth = current.MaxWidth, Height = current.Height,
            Margin = current.Margin, Stretch = current.Stretch,
            RenderTransform = new TranslateTransform(current.RenderTransform.Value.OffsetX, 0) };
        Replace(host, current, direction);
    }
}
