using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MajdataSessionUi;

namespace MajdataEdit;

public partial class MainWindow
{
    private readonly Dictionary<Type, (Window Window, TabItem Tab)> toolTabs = new();
    private readonly Dictionary<Type, Window> detachedTools = new();
    private TabControl? toolSidebar;
    private ColumnDefinition? toolSidebarColumn;
    private GridSplitter? toolSidebarSplitter;
    private Grid? editorSurface;
    private Grid? toolSidebarLayout;
    private bool sidebarExpanded;
    private double sidebarRatio = 0.425;
    private ColumnDefinition? editorColumn;
    private readonly Dictionary<ContentElement, PaneSizeAnimation> paneAnimations = new();

    private void EnsureToolSidebar()
    {
        if (toolSidebar != null) return;
        var editor = (Grid)Content; Content = null;
        editorSurface = editor;
        editor.Children.Remove(MainMenu);
        editor.Children.Remove(SidebarExpandButton);
        editor.Children.Remove(SidebarToggleButton);
        var layout = new Grid { Background = SessionTheme.Brush("WindowBackground", "#20242B"), ClipToBounds = true };
        layout.SetResourceReference(BackgroundProperty, "WindowBackground");
        layout.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "UiFontFamily");
        layout.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13d);
        TextOptions.SetTextFormattingMode(layout, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(layout, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(layout, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(layout, ClearTypeHint.Enabled);
        layout.SnapsToDevicePixels = true;
        toolSidebarLayout = layout;
        chartFilesColumn = new ColumnDefinition { Width = new GridLength(0) };
        layout.ColumnDefinitions.Add(chartFilesColumn);
        editorColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 };
        layout.ColumnDefinitions.Add(editorColumn);
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolSidebarColumn = new ColumnDefinition { Width = new GridLength(0) }; layout.ColumnDefinitions.Add(toolSidebarColumn);
        var editorHost = CreateEditorPanes(editor);
        Grid.SetColumn(editorHost, 1); layout.Children.Add(editorHost);
        layout.Children.Add(CreateChartFilesPane());
        // The file pane uses its own column; the existing splitter still
        // separates the editor from the right tool pages.
        toolSidebarSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        toolSidebarSplitter.FocusVisualStyle = null;
        toolSidebarSplitter.Margin = new Thickness(-5, 24, 0, 0);
        toolSidebarSplitter.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridSplitter'><Border Background='Transparent'/></ControlTemplate>");
        toolSidebarSplitter.DragCompleted += (_, _) =>
        {
            var total = editorColumn.ActualWidth + toolSidebarColumn.ActualWidth;
            if (total > 0) sidebarRatio = toolSidebarColumn.ActualWidth / total;
        };
        Grid.SetColumn(toolSidebarSplitter, 2); layout.Children.Add(toolSidebarSplitter);
        chartFilesSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 24, 0, 0),
            ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.CurrentAndNext,
            Background = Brushes.Transparent, Visibility = Visibility.Collapsed, FocusVisualStyle = null,
            Template = toolSidebarSplitter.Template };
        chartFilesSplitter.DragStarted += (_, _) =>
        {
            StopPaneSizeAnimation(chartFilesPane!, chartFilesColumn, ColumnDefinition.WidthProperty);
            chartFilesColumn.MinWidth = 140;
        };
        chartFilesSplitter.DragCompleted += (_, _) => chartFilesWidth = chartFilesColumn.ActualWidth;
        Panel.SetZIndex(chartFilesSplitter, 2);
        layout.Children.Add(chartFilesSplitter);
        toolSidebar = CreateSidebarTabs();
        Grid.SetColumn(toolSidebar, 3); layout.Children.Add(toolSidebar);
        Grid.SetRow(MainMenu, 0); Grid.SetRowSpan(MainMenu, 1); Grid.SetColumn(MainMenu, 0); Grid.SetColumnSpan(MainMenu, 4);
        Grid.SetRow(SidebarExpandButton, 0); Grid.SetRowSpan(SidebarExpandButton, 1); Grid.SetColumn(SidebarExpandButton, 0); Grid.SetColumnSpan(SidebarExpandButton, 4);
        Grid.SetRow(SidebarToggleButton, 0); Grid.SetRowSpan(SidebarToggleButton, 1); Grid.SetColumn(SidebarToggleButton, 0); Grid.SetColumnSpan(SidebarToggleButton, 4);
        layout.Children.Add(MainMenu); layout.Children.Add(SidebarExpandButton); layout.Children.Add(SidebarToggleButton); Content = layout;
        AddPaneButtons(layout);
        dockedPetButton = CreatePetButton();
        layout.Children.Add(dockedPetButton);
        UpdatePaneToolbar();
        layout.SizeChanged += (_, _) =>
        {
            toolSidebarColumn.MaxWidth = sidebarExpanded ? double.PositiveInfinity : Math.Max(280, layout.ActualWidth - 265);
            chartFilesColumn.MaxWidth = Math.Max(140, layout.ActualWidth - 260 -
                (toolSidebar.Visibility == Visibility.Visible && !sidebarExpanded ? 285 : 0));
        };
        Closed += (_, _) =>
        {
            detachedChartFiles?.Close();
            foreach (var entry in toolTabs.Values.ToArray()) entry.Window.Close();
        };
    }

    private static TabControl CreateSidebarTabs()
    {
        var sidebar = new TabControl { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0),
            FontFamily = (FontFamily)Application.Current.FindResource("UiFontFamily"), FontSize = 13,
            FocusVisualStyle = null,
            Margin = new Thickness(0, 24, 0, 0),
            Background = SessionTheme.Brush("WindowBackground", "#20242B"), Foreground = SessionTheme.Brush("ButtonForeground", "#F4F7FA") };
        sidebar.SetResourceReference(BackgroundProperty, "PanelBackground");
        sidebar.SetResourceReference(ForegroundProperty, "ButtonForeground");
        sidebar.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:local='clr-namespace:MajdataEdit;assembly=MajdataEdit' TargetType='TabControl'>
 <Border Background='{TemplateBinding Background}' BorderBrush='{DynamicResource MenuSeparator}' BorderThickness='1,0,1,0'>
  <DockPanel>
   <ScrollViewer DockPanel.Dock='Top' HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled' CanContentScroll='False' Background='{DynamicResource SidebarHeaderBackground}'>
    <local:SidebarTabPanel IsItemsHost='True' ViewportWidth='{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ScrollViewer}}'/>
   </ScrollViewer>
   <ContentPresenter ContentSource='SelectedContent' Margin='0' HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/>
  </DockPanel>
 </Border>
