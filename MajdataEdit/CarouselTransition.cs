using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MajdataEdit;

internal static class CarouselTransition
{
    public static void Replace(Border host, UIElement next, int direction)
    {
        var old = host.Child;
        host.Child = null;
        if (old == null || direction == 0 || !host.IsLoaded) { host.Child = next; return; }
        var stage = new Grid { ClipToBounds = true };
        stage.Children.Add(old); stage.Children.Add(next); host.Child = stage;
        var distance = Math.Max(80, host.ActualWidth) * Math.Sign(direction);
        var duration = TimeSpan.FromMilliseconds(230);
        var outgoing = new TranslateTransform(); old.RenderTransform = outgoing;
        var incoming = new TranslateTransform(); next.RenderTransform = incoming;
        outgoing.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -distance, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var move = new DoubleAnimation(distance, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        move.Completed += (_, _) =>
        {
            if (!ReferenceEquals(host.Child, stage)) return;
            stage.Children.Clear(); next.RenderTransform = Transform.Identity; host.Child = next;
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
        host.Child = new Image { Source = oldSource, Width = current.Width, Height = current.Height,
            Margin = current.Margin, Stretch = current.Stretch };
        Replace(host, current, direction);
    }
}
