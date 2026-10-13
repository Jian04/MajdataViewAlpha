using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void EnsureToolSidebar()
    {
        if (toolSidebar != null) return;
        var editor = (Grid)Content; Content = null;
        editorSurface = editor;
        editor.Children.Remove(MainMenu);
        editor.Children.Remove(SidebarExpandButton);
        editor.Children.Remove(SidebarToggleButton);
        var layout = new Grid { Background = SessionTheme.Brush("WindowBackground", "#20242B") };
        toolSidebarLayout = layout;
        editorColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 };
        layout.ColumnDefinitions.Add(editorColumn);
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolSidebarColumn = new ColumnDefinition { Width = new GridLength(0) }; layout.ColumnDefinitions.Add(toolSidebarColumn);
        layout.Children.Add(editor);
        toolSidebarSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        toolSidebarSplitter.FocusVisualStyle = null;
        toolSidebarSplitter.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridSplitter'><Border Background='Transparent'/></ControlTemplate>");
        toolSidebarSplitter.DragCompleted += (_, _) =>
        {
            var total = editorColumn.ActualWidth + toolSidebarColumn.ActualWidth;
            if (total > 0) sidebarRatio = toolSidebarColumn.ActualWidth / total;
        };
        Grid.SetColumn(toolSidebarSplitter, 1); layout.Children.Add(toolSidebarSplitter);
        toolSidebar = new TabControl { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0),
            FocusVisualStyle = null,
            Margin = new Thickness(0, 24, 0, 0),
            Background = SessionTheme.Brush("WindowBackground", "#20242B"), Foreground = SessionTheme.Brush("ButtonForeground", "#F4F7FA") };
        toolSidebar.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabControl'>
 <Border Background='{TemplateBinding Background}'>
  <DockPanel>
   <ScrollViewer DockPanel.Dock='Top' HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled' CanContentScroll='True'>
    <StackPanel Orientation='Horizontal' IsItemsHost='True'/>
   </ScrollViewer>
   <ContentPresenter ContentSource='SelectedContent' Margin='0' HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/>
  </DockPanel>
 </Border>