</ControlTemplate>");
        var itemStyle = new Style(typeof(TabItem));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ButtonForeground")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabItem'>
 <Grid Height='30' SnapsToDevicePixels='True'>
  <Border Name='tab' Background='{TemplateBinding Background}' BorderThickness='0' Padding='4,0' ClipToBounds='True'>
   <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
  </Border>
  <Border Name='activeLine' Height='2' VerticalAlignment='Bottom' Background='{DynamicResource AccentBrush}' Visibility='Collapsed'/>
 </Grid>
 <ControlTemplate.Triggers>
  <Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource PanelBackground}'/><Setter TargetName='activeLine' Property='Visibility' Value='Visible'/></Trigger>
  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource HoverBackground}'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>")));
        sidebar.ItemContainerStyle = itemStyle;
        return sidebar;
    }
    private bool FocusTool<T>() where T : Window
    {
        if (!toolTabs.TryGetValue(typeof(T), out var existing)) return false;
        if (detachedTools.TryGetValue(typeof(T), out var floating))
        {
            floating.Activate();
            return true;
        }
        toolSidebar!.SelectedItem = existing.Tab;
        SetSidebarVisible(true);
        return true;
    }
    private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleToolSidebar();
    private void SidebarExpand_Click(object sender, RoutedEventArgs e)
    {
        EnsureToolSidebar();
        if (toolTabs.Count == 0)
            AlphaSyntaxHelp_MenuItem_Click(this, new RoutedEventArgs());
        sidebarExpanded = !sidebarExpanded;
        SetSidebarVisible(true);
    }
    private void SidebarKeyDown(object sender, KeyEventArgs e)
    {
        if (ShortcutMatches(e, editorSetting?.SidebarKey))
        { ToggleToolSidebar(); e.Handled = true; }
    }
    private void ToggleToolSidebar()
    {
        EnsureToolSidebar();
        if (toolTabs.Count == 0) { AlphaSyntaxHelp_MenuItem_Click(this, new RoutedEventArgs()); return; }
        SetSidebarVisible(SidebarToggleButton.Tag is not true);
    }
    private void SetSidebarVisible(bool visible, bool animate = true)
    {
        if (toolSidebar == null || toolSidebarColumn == null || toolSidebarSplitter == null ||
            editorColumn == null || editorSurface == null || toolSidebarLayout == null) return;
        var currentWidth = toolSidebarColumn.ActualWidth;
        var opening = visible && SidebarToggleButton.Tag is not true;
        StopPaneSizeAnimation(toolSidebar, toolSidebarColumn, ColumnDefinition.WidthProperty);
        if (!visible && animate && toolSidebar.Visibility == Visibility.Visible)
        {
            SidebarToggleButton.Tag = false;
            // Release the editor immediately so it expands alongside the closing pane.
            ((FrameworkElement)editorSurface.Parent).Visibility = Visibility.Visible;
            editorColumn.MinWidth = 0;
            editorColumn.Width = new GridLength(1, GridUnitType.Star);
            toolSidebarColumn.MinWidth = 0;
            toolSidebarColumn.Width = new GridLength(currentWidth);
            AnimatePaneSize(toolSidebar, toolSidebarColumn, ColumnDefinition.WidthProperty,
                currentWidth, 0, () => SetSidebarVisible(false, false));
            return;
        }
        if (!visible) sidebarExpanded = false;
        var full = visible && sidebarExpanded;
        if (chartFilesColumn != null && chartFilesPane != null)
        {
            chartFilesColumn.MinWidth = chartFilesVisible && !full ? 140 : 0;
            chartFilesColumn.Width = chartFilesVisible && !full ? new GridLength(chartFilesWidth) : new GridLength(0);
            chartFilesPane.Visibility = chartFilesVisible && !full ? Visibility.Visible : Visibility.Collapsed;
            chartFilesSplitter!.Visibility = chartFilesPane.Visibility;
        }
        toolSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        toolSidebarSplitter.Visibility = visible && !full ? Visibility.Visible : Visibility.Collapsed;
        ((FrameworkElement)editorSurface.Parent).Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        editorColumn.MinWidth = full ? 0 : 260;
        editorColumn.Width = full ? new GridLength(0) :
            new GridLength(visible ? 1 - sidebarRatio : 1, GridUnitType.Star);
        toolSidebarColumn.MinWidth = visible && !full ? 280 : 0;
        toolSidebarColumn.MaxWidth = full ? double.PositiveInfinity :
            Math.Max(280, toolSidebarLayout.ActualWidth - 265);
        toolSidebarColumn.Width = !visible ? new GridLength(0) :
            full ? new GridLength(1, GridUnitType.Star) :
            new GridLength(sidebarRatio, GridUnitType.Star);
        if (opening && animate && !full)
        {
            var available = toolSidebarLayout.ActualWidth - (chartFilesVisible ? chartFilesWidth : 0);
            var targetWidth = Math.Clamp(available * sidebarRatio, 280, Math.Max(280, available - 260));
            toolSidebarColumn.MinWidth = 0;
            AnimatePaneSize(toolSidebar, toolSidebarColumn, ColumnDefinition.WidthProperty,
                currentWidth, targetWidth, () => toolSidebarColumn.MinWidth = 280);
        }
        SidebarToggleButton.Tag = visible;
        SidebarExpandButton.Tag = full;
        SidebarExpandButton.ToolTip = full ? "还原侧边栏宽度" : "侧边栏占满编辑器";
        UpdatePaneToolbar();
    }

    private static void AnimatePaneOpen(FrameworkElement pane)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var transition = new TranslateTransform();
        pane.RenderTransform = transition;
        pane.BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, duration)
        { FillBehavior = FillBehavior.Stop });
        transition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(8d, 0d, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    private void StopPaneSizeAnimation(FrameworkElement pane, ContentElement definition, DependencyProperty property)
    {
        var wasAnimating = paneAnimations.Remove(definition);
        definition.BeginAnimation(property, null);
        pane.Width = pane.Height = double.NaN;
        pane.HorizontalAlignment = HorizontalAlignment.Stretch;
        pane.VerticalAlignment = VerticalAlignment.Stretch;
        if (wasAnimating)
        {
            QueueWaveResize();
        }
    }

    private void AnimatePaneSize(FrameworkElement pane, ContentElement definition,
        DependencyProperty property, double from, double to, Action completed)
    {
        StopPaneSizeAnimation(pane, definition, property);
        // Keep the page at its final size while revealing it, instead of reflowing
        // every label on each animation frame. The outer grid clips the excess.
        var horizontal = property == ColumnDefinition.WidthProperty;
        pane.BeginAnimation(OpacityProperty, null);
        pane.RenderTransform = Transform.Identity;
        if (horizontal)
        {
            pane.Width = Math.Max(from, to);
            pane.HorizontalAlignment = Grid.GetColumn(pane) == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        else
        {
            pane.Height = Math.Max(from, to);
            pane.VerticalAlignment = VerticalAlignment.Top;
        }
        var destination = definition.GetValue(property);
        definition.SetValue(property, new GridLength(from));
        // Prepare the page's layout before starting the animation clock.
        pane.UpdateLayout();
        var animation = new PaneSizeAnimation(from, to) { Duration = TimeSpan.FromMilliseconds(160) };
        paneAnimations[definition] = animation;
        animation.Completed += (_, _) =>
        {
            // A completion already queued by WPF must not close a reopened pane.
            if (!paneAnimations.TryGetValue(definition, out var current) || current != animation) return;
            StopPaneSizeAnimation(pane, definition, property);
            completed();
        };
        pane.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!paneAnimations.TryGetValue(definition, out var current) || current != animation) return;
            definition.SetValue(property, destination);
            definition.BeginAnimation(property, animation);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private sealed class PaneSizeAnimation : AnimationTimeline
    {
        private readonly double from;
        private readonly double to;
        public PaneSizeAnimation(double from, double to) { this.from = from; this.to = to; }
        public override Type TargetPropertyType => typeof(GridLength);
        protected override Freezable CreateInstanceCore() => new PaneSizeAnimation(from, to);
        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
        {
            var progress = clock.CurrentProgress ?? 0;
            return new GridLength(from + (to - from) * (1 - Math.Pow(1 - progress, 3)));
        }
    }
    internal Window ShowTool(Window window, string? title = null)
    {
        EnsureToolSidebar();
        if (toolTabs.TryGetValue(window.GetType(), out var existing))
        {
            if (detachedTools.TryGetValue(window.GetType(), out var floating))
            {
                floating.Activate();
                return existing.Window;
            }
            toolSidebar!.SelectedItem = existing.Tab; SetSidebarVisible(true);
            return existing.Window;
        }
        window.Owner = this;
        var content = window.Content as FrameworkElement ?? throw new InvalidOperationException("工具没有可显示的内容。");
        window.Content = null;
        if (content.DataContext == null) content.DataContext = window.DataContext;
        foreach (var key in window.Resources.Keys) content.Resources[key] = window.Resources[key];
        content.Width = double.NaN; content.Height = double.NaN; content.MinWidth = 0; content.MinHeight = 0;
        content.MaxWidth = content.MaxHeight = double.PositiveInfinity;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Stretch;
        content.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, (FontFamily)Application.Current.FindResource("UiFontFamily"));
        content.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13d);
        if (window is MultiControlWindow control) control.UseSidebarLayout();
        if (window is AlphaSyntaxHelp help) help.UseSidebarLayout(content);
        if (window is SoundSetting mixer && content is Grid mixerGrid)
            content = mixer.CreateSidebarContent(mixerGrid);
        if (window is RecordVideoWindow && content is Grid recordGrid)
        {
            foreach (var row in recordGrid.RowDefinitions) row.Height = GridLength.Auto;
            foreach (var scroll in recordGrid.Children.OfType<ScrollViewer>().ToArray())
            {
                var child = (UIElement)scroll.Content; scroll.Content = null;
                Grid.SetRow(child, Grid.GetRow(scroll)); recordGrid.Children.Remove(scroll); recordGrid.Children.Add(child);
            }
            content = new ScrollViewer { Content = recordGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        if (window is EditorSettingPanel or RecordVideoWindow or Infomation)
            NormalizeToolControls(content);
        var tab = CreateSidebarTab(title ?? window.Title, content, window.Close,
            (item, point) => DetachTool(window, item, point));
        toolTabs.Add(window.GetType(), (window, tab)); toolSidebar!.Items.Add(tab);
        window.Closed += (_, _) =>
        {
            if (detachedTools.Remove(window.GetType(), out var floating))
                floating.Close();
            toolSidebar.Items.Remove(tab); toolTabs.Remove(window.GetType());
            if (toolTabs.Count == 0) SetSidebarVisible(false);
        };
        toolSidebar.SelectedItem = tab; SetSidebarVisible(true);
        // Keep existing window-owned setup/disposal handlers while hosting their
        // content in Edit. No native child window or modal owner is created.
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, window));
        return window;
    }
    private void DetachTool(Window window, TabItem tab, Point screenPoint)
    {
        if (toolSidebar == null || detachedTools.ContainsKey(window.GetType())) return;
        var content = (UIElement)tab.Content;
        tab.Content = null;
        toolSidebar.Items.Remove(tab);
        var popup = CreateDetachedSidebarWindow(window.Title, content,
            Math.Max(560, Math.Min(900, window.Width)), Math.Max(450, Math.Min(760, window.Height)), 420);
        PositionDetachedSidebarWindow(popup, screenPoint);
        detachedTools.Add(window.GetType(), popup);
        popup.Closing += (_, e) =>
        {
            if (detachedTools.ContainsKey(window.GetType()) && window is MajdataSessionUi.ChartRoomWindow room)
                e.Cancel = !room.PrepareClose();
        };
        popup.Closed += (_, _) =>
        {
            if (detachedTools.Remove(window.GetType())) window.Close();
        };
        if (toolSidebar.Items.Count == 0) SetSidebarVisible(false);
        ShowDetachedSidebarWindow(popup);
    }

    private TabItem CreateSidebarTab(string title, UIElement content, Action closePage, Action<TabItem, Point>? detach)
    {
        var header = new DockPanel { LastChildFill = true, ClipToBounds = true };
        var caption = SessionTheme.Text(title, size: 12);
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.MinWidth = 0;
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.ClearValue(TextBlock.LineHeightProperty);
        caption.Cursor = detach == null ? Cursors.Arrow : Cursors.SizeAll;
        caption.ToolTip = detach == null ? null : "拖出侧边栏可作为独立窗口";
        var close = new Button { Width = 20, Height = 20, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            FocusVisualStyle = null, Template = (ControlTemplate)FindResource("TabCloseButton"), ToolTip = "关闭标签" };
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(caption);
        var tab = new TabItem { Header = header, Content = content };
        close.Click += (_, _) =>
        {
            if (tab.Tag is ChartDocument document) CloseChartDocument(document);
            else closePage();
        };
        if (detach == null) return tab;
        Point? dragOrigin = null;
        caption.PreviewMouseLeftButtonDown += (_, e) =>
        {
            tab.IsSelected = true;
            dragOrigin = caption.PointToScreen(e.GetPosition(caption));
            caption.CaptureMouse();
        };
        caption.PreviewMouseLeftButtonUp += (_, _) =>
        {
            dragOrigin = null;
            caption.ReleaseMouseCapture();
        };
        caption.LostMouseCapture += (_, _) => dragOrigin = null;
        caption.PreviewMouseMove += (_, e) =>
        {
            if (dragOrigin == null || e.LeftButton != MouseButtonState.Pressed) return;
            var current = caption.PointToScreen(e.GetPosition(caption));
            if ((current - dragOrigin.Value).Length < 24) return;
            dragOrigin = null;
            caption.ReleaseMouseCapture();
            detach(tab, current);
        };
        return tab;
    }

    private Window CreateDetachedSidebarWindow(string title, UIElement content, double width, double height, double minWidth)
    {
        var popup = new Window { Title = title, Owner = Window.GetWindow(toolSidebarLayout!), Content = content,
            MinWidth = minWidth, MinHeight = 350 };
        SessionTheme.Apply(popup, width, height);
        return popup;
    }

    private void PositionDetachedSidebarWindow(Window popup, Point screenPoint)
    {
        popup.WindowStartupLocation = WindowStartupLocation.Manual;
        var dpi = PresentationSource.FromVisual(toolSidebarLayout!)?.CompositionTarget.TransformFromDevice
            ?? Matrix.Identity;
        var position = dpi.Transform(screenPoint);
        var workArea = SystemParameters.WorkArea;
        popup.Width = Math.Min(popup.Width, Math.Max(popup.MinWidth, workArea.Width - 16));
        popup.Height = Math.Min(popup.Height, Math.Max(popup.MinHeight, workArea.Height - 16));
        popup.Left = Math.Clamp(position.X - 60, workArea.Left + 8,
            Math.Max(workArea.Left + 8, workArea.Right - popup.Width - 8));
        popup.Top = Math.Clamp(position.Y - 18, workArea.Top + 8,
            Math.Max(workArea.Top + 8, workArea.Bottom - popup.Height - 8));
    }

    private static void ShowDetachedSidebarWindow(Window popup)
    {
        popup.Show();
        // Continue the held gesture in the native window until mouse release.
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            try { popup.DragMove(); }
            catch (InvalidOperationException) { /* The button was released while Show created the HWND. */ }
        }
    }
    private static void NormalizeToolControls(DependencyObject root)
    {
        // Joined appearance pickers already own their arrow size and surface.
        if (root is VisualStylePicker or FrameworkElement { Tag: "AppearancePicker" }) return;
        if (root is TextBox { AcceptsReturn: false } or ComboBox or PasswordBox)
        {
            if (LogicalTreeHelper.GetParent(root) is Grid) ((Control)root).Width = double.NaN;
            SessionTheme.Input((Control)root);
        }
        if (root is Button button && (double.IsNaN(button.Height) || button.Height <= 40))
        {
            var appearance = SessionTheme.Button("");
            button.Height = 30; button.MinHeight = 30; button.FontSize = 13;
            button.FontFamily = appearance.FontFamily; button.Padding = new Thickness(8, 0, 8, 0);
            button.Template = appearance.Template;
            button.SetResourceReference(Control.BackgroundProperty, "ButtonsBackground");
            button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
            button.SetResourceReference(Control.BorderBrushProperty, "MenuSeparator");
            button.BorderThickness = new Thickness(1);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) NormalizeToolControls(child);
    }
}
