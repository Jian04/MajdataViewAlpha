using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MajdataEdit;

internal sealed partial class ChartListPanel
{
    private readonly Grid dragSurface = new() { ClipToBounds = true };
    private readonly Canvas dragLayer = new() { IsHitTestVisible = false };
    private readonly ScrollViewer listScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly DispatcherTimer dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private Border? draggedCard, dragGap;
    private Border? pendingCard;
    private Point pendingStart;
    private Point dragOffset;
    private double savedCardWidth;

    private void InitializeDragSurface(DockPanel root)
    {
        listScroll.Content = rows;
        dragSurface.Children.Add(listScroll);
        dragSurface.Children.Add(dragLayer);
        root.Children.Add(dragSurface);
        dragSurface.PreviewMouseMove += (_, e) =>
        {
            var point = e.GetPosition(dragSurface);
            if (draggedCard != null) { UpdateDrag(point); return; }
            if (pendingCard == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { pendingCard = null; return; }
            if ((point - pendingStart).Length < SystemParameters.MinimumVerticalDragDistance) return;
            BeginDrag(pendingCard, point); e.Handled = true;
        };
        dragSurface.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (draggedCard == null) return;
            var p = e.GetPosition(dragSurface);
            FinishDrag(p.X >= 0 && p.X <= dragSurface.ActualWidth && p.Y >= 0 && p.Y <= dragSurface.ActualHeight);
            e.Handled = true;
        };
        dragSurface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || draggedCard == null) return;
            FinishDrag(false); e.Handled = true;
        };
        dragSurface.LostMouseCapture += (_, _) =>
        {
            if (draggedCard != null && Mouse.Captured != dragSurface) FinishDrag(false);
        };
        dragSurface.Unloaded += (_, _) => FinishDrag(false);
        dragScrollTimer.Tick += (_, _) =>
        {
            if (draggedCard == null) return;
            var p = Mouse.GetPosition(dragSurface);
            double delta = p.Y < 36 ? -12 : p.Y > dragSurface.ActualHeight - 36 ? 12 : 0;
            if (delta == 0) return;
            listScroll.ScrollToVerticalOffset(listScroll.VerticalOffset + delta);
            listScroll.UpdateLayout(); UpdateDrag(p);
        };
    }

    private void AttachReorderHandle(Border frame, Button remove, Action open)
    {
        frame.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pendingCard = remove.IsMouseOver ? null : frame;
            pendingStart = e.GetPosition(dragSurface);
            if (pendingCard != null)
            {
                Mouse.Capture(frame, CaptureMode.Element);
                e.Handled = true;
            }
        };
        frame.MouseLeftButtonUp += (_, e) =>
        {
            if (pendingCard != frame) return;
            pendingCard = null;
            if (Mouse.Captured == frame) Mouse.Capture(null);
            if (frame.IsMouseOver) open();
            e.Handled = true;
        };
    }

    private void BeginDrag(Border frame, Point pointer)
    {
            SetStatus("正在调整顺序…");
            dragOffset = dragSurface.TranslatePoint(pendingStart, frame);
            pendingCard = null;
            savedCardWidth = frame.Width;
            var width = frame.ActualWidth;
            var index = rows.Children.IndexOf(frame);
            dragGap = new Border { Height = frame.ActualHeight, Margin = frame.Margin,
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Opacity = 0.35 };
            dragGap.SetResourceReference(Border.BorderBrushProperty, "HelperForeground");
            dragGap.SetResourceReference(Border.BackgroundProperty, "ButtonsBackground");
            rows.Children.Remove(frame); rows.Children.Insert(index, dragGap);
            // Layout the placeholder before asking LayoutInformation for its slot.
            // Until this pass it reports Y=0 even when the card came from below.
            rows.UpdateLayout();
            draggedCard = frame; frame.Width = width; frame.Opacity = 0.88;
            frame.IsHitTestVisible = false; dragLayer.Children.Add(frame);
            dragSurface.Focusable = true; dragSurface.Focus();
            Mouse.Capture(dragSurface, CaptureMode.Element);
            dragScrollTimer.Start(); UpdateDrag(pointer);
    }

    private void UpdateDrag(Point pointer)
    {
        if (draggedCard == null || dragGap == null) return;
        Canvas.SetLeft(draggedCard, Math.Clamp(pointer.X - dragOffset.X, 0,
            Math.Max(0, dragSurface.ActualWidth - draggedCard.ActualWidth - 16)));
        Canvas.SetTop(draggedCard, pointer.Y - dragOffset.Y);
        var cards = rows.Children.OfType<Border>().Where(x => x != dragGap).ToArray();
        var positions = cards.ToDictionary(x => x, x => x.TranslatePoint(new Point(), rows).Y);
        var pointerY = dragSurface.TranslatePoint(pointer, rows).Y;
        var destination = rows.Children.IndexOf(dragGap);
        var gapTop = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(dragGap).Y;
        if (pointerY > gapTop + dragGap.ActualHeight)
        {
            while (destination < cards.Length)
            {
                var next = (Border)rows.Children[destination + 1];
                var nextTop = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(next).Y;
                if (pointerY < nextTop + Math.Min(next.ActualHeight * 0.25, 32)) break;
                destination++;
            }
        }
        else if (pointerY < gapTop)
        {
            while (destination > 0)
            {
                var previous = (Border)rows.Children[destination - 1];
                var previousTop = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(previous).Y;
                if (pointerY > previousTop + previous.ActualHeight - Math.Min(previous.ActualHeight * 0.25, 32)) break;
                destination--;
            }
        }
        if (rows.Children.IndexOf(dragGap) == destination) return;
        rows.Children.Remove(dragGap); rows.Children.Insert(destination, dragGap);
        rows.UpdateLayout();
        foreach (var card in cards)
        {
            var delta = positions[card] - card.TranslatePoint(new Point(), rows).Y;
            var shift = new TranslateTransform(); card.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(delta, 0,
                TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private void FinishDrag(bool commit)
    {
        if (draggedCard == null || dragGap == null) return;
        dragScrollTimer.Stop();
        var card = draggedCard; var gap = dragGap;
        draggedCard = null; dragGap = null;
        var index = rows.Children.IndexOf(gap);
        var next = rows.Children.OfType<Border>().Skip(index + 1).Select(x => x.Tag).OfType<ChartListEntry>().FirstOrDefault();
        dragLayer.Children.Remove(card); rows.Children.Remove(gap);
        card.Width = savedCardWidth; card.Opacity = 1; card.IsHitTestVisible = true;
        Mouse.Capture(null);
        try
        {
            if (commit && card.Tag is ChartListEntry entry)
            {
                ChartListStore.Update(list =>
                {
                    var source = list.FindIndex(x => ChartListStore.Same(x, entry));
                    if (source < 0) return;
                    var moved = list[source]; list.RemoveAt(source);
                    var target = next == null ? -1 : list.FindIndex(x => ChartListStore.Same(x, next));
                    list.Insert(target < 0 ? list.Count : target, moved);
                });
                SetStatus("顺序已保存。");
            }
            else SetStatus("");
        }
        catch (Exception ex) { SetStatus("排序未保存：" + ex.Message); }
        Refresh();
    }
}