</ControlTemplate>");
        var itemStyle = new Style(typeof(TabItem));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, toolSidebar.Foreground));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, SessionTheme.Brush("EditorBackground", "#272C34")));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabItem'>
 <Border Name='tab' Background='{TemplateBinding Background}' BorderThickness='0' Padding='8,7'>
  <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
 </Border>
 <ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='BorderBrush' Value='#3DA5FF'/><Setter TargetName='tab' Property='Background' Value='#343C48'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>")));
        toolSidebar.ItemContainerStyle = itemStyle;
        Grid.SetColumn(toolSidebar, 2); layout.Children.Add(toolSidebar);
        Grid.SetRow(MainMenu, 0); Grid.SetRowSpan(MainMenu, 1); Grid.SetColumn(MainMenu, 0); Grid.SetColumnSpan(MainMenu, 3);
        Grid.SetRow(SidebarExpandButton, 0); Grid.SetRowSpan(SidebarExpandButton, 1); Grid.SetColumn(SidebarExpandButton, 0); Grid.SetColumnSpan(SidebarExpandButton, 3);
        Grid.SetRow(SidebarToggleButton, 0); Grid.SetRowSpan(SidebarToggleButton, 1); Grid.SetColumn(SidebarToggleButton, 0); Grid.SetColumnSpan(SidebarToggleButton, 3);
        layout.Children.Add(MainMenu); layout.Children.Add(SidebarExpandButton); layout.Children.Add(SidebarToggleButton); Content = layout;
        layout.SizeChanged += (_, _) => toolSidebarColumn.MaxWidth = sidebarExpanded
            ? double.PositiveInfinity : Math.Max(280, layout.ActualWidth - 265);
        Closed += (_, _) => { foreach (var entry in toolTabs.Values.ToArray()) entry.Window.Close(); };
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
        SetSidebarVisible(toolSidebar!.Visibility != Visibility.Visible);
    }
    private void SetSidebarVisible(bool visible)
    {
        if (toolSidebar == null || toolSidebarColumn == null || toolSidebarSplitter == null ||
            editorColumn == null || editorSurface == null || toolSidebarLayout == null) return;
        if (!visible) sidebarExpanded = false;
        var full = visible && sidebarExpanded;
        toolSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        toolSidebarSplitter.Visibility = visible && !full ? Visibility.Visible : Visibility.Collapsed;
        editorSurface.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        editorColumn.MinWidth = full ? 0 : 260;
        editorColumn.Width = full ? new GridLength(0) :
            new GridLength(visible ? 1 - sidebarRatio : 1, GridUnitType.Star);
        toolSidebarColumn.MinWidth = visible && !full ? 280 : 0;
        toolSidebarColumn.MaxWidth = full ? double.PositiveInfinity :
            Math.Max(280, toolSidebarLayout.ActualWidth - 265);
        toolSidebarColumn.Width = !visible ? new GridLength(0) :
            full ? new GridLength(1, GridUnitType.Star) :
            new GridLength(sidebarRatio, GridUnitType.Star);
        SidebarToggleButton.Tag = visible;
        SidebarIconFill.Opacity = visible ? 0.9 : 0.25;
        SidebarExpandButton.Tag = full;
        SidebarExpandButton.ToolTip = full ? "还原侧边栏宽度" : "侧边栏占满编辑器";
        SidebarExpandIcon.Data = Geometry.Parse(full
            ? "M4,1 L15,1 L15,11 M1,4 L12,4 L12,14 L1,14 Z"
            : "M2,2 L14,2 L14,12 L2,12 Z M2,4 L14,4");
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
        content.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, new FontFamily("Microsoft YaHei UI"));
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
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var caption = SessionTheme.Text(title ?? window.Title, size: 12);
        caption.Cursor = Cursors.SizeAll;
        caption.ToolTip = "拖出侧边栏可作为独立窗口";
        header.Children.Add(caption);
        var close = new Button { Content = "×", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = toolSidebar!.Foreground, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(3, 0, 3, 0), ToolTip = "关闭标签" };
        header.Children.Add(close);
        var tab = new TabItem { Header = header, Content = content };
        toolTabs.Add(window.GetType(), (window, tab)); toolSidebar.Items.Add(tab);
        Point? dragOrigin = null;
        caption.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragOrigin = caption.PointToScreen(e.GetPosition(caption));
            caption.CaptureMouse();
        };
        caption.PreviewMouseLeftButtonUp += (_, _) =>
        {
            dragOrigin = null;
            caption.ReleaseMouseCapture();
        };
        caption.PreviewMouseMove += (_, e) =>
        {
            if (dragOrigin == null || e.LeftButton != MouseButtonState.Pressed) return;
            var current = caption.PointToScreen(e.GetPosition(caption));
            if ((current - dragOrigin.Value).Length < 24) return;
            dragOrigin = null;
            caption.ReleaseMouseCapture();
            DetachTool(window, tab, current);
        };
        close.Click += (_, _) => window.Close();
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
        var popup = new Window
        {
            Title = window.Title,
            Owner = this,
            Content = content,
            WindowStartupLocation = WindowStartupLocation.Manual,
            MinWidth = 420,
            MinHeight = 350
        };
        SessionTheme.Apply(popup, Math.Max(560, Math.Min(900, window.Width)),
            Math.Max(450, Math.Min(760, window.Height)));
        popup.WindowStartupLocation = WindowStartupLocation.Manual;
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice
            ?? Matrix.Identity;
        var position = dpi.Transform(screenPoint);
        var workArea = SystemParameters.WorkArea;
        popup.Width = Math.Min(popup.Width, Math.Max(popup.MinWidth, workArea.Width - 16));
        popup.Height = Math.Min(popup.Height, Math.Max(popup.MinHeight, workArea.Height - 16));
        popup.Left = Math.Clamp(position.X - 60, workArea.Left + 8,
            Math.Max(workArea.Left + 8, workArea.Right - popup.Width - 8));
        popup.Top = Math.Clamp(position.Y - 18, workArea.Top + 8,
            Math.Max(workArea.Top + 8, workArea.Bottom - popup.Height - 8));
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
        popup.Show();
        // Hand the still-held mouse gesture to the detached native window.
        // DragMove tracks it until release, like dragging the pet out of Edit.
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            try { popup.DragMove(); }
            catch (InvalidOperationException) { /* The button was released while Show created the HWND. */ }
        }
    }
    private static void NormalizeToolControls(DependencyObject root)
    {
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
            button.Template = appearance.Template; button.Background = appearance.Background;
            button.Foreground = appearance.Foreground; button.BorderBrush = appearance.BorderBrush;
            button.BorderThickness = new Thickness(1);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) NormalizeToolControls(child);
    }
}
