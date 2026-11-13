using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using MajdataEdit.Editor;

namespace MajdataEdit;

internal sealed class CodeOverview : Border
{
    private readonly OverviewMap map;
    private readonly TextEditor source;
    internal CodeOverview(TextEditor editor, Action close)
    {
        source = editor;
        Width = 220; VerticalAlignment = VerticalAlignment.Top;
        HorizontalAlignment = HorizontalAlignment.Right; CornerRadius = new CornerRadius(4);
        var content = new Grid();
        var button = new Button { Width = 22, Height = 22, ToolTip = "收起代码概览",
            Template = (ControlTemplate)FindResource("TabCloseButton") };
        button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        button.Click += (_, _) => close();
        button.HorizontalAlignment = HorizontalAlignment.Right; button.VerticalAlignment = VerticalAlignment.Top;
        map = new OverviewMap(editor); content.Children.Add(map); content.Children.Add(button); Child = content;
        editor.TextArea.TextView.SizeChanged += AlignToText;
        Loaded += AlignToText;
        ThemeManager.ThemeChanged += ApplyTheme; ApplyTheme();
    }
    private void ApplyTheme()
    {
        var brush = ((Brush)FindResource("EditorBackground")).Clone(); brush.Opacity = .5;
        Background = brush;
    }
    private void AlignToText(object sender, RoutedEventArgs e)
    {
        var view = source.TextArea.TextView;
        var at = view.TranslatePoint(new Point(), source);
        var horizontal = source.HorizontalScrollBarVisibility == ScrollBarVisibility.Visible || source.ExtentWidth > source.ViewportWidth;
        Height = Math.Max(0, Math.Min(view.ActualHeight, source.ActualHeight -
            (horizontal ? SystemParameters.HorizontalScrollBarHeight : 0)));
        Margin = new Thickness(0, at.Y, SystemParameters.VerticalScrollBarWidth, 0);
    }
    internal void Detach() { ThemeManager.ThemeChanged -= ApplyTheme; source.TextArea.TextView.SizeChanged -= AlignToText; map.Detach(); }

