using System.Windows;
using System.Windows.Controls;

namespace MajdataEdit;

// Shrink tab captions before scrolling. Twenty-eight pixels retain the close
// button; only when even those buttons no longer fit does the strip overflow.
public sealed class SidebarTabPanel : Panel
{
    private static double PreferredTabWidth => Math.Ceiling(new System.Windows.Media.FormattedText(
        "Edit 设置", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new System.Windows.Media.Typeface(App.UiFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        12, System.Windows.Media.Brushes.White, 1).Width) + 28;
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(SidebarTabPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }

    public static readonly DependencyProperty WidthMultiplierProperty = DependencyProperty.RegisterAttached(
        "WidthMultiplier", typeof(double), typeof(SidebarTabPanel), new FrameworkPropertyMetadata(1d,
            FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static void SetWidthMultiplier(UIElement tab, double value) => tab.SetValue(WidthMultiplierProperty, value);
    public static double GetWidthMultiplier(UIElement tab) => (double)tab.GetValue(WidthMultiplierProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Cast<UIElement>().Count(child => child.Visibility != Visibility.Collapsed);
        if (count == 0) return new Size();
        var preferred = PreferredTabWidth;
        var scale = Math.Min(1, ViewportWidth / (preferred * InternalChildren.Cast<UIElement>()
            .Where(child => child.Visibility != Visibility.Collapsed).Sum(GetWidthMultiplier)));
        var total = 0d;
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed)
            {
                var width = Math.Max(28, preferred * GetWidthMultiplier(child) * scale);
                child.Measure(new Size(width, 30));
                total += width;
            }
        return new Size(total, 30);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Cast<UIElement>().Count(child => child.Visibility != Visibility.Collapsed);
        if (count == 0) return finalSize;
        var preferred = PreferredTabWidth;
        var scale = Math.Min(1, ViewportWidth / (preferred * InternalChildren.Cast<UIElement>()
            .Where(child => child.Visibility != Visibility.Collapsed).Sum(GetWidthMultiplier)));
        var left = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var width = Math.Max(28, preferred * GetWidthMultiplier(child) * scale);
            child.Arrange(new Rect(left, 0, width, finalSize.Height)); left += width;
        }
        return finalSize;
    }
}
