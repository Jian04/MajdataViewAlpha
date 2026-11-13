using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MajdataEdit;

public partial class EditorSettingPanel
{
    private const double PreviewStarSize = 48;
    private int starDisplayMode = 1;
    private Border? starOrderHost;
    private TextBlock? starOrderSelection;

    private DrawingImage SlideSamples(string skin, bool head = true, string file = "slide.png")
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 224, 64));
            var path = PreviewPath(skin, file);
            if (File.Exists(path))
            {
                var image = LoadPreviewImage(path, true);
                var width = 24 * image.Width / image.Height;
                var step = width * .6;
                for (var x = 36d; x + width / 2 <= 220; x += step)
                {
                    context.PushTransform(new RotateTransform(180, x, 32));
                    context.DrawImage(image, new Rect(x - width / 2, 20, width, 24));
                    context.Pop();
                }
            }
            if (head)
            {
                context.DrawDrawing(StarDrawing(GetSkinGroup("star", "dx"), pinkStar ? "star_pink.png" : "star.png", 26));
            }
        }
        var result = new DrawingImage(drawing);
        if (!head) result.Freeze();
        return result;
    }

    private DrawingGroup StarDrawing(string skin, string file, double centerX, bool horizontal = false)
    {
        var drawing = new DrawingGroup { Transform = new RotateTransform(horizontal ? 90 : 0, centerX, 32) };
        var path = PreviewPath(skin, file);
        if (File.Exists(path))
        {
            var source = LoadPreviewImage(path, !horizontal);
            if (horizontal)
            {
                // Like a sliced Hold body: retain both caps and stretch only the middle.
                const double width = 36, length = 164;
                // CustomSkin.LoadSkin uses 58-pixel borders for the native Hold sprite.
                var capPixels = Math.Min(58, (source.PixelHeight - 1) / 2);
                var capHeight = capPixels * width / source.PixelWidth;
                var top = 32 - length / 2;
                void Slice(int y, int pixels, double destinationY, double height) =>
                    drawing.Children.Add(new ImageDrawing(new CroppedBitmap(source,
                        new Int32Rect(0, y, source.PixelWidth, pixels)),
                        new Rect(centerX - width / 2, destinationY, width, height)));
                Slice(0, capPixels, top, capHeight);
                Slice(capPixels, source.PixelHeight - 2 * capPixels, top + capHeight, length - 2 * capHeight);
                Slice(source.PixelHeight - capPixels, capPixels, top + length - capHeight, capHeight);
            }
            else
            {
                var scale = PreviewStarSize / Math.Max(source.Width, source.Height);
                var width = source.Width * scale; var height = source.Height * scale;
                drawing.Children.Add(new ImageDrawing(source, new Rect(centerX - width / 2, 32 - height / 2, width, height)));
            }
        }
        return drawing;
    }

    private DrawingImage StarSample(string skin, string file, bool horizontal = false)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 224, 64))));
        drawing.Children.Add(StarDrawing(skin, file, 112, horizontal));
        return new DrawingImage(drawing);
    }

    private FrameworkElement BuildStarOrderCard()
    {
        starOrderHost = new Border { Height = 66, ClipToBounds = true };
        var arrows = new List<Button>();
        foreach (var direction in new[] { -1, 1 })
        {
            var arrow = new Button { Content = direction < 0 ? "‹" : "›" };
            arrow.Click += (_, _) =>
            {
                starDisplayMode = 1 - starDisplayMode;
                UpdateStarOrderPreview(direction);
            };
            arrows.Add(arrow);
        }
        starOrderSelection = new TextBlock();
        var row = VisualStylePicker.CreateRow(new TextBlock { Text = "Slide 出现样式" }, starOrderSelection,
            VisualStylePicker.CreatePicker(starOrderHost, arrows[0], arrows[1]));
        UpdateStarOrderPreview();
        return row;
    }

    private void UpdateStarOrderPreview(int direction = 0)
    {
        if (starOrderHost == null || starOrderSelection == null) return;
        starOrderSelection.Text = starDisplayMode == 1 ? "新版（先出在上）" : "旧版（后出在上）";
        CarouselTransition.Replace(starOrderHost, BuildSlidePreview(true), direction);
    }

    private FrameworkElement BuildSlidePreview(bool showSecond)
    {
        var canvas = new Canvas { Width = 224, Height = 64, ClipToBounds = true };
        var headSkin = GetSkinGroup("star", "dx");
        var slideSkin = GetSkinGroup("slide", "dx");
        var blue = Lane(false, 0, starDisplayMode == 1 ? 1 : 0);
        var red = Lane(true, .75, starDisplayMode == 1 ? 0 : 1);
        canvas.Children.Add(blue);
        if (showSecond) canvas.Children.Add(red);
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        return new Viewbox { MaxWidth = 224, Height = 64, Child = canvas, Stretch = Stretch.Uniform };

        Canvas Lane(bool isBreak, double delay, int layer)
        {
            var lane = new Canvas { Width = 224, Height = 64, Opacity = 0 };
            Panel.SetZIndex(lane, layer);
            var trail = new Image { Width = 224, Height = 64, Stretch = Stretch.Fill,
                Source = SlideSamples(slideSkin, false, isBreak ? "slide_break.png" : "slide.png") };
            var clip = new RectangleGeometry(new Rect(0, 0, 224, 64));
            trail.Clip = clip;
            lane.Children.Add(trail);
            var path = PreviewPath(headSkin, isBreak ? "star_break.png" : pinkStar ? "star_pink.png" : "star.png");
            var image = new Image { Width = PreviewStarSize, Height = PreviewStarSize, Stretch = Stretch.Uniform,
                Source = File.Exists(path) ? LoadPreviewImage(path, true) : null };
            Canvas.SetLeft(image, 2); Canvas.SetTop(image, 8);
            lane.Children.Add(image);
            var move = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.4), RepeatBehavior = RepeatBehavior.Forever };
            move.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            move.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            move.KeyFrames.Add(new LinearDoubleKeyFrame(174, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
            move.KeyFrames.Add(new DiscreteDoubleKeyFrame(174, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
            var translate = new TranslateTransform();
            var rotate = new RotateTransform(0, PreviewStarSize / 2, PreviewStarSize / 2);
            image.RenderTransform = new TransformGroup { Children = { rotate, translate } };
            var erase = new RectAnimationUsingKeyFrames { Duration = move.Duration, RepeatBehavior = RepeatBehavior.Forever };
            erase.KeyFrames.Add(new DiscreteRectKeyFrame(new Rect(0, 0, 224, 64), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            erase.KeyFrames.Add(new LinearRectKeyFrame(new Rect(0, 0, 224, 64), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            erase.KeyFrames.Add(new LinearRectKeyFrame(new Rect(200, 0, 24, 64), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
            var appear = new DoubleAnimationUsingKeyFrames { Duration = move.Duration, RepeatBehavior = RepeatBehavior.Forever };
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            appear.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay))));
            appear.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + .18))));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
            appear.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            lane.IsVisibleChanged += (_, _) =>
            {
                lane.BeginAnimation(OpacityProperty, lane.IsVisible ? appear : null);
                translate.BeginAnimation(TranslateTransform.XProperty, lane.IsVisible ? move : null);
                clip.BeginAnimation(RectangleGeometry.RectProperty, lane.IsVisible ? erase : null);
                rotate.BeginAnimation(RotateTransform.AngleProperty, lane.IsVisible
                    ? new DoubleAnimation(0, -360, TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever }
                    : null);
            };
            return lane;
        }
    }
}