    private sealed class OverviewMap : Grid
    {
        private readonly TextEditor editor;
        private readonly TextEditor preview;
        private readonly Canvas viewport = new() { IsHitTestVisible = false };
        private readonly System.Windows.Shapes.Rectangle indicator = new();
        private readonly WrappedLineSpacing spacing = new();
        private bool syncQueued, syncing, navigating;
        private double zoom = 6;
        private bool updatingScroll;
        private readonly System.Windows.Controls.Primitives.ScrollBar scroll = new()
        { Orientation = Orientation.Vertical, Width = 12, HorizontalAlignment = HorizontalAlignment.Right };
        internal OverviewMap(TextEditor editor)
        {
            this.editor = editor; ClipToBounds = true;
            preview = new TextEditor { Document = editor.Document, IsReadOnly = true,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                ShowLineNumbers = false, Padding = new Thickness(4, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            preview.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
            preview.TextArea.TextView.LineTransformers.Add(new SimaiColorizer(preview));
            Children.Add(preview); viewport.Children.Add(indicator); Children.Add(viewport);
            Children.Add(scroll);
            scroll.ValueChanged += (_, _) => { if (!updatingScroll) { navigating = true; preview.ScrollToVerticalOffset(scroll.Value); } };
            indicator.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HelperForeground");
            indicator.StrokeThickness = 1;
            preview.PreviewMouseWheel += Wheel;
            preview.PreviewMouseLeftButtonDown += (_, e) => { navigating = true; Jump(e.GetPosition(preview)); preview.CaptureMouse(); e.Handled = true; };
            preview.PreviewMouseMove += (_, e) => { if (preview.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Jump(e.GetPosition(preview)); };
            preview.PreviewMouseLeftButtonUp += (_, e) => { if (preview.IsMouseCaptured) { preview.ReleaseMouseCapture(); if (!preview.IsMouseOver) { navigating = false; QueueSync(); } e.Handled = true; } };
            preview.MouseLeave += (_, _) => { if (!preview.IsMouseCaptured) { navigating = false; QueueSync(); } };
            editor.PreviewKeyDown += EditorKeyDown;
            preview.TextArea.TextView.ScrollOffsetChanged += PreviewScrolled;
            editor.DocumentChanged += DocumentChanged;
            editor.TextArea.TextView.ScrollOffsetChanged += EditorScrolled;
            editor.TextArea.TextView.VisualLinesChanged += EditorScrolled;
            editor.SizeChanged += EditorScrolled;
            ThemeManager.ThemeChanged += ThemeChanged;
            IsVisibleChanged += (_, _) => { if (IsVisible) QueueSync(); };
        }
        internal void Detach()
        {
            editor.DocumentChanged -= DocumentChanged;
            editor.PreviewKeyDown -= EditorKeyDown;
            editor.TextArea.TextView.ScrollOffsetChanged -= EditorScrolled;
            editor.TextArea.TextView.VisualLinesChanged -= EditorScrolled;
            editor.SizeChanged -= EditorScrolled;
            preview.TextArea.TextView.ScrollOffsetChanged -= PreviewScrolled;
            ThemeManager.ThemeChanged -= ThemeChanged;
        }
        private void DocumentChanged(object? sender, EventArgs e) { preview.Document = editor.Document; QueueSync(); }
        private void EditorScrolled(object? sender, EventArgs e) => QueueSync();
        private void EditorKeyDown(object sender, KeyEventArgs e) { navigating = false; QueueSync(); }
        private void PreviewScrolled(object? sender, EventArgs e) => UpdateIndicator();
        private void ThemeChanged() { preview.TextArea.TextView.Redraw(); QueueSync(); }
        private void QueueSync()
        {
            if (!IsVisible || syncQueued || syncing) return;
            syncQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { syncQueued = false; if (IsVisible) Sync(); }), DispatcherPriority.Render);
        }
        private void Sync()
        {
            syncing = true;
            try
            {
                var ratio = zoom / editor.FontSize;
                preview.FontFamily = editor.FontFamily; preview.FontSize = zoom;
                preview.WordWrap = editor.WordWrap;
                var generators = preview.TextArea.TextView.ElementGenerators;
                if (editor.WordWrap && !generators.Contains(spacing)) generators.Add(spacing);
                if (!editor.WordWrap) generators.Remove(spacing);
                preview.Width = Math.Max(40, editor.TextArea.TextView.ActualWidth * ratio + 4);
                preview.UpdateLayout();
                if (!navigating && TryVisibleBounds(out var top, out var bottom))
                {
                    var context = Math.Max(0, (preview.ViewportHeight - (bottom - top)) / 2);
                    preview.ScrollToVerticalOffset(Math.Max(0, top - context));
                    preview.ScrollToHorizontalOffset(editor.HorizontalOffset * ratio);
                }
                UpdateIndicator();
            }
            finally { syncing = false; }
        }
        private void UpdateIndicator()
        {
            updatingScroll = true;
            scroll.Maximum = Math.Max(0, preview.ExtentHeight - preview.ViewportHeight);
            scroll.ViewportSize = preview.ViewportHeight;
            scroll.LargeChange = preview.ViewportHeight;
            scroll.SmallChange = preview.TextArea.TextView.DefaultLineHeight;
            scroll.Value = preview.VerticalOffset;
            updatingScroll = false;
            if (!TryVisibleBounds(out var top, out var bottom)) return;
            top -= preview.VerticalOffset; bottom -= preview.VerticalOffset;
            var clippedTop = Math.Clamp(top, 0, ActualHeight);
            Canvas.SetTop(indicator, clippedTop); Canvas.SetLeft(indicator, 1);
            indicator.Width = Math.Max(0, ActualWidth - 2);
            indicator.Height = Math.Max(2, Math.Min(ActualHeight, bottom) - clippedTop);
        }
        private bool TryVisibleBounds(out double top, out double bottom)
        {
            top = bottom = 0;
            var view = editor.TextArea.TextView;
            if (!view.VisualLinesValid || !preview.TextArea.TextView.VisualLinesValid) return false;
            var first = view.GetPosition(new Point(editor.HorizontalOffset, editor.VerticalOffset));
            var last = view.GetPosition(new Point(editor.HorizontalOffset, editor.VerticalOffset + Math.Max(0, view.ActualHeight - 1)));
            if (!first.HasValue || !last.HasValue) return false;
            top = preview.TextArea.TextView.GetVisualPosition(first.Value, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop).Y;
            bottom = preview.TextArea.TextView.GetVisualPosition(last.Value, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineBottom).Y;
            return true;
        }
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) { navigating = true; return; }
            zoom = Math.Clamp(zoom + Math.Sign(e.Delta) * .5, 2, 12);
            Sync(); e.Handled = true;
        }
        private void Jump(Point point)
        {
            var position = preview.GetPositionFromPoint(point);
            if (!position.HasValue) return;
            var line = editor.Document.GetLineByNumber(position.Value.Line);
            editor.CaretOffset = line.Offset + Math.Min(line.Length, position.Value.Column - 1);
            editor.ScrollToLine(line.LineNumber); editor.Focus(); UpdateIndicator();
        }
    }
}
